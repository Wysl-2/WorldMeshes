#ifndef WORLDMESHES_TERRAIN_SURFACE_MASK_SAMPLING_INCLUDED
#define WORLDMESHES_TERRAIN_SURFACE_MASK_SAMPLING_INCLUDED

/*
 * Stage 8 runtime surface-mask sampling.
 *
 * Surface tiles use the same absolute tile grid, native sample spacing, and
 * duplicated edge samples as the runtime height tiles.
 */

bool WorldMeshesTryResolveSurfaceMaskAddress(
    float2 worldXZ,
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
        _SurfaceMaskCacheReady <
            0.5
        ||
        _SurfaceMaskCacheSize.x <=
            0.0
        ||
        _SurfaceMaskCacheSize.y <=
            0.0
        ||
        _SurfaceMaskSamplesPerSide <=
            1.0
        ||
        _SurfaceMaskSampleSpacing <=
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
                _SurfaceMaskSampleSpacing
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
        (int)_SurfaceMaskSamplesPerSide;

    int tileIntervals =
        samplesPerSide -
        1;

    float tileWorldSize =
        tileIntervals *
        _SurfaceMaskSampleSpacing;

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
        _SurfaceMaskCacheOriginTile.xy;

    int2 cacheSize =
        (int2)
        _SurfaceMaskCacheSize.xy;

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
        _SurfaceMaskSampleSpacing;

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


bool WorldMeshesTrySampleBakedScreeSuitability(
    float2 worldXZ,
    out float suitability
)
{
    suitability =
        0.0;

    int sliceIndex;
    float2 sample;

    if (
        !WorldMeshesTryResolveSurfaceMaskAddress(
            worldXZ,
            sliceIndex,
            sample
        )
    )
    {
        return false;
    }

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

    suitability =
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

    return true;
}

#endif
