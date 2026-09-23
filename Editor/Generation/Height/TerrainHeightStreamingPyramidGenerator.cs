using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightStreamingPyramidGenerator
{
    private enum AssetWriteOutcome
    {
        Failed,
        Created,
        Updated
    }

    public static TerrainHeightStreamingFamilyWriteResult
        PrepareFamilyFromNativeBuffer(
            WorldSettings worldSettings,
            Vector2Int coordinate,
            IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors,
            float[] authoritativeHeightData
        )
    {
        return
            PrepareFamilyFromNativeBuffer(
                worldSettings,
                coordinate,
                descriptors,
                authoritativeHeightData,
                null
            );
    }

    internal static TerrainHeightStreamingFamilyWriteResult
        PrepareFamilyFromNativeBuffer(
            WorldSettings worldSettings,
            Vector2Int coordinate,
            IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors,
            float[] authoritativeHeightData,
            TerrainHeightBakeResidencyBatch residencyBatch
        )
    {
        int requestedStrideCount =
            descriptors != null
                ? descriptors.Count
                : 0;

        if (worldSettings == null)
        {
            return
                Failure(
                    coordinate,
                    requestedStrideCount,
                    0,
                    0,
                    0,
                    0,
                    false,
                    "WorldSettings is null while preparing the height-streaming family."
                );
        }

        if (descriptors == null)
        {
            return
                Failure(
                    coordinate,
                    0,
                    0,
                    0,
                    0,
                    0,
                    false,
                    "The height-streaming descriptor list is null."
                );
        }

        int nativeSamplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedNativeSampleCount =
            nativeSamplesPerSide *
            nativeSamplesPerSide;

        if (
            authoritativeHeightData == null
            ||
            authoritativeHeightData.Length !=
                expectedNativeSampleCount
        )
        {
            return
                Failure(
                    coordinate,
                    requestedStrideCount,
                    0,
                    0,
                    0,
                    0,
                    false,
                    "The authoritative native height buffer has an unexpected sample count."
                );
        }

        for (
            int index = 0;
            index < authoritativeHeightData.Length;
            index++
        )
        {
            if (!IsFinite(authoritativeHeightData[index]))
            {
                return
                    Failure(
                        coordinate,
                        requestedStrideCount,
                        0,
                        0,
                        0,
                        0,
                        false,
                        "The authoritative native height buffer contains a non-finite sample."
                    );
            }
        }

        int preparedStrideCount =
            0;

        int createdAssetCount =
            0;

        int updatedAssetCount =
            0;

        bool outputMayHaveChanged =
            false;

        for (
            int index = 0;
            index < descriptors.Count;
            index++
        )
        {
            TerrainHeightStreamingLevelDescriptor descriptor =
                descriptors[index];

            if (
                !descriptor.IsStructurallyValid
                ||
                !descriptor.IsDerived
                ||
                !TerrainHeightStreamingPyramidPolicy
                    .IsDerivedStrideSupported(
                        worldSettings,
                        descriptor.SampleStride
                    )
                ||
                descriptor.SamplesPerSide !=
                    TerrainHeightStreamingPyramidPolicy
                        .GetSamplesPerSide(
                            worldSettings,
                            descriptor.SampleStride
                        )
            )
            {
                return
                    Failure(
                        coordinate,
                        requestedStrideCount,
                        preparedStrideCount,
                        createdAssetCount,
                        updatedAssetCount,
                        descriptor.SampleStride,
                        outputMayHaveChanged,
                        "A height-streaming level descriptor is incompatible with the current streaming policy."
                    );
            }

            AssetWriteOutcome outcome =
                PrepareOneRepresentation(
                    coordinate,
                    descriptor,
                    nativeSamplesPerSide,
                    authoritativeHeightData,
                    residencyBatch,
                    out bool representationMayHaveChanged,
                    out string errorMessage
                );

            outputMayHaveChanged |=
                representationMayHaveChanged;

            if (
                outcome ==
                AssetWriteOutcome.Failed
            )
            {
                return
                    Failure(
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

            if (
                outcome ==
                AssetWriteOutcome.Created
            )
            {
                createdAssetCount++;
            }
            else
            {
                updatedAssetCount++;
            }
        }

        return
            new TerrainHeightStreamingFamilyWriteResult(
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

    public static TerrainHeightStreamingFamilyWriteResult
        PrepareFamilyFromExistingNativeTexture(
            WorldSettings worldSettings,
            Vector2Int coordinate,
            IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors,
            Texture2D authoritativeTexture,
            float[] reusableNativeBuffer
        )
    {
        return
            PrepareFamilyFromExistingNativeTexture(
                worldSettings,
                coordinate,
                descriptors,
                authoritativeTexture,
                reusableNativeBuffer,
                null
            );
    }

    internal static TerrainHeightStreamingFamilyWriteResult
        PrepareFamilyFromExistingNativeTexture(
            WorldSettings worldSettings,
            Vector2Int coordinate,
            IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors,
            Texture2D authoritativeTexture,
            float[] reusableNativeBuffer,
            TerrainHeightBakeResidencyBatch residencyBatch
        )
    {
        int requestedStrideCount =
            descriptors != null
                ? descriptors.Count
                : 0;

        if (worldSettings == null)
        {
            return
                TerrainHeightStreamingFamilyWriteResult
                    .NotAttempted(
                        coordinate,
                        requestedStrideCount,
                        "WorldSettings is null."
                    );
        }

        int nativeSamplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedSampleCount =
            nativeSamplesPerSide *
            nativeSamplesPerSide;

        if (
            authoritativeTexture == null
            ||
            authoritativeTexture.width !=
                nativeSamplesPerSide
            ||
            authoritativeTexture.height !=
                nativeSamplesPerSide
            ||
            authoritativeTexture.format !=
                TextureFormat.RFloat
            ||
            reusableNativeBuffer == null
            ||
            reusableNativeBuffer.Length !=
                expectedSampleCount
        )
        {
            return
                TerrainHeightStreamingFamilyWriteResult
                    .NotAttempted(
                        coordinate,
                        requestedStrideCount,
                        "The existing authoritative runtime height texture or reusable native buffer has an invalid layout."
                    );
        }

        try
        {
            NativeArray<float> sourceData =
                authoritativeTexture.GetPixelData<float>(
                    0
                );

            if (
                sourceData.Length !=
                    expectedSampleCount
            )
            {
                return
                    TerrainHeightStreamingFamilyWriteResult
                        .NotAttempted(
                            coordinate,
                            requestedStrideCount,
                            "The existing authoritative runtime height texture has an unexpected sample count."
                        );
            }

            for (
                int index = 0;
                index < sourceData.Length;
                index++
            )
            {
                reusableNativeBuffer[index] =
                    sourceData[index];
            }
        }
        catch (Exception exception)
        {
            return
                TerrainHeightStreamingFamilyWriteResult
                    .NotAttempted(
                        coordinate,
                        requestedStrideCount,
                        "Could not read the existing authoritative runtime height texture.\n\n" +
                        exception.Message
                    );
        }

        return
            PrepareFamilyFromNativeBuffer(
                worldSettings,
                coordinate,
                descriptors,
                reusableNativeBuffer,
                residencyBatch
            );
    }

    private static AssetWriteOutcome PrepareOneRepresentation(
        Vector2Int coordinate,
        TerrainHeightStreamingLevelDescriptor descriptor,
        int nativeSamplesPerSide,
        float[] authoritativeHeightData,
        TerrainHeightBakeResidencyBatch residencyBatch,
        out bool outputMayHaveChanged,
        out string errorMessage
    )
    {
        outputMayHaveChanged =
            false;

        errorMessage =
            "";

        string assetPath =
            TerrainRuntimeHeightAssetUtility
                .GetStreamingHeightTilePath(
                    descriptor.SampleStride,
                    coordinate.x,
                    coordinate.y
                );

        Texture2D texture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(
                assetPath
            );

        if (texture != null)
        {
            residencyBatch?.TrackDirtyOutput(
                texture
            );
        }

        if (texture == null)
        {
            Texture2D createdTexture =
                null;

            try
            {
                createdTexture =
                    new Texture2D(
                        descriptor.SamplesPerSide,
                        descriptor.SamplesPerSide,
                        TextureFormat.RFloat,
                        false,
                        true
                    );

                ConfigureTexture(
                    createdTexture,
                    coordinate
                );

                FillDerivedSamples(
                    createdTexture,
                    descriptor,
                    nativeSamplesPerSide,
                    authoritativeHeightData
                );

                createdTexture.Apply(
                    false,
                    false
                );

                AssetDatabase.CreateAsset(
                    createdTexture,
                    assetPath
                );

                residencyBatch?.TrackDirtyOutput(
                    createdTexture
                );

                outputMayHaveChanged =
                    true;

                EditorUtility.SetDirty(
                    createdTexture
                );

                return
                    AssetWriteOutcome.Created;
            }
            catch (Exception exception)
            {
                outputMayHaveChanged =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        assetPath
                    ) != null;

                if (
                    createdTexture != null
                    &&
                    !outputMayHaveChanged
                )
                {
                    UnityEngine.Object.DestroyImmediate(
                        createdTexture
                    );
                }

                errorMessage =
                    "Could not create runtime height-streaming texture:\n" +
                    assetPath +
                    "\n\n" +
                    exception.Message;

                return
                    AssetWriteOutcome.Failed;
            }
        }

        try
        {
            outputMayHaveChanged =
                true;

            bool reinitialized =
                texture.Reinitialize(
                    descriptor.SamplesPerSide,
                    descriptor.SamplesPerSide,
                    TextureFormat.RFloat,
                    false
                );

            if (!reinitialized)
            {
                errorMessage =
                    "Could not reinitialize runtime height-streaming texture:\n" +
                    assetPath;

                return
                    AssetWriteOutcome.Failed;
            }

            ConfigureTexture(
                texture,
                coordinate
            );

            FillDerivedSamples(
                texture,
                descriptor,
                nativeSamplesPerSide,
                authoritativeHeightData
            );

            texture.Apply(
                false,
                false
            );

            EditorUtility.SetDirty(
                texture
            );

            return
                AssetWriteOutcome.Updated;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not update runtime height-streaming texture:\n" +
                assetPath +
                "\n\n" +
                exception.Message;

            return
                AssetWriteOutcome.Failed;
        }
    }

    private static void FillDerivedSamples(
        Texture2D texture,
        TerrainHeightStreamingLevelDescriptor descriptor,
        int nativeSamplesPerSide,
        float[] authoritativeHeightData
    )
    {
        NativeArray<float> destination =
            texture.GetPixelData<float>(
                0
            );

        int derivedSamplesPerSide =
            descriptor.SamplesPerSide;

        int expectedDestinationCount =
            derivedSamplesPerSide *
            derivedSamplesPerSide;

        if (
            destination.Length !=
                expectedDestinationCount
        )
        {
            throw new InvalidOperationException(
                "The derived height texture has an unexpected pixel-data length."
            );
        }

        int stride =
            descriptor.SampleStride;

        for (
            int z = 0;
            z < derivedSamplesPerSide;
            z++
        )
        {
            int sourceZ =
                z *
                stride;

            for (
                int x = 0;
                x < derivedSamplesPerSide;
                x++
            )
            {
                int sourceX =
                    x *
                    stride;

                int sourceIndex =
                    sourceX +
                    sourceZ *
                    nativeSamplesPerSide;

                float height =
                    authoritativeHeightData[
                        sourceIndex
                    ];

                if (!IsFinite(height))
                {
                    throw new InvalidOperationException(
                        "The authoritative source contains a non-finite height sample."
                    );
                }

                destination[
                    x +
                    z *
                    derivedSamplesPerSide
                ] =
                    height;
            }
        }
    }

    private static void ConfigureTexture(
        Texture2D texture,
        Vector2Int coordinate
    )
    {
        texture.name =
            TerrainRuntimeHeightAssetUtility
                .GetHeightTileName(
                    coordinate.x,
                    coordinate.y
                );

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Point;
    }

    private static TerrainHeightStreamingFamilyWriteResult Failure(
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
        return
            new TerrainHeightStreamingFamilyWriteResult(
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

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }
}
