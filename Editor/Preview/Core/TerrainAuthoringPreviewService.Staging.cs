using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    public static event Action<TerrainHeightCacheWindow, string> HeightCacheTransitionFailed;

    private static TerrainAuthoringPreviewCacheSetTransition lastFailedCacheSetRequest;
    private static TerrainAuthoringPreviewLodState retiringAnalysisState;
    private static bool hasTransitionFailure;

    private static string lastTransitionFailureMessage = "";


    public static bool TransitionInProgress => currentCacheSetTransition != null && currentCacheSetTransition.InProgress;
    public static bool HasTransitionDiagnostics => currentCacheSetTransition != null || hasTransitionFailure;
    public static string TransitionStateLabel => currentCacheSetTransition != null
        ? currentCacheSetTransition.State.ToString() : hasTransitionFailure ? "Failed" : "Idle";
    public static bool HasTransitionFailure => hasTransitionFailure;
    public static string LastTransitionFailureMessage => lastTransitionFailureMessage;

    public static int LastTransitionRetainedTileCount => SumSetTiles(2);
    public static int LastTransitionEnteringTileCount => SumSetTiles(3);
    public static int LastTransitionLeavingTileCount => SumSetTiles(4);
    public static int LastTransitionReusableRetainedTileCount => StreamingRetainedTileCount;
    public static int LastTransitionRetainedGpuCopyCount => StreamingRetainedCopiedCount;
    public static int LastTransitionCommittedSourceLoadCount => StreamingSourceLoadedCount;
    public static int LastTransitionComposedTileCount => StreamingSourceComposedCount;
    public static long ApproximateStagingGpuMemoryBytes => EstimateOwnedHeightMemory(false);
    public static long ApproximateTotalResidentGpuMemoryBytes => EstimateOwnedHeightMemory(true);
    internal static long ApproximateDisplayGpuMemoryBytes => CaptureHeightOwnership().DisplayActiveBytes;
    internal static long ApproximateAnalysisGpuMemoryBytes => CaptureHeightOwnership().AnalysisActiveBytes;



    private static long EstimateOwnedHeightMemory(bool includeActive)
    {
        var ownership = CaptureHeightOwnership();
        return includeActive ? ownership.TotalBytes : ownership.StagingBytes;
    }







    internal static bool IsRetainedReuseGloballyEligible(TerrainAuthoringPreviewCache source,
        TerrainAuthoringPreviewCache destination, string committed, string overall, bool rebuild)
    {
        return source != null && destination != null && source.IsCompleteForActivation && !rebuild
            && source.SourceCommittedHeightfieldSignature == committed && source.SourceOverallAuthoringSignature == overall
            && source.SampleStride == destination.SampleStride && source.SamplesPerSide == destination.SamplesPerSide
            && Mathf.Approximately(source.SampleSpacing, destination.SampleSpacing) && source.WorldSizeXZ == destination.WorldSizeXZ;
    }

    // Placement, MPBs, bounds and resource ownership publish synchronously.
    // No callbacks observe a partially bound set or an uncounted retiring owner.
    private static bool TryCommitDisplayHeight(TerrainAuthoringPreviewCacheSetTransition transaction,
        TerrainAuthoringPreviewDisplayIntent intent, TerrainAuthoringPreviewLodState[] candidate, out string error)
    {
        error = "";
        if (displayCommitInProgress || retiringHeightStates != null || intent == null
            || !ReferenceEquals(intent, latestDisplayIntent) || !intent.ConfigurationMatches(LoadWorldSettings()) || !CanRunEditorPreviewWork
            || intent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
        { error = "The paired display intent became stale before publication."; return false; }
        if (transaction != null)
        {
            if (!ValidateCacheSetTransaction(transaction, intent.Settings, LoadAuthoringData(), out error)
                || !transaction.Complete || transaction.State != TerrainAuthoringPreviewTransitionState.ReadyToActivate) return false;
            candidate = new TerrainAuthoringPreviewLodState[transaction.Entries.Length];
            for (int i = 0; i < candidate.Length; i++) candidate[i] = transaction.Entries[i].Destination;
        }
        else if (!CanActiveHeightCacheSetCover(intent.Plan))
        { error = "The retained Height set does not provide usable coverage for this layout."; return false; }
        var renderers = new List<TerrainClipmapRendererBinding>();
        if (!TerrainAuthoringSceneViewController.TryPreflightDisplayPlacement(intent, renderers, out error)) return false;
        var view = CreateHeightView(candidate);
        if (!TerrainAuthoringPreviewHeightBindingUtility.TryPreflight(intent.Settings, intent.Plan, renderers, view, out error)) return false;
        var boundsController = intent.Root.GetComponent<TerrainClipmapBoundsController>();
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        foreach (var s in candidate)
        {
            var c = s.ActiveCache ?? s.StagingCache;
            low = Mathf.Min(low, c.MinimumHeight); high = Mathf.Max(high, c.MaximumHeight);
        }
        if (boundsController == null || float.IsNaN(low) || float.IsNaN(high)
            || float.IsInfinity(low) || float.IsInfinity(high) || low > high)
        { error = "The clipmap transient displacement bounds cannot be applied."; return false; }
        var previous = activeHeightStates;
        var allRenderers = new HashSet<MeshRenderer>();
        foreach (var b in boundHeightRenderers) if (b.Renderer != null) allRenderers.Add(b.Renderer);
        foreach (var b in renderers) allRenderers.Add(b.Renderer);
        var blocks = new Dictionary<MeshRenderer, MaterialPropertyBlock>();
        var bounds = new Dictionary<MeshRenderer, Bounds>();
        foreach (var r in allRenderers)
        {
            var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block); blocks.Add(r, block); bounds.Add(r, r.localBounds);
        }
        TerrainAuthoringSceneViewController.DisplayPlacementSnapshot placement = null;
        TerrainAuthoringPreviewLodState[] transferred = null;
        displayCommitInProgress = true;
        try
        {
            placement = TerrainAuthoringSceneViewController.BeginDisplayPlacement();
            if (!TerrainAuthoringSceneViewController.TryApplyDisplayPlacement(intent, out error)) throw new InvalidOperationException(error);
            TerrainAuthoringPreviewHeightBindingUtility.Bind(renderers, view);
            if (!boundsController.ApplyBoundsForRange(low, high)) throw new InvalidOperationException("Transient Height bounds failed.");
            // Recheck after every renderer write, before any ownership transfer.
            if (!ReferenceEquals(intent, latestDisplayIntent)
                || !intent.ConfigurationMatches(LoadWorldSettings())
                || intent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
                || (transaction != null && !ValidateCacheSetTransaction(transaction, intent.Settings, LoadAuthoringData(), out error)))
                throw new InvalidOperationException("Height ownership or authoring changed during publication. " + error);
            // Complete fallible renderer and placement work before transferring resources.
            var removed = new List<TerrainClipmapRendererBinding>();
            foreach (var b in boundHeightRenderers)
                if (!renderers.Exists(n => n.Renderer == b.Renderer)) removed.Add(b);
            TerrainAuthoringPreviewHeightBindingUtility.Disable(removed);
            TerrainAuthoringSceneViewController.CommitDisplayPlacement(intent);
            transferred = transaction == null ? candidate : transaction.TransferPreparedStates();
            if (transaction != null) retiringHeightStates = previous;
            activeHeightStates = transferred; activeHeightView = view; activeDisplayIntent = intent;
            for (int i = 0; i < transferred.Length; i++) transferred[i].ActiveRequiredWindow = intent.Plan.Levels[i].RequiredWindow;
            boundHeightRenderers.Clear(); boundHeightRenderers.AddRange(renderers); boundClipmapRoot = intent.Root;
            aggregateMinimumHeight = low; aggregateMaximumHeight = high;
            committedRebuildRequested = false; clipmapRebindRequested = false;
            ClearBoundsFollowUp();
            if (transaction != null) ClearPreviewFollowUps();
            diagnosticBindingApplyCount++;
        }
        catch (Exception exception)
        {
            error = "The complete Height display could not be published: " + exception.Message;
            bool restored = TerrainAuthoringSceneViewController.RestoreDisplayPlacement(placement);
            foreach (var pair in blocks)
            {
                if (pair.Key == null) { restored = false; continue; }
                pair.Key.SetPropertyBlock(pair.Value); pair.Key.localBounds = bounds[pair.Key];
            }
            if (!restored)
            {
                TerrainAuthoringPreviewHeightBindingUtility.Disable(renderers);
                TerrainAuthoringPreviewHeightBindingUtility.Disable(boundHeightRenderers);
                if (previous != null) foreach (var s in previous) s.WriteFailed = true;
                error += " The previous hierarchy could not be restored; invalid Height bindings were disabled.";
            }
            return false;
        }
        finally
        {
            TerrainAuthoringSceneViewController.EndDisplayPlacement(); displayCommitInProgress = false;
        }
        CaptureTransitionMemoryEstimate();
        try
        {
            if (transaction != null)
            {
                ReleaseActiveDirtySource(); ClearInteractiveDirtyHint();
                displayPreparationDeferredForInteractiveEdit = false; dirtyCompositeTiles.Clear();
                pendingCompositePublication.Clear(); pendingNativePublication.Clear(); diagnosticPendingGeographicDirty.Clear();
                AcknowledgePendingRegionalElevationAfterActivation(transaction.AuthoringGeneration);
                if (retiringHeightStates != null) foreach (var s in retiringHeightStates) DetachTerrainAnalysisBorrowing(s.ActiveCache);
                ClearDisplayTransitionFailureSuppression(); fullCommittedBuildCount++;
            }
            SetStatus(TerrainAuthoringPreviewStatus.Ready, "The complete multiresolution Height preview is active.");
            NotifyHeightCacheCoverageIfChanged(); NotifyPreviewStateChanged(); RepaintEditorViews();
        }
        finally
        {
            heightCompositor.ReleaseTextureBindings(); activeDirtyMaterializer.ReleaseTextureBindings();
            if (retiringHeightStates != null) foreach (var s in retiringHeightStates) s.Dispose();
            retiringHeightStates = null;
            if (transaction != null) CompleteTransitionMemoryTracking();
        }
        return true;
    }

    private static void ClearDisplayTransitionFailureSuppression()
    {
        lastFailedCacheSetRequest = null; hasTransitionFailure = false;
        lastTransitionFailureMessage = "";
    }

    private static void ClearTransitionFailureSuppression()
    {
        ClearDisplayTransitionFailureSuppression(); lastFailedAnalysisCacheSetRequest = null; analysisSourceError = "";
    }

    private static void ReleaseStagingCacheOnly()
    {
        if (currentCacheSetTransition == null) return;
        heightCompositor.ReleaseTextureBindings();
        CompleteTransitionMemoryTracking(); currentCacheSetTransition.Dispose();
    }

    private static bool IsCacheSetFailureSuppressed(TerrainAuthoringPreviewCacheSetTransition request)
    {
        var failed = request.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis ? lastFailedAnalysisCacheSetRequest : lastFailedCacheSetRequest;
        return failed != null && failed.Publication == request.Publication
            && failed.MatchesContent(request.CommittedSignature, request.OverallSignature, request.AuthoringGeneration,
                request.OwnershipGeneration, request.RebuildRequested)
            && TerrainAuthoringPreviewStreamingPolicy.AreCacheSetTargetsEquivalent(failed, request)
            && TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(failed.AcceptedPlan, request.AcceptedPlan)
            && (request.Publication != TerrainAuthoringPreviewCachePublication.DisplayHeightSet
                || (failed.DisplayIntent?.PlacementGeneration == request.DisplayIntent?.PlacementGeneration
                    && failed.DisplayIntent?.Root == request.DisplayIntent?.Root));
    }

    private static void FailCacheSetTransaction(TerrainAuthoringPreviewCacheSetTransition t, string error)
    {
        t.State = TerrainAuthoringPreviewTransitionState.Failed; t.Error = error; CaptureTransitionMemoryEstimate();
        if (t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
        {
            lastFailedAnalysisCacheSetRequest = t; analysisSourceError = "Native analysis source: " + error;
        }
        else
        {
            lastFailedCacheSetRequest = t; hasTransitionFailure = true;
            lastTransitionFailureMessage = error;
        }
        ReleaseStagingCacheOnly(); ClearPendingStreamingStart(); lastStreamingFailureMessage = error;
        SetStreamingState(TerrainAuthoringPreviewStreamingState.Failed, error);
        if (t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis) PublishTerrainAnalysisSourceState(true);
        else
        {
            SetStatus(HasDrawableHeightPreview ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Error,
                "Height display streaming failed. " + error);
            DispatchPreviewObservers(HeightCacheTransitionFailed, t.Entries[0].Target, error, "Height transition");
        }
    }

    private static void ReleaseAllPreviewCaches(bool notifyObservers = true)
    {
        ReleaseBinding(); ReleaseActiveDirtySource(); ReleaseStagingCacheOnly(); ClearPendingStreamingStart();
        ReleaseTerrainAnalysisSource(notifyObservers); retiringAnalysisState?.Dispose(); retiringAnalysisState = null;
        ReleaseActiveHeightCacheSet(); currentCacheSetTransition = null;
        ClearMultiresolutionResidencyIntent(); ResetStreamingStateForResourceRelease(notifyObservers);
        ClearRegionalElevationResidencyForResourceRelease(); ClearTransitionFailureSuppression();
        if (notifyObservers) { NotifyHeightCacheCoverageIfChanged(); NotifyPreviewStateChanged(); }
    }
    private static long EstimatePublishedHeightMemory() => CaptureHeightOwnership().ActiveBytes;

    internal static TerrainAuthoringPreviewOwnershipSnapshot CaptureHeightOwnership() =>
        CaptureHeightOwnership(activeHeightStates, analysisOwnedState, currentCacheSetTransition,
            pendingCacheSetTransition, retiringHeightStates, retiringAnalysisState);

    internal static TerrainAuthoringPreviewOwnershipSnapshot CaptureHeightOwnership(TerrainAuthoringPreviewLodState[] display,
        TerrainAuthoringPreviewLodState analysis, TerrainAuthoringPreviewCacheSetTransition running,
        TerrainAuthoringPreviewCacheSetTransition queued, TerrainAuthoringPreviewLodState[] retiringDisplay,
        TerrainAuthoringPreviewLodState retiringAnalysis)
    {
        var seen = new HashSet<TerrainAuthoringPreviewCache>();
        long[] bytes = new long[6];
        int[] counts = new int[6];
        int arrays = 0;
        var scratchSeen = new HashSet<RenderTexture>();
        long scratchBytes = 0L;
        if (display != null) foreach (var state in display) CountOwnedDirtyScratch(state, scratchSeen, ref scratchBytes);
        if (retiringDisplay != null) foreach (var state in retiringDisplay) CountOwnedDirtyScratch(state, scratchSeen, ref scratchBytes);
        if (display != null) foreach (var state in display)
            CountOwnedHeight(state.ActiveCache, 0, seen, bytes, counts, ref arrays);
        CountOwnedHeight(analysis?.ActiveCache, 1, seen, bytes, counts, ref arrays);
        foreach (var transaction in new[] { running, queued })
            if (transaction != null) foreach (var entry in transaction.Entries)
                CountOwnedHeight(entry.Destination?.StagingCache,
                    transaction.Publication == TerrainAuthoringPreviewCachePublication.DisplayHeightSet ? 2 : 3,
                    seen, bytes, counts, ref arrays);
        if (retiringDisplay != null) foreach (var state in retiringDisplay)
            CountOwnedHeight(state.ActiveCache, 4, seen, bytes, counts, ref arrays);
        CountOwnedHeight(retiringAnalysis?.ActiveCache, 5, seen, bytes, counts, ref arrays);
        return new TerrainAuthoringPreviewOwnershipSnapshot(bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5],
            seen.Count, arrays, counts[0], counts[1], counts[2], counts[3], counts[4] + counts[5], scratchBytes, scratchSeen.Count);
    }

    private static void CountOwnedDirtyScratch(TerrainAuthoringPreviewLodState state,
        HashSet<RenderTexture> seen, ref long bytes)
    {
        var scratch = state?.DirtyScratch;
        if (scratch != null && scratch.IsCreated() && seen.Add(scratch)) bytes += state.DirtyScratchBytes;
    }

    private static void CountOwnedHeight(TerrainAuthoringPreviewCache cache, int category,
        HashSet<TerrainAuthoringPreviewCache> seen, long[] bytes, int[] counts, ref int arrays)
    {
        if (cache == null || !seen.Add(cache)) return;
        counts[category]++;
        if (cache.HeightCache != null && cache.HeightCache.IsCreated())
        {
            arrays++;
            bytes[category] += cache.ApproximateGpuMemoryBytes;
        }
    }

    // Borrowed for an immediate explicit validation operation; never dispose it.
    internal static bool TryGetActiveDisplayLodCacheForValidation(int level, out TerrainAuthoringPreviewCache cache)
    {
        cache = null;
        if (activeHeightStates == null || level < 0 || level >= activeHeightStates.Length) return false;
        var state = activeHeightStates[level];
        if (state.Level != level || state.WriteFailed || state.ActiveCache == null || !state.ActiveCache.IsReady) return false;
        cache = state.ActiveCache;
        return true;
    }

}

