using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * GPU-only exact native-lattice extraction into one Height cache slice.
 * Sources remain native and may be reused across destination representations.
 * Shader assets are borrowed; no temporary textures or source tiles are owned.
 */
internal sealed class TerrainAuthoringPreviewHeightMaterializer
{
    internal const string ComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/Authoring/" +
        "TerrainHeightRepresentationMaterialization.compute";

    private const string KernelName = "MaterializeHeightRepresentation";

    private static readonly int NativeHeightSourceId =
        Shader.PropertyToID("_NativeHeightSource");
    private static readonly int HeightCacheId =
        Shader.PropertyToID("_HeightCache");
    private static readonly int DestinationSliceId =
        Shader.PropertyToID("_DestinationSlice");
    private static readonly int DestinationSamplesPerSideId =
        Shader.PropertyToID("_DestinationSamplesPerSide");
    private static readonly int SampleStrideId =
        Shader.PropertyToID("_SampleStride");

    private ComputeShader computeShader;
    private int kernel = -1;
    private uint threadGroupSizeX;
    private uint threadGroupSizeY;

    internal bool TryPrepare(out string errorMessage)
    {
        errorMessage = "";

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The graphics device does not support Height materialization compute shaders.";
            return false;
        }

        if (
            computeShader != null
            && kernel >= 0
            && threadGroupSizeX > 0
            && threadGroupSizeY > 0
        )
        {
            return true;
        }

        try
        {
            computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                ComputeShaderAssetPath
            );

            if (computeShader == null)
            {
                throw new InvalidOperationException(
                    "The Height materialization compute shader could not be loaded:\n\n" +
                    ComputeShaderAssetPath
                );
            }

            if (!computeShader.HasKernel(KernelName))
            {
                throw new InvalidOperationException(
                    "The Height materialization kernel is unavailable: " + KernelName
                );
            }

            kernel = computeShader.FindKernel(KernelName);
            computeShader.GetKernelThreadGroupSizes(
                kernel,
                out threadGroupSizeX,
                out threadGroupSizeY,
                out uint threadGroupSizeZ
            );

            if (
                threadGroupSizeX == 0
                || threadGroupSizeY == 0
                || threadGroupSizeZ != 1
            )
            {
                throw new InvalidOperationException(
                    "The Height materialization kernel has an invalid thread-group configuration."
                );
            }
        }
        catch (Exception exception)
        {
            computeShader = null;
            kernel = -1;
            threadGroupSizeX = 0;
            threadGroupSizeY = 0;
            errorMessage = exception.Message;
            return false;
        }

        return true;
    }

    internal bool TryMaterialize(
        Texture2D nativeSource,
        RenderTexture destinationCache,
        int destinationSlice,
        int nativeSamplesPerSide,
        int destinationSamplesPerSide,
        int sampleStride,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TerrainAuthoringPreviewHeightSourceUtility.TryValidateNativeSource(
                nativeSource,
                nativeSamplesPerSide,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TerrainHeightResolutionUtility.IsPowerOfTwo(sampleStride)
            || (nativeSamplesPerSide - 1) % sampleStride != 0
            || destinationSamplesPerSide <= 1
            || destinationSamplesPerSide !=
                (nativeSamplesPerSide - 1) / sampleStride + 1
        )
        {
            errorMessage =
                "The Height materialization stride or destination sample count is incompatible with the native lattice.";
            return false;
        }

        if (
            destinationCache == null
            || !destinationCache.IsCreated()
            || destinationCache.dimension != TextureDimension.Tex2DArray
            || destinationCache.format != RenderTextureFormat.RFloat
            || destinationCache.width != destinationSamplesPerSide
            || destinationCache.height != destinationSamplesPerSide
            || destinationCache.antiAliasing != 1
            || destinationSlice < 0
            || destinationSlice >= destinationCache.volumeDepth
        )
        {
            errorMessage =
                "The Height materialization destination array, dimensions, format, or slice is invalid.";
            return false;
        }

        if (sampleStride == 1)
        {
            CopyTextureSupport requiredCopySupport =
                CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT;
            if (
                (SystemInfo.copyTextureSupport & requiredCopySupport) != requiredCopySupport
            )
            {
                errorMessage =
                    "The graphics device cannot copy native Height textures into texture arrays.";
                return false;
            }
        }
        else if (
            !destinationCache.enableRandomWrite
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The coarse Height destination does not support RFloat random writes.";
            return false;
        }
        else if (!TryPrepare(out errorMessage))
        {
            return false;
        }

        try
        {
            using (WorldMeshesProfiler.PreviewCopyTiles.Auto())
            {
                if (sampleStride == 1)
                {
                    Graphics.CopyTexture(
                        nativeSource, 0, 0, destinationCache, destinationSlice, 0
                    );
                }
                else
                {
                    computeShader.SetTexture(kernel, NativeHeightSourceId, nativeSource);
                    computeShader.SetTexture(kernel, HeightCacheId, destinationCache);
                    computeShader.SetInt(DestinationSliceId, destinationSlice);
                    computeShader.SetInt(DestinationSamplesPerSideId, destinationSamplesPerSide);
                    computeShader.SetInt(SampleStrideId, sampleStride);
                    computeShader.Dispatch(
                        kernel,
                        (int)(((long)destinationSamplesPerSide + threadGroupSizeX - 1) / threadGroupSizeX),
                        (int)(((long)destinationSamplesPerSide + threadGroupSizeY - 1) / threadGroupSizeY),
                        1
                    );
                }
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                "The Height representation could not be materialized.\n\n" +
                exception.Message;
            return false;
        }

        return true;
    }
}
