using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public sealed class TerrainAnalysisGpuGenerator :
    ITerrainAnalysisGenerator
{
    public const string ComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/TerrainAnalysis.compute";

    private const string SlopeKernelName =
        "GenerateSlope";

    private const string CurvatureKernelName =
        "GenerateCurvature";

    private static readonly TerrainAnalysisGpuGenerator Instance =
        new TerrainAnalysisGpuGenerator();

    private ComputeShader computeShader;

    private int slopeKernel = -1;
    private int curvatureKernel = -1;

    private uint slopeThreadGroupSizeX;
    private uint slopeThreadGroupSizeY;
    private uint curvatureThreadGroupSizeX;
    private uint curvatureThreadGroupSizeY;

    static TerrainAnalysisGpuGenerator()
    {
        TerrainAnalysisService.RegisterGenerator(
            Instance
        );

        TerrainAuthoringPreviewService.PreviewStateChanged +=
            OnPreviewStateChanged;

        AssemblyReloadEvents.beforeAssemblyReload +=
            Shutdown;

        EditorApplication.quitting +=
            Shutdown;
    }

    public bool TryGenerate(
        TerrainAnalysisKey key,
        out TerrainAnalysisGenerationResult result,
        out string errorMessage
    )
    {
        result = default;
        errorMessage = "";

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Terrain analysis authoring generation is unavailable " +
                "while entering or running Play Mode.";

            return false;
        }

        if (
            !TryValidateKey(
                key,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (!TryPrepare(out errorMessage))
        {
            return false;
        }

        if (
            !TerrainAuthoringPreviewService.TryGetTerrainAnalysisSource(
                out RenderTexture heightCache,
                out Vector2Int cacheOriginTile,
                out Vector2Int cacheSize,
                out int samplesPerSide,
                out float sampleSpacing,
                out Vector2 worldSizeXZ,
                out string sourceSignature
            )
        )
        {
            errorMessage =
                "Terrain analysis requires a ready Terrain Authoring " +
                "Height Preview.";

            return false;
        }

        int sliceCount =
            cacheSize.x *
            cacheSize.y;

        if (
            heightCache == null ||
            !heightCache.IsCreated() ||
            cacheSize.x <= 0 ||
            cacheSize.y <= 0 ||
            sliceCount <= 0 ||
            samplesPerSide <= 1 ||
            sampleSpacing <= 0f ||
            worldSizeXZ.x <= 0f ||
            worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "The Terrain Authoring Height Preview reported an invalid " +
                "analysis source layout.";

            return false;
        }

        RenderTexture output =
            CreateAnalysisTexture(
                key,
                samplesPerSide,
                sliceCount
            );

        if (
            output == null ||
            !output.IsCreated()
        )
        {
            DestroyCandidate(output);

            errorMessage =
                "The GPU terrain-analysis texture could not be created.";

            return false;
        }

        int kernel;
        uint threadGroupSizeX;
        uint threadGroupSizeY;

        switch (key.Type)
        {
            case TerrainAnalysisType.Slope:
                kernel = slopeKernel;
                threadGroupSizeX = slopeThreadGroupSizeX;
                threadGroupSizeY = slopeThreadGroupSizeY;
                break;

            case TerrainAnalysisType.Curvature:
                kernel = curvatureKernel;
                threadGroupSizeX = curvatureThreadGroupSizeX;
                threadGroupSizeY = curvatureThreadGroupSizeY;
                break;

            default:
                DestroyCandidate(output);

                errorMessage =
                    "Unsupported terrain analysis type: " +
                    key.Type;

                return false;
        }

        int groupsX =
            DivideRoundUp(
                samplesPerSide,
                threadGroupSizeX
            );

        int groupsY =
            DivideRoundUp(
                samplesPerSide,
                threadGroupSizeY
            );

        if (
            groupsX <= 0 ||
            groupsY <= 0
        )
        {
            DestroyCandidate(output);

            errorMessage =
                "Terrain analysis calculated an invalid compute dispatch.";

            return false;
        }

        try
        {
            computeShader.SetTexture(
                kernel,
                "_HeightCache",
                heightCache
            );

            computeShader.SetTexture(
                kernel,
                "_AnalysisOutput",
                output
            );

            computeShader.SetInts(
                "_HeightCacheOriginTile",
                cacheOriginTile.x,
                cacheOriginTile.y
            );

            computeShader.SetInts(
                "_HeightCacheSize",
                cacheSize.x,
                cacheSize.y
            );

            computeShader.SetInt(
                "_SamplesPerSide",
                samplesPerSide
            );

            computeShader.SetFloat(
                "_SampleSpacing",
                sampleSpacing
            );

            computeShader.SetVector(
                "_WorldSizeXZ",
                new Vector4(
                    worldSizeXZ.x,
                    worldSizeXZ.y,
                    0f,
                    0f
                )
            );

            computeShader.SetFloat(
                "_AnalysisScaleMeters",
                key.HasScale
                    ? key.ScaleMeters
                    : 0f
            );

            computeShader.Dispatch(
                kernel,
                groupsX,
                groupsY,
                sliceCount
            );
        }
        catch (Exception exception)
        {
            DestroyCandidate(output);

            errorMessage =
                "Terrain analysis GPU dispatch failed for " +
                key +
                ".\n\n" +
                exception.Message;

            return false;
        }

        result =
            new TerrainAnalysisGenerationResult(
                output,
                cacheOriginTile,
                cacheSize,
                samplesPerSide,
                sampleSpacing,
                worldSizeXZ,
                sourceSignature
            );

        if (!result.IsValid)
        {
            DestroyCandidate(output);

            result = default;

            errorMessage =
                "Terrain analysis generation completed, but the generated " +
                "layer layout was invalid.";

            return false;
        }

        return true;
    }

    private bool TryPrepare(
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            computeShader != null &&
            slopeKernel >= 0 &&
            curvatureKernel >= 0 &&
            slopeThreadGroupSizeX > 0 &&
            slopeThreadGroupSizeY > 0 &&
            curvatureThreadGroupSizeX > 0 &&
            curvatureThreadGroupSizeY > 0
        )
        {
            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support compute shaders.";

            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support 2D texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RHalf
            )
        )
        {
            errorMessage =
                "The current graphics device does not support RHalf render " +
                "textures required by Terrain Analysis.";

            return false;
        }

        computeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                ComputeShaderAssetPath
            );

        if (computeShader == null)
        {
            ResetShaderState();

            errorMessage =
                "TerrainAnalysis.compute could not be loaded:\n\n" +
                ComputeShaderAssetPath;

            return false;
        }

        try
        {
            slopeKernel =
                computeShader.FindKernel(
                    SlopeKernelName
                );

            curvatureKernel =
                computeShader.FindKernel(
                    CurvatureKernelName
                );

            computeShader.GetKernelThreadGroupSizes(
                slopeKernel,
                out slopeThreadGroupSizeX,
                out slopeThreadGroupSizeY,
                out _
            );

            computeShader.GetKernelThreadGroupSizes(
                curvatureKernel,
                out curvatureThreadGroupSizeX,
                out curvatureThreadGroupSizeY,
                out _
            );
        }
        catch (Exception exception)
        {
            ResetShaderState();

            errorMessage =
                "TerrainAnalysis.compute is missing one or more required " +
                "kernels.\n\nRequired:\n- " +
                SlopeKernelName +
                "\n- " +
                CurvatureKernelName +
                "\n\n" +
                exception.Message;

            return false;
        }

        if (
            slopeThreadGroupSizeX == 0 ||
            slopeThreadGroupSizeY == 0 ||
            curvatureThreadGroupSizeX == 0 ||
            curvatureThreadGroupSizeY == 0
        )
        {
            ResetShaderState();

            errorMessage =
                "TerrainAnalysis.compute reported an invalid thread-group size.";

            return false;
        }

        return true;
    }

    private static bool TryValidateKey(
        TerrainAnalysisKey key,
        out string errorMessage
    )
    {
        errorMessage = "";

        switch (key.Type)
        {
            case TerrainAnalysisType.Slope:
                if (key.HasScale)
                {
                    errorMessage =
                        "Slope analysis is scale-independent and must not specify a scale.";

                    return false;
                }

                return true;

            case TerrainAnalysisType.Curvature:
                if (!key.HasScale)
                {
                    errorMessage =
                        "Curvature analysis requires a world-space scale.";

                    return false;
                }

                return true;

            default:
                errorMessage =
                    "Unsupported terrain analysis type: " +
                    key.Type;

                return false;
        }
    }

    private static RenderTexture CreateAnalysisTexture(
        TerrainAnalysisKey key,
        int samplesPerSide,
        int sliceCount
    )
    {
        RenderTextureDescriptor descriptor =
            new RenderTextureDescriptor(
                samplesPerSide,
                samplesPerSide,
                RenderTextureFormat.RHalf,
                0
            );

        descriptor.dimension =
            TextureDimension.Tex2DArray;

        descriptor.volumeDepth =
            sliceCount;

        descriptor.msaaSamples = 1;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.enableRandomWrite = true;
        descriptor.sRGB = false;

        RenderTexture texture =
            new RenderTexture(
                descriptor
            );

        texture.name =
            "WorldMeshes Terrain Analysis - " +
            key;

        texture.filterMode =
            FilterMode.Bilinear;

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.hideFlags =
            HideFlags.HideAndDontSave;

        if (!texture.Create())
        {
            DestroyCandidate(texture);
            return null;
        }

        return texture;
    }

    private static int DivideRoundUp(
        int value,
        uint divisor
    )
    {
        if (
            value <= 0 ||
            divisor == 0
        )
        {
            return 0;
        }

        return
            (
                value +
                (int)divisor -
                1
            )
            /
            (int)divisor;
    }

    private static void OnPreviewStateChanged()
    {
        if (TerrainAuthoringPreviewService.CacheReady)
        {
            TerrainAnalysisService.InvalidateAll();
        }
        else
        {
            TerrainAnalysisService.Clear();
        }
    }

    private static void Shutdown()
    {
        TerrainAuthoringPreviewService.PreviewStateChanged -=
            OnPreviewStateChanged;

        AssemblyReloadEvents.beforeAssemblyReload -=
            Shutdown;

        EditorApplication.quitting -=
            Shutdown;

        TerrainAnalysisService.Clear();

        TerrainAnalysisService.UnregisterGenerator(
            Instance
        );

        Instance.ResetShaderState();
    }

    private void ResetShaderState()
    {
        computeShader = null;
        slopeKernel = -1;
        curvatureKernel = -1;
        slopeThreadGroupSizeX = 0;
        slopeThreadGroupSizeY = 0;
        curvatureThreadGroupSizeX = 0;
        curvatureThreadGroupSizeY = 0;
    }

    private static void DestroyCandidate(
        RenderTexture texture
    )
    {
        if (texture == null)
        {
            return;
        }

        if (texture.IsCreated())
        {
            texture.Release();
        }

        UnityEngine.Object.DestroyImmediate(
            texture
        );
    }
}
