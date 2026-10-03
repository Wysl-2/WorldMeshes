using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainSurfaceStreamingPyramidGenerator
{
    private enum AssetWriteOutcome
    {
        Failed,
        Created,
        Updated
    }

    public static TerrainSurfaceStreamingFamilyWriteResult PrepareFamilyFromExistingNativeTexture(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        Vector2Int coordinate,
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> descriptors,
        Texture2D authoritativeTexture,
        Dictionary<int, byte[]> reusableDerivedBuffers,
        List<Texture2D> dirtyOutputs
    )
    {
        int requestedStrideCount = descriptors != null ? descriptors.Count : 0;

        if (worldSettings == null || manifest == null)
        {
            return TerrainSurfaceStreamingFamilyWriteResult.NotAttempted(
                coordinate,
                requestedStrideCount,
                "WorldSettings or the authoritative Surface manifest is unavailable."
            );
        }

        if (descriptors == null || reusableDerivedBuffers == null || dirtyOutputs == null)
        {
            return TerrainSurfaceStreamingFamilyWriteResult.NotAttempted(
                coordinate,
                requestedStrideCount,
                "Surface streaming generation received an invalid working buffer or output collection."
            );
        }

        int nativeSamplesPerSide = manifest.samplesPerSide;
        int expectedNativeSampleCount = nativeSamplesPerSide * nativeSamplesPerSide;

        if (
            authoritativeTexture == null
            || authoritativeTexture.width != nativeSamplesPerSide
            || authoritativeTexture.height != nativeSamplesPerSide
            || authoritativeTexture.format != TextureFormat.R8
        )
        {
            return TerrainSurfaceStreamingFamilyWriteResult.NotAttempted(
                coordinate,
                requestedStrideCount,
                "The authoritative Surface texture is missing or has an invalid layout."
            );
        }

        NativeArray<byte> nativeData;

        try
        {
            nativeData = authoritativeTexture.GetPixelData<byte>(0);
        }
        catch (Exception exception)
        {
            return TerrainSurfaceStreamingFamilyWriteResult.NotAttempted(
                coordinate,
                requestedStrideCount,
                "Could not read the authoritative Surface texture.\n\n" + exception.Message
            );
        }

        if (nativeData.Length != expectedNativeSampleCount)
        {
            return TerrainSurfaceStreamingFamilyWriteResult.NotAttempted(
                coordinate,
                requestedStrideCount,
                "The authoritative Surface texture has an unexpected sample count."
            );
        }

        int preparedStrideCount = 0;
        int createdAssetCount = 0;
        int updatedAssetCount = 0;
        bool outputMayHaveChanged = false;

        for (int index = 0; index < descriptors.Count; index++)
        {
            TerrainSurfaceStreamingLevelDescriptor descriptor = descriptors[index];

            if (
                !descriptor.IsStructurallyValid
                || !descriptor.IsDerived
                || !TerrainSurfaceStreamingPyramidPolicy.IsSurfaceRepresentationStrideSupported(
                    worldSettings,
                    manifest,
                    descriptor.SampleStride
                )
                || descriptor.SamplesPerSide != TerrainSurfaceStreamingPyramidPolicy.GetSamplesPerSide(
                    manifest,
                    descriptor.SampleStride
                )
            )
            {
                return Failure(
                    coordinate,
                    requestedStrideCount,
                    preparedStrideCount,
                    createdAssetCount,
                    updatedAssetCount,
                    descriptor.SampleStride,
                    outputMayHaveChanged,
                    "A Surface streaming level descriptor is incompatible with the current streaming policy."
                );
            }

            byte[] derivedBuffer = GetOrCreateReusableBuffer(
                reusableDerivedBuffers,
                descriptor.SampleStride,
                descriptor.SamplesPerSide
            );

            FillDerivedSamples(
                nativeData,
                nativeSamplesPerSide,
                descriptor,
                derivedBuffer
            );

            AssetWriteOutcome outcome = PrepareOneRepresentation(
                coordinate,
                descriptor,
                derivedBuffer,
                dirtyOutputs,
                out bool representationMayHaveChanged,
                out string errorMessage
            );

            outputMayHaveChanged |= representationMayHaveChanged;

            if (outcome == AssetWriteOutcome.Failed)
            {
                return Failure(
                    coordinate,
                    requestedStrideCount,
                    preparedStrideCount,
                    createdAssetCount,
                    updatedAssetCount,
                    descriptor.SampleStride,
                    outputMayHaveChanged,
                    errorMessage
                );
            }

            preparedStrideCount++;

            if (outcome == AssetWriteOutcome.Created)
            {
                createdAssetCount++;
            }
            else
            {
                updatedAssetCount++;
            }
        }

        return new TerrainSurfaceStreamingFamilyWriteResult(
            coordinate,
            true,
            true,
            requestedStrideCount,
            preparedStrideCount,
            createdAssetCount,
            updatedAssetCount,
            0,
            outputMayHaveChanged,
            ""
        );
    }

    private static byte[] GetOrCreateReusableBuffer(
        Dictionary<int, byte[]> reusableBuffers,
        int sampleStride,
        int samplesPerSide
    )
    {
        int expectedCount = samplesPerSide * samplesPerSide;

        if (
            !reusableBuffers.TryGetValue(sampleStride, out byte[] buffer)
            || buffer == null
            || buffer.Length != expectedCount
        )
        {
            buffer = new byte[expectedCount];
            reusableBuffers[sampleStride] = buffer;
        }

        return buffer;
    }

    private static void FillDerivedSamples(
        NativeArray<byte> nativeData,
        int nativeSamplesPerSide,
        TerrainSurfaceStreamingLevelDescriptor descriptor,
        byte[] derivedBuffer
    )
    {
        int stride = descriptor.SampleStride;
        int derivedSamplesPerSide = descriptor.SamplesPerSide;

        for (int z = 0; z < derivedSamplesPerSide; z++)
        {
            int nativeZ = z * stride;

            for (int x = 0; x < derivedSamplesPerSide; x++)
            {
                int nativeX = x * stride;
                derivedBuffer[x + z * derivedSamplesPerSide] =
                    nativeData[nativeX + nativeZ * nativeSamplesPerSide];
            }
        }
    }

    private static AssetWriteOutcome PrepareOneRepresentation(
        Vector2Int coordinate,
        TerrainSurfaceStreamingLevelDescriptor descriptor,
        byte[] values,
        List<Texture2D> dirtyOutputs,
        out bool outputMayHaveChanged,
        out string errorMessage
    )
    {
        outputMayHaveChanged = false;
        errorMessage = "";

        string assetPath = TerrainRuntimeSurfaceMaskAssetUtility.GetStreamingSurfaceTilePath(
            descriptor.SampleStride,
            coordinate.x,
            coordinate.y
        );

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        bool existedBefore = texture != null;

        if (
            texture != null
            && (
                texture.width != descriptor.SamplesPerSide
                || texture.height != descriptor.SamplesPerSide
                || texture.format != TextureFormat.R8
            )
        )
        {
            bool reinitialized = false;

            try
            {
                reinitialized = texture.Reinitialize(
                    descriptor.SamplesPerSide,
                    descriptor.SamplesPerSide,
                    TextureFormat.R8,
                    false
                );
            }
            catch
            {
                reinitialized = false;
            }

            if (!reinitialized)
            {
                outputMayHaveChanged = true;

                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    errorMessage =
                        "Could not replace incompatible Surface streaming texture:\n" + assetPath;
                    return AssetWriteOutcome.Failed;
                }

                texture = null;
            }
        }

        if (texture == null)
        {
            Texture2D createdTexture = null;

            try
            {
                createdTexture = new Texture2D(
                    descriptor.SamplesPerSide,
                    descriptor.SamplesPerSide,
                    TextureFormat.R8,
                    false,
                    true
                );

                ConfigureTexture(createdTexture, coordinate);
                createdTexture.SetPixelData<byte>(values, 0);
                createdTexture.Apply(false, false);
                AssetDatabase.CreateAsset(createdTexture, assetPath);
                EditorUtility.SetDirty(createdTexture);
                dirtyOutputs.Add(createdTexture);
                outputMayHaveChanged = true;

                return existedBefore
                    ? AssetWriteOutcome.Updated
                    : AssetWriteOutcome.Created;
            }
            catch (Exception exception)
            {
                bool persisted = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null;

                if (createdTexture != null && !persisted)
                {
                    UnityEngine.Object.DestroyImmediate(createdTexture);
                }

                outputMayHaveChanged |= persisted;
                errorMessage =
                    "Could not create Surface streaming texture:\n" +
                    assetPath +
                    "\n\n" +
                    exception.Message;
                return AssetWriteOutcome.Failed;
            }
        }

        try
        {
            outputMayHaveChanged = true;
            ConfigureTexture(texture, coordinate);
            texture.SetPixelData<byte>(values, 0);
            texture.Apply(false, false);
            EditorUtility.SetDirty(texture);
            dirtyOutputs.Add(texture);
            return AssetWriteOutcome.Updated;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not update Surface streaming texture:\n" +
                assetPath +
                "\n\n" +
                exception.Message;
            return AssetWriteOutcome.Failed;
        }
    }

    private static void ConfigureTexture(
        Texture2D texture,
        Vector2Int coordinate
    )
    {
        texture.name =
            TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTileName(
                coordinate.x,
                coordinate.y
            );
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 0;
    }

    private static TerrainSurfaceStreamingFamilyWriteResult Failure(
        Vector2Int coordinate,
        int requestedStrideCount,
        int preparedStrideCount,
        int createdAssetCount,
        int updatedAssetCount,
        int failedStride,
        bool outputMayHaveChanged,
        string errorMessage
    )
    {
        return new TerrainSurfaceStreamingFamilyWriteResult(
            coordinate,
            true,
            false,
            requestedStrideCount,
            preparedStrideCount,
            createdAssetCount,
            updatedAssetCount,
            failedStride,
            outputMayHaveChanged,
            errorMessage
        );
    }
}
