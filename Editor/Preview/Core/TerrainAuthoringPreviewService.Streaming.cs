using System;
using System.Diagnostics;
using UnityEngine;

public enum TerrainAuthoringPreviewStreamingState
{
    Idle, Preparing, CopyingRetained, Loading, Composing, Finalizing, Activating, Failed
}

public static partial class TerrainAuthoringPreviewService
{
    public static event Action StreamingStateChanged;
    internal const int DefaultRetainedCopiesPerUpdate = 8;
    internal const int DefaultCommittedLoadsPerUpdate = 1;
    internal const int DefaultCompositionsPerUpdate = 1;
    internal const int DefaultMaterializationsPerUpdate = 8;
    internal const double DefaultSoftWorkBudgetMilliseconds = 4.0;

    private static TerrainAuthoringPreviewCacheSetTransition currentCacheSetTransition;
    private static TerrainAuthoringPreviewCacheSetTransition pendingCacheSetTransition;
    private static TerrainAuthoringPreviewStreamingState streamingState;
    private static string streamingStatusMessage = "Streaming is idle.";
    private static bool hasLatestRequiredResidencyWindow;
    private static TerrainHeightCacheWindow latestRequiredResidencyWindow;
    private static long streamingRequestGeneration;
    private static float streamingProgress;
    private static string lastStreamingFailureMessage = "";
    private static string lastStreamingCancellationReason = "";
    private static string lastPublishedStreamingSnapshot = "";
    private static bool hasPendingStreamingStart => pendingCacheSetTransition != null;
    private static bool pendingStreamingCommittedRebuild => pendingCacheSetTransition != null
        && pendingCacheSetTransition.RebuildRequested;

    public static TerrainAuthoringPreviewStreamingState StreamingState => streamingState;
    public static string StreamingStateLabel
    {
        get
        {
            switch (streamingState)
            {
                case TerrainAuthoringPreviewStreamingState.CopyingRetained:
                    return "Copying Retained";

                case TerrainAuthoringPreviewStreamingState.Loading:
                    return "Loading";

                case TerrainAuthoringPreviewStreamingState.Composing:
                    return "Composing";

                case TerrainAuthoringPreviewStreamingState.Finalizing:
                    return "Finalizing";

                case TerrainAuthoringPreviewStreamingState.Activating:
                    return "Activating";

                case TerrainAuthoringPreviewStreamingState.Preparing:
                    return "Preparing";

                case TerrainAuthoringPreviewStreamingState.Failed:
                    return "Failed";

                default:
                    return "Idle";
            }
        }
    }

    public static string StreamingStatusMessage => streamingStatusMessage;
    public static bool IsStreaming => streamingState != TerrainAuthoringPreviewStreamingState.Idle
        && streamingState != TerrainAuthoringPreviewStreamingState.Failed;
    public static bool IsWaitingForStreamingCoverage => Enabled && hasLatestRequiredResidencyWindow
        && !ActiveCacheContains(latestRequiredResidencyWindow);
    public static float StreamingProgress => Mathf.Clamp01(streamingProgress);
    public static long StreamingRequestGeneration => streamingRequestGeneration;
    public static int StreamingRetainedTileCount => SumSetTiles(0);
    public static int StreamingRetainedCopiedCount => currentCacheSetTransition?.RetainedCopies ?? 0;
    public static int StreamingSourceTileCount => SumSetTiles(1);
    public static int StreamingSourceLoadedCount => currentCacheSetTransition?.SourceLoads ?? 0;
    public static int StreamingSourceMaterializedCount => currentCacheSetTransition?.MaterializedSlices ?? 0;
    public static int StreamingSourceComposedCount => currentCacheSetTransition?.ComposedSlices ?? 0;
    public static int StreamingRetainedCopiesPerUpdate => DefaultRetainedCopiesPerUpdate;
    public static int StreamingCommittedLoadsPerUpdate => DefaultCommittedLoadsPerUpdate;
    public static int StreamingCompositionsPerUpdate => DefaultCompositionsPerUpdate;
    public static int StreamingMaterializationsPerUpdate => DefaultMaterializationsPerUpdate;
    public static double StreamingSoftWorkBudgetMilliseconds => DefaultSoftWorkBudgetMilliseconds;
    public static string StreamingCoverageLabel => !TryGetActiveResidentWindow(out _)
        ? "No Active Cache" : IsWaitingForStreamingCoverage ? "Waiting For Destination" : "Active Safe";

    private static int SumSetTiles(int kind)
    {
        int count = 0;
        if (currentCacheSetTransition == null) return count;
        foreach (var e in currentCacheSetTransition.Entries)
        {
            var t = e.Transition;
            count += kind == 0 ? t.ReusableRetainedTiles.Count
                : kind == 1 ? t.SourceMaterializationTiles.Count
                : kind == 2 ? t.RetainedTiles.Count
                : kind == 3 ? t.EnteringTiles.Count : t.LeavingTiles.Count;
        }
        return count;
    }

    public static bool TryGetLatestRequiredResidentWindow(out TerrainHeightCacheWindow window)
    {
        window = hasLatestRequiredResidencyWindow ? latestRequiredResidencyWindow : default;
        return hasLatestRequiredResidencyWindow && window.IsValid;
    }

    internal static bool RecordStreamingResidencyIntent(
        TerrainHeightCacheWindow requiredWindow, TerrainHeightCacheWindow desiredWindow)
    {
        bool changed = !TerrainAuthoringPreviewStreamingPolicy.IsSameResidencyIntent(
            hasLatestRequiredResidencyWindow, latestRequiredResidencyWindow,
            hasDesiredResidencyWindow, desiredResidencyWindow, requiredWindow, desiredWindow);
        hasLatestRequiredResidencyWindow = true;
        latestRequiredResidencyWindow = requiredWindow;
        hasDesiredResidencyWindow = true;
        desiredResidencyWindow = desiredWindow;
        if (changed)
        {
            streamingRequestGeneration++;
            if (streamingState == TerrainAuthoringPreviewStreamingState.Failed)
            {
                lastStreamingFailureMessage = "";
                streamingState = TerrainAuthoringPreviewStreamingState.Idle;
            }
        }
        PublishStreamingStateIfChanged();
        return changed;
    }

    internal static void NotifyStreamingIntentNoLongerRequiresTarget()
    {
        if (hasPendingStreamingStart
            && pendingCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativePreview
            && !pendingStreamingCommittedRebuild)
        {
            ClearPendingStreamingStart();
            if (!TransitionInProgress) SetStreamingState(TerrainAuthoringPreviewStreamingState.Idle,
                "Queued prefetch was cancelled because active residency is comfortably sufficient.");
        }
    }

    private static bool RequestIncrementalStagedTransition(
        WorldSettings settings, TerrainAuthoringData data, TerrainHeightCacheWindow target,
        string committed, string overall, Transform clipmapRoot, out string error)
    {
        error = "";
        if (settings == null || data == null || clipmapRoot == null || !target.IsValid
            || string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            error = "The incremental staging request is missing required state.";
            return false;
        }
        bool preparingUnbound = (TransitionInProgress && currentCacheSetTransition.Publication ==
            TerrainAuthoringPreviewCachePublication.PreparedHeightSet)
            || (hasPendingStreamingStart && pendingCacheSetTransition.Publication ==
            TerrainAuthoringPreviewCachePublication.PreparedHeightSet);
        bool nativeCoverageCurrent = activeCache != null && activeCache.IsCompleteForActivation
            && activeCacheAuthoringGeneration == authoringGeneration
            && activeCache.SourceCommittedHeightfieldSignature == committed
            && activeCache.SourceOverallAuthoringSignature == overall
            && (!hasLatestRequiredResidencyWindow || ActiveCacheContains(latestRequiredResidencyWindow));
        if (preparingUnbound && nativeCoverageCurrent && !committedRebuildRequested)
            return true; // Retry optional native guard work after the unbound result publishes.
        var plan = new TerrainAuthoringPreviewResidencyPlan
        {
            LevelCount = 1, Levels = new[] { new TerrainAuthoringPreviewLodResidencyPlan
            {
                Level = 0, SampleStride = 1,
                SamplesPerSide = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1),
                SampleSpacing = TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1),
                RequiredWindow = hasLatestRequiredResidencyWindow ? latestRequiredResidencyWindow : target,
                DesiredWindow = target
            }}
        };
        // A native target was already selected by the established native residency policy.
        var request = CreateCacheSetRequest(settings, plan, new[] { target }, new[] { false },
            TerrainAuthoringPreviewCachePublication.NativePreview, committed, overall,
            committedRebuildRequested);
        return QueueCacheSetRequest(request, out error);
    }

    private static TerrainAuthoringPreviewCacheSetTransition CreateCacheSetRequest(
        WorldSettings settings, TerrainAuthoringPreviewResidencyPlan plan,
        TerrainHeightCacheWindow[] targets, bool[] expansions,
        TerrainAuthoringPreviewCachePublication publication, string committed, string overall, bool rebuild)
    {
        var sources = new TerrainAuthoringPreviewCache[plan.LevelCount];
        var generations = new long[plan.LevelCount];
        for (int i = 0; i < sources.Length; i++)
        {
            if (publication == TerrainAuthoringPreviewCachePublication.NativePreview)
            {
                sources[i] = activeCache;
                generations[i] = activeCacheAuthoringGeneration;
            }
            else if (publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
            {
                if (analysisOwnedOwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
                {
                    sources[i] = analysisOwnedState?.ActiveCache;
                    generations[i] = analysisOwnedState?.ActiveAuthoringGeneration ?? 0L;
                }
            }
            else if (preparedHeightStates != null && i < preparedHeightStates.Length
                && preparedHeightStates[i].Level == i)
            {
                sources[i] = preparedHeightStates[i].ActiveCache;
                generations[i] = preparedHeightStates[i].ActiveAuthoringGeneration;
            }
        }
        return new TerrainAuthoringPreviewCacheSetTransition(plan, targets, expansions, sources,
            generations, publication, committed, overall, authoringGeneration,
            ++streamingRequestGeneration, TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration,
            rebuild, settings.HeightTileWorldSize,
            TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings));
    }

    private static bool QueueCacheSetRequest(TerrainAuthoringPreviewCacheSetTransition request, out string error)
    {
        error = "";
        bool nativeRecovery = request.Publication == TerrainAuthoringPreviewCachePublication.NativePreview
            && (request.RebuildRequested || activeCache == null || !activeCache.IsCompleteForActivation
                || activeCacheAuthoringGeneration != request.AuthoringGeneration
                || activeCache.SourceCommittedHeightfieldSignature != request.CommittedSignature
                || activeCache.SourceOverallAuthoringSignature != request.OverallSignature
                || !ActiveCacheContains(request.AcceptedPlan.Levels[0].RequiredWindow));
        var occupied = TransitionInProgress ? currentCacheSetTransition : pendingCacheSetTransition;
        if (TerrainAuthoringPreviewStreamingPolicy.ShouldDeferForNativeAnalysis(
            request.Publication, nativeRecovery, TerrainAnalysisHeightIsRequired, occupied?.Publication))
        {
            request.Dispose();
            if (request.Publication == TerrainAuthoringPreviewCachePublication.NativePreview)
                return true; // Required display coverage is current; optional guard work can wait.
            error = "Current native analysis Height must finish before new optional Height preparation.";
            return false;
        }
        foreach (var existing in new[] { currentCacheSetTransition, pendingCacheSetTransition })
        {
            if (existing == null || !existing.InProgress || existing.Publication != request.Publication
                || !existing.MatchesContent(request.CommittedSignature, request.OverallSignature,
                    request.AuthoringGeneration, request.OwnershipGeneration, request.RebuildRequested)) continue;
            bool same = TerrainAuthoringPreviewStreamingPolicy.AreCacheSetTargetsEquivalent(existing, request);
            bool useful = TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(existing, request.AcceptedPlan);
            if (same || useful)
            {
                existing.AcceptIntent(request.AcceptedPlan, request.RequestGeneration);
                request.Dispose();
                return true;
            }
        }
        if (request.Publication == TerrainAuthoringPreviewCachePublication.PreparedHeightSet
            && ((TransitionInProgress && currentCacheSetTransition.Publication ==
                TerrainAuthoringPreviewCachePublication.NativePreview)
                || (hasPendingStreamingStart && pendingCacheSetTransition.Publication ==
                TerrainAuthoringPreviewCachePublication.NativePreview)))
        {
            request.Dispose();
            error = "Native preview coverage work must finish before preparing a Height cache set.";
            return false;
        }
        if (IsCacheSetFailureSuppressed(request))
        {
            request.Dispose();
            error = request.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis
                ? lastFailedAnalysisCacheSetRequest.Error : lastStreamingFailureMessage;
            return false;
        }
        CancelCurrentStreamingTransition("A newer residency request superseded staging.", false);
        pendingCacheSetTransition = request;
        streamingProgress = 0;
        lastStreamingFailureMessage = "";
        if (request.Publication == TerrainAuthoringPreviewCachePublication.NativePreview)
            SetStatus(activeCache != null && activeCache.IsReady ? TerrainAuthoringPreviewStatus.Ready
                : TerrainAuthoringPreviewStatus.Preparing, "The resident Height cache is streaming incrementally.");
        SetStreamingState(TerrainAuthoringPreviewStreamingState.Preparing, "Queued Height cache preparation.");
        return true;
    }

    private static void OnStreamingEditorUpdate()
    {
        if (!CanRunEditorPreviewWork) return;
        using var scope = WorldMeshesProfiler.PreviewStreamingUpdate.Auto();
        var watch = Stopwatch.StartNew();
        var settings = LoadWorldSettings();
        var data = LoadAuthoringData();
        AdmitPendingTerrainAnalysisSource(settings, data);
        if (!TransitionInProgress && hasPendingStreamingStart)
        {
            if (TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(
                pendingCacheSetTransition.Publication, HasActiveInteractiveTerrainAuthoringEdit,
                TerrainAuthoringVisualizationController.RequiresLiveTerrainAnalysisDuringInteractiveEdit)) return;
            ReleaseStagingCacheOnly();
            currentCacheSetTransition = pendingCacheSetTransition;
            pendingCacheSetTransition = null;
            BeginTransitionMemoryTracking();
        }
        if (!TransitionInProgress) return;
        var t = currentCacheSetTransition;
        if (!ValidateCacheSetTransaction(t, settings, data, out string staleReason))
        {
            CancelCurrentStreamingTransition(staleReason, false);
            ScheduleRefresh();
            return;
        }
        bool readyOnEntry = t.State == TerrainAuthoringPreviewTransitionState.ReadyToActivate;
        bool advanced = AdvanceHeightCacheSet(t, settings, data, heightCompositor, watch,
            DefaultSoftWorkBudgetMilliseconds, DefaultMaterializationsPerUpdate,
            TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile, out string error);
        if (!ReferenceEquals(t, currentCacheSetTransition)) return;
        if (t.AuthoringGeneration != authoringGeneration
            || t.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
        {
            CancelCurrentStreamingTransition("Height request generations changed during worker execution.", false);
            ScheduleRefresh();
            return;
        }
        if (!advanced)
        {
            FailCacheSetTransaction(t, error);
            return;
        }
        CaptureTransitionMemoryEstimate();
        streamingProgress = Mathf.Max(streamingProgress, (float)t.CompletedWorkUnits / t.TotalWorkUnits);
        if (t.State == TerrainAuthoringPreviewTransitionState.ReadyToActivate
            && (readyOnEntry || watch.Elapsed.TotalMilliseconds < DefaultSoftWorkBudgetMilliseconds))
        {
            if (!ValidateCacheSetTransaction(t, settings, data, out staleReason))
            {
                CancelCurrentStreamingTransition(staleReason, false);
                ScheduleRefresh();
                return;
            }
            if (t.Publication == TerrainAuthoringPreviewCachePublication.NativePreview)
            {
                if (!TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out error)
                    || !TryActivateStagingCache(root, currentTransition, t.CommittedSignature,
                        t.OverallSignature, out error))
                {
                    if (t.State != TerrainAuthoringPreviewTransitionState.Failed)
                        FailCacheSetTransaction(t, error);
                    return;
                }
                MarkActiveCacheAuthoringGeneration(t.AuthoringGeneration);
                AcknowledgePendingRegionalElevationAfterActivation(t.AuthoringGeneration);
                t.State = TerrainAuthoringPreviewTransitionState.Activated;
                t.CompletedWorkUnits++;
                ScheduleRefresh();
            }
            else if (t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
                PublishTerrainAnalysisHeight(t);
            else PublishPreparedHeightCacheSet(t);
            t.Dispose();
            ScheduleRefresh();
            streamingProgress = 1;
            SetStreamingState(TerrainAuthoringPreviewStreamingState.Idle, "The complete Height cache result was published.");
        }
        else SetStreamingState(ToStreamingState(t.State), "Preparing the requested Height cache result.");
        PublishStreamingStateIfChanged();
    }

    internal delegate bool NativeHeightSourceLoader(WorldSettings settings, Vector2Int tile,
        out Texture2D source, out string error);

    // Shared by the sole production callback and isolated explicit validator fixtures.
    internal static bool AdvanceHeightCacheSet(TerrainAuthoringPreviewCacheSetTransition t,
        WorldSettings settings, TerrainAuthoringData data, TerrainHeightCompositor compositor,
        Stopwatch watch, double softBudgetMilliseconds, int materializationLimit,
        NativeHeightSourceLoader loadSource, out string error)
    {
        error = "";
        t.LastUpdateAllocations = t.LastUpdateLoads = t.LastUpdateMaterializations =
            t.LastUpdateCopies = t.LastUpdateCompositions = 0;
        if (!t.InProgress || t.State == TerrainAuthoringPreviewTransitionState.ReadyToActivate) return true;
        try
        {
            // Permit one indivisible work unit even if validity checks exhausted the soft target.
            // Subsequent units share the same clock, so expensive metadata cannot starve progress.
            bool performedOperation = false;
            while (!performedOperation || watch.Elapsed.TotalMilliseconds < softBudgetMilliseconds)
            {
                if (t.PreparationCursor < t.Entries.Length)
                {
                    if (t.LastUpdateAllocations == 1) break;
                    var e = t.Entries[t.PreparationCursor];
                    performedOperation = true;
                    e.Destination.StagingCache = new TerrainAuthoringPreviewCache();
                    if (!e.Destination.StagingCache.TryInitializeStagingWindow(settings, data,
                        e.Target, e.Plan.SampleStride, out error))
                        return WorkerFailure(t, e, default, "allocation", error, out error);
                    e.Prepared = true;
                    e.Destination.TransitionState = TerrainAuthoringPreviewLodTransitionState.PopulatingStaging;
                    e.Transition.DestinationTextureInstanceId = e.Destination.StagingCache.HeightCache.GetInstanceID();
                    t.PreparationCursor++;
                    t.LastUpdateAllocations++;
                    t.CompletedWorkUnits++;
                    continue;
                }
                // Fine compositions are selected as soon as their source dispatch was submitted.
                TerrainAuthoringPreviewCacheSetTransition.Entry composition = null;
                foreach (var e in t.Entries)
                    if (e.Composition.Count > 0) { composition = e; break; }
                if (composition != null && t.LastUpdateCompositions < DefaultCompositionsPerUpdate)
                {
                    var e = composition;
                    performedOperation = true;
                    var tile = e.Composition.Peek();
                    var cache = e.Destination.StagingCache;
                    if (!cache.TryGetCommittedRange(tile, out float low, out float high, out error)
                        || !compositor.TryComposeTile(cache.HeightCache, tile,
                            cache.GetSliceIndex(tile.x, tile.y), cache.SamplesPerSide,
                            cache.SampleSpacing, settings.HeightTileWorldSize, cache.WorldSizeXZ,
                            data, low, high, out float finalLow, out float finalHigh, out error)
                        || !cache.TryCommitFinalCompositeTile(tile, finalLow, finalHigh, out error))
                        return WorkerFailure(t, e, tile, "composition", error, out error);
                    e.Composition.Dequeue();
                    e.Transition.SetState(TerrainAuthoringPreviewTransitionState.ComposingSourceTiles);
                    e.Transition.FullyComposedTileCount++;
                    e.Transition.CompositionCursor++;
                    t.ComposedSlices++;
                    t.LastUpdateCompositions++;
                    t.CompletedWorkUnits++;
                    t.State = TerrainAuthoringPreviewTransitionState.ComposingSourceTiles;
                    continue;
                }
                TerrainAuthoringPreviewCacheSetTransition.Entry retained = null;
                foreach (var e in t.Entries)
                    if (e.Transition.RetainedCopyCursor < e.Transition.ReusableRetainedTiles.Count)
                    { retained = e; break; }
                if (retained != null && t.LastUpdateCopies < DefaultRetainedCopiesPerUpdate)
                {
                    var e = retained;
                    performedOperation = true;
                    var tile = e.Transition.ReusableRetainedTiles[e.Transition.RetainedCopyCursor];
                    if (!IsRetainedReuseGloballyEligible(e.Source, e.Destination.StagingCache,
                        t.CommittedSignature, t.OverallSignature, t.RebuildRequested)
                        || e.SourceAuthoringGeneration != t.AuthoringGeneration)
                        return WorkerFailure(t, e, tile, "retained eligibility", "The borrowed source became stale.", out error);
                    if (!e.Destination.StagingCache.TryCopyFinalCompositeTileFrom(e.Source, tile, out error))
                        return WorkerFailure(t, e, tile, "retained copy", error, out error);
                    e.Transition.SetState(TerrainAuthoringPreviewTransitionState.CopyingRetained);
                    e.Transition.RetainedCopyCursor++;
                    e.Transition.RetainedGpuCopyCount++;
                    t.RetainedCopies++;
                    t.LastUpdateCopies++;
                    t.CompletedWorkUnits++;
                    t.State = TerrainAuthoringPreviewTransitionState.CopyingRetained;
                    continue;
                }
                if (t.GroupCursor < t.SourceGroups.Count)
                {
                    var group = t.SourceGroups[t.GroupCursor];
                    if (t.CurrentSource == null && t.LastUpdateLoads < DefaultCommittedLoadsPerUpdate)
                    {
                        performedOperation = true;
                        if (!loadSource(settings, group.Tile, out t.CurrentSource, out error))
                            return WorkerFailure(t, group.Destinations[0], group.Tile, "native source load", error, out error);
                        t.SourceLoads++;
                        t.LastUpdateLoads++;
                        group.Destinations[0].Transition.CommittedSourceLoadCount++;
                        t.CompletedWorkUnits++;
                        t.State = TerrainAuthoringPreviewTransitionState.LoadingSourceTiles;
                        continue;
                    }
                    if (t.CurrentSource != null && t.LastUpdateMaterializations < Math.Max(1, materializationLimit))
                    {
                        var e = group.Destinations[group.Cursor];
                        performedOperation = true;
                        if (!e.Destination.StagingCache.TryMaterializeCommittedBaseTile(
                            t.CurrentSource, t.Materializer, group.Tile, out error))
                            return WorkerFailure(t, e, group.Tile, "materialization", error, out error);
                        t.Materializer.ReleaseTextureBindings();
                        e.Transition.SetState(TerrainAuthoringPreviewTransitionState.LoadingSourceTiles);
                        e.Composition.Enqueue(group.Tile);
                        e.Transition.CommittedLoadCursor++;
                        group.Cursor++;
                        t.MaterializedSlices++;
                        t.LastUpdateMaterializations++;
                        t.CompletedWorkUnits++;
                        if (group.Cursor == group.Destinations.Count)
                        {
                            t.ReleaseCurrentSource();
                            t.GroupCursor++;
                        }
                        continue;
                    }
                }
                bool finalized = false;
                foreach (var e in t.Entries)
                {
                    if (e.Finalized || e.Composition.Count != 0
                        || e.Transition.RetainedCopyCursor != e.Transition.ReusableRetainedTiles.Count
                        || e.Transition.FullyComposedTileCount != e.Transition.SourceMaterializationTiles.Count) continue;
                    performedOperation = true;
                    if (!e.Destination.StagingCache.TryFinalizeStagingForActivation(t.OverallSignature, out error))
                        return WorkerFailure(t, e, default, "finalization", error, out error);
                    e.Finalized = true;
                    e.Transition.SetState(TerrainAuthoringPreviewTransitionState.ReadyToActivate);
                    e.Destination.TransitionState = TerrainAuthoringPreviewLodTransitionState.CommitPending;
                    t.CompletedWorkUnits++;
                    t.State = TerrainAuthoringPreviewTransitionState.Finalizing;
                    finalized = true;
                    break;
                }
                if (finalized) continue;
                if (t.Complete) t.State = TerrainAuthoringPreviewTransitionState.ReadyToActivate;
                break;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = "Height cache work failed: " + exception.Message;
            t.Error = error;
            t.State = TerrainAuthoringPreviewTransitionState.Failed;
            t.ReleaseCurrentSource();
            return false;
        }
    }

    private static bool WorkerFailure(TerrainAuthoringPreviewCacheSetTransition t,
        TerrainAuthoringPreviewCacheSetTransition.Entry entry, Vector2Int tile,
        string operation, string detail, out string error)
    {
        error = $"Height {operation}, LOD {entry.Plan.Level}, tile {tile}: {detail}";
        t.Error = error;
        t.State = TerrainAuthoringPreviewTransitionState.Failed;
        entry.Transition.MarkFailed(error);
        t.ReleaseCurrentSource();
        return false;
    }

    private static bool ValidateCacheSetTransaction(TerrainAuthoringPreviewCacheSetTransition t,
        WorldSettings settings, TerrainAuthoringData data, out string reason)
    {
        reason = "The accepted Height cache request became stale.";
        if (settings == null || data == null
            || !t.MatchesContent(TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings),
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data),
                authoringGeneration, TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration,
                t.RebuildRequested) || !t.BorrowedSourcesAreValid()) return false;
        var grid = new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        foreach (var e in t.Entries)
            if (!TerrainAuthoringPreviewResidencyPolicy.IsWindowInsideWorldGrid(e.Target, grid)
                || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, e.Plan.SampleStride)
                || TerrainHeightResolutionUtility.GetSamplesPerSide(settings, e.Plan.SampleStride) != e.Plan.SamplesPerSide
                || !Mathf.Approximately(TerrainHeightResolutionUtility.GetSampleSpacing(settings,
                    e.Plan.SampleStride), e.Plan.SampleSpacing)) return false;
        if (t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
        {
            if (!hasAnalysisSourceDemand || !hasAnalysisSourceIntent || settings != analysisSettings
                || analysisOwnershipGeneration != t.OwnershipGeneration
                || !IsTerrainAnalysisTransactionCurrent(t, CreateTerrainAnalysisPlan(settings),
                    analysisAcceptedRequestGeneration)) return false;
        }
        else if (t.Publication == TerrainAuthoringPreviewCachePublication.PreparedHeightSet)
        {
            if (!TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(
                t.AcceptedPlan, acceptedPreparedHeightIntent)
                || t.RequestGeneration != acceptedPreparedHeightRequestGeneration
                || !TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(t, acceptedPreparedHeightIntent)) return false;
        }
        else if (hasLatestRequiredResidencyWindow && hasDesiredResidencyWindow)
        {
            if (!TerrainAuthoringPreviewStreamingPolicy.IsStagingTargetUseful(t.Entries[0].Target,
                latestRequiredResidencyWindow, desiredResidencyWindow)) return false;
            // Adopt current native intent without altering frozen work targets.
            var accepted = t.AcceptedPlan.CreateSnapshot();
            accepted.Levels[0].RequiredWindow = latestRequiredResidencyWindow;
            accepted.Levels[0].DesiredWindow = desiredResidencyWindow;
            t.AcceptIntent(accepted, streamingRequestGeneration);
            if (!t.RebuildRequested && TryGetActiveResidentWindow(out var active)
                && TerrainAuthoringPreviewStreamingPolicy.IsActiveComfortablySufficient(active,
                    latestRequiredResidencyWindow, desiredResidencyWindow, grid)) return false;
        }
        reason = "";
        return true;
    }

    private static TerrainAuthoringPreviewStreamingState ToStreamingState(TerrainAuthoringPreviewTransitionState state)
    {
        switch (state)
        {
            case TerrainAuthoringPreviewTransitionState.CopyingRetained: return TerrainAuthoringPreviewStreamingState.CopyingRetained;
            case TerrainAuthoringPreviewTransitionState.LoadingSourceTiles: return TerrainAuthoringPreviewStreamingState.Loading;
            case TerrainAuthoringPreviewTransitionState.ComposingSourceTiles: return TerrainAuthoringPreviewStreamingState.Composing;
            case TerrainAuthoringPreviewTransitionState.Finalizing: return TerrainAuthoringPreviewStreamingState.Finalizing;
            case TerrainAuthoringPreviewTransitionState.ReadyToActivate: return TerrainAuthoringPreviewStreamingState.Activating;
            default: return TerrainAuthoringPreviewStreamingState.Preparing;
        }
    }

    private static void CancelCurrentStreamingTransition(string reason, bool clearIntent)
    {
        if (currentCacheSetTransition != null && currentCacheSetTransition.InProgress)
        {
            currentCacheSetTransition.State = TerrainAuthoringPreviewTransitionState.Cancelled;
            foreach (var e in currentCacheSetTransition.Entries) e.Transition.MarkCancelled(reason);
        }
        lastStreamingCancellationReason = reason;
        ReleaseStagingCacheOnly();
        currentCacheSetTransition = null;
        ClearPendingStreamingStart();
        if (clearIntent)
        {
            hasLatestRequiredResidencyWindow = false;
            latestRequiredResidencyWindow = default;
        }
        streamingProgress = 0;
        SetStreamingState(TerrainAuthoringPreviewStreamingState.Idle, reason);
    }

    private static void ClearPendingStreamingStart()
    {
        pendingCacheSetTransition?.Dispose();
        pendingCacheSetTransition = null;
    }

    private static void ResetStreamingStateForResourceRelease(bool notifyObservers = true)
    {
        ClearPendingStreamingStart();
        hasLatestRequiredResidencyWindow = false;
        latestRequiredResidencyWindow = default;
        ClearMultiresolutionResidencyIntent();
        streamingState = TerrainAuthoringPreviewStreamingState.Idle;
        streamingStatusMessage = "Streaming is idle.";
        streamingProgress = 0;
        lastStreamingFailureMessage = lastStreamingCancellationReason = lastPublishedStreamingSnapshot = "";
        if (notifyObservers) PublishStreamingStateIfChanged();
    }

    private static void SetStreamingState(TerrainAuthoringPreviewStreamingState state, string message)
    {
        streamingState = state;
        streamingStatusMessage = message ?? "";
        PublishStreamingStateIfChanged();
    }

    private static void PublishStreamingStateIfChanged()
    {
        string snapshot = $"{streamingState}|{streamingStatusMessage}|{streamingRequestGeneration}|" +
            $"{StreamingRetainedCopiedCount}|{StreamingSourceLoadedCount}|{StreamingSourceMaterializedCount}|" +
            $"{StreamingSourceComposedCount}|{streamingProgress}|{IsWaitingForStreamingCoverage}";
        if (snapshot == lastPublishedStreamingSnapshot) return;
        lastPublishedStreamingSnapshot = snapshot;
        StreamingStateChanged?.Invoke();
        RepaintEditorViews();
    }
}
