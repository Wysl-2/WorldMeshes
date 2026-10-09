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

#if defined(WORLDMESHES_EDITOR_SHARED_HEIGHT)
// Finite binding family: native-relative stride 1..1024, pool index 0..10.
TEXTURE2D(_EditorSharedHeightMap);
TEXTURE2D_ARRAY(_EditorSharedHeightPool0);
TEXTURE2D_ARRAY(_EditorSharedHeightPool1);
TEXTURE2D_ARRAY(_EditorSharedHeightPool2);
TEXTURE2D_ARRAY(_EditorSharedHeightPool3);
TEXTURE2D_ARRAY(_EditorSharedHeightPool4);
TEXTURE2D_ARRAY(_EditorSharedHeightPool5);
TEXTURE2D_ARRAY(_EditorSharedHeightPool6);
TEXTURE2D_ARRAY(_EditorSharedHeightPool7);
TEXTURE2D_ARRAY(_EditorSharedHeightPool8);
TEXTURE2D_ARRAY(_EditorSharedHeightPool9);
TEXTURE2D_ARRAY(_EditorSharedHeightPool10);

float4 SharedHeightPoolInfo(int pool)
{
    switch (pool)
    {
        case 0: return _EditorSharedHeightPoolInfo0;
        case 1: return _EditorSharedHeightPoolInfo1;
        case 2: return _EditorSharedHeightPoolInfo2;
        case 3: return _EditorSharedHeightPoolInfo3;
        case 4: return _EditorSharedHeightPoolInfo4;
        case 5: return _EditorSharedHeightPoolInfo5;
        case 6: return _EditorSharedHeightPoolInfo6;
        case 7: return _EditorSharedHeightPoolInfo7;
        case 8: return _EditorSharedHeightPoolInfo8;
        case 9: return _EditorSharedHeightPoolInfo9;
        case 10: return _EditorSharedHeightPoolInfo10;
    }
    return 0.0;
}

float LoadSharedHeightTexel(int pool, int2 texel, int slice)
{
    switch (pool)
    {
        case 0: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool0, texel, slice).r;
        case 1: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool1, texel, slice).r;
        case 2: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool2, texel, slice).r;
        case 3: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool3, texel, slice).r;
        case 4: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool4, texel, slice).r;
        case 5: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool5, texel, slice).r;
        case 6: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool6, texel, slice).r;
        case 7: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool7, texel, slice).r;
        case 8: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool8, texel, slice).r;
        case 9: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool9, texel, slice).r;
        case 10: return LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool10, texel, slice).r;
    }
    return 0.0; // Only reachable with valid == 0; never a drawable fallback.
}

struct SharedHeightPage
{
    int2 tile;
    int pool, slice;
    float stride, spacing, valid, required;
};

SharedHeightPage LookupSharedHeightPage(int2 tile)
{
    SharedHeightPage p;
    p.tile = tile; p.pool = -1; p.slice = -1;
    p.stride = 0.0; p.spacing = 0.0; p.valid = 0.0;
    p.required = all(tile >= 0) && all(tile < (int2)_EditorSharedHeightTopology.zw) ? 1.0 : 0.0;
    if (p.required < 0.5) return p;
    int2 cell = tile - (int2)_EditorSharedHeightMapWindow.xy;
    if (any(cell < 0) || any(cell >= (int2)_EditorSharedHeightMapWindow.zw)) return p;
    float4 row = LOAD_TEXTURE2D(_EditorSharedHeightMap, cell);
    if (!all(isfinite(row)) || row.x != 1.0 || row.y < 0.0 || row.y > 10.0 || row.z < 0.0) return p;
    int pool = (int)row.y, slice = (int)row.z;
    if (row.y != (float)pool || row.z != (float)slice) return p;
    float4 info = SharedHeightPoolInfo(pool); // samples, spacing, capacity, allocated
    float stride = exp2((float)pool);
    if (info.w < 0.5 || info.x < 2.0 || info.y <= 0.0 || slice >= (int)info.z
        || row.w != stride || info.x != _EditorSharedHeightTopology.y / stride + 1.0) return p;
    p.pool = pool; p.slice = slice; p.stride = stride; p.spacing = info.y; p.valid = 1.0;
    return p;
}

float SampleSharedHeightPage(SharedHeightPage page, float2 point)
{
    float4 info = SharedHeightPoolInfo(page.pool);
    float2 coordinate = clamp((ClampTerrainWorldXZ(point) - (float2)page.tile * _EditorSharedHeightTopology.x)
        / page.spacing, 0.0, info.x - 1.0);
    int2 low = (int2)floor(coordinate);
    int2 high = min(low + 1, (int)info.x - 1);
    float2 fraction = coordinate - (float2)low;
    float a = LoadSharedHeightTexel(page.pool, low, page.slice);
    float b = LoadSharedHeightTexel(page.pool, int2(high.x, low.y), page.slice);
    float c = LoadSharedHeightTexel(page.pool, int2(low.x, high.y), page.slice);
    float d = LoadSharedHeightTexel(page.pool, high, page.slice);
    return lerp(lerp(a, b, fraction.x), lerp(c, d, fraction.x), fraction.y);
}

// Coarsest wins. Fixed Z/X iteration makes ties independent of tile/traversal direction.
SharedHeightPage PickSharedHeightProfile(SharedHeightPage a, SharedHeightPage b)
{
    SharedHeightPage result = a;
    if (a.pool < 0 || b.pool >= 0 && b.stride > a.stride) result = b;
    result.required = max(a.required, b.required);
    result.valid = (a.required < 0.5 || a.valid > 0.5) && (b.required < 0.5 || b.valid > 0.5) ? 1.0 : 0.0;
    return result;
}
SharedHeightPage PickSharedHeightCorner(SharedHeightPage a, SharedHeightPage b, SharedHeightPage c, SharedHeightPage d)
{
    return PickSharedHeightProfile(PickSharedHeightProfile(a, b), PickSharedHeightProfile(c, d));
}

bool SharedHeightCornerNeedsBlend(SharedHeightPage a, SharedHeightPage b, SharedHeightPage c, SharedHeightPage d)
{
    SharedHeightPage corner = PickSharedHeightCorner(a, b, c, d);
    if (corner.valid < 0.5) return true; // A missing in-world neighbour must not masquerade as a compatible edge.
    return (a.required > 0.5 && a.stride != corner.stride)
        || (b.required > 0.5 && b.stride != corner.stride)
        || (c.required > 0.5 && c.stride != corner.stride)
        || (d.required > 0.5 && d.stride != corner.stride);
}

float SharedHeightBandWeight(float distance, float width)
{
    if (width <= 0.0) return distance <= 0.0 ? 1.0 : 0.0;
    return 1.0 - smoothstep(0.0, width, max(distance, 0.0));
}

float SampleEditorSharedTerrainHeight(float2 worldXZ, out float valid)
{
    valid = 0.0;
    if (_EditorSharedHeightEnabled < 0.5 || TerrainWorldBoundsAreReady() < 0.5
        || _EditorSharedHeightTopology.x <= 0.0 || _EditorSharedHeightTopology.y < 1.0
        || any(_EditorSharedHeightMapWindow.zw < 1.0) || !all(isfinite(worldXZ))) return 0.0;
    float2 point = ClampTerrainWorldXZ(worldXZ);
    int2 tile = min((int2)floor(point / _EditorSharedHeightTopology.x), (int2)_EditorSharedHeightTopology.zw - 1);
    SharedHeightPage pages[9];
    [unroll] for (int z = 0; z < 3; z++)
        [unroll] for (int x = 0; x < 3; x++)
            pages[x + z * 3] = LookupSharedHeightPage(tile + int2(x - 1, z - 1));
    if (pages[4].valid < 0.5) return 0.0;
    SharedHeightPage bl = PickSharedHeightCorner(pages[0], pages[1], pages[3], pages[4]);
    SharedHeightPage br = PickSharedHeightCorner(pages[1], pages[2], pages[4], pages[5]);
    SharedHeightPage tl = PickSharedHeightCorner(pages[3], pages[4], pages[6], pages[7]);
    SharedHeightPage tr = PickSharedHeightCorner(pages[4], pages[5], pages[7], pages[8]);
    float size = _EditorSharedHeightTopology.x;
    float2 origin = (float2)tile * size, local = point - origin;
    float2 unit = saturate(local / size);
    // Corner spacings on each edge have the same endpoints on both neighbouring tiles.
    // Width never exceeds one tile, so opposing weights sum to at most one.
    float baseSpacing = pages[4].spacing;
    bool blendBL = SharedHeightCornerNeedsBlend(pages[0], pages[1], pages[3], pages[4]);
    bool blendBR = SharedHeightCornerNeedsBlend(pages[1], pages[2], pages[4], pages[5]);
    bool blendTL = SharedHeightCornerNeedsBlend(pages[3], pages[4], pages[6], pages[7]);
    bool blendTR = SharedHeightCornerNeedsBlend(pages[4], pages[5], pages[7], pages[8]);
    // Equal-resolution compatible corners have zero-width bands. Widths interpolate
    // between identical corner endpoints on both sides, including mixed junctions.
    // Thus equal neighbourhoods preserve all original lattice/edge samples.
    float4 cornerSpacing = max(float4(bl.spacing, br.spacing, tl.spacing, tr.spacing), baseSpacing)
        * float4(blendBL ? 1.0 : 0.0, blendBR ? 1.0 : 0.0, blendTL ? 1.0 : 0.0, blendTR ? 1.0 : 0.0);
    float left = SharedHeightBandWeight(local.x, lerp(cornerSpacing.x, cornerSpacing.z, unit.y));
    float right = SharedHeightBandWeight(size - local.x, lerp(cornerSpacing.y, cornerSpacing.w, unit.y));
    float bottom = SharedHeightBandWeight(local.y, lerp(cornerSpacing.x, cornerSpacing.y, unit.x));
    float top = SharedHeightBandWeight(size - local.y, lerp(cornerSpacing.z, cornerSpacing.w, unit.x));
    float interiorX = max(0.0, 1.0 - left - right), interiorZ = max(0.0, 1.0 - bottom - top);
    float height = 0.0;
    if (interiorX * interiorZ > 0.0) height += interiorX * interiorZ * SampleSharedHeightPage(pages[4], point);
    SharedHeightPage edge;
    if (left * interiorZ > 0.0)
    { edge = PickSharedHeightProfile(pages[3], pages[4]); if (edge.valid < 0.5) return 0.0;
      height += left * interiorZ * SampleSharedHeightPage(edge, float2(origin.x, point.y)); }
    if (right * interiorZ > 0.0)
    { edge = PickSharedHeightProfile(pages[4], pages[5]); if (edge.valid < 0.5) return 0.0;
      height += right * interiorZ * SampleSharedHeightPage(edge, float2(origin.x + size, point.y)); }
    if (bottom * interiorX > 0.0)
    { edge = PickSharedHeightProfile(pages[1], pages[4]); if (edge.valid < 0.5) return 0.0;
      height += bottom * interiorX * SampleSharedHeightPage(edge, float2(point.x, origin.y)); }
    if (top * interiorX > 0.0)
    { edge = PickSharedHeightProfile(pages[4], pages[7]); if (edge.valid < 0.5) return 0.0;
      height += top * interiorX * SampleSharedHeightPage(edge, float2(point.x, origin.y + size)); }
    if (left * bottom > 0.0)
    { if (bl.valid < 0.5) return 0.0; height += left * bottom * SampleSharedHeightPage(bl, origin); }
    if (right * bottom > 0.0)
    { if (br.valid < 0.5) return 0.0; height += right * bottom * SampleSharedHeightPage(br, origin + float2(size, 0)); }
    if (left * top > 0.0)
    { if (tl.valid < 0.5) return 0.0; height += left * top * SampleSharedHeightPage(tl, origin + float2(0, size)); }
    if (right * top > 0.0)
    { if (tr.valid < 0.5) return 0.0; height += right * top * SampleSharedHeightPage(tr, origin + size); }
    valid = isfinite(height) ? 1.0 : 0.0;
    return valid > 0.5 ? height : 0.0;
}

float ResolveSharedHeightNormalSpacing(float2 worldXZ, float requested)
{
    float2 point = ClampTerrainWorldXZ(worldXZ);
    float size = _EditorSharedHeightTopology.x;
    int2 tile = min((int2)floor(point / size), (int2)_EditorSharedHeightTopology.zw - 1);
    float spacing = requested;
    [unroll] for (int z = -1; z <= 1; z++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            SharedHeightPage p = LookupSharedHeightPage(tile + int2(x, z));
            float2 minimum = (float2)p.tile * size;
            float2 distance = max(max(minimum - point, point - (minimum + size)), 0.0);
            if (p.valid > 0.5 && max(distance.x, distance.y) <= p.spacing) spacing = max(spacing, p.spacing);
        }
    return spacing;
}
#endif

float SampleLegacyTerrainHeight(
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

float SampleTerrainHeight(float2 worldXZ, out float valid)
{
#if defined(WORLDMESHES_EDITOR_SHARED_HEIGHT)
    if (_EditorSharedHeightEnabled > 0.5) return SampleEditorSharedTerrainHeight(worldXZ, valid);
#endif
    return SampleLegacyTerrainHeight(worldXZ, valid);
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

#if defined(WORLDMESHES_EDITOR_SHARED_HEIGHT)
    if (_EditorSharedHeightEnabled > 0.5) spacing = ResolveSharedHeightNormalSpacing(worldXZ, spacing);
#endif

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
