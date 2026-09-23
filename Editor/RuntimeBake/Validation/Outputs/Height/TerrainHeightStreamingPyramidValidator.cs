using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightStreamingPyramidValidator
{
    public static bool Validate(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate Height Streaming: WorldSettings is null."
            );

            return false;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
            );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot validate Height Streaming because the runtime height manifest is missing."
            );

            return false;
        }

        if (
            !TerrainGenerationStateUtility
                .IsHeightStreamingManifestCurrent(
                    manifest,
                    worldSettings,
                    worldSettings.heightmapGenerationRevision
                )
        )
        {
            Debug.LogError(
                "Height Streaming validation failed because the streaming manifest metadata is not current for the authoritative runtime height revision."
            );

            return false;
        }

        List<int> strides =
            new List<int>();

        if (
            !TerrainHeightStreamingPyramidPolicy
                .TryGetDerivedStrides(
                    worldSettings,
                    strides,
                    out string strideError
                )
        )
        {
            Debug.LogError(
                "Height Streaming validation failed.\n\n" +
                strideError
            );

            return false;
        }

        int validatedRepresentations =
            0;

        long comparedSamples =
            0;

        for (
            int tileZ = 0;
            tileZ < worldSettings.HeightTileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < worldSettings.HeightTileGridWidth;
                tileX++
            )
            {
                Vector2Int coordinate =
                    new Vector2Int(
                        tileX,
                        tileZ
                    );

                Texture2D nativeTexture =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        TerrainRuntimeHeightAssetUtility
                            .GetHeightTilePath(
                                tileX,
                                tileZ
                            )
                    );

                if (
                    !TryReadNativeData(
                        worldSettings,
                        coordinate,
                        nativeTexture,
                        out NativeArray<float> nativeData,
                        out string nativeError
                    )
                )
                {
                    if (nativeTexture != null)
                    {
                        Resources.UnloadAsset(
                            nativeTexture
                        );
                    }

                    Debug.LogError(
                        "Height Streaming validation failed.\n\n" +
                        nativeError
                    );

                    return false;
                }

                for (
                    int strideIndex = 0;
                    strideIndex < strides.Count;
                    strideIndex++
                )
                {
                    int stride =
                        strides[strideIndex];

                    if (
                        !manifest.TryGetStreamingLevelDescriptor(
                            stride,
                            out TerrainHeightStreamingLevelDescriptor descriptor
                        )
                    )
                    {
                        Resources.UnloadAsset(
                            nativeTexture
                        );

                        Debug.LogError(
                            $"Height Streaming validation failed because stride {stride} has no valid manifest descriptor."
                        );

                        return false;
                    }

                    string path =
                        TerrainRuntimeHeightAssetUtility
                            .GetStreamingHeightTilePath(
                                stride,
                                tileX,
                                tileZ
                            );

                    Texture2D derivedTexture =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            path
                        );

                    bool derivedValid =
                        TryValidateDerivedAgainstNative(
                            coordinate,
                            descriptor,
                            worldSettings.HeightTileSamplesPerSide,
                            nativeData,
                            derivedTexture,
                            out long representationComparisons,
                            out string derivedError
                        );

                    if (derivedTexture != null)
                    {
                        Resources.UnloadAsset(
                            derivedTexture
                        );
                    }

                    if (!derivedValid)
                    {
                        Resources.UnloadAsset(
                            nativeTexture
                        );

                        Debug.LogError(
                            "Height Streaming validation failed.\n\n" +
                            derivedError
                        );

                        return false;
                    }

                    validatedRepresentations++;

                    comparedSamples +=
                        representationComparisons;
                }

                Resources.UnloadAsset(
                    nativeTexture
                );
            }
        }

        if (
            !ValidateSharedBorders(
                worldSettings,
                manifest,
                strides,
                out string borderError
            )
        )
        {
            Debug.LogError(
                "Height Streaming validation failed.\n\n" +
                borderError
            );

            return false;
        }

        Debug.Log(
            "Height Streaming pyramid validation passed.\n\n" +
            $"Derived Strides: {string.Join(", ", strides)}\n" +
            $"Representations Validated: {validatedRepresentations:N0}\n" +
            $"Native/Derived Samples Compared: {comparedSamples:N0}\n" +
            "Exact Shared-Lattice Equality: Passed\n" +
            "Derived Tile Borders: Passed"
        );

        return true;
    }

    private static bool TryReadNativeData(
        WorldSettings worldSettings,
        Vector2Int coordinate,
        Texture2D nativeTexture,
        out NativeArray<float> nativeData,
        out string errorMessage
    )
    {
        nativeData =
            default;

        errorMessage =
            "";

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        if (
            nativeTexture == null
            ||
            nativeTexture.width !=
                samplesPerSide
            ||
            nativeTexture.height !=
                samplesPerSide
            ||
            nativeTexture.format !=
                TextureFormat.RFloat
        )
        {
            errorMessage =
                $"Authoritative runtime height tile ({coordinate.x}, {coordinate.y}) is missing or has an invalid layout.";

            return false;
        }

        try
        {
            nativeData =
                nativeTexture.GetPixelData<float>(
                    0
                );
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Could not read authoritative runtime height tile ({coordinate.x}, {coordinate.y}).\n\n" +
                exception.Message;

            return false;
        }

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        if (
            nativeData.Length !=
                expectedSampleCount
        )
        {
            errorMessage =
                $"Authoritative runtime height tile ({coordinate.x}, {coordinate.y}) has an unexpected sample count.";

            nativeData =
                default;

            return false;
        }

        return true;
    }

    private static bool TryValidateDerivedAgainstNative(
        Vector2Int coordinate,
        TerrainHeightStreamingLevelDescriptor descriptor,
        int nativeSamplesPerSide,
        NativeArray<float> nativeData,
        Texture2D derivedTexture,
        out long comparisonCount,
        out string errorMessage
    )
    {
        comparisonCount =
            0;

        errorMessage =
            "";

        if (
            derivedTexture == null
            ||
            derivedTexture.width !=
                descriptor.SamplesPerSide
            ||
            derivedTexture.height !=
                descriptor.SamplesPerSide
            ||
            derivedTexture.format !=
                TextureFormat.RFloat
        )
        {
            errorMessage =
                $"Derived height tile ({coordinate.x}, {coordinate.y}) stride {descriptor.SampleStride} is missing or has an invalid layout.";

            return false;
        }

        NativeArray<float> derivedData;

        try
        {
            derivedData =
                derivedTexture.GetPixelData<float>(
                    0
                );
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Could not read derived height tile ({coordinate.x}, {coordinate.y}) stride {descriptor.SampleStride}.\n\n" +
                exception.Message;

            return false;
        }

        int derivedSamplesPerSide =
            descriptor.SamplesPerSide;

        if (
            derivedData.Length !=
                derivedSamplesPerSide *
                derivedSamplesPerSide
        )
        {
            errorMessage =
                $"Derived height tile ({coordinate.x}, {coordinate.y}) stride {descriptor.SampleStride} has an unexpected sample count.";

            return false;
        }

        int stride =
            descriptor.SampleStride;

        for (
            int z = 0;
            z < derivedSamplesPerSide;
            z++
        )
        {
            int nativeZ =
                z *
                stride;

            for (
                int x = 0;
                x < derivedSamplesPerSide;
                x++
            )
            {
                int nativeX =
                    x *
                    stride;

                float expected =
                    nativeData[
                        nativeX +
                        nativeZ *
                        nativeSamplesPerSide
                    ];

                float actual =
                    derivedData[
                        x +
                        z *
                        derivedSamplesPerSide
                    ];

                comparisonCount++;

                if (
                    float.IsNaN(actual)
                    ||
                    float.IsInfinity(actual)
                    ||
                    actual != expected
                )
                {
                    errorMessage =
                        $"Derived height mismatch at tile ({coordinate.x}, {coordinate.y}), stride {stride}, sample ({x}, {z}).\n\n" +
                        $"Expected Native Value: {expected:R}\n" +
                        $"Derived Value: {actual:R}";

                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateSharedBorders(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        IReadOnlyList<int> strides,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        for (
            int strideIndex = 0;
            strideIndex < strides.Count;
            strideIndex++
        )
        {
            int stride =
                strides[strideIndex];

            if (
                !manifest.TryGetStreamingLevelDescriptor(
                    stride,
                    out TerrainHeightStreamingLevelDescriptor descriptor
                )
            )
            {
                errorMessage =
                    $"Missing streaming descriptor for stride {stride}.";

                return false;
            }

            int samplesPerSide =
                descriptor.SamplesPerSide;

            for (
                int tileZ = 0;
                tileZ < worldSettings.HeightTileGridHeight;
                tileZ++
            )
            {
                for (
                    int tileX = 0;
                    tileX < worldSettings.HeightTileGridWidth;
                    tileX++
                )
                {
                    Texture2D tile =
                        LoadDerived(
                            stride,
                            tileX,
                            tileZ
                        );

                    if (tile == null)
                    {
                        errorMessage =
                            $"Missing derived tile ({tileX}, {tileZ}) stride {stride} during border validation.";

                        return false;
                    }

                    NativeArray<float> data =
                        tile.GetPixelData<float>(
                            0
                        );

                    if (
                        tileX + 1 <
                            worldSettings.HeightTileGridWidth
                    )
                    {
                        Texture2D right =
                            LoadDerived(
                                stride,
                                tileX + 1,
                                tileZ
                            );

                        if (right == null)
                        {
                            errorMessage =
                                $"Missing right-neighbour derived tile for ({tileX}, {tileZ}) stride {stride}.";

                            return false;
                        }

                        NativeArray<float> rightData =
                            right.GetPixelData<float>(
                                0
                            );

                        for (
                            int sampleZ = 0;
                            sampleZ < samplesPerSide;
                            sampleZ++
                        )
                        {
                            float leftValue =
                                data[
                                    sampleZ *
                                    samplesPerSide +
                                    samplesPerSide - 1
                                ];

                            float rightValue =
                                rightData[
                                    sampleZ *
                                    samplesPerSide
                                ];

                            if (leftValue != rightValue)
                            {
                                errorMessage =
                                    $"Derived X border mismatch between ({tileX}, {tileZ}) and ({tileX + 1}, {tileZ}) at stride {stride}, sample {sampleZ}.";

                                return false;
                            }
                        }

                        Resources.UnloadAsset(
                            right
                        );
                    }

                    if (
                        tileZ + 1 <
                            worldSettings.HeightTileGridHeight
                    )
                    {
                        Texture2D upper =
                            LoadDerived(
                                stride,
                                tileX,
                                tileZ + 1
                            );

                        if (upper == null)
                        {
                            errorMessage =
                                $"Missing upper-neighbour derived tile for ({tileX}, {tileZ}) stride {stride}.";

                            return false;
                        }

                        NativeArray<float> upperData =
                            upper.GetPixelData<float>(
                                0
                            );

                        for (
                            int sampleX = 0;
                            sampleX < samplesPerSide;
                            sampleX++
                        )
                        {
                            float lowerValue =
                                data[
                                    (samplesPerSide - 1) *
                                    samplesPerSide +
                                    sampleX
                                ];

                            float upperValue =
                                upperData[
                                    sampleX
                                ];

                            if (lowerValue != upperValue)
                            {
                                errorMessage =
                                    $"Derived Z border mismatch between ({tileX}, {tileZ}) and ({tileX}, {tileZ + 1}) at stride {stride}, sample {sampleX}.";

                                return false;
                            }
                        }

                        Resources.UnloadAsset(
                            upper
                        );
                    }

                    Resources.UnloadAsset(
                        tile
                    );
                }
            }
        }

        return true;
    }

    private static Texture2D LoadDerived(
        int stride,
        int tileX,
        int tileZ
    )
    {
        return
            AssetDatabase.LoadAssetAtPath<Texture2D>(
                TerrainRuntimeHeightAssetUtility
                    .GetStreamingHeightTilePath(
                        stride,
                        tileX,
                        tileZ
                    )
            );
    }
}
