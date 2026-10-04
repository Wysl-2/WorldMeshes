using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawMultiresolutionRuntimeStreamingState(
        TerrainHeightmapStreamer streamer
    )
    {
        GUILayout.Space(4f);

        GUILayout.Label(
            "Live Multiresolution Streaming State",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Height LOD States",
            streamer.HeightLodRuntimeStateCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Surface LOD States",
            streamer.SurfaceLodRuntimeStateCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Transition Running",
            streamer.MultiresolutionTransitionRunning
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Prepared Activation Pending",
            streamer.PreparedMultiresolutionActivationPending
                ? "Yes"
                : "No"
        );

        GUILayout.Space(5f);

        DrawHeightSchedulerRuntimeDiagnostics(
            streamer
        );

        GUILayout.Space(5f);

        DrawSurfaceSchedulerRuntimeDiagnostics(
            streamer
        );

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Surface Ready",
            streamer.SurfaceCacheReady
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Surface Transition State",
            streamer.SurfaceTransitionState.ToString()
        );

        EditorGUILayout.LabelField(
            "Surface Resident Valid Pages",
            streamer.ResidentSurfaceTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Surface GPU Estimate",
            FormatDiagnosticsBytes(
                streamer.EstimatedSurfaceGpuCacheBytes
            )
        );

        GUILayout.Space(5f);

        GUILayout.Label(
            "Height LODs",
            EditorStyles.boldLabel
        );

        for (
            int level = 0;
            level < streamer.HeightLodRuntimeStateCount;
            level++
        )
        {
            if (
                !streamer.TryGetHeightLodDiagnostics(
                    level,
                    out TerrainHeightLodDiagnosticsSnapshot lod
                )
            )
            {
                continue;
            }

            GUILayout.BeginVertical(
                EditorStyles.helpBox
            );

            GUILayout.Label(
                $"LOD{lod.Level}  |  Stride {lod.SampleStride}",
                EditorStyles.boldLabel
            );

            EditorGUILayout.LabelField(
                "Sample Spacing",
                lod.SampleSpacing.ToString("R")
            );

            EditorGUILayout.LabelField(
                "Page Resolution",
                $"{lod.SamplesPerSide} x {lod.SamplesPerSide}"
            );

            EditorGUILayout.LabelField(
                "Cache Origin",
                lod.CacheOrigin.ToString()
            );

            EditorGUILayout.LabelField(
                "Cache Size",
                $"{lod.CacheWidth} x {lod.CacheHeight}"
            );

            EditorGUILayout.LabelField(
                "Required Pages",
                FormatRuntimeStreamingPageRect(
                    lod.ActiveRequiredPages
                )
            );

            EditorGUILayout.LabelField(
                "Requested Prefetch",
                FormatRuntimeStreamingPageRect(
                    lod.RequestedPrefetchPages
                )
            );

            EditorGUILayout.LabelField(
                "Active / Staging Valid",
                $"{lod.ActiveValidPageCount} / {lod.StagingValidPageCount}"
            );

            EditorGUILayout.LabelField(
                "Queued Required / Prefetch",
                $"{lod.QueuedRequiredPageCount} / {lod.QueuedPrefetchPageCount}"
            );

            EditorGUILayout.LabelField(
                "In Flight Required / Prefetch",
                $"{lod.InFlightRequiredPageCount} / {lod.InFlightPrefetchPageCount}"
            );

            EditorGUILayout.LabelField(
                "Transition State",
                lod.TransitionState.ToString()
            );

            EditorGUILayout.LabelField(
                "Ready",
                lod.CacheReady
                    ? "Yes"
                    : "No"
            );

            EditorGUILayout.LabelField(
                "GPU Active / Staging",
                FormatDiagnosticsBytes(
                    lod.EstimatedActiveGpuBytes
                ) +
                " / " +
                FormatDiagnosticsBytes(
                    lod.EstimatedStagingGpuBytes
                )
            );

            GUILayout.EndVertical();
        }

        GUILayout.Space(5f);

        GUILayout.Label(
            "Surface LODs",
            EditorStyles.boldLabel
        );

        for (
            int level = 0;
            level < streamer.SurfaceLodRuntimeStateCount;
            level++
        )
        {
            if (
                !streamer.TryGetSurfaceLodDiagnostics(
                    level,
                    out TerrainSurfaceLodDiagnosticsSnapshot lod
                )
            )
            {
                continue;
            }

            GUILayout.BeginVertical(
                EditorStyles.helpBox
            );

            GUILayout.Label(
                $"LOD{lod.Level}  |  Stride {lod.SampleStride}",
                EditorStyles.boldLabel
            );

            EditorGUILayout.LabelField(
                "Sample Spacing",
                lod.SampleSpacing.ToString("R")
            );

            EditorGUILayout.LabelField(
                "Page Resolution",
                $"{lod.SamplesPerSide} x {lod.SamplesPerSide}"
            );

            EditorGUILayout.LabelField(
                "Active Cache Origin",
                lod.ActiveCacheOrigin.ToString()
            );

            EditorGUILayout.LabelField(
                "Requested Cache Origin",
                lod.RequestedCacheOrigin.ToString()
            );

            EditorGUILayout.LabelField(
                "Staging Cache Origin",
                lod.StagingCacheOrigin.ToString()
            );

            EditorGUILayout.LabelField(
                "Cache Size",
                $"{lod.CacheWidth} x {lod.CacheHeight}"
            );

            EditorGUILayout.LabelField(
                "Active Required Pages",
                FormatRuntimeStreamingPageRect(
                    lod.ActiveRequiredPages
                )
            );

            EditorGUILayout.LabelField(
                "Requested Required Pages",
                FormatRuntimeStreamingPageRect(
                    lod.RequestedRequiredPages
                )
            );

            EditorGUILayout.LabelField(
                "Requested Prefetch",
                FormatRuntimeStreamingPageRect(
                    lod.RequestedPrefetchPages
                )
            );

            EditorGUILayout.LabelField(
                "Active / Staging Valid",
                $"{lod.ActiveValidPageCount} / {lod.StagingValidPageCount}"
            );

            EditorGUILayout.LabelField(
                "Queued Required / Prefetch",
                $"{lod.QueuedRequiredPageCount} / {lod.QueuedPrefetchPageCount}"
            );

            EditorGUILayout.LabelField(
                "In Flight Required / Prefetch",
                $"{lod.InFlightRequiredPageCount} / {lod.InFlightPrefetchPageCount}"
            );

            EditorGUILayout.LabelField(
                "Transition State",
                lod.TransitionState.ToString()
            );

            EditorGUILayout.LabelField(
                "Ready",
                lod.CacheReady
                    ? "Yes"
                    : "No"
            );

            EditorGUILayout.LabelField(
                "GPU Active / Staging",
                FormatDiagnosticsBytes(
                    lod.EstimatedActiveGpuBytes
                ) +
                " / " +
                FormatDiagnosticsBytes(
                    lod.EstimatedStagingGpuBytes
                )
            );

            GUILayout.EndVertical();
        }
    }

    private void DrawHeightSchedulerRuntimeDiagnostics(
        TerrainHeightmapStreamer streamer
    )
    {
        GUILayout.Label(
            "Height Scheduler",
            EditorStyles.boldLabel
        );

        if (
            !streamer.TryGetHeightSchedulerDiagnostics(
                out TerrainHeightSchedulerDiagnosticsSnapshot scheduler
            )
        )
        {
            EditorGUILayout.LabelField(
                "Status",
                "Unavailable"
            );

            return;
        }

        DrawRuntimeSchedulerCommon(
            scheduler.ActiveLoadCount,
            scheduler.ConcurrencyLimit,
            scheduler.ReservedRequiredSlots,
            scheduler.TransientSourceSlotCount,
            scheduler.ReadySourceCount,
            scheduler.PendingGpuReleaseCount,
            scheduler.QueuedRequiredCount,
            scheduler.QueuedPrefetchCount,
            scheduler.PeakActiveLoadCount,
            scheduler.PeakTransientSourceCount,
            scheduler.PeakQueuedRequestCount,
            scheduler.EstimatedLogicalSourceBytes,
            scheduler.PeakEstimatedLogicalSourceBytes,
            scheduler.RequestsStarted,
            scheduler.SourceUploadCount,
            scheduler.CacheToCacheReuseCount,
            scheduler.CoalescedRequestCount,
            scheduler.PrefetchPromotedToRequiredCount,
            scheduler.StaleQueuedRequestDiscardCount,
            scheduler.StaleCompletedSourceDiscardCount,
            scheduler.PriorityViolationCount,
            scheduler.DuplicateStartViolationCount,
            scheduler.GraphicsFenceSupported
        );
    }

    private void DrawSurfaceSchedulerRuntimeDiagnostics(
        TerrainHeightmapStreamer streamer
    )
    {
        GUILayout.Label(
            "Surface Scheduler",
            EditorStyles.boldLabel
        );

        if (
            !streamer.TryGetSurfaceSchedulerDiagnostics(
                out TerrainSurfaceSchedulerDiagnosticsSnapshot scheduler
            )
        )
        {
            EditorGUILayout.LabelField(
                "Status",
                "Unavailable"
            );

            return;
        }

        DrawRuntimeSchedulerCommon(
            scheduler.ActiveLoadCount,
            scheduler.ConcurrencyLimit,
            scheduler.ReservedRequiredSlots,
            scheduler.TransientSourceSlotCount,
            scheduler.ReadySourceCount,
            scheduler.PendingGpuReleaseCount,
            scheduler.QueuedRequiredCount,
            scheduler.QueuedPrefetchCount,
            scheduler.PeakActiveLoadCount,
            scheduler.PeakTransientSourceCount,
            scheduler.PeakQueuedRequestCount,
            scheduler.EstimatedLogicalSourceBytes,
            scheduler.PeakEstimatedLogicalSourceBytes,
            scheduler.RequestsStarted,
            scheduler.SourceUploadCount,
            scheduler.CacheToCacheReuseCount,
            scheduler.CoalescedRequestCount,
            scheduler.PrefetchPromotedToRequiredCount,
            scheduler.StaleQueuedRequestDiscardCount,
            scheduler.StaleCompletedSourceDiscardCount,
            scheduler.PriorityViolationCount,
            scheduler.DuplicateStartViolationCount,
            scheduler.GraphicsFenceSupported
        );
    }

    private void DrawRuntimeSchedulerCommon(
        int activeLoadCount,
        int concurrencyLimit,
        int reservedRequiredSlots,
        int transientSourceSlotCount,
        int readySourceCount,
        int pendingGpuReleaseCount,
        int queuedRequiredCount,
        int queuedPrefetchCount,
        int peakActiveLoadCount,
        int peakTransientSourceCount,
        int peakQueuedRequestCount,
        long estimatedLogicalSourceBytes,
        long peakEstimatedLogicalSourceBytes,
        long requestsStarted,
        long sourceUploadCount,
        long cacheToCacheReuseCount,
        long coalescedRequestCount,
        long prefetchPromotedToRequiredCount,
        long staleQueuedRequestDiscardCount,
        long staleCompletedSourceDiscardCount,
        int priorityViolationCount,
        int duplicateStartViolationCount,
        bool graphicsFenceSupported
    )
    {
        EditorGUILayout.LabelField(
            "Active / Limit",
            $"{activeLoadCount} / {concurrencyLimit}"
        );

        EditorGUILayout.LabelField(
            "Reserved Required Slots",
            reservedRequiredSlots.ToString()
        );

        EditorGUILayout.LabelField(
            "Transient Sources / Limit",
            $"{transientSourceSlotCount} / {concurrencyLimit}"
        );

        EditorGUILayout.LabelField(
            "Ready / Pending Release",
            $"{readySourceCount} / {pendingGpuReleaseCount}"
        );

        EditorGUILayout.LabelField(
            "Required Queue",
            queuedRequiredCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Prefetch Queue",
            queuedPrefetchCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Peak Active",
            peakActiveLoadCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Peak Transient Sources",
            peakTransientSourceCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Peak Queued Requests",
            peakQueuedRequestCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Logical Source Estimate",
            FormatDiagnosticsBytes(
                estimatedLogicalSourceBytes
            )
        );

        EditorGUILayout.LabelField(
            "Peak Source Estimate",
            FormatDiagnosticsBytes(
                peakEstimatedLogicalSourceBytes
            )
        );

        EditorGUILayout.LabelField(
            "Requests Started",
            requestsStarted.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Source Uploads / Cache Reuses",
            $"{sourceUploadCount:N0} / {cacheToCacheReuseCount:N0}"
        );

        EditorGUILayout.LabelField(
            "Coalesced / Promoted",
            $"{coalescedRequestCount:N0} / {prefetchPromotedToRequiredCount:N0}"
        );

        EditorGUILayout.LabelField(
            "Stale Queue / Completed Discards",
            $"{staleQueuedRequestDiscardCount:N0} / {staleCompletedSourceDiscardCount:N0}"
        );

        EditorGUILayout.LabelField(
            "Priority Violations",
            priorityViolationCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Duplicate Starts",
            duplicateStartViolationCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Source Release Mode",
            graphicsFenceSupported
                ? "GPU Fence"
                : "Conservative Frame Delay"
        );
    }
}
