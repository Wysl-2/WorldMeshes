#ifndef WORLDMESHES_CLIPMAP_TERRAIN_SUITABILITY_INCLUDED
#define WORLDMESHES_CLIPMAP_TERRAIN_SUITABILITY_INCLUDED

/*
 * Shared terrain suitability calculations.
 *
 * ClipmapTerrainHeight.hlsl and ClipmapTerrainAnalysis.hlsl must be
 * included before this file.
 *
 * The caller must declare:
 *
 *     float _ScreeSlopeMin;
 *     float _ScreeSlopePreferredMin;
 *     float _ScreeSlopePreferredMax;
 *     float _ScreeSlopeMax;
 *
 *     float _ScreeCurvatureScale;
 *     float _ScreeConvexRejectStart;
 *     float _ScreeConvexRejectEnd;
 *
 *     float _ScreeGeologyScale;
 *     float _ScreeGeologyStrength;
 */

// =========================================================
// SLOPE
// =========================================================

float GetScreeSlopeDegrees(
    float3 normalWS
)
{
    float upDot =
        saturate(
            normalize(
                normalWS
            ).y
        );

    return
        degrees(
            acos(
                upDot
            )
        );
}

float GetScreeSlopeWeight(
    float3 normalWS
)
{
    float slopeDegrees =
        GetScreeSlopeDegrees(
            normalWS
        );

    float minimumSlope =
        min(
            _ScreeSlopeMin,
            _ScreeSlopeMax
        );

    float maximumSlope =
        max(
            _ScreeSlopeMin,
            _ScreeSlopeMax
        );

    float preferredMinimum =
        clamp(
            _ScreeSlopePreferredMin,
            minimumSlope,
            maximumSlope
        );

    float preferredMaximum =
        clamp(
            _ScreeSlopePreferredMax,
            preferredMinimum,
            maximumSlope
        );

    float risingEnd =
        max(
            preferredMinimum,
            minimumSlope +
                0.0001
        );

    float fallingEnd =
        max(
            maximumSlope,
            preferredMaximum +
                0.0001
        );

    float lowerWeight =
        smoothstep(
            minimumSlope,
            risingEnd,
            slopeDegrees
        );

    float upperWeight =
        1.0 -
        smoothstep(
            preferredMaximum,
            fallingEnd,
            slopeDegrees
        );

    return
        saturate(
            lowerWeight *
            upperWeight
        );
}

// =========================================================
// CURVATURE
// =========================================================

float GetScreeCurvatureWeight(
    float3 positionWS
)
{
    float curvature =
        CalculateTerrainCurvature(
            positionWS.xz,
            positionWS.y,
            _ScreeCurvatureScale
        );

    /*
     * Scree may occupy planar and mildly concave slopes, but strongly
     * convex terrain is more characteristic of exposed ridges/outcrops.
     * Only positive curvature is therefore rejected in this first pass.
     */
    float positiveCurvature =
        max(
            curvature,
            0.0
        );

    float rejectStart =
        min(
            _ScreeConvexRejectStart,
            _ScreeConvexRejectEnd
        );

    float rejectEnd =
        max(
            max(
                _ScreeConvexRejectStart,
                _ScreeConvexRejectEnd
            ),
            rejectStart +
                0.0001
        );

    float rejection =
        smoothstep(
            rejectStart,
            rejectEnd,
            positiveCurvature
        );

    return
        1.0 -
        rejection;
}

// =========================================================
// BROAD GEOLOGICAL VARIATION
// =========================================================

float WorldMeshesScreeHash21(
    float2 value
)
{
    float3 p3 =
        frac(
            float3(
                value.x,
                value.y,
                value.x
            )
            *
            0.1031
        );

    p3 +=
        dot(
            p3,
            p3.yzx +
                33.33
        );

    return
        frac(
            (
                p3.x +
                p3.y
            )
            *
            p3.z
        );
}

float WorldMeshesScreeValueNoise(
    float2 coordinate
)
{
    float2 cell =
        floor(
            coordinate
        );

    float2 local =
        frac(
            coordinate
        );

    float2 smoothLocal =
        local *
        local *
        (
            3.0 -
            2.0 *
                local
        );

    float a =
        WorldMeshesScreeHash21(
            cell
        );

    float b =
        WorldMeshesScreeHash21(
            cell +
            float2(
                1.0,
                0.0
            )
        );

    float c =
        WorldMeshesScreeHash21(
            cell +
            float2(
                0.0,
                1.0
            )
        );

    float d =
        WorldMeshesScreeHash21(
            cell +
            float2(
                1.0,
                1.0
            )
        );

    return
        lerp(
            lerp(
                a,
                b,
                smoothLocal.x
            ),
            lerp(
                c,
                d,
                smoothLocal.x
            ),
            smoothLocal.y
        );
}

float GetScreeGeologyWeight(
    float2 worldXZ
)
{
    float geologyScale =
        max(
            _ScreeGeologyScale,
            0.001
        );

    float2 coordinate =
        worldXZ /
        geologyScale;

    /*
     * Two low-frequency octaves reduce obvious square-cell structure while
     * keeping this deliberately cheap enough for the first implementation.
     */
    float broadNoise =
        WorldMeshesScreeValueNoise(
            coordinate
        );

    float secondaryNoise =
        WorldMeshesScreeValueNoise(
            coordinate *
                2.03 +
            float2(
                17.31,
                9.17
            )
        );

    float geologyNoise =
        broadNoise *
            0.72 +
        secondaryNoise *
            0.28;

    float patchWeight =
        smoothstep(
            0.30,
            0.70,
            geologyNoise
        );

    return
        lerp(
            1.0,
            patchWeight,
            saturate(
                _ScreeGeologyStrength
            )
        );
}

// =========================================================
// FINAL SCREE SUITABILITY
// =========================================================

float GetScreeSuitability(
    float3 positionWS,
    float3 normalWS
)
{
    float slopeWeight =
        GetScreeSlopeWeight(
            normalWS
        );

    /*
     * Avoid the four additional curvature samples when slope alone has
     * already rejected the fragment.
     */
    if (slopeWeight <= 0.0001)
    {
        return
            0.0;
    }

    float curvatureWeight =
        GetScreeCurvatureWeight(
            positionWS
        );

    if (curvatureWeight <= 0.0001)
    {
        return
            0.0;
    }

    float geologyWeight =
        GetScreeGeologyWeight(
            positionWS.xz
        );

    return
        saturate(
            slopeWeight *
            curvatureWeight *
            geologyWeight
        );
}

#endif
