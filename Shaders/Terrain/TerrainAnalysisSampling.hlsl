#ifndef WORLDMESHES_TERRAIN_ANALYSIS_SAMPLING_INCLUDED
#define WORLDMESHES_TERRAIN_ANALYSIS_SAMPLING_INCLUDED

/*
 * Shared GPU sampling helpers for TerrainAnalysisLayer Texture2DArray data.
 *
 * Consumers provide the layer metadata bound to their shader:
 *
 *     int2   cacheOriginTile
 *     int2   cacheSize
 *     int    samplesPerSide
 *     float  sampleSpacing
 *     float2 worldSizeXZ
 *
 * This include performs authoritative world XZ -> tile -> slice -> sample
 * addressing and supports nearest or bilinear sampling.
 */

bool WorldMeshesTryResolveTerrainAnalysisAddress(
    float2 worldXZ,
    int2 cacheOriginTile,
    int2 cacheSize,
    int samplesPerSide,
    float sampleSpacing,
    float2 worldSizeXZ,
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
        cacheSize.x <= 0
        ||
        cacheSize.y <= 0
        ||
        samplesPerSide <= 1
        ||
        sampleSpacing <= 0.0
        ||
        worldSizeXZ.x <= 0.0
        ||
        worldSizeXZ.y <= 0.0
    )
    {
        return false;
    }

    float tolerance =
        max(
            abs(sampleSpacing) *
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
            worldSizeXZ.x +
            tolerance
        ||
        worldXZ.y >
            worldSizeXZ.y +
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
            worldSizeXZ
        );

    int tileIntervals =
        samplesPerSide -
        1;

    float tileWorldSize =
        tileIntervals *
        sampleSpacing;

    int2 logicalTileCount =
        max(
            (int2)ceil(
                worldSizeXZ /
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

    int2 cacheLocalTile =
        tileCoordinate -
        cacheOriginTile;

    if (
        cacheLocalTile.x < 0
        ||
        cacheLocalTile.y < 0
        ||
        cacheLocalTile.x >=
            cacheSize.x
        ||
        cacheLocalTile.y >=
            cacheSize.y
    )
    {
        return false;
    }

    sliceIndex =
        cacheLocalTile.x +
        cacheLocalTile.y *
        cacheSize.x;

    float2 tileWorldOriginXZ =
        (float2)tileCoordinate *
        tileWorldSize;

    continuousSample =
        (
            clampedWorldXZ -
            tileWorldOriginXZ
        )
        /
        sampleSpacing;

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


float WorldMeshesLoadTerrainAnalysisSample(
    Texture2DArray<float> analysisTexture,
    int2 sampleCoordinate,
    int sliceIndex
)
{
    return
        analysisTexture.Load(
            int4(
                sampleCoordinate.x,
                sampleCoordinate.y,
                sliceIndex,
                0
            )
        );
}


float WorldMeshesSampleTerrainAnalysisNearest(
    Texture2DArray<float> analysisTexture,
    float2 worldXZ,
    int2 cacheOriginTile,
    int2 cacheSize,
    int samplesPerSide,
    float sampleSpacing,
    float2 worldSizeXZ,
    out float valid
)
{
    valid =
        0.0;

    int sliceIndex;
    float2 continuousSample;

    if (
        !WorldMeshesTryResolveTerrainAnalysisAddress(
            worldXZ,
            cacheOriginTile,
            cacheSize,
            samplesPerSide,
            sampleSpacing,
            worldSizeXZ,
            sliceIndex,
            continuousSample
        )
    )
    {
        return 0.0;
    }

    int2 sampleCoordinate =
        (int2)floor(
            continuousSample +
            0.5
        );

    sampleCoordinate =
        clamp(
            sampleCoordinate,
            int2(
                0,
                0
            ),
            int2(
                samplesPerSide - 1,
                samplesPerSide - 1
            )
        );

    valid =
        1.0;

    return
        WorldMeshesLoadTerrainAnalysisSample(
            analysisTexture,
            sampleCoordinate,
            sliceIndex
        );
}


float WorldMeshesSampleTerrainAnalysisBilinear(
    Texture2DArray<float> analysisTexture,
    float2 worldXZ,
    int2 cacheOriginTile,
    int2 cacheSize,
    int samplesPerSide,
    float sampleSpacing,
    float2 worldSizeXZ,
    out float valid
)
{
    valid =
        0.0;

    int sliceIndex;
    float2 continuousSample;

    if (
        !WorldMeshesTryResolveTerrainAnalysisAddress(
            worldXZ,
            cacheOriginTile,
            cacheSize,
            samplesPerSide,
            sampleSpacing,
            worldSizeXZ,
            sliceIndex,
            continuousSample
        )
    )
    {
        return 0.0;
    }

    int2 sample0 =
        (int2)floor(
            continuousSample
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
            continuousSample -
            (float2)sample0
        );

    float value00 =
        WorldMeshesLoadTerrainAnalysisSample(
            analysisTexture,
            int2(
                sample0.x,
                sample0.y
            ),
            sliceIndex
        );

    float value10 =
        WorldMeshesLoadTerrainAnalysisSample(
            analysisTexture,
            int2(
                sample1.x,
                sample0.y
            ),
            sliceIndex
        );

    float value01 =
        WorldMeshesLoadTerrainAnalysisSample(
            analysisTexture,
            int2(
                sample0.x,
                sample1.y
            ),
            sliceIndex
        );

    float value11 =
        WorldMeshesLoadTerrainAnalysisSample(
            analysisTexture,
            int2(
                sample1.x,
                sample1.y
            ),
            sliceIndex
        );

    float valueX0 =
        lerp(
            value00,
            value10,
            blend.x
        );

    float valueX1 =
        lerp(
            value01,
            value11,
            blend.x
        );

    valid =
        1.0;

    return
        lerp(
            valueX0,
            valueX1,
            blend.y
        );
}

#endif
