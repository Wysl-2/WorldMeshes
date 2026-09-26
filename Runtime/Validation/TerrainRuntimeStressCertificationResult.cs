using System.Text;

public sealed class TerrainRuntimeStressCertificationResult
{
    public TerrainRuntimeValidationStatus BoundaryStressStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus ContinuousMovementStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus RapidSupersessionStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus RepeatedTransitionStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus DeferredReleaseStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus StreamerLifecycleStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus FinalRestoreStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public TerrainRuntimeValidationStatus OverallStatus { get; internal set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public float WorldWidth { get; internal set; }
    public float WorldHeight { get; internal set; }
    public float ClipmapDiameter { get; internal set; }
    public int HeightLodCount { get; internal set; }
    public int MaximumHeightSampleStride { get; internal set; }
    public int NativeHeightPageSamplesPerSide { get; internal set; }
    public long HeightSourceUpperBoundBytes { get; internal set; }
    public int SurfaceCacheWidth { get; internal set; }
    public int SurfaceCacheHeight { get; internal set; }
    public long SurfaceSourceUpperBoundBytes { get; internal set; }

    public int BoundaryLayoutsCompleted { get; internal set; }
    public int ContinuousMovementStepsCompleted { get; internal set; }
    public int RapidRequestsSubmitted { get; internal set; }
    public int RepeatedTransitionsCompleted { get; internal set; }
    public int StreamerLifecycleCyclesCompleted { get; internal set; }

    public int PeakActiveHeightLoads { get; internal set; }
    public int PeakHeightTransientSources { get; internal set; }
    public long PeakHeightSourceBytes { get; internal set; }
    public int PeakDeferredReleaseCount { get; internal set; }
    public long PeakDeferredReleaseBytes { get; internal set; }
    public int MaximumSurfaceResidentSources { get; internal set; }
    public long MaximumSurfaceSourceBytes { get; internal set; }

    public long HeightRequestsStarted { get; internal set; }
    public long HeightSourceUploads { get; internal set; }
    public long HeightCacheToCacheReuses { get; internal set; }
    public long RepeatedPlanSkips { get; internal set; }
    public long CoalescedRequests { get; internal set; }
    public long PrefetchPromotions { get; internal set; }
    public long StaleQueuedDiscards { get; internal set; }
    public long StaleCompletedDiscards { get; internal set; }
    public int PriorityViolations { get; internal set; }
    public int DuplicateStartViolations { get; internal set; }

    public long DeferredSourcesEnqueued { get; internal set; }
    public long DeferredSourcesReleased { get; internal set; }
    public long DeferredSourcesForcedReleased { get; internal set; }
    public long DeferredFenceFallbacks { get; internal set; }

    public string FailureReason { get; internal set; } = "";
    public string DeferredReleaseNote { get; internal set; } = "";

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "Runtime Terrain Stress Certification"
        );

        builder.AppendLine();
        builder.AppendLine("Configuration:");
        builder.AppendLine(
            $"  World: {WorldWidth:0.##} x {WorldHeight:0.##}"
        );
        builder.AppendLine(
            $"  Clipmap Diameter: {ClipmapDiameter:0.##}"
        );
        builder.AppendLine(
            $"  Height LODs: {HeightLodCount}"
        );
        builder.AppendLine(
            $"  Maximum Height Stride: {MaximumHeightSampleStride}"
        );
        builder.AppendLine(
            $"  Native Height Page: {NativeHeightPageSamplesPerSide} x {NativeHeightPageSamplesPerSide}"
        );
        builder.AppendLine(
            $"  Height Source Bound: {FormatBytes(HeightSourceUpperBoundBytes)}"
        );
        builder.AppendLine(
            $"  Surface Cache: {SurfaceCacheWidth} x {SurfaceCacheHeight}"
        );
        builder.AppendLine(
            $"  Surface Source Bound: {FormatBytes(SurfaceSourceUpperBoundBytes)}"
        );

        builder.AppendLine();
        builder.AppendLine("Phase Results:");
        AppendStatus(builder, "Boundary Stress", BoundaryStressStatus);
        AppendStatus(builder, "Continuous Movement", ContinuousMovementStatus);
        AppendStatus(builder, "Rapid Supersession", RapidSupersessionStatus);
        AppendStatus(builder, "Repeated Transitions", RepeatedTransitionStatus);
        AppendStatus(builder, "Deferred Release", DeferredReleaseStatus);
        AppendStatus(builder, "Streamer Lifecycle", StreamerLifecycleStatus);
        AppendStatus(builder, "Final Restore", FinalRestoreStatus);

        builder.AppendLine();
        builder.AppendLine("Height Scheduler:");
        builder.AppendLine(
            $"  Peak Active Loads: {PeakActiveHeightLoads}"
        );
        builder.AppendLine(
            $"  Peak Transient Sources: {PeakHeightTransientSources}"
        );
        builder.AppendLine(
            $"  Peak Source Payload: {FormatBytes(PeakHeightSourceBytes)} / {FormatBytes(HeightSourceUpperBoundBytes)}"
        );
        builder.AppendLine(
            $"  Requests Started: {HeightRequestsStarted}"
        );
        builder.AppendLine(
            $"  Source Uploads: {HeightSourceUploads}"
        );
        builder.AppendLine(
            $"  Cache-to-Cache Reuses: {HeightCacheToCacheReuses}"
        );
        builder.AppendLine(
            $"  Repeated Plan Skips: {RepeatedPlanSkips}"
        );
        builder.AppendLine(
            $"  Coalesced Requests: {CoalescedRequests}"
        );
        builder.AppendLine(
            $"  Prefetch Promotions: {PrefetchPromotions}"
        );
        builder.AppendLine(
            $"  Stale Queued Discards: {StaleQueuedDiscards}"
        );
        builder.AppendLine(
            $"  Stale Completed Discards: {StaleCompletedDiscards}"
        );
        builder.AppendLine(
            $"  Priority Violations: {PriorityViolations}"
        );
        builder.AppendLine(
            $"  Duplicate Starts: {DuplicateStartViolations}"
        );

        builder.AppendLine();
        builder.AppendLine("Deferred Height Sources:");
        builder.AppendLine(
            $"  Peak Pending Count: {PeakDeferredReleaseCount}"
        );
        builder.AppendLine(
            $"  Peak Pending Payload: {FormatBytes(PeakDeferredReleaseBytes)}"
        );
        builder.AppendLine(
            $"  Enqueued: {DeferredSourcesEnqueued}"
        );
        builder.AppendLine(
            $"  Released: {DeferredSourcesReleased}"
        );
        builder.AppendLine(
            $"  Forced Releases: {DeferredSourcesForcedReleased}"
        );
        builder.AppendLine(
            $"  Fence Fallbacks: {DeferredFenceFallbacks}"
        );

        if (!string.IsNullOrEmpty(DeferredReleaseNote))
        {
            builder.AppendLine(
                "  Note: " + DeferredReleaseNote
            );
        }

        builder.AppendLine();
        builder.AppendLine("Surface:");
        builder.AppendLine(
            $"  Maximum Resident Sources: {MaximumSurfaceResidentSources}"
        );
        builder.AppendLine(
            $"  Maximum Source Payload: {FormatBytes(MaximumSurfaceSourceBytes)} / {FormatBytes(SurfaceSourceUpperBoundBytes)}"
        );

        builder.AppendLine();
        builder.AppendLine("Completed Work:");
        builder.AppendLine(
            $"  Boundary Layouts: {BoundaryLayoutsCompleted}"
        );
        builder.AppendLine(
            $"  Continuous Steps: {ContinuousMovementStepsCompleted}"
        );
        builder.AppendLine(
            $"  Rapid Requests: {RapidRequestsSubmitted}"
        );
        builder.AppendLine(
            $"  Repeated Transitions: {RepeatedTransitionsCompleted}"
        );
        builder.AppendLine(
            $"  Lifecycle Cycles: {StreamerLifecycleCyclesCompleted}"
        );

        if (!string.IsNullOrEmpty(FailureReason))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Failure: " + FailureReason
            );
        }

        builder.AppendLine();
        builder.AppendLine(
            "Overall: " + OverallStatus
        );

        return builder.ToString();
    }

    private static void AppendStatus(
        StringBuilder builder,
        string label,
        TerrainRuntimeValidationStatus status
    )
    {
        builder.AppendLine(
            $"  {label}: {status}"
        );
    }

    private static string FormatBytes(
        long bytes
    )
    {
        if (bytes <= 0L)
        {
            return "0.00 MiB";
        }

        return
            (bytes / (1024d * 1024d))
                .ToString("N2") +
            " MiB";
    }
}
