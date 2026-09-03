using System;
using System.Collections.Generic;
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

    /*
     * Used to distinguish ordinary in-place preview slice updates from a
     * complete TerrainAuthoringPreviewCache RenderTexture replacement.
     */
    private static int lastObservedHeightCacheInstanceId;

    static TerrainAnalysisGpuGenerator()
    {
        TerrainAnalysisService.RegisterGenerator(
            Instance
        );

        TerrainAuthoringPreviewService.CompositeTilesUpdated +=
            OnCompositeTilesUpdated;

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
            !TryGetSource(
                out RenderTexture heightCache,
                out Vector2Int cacheOriginTile,
                out Vector2Int cacheSize,
                out int samplesPerSide,
                out float sampleSpacing,
                out Vector2 worldSizeXZ,
                out string sourceSignature,
                out int sourceHeightCacheInstanceId,
                out errorMessage
            )
        )
        {
            return false;
        }

        int sliceCount =
            cacheSize.x *
            cacheSize.y;

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

        if (
            !TryGetKernel(
                key,
                out int kernel,
                out uint threadGroupSizeX,
                out uint threadGroupSizeY,
                out errorMessage
            )
        )
        {
            DestroyCandidate(output);
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
            SetCommonKernelParameters(
                kernel,
                key,
                heightCache,
                output,
                cacheOriginTile,
                cacheSize,
                samplesPerSide,
                sampleSpacing,
                worldSizeXZ
            );

            computeShader.SetInt(
                "_OutputSliceOffset",
                0
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
                sourceSignature,
                sourceHeightCacheInstanceId
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

        lastObservedHeightCacheInstanceId =
            sourceHeightCacheInstanceId;

        return true;
    }

    public bool TryUpdateTiles(
        TerrainAnalysisLayer layer,
        IReadOnlyList<Vector2Int> tileCoordinates,
        out string sourceSignature,
        out string errorMessage
    )
    {
        sourceSignature = "";
        errorMessage = "";

        if (
            layer == null ||
            !layer.IsReady ||
            layer.Texture == null ||
            !layer.Texture.IsCreated()
        )
        {
            errorMessage =
                "Incremental terrain analysis requires a ready analysis layer.";

            return false;
        }

        if (
            tileCoordinates == null ||
            tileCoordinates.Count == 0
        )
        {
            sourceSignature =
                layer.SourceSignature;

            return true;
        }

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
                layer.Key,
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
            !TryGetSource(
                out RenderTexture heightCache,
                out Vector2Int cacheOriginTile,
                out Vector2Int cacheSize,
                out int samplesPerSide,
                out float sampleSpacing,
                out Vector2 worldSizeXZ,
                out sourceSignature,
                out int sourceHeightCacheInstanceId,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            sourceHeightCacheInstanceId !=
                layer.SourceHeightCacheInstanceId
            ||
            cacheOriginTile !=
                layer.CacheOriginTile
            ||
            cacheSize !=
                layer.CacheSize
            ||
            samplesPerSide !=
                layer.SamplesPerSide
            ||
            !Mathf.Approximately(
                sampleSpacing,
                layer.SampleSpacing
            )
            ||
            !VectorApproximately(
                worldSizeXZ,
                layer.WorldSizeXZ
            )
        )
        {
            errorMessage =
                "The Terrain Authoring Height Preview was replaced or its " +
                "layout changed. This analysis layer requires a complete " +
                "regeneration.";

            return false;
        }

        if (
            layer.Texture.width !=
                samplesPerSide
            ||
            layer.Texture.height !=
                samplesPerSide
            ||
            layer.Texture.volumeDepth !=
                cacheSize.x *
                cacheSize.y
        )
        {
            errorMessage =
                "The existing analysis texture no longer matches the " +
                "authoring height-cache layout.";

            return false;
        }

        if (
            !TryGetKernel(
                layer.Key,
                out int kernel,
                out uint threadGroupSizeX,
                out uint threadGroupSizeY,
                out errorMessage
            )
        )
        {
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
            errorMessage =
                "Terrain analysis calculated an invalid incremental " +
                "compute dispatch.";

            return false;
        }

        HashSet<int> uniqueSlices =
            new HashSet<int>();

        for (
            int tileIndex = 0;
            tileIndex < tileCoordinates.Count;
            tileIndex++
        )
        {
            Vector2Int tile =
                tileCoordinates[
                    tileIndex
                ];

            Vector2Int localTile =
                tile -
                cacheOriginTile;

            if (
                localTile.x < 0 ||
                localTile.y < 0 ||
                localTile.x >=
                    cacheSize.x ||
                localTile.y >=
                    cacheSize.y
            )
            {
                continue;
            }

            int slice =
                localTile.x +
                localTile.y *
                cacheSize.x;

            uniqueSlices.Add(
                slice
            );
        }

        if (uniqueSlices.Count == 0)
        {
            lastObservedHeightCacheInstanceId =
                sourceHeightCacheInstanceId;

            return true;
        }

        List<int> sortedSlices =
            new List<int>(
                uniqueSlices
            );

        sortedSlices.Sort();

        try
        {
            SetCommonKernelParameters(
                kernel,
                layer.Key,
                heightCache,
                layer.Texture,
                cacheOriginTile,
                cacheSize,
                samplesPerSide,
                sampleSpacing,
                worldSizeXZ
            );

            /*
             * Batch adjacent slice indices into one Z dispatch. This keeps
             * the dispatch count low while still touching only requested
             * analysis tiles.
             */
            int sliceIndex =
                0;

            while (
                sliceIndex <
                sortedSlices.Count
            )
            {
                int firstSlice =
                    sortedSlices[
                        sliceIndex
                    ];

                int lastSlice =
                    firstSlice;

                int nextIndex =
                    sliceIndex +
                    1;

                while (
                    nextIndex <
                        sortedSlices.Count
                    &&
                    sortedSlices[
                        nextIndex
                    ]
                    ==
                    lastSlice +
                    1
                )
                {
                    lastSlice =
                        sortedSlices[
                            nextIndex
                        ];

                    nextIndex++;
                }

                int runLength =
                    lastSlice -
                    firstSlice +
                    1;

                computeShader.SetInt(
                    "_OutputSliceOffset",
                    firstSlice
                );

                computeShader.Dispatch(
                    kernel,
                    groupsX,
                    groupsY,
                    runLength
                );

                sliceIndex =
                    nextIndex;
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                "Incremental terrain analysis GPU dispatch failed for " +
                layer.Key +
                ".\n\n" +
                exception.Message;

            return false;
        }

        lastObservedHeightCacheInstanceId =
            sourceHeightCacheInstanceId;

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

    private bool TryGetKernel(
        TerrainAnalysisKey key,
        out int kernel,
        out uint threadGroupSizeX,
        out uint threadGroupSizeY,
        out string errorMessage
    )
    {
        kernel = -1;
        threadGroupSizeX = 0;
        threadGroupSizeY = 0;
        errorMessage = "";

        switch (key.Type)
        {
            case TerrainAnalysisType.Slope:
                kernel =
                    slopeKernel;

                threadGroupSizeX =
                    slopeThreadGroupSizeX;

                threadGroupSizeY =
                    slopeThreadGroupSizeY;

                return true;

            case TerrainAnalysisType.Curvature:
                kernel =
                    curvatureKernel;

                threadGroupSizeX =
                    curvatureThreadGroupSizeX;

                threadGroupSizeY =
                    curvatureThreadGroupSizeY;

                return true;

            default:
                errorMessage =
                    "Unsupported terrain analysis type: " +
                    key.Type;

                return false;
        }
    }

    private static bool TryGetSource(
        out RenderTexture heightCache,
        out Vector2Int cacheOriginTile,
        out Vector2Int cacheSize,
        out int samplesPerSide,
        out float sampleSpacing,
        out Vector2 worldSizeXZ,
        out string sourceSignature,
        out int sourceHeightCacheInstanceId,
        out string errorMessage
    )
    {
        sourceHeightCacheInstanceId =
            0;

        errorMessage =
            "";

        if (
            !TerrainAuthoringPreviewService
                .TryGetTerrainAnalysisSource(
                    out heightCache,
                    out cacheOriginTile,
                    out cacheSize,
                    out samplesPerSide,
                    out sampleSpacing,
                    out worldSizeXZ,
                    out sourceSignature
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

        sourceHeightCacheInstanceId =
            heightCache.GetInstanceID();

        return
            sourceHeightCacheInstanceId !=
            0;
    }

    private void SetCommonKernelParameters(
        int kernel,
        TerrainAnalysisKey key,
        RenderTexture heightCache,
        RenderTexture output,
        Vector2Int cacheOriginTile,
        Vector2Int cacheSize,
        int samplesPerSide,
        float sampleSpacing,
        Vector2 worldSizeXZ
    )
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

    private static void OnCompositeTilesUpdated(
        IReadOnlyList<Vector2Int> tileCoordinates
    )
    {
        TerrainAnalysisService
            .NotifySourceTilesChanged(
                tileCoordinates
            );
    }

    private static void OnPreviewStateChanged()
    {
        if (!TerrainAuthoringPreviewService.CacheReady)
        {
            lastObservedHeightCacheInstanceId =
                0;

            TerrainAnalysisService.Clear();

            return;
        }

        int currentCacheInstanceId =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        if (currentCacheInstanceId == 0)
        {
            lastObservedHeightCacheInstanceId =
                0;

            TerrainAnalysisService.Clear();

            return;
        }

        if (
            lastObservedHeightCacheInstanceId ==
            0
        )
        {
            lastObservedHeightCacheInstanceId =
                currentCacheInstanceId;

            TerrainAnalysisService
                .InvalidateAll();

            return;
        }

        if (
            currentCacheInstanceId !=
            lastObservedHeightCacheInstanceId
        )
        {
            lastObservedHeightCacheInstanceId =
                currentCacheInstanceId;

            TerrainAnalysisService
                .InvalidateAll();
        }

        /*
         * Same cache object means ordinary composite/metadata updates.
         * CompositeTilesUpdated already handled the actual changed source
         * tiles, so do not invalidate every analysis layer here.
         */
    }

    private static void Shutdown()
    {
        TerrainAuthoringPreviewService.CompositeTilesUpdated -=
            OnCompositeTilesUpdated;

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
        lastObservedHeightCacheInstanceId = 0;
    }

    private static bool VectorApproximately(
        Vector2 a,
        Vector2 b
    )
    {
        return
            Mathf.Approximately(
                a.x,
                b.x
            )
            &&
            Mathf.Approximately(
                a.y,
                b.y
            );
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
