#ifndef WORLDMESHES_CLIPMAP_TERRAIN_HEIGHT_INCLUDED
#define WORLDMESHES_CLIPMAP_TERRAIN_HEIGHT_INCLUDED

/*
 * Shared clipmap terrain displacement code.
 *
 * Callers must declare these material properties before including
 * this file:
 *
 *     float4 _HeightCacheOriginTile;
 *     float4 _HeightCacheSize;
 *     float  _HeightTileSamplesPerSide;
 *     float  _HeightSampleSpacing;
 *
 *     float4 _WorldSizeXZ;
 *     float  _WorldBoundsReady;
 *
 *     float  _HeightCacheReady;
 *     float4 _ClipmapTransitionOffset;
 *
 * Core.hlsl must also be included before this file so Unity's
 * texture-array macros and world/clip-space helpers are available.
 */

TEXTURE2D_ARRAY(
    _HeightCache
);

// =========================================================
// WORLD BOUNDS
// =========================================================

float TerrainWorldBoundsAreReady()
{
    if (
        _WorldBoundsReady < 0.5
        ||
        _WorldSizeXZ.x <= 0.0
        ||
        _WorldSizeXZ.y <= 0.0
    )
    {
        return 0.0;
    }

    return 1.0;
}

float2 ClampTerrainWorldXZ(
    float2 worldXZ
)
{
    float2 safeWorldSize =
        max(
            _WorldSizeXZ.xy,
            float2(
                0.0,
                0.0
            )
        );

    return
        clamp(
            worldXZ,
            float2(
                0.0,
                0.0
            ),
            safeWorldSize
        );
}

/*
 * Exact logical terrain rectangle.
 *
 * The clipmap mesh is only a rendering structure. It may be
 * smaller than, equal to, or larger than the world.
 *
 * Fragment clipping keeps triangles that cross the boundary
 * stable while discarding only the portion outside:
 *
 *     X < 0
 *     Z < 0
 *     X > WorldSizeX
 *     Z > WorldSizeZ
 *
 * Values exactly on the boundary remain valid because HLSL clip()
 * discards only negative values.
 */
float GetTerrainWorldBoundaryDistance(
    float2 worldXZ
)
{
    float2 distanceFromMinimum =
        worldXZ;

    float2 distanceFromMaximum =
        _WorldSizeXZ.xy -
        worldXZ;

    float minimumBoundaryDistance =
        min(
            distanceFromMinimum.x,
            distanceFromMinimum.y
        );

    float maximumBoundaryDistance =
        min(
            distanceFromMaximum.x,
            distanceFromMaximum.y
        );

    return
        min(
            minimumBoundaryDistance,
            maximumBoundaryDistance
        );
}

void ClipTerrainFragmentToWorld(
    float2 worldXZ
)
{
    if (
        TerrainWorldBoundsAreReady() <
        0.5
    )
    {
        return;
    }

    clip(
        GetTerrainWorldBoundaryDistance(
            worldXZ
        )
    );
}

// =========================================================
// CLIPMAP STITCH DISPLACEMENT
// =========================================================

float3 ApplyClipmapTransitionOffset(
    float3 positionWS,
    float transitionWeight
)
{
    float safeTransitionWeight =
        saturate(
            transitionWeight
        );

    positionWS.xz +=
        _ClipmapTransitionOffset.xz *
        safeTransitionWeight;

    return
        positionWS;
}

// =========================================================
// SAMPLE TERRAIN HEIGHT
// =========================================================

float SampleTerrainHeight(
    float2 worldXZ,
    out float valid
)
{
    valid =
        0.0;

    // -----------------------------------------------------
    // Required source state
    // -----------------------------------------------------

    /*
     * Height sampling requires both:
     *
     * - a resident height cache
     * - valid logical world dimensions
     *
     * World clipping itself does NOT depend on height-cache
     * readiness.
     */
    if (
        _HeightCacheReady <
            0.5
        ||
        TerrainWorldBoundsAreReady() <
            0.5
    )
    {
        return 0.0;
    }

    // -----------------------------------------------------
    // Safe metadata
    // -----------------------------------------------------

    float sampleSpacing =
        max(
            _HeightSampleSpacing,
            0.000001
        );

    int samplesPerSide =
        max(
            (int)floor(
                _HeightTileSamplesPerSide
                +
                0.5
            ),
            2
        );

    int tileIntervals =
        samplesPerSide -
        1;

    int cacheWidth =
        max(
            (int)floor(
                _HeightCacheSize.x
                +
                0.5
            ),
            1
        );

    int cacheHeight =
        max(
            (int)floor(
                _HeightCacheSize.y
                +
                0.5
            ),
            1
        );

    float2 worldSize =
        max(
            _WorldSizeXZ.xy,
            float2(
                0.0,
                0.0
            )
        );

    // -----------------------------------------------------
    // Clamp lookup position to actual world
    // -----------------------------------------------------

    /*
     * A clipmap triangle can cross the logical world boundary.
     *
     * Sampling the nearest edge height for vertices just outside
     * the world keeps the geometry stable up to the boundary.
     * Fragment clipping later removes the outside pixels exactly.
     */
    float2 clampedWorldXZ =
        ClampTerrainWorldXZ(
            worldXZ
        );

    // =====================================================
    // GLOBAL HEIGHT SAMPLE
    // =====================================================

    int2 globalSample =
        (int2)floor(
            clampedWorldXZ /
            sampleSpacing
            +
            0.5
        );

    int2 worldMaxSample =
        (int2)floor(
            worldSize /
            sampleSpacing
            +
            0.5
        );

    // =====================================================
    // HEIGHT TILE GRID SIZE
    // =====================================================

    int2 totalTileCount =
        (
            worldMaxSample
            +
            tileIntervals
            -
            1
        )
        /
        tileIntervals;

    totalTileCount =
        max(
            totalTileCount,
            int2(
                1,
                1
            )
        );

    // =====================================================
    // ABSOLUTE TILE COORDINATE
    // =====================================================

    int2 tileCoordinate =
        globalSample /
        tileIntervals;

    /*
     * At the exact maximum world sample the raw coordinate points
     * one tile beyond the grid.
     */
    tileCoordinate =
        min(
            tileCoordinate,
            totalTileCount -
            1
        );

    // =====================================================
    // SAMPLE COORDINATE INSIDE TILE
    // =====================================================

    int2 localSample =
        globalSample
        -
        tileCoordinate *
        tileIntervals;

    // =====================================================
    // CACHE-LOCAL TILE COORDINATE
    // =====================================================

    int2 cacheOrigin =
        (int2)floor(
            _HeightCacheOriginTile.xy
            +
            0.5
        );

    int2 cacheLocalTile =
        tileCoordinate
        -
        cacheOrigin;

    if (
        cacheLocalTile.x < 0
        ||
        cacheLocalTile.y < 0
        ||
        cacheLocalTile.x >=
            cacheWidth
        ||
        cacheLocalTile.y >=
            cacheHeight
    )
    {
        return 0.0;
    }

    // =====================================================
    // TEXTURE ARRAY SLICE
    // =====================================================

    int slice =
        cacheLocalTile.x
        +
        cacheLocalTile.y *
        cacheWidth;

    // =====================================================
    // EXACT HEIGHT TEXEL
    // =====================================================

    float terrainHeight =
        LOAD_TEXTURE2D_ARRAY(
            _HeightCache,
            localSample,
            slice
        ).r;

    valid =
        1.0;

    return
        terrainHeight;
}

// =========================================================
// CALCULATE TERRAIN NORMAL
// =========================================================

float ResolveTerrainNormalSampleSpacing(
    float transitionWeight
)
{
    float fallbackSpacing =
        max(
            _HeightSampleSpacing,
            0.000001
        );

    float fineSpacing =
        _HeightNormalSampleSpacingFine > 0.0
            ? _HeightNormalSampleSpacingFine
            : fallbackSpacing;

    float coarseSpacing =
        _HeightNormalSampleSpacingCoarse > 0.0
            ? _HeightNormalSampleSpacingCoarse
            : fallbackSpacing;

    return
        lerp(
            max(
                coarseSpacing,
                0.000001
            ),
            max(
                fineSpacing,
                0.000001
            ),
            saturate(
                transitionWeight
            )
        );
}

float3 CalculateTerrainNormal(
    float2 worldXZ,
    float centerHeight,
    float normalSampleSpacing
)
{
    /*
     * Calculate terrain slope directly from neighboring
     * authoritative height samples.
     */
    float spacing =
        max(
            normalSampleSpacing,
            0.000001
        );

    float leftValid;
    float rightValid;
    float backValid;
    float forwardValid;

    float heightLeft =
        SampleTerrainHeight(
            worldXZ
            -
            float2(
                spacing,
                0.0
            ),
            leftValid
        );

    float heightRight =
        SampleTerrainHeight(
            worldXZ
            +
            float2(
                spacing,
                0.0
            ),
            rightValid
        );

    float heightBack =
        SampleTerrainHeight(
            worldXZ
            -
            float2(
                0.0,
                spacing
            ),
            backValid
        );

    float heightForward =
        SampleTerrainHeight(
            worldXZ
            +
            float2(
                0.0,
                spacing
            ),
            forwardValid
        );

    /*
     * At cache/world edges, fall back to the center height rather
     * than producing a slope from invalid data.
     */
    if (leftValid < 0.5)
    {
        heightLeft =
            centerHeight;
    }

    if (rightValid < 0.5)
    {
        heightRight =
            centerHeight;
    }

    if (backValid < 0.5)
    {
        heightBack =
            centerHeight;
    }

    if (forwardValid < 0.5)
    {
        heightForward =
            centerHeight;
    }

    float3 normalWS =
        float3(
            heightLeft -
                heightRight,

            2.0 *
                spacing,

            heightBack -
                heightForward
        );

    return
        normalize(
            normalWS
        );
}

// =========================================================
// APPLY HEIGHT DISPLACEMENT
// =========================================================

/*
 * Applies authoritative world-space Y displacement when a valid
 * height sample is available.
 *
 * When no height cache is available the caller receives a flat
 * up normal and the input Y remains unchanged. World-boundary
 * clipping remains active independently.
 */
float ApplyTerrainHeightDisplacementPositionOnly(
    inout float3 positionWS
)
{
    float heightValid;

    float terrainHeight =
        SampleTerrainHeight(
            positionWS.xz,
            heightValid
        );

    if (heightValid < 0.5)
    {
        return 0.0;
    }

    positionWS.y =
        terrainHeight;

    return 1.0;
}

float ApplyTerrainHeightDisplacement(
    inout float3 positionWS,
    out float3 normalWS,
    float transitionWeight
)
{
    normalWS =
        float3(
            0.0,
            1.0,
            0.0
        );

    float heightValid =
        ApplyTerrainHeightDisplacementPositionOnly(
            positionWS
        );

    if (heightValid < 0.5)
    {
        return 0.0;
    }

    float normalSampleSpacing =
        ResolveTerrainNormalSampleSpacing(
            transitionWeight
        );

    normalWS =
        CalculateTerrainNormal(
            positionWS.xz,
            positionWS.y,
            normalSampleSpacing
        );

    return 1.0;
}

#endif
