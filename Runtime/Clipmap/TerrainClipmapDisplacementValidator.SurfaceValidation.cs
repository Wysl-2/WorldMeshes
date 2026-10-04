using System;
using UnityEngine;

public partial class TerrainClipmapDisplacementValidator
{
    private static readonly int
        SurfaceValidationCachePropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCache"
            );

    private static readonly int
        SurfaceValidationCacheOriginPropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCacheOriginTile"
            );

    private static readonly int
        SurfaceValidationCacheSizePropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCacheSize"
            );

    private static readonly int
        SurfaceValidationSamplesPerSidePropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskSamplesPerSide"
            );

    private static readonly int
        SurfaceValidationSampleSpacingPropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskSampleSpacing"
            );

    private static readonly int
        SurfaceValidationCoarseCachePropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCoarseCache"
            );

    private static readonly int
        SurfaceValidationCoarseCacheOriginPropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCoarseCacheOriginTile"
            );

    private static readonly int
        SurfaceValidationCoarseCacheSizePropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCoarseCacheSize"
            );

    private static readonly int
        SurfaceValidationCoarseSamplesPerSidePropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCoarseSamplesPerSide"
            );

    private static readonly int
        SurfaceValidationCoarseSampleSpacingPropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCoarseSampleSpacing"
            );

    private static readonly int
        SurfaceValidationDualResolutionPropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskDualResolutionEnabled"
            );

    private static readonly int
        SurfaceValidationReadyPropertyId =
            Shader.PropertyToID(
                "_SurfaceMaskCacheReady"
            );

    private bool TryValidateSurfaceRendererBinding(
        TerrainClipmapRendererBinding binding,
        MaterialPropertyBlock block,
        ref int primaryBindingCount,
        ref int dualResolutionStitchCount,
        out string error
    )
    {
        error = null;

        TerrainClipmapRendererRole role =
            binding.Role;

        int primaryLevel;

        switch (role.Kind)
        {
            case TerrainClipmapRendererKind.Center:
            case TerrainClipmapRendererKind.Ring:
                primaryLevel =
                    role.Level;
                break;

            case TerrainClipmapRendererKind.Stitch:
                primaryLevel =
                    role.FineLevel;
                break;

            default:
                error =
                    $"Renderer '{binding.Renderer.name}' has an unsupported Surface renderer role.";
                return false;
        }

        if (
            !TryGetSurfaceInspectionSnapshotForValidation(
                primaryLevel,
                out TerrainSurfaceLodInspectionSnapshot primary
            )
        )
        {
            error =
                $"Renderer '{binding.Renderer.name}' has no active Surface inspection state for LOD{primaryLevel}.";
            return false;
        }

        Texture primaryTexture =
            block.GetTexture(
                SurfaceValidationCachePropertyId
            );

        if (
            !(primaryTexture is Texture2DArray primaryCache)
            ||
            primaryCache == null
            ||
            primaryCache != primary.ActiveCache
        )
        {
            error =
                $"Renderer '{binding.Renderer.name}' does not reference primary Surface LOD{primaryLevel} cache.";
            return false;
        }

        Vector4 primaryOrigin =
            block.GetVector(
                SurfaceValidationCacheOriginPropertyId
            );

        Vector4 primarySize =
            block.GetVector(
                SurfaceValidationCacheSizePropertyId
            );

        float primarySamples =
            block.GetFloat(
                SurfaceValidationSamplesPerSidePropertyId
            );

        float primarySpacing =
            block.GetFloat(
                SurfaceValidationSampleSpacingPropertyId
            );

        float ready =
            block.GetFloat(
                SurfaceValidationReadyPropertyId
            );

        if (
            Mathf.Abs(
                primaryOrigin.x -
                primary.ActiveCacheOrigin.x
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                primaryOrigin.y -
                primary.ActiveCacheOrigin.y
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                primarySize.x -
                primary.CacheWidth
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                primarySize.y -
                primary.CacheHeight
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                primarySamples -
                primary.Descriptor.SamplesPerSide
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                primarySpacing -
                primary.Descriptor.SampleSpacing
            ) > multiresolutionBindingTolerance
            ||
            ready < 0.5f
        )
        {
            error =
                $"Renderer '{binding.Renderer.name}' has Surface cache metadata that does not match primary LOD{primaryLevel}.";
            return false;
        }

        primaryBindingCount++;

        float dualResolution =
            block.GetFloat(
                SurfaceValidationDualResolutionPropertyId
            );

        if (
            role.Kind !=
                TerrainClipmapRendererKind.Stitch
        )
        {
            if (
                dualResolution >= 0.5f
            )
            {
                error =
                    $"Renderer '{binding.Renderer.name}' enables dual-resolution Surface sampling outside a stitch.";
                return false;
            }

            return true;
        }

        if (
            role.CoarseLevel !=
                role.FineLevel + 1
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' has invalid fine/coarse Surface ownership.";
            return false;
        }

        if (
            !TryGetSurfaceInspectionSnapshotForValidation(
                role.CoarseLevel,
                out TerrainSurfaceLodInspectionSnapshot coarse
            )
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' has no active coarse Surface inspection state for LOD{role.CoarseLevel}.";
            return false;
        }

        Texture coarseTexture =
            block.GetTexture(
                SurfaceValidationCoarseCachePropertyId
            );

        if (
            !(coarseTexture is Texture2DArray coarseCache)
            ||
            coarseCache == null
            ||
            coarseCache != coarse.ActiveCache
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' does not reference coarse Surface LOD{role.CoarseLevel} cache.";
            return false;
        }

        Vector4 coarseOrigin =
            block.GetVector(
                SurfaceValidationCoarseCacheOriginPropertyId
            );

        Vector4 coarseSize =
            block.GetVector(
                SurfaceValidationCoarseCacheSizePropertyId
            );

        float coarseSamples =
            block.GetFloat(
                SurfaceValidationCoarseSamplesPerSidePropertyId
            );

        float coarseSpacing =
            block.GetFloat(
                SurfaceValidationCoarseSampleSpacingPropertyId
            );

        if (
            dualResolution < 0.5f
            ||
            Mathf.Abs(
                coarseOrigin.x -
                coarse.ActiveCacheOrigin.x
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                coarseOrigin.y -
                coarse.ActiveCacheOrigin.y
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                coarseSize.x -
                coarse.CacheWidth
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                coarseSize.y -
                coarse.CacheHeight
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                coarseSamples -
                coarse.Descriptor.SamplesPerSide
            ) > multiresolutionBindingTolerance
            ||
            Mathf.Abs(
                coarseSpacing -
                coarse.Descriptor.SampleSpacing
            ) > multiresolutionBindingTolerance
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' coarse Surface cache metadata does not match LOD{role.CoarseLevel}.";
            return false;
        }

        dualResolutionStitchCount++;
        return true;
    }

    private bool TryGetSurfaceInspectionSnapshotForValidation(
        int level,
        out TerrainSurfaceLodInspectionSnapshot snapshot
    )
    {
        snapshot = default;

        if (
            streamer == null
            ||
            !streamer.TryBeginMultiresolutionCacheInspection(
                out _
            )
        )
        {
            return false;
        }

        try
        {
            return
                streamer.TryGetSurfaceLodInspectionSnapshot(
                    level,
                    out snapshot
                );
        }
        finally
        {
            streamer.EndCacheInspection();
        }
    }

    private bool TryValidateSurfaceRequiredCoverage(
        out int requiredPageCount,
        out string error
    )
    {
        requiredPageCount = 0;
        error = null;

        string inspectionReason =
            null;

        if (
            streamer == null
            ||
            !streamer.TryBeginMultiresolutionCacheInspection(
                out inspectionReason
            )
        )
        {
            error =
                string.IsNullOrEmpty(
                    inspectionReason
                )
                    ? "Multiresolution Surface cache inspection is unavailable."
                    : inspectionReason;

            return false;
        }

        try
        {
            for (
                int level = 0;
                level <
                    streamer.SurfaceLodRuntimeStateCount;
                level++
            )
            {
                if (
                    !streamer.TryGetSurfaceLodInspectionSnapshot(
                        level,
                        out TerrainSurfaceLodInspectionSnapshot snapshot
                    )
                )
                {
                    error =
                        $"Surface LOD{level} inspection state is unavailable.";
                    return false;
                }

                if (
                    snapshot.ActiveCache == null
                    ||
                    !snapshot.CacheReady
                )
                {
                    error =
                        $"Surface LOD{level} has no ready active cache.";
                    return false;
                }

                if (
                    snapshot.ActiveCache.width !=
                        snapshot.Descriptor.SamplesPerSide
                    ||
                    snapshot.ActiveCache.height !=
                        snapshot.Descriptor.SamplesPerSide
                    ||
                    snapshot.ActiveCache.depth <
                        snapshot.CacheWidth *
                        snapshot.CacheHeight
                )
                {
                    error =
                        $"Surface LOD{level} active cache dimensions do not match its runtime descriptor.";
                    return false;
                }

                TerrainHeightPageRect required =
                    snapshot.ActiveRequiredPages;

                if (!required.IsValid)
                {
                    continue;
                }

                for (
                    int y = required.Minimum.y;
                    y <= required.Maximum.y;
                    y++
                )
                {
                    for (
                        int x = required.Minimum.x;
                        x <= required.Maximum.x;
                        x++
                    )
                    {
                        Vector2Int coordinate =
                            new Vector2Int(
                                x,
                                y
                            );

                        if (
                            !streamer.TryGetSurfaceLodCacheSliceForInspection(
                                level,
                                coordinate,
                                out int cacheSlice,
                                out bool activeValid,
                                out bool isRequired
                            )
                            ||
                            !activeValid
                            ||
                            !isRequired
                            ||
                            cacheSlice < 0
                            ||
                            cacheSlice >=
                                snapshot.ActiveCache.depth
                        )
                        {
                            error =
                                $"Surface LOD{level} required page {coordinate} is not valid in the active cache.";
                            return false;
                        }

                        requiredPageCount++;
                    }
                }
            }

            return true;
        }
        finally
        {
            streamer.EndCacheInspection();
        }
    }

    private bool TryValidateFocusedTerrainSchedulerBounds(
        out string error
    )
    {
        error = null;

        if (
            !streamer.TryGetHeightSchedulerDiagnostics(
                out TerrainHeightSchedulerDiagnosticsSnapshot height
            )
        )
        {
            error =
                "Height scheduler diagnostics are unavailable during terrain scheduler stress.";
            return false;
        }

        if (
            !streamer.TryGetSurfaceSchedulerDiagnostics(
                out TerrainSurfaceSchedulerDiagnosticsSnapshot surface
            )
        )
        {
            error =
                "Surface scheduler diagnostics are unavailable during terrain scheduler stress.";
            return false;
        }

        if (
            !streamer.TryGetRuntimeResidencyDiagnostics(
                out TerrainRuntimeResidencyDiagnosticsSnapshot residency,
                out string reason
            )
        )
        {
            error =
                string.IsNullOrEmpty(
                    reason
                )
                    ? "Runtime residency diagnostics are unavailable during terrain scheduler stress."
                    : reason;

            return false;
        }

        if (
            height.ActiveLoadCount >
                height.ConcurrencyLimit
            ||
            height.TransientSourceSlotCount >
                height.ConcurrencyLimit
            ||
            height.PeakActiveLoadCount >
                height.ConcurrencyLimit
            ||
            height.PeakTransientSourceCount >
                height.ConcurrencyLimit
        )
        {
            error =
                "Height scheduler exceeded its configured concurrency bound.";
            return false;
        }

        if (
            height.EstimatedLogicalSourceBytes >
                residency.HeightSourceUpperBoundBytes
            ||
            height.PeakEstimatedLogicalSourceBytes >
                residency.HeightSourceUpperBoundBytes
        )
        {
            error =
                "Height scheduler source residency exceeded its configured runtime bound.";
            return false;
        }

        if (
            height.PriorityViolationCount != 0
            ||
            height.DuplicateStartViolationCount != 0
        )
        {
            error =
                "Height scheduler priority or duplicate-start invariants failed.";
            return false;
        }

        if (
            surface.ActiveLoadCount >
                surface.ConcurrencyLimit
            ||
            surface.TransientSourceSlotCount >
                surface.ConcurrencyLimit
            ||
            surface.PeakActiveLoadCount >
                surface.ConcurrencyLimit
            ||
            surface.PeakTransientSourceCount >
                surface.ConcurrencyLimit
        )
        {
            error =
                "Surface scheduler exceeded its configured concurrency bound.";
            return false;
        }

        if (
            surface.EstimatedLogicalSourceBytes >
                residency.Surface
                    .EstimatedSourceUpperBoundBytes
            ||
            surface.PeakEstimatedLogicalSourceBytes >
                residency.Surface
                    .EstimatedSourceUpperBoundBytes
        )
        {
            error =
                "Surface scheduler source residency exceeded its configured runtime bound.";
            return false;
        }

        if (
            surface.PriorityViolationCount != 0
            ||
            surface.DuplicateStartViolationCount != 0
        )
        {
            error =
                "Surface scheduler priority or duplicate-start invariants failed.";
            return false;
        }

        return true;
    }

    private static string BuildTerrainSchedulerStressSummary(
        TerrainHeightSchedulerDiagnosticsSnapshot height,
        TerrainSurfaceSchedulerDiagnosticsSnapshot surface
    )
    {
        return
            "Height Scheduler\n" +
            $"  Peak Active Loads: {height.PeakActiveLoadCount}/{height.ConcurrencyLimit}\n" +
            $"  Peak Transient Sources: {height.PeakTransientSourceCount}/{height.ConcurrencyLimit}\n" +
            $"  Peak Source Estimate: {height.PeakEstimatedLogicalSourceBytes:N0} bytes\n" +
            $"  Requests Started: {height.RequestsStarted}\n" +
            $"  Source Uploads: {height.SourceUploadCount}\n" +
            $"  Cache-to-Cache Reuses: {height.CacheToCacheReuseCount}\n" +
            $"  Repeated Plan Skips: {height.RepeatedPlanSubmissionSkipCount}\n" +
            $"  Coalesced Requests: {height.CoalescedRequestCount}\n" +
            $"  Prefetch Promotions: {height.PrefetchPromotedToRequiredCount}\n" +
            $"  Stale Queued Discards: {height.StaleQueuedRequestDiscardCount}\n" +
            $"  Stale Completed Discards: {height.StaleCompletedSourceDiscardCount}\n" +
            $"  Priority Violations: {height.PriorityViolationCount}\n" +
            $"  Duplicate Starts: {height.DuplicateStartViolationCount}\n\n" +
            "Surface Scheduler\n" +
            $"  Peak Active Loads: {surface.PeakActiveLoadCount}/{surface.ConcurrencyLimit}\n" +
            $"  Peak Transient Sources: {surface.PeakTransientSourceCount}/{surface.ConcurrencyLimit}\n" +
            $"  Peak Source Estimate: {surface.PeakEstimatedLogicalSourceBytes:N0} bytes\n" +
            $"  Requests Started: {surface.RequestsStarted}\n" +
            $"  Source Uploads: {surface.SourceUploadCount}\n" +
            $"  Cache-to-Cache Reuses: {surface.CacheToCacheReuseCount}\n" +
            $"  Coalesced Requests: {surface.CoalescedRequestCount}\n" +
            $"  Prefetch Promotions: {surface.PrefetchPromotedToRequiredCount}\n" +
            $"  Stale Queued Discards: {surface.StaleQueuedRequestDiscardCount}\n" +
            $"  Stale Completed Discards: {surface.StaleCompletedSourceDiscardCount}\n" +
            $"  Priority Violations: {surface.PriorityViolationCount}\n" +
            $"  Duplicate Starts: {surface.DuplicateStartViolationCount}";
    }

    private static bool TerrainSchedulersRecordedWork(
        TerrainHeightSchedulerDiagnosticsSnapshot height,
        TerrainSurfaceSchedulerDiagnosticsSnapshot surface
    )
    {
        return
            height.RequestsStarted > 0
            ||
            height.SourceUploadCount > 0
            ||
            height.CacheToCacheReuseCount > 0
            ||
            surface.RequestsStarted > 0
            ||
            surface.SourceUploadCount > 0
            ||
            surface.CacheToCacheReuseCount > 0;
    }

    private bool TryValidateSurfaceRuntimeStressBounds(
        TerrainRuntimeStreamingStressResult result,
        out string error
    )
    {
        error = null;

        if (
            !streamer.TryGetSurfaceSchedulerDiagnostics(
                out TerrainSurfaceSchedulerDiagnosticsSnapshot scheduler
            )
        )
        {
            error =
                "Surface scheduler diagnostics are unavailable during runtime stress.";
            return false;
        }

        ObserveSurfaceSchedulerDiagnostics(
            result,
            scheduler
        );

        if (
            scheduler.ActiveLoadCount >
                scheduler.ConcurrencyLimit
            ||
            scheduler.TransientSourceSlotCount >
                scheduler.ConcurrencyLimit
            ||
            scheduler.PeakActiveLoadCount >
                scheduler.ConcurrencyLimit
            ||
            scheduler.PeakTransientSourceCount >
                scheduler.ConcurrencyLimit
        )
        {
            error =
                "Surface scheduler exceeded its configured concurrency bound.";
            return false;
        }

        if (
            scheduler.EstimatedLogicalSourceBytes >
                runtimeStressSurfaceSourceUpperBoundBytes
            ||
            scheduler.PeakEstimatedLogicalSourceBytes >
                runtimeStressSurfaceSourceUpperBoundBytes
        )
        {
            error =
                "Surface logical source residency exceeded the configured runtime residency bound.";
            return false;
        }

        if (
            scheduler.PriorityViolationCount != 0
            ||
            scheduler.DuplicateStartViolationCount != 0
        )
        {
            error =
                "Surface scheduler priority or duplicate-start invariants failed.";
            return false;
        }

        return true;
    }

    private static bool SurfaceSchedulerShowsReacquisition(
        TerrainSurfaceSchedulerDiagnosticsSnapshot scheduler
    )
    {
        return
            scheduler.RequestsStarted > 0
            ||
            scheduler.SourceUploadCount > 0
            ||
            scheduler.CacheToCacheReuseCount > 0
            ||
            scheduler.ActiveLoadCount > 0
            ||
            scheduler.QueuedRequiredCount > 0
            ||
            scheduler.QueuedPrefetchCount > 0;
    }

    private void ObserveSurfaceSchedulerDiagnostics(
        TerrainRuntimeStreamingStressResult result,
        TerrainSurfaceSchedulerDiagnosticsSnapshot scheduler
    )
    {
        result.PeakActiveSurfaceLoads =
            Math.Max(
                result.PeakActiveSurfaceLoads,
                Math.Max(
                    scheduler.ActiveLoadCount,
                    scheduler.PeakActiveLoadCount
                )
            );

        result.PeakSurfaceTransientSources =
            Math.Max(
                result.PeakSurfaceTransientSources,
                Math.Max(
                    scheduler.TransientSourceSlotCount,
                    scheduler.PeakTransientSourceCount
                )
            );

        result.PeakSurfaceSourceBytes =
            Math.Max(
                result.PeakSurfaceSourceBytes,
                Math.Max(
                    scheduler.EstimatedLogicalSourceBytes,
                    scheduler.PeakEstimatedLogicalSourceBytes
                )
            );
    }

    private void CaptureSurfaceSchedulerLifetime(
        TerrainRuntimeStreamingStressResult result,
        TerrainSurfaceSchedulerDiagnosticsSnapshot scheduler
    )
    {
        ObserveSurfaceSchedulerDiagnostics(
            result,
            scheduler
        );

        result.SurfaceRequestsStarted +=
            scheduler.RequestsStarted;

        result.SurfaceSourceUploads +=
            scheduler.SourceUploadCount;

        result.SurfaceCacheToCacheReuses +=
            scheduler.CacheToCacheReuseCount;

        result.SurfaceCoalescedRequests +=
            scheduler.CoalescedRequestCount;

        result.SurfacePrefetchPromotions +=
            scheduler.PrefetchPromotedToRequiredCount;

        result.SurfaceStaleQueuedDiscards +=
            scheduler.StaleQueuedRequestDiscardCount;

        result.SurfaceStaleCompletedDiscards +=
            scheduler.StaleCompletedSourceDiscardCount;

        result.SurfacePriorityViolations +=
            scheduler.PriorityViolationCount;

        result.SurfaceDuplicateStartViolations +=
            scheduler.DuplicateStartViolationCount;
    }
}
