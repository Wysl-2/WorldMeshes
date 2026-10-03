using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Deterministic policy for Surface streaming representations.
 *
 * Surface resolution follows the clipmap sample lattice directly. The policy
 * intentionally owns no serialized configuration of its own and does not
 * depend on generated Height streaming assets.
 */
public static class TerrainSurfaceStreamingPyramidPolicy
{
    public const int CurrentPolicyVersion =
        1;

    public const int MinimumDerivedStride =
        2;

    public static bool TryValidatePolicy(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
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

        if (
            manifest == null
            ||
            !manifest.isComplete
        )
        {
            errorMessage =
                "The authoritative Surface manifest is missing or incomplete.";

            return false;
        }

        if (
            manifest.samplesPerSide < 2
            ||
            manifest.tileGridWidth <= 0
            ||
            manifest.tileGridHeight <= 0
            ||
            !IsFinite(manifest.sampleSpacing)
            ||
            manifest.sampleSpacing <= 0f
            ||
            !IsFinite(manifest.tileWorldSize)
            ||
            manifest.tileWorldSize <= 0f
        )
        {
            errorMessage =
                "The authoritative Surface manifest has an invalid native layout.";

            return false;
        }

        int nativeIntervals =
            manifest.samplesPerSide - 1;

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
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
                !IsPowerOfTwo(
                    requiredStride
                )
            )
            {
                errorMessage =
                    $"Surface streaming configuration is incompatible with clipmap LOD{level}.\n\n" +
                    $"Required Sample Stride: {requiredStride}\n" +
                    "Surface streaming representations require power-of-two sample strides.";

                return false;
            }

            if (
                requiredStride >
                    nativeIntervals
                ||
                nativeIntervals %
                    requiredStride !=
                    0
            )
            {
                errorMessage =
                    $"Surface streaming configuration is incompatible with clipmap LOD{level}.\n\n" +
                    $"Required Sample Stride: {requiredStride}\n" +
                    $"Native Surface Intervals Per Side: {nativeIntervals}";

                return false;
            }

            int samplesPerSide =
                nativeIntervals /
                    requiredStride +
                1;

            if (samplesPerSide < 2)
            {
                errorMessage =
                    $"Surface streaming stride {requiredStride} would produce an invalid representation resolution.";

                return false;
            }
        }

        return true;
    }

    public static bool TryGetDerivedStrides(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
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
                manifest,
                out errorMessage
            )
        )
        {
            return false;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
            );

        int previousStride =
            1;

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
                outputStrides.Clear();
                return false;
            }

            if (
                requiredStride > 1
                &&
                requiredStride !=
                    previousStride
            )
            {
                outputStrides.Add(
                    requiredStride
                );
            }

            previousStride =
                requiredStride;
        }

        return true;
    }

    public static bool IsSurfaceRepresentationStrideSupported(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        int sampleStride
    )
    {
        if (
            worldSettings == null
            ||
            manifest == null
            ||
            !manifest.isComplete
            ||
            sampleStride < 1
            ||
            !IsPowerOfTwo(
                sampleStride
            )
        )
        {
            return false;
        }

        int nativeIntervals =
            manifest.samplesPerSide - 1;

        if (
            nativeIntervals <= 0
            ||
            sampleStride > nativeIntervals
            ||
            nativeIntervals % sampleStride != 0
        )
        {
            return false;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
            );

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            if (
                TryGetRequiredStrideForClipmapLevel(
                    worldSettings,
                    level,
                    out int requiredStride,
                    out _
                )
                &&
                requiredStride ==
                    sampleStride
            )
            {
                return true;
            }
        }

        return
            sampleStride == 1;
    }

    public static int GetSamplesPerSide(
        TerrainSurfaceMaskManifest manifest,
        int sampleStride
    )
    {
        if (
            manifest == null
            ||
            sampleStride < 1
            ||
            !IsPowerOfTwo(
                sampleStride
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "The Surface sample stride is invalid."
            );
        }

        int nativeIntervals =
            manifest.samplesPerSide - 1;

        if (
            nativeIntervals <= 0
            ||
            sampleStride > nativeIntervals
            ||
            nativeIntervals % sampleStride != 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "The Surface sample stride is incompatible with the native Surface lattice."
            );
        }

        return
            nativeIntervals /
                sampleStride +
            1;
    }

    public static float GetSampleSpacing(
        TerrainSurfaceMaskManifest manifest,
        int sampleStride
    )
    {
        GetSamplesPerSide(
            manifest,
            sampleStride
        );

        return
            manifest.sampleSpacing *
            sampleStride;
    }

    public static bool TryBuildLevelDescriptor(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        int sampleStride,
        out TerrainSurfaceStreamingLevelDescriptor descriptor,
        out string errorMessage
    )
    {
        descriptor =
            default;

        errorMessage =
            "";

        if (
            sampleStride <= 1
            ||
            !IsSurfaceRepresentationStrideSupported(
                worldSettings,
                manifest,
                sampleStride
            )
        )
        {
            errorMessage =
                "The requested derived Surface streaming stride is not required by the current clipmap configuration.";

            return false;
        }

        descriptor =
            TerrainSurfaceStreamingLevelDescriptor
                .Create(
                    sampleStride,
                    GetSampleSpacing(
                        manifest,
                        sampleStride
                    ),
                    GetSamplesPerSide(
                        manifest,
                        sampleStride
                    ),
                    manifest.tileWorldSize,
                    manifest.tileGridWidth,
                    manifest.tileGridHeight,
                    TextureFormat.R8
                );

        if (!descriptor.IsStructurallyValid)
        {
            descriptor =
                default;

            errorMessage =
                "The derived Surface streaming descriptor is structurally invalid.";

            return false;
        }

        return true;
    }

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
                TerrainClipmapTopologyUtility.MaximumLevelCount
        )
        {
            errorMessage =
                "The requested clipmap level is outside the supported range.";

            return false;
        }

        long stride =
            Mathf.Max(
                1,
                worldSettings.clipmapBaseSampleStep
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
                    $"Clipmap LOD{level} requires a Surface sample stride larger than Int32 can represent.";

                return false;
            }
        }

        sampleStride =
            (int)stride;

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

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }
}
