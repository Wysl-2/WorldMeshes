using System.Collections.Generic;

/*
 * Stage 9 registration point for terrain-analysis types.
 *
 * Adding a new built-in analysis should require:
 *
 * 1. one TerrainAnalysisType enum value
 * 2. one registration here
 * 3. one compute kernel with the registered name
 *
 * The cache, service, invalidation, addressing, async readback, and generic
 * visualization systems do not need type-specific changes.
 */
public static class TerrainAnalysisRegistry
{
    private static readonly Dictionary<
        TerrainAnalysisType,
        TerrainAnalysisDefinition
    > DefinitionsByType =
        new Dictionary<
            TerrainAnalysisType,
            TerrainAnalysisDefinition
        >();

    private static readonly List<
        TerrainAnalysisDefinition
    > DefinitionsList =
        new List<
            TerrainAnalysisDefinition
        >();

    public static IReadOnlyList<
        TerrainAnalysisDefinition
    > Definitions =>
        DefinitionsList;

    static TerrainAnalysisRegistry()
    {
        RegisterBuiltIn(
            new TerrainAnalysisDefinition(
                TerrainAnalysisType.Slope,
                "Slope",
                false,
                "GenerateSlope",
                TerrainAnalysisDependencyRadiusMode
                    .NativeSampleSpacing,
                TerrainAnalysisVisualizationKind
                    .Unsigned,
                0f,
                90f
            )
        );

        RegisterBuiltIn(
            new TerrainAnalysisDefinition(
                TerrainAnalysisType.Curvature,
                "Curvature",
                true,
                "GenerateCurvature",
                TerrainAnalysisDependencyRadiusMode
                    .AnalysisScale,
                TerrainAnalysisVisualizationKind
                    .Signed,
                -0.25f,
                0.25f
            )
        );

        /*
         * Roughness is a dimensionless RMS residual from a local planar trend.
         * Values around 0 are smooth/planar; increasing values indicate
         * stronger small-scale terrain irregularity.
         */
        RegisterBuiltIn(
            new TerrainAnalysisDefinition(
                TerrainAnalysisType.Roughness,
                "Roughness",
                true,
                "GenerateRoughness",
                TerrainAnalysisDependencyRadiusMode
                    .AnalysisScale,
                TerrainAnalysisVisualizationKind
                    .Unsigned,
                0f,
                0.35f
            )
        );

        /*
         * Local Relief is measured in metres (local max height - local min
         * height). The default diagnostic range scales with the selected
         * world-space radius so it remains useful across different scales.
         */
        RegisterBuiltIn(
            new TerrainAnalysisDefinition(
                TerrainAnalysisType.LocalRelief,
                "Local Relief",
                true,
                "GenerateLocalRelief",
                TerrainAnalysisDependencyRadiusMode
                    .AnalysisScale,
                TerrainAnalysisVisualizationKind
                    .Unsigned,
                0f,
                1f,
                2f
            )
        );
    }

    public static bool TryGetDefinition(
        TerrainAnalysisType type,
        out TerrainAnalysisDefinition definition
    )
    {
        return
            DefinitionsByType.TryGetValue(
                type,
                out definition
            );
    }

    public static bool TryValidateKey(
        TerrainAnalysisKey key,
        out TerrainAnalysisDefinition definition,
        out string errorMessage
    )
    {
        definition =
            null;

        errorMessage =
            "";

        if (
            !TryGetDefinition(
                key.Type,
                out definition
            )
            ||
            definition == null
        )
        {
            errorMessage =
                "Unsupported terrain analysis type: " +
                key.Type;

            return false;
        }

        return
            definition.TryValidateKey(
                key,
                out errorMessage
            );
    }

    private static void RegisterBuiltIn(
        TerrainAnalysisDefinition definition
    )
    {
        if (definition == null)
        {
            return;
        }

        if (
            DefinitionsByType.ContainsKey(
                definition.Type
            )
        )
        {
            throw new System.InvalidOperationException(
                "Terrain analysis type is already registered: " +
                definition.Type
            );
        }

        DefinitionsByType.Add(
            definition.Type,
            definition
        );

        DefinitionsList.Add(
            definition
        );
    }
}
