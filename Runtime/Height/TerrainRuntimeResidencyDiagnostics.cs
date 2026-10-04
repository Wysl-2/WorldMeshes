using UnityEngine;

public readonly struct TerrainSurfaceResidencyDiagnosticsSnapshot
{
    public bool CacheReady { get; }
    public TerrainSurfaceCacheTransitionState TransitionState { get; }
    public int CacheWidth { get; }
    public int CacheHeight { get; }
    public int SamplesPerSide { get; }
    public int ResidentSourceCount { get; }
    public long EstimatedActiveGpuBytes { get; }
    public long EstimatedStagingGpuBytes { get; }
    public long EstimatedCurrentSourceBytes { get; }
    public long EstimatedSourceUpperBoundBytes { get; }

    public long EstimatedGpuCacheBytes =>
        EstimatedActiveGpuBytes +
        EstimatedStagingGpuBytes;

    public long EstimatedCurrentLogicalBytes =>
        EstimatedGpuCacheBytes +
        EstimatedCurrentSourceBytes;

    public long EstimatedConservativeUpperBoundBytes =>
        EstimatedGpuCacheBytes +
        EstimatedSourceUpperBoundBytes;

    internal TerrainSurfaceResidencyDiagnosticsSnapshot(
        bool cacheReady,
        TerrainSurfaceCacheTransitionState transitionState,
        int cacheWidth,
        int cacheHeight,
        int samplesPerSide,
        int residentSourceCount,
        long estimatedActiveGpuBytes,
        long estimatedStagingGpuBytes,
        long estimatedCurrentSourceBytes,
        long estimatedSourceUpperBoundBytes
    )
    {
        CacheReady = cacheReady;
        TransitionState = transitionState;
        CacheWidth = cacheWidth;
        CacheHeight = cacheHeight;
        SamplesPerSide = samplesPerSide;
        ResidentSourceCount = residentSourceCount;
        EstimatedActiveGpuBytes = estimatedActiveGpuBytes;
        EstimatedStagingGpuBytes = estimatedStagingGpuBytes;
        EstimatedCurrentSourceBytes = estimatedCurrentSourceBytes;
        EstimatedSourceUpperBoundBytes = estimatedSourceUpperBoundBytes;
    }
}

public readonly struct TerrainHeightLegacyResidencyEstimate
{
    public int CacheWidth { get; }
    public int CacheHeight { get; }
    public long NativePageBytes { get; }
    public long EstimatedActiveGpuBytes { get; }
    public long EstimatedStagingGpuBytes { get; }
    public long EstimatedSteadySourceBytes { get; }
    public long EstimatedTransitionSourceUpperBoundBytes { get; }

    public long EstimatedGpuCacheBytes =>
        EstimatedActiveGpuBytes +
        EstimatedStagingGpuBytes;

    public long EstimatedSteadyLogicalBytes =>
        EstimatedGpuCacheBytes +
        EstimatedSteadySourceBytes;

    public long EstimatedConservativeUpperBoundBytes =>
        EstimatedGpuCacheBytes +
        EstimatedTransitionSourceUpperBoundBytes;

    internal TerrainHeightLegacyResidencyEstimate(
        int cacheWidth,
        int cacheHeight,
        long nativePageBytes,
        long estimatedActiveGpuBytes,
        long estimatedStagingGpuBytes,
        long estimatedSteadySourceBytes,
        long estimatedTransitionSourceUpperBoundBytes
    )
    {
        CacheWidth = cacheWidth;
        CacheHeight = cacheHeight;
        NativePageBytes = nativePageBytes;
        EstimatedActiveGpuBytes = estimatedActiveGpuBytes;
        EstimatedStagingGpuBytes = estimatedStagingGpuBytes;
        EstimatedSteadySourceBytes = estimatedSteadySourceBytes;
        EstimatedTransitionSourceUpperBoundBytes =
            estimatedTransitionSourceUpperBoundBytes;
    }
}

public readonly struct TerrainRuntimeResidencyDiagnosticsSnapshot
{
    public int HeightLodCount { get; }
    public long HeightActiveGpuBytes { get; }
    public long HeightStagingGpuBytes { get; }
    public long HeightCurrentSourceBytes { get; }
    public long HeightObservedPeakSourceBytes { get; }
    public long HeightSourceUpperBoundBytes { get; }
    public TerrainSurfaceResidencyDiagnosticsSnapshot Surface { get; }
    public TerrainHeightLegacyResidencyEstimate LegacyHeight { get; }

    public long HeightGpuCacheBytes =>
        HeightActiveGpuBytes +
        HeightStagingGpuBytes;

    public long HeightCurrentLogicalBytes =>
        HeightGpuCacheBytes +
        HeightCurrentSourceBytes;

    public long HeightObservedPeakLogicalBytes =>
        HeightGpuCacheBytes +
        System.Math.Max(
            HeightCurrentSourceBytes,
            HeightObservedPeakSourceBytes
        );

    public long HeightConservativeUpperBoundBytes =>
        HeightGpuCacheBytes +
        HeightSourceUpperBoundBytes;

    public long CurrentTerrainLogicalBytes =>
        HeightCurrentLogicalBytes +
        Surface.EstimatedCurrentLogicalBytes;

    public long ConservativeTerrainUpperBoundBytes =>
        HeightConservativeUpperBoundBytes +
        Surface.EstimatedConservativeUpperBoundBytes;

    internal TerrainRuntimeResidencyDiagnosticsSnapshot(
        int heightLodCount,
        long heightActiveGpuBytes,
        long heightStagingGpuBytes,
        long heightCurrentSourceBytes,
        long heightObservedPeakSourceBytes,
        long heightSourceUpperBoundBytes,
        TerrainSurfaceResidencyDiagnosticsSnapshot surface,
        TerrainHeightLegacyResidencyEstimate legacyHeight
    )
    {
        HeightLodCount = heightLodCount;
        HeightActiveGpuBytes = heightActiveGpuBytes;
        HeightStagingGpuBytes = heightStagingGpuBytes;
        HeightCurrentSourceBytes = heightCurrentSourceBytes;
        HeightObservedPeakSourceBytes = heightObservedPeakSourceBytes;
        HeightSourceUpperBoundBytes = heightSourceUpperBoundBytes;
        Surface = surface;
        LegacyHeight = legacyHeight;
    }
}

public partial class TerrainHeightmapStreamer
{
    public bool TryGetRuntimeResidencyDiagnostics(
        out TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        out string reason
    )
    {
        snapshot = default;
        reason = null;

        if (!initialized)
        {
            reason =
                "The terrain heightmap streamer has not initialized.";

            return false;
        }

        if (heightmapManifest == null)
        {
            reason =
                "The runtime Height manifest is unavailable.";

            return false;
        }

        if (surfaceMaskManifest == null)
        {
            reason =
                "The runtime Surface manifest is unavailable.";

            return false;
        }

        if (
            heightLodStates == null
            ||
            heightLodStates.Length == 0
        )
        {
            reason =
                "No multiresolution Height LOD states are available.";

            return false;
        }

        if (
            surfaceLodStates == null
            ||
            surfaceLodStates.Length == 0
        )
        {
            reason =
                "No multiresolution Surface LOD states are available.";

            return false;
        }

        if (
            heightmapManifest.heightTileSamplesPerSide <= 0
            ||
            heightmapManifest.heightTileWorldSize <= 0f
            ||
            heightmapManifest.heightTileGridWidth <= 0
            ||
            heightmapManifest.heightTileGridHeight <= 0
        )
        {
            reason =
                "The runtime Height manifest has invalid residency geometry.";

            return false;
        }

        if (
            surfaceMaskManifest.tileGridWidth <= 0
            ||
            surfaceMaskManifest.tileGridHeight <= 0
            ||
            surfaceMaskManifest.tileWorldSize <= 0f
        )
        {
            reason =
                "The runtime Surface manifest has invalid residency geometry.";

            return false;
        }

        long heightActiveGpuBytes =
            0L;

        long heightStagingGpuBytes =
            0L;

        for (
            int level = 0;
            level < heightLodStates.Length;
            level++
        )
        {
            if (
                !TryGetHeightLodDiagnostics(
                    level,
                    out TerrainHeightLodDiagnosticsSnapshot lod
                )
            )
            {
                reason =
                    $"Height LOD{level} diagnostics are unavailable.";

                return false;
            }

            heightActiveGpuBytes +=
                lod.EstimatedActiveGpuBytes;

            heightStagingGpuBytes +=
                lod.EstimatedStagingGpuBytes;
        }

        if (
            !TryGetHeightSchedulerDiagnostics(
                out TerrainHeightSchedulerDiagnosticsSnapshot scheduler
            )
        )
        {
            reason =
                "Height scheduler diagnostics are unavailable.";

            return false;
        }

        if (surfacePageLoadScheduler == null)
        {
            reason =
                "Surface scheduler diagnostics are unavailable.";

            return false;
        }

        long nativeHeightSamplesPerSide =
            Mathf.Max(
                0,
                heightmapManifest
                    .heightTileSamplesPerSide
            );

        long nativeHeightPageBytes =
            nativeHeightSamplesPerSide *
            nativeHeightSamplesPerSide *
            4L;

        long heightSourceUpperBoundBytes =
            nativeHeightPageBytes *
            Mathf.Max(
                1,
                scheduler.ConcurrencyLimit
            );

        long surfaceMaximumSourceBytes =
            0L;

        for (
            int level = 0;
            level < surfaceLodStates.Length;
            level++
        )
        {
            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            if (
                state == null
                ||
                state.Descriptor
                    .SamplesPerSide <= 0
                ||
                state.CacheWidth <= 0
                ||
                state.CacheHeight <= 0
            )
            {
                reason =
                    $"Surface LOD{level} has invalid residency geometry.";

                return false;
            }

            long samples =
                state.Descriptor
                    .SamplesPerSide;

            surfaceMaximumSourceBytes =
                System.Math.Max(
                    surfaceMaximumSourceBytes,
                    samples *
                        samples
                );
        }

        long surfaceActiveGpuBytes =
            EstimateSurfaceActiveGpuCacheBytes();

        long surfaceStagingGpuBytes =
            EstimateSurfaceStagingGpuCacheBytes();

        int residentSurfaceSourceCount =
            surfacePageLoadScheduler
                .TransientSourceSlotCount;

        long surfaceCurrentSourceBytes =
            surfacePageLoadScheduler
                .EstimatedLogicalSourceBytes;

        long surfaceSourceUpperBoundBytes =
            surfaceMaximumSourceBytes *
            Mathf.Max(
                1,
                MaxConcurrentSurfacePageLoads
            );

        TerrainSurfaceLodRuntimeState compatibilityState =
            surfaceLodStates[0];

        TerrainSurfaceResidencyDiagnosticsSnapshot surface =
            new TerrainSurfaceResidencyDiagnosticsSnapshot(
                SurfaceCacheReady,
                SurfaceTransitionState,
                compatibilityState.CacheWidth,
                compatibilityState.CacheHeight,
                compatibilityState
                    .Descriptor
                    .SamplesPerSide,
                residentSurfaceSourceCount,
                surfaceActiveGpuBytes,
                surfaceStagingGpuBytes,
                surfaceCurrentSourceBytes,
                surfaceSourceUpperBoundBytes
            );

        TerrainHeightLegacyResidencyEstimate legacyHeight =
            CalculateLegacyHeightResidencyEstimate(
                nativeHeightPageBytes
            );

        snapshot =
            new TerrainRuntimeResidencyDiagnosticsSnapshot(
                heightLodStates.Length,
                heightActiveGpuBytes,
                heightStagingGpuBytes,
                scheduler.EstimatedLogicalSourceBytes,
                scheduler.PeakEstimatedLogicalSourceBytes,
                heightSourceUpperBoundBytes,
                surface,
                legacyHeight
            );

        return true;
    }

    private TerrainHeightLegacyResidencyEstimate
        CalculateLegacyHeightResidencyEstimate(
            long nativeHeightPageBytes
        )
    {
        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                heightmapManifest
                    .heightTileWorldSize
            );

        int clipmapTileSpan =
            Mathf.CeilToInt(
                CalculateClipmapDiameter() /
                tileWorldSize
            );

        int requestedCacheSize =
            Mathf.Max(
                1,
                clipmapTileSpan +
                Mathf.Max(
                    0,
                    guardTileCount
                )
                *
                2
            );

        int cacheWidth =
            Mathf.Min(
                requestedCacheSize,
                Mathf.Max(
                    1,
                    heightmapManifest
                        .heightTileGridWidth
                )
            );

        int cacheHeight =
            Mathf.Min(
                requestedCacheSize,
                Mathf.Max(
                    1,
                    heightmapManifest
                        .heightTileGridHeight
                )
            );

        long cacheSliceCount =
            (long)cacheWidth *
            cacheHeight;

        long cacheBytes =
            nativeHeightPageBytes *
            cacheSliceCount;

        long totalNativeTileCount =
            (long)Mathf.Max(
                0,
                heightmapManifest
                    .heightTileGridWidth
            )
            *
            Mathf.Max(
                0,
                heightmapManifest
                    .heightTileGridHeight
            );

        long transitionSourceCount =
            System.Math.Min(
                totalNativeTileCount,
                cacheSliceCount * 2L
            );

        return
            new TerrainHeightLegacyResidencyEstimate(
                cacheWidth,
                cacheHeight,
                nativeHeightPageBytes,
                cacheBytes,
                cacheBytes,
                cacheBytes,
                nativeHeightPageBytes *
                    transitionSourceCount
            );
    }
}
