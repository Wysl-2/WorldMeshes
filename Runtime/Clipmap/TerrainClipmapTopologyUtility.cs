using UnityEngine;

/*
 * Shared deterministic clipmap topology calculations.
 *
 * This utility owns the relationship between:
 *
 * - power-of-two LOD vertex spacing
 * - exact per-LOD outer grid resolution
 * - world-space coverage
 * - ring inner boundaries
 * - 2:1 stitch requirements
 *
 * It contains no scene, streaming, mesh asset, or editor state.
 */
public static class TerrainClipmapTopologyUtility
{
    public const int MinimumLevelCount =
        1;

    public const int MaximumLevelCount =
        10;

    public const int MinimumOuterResolution =
        8;

    public const int OuterResolutionAlignment =
        4;

    // =====================================================
    // LOD OUTER RESOLUTION
    // =====================================================

    /*
     * LOD0 continues to use clipmapCenterResolution.
     *
     * LOD1+ use clipmapLODOuterResolutions when an explicit
     * positive entry exists.
     *
     * Missing entries deliberately fall back to the LOD0
     * resolution. This reproduces the legacy WorldMeshes
     * topology exactly for WorldSettings assets created before
     * per-LOD coverage was introduced.
     */
    public static int GetLODOuterResolution(
        WorldSettings worldSettings,
        int level
    )
    {
        if (worldSettings == null)
        {
            return
                MinimumOuterResolution;
        }

        int centerResolution =
            Mathf.Max(
                MinimumOuterResolution,
                worldSettings.clipmapCenterResolution
            );

        int safeLevel =
            Mathf.Max(
                0,
                level
            );

        if (safeLevel == 0)
        {
            return
                centerResolution;
        }

        int[] outerResolutions =
            worldSettings.clipmapLODOuterResolutions;

        int index =
            safeLevel -
            1;

        if (
            outerResolutions == null
            ||
            index < 0
            ||
            index >=
                outerResolutions.Length
            ||
            outerResolutions[index] <=
                0
        )
        {
            return
                centerResolution;
        }

        return
            outerResolutions[index];
    }

    // =====================================================
    // LOD SPACING
    // =====================================================

    public static float GetLODSpacing(
        WorldSettings worldSettings,
        int level
    )
    {
        float baseSpacing =
            1f;

        if (worldSettings != null)
        {
            baseSpacing =
                Mathf.Max(
                    0.0001f,
                    worldSettings
                        .ClipmapBaseSpacing
                );
        }

        int safeLevel =
            Mathf.Max(
                0,
                level
            );

        return
            baseSpacing *
            Mathf.Pow(
                2f,
                safeLevel
            );
    }

    // =====================================================
    // COVERAGE
    // =====================================================

    public static float GetLODOuterCoverage(
        WorldSettings worldSettings,
        int level
    )
    {
        return
            GetLODOuterResolution(
                worldSettings,
                level
            )
            *
            GetLODSpacing(
                worldSettings,
                level
            );
    }

    public static float GetLODHalfExtent(
        WorldSettings worldSettings,
        int level
    )
    {
        return
            GetLODOuterCoverage(
                worldSettings,
                level
            )
            *
            0.5f;
    }

    public static float CalculateClipmapDiameter(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                0f;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                MinimumLevelCount,
                MaximumLevelCount
            );

        return
            GetLODOuterCoverage(
                worldSettings,
                levelCount - 1
            );
    }

    // =====================================================
    // RING INNER BOUNDARY
    // =====================================================

    /*
     * The coarse ring is expressed in its own vertex-spacing
     * units.
     *
     * The finer outer boundary is one half-resolution away from
     * the finer center. Because the coarse spacing is exactly
     * twice the finer spacing, the same physical boundary is:
     *
     *     finerOuterResolution / 4
     *
     * coarse cells from the coarse center.
     *
     * One additional coarse cell is reserved for the existing
     * adaptive stitch band.
     */
    public static int GetRingInnerHalfResolution(
        WorldSettings worldSettings,
        int coarseLevel
    )
    {
        if (coarseLevel <= 0)
        {
            return
                0;
        }

        int finerOuterResolution =
            GetLODOuterResolution(
                worldSettings,
                coarseLevel - 1
            );

        return
            finerOuterResolution /
            4
            +
            1;
    }

    public static int GetRingHoleQuadsPerSide(
        int finerOuterResolution
    )
    {
        int safeFinerResolution =
            Mathf.Max(
                MinimumOuterResolution,
                finerOuterResolution
            );

        return
            safeFinerResolution /
            2
            +
            2;
    }

    // =====================================================
    // RESOLUTION ALIGNMENT
    // =====================================================

    public static int AlignOuterResolutionUp(
        int resolution
    )
    {
        int safeResolution =
            Mathf.Max(
                MinimumOuterResolution,
                resolution
            );

        int remainder =
            safeResolution %
            OuterResolutionAlignment;

        if (remainder == 0)
        {
            return
                safeResolution;
        }

        return
            safeResolution
            +
            (
                OuterResolutionAlignment -
                remainder
            );
    }

    /*
     * Minimum coarse resolution that:
     *
     * - preserves the current one-cell coarse stitch gap
     * - leaves at least one coarse ring cell outside that gap
     * - contains the finer LOD for every valid independently
     *   snapped adjacent-LOD offset
     *
     * All returned resolutions remain divisible by four.
     */
    public static int GetMinimumOuterResolutionForFiner(
        int finerOuterResolution
    )
    {
        int safeFinerResolution =
            AlignOuterResolutionUp(
                finerOuterResolution
            );

        int minimumResolution =
            safeFinerResolution /
            2
            +
            4;

        return
            AlignOuterResolutionUp(
                minimumResolution
            );
    }

    /*
     * Converts a user-facing world-space coverage request into
     * exact generated topology.
     *
     * Coverage is rounded upward so the generated LOD never
     * covers less terrain than requested.
     */
    public static int ResolveOuterResolutionForCoverage(
        float requestedCoverage,
        float spacing,
        int finerOuterResolution
    )
    {
        float safeSpacing =
            Mathf.Max(
                0.0001f,
                spacing
            );

        float safeCoverage =
            IsFinite(
                requestedCoverage
            )
                ? Mathf.Max(
                    0f,
                    requestedCoverage
                )
                : 0f;

        int requestedResolution =
            Mathf.CeilToInt(
                safeCoverage /
                safeSpacing
            );

        int minimumResolution =
            finerOuterResolution > 0
                ? GetMinimumOuterResolutionForFiner(
                    finerOuterResolution
                )
                : MinimumOuterResolution;

        return
            AlignOuterResolutionUp(
                Mathf.Max(
                    requestedResolution,
                    minimumResolution
                )
            );
    }

    // =====================================================
    // TRIANGLE ESTIMATES
    // =====================================================

    public static long CalculateCenterTriangleCount(
        int outerResolution
    )
    {
        long resolution =
            Mathf.Max(
                MinimumOuterResolution,
                outerResolution
            );

        return
            2L *
            resolution *
            resolution;
    }

    public static long CalculateCenterTriangleCount(
        WorldSettings worldSettings
    )
    {
        return
            CalculateCenterTriangleCount(
                GetLODOuterResolution(
                    worldSettings,
                    0
                )
            );
    }

    public static long CalculateRingTriangleCount(
        int outerResolution,
        int finerOuterResolution
    )
    {
        long outer =
            Mathf.Max(
                MinimumOuterResolution,
                outerResolution
            );

        long hole =
            GetRingHoleQuadsPerSide(
                finerOuterResolution
            );

        long ringQuads =
            outer *
            outer
            -
            hole *
            hole;

        return
            (
                ringQuads >
                    0L
                    ? ringQuads
                    : 0L
            )
            *
            2L;
    }

    public static long CalculateRingTriangleCount(
        WorldSettings worldSettings,
        int level
    )
    {
        if (level <= 0)
        {
            return
                0L;
        }

        return
            CalculateRingTriangleCount(
                GetLODOuterResolution(
                    worldSettings,
                    level
                ),
                GetLODOuterResolution(
                    worldSettings,
                    level - 1
                )
            );
    }

    public static long CalculateStitchTriangleCount(
        int finerOuterResolution
    )
    {
        long finer =
            Mathf.Max(
                MinimumOuterResolution,
                finerOuterResolution
            );

        return
            6L *
            finer
            +
            8L;
    }

    public static long CalculateStitchTriangleCount(
        WorldSettings worldSettings,
        int coarseLevel
    )
    {
        if (coarseLevel <= 0)
        {
            return
                0L;
        }

        return
            CalculateStitchTriangleCount(
                GetLODOuterResolution(
                    worldSettings,
                    coarseLevel - 1
                )
            );
    }

    public static long CalculateTotalTriangleCount(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                0L;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                MinimumLevelCount,
                MaximumLevelCount
            );

        long total =
            CalculateCenterTriangleCount(
                worldSettings
            );

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            total +=
                CalculateRingTriangleCount(
                    worldSettings,
                    level
                );

            total +=
                CalculateStitchTriangleCount(
                    worldSettings,
                    level
                );
        }

        return
            total;
    }

    // =====================================================
    // VALIDATION
    // =====================================================

    public static bool TryValidateSettings(
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

            return
                false;
        }

        int centerResolution =
            worldSettings.clipmapCenterResolution;

        if (
            centerResolution <
                MinimumOuterResolution
            ||
            centerResolution %
                OuterResolutionAlignment
            !=
            0
        )
        {
            errorMessage =
                "Clipmap Center Resolution must be at least " +
                $"{MinimumOuterResolution} and evenly divisible " +
                $"by {OuterResolutionAlignment}.\n\n" +
                $"Current Resolution: {centerResolution}";

            return
                false;
        }

        float baseSpacing =
            worldSettings.ClipmapBaseSpacing;

        if (
            baseSpacing <= 0f
            ||
            !IsFinite(
                baseSpacing
            )
        )
        {
            errorMessage =
                "The derived clipmap base spacing is invalid.";

            return
                false;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                MinimumLevelCount,
                MaximumLevelCount
            );

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            int finerResolution =
                GetLODOuterResolution(
                    worldSettings,
                    level - 1
                );

            int resolution =
                GetLODOuterResolution(
                    worldSettings,
                    level
                );

            if (
                resolution <
                    MinimumOuterResolution
                ||
                resolution %
                    OuterResolutionAlignment
                !=
                0
            )
            {
                errorMessage =
                    $"LOD{level} outer resolution must be at least " +
                    $"{MinimumOuterResolution} and evenly divisible " +
                    $"by {OuterResolutionAlignment}.\n\n" +
                    $"Current Resolution: {resolution}";

                return
                    false;
            }

            int minimumResolution =
                GetMinimumOuterResolutionForFiner(
                    finerResolution
                );

            if (
                resolution <
                    minimumResolution
            )
            {
                float spacing =
                    GetLODSpacing(
                        worldSettings,
                        level
                    );

                errorMessage =
                    $"LOD{level} outer coverage is too small for " +
                    $"LOD{level - 1} and its 2:1 transition.\n\n" +
                    $"Current Outer Resolution: {resolution}\n" +
                    $"Minimum Outer Resolution: {minimumResolution}\n" +
                    $"Minimum Coverage: " +
                    $"{minimumResolution * spacing}";

                return
                    false;
            }

            float finerCoverage =
                GetLODOuterCoverage(
                    worldSettings,
                    level - 1
                );

            float coverage =
                GetLODOuterCoverage(
                    worldSettings,
                    level
                );

            if (
                coverage <=
                    finerCoverage
            )
            {
                errorMessage =
                    $"LOD{level} coverage must extend beyond " +
                    $"LOD{level - 1}.\n\n" +
                    $"LOD{level - 1}: {finerCoverage}\n" +
                    $"LOD{level}: {coverage}";

                return
                    false;
            }
        }

        return
            true;
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
