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

    public const int InteractiveOutputRadiusTiles = 1;

    public static bool TryCalculateNativeInteractiveWindows(WorldSettings settings, Vector3 focus,
        out TerrainHeightCacheWindow output, out TerrainHeightCacheWindow source,
        out int guardTileCount, out string error)
    {
        output = source = default;
        guardTileCount = 0;
        error = "";
        if (settings == null)
        {
            error = "Native terrain analysis requires WorldSettings.";
            return false;
        }
        return TryCalculateNativeInteractiveWindows(
            new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight),
            TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1),
            TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1), focus,
            out output, out source, out guardTileCount, out error);
    }

    internal static bool TryCalculateNativeInteractiveWindows(Vector2Int grid, int samples,
        float spacing, Vector3 focus, out TerrainHeightCacheWindow output,
        out TerrainHeightCacheWindow source, out int guardTileCount, out string error)
    {
        output = source = default;
        guardTileCount = 0;
        error = "";
        float tileSize = (samples - 1) * spacing;
        if (grid.x <= 0 || grid.y <= 0 || samples <= 1 || spacing <= 0f
            || float.IsNaN(tileSize) || float.IsInfinity(tileSize) || tileSize <= 0f
            || float.IsNaN(focus.x) || float.IsInfinity(focus.x)
            || float.IsNaN(focus.z) || float.IsInfinity(focus.z))
        {
            error = "The native analysis focus, tile grid, or sampling is invalid.";
            return false;
        }
        int x = Mathf.FloorToInt(Mathf.Clamp(focus.x / tileSize, 0f, grid.x - 1f));
        int z = Mathf.FloorToInt(Mathf.Clamp(focus.z / tileSize, 0f, grid.y - 1f));
        var minimum = new Vector2Int(Mathf.Max(0, x - InteractiveOutputRadiusTiles),
            Mathf.Max(0, z - InteractiveOutputRadiusTiles));
        var maximum = new Vector2Int((int)System.Math.Min(grid.x, (long)x + InteractiveOutputRadiusTiles + 1),
            (int)System.Math.Min(grid.y, (long)z + InteractiveOutputRadiusTiles + 1));
        output = new TerrainHeightCacheWindow(minimum, maximum - minimum);
        guardTileCount = CalculateRequiredInteractiveGuardTileCount(samples, spacing);
        // Clamp arithmetic in long space: large logical grids cannot overflow
        // the exclusive maximum while preserving the full dependency radius.
        long minX = System.Math.Max(0L, (long)minimum.x - guardTileCount);
        long minZ = System.Math.Max(0L, (long)minimum.y - guardTileCount);
        long maxX = System.Math.Min(grid.x, (long)maximum.x + guardTileCount);
        long maxZ = System.Math.Min(grid.y, (long)maximum.y + guardTileCount);
        if ((maxX - minX) * (maxZ - minZ) > int.MaxValue)
        {
            error = "The native analysis dependency window exceeds supported tile counts.";
            output = default;
            return false;
        }
        return TryExpandOutputWindow(output, grid, guardTileCount, out source, out error);
    }

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
                    (int)System.Math.Min(int.MaxValue, (long)outputWindow.MaximumExclusive.x + guard)
                ),
                Mathf.Min(
                    worldGridSize.y,
                    (int)System.Math.Min(int.MaxValue, (long)outputWindow.MaximumExclusive.y + guard)
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
