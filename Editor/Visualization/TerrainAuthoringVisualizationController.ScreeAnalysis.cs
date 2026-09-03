using UnityEditor;
using UnityEngine;

/*
 * Stage 6 cached Scree Suitability analysis integration.
 *
 * Edit-mode Lit/Scree visualization uses cached Terrain Analysis:
 *
 *     Slope
 *     Curvature(_ScreeCurvatureScale)
 *
 * Geology remains a suitability-layer calculation in HLSL.
 *
 * Runtime continues to use the direct slope/curvature fallback because the
 * Terrain Analysis generation backend is editor-only at this stage.
 */
public static partial class TerrainAuthoringVisualizationController
{
    private const string ScreeCurvatureAnalysisOwnerId =
        "WorldMeshes.ScreeSuitability.Curvature";

    private static readonly int ScreeCurvatureScaleMaterialPropertyId =
        Shader.PropertyToID(
            "_ScreeCurvatureScale"
        );

    private static readonly int ScreeSlopeAnalysisTexturePropertyId =
        Shader.PropertyToID(
            "_AuthoringScreeSlopeAnalysis"
        );

    private static readonly int ScreeCurvatureAnalysisTexturePropertyId =
        Shader.PropertyToID(
            "_AuthoringScreeCurvatureAnalysis"
        );

    private static readonly int ScreeAnalysisReadyPropertyId =
        Shader.PropertyToID(
            "_AuthoringScreeAnalysisReady"
        );

    private static bool TryPrepareScreeAnalysis(
        out TerrainAnalysisLayer slopeLayer,
        out TerrainAnalysisLayer curvatureLayer,
        out string errorMessage
    )
    {
        slopeLayer =
            null;

        curvatureLayer =
            null;

        errorMessage =
            "";

        Material terrainMaterial =
            AssetDatabase.LoadAssetAtPath<Material>(
                WorldMeshesPaths
                    .ClipmapTerrainMaterialPath
            );

        if (terrainMaterial == null)
        {
            errorMessage =
                "The clipmap terrain material could not be loaded from:\n" +
                WorldMeshesPaths.ClipmapTerrainMaterialPath;

            ReleaseScreeSuitabilityAnalysis();

            return false;
        }

        if (
            !terrainMaterial.HasProperty(
                ScreeCurvatureScaleMaterialPropertyId
            )
        )
        {
            errorMessage =
                "The clipmap terrain material does not expose _ScreeCurvatureScale.";

            ReleaseScreeSuitabilityAnalysis();

            return false;
        }

        float curvatureScale =
            Mathf.Max(
                0.25f,
                terrainMaterial.GetFloat(
                    ScreeCurvatureScaleMaterialPropertyId
                )
            );

        slopeLayer =
            TerrainAnalysisService
                .RequestLayer(
                    TerrainAnalysisKey.Slope
                );

        if (!IsReadyAnalysisLayer(slopeLayer))
        {
            errorMessage =
                GetAnalysisLayerError(
                    slopeLayer,
                    "Slope analysis could not be generated for Scree Suitability."
                );

            ReleaseScreeSuitabilityAnalysis();

            return false;
        }

        curvatureLayer =
            TerrainAnalysisService
                .RequestTransientLayer(
                    ScreeCurvatureAnalysisOwnerId,
                    TerrainAnalysisKey.Curvature(
                        curvatureScale
                    )
                );

        if (!IsReadyAnalysisLayer(curvatureLayer))
        {
            errorMessage =
                GetAnalysisLayerError(
                    curvatureLayer,
                    "Curvature analysis could not be generated for Scree Suitability."
                );

            ReleaseScreeSuitabilityAnalysis();

            return false;
        }

        if (
            !AnalysisLayoutsMatch(
                slopeLayer,
                curvatureLayer
            )
        )
        {
            errorMessage =
                "Slope and Curvature analysis layers do not share the same terrain cache layout.";

            ReleaseScreeSuitabilityAnalysis();

            return false;
        }

        return true;
    }

    private static void ApplyScreeAnalysisProperties(
        MaterialPropertyBlock block,
        TerrainAnalysisLayer slopeLayer,
        TerrainAnalysisLayer curvatureLayer
    )
    {
        bool ready =
            IsReadyAnalysisLayer(
                slopeLayer
            )
            &&
            IsReadyAnalysisLayer(
                curvatureLayer
            )
            &&
            AnalysisLayoutsMatch(
                slopeLayer,
                curvatureLayer
            );

        block.SetFloat(
            ScreeAnalysisReadyPropertyId,
            ready
                ? 1f
                : 0f
        );

        if (!ready)
        {
            return;
        }

        block.SetTexture(
            ScreeSlopeAnalysisTexturePropertyId,
            slopeLayer.Texture
        );

        block.SetTexture(
            ScreeCurvatureAnalysisTexturePropertyId,
            curvatureLayer.Texture
        );

        /*
         * Stage 5 introduced shared authoring analysis layout properties.
         * Slope and Curvature are generated from the same authoritative
         * preview cache and are required above to have identical layout.
         */
        block.SetVector(
            AnalysisCacheOriginTilePropertyId,
            new Vector4(
                slopeLayer.CacheOriginTile.x,
                slopeLayer.CacheOriginTile.y,
                0f,
                0f
            )
        );

        block.SetVector(
            AnalysisCacheSizePropertyId,
            new Vector4(
                slopeLayer.CacheSize.x,
                slopeLayer.CacheSize.y,
                0f,
                0f
            )
        );

        block.SetFloat(
            AnalysisSamplesPerSidePropertyId,
            slopeLayer.SamplesPerSide
        );

        block.SetFloat(
            AnalysisSampleSpacingPropertyId,
            slopeLayer.SampleSpacing
        );

        block.SetVector(
            AnalysisWorldSizeXZPropertyId,
            new Vector4(
                slopeLayer.WorldSizeXZ.x,
                slopeLayer.WorldSizeXZ.y,
                0f,
                0f
            )
        );
    }

    private static void ReleaseScreeSuitabilityAnalysis()
    {
        /*
         * Slope is a scale-independent persistent layer that may be shared by
         * future consumers. Only the scale-editable Scree Curvature scratch
         * layer is owner-scoped and released here.
         */
        TerrainAnalysisService
            .ReleaseTransientLayer(
                ScreeCurvatureAnalysisOwnerId
            );
    }

    private static bool IsReadyAnalysisLayer(
        TerrainAnalysisLayer layer
    )
    {
        return
            layer != null
            &&
            layer.IsReady
            &&
            layer.Texture != null
            &&
            layer.Texture.IsCreated();
    }

    private static string GetAnalysisLayerError(
        TerrainAnalysisLayer layer,
        string fallback
    )
    {
        return
            layer != null
            &&
            !string.IsNullOrEmpty(
                layer.ErrorMessage
            )
                ? layer.ErrorMessage
                : fallback;
    }

    private static bool AnalysisLayoutsMatch(
        TerrainAnalysisLayer first,
        TerrainAnalysisLayer second
    )
    {
        if (
            first == null
            ||
            second == null
        )
        {
            return false;
        }

        return
            first.CacheOriginTile ==
                second.CacheOriginTile
            &&
            first.CacheSize ==
                second.CacheSize
            &&
            first.SamplesPerSide ==
                second.SamplesPerSide
            &&
            Mathf.Approximately(
                first.SampleSpacing,
                second.SampleSpacing
            )
            &&
            Mathf.Approximately(
                first.WorldSizeXZ.x,
                second.WorldSizeXZ.x
            )
            &&
            Mathf.Approximately(
                first.WorldSizeXZ.y,
                second.WorldSizeXZ.y
            );
    }
}
