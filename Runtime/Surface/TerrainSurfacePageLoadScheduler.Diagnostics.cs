using UnityEngine;

internal sealed partial class TerrainSurfacePageLoadScheduler
{
    private int priorityViolationCount;
    private int duplicateStartViolationCount;

    public int QueuedRequiredCount
    {
        get
        {
            int count =
                0;

            for (
                int index = 0;
                index < queued.Count;
                index++
            )
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
        QueuedLoadCount -
        QueuedRequiredCount;

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

        for (
            int index = 0;
            index < queued.Count;
            index++
        )
        {
            TerrainSurfacePageSourceTransfer request =
                queued[index];

            if (
                request.Owner == null
                ||
                request.Owner.Level != level
            )
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

        for (
            int index = 0;
            index < inFlight.Count;
            index++
        )
        {
            TerrainSurfacePageSourceTransfer request =
                inFlight[index];

            if (
                request.Owner == null
                ||
                request.Owner.Level != level
            )
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

    internal TerrainSurfaceSchedulerDiagnosticsSnapshot
        GetDiagnosticsSnapshot()
    {
        int concurrencyLimit =
            Mathf.Max(
                1,
                MaxConcurrentLoads
            );

        return
            new TerrainSurfaceSchedulerDiagnosticsSnapshot(
                concurrencyLimit,
                Mathf.Clamp(
                    ReservedRequiredSlots,
                    0,
                    Mathf.Max(
                        0,
                        concurrencyLimit - 1
                    )
                ),
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
        peakActiveLoadCount =
            ActiveLoadCount;

        peakTransientSourceCount =
            TransientSourceSlotCount;

        peakQueuedRequestCount =
            queued.Count;

        peakEstimatedLogicalSourceBytes =
            EstimateLogicalSourceBytes();

        requestsStarted = 0L;
        coalescedRequestCount = 0L;
        sourceUploadCount = 0L;
        cacheToCacheReuseCount = 0L;
        prefetchPromotedToRequiredCount = 0L;
        staleQueuedRequestDiscardCount = 0L;
        staleCompletedSourceDiscardCount = 0L;
        priorityViolationCount = 0;
        duplicateStartViolationCount = 0;
    }

    private bool HasQueuedRequiredRequest()
    {
        for (
            int index = 0;
            index < queued.Count;
            index++
        )
        {
            if (queued[index].Required)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasInFlightKey(
        TerrainSurfacePageKey key
    )
    {
        for (
            int index = 0;
            index < inFlight.Count;
            index++
        )
        {
            if (
                inFlight[index]
                    .Key
                    .Equals(
                        key
                    )
            )
            {
                return true;
            }
        }

        return false;
    }
}
