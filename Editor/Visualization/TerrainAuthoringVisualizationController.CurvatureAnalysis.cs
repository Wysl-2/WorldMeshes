using UnityEngine;

/*
 * Stage 5 cached Curvature visualization integration.
 *
 * This partial keeps analysis binding details separate from the existing
 * visualization controller's general editor-state code.
 */
public static partial class TerrainAuthoringVisualizationController
{
    private const string CurvatureVisualizationAnalysisOwnerId =
        "WorldMeshes.AuthoringVisualization.Curvature";

    private static readonly int CurvatureAnalysisTexturePropertyId =
        Shader.PropertyToID(
            "_AuthoringCurvatureAnalysis"
        );

    private static readonly int CurvatureAnalysisReadyPropertyId =
        Shader.PropertyToID(
            "_AuthoringCurvatureAnalysisReady"
        );

    private static readonly int AnalysisCacheOriginTilePropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisCacheOriginTile"
        );

    private static readonly int AnalysisCacheSizePropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisCacheSize"
        );

    private static readonly int AnalysisSamplesPerSidePropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisSamplesPerSide"
        );

    private static readonly int AnalysisSampleSpacingPropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisSampleSpacing"
        );

    private static readonly int AnalysisWorldSizeXZPropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisWorldSizeXZ"
        );

    private static bool TryPrepareCurvatureAnalysis(
        out TerrainAnalysisLayer layer,
        out string errorMessage
    )
    {
        layer =
            TerrainAnalysisService
                .RequestTransientLayer(
                    CurvatureVisualizationAnalysisOwnerId,
                    TerrainAnalysisKey.Curvature(
                        CurvatureScale
                    )
                );

        if (
            layer != null &&
            layer.IsReady &&
            layer.Texture != null &&
            layer.Texture.IsCreated()
        )
        {
            errorMessage =
                "";

            return true;
        }

        errorMessage =
            layer != null &&
            !string.IsNullOrEmpty(
                layer.ErrorMessage
            )
                ? layer.ErrorMessage
                : "Curvature analysis could not be generated for the authoring visualization.";

        return false;
    }

    private static void ApplyCurvatureAnalysisProperties(
        MaterialPropertyBlock block,
        TerrainAnalysisLayer layer
    )
    {
        bool ready =
            layer != null &&
            layer.IsReady &&
            layer.Texture != null &&
            layer.Texture.IsCreated();

        block.SetFloat(
            CurvatureAnalysisReadyPropertyId,
            ready
                ? 1f
                : 0f
        );

        if (!ready)
        {
            return;
        }

        block.SetTexture(
            CurvatureAnalysisTexturePropertyId,
            layer.Texture
        );

        block.SetVector(
            AnalysisCacheOriginTilePropertyId,
            new Vector4(
                layer.CacheOriginTile.x,
                layer.CacheOriginTile.y,
                0f,
                0f
            )
        );

        block.SetVector(
            AnalysisCacheSizePropertyId,
            new Vector4(
                layer.CacheSize.x,
                layer.CacheSize.y,
                0f,
                0f
            )
        );

        block.SetFloat(
            AnalysisSamplesPerSidePropertyId,
            layer.SamplesPerSide
        );

        block.SetFloat(
            AnalysisSampleSpacingPropertyId,
            layer.SampleSpacing
        );

        block.SetVector(
            AnalysisWorldSizeXZPropertyId,
            new Vector4(
                layer.WorldSizeXZ.x,
                layer.WorldSizeXZ.y,
                0f,
                0f
            )
        );
    }

    private static void ReleaseCurvatureVisualizationAnalysis()
    {
        TerrainAnalysisService
            .ReleaseTransientLayer(
                CurvatureVisualizationAnalysisOwnerId
            );
    }
}
