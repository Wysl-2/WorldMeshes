using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static partial class TerrainGenerationStateUtility
{
    public const int RuntimeHeightStreamingCompilerVersion =
        1;

    public static string GetCurrentHeightStreamingGenerationSignature(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return "";
        }

        List<int> strides =
            new List<int>();

        if (
            !TerrainHeightStreamingPyramidPolicy
                .TryGetDerivedStrides(
                    worldSettings,
                    strides,
                    out _
                )
        )
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "TerrainHeightStreamingPyramid"
        );

        AppendValue(
            builder,
            RuntimeHeightStreamingCompilerVersion
        );

        AppendValue(
            builder,
            TerrainHeightStreamingPyramidPolicy
                .CurrentPolicyVersion
        );

        AppendValue(
            builder,
            TerrainHeightStreamingPyramidPolicy
                .GetConfiguredMaximumStride(
                    worldSettings
                )
        );

        AppendValue(
            builder,
            TerrainHeightStreamingPyramidPolicy
                .GetMaximumSupportedDerivedStride(
                    worldSettings
                )
        );

        AppendValue(
            builder,
            worldSettings.HeightTileIntervalsPerSide
        );

        AppendValue(
            builder,
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
            /
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            )
        );

        AppendValue(
            builder,
            worldSettings.HeightTileWorldSize
        );

        AppendValue(
            builder,
            worldSettings.HeightTileGridWidth
        );

        AppendValue(
            builder,
            worldSettings.HeightTileGridHeight
        );

        AppendValue(
            builder,
            (int)TextureFormat.RFloat
        );

        AppendValue(
            builder,
            strides.Count
        );

        for (
            int index = 0;
            index < strides.Count;
            index++
        )
        {
            int stride =
                strides[index];

            AppendValue(
                builder,
                stride
            );

            AppendValue(
                builder,
                TerrainHeightStreamingPyramidPolicy
                    .GetSamplesPerSide(
                        worldSettings,
                        stride
                    )
            );
        }

        return
            ComputeSHA256(
                builder.ToString()
            );
    }

    public static bool IsHeightStreamingManifestCurrent(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings,
        int authoritativeHeightRevision
    )
    {
        if (
            manifest == null
            ||
            worldSettings == null
            ||
            !manifest.streamingPyramidIsComplete
            ||
            manifest.streamingPyramidCompilerVersion !=
                RuntimeHeightStreamingCompilerVersion
            ||
            manifest.streamingSourceHeightmapGenerationRevision !=
                authoritativeHeightRevision
        )
        {
            return false;
        }

        string signature =
            GetCurrentHeightStreamingGenerationSignature(
                worldSettings
            );

        if (
            string.IsNullOrEmpty(
                signature
            )
            ||
            !string.Equals(
                manifest.streamingGenerationSignature,
                signature,
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        List<int> strides =
            new List<int>();

        if (
            !TerrainHeightStreamingPyramidPolicy
                .TryGetDerivedStrides(
                    worldSettings,
                    strides,
                    out _
                )
            ||
            manifest.StreamingLevelCount !=
                strides.Count
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < strides.Count;
            index++
        )
        {
            int stride =
                strides[index];

            if (
                !TerrainHeightStreamingPyramidPolicy
                    .TryBuildLevelDescriptor(
                        worldSettings,
                        stride,
                        out TerrainHeightStreamingLevelDescriptor expected,
                        out _
                    )
                ||
                !manifest
                    .TryGetStreamingLevelDescriptor(
                        stride,
                        out TerrainHeightStreamingLevelDescriptor actual
                    )
                ||
                !DescriptorsMatch(
                    expected,
                    actual
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool DescriptorsMatch(
        TerrainHeightStreamingLevelDescriptor left,
        TerrainHeightStreamingLevelDescriptor right
    )
    {
        return
            left.SampleStride ==
                right.SampleStride
            &&
            Mathf.Approximately(
                left.SampleSpacing,
                right.SampleSpacing
            )
            &&
            left.SamplesPerSide ==
                right.SamplesPerSide
            &&
            Mathf.Approximately(
                left.TileWorldSize,
                right.TileWorldSize
            )
            &&
            left.TileGridWidth ==
                right.TileGridWidth
            &&
            left.TileGridHeight ==
                right.TileGridHeight
            &&
            left.TextureFormat ==
                right.TextureFormat;
    }
}
