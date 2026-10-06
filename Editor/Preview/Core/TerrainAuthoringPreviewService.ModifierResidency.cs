using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    private static long authoringGeneration;

    private static long activeCacheAuthoringGeneration;

    private static string lastAuthoringGenerationReason =
        "";

    private static int lastGlobalDirtyTileCount;

    private static int lastResidentDirtyTileCount;

    private static int lastNonresidentDirtyTileCount;

    private static int lastPublishedCompositeTileCount;

    public static long AuthoringGeneration =>
        authoringGeneration;

    public static long ActiveCacheAuthoringGeneration => CacheReady ? authoringGeneration : -1L;

    public static string LastAuthoringGenerationReason =>
        lastAuthoringGenerationReason;

    public static int PendingGlobalDirtyTileCount =>
        dirtyCompositeTiles.Count;

    public static int LastGlobalDirtyTileCount =>
        lastGlobalDirtyTileCount;

    public static int LastResidentDirtyTileCount =>
        lastResidentDirtyTileCount;

    public static int LastNonresidentDirtyTileCount =>
        lastNonresidentDirtyTileCount;

    public static int LastPublishedCompositeTileCount =>
        lastPublishedCompositeTileCount;

    public static bool InteractiveModifierEditActive =>
        TerrainAuthoringModifierService
            .HasActiveInteractiveEdit;

    public static bool StreamingRestartDeferredForInteractiveEdit =>
        HasActiveInteractiveTerrainAuthoringEdit
        &&
        (
            hasPendingStreamingStart
            ||
            (
                currentCacheSetTransition != null
                &&
                TransitionInProgress
            )
        );

    private static void RegisterPreviewAuthoringInvalidation(
        string reason
    )
    {
        authoringGeneration =
            CalculateNextAuthoringGeneration(
                authoringGeneration
            );

        lastAuthoringGenerationReason =
            string.IsNullOrEmpty(
                reason
            )
                ? "Preview-affecting authoring state changed."
                : reason;

        ClearTransitionFailureSuppression();
        InvalidateActiveHeightContent();
        InvalidateTerrainAnalysisAuthoring();

        bool hasStreamingWork =
            hasPendingStreamingStart
            ||
            (
                currentCacheSetTransition != null
                &&
                TransitionInProgress
            );

        if (hasStreamingWork)
        {
            CancelCurrentStreamingTransition(
                "Preview authoring changed while staging was in progress. " +
                "The stale destination was cancelled and the latest " +
                "residency intent was preserved.",
                false
            );
        }

        if (
            streamingState ==
                TerrainAuthoringPreviewStreamingState.Failed
        )
        {
            lastStreamingFailureMessage =
                "";

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Idle,
                "Authoring changed; the previous streaming failure is no longer current."
            );
        }
        else
        {
            /*
             * Generation is part of authoring-content streaming diagnostics. Force a
             * publication even when the streaming phase/counters are otherwise
             * unchanged.
             */
            lastPublishedStreamingSnapshot =
                "";

            PublishStreamingStateIfChanged();
        }
    }

    internal static long CalculateNextAuthoringGeneration(
        long currentGeneration
    )
    {
        return
            currentGeneration < long.MaxValue
                ? currentGeneration + 1L
                : long.MaxValue;
    }



    internal static bool IsTransitionAuthoringStateCurrent(
        TerrainAuthoringPreviewCacheTransition transition,
        long currentGeneration,
        string committedSignature,
        string overallSignature
    )
    {
        return
            transition != null
            &&
            transition.TargetAuthoringGeneration ==
                currentGeneration
            &&
            transition.TargetCommittedHeightfieldSignature ==
                (committedSignature ?? "")
            &&
            transition.TargetOverallAuthoringSignature ==
                (overallSignature ?? "");
    }

    internal static bool ShouldDeferStreamingRestart(
        bool hasActiveInteractiveEdit
    )
    {
        return
            hasActiveInteractiveEdit;
    }

    internal static bool CanAcknowledgeActiveAuthoringState(
        bool committedSourceCurrent,
        int residentDirtyTileCount
    )
    {
        return
            committedSourceCurrent
            &&
            residentDirtyTileCount >= 0;
    }

    internal static void PartitionDirtyTilesForActiveWindow(
        IEnumerable<Vector2Int> globalDirtyTiles,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        List<Vector2Int> residentTiles,
        List<Vector2Int> nonresidentTiles
    )
    {
        if (residentTiles == null || nonresidentTiles == null)
        {
            return;
        }

        residentTiles.Clear();
        nonresidentTiles.Clear();

        if (globalDirtyTiles == null)
        {
            return;
        }

        HashSet<Vector2Int> unique =
            new HashSet<Vector2Int>();

        foreach (
            Vector2Int tile
            in globalDirtyTiles
        )
        {
            unique.Add(
                tile
            );
        }

        List<Vector2Int> ordered =
            new List<Vector2Int>(
                unique
            );

        SortWorldTilesRowMajor(
            ordered
        );

        for (
            int index = 0;
            index < ordered.Count;
            index++
        )
        {
            Vector2Int tile =
                ordered[index];

            if (
                hasActiveWindow
                &&
                activeWindow.Contains(
                    tile
                )
            )
            {
                residentTiles.Add(
                    tile
                );
            }
            else
            {
                nonresidentTiles.Add(
                    tile
                );
            }
        }
    }

    private static void SortWorldTilesRowMajor(
        List<Vector2Int> tiles
    )
    {
        if (tiles == null)
        {
            return;
        }

        tiles.Sort(
            (a, b) =>
            {
                int zCompare =
                    a.y.CompareTo(
                        b.y
                    );

                if (zCompare != 0)
                {
                    return zCompare;
                }

                return
                    a.x.CompareTo(
                        b.x
                    );
            }
        );
    }



    public static TerrainAuthoringPreviewReadiness GetWorldTileReadiness(
        Vector2Int worldTile
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        bool previewAvailable =
            TryResolveReadinessContext(
                worldSettings,
                out string currentCommittedSignature
            );

        return
            EvaluateWorldTileReadiness(
                worldSettings,
                worldTile,
                previewAvailable,
                currentCommittedSignature
            );
    }

    private static bool TryResolveReadinessContext(
        WorldSettings worldSettings,
        out string currentCommittedSignature
    )
    {
        currentCommittedSignature =
            "";

        if (
            worldSettings == null
            ||
            !Enabled
            ||
            Application.isPlaying
            ||
            UnityEditor.EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            LoadAuthoringData() == null
            ||
            Status ==
                TerrainAuthoringPreviewStatus.Disabled
            ||
            Status ==
                TerrainAuthoringPreviewStatus.PlayMode
            ||
            Status ==
                TerrainAuthoringPreviewStatus.AuthoringUnavailable
            ||
            Status ==
                TerrainAuthoringPreviewStatus.ClipmapUnavailable

        )
        {
            return false;
        }

        currentCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        return
            !string.IsNullOrEmpty(
                currentCommittedSignature
            );
    }

    private static TerrainAuthoringPreviewReadiness EvaluateWorldTileReadiness(WorldSettings settings,
        Vector2Int tile, bool previewAvailable, string committed)
    {
        if (settings == null || tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth || tile.y >= settings.HeightTileGridHeight)
            return TerrainAuthoringPreviewReadiness.OutsideWorld;
        var state = FindFinestResidentDisplayState(tile);
        var cache = state?.ActiveCache;
        bool pending = state != null && (state.WriteFailed || !state.CacheReady || state.PendingDirtyTiles.Contains(tile)
            || state.ActiveAuthoringGeneration != authoringGeneration || dirtyCompositeTiles.Contains(tile)
            || IsWorldTilePendingRegionalElevationRecomposition(settings, tile));
        return TerrainAuthoringPreviewReadinessPolicy.EvaluateTile(previewAvailable, true, cache != null,
            cache != null && cache.SourceCommittedHeightfieldSignature == committed, cache != null,
            cache != null && cache.IsSliceFinalCompositeReady(tile), pending);
    }

    public static TerrainAuthoringPreviewReadiness GetWorldTileReadiness(
        int tileX,
        int tileZ
    )
    {
        return
            GetWorldTileReadiness(
                new Vector2Int(
                    tileX,
                    tileZ
                )
            );
    }

    public static TerrainAuthoringPreviewReadiness GetWorldPositionReadiness(
        Vector2 worldXZ
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            worldXZ.x < 0f
            ||
            worldXZ.y < 0f
            ||
            worldXZ.x > worldSize.x
            ||
            worldXZ.y > worldSize.y
        )
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        int tileX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    worldXZ.x /
                    tileWorldSize
                ),
                0,
                Mathf.Max(
                    0,
                    worldSettings.HeightTileGridWidth - 1
                )
            );

        int tileZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    worldXZ.y /
                    tileWorldSize
                ),
                0,
                Mathf.Max(
                    0,
                    worldSettings.HeightTileGridHeight - 1
                )
            );

        return
            GetWorldTileReadiness(
                tileX,
                tileZ
            );
    }

    public static TerrainAuthoringPreviewReadiness GetWorldPositionReadiness(
        Vector3 worldPosition
    )
    {
        return
            GetWorldPositionReadiness(
                new Vector2(
                    worldPosition.x,
                    worldPosition.z
                )
            );
    }

    public static TerrainAuthoringPreviewReadiness GetWorldBoundsReadiness(
        Bounds bounds,
        int samplePadding = 1
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            bounds.max.x < 0f
            ||
            bounds.max.z < 0f
            ||
            bounds.min.x > worldSize.x
            ||
            bounds.min.z > worldSize.y
        )
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        List<Vector2Int> tiles =
            new List<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                bounds,
                tiles,
                samplePadding
            );

        if (tiles.Count <= 0)
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        SortWorldTilesRowMajor(
            tiles
        );

        bool previewAvailable =
            TryResolveReadinessContext(
                worldSettings,
                out string currentCommittedSignature
            );

        for (
            int index = 0;
            index < tiles.Count;
            index++
        )
        {
            TerrainAuthoringPreviewReadiness readiness =
                EvaluateWorldTileReadiness(
                    worldSettings,
                    tiles[index],
                    previewAvailable,
                    currentCommittedSignature
                );

            if (
                readiness ==
                    TerrainAuthoringPreviewReadiness.PreviewUnavailable
            )
            {
                return readiness;
            }

            if (
                readiness !=
                    TerrainAuthoringPreviewReadiness.Ready
            )
            {
                return
                    TerrainAuthoringPreviewReadiness.Loading;
            }
        }

        return
            TerrainAuthoringPreviewReadiness.Ready;
    }
    private static readonly HashSet<Vector2Int> pendingCompositePublication = new HashSet<Vector2Int>();
    private static readonly HashSet<Vector2Int> pendingNativePublication = new HashSet<Vector2Int>();
    private static Texture2D activeDirtySource;
    private static Vector2Int activeDirtySourceTile;
    private static long activeDirtySourceGeneration;
    private static readonly TerrainAuthoringPreviewHeightMaterializer activeDirtyMaterializer = new TerrainAuthoringPreviewHeightMaterializer();
    private static long activeDirtyFailureGeneration = -1L;
    internal static int LastDirtyUpdateLoads { get; private set; }
    internal static int LastDirtyUpdateMaterializations { get; private set; }
    internal static int LastDirtyUpdateCompositions { get; private set; }
    private static bool HasPendingActiveDirtyWork
    {
        get
        {
            if (activeHeightStates != null) foreach (var s in activeHeightStates)
                if (s.PendingDirtyTiles.Count > 0) return true;
            return false;
        }
    }
    private static bool HasRequiredActiveDirtyWork
    {
        get
        {
            if (activeHeightStates != null && activeHeightStates.Length > 0 && activeHeightStates[0].PendingDirtyTiles.Count > 0) return true;
            if (activeHeightStates != null) foreach (var s in activeHeightStates)
                foreach (var tile in s.PendingDirtyTiles) if (s.ActiveRequiredWindow.Contains(tile)) return true;
            return false;
        }
    }

    private static TerrainAuthoringPreviewLodState FindFinestResidentDisplayState(Vector2Int tile)
    {
        if (activeHeightStates != null) foreach (var s in activeHeightStates)
            if (s.ActiveCache != null && s.ActiveCache.IsReady && s.ActiveCache.GetSliceIndex(tile.x, tile.y) >= 0) return s;
        return null;
    }

    private static void ReleaseActiveDirtySource()
    {
        activeDirtyMaterializer.ReleaseTextureBindings(); activeDirtySource = null; activeDirtySourceGeneration = 0;
    }

    private static void InvalidateActiveHeightContent()
    {
        ReleaseActiveDirtySource();
        if (activeHeightStates != null) foreach (var s in activeHeightStates) s.CacheReady = false;
        activeCacheAuthoringGeneration = -1L;
    }

    // Every obligation is assigned before the incoming global/regional scope is
    // consumed. Window clipping keeps regional invalidation bounded by residency.
    private static bool TryProjectPendingDisplayAuthoring(WorldSettings settings, string committed,
        string overall, out string error)
    {
        error = "";
        if (activeHeightStates == null) return true;
        if (dirtyCompositeTiles.Count == 0 && !hasPendingRegionalElevationInvalidation
            && !overallSignatureAcknowledgementRequested) return true;
        var modifierResident = new HashSet<Vector2Int>(); var regionalResident = new HashSet<Vector2Int>();
        foreach (var s in activeHeightStates)
        {
            var cache = s.ActiveCache;
            if (cache == null || !cache.IsReady || cache.SourceCommittedHeightfieldSignature != committed) continue;
            var window = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
            QueueDirtyTilesForLod(s, window, dirtyCompositeTiles, modifierResident);
            if (hasPendingRegionalElevationInvalidation)
            {
                var resident = new List<Vector2Int>();
                if (!TerrainRegionalElevationResidencyPolicy.TryCollectResidentTiles(settings,
                    pendingRegionalElevationInvalidation, true, window, resident, out error)) return false;
                QueueDirtyTilesForLod(s, window, resident, regionalResident);
                foreach (var tile in resident) s.PendingRegionalTiles.Add(tile);
            }
            s.DirtyTargetGeneration = authoringGeneration;
            if (s.PendingDirtyTiles.Count > 0) s.CacheReady = false;
        }
        lastGlobalDirtyTileCount = dirtyCompositeTiles.Count;
        lastResidentDirtyTileCount = modifierResident.Count;
        lastNonresidentDirtyTileCount = Math.Max(0, lastGlobalDirtyTileCount - lastResidentDirtyTileCount);
        lastPublishedCompositeTileCount = 0;
        if (hasPendingRegionalElevationInvalidation)
        {
            lastRegionalResidentAffectedTileCount = regionalResident.Count;
            lastRegionalNonresidentAffectedTileCount = Math.Max(0L, lastRegionalLogicalAffectedTileCount - regionalResident.Count);
            ConsumePendingRegionalElevationInvalidation(0);
        }
        dirtyCompositeTiles.Clear(); overallSignatureAcknowledgementRequested = false;
        AcknowledgeCompletedDisplayAuthoring(committed, overall);
        return true;
    }

    private static void AcknowledgeCompletedDisplayAuthoring(string committed, string overall)
    {
        if (activeHeightStates == null) return;
        bool all = true;
        foreach (var s in activeHeightStates)
        {
            var c = s.ActiveCache;
            if (c != null && c.IsCompleteForActivation && c.SourceCommittedHeightfieldSignature == committed
                && !s.WriteFailed && s.PendingDirtyTiles.Count == 0)
            {
                c.MarkOverallAuthoringSignature(overall); s.ActiveAuthoringGeneration = authoringGeneration;
                s.DirtyTargetGeneration = authoringGeneration; s.CacheReady = true;
            }
            else all = false;
        }
        activeCacheAuthoringGeneration = all ? authoringGeneration : -1L;
    }

    private static TerrainAuthoringPreviewLodState ChooseActiveDirtyDestination(out Vector2Int tile)
    {
        tile = default;
        if (activeHeightStates == null) return null;
        // Finish a geographic group with its one held native asset. During a
        // gesture, newly queued fine work may preempt a held coarse destination.
        if (activeDirtySource != null)
        {
            bool finePending = HasActiveInteractiveTerrainAuthoringEdit && activeHeightStates[0].PendingDirtyTiles.Count > 0;
            foreach (var s in activeHeightStates)
                if (s.PendingDirtyTiles.Contains(activeDirtySourceTile) && (!finePending || s.Level == 0))
                { tile = activeDirtySourceTile; return s; }
            ReleaseActiveDirtySource();
        }
        if (activeHeightStates[0].PendingDirtyTiles.Count > 0)
        {
            var fine = new List<Vector2Int>(activeHeightStates[0].PendingDirtyTiles); SortWorldTilesRowMajor(fine);
            tile = fine[0]; return activeHeightStates[0];
        }
        foreach (bool required in new[] { true, false })
            foreach (var s in activeHeightStates)
            {
                bool found = false;
                foreach (var t in s.PendingDirtyTiles)
                {
                    if (s.ActiveRequiredWindow.Contains(t) != required) continue;
                    if (!found || t.y < tile.y || (t.y == tile.y && t.x < tile.x)) { tile = t; found = true; }
                }
                if (found) return s;
            }
        return null;
    }

    private static void AdvanceActiveDisplayDirty(WorldSettings settings, TerrainAuthoringData data,
        System.Diagnostics.Stopwatch watch)
    {
        LastDirtyUpdateLoads = LastDirtyUpdateMaterializations = LastDirtyUpdateCompositions = 0;
        if (activeDirtyFailureGeneration == authoringGeneration || !HasPendingActiveDirtyWork) return;
        if (activeDirtySource != null && activeDirtySourceGeneration != authoringGeneration) ReleaseActiveDirtySource();
        var state = ChooseActiveDirtyDestination(out Vector2Int tile);
        if (state == null) return;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        var cache = state.ActiveCache;
        if (cache == null || !cache.IsReady || cache.SourceCommittedHeightfieldSignature != committed) { ScheduleRefresh(); return; }
        string error = ""; bool attemptedWrite = false;
        try
        {
            if (activeDirtySource == null)
            {
                if (!TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile(settings, tile, out activeDirtySource, out error))
                    throw new InvalidOperationException(error);
                activeDirtySourceTile = tile; activeDirtySourceGeneration = authoringGeneration; LastDirtyUpdateLoads++;
                if (watch.Elapsed.TotalMilliseconds >= DefaultSoftWorkBudgetMilliseconds) return;
            }
            // Materialize, compose and commit final readiness as one indivisible
            // visible unit; never leave a base-only active slice between callbacks.
            attemptedWrite = true;
            LastDirtyUpdateMaterializations++;
            if (!cache.TryMaterializeCommittedBaseTile(activeDirtySource, activeDirtyMaterializer, tile, out error)
                || !cache.TryGetCommittedRange(tile, out float low, out float high, out error))
                throw new InvalidOperationException(error);
            LastDirtyUpdateCompositions++;
            if (!heightCompositor.TryComposeTile(cache.HeightCache, tile, cache.GetSliceIndex(tile.x, tile.y),
                cache.SamplesPerSide, cache.SampleSpacing, settings.HeightTileWorldSize, cache.WorldSizeXZ,
                data, low, high, out float finalLow, out float finalHigh, out error)
                || !cache.ApplyCompositeSliceRangeBatch(new[]
                    { new TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate(tile, finalLow, finalHigh) },
                    out _, out error)) throw new InvalidOperationException(error);
            activeDirtyMaterializer.ReleaseTextureBindings();
            state.PendingDirtyTiles.Remove(tile); state.SuccessfulDirtyTiles.Add(tile);
            if (state.PendingRegionalTiles.Remove(tile)) lastRegionalPublishedCompositeTileCount++;
            if (state.PendingDirtyTiles.Count == 0) state.WriteFailed = false;
            pendingCompositePublication.Add(tile);
            if (state.Level == 0 && state.SampleStride == 1) pendingNativePublication.Add(tile);
            bool groupPending = false;
            foreach (var s in activeHeightStates) if (s.PendingDirtyTiles.Contains(tile)) groupPending = true;
            if (!groupPending) ReleaseActiveDirtySource();
            AcknowledgeCompletedDisplayAuthoring(committed, overall);
            RefreshAggregateHeightRange(!ActiveHeightContentIsCurrent(settings, data));
            if (!ApplyCurrentPreviewBounds(boundClipmapRoot, out error)) throw new InvalidOperationException(error);
            PublishCompletedDisplayDirtyTiles(committed, overall);
            ScheduleRefresh(); NotifyPreviewStateChanged(); RepaintEditorViews();
        }
        catch (Exception exception)
        {
            state.PendingDirtyTiles.Add(tile); state.CacheReady = false;
            activeDirtyFailureGeneration = authoringGeneration; ReleaseActiveDirtySource();
            if (attemptedWrite)
            {
                state.WriteFailed = true;
                TerrainAuthoringPreviewHeightBindingUtility.Disable(boundHeightRenderers, state.Level);
            }
            SetStatus(TerrainAuthoringPreviewStatus.Error, $"Resident Height update failed at LOD {state.Level}, tile {tile}. "
                + "The obligation is retained for explicit refresh or newer authoring. " + exception.Message);
            NotifyPreviewStateChanged();
        }
    }

    private static void PublishCompletedDisplayDirtyTiles(string committed, string overall)
    {
        if (activeHeightStates == null) return;
        var native = activeHeightStates[0];
        if (native.SampleStride == 1 && StateContentIsCurrent(native, committed, overall) && pendingNativePublication.Count > 0)
        {
            var tiles = new List<Vector2Int>(pendingNativePublication); SortWorldTilesRowMajor(tiles);
            pendingNativePublication.Clear(); PublishNativeTerrainAnalysisCompositeUpdate(tiles);
        }
        var completed = new List<Vector2Int>();
        foreach (var tile in pendingCompositePublication)
        {
            bool current = true;
            foreach (var s in activeHeightStates)
                if (s.ActiveCache.GetSliceIndex(tile.x, tile.y) >= 0 && !StateContentIsCurrent(s, committed, overall)) current = false;
            if (current) completed.Add(tile);
        }
        if (completed.Count == 0) return;
        SortWorldTilesRowMajor(completed);
        foreach (var tile in completed) pendingCompositePublication.Remove(tile);
        lastPublishedCompositeTileCount = completed.Count;
        CompositeTilesUpdated?.Invoke(completed);
    }

    internal static void QueueDirtyTilesForLod(TerrainAuthoringPreviewLodState state,
        TerrainHeightCacheWindow physicalWindow, IEnumerable<Vector2Int> tiles, ISet<Vector2Int> resident)
    {
        foreach (var tile in tiles)
            if (physicalWindow.Contains(tile))
            {
                state.PendingDirtyTiles.Add(tile); state.SuccessfulDirtyTiles.Remove(tile); resident?.Add(tile);
            }
    }

}
