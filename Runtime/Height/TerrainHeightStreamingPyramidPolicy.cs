using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Single source of truth for the derived runtime height-streaming pyramid.
 *
 * The policy describes which power-of-two sample strides are generated from
 * the authoritative native heightfield. It is deliberately independent from
 * player position and clipmap world-space coverage.
 */
public static class TerrainHeightStreamingPyramidPolicy
{
    public const int CurrentPolicyVersion =
        1;

    public const int MinimumDerivedStride =
        2;

    public const int DefaultMaximumStride =
        32;

    // =====================================================
    // POLICY VALIDATION
    // =====================================================

    public static bool TryValidatePolicy(
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        int maximumStride =
            GetConfiguredMaximumStride(
                worldSettings
            );

        if (!IsPowerOfTwo(maximumStride))
        {
            errorMessage =
                "Height streaming maximum stride must be a power of two.\n\n" +
                $"Configured Value: {maximumStride}";

            return false;
        }

        if (
            worldSettings
                .HeightTileIntervalsPerSide <=
                0
        )
        {
            errorMessage =
                "The height tile interval count is invalid.";

            return false;
        }

        return true;
    }

    // =====================================================
    // CONFIGURED CAP
    // =====================================================

    public static int GetConfiguredMaximumStride(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                DefaultMaximumStride;
        }

        int configured =
            worldSettings
                .heightStreamingMaximumStride;

        if (configured < MinimumDerivedStride)
        {
            return
                DefaultMaximumStride;
        }

        return
            configured;
    }

    // =====================================================
    // DERIVED STRIDE SET
    // =====================================================

    public static bool TryGetDerivedStrides(
        WorldSettings worldSettings,
        List<int> outputStrides,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (outputStrides == null)
        {
            errorMessage =
                "The output stride list is null.";

            return false;
        }

        outputStrides.Clear();

        if (
            !TryValidatePolicy(
                worldSettings,
                out errorMessage
            )
        )
        {
            return false;
        }

        int intervals =
            worldSettings
                .HeightTileIntervalsPerSide;

        int maximumStride =
            GetConfiguredMaximumStride(
                worldSettings
            );

        int stride =
            MinimumDerivedStride;

        while (stride <= maximumStride)
        {
            if (
                intervals % stride !=
                0
            )
            {
                break;
            }

            outputStrides.Add(
                stride
            );

            if (
                stride >
                    int.MaxValue / 2
            )
            {
                break;
            }

            stride *=
                2;
        }

        return true;
    }

    public static int GetMaximumSupportedDerivedStride(
        WorldSettings worldSettings
    )
    {
        if (
            !TryValidatePolicy(
                worldSettings,
                out _
            )
        )
        {
            return 0;
        }

        int intervals =
            worldSettings
                .HeightTileIntervalsPerSide;

        int maximumStride =
            GetConfiguredMaximumStride(
                worldSettings
            );

        int resolvedMaximum =
            0;

        int stride =
            MinimumDerivedStride;

        while (stride <= maximumStride)
        {
            if (
                intervals % stride !=
                0
            )
            {
                break;
            }

            resolvedMaximum =
                stride;

            if (
                stride >
                    int.MaxValue / 2
            )
            {
                break;
            }

            stride *=
                2;
        }

        return
            resolvedMaximum;
    }

    public static bool IsDerivedStrideSupported(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (
            !TryValidatePolicy(
                worldSettings,
                out _
            )
            ||
            sampleStride <
                MinimumDerivedStride
            ||
            !IsPowerOfTwo(
                sampleStride
            )
            ||
            sampleStride >
                GetConfiguredMaximumStride(
                    worldSettings
                )
        )
        {
            return false;
        }

        return
            worldSettings
                .HeightTileIntervalsPerSide %
                sampleStride ==
                0;
    }

    public static bool IsHeightRepresentationStrideSupported(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (sampleStride == 1)
        {
            return
                worldSettings != null
                &&
                worldSettings
                    .HeightTileIntervalsPerSide >
                    0;
        }

        return
            IsDerivedStrideSupported(
                worldSettings,
                sampleStride
            );
    }

    // =====================================================
    // REPRESENTATION LAYOUT
    // =====================================================

    public static int GetSamplesPerSide(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (
            !IsHeightRepresentationStrideSupported(
                worldSettings,
                sampleStride
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "The sample stride is not supported by the current height-streaming policy."
            );
        }

        return
            worldSettings
                .HeightTileIntervalsPerSide /
                sampleStride +
            1;
    }

    public static float GetSampleSpacing(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (
            !IsHeightRepresentationStrideSupported(
                worldSettings,
                sampleStride
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "The sample stride is not supported by the current height-streaming policy."
            );
        }

        float nativeSpacing =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
            /
            Mathf.Max(
                1,
                worldSettings
                    .heightfieldResolutionPerChunk
            );

        return
            nativeSpacing *
            sampleStride;
    }

    public static bool TryBuildLevelDescriptor(
        WorldSettings worldSettings,
        int sampleStride,
        out TerrainHeightStreamingLevelDescriptor descriptor,
        out string errorMessage
    )
    {
        descriptor =
            default;

        errorMessage =
            "";

        if (
            !IsDerivedStrideSupported(
                worldSettings,
                sampleStride
            )
        )
        {
            errorMessage =
                "The requested derived height-streaming stride is not supported.\n\n" +
                $"Requested Stride: {sampleStride}";

            return false;
        }

        descriptor =
            TerrainHeightStreamingLevelDescriptor
                .Create(
                    sampleStride,
                    GetSampleSpacing(
                        worldSettings,
                        sampleStride
                    ),
                    GetSamplesPerSide(
                        worldSettings,
                        sampleStride
                    ),
                    worldSettings
                        .HeightTileWorldSize,
                    worldSettings
                        .HeightTileGridWidth,
                    worldSettings
                        .HeightTileGridHeight,
                    TextureFormat.RFloat
                );

        if (!descriptor.IsStructurallyValid)
        {
            errorMessage =
                "The derived height-streaming descriptor is structurally invalid.";

            descriptor =
                default;

            return false;
        }

        return true;
    }

    // =====================================================
    // CLIPMAP -> HEIGHT STRIDE
    // =====================================================

    public static bool TryGetRequiredStrideForClipmapLevel(
        WorldSettings worldSettings,
        int level,
        out int sampleStride,
        out string errorMessage
    )
    {
        sampleStride =
            0;

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (
            level < 0
            ||
            level >=
                TerrainClipmapTopologyUtility
                    .MaximumLevelCount
        )
        {
            errorMessage =
                "The requested clipmap level is outside the supported range.";

            return false;
        }

        return TryGetRequiredStrideForClipmapLevel(
            worldSettings.clipmapBaseSampleStep,
            level,
            out sampleStride,
            out errorMessage
        );
    }

    public static bool TryGetRequiredStrideForClipmapLevel(
        int baseSampleStep,
        int level,
        out int sampleStride,
        out string errorMessage
    )
    {
        sampleStride = 0;
        errorMessage = "";

        if (
            level < 0
            || level >= TerrainClipmapTopologyUtility.MaximumLevelCount
        )
        {
            errorMessage =
                "The requested clipmap level is outside the supported range.";
            return false;
        }

        long stride =
            Mathf.Max(
                1,
                baseSampleStep
            );

        for (
            int index = 0;
            index < level;
            index++
        )
        {
            stride *=
                2L;

            if (stride > int.MaxValue)
            {
                errorMessage =
                    $"Clipmap LOD{level} requires a sample stride larger than Int32 can represent.";

                return false;
            }
        }

        sampleStride =
            (int)stride;

        return true;
    }

    public static bool TryValidateClipmapCompatibility(
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryValidatePolicy(
                worldSettings,
                out errorMessage
            )
        )
        {
            return false;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings
                    .clipmapLevelCount,
                TerrainClipmapTopologyUtility
                    .MinimumLevelCount,
                TerrainClipmapTopologyUtility
                    .MaximumLevelCount
            );

        int maximumSupportedStride =
            Mathf.Max(
                1,
                GetMaximumSupportedDerivedStride(
                    worldSettings
                )
            );

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            if (
                !TryGetRequiredStrideForClipmapLevel(
                    worldSettings,
                    level,
                    out int requiredStride,
                    out errorMessage
                )
            )
            {
                return false;
            }

            if (
                !IsHeightRepresentationStrideSupported(
                    worldSettings,
                    requiredStride
                )
            )
            {
                errorMessage =
                    $"Height streaming configuration is incompatible with clipmap LOD{level}.\n\n" +
                    $"Required Sample Stride: {requiredStride}\n" +
                    $"Maximum Supported Stride: {maximumSupportedStride}\n" +
                    $"Configured Streaming Maximum Stride: " +
                    $"{GetConfiguredMaximumStride(worldSettings)}\n" +
                    $"Height Tile Intervals Per Side: " +
                    $"{worldSettings.HeightTileIntervalsPerSide}";

                return false;
            }
        }

        return true;
    }

    public static bool IsPowerOfTwo(
        int value
    )
    {
        return
            value > 0
            &&
            (
                value &
                (value - 1)
            ) == 0;
    }
}
