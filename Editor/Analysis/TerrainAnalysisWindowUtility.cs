using UnityEngine;

/*
 * Pure window/guard policy for bounded Terrain Analysis.
 *
 * A resident height cache may include source-only guard tiles. Public
 * interactive analysis is generated only for the safe interior, except where
 * the source touches a real logical-world edge (where world-edge clamping is
 * the intended boundary condition).
 */
public static class TerrainAnalysisWindowUtility
{
    public const float MaximumInteractiveDependencyRadiusMeters =
        256f;

    public static int CalculateRequiredGuardTileCount(
        int samplesPerSide,
        float sampleSpacing,
        float dependencyRadiusMeters
    )
    {
        int intervals =
            Mathf.Max(
                1,
                samplesPerSide - 1
            );

        float tileWorldSize =
            intervals *
            Mathf.Max(
                0.000001f,
                sampleSpacing
            );

        float safeRadius =
            Mathf.Max(
                0f,
                dependencyRadiusMeters
            );

        if (safeRadius <= 0f)
        {
            return 0;
        }

        return
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    safeRadius /
                    tileWorldSize
                )
            );
    }

    public static int CalculateRequiredInteractiveGuardTileCount(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return 0;
        }

        return
            CalculateRequiredGuardTileCount(
                worldSettings.HeightTileSamplesPerSide,
                TerrainAuthoringPreviewResidencyUtility
                    .CalculateHeightSampleSpacing(
                        worldSettings
                    ),
                MaximumInteractiveDependencyRadiusMeters
            );
    }

    public static int CalculateRequiredInteractiveGuardTileCount(
        int samplesPerSide,
        float sampleSpacing
    )
    {
        return
            CalculateRequiredGuardTileCount(
                samplesPerSide,
                sampleSpacing,
                MaximumInteractiveDependencyRadiusMeters
            );
    }

    public static bool TryCalculateInteractiveOutputWindow(
        TerrainAnalysisGpuSource source,
        out TerrainHeightCacheWindow outputWindow,
        out string errorMessage
    )
    {
        int guardTileCount =
            CalculateRequiredInteractiveGuardTileCount(
                source.SamplesPerSide,
                source.SampleSpacing
            );

        return
            TryCalculateSafeOutputWindow(
                source.SourceWindow,
                source.WorldTileGridSize,
                guardTileCount,
                out outputWindow,
                out errorMessage
            );
    }

    public static bool TryCalculateSafeOutputWindow(
        TerrainHeightCacheWindow sourceWindow,
        Vector2Int worldGridSize,
        int guardTileCount,
        out TerrainHeightCacheWindow outputWindow,
        out string errorMessage
    )
    {
        outputWindow = default;
        errorMessage = "";

        if (
            !sourceWindow.IsValid
            ||
            worldGridSize.x <= 0
            ||
            worldGridSize.y <= 0
            ||
            sourceWindow.OriginTile.x < 0
            ||
            sourceWindow.OriginTile.y < 0
            ||
            sourceWindow.MaximumExclusive.x > worldGridSize.x
            ||
            sourceWindow.MaximumExclusive.y > worldGridSize.y
        )
        {
            errorMessage =
                "The Terrain Analysis source window is invalid for the logical height-tile grid.";

            return false;
        }

        int guard =
            Mathf.Max(
                0,
                guardTileCount
            );

        int leftInset =
            sourceWindow.OriginTile.x > 0
                ? guard
                : 0;

        int bottomInset =
            sourceWindow.OriginTile.y > 0
                ? guard
                : 0;

        int rightInset =
            sourceWindow.MaximumExclusive.x < worldGridSize.x
                ? guard
                : 0;

        int topInset =
            sourceWindow.MaximumExclusive.y < worldGridSize.y
                ? guard
                : 0;

        Vector2Int outputOrigin =
            sourceWindow.OriginTile +
            new Vector2Int(
                leftInset,
                bottomInset
            );

        Vector2Int outputSize =
            sourceWindow.Size -
            new Vector2Int(
                leftInset + rightInset,
                bottomInset + topInset
            );

        if (
            outputSize.x <= 0
            ||
            outputSize.y <= 0
        )
        {
            errorMessage =
                "The resident height source is too small to expose a numerically safe Terrain Analysis output window.";

            return false;
        }

        outputWindow =
            new TerrainHeightCacheWindow(
                outputOrigin,
                outputSize
            );

        return true;
    }

    public static bool TryExpandOutputWindow(
        TerrainHeightCacheWindow outputWindow,
        Vector2Int worldGridSize,
        int guardTileCount,
        out TerrainHeightCacheWindow sourceWindow,
        out string errorMessage
    )
    {
        sourceWindow = default;
        errorMessage = "";

        if (
            !outputWindow.IsValid
            ||
            worldGridSize.x <= 0
            ||
            worldGridSize.y <= 0
            ||
            outputWindow.OriginTile.x < 0
            ||
            outputWindow.OriginTile.y < 0
            ||
            outputWindow.MaximumExclusive.x > worldGridSize.x
            ||
            outputWindow.MaximumExclusive.y > worldGridSize.y
        )
        {
            errorMessage =
                "The requested Terrain Analysis output window is outside the logical height-tile grid.";

            return false;
        }

        int guard =
            Mathf.Max(
                0,
                guardTileCount
            );

        Vector2Int minimum =
            new Vector2Int(
                Mathf.Max(
                    0,
                    outputWindow.OriginTile.x - guard
                ),
                Mathf.Max(
                    0,
                    outputWindow.OriginTile.y - guard
                )
            );

        Vector2Int maximumExclusive =
            new Vector2Int(
                Mathf.Min(
                    worldGridSize.x,
                    outputWindow.MaximumExclusive.x + guard
                ),
                Mathf.Min(
                    worldGridSize.y,
                    outputWindow.MaximumExclusive.y + guard
                )
            );

        sourceWindow =
            new TerrainHeightCacheWindow(
                minimum,
                maximumExclusive - minimum
            );

        return sourceWindow.IsValid;
    }
}
