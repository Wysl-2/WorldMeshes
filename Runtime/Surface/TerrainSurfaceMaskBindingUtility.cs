using System.Collections.Generic;
using UnityEngine;

/*
 * Per-renderer multiresolution Surface cache binding.
 *
 * Center/ring renderers use their own LOD Surface cache. A stitch uses the
 * fine adjacent LOD cache until dual-resolution stitch sampling is enabled.
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

    private static readonly int SurfaceMaskCoarseCachePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCoarseCache"
        );

    private static readonly int SurfaceMaskCoarseCacheOriginTilePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCoarseCacheOriginTile"
        );

    private static readonly int SurfaceMaskCoarseCacheSizePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCoarseCacheSize"
        );

    private static readonly int SurfaceMaskCoarseSamplesPerSidePropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCoarseSamplesPerSide"
        );

    private static readonly int SurfaceMaskCoarseSampleSpacingPropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCoarseSampleSpacing"
        );

    private static readonly int SurfaceMaskDualResolutionEnabledPropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskDualResolutionEnabled"
        );

    private static readonly int SurfaceMaskCacheReadyPropertyId =
        Shader.PropertyToID(
            "_SurfaceMaskCacheReady"
        );

    internal static bool TryBind(
        Transform clipmapRoot,
        IReadOnlyList<TerrainClipmapRendererBinding>
            rendererBindings,
        IReadOnlyList<TerrainSurfaceLodRuntimeState>
            lodStates,
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

        if (
            rendererBindings == null
            ||
            rendererBindings.Count == 0
        )
        {
            errorMessage =
                "No clipmap renderer bindings are available.";

            return false;
        }

        if (
            lodStates == null
            ||
            lodStates.Count == 0
        )
        {
            errorMessage =
                "No multiresolution Surface LOD states are available.";

            return false;
        }

        MaterialPropertyBlock block =
            new MaterialPropertyBlock();

        for (
            int index = 0;
            index < rendererBindings.Count;
            index++
        )
        {
            TerrainClipmapRendererBinding binding =
                rendererBindings[index];

            if (!binding.IsValid)
            {
                errorMessage =
                    "A clipmap renderer binding is invalid.";

                return false;
            }

            int primaryLevel;
            int coarseLevel =
                -1;

            bool dualResolution =
                binding.Role.Kind ==
                TerrainClipmapRendererKind.Stitch;

            switch (binding.Role.Kind)
            {
                case TerrainClipmapRendererKind.Center:
                case TerrainClipmapRendererKind.Ring:
                    primaryLevel =
                        binding.Role.Level;

                    break;

                case TerrainClipmapRendererKind.Stitch:
                    primaryLevel =
                        binding.Role.FineLevel;

                    coarseLevel =
                        binding.Role.CoarseLevel;

                    break;

                default:
                    errorMessage =
                        $"Renderer '{binding.Renderer.name}' has an unsupported Surface renderer role.";

                    return false;
            }

            if (
                primaryLevel < 0
                ||
                primaryLevel >= lodStates.Count
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' resolved an invalid primary Surface owner LOD {primaryLevel}.";

                return false;
            }

            TerrainSurfaceLodRuntimeState primaryState =
                lodStates[
                    primaryLevel
                ];

            if (
                primaryState == null
                ||
                !primaryState.CacheReady
                ||
                primaryState.ActiveCache == null
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' has no ready primary Surface cache for LOD{primaryLevel}.";

                return false;
            }

            TerrainSurfaceLodRuntimeState coarseState =
                null;

            if (dualResolution)
            {
                if (
                    coarseLevel < 0
                    ||
                    coarseLevel >= lodStates.Count
                )
                {
                    errorMessage =
                        $"Renderer '{binding.Renderer.name}' resolved an invalid coarse Surface owner LOD {coarseLevel}.";

                    return false;
                }

                coarseState =
                    lodStates[
                        coarseLevel
                    ];

                if (
                    coarseState == null
                    ||
                    !coarseState.CacheReady
                    ||
                    coarseState.ActiveCache == null
                )
                {
                    errorMessage =
                        $"Renderer '{binding.Renderer.name}' has no ready coarse Surface cache for LOD{coarseLevel}.";

                    return false;
                }
            }

            Material material =
                binding.Renderer
                    .sharedMaterial;

            if (
                material == null
                ||
                !material.HasProperty(
                    SurfaceMaskCachePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCacheOriginTilePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCacheSizePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskSamplesPerSidePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskSampleSpacingPropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCoarseCachePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCoarseCacheOriginTilePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCoarseCacheSizePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCoarseSamplesPerSidePropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCoarseSampleSpacingPropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskDualResolutionEnabledPropertyId
                )
                ||
                !material.HasProperty(
                    SurfaceMaskCacheReadyPropertyId
                )
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' does not use a compatible terrain Surface material.";

                return false;
            }

            binding.Renderer
                .GetPropertyBlock(
                    block
                );

            block.SetTexture(
                SurfaceMaskCachePropertyId,
                primaryState.ActiveCache
            );

            block.SetVector(
                SurfaceMaskCacheOriginTilePropertyId,
                new Vector4(
                    primaryState.ActiveCacheOrigin.x,
                    primaryState.ActiveCacheOrigin.y,
                    0f,
                    0f
                )
            );

            block.SetVector(
                SurfaceMaskCacheSizePropertyId,
                new Vector4(
                    primaryState.CacheWidth,
                    primaryState.CacheHeight,
                    0f,
                    0f
                )
            );

            block.SetFloat(
                SurfaceMaskSamplesPerSidePropertyId,
                primaryState.Descriptor
                    .SamplesPerSide
            );

            block.SetFloat(
                SurfaceMaskSampleSpacingPropertyId,
                primaryState.Descriptor
                    .SampleSpacing
            );

            if (dualResolution)
            {
                block.SetTexture(
                    SurfaceMaskCoarseCachePropertyId,
                    coarseState.ActiveCache
                );

                block.SetVector(
                    SurfaceMaskCoarseCacheOriginTilePropertyId,
                    new Vector4(
                        coarseState.ActiveCacheOrigin.x,
                        coarseState.ActiveCacheOrigin.y,
                        0f,
                        0f
                    )
                );

                block.SetVector(
                    SurfaceMaskCoarseCacheSizePropertyId,
                    new Vector4(
                        coarseState.CacheWidth,
                        coarseState.CacheHeight,
                        0f,
                        0f
                    )
                );

                block.SetFloat(
                    SurfaceMaskCoarseSamplesPerSidePropertyId,
                    coarseState.Descriptor
                        .SamplesPerSide
                );

                block.SetFloat(
                    SurfaceMaskCoarseSampleSpacingPropertyId,
                    coarseState.Descriptor
                        .SampleSpacing
                );
            }

            /*
             * Enable dual-resolution sampling only after both required
             * representation bindings are complete.
             */
            block.SetFloat(
                SurfaceMaskDualResolutionEnabledPropertyId,
                dualResolution
                    ? 1f
                    : 0f
            );

            /*
             * Set readiness last so the shader never observes an enabled
             * Surface cache with incomplete layout metadata.
             */
            block.SetFloat(
                SurfaceMaskCacheReadyPropertyId,
                1f
            );

            binding.Renderer
                .SetPropertyBlock(
                    block
                );

            boundRendererCount++;
        }

        return
            boundRendererCount ==
            rendererBindings.Count;
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

            block.SetFloat(
                SurfaceMaskDualResolutionEnabledPropertyId,
                0f
            );

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
                SurfaceMaskCoarseCachePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCoarseCacheOriginTilePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCoarseCacheSizePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCoarseSamplesPerSidePropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCoarseSampleSpacingPropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskDualResolutionEnabledPropertyId
            )
            &&
            material.HasProperty(
                SurfaceMaskCacheReadyPropertyId
            );
    }
}
