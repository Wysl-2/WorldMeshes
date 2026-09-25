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
    public int ActiveValidPageCount { get; }
    public int StagingValidPageCount { get; }
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
        ActiveValidPageCount = state.ActiveValidPages.Count;
        StagingValidPageCount = state.StagingValidPages.Count;
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

        EstimatedActiveGpuBytes = state.ActiveCache != null ? cacheBytes : 0L;
        EstimatedStagingGpuBytes = state.StagingCache != null ? cacheBytes : 0L;
    }
}

public readonly struct TerrainHeightSchedulerDiagnosticsSnapshot
{
    public int ConcurrencyLimit { get; }
    public int ReservedRequiredSlots { get; }
    public int ActiveLoadCount { get; }
    public int ReadySourceCount { get; }
    public int PendingGpuReleaseCount { get; }
    public int TransientSourceSlotCount { get; }
    public int QueuedRequiredCount { get; }
    public int QueuedPrefetchCount { get; }
    public int PeakActiveLoadCount { get; }
    public int PeakTransientSourceCount { get; }
    public int PeakQueuedRequestCount { get; }
    public long EstimatedLogicalSourceBytes { get; }
    public long PeakEstimatedLogicalSourceBytes { get; }
    public long RequestsStarted { get; }
    public long CoalescedRequestCount { get; }
    public long RepeatedPlanSubmissionSkipCount { get; }
    public long SourceUploadCount { get; }
    public long CacheToCacheReuseCount { get; }
    public long PrefetchPromotedToRequiredCount { get; }
    public long StaleQueuedRequestDiscardCount { get; }
    public long StaleCompletedSourceDiscardCount { get; }
    public int PriorityViolationCount { get; }
    public int DuplicateStartViolationCount { get; }
    public bool GraphicsFenceSupported { get; }

    internal TerrainHeightSchedulerDiagnosticsSnapshot(
        int concurrencyLimit,
        int reservedRequiredSlots,
        int activeLoadCount,
        int readySourceCount,
        int pendingGpuReleaseCount,
        int transientSourceSlotCount,
        int queuedRequiredCount,
        int queuedPrefetchCount,
        int peakActiveLoadCount,
        int peakTransientSourceCount,
        int peakQueuedRequestCount,
        long estimatedLogicalSourceBytes,
        long peakEstimatedLogicalSourceBytes,
        long requestsStarted,
        long coalescedRequestCount,
        long repeatedPlanSubmissionSkipCount,
        long sourceUploadCount,
        long cacheToCacheReuseCount,
        long prefetchPromotedToRequiredCount,
        long staleQueuedRequestDiscardCount,
        long staleCompletedSourceDiscardCount,
        int priorityViolationCount,
        int duplicateStartViolationCount,
        bool graphicsFenceSupported
    )
    {
        ConcurrencyLimit = concurrencyLimit;
        ReservedRequiredSlots = reservedRequiredSlots;
        ActiveLoadCount = activeLoadCount;
        ReadySourceCount = readySourceCount;
        PendingGpuReleaseCount = pendingGpuReleaseCount;
        TransientSourceSlotCount = transientSourceSlotCount;
        QueuedRequiredCount = queuedRequiredCount;
        QueuedPrefetchCount = queuedPrefetchCount;
        PeakActiveLoadCount = peakActiveLoadCount;
        PeakTransientSourceCount = peakTransientSourceCount;
        PeakQueuedRequestCount = peakQueuedRequestCount;
        EstimatedLogicalSourceBytes = estimatedLogicalSourceBytes;
        PeakEstimatedLogicalSourceBytes = peakEstimatedLogicalSourceBytes;
        RequestsStarted = requestsStarted;
        CoalescedRequestCount = coalescedRequestCount;
        RepeatedPlanSubmissionSkipCount = repeatedPlanSubmissionSkipCount;
        SourceUploadCount = sourceUploadCount;
        CacheToCacheReuseCount = cacheToCacheReuseCount;
        PrefetchPromotedToRequiredCount = prefetchPromotedToRequiredCount;
        StaleQueuedRequestDiscardCount = staleQueuedRequestDiscardCount;
        StaleCompletedSourceDiscardCount = staleCompletedSourceDiscardCount;
        PriorityViolationCount = priorityViolationCount;
        DuplicateStartViolationCount = duplicateStartViolationCount;
        GraphicsFenceSupported = graphicsFenceSupported;
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

    internal TerrainHeightLodInspectionSnapshot(TerrainHeightLodRuntimeState state)
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
