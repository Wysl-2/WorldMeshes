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

    internal static TerrainAuthoringPreviewDiagnosticsSnapshot
        GetDiagnosticsSnapshot()
    {
        bool hasActiveWindow =
            TryGetActiveResidentWindow(
                out TerrainHeightCacheWindow activeWindow
            );

        bool hasRequiredWindow =
            TryGetLatestRequiredResidentWindow(
                out TerrainHeightCacheWindow requiredWindow
            );

        bool hasDesiredWindow =
            TryGetDesiredResidentWindow(
                out TerrainHeightCacheWindow desiredWindow
            );

        bool hasRequestedWindow =
            TryGetRequestedResidentWindow(
                out TerrainHeightCacheWindow requestedWindow
            );

        bool hasStagingWindow =
            TryGetStagingResidentWindow(
                out TerrainHeightCacheWindow stagingWindow
            );

        bool hasTargetWindow =
            hasStagingWindow
            ||
            hasRequestedWindow;

        TerrainHeightCacheWindow targetWindow =
            hasStagingWindow
                ? stagingWindow
                : hasRequestedWindow
                    ? requestedWindow
                    : default;

        bool activeCoversRequired = latestDisplayIntent != null && CanActiveHeightCacheSetCover(latestDisplayIntent.Plan);
        bool failureAffectsRequiredCoverage = hasTransitionFailure && latestDisplayIntent != null
            && lastFailedCacheSetRequest != null && !activeCoversRequired
            && lastFailedCacheSetRequest.DisplayIntent?.PlacementGeneration == latestDisplayIntent.PlacementGeneration;
        int activeTileCount = 0;
        if (activeHeightStates != null) foreach (var state in activeHeightStates) activeTileCount += state.ActiveCache?.SliceCount ?? 0;

        int retainedTileCount = LastTransitionRetainedTileCount;

        int reusableRetainedTileCount = StreamingRetainedTileCount;

        int enteringTileCount = LastTransitionEnteringTileCount;

        int leavingTileCount = LastTransitionLeavingTileCount;

        int retainedCopiedCount = StreamingRetainedCopiedCount;

        int sourceLoadedCount = StreamingSourceLoadedCount;

        int sourceComposedCount = StreamingSourceComposedCount;

        int sourceTileCount = StreamingSourceTileCount;

        return
            new TerrainAuthoringPreviewDiagnosticsSnapshot(
                Enabled,
                CacheReady,
                status,
                statusMessage,
                hasActiveWindow,
                activeWindow,
                hasRequiredWindow,
                requiredWindow,
                hasDesiredWindow,
                desiredWindow,
                hasRequestedWindow,
                requestedWindow,
                hasStagingWindow,
                stagingWindow,
                hasTargetWindow,
                targetWindow,
                streamingState,
                streamingStatusMessage,
                StreamingProgress,
                IsStreaming,
                IsWaitingForStreamingCoverage,
                activeTileCount,
                retainedTileCount,
                reusableRetainedTileCount,
                enteringTileCount,
                leavingTileCount,
                retainedCopiedCount,
                sourceLoadedCount,
                sourceComposedCount,
                sourceTileCount,
                authoringGeneration,
                streamingRequestGeneration,
                analysisResidencyGeneration,
                analysisCompositeGeneration,
                ApproximateGpuMemoryBytes,
                ApproximateStagingGpuMemoryBytes,
                ApproximateTotalResidentGpuMemoryBytes,
                PeakTransitionGpuMemoryBytes,
                TerrainAuthoringPreviewCache
                    .DiagnosticCreateCount,
                TerrainAuthoringPreviewCache
                    .DiagnosticDisposeCount,
                TerrainAuthoringPreviewCache
                    .DiagnosticLiveCount,
                IsEditorLifecycleStable,
                CanRunEditorPreviewWork,
                lifecycleResumePending,
                suspensionReasons,
                TerrainAuthoringSceneViewController
                    .HasControllingSceneView,
                TerrainAuthoringSceneViewController
                    .ControllingSceneViewInstanceId,
                TerrainAuthoringSceneViewController
                    .SceneViewOwnershipGeneration,
                TerrainAuthoringSceneViewController
                    .FollowSceneView,
                TerrainAuthoringSceneViewController
                    .FollowSource,
                TerrainAuthoringSceneViewController
                    .FreezePreview,
                hasTransitionFailure,
                lastFailedTransitionWindow,
                lastTransitionFailureMessage,
                failureAffectsRequiredCoverage
            );
    }

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
}
