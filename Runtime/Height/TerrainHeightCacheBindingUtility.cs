using UnityEngine;

/*
 * Low-level single-cache Height shader binding and readiness reset.
 *
 * Edit-mode authoring preview uses the single-cache binding path because it
 * owns one resident preview cache. Runtime multiresolution terrain binding is
 * semantic and per-renderer through TerrainHeightMultiresolutionBindingUtility.
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

    public static bool TryBind(
        Transform clipmapRoot,
        Texture heightCache,
        Vector2Int cacheOriginTile,
        Vector2Int cacheSize,
        int heightTileSamplesPerSide,
        float heightSampleSpacing,
        Vector2 worldSizeXZ,
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

        if (heightCache == null)
        {
            errorMessage =
                "Height cache texture is null.";

            return false;
        }

        if (
            cacheSize.x <= 0
            ||
            cacheSize.y <= 0
        )
        {
            errorMessage =
                "Height cache dimensions must be greater than zero.";

            return false;
        }

        if (heightTileSamplesPerSide <= 1)
        {
            errorMessage =
                "Height tile sample count is invalid.";

            return false;
        }

        if (
            !IsFinite(
                heightSampleSpacing
            )
            ||
            heightSampleSpacing <= 0f
        )
        {
            errorMessage =
                "Height sample spacing is invalid.";

            return false;
        }

        if (
            !IsFinite(
                worldSizeXZ.x
            )
            ||
            !IsFinite(
                worldSizeXZ.y
            )
            ||
            worldSizeXZ.x <= 0f
            ||
            worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "World size is invalid.";

            return false;
        }

        if (
            !TerrainClipmapWorldBoundsBindingUtility
                .TryBind(
                    clipmapRoot,
                    worldSizeXZ,
                    out _,
                    out string worldBoundsError
                )
        )
        {
            errorMessage =
                "The clipmap world bounds could not be bound.\n\n" +
                worldBoundsError;

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
                "No child MeshRenderer components were found " +
                "under the clipmap root.";

            return false;
        }

        MaterialPropertyBlock propertyBlock =
            new MaterialPropertyBlock();

        TerrainSurfaceSettings surfaceSettings =
            TerrainSurfaceSettings.LoadDefault();

        Vector4 cacheOrigin =
            new Vector4(
                cacheOriginTile.x,
                cacheOriginTile.y,
                0f,
                0f
            );

        Vector4 cacheDimensions =
            new Vector4(
                cacheSize.x,
                cacheSize.y,
                0f,
                0f
            );

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

            if (surfaceSettings != null)
            {
                TerrainSurfaceSettingsBindingUtility
                    .TryApplyToPropertyBlock(
                        propertyBlock,
                        surfaceSettings,
                        out _
                    );
            }

            propertyBlock.SetTexture(
                HeightCachePropertyId,
                heightCache
            );

            propertyBlock.SetVector(
                HeightCacheOriginTilePropertyId,
                cacheOrigin
            );

            propertyBlock.SetVector(
                HeightCacheSizePropertyId,
                cacheDimensions
            );

            propertyBlock.SetFloat(
                HeightTileSamplesPerSidePropertyId,
                heightTileSamplesPerSide
            );

            propertyBlock.SetFloat(
                HeightSampleSpacingPropertyId,
                heightSampleSpacing
            );

            propertyBlock.SetFloat(
                HeightCacheReadyPropertyId,
                1f
            );

            meshRenderer.SetPropertyBlock(
                propertyBlock
            );

            boundRendererCount++;
        }

        if (boundRendererCount <= 0)
        {
            errorMessage =
                "No clipmap terrain renderer was found using a " +
                "material with the expected height-cache shader " +
                "properties.";

            return false;
        }

        return true;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

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
