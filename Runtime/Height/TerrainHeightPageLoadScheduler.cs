using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/*
 * Bounded multiresolution height-page scheduler.
 *
 * MRH07 adds validation/diagnostic counters only. Scheduling policy remains:
 * required before prefetch, near LOD before far LOD, bounded concurrency, and
 * one queued/in-flight request per (stride, coordinate).
 */
internal sealed class TerrainHeightPageLoadScheduler
{
    private sealed class Request
    {
        public TerrainHeightPageKey Key;
        public TerrainHeightLodRuntimeState Owner;
        public string Address;
        public bool Required;
        public int Generation;
        public readonly HashSet<int> RequiredGenerations =
            new HashSet<int>();
        public int LodLevel;
        public int DistancePriority;
        public long Sequence;
        public AsyncOperationHandle<Texture2D> Handle;
        public bool Started;
    }

    private readonly List<Request> queued =
        new List<Request>();

    private readonly List<Request> inFlight =
        new List<Request>();

    private readonly Dictionary<TerrainHeightPageKey, Request>
        requestsByKey =
            new Dictionary<TerrainHeightPageKey, Request>();

    private readonly HashSet<int> failedRequiredGenerations =
        new HashSet<int>();

    private long nextSequence;

    private int peakActiveLoadCount;
    private long requestsStarted;
    private long coalescedRequestCount;
    private long prefetchPromotedToRequiredCount;
    private long staleQueuedRequestDiscardCount;
    private int priorityViolationCount;
    private int duplicateStartViolationCount;

    public int MaxConcurrentLoads { get; set; } = 8;

    public int ActiveLoadCount =>
        inFlight.Count;

    public int QueuedLoadCount =>
        queued.Count;

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

    public int QueuedPrefetchCount =>
        QueuedLoadCount - QueuedRequiredCount;

    public void Enqueue(
        TerrainHeightLodRuntimeState owner,
        Vector2Int coordinate,
        string address,
        bool required,
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

        if (owner.HasLoadedPage(coordinate))
        {
            TerrainHeightResidentPage resident =
                owner.ResidentPages[coordinate];

            resident.LastRequestGeneration =
                Mathf.Max(
                    resident.LastRequestGeneration,
                    generation
                );

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
                out Request existing
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

            existing.Generation =
                Mathf.Max(
                    existing.Generation,
                    generation
                );

            existing.DistancePriority =
                Mathf.Min(
                    existing.DistancePriority,
                    distancePriority
                );

            existing.LodLevel =
                Mathf.Min(
                    existing.LodLevel,
                    owner.Level
                );

            return;
        }

        Request request =
            new Request
            {
                Key = key,
                Owner = owner,
                Address = address,
                Required = required,
                Generation = generation,
                LodLevel = owner.Level,
                DistancePriority = distancePriority,
                Sequence = nextSequence++
            };

        if (required)
        {
            request.RequiredGenerations.Add(generation);
        }

        requestsByKey.Add(key, request);
        queued.Add(request);
    }

    public void Pump()
    {
        FinalizeCompletedLoads();

        if (queued.Count <= 0)
        {
            return;
        }

        queued.Sort(CompareRequests);

        int limit =
            Mathf.Max(
                1,
                MaxConcurrentLoads
            );

        while (
            inFlight.Count < limit
            && queued.Count > 0
        )
        {
            Request request = queued[0];

            if (!request.Required && HasQueuedRequiredRequest())
            {
                priorityViolationCount++;
            }

            queued.RemoveAt(0);
            StartRequest(request);
        }
    }

    public bool HasRequiredFailure(
        int generation
    )
    {
        return
            failedRequiredGenerations.Contains(
                generation
            );
    }

    public void ClearRequiredFailure(
        int generation
    )
    {
        failedRequiredGenerations.Remove(generation);
    }

    public void DiscardQueuedOlderThan(
        int generation
    )
    {
        for (
            int index = queued.Count - 1;
            index >= 0;
            index--
        )
        {
            Request request = queued[index];

            if (request.Generation >= generation)
            {
                continue;
            }

            queued.RemoveAt(index);
            requestsByKey.Remove(request.Key);
            staleQueuedRequestDiscardCount++;
        }
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
            Request request = queued[index];

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
            Request request = inFlight[index];

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

    internal TerrainHeightSchedulerDiagnosticsSnapshot
        GetDiagnosticsSnapshot()
    {
        return
            new TerrainHeightSchedulerDiagnosticsSnapshot(
                Mathf.Max(1, MaxConcurrentLoads),
                ActiveLoadCount,
                QueuedRequiredCount,
                QueuedPrefetchCount,
                peakActiveLoadCount,
                requestsStarted,
                coalescedRequestCount,
                prefetchPromotedToRequiredCount,
                staleQueuedRequestDiscardCount,
                priorityViolationCount,
                duplicateStartViolationCount
            );
    }

    internal void ResetDiagnosticsCounters()
    {
        peakActiveLoadCount =
            ActiveLoadCount;

        requestsStarted = 0L;
        coalescedRequestCount = 0L;
        prefetchPromotedToRequiredCount = 0L;
        staleQueuedRequestDiscardCount = 0L;
        priorityViolationCount = 0;
        duplicateStartViolationCount = 0;
    }

    public void Shutdown()
    {
        queued.Clear();

        for (
            int index = 0;
            index < inFlight.Count;
            index++
        )
        {
            Request request = inFlight[index];

            if (
                request.Started
                && request.Handle.IsValid()
            )
            {
                Addressables.Release(request.Handle);
            }
        }

        inFlight.Clear();
        requestsByKey.Clear();
        failedRequiredGenerations.Clear();
    }

    private void StartRequest(
        Request request
    )
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
            request.Handle =
                Addressables.LoadAssetAsync<Texture2D>(
                    request.Address
                );

            request.Started = true;
            inFlight.Add(request);

            requestsStarted++;
            peakActiveLoadCount =
                Mathf.Max(
                    peakActiveLoadCount,
                    inFlight.Count
                );
        }
        catch (Exception)
        {
            requestsByKey.Remove(request.Key);
            MarkRequiredFailure(request);
        }
    }

    private void FinalizeCompletedLoads()
    {
        for (
            int index = inFlight.Count - 1;
            index >= 0;
            index--
        )
        {
            Request request = inFlight[index];

            if (!request.Handle.IsDone)
            {
                continue;
            }

            inFlight.RemoveAt(index);
            requestsByKey.Remove(request.Key);

            bool succeeded =
                request.Handle.Status ==
                    AsyncOperationStatus.Succeeded
                && request.Handle.Result != null
                && ValidateTexture(
                    request.Owner,
                    request.Handle.Result
                );

            if (!succeeded)
            {
                if (request.Handle.IsValid())
                {
                    Addressables.Release(request.Handle);
                }

                MarkRequiredFailure(request);
                continue;
            }

            TerrainHeightResidentPage page =
                new TerrainHeightResidentPage
                {
                    Coordinate = request.Key.Coordinate,
                    SampleStride = request.Key.SampleStride,
                    Address = request.Address,
                    Handle = request.Handle,
                    Texture = request.Handle.Result,
                    LastRequestGeneration = request.Generation,
                    State = TerrainHeightResidentPageState.Loaded
                };

            if (
                request.Owner.ResidentPages.TryGetValue(
                    page.Coordinate,
                    out TerrainHeightResidentPage previous
                )
                && previous != null
                && previous.Handle.IsValid()
            )
            {
                Addressables.Release(previous.Handle);
            }

            request.Owner.ResidentPages[
                page.Coordinate
            ] = page;
        }
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

    private bool HasInFlightKey(
        TerrainHeightPageKey key
    )
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

    private void MarkRequiredFailure(
        Request request
    )
    {
        if (request == null || !request.Required)
        {
            return;
        }

        if (request.RequiredGenerations.Count == 0)
        {
            failedRequiredGenerations.Add(
                request.Generation
            );

            return;
        }

        foreach (
            int generation
            in request.RequiredGenerations
        )
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

        TerrainHeightStreamingLevelDescriptor descriptor =
            owner.Descriptor;

        return
            texture.width == descriptor.SamplesPerSide
            && texture.height == descriptor.SamplesPerSide
            && texture.format == descriptor.TextureFormat;
    }

    private static int CompareRequests(
        Request left,
        Request right
    )
    {
        if (left.Required != right.Required)
        {
            return left.Required ? -1 : 1;
        }

        int levelComparison =
            left.LodLevel.CompareTo(
                right.LodLevel
            );

        if (levelComparison != 0)
        {
            return levelComparison;
        }

        int distanceComparison =
            left.DistancePriority.CompareTo(
                right.DistancePriority
            );

        if (distanceComparison != 0)
        {
            return distanceComparison;
        }

        return
            left.Sequence.CompareTo(
                right.Sequence
            );
    }
}
