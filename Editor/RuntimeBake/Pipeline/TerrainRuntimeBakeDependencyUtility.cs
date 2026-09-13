using System.Collections.Generic;
using UnityEngine;

/*
 * Pure coordinate/dependency conversion helpers shared by invalidation and
 * planning. These methods do not mutate persistent state or generated data.
 */
public static class TerrainRuntimeBakeDependencyUtility
{
    public static bool IsHeightTileCoordinateValid(
        WorldSettings worldSettings,
        Vector2Int coordinate
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        return
            coordinate.x >= 0
            &&
            coordinate.y >= 0
            &&
            coordinate.x <
                Mathf.Max(
                    1,
                    worldSettings.HeightTileGridWidth
                )
            &&
            coordinate.y <
                Mathf.Max(
                    1,
                    worldSettings.HeightTileGridHeight
                );
    }

    public static bool IsCollisionChunkCoordinateValid(
        WorldSettings worldSettings,
        Vector2Int coordinate
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        return
            coordinate.x >= 0
            &&
            coordinate.y >= 0
            &&
            coordinate.x <
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
            &&
            coordinate.y <
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                );
    }

    public static bool TryCopyValidHeightTiles(
        WorldSettings worldSettings,
        IEnumerable<Vector2Int> source,
        ISet<Vector2Int> output,
        out string errorMessage
    )
    {
        return
            TryCopyCoordinates(
                source,
                output,
                coordinate =>
                    IsHeightTileCoordinateValid(
                        worldSettings,
                        coordinate
                    ),
                "height tile",
                out errorMessage
            );
    }

    public static bool TryCopyValidSurfaceTiles(
        WorldSettings worldSettings,
        IEnumerable<Vector2Int> source,
        ISet<Vector2Int> output,
        out string errorMessage
    )
    {
        // Runtime surface masks currently share the runtime height-tile grid.
        return
            TryCopyCoordinates(
                source,
                output,
                coordinate =>
                    IsHeightTileCoordinateValid(
                        worldSettings,
                        coordinate
                    ),
                "surface tile",
                out errorMessage
            );
    }

    public static bool TryCopyValidCollisionChunks(
        WorldSettings worldSettings,
        IEnumerable<Vector2Int> source,
        ISet<Vector2Int> output,
        out string errorMessage
    )
    {
        return
            TryCopyCoordinates(
                source,
                output,
                coordinate =>
                    IsCollisionChunkCoordinateValid(
                        worldSettings,
                        coordinate
                    ),
                "collision chunk",
                out errorMessage
            );
    }

    public static float GetSurfaceDependencyRadiusMeters(
        WorldSettings worldSettings,
        TerrainSurfaceSettings surfaceSettings
    )
    {
        if (
            worldSettings == null
            ||
            surfaceSettings == null
        )
        {
            return 0f;
        }

        float sampleSpacing =
            Mathf.Max(
                0.000001f,
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

        ScreeSettings scree =
            surfaceSettings.Scree;

        float curvatureRadius =
            scree != null
                ? Mathf.Max(
                    0f,
                    scree.curvatureScale
                )
                : 0f;

        return
            Mathf.Max(
                sampleSpacing,
                curvatureRadius
            );
    }

    public static bool TryCollectDependentSurfaceTiles(
        WorldSettings worldSettings,
        TerrainSurfaceSettings surfaceSettings,
        IEnumerable<Vector2Int> dirtyHeightTiles,
        ISet<Vector2Int> output,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            worldSettings == null
            ||
            surfaceSettings == null
            ||
            output == null
        )
        {
            errorMessage =
                "Surface dependency mapping received an invalid context.";

            return false;
        }

        HashSet<Vector2Int> sourceTiles =
            new HashSet<Vector2Int>();

        if (
            !TryCopyValidHeightTiles(
                worldSettings,
                dirtyHeightTiles,
                sourceTiles,
                out errorMessage
            )
        )
        {
            return false;
        }

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        float dependencyRadius =
            GetSurfaceDependencyRadiusMeters(
                worldSettings,
                surfaceSettings
            );

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        foreach (
            Vector2Int tile
            in sourceTiles
        )
        {
            float minimumX =
                tile.x *
                tileWorldSize;

            float minimumZ =
                tile.y *
                tileWorldSize;

            float maximumX =
                Mathf.Min(
                    minimumX +
                    tileWorldSize,
                    worldSize.x
                );

            float maximumZ =
                Mathf.Min(
                    minimumZ +
                    tileWorldSize,
                    worldSize.y
                );

            Rect expandedWorldRect =
                Rect.MinMaxRect(
                    minimumX -
                        dependencyRadius,
                    minimumZ -
                        dependencyRadius,
                    maximumX +
                        dependencyRadius,
                    maximumZ +
                        dependencyRadius
                );

            /*
             * The rectangle is already expanded in world metres by the exact
             * analysis radius. Do not add the preview helper's default sample
             * padding a second time.
             */
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingWorldRect(
                    worldSettings,
                    expandedWorldRect,
                    output,
                    0
                );
        }

        return true;
    }

    public static bool TryCollectDependentCollisionChunks(
        WorldSettings worldSettings,
        IEnumerable<Vector2Int> dirtyHeightTiles,
        ISet<Vector2Int> output,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            worldSettings == null
            ||
            output == null
        )
        {
            errorMessage =
                "Collision dependency mapping received an invalid context.";

            return false;
        }

        HashSet<Vector2Int> sourceTiles =
            new HashSet<Vector2Int>();

        if (
            !TryCopyValidHeightTiles(
                worldSettings,
                dirtyHeightTiles,
                sourceTiles,
                out errorMessage
            )
        )
        {
            return false;
        }

        int span =
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            );

        int gridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        int gridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        foreach (
            Vector2Int tile
            in sourceTiles
        )
        {
            int startChunkX =
                tile.x *
                span;

            int startChunkZ =
                tile.y *
                span;

            int endChunkX =
                Mathf.Min(
                    startChunkX +
                    span,
                    gridWidth
                );

            int endChunkZ =
                Mathf.Min(
                    startChunkZ +
                    span,
                    gridHeight
                );

            for (
                int chunkZ = startChunkZ;
                chunkZ < endChunkZ;
                chunkZ++
            )
            {
                for (
                    int chunkX = startChunkX;
                    chunkX < endChunkX;
                    chunkX++
                )
                {
                    output.Add(
                        new Vector2Int(
                            chunkX,
                            chunkZ
                        )
                    );
                }
            }
        }

        return true;
    }

    public static void CollectAllHeightTiles(
        WorldSettings worldSettings,
        ISet<Vector2Int> output
    )
    {
        if (
            worldSettings == null
            ||
            output == null
        )
        {
            return;
        }

        int width =
            Mathf.Max(
                1,
                worldSettings.HeightTileGridWidth
            );

        int height =
            Mathf.Max(
                1,
                worldSettings.HeightTileGridHeight
            );

        for (
            int tileZ = 0;
            tileZ < height;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < width;
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
    }

    public static void CollectAllCollisionChunks(
        WorldSettings worldSettings,
        ISet<Vector2Int> output
    )
    {
        if (
            worldSettings == null
            ||
            output == null
        )
        {
            return;
        }

        int width =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        int height =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        for (
            int chunkZ = 0;
            chunkZ < height;
            chunkZ++
        )
        {
            for (
                int chunkX = 0;
                chunkX < width;
                chunkX++
            )
            {
                output.Add(
                    new Vector2Int(
                        chunkX,
                        chunkZ
                    )
                );
            }
        }
    }

    private static bool TryCopyCoordinates(
        IEnumerable<Vector2Int> source,
        ISet<Vector2Int> output,
        System.Func<Vector2Int, bool> validator,
        string coordinateLabel,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            source == null
            ||
            output == null
            ||
            validator == null
        )
        {
            errorMessage =
                "Coordinate validation received an invalid context.";

            return false;
        }

        HashSet<Vector2Int> validated =
            new HashSet<Vector2Int>();

        foreach (
            Vector2Int coordinate
            in source
        )
        {
            if (!validator(coordinate))
            {
                errorMessage =
                    $"Invalid {coordinateLabel} coordinate: " +
                    $"({coordinate.x}, {coordinate.y}).";

                return false;
            }

            validated.Add(
                coordinate
            );
        }

        foreach (
            Vector2Int coordinate
            in validated
        )
        {
            output.Add(
                coordinate
            );
        }

        return true;
    }
}
