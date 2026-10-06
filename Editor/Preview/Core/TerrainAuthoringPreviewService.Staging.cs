using System;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    public static event Action<
        TerrainHeightCacheWindow,
        string
    > HeightCacheTransitionFailed;

    private static TerrainAuthoringPreviewCacheTransition currentTransition =>
        currentCacheSetTransition != null
        && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativePreview
            ? currentCacheSetTransition.Entries[0].Transition : null;

    private static TerrainAuthoringPreviewCacheSetTransition lastFailedCacheSetRequest;

    private static bool hasTransitionFailure;

    private static TerrainHeightCacheWindow
        lastFailedTransitionWindow;

    private static string lastTransitionFailureMessage =
        "";

    private static string lastFailedCommittedSignature =
        "";

    private static string lastFailedOverallSignature =
        "";

    public static bool TransitionInProgress => currentCacheSetTransition != null
        && currentCacheSetTransition.InProgress;

    public static bool HasTransitionDiagnostics
    {
        get
        {
            return
                currentCacheSetTransition != null
                ||
                hasTransitionFailure;
        }
    }

    public static string TransitionStateLabel
    {
        get
        {
            if (currentCacheSetTransition == null) return hasTransitionFailure ? "Failed" : "Idle";
            switch (currentCacheSetTransition.State)
            {
                case TerrainAuthoringPreviewTransitionState.CopyingRetained: return "Copying Retained";
                case TerrainAuthoringPreviewTransitionState.LoadingSourceTiles: return "Loading Source Tiles";
                case TerrainAuthoringPreviewTransitionState.ComposingSourceTiles: return "Composing Source Tiles";
                case TerrainAuthoringPreviewTransitionState.ReadyToActivate: return "Ready To Activate";
                default: return currentCacheSetTransition.State.ToString();
            }
        }
    }

    public static bool HasTransitionFailure =>
        hasTransitionFailure;

    public static string LastTransitionFailureMessage =>
        lastTransitionFailureMessage;

    public static TerrainHeightCacheWindow LastFailedTransitionWindow =>
        lastFailedTransitionWindow;

    public static int LastTransitionRetainedTileCount => SumSetTiles(2);

    public static int LastTransitionEnteringTileCount => SumSetTiles(3);

    public static int LastTransitionLeavingTileCount => SumSetTiles(4);

    public static int LastTransitionReusableRetainedTileCount => StreamingRetainedTileCount;

    public static int LastTransitionRetainedGpuCopyCount => StreamingRetainedCopiedCount;

    public static int LastTransitionCommittedSourceLoadCount => StreamingSourceLoadedCount;

    public static int LastTransitionComposedTileCount => StreamingSourceComposedCount;

    public static long ApproximateStagingGpuMemoryBytes => EstimateOwnedHeightMemory(false);

    public static long ApproximateTotalResidentGpuMemoryBytes => EstimateOwnedHeightMemory(true);

    private static long EstimateOwnedHeightMemory(bool includeActive)
    {
        var seen = new System.Collections.Generic.HashSet<TerrainAuthoringPreviewCache>();
        long bytes = 0;
        if (includeActive && activeCache != null && seen.Add(activeCache))
            bytes += activeCache.ApproximateGpuMemoryBytes;
        if (includeActive && preparedHeightStates != null)
            foreach (var state in preparedHeightStates)
                if (state.ActiveCache != null && seen.Add(state.ActiveCache))
                    bytes += state.ActiveCache.ApproximateGpuMemoryBytes;
        if (currentCacheSetTransition != null)
            foreach (var e in currentCacheSetTransition.Entries)
                if (e.Destination?.StagingCache != null && seen.Add(e.Destination.StagingCache))
                    bytes += e.Destination.StagingCache.ApproximateGpuMemoryBytes;
        return bytes;
    }

    public static bool TryGetStagingResidentWindow(
        out TerrainHeightCacheWindow window
    )
    {
        window =
            default;

        if (
            stagingCache == null
            ||
            !stagingCache.IsReady
        )
        {
            return false;
        }

        window =
            new TerrainHeightCacheWindow(
                stagingCache.CacheOriginTile,
                stagingCache.CacheSize
            );

        return
            window.IsValid;
    }

    internal static bool TryGetActiveCacheForValidation(
        out TerrainAuthoringPreviewCache cache
    )
    {
        cache =
            activeCache;

        return
            cache != null
            &&
            cache.IsReady;
    }

    internal static bool IsRetainedReuseGloballyEligible(
        TerrainAuthoringPreviewCache sourceCache,
        TerrainAuthoringPreviewCache destinationCache,
        string currentCommittedSignature,
        string currentOverallSignature,
        bool rebuildRequested
    )
    {
        return
            sourceCache != null
            &&
            destinationCache != null
            &&
            sourceCache.IsCompleteForActivation
            &&
            !rebuildRequested
            &&
            sourceCache
                .SourceCommittedHeightfieldSignature
            ==
            currentCommittedSignature
            &&
            sourceCache
                .SourceOverallAuthoringSignature
            ==
            currentOverallSignature
            &&
            sourceCache.SampleStride == destinationCache.SampleStride
            &&
            sourceCache.SamplesPerSide ==
                destinationCache.SamplesPerSide
            &&
            Mathf.Approximately(
                sourceCache.SampleSpacing,
                destinationCache.SampleSpacing
            )
            &&
            sourceCache.WorldSizeXZ ==
                destinationCache.WorldSizeXZ;
    }

    private static bool TryActivateStagingCache(
        Transform clipmapRoot,
        TerrainAuthoringPreviewCacheTransition transition,
        string currentCommittedSignature,
        string currentOverallSignature,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            stagingCache == null
            ||
            transition == null
            ||
            transition.State !=
                TerrainAuthoringPreviewTransitionState
                    .ReadyToActivate
        )
        {
            errorMessage =
                "The staging cache is not ready for activation.";

            return
                FailStagedTransition(
                    transition,
                    transition != null
                        ? transition.TargetWindow
                        : default,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        if (
            !stagingCache.TryValidateCompleteForActivation(
                out errorMessage
            )
            ||
            stagingCache.CacheOriginTile !=
                transition.TargetWindow.OriginTile
            ||
            stagingCache.CacheSize !=
                transition.TargetWindow.Size
            ||
            stagingCache
                .SourceCommittedHeightfieldSignature
            !=
            transition.TargetCommittedHeightfieldSignature
            ||
            stagingCache
                .SourceOverallAuthoringSignature
            !=
            transition.TargetOverallAuthoringSignature
        )
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage =
                    "The completed staging cache does not match its transition target metadata.";
            }

            return
                FailStagedTransition(
                    transition,
                    transition.TargetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        TerrainAuthoringPreviewCache previousActive =
            activeCache;

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .Activating
        );

        if (
            !TryBindSpecificPreviewCache(
                stagingCache,
                clipmapRoot,
                out errorMessage
            )
        )
        {
            if (
                previousActive != null
                &&
                previousActive.IsReady
            )
            {
                TryBindSpecificPreviewCache(
                    previousActive,
                    clipmapRoot,
                    out _
                );
            }
            else
            {
                TerrainHeightCacheBindingUtility
                    .Disable(
                        clipmapRoot
                    );
            }

            return
                FailStagedTransition(
                    transition,
                    transition.TargetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        /*
         * Capture the active + staging coexistence peak before staging
         * becomes the new authoritative active cache.
         */
        CaptureTransitionMemoryEstimate();

        activeCache =
            stagingCache;

        /*
         * Break the staging alias before completing memory tracking. The
         * previously captured coexistence value remains the transition peak.
         */
        currentCacheSetTransition.Entries[0].Destination.DetachStagingCache();

        CompleteTransitionMemoryTracking();

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .Activated
        );
        currentCacheSetTransition.State = TerrainAuthoringPreviewTransitionState.Activated;

        if (
            hasRequestedResidencyWindow
            &&
            requestedResidencyWindow ==
                transition.TargetWindow
        )
        {
            ClearRequestedResidency();
        }

        committedRebuildRequested =
            false;

        clipmapRebindRequested =
            false;

        dirtyCompositeTiles.Clear();

        overallSignatureAcknowledgementRequested =
            false;

        fullCommittedBuildCount++;

        ClearNativeTransitionFailureSuppression();

        MarkActiveCacheAuthoringGeneration(transition.TargetAuthoringGeneration);

        SetStatus(
            TerrainAuthoringPreviewStatus.Ready,
            "The staged resident height cache was activated."
        );

        NotifyPreviewStateChanged();

        /*
         * Active ownership and binding are already coherent before this
         * event. SceneViewController can therefore apply its latest desired
         * layout immediately against the new active coverage.
         */
        NotifyHeightCacheCoverageIfChanged();

        if (
            previousActive != null
            &&
            previousActive !=
                activeCache
        )
        {
            DetachTerrainAnalysisBorrowing(previousActive);
            previousActive.Dispose();
        }

        return true;
    }

    private static bool TryBindSpecificPreviewCache(
        TerrainAuthoringPreviewCache cache,
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            cache == null
            ||
            !cache.IsReady
            ||
            clipmapRoot == null
        )
        {
            errorMessage =
                "The requested preview cache cannot be bound because the cache or clipmap is unavailable.";

            return false;
        }

        TerrainClipmapBoundsController boundsController =
            clipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController == null)
        {
            errorMessage =
                "TerrainClipmapBoundsController is missing from WorldRoot/Clipmap.";

            return false;
        }

        using var bindingProfilerScope =
            WorldMeshesProfiler.PreviewBindCache.Auto();

        if (
            !TerrainHeightCacheBindingUtility
                .TryBind(
                    clipmapRoot,
                    cache.HeightCache,
                    cache.CacheOriginTile,
                    cache.CacheSize,
                    cache.SamplesPerSide,
                    cache.SampleSpacing,
                    cache.WorldSizeXZ,
                    out _,
                    out string bindingError
                )
        )
        {
            errorMessage =
                "The staged preview height cache could not be bound to the clipmap.\n\n" +
                bindingError;

            return false;
        }

        if (
            !boundsController.ApplyBoundsForRange(
                cache.MinimumHeight,
                cache.MaximumHeight
            )
        )
        {
            errorMessage =
                "The staged preview cache was bound, but its displacement bounds could not be applied.";

            return false;
        }

        boundClipmapRoot =
            clipmapRoot;

        diagnosticBindingApplyCount++;

        return true;
    }

    private static bool FailStagedTransition(
        TerrainAuthoringPreviewCacheTransition transition,
        TerrainHeightCacheWindow targetWindow,
        string targetCommittedSignature,
        string targetOverallSignature,
        string failureMessage
    )
    {
        string message =
            string.IsNullOrEmpty(
                failureMessage
            )
                ? "The staged height-cache transition failed."
                : failureMessage;

        if (transition != null)
        {
            transition.MarkFailed(
                message
            );


        }

        if (currentCacheSetTransition != null)
        {
            currentCacheSetTransition.State = TerrainAuthoringPreviewTransitionState.Failed;
            lastFailedCacheSetRequest = currentCacheSetTransition;
        }
        lastStreamingFailureMessage = message;
        SetStreamingState(TerrainAuthoringPreviewStreamingState.Failed, message);
        ReleaseStagingCacheOnly();

        hasTransitionFailure =
            true;

        lastFailedTransitionWindow =
            targetWindow;

        lastTransitionFailureMessage =
            message;

        lastFailedCommittedSignature =
            targetCommittedSignature ?? "";

        lastFailedOverallSignature =
            targetOverallSignature ?? "";

        bool activeStillCurrent =
            activeCache != null
            &&
            activeCache.IsCompleteForActivation
            &&
            activeCache
                .SourceCommittedHeightfieldSignature
            ==
            targetCommittedSignature
            &&
            activeCache
                .SourceOverallAuthoringSignature
            ==
            targetOverallSignature;

        if (activeStillCurrent)
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Ready,
                "The requested staged height-cache transition failed. " +
                "The previous resident terrain preview remains active.\n\n" +
                message
            );
        }
        else
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Error,
                "The requested staged height-cache transition failed. " +
                "The previous GPU cache was preserved, but it does not " +
                "represent the current authoritative authoring state.\n\n" +
                message
            );
        }

        HeightCacheTransitionFailed?.Invoke(
            targetWindow,
            message
        );

        RepaintEditorViews();

        return false;
    }

    private static bool IsTransitionFailureSuppressed(
        TerrainHeightCacheWindow targetWindow,
        out string failureMessage
    )
    {
        failureMessage =
            "";

        if (!hasTransitionFailure)
        {
            return false;
        }

        if (
            lastFailedTransitionWindow !=
                targetWindow
        )
        {
            ClearNativeTransitionFailureSuppression();

            return false;
        }

        WorldSettings worldSettings =
            LoadWorldSettings();

        TerrainAuthoringData authoringData =
            LoadAuthoringData();

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            ClearNativeTransitionFailureSuppression();

            return false;
        }

        string committedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            committedSignature !=
                lastFailedCommittedSignature
            ||
            overallSignature !=
                lastFailedOverallSignature
        )
        {
            ClearNativeTransitionFailureSuppression();

            return false;
        }

        failureMessage =
            lastTransitionFailureMessage;

        return true;
    }

    private static void ClearNativeTransitionFailureSuppression()
    {
        var preparedFailure = lastFailedCacheSetRequest != null
            && lastFailedCacheSetRequest.Publication == TerrainAuthoringPreviewCachePublication.PreparedHeightSet
                ? lastFailedCacheSetRequest : null;
        var analysisFailure = lastFailedAnalysisCacheSetRequest;
        ClearTransitionFailureSuppression();
        lastFailedCacheSetRequest = preparedFailure;
        lastFailedAnalysisCacheSetRequest = analysisFailure;
    }

    private static void ClearTransitionFailureSuppression()
    {
        lastFailedCacheSetRequest = null;
        lastFailedAnalysisCacheSetRequest = null;
        analysisSourceError = "";
        hasTransitionFailure =
            false;

        lastFailedTransitionWindow =
            default;

        lastTransitionFailureMessage =
            "";

        lastFailedCommittedSignature =
            "";

        lastFailedOverallSignature =
            "";
    }

    private static void ReleaseStagingCacheOnly()
    {
        if (currentCacheSetTransition == null) return;
        CompleteTransitionMemoryTracking();
        currentCacheSetTransition.Dispose();
    }

    private static bool IsCacheSetFailureSuppressed(TerrainAuthoringPreviewCacheSetTransition request)
    {
        var failed = request.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis
            ? lastFailedAnalysisCacheSetRequest : lastFailedCacheSetRequest;
        return failed != null && failed.Publication == request.Publication
            && failed.MatchesContent(request.CommittedSignature, request.OverallSignature,
                request.AuthoringGeneration, request.OwnershipGeneration, request.RebuildRequested)
            && TerrainAuthoringPreviewStreamingPolicy.AreCacheSetTargetsEquivalent(failed, request)
            && TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(
                failed.AcceptedPlan, request.AcceptedPlan);
    }

    private static void FailCacheSetTransaction(TerrainAuthoringPreviewCacheSetTransition t, string error)
    {
        t.State = TerrainAuthoringPreviewTransitionState.Failed;
        t.Error = error;
        CaptureTransitionMemoryEstimate();
        lastFailedCacheSetRequest = t;
        if (t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
        {
            lastFailedAnalysisCacheSetRequest = t;
            analysisSourceError = "Native analysis output " + analysisOutputWindow
                + "; dependency source " + analysisRequiredSourceWindow + ": " + error;
            PublishTerrainAnalysisSourceState(true);
        }
        lastStreamingFailureMessage = error;
        if (t.Publication == TerrainAuthoringPreviewCachePublication.NativePreview)
            FailStagedTransition(t.Entries[0].Transition, t.Entries[0].Target,
                t.CommittedSignature, t.OverallSignature, error);
        else ReleaseStagingCacheOnly();
        ClearPendingStreamingStart();
        SetStreamingState(TerrainAuthoringPreviewStreamingState.Failed, error);
    }

    private static void ReleaseAllPreviewCaches(
        bool notifyObservers = true
    )
    {
        ReleaseStagingCacheOnly();
        ClearPendingStreamingStart();
        ReleaseTerrainAnalysisSource(notifyObservers);

        if (activeCache != null)
        {
            activeCache.Dispose();

            activeCache =
                null;
        }

        currentCacheSetTransition = null;
        ReleasePreparedHeightCacheSet();

        ResetStreamingStateForResourceRelease(
            notifyObservers
        );

        ClearRegionalElevationResidencyForResourceRelease();

        ClearDesiredResidency();
        ClearTransitionFailureSuppression();

        if (notifyObservers)
        {
            NotifyHeightCacheCoverageIfChanged();

            NotifyPreviewStateChanged();
        }
    }
}

