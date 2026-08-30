using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public enum TerrainAuthoringPreviewStatus
{
    Disabled,
    PlayMode,
    AuthoringUnavailable,
    ClipmapUnavailable,
    Ready,
    Error
}

/*
 * Edit-mode authoring height preview lifecycle.
 *
 * Stage 7 separates three invalidation classes:
 *
 * 1. Committed base heightfield changed
 *      -> full cache validation/rebuild
 *
 * 2. Composite authoring tiles changed
 *      -> update only dirty slices in the existing cache
 *
 * 3. Clipmap hierarchy changed
 *      -> rebind the existing cache, do not rebuild it
 *
 * The existing PreviewStateChanged event is retained for Stage 5
 * visualization consumers. It is an outbound "preview metadata changed"
 * notification and does not request a cache rebuild.
 */
[InitializeOnLoad]
public static class TerrainAuthoringPreviewService
{
    // =====================================================
    // OUTBOUND STATE EVENT
    // =====================================================

    public static event System.Action PreviewStateChanged;

    // =====================================================
    // EDITOR PREFERENCE
    // =====================================================

    private const string PreviewEnabledEditorPrefsKey =
        "WorldMeshes.TerrainAuthoringPreview.Enabled";

    // =====================================================
    // STATE
    // =====================================================

    private static TerrainAuthoringPreviewCache previewCache;

    private static Transform boundClipmapRoot;

    private static TerrainAuthoringPreviewStatus status =
        TerrainAuthoringPreviewStatus.Disabled;

    private static string statusMessage =
        "Terrain authoring preview is disabled.";

    private static bool refreshScheduled;

    private static bool committedRebuildRequested =
        true;

    private static bool clipmapRebindRequested =
        true;

    private static readonly HashSet<Vector2Int>
        dirtyCompositeTiles =
            new HashSet<Vector2Int>();

    private static bool overallSignatureAcknowledgementRequested;

    private static long fullCommittedBuildCount;

    /*
     * Stage 10 validation-only monotonic binding diagnostic.
     *
     * This is not preview lifecycle state and does not influence any
     * cache/binding decision.
     */
    private static long diagnosticBindingApplyCount;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    static TerrainAuthoringPreviewService()
    {
        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        EditorApplication.hierarchyChanged +=
            OnHierarchyChanged;

        EditorApplication.projectChanged +=
            OnProjectChanged;

        Undo.undoRedoPerformed +=
            OnUndoRedo;

        AssemblyReloadEvents.beforeAssemblyReload +=
            OnBeforeAssemblyReload;

        EditorApplication.quitting +=
            OnEditorQuitting;

        NotifyCommittedHeightfieldChanged();
    }

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public static bool Enabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    PreviewEnabledEditorPrefsKey,
                    true
                );
        }

        set
        {
            bool oldValue =
                Enabled;

            if (oldValue == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                PreviewEnabledEditorPrefsKey,
                value
            );

            if (!value)
            {
                ReleaseBinding();
                ReleaseCache();

                dirtyCompositeTiles.Clear();

                SetStatus(
                    TerrainAuthoringPreviewStatus.Disabled,
                    "Terrain authoring preview is disabled."
                );

                RepaintEditorViews();

                return;
            }

            NotifyCommittedHeightfieldChanged();
        }
    }

    public static TerrainAuthoringPreviewStatus Status
    {
        get
        {
            return status;
        }
    }

    public static string StatusLabel
    {
        get
        {
            switch (status)
            {
                case TerrainAuthoringPreviewStatus.PlayMode:
                    return
                        "Runtime Owns Height Cache";

                case TerrainAuthoringPreviewStatus.AuthoringUnavailable:
                    return
                        "Authoring Unavailable";

                case TerrainAuthoringPreviewStatus.ClipmapUnavailable:
                    return
                        "Clipmap Unavailable";

                case TerrainAuthoringPreviewStatus.Ready:
                    return
                        "Ready";

                case TerrainAuthoringPreviewStatus.Error:
                    return
                        "Error";

                default:
                    return
                        "Disabled";
            }
        }
    }

    public static string StatusMessage
    {
        get
        {
            return statusMessage;
        }
    }

    public static bool CacheReady
    {
        get
        {
            return
                previewCache != null
                &&
                previewCache.IsReady;
        }
    }

    public static int CacheWidth
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.CacheWidth
                    : 0;
        }
    }

    public static int CacheHeight
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.CacheHeight
                    : 0;
        }
    }

    public static int CacheSliceCount
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.SliceCount
                    : 0;
        }
    }

    public static int SamplesPerSide
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.SamplesPerSide
                    : 0;
        }
    }

    public static Vector2Int CacheOriginTile
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.CacheOriginTile
                    : Vector2Int.zero;
        }
    }

    public static long ApproximateGpuMemoryBytes
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.ApproximateGpuMemoryBytes
                    : 0L;
        }
    }

    public static float MinimumPreviewHeight
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.MinimumHeight
                    : 0f;
        }
    }

    public static float MaximumPreviewHeight
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.MaximumHeight
                    : 0f;
        }
    }

    public static string SourceCommittedHeightfieldSignature
    {
        get
        {
            return
                previewCache != null
                    ? previewCache
                        .SourceCommittedHeightfieldSignature
                    : "";
        }
    }

    public static string SourceOverallAuthoringSignature
    {
        get
        {
            return
                previewCache != null
                    ? previewCache
                        .SourceOverallAuthoringSignature
                    : "";
        }
    }

    public static int PendingDirtyTileCount
    {
        get
        {
            return
                dirtyCompositeTiles.Count;
        }
    }

    public static int LastIncrementalSliceCount
    {
        get
        {
            return
                previewCache != null
                    ? previewCache
                        .LastIncrementalSliceCount
                    : 0;
        }
    }

    public static long TotalIncrementalSliceUpdates
    {
        get
        {
            return
                previewCache != null
                    ? previewCache
                        .TotalIncrementalSliceUpdates
                    : 0L;
        }
    }

    public static long FullCommittedBuildCount
    {
        get
        {
            return
                fullCommittedBuildCount;
        }
    }

    public static int CacheTextureInstanceId
    {
        get
        {
            return
                previewCache != null
                &&
                previewCache.HeightCache != null
                    ? previewCache
                        .HeightCache
                        .GetInstanceID()
                    : 0;
        }
    }


    // =====================================================
    // STAGE 10 VALIDATION DIAGNOSTICS
    // =====================================================

    /*
     * Narrow editor-assembly diagnostics for Stage 10 validation.
     *
     * The preview cache itself remains private. Validation and future
     * internal diagnostics can inspect addressing/range metadata
     * without gaining access to cache allocation, GPU resources, or
     * renderer binding ownership.
     */
    internal static bool TryGetSliceIndex(
        int tileX,
        int tileZ,
        out int sliceIndex
    )
    {
        sliceIndex =
            -1;

        if (
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            return false;
        }

        sliceIndex =
            previewCache.GetSliceIndex(
                tileX,
                tileZ
            );

        return
            sliceIndex >= 0;
    }

    internal static bool TryGetTileCoordinate(
        int sliceIndex,
        out Vector2Int tileCoordinate
    )
    {
        tileCoordinate =
            Vector2Int.zero;

        if (
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            return false;
        }

        return
            previewCache.TryGetTileCoordinate(
                sliceIndex,
                out tileCoordinate
            );
    }

    internal static bool TryGetCompositeSliceRange(
        int tileX,
        int tileZ,
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            0f;

        maximumHeight =
            0f;

        if (
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            return false;
        }

        return
            previewCache.TryGetCompositeSliceRange(
                tileX,
                tileZ,
                out minimumHeight,
                out maximumHeight
            );
    }

    /*
     * Monotonic editor-session count used only to prove that
     * NotifyClipmapHierarchyChanged() caused a real binding pass.
     */
    internal static long DiagnosticBindingApplyCount
    {
        get
        {
            return
                diagnosticBindingApplyCount;
        }
    }

    // =====================================================
    // CLEAR INVALIDATION API
    // =====================================================

    /*
     * Use when the physical committed base heightfield or its layout
     * changed.
     *
     * This is the normal full-cache rebuild boundary.
     */
    public static void NotifyCommittedHeightfieldChanged()
    {
        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        /*
         * Dirty composite coordinates belong to the old committed
         * base transaction. A future modifier system should submit
         * new dirty regions after the new base is committed.
         */
        dirtyCompositeTiles.Clear();

        overallSignatureAcknowledgementRequested =
            false;

        ScheduleRefresh();
    }

    /*
     * Use when non-destructive authoring/modifier output changed but
     * the committed base heightfield did not.
     *
     * Multiple notifications before the scheduled refresh coalesce
     * into one unique dirty-tile set.
     */
    public static void NotifyCompositeTileChanged(
        int tileX,
        int tileZ
    )
    {
        dirtyCompositeTiles.Add(
            new Vector2Int(
                tileX,
                tileZ
            )
        );

        ScheduleRefresh();
    }

    public static void NotifyCompositeTileChanged(
        Vector2Int tileCoordinate
    )
    {
        dirtyCompositeTiles.Add(
            tileCoordinate
        );

        ScheduleRefresh();
    }

    public static void NotifyCompositeTilesChanged(
        IEnumerable<Vector2Int> tileCoordinates
    )
    {
        if (tileCoordinates == null)
        {
            return;
        }

        bool anyAdded =
            false;

        foreach (
            Vector2Int coordinate
            in tileCoordinates
        )
        {
            if (
                dirtyCompositeTiles.Add(
                    coordinate
                )
            )
            {
                anyAdded =
                    true;
            }
        }

        if (anyAdded)
        {
            ScheduleRefresh();
        }
    }


    /*
     * Stage 12 modifier-authoring notification.
     *
     * This also handles valid changes with zero in-world dirty tiles.
     */
    public static void NotifyCompositeAuthoringStateChanged(
        IEnumerable<Vector2Int> tileCoordinates
    )
    {
        if (tileCoordinates != null)
        {
            foreach (
                Vector2Int coordinate
                in tileCoordinates
            )
            {
                dirtyCompositeTiles.Add(
                    coordinate
                );
            }
        }

        overallSignatureAcknowledgementRequested =
            true;

        ScheduleRefresh();
    }

    /*
     * Convenience API for future modifier tools.
     *
     * The dirty region is expanded by one native height sample by
     * default, which is useful for boundary-adjacent normal sampling.
     */
    public static void NotifyCompositeWorldBoundsChanged(
        Bounds worldBounds,
        int samplePadding = 1
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return;
        }

        HashSet<Vector2Int> affectedTiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                worldBounds,
                affectedTiles,
                samplePadding
            );

        NotifyCompositeTilesChanged(
            affectedTiles
        );
    }

    /*
     * Use when WorldRoot/Clipmap renderers were created/replaced or
     * the active generated hierarchy changed.
     *
     * Cache contents remain valid.
     */
    public static void NotifyClipmapHierarchyChanged()
    {
        clipmapRebindRequested =
            true;

        ScheduleRefresh();
    }

    /*
     * Explicit user command: validate/rebuild the committed cache now.
     */
    public static void ForceCommittedRebuildNow()
    {
        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        dirtyCompositeTiles.Clear();

        ExecuteRefresh();
    }

    // =====================================================
    // COMPATIBILITY API
    // =====================================================

    /*
     * Existing callers remain source-compatible while Stage 7
     * migrates call sites to the clearer invalidation methods above.
     */
    public static void RequestRefresh()
    {
        ScheduleRefresh();
    }

    public static void RequestRebuild()
    {
        NotifyCommittedHeightfieldChanged();
    }

    public static void RequestRebind()
    {
        NotifyClipmapHierarchyChanged();
    }

    public static void RefreshNow()
    {
        ForceCommittedRebuildNow();
    }

    // =====================================================
    // RESOURCE SHUTDOWN
    // =====================================================

    public static void Shutdown()
    {
        refreshScheduled =
            false;

        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        dirtyCompositeTiles.Clear();

        overallSignatureAcknowledgementRequested =
            false;

        ReleaseBinding();
        ReleaseCache();

        SetStatus(
            TerrainAuthoringPreviewStatus.Disabled,
            "Terrain authoring preview resources were released."
        );

        RepaintEditorViews();
    }

    // =====================================================
    // SCHEDULE
    // =====================================================

    private static void ScheduleRefresh()
    {
        if (refreshScheduled)
        {
            return;
        }

        refreshScheduled =
            true;

        EditorApplication.delayCall +=
            ExecuteScheduledRefresh;
    }

    private static void ExecuteScheduledRefresh()
    {
        refreshScheduled =
            false;

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            ScheduleRefresh();

            return;
        }

        ExecuteRefresh();
    }

    // =====================================================
    // REFRESH
    // =====================================================

    private static void ExecuteRefresh()
    {
        if (!Enabled)
        {
            ReleaseBinding();
            ReleaseCache();

            dirtyCompositeTiles.Clear();

            SetStatus(
                TerrainAuthoringPreviewStatus.Disabled,
                "Terrain authoring preview is disabled."
            );

            RepaintEditorViews();

            return;
        }

        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            ReleaseBinding();
            ReleaseCache();

            dirtyCompositeTiles.Clear();

            SetStatus(
                TerrainAuthoringPreviewStatus.PlayMode,
                "The editor preview is inactive in Play Mode. " +
                "TerrainHeightmapStreamer owns the clipmap " +
                "height cache while the game is running."
            );

            RepaintEditorViews();

            return;
        }

        WorldSettings worldSettings =
            LoadWorldSettings();

        TerrainAuthoringData authoringData =
            LoadAuthoringData();

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        worldSettings,
                        authoringData
                    );

        if (
            authoringStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            ReleaseBinding();
            ReleaseCache();

            dirtyCompositeTiles.Clear();

            SetStatus(
                TerrainAuthoringPreviewStatus.AuthoringUnavailable,
                "The committed authoring heightfield is not " +
                "current. Initialize or reinitialize the " +
                "authoring heightfield before building the " +
                "edit-mode preview."
            );

            RepaintEditorViews();

            return;
        }

        // =================================================
        // SCENE HIERARCHY
        // =================================================

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out Transform clipmapRoot,
                    out string sceneLookupError
                )
        )
        {
            ReleaseBinding();

            SetStatus(
                TerrainAuthoringPreviewStatus.Error,
                sceneLookupError
            );

            RepaintEditorViews();

            return;
        }

        if (clipmapRoot == null)
        {
            ReleaseBinding();

            SetStatus(
                TerrainAuthoringPreviewStatus.ClipmapUnavailable,
                "WorldRoot/Clipmap was not found in the active " +
                "scene. Generate clipmap meshes and run " +
                "Sync World Hierarchy."
            );

            RepaintEditorViews();

            return;
        }

        // =================================================
        // SIGNATURES
        // =================================================

        string currentCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string currentOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                currentCommittedSignature
            )
            ||
            string.IsNullOrEmpty(
                currentOverallSignature
            )
        )
        {
            ReleaseBinding();
            ReleaseCache();

            SetStatus(
                TerrainAuthoringPreviewStatus.AuthoringUnavailable,
                "The authoring signatures could not be calculated."
            );

            RepaintEditorViews();

            return;
        }

        // =================================================
        // FULL COMMITTED BUILD DECISION
        // =================================================

        bool cacheNeedsBuild =
            previewCache == null
            ||
            !previewCache.IsReady
            ||
            committedRebuildRequested
            ||
            previewCache
                .SourceCommittedHeightfieldSignature
            !=
            currentCommittedSignature;

        if (cacheNeedsBuild)
        {
            /*
             * Full committed rebuilds replace the RenderTexture
             * object, so stop the clipmap sampling the previous cache
             * before the atomic cache transaction.
             */
            ReleaseBinding();

            clipmapRebindRequested =
                true;

            TerrainAuthoringPreviewCache newCache =
                previewCache
                ??
                new TerrainAuthoringPreviewCache();

            if (
                !newCache.TryBuild(
                    worldSettings,
                    authoringData,
                    out string buildError
                )
            )
            {
                ReleaseBinding();

                if (previewCache == null)
                {
                    newCache.Dispose();
                }
                else
                {
                    ReleaseCache();
                }

                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    buildError
                );

                RepaintEditorViews();

                return;
            }

            previewCache =
                newCache;

            fullCommittedBuildCount++;

            committedRebuildRequested =
                false;

            clipmapRebindRequested =
                true;

            NotifyPreviewStateChanged();
        }

        // =================================================
        // DIRTY COMPOSITE SLICE UPDATE
        // =================================================

        bool compositeRangeChanged =
            false;

        int updatedCompositeSliceCount =
            0;

        if (
            previewCache != null
            &&
            previewCache.IsReady
            &&
            dirtyCompositeTiles.Count > 0
        )
        {
            /*
             * Do not update slices on top of a stale committed base.
             * A committed signature mismatch should already have
             * triggered the full-build path above.
             */
            if (
                previewCache
                    .SourceCommittedHeightfieldSignature
                !=
                currentCommittedSignature
            )
            {
                committedRebuildRequested =
                    true;

                ScheduleRefresh();

                return;
            }

            List<Vector2Int> dirtySnapshot =
                new List<Vector2Int>(
                    dirtyCompositeTiles
                );

            if (
                !previewCache
                    .UpdateCompositeTiles(
                        dirtySnapshot,
                        out updatedCompositeSliceCount,
                        out compositeRangeChanged,
                        out string compositeError
                    )
            )
            {
                /*
                 * Keep the dirty set so a future retry does not lose
                 * the requested update.
                 */
                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    "The preview cache could not update its dirty " +
                    "composite slices.\n\n" +
                    compositeError
                );

                RepaintEditorViews();

                return;
            }

            dirtyCompositeTiles.Clear();

            previewCache
                .MarkOverallAuthoringSignature(
                    currentOverallSignature
                );

            overallSignatureAcknowledgementRequested =
                false;

            /*
             * The same RenderTexture object remains bound. Notify
             * consumers so Height-mode range metadata can refresh,
             * but do not rebind/rebuild the terrain cache.
             */
            NotifyPreviewStateChanged();
        }


        // =================================================
        // OVERALL AUTHORING SIGNATURE ACKNOWLEDGEMENT
        // =================================================

        if (
            overallSignatureAcknowledgementRequested
            &&
            previewCache != null
            &&
            previewCache.IsReady
            &&
            dirtyCompositeTiles.Count == 0
            &&
            previewCache
                .SourceCommittedHeightfieldSignature
            ==
            currentCommittedSignature
        )
        {
            previewCache
                .MarkOverallAuthoringSignature(
                    currentOverallSignature
                );

            overallSignatureAcknowledgementRequested =
                false;

            NotifyPreviewStateChanged();
        }

        // =================================================
        // CLIPMAP BINDING
        // =================================================

        bool rootChanged =
            boundClipmapRoot !=
            clipmapRoot;

        if (
            rootChanged
            ||
            clipmapRebindRequested
        )
        {
            if (
                boundClipmapRoot != null
                &&
                boundClipmapRoot !=
                    clipmapRoot
            )
            {
                ReleaseBinding();
            }

            if (
                !BindPreviewToClipmap(
                    clipmapRoot,
                    out string bindError
                )
            )
            {
                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    bindError
                );

                RepaintEditorViews();

                return;
            }

            clipmapRebindRequested =
                false;
        }
        else if (
            compositeRangeChanged
            &&
            !ApplyCurrentPreviewBounds(
                clipmapRoot,
                out string boundsError
            )
        )
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Error,
                boundsError
            );

            RepaintEditorViews();

            return;
        }

        // =================================================
        // STATUS
        // =================================================

        bool overallSignatureMatches =
            previewCache != null
            &&
            previewCache
                .SourceOverallAuthoringSignature
            ==
            currentOverallSignature;

        string readyMessage;

        if (!overallSignatureMatches)
        {
            readyMessage =
                "The committed preview cache is current, but the " +
                "overall authoring signature differs from the last " +
                "composited state. Future modifier tools must call " +
                "NotifyCompositeTilesChanged(...) for their affected " +
                "tiles.";
        }
        else if (updatedCompositeSliceCount > 0)
        {
            readyMessage =
                $"Updated {updatedCompositeSliceCount:N0} dirty " +
                "preview slice(s) in place. The existing GPU cache " +
                "remained bound to the clipmap.";
        }
        else
        {
            readyMessage =
                "The committed base heightfield is cached once and " +
                "the preview is ready for incremental composite " +
                "slice updates.";
        }

        SetStatus(
            TerrainAuthoringPreviewStatus.Ready,
            readyMessage
        );

        RepaintEditorViews();
    }

    // =====================================================
    // BIND PREVIEW
    // =====================================================

    private static bool BindPreviewToClipmap(
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            clipmapRoot == null
            ||
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            errorMessage =
                "The editor preview cannot bind because the " +
                "clipmap or preview cache is unavailable.";

            return false;
        }

        TerrainClipmapBoundsController boundsController =
            clipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController == null)
        {
            errorMessage =
                "TerrainClipmapBoundsController is missing from " +
                "WorldRoot/Clipmap. Run Sync World Hierarchy.";

            return false;
        }

        if (
            !TerrainHeightCacheBindingUtility
                .TryBind(
                    clipmapRoot,
                    previewCache.HeightCache,
                    previewCache.CacheOriginTile,
                    previewCache.CacheSize,
                    previewCache.SamplesPerSide,
                    previewCache.SampleSpacing,
                    previewCache.WorldSizeXZ,
                    out _,
                    out string bindingError
                )
        )
        {
            errorMessage =
                "The editor preview height cache could not be " +
                "bound to the clipmap.\n\n" +
                bindingError;

            return false;
        }

        if (
            !boundsController
                .ApplyBoundsForRange(
                    previewCache.MinimumHeight,
                    previewCache.MaximumHeight
                )
        )
        {
            TerrainHeightCacheBindingUtility
                .Disable(
                    clipmapRoot
                );

            errorMessage =
                "The editor preview cache was created, but the " +
                "clipmap displacement bounds could not be applied.";

            return false;
        }

        boundClipmapRoot =
            clipmapRoot;

        diagnosticBindingApplyCount++;

        return true;
    }

    private static bool ApplyCurrentPreviewBounds(
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            clipmapRoot == null
            ||
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            errorMessage =
                "The preview bounds cannot be updated because the " +
                "clipmap or preview cache is unavailable.";

            return false;
        }

        TerrainClipmapBoundsController boundsController =
            clipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController == null)
        {
            errorMessage =
                "TerrainClipmapBoundsController is missing from " +
                "WorldRoot/Clipmap.";

            return false;
        }

        if (
            !boundsController
                .ApplyBoundsForRange(
                    previewCache.MinimumHeight,
                    previewCache.MaximumHeight
                )
        )
        {
            errorMessage =
                "The incremental preview update succeeded, but the " +
                "clipmap displacement bounds could not be refreshed.";

            return false;
        }

        return true;
    }

    // =====================================================
    // RELEASE BINDING / CACHE
    // =====================================================

    private static void ReleaseBinding()
    {
        if (boundClipmapRoot == null)
        {
            boundClipmapRoot =
                null;

            return;
        }

        TerrainHeightCacheBindingUtility
            .Disable(
                boundClipmapRoot
            );

        TerrainClipmapBoundsController boundsController =
            boundClipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController != null)
        {
            boundsController
                .RestoreConfiguredBounds();
        }

        boundClipmapRoot =
            null;
    }

    private static void ReleaseCache()
    {
        if (previewCache == null)
        {
            return;
        }

        previewCache.Dispose();

        previewCache =
            null;

        NotifyPreviewStateChanged();
    }

    // =====================================================
    // EDITOR EVENTS
    // =====================================================

    private static void OnHierarchyChanged()
    {
        /*
         * Hierarchy changes never imply committed height data changed.
         */
        NotifyClipmapHierarchyChanged();
    }

    private static void OnProjectChanged()
    {
        /*
         * Do not blindly rebuild the world cache.
         *
         * The next refresh compares the cheap committed-heightfield
         * signature. Authorized committed-base transactions use
         * the explicit committed-heightfield invalidation API.
         */
        ScheduleRefresh();
    }

    private static void OnUndoRedo()
    {
        /*
         * Future modifier editors should notify precise dirty tiles
         * as part of their own Undo/Redo integration.
         */
        ScheduleRefresh();
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            case PlayModeStateChange.EnteredPlayMode:
            {
                ReleaseBinding();
                ReleaseCache();

                dirtyCompositeTiles.Clear();

                SetStatus(
                    TerrainAuthoringPreviewStatus.PlayMode,
                    "The editor preview released its height " +
                    "cache for Play Mode."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.EnteredEditMode:
            {
                NotifyCommittedHeightfieldChanged();

                break;
            }
        }
    }

    private static void OnBeforeAssemblyReload()
    {
        ReleaseBinding();
        ReleaseCache();

        dirtyCompositeTiles.Clear();
    }

    private static void OnEditorQuitting()
    {
        ReleaseBinding();
        ReleaseCache();

        dirtyCompositeTiles.Clear();
    }

    // =====================================================
    // ASSET LOAD
    // =====================================================

    private static WorldSettings LoadWorldSettings()
    {
        return
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );
    }

    private static TerrainAuthoringData LoadAuthoringData()
    {
        return
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );
    }

    // =====================================================
    // OUTBOUND PREVIEW STATE NOTIFICATION
    // =====================================================

    private static void NotifyPreviewStateChanged()
    {
        PreviewStateChanged?.Invoke();
    }

    // =====================================================
    // STATUS / REPAINT
    // =====================================================

    private static void SetStatus(
        TerrainAuthoringPreviewStatus newStatus,
        string message
    )
    {
        status =
            newStatus;

        statusMessage =
            string.IsNullOrEmpty(
                message
            )
                ? ""
                : message;
    }

    private static void RepaintEditorViews()
    {
        SceneView.RepaintAll();

        WorldMeshesEditorWindow[] windows =
            Resources
                .FindObjectsOfTypeAll<WorldMeshesEditorWindow>();

        foreach (
            WorldMeshesEditorWindow window
            in windows
        )
        {
            if (window != null)
            {
                window.Repaint();
            }
        }
    }
}
