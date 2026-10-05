using UnityEngine;

/*
 * Pure edit-mode planner that converts one exact clipmap layout into bounded
 * per-LOD Height residency intent.
 *
 * The planner allocates no GPU resources and performs no AssetDatabase work.
 */
internal static class TerrainAuthoringPreviewLodResidencyUtility
{
    internal static bool TryBuildPlan(
        WorldSettings worldSettings,
        TerrainClipmapLayout layout,
        int generation,
        out TerrainAuthoringPreviewResidencyPlan plan,
        out string errorMessage
    )
    {
        plan =
            null;

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (
            layout == null
            ||
            !layout.IsValid
        )
        {
            errorMessage =
                "TerrainClipmapLayout is unavailable or invalid.";

            return false;
        }

        int levelCount =
            layout.LevelCount;

        if (
            levelCount <
                TerrainClipmapTopologyUtility.MinimumLevelCount
            ||
            levelCount >
                TerrainClipmapTopologyUtility.MaximumLevelCount
        )
        {
            errorMessage =
                "The requested clipmap layout has an unsupported LOD count.";

            return false;
        }

        TerrainAuthoringPreviewResidencyPlan result =
            new TerrainAuthoringPreviewResidencyPlan
            {
                Generation =
                    generation,

                LevelCount =
                    levelCount,

                MinimumXZ =
                    layout.MinimumXZ,

                MaximumXZ =
                    layout.MaximumXZ,

                CoverageCenter =
                    layout.CoverageCenter,

                Levels =
                    new TerrainAuthoringPreviewLodResidencyPlan[
                        levelCount
                    ]
            };

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            if (
                !TerrainHeightResolutionUtility
                    .TryGetRequiredStrideForClipmapLevel(
                        worldSettings,
                        level,
                        out int sampleStride,
                        out string strideError
                    )
            )
            {
                errorMessage =
                    $"Could not resolve the Height representation for LOD{level}.\n\n" +
                    strideError;

                return false;
            }

            if (
                !TerrainHeightStreamingPyramidPolicy
                    .IsHeightRepresentationStrideSupported(
                        worldSettings,
                        sampleStride
                    )
            )
            {
                errorMessage =
                    $"LOD{level} requires Height sample stride {sampleStride}, " +
                    "which is outside the configured Height representation policy.";

                return false;
            }

            int samplesPerSide =
                TerrainHeightResolutionUtility
                    .GetSamplesPerSide(
                        worldSettings,
                        sampleStride
                    );

            float sampleSpacing =
                TerrainHeightResolutionUtility
                    .GetSampleSpacing(
                        worldSettings,
                        sampleStride
                    );

            float layoutSpacing =
                layout.GetSpacing(
                    level
                );

            if (
                !Mathf.Approximately(
                    sampleSpacing,
                    layoutSpacing
                )
            )
            {
                errorMessage =
                    $"LOD{level} clipmap spacing does not match its Height " +
                    "representation.\n\n" +
                    $"Clipmap Spacing: {layoutSpacing}\n" +
                    $"Height Spacing: {sampleSpacing}\n" +
                    $"Sample Stride: {sampleStride}";

                return false;
            }

            float coarseSampleSpacing =
                sampleSpacing;

            if (level < levelCount - 1)
            {
                if (
                    !TerrainHeightResolutionUtility
                        .TryGetRequiredStrideForClipmapLevel(
                            worldSettings,
                            level + 1,
                            out int coarseSampleStride,
                            out string coarseStrideError
                        )
                )
                {
                    errorMessage =
                        $"Could not resolve the adjacent coarse Height " +
                        $"representation for LOD{level}.\n\n" +
                        coarseStrideError;

                    return false;
                }

                if (
                    !TerrainHeightStreamingPyramidPolicy
                        .IsHeightRepresentationStrideSupported(
                            worldSettings,
                            coarseSampleStride
                        )
                )
                {
                    errorMessage =
                        $"LOD{level + 1} requires Height sample stride " +
                        $"{coarseSampleStride}, which is outside the " +
                        "configured Height representation policy.";

                    return false;
                }

                coarseSampleSpacing =
                    TerrainHeightResolutionUtility
                        .GetSampleSpacing(
                            worldSettings,
                            coarseSampleStride
                        );
            }

            if (
                !TerrainHeightClipmapCoverageUtility
                    .TryCalculateRequiredWorldBounds(
                        worldSettings,
                        layout,
                        level,
                        sampleSpacing,
                        coarseSampleSpacing,
                        out Vector2 requiredMinimumXZ,
                        out Vector2 requiredMaximumXZ,
                        out string coverageError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate required Height coverage for " +
                    $"LOD{level}.\n\n" +
                    coverageError;

                return false;
            }

            if (
                !TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateRequiredWindowForRepresentation(
                        worldSettings,
                        requiredMinimumXZ,
                        requiredMaximumXZ,
                        sampleStride,
                        out TerrainHeightCacheWindow requiredWindow,
                        out string requiredWindowError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate the required Height tile window " +
                    $"for LOD{level}.\n\n" +
                    requiredWindowError;

                return false;
            }

            if (
                !TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateDesiredWindowForRepresentation(
                        worldSettings,
                        requiredWindow,
                        TerrainAuthoringPreviewResidencyUtility.DefaultGuardTileCount,
                        out TerrainHeightCacheWindow desiredWindow,
                        out string desiredWindowError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate the desired Height tile window " +
                    $"for LOD{level}.\n\n" +
                    desiredWindowError;

                return false;
            }

            result.Levels[level] =
                new TerrainAuthoringPreviewLodResidencyPlan
                {
                    Level =
                        level,

                    SampleStride =
                        sampleStride,

                    SamplesPerSide =
                        samplesPerSide,

                    SampleSpacing =
                        sampleSpacing,

                    Anchor =
                        layout.GetAnchor(
                            level
                        ),

                    RequiredMinimumXZ =
                        requiredMinimumXZ,

                    RequiredMaximumXZ =
                        requiredMaximumXZ,

                    RequiredWindow =
                        requiredWindow,

                    DesiredWindow =
                        desiredWindow
                };
        }

        if (!result.IsStructurallyValid)
        {
            errorMessage =
                "The calculated multiresolution Height residency plan is structurally invalid.";

            return false;
        }

        plan =
            result;

        return true;
    }
}
