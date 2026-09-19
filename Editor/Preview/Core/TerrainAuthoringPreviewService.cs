using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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

    /*
     * Fired only when the usable authoring preview height-cache
     * coverage changes, becomes available, or becomes unavailable.
     *
     * Cache content changes inside the same coverage do not emit
     * this event.
     */
    public static event System.Action HeightCacheCoverageChanged;

    /*
     * Fired only after a dirty composite-height transaction has
     * completed successfully and the listed source tiles contain
     * their final current authoring heights.
     */
    public static event System.Action<IReadOnlyList<Vector2Int>>
        CompositeTilesUpdated;

    // =====================================================
    // EDITOR PREFERENCE
    // =====================================================

    private const string PreviewEnabledEditorPrefsKey =
        "WorldMeshes.TerrainAuthoringPreview.Enabled";

    // =====================================================
    // STATE
    // =====================================================

    private static TerrainAuthoringPreviewCache previewCache;

    /*
     * Stage 13A composition executor.
     *
     * PreviewService owns when/which tiles are recomposed. The
     * compositor owns only GPU dispatch into the existing cache.
     */
    private static readonly TerrainHeightCompositor
        heightCompositor =
            new TerrainHeightCompositor();

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

    private static bool hasPublishedHeightCacheCoverage;

    private static Vector2 publishedHeightCacheMinimumXZ =
        Vector2.zero;

    private static Vector2 publishedHeightCacheMaximumXZ =
        Vector2.zero;

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

                heightCompositor.Dispose();

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

    public static bool TryGetHeightCacheWorldCoverage(
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        if (
            !Enabled
            ||
            status !=
                TerrainAuthoringPreviewStatus.Ready
            ||
            previewCache == null
        )
        {
            return false;
        }

        return
            previewCache.TryGetWorldCoverage(
                out minimumXZ,
                out maximumXZ
            );
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
    // STAGE 13A GPU COMPOSITOR DIAGNOSTICS
    // =====================================================

    public static bool HeightCompositorPrepared
    {
        get
        {
            return
                heightCompositor.IsPrepared;
        }
    }

    public static string HeightCompositorComputeShaderPath
    {
        get
        {
            return
                TerrainHeightCompositor
                    .ComputeShaderAssetPath;
        }
    }

    public static int LastCompositeDispatchTileCount
    {
        get
        {
            return
                heightCompositor
                    .LastDispatchTileCount;
        }
    }

    public static long TotalCompositeDispatchTileCount
    {
        get
        {
            return
                heightCompositor
                    .TotalDispatchTileCount;
        }
    }

    public static int LastCompositeModifierConsideredCount =>
        heightCompositor
            .LastModifierConsideredCount;

    public static long TotalCompositeModifierConsideredCount =>
        heightCompositor
            .TotalModifierConsideredCount;

    public static int LastCompositeModifierDispatchCount =>
        heightCompositor
            .LastModifierDispatchCount;

    public static long TotalCompositeModifierDispatchCount =>
        heightCompositor
            .TotalModifierDispatchCount;

    public static int LastCompositeComputeDispatchCount =>
        heightCompositor
            .LastComputeDispatchCount;

    public static long TotalCompositeComputeDispatchCount =>
        heightCompositor
            .TotalComputeDispatchCount;

    public static int LastRegionalElevationDispatchCount =>
        heightCompositor
            .LastRegionalElevationDispatchCount;

    public static long TotalRegionalElevationDispatchCount =>
        heightCompositor
            .TotalRegionalElevationDispatchCount;



    // =====================================================
    // TERRAIN ANALYSIS SOURCE ACCESS
    // =====================================================

    /*
     * Narrow editor-only access for TerrainAnalysisGpuGenerator.
     *
     * PreviewService retains ownership of the height cache. Terrain Analysis
     * receives only the current GPU source and immutable layout metadata
     * required to generate derived analysis layers.
     */
    internal static bool TryGetTerrainAnalysisSource(
        out RenderTexture heightCache,
        out Vector2Int cacheOriginTile,
        out Vector2Int cacheSize,
        out int samplesPerSide,
        out float sampleSpacing,
        out Vector2 worldSizeXZ,
        out string sourceSignature
    )
    {
        heightCache = null;
        cacheOriginTile = Vector2Int.zero;
        cacheSize = Vector2Int.zero;
        samplesPerSide = 0;
        sampleSpacing = 0f;
        worldSizeXZ = Vector2.zero;
        sourceSignature = "";

        if (
            previewCache == null ||
            !previewCache.IsReady
        )
        {
            return false;
        }

        heightCache =
            previewCache.HeightCache;

        cacheOriginTile =
            previewCache.CacheOriginTile;

        cacheSize =
            previewCache.CacheSize;

        samplesPerSide =
            previewCache.SamplesPerSide;

        sampleSpacing =
            previewCache.SampleSpacing;

        worldSizeXZ =
            previewCache.WorldSizeXZ;

        string authoringSignature =
            previewCache.SourceOverallAuthoringSignature;

        if (
            string.IsNullOrEmpty(
                authoringSignature
            )
        )
        {
            authoringSignature =
                previewCache.SourceCommittedHeightfieldSignature;
        }

        sourceSignature =
            authoringSignature +
            "|cache:" +
            heightCache.GetInstanceID() +
            "|updates:" +
            previewCache.TotalIncrementalSliceUpdates;

        return
            heightCache != null &&
            heightCache.IsCreated() &&
            cacheSize.x > 0 &&
            cacheSize.y > 0 &&
            samplesPerSide > 1 &&
            sampleSpacing > 0f &&
            worldSizeXZ.x > 0f &&
            worldSizeXZ.y > 0f;
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
    // STAGE 13A INTERNAL VALIDATION ACCESS
    // =====================================================

    internal static bool TryPrepareHeightCompositor(
        out string errorMessage
    )
    {
        return
            heightCompositor
                .TryPrepare(
                    out errorMessage
                );
    }

    /*
     * Copies the current pending dirty set without exposing ownership of
     * the internal HashSet.
     */
    internal static void CopyPendingDirtyTiles(
        ICollection<Vector2Int> output
    )
    {
        if (output == null)
        {
            return;
        }

        foreach (
            Vector2Int tile
            in dirtyCompositeTiles
        )
        {
            output.Add(
                tile
            );
        }
    }

    /*
     * Validation-only synchronous readback of one existing composite
     * cache slice. The cache and RenderTexture remain private.
     */
    internal static bool TryReadCompositeSlice(
        int tileX,
        int tileZ,
        out float[] values,
        out string errorMessage
    )
    {
        values =
            null;

        errorMessage =
            "";

        if (
            previewCache == null
            ||
            !previewCache.IsReady
            ||
            previewCache.HeightCache == null
            ||
            !previewCache.HeightCache.IsCreated()
        )
        {
            errorMessage =
                "The preview cache is not ready.";

            return false;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            errorMessage =
                "The current graphics device does not support AsyncGPUReadback.";

            return false;
        }

        int sliceIndex =
            previewCache.GetSliceIndex(
                tileX,
                tileZ
            );

        if (sliceIndex < 0)
        {
            errorMessage =
                $"Tile ({tileX}, {tileZ}) is outside the preview cache.";

            return false;
        }

        int samples =
            previewCache.SamplesPerSide;

        AsyncGPUReadbackRequest request =
            AsyncGPUReadback.Request(
                previewCache.HeightCache,
                0,
                0,
                samples,
                0,
                samples,
                sliceIndex,
                1,
                TextureFormat.RFloat,
                null
            );

        request.WaitForCompletion();

        if (request.hasError)
        {
            errorMessage =
                $"GPU readback failed for tile ({tileX}, {tileZ}).";

            return false;
        }

        var data =
            request.GetData<float>();

        int expectedLength =
            samples
            *
            samples;

        if (data.Length != expectedLength)
        {
            errorMessage =
                "GPU readback returned an unexpected sample count. "
                +
                $"Expected {expectedLength}, received {data.Length}.";

            return false;
        }

        values =
            new float[
                data.Length
            ];

        for (
            int index = 0;
            index < data.Length;
            index++
        )
        {
            values[index] =
                data[index];
        }

        return true;
    }

    internal static bool DiagnosticCacheRandomWriteEnabled
    {
        get
        {
            return
                previewCache != null
                &&
                previewCache.HeightCache != null
                &&
                previewCache.HeightCache.IsCreated()
                &&
                previewCache.HeightCache.enableRandomWrite;
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

        heightCompositor.Dispose();

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
        using var profilerScope =
            WorldMeshesProfiler.PreviewUpdate.Auto();

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
                "Setup / Repair World Hierarchy."
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
            using var rebuildProfilerScope =
                WorldMeshesProfiler.PreviewRebuild.Auto();

            /*
             * Full committed rebuilds replace the RenderTexture object,
             * so stop the clipmap sampling the previous cache before the
             * atomic committed-base transaction.
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

            /*
             * A full build has created committed/base cache contents only.
             *
             * Rebuild the current complete modifier state through the
             * SAME dirty-slice transaction used by ordinary edits.
             * This prevents full preview rebuilds from silently erasing
             * enabled modifiers while claiming the overall signature is
             * current.
             */
            HashSet<Vector2Int> fullRebuildCompositionTiles =
                new HashSet<Vector2Int>();

            if (
                !TerrainRegionalElevationCompositionUtility
                    .TryCollectRequiredHeightTiles(
                        worldSettings,
                        authoringData,
                        fullRebuildCompositionTiles,
                        1,
                        out _,
                        out string compositionSetError
                    )
            )
            {
                /*
                 * The committed cache remains a valid base, but the complete
                 * authored surface cannot be reconstructed. Keep the preview
                 * stale and report the regional/modifier composition problem.
                 */
                overallSignatureAcknowledgementRequested =
                    false;

                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    "The committed preview cache was rebuilt, but the " +
                    "complete regional/modifier composition set could not " +
                    "be resolved.\n\n" +
                    compositionSetError
                );

                RepaintEditorViews();

                return;
            }

            /*
             * Dirty requests that belonged to the previous committed cache
             * are obsolete. With a global node regional source this set is
             * the complete logical world; without one it retains the
             * established enabled-modifier footprint behavior.
             */
            dirtyCompositeTiles.Clear();

            dirtyCompositeTiles.UnionWith(
                fullRebuildCompositionTiles
            );

            /*
             * When neither regional elevation nor an enabled modifier
             * requires composition, the committed base already represents
             * the complete output. The normal zero-dirty acknowledgement
             * path below handles that case. Otherwise acknowledgement occurs
             * only after the dirty composition transaction succeeds.
             */
            overallSignatureAcknowledgementRequested =
                true;
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

            using var compositionProfilerScope =
                WorldMeshesProfiler.PreviewComposeTiles.Auto();

            /*
             * Compare the final transaction range against the range that
             * was authoritative before any dirty slice was reset.
             *
             * ResetCompositeTilesForRecomposition temporarily restores
             * committed ranges;
             * those intermediate values must not decide whether clipmap
             * bounds need to expand or shrink.
             */
            float globalMinimumBefore =
                previewCache.MinimumHeight;

            float globalMaximumBefore =
                previewCache.MaximumHeight;

            /*
             * Recomposition transaction:
             *
             * 1. reset every valid dirty slice from committed base
             * 2. obtain that committed/base slice range
             * 3. compose the CURRENT complete modifier stack
             * 4. calculate one conservative final range for the tile
             * 5. store that range once for the completed tile
             * 6. only after every tile succeeds clear dirty state and
             *    acknowledge the overall signature
             */
            heightCompositor
                .BeginTransactionDiagnostics();

            if (
                !previewCache
                    .ResetCompositeTilesForRecomposition(
                        dirtySnapshot,
                        out updatedCompositeSliceCount,
                        out string compositeError
                    )
            )
            {
                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    "The preview cache could not reset its dirty " +
                    "composite slices from committed base data.\n\n" +
                    compositeError
                );

                RepaintEditorViews();

                return;
            }

            List<TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate>
                finalCompositeRanges =
                    new List<TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate>(
                        updatedCompositeSliceCount
                    );

            foreach (
                Vector2Int dirtyTile
                in dirtySnapshot
            )
            {
                int sliceIndex =
                    previewCache.GetSliceIndex(
                        dirtyTile.x,
                        dirtyTile.y
                    );

                if (sliceIndex < 0)
                {
                    continue;
                }

                /*
                 * ResetCompositeTilesForRecomposition has just restored the
                 * slice from committed data, so its current range is the base
                 * absolute range supplied to the mode-aware compositor.
                 */
                if (
                    !previewCache
                        .TryGetCompositeSliceRange(
                            dirtyTile.x,
                            dirtyTile.y,
                            out float baseMinimumHeight,
                            out float baseMaximumHeight
                        )
                )
                {
                    SetStatus(
                        TerrainAuthoringPreviewStatus.Error,
                        "The preview cache could not provide the " +
                        $"committed/base range for dirty tile " +
                        $"({dirtyTile.x}, {dirtyTile.y}). The dirty " +
                        "set has been retained for retry."
                    );

                    RepaintEditorViews();

                    return;
                }

                if (
                    !heightCompositor
                        .TryComposeTile(
                            previewCache.HeightCache,
                            dirtyTile,
                            sliceIndex,
                            previewCache.SamplesPerSide,
                            previewCache.SampleSpacing,
                            worldSettings.HeightTileWorldSize,
                            previewCache.WorldSizeXZ,
                            authoringData,
                            baseMinimumHeight,
                            baseMaximumHeight,
                            out float compositeMinimumHeight,
                            out float compositeMaximumHeight,
                            out string compositorError
                        )
                )
                {
                    SetStatus(
                        TerrainAuthoringPreviewStatus.Error,
                        "The preview GPU compositor could not process " +
                        "all dirty slices. The dirty set has been " +
                        "retained for retry.\n\n" +
                        compositorError
                    );

                    RepaintEditorViews();

                    return;
                }

                finalCompositeRanges.Add(
                    new TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate(
                        dirtyTile,
                        compositeMinimumHeight,
                        compositeMaximumHeight
                    )
                );
            }

            if (
                heightCompositor
                    .LastDispatchTileCount
                !=
                updatedCompositeSliceCount
            )
            {
                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    "The reset/composition transaction produced " +
                    "different valid-slice counts. Dirty state has " +
                    "been retained.\n\n" +
                    $"Committed resets: {updatedCompositeSliceCount}\n" +
                    $"Composited tiles: " +
                    $"{heightCompositor.LastDispatchTileCount}"
                );

                RepaintEditorViews();

                return;
            }

            /*
             * All dirty slices are now fully composed. Commit their final
             * range metadata as one validated batch so the full-world range
             * is recalculated exactly once for this transaction.
             */
            if (
                !previewCache
                    .ApplyCompositeSliceRangeBatch(
                        finalCompositeRanges,
                        out _,
                        out string rangeBatchError
                    )
            )
            {
                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    "The preview GPU composition succeeded, but the final " +
                    "composite range batch could not be committed. The dirty " +
                    "set has been retained for retry.\n\n" +
                    rangeBatchError
                );

                RepaintEditorViews();

                return;
            }

            /*
             * This comparison observes the FINAL composite range after
             * every dirty tile has been fully recomposed. It therefore
             * detects expansion and shrinkage in both directions:
             *
             * - maximum increased
             * - maximum decreased
             * - minimum decreased
             * - minimum increased
             */
            compositeRangeChanged =
                !Mathf.Approximately(
                    globalMinimumBefore,
                    previewCache.MinimumHeight
                )
                ||
                !Mathf.Approximately(
                    globalMaximumBefore,
                    previewCache.MaximumHeight
                );

            dirtyCompositeTiles.Clear();

            previewCache
                .MarkOverallAuthoringSignature(
                    currentOverallSignature
                );

            overallSignatureAcknowledgementRequested =
                false;

            /*
             * Analysis consumers need the exact successfully updated
             * source tiles before the broader preview-state event.
             */
            CompositeTilesUpdated?.Invoke(
                dirtySnapshot
            );

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

        NotifyHeightCacheCoverageIfChanged();

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

        using var bindingProfilerScope =
            WorldMeshesProfiler.PreviewBindCache.Auto();

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
                "WorldRoot/Clipmap. Run Setup / Repair World Hierarchy.";

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

        NotifyHeightCacheCoverageIfChanged();
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

        heightCompositor.Dispose();

        dirtyCompositeTiles.Clear();
    }

    private static void OnEditorQuitting()
    {
        ReleaseBinding();
        ReleaseCache();

        heightCompositor.Dispose();

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
    // HEIGHT CACHE COVERAGE NOTIFICATION
    // =====================================================

    private static void NotifyHeightCacheCoverageIfChanged()
    {
        bool hasCoverage =
            TryGetHeightCacheWorldCoverage(
                out Vector2 minimumXZ,
                out Vector2 maximumXZ
            );

        bool unchanged =
            hasPublishedHeightCacheCoverage ==
                hasCoverage
            &&
            (
                !hasCoverage
                ||
                (
                    publishedHeightCacheMinimumXZ ==
                        minimumXZ
                    &&
                    publishedHeightCacheMaximumXZ ==
                        maximumXZ
                )
            );

        if (unchanged)
        {
            return;
        }

        hasPublishedHeightCacheCoverage =
            hasCoverage;

        publishedHeightCacheMinimumXZ =
            hasCoverage
                ? minimumXZ
                : Vector2.zero;

        publishedHeightCacheMaximumXZ =
            hasCoverage
                ? maximumXZ
                : Vector2.zero;

        HeightCacheCoverageChanged?.Invoke();
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
