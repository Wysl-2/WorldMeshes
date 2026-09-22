using System;

/*
 * Package 08B read-only projection of the live edit-mode preview.
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

        bool activeCoversRequired =
            hasActiveWindow
            &&
            hasRequiredWindow
            &&
            activeWindow.Contains(
                requiredWindow
            );

        bool failedTargetRelevant =
            hasTransitionFailure
            &&
            hasRequiredWindow
            &&
            lastFailedTransitionWindow.IsValid
            &&
            (
                lastFailedTransitionWindow.Contains(
                    requiredWindow
                )
                ||
                lastFailedTransitionWindow.Overlaps(
                    requiredWindow
                )
            );

        bool failureAffectsRequiredCoverage =
            hasTransitionFailure
            &&
            hasRequiredWindow
            &&
            !activeCoversRequired
            &&
            failedTargetRelevant;

        int activeTileCount =
            hasActiveWindow
                ? activeWindow.TileCount
                : 0;

        int retainedTileCount =
            currentTransition != null
                ? currentTransition.RetainedTiles.Count
                : 0;

        int reusableRetainedTileCount =
            currentTransition != null
                ? currentTransition.ReusableRetainedTiles.Count
                : 0;

        int enteringTileCount =
            currentTransition != null
                ? currentTransition.EnteringTiles.Count
                : 0;

        int leavingTileCount =
            currentTransition != null
                ? currentTransition.LeavingTiles.Count
                : 0;

        int retainedCopiedCount =
            currentTransition != null
                ? currentTransition.RetainedGpuCopyCount
                : 0;

        int sourceLoadedCount =
            currentTransition != null
                ? currentTransition.CommittedSourceLoadCount
                : 0;

        int sourceComposedCount =
            currentTransition != null
                ? currentTransition.FullyComposedTileCount
                : 0;

        int sourceTileCount =
            currentTransition != null
                ? currentTransition.SourceMaterializationTiles.Count
                : 0;

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
