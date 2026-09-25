using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

internal enum TerrainHeightPagePriorityClass
{
    VisibleRequired = 0,
    StitchRequired = 1,
    NormalMarginRequired = 2,
    DirectionalPrefetch = 3,
    GuardPrefetch = 4
}

internal sealed class TerrainHeightPageSourceTransfer
{
    public TerrainHeightPageKey Key;
    public TerrainHeightLodRuntimeState Owner;
    public string Address;
    public bool Required;
    public int Generation;
    public readonly HashSet<int> RequiredGenerations = new HashSet<int>();
    public int LodLevel;
    public int DistancePriority;
    public TerrainHeightPagePriorityClass PriorityClass;
    public long Sequence;
    public AsyncOperationHandle<Texture2D> Handle;
    public bool Started;
    public bool Abandoned;
    public Texture2DArray DestinationCache;
    public int DestinationSlice;
    public long EstimatedSourceBytes;
}

/*
 * Bounded Height source scheduler.
 *
 * Addressable Texture2Ds are transport resources only. The scheduler owns
 * every source handle from load start until a GPU-safe release boundary.
 * Durable terrain residency remains in the per-LOD Texture2DArray caches.
 */
internal sealed class TerrainHeightPageLoadScheduler
{
    private sealed class PendingGpuRelease
    {
        public AsyncOperationHandle<Texture2D> Handle;
        public bool UsesFence;
        public GraphicsFence Fence;
        public int ReleaseFrame;
        public long EstimatedSourceBytes;
    }

    private readonly List<TerrainHeightPageSourceTransfer> queued =
        new List<TerrainHeightPageSourceTransfer>();

    private readonly List<TerrainHeightPageSourceTransfer> inFlight =
        new List<TerrainHeightPageSourceTransfer>();

    private readonly List<TerrainHeightPageSourceTransfer> ready =
        new List<TerrainHeightPageSourceTransfer>();

    private readonly List<PendingGpuRelease> pendingGpuRelease =
        new List<PendingGpuRelease>();

    private readonly Dictionary<TerrainHeightPageKey, TerrainHeightPageSourceTransfer>
        requestsByKey =
            new Dictionary<TerrainHeightPageKey, TerrainHeightPageSourceTransfer>();

    private readonly HashSet<int> failedRequiredGenerations =
        new HashSet<int>();

    private long nextSequence;

    private int peakActiveLoadCount;
    private int peakTransientSourceCount;
    private int peakQueuedRequestCount;
    private long peakEstimatedLogicalSourceBytes;
    private long requestsStarted;
    private long coalescedRequestCount;
    private long repeatedPlanSubmissionSkipCount;
    private long sourceUploadCount;
    private long cacheToCacheReuseCount;
    private long prefetchPromotedToRequiredCount;
    private long staleQueuedRequestDiscardCount;
    private long staleCompletedSourceDiscardCount;
    private int priorityViolationCount;
    private int duplicateStartViolationCount;

    public int MaxConcurrentLoads { get; set; } = 8;
    public int ReservedRequiredSlots { get; set; } = 2;

    public int ActiveLoadCount => inFlight.Count;
    public int ReadySourceCount => ready.Count;
    public int PendingGpuReleaseCount => pendingGpuRelease.Count;

    public int TransientSourceSlotCount =>
        inFlight.Count + ready.Count + pendingGpuRelease.Count;

    public int QueuedLoadCount => queued.Count;

    public int QueuedRequiredCount
    {
        get
        {
            int count = 0;

            for (int index = 0; index < queued.Count; index++)
            {
                if (queued[index].Required)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int QueuedPrefetchCount => QueuedLoadCount - QueuedRequiredCount;

    public void Enqueue(
        TerrainHeightLodRuntimeState owner,
        Vector2Int coordinate,
        string address,
        Texture2DArray destinationCache,
        int destinationSlice,
        bool required,
        TerrainHeightPagePriorityClass priorityClass,
        int generation,
        int distancePriority
    )
    {
        if (
            owner == null
            || string.IsNullOrEmpty(address)
        )
        {
            return;
        }

        TerrainHeightPageKey key =
            new TerrainHeightPageKey(
                owner.SampleStride,
                coordinate
            );

        if (
            requestsByKey.TryGetValue(
                key,
                out TerrainHeightPageSourceTransfer existing
            )
        )
        {
            coalescedRequestCount++;

            if (required)
            {
                if (!existing.Required)
                {
                    prefetchPromotedToRequiredCount++;
                }

                existing.Required = true;
                existing.RequiredGenerations.Add(generation);
            }

            existing.Generation = Mathf.Max(existing.Generation, generation);
            existing.DistancePriority = Mathf.Min(existing.DistancePriority, distancePriority);
            existing.LodLevel = Mathf.Min(existing.LodLevel, owner.Level);

            if ((int)priorityClass < (int)existing.PriorityClass)
            {
                existing.PriorityClass = priorityClass;
            }

            if (destinationCache != null && destinationSlice >= 0)
            {
                existing.DestinationCache = destinationCache;
                existing.DestinationSlice = destinationSlice;
                existing.Owner = owner;
            }

            existing.Abandoned = false;
            return;
        }

        TerrainHeightPageSourceTransfer request =
            new TerrainHeightPageSourceTransfer
            {
                Key = key,
                Owner = owner,
                Address = address,
                Required = required,
                Generation = generation,
                LodLevel = owner.Level,
                DistancePriority = distancePriority,
                PriorityClass = priorityClass,
                Sequence = nextSequence++,
                DestinationCache = destinationCache,
                DestinationSlice = destinationSlice,
                EstimatedSourceBytes = EstimateSourceBytes(owner)
            };

        if (required)
        {
            request.RequiredGenerations.Add(generation);
        }

        requestsByKey.Add(key, request);
        queued.Add(request);
        peakQueuedRequestCount = Mathf.Max(peakQueuedRequestCount, queued.Count);
    }

    public void Pump()
    {
        FinalizeGpuReleases();
        FinalizeCompletedLoads();
        UpdateTransientPeaks();

        if (queued.Count <= 0)
        {
            return;
        }

        queued.Sort(CompareRequests);

        int limit = Mathf.Max(1, MaxConcurrentLoads);
        int reserved = Mathf.Clamp(ReservedRequiredSlots, 0, Mathf.Max(0, limit - 1));
        int optionalLimit = Mathf.Max(1, limit - reserved);

        while (
            TransientSourceSlotCount < limit
            && queued.Count > 0
        )
        {
            int requestIndex = FindNextStartableRequest(optionalLimit);

            if (requestIndex < 0)
            {
                break;
            }

            TerrainHeightPageSourceTransfer request = queued[requestIndex];

            if (!request.Required && HasQueuedRequiredRequest())
            {
                priorityViolationCount++;
            }

            queued.RemoveAt(requestIndex);
            StartRequest(request);
            UpdateTransientPeaks();
        }
    }

    public bool TryGetReadySource(
        out TerrainHeightPageSourceTransfer source
    )
    {
        source = ready.Count > 0 ? ready[0] : null;
        return source != null;
    }

    public void CompleteReadySourceUpload(
        TerrainHeightPageSourceTransfer source
    )
    {
        if (source == null || !ready.Remove(source))
        {
            return;
        }

        requestsByKey.Remove(source.Key);
        sourceUploadCount++;

        PendingGpuRelease pending =
            new PendingGpuRelease
            {
                Handle = source.Handle,
                EstimatedSourceBytes = source.EstimatedSourceBytes
            };

        if (SystemInfo.supportsGraphicsFence)
        {
            try
            {
                pending.Fence =
                    Graphics.CreateGraphicsFence(
                        GraphicsFenceType.AsyncQueueSynchronisation,
                        SynchronisationStageFlags.AllGPUOperations
                    );

                pending.UsesFence = true;
            }
            catch (Exception)
            {
                pending.UsesFence = false;
                pending.ReleaseFrame = Time.frameCount + 4;
            }
        }
        else
        {
            pending.UsesFence = false;
            pending.ReleaseFrame = Time.frameCount + 4;
        }

        pendingGpuRelease.Add(pending);
        UpdateTransientPeaks();
    }

    public void FailReadySourceUpload(
        TerrainHeightPageSourceTransfer source
    )
    {
        if (source == null || !ready.Remove(source))
        {
            return;
        }

        requestsByKey.Remove(source.Key);
        MarkRequiredFailure(source);

        PendingGpuRelease pending =
            new PendingGpuRelease
            {
                Handle = source.Handle,
                EstimatedSourceBytes = source.EstimatedSourceBytes
            };

        if (SystemInfo.supportsGraphicsFence)
        {
            try
            {
                pending.Fence =
                    Graphics.CreateGraphicsFence(
                        GraphicsFenceType.AsyncQueueSynchronisation,
                        SynchronisationStageFlags.AllGPUOperations
                    );

                pending.UsesFence = true;
            }
            catch (Exception)
            {
                pending.UsesFence = false;
                pending.ReleaseFrame = Time.frameCount + 4;
            }
        }
        else
        {
            pending.UsesFence = false;
            pending.ReleaseFrame = Time.frameCount + 4;
        }

        pendingGpuRelease.Add(pending);
        UpdateTransientPeaks();
    }

    public void DiscardReadySource(
        TerrainHeightPageSourceTransfer source,
        bool stale
    )
    {
        if (source == null || !ready.Remove(source))
        {
            return;
        }

        requestsByKey.Remove(source.Key);

        if (source.Handle.IsValid())
        {
            Addressables.Release(source.Handle);
        }

        if (stale)
        {
            staleCompletedSourceDiscardCount++;
        }
    }

    public void DiscardQueuedOptionalOlderThan(
        int generation
    )
    {
        for (int index = queued.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = queued[index];

            if (request.Required || request.Generation >= generation)
            {
                continue;
            }

            queued.RemoveAt(index);
            requestsByKey.Remove(request.Key);
            staleQueuedRequestDiscardCount++;
        }

        for (int index = 0; index < inFlight.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = inFlight[index];

            if (!request.Required && request.Generation < generation)
            {
                request.Abandoned = true;
            }
        }

        for (int index = ready.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = ready[index];

            if (request.Required || request.Generation >= generation)
            {
                continue;
            }

            ready.RemoveAt(index);
            requestsByKey.Remove(request.Key);

            if (request.Handle.IsValid())
            {
                Addressables.Release(request.Handle);
            }

            staleCompletedSourceDiscardCount++;
        }
    }

    public void CompleteGeneration(
        int generation
    )
    {
        for (int index = queued.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = queued[index];

            if (request.Generation == generation && !request.Required)
            {
                queued.RemoveAt(index);
                requestsByKey.Remove(request.Key);
                staleQueuedRequestDiscardCount++;
            }
        }

        for (int index = 0; index < inFlight.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = inFlight[index];

            if (request.Generation == generation && !request.Required)
            {
                request.Abandoned = true;
            }
        }

        for (int index = ready.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = ready[index];

            if (request.Generation == generation && !request.Required)
            {
                ready.RemoveAt(index);
                requestsByKey.Remove(request.Key);

                if (request.Handle.IsValid())
                {
                    Addressables.Release(request.Handle);
                }

                staleCompletedSourceDiscardCount++;
            }
        }
    }

    public void AbandonGeneration(
        int generation
    )
    {
        failedRequiredGenerations.Remove(generation);

        for (int index = queued.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = queued[index];

            if (request.Generation != generation)
            {
                continue;
            }

            queued.RemoveAt(index);
            requestsByKey.Remove(request.Key);
            staleQueuedRequestDiscardCount++;
        }

        for (int index = 0; index < inFlight.Count; index++)
        {
            if (inFlight[index].Generation == generation)
            {
                inFlight[index].Abandoned = true;
            }
        }

        for (int index = ready.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = ready[index];

            if (request.Generation != generation)
            {
                continue;
            }

            ready.RemoveAt(index);
            requestsByKey.Remove(request.Key);

            if (request.Handle.IsValid())
            {
                Addressables.Release(request.Handle);
            }

            staleCompletedSourceDiscardCount++;
        }
    }

    public bool HasRequiredFailure(int generation)
    {
        return failedRequiredGenerations.Contains(generation);
    }

    public void ClearRequiredFailure(int generation)
    {
        failedRequiredGenerations.Remove(generation);
    }

    public void RecordRepeatedPlanSubmissionSkip()
    {
        repeatedPlanSubmissionSkipCount++;
    }

    public void RecordCacheToCacheReuse()
    {
        cacheToCacheReuseCount++;
    }

    internal void GetCountsForLod(
        int level,
        out int queuedRequired,
        out int queuedPrefetch,
        out int inFlightRequired,
        out int inFlightPrefetch
    )
    {
        queuedRequired = 0;
        queuedPrefetch = 0;
        inFlightRequired = 0;
        inFlightPrefetch = 0;

        for (int index = 0; index < queued.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = queued[index];

            if (request.Owner == null || request.Owner.Level != level)
            {
                continue;
            }

            if (request.Required)
            {
                queuedRequired++;
            }
            else
            {
                queuedPrefetch++;
            }
        }

        for (int index = 0; index < inFlight.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = inFlight[index];

            if (request.Owner == null || request.Owner.Level != level)
            {
                continue;
            }

            if (request.Required)
            {
                inFlightRequired++;
            }
            else
            {
                inFlightPrefetch++;
            }
        }
    }

    internal TerrainHeightSchedulerDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return new TerrainHeightSchedulerDiagnosticsSnapshot(
            Mathf.Max(1, MaxConcurrentLoads),
            Mathf.Clamp(ReservedRequiredSlots, 0, Mathf.Max(0, MaxConcurrentLoads - 1)),
            ActiveLoadCount,
            ReadySourceCount,
            PendingGpuReleaseCount,
            TransientSourceSlotCount,
            QueuedRequiredCount,
            QueuedPrefetchCount,
            peakActiveLoadCount,
            peakTransientSourceCount,
            peakQueuedRequestCount,
            EstimateLogicalSourceBytes(),
            peakEstimatedLogicalSourceBytes,
            requestsStarted,
            coalescedRequestCount,
            repeatedPlanSubmissionSkipCount,
            sourceUploadCount,
            cacheToCacheReuseCount,
            prefetchPromotedToRequiredCount,
            staleQueuedRequestDiscardCount,
            staleCompletedSourceDiscardCount,
            priorityViolationCount,
            duplicateStartViolationCount,
            SystemInfo.supportsGraphicsFence
        );
    }

    internal void ResetDiagnosticsCounters()
    {
        peakActiveLoadCount = ActiveLoadCount;
        peakTransientSourceCount = TransientSourceSlotCount;
        peakQueuedRequestCount = queued.Count;
        peakEstimatedLogicalSourceBytes = EstimateLogicalSourceBytes();
        requestsStarted = 0L;
        coalescedRequestCount = 0L;
        repeatedPlanSubmissionSkipCount = 0L;
        sourceUploadCount = 0L;
        cacheToCacheReuseCount = 0L;
        prefetchPromotedToRequiredCount = 0L;
        staleQueuedRequestDiscardCount = 0L;
        staleCompletedSourceDiscardCount = 0L;
        priorityViolationCount = 0;
        duplicateStartViolationCount = 0;
    }

    public void Shutdown()
    {
        queued.Clear();

        for (int index = 0; index < inFlight.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = inFlight[index];

            if (request.Started && request.Handle.IsValid())
            {
                Addressables.Release(request.Handle);
            }
        }

        for (int index = 0; index < ready.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = ready[index];

            if (request.Handle.IsValid())
            {
                Addressables.Release(request.Handle);
            }
        }

        for (int index = 0; index < pendingGpuRelease.Count; index++)
        {
            PendingGpuRelease pending = pendingGpuRelease[index];

            TerrainHeightDeferredSourceReleaseQueue.Enqueue(
                pending.Handle,
                pending.UsesFence,
                pending.Fence,
                pending.ReleaseFrame
            );
        }

        inFlight.Clear();
        ready.Clear();
        pendingGpuRelease.Clear();
        requestsByKey.Clear();
        failedRequiredGenerations.Clear();
    }

    private void StartRequest(TerrainHeightPageSourceTransfer request)
    {
        if (HasInFlightKey(request.Key))
        {
            duplicateStartViolationCount++;
            requestsByKey.Remove(request.Key);
            MarkRequiredFailure(request);
            return;
        }

        try
        {
            request.Handle = Addressables.LoadAssetAsync<Texture2D>(request.Address);
            request.Started = true;
            inFlight.Add(request);
            requestsStarted++;
            peakActiveLoadCount = Mathf.Max(peakActiveLoadCount, inFlight.Count);
        }
        catch (Exception)
        {
            requestsByKey.Remove(request.Key);
            MarkRequiredFailure(request);
        }
    }

    private void FinalizeCompletedLoads()
    {
        for (int index = inFlight.Count - 1; index >= 0; index--)
        {
            TerrainHeightPageSourceTransfer request = inFlight[index];

            if (!request.Handle.IsDone)
            {
                continue;
            }

            inFlight.RemoveAt(index);

            bool succeeded =
                request.Handle.Status == AsyncOperationStatus.Succeeded
                && request.Handle.Result != null
                && ValidateTexture(request.Owner, request.Handle.Result);

            if (request.Abandoned)
            {
                requestsByKey.Remove(request.Key);

                if (request.Handle.IsValid())
                {
                    Addressables.Release(request.Handle);
                }

                staleCompletedSourceDiscardCount++;
                continue;
            }

            if (!succeeded)
            {
                requestsByKey.Remove(request.Key);

                if (request.Handle.IsValid())
                {
                    Addressables.Release(request.Handle);
                }

                MarkRequiredFailure(request);
                continue;
            }

            request.EstimatedSourceBytes = EstimateSourceBytes(request.Owner);
            ready.Add(request);
        }
    }

    private void FinalizeGpuReleases()
    {
        for (int index = pendingGpuRelease.Count - 1; index >= 0; index--)
        {
            PendingGpuRelease pending = pendingGpuRelease[index];
            bool canRelease;

            if (pending.UsesFence)
            {
                try
                {
                    canRelease = pending.Fence.passed;
                }
                catch (Exception)
                {
                    pending.UsesFence = false;
                    pending.ReleaseFrame = Time.frameCount + 4;
                    canRelease = false;
                }
            }
            else
            {
                canRelease = Time.frameCount >= pending.ReleaseFrame;
            }

            if (!canRelease)
            {
                continue;
            }

            pendingGpuRelease.RemoveAt(index);

            if (pending.Handle.IsValid())
            {
                Addressables.Release(pending.Handle);
            }
        }
    }

    private int FindNextStartableRequest(int optionalLimit)
    {
        for (int index = 0; index < queued.Count; index++)
        {
            TerrainHeightPageSourceTransfer request = queued[index];

            if (request.Required)
            {
                return index;
            }

            if (TransientSourceSlotCount < optionalLimit)
            {
                return index;
            }
        }

        return -1;
    }

    private bool HasQueuedRequiredRequest()
    {
        for (int index = 0; index < queued.Count; index++)
        {
            if (queued[index].Required)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasInFlightKey(TerrainHeightPageKey key)
    {
        for (int index = 0; index < inFlight.Count; index++)
        {
            if (inFlight[index].Key.Equals(key))
            {
                return true;
            }
        }

        return false;
    }

    private void MarkRequiredFailure(TerrainHeightPageSourceTransfer request)
    {
        if (request == null || !request.Required)
        {
            return;
        }

        if (request.RequiredGenerations.Count == 0)
        {
            failedRequiredGenerations.Add(request.Generation);
            return;
        }

        foreach (int generation in request.RequiredGenerations)
        {
            failedRequiredGenerations.Add(generation);
        }
    }

    private static bool ValidateTexture(
        TerrainHeightLodRuntimeState owner,
        Texture2D texture
    )
    {
        if (owner == null || texture == null)
        {
            return false;
        }

        TerrainHeightStreamingLevelDescriptor descriptor = owner.Descriptor;

        return
            texture.width == descriptor.SamplesPerSide
            && texture.height == descriptor.SamplesPerSide
            && texture.format == descriptor.TextureFormat;
    }

    private static int CompareRequests(
        TerrainHeightPageSourceTransfer left,
        TerrainHeightPageSourceTransfer right
    )
    {
        if (left.Required != right.Required)
        {
            return left.Required ? -1 : 1;
        }

        int levelComparison = left.LodLevel.CompareTo(right.LodLevel);

        if (levelComparison != 0)
        {
            return levelComparison;
        }

        int classComparison =
            ((int)left.PriorityClass).CompareTo((int)right.PriorityClass);

        if (classComparison != 0)
        {
            return classComparison;
        }

        int distanceComparison =
            left.DistancePriority.CompareTo(right.DistancePriority);

        if (distanceComparison != 0)
        {
            return distanceComparison;
        }

        return left.Sequence.CompareTo(right.Sequence);
    }

    private static long EstimateSourceBytes(TerrainHeightLodRuntimeState owner)
    {
        if (owner == null)
        {
            return 0L;
        }

        long samples = Mathf.Max(0, owner.Descriptor.SamplesPerSide);
        return samples * samples * 4L;
    }

    private long EstimateLogicalSourceBytes()
    {
        long total = 0L;

        for (int index = 0; index < inFlight.Count; index++)
        {
            total += inFlight[index].EstimatedSourceBytes;
        }

        for (int index = 0; index < ready.Count; index++)
        {
            total += ready[index].EstimatedSourceBytes;
        }

        for (int index = 0; index < pendingGpuRelease.Count; index++)
        {
            total += pendingGpuRelease[index].EstimatedSourceBytes;
        }

        return total;
    }

    private void UpdateTransientPeaks()
    {
        peakTransientSourceCount =
            Mathf.Max(peakTransientSourceCount, TransientSourceSlotCount);

        peakQueuedRequestCount =
            Mathf.Max(peakQueuedRequestCount, queued.Count);

        peakEstimatedLogicalSourceBytes =
            Math.Max(
                peakEstimatedLogicalSourceBytes,
                EstimateLogicalSourceBytes()
            );
    }
}
