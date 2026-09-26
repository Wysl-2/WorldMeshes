public readonly struct TerrainHeightDeferredReleaseDiagnosticsSnapshot
{
    public int PendingCount { get; }
    public long EstimatedPendingSourceBytes { get; }
    public int PeakPendingCount { get; }
    public long PeakEstimatedPendingSourceBytes { get; }
    public long EnqueuedCount { get; }
    public long ReleasedCount { get; }
    public long ForcedReleaseCount { get; }
    public long FenceFallbackCount { get; }

    internal TerrainHeightDeferredReleaseDiagnosticsSnapshot(
        int pendingCount,
        long estimatedPendingSourceBytes,
        int peakPendingCount,
        long peakEstimatedPendingSourceBytes,
        long enqueuedCount,
        long releasedCount,
        long forcedReleaseCount,
        long fenceFallbackCount
    )
    {
        PendingCount = pendingCount;
        EstimatedPendingSourceBytes = estimatedPendingSourceBytes;
        PeakPendingCount = peakPendingCount;
        PeakEstimatedPendingSourceBytes =
            peakEstimatedPendingSourceBytes;
        EnqueuedCount = enqueuedCount;
        ReleasedCount = releasedCount;
        ForcedReleaseCount = forcedReleaseCount;
        FenceFallbackCount = fenceFallbackCount;
    }
}

public partial class TerrainHeightmapStreamer
{
    public bool RuntimeStreamingInitialized =>
        initialized;

    public TerrainHeightDeferredReleaseDiagnosticsSnapshot
        GetHeightDeferredReleaseDiagnostics()
    {
        return
            TerrainHeightDeferredSourceReleaseQueue
                .GetDiagnosticsSnapshot();
    }

    public void ResetHeightDeferredReleaseValidationCounters()
    {
        TerrainHeightDeferredSourceReleaseQueue
            .ResetDiagnosticsCounters();
    }
}
