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
    Preparing,
    Ready,
    Error
}

/*
 * Final edit-mode streamed height-preview ownership model.
 *
 * Persistent/global authoring state is authoritative independently of GPU
 * residency. activeHeightStates own the published per-LOD GPU sources;
 * one disposable cache-set transaction owns any replacement under construction
 * until the complete layout and renderer bindings publish synchronously.
 *
 * Scene View/canonical placement publishes residency intent. PreviewService
 * owns cache loading, composition, binding, transition lifetime, and resource
 * disposal. Nonresident terrain remains valid persistent terrain; it is
 * simply outside the current bounded GPU window.
 *
 * Temporary editor instability pauses work. Ownership boundaries such as
 * Play Mode, active-scene replacement, preview disable, assembly reload, and
 * editor shutdown release edit-mode resources through the lifecycle layer.
 *
 * PreviewStateChanged is an outbound metadata/content notification. It does
 * not itself request a cache rebuild.
 */
[InitializeOnLoad]
public static partial class TerrainAuthoringPreviewService
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

    // Explicit private display LOD0 projection for native-analysis borrowing.
    private static TerrainAuthoringPreviewCache Lod0DisplayCache => activeHeightStates != null && activeHeightStates.Length > 0
        ? activeHeightStates[0].ActiveCache : null;


    /*
     * PreviewService owns when and which resident tiles are recomposed.
     * The compositor owns only GPU dispatch into the cache supplied by
     * PreviewService.
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
     * Validation-only monotonic binding diagnostic.
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
        InitializePreviewLifecycle();
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

            HandlePreviewEnabledChanged(
                value
            );
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

                case TerrainAuthoringPreviewStatus.Preparing:
                    return
                        "Preparing Preview";

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

    public static bool CacheReady => HasDrawableHeightPreview && activeDisplayIntent != null
        && activeDisplayIntent.OwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
        && activeDisplayIntent.ConfigurationMatches(LoadWorldSettings())
        && ActiveHeightContentIsCurrent(LoadWorldSettings(), LoadAuthoringData());

    public static bool TryGetHeightCacheWorldCoverage(out Vector2 minimumXZ, out Vector2 maximumXZ)
    {
        minimumXZ = maximumXZ = Vector2.zero;
        if (!HasDrawableHeightPreview) return false;
        minimumXZ = activeDisplayIntent.Layout.MinimumXZ; maximumXZ = activeDisplayIntent.Layout.MaximumXZ;
        return true;
    }


    public static int ResidencySizeToleranceTiles =>
        TerrainAuthoringPreviewResidencyPolicy
            .DefaultResidentSizeToleranceTiles;


    public static long ApproximateGpuMemoryBytes => EstimatePublishedHeightMemory();

    public static float MinimumPreviewHeight => aggregateMinimumHeight;

    public static float MaximumPreviewHeight => aggregateMaximumHeight;


    public static int PendingDirtyTileCount
    {
        get
        {
            int count = dirtyCompositeTiles.Count;
            if (activeHeightStates != null) foreach (var s in activeHeightStates) count += s.PendingDirtyTiles.Count;
            return count;
        }
    }

    public static int LastIncrementalSliceCount
    {
        get { int count = 0; if (activeHeightStates != null) foreach (var s in activeHeightStates) count += s.ActiveCache?.LastIncrementalSliceCount ?? 0; return count; }
    }

    public static long TotalIncrementalSliceUpdates
    {
        get { long count = 0; if (activeHeightStates != null) foreach (var s in activeHeightStates) count += s.ActiveCache?.TotalIncrementalSliceUpdates ?? 0L; return count; }
    }

    public static long FullCommittedBuildCount
    {
        get
        {
            return
                fullCommittedBuildCount;
        }
    }

    public static long ResidentCacheBuildCount
    {
        get
        {
            return
                fullCommittedBuildCount;
        }
    }


    // =====================================================
    // GPU COMPOSITOR DIAGNOSTICS
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
    // VALIDATION DIAGNOSTICS
    // =====================================================

    /*
     * Narrow editor-assembly diagnostics for height-cache validation.
     *
     * The preview cache itself remains private. Validation and future
     * internal diagnostics can inspect addressing/range metadata
     * without gaining access to cache allocation, GPU resources, or
     * renderer binding ownership.
     */


    internal static bool TryGetCompositeSliceRange(int tileX, int tileZ, out float minimumHeight, out float maximumHeight)
    {
        minimumHeight = maximumHeight = 0;
        var tile = new Vector2Int(tileX, tileZ);
        var state = FindFinestResidentDisplayState(tile);
        if (state == null || state.WriteFailed || state.PendingDirtyTiles.Contains(tile)
            || dirtyCompositeTiles.Contains(tile) || IsWorldTilePendingRegionalElevationRecomposition(LoadWorldSettings(), tile)
            || !state.ActiveCache.IsSliceFinalCompositeReady(tile)) return false;
        return state.ActiveCache.TryGetCompositeSliceRange(tileX, tileZ, out minimumHeight, out maximumHeight);
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
    // INTERNAL VALIDATION ACCESS
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
    internal static void CopyPendingDirtyTiles(ICollection<Vector2Int> output)
    {
        if (output == null) return;
        var unique = new HashSet<Vector2Int>(dirtyCompositeTiles);
        if (activeHeightStates != null) foreach (var s in activeHeightStates) unique.UnionWith(s.PendingDirtyTiles);
        foreach (var tile in unique) output.Add(tile);
    }

    /*
     * Validation-only synchronous readback of one existing composite
     * cache slice. The cache and RenderTexture remain private.
     */
    internal static bool TryReadDisplayLodCompositeSlice(
        int level,
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

        if (!DisplayLodTileCurrentForValidation(level, tileX, tileZ, out var cache))
        {
            errorMessage = "The selected display LOD slice is unavailable, stale or outside residency.";
            return false;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            errorMessage =
                "The current graphics device does not support AsyncGPUReadback.";

            return false;
        }

        int sliceIndex =
            cache.GetSliceIndex(
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
            cache.SamplesPerSide;

        AsyncGPUReadbackRequest request =
            AsyncGPUReadback.Request(
                cache.HeightCache,
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


    // =====================================================
    // CLEAR INVALIDATION API
    // =====================================================

    /*
     * Use when the physical committed base heightfield or its layout
     * changed.
     *
     * This is the normal resident-cache rebuild boundary.
     */
    public static void NotifyCommittedHeightfieldChanged()
    {
        ClearTransitionFailureSuppression();

        RegisterPreviewAuthoringInvalidation(
            "Committed heightfield changed."
        );

        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        /*
         * Dirty composite coordinates and pending regional invalidation belong
         * to the old committed-base transaction. Fresh authoring notifications
         * are evaluated against the replacement committed source.
         */
        dirtyCompositeTiles.Clear();

        ClearPendingRegionalElevationInvalidationForCommittedChange();

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
        RegisterPreviewAuthoringInvalidation(
            "A composite authoring tile changed."
        );

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
        RegisterPreviewAuthoringInvalidation(
            "A composite authoring tile changed."
        );

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

        RegisterPreviewAuthoringInvalidation(
            "Composite authoring tiles changed."
        );

        foreach (
            Vector2Int coordinate
            in tileCoordinates
        )
        {
            dirtyCompositeTiles.Add(
                coordinate
            );
        }

        ScheduleRefresh();
    }


    /*
     * Modifier-authoring notification.
     *
     * This also handles valid changes with zero in-world dirty tiles.
     */
    public static void NotifyCompositeAuthoringStateChanged(
        IEnumerable<Vector2Int> tileCoordinates
    )
    {
        ClearTransitionFailureSuppression();

        RegisterPreviewAuthoringInvalidation(
            "Composite authoring state changed."
        );

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
        ClearTransitionFailureSuppression();
        RegisterPreviewAuthoringInvalidation("An explicit committed preview rebuild was requested.");

        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        dirtyCompositeTiles.Clear();

        ScheduleRefresh();
    }

    // =====================================================
    // COMPATIBILITY API
    // =====================================================

    /*
     * Compatibility callers use the explicit invalidation methods above.
     */
    public static void RequestRefresh()
    {
        lastFailedAnalysisCacheSetRequest = null;
        analysisSourceError = "";
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
    // RESIDENCY HELPERS
    // =====================================================


    private static bool IsWindowInsideWorldGrid(
        TerrainHeightCacheWindow window,
        Vector2Int worldGridSize
    )
    {
        if (
            !window.IsValid
            ||
            worldGridSize.x <= 0
            ||
            worldGridSize.y <= 0
            ||
            window.OriginTile.x < 0
            ||
            window.OriginTile.y < 0
        )
        {
            return false;
        }

        Vector2Int maximumExclusive =
            window.MaximumExclusive;

        return
            maximumExclusive.x <=
                worldGridSize.x
            &&
            maximumExclusive.y <=
                worldGridSize.y;
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

        RefreshTransientSuspensionState();

        if (!CanRunEditorPreviewWork)
        {
            /*
             * Lifecycle resume schedules a fresh refresh after
             * temporary editor instability clears. Do not churn delayCall
             * while compilation/import or an ownership handoff is active.
             */
            return;
        }

        ExecuteRefresh();
    }

    // =====================================================
    // REFRESH
    // =====================================================

    private static void ExecuteRefresh()
    {
        using var scope = WorldMeshesProfiler.PreviewUpdate.Auto();
        if (displayCommitInProgress) return;
        if (!Enabled || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            ReleaseBinding(); ReleaseAllPreviewCaches(); dirtyCompositeTiles.Clear();
            SetStatus(Enabled ? TerrainAuthoringPreviewStatus.PlayMode : TerrainAuthoringPreviewStatus.Disabled,
                Enabled ? "Runtime streaming owns Height in Play Mode." : "Terrain authoring preview is disabled.");
            return;
        }
        if (!CanRunEditorPreviewWork) return;
        var settings = LoadWorldSettings(); var data = LoadAuthoringData();
        if (settings == null || data == null || TerrainGenerationStateUtility.GetAuthoringHeightfieldStatus(settings, data)
            != TerrainGenerationStateUtility.GenerationStatus.Current)
        {
            ReleaseAllPreviewCaches(); dirtyCompositeTiles.Clear();
            SetStatus(TerrainAuthoringPreviewStatus.AuthoringUnavailable, "Initialize or reinitialize the committed authoring heightfield.");
            return;
        }
        if (!TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out string error) || root == null)
        {
            ReleaseBinding(); SetStatus(TerrainAuthoringPreviewStatus.ClipmapUnavailable,
                "WorldRoot/Clipmap is unavailable. " + error); return;
        }
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            ReleaseAllPreviewCaches(); SetStatus(TerrainAuthoringPreviewStatus.AuthoringUnavailable,
                "The current authoring signatures are unavailable."); return;
        }
        if (!TryProjectPendingDisplayAuthoring(settings, committed, overall, out error))
        { SetStatus(TerrainAuthoringPreviewStatus.Error, error); return; }
        if (activeDirtyFailureGeneration == authoringGeneration && HasPendingActiveDirtyWork) return;
        if (latestDisplayIntent == null || latestDisplayIntent.Root != root || !latestDisplayIntent.ConfigurationMatches(settings)
            || latestDisplayIntent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
        {
            TerrainAuthoringSceneViewController.RequestReapply();
            SetStatus(HasDrawableHeightPreview ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Preparing,
                "Waiting for the current Scene View's paired display layout and residency intent."); return;
        }
        if (!RequestPreparedHeightCacheSet(settings, data, latestDisplayIntent.Plan, committedRebuildRequested, out error))
        {
            SetStatus(HasDrawableHeightPreview ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Error,
                "The requested multiresolution Height display is unavailable. " + error); return;
        }
        SetStatus(HasDrawableHeightPreview ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Preparing,
            HasPendingActiveDirtyWork ? "Updating resident Height representations incrementally."
                : IsWaitingForStreamingCoverage ? "Waiting for complete multiresolution Height coverage."
                : "The complete multiresolution Height preview is active.");
        NotifyHeightCacheCoverageIfChanged(); NotifyPreviewStateChanged(); RepaintEditorViews();
    }

    // =====================================================
    // BIND PREVIEW
    // =====================================================

    private static bool BindPreviewToClipmap(Transform root, out string errorMessage)
    {
        errorMessage = "";
        return latestDisplayIntent != null && root == latestDisplayIntent.Root
            && TryCommitDisplayHeight(null, latestDisplayIntent, activeHeightStates, out errorMessage);
    }

    private static bool ApplyCurrentPreviewBounds(Transform root, out string errorMessage)
    {
        errorMessage = "";
        var controller = root != null ? root.GetComponent<TerrainClipmapBoundsController>() : null;
        if (controller == null || !controller.ApplyBoundsForRange(aggregateMinimumHeight, aggregateMaximumHeight))
        { errorMessage = "The transient multiresolution Height range could not be applied."; return false; }
        return true;
    }

    // =====================================================
    // RELEASE BINDING / CACHE
    // =====================================================

    private static void ReleaseBinding()
    {
        TerrainAuthoringPreviewHeightBindingUtility.Disable(boundHeightRenderers); boundHeightRenderers.Clear();
        var controller = boundClipmapRoot != null ? boundClipmapRoot.GetComponent<TerrainClipmapBoundsController>() : null;
        if (controller != null) controller.RestoreConfiguredBounds();
        boundClipmapRoot = null;
    }

    private static void ReleaseCache()
    {
        ReleaseAllPreviewCaches();
    }

    // =====================================================
    // EDITOR EVENTS
    // =====================================================

    private static void OnHierarchyChanged()
    {
        if (displayCommitInProgress) return;
        clipmapRebindRequested = true;
        ScheduleRefresh();
    }

    private static void OnProjectChanged()
    {
        /*
         * Do not blindly rebuild or recenter the resident cache.
         *
         * The next refresh compares the cheap committed-heightfield
         * signature. Authorized committed-base transactions use
         * the explicit committed-heightfield invalidation API.
         */
        ClearTransitionFailureSuppression();

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
        HandlePreviewPlayModeStateChanged(
            state
        );
    }

    private static void OnBeforeAssemblyReload()
    {
        BeginTerminalPreviewShutdown(
            TerrainAuthoringPreviewSuspensionReason.AssemblyReload
        );
    }

    private static void OnEditorQuitting()
    {
        BeginTerminalPreviewShutdown(
            TerrainAuthoringPreviewSuspensionReason.EditorQuitting
        );
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
        bool hasCoverage = TryGetHeightCacheWorldCoverage(out Vector2 low, out Vector2 high);
        var stamp = new System.Text.StringBuilder();
        if (hasCoverage)
        {
            stamp.Append(activeDisplayIntent.PlacementGeneration).Append(':').Append(activeDisplayIntent.OwnershipGeneration)
                .Append(':').Append(activeDisplayIntent.Root.GetInstanceID());
            foreach (var s in activeHeightStates)
                stamp.Append('|').Append(s.Level).Append(':').Append(s.SampleStride).Append(':')
                    .Append(new TerrainHeightCacheWindow(s.ActiveCache.CacheOriginTile, s.ActiveCache.CacheSize))
                    .Append(':').Append(s.ActiveRequiredWindow);
        }
        string next = stamp.ToString();
        if (hasPublishedHeightCacheCoverage == hasCoverage && publishedHeightCoverageStamp == next) return;
        hasPublishedHeightCacheCoverage = hasCoverage; publishedHeightCoverageStamp = next;
        publishedHeightCacheMinimumXZ = hasCoverage ? low : Vector2.zero;
        publishedHeightCacheMaximumXZ = hasCoverage ? high : Vector2.zero;
        HeightCacheCoverageChanged?.Invoke();
    }

    // =====================================================
    // OUTBOUND PREVIEW STATE NOTIFICATION
    // =====================================================

    private static void NotifyPreviewStateChanged()
    {
        if (hasAnalysisSourceIntent)
            EvaluateTerrainAnalysisSource(analysisSettings, LoadAuthoringData(), false);
        PublishTerrainAnalysisSourceState(true);
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
    internal static bool TryGetDisplayLodSliceIndex(int level, int tileX, int tileZ, out int sliceIndex)
    {
        sliceIndex = -1;
        if (!TryGetActiveDisplayLodCacheForValidation(level, out var cache)) return false;
        sliceIndex = cache.GetSliceIndex(tileX, tileZ);
        return sliceIndex >= 0;
    }

    internal static bool TryGetDisplayLodTileCoordinate(int level, int sliceIndex, out Vector2Int tile)
    {
        tile = default;
        return TryGetActiveDisplayLodCacheForValidation(level, out var cache) && cache.TryGetTileCoordinate(sliceIndex, out tile);
    }

    private static bool DisplayLodTileCurrentForValidation(int level, int tileX, int tileZ, out TerrainAuthoringPreviewCache cache)
    {
        if (!TryGetActiveDisplayLodCacheForValidation(level, out cache)) return false;
        var state = activeHeightStates[level]; var tile = new Vector2Int(tileX, tileZ);
        return state.ActiveAuthoringGeneration == authoringGeneration && !state.PendingDirtyTiles.Contains(tile)
            && cache.IsSliceFinalCompositeReady(tile);
    }

    internal static bool TryGetDisplayLodCompositeSliceRange(int level, int tileX, int tileZ,
        out float minimum, out float maximum)
    {
        minimum = maximum = 0f;
        return DisplayLodTileCurrentForValidation(level, tileX, tileZ, out var cache)
            && cache.TryGetCompositeSliceRange(tileX, tileZ, out minimum, out maximum);
    }

    internal static bool DisplaySetSignaturesCurrentForValidation(string committed, string overall)
    {
        if (activeHeightStates == null || activeHeightStates.Length == 0) return false;
        foreach (var state in activeHeightStates)
            if (!StateContentIsCurrent(state, committed, overall)) return false;
        return true;
    }

    internal static string CaptureDisplayIdentityForValidation()
    {
        var result = new System.Text.StringBuilder();
        if (activeHeightStates != null) foreach (var state in activeHeightStates)
        {
            var cache = state.ActiveCache;
            result.Append(state.Level).Append(':').Append(state.SampleStride).Append(':')
                .Append(cache?.HeightCache != null ? cache.HeightCache.GetInstanceID() : 0).Append(':')
                .Append(cache != null ? new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize).ToString() : "none").Append(';');
        }
        return result.ToString();
    }

    internal static string CaptureDisplaySignaturesForValidation()
    {
        var identity = new System.Text.StringBuilder();
        if (activeHeightStates != null)
            foreach (var state in activeHeightStates)
                identity.Append(state.Level).Append(':').Append(state.ActiveAuthoringGeneration).Append(':')
                    .Append(state.ActiveCache?.SourceCommittedHeightfieldSignature).Append(':')
                    .Append(state.ActiveCache?.SourceOverallAuthoringSignature).Append(';');
        return identity.ToString();
    }

}
