/*
 * Describes how far an analysis sample reaches into its source heightfield.
 *
 * Stage 9 delegates this to TerrainAnalysisRegistry so new analysis types do
 * not require another type-specific switch in the invalidation system.
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

        if (
            !TerrainAnalysisRegistry
                .TryValidateKey(
                    key,
                    out TerrainAnalysisDefinition definition,
                    out _
                )
            ||
            definition == null
        )
        {
            return false;
        }

        return
            definition
                .TryGetDependencyRadiusMeters(
                    key,
                    sampleSpacing,
                    out dependencyRadiusMeters
                );
    }
}
