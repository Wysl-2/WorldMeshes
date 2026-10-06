using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    public static event Action<TerrainHeightCacheWindow, string> HeightCacheTransitionFailed;
    private static TerrainAuthoringPreviewCacheTransition currentTransition => currentCacheSetTransition != null
        && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.DisplayHeightSet
            ? currentCacheSetTransition.Entries[0].Transition : null;
    private static TerrainAuthoringPreviewCacheSetTransition lastFailedCacheSetRequest;
    private static TerrainAuthoringPreviewLodState retiringAnalysisState;
    private static bool hasTransitionFailure;
    private static TerrainHeightCacheWindow lastFailedTransitionWindow;
    private static string lastTransitionFailureMessage = "";
    private static string lastFailedCommittedSignature = "";
    private static string lastFailedOverallSignature = "";
    public static bool TransitionInProgress => currentCacheSetTransition != null && currentCacheSetTransition.InProgress;
    public static bool HasTransitionDiagnostics => currentCacheSetTransition != null || hasTransitionFailure;
    public static string TransitionStateLabel => currentCacheSetTransition != null
        ? currentCacheSetTransition.State.ToString() : hasTransitionFailure ? "Failed" : "Idle";
    public static bool HasTransitionFailure => hasTransitionFailure;
    public static string LastTransitionFailureMessage => lastTransitionFailureMessage;
    public static TerrainHeightCacheWindow LastFailedTransitionWindow => lastFailedTransitionWindow;
    public static int LastTransitionRetainedTileCount => SumSetTiles(2);
    public static int LastTransitionEnteringTileCount => SumSetTiles(3);
    public static int LastTransitionLeavingTileCount => SumSetTiles(4);
    public static int LastTransitionReusableRetainedTileCount => StreamingRetainedTileCount;
    public static int LastTransitionRetainedGpuCopyCount => StreamingRetainedCopiedCount;
    public static int LastTransitionCommittedSourceLoadCount => StreamingSourceLoadedCount;
    public static int LastTransitionComposedTileCount => StreamingSourceComposedCount;
    public static long ApproximateStagingGpuMemoryBytes => EstimateOwnedHeightMemory(false);
    public static long ApproximateTotalResidentGpuMemoryBytes => EstimateOwnedHeightMemory(true);
    internal static long ApproximateDisplayGpuMemoryBytes => EstimateStatesMemory(activeHeightStates);
    internal static long ApproximateAnalysisGpuMemoryBytes => analysisOwnedState?.ActiveCache?.ApproximateGpuMemoryBytes ?? 0L;

    private static long EstimateStatesMemory(TerrainAuthoringPreviewLodState[] states)
    {
        long bytes = 0; var seen = new HashSet<TerrainAuthoringPreviewCache>();
        if (states != null) foreach (var s in states)
            if (s.ActiveCache != null && seen.Add(s.ActiveCache)) bytes += s.ActiveCache.ApproximateGpuMemoryBytes;
        return bytes;
    }

    private static long EstimateOwnedHeightMemory(bool includeActive)
    {
        var seen = new HashSet<TerrainAuthoringPreviewCache>(); long bytes = 0;
        if (includeActive)
        {
            if (activeHeightStates != null) foreach (var s in activeHeightStates)
                AddOwnedMemory(s.ActiveCache, seen, ref bytes);
            AddOwnedMemory(analysisOwnedState?.ActiveCache, seen, ref bytes);
            AddOwnedMemory(retiringAnalysisState?.ActiveCache, seen, ref bytes);
            if (retiringHeightStates != null) foreach (var s in retiringHeightStates)
                AddOwnedMemory(s.ActiveCache, seen, ref bytes);
        }
        if (currentCacheSetTransition != null) foreach (var e in currentCacheSetTransition.Entries)
            AddOwnedMemory(e.Destination?.StagingCache, seen, ref bytes);
        return bytes;
    }

    private static void AddOwnedMemory(TerrainAuthoringPreviewCache cache,
        HashSet<TerrainAuthoringPreviewCache> seen, ref long bytes)
    {
        if (cache != null && seen.Add(cache)) bytes += cache.ApproximateGpuMemoryBytes;
    }

    public static bool TryGetStagingResidentWindow(out TerrainHeightCacheWindow window)
    {
        var c = stagingCache; window = c == null ? default : new TerrainHeightCacheWindow(c.CacheOriginTile, c.CacheSize);
        return c != null && c.IsReady && window.IsValid;
    }

    internal static bool TryGetActiveCacheForValidation(out TerrainAuthoringPreviewCache cache)
    {
        cache = activeCache; return cache != null && cache.IsReady;
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
        { error = "The retained complete Height set is not current for this layout."; return false; }
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
            activeCacheAuthoringGeneration = authoringGeneration;
            committedRebuildRequested = false; clipmapRebindRequested = false;
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
                ReleaseActiveDirtySource(); dirtyCompositeTiles.Clear();
                pendingCompositePublication.Clear(); pendingNativePublication.Clear();
                AcknowledgePendingRegionalElevationAfterActivation(transaction.AuthoringGeneration);
                if (retiringHeightStates != null) foreach (var s in retiringHeightStates) DetachTerrainAnalysisBorrowing(s.ActiveCache);
                ClearDisplayTransitionFailureSuppression(); fullCommittedBuildCount++;
            }
            SetStatus(TerrainAuthoringPreviewStatus.Ready, "The complete multiresolution Height preview is active.");
            NotifyHeightCacheCoverageIfChanged(); NotifyPreviewStateChanged(); RepaintEditorViews();
        }
        finally
        {
            if (retiringHeightStates != null) foreach (var s in retiringHeightStates) s.Dispose();
            retiringHeightStates = null;
            if (transaction != null) CompleteTransitionMemoryTracking();
        }
        return true;
    }

    private static void ClearDisplayTransitionFailureSuppression()
    {
        lastFailedCacheSetRequest = null; hasTransitionFailure = false;
        lastFailedTransitionWindow = default; lastTransitionFailureMessage = "";
        lastFailedCommittedSignature = lastFailedOverallSignature = "";
    }

    private static void ClearTransitionFailureSuppression()
    {
        ClearDisplayTransitionFailureSuppression(); lastFailedAnalysisCacheSetRequest = null; analysisSourceError = "";
        activeDirtyFailureGeneration = -1L;
    }

    private static void ReleaseStagingCacheOnly()
    {
        if (currentCacheSetTransition == null) return;
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
            lastFailedTransitionWindow = t.Entries[0].Target;
            lastTransitionFailureMessage = error; lastFailedCommittedSignature = t.CommittedSignature; lastFailedOverallSignature = t.OverallSignature;
        }
        ReleaseStagingCacheOnly(); ClearPendingStreamingStart(); lastStreamingFailureMessage = error;
        SetStreamingState(TerrainAuthoringPreviewStreamingState.Failed, error);
        if (t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis) PublishTerrainAnalysisSourceState(true);
        else
        {
            SetStatus(HasDrawableHeightPreview ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Error,
                "Height display streaming failed. " + error);
            HeightCacheTransitionFailed?.Invoke(lastFailedTransitionWindow, error);
        }
    }

    private static void ReleaseAllPreviewCaches(bool notifyObservers = true)
    {
        ReleaseBinding(); ReleaseActiveDirtySource(); ReleaseStagingCacheOnly(); ClearPendingStreamingStart();
        ReleaseTerrainAnalysisSource(notifyObservers); retiringAnalysisState?.Dispose(); retiringAnalysisState = null;
        ReleaseActiveHeightCacheSet(); currentCacheSetTransition = null;
        ClearMultiresolutionResidencyIntent(); ResetStreamingStateForResourceRelease(notifyObservers);
        ClearRegionalElevationResidencyForResourceRelease(); ClearDesiredResidency(); ClearTransitionFailureSuppression();
        if (notifyObservers) { NotifyHeightCacheCoverageIfChanged(); NotifyPreviewStateChanged(); }
    }
    private static long EstimatePublishedHeightMemory()
    {
        var seen = new HashSet<TerrainAuthoringPreviewCache>(); long bytes = 0;
        if (activeHeightStates != null) foreach (var s in activeHeightStates) AddOwnedMemory(s.ActiveCache, seen, ref bytes);
        AddOwnedMemory(analysisOwnedState?.ActiveCache, seen, ref bytes);
        return bytes;
    }

}
