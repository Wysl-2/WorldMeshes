using UnityEngine;

/*
 * Low-level Height shader readiness reset.
 *
 * Production Height binding is semantic and per-renderer through
 * TerrainHeightMultiresolutionBindingUtility. This utility only disables
 * existing Height bindings while preserving unrelated property-block state.
 */
public static class TerrainHeightCacheBindingUtility
{
    private static readonly int HeightCachePropertyId =
        Shader.PropertyToID(
            "_HeightCache"
        );

    private static readonly int HeightCacheOriginTilePropertyId =
        Shader.PropertyToID(
            "_HeightCacheOriginTile"
        );

    private static readonly int HeightCacheSizePropertyId =
        Shader.PropertyToID(
            "_HeightCacheSize"
        );

    private static readonly int HeightTileSamplesPerSidePropertyId =
        Shader.PropertyToID(
            "_HeightTileSamplesPerSide"
        );

    private static readonly int HeightSampleSpacingPropertyId =
        Shader.PropertyToID(
            "_HeightSampleSpacing"
        );

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID(
            "_HeightCacheReady"
        );

    public static int Disable(
        Transform clipmapRoot
    )
    {
        if (clipmapRoot == null)
        {
            return 0;
        }

        MeshRenderer[] renderers =
            clipmapRoot
                .GetComponentsInChildren<MeshRenderer>(
                    true
                );

        if (renderers == null)
        {
            return 0;
        }

        MaterialPropertyBlock propertyBlock =
            new MaterialPropertyBlock();

        int disabledRendererCount =
            0;

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (!IsClipmapTerrainRenderer(meshRenderer))
            {
                continue;
            }

            meshRenderer.GetPropertyBlock(
                propertyBlock
            );

            propertyBlock.SetFloat(
                HeightCacheReadyPropertyId,
                0f
            );

            meshRenderer.SetPropertyBlock(
                propertyBlock
            );

            disabledRendererCount++;
        }

        return disabledRendererCount;
    }

    private static bool IsClipmapTerrainRenderer(
        MeshRenderer meshRenderer
    )
    {
        if (
            meshRenderer == null
            || meshRenderer.sharedMaterial == null
        )
        {
            return false;
        }

        Material material =
            meshRenderer.sharedMaterial;

        return
            material.HasProperty(
                HeightCachePropertyId
            )
            && material.HasProperty(
                HeightCacheOriginTilePropertyId
            )
            && material.HasProperty(
                HeightCacheSizePropertyId
            )
            && material.HasProperty(
                HeightTileSamplesPerSidePropertyId
            )
            && material.HasProperty(
                HeightSampleSpacingPropertyId
            )
            && material.HasProperty(
                HeightCacheReadyPropertyId
            );
    }
}
