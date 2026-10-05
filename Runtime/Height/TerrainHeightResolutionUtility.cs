using System;
using UnityEngine;

/*
 * Shared Height representation mathematics.
 *
 * This utility describes structural relationships that are common to native
 * and derived Height representations. It deliberately owns no runtime
 * generated-data policy, Addressables state, Editor residency, or GPU
 * resources.
 */
public static class TerrainHeightResolutionUtility
{
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

    public static bool IsRepresentationStrideCompatible(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (
            worldSettings == null
            ||
            sampleStride < 1
            ||
            !IsPowerOfTwo(
                sampleStride
            )
            ||
            worldSettings
                .HeightTileIntervalsPerSide <=
                0
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

    public static int GetSamplesPerSide(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (
            !IsRepresentationStrideCompatible(
                worldSettings,
                sampleStride
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "The sample stride is not compatible with the current Height tile topology."
            );
        }

        return
            worldSettings
                .HeightTileIntervalsPerSide /
                sampleStride +
            1;
    }

    public static float GetNativeSampleSpacing(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            throw new ArgumentNullException(
                nameof(worldSettings)
            );
        }

        return
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
    }

    public static float GetSampleSpacing(
        WorldSettings worldSettings,
        int sampleStride
    )
    {
        if (
            !IsRepresentationStrideCompatible(
                worldSettings,
                sampleStride
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "The sample stride is not compatible with the current Height tile topology."
            );
        }

        return
            GetNativeSampleSpacing(
                worldSettings
            )
            *
            sampleStride;
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

        return
            TryGetRequiredStrideForClipmapLevel(
                worldSettings
                    .clipmapBaseSampleStep,
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
        sampleStride =
            0;

        errorMessage =
            "";

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
}
