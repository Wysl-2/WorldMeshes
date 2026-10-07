using System;

/*
 * Read-only projection of the live edit-mode preview.
 *
 * The snapshot is informational only. Streaming, lifecycle, residency,
 * authoring, and Scene View ownership decisions continue to use their
 * established authoritative state directly.
 *
 * GPU values are estimates derived from resident cache allocations rather
 * than exact driver-level VRAM accounting.
 */
public static partial class TerrainAuthoringPreviewService
{
    private static bool transitionMemoryTrackingActive;

    private static long currentTransitionPeakGpuMemoryBytes;

    private static long lastTransitionPeakGpuMemoryBytes;

    internal static long PeakTransitionGpuMemoryBytes =>
        transitionMemoryTrackingActive
            ? currentTransitionPeakGpuMemoryBytes
            : lastTransitionPeakGpuMemoryBytes;



    private static void BeginTransitionMemoryTracking()
    {
        transitionMemoryTrackingActive =
            true;

        currentTransitionPeakGpuMemoryBytes =
            ApproximateTotalResidentGpuMemoryBytes;

        CaptureTransitionMemoryEstimate();
    }

    private static void CaptureTransitionMemoryEstimate()
    {
        if (!transitionMemoryTrackingActive)
        {
            return;
        }

        currentTransitionPeakGpuMemoryBytes =
            Math.Max(
                currentTransitionPeakGpuMemoryBytes,
                ApproximateTotalResidentGpuMemoryBytes
            );
    }

    private static void CompleteTransitionMemoryTracking()
    {
        if (!transitionMemoryTrackingActive)
        {
            return;
        }

        CaptureTransitionMemoryEstimate();

        lastTransitionPeakGpuMemoryBytes =
            currentTransitionPeakGpuMemoryBytes;

        currentTransitionPeakGpuMemoryBytes =
            0L;

        transitionMemoryTrackingActive =
            false;
    }
    internal static TerrainAuthoringPreviewRepresentationSnapshot CaptureRepresentation(TerrainAuthoringPreviewLodResidencyPlan plan)
    {
        return plan == null ? default : new TerrainAuthoringPreviewRepresentationSnapshot(plan.SampleStride, plan.SamplesPerSide, plan.SampleSpacing);
    }

    internal static TerrainAuthoringPreviewCacheSnapshot CaptureCacheMetadata(TerrainAuthoringPreviewCache cache,
        bool complete, bool current, long generation)
    {
        if (cache == null) return default;
        bool allocated = cache.HeightCache != null && cache.HeightCache.IsCreated();
        return new TerrainAuthoringPreviewCacheSnapshot(true, allocated,
            new TerrainAuthoringPreviewRepresentationSnapshot(cache.SampleStride, cache.SamplesPerSide, cache.SampleSpacing),
            new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize),
            allocated ? cache.HeightCache.GetInstanceID() : 0, allocated ? cache.SliceCount : 0,
            allocated && complete, allocated && current, generation, allocated ? cache.ApproximateGpuMemoryBytes : 0L);
    }

    private static bool PublishedDisplayDrawableForDiagnostics()
    {
        if (!Enabled || activeDisplayIntent == null || boundClipmapRoot == null
            || activeDisplayIntent.Root != boundClipmapRoot || activeHeightStates == null
            || activeHeightStates.Length != activeDisplayIntent.Plan.LevelCount
            || boundHeightRenderers.Count != activeHeightStates.Length * 2 - 1) return false;
        foreach (var binding in boundHeightRenderers) if (!binding.IsValid) return false;
        // Published states passed complete validation at handoff. No per-slice scan here.
        foreach (var state in activeHeightStates)
            if (state.WriteFailed || state.ActiveCache == null || !state.ActiveCache.IsReady) return false;
        return true;
    }

    private static bool StateCurrentForDiagnostics(TerrainAuthoringPreviewLodState state, bool configurationCurrent)
    {
        return configurationCurrent && state != null && state.CacheReady && !state.WriteFailed
            && state.PendingDirtyTiles.Count == 0 && state.ActiveAuthoringGeneration == authoringGeneration
            && state.ActiveCache != null && state.ActiveCache.IsReady;
    }

    internal static TerrainAuthoringPreviewWorkerSnapshot CaptureWorkerMetadata(TerrainAuthoringPreviewCacheSetTransition transaction)
    {
        if (transaction == null) return default;
        int representations = 0, reusable = 0;
        foreach (var entry in transaction.Entries)
        {
            representations += entry.Transition.SourceMaterializationTiles.Count;
            reusable += entry.Transition.ReusableRetainedTiles.Count;
        }
        return new TerrainAuthoringPreviewWorkerSnapshot(true, transaction.Publication, transaction.State,
            transaction.RequestGeneration, transaction.AuthoringGeneration, transaction.OwnershipGeneration,
            transaction.TotalWorkUnits > 0 ? UnityEngine.Mathf.Clamp01((float)transaction.CompletedWorkUnits / transaction.TotalWorkUnits) : 0f,
            transaction.SourceGroups.Count, transaction.SourceLoads, representations, transaction.MaterializedSlices,
            transaction.ComposedSlices, reusable, transaction.RetainedCopies, transaction.LastUpdateAllocations,
            transaction.LastUpdateLoads, transaction.LastUpdateCopies, transaction.LastUpdateMaterializations, transaction.LastUpdateCompositions);
    }

    private static TerrainAuthoringPreviewFailureSnapshot CaptureFailure(TerrainAuthoringPreviewCacheSetTransition transaction)
    {
        return transaction == null ? default : new TerrainAuthoringPreviewFailureSnapshot(true, transaction.Publication,
            transaction.FailedLevel, transaction.HasFailedTile, transaction.FailedTile, transaction.FailedWindow,
            transaction.RequestGeneration, transaction.DisplayIntent?.PlacementGeneration ?? 0L, transaction.Error);
    }

    private static TerrainAuthoringPreviewCacheSetTransition.Entry DisplayEntry(TerrainAuthoringPreviewCacheSetTransition transaction, int level)
    {
        return transaction != null && transaction.InProgress && transaction.Publication == TerrainAuthoringPreviewCachePublication.DisplayHeightSet
            && level < transaction.Entries.Length ? transaction.Entries[level] : null;
    }

    internal static TerrainAuthoringPreviewLodDiagnosticsSnapshot CaptureDisplayLodMetadata(int level,
        TerrainAuthoringPreviewLodState state, TerrainAuthoringPreviewLodResidencyPlan latest,
        TerrainAuthoringPreviewCacheSetTransition.Entry entry, TerrainAuthoringPreviewCacheSetTransition.Entry queued,
        bool configurationCurrent)
    {
        bool current = StateCurrentForDiagnostics(state, configurationCurrent);
        var active = CaptureCacheMetadata(state?.ActiveCache, state != null && !state.WriteFailed, current,
            state?.ActiveAuthoringGeneration ?? 0L);
        var staging = CaptureCacheMetadata(entry?.Destination?.StagingCache, entry?.Finalized ?? false,
            (entry?.Finalized ?? false) && entry.Destination?.StagingAuthoringGeneration == authoringGeneration,
            entry?.Destination?.StagingAuthoringGeneration ?? 0L);
        var representation = CaptureRepresentation(latest);
        bool comparable = configurationCurrent && active.HasTexture && latest != null
            && active.Representation.Stride == representation.Stride
            && active.Representation.SamplesPerSide == representation.SamplesPerSide
            && UnityEngine.Mathf.Approximately(active.Representation.SampleSpacing, representation.SampleSpacing);
        var health = comparable ? TerrainAuthoringPreviewResidencyPolicy.EvaluateSizeHealth(true, active.Window, latest.DesiredWindow)
            : TerrainAuthoringPreviewResidencySizeHealth.Unavailable;
        var work = entry?.Transition;
        return new TerrainAuthoringPreviewLodDiagnosticsSnapshot(level, latest != null, representation, active, staging,
            state?.ActiveRequiredWindow ?? default, latest?.RequiredWindow ?? default,
            // The plan's desired window includes the undirected guard. Directional prefetch is the frozen worker target.
            latest?.DesiredWindow ?? default, latest?.DesiredWindow ?? default,
            entry?.Destination?.RequestedRequiredWindow ?? default, entry?.Target ?? default, CaptureRepresentation(entry?.Plan),
            queued?.Target ?? default, CaptureRepresentation(queued?.Plan), state?.PendingDirtyTiles.Count ?? 0,
            state?.WriteFailed ?? false, state?.ActiveAuthoringGeneration ?? 0L,
            entry?.Destination?.RequestGeneration ?? queued?.Destination?.RequestGeneration ?? 0L,
            work?.State ?? TerrainAuthoringPreviewTransitionState.Planned,
            work?.RetainedTiles.Count ?? 0, work?.ReusableRetainedTiles.Count ?? 0,
            work?.EnteringTiles.Count ?? 0, work?.LeavingTiles.Count ?? 0, work?.RetainedGpuCopyCount ?? 0,
            work?.CommittedLoadCursor ?? 0,
            work != null ? Math.Max(0, work.SourceMaterializationTiles.Count - work.FullyComposedTileCount) : 0,
            work?.FullyComposedTileCount ?? 0, health);
    }

    internal static TerrainAuthoringPreviewResidencySizeHealth AggregateSizeHealth(
        System.Collections.Generic.IReadOnlyList<TerrainAuthoringPreviewLodDiagnosticsSnapshot> rows)
    {
        bool any = false, oversized = false, undersized = false;
        foreach (var row in rows)
        {
            if (!row.HasLatestPlan) continue;
            any = true;
            if (row.SizeHealth == TerrainAuthoringPreviewResidencySizeHealth.Unavailable)
                return TerrainAuthoringPreviewResidencySizeHealth.Unavailable;
            oversized |= row.SizeHealth == TerrainAuthoringPreviewResidencySizeHealth.Oversized;
            undersized |= row.SizeHealth == TerrainAuthoringPreviewResidencySizeHealth.Undersized;
        }
        return !any ? TerrainAuthoringPreviewResidencySizeHealth.Unavailable
            : oversized ? TerrainAuthoringPreviewResidencySizeHealth.Oversized
            : undersized ? TerrainAuthoringPreviewResidencySizeHealth.Undersized : TerrainAuthoringPreviewResidencySizeHealth.Healthy;
    }

    internal static TerrainAuthoringPreviewDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        bool configurationCurrent = activeDisplayIntent != null && activeDisplayIntent.Root == boundClipmapRoot
            && activeDisplayIntent.OwnershipGeneration == owner && activeDisplayIntent.ConfigurationMatches(activeDisplayIntent.Settings);
        bool drawable = PublishedDisplayDrawableForDiagnostics();
        bool current = drawable && configurationCurrent;
        int levelCount = Math.Max(activeHeightStates?.Length ?? 0, latestDisplayIntent?.Plan.LevelCount ?? 0);
        if (currentCacheSetTransition != null && currentCacheSetTransition.InProgress
            && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.DisplayHeightSet)
            levelCount = Math.Max(levelCount, currentCacheSetTransition.Entries.Length);
        if (pendingCacheSetTransition != null && pendingCacheSetTransition.InProgress
            && pendingCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.DisplayHeightSet)
            levelCount = Math.Max(levelCount, pendingCacheSetTransition.Entries.Length);
        var rows = new TerrainAuthoringPreviewLodDiagnosticsSnapshot[levelCount];
        bool coverage = current && latestDisplayIntent != null && latestDisplayIntent.OwnershipGeneration == owner
            && latestDisplayIntent.Root == boundClipmapRoot && latestDisplayIntent.ConfigurationMatches(latestDisplayIntent.Settings)
            && activeHeightStates.Length == latestDisplayIntent.Plan.LevelCount;
        for (int level = 0; level < levelCount; level++)
        {
            var state = activeHeightStates != null && level < activeHeightStates.Length ? activeHeightStates[level] : null;
            var latest = latestDisplayIntent != null && level < latestDisplayIntent.Plan.LevelCount ? latestDisplayIntent.Plan.Levels[level] : null;
            rows[level] = CaptureDisplayLodMetadata(level, state, latest,
                DisplayEntry(currentCacheSetTransition, level), DisplayEntry(pendingCacheSetTransition, level), configurationCurrent);
            var row = rows[level];
            if (state != null) current &= row.Active.Current;
            coverage &= row.Active.Current && latest != null && row.Active.Window.Contains(latest.RequiredWindow)
                && row.Active.Representation.Stride == latest.SampleStride
                && row.Active.Representation.SamplesPerSide == latest.SamplesPerSide
                && UnityEngine.Mathf.Approximately(row.Active.Representation.SampleSpacing, latest.SampleSpacing);
        }
        bool placement = drawable && latestDisplayIntent != null && activeDisplayIntent != null
            && activeDisplayIntent.OwnershipGeneration == owner && latestDisplayIntent.Root == activeDisplayIntent.Root
            && latestDisplayIntent.OwnershipGeneration == owner
            && TerrainAuthoringPreviewDisplayIntent.PlacementMatches(activeDisplayIntent.Layout, latestDisplayIntent.Layout);
        bool latestReady = coverage && placement;
        bool waiting = Enabled && latestDisplayIntent != null && !latestReady;
        var failure = CaptureFailure(lastFailedCacheSetRequest);
        var analysisFailure = CaptureFailure(lastFailedAnalysisCacheSetRequest);
        bool failureRelevant = failure.Present && waiting && latestDisplayIntent != null
            && lastFailedCacheSetRequest.DisplayIntent?.PlacementGeneration == latestDisplayIntent.PlacementGeneration;
        var analysis = CaptureAnalysisSourceMetadata(configurationCurrent);
        var analysisEntry = currentCacheSetTransition != null && currentCacheSetTransition.InProgress
            && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis
            ? currentCacheSetTransition.Entries[0] : null;
        var analysisActive = CaptureCacheMetadata(analysisOwnedState?.ActiveCache,
            analysisOwnedState != null && !analysisOwnedState.WriteFailed,
            StateCurrentForDiagnostics(analysisOwnedState, analysisOwnedOwnershipGeneration == owner
                && NativeCacheGeometryCurrentForDiagnostics(analysisOwnedState?.ActiveCache, analysis.SamplesPerSide, analysis.SampleSpacing)),
            analysisOwnedState?.ActiveAuthoringGeneration ?? 0L);
        var analysisStaging = CaptureCacheMetadata(analysisEntry?.Destination?.StagingCache,
            analysisEntry?.Finalized ?? false, (analysisEntry?.Finalized ?? false)
                && analysisEntry.Destination?.StagingAuthoringGeneration == authoringGeneration,
            analysisEntry?.Destination?.StagingAuthoringGeneration ?? 0L);
        var worker = CaptureWorkerMetadata(currentCacheSetTransition != null && currentCacheSetTransition.InProgress ? currentCacheSetTransition : null);
        var queued = CaptureWorkerMetadata(pendingCacheSetTransition != null && pendingCacheSetTransition.InProgress ? pendingCacheSetTransition : null);
        return new TerrainAuthoringPreviewDiagnosticsSnapshot(Enabled, current, drawable, coverage, placement, latestReady,
            status, statusMessage, streamingState, streamingStatusMessage,
            worker.Present ? worker.Progress : queued.Present ? queued.Progress : 0f,
            IsStreaming, waiting, authoringGeneration, streamingRequestGeneration, worker, queued, analysis,
            analysisActive, analysisStaging, CaptureHeightOwnership(), failure, analysisFailure, failureRelevant,
            PeakTransitionGpuMemoryBytes, TerrainAuthoringPreviewCache.DiagnosticCreateCount,
            TerrainAuthoringPreviewCache.DiagnosticDisposeCount, TerrainAuthoringPreviewCache.DiagnosticLiveCount,
            IsEditorLifecycleStable, CanRunEditorPreviewWork, lifecycleResumePending, suspensionReasons,
            TerrainAuthoringSceneViewController.HasControllingSceneView,
            TerrainAuthoringSceneViewController.ControllingSceneViewInstanceId, owner,
            TerrainAuthoringSceneViewController.FollowSceneView, TerrainAuthoringSceneViewController.FollowSource,
            TerrainAuthoringSceneViewController.FreezePreview,
            diagnosticPendingGeographicDirty.Count, LastDirtyUpdateLoads, LastDirtyUpdateMaterializations, LastDirtyUpdateCompositions,
            lastStreamingCancellationReason, rows);
    }

}
