using UnityEngine;

/*
 * Pure edit-mode height-cache residency mathematics.
 *
 * This utility converts world-space clipmap coverage into authoritative
 * world height-tile windows. It deliberately owns no cache resources,
 * Scene View state, hierarchy mutation, AssetDatabase loading, or async work.
 */
public static class TerrainAuthoringPreviewResidencyUtility
{
    public const int DefaultSamplePadding =
        1;

    public const int DefaultGuardTileCount =
        1;

    // =====================================================
    // REQUIRED SAMPLE-SAFE WINDOW
    // =====================================================

    public static bool TryCalculateRequiredWindow(
        WorldSettings worldSettings,
        Vector2 minimumXZ,
        Vector2 maximumXZ,
        int samplePadding,
        out TerrainHeightCacheWindow requiredWindow,
        out string errorMessage
    )
    {
        requiredWindow =
            default;

        errorMessage =
            "";

        if (!TryValidateSettings(
            worldSettings,
            out errorMessage
        ))
        {
            return false;
        }

        if (
            !IsFinite(
                minimumXZ.x
            )
            ||
            !IsFinite(
                minimumXZ.y
            )
            ||
            !IsFinite(
                maximumXZ.x
            )
            ||
            !IsFinite(
                maximumXZ.y
            )
            ||
            minimumXZ.x >
                maximumXZ.x
            ||
            minimumXZ.y >
                maximumXZ.y
        )
        {
            errorMessage =
                "The requested clipmap world bounds are invalid.";

            return false;
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            worldSizeXZ.x <= 0f
            ||
            worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "The logical terrain world size is invalid.";

            return false;
        }

        float sampleSpacing =
            CalculateHeightSampleSpacing(
                worldSettings
            );

        float sampleMargin =
            Mathf.Max(
                0,
                samplePadding
            )
            *
            sampleSpacing;

        float visibleMinimumX =
            Mathf.Max(
                0f,
                minimumXZ.x -
                    sampleMargin
            );

        float visibleMaximumX =
            Mathf.Min(
                worldSizeXZ.x,
                maximumXZ.x +
                    sampleMargin
            );

        float visibleMinimumZ =
            Mathf.Max(
                0f,
                minimumXZ.y -
                    sampleMargin
            );

        float visibleMaximumZ =
            Mathf.Min(
                worldSizeXZ.y,
                maximumXZ.y +
                    sampleMargin
            );

        if (
            visibleMinimumX >
                visibleMaximumX
            ||
            visibleMinimumZ >
                visibleMaximumZ
        )
        {
            errorMessage =
                "The requested clipmap bounds do not intersect the " +
                "logical terrain world.";

            return false;
        }

        int samplesPerSide =
            Mathf.Max(
                2,
                worldSettings
                    .HeightTileSamplesPerSide
            );

        int minimumTileX =
            WorldPositionToTileCoordinate(
                visibleMinimumX,
                worldSizeXZ.x,
                sampleSpacing,
                samplesPerSide,
                worldSettings
                    .HeightTileGridWidth
            );

        int maximumTileX =
            WorldPositionToTileCoordinate(
                visibleMaximumX,
                worldSizeXZ.x,
                sampleSpacing,
                samplesPerSide,
                worldSettings
                    .HeightTileGridWidth
            );

        int minimumTileZ =
            WorldPositionToTileCoordinate(
                visibleMinimumZ,
                worldSizeXZ.y,
                sampleSpacing,
                samplesPerSide,
                worldSettings
                    .HeightTileGridHeight
            );

        int maximumTileZ =
            WorldPositionToTileCoordinate(
                visibleMaximumZ,
                worldSizeXZ.y,
                sampleSpacing,
                samplesPerSide,
                worldSettings
                    .HeightTileGridHeight
            );

        requiredWindow =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    minimumTileX,
                    minimumTileZ
                ),
                new Vector2Int(
                    maximumTileX -
                        minimumTileX +
                        1,
                    maximumTileZ -
                        minimumTileZ +
                        1
                )
            );

        if (!requiredWindow.IsValid)
        {
            errorMessage =
                "The sample-safe required height-cache window is invalid.";

            requiredWindow =
                default;

            return false;
        }

        return true;
    }

    // =====================================================
    // GUARDED RESIDENT WINDOW
    // =====================================================

    public static bool TryCalculateResidentWindow(
        WorldSettings worldSettings,
        Vector2 minimumXZ,
        Vector2 maximumXZ,
        int samplePadding,
        int guardTileCount,
        out TerrainHeightCacheWindow residentWindow,
        out string errorMessage
    )
    {
        return
            TryCalculateResidentWindow(
                worldSettings,
                minimumXZ,
                maximumXZ,
                samplePadding,
                guardTileCount,
                Vector2Int.zero,
                out residentWindow,
                out errorMessage
            );
    }

    public static bool TryCalculateResidentWindow(
        WorldSettings worldSettings,
        Vector2 minimumXZ,
        Vector2 maximumXZ,
        int samplePadding,
        int guardTileCount,
        Vector2Int minimumResidentSize,
        out TerrainHeightCacheWindow residentWindow,
        out string errorMessage
    )
    {
        residentWindow =
            default;

        errorMessage =
            "";

        if (
            !TryCalculateRequiredWindow(
                worldSettings,
                minimumXZ,
                maximumXZ,
                samplePadding,
                out TerrainHeightCacheWindow requiredWindow,
                out errorMessage
            )
        )
        {
            return false;
        }

        int safeGuard =
            Mathf.Max(
                0,
                guardTileCount
            );

        Vector2Int guardedOrigin =
            requiredWindow.OriginTile -
            new Vector2Int(
                safeGuard,
                safeGuard
            );

        Vector2Int guardedSize =
            requiredWindow.Size +
            new Vector2Int(
                safeGuard * 2,
                safeGuard * 2
            );

        Vector2Int preferredSize =
            new Vector2Int(
                Mathf.Max(
                    guardedSize.x,
                    Mathf.Max(
                        0,
                        minimumResidentSize.x
                    )
                ),
                Mathf.Max(
                    guardedSize.y,
                    Mathf.Max(
                        0,
                        minimumResidentSize.y
                    )
                )
            );

        Vector2Int extraSize =
            preferredSize -
            guardedSize;

        Vector2Int preferredOrigin =
            guardedOrigin -
            new Vector2Int(
                extraSize.x / 2,
                extraSize.y / 2
            );

        Vector2Int worldGridSize =
            new Vector2Int(
                worldSettings
                    .HeightTileGridWidth,
                worldSettings
                    .HeightTileGridHeight
            );

        if (
            !TerrainHeightCacheWindow
                .TryFitToWorld(
                    preferredOrigin,
                    preferredSize,
                    worldGridSize,
                    out residentWindow
                )
        )
        {
            errorMessage =
                "The resident height-cache window could not be fit " +
                "inside the logical height-tile grid.";

            return false;
        }

        if (
            !residentWindow.IsValid
            ||
            !residentWindow.Contains(
                requiredWindow
            )
        )
        {
            errorMessage =
                "The fitted resident height-cache window does not " +
                "contain the complete sample-safe required window.";

            residentWindow =
                default;

            return false;
        }

        return true;
    }

    // =====================================================
    // CANONICAL CENTERED CLIPMAP
    // =====================================================

    public static bool TryCalculateCanonicalClipmapBounds(
        WorldSettings worldSettings,
        out Vector2 minimumXZ,
        out Vector2 maximumXZ,
        out string errorMessage
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        errorMessage =
            "";

        if (!TryValidateSettings(
            worldSettings,
            out errorMessage
        ))
        {
            return false;
        }

        Vector3 center =
            TerrainClipmapLayoutUtility
                .CalculateWorldCenterPosition(
                    worldSettings,
                    0f
                );

        float diameter =
            TerrainClipmapLayoutUtility
                .CalculateClipmapDiameter(
                    worldSettings
                );

        if (
            !IsFinite(
                diameter
            )
            ||
            diameter <= 0f
        )
        {
            errorMessage =
                "The canonical clipmap diameter is invalid.";

            return false;
        }

        float halfDiameter =
            diameter *
            0.5f;

        minimumXZ =
            new Vector2(
                center.x -
                    halfDiameter,
                center.z -
                    halfDiameter
            );

        maximumXZ =
            new Vector2(
                center.x +
                    halfDiameter,
                center.z +
                    halfDiameter
            );

        return true;
    }

    public static bool TryCalculateCanonicalResidentWindow(
        WorldSettings worldSettings,
        out TerrainHeightCacheWindow residentWindow,
        out string errorMessage
    )
    {
        residentWindow =
            default;

        errorMessage =
            "";

        if (
            !TryCalculateCanonicalClipmapBounds(
                worldSettings,
                out Vector2 minimumXZ,
                out Vector2 maximumXZ,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            TryCalculateResidentWindow(
                worldSettings,
                minimumXZ,
                maximumXZ,
                DefaultSamplePadding,
                DefaultGuardTileCount,
                out residentWindow,
                out errorMessage
            );
    }

    // =====================================================
    // SHADER-COMPATIBLE SAMPLE ADDRESSING
    // =====================================================

    internal static int WorldPositionToTileCoordinate(
        float coordinate,
        float worldSize,
        float sampleSpacing,
        int samplesPerSide,
        int tileCount
    )
    {
        float safeSampleSpacing =
            Mathf.Max(
                0.000001f,
                sampleSpacing
            );

        int safeSamplesPerSide =
            Mathf.Max(
                2,
                samplesPerSide
            );

        int tileIntervals =
            safeSamplesPerSide -
            1;

        float clampedCoordinate =
            Mathf.Clamp(
                coordinate,
                0f,
                Mathf.Max(
                    0f,
                    worldSize
                )
            );

        int globalSample =
            Mathf.FloorToInt(
                clampedCoordinate /
                safeSampleSpacing
                +
                0.5f
            );

        int tileCoordinate =
            globalSample /
            tileIntervals;

        return
            Mathf.Clamp(
                tileCoordinate,
                0,
                Mathf.Max(
                    0,
                    tileCount -
                    1
                )
            );
    }

    internal static float CalculateHeightSampleSpacing(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return 0f;
        }

        return
            Mathf.Max(
                0.000001f,
                worldSettings
                    .HeightTileWorldSize /
                Mathf.Max(
                    1,
                    worldSettings
                        .HeightTileSamplesPerSide -
                    1
                )
            );
    }

    // =====================================================
    // VALIDATION
    // =====================================================

    private static bool TryValidateSettings(
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

        if (
            worldSettings.HeightTileGridWidth <= 0
            ||
            worldSettings.HeightTileGridHeight <= 0
            ||
            worldSettings.HeightTileSamplesPerSide <= 1
            ||
            CalculateHeightSampleSpacing(
                worldSettings
            ) <= 0f
        )
        {
            errorMessage =
                "WorldSettings contains an invalid heightfield layout.";

            return false;
        }

        return true;
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
