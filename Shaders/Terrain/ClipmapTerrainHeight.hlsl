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

#if defined(SHADER_API_GLCORE)
// Use the hardware bilinear sampler for fractional page coordinates on
// OpenGL. Exact lattice points retain point reads from the original page.
// This reduces the expanded vertex-stage texture-read paths without changing
// the geographical page selection, boundary blending or missing-page policy.
// OpenGL Core does not support Unity inline sampler states. These names
// pair with the corresponding pool textures and inherit their filter mode.
SAMPLER(sampler_EditorSharedHeightPool0);
SAMPLER(sampler_EditorSharedHeightPool1);
SAMPLER(sampler_EditorSharedHeightPool2);
SAMPLER(sampler_EditorSharedHeightPool3);
SAMPLER(sampler_EditorSharedHeightPool4);
SAMPLER(sampler_EditorSharedHeightPool5);
SAMPLER(sampler_EditorSharedHeightPool6);
SAMPLER(sampler_EditorSharedHeightPool7);
SAMPLER(sampler_EditorSharedHeightPool8);
SAMPLER(sampler_EditorSharedHeightPool9);
SAMPLER(sampler_EditorSharedHeightPool10);

float SampleSharedHeightBilinear(int pool, int2 low, int2 high, int slice, float2 fraction)
{
    float sampleCount = max(SharedHeightPoolInfo(pool).x, 2.0);
    float2 coordinate = lerp((float2)low, (float2)high, fraction);
    float2 uv = (coordinate + 0.5) / sampleCount;
    bool exactLatticePoint = all(fraction == float2(0.0, 0.0));
    switch (pool)
    {
        case 0:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool0, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool0, sampler_EditorSharedHeightPool0, uv, slice, 0).r;
        case 1:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool1, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool1, sampler_EditorSharedHeightPool1, uv, slice, 0).r;
        case 2:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool2, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool2, sampler_EditorSharedHeightPool2, uv, slice, 0).r;
        case 3:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool3, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool3, sampler_EditorSharedHeightPool3, uv, slice, 0).r;
        case 4:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool4, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool4, sampler_EditorSharedHeightPool4, uv, slice, 0).r;
        case 5:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool5, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool5, sampler_EditorSharedHeightPool5, uv, slice, 0).r;
        case 6:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool6, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool6, sampler_EditorSharedHeightPool6, uv, slice, 0).r;
        case 7:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool7, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool7, sampler_EditorSharedHeightPool7, uv, slice, 0).r;
        case 8:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool8, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool8, sampler_EditorSharedHeightPool8, uv, slice, 0).r;
        case 9:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool9, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool9, sampler_EditorSharedHeightPool9, uv, slice, 0).r;
        case 10:
            return exactLatticePoint
                ? LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool10, low, slice).r
                : SAMPLE_TEXTURE2D_ARRAY_LOD(_EditorSharedHeightPool10, sampler_EditorSharedHeightPool10, uv, slice, 0).r;
        default: return 0.0;
    }
}
#else
// Select the physical pool once for all four bilinear taps. Repeating the
// pool switch for each tap multiplies the translated vertex program.
float SampleSharedHeightBilinear(int pool, int2 low, int2 high, int slice, float2 fraction)
{
    float4 taps = 0.0;
    switch (pool)
    {
        case 0:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool0, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool0, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool0, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool0, high, slice).r);
            break;
        case 1:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool1, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool1, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool1, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool1, high, slice).r);
            break;
        case 2:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool2, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool2, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool2, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool2, high, slice).r);
            break;
        case 3:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool3, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool3, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool3, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool3, high, slice).r);
            break;
        case 4:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool4, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool4, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool4, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool4, high, slice).r);
            break;
        case 5:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool5, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool5, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool5, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool5, high, slice).r);
            break;
        case 6:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool6, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool6, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool6, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool6, high, slice).r);
            break;
        case 7:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool7, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool7, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool7, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool7, high, slice).r);
            break;
        case 8:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool8, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool8, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool8, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool8, high, slice).r);
            break;
        case 9:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool9, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool9, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool9, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool9, high, slice).r);
            break;
        case 10:
            taps = float4(LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool10, low, slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool10, int2(high.x, low.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool10, int2(low.x, high.y), slice).r,
                LOAD_TEXTURE2D_ARRAY(_EditorSharedHeightPool10, high, slice).r);
            break;
    }
    return lerp(lerp(taps.x, taps.y, fraction.x), lerp(taps.z, taps.w, fraction.x), fraction.y);
}
#endif

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

float SampleSharedHeightPage(SharedHeightPage page, float2 sampleWorldXZ)
{
    float4 info = SharedHeightPoolInfo(page.pool);
    float2 coordinate = clamp((ClampTerrainWorldXZ(sampleWorldXZ) - (float2)page.tile * _EditorSharedHeightTopology.x)
        / page.spacing, 0.0, info.x - 1.0);
    int2 low = (int2)floor(coordinate);
    int2 high = min(low + 1, (int)info.x - 1);
    float2 fraction = coordinate - (float2)low;
    return SampleSharedHeightBilinear(page.pool, low, high, page.slice, fraction);
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
    float2 sampleWorldXZ = ClampTerrainWorldXZ(worldXZ);
    int2 tile = min((int2)floor(sampleWorldXZ / _EditorSharedHeightTopology.x), (int2)_EditorSharedHeightTopology.zw - 1);
    // Explicit neighbours avoid a temporary array of structs in cross-compilers.
    SharedHeightPage pageBL = LookupSharedHeightPage(tile + int2(-1, -1));
    SharedHeightPage pageB = LookupSharedHeightPage(tile + int2(0, -1));
    SharedHeightPage pageBR = LookupSharedHeightPage(tile + int2(1, -1));
    SharedHeightPage pageL = LookupSharedHeightPage(tile + int2(-1, 0));
    SharedHeightPage pageC = LookupSharedHeightPage(tile + int2(0, 0));
    SharedHeightPage pageR = LookupSharedHeightPage(tile + int2(1, 0));
    SharedHeightPage pageTL = LookupSharedHeightPage(tile + int2(-1, 1));
    SharedHeightPage pageT = LookupSharedHeightPage(tile + int2(0, 1));
    SharedHeightPage pageTR = LookupSharedHeightPage(tile + int2(1, 1));
    if (pageC.valid < 0.5) return 0.0;
    SharedHeightPage bl = PickSharedHeightCorner(pageBL, pageB, pageL, pageC);
    SharedHeightPage br = PickSharedHeightCorner(pageB, pageBR, pageC, pageR);
    SharedHeightPage tl = PickSharedHeightCorner(pageL, pageC, pageTL, pageT);
    SharedHeightPage tr = PickSharedHeightCorner(pageC, pageR, pageT, pageTR);
    float size = _EditorSharedHeightTopology.x;
    float2 origin = (float2)tile * size, local = sampleWorldXZ - origin;
    float2 unit = saturate(local / size);
    // Corner spacings on each edge have the same endpoints on both neighbouring tiles.
    // Width never exceeds one tile, so opposing weights sum to at most one.
    float baseSpacing = pageC.spacing;
    bool blendBL = SharedHeightCornerNeedsBlend(pageBL, pageB, pageL, pageC);
    bool blendBR = SharedHeightCornerNeedsBlend(pageB, pageBR, pageC, pageR);
    bool blendTL = SharedHeightCornerNeedsBlend(pageL, pageC, pageTL, pageT);
    bool blendTR = SharedHeightCornerNeedsBlend(pageC, pageR, pageT, pageTR);
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
    // Preserve the centre/edge/corner contribution order while keeping one
    // rolled texture-sampling body. Normals call this sampler at four positions.
    [loop] for (int contribution = 0; contribution < 9; contribution++)
    {
        SharedHeightPage profile = pageC;
        float weight = 0.0;
        float2 profileWorldXZ = sampleWorldXZ;
        switch (contribution)
        {
            case 0: weight = interiorX * interiorZ; break;
            case 1:
                weight = left * interiorZ; profile = PickSharedHeightProfile(pageL, pageC);
                profileWorldXZ = float2(origin.x, sampleWorldXZ.y); break;
            case 2:
                weight = right * interiorZ; profile = PickSharedHeightProfile(pageC, pageR);
                profileWorldXZ = float2(origin.x + size, sampleWorldXZ.y); break;
            case 3:
                weight = bottom * interiorX; profile = PickSharedHeightProfile(pageB, pageC);
                profileWorldXZ = float2(sampleWorldXZ.x, origin.y); break;
            case 4:
                weight = top * interiorX; profile = PickSharedHeightProfile(pageC, pageT);
                profileWorldXZ = float2(sampleWorldXZ.x, origin.y + size); break;
            case 5: weight = left * bottom; profile = bl; profileWorldXZ = origin; break;
            case 6: weight = right * bottom; profile = br; profileWorldXZ = origin + float2(size, 0); break;
            case 7: weight = left * top; profile = tl; profileWorldXZ = origin + float2(0, size); break;
            case 8: weight = right * top; profile = tr; profileWorldXZ = origin + size; break;
        }
        if (weight > 0.0)
        {
            if (profile.valid < 0.5) return 0.0;
            height += weight * SampleSharedHeightPage(profile, profileWorldXZ);
        }
    }
    valid = isfinite(height) ? 1.0 : 0.0;
    return valid > 0.5 ? height : 0.0;
}

float ResolveSharedHeightNormalSpacing(float2 worldXZ, float requested)
{
    float2 sampleWorldXZ = ClampTerrainWorldXZ(worldXZ);
    float size = _EditorSharedHeightTopology.x;
    int2 tile = min((int2)floor(sampleWorldXZ / size), (int2)_EditorSharedHeightTopology.zw - 1);
    float spacing = requested;
    [loop] for (int z = -1; z <= 1; z++)
        [loop] for (int x = -1; x <= 1; x++)
        {
            SharedHeightPage p = LookupSharedHeightPage(tile + int2(x, z));
            float2 minimum = (float2)p.tile * size;
            float2 distance = max(max(minimum - sampleWorldXZ, sampleWorldXZ - (minimum + size)), 0.0);
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
