using System.Collections.Generic;
using UnityEngine;

/*
 * Converts changed source height tiles into the analysis tiles whose samples
 * can depend on those changes.
 *
 * The conversion is performed in absolute world space rather than by adding
 * a fixed number of neighbouring tile coordinates. This keeps invalidation
 * correct for arbitrary analysis radii and future windowed tile caches.
 */
public static class TerrainAnalysisInvalidationUtility
{
    public static void CollectAffectedTiles(
        TerrainAnalysisLayer layer,
        IReadOnlyList<Vector2Int> changedSourceTiles,
        float dependencyRadiusMeters,
        ICollection<Vector2Int> output
    )
    {
        if (
            layer == null
            ||
            changedSourceTiles == null
            ||
            output == null
            ||
            layer.CacheSize.x <= 0
            ||
            layer.CacheSize.y <= 0
            ||
            layer.SamplesPerSide <= 1
            ||
            layer.SampleSpacing <= 0f
        )
        {
            return;
        }

        float tileWorldSize =
            (
                layer.SamplesPerSide -
                1
            )
            *
            layer.SampleSpacing;

        if (tileWorldSize <= 0f)
        {
            return;
        }

        float dependencyRadius =
            Mathf.Max(
                0f,
                dependencyRadiusMeters
            );

        int cacheMinimumTileX =
            layer.CacheOriginTile.x;

        int cacheMinimumTileZ =
            layer.CacheOriginTile.y;

        int cacheMaximumTileX =
            cacheMinimumTileX +
            layer.CacheSize.x -
            1;

        int cacheMaximumTileZ =
            cacheMinimumTileZ +
            layer.CacheSize.y -
            1;

        float worldMaximumX =
            Mathf.Max(
                0f,
                layer.WorldSizeXZ.x
            );

        float worldMaximumZ =
            Mathf.Max(
                0f,
                layer.WorldSizeXZ.y
            );

        for (
            int sourceIndex = 0;
            sourceIndex < changedSourceTiles.Count;
            sourceIndex++
        )
        {
            Vector2Int sourceTile =
                changedSourceTiles[
                    sourceIndex
                ];

            float sourceMinimumX =
                sourceTile.x *
                tileWorldSize;

            float sourceMinimumZ =
                sourceTile.y *
                tileWorldSize;

            float sourceMaximumX =
                sourceMinimumX +
                tileWorldSize;

            float sourceMaximumZ =
                sourceMinimumZ +
                tileWorldSize;

            if (
                sourceMaximumX < 0f
                ||
                sourceMaximumZ < 0f
                ||
                sourceMinimumX > worldMaximumX
                ||
                sourceMinimumZ > worldMaximumZ
            )
            {
                continue;
            }

            float expandedMinimumX =
                Mathf.Clamp(
                    sourceMinimumX -
                    dependencyRadius,
                    0f,
                    worldMaximumX
                );

            float expandedMinimumZ =
                Mathf.Clamp(
                    sourceMinimumZ -
                    dependencyRadius,
                    0f,
                    worldMaximumZ
                );

            float expandedMaximumX =
                Mathf.Clamp(
                    sourceMaximumX +
                    dependencyRadius,
                    0f,
                    worldMaximumX
                );

            float expandedMaximumZ =
                Mathf.Clamp(
                    sourceMaximumZ +
                    dependencyRadius,
                    0f,
                    worldMaximumZ
                );

            int minimumTileX =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        expandedMinimumX /
                        tileWorldSize
                    ),
                    cacheMinimumTileX,
                    cacheMaximumTileX
                );

            int minimumTileZ =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        expandedMinimumZ /
                        tileWorldSize
                    ),
                    cacheMinimumTileZ,
                    cacheMaximumTileZ
                );

            int maximumTileX =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        expandedMaximumX /
                        tileWorldSize
                    ),
                    cacheMinimumTileX,
                    cacheMaximumTileX
                );

            int maximumTileZ =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        expandedMaximumZ /
                        tileWorldSize
                    ),
                    cacheMinimumTileZ,
                    cacheMaximumTileZ
                );

            if (
                minimumTileX >
                    maximumTileX
                ||
                minimumTileZ >
                    maximumTileZ
            )
            {
                continue;
            }

            for (
                int tileZ = minimumTileZ;
                tileZ <= maximumTileZ;
                tileZ++
            )
            {
                for (
                    int tileX = minimumTileX;
                    tileX <= maximumTileX;
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
    }
}
