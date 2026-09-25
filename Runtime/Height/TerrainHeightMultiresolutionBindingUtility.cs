using System.Collections.Generic;
using UnityEngine;

/*
 * Per-renderer multiresolution Height cache binding.
 *
 * Center/ring renderers use their own LOD cache. A stitch uses the finer
 * adjacent LOD cache through TerrainClipmapRendererRole.HeightOwnerLevel.
 */
internal static class TerrainHeightMultiresolutionBindingUtility
{
    private static readonly int HeightCachePropertyId =
        Shader.PropertyToID("_HeightCache");

    private static readonly int HeightCacheOriginTilePropertyId =
        Shader.PropertyToID("_HeightCacheOriginTile");

    private static readonly int HeightCacheSizePropertyId =
        Shader.PropertyToID("_HeightCacheSize");

    private static readonly int HeightTileSamplesPerSidePropertyId =
        Shader.PropertyToID("_HeightTileSamplesPerSide");

    private static readonly int HeightSampleSpacingPropertyId =
        Shader.PropertyToID("_HeightSampleSpacing");

    private static readonly int HeightNormalSampleSpacingFinePropertyId =
        Shader.PropertyToID("_HeightNormalSampleSpacingFine");

    private static readonly int HeightNormalSampleSpacingCoarsePropertyId =
        Shader.PropertyToID("_HeightNormalSampleSpacingCoarse");

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID("_HeightCacheReady");

    public static bool TryBind(
        Transform clipmapRoot,
        IReadOnlyList<TerrainClipmapRendererBinding> rendererBindings,
        IReadOnlyList<TerrainHeightLodRuntimeState> lodStates,
        Vector2 worldSizeXZ,
        out int boundRendererCount,
        out string errorMessage
    )
    {
        boundRendererCount = 0;
        errorMessage = "";

        if (clipmapRoot == null)
        {
            errorMessage = "Clipmap root is null.";
            return false;
        }

        if (
            rendererBindings == null
            || rendererBindings.Count == 0
        )
        {
            errorMessage = "No clipmap renderer bindings are available.";
            return false;
        }

        if (
            lodStates == null
            || lodStates.Count == 0
        )
        {
            errorMessage = "No multiresolution Height LOD states are available.";
            return false;
        }

        if (
            !TerrainClipmapWorldBoundsBindingUtility.TryBind(
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

        TerrainSurfaceSettings surfaceSettings =
            TerrainSurfaceSettings.LoadDefault();

        MaterialPropertyBlock propertyBlock =
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

            int ownerLevel =
                binding.Role.HeightOwnerLevel;

            if (
                ownerLevel < 0
                || ownerLevel >= lodStates.Count
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' resolved an invalid Height owner LOD {ownerLevel}.";

                return false;
            }

            TerrainHeightLodRuntimeState state =
                lodStates[ownerLevel];

            if (
                state == null
                || !state.CacheReady
                || state.ActiveCache == null
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' has no ready Height cache for LOD{ownerLevel}.";

                return false;
            }

            Material material =
                binding.Renderer.sharedMaterial;

            if (
                material == null
                || !material.HasProperty(HeightCachePropertyId)
                || !material.HasProperty(HeightCacheOriginTilePropertyId)
                || !material.HasProperty(HeightCacheSizePropertyId)
                || !material.HasProperty(HeightTileSamplesPerSidePropertyId)
                || !material.HasProperty(HeightSampleSpacingPropertyId)
                || !material.HasProperty(HeightCacheReadyPropertyId)
            )
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' does not use a compatible terrain Height material.";

                return false;
            }

            float fineNormalSpacing =
                state.Descriptor.SampleSpacing;

            float coarseNormalSpacing =
                fineNormalSpacing;

            if (
                binding.Role.Kind == TerrainClipmapRendererKind.Stitch
            )
            {
                int coarseLevel =
                    binding.Role.CoarseLevel;

                if (
                    coarseLevel < 0
                    || coarseLevel >= lodStates.Count
                    || lodStates[coarseLevel] == null
                )
                {
                    errorMessage =
                        $"Stitch renderer '{binding.Renderer.name}' has no adjacent coarse Height state.";

                    return false;
                }

                coarseNormalSpacing =
                    lodStates[coarseLevel]
                        .Descriptor
                        .SampleSpacing;
            }

            binding.Renderer.GetPropertyBlock(
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
                state.ActiveCache
            );

            propertyBlock.SetVector(
                HeightCacheOriginTilePropertyId,
                new Vector4(
                    state.ActiveCacheOrigin.x,
                    state.ActiveCacheOrigin.y,
                    0f,
                    0f
                )
            );

            propertyBlock.SetVector(
                HeightCacheSizePropertyId,
                new Vector4(
                    state.CacheWidth,
                    state.CacheHeight,
                    0f,
                    0f
                )
            );

            propertyBlock.SetFloat(
                HeightTileSamplesPerSidePropertyId,
                state.Descriptor.SamplesPerSide
            );

            propertyBlock.SetFloat(
                HeightSampleSpacingPropertyId,
                state.Descriptor.SampleSpacing
            );

            propertyBlock.SetFloat(
                HeightNormalSampleSpacingFinePropertyId,
                fineNormalSpacing
            );

            propertyBlock.SetFloat(
                HeightNormalSampleSpacingCoarsePropertyId,
                coarseNormalSpacing
            );

            propertyBlock.SetFloat(
                HeightCacheReadyPropertyId,
                1f
            );

            binding.Renderer.SetPropertyBlock(
                propertyBlock
            );

            boundRendererCount++;
        }

        return true;
    }
}
