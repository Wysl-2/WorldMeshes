using UnityEngine;

public static class TerrainHeightCacheBindingUtility
{
    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

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

    private static readonly int WorldSizeXZPropertyId =
        Shader.PropertyToID(
            "_WorldSizeXZ"
        );

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID(
            "_HeightCacheReady"
        );

    // =====================================================
    // BIND
    // =====================================================

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

        Vector4 worldSize =
            new Vector4(
                worldSizeXZ.x,
                worldSizeXZ.y,
                0f,
                0f
            );

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (
                !IsClipmapTerrainRenderer(
                    meshRenderer
                )
            )
            {
                continue;
            }

            /*
             * Preserve unrelated renderer overrides.
             *
             * TerrainClipmapController uses the same
             * MaterialPropertyBlock mechanism for
             * _ClipmapTransitionOffset.
             */
            meshRenderer.GetPropertyBlock(
                propertyBlock
            );

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

            propertyBlock.SetVector(
                WorldSizeXZPropertyId,
                worldSize
            );

            /*
             * Set readiness last. At this point the texture and
             * all cache-layout metadata are valid.
             */
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

    // =====================================================
    // DISABLE
    // =====================================================

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
            if (
                !IsClipmapTerrainRenderer(
                    meshRenderer
                )
            )
            {
                continue;
            }

            meshRenderer.GetPropertyBlock(
                propertyBlock
            );

            /*
             * There is no need to clear the texture reference.
             * Setting readiness to zero prevents the shader from
             * sampling the cache, while preserving every unrelated
             * property-block override.
             */
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

    // =====================================================
    // MATERIAL COMPATIBILITY
    // =====================================================

    private static bool IsClipmapTerrainRenderer(
        MeshRenderer meshRenderer
    )
    {
        if (
            meshRenderer == null
            ||
            meshRenderer.sharedMaterial == null
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
            &&
            material.HasProperty(
                HeightCacheOriginTilePropertyId
            )
            &&
            material.HasProperty(
                HeightCacheSizePropertyId
            )
            &&
            material.HasProperty(
                HeightTileSamplesPerSidePropertyId
            )
            &&
            material.HasProperty(
                HeightSampleSpacingPropertyId
            )
            &&
            material.HasProperty(
                WorldSizeXZPropertyId
            )
            &&
            material.HasProperty(
                HeightCacheReadyPropertyId
            );
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
}
