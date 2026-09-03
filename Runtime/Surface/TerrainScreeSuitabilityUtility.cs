using UnityEngine;

/*
 * Immutable snapshot of Scree suitability configuration.
 *
 * Generation jobs should capture this once, then use the snapshot throughout
 * the job so an inspector edit cannot partially change a long-running bake.
 */
public readonly struct TerrainScreeSuitabilitySettings
{
    public readonly float SlopeMin;
    public readonly float SlopePreferredMin;
    public readonly float SlopePreferredMax;
    public readonly float SlopeMax;

    public readonly float CurvatureScale;
    public readonly float ConvexRejectStart;
    public readonly float ConvexRejectEnd;

    public readonly float GeologyScale;
    public readonly float GeologyStrength;

    public TerrainScreeSuitabilitySettings(
        ScreeSettings source
    )
    {
        SlopeMin =
            source != null
                ? source.slopeMin
                : 0f;

        SlopePreferredMin =
            source != null
                ? source.slopePreferredMin
                : 0f;

        SlopePreferredMax =
            source != null
                ? source.slopePreferredMax
                : 0f;

        SlopeMax =
            source != null
                ? source.slopeMax
                : 0f;

        CurvatureScale =
            source != null
                ? source.curvatureScale
                : 1f;

        ConvexRejectStart =
            source != null
                ? source.convexRejectStart
                : 0f;

        ConvexRejectEnd =
            source != null
                ? source.convexRejectEnd
                : 0f;

        GeologyScale =
            source != null
                ? source.geologyScale
                : 1f;

        GeologyStrength =
            source != null
                ? source.geologyStrength
                : 0f;
    }
}

/*
 * Shared CPU Scree suitability evaluator.
 *
 * TerrainSurfaceMaskCompiler and future placement/generation systems use this
 * instead of each carrying a private copy of the suitability formula.
 *
 * The formula mirrors ClipmapTerrainSuitability.hlsl.
 */
public static class TerrainScreeSuitabilityUtility
{
    public static TerrainScreeSuitabilitySettings Capture(
        ScreeSettings source
    )
    {
        return
            new TerrainScreeSuitabilitySettings(
                source
            );
    }

    public static TerrainAnalysisKey[] GetRequiredAnalysisKeys(
        TerrainScreeSuitabilitySettings scree
    )
    {
        return
            new[]
            {
                TerrainAnalysisKey.Slope,
                TerrainAnalysisKey.Curvature(
                    Mathf.Max(
                        0.01f,
                        scree.CurvatureScale
                    )
                )
            };
    }

    public static float Evaluate(
        TerrainScreeSuitabilitySettings scree,
        Vector2 worldXZ,
        float slopeDegrees,
        float curvature
    )
    {
        float slopeWeight =
            GetSlopeWeight(
                scree,
                slopeDegrees
            );

        if (slopeWeight <= 0.0001f)
        {
            return 0f;
        }

        float curvatureWeight =
            GetCurvatureWeight(
                scree,
                curvature
            );

        if (curvatureWeight <= 0.0001f)
        {
            return 0f;
        }

        float geologyWeight =
            GetGeologyWeight(
                scree,
                worldXZ
            );

        return
            Mathf.Clamp01(
                slopeWeight *
                curvatureWeight *
                geologyWeight
            );
    }

    public static bool TryEvaluate(
        TerrainScreeSuitabilitySettings scree,
        TerrainAnalysisTileSet analysis,
        Vector2 worldXZ,
        out float suitability
    )
    {
        suitability =
            0f;

        if (analysis == null)
        {
            return false;
        }

        TerrainAnalysisKey slopeKey =
            TerrainAnalysisKey.Slope;

        TerrainAnalysisKey curvatureKey =
            TerrainAnalysisKey.Curvature(
                Mathf.Max(
                    0.01f,
                    scree.CurvatureScale
                )
            );

        if (
            !analysis.TrySampleBilinear(
                slopeKey,
                worldXZ,
                out float slope
            )
            ||
            !analysis.TrySampleBilinear(
                curvatureKey,
                worldXZ,
                out float curvature
            )
        )
        {
            return false;
        }

        suitability =
            Evaluate(
                scree,
                worldXZ,
                slope,
                curvature
            );

        return true;
    }

    private static float GetSlopeWeight(
        TerrainScreeSuitabilitySettings scree,
        float slopeDegrees
    )
    {
        float minimumSlope =
            Mathf.Min(
                scree.SlopeMin,
                scree.SlopeMax
            );

        float maximumSlope =
            Mathf.Max(
                scree.SlopeMin,
                scree.SlopeMax
            );

        float preferredMinimum =
            Mathf.Clamp(
                scree.SlopePreferredMin,
                minimumSlope,
                maximumSlope
            );

        float preferredMaximum =
            Mathf.Clamp(
                scree.SlopePreferredMax,
                preferredMinimum,
                maximumSlope
            );

        float risingEnd =
            Mathf.Max(
                preferredMinimum,
                minimumSlope +
                    0.0001f
            );

        float fallingEnd =
            Mathf.Max(
                maximumSlope,
                preferredMaximum +
                    0.0001f
            );

        float lowerWeight =
            SmoothStep(
                minimumSlope,
                risingEnd,
                slopeDegrees
            );

        float upperWeight =
            1f -
            SmoothStep(
                preferredMaximum,
                fallingEnd,
                slopeDegrees
            );

        return
            Mathf.Clamp01(
                lowerWeight *
                upperWeight
            );
    }

    private static float GetCurvatureWeight(
        TerrainScreeSuitabilitySettings scree,
        float curvature
    )
    {
        float positiveCurvature =
            Mathf.Max(
                curvature,
                0f
            );

        float rejectStart =
            Mathf.Min(
                scree.ConvexRejectStart,
                scree.ConvexRejectEnd
            );

        float rejectEnd =
            Mathf.Max(
                Mathf.Max(
                    scree.ConvexRejectStart,
                    scree.ConvexRejectEnd
                ),
                rejectStart +
                    0.0001f
            );

        return
            1f -
            SmoothStep(
                rejectStart,
                rejectEnd,
                positiveCurvature
            );
    }

    private static float GetGeologyWeight(
        TerrainScreeSuitabilitySettings scree,
        Vector2 worldXZ
    )
    {
        float geologyScale =
            Mathf.Max(
                scree.GeologyScale,
                0.001f
            );

        Vector2 coordinate =
            worldXZ /
            geologyScale;

        float broadNoise =
            ValueNoise(
                coordinate
            );

        float secondaryNoise =
            ValueNoise(
                coordinate *
                    2.03f +
                new Vector2(
                    17.31f,
                    9.17f
                )
            );

        float geologyNoise =
            broadNoise *
                0.72f +
            secondaryNoise *
                0.28f;

        float patchWeight =
            SmoothStep(
                0.30f,
                0.70f,
                geologyNoise
            );

        return
            Mathf.Lerp(
                1f,
                patchWeight,
                Mathf.Clamp01(
                    scree.GeologyStrength
                )
            );
    }

    private static float Hash21(
        Vector2 value
    )
    {
        Vector3 p3 =
            Frac(
                new Vector3(
                    value.x,
                    value.y,
                    value.x
                )
                *
                0.1031f
            );

        float d =
            Vector3.Dot(
                p3,
                new Vector3(
                    p3.y,
                    p3.z,
                    p3.x
                )
                +
                new Vector3(
                    33.33f,
                    33.33f,
                    33.33f
                )
            );

        p3 +=
            new Vector3(
                d,
                d,
                d
            );

        return
            Frac(
                (
                    p3.x +
                    p3.y
                )
                *
                p3.z
            );
    }

    private static float ValueNoise(
        Vector2 coordinate
    )
    {
        Vector2 cell =
            new Vector2(
                Mathf.Floor(
                    coordinate.x
                ),
                Mathf.Floor(
                    coordinate.y
                )
            );

        Vector2 local =
            new Vector2(
                Frac(
                    coordinate.x
                ),
                Frac(
                    coordinate.y
                )
            );

        Vector2 smoothLocal =
            new Vector2(
                local.x *
                    local.x *
                    (
                        3f -
                        2f *
                        local.x
                    ),
                local.y *
                    local.y *
                    (
                        3f -
                        2f *
                        local.y
                    )
            );

        float a =
            Hash21(
                cell
            );

        float b =
            Hash21(
                cell +
                new Vector2(
                    1f,
                    0f
                )
            );

        float c =
            Hash21(
                cell +
                new Vector2(
                    0f,
                    1f
                )
            );

        float d =
            Hash21(
                cell +
                new Vector2(
                    1f,
                    1f
                )
            );

        return
            Mathf.Lerp(
                Mathf.Lerp(
                    a,
                    b,
                    smoothLocal.x
                ),
                Mathf.Lerp(
                    c,
                    d,
                    smoothLocal.x
                ),
                smoothLocal.y
            );
    }

    private static Vector3 Frac(
        Vector3 value
    )
    {
        return
            new Vector3(
                Frac(value.x),
                Frac(value.y),
                Frac(value.z)
            );
    }

    private static float Frac(
        float value
    )
    {
        return
            value -
            Mathf.Floor(
                value
            );
    }

    private static float SmoothStep(
        float edge0,
        float edge1,
        float value
    )
    {
        float denominator =
            edge1 -
            edge0;

        if (
            Mathf.Abs(
                denominator
            )
            <=
            0.0000001f
        )
        {
            return
                value >= edge1
                    ? 1f
                    : 0f;
        }

        float t =
            Mathf.Clamp01(
                (
                    value -
                    edge0
                )
                /
                denominator
            );

        return
            t *
            t *
            (
                3f -
                2f *
                t
            );
    }
}
