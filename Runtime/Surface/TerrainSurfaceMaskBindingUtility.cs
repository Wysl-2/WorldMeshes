using UnityEngine;

/*
 * Binds the synchronized runtime surface-mask Texture2DArray to clipmap
 * renderers without disturbing unrelated MaterialPropertyBlock state.
 */
public static class TerrainSurfaceMaskBindingUtility
{
    private static readonly int SurfaceMaskCachePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCache"
        );

    private static readonly int SurfaceMaskCacheOriginTilePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCacheOriginTile"
        );

    private static readonly int SurfaceMaskCacheSizePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCacheSize"
        );

    private static readonly int SurfaceMaskSamplesPerSidePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskSamplesPerSide"
        );

    private static readonly int SurfaceMaskSampleSpacingPropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskSampleSpacing"
        );

    private static readonly int SurfaceMaskCacheReadyPropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCacheReady"
        );

    public static bool TryBind(
        Transform clipmapRoot,
        Texture surfaceMaskCache,
        Vector2Int cacheOriginTile,
        Vector2Int cacheSize,
        int samplesPerSide,
        float sampleSpacing,
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

        if (surfaceMaskCache == null)
        {
            errorMessage =
                "Surface-mask cache texture is null.";

            return false;
        }

        if (
            cacheSize.x <= 0
            ||
            cacheSize.y <= 0
        )
        {
            errorMessage =
                "Surface-mask cache dimensions are invalid.";

            return false;
        }

        if (samplesPerSide <= 1)
        {
            errorMessage =
                "Surface-mask sample count is invalid.";

            return false;
        }

        if (
            float.IsNaN(
                sampleSpacing
            )
            ||
            float.IsInfinity(
                sampleSpacing
            )
            ||
            sampleSpacing <= 0f
        )
        {
            errorMessage =
                "Surface-mask sample spacing is invalid.";

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

        MaterialPropertyBlock block =
            new MaterialPropertyBlock();

        Vector4 origin =
            new Vector4(
                cacheOriginTile.x,
                cacheOriginTile.y,
                0f,
                0f
            );

        Vector4 dimensions =
            new Vector4(
                cacheSize.x,
                cacheSize.y,
                0f,
                0f
            );

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
                block
            );

            block.SetTexture(
                SurfaceMaskCachePropertyId,
                surfaceMaskCache
            );

            block.SetVector(
                SurfaceMaskCacheOriginTilePropertyId,
                origin
            );

            block.SetVector(
                SurfaceMaskCacheSizePropertyId,
                dimensions
            );

            block.SetFloat(
                SurfaceMaskSamplesPerSidePropertyId,
                samplesPerSide
            );

            block.SetFloat(
                SurfaceMaskSampleSpacingPropertyId,
                sampleSpacing
            );

            /*
             * Set readiness last so the shader never observes an enabled
             * surface cache with incomplete layout metadata.
             */
            block.SetFloat(
                SurfaceMaskCacheReadyPropertyId,
                1f
            );

            renderer.SetPropertyBlock(
                block
            );

            boundRendererCount++;
        }

        if (boundRendererCount <= 0)
        {
            errorMessage =
                "No clipmap terrain renderer exposes the Stage 8 surface-mask shader properties.";

            return false;
        }

        return true;
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

        MaterialPropertyBlock block =
            new MaterialPropertyBlock();

        int count =
            0;

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
                block
            );

            /*
             * Do not pass null to SetTexture. The ready flag is authoritative.
             */
            block.SetFloat(
                SurfaceMaskCacheReadyPropertyId,
                0f
            );

            renderer.SetPropertyBlock(
                block
            );

            count++;
        }

        return count;
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
                SurfaceMaskCachePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCacheOriginTilePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCacheSizePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskSamplesPerSidePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskSampleSpacingPropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCacheReadyPropertyId
            );
    }
}
