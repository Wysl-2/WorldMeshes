#ifndef WORLDMESHES_TERRAIN_SURFACE_MASK_SAMPLING_INCLUDED
#define WORLDMESHES_TERRAIN_SURFACE_MASK_SAMPLING_INCLUDED

/*
 * Runtime surface-mask sampling.
 *
 * Every Surface representation preserves the same absolute tile grid and
 * tile world footprint. Sample spacing and samples-per-side vary by stride.
 */

bool WorldMeshesTryResolveSurfaceMaskAddressForRepresentation(
    float2 worldXZ,
    float4 cacheOriginTileValue,
    float4 cacheSizeValue,
    float samplesPerSideValue,
    float sampleSpacingValue,
    out int sliceIndex,
    out float2 continuousSample
)
{
    sliceIndex =
        -1;

    continuousSample =
        float2(
            0.0,
            0.0
        );

    if (
        cacheSizeValue.x <=
            0.0
        ||
        cacheSizeValue.y <=
            0.0
        ||
        samplesPerSideValue <=
            1.0
        ||
        sampleSpacingValue <=
            0.0
        ||
        _WorldBoundsReady <
            0.5
    )
    {
        return false;
    }

    float2 worldSize =
        _WorldSizeXZ.xy;

    float tolerance =
        max(
            abs(
                sampleSpacingValue
            )
            *
            0.001,
            0.00001
        );

    if (
        worldXZ.x <
            -tolerance
        ||
        worldXZ.y <
            -tolerance
        ||
        worldXZ.x >
            worldSize.x +
            tolerance
        ||
        worldXZ.y >
            worldSize.y +
            tolerance
    )
    {
        return false;
    }

    float2 clampedWorldXZ =
        clamp(
            worldXZ,
            float2(
                0.0,
                0.0
            ),
            worldSize
        );

    int samplesPerSide =
        (int)samplesPerSideValue;

    int tileIntervals =
        samplesPerSide -
        1;

    float tileWorldSize =
        tileIntervals *
        sampleSpacingValue;

    int2 logicalTileCount =
        max(
            (int2)ceil(
                worldSize /
                max(
                    tileWorldSize,
                    0.000001
                )
            ),
            int2(
                1,
                1
            )
        );

    int2 tileCoordinate =
        (int2)floor(
            clampedWorldXZ /
            max(
                tileWorldSize,
                0.000001
            )
        );

    tileCoordinate =
        clamp(
            tileCoordinate,
            int2(
                0,
                0
            ),
            logicalTileCount -
                1
        );

    int2 cacheOrigin =
        (int2)
        cacheOriginTileValue.xy;

    int2 cacheSize =
        (int2)
        cacheSizeValue.xy;

    int2 localTile =
        tileCoordinate -
        cacheOrigin;

    if (
        localTile.x < 0
        ||
        localTile.y < 0
        ||
        localTile.x >=
            cacheSize.x
        ||
        localTile.y >=
            cacheSize.y
    )
    {
        return false;
    }

    sliceIndex =
        localTile.x +
        localTile.y *
        cacheSize.x;

    float2 tileOrigin =
        (float2)tileCoordinate *
        tileWorldSize;

    continuousSample =
        (
            clampedWorldXZ -
            tileOrigin
        )
        /
        sampleSpacingValue;

    continuousSample =
        clamp(
            continuousSample,
            float2(
                0.0,
                0.0
            ),
            float2(
                tileIntervals,
                tileIntervals
            )
        );

    return true;
}


bool WorldMeshesTryResolveSurfaceMaskAddress(
    float2 worldXZ,
    out int sliceIndex,
    out float2 continuousSample
)
{
    if (
        _SurfaceMaskCacheReady <
        0.5
    )
    {
        sliceIndex =
            -1;

        continuousSample =
            float2(
                0.0,
                0.0
            );

        return false;
    }

    return
        WorldMeshesTryResolveSurfaceMaskAddressForRepresentation(
            worldXZ,
            _SurfaceMaskCacheOriginTile,
            _SurfaceMaskCacheSize,
            _SurfaceMaskSamplesPerSide,
            _SurfaceMaskSampleSpacing,
            sliceIndex,
            continuousSample
        );
}


bool WorldMeshesTryResolveCoarseSurfaceMaskAddress(
    float2 worldXZ,
    out int sliceIndex,
    out float2 continuousSample
)
{
    if (
        _SurfaceMaskCacheReady <
            0.5
        ||
        _SurfaceMaskDualResolutionEnabled <
            0.5
    )
    {
        sliceIndex =
            -1;

        continuousSample =
            float2(
                0.0,
                0.0
            );

        return false;
    }

    return
        WorldMeshesTryResolveSurfaceMaskAddressForRepresentation(
            worldXZ,
            _SurfaceMaskCoarseCacheOriginTile,
            _SurfaceMaskCoarseCacheSize,
            _SurfaceMaskCoarseSamplesPerSide,
            _SurfaceMaskCoarseSampleSpacing,
            sliceIndex,
            continuousSample
        );
}


float WorldMeshesBlendSurfaceMaskSamples(
    float v00,
    float v10,
    float v01,
    float v11,
    float2 blend
)
{
    return
        lerp(
            lerp(
                v00,
                v10,
                blend.x
            ),
            lerp(
                v01,
                v11,
                blend.x
            ),
            blend.y
        );
}


float WorldMeshesSamplePrimarySurfaceMask(
    int sliceIndex,
    float2 sample
)
{
    int samplesPerSide =
        (int)_SurfaceMaskSamplesPerSide;

    int2 sample0 =
        (int2)floor(
            sample
        );

    int2 sample1 =
        min(
            sample0 +
                1,
            int2(
                samplesPerSide - 1,
                samplesPerSide - 1
            )
        );

    float2 blend =
        saturate(
            sample -
            (float2)sample0
        );

    float v00 =
        _SurfaceMaskCache.Load(
            int4(
                sample0.x,
                sample0.y,
                sliceIndex,
                0
            )
        );

    float v10 =
        _SurfaceMaskCache.Load(
            int4(
                sample1.x,
                sample0.y,
                sliceIndex,
                0
            )
        );

    float v01 =
        _SurfaceMaskCache.Load(
            int4(
                sample0.x,
                sample1.y,
                sliceIndex,
                0
            )
        );

    float v11 =
        _SurfaceMaskCache.Load(
            int4(
                sample1.x,
                sample1.y,
                sliceIndex,
                0
            )
        );

    return
        WorldMeshesBlendSurfaceMaskSamples(
            v00,
            v10,
            v01,
            v11,
            blend
        );
}


float WorldMeshesSampleCoarseSurfaceMask(
    int sliceIndex,
    float2 sample
)
{
    int samplesPerSide =
        (int)_SurfaceMaskCoarseSamplesPerSide;

    int2 sample0 =
        (int2)floor(
            sample
        );

    int2 sample1 =
        min(
            sample0 +
                1,
            int2(
                samplesPerSide - 1,
                samplesPerSide - 1
            )
        );

    float2 blend =
        saturate(
            sample -
            (float2)sample0
        );

    float v00 =
        _SurfaceMaskCoarseCache.Load(
            int4(
                sample0.x,
                sample0.y,
                sliceIndex,
                0
            )
        );

    float v10 =
        _SurfaceMaskCoarseCache.Load(
            int4(
                sample1.x,
                sample0.y,
                sliceIndex,
                0
            )
        );

    float v01 =
        _SurfaceMaskCoarseCache.Load(
            int4(
                sample0.x,
                sample1.y,
                sliceIndex,
                0
            )
        );

    float v11 =
        _SurfaceMaskCoarseCache.Load(
            int4(
                sample1.x,
                sample1.y,
                sliceIndex,
                0
            )
        );

    return
        WorldMeshesBlendSurfaceMaskSamples(
            v00,
            v10,
            v01,
            v11,
            blend
        );
}


bool WorldMeshesTrySampleBakedScreeSuitability(
    float2 worldXZ,
    float surfaceTransitionWeight,
    out float suitability
)
{
    suitability =
        0.0;

    int fineSliceIndex;
    float2 fineSample;

    if (
        !WorldMeshesTryResolveSurfaceMaskAddress(
            worldXZ,
            fineSliceIndex,
            fineSample
        )
    )
    {
        return false;
    }

    float fineSuitability =
        WorldMeshesSamplePrimarySurfaceMask(
            fineSliceIndex,
            fineSample
        );

    if (
        _SurfaceMaskDualResolutionEnabled <
            0.5
    )
    {
        suitability =
            fineSuitability;

        return true;
    }

    int coarseSliceIndex;
    float2 coarseSample;

    if (
        !WorldMeshesTryResolveCoarseSurfaceMaskAddress(
            worldXZ,
            coarseSliceIndex,
            coarseSample
        )
    )
    {
        return false;
    }

    float coarseSuitability =
        WorldMeshesSampleCoarseSurfaceMask(
            coarseSliceIndex,
            coarseSample
        );

    suitability =
        lerp(
            coarseSuitability,
            fineSuitability,
            saturate(
                surfaceTransitionWeight
            )
        );

    return true;
}


bool WorldMeshesTrySampleBakedScreeSuitability(
    float2 worldXZ,
    out float suitability
)
{
    return
        WorldMeshesTrySampleBakedScreeSuitability(
            worldXZ,
            1.0,
            suitability
        );
}

#endif
