using UnityEngine;

public readonly struct TerrainHeightLodDiagnosticsSnapshot
{
    public int Level { get; }
    public int SampleStride { get; }
    public float SampleSpacing { get; }
    public int SamplesPerSide { get; }
    public Vector2Int CacheOrigin { get; }
    public int CacheWidth { get; }
    public int CacheHeight { get; }
    public TerrainHeightPageRect ActiveRequiredPages { get; }
    public TerrainHeightPageRect RequestedRequiredPages { get; }
    public TerrainHeightPageRect RequestedPrefetchPages { get; }
    public int ResidentPageCount { get; }
    public int ActiveValidPageCount { get; }
    public int QueuedRequiredPageCount { get; }
    public int QueuedPrefetchPageCount { get; }
    public int InFlightRequiredPageCount { get; }
    public int InFlightPrefetchPageCount { get; }
    public bool CacheReady { get; }
    public TerrainHeightLodTransitionState TransitionState { get; }
    public long EstimatedActiveGpuBytes { get; }
    public long EstimatedStagingGpuBytes { get; }

    internal TerrainHeightLodDiagnosticsSnapshot(
        TerrainHeightLodRuntimeState state,
        int queuedRequired,
        int queuedPrefetch,
        int inFlightRequired,
        int inFlightPrefetch
    )
    {
        Level = state.Level;
        SampleStride = state.SampleStride;
        SampleSpacing = state.Descriptor.SampleSpacing;
        SamplesPerSide = state.Descriptor.SamplesPerSide;
        CacheOrigin = state.ActiveCacheOrigin;
        CacheWidth = state.CacheWidth;
        CacheHeight = state.CacheHeight;
        ActiveRequiredPages = state.ActiveRequiredPages;
        RequestedRequiredPages = state.RequestedRequiredPages;
        RequestedPrefetchPages = state.RequestedPrefetchPages;
        ResidentPageCount = state.ResidentPages.Count;
        ActiveValidPageCount = state.ActiveValidPages.Count;
        QueuedRequiredPageCount = queuedRequired;
        QueuedPrefetchPageCount = queuedPrefetch;
        InFlightRequiredPageCount = inFlightRequired;
        InFlightPrefetchPageCount = inFlightPrefetch;
        CacheReady = state.CacheReady;
        TransitionState = state.TransitionState;

        long sliceBytes =
            (long)Mathf.Max(0, state.Descriptor.SamplesPerSide) *
            Mathf.Max(0, state.Descriptor.SamplesPerSide) *
            4L;

        long cacheBytes =
            sliceBytes *
            Mathf.Max(0, state.CacheWidth) *
            Mathf.Max(0, state.CacheHeight);

        EstimatedActiveGpuBytes =
            state.ActiveCache != null
                ? cacheBytes
                : 0L;

        EstimatedStagingGpuBytes =
            state.StagingCache != null
                ? cacheBytes
                : 0L;
    }
}

public readonly struct TerrainHeightSchedulerDiagnosticsSnapshot
{
    public int ConcurrencyLimit { get; }
    public int ActiveLoadCount { get; }
    public int QueuedRequiredCount { get; }
    public int QueuedPrefetchCount { get; }
    public int PeakActiveLoadCount { get; }
    public long RequestsStarted { get; }
    public long CoalescedRequestCount { get; }
    public long PrefetchPromotedToRequiredCount { get; }
    public long StaleQueuedRequestDiscardCount { get; }
    public int PriorityViolationCount { get; }
    public int DuplicateStartViolationCount { get; }

    internal TerrainHeightSchedulerDiagnosticsSnapshot(
        int concurrencyLimit,
        int activeLoadCount,
        int queuedRequiredCount,
        int queuedPrefetchCount,
        int peakActiveLoadCount,
        long requestsStarted,
        long coalescedRequestCount,
        long prefetchPromotedToRequiredCount,
        long staleQueuedRequestDiscardCount,
        int priorityViolationCount,
        int duplicateStartViolationCount
    )
    {
        ConcurrencyLimit = concurrencyLimit;
        ActiveLoadCount = activeLoadCount;
        QueuedRequiredCount = queuedRequiredCount;
        QueuedPrefetchCount = queuedPrefetchCount;
        PeakActiveLoadCount = peakActiveLoadCount;
        RequestsStarted = requestsStarted;
        CoalescedRequestCount = coalescedRequestCount;
        PrefetchPromotedToRequiredCount = prefetchPromotedToRequiredCount;
        StaleQueuedRequestDiscardCount = staleQueuedRequestDiscardCount;
        PriorityViolationCount = priorityViolationCount;
        DuplicateStartViolationCount = duplicateStartViolationCount;
    }
}

internal readonly struct TerrainHeightLodInspectionSnapshot
{
    public int Level { get; }
    public int SampleStride { get; }
    public TerrainHeightStreamingLevelDescriptor Descriptor { get; }
    public Texture2DArray ActiveCache { get; }
    public Vector2Int ActiveCacheOrigin { get; }
    public int CacheWidth { get; }
    public int CacheHeight { get; }
    public TerrainHeightPageRect ActiveRequiredPages { get; }
    public TerrainHeightPageRect RequestedRequiredPages { get; }
    public TerrainHeightPageRect RequestedPrefetchPages { get; }
    public bool CacheReady { get; }
    public TerrainHeightLodTransitionState TransitionState { get; }

    internal TerrainHeightLodInspectionSnapshot(
        TerrainHeightLodRuntimeState state
    )
    {
        Level = state.Level;
        SampleStride = state.SampleStride;
        Descriptor = state.Descriptor;
        ActiveCache = state.ActiveCache;
        ActiveCacheOrigin = state.ActiveCacheOrigin;
        CacheWidth = state.CacheWidth;
        CacheHeight = state.CacheHeight;
        ActiveRequiredPages = state.ActiveRequiredPages;
        RequestedRequiredPages = state.RequestedRequiredPages;
        RequestedPrefetchPages = state.RequestedPrefetchPages;
        CacheReady = state.CacheReady;
        TransitionState = state.TransitionState;
    }
}
