using System.Collections.Generic;
using UnityEngine;

public enum TerrainRegionalElevationInvalidationKind
{
    None = 0,
    WorldBounds = 1,
    WholeWorld = 2
}

internal readonly struct TerrainRegionalElevationInvalidationScope
{
    public TerrainRegionalElevationInvalidationKind Kind { get; }
    public Bounds WorldBounds { get; }

    private TerrainRegionalElevationInvalidationScope(
        TerrainRegionalElevationInvalidationKind kind,
        Bounds worldBounds
    )
    {
        Kind =
            kind;

        WorldBounds =
            worldBounds;
    }

    public static TerrainRegionalElevationInvalidationScope None =>
        new TerrainRegionalElevationInvalidationScope(
            TerrainRegionalElevationInvalidationKind.None,
            default
        );

    public static TerrainRegionalElevationInvalidationScope WholeWorld =>
        new TerrainRegionalElevationInvalidationScope(
            TerrainRegionalElevationInvalidationKind.WholeWorld,
            default
        );

    public static TerrainRegionalElevationInvalidationScope FromWorldBounds(
        Bounds worldBounds
    )
    {
        return
            new TerrainRegionalElevationInvalidationScope(
                TerrainRegionalElevationInvalidationKind.WorldBounds,
                worldBounds
            );
    }
}

/*
 * Package 06 pure regional-elevation residency policy.
 *
 * Regional authoring may have whole-world logical influence without
 * materializing the complete logical height-tile grid for preview work.
 */
internal static class TerrainRegionalElevationResidencyPolicy
{
    internal static TerrainRegionalElevationInvalidationScope Merge(
        TerrainRegionalElevationInvalidationScope a,
        TerrainRegionalElevationInvalidationScope b
    )
    {
        if (
            a.Kind ==
                TerrainRegionalElevationInvalidationKind.WholeWorld
            ||
            b.Kind ==
                TerrainRegionalElevationInvalidationKind.WholeWorld
        )
        {
            return
                TerrainRegionalElevationInvalidationScope.WholeWorld;
        }

        if (
            a.Kind ==
                TerrainRegionalElevationInvalidationKind.None
        )
        {
            return
                b;
        }

        if (
            b.Kind ==
                TerrainRegionalElevationInvalidationKind.None
        )
        {
            return
                a;
        }

        Bounds merged =
            a.WorldBounds;

        merged.Encapsulate(
            b.WorldBounds.min
        );

        merged.Encapsulate(
            b.WorldBounds.max
        );

        return
            TerrainRegionalElevationInvalidationScope
                .FromWorldBounds(
                    merged
                );
    }

    internal static long GetLogicalAffectedTileCount(
        WorldSettings worldSettings,
        TerrainRegionalElevationInvalidationScope scope
    )
    {
        if (
            worldSettings == null
            ||
            scope.Kind ==
                TerrainRegionalElevationInvalidationKind.None
        )
        {
            return
                0L;
        }

        int gridWidth =
            Mathf.Max(
                0,
                worldSettings.HeightTileGridWidth
            );

        int gridHeight =
            Mathf.Max(
                0,
                worldSettings.HeightTileGridHeight
            );

        if (
            gridWidth <= 0
            ||
            gridHeight <= 0
        )
        {
            return
                0L;
        }

        if (
            scope.Kind ==
                TerrainRegionalElevationInvalidationKind.WholeWorld
        )
        {
            return
                (long)gridWidth *
                gridHeight;
        }

        if (
            !TryCalculateAffectedWindow(
                worldSettings,
                scope,
                out TerrainHeightCacheWindow affectedWindow
            )
        )
        {
            return
                0L;
        }

        return
            (long)affectedWindow.Width *
            affectedWindow.Height;
    }

    internal static bool TryCollectResidentTiles(
        WorldSettings worldSettings,
        TerrainRegionalElevationInvalidationScope scope,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        List<Vector2Int> output,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (output == null)
        {
            errorMessage =
                "Regional elevation resident-tile output is null.";

            return false;
        }

        output.Clear();

        if (
            scope.Kind ==
                TerrainRegionalElevationInvalidationKind.None
            ||
            !hasActiveWindow
            ||
            !activeWindow.IsValid
        )
        {
            return true;
        }

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is unavailable for regional elevation residency.";

            return false;
        }

        TerrainHeightCacheWindow affectedWindow;

        if (
            scope.Kind ==
                TerrainRegionalElevationInvalidationKind.WholeWorld
        )
        {
            Vector2Int worldGridSize =
                new Vector2Int(
                    Mathf.Max(
                        0,
                        worldSettings.HeightTileGridWidth
                    ),
                    Mathf.Max(
                        0,
                        worldSettings.HeightTileGridHeight
                    )
                );

            if (
                !TerrainHeightCacheWindow.TryFitToWorld(
                    activeWindow.OriginTile,
                    activeWindow.Size,
                    worldGridSize,
                    out affectedWindow
                )
            )
            {
                return true;
            }
        }
        else if (
            !TryCalculateAffectedWindow(
                worldSettings,
                scope,
                out affectedWindow
            )
        )
        {
            return true;
        }

        if (
            !activeWindow.TryGetIntersection(
                affectedWindow,
                out TerrainHeightCacheWindow intersection
            )
        )
        {
            return true;
        }

        Vector2Int maximumExclusive =
            intersection.MaximumExclusive;

        for (
            int tileZ = intersection.OriginTile.y;
            tileZ < maximumExclusive.y;
            tileZ++
        )
        {
            for (
                int tileX = intersection.OriginTile.x;
                tileX < maximumExclusive.x;
                tileX++
            )
            {
                output.Add(
                    new Vector2Int(
                        tileX,
                        tileZ
                    )
                );
            }
        }

        return true;
    }

    internal static bool ScopeAffectsTile(
        WorldSettings worldSettings,
        TerrainRegionalElevationInvalidationScope scope,
        Vector2Int worldTile
    )
    {
        if (
            worldSettings == null
            ||
            scope.Kind ==
                TerrainRegionalElevationInvalidationKind.None
        )
        {
            return false;
        }

        if (
            worldTile.x < 0
            ||
            worldTile.y < 0
            ||
            worldTile.x >=
                worldSettings.HeightTileGridWidth
            ||
            worldTile.y >=
                worldSettings.HeightTileGridHeight
        )
        {
            return false;
        }

        if (
            scope.Kind ==
                TerrainRegionalElevationInvalidationKind.WholeWorld
        )
        {
            return true;
        }

        return
            TryCalculateAffectedWindow(
                worldSettings,
                scope,
                out TerrainHeightCacheWindow affectedWindow
            )
            &&
            affectedWindow.Contains(
                worldTile
            );
    }

    internal static string GetDisplayName(
        TerrainRegionalElevationInvalidationKind kind
    )
    {
        switch (kind)
        {
            case TerrainRegionalElevationInvalidationKind.WorldBounds:
                return "World Bounds";

            case TerrainRegionalElevationInvalidationKind.WholeWorld:
                return "Whole World";

            default:
                return "None";
        }
    }

    internal static int ClampLogicalCountToInt(
        long count
    )
    {
        if (count <= 0L)
        {
            return 0;
        }

        return
            count >= int.MaxValue
                ? int.MaxValue
                : (int)count;
    }

    private static bool TryCalculateAffectedWindow(
        WorldSettings worldSettings,
        TerrainRegionalElevationInvalidationScope scope,
        out TerrainHeightCacheWindow affectedWindow
    )
    {
        affectedWindow =
            default;

        if (
            worldSettings == null
            ||
            scope.Kind !=
                TerrainRegionalElevationInvalidationKind.WorldBounds
        )
        {
            return false;
        }

        int gridWidth =
            Mathf.Max(
                0,
                worldSettings.HeightTileGridWidth
            );

        int gridHeight =
            Mathf.Max(
                0,
                worldSettings.HeightTileGridHeight
            );

        if (
            gridWidth <= 0
            ||
            gridHeight <= 0
        )
        {
            return false;
        }

        Bounds bounds =
            scope.WorldBounds;

        if (
            !IsFinite(
                bounds.min.x
            )
            ||
            !IsFinite(
                bounds.min.z
            )
            ||
            !IsFinite(
                bounds.max.x
            )
            ||
            !IsFinite(
                bounds.max.z
            )
        )
        {
            return false;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        float minimumX =
            bounds.min.x;

        float maximumX =
            bounds.max.x;

        float minimumZ =
            bounds.min.z;

        float maximumZ =
            bounds.max.z;

        if (
            maximumX < 0f
            ||
            maximumZ < 0f
            ||
            minimumX > worldSize.x
            ||
            minimumZ > worldSize.y
        )
        {
            return false;
        }

        minimumX =
            Mathf.Clamp(
                minimumX,
                0f,
                worldSize.x
            );

        maximumX =
            Mathf.Clamp(
                maximumX,
                0f,
                worldSize.x
            );

        minimumZ =
            Mathf.Clamp(
                minimumZ,
                0f,
                worldSize.y
            );

        maximumZ =
            Mathf.Clamp(
                maximumZ,
                0f,
                worldSize.y
            );

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        int minimumTileX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    minimumX /
                    tileWorldSize
                ),
                0,
                gridWidth - 1
            );

        int maximumTileX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    maximumX /
                    tileWorldSize
                ),
                0,
                gridWidth - 1
            );

        int minimumTileZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    minimumZ /
                    tileWorldSize
                ),
                0,
                gridHeight - 1
            );

        int maximumTileZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    maximumZ /
                    tileWorldSize
                ),
                0,
                gridHeight - 1
            );

        affectedWindow =
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

        return
            affectedWindow.IsValid;
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
