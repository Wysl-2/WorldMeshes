using System.Collections.Generic;
using UnityEngine;

/*
 * Converts authoring-space dirty regions into height-tile coordinates.
 *
 * Future terrain modifier tools can use this utility to conservatively
 * invalidate only the preview slices touched by a modifier.
 */
public static class TerrainAuthoringPreviewDirtyRegionUtility
{
    /*
     * Collect every height tile touched by an XZ world-space rectangle.
     *
     * samplePadding expands the rectangle by native height samples.
     * A default of one sample helps cover normal sampling and
     * boundary-adjacent edits.
     */
    public static void CollectTilesOverlappingWorldRect(
        WorldSettings worldSettings,
        Rect worldXZRect,
        ICollection<Vector2Int> output,
        int samplePadding = 1
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

        int tileGridWidth =
            Mathf.Max(
                1,
                worldSettings.HeightTileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                worldSettings.HeightTileGridHeight
            );

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        float sampleSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        float padding =
            Mathf.Max(
                0,
                samplePadding
            )
            *
            sampleSpacing;

        float minimumX =
            worldXZRect.xMin -
            padding;

        float maximumX =
            worldXZRect.xMax +
            padding;

        float minimumZ =
            worldXZRect.yMin -
            padding;

        float maximumZ =
            worldXZRect.yMax +
            padding;

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            maximumX < 0f
            ||
            maximumZ < 0f
            ||
            minimumX >
                worldSize.x
            ||
            minimumZ >
                worldSize.y
        )
        {
            return;
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

        int minimumTileX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    minimumX /
                    tileWorldSize
                ),
                0,
                tileGridWidth -
                    1
            );

        int maximumTileX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    maximumX /
                    tileWorldSize
                ),
                0,
                tileGridWidth -
                    1
            );

        int minimumTileZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    minimumZ /
                    tileWorldSize
                ),
                0,
                tileGridHeight -
                    1
            );

        int maximumTileZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    maximumZ /
                    tileWorldSize
                ),
                0,
                tileGridHeight -
                    1
            );

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

    public static void CollectTilesOverlappingBounds(
        WorldSettings worldSettings,
        Bounds worldBounds,
        ICollection<Vector2Int> output,
        int samplePadding = 1
    )
    {
        Rect worldXZRect =
            Rect.MinMaxRect(
                worldBounds.min.x,
                worldBounds.min.z,
                worldBounds.max.x,
                worldBounds.max.z
            );

        CollectTilesOverlappingWorldRect(
            worldSettings,
            worldXZRect,
            output,
            samplePadding
        );
    }
}
