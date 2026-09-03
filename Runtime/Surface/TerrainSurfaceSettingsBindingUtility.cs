using UnityEngine;

/*
 * Runtime-safe shader binding for TerrainSurfaceSettings.
 *
 * The ScriptableObject is the authoritative suitability configuration.
 * Shader properties remain transport/binding values only.
 */
public static class TerrainSurfaceSettingsBindingUtility
{
    private static readonly int ScreeSlopeMinPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopeMin"
        );

    private static readonly int ScreeSlopePreferredMinPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopePreferredMin"
        );

    private static readonly int ScreeSlopePreferredMaxPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopePreferredMax"
        );

    private static readonly int ScreeSlopeMaxPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopeMax"
        );

    private static readonly int ScreeCurvatureScalePropertyId =
        Shader.PropertyToID(
            "_ScreeCurvatureScale"
        );

    private static readonly int ScreeConvexRejectStartPropertyId =
        Shader.PropertyToID(
            "_ScreeConvexRejectStart"
        );

    private static readonly int ScreeConvexRejectEndPropertyId =
        Shader.PropertyToID(
            "_ScreeConvexRejectEnd"
        );

    private static readonly int ScreeGeologyScalePropertyId =
        Shader.PropertyToID(
            "_ScreeGeologyScale"
        );

    private static readonly int ScreeGeologyStrengthPropertyId =
        Shader.PropertyToID(
            "_ScreeGeologyStrength"
        );

    public static bool TryApplyToPropertyBlock(
        MaterialPropertyBlock propertyBlock,
        TerrainSurfaceSettings settings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (propertyBlock == null)
        {
            errorMessage =
                "MaterialPropertyBlock is null.";

            return false;
        }

        if (settings == null)
        {
            errorMessage =
                "TerrainSurfaceSettings is null.";

            return false;
        }

        ScreeSettings scree =
            settings.Scree;

        if (scree == null)
        {
            errorMessage =
                "TerrainSurfaceSettings does not contain ScreeSettings.";

            return false;
        }

        propertyBlock.SetFloat(
            ScreeSlopeMinPropertyId,
            Mathf.Clamp(
                scree.slopeMin,
                0f,
                90f
            )
        );

        propertyBlock.SetFloat(
            ScreeSlopePreferredMinPropertyId,
            Mathf.Clamp(
                scree.slopePreferredMin,
                0f,
                90f
            )
        );

        propertyBlock.SetFloat(
            ScreeSlopePreferredMaxPropertyId,
            Mathf.Clamp(
                scree.slopePreferredMax,
                0f,
                90f
            )
        );

        propertyBlock.SetFloat(
            ScreeSlopeMaxPropertyId,
            Mathf.Clamp(
                scree.slopeMax,
                0f,
                90f
            )
        );

        propertyBlock.SetFloat(
            ScreeCurvatureScalePropertyId,
            Mathf.Clamp(
                scree.curvatureScale,
                1f,
                256f
            )
        );

        propertyBlock.SetFloat(
            ScreeConvexRejectStartPropertyId,
            Mathf.Clamp(
                scree.convexRejectStart,
                0f,
                0.5f
            )
        );

        propertyBlock.SetFloat(
            ScreeConvexRejectEndPropertyId,
            Mathf.Clamp(
                scree.convexRejectEnd,
                0f,
                0.5f
            )
        );

        propertyBlock.SetFloat(
            ScreeGeologyScalePropertyId,
            Mathf.Clamp(
                scree.geologyScale,
                1f,
                512f
            )
        );

        propertyBlock.SetFloat(
            ScreeGeologyStrengthPropertyId,
            Mathf.Clamp01(
                scree.geologyStrength
            )
        );

        return true;
    }

    public static bool TryBind(
        Transform clipmapRoot,
        TerrainSurfaceSettings settings,
        out int boundRendererCount,
        out string errorMessage
    )
    {
        boundRendererCount =
            0;

        errorMessage =
            "";

        if (clipmapRoot == null)
        {
            errorMessage =
                "Clipmap root is null.";

            return false;
        }

        if (settings == null)
        {
            errorMessage =
                "TerrainSurfaceSettings is null.";

            return false;
        }

        MeshRenderer[] renderers =
            clipmapRoot
                .GetComponentsInChildren<MeshRenderer>(
                    true
                );

        if (
            renderers == null
            ||
            renderers.Length == 0
        )
        {
            errorMessage =
                "No child MeshRenderer components were found under the clipmap root.";

            return false;
        }

        MaterialPropertyBlock propertyBlock =
            new MaterialPropertyBlock();

        foreach (
            MeshRenderer renderer
            in renderers
        )
        {
            if (!IsCompatibleTerrainRenderer(renderer))
            {
                continue;
            }

            renderer.GetPropertyBlock(
                propertyBlock
            );

            if (
                !TryApplyToPropertyBlock(
                    propertyBlock,
                    settings,
                    out errorMessage
                )
            )
            {
                return false;
            }

            renderer.SetPropertyBlock(
                propertyBlock
            );

            boundRendererCount++;
        }

        if (boundRendererCount <= 0)
        {
            errorMessage =
                "No clipmap terrain renderer exposes the expected Scree suitability shader properties.";

            return false;
        }

        return true;
    }

    public static bool TryBindDefault(
        Transform clipmapRoot,
        out int boundRendererCount,
        out string errorMessage
    )
    {
        TerrainSurfaceSettings settings =
            TerrainSurfaceSettings.LoadDefault();

        if (settings == null)
        {
            boundRendererCount =
                0;

            errorMessage =
                "TerrainSurfaceSettings could not be loaded from Resources.";

            return false;
        }

        return
            TryBind(
                clipmapRoot,
                settings,
                out boundRendererCount,
                out errorMessage
            );
    }

    private static bool IsCompatibleTerrainRenderer(
        MeshRenderer renderer
    )
    {
        if (
            renderer == null
            ||
            renderer.sharedMaterial == null
        )
        {
            return false;
        }

        Material material =
            renderer.sharedMaterial;

        return
            material.HasProperty(
                ScreeSlopeMinPropertyId
            )
            &&
            material.HasProperty(
                ScreeGeologyStrengthPropertyId
            );
    }
}
