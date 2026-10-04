using UnityEngine;

/*
 * Surface-mask manifest validation and per-LOD GPU cache resource helpers for
 * TerrainHeightmapStreamer. Addressable source handles are owned by the
 * independent Surface page scheduler.
 */
public partial class TerrainHeightmapStreamer
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [SerializeField]
    private TerrainSurfaceMaskManifest
        surfaceMaskManifest;

    public TerrainSurfaceMaskManifest
        SurfaceMaskManifest =>
            surfaceMaskManifest;

    // =====================================================
    // HIERARCHY CONFIGURATION
    // =====================================================

    public bool ConfigureSurfaceMaskManifest(
        TerrainSurfaceMaskManifest manifest
    )
    {
        if (surfaceMaskManifest == manifest)
        {
            return false;
        }

        surfaceMaskManifest =
            manifest;

        return true;
    }

    // =====================================================
    // VALIDATION
    // =====================================================

    private bool ValidateSurfaceMaskConfiguration()
    {
        if (surfaceMaskManifest == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainSurfaceMaskManifest is not assigned.\n\n" +
                "Open Runtime and run Bake Runtime Changes. If Runtime reports " +
                "a structural hierarchy problem, run Setup / Repair World Hierarchy.",
                this
            );

            return false;
        }

        if (!surfaceMaskManifest.isComplete)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The surface-mask manifest is incomplete.",
                this
            );

            return false;
        }

        if (
            surfaceMaskManifest.compilerVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentCompilerVersion
            ||
            surfaceMaskManifest.channelLayoutVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentChannelLayoutVersion
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The generated surface-mask format is out of date.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            !surfaceMaskManifest
                .MatchesHeightLayout(
                    heightmapManifest
                )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The surface-mask layout does not match the runtime heightmap layout.",
                this
            );

            return false;
        }

        if (
            surfaceMaskManifest
                .sourceHeightmapGenerationRevision !=
            worldSettings
                .heightmapGenerationRevision
            ||
            surfaceMaskManifest
                .sourceHeightmapSignature !=
            worldSettings
                .lastGeneratedHeightSignature
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The baked surface masks were generated from an older runtime heightmap revision.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            surfaceMaskManifest
                .surfaceMaskGenerationRevision !=
            worldSettings
                .surfaceMaskGenerationRevision
            ||
            surfaceMaskManifest
                .surfaceGenerationSignature !=
            worldSettings
                .lastGeneratedSurfaceSignature
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "WorldSettings and the surface-mask manifest do not describe the same generated surface revision.",
                this
            );

            return false;
        }

        TerrainSurfaceSettings settings =
            TerrainSurfaceSettings.LoadDefault();

        if (settings == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainSurfaceSettings could not be loaded from Resources.",
                this
            );

            return false;
        }

        string currentSettingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    settings
                );

        if (
            string.IsNullOrEmpty(
                currentSettingsSignature
            )
            ||
            currentSettingsSignature !=
                surfaceMaskManifest
                    .surfaceSettingsSignature
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainSurfaceSettings changed after the runtime surface masks were baked.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            !surfaceMaskManifest
                .streamingPyramidIsComplete
            ||
            surfaceMaskManifest
                .streamingPyramidCompilerVersion !=
            TerrainSurfaceMaskManifest
                .CurrentStreamingPyramidCompilerVersion
            ||
            surfaceMaskManifest
                .streamingPyramidPolicyVersion !=
            TerrainSurfaceStreamingPyramidPolicy
                .CurrentPolicyVersion
            ||
            surfaceMaskManifest
                .streamingSourceSurfaceMaskGenerationRevision !=
            surfaceMaskManifest
                .surfaceMaskGenerationRevision
            ||
            surfaceMaskManifest
                .streamingSourceSurfaceGenerationSignature !=
            surfaceMaskManifest
                .surfaceGenerationSignature
            ||
            surfaceMaskManifest
                .streamingGenerationRevision <=
            0
            ||
            string.IsNullOrEmpty(
                surfaceMaskManifest
                    .streamingGenerationSignature
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "Surface Streaming is missing, stale, or incomplete.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            !TerrainSurfaceStreamingPyramidPolicy
                .TryValidatePolicy(
                    worldSettings,
                    surfaceMaskManifest,
                    out string policyError
                )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize multiresolution Surface streaming.\n\n" +
                policyError,
                this
            );

            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            if (
                !TerrainSurfaceStreamingPyramidPolicy
                    .TryGetRequiredStrideForClipmapLevel(
                        worldSettings,
                        level,
                        out int sampleStride,
                        out string strideError
                    )
            )
            {
                Debug.LogError(
                    "TerrainHeightmapStreamer cannot initialize multiresolution Surface streaming.\n\n" +
                    strideError,
                    this
                );

                return false;
            }

            if (
                !surfaceMaskManifest
                    .TryGetSurfaceRepresentationDescriptor(
                        sampleStride,
                        out TerrainSurfaceStreamingLevelDescriptor
                            descriptor
                    )
            )
            {
                Debug.LogError(
                    "TerrainHeightmapStreamer cannot initialize multiresolution Surface streaming.\n\n" +
                    $"Surface representation stride {sampleStride} required by LOD{level} is unavailable.",
                    this
                );

                return false;
            }

            float expectedSpacing =
                TerrainClipmapLayoutUtility
                    .GetLODSpacing(
                        worldSettings,
                        level
                    );

            if (
                !descriptor.IsStructurallyValid
                ||
                descriptor.TextureFormat !=
                    TextureFormat.R8
                ||
                descriptor.TileGridWidth !=
                    surfaceMaskManifest
                        .tileGridWidth
                ||
                descriptor.TileGridHeight !=
                    surfaceMaskManifest
                        .tileGridHeight
                ||
                !Mathf.Approximately(
                    descriptor.TileWorldSize,
                    surfaceMaskManifest
                        .tileWorldSize
                )
                ||
                !Mathf.Approximately(
                    descriptor.SampleSpacing,
                    expectedSpacing
                )
            )
            {
                Debug.LogError(
                    "TerrainHeightmapStreamer cannot initialize multiresolution Surface streaming.\n\n" +
                    $"Surface representation stride {sampleStride} for LOD{level} is incompatible with the runtime clipmap.",
                    this
                );

                return false;
            }
        }

        if (
            !SystemInfo.SupportsTextureFormat(
                TextureFormat.R8
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The current graphics device does not support TextureFormat.R8.",
                this
            );

            return false;
        }

        return true;
    }

    // =====================================================
    // GPU CACHE RESOURCE
    // =====================================================

    private static Texture2DArray
        CreateSurfaceMaskCacheTexture(
            string textureName,
            int samplesPerSide,
            int sliceCount
        )
    {
        Texture2DArray cache =
            new Texture2DArray(
                samplesPerSide,
                samplesPerSide,
                sliceCount,
                TextureFormat.R8,
                false,
                true
            );

        cache.name =
            textureName;

        cache.wrapMode =
            TextureWrapMode.Clamp;

        cache.filterMode =
            FilterMode.Bilinear;

        cache.anisoLevel =
            0;

        return cache;
    }

    // =====================================================
    // SHADER BINDING
    // =====================================================

    private bool BindSurfaceMaskCacheToClipmapRenderers(
        out string errorMessage
    )
    {
        return
            TerrainSurfaceMaskBindingUtility
                .TryBind(
                    transform,
                    multiresolutionRendererBindings,
                    surfaceLodStates,
                    out _,
                    out errorMessage
                );
    }

    private void DisableSurfaceMaskOnClipmapRenderers()
    {
        TerrainSurfaceMaskBindingUtility
            .Disable(
                transform
            );
    }
}
