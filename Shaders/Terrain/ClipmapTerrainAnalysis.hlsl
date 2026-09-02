#ifndef WORLDMESHES_CLIPMAP_TERRAIN_ANALYSIS_INCLUDED
#define WORLDMESHES_CLIPMAP_TERRAIN_ANALYSIS_INCLUDED

/*
 * Shared terrain-analysis helpers for ClipmapTerrain.shader.
 *
 * ClipmapTerrainHeight.hlsl must be included first because curvature
 * analysis samples the authoritative height cache through
 * SampleTerrainHeight(...).
 */

// =========================================================
// MULTI-SCALE CURVATURE
// =========================================================

/*
 * Returns a dimensionless center-vs-neighbour curvature signal at a
 * caller-selected world-space radius.
 *
 * Positive values:
 *     center sits above its surroundings -> convex / ridge-like
 *
 * Negative values:
 *     center sits below its surroundings -> concave / gully-like
 *
 * Values near zero:
 *     locally planar at the selected analysis scale
 *
 * This is intentionally a practical heightfield analysis signal rather
 * than a differential-geometry curvature estimator.
 */
float CalculateTerrainCurvature(
    float2 worldXZ,
    float centerHeight,
    float radiusMeters
)
{
    float nativeSpacing =
        max(
            _HeightSampleSpacing,
            0.000001
        );

    float radius =
        max(
            radiusMeters,
            nativeSpacing
        );

    float leftValid;
    float rightValid;
    float backValid;
    float forwardValid;

    float heightLeft =
        SampleTerrainHeight(
            worldXZ -
            float2(
                radius,
                0.0
            ),
            leftValid
        );

    float heightRight =
        SampleTerrainHeight(
            worldXZ +
            float2(
                radius,
                0.0
            ),
            rightValid
        );

    float heightBack =
        SampleTerrainHeight(
            worldXZ -
            float2(
                0.0,
                radius
            ),
            backValid
        );

    float heightForward =
        SampleTerrainHeight(
            worldXZ +
            float2(
                0.0,
                radius
            ),
            forwardValid
        );

    /*
     * At logical-world/cache boundaries, use the center value for an
     * unavailable neighbour. This avoids artificial curvature spikes.
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

    float neighbourAverage =
        (
            heightLeft +
            heightRight +
            heightBack +
            heightForward
        )
        *
        0.25;

    return
        (
            centerHeight -
            neighbourAverage
        )
        /
        radius;
}

#endif
