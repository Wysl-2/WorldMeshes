using UnityEngine;

/*
 * Describes how far an analysis sample reaches into its source heightfield.
 *
 * Stage 3 uses this dependency radius to expand height-preview dirty regions
 * before deciding which analysis tiles must be regenerated.
 */
public static class TerrainAnalysisDependencyUtility
{
    public static bool TryGetDependencyRadiusMeters(
        TerrainAnalysisKey key,
        float sampleSpacing,
        out float dependencyRadiusMeters
    )
    {
        dependencyRadiusMeters =
            0f;

        float safeSampleSpacing =
            Mathf.Max(
                0.000001f,
                sampleSpacing
            );

        switch (key.Type)
        {
            case TerrainAnalysisType.Slope:
                /*
                 * Slope samples one native height sample to the left,
                 * right, back, and forward.
                 */
                dependencyRadiusMeters =
                    safeSampleSpacing;

                return true;

            case TerrainAnalysisType.Curvature:
                /*
                 * Curvature samples at the requested world-space scale, but
                 * TerrainAnalysis.compute clamps that radius to at least one
                 * native height sample.
                 */
                dependencyRadiusMeters =
                    Mathf.Max(
                        key.ScaleMeters,
                        safeSampleSpacing
                    );

                return true;

            default:
                return false;
        }
    }
}
