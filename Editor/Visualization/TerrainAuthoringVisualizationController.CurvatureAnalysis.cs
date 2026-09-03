using UnityEngine;

/*
 * Stage 9 generic raw Terrain Analysis visualization.
 *
 * This file intentionally keeps the historical CurvatureAnalysis.cs path so
 * the existing Unity .meta/GUID is preserved during migration, but the
 * implementation is no longer Curvature-specific.
 */
public static partial class TerrainAuthoringVisualizationController
{
    private const string AnalysisVisualizationOwnerId =
        "WorldMeshes.AuthoringVisualization.Analysis";

    private static readonly int AnalysisVisualizationTexturePropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisTexture"
        );

    private static readonly int AnalysisVisualizationReadyPropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisReady"
        );

    private static readonly int AnalysisVisualizationDisplayRangePropertyId =
        Shader.PropertyToID(
            "_AuthoringAnalysisDisplayRange"
        );

    /*
     * These layout properties remain shared with the Scree authoring
     * suitability path, which binds Slope + Curvature generated from the same
     * preview cache.
     */
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

    public static bool IsRawAnalysisVisualizationMode(
        TerrainAuthoringVisualizationMode mode
    )
    {
        return
            mode ==
                TerrainAuthoringVisualizationMode.Slope
            ||
            mode ==
                TerrainAuthoringVisualizationMode.Curvature
            ||
            mode ==
                TerrainAuthoringVisualizationMode.Roughness
            ||
            mode ==
                TerrainAuthoringVisualizationMode.LocalRelief;
    }

    public static bool TryGetAnalysisVisualizationKey(
        TerrainAuthoringVisualizationMode mode,
        out TerrainAnalysisKey key
    )
    {
        switch (mode)
        {
            case TerrainAuthoringVisualizationMode.Slope:
                key =
                    TerrainAnalysisKey.Slope;

                return true;

            case TerrainAuthoringVisualizationMode.Curvature:
                key =
                    TerrainAnalysisKey.Curvature(
                        CurvatureScale
                    );

                return true;

            case TerrainAuthoringVisualizationMode.Roughness:
                key =
                    TerrainAnalysisKey.Roughness(
                        RoughnessScale
                    );

                return true;

            case TerrainAuthoringVisualizationMode.LocalRelief:
                key =
                    TerrainAnalysisKey.LocalRelief(
                        LocalReliefScale
                    );

                return true;

            default:
                key =
                    default;

                return false;
        }
    }

    public static float GetAnalysisVisualizationScale(
        TerrainAuthoringVisualizationMode mode
    )
    {
        switch (mode)
        {
            case TerrainAuthoringVisualizationMode.Curvature:
                return
                    CurvatureScale;

            case TerrainAuthoringVisualizationMode.Roughness:
                return
                    RoughnessScale;

            case TerrainAuthoringVisualizationMode.LocalRelief:
                return
                    LocalReliefScale;

            default:
                return
                    0f;
        }
    }

    public static void SetAnalysisVisualizationScale(
        TerrainAuthoringVisualizationMode mode,
        float scale
    )
    {
        switch (mode)
        {
            case TerrainAuthoringVisualizationMode.Curvature:
                CurvatureScale =
                    scale;

                break;

            case TerrainAuthoringVisualizationMode.Roughness:
                RoughnessScale =
                    scale;

                break;

            case TerrainAuthoringVisualizationMode.LocalRelief:
                LocalReliefScale =
                    scale;

                break;
        }
    }

    private static bool TryPrepareAnalysisVisualization(
        TerrainAuthoringVisualizationMode mode,
        out TerrainAnalysisLayer layer,
        out TerrainAnalysisDefinition definition,
        out string errorMessage
    )
    {
        layer =
            null;

        definition =
            null;

        errorMessage =
            "";

        if (
            !TryGetAnalysisVisualizationKey(
                mode,
                out TerrainAnalysisKey key
            )
        )
        {
            errorMessage =
                "The selected authoring mode is not a raw Terrain Analysis visualization.";

            return false;
        }

        if (
            !TerrainAnalysisRegistry
                .TryValidateKey(
                    key,
                    out definition,
                    out errorMessage
                )
            ||
            definition == null
        )
        {
            return false;
        }

        if (definition.RequiresScale)
        {
            /*
             * A single owner slot prevents interactive scale changes from
             * accumulating full Texture2DArray layers.
             */
            layer =
                TerrainAnalysisService
                    .RequestTransientLayer(
                        AnalysisVisualizationOwnerId,
                        key
                    );
        }
        else
        {
            /*
             * Scale-independent Slope is useful to other consumers such as
             * Scree generation. Reuse the persistent shared layer.
             */
            TerrainAnalysisService
                .ReleaseTransientLayer(
                    AnalysisVisualizationOwnerId
                );

            layer =
                TerrainAnalysisService
                    .RequestLayer(
                        key
                    );
        }

        if (
            layer != null
            &&
            layer.IsReady
            &&
            layer.Texture != null
            &&
            layer.Texture.IsCreated()
        )
        {
            return true;
        }

        errorMessage =
            layer != null
            &&
            !string.IsNullOrEmpty(
                layer.ErrorMessage
            )
                ? layer.ErrorMessage
                : definition.DisplayName +
                  " analysis could not be generated for authoring visualization.";

        return false;
    }

    private static void ApplyAnalysisVisualizationProperties(
        MaterialPropertyBlock block,
        TerrainAnalysisLayer layer,
        TerrainAnalysisDefinition definition
    )
    {
        bool ready =
            layer != null
            &&
            definition != null
            &&
            layer.IsReady
            &&
            layer.Texture != null
            &&
            layer.Texture.IsCreated();

        block.SetFloat(
            AnalysisVisualizationReadyPropertyId,
            ready
                ? 1f
                : 0f
        );

        if (!ready)
        {
            return;
        }

        block.SetTexture(
            AnalysisVisualizationTexturePropertyId,
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

        definition.GetVisualizationRange(
            layer.Key,
            out float minimum,
            out float maximum
        );

        block.SetVector(
            AnalysisVisualizationDisplayRangePropertyId,
            new Vector4(
                minimum,
                maximum,
                definition.VisualizationKind ==
                    TerrainAnalysisVisualizationKind.Signed
                    ? 1f
                    : 0f,
                0f
            )
        );
    }

    private static void ReleaseAnalysisVisualization()
    {
        TerrainAnalysisService
            .ReleaseTransientLayer(
                AnalysisVisualizationOwnerId
            );
    }
}
