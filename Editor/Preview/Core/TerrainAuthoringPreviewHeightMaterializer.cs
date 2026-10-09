using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * GPU-only exact lattice extraction into one Height cache slice.
 * Native and derived sources supply an explicit native-relative stride.
 * Dispatches use a temporary shader instance so borrowed texture bindings do not
 * remain on the project asset. No temporary textures or source tiles are owned.
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

    internal void ReleaseTextureBindings()
    {
        ComputeShader instance = computeShader;
        computeShader = null;
        kernel = -1;
        threadGroupSizeX = 0;
        threadGroupSizeY = 0;

        // SetTexture requires a non-null texture. Destroy only the owned shader
        // instance to release its bindings without retaining source/cache assets.
        if (instance != null)
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

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
            ComputeShader shaderAsset = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                ComputeShaderAssetPath
            );

            if (shaderAsset == null)
            {
                throw new InvalidOperationException(
                    "The Height materialization compute shader could not be loaded:\n\n" +
                    ComputeShaderAssetPath
                );
            }

            computeShader = UnityEngine.Object.Instantiate(shaderAsset);
            computeShader.hideFlags = HideFlags.HideAndDontSave;

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
            ReleaseTextureBindings();
            errorMessage = exception.Message;
            return false;
        }

        return true;
    }

    // Compatibility for native-only callers and existing validator fixtures.
    internal bool TryMaterialize(Texture2D nativeSource, RenderTexture destinationCache, int destinationSlice,
        int nativeSamplesPerSide, int destinationSamplesPerSide, int sampleStride, out string errorMessage)
    {
        return TryMaterialize(nativeSource, destinationCache, destinationSlice, nativeSamplesPerSide,
            destinationSamplesPerSide, 1, sampleStride, out errorMessage);
    }

    internal bool TryMaterialize(TerrainAuthoringPreviewHeightSourceLease source, RenderTexture destinationCache,
        int destinationSlice, int nativeSamplesPerSide, int destinationSamplesPerSide, int sampleStride, out string errorMessage)
    {
        if (source == null || source.NativeSamplesPerSide != nativeSamplesPerSide)
        {
            errorMessage = "The committed Height source lease is incompatible.";
            return false;
        }
        return TryMaterialize(source.Texture, destinationCache, destinationSlice, nativeSamplesPerSide,
            destinationSamplesPerSide, source.SourceStride, sampleStride, out errorMessage);
    }

    internal bool TryMaterialize(
        Texture2D source,
        RenderTexture destinationCache,
        int destinationSlice,
        int nativeSamplesPerSide,
        int destinationSamplesPerSide,
        int sourceStride,
        int sampleStride,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            nativeSamplesPerSide <= 1
            || !TerrainHeightResolutionUtility.IsPowerOfTwo(sourceStride)
            || !TerrainHeightResolutionUtility.IsPowerOfTwo(sampleStride)
            || sourceStride > sampleStride || sampleStride % sourceStride != 0
            || (nativeSamplesPerSide - 1) % sourceStride != 0
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

        int sourceSamples = (nativeSamplesPerSide - 1) / sourceStride + 1;
        if (source == null || source.width != sourceSamples || source.height != sourceSamples
            || source.format != TextureFormat.RFloat)
        {
            errorMessage = "The committed Height source dimensions or format do not match its actual stride.";
            return false;
        }
        int relativeStride = sampleStride / sourceStride;
        CopyTextureSupport requiredCopySupport =
            CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT;
        bool directCopy = relativeStride == 1
            && (SystemInfo.copyTextureSupport & requiredCopySupport) == requiredCopySupport;

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

        if (!directCopy)
        {
            if (!destinationCache.enableRandomWrite
                || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat))
            {
                errorMessage = "The Height destination does not support RFloat random writes.";
                return false;
            }
            if (!TryPrepare(out errorMessage)) return false;
        }

        try
        {
            using (WorldMeshesProfiler.PreviewCopyTiles.Auto())
            {
                if (directCopy)
                {
                    Graphics.CopyTexture(
                        source, 0, 0, destinationCache, destinationSlice, 0
                    );
                }
                else
                {
                    computeShader.SetTexture(kernel, NativeHeightSourceId, source);
                    computeShader.SetTexture(kernel, HeightCacheId, destinationCache);
                    computeShader.SetInt(DestinationSliceId, destinationSlice);
                    computeShader.SetInt(DestinationSamplesPerSideId, destinationSamplesPerSide);
                    computeShader.SetInt(SampleStrideId, relativeStride);
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
        finally
        {
            ReleaseTextureBindings();
        }

        return true;
    }
}

