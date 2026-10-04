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

            int ownerLevel;

            switch (binding.Role.Kind)
            {
                case TerrainClipmapRendererKind.Center:
                case TerrainClipmapRendererKind.Ring:
                    ownerLevel =
                        binding.Role.Level;

                    break;

                case TerrainClipmapRendererKind.Stitch:
                    ownerLevel =
                        binding.Role.FineLevel;

                    break;

                default:
                    errorMessage =
                        $"Renderer '{binding.Renderer.name}' has an unsupported Surface renderer role.";

                    return false;
            }

            if (
                ownerLevel < 0
                ||
                ownerLevel >= lodStates.Count
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' resolved an invalid Surface owner LOD {ownerLevel}.";

                return false;
            }

            TerrainSurfaceLodRuntimeState state =
                lodStates[
                    ownerLevel
                ];

            if (
                state == null
                ||
                !state.CacheReady
                ||
                state.ActiveCache == null
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' has no ready Surface cache for LOD{ownerLevel}.";

                return false;
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
                state.ActiveCache
            );

            block.SetVector(
                SurfaceMaskCacheOriginTilePropertyId,
                new Vector4(
                    state.ActiveCacheOrigin.x,
                    state.ActiveCacheOrigin.y,
                    0f,
                    0f
                )
            );

            block.SetVector(
                SurfaceMaskCacheSizePropertyId,
                new Vector4(
                    state.CacheWidth,
                    state.CacheHeight,
                    0f,
                    0f
                )
            );

            block.SetFloat(
                SurfaceMaskSamplesPerSidePropertyId,
                state.Descriptor
                    .SamplesPerSide
            );

            block.SetFloat(
                SurfaceMaskSampleSpacingPropertyId,
                state.Descriptor
                    .SampleSpacing
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
