using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainSurfaceStreamingPyramidValidator
{
    public static bool Validate(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate Surface Streaming: WorldSettings is null."
            );

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Surface Streaming validation requires current authoritative Surface masks."
            );

            return false;
        }

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (
            manifest == null
            || !manifest.isComplete
        )
        {
            Debug.LogError(
                "Surface Streaming validation failed because the authoritative Surface manifest is missing or incomplete."
            );

            return false;
        }

        List<int> strides =
            new List<int>();

        if (
            !TerrainSurfaceStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                manifest,
                strides,
                out string strideError
            )
        )
        {
            Debug.LogError(
                "Surface Streaming validation failed.\n\n" +
                strideError
            );

            return false;
        }

        List<TerrainSurfaceStreamingLevelDescriptor> expectedDescriptors =
            new List<TerrainSurfaceStreamingLevelDescriptor>(
                strides.Count
            );

        for (
            int index = 0;
            index < strides.Count;
            index++
        )
        {
            if (
                !TerrainSurfaceStreamingPyramidPolicy.TryBuildLevelDescriptor(
                    worldSettings,
                    manifest,
                    strides[index],
                    out TerrainSurfaceStreamingLevelDescriptor descriptor,
                    out string descriptorError
                )
            )
            {
                Debug.LogError(
                    "Surface Streaming validation failed.\n\n" +
                    descriptorError
                );

                return false;
            }

            expectedDescriptors.Add(descriptor);
        }

        if (
            !ValidateManifestMetadata(
                manifest,
                expectedDescriptors,
                out string metadataError
            )
        )
        {
            Debug.LogError(
                "Surface Streaming validation failed.\n\n" +
                metadataError
            );

            return false;
        }

        int validatedRepresentations = 0;
        long comparedSamples = 0L;

        for (
            int tileZ = 0;
            tileZ < manifest.tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < manifest.tileGridWidth;
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
                        TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(
                            tileX,
                            tileZ
                        )
                    );

                if (
                    !TryReadNativeData(
                        manifest,
                        coordinate,
                        nativeTexture,
                        out NativeArray<byte> nativeData,
                        out string nativeError
                    )
                )
                {
                    if (nativeTexture != null)
                    {
                        Resources.UnloadAsset(nativeTexture);
                    }

                    Debug.LogError(
                        "Surface Streaming validation failed.\n\n" +
                        nativeError
                    );

                    return false;
                }

                for (
                    int descriptorIndex = 0;
                    descriptorIndex < expectedDescriptors.Count;
                    descriptorIndex++
                )
                {
                    TerrainSurfaceStreamingLevelDescriptor descriptor =
                        expectedDescriptors[descriptorIndex];

                    Texture2D derivedTexture =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            TerrainRuntimeSurfaceMaskAssetUtility.GetStreamingSurfaceTilePath(
                                descriptor.SampleStride,
                                tileX,
                                tileZ
                            )
                        );

                    bool representationValid =
                        TryValidateDerivedAgainstNative(
                            coordinate,
                            descriptor,
                            manifest.samplesPerSide,
                            nativeData,
                            derivedTexture,
                            out long representationComparisons,
                            out string representationError
                        );

                    if (derivedTexture != null)
                    {
                        Resources.UnloadAsset(derivedTexture);
                    }

                    if (!representationValid)
                    {
                        Resources.UnloadAsset(nativeTexture);

                        Debug.LogError(
                            "Surface Streaming validation failed.\n\n" +
                            representationError
                        );

                        return false;
                    }

                    validatedRepresentations++;
                    comparedSamples +=
                        representationComparisons;
                }

                Resources.UnloadAsset(nativeTexture);
            }
        }

        if (
            !ValidateSharedBorders(
                manifest,
                expectedDescriptors,
                out string borderError
            )
        )
        {
            Debug.LogError(
                "Surface Streaming validation failed.\n\n" +
                borderError
            );

            return false;
        }

        Debug.Log(
            "Surface Streaming pyramid validation passed.\n\n" +
            $"Derived Strides: {(strides.Count > 0 ? string.Join(", ", strides) : "None")}\n" +
            $"Representations Validated: {validatedRepresentations:N0}\n" +
            $"Native/Derived Samples Compared: {comparedSamples:N0}\n" +
            "Exact Shared-Lattice Equality: Passed\n" +
            "Derived Tile Borders: Passed"
        );

        return true;
    }

    private static bool ValidateManifestMetadata(
        TerrainSurfaceMaskManifest manifest,
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> expectedDescriptors,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !manifest.streamingPyramidIsComplete
            || manifest.streamingPyramidCompilerVersion !=
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion
            || manifest.streamingPyramidPolicyVersion !=
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion
            || manifest.streamingSourceSurfaceMaskGenerationRevision !=
                manifest.surfaceMaskGenerationRevision
            || !string.Equals(
                manifest.streamingSourceSurfaceGenerationSignature ?? "",
                manifest.surfaceGenerationSignature ?? "",
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Surface Streaming manifest metadata is incomplete or does not match the authoritative Surface generation identity.";

            return false;
        }

        if (
            manifest.StreamingLevelCount !=
                expectedDescriptors.Count
        )
        {
            errorMessage =
                "Surface Streaming manifest descriptor count does not match the current clipmap-driven policy.";

            return false;
        }

        for (
            int index = 0;
            index < expectedDescriptors.Count;
            index++
        )
        {
            TerrainSurfaceStreamingLevelDescriptor expected =
                expectedDescriptors[index];

            if (
                !manifest.TryGetStreamingLevelDescriptor(
                    expected.SampleStride,
                    out TerrainSurfaceStreamingLevelDescriptor actual
                )
                || !DescriptorsMatch(
                    expected,
                    actual
                )
            )
            {
                errorMessage =
                    $"Surface Streaming manifest descriptor for stride {expected.SampleStride} is missing or incompatible.";

                return false;
            }
        }

        string expectedSignature =
            TerrainSurfaceSignatureUtility.GetStreamingGenerationSignature(
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion,
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion,
                manifest.surfaceMaskGenerationRevision,
                manifest.surfaceGenerationSignature,
                manifest,
                expectedDescriptors
            );

        if (
            string.IsNullOrEmpty(expectedSignature)
            || !string.Equals(
                manifest.streamingGenerationSignature,
                expectedSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Surface Streaming manifest generation signature does not match the current representation target.";

            return false;
        }

        return true;
    }

    private static bool TryReadNativeData(
        TerrainSurfaceMaskManifest manifest,
        Vector2Int coordinate,
        Texture2D nativeTexture,
        out NativeArray<byte> nativeData,
        out string errorMessage
    )
    {
        nativeData = default;
        errorMessage = "";

        if (
            nativeTexture == null
            || nativeTexture.width != manifest.samplesPerSide
            || nativeTexture.height != manifest.samplesPerSide
            || nativeTexture.format != TextureFormat.R8
        )
        {
            errorMessage =
                $"Authoritative Surface tile ({coordinate.x}, {coordinate.y}) is missing or has an invalid layout.";

            return false;
        }

        try
        {
            nativeData =
                nativeTexture.GetPixelData<byte>(0);
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Could not read authoritative Surface tile ({coordinate.x}, {coordinate.y}).\n\n" +
                exception.Message;

            return false;
        }

        int expectedCount =
            manifest.samplesPerSide *
            manifest.samplesPerSide;

        if (nativeData.Length != expectedCount)
        {
            errorMessage =
                $"Authoritative Surface tile ({coordinate.x}, {coordinate.y}) has an unexpected sample count.";

            nativeData = default;
            return false;
        }

        return true;
    }

    private static bool TryValidateDerivedAgainstNative(
        Vector2Int coordinate,
        TerrainSurfaceStreamingLevelDescriptor descriptor,
        int nativeSamplesPerSide,
        NativeArray<byte> nativeData,
        Texture2D derivedTexture,
        out long comparisonCount,
        out string errorMessage
    )
    {
        comparisonCount = 0L;
        errorMessage = "";

        if (
            derivedTexture == null
            || derivedTexture.width != descriptor.SamplesPerSide
            || derivedTexture.height != descriptor.SamplesPerSide
            || derivedTexture.format != TextureFormat.R8
        )
        {
            errorMessage =
                $"Derived Surface tile ({coordinate.x}, {coordinate.y}) stride {descriptor.SampleStride} is missing or has an invalid layout.";

            return false;
        }

        NativeArray<byte> derivedData;

        try
        {
            derivedData =
                derivedTexture.GetPixelData<byte>(0);
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Could not read derived Surface tile ({coordinate.x}, {coordinate.y}) stride {descriptor.SampleStride}.\n\n" +
                exception.Message;

            return false;
        }

        int samplesPerSide =
            descriptor.SamplesPerSide;

        if (
            derivedData.Length !=
                samplesPerSide * samplesPerSide
        )
        {
            errorMessage =
                $"Derived Surface tile ({coordinate.x}, {coordinate.y}) stride {descriptor.SampleStride} has an unexpected sample count.";

            return false;
        }

        int stride =
            descriptor.SampleStride;

        for (
            int z = 0;
            z < samplesPerSide;
            z++
        )
        {
            int nativeZ =
                z * stride;

            for (
                int x = 0;
                x < samplesPerSide;
                x++
            )
            {
                int nativeX =
                    x * stride;

                byte expected =
                    nativeData[
                        nativeX +
                        nativeZ * nativeSamplesPerSide
                    ];

                byte actual =
                    derivedData[
                        x +
                        z * samplesPerSide
                    ];

                comparisonCount++;

                if (actual != expected)
                {
                    errorMessage =
                        $"Derived Surface mismatch at tile ({coordinate.x}, {coordinate.y}), stride {stride}, sample ({x}, {z}).\n\n" +
                        $"Expected Native Value: {expected}\n" +
                        $"Derived Value: {actual}";

                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateSharedBorders(
        TerrainSurfaceMaskManifest manifest,
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> descriptors,
        out string errorMessage
    )
    {
        errorMessage = "";

        for (
            int descriptorIndex = 0;
            descriptorIndex < descriptors.Count;
            descriptorIndex++
        )
        {
            TerrainSurfaceStreamingLevelDescriptor descriptor =
                descriptors[descriptorIndex];

            int stride =
                descriptor.SampleStride;

            int samplesPerSide =
                descriptor.SamplesPerSide;

            for (
                int tileZ = 0;
                tileZ < manifest.tileGridHeight;
                tileZ++
            )
            {
                for (
                    int tileX = 0;
                    tileX < manifest.tileGridWidth;
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
                            $"Missing derived Surface tile ({tileX}, {tileZ}) stride {stride} during border validation.";

                        return false;
                    }

                    NativeArray<byte> data;

                    try
                    {
                        data = tile.GetPixelData<byte>(0);
                    }
                    catch (Exception exception)
                    {
                        Resources.UnloadAsset(tile);

                        errorMessage =
                            $"Could not read derived Surface tile ({tileX}, {tileZ}) stride {stride} during border validation.\n\n" +
                            exception.Message;

                        return false;
                    }

                    if (
                        tileX + 1 <
                            manifest.tileGridWidth
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
                            Resources.UnloadAsset(tile);
                            errorMessage =
                                $"Missing right-neighbour derived Surface tile for ({tileX}, {tileZ}) stride {stride}.";
                            return false;
                        }

                        NativeArray<byte> rightData =
                            right.GetPixelData<byte>(0);

                        for (
                            int sampleZ = 0;
                            sampleZ < samplesPerSide;
                            sampleZ++
                        )
                        {
                            byte leftValue =
                                data[
                                    sampleZ * samplesPerSide +
                                    samplesPerSide - 1
                                ];

                            byte rightValue =
                                rightData[
                                    sampleZ * samplesPerSide
                                ];

                            if (leftValue != rightValue)
                            {
                                Resources.UnloadAsset(right);
                                Resources.UnloadAsset(tile);
                                errorMessage =
                                    $"Derived Surface X border mismatch between ({tileX}, {tileZ}) and ({tileX + 1}, {tileZ}) at stride {stride}, sample {sampleZ}.";
                                return false;
                            }
                        }

                        Resources.UnloadAsset(right);
                    }

                    if (
                        tileZ + 1 <
                            manifest.tileGridHeight
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
                            Resources.UnloadAsset(tile);
                            errorMessage =
                                $"Missing upper-neighbour derived Surface tile for ({tileX}, {tileZ}) stride {stride}.";
                            return false;
                        }

                        NativeArray<byte> upperData =
                            upper.GetPixelData<byte>(0);

                        for (
                            int sampleX = 0;
                            sampleX < samplesPerSide;
                            sampleX++
                        )
                        {
                            byte lowerValue =
                                data[
                                    (samplesPerSide - 1) *
                                    samplesPerSide +
                                    sampleX
                                ];

                            byte upperValue =
                                upperData[sampleX];

                            if (lowerValue != upperValue)
                            {
                                Resources.UnloadAsset(upper);
                                Resources.UnloadAsset(tile);
                                errorMessage =
                                    $"Derived Surface Z border mismatch between ({tileX}, {tileZ}) and ({tileX}, {tileZ + 1}) at stride {stride}, sample {sampleX}.";
                                return false;
                            }
                        }

                        Resources.UnloadAsset(upper);
                    }

                    Resources.UnloadAsset(tile);
                }
            }
        }

        return true;
    }

    private static bool DescriptorsMatch(
        TerrainSurfaceStreamingLevelDescriptor left,
        TerrainSurfaceStreamingLevelDescriptor right
    )
    {
        return
            left.SampleStride == right.SampleStride
            && Mathf.Approximately(
                left.SampleSpacing,
                right.SampleSpacing
            )
            && left.SamplesPerSide == right.SamplesPerSide
            && Mathf.Approximately(
                left.TileWorldSize,
                right.TileWorldSize
            )
            && left.TileGridWidth == right.TileGridWidth
            && left.TileGridHeight == right.TileGridHeight
            && left.TextureFormat == right.TextureFormat;
    }

    private static Texture2D LoadDerived(
        int stride,
        int tileX,
        int tileZ
    )
    {
        return
            AssetDatabase.LoadAssetAtPath<Texture2D>(
                TerrainRuntimeSurfaceMaskAssetUtility.GetStreamingSurfaceTilePath(
                    stride,
                    tileX,
                    tileZ
                )
            );
    }
}
