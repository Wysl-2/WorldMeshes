#ifndef WORLDMESHES_TERRAIN_ANALYSIS_COMMON_INCLUDED
#define WORLDMESHES_TERRAIN_ANALYSIS_COMMON_INCLUDED

/*
 * Pure terrain-analysis math shared by compute generation and terrain
 * rendering consumers.
 *
 * This file must not depend on height-cache bindings, editor state, scree,
 * vegetation, rocks, or any other suitability/consumer concept.
 */

// =========================================================
// SLOPE
// =========================================================

/*
 * Converts a terrain surface normal into slope angle in degrees.
 *
 * 0 degrees  = horizontal
 * 90 degrees = vertical
 */
float CalculateTerrainSlopeDegrees(
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

#endif
