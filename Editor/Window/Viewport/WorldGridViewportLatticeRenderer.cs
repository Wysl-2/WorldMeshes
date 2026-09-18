using UnityEditor;
using UnityEngine;

internal static class WorldGridViewportLatticeRenderer
{
    internal static void Draw(
        WorldGridViewportLattice lattice,
        WorldGridViewportContext context
    )
    {
        if (
            lattice ==
                WorldGridViewportLattice.None
            ||
            context.WorldSettings == null
            ||
            context.Transform == null
            ||
            !context.Transform.IsInitialized
            ||
            context.ViewportRect.width <= 0f
            ||
            context.ViewportRect.height <= 0f
        )
        {
            return;
        }

        switch (lattice)
        {
            case WorldGridViewportLattice.HeightTiles:
                DrawHeightTileLattice(
                    context
                );
                break;

            case WorldGridViewportLattice.WorldChunks:
            default:
                DrawWorldChunkLattice(
                    context
                );
                break;
        }
    }

    private static void DrawWorldChunkLattice(
        WorldGridViewportContext context
    )
    {
        WorldSettings settings =
            context.WorldSettings;

        DrawRectangularLattice(
            context,
            Mathf.Max(
                0.01f,
                settings.chunkSize
            ),
            Mathf.Max(
                1,
                settings.gridWidth
            ),
            Mathf.Max(
                1,
                settings.gridHeight
            )
        );
    }

    private static void DrawHeightTileLattice(
        WorldGridViewportContext context
    )
    {
        WorldSettings settings =
            context.WorldSettings;

        DrawRectangularLattice(
            context,
            Mathf.Max(
                0.01f,
                settings.HeightTileWorldSize
            ),
            Mathf.Max(
                1,
                settings.HeightTileGridWidth
            ),
            Mathf.Max(
                1,
                settings.HeightTileGridHeight
            )
        );
    }

    private static void DrawRectangularLattice(
        WorldGridViewportContext context,
        float cellWorldSize,
        int cellCountX,
        int cellCountZ
    )
    {
        Rect viewport =
            context.ViewportRect;

        WorldGridViewportTransform transform =
            context.Transform;

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    context.WorldSettings
                );

        Vector2 topLeftWorld =
            transform.ViewportToWorld(
                new Vector2(
                    viewport.xMin,
                    viewport.yMin
                ),
                viewport
            );

        Vector2 bottomRightWorld =
            transform.ViewportToWorld(
                new Vector2(
                    viewport.xMax,
                    viewport.yMax
                ),
                viewport
            );

        float visibleMinX =
            Mathf.Min(
                topLeftWorld.x,
                bottomRightWorld.x
            );

        float visibleMaxX =
            Mathf.Max(
                topLeftWorld.x,
                bottomRightWorld.x
            );

        float visibleMinZ =
            Mathf.Min(
                topLeftWorld.y,
                bottomRightWorld.y
            );

        float visibleMaxZ =
            Mathf.Max(
                topLeftWorld.y,
                bottomRightWorld.y
            );

        float drawMinX =
            Mathf.Max(
                0f,
                visibleMinX
            );

        float drawMaxX =
            Mathf.Min(
                worldSizeXZ.x,
                visibleMaxX
            );

        float drawMinZ =
            Mathf.Max(
                0f,
                visibleMinZ
            );

        float drawMaxZ =
            Mathf.Min(
                worldSizeXZ.y,
                visibleMaxZ
            );

        if (
            drawMinX > drawMaxX
            ||
            drawMinZ > drawMaxZ
        )
        {
            return;
        }

        Handles.BeginGUI();

        DrawVerticalBoundaries(
            transform,
            viewport,
            cellWorldSize,
            cellCountX,
            worldSizeXZ.x,
            drawMinX,
            drawMaxX,
            drawMinZ,
            drawMaxZ
        );

        DrawHorizontalBoundaries(
            transform,
            viewport,
            cellWorldSize,
            cellCountZ,
            worldSizeXZ.y,
            drawMinX,
            drawMaxX,
            drawMinZ,
            drawMaxZ
        );

        Handles.EndGUI();
    }

    private static void DrawVerticalBoundaries(
        WorldGridViewportTransform transform,
        Rect viewport,
        float cellWorldSize,
        int cellCount,
        float worldMaximum,
        float drawMinX,
        float drawMaxX,
        float drawMinZ,
        float drawMaxZ
    )
    {
        int minimumIndex =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    drawMinX /
                    cellWorldSize
                ),
                0,
                cellCount
            );

        int maximumIndex =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    drawMaxX /
                    cellWorldSize
                ),
                0,
                cellCount
            );

        for (
            int x = minimumIndex;
            x <= maximumIndex;
            x++
        )
        {
            float worldX =
                x *
                cellWorldSize;

            if (worldX > worldMaximum)
            {
                break;
            }

            DrawVerticalLine(
                transform,
                viewport,
                worldX,
                drawMinZ,
                drawMaxZ
            );
        }

        float nominalMaximum =
            cellCount *
            cellWorldSize;

        if (
            !Mathf.Approximately(
                nominalMaximum,
                worldMaximum
            )
            &&
            worldMaximum >= drawMinX
            &&
            worldMaximum <= drawMaxX
        )
        {
            DrawVerticalLine(
                transform,
                viewport,
                worldMaximum,
                drawMinZ,
                drawMaxZ
            );
        }
    }

    private static void DrawHorizontalBoundaries(
        WorldGridViewportTransform transform,
        Rect viewport,
        float cellWorldSize,
        int cellCount,
        float worldMaximum,
        float drawMinX,
        float drawMaxX,
        float drawMinZ,
        float drawMaxZ
    )
    {
        int minimumIndex =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    drawMinZ /
                    cellWorldSize
                ),
                0,
                cellCount
            );

        int maximumIndex =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    drawMaxZ /
                    cellWorldSize
                ),
                0,
                cellCount
            );

        for (
            int z = minimumIndex;
            z <= maximumIndex;
            z++
        )
        {
            float worldZ =
                z *
                cellWorldSize;

            if (worldZ > worldMaximum)
            {
                break;
            }

            DrawHorizontalLine(
                transform,
                viewport,
                worldZ,
                drawMinX,
                drawMaxX
            );
        }

        float nominalMaximum =
            cellCount *
            cellWorldSize;

        if (
            !Mathf.Approximately(
                nominalMaximum,
                worldMaximum
            )
            &&
            worldMaximum >= drawMinZ
            &&
            worldMaximum <= drawMaxZ
        )
        {
            DrawHorizontalLine(
                transform,
                viewport,
                worldMaximum,
                drawMinX,
                drawMaxX
            );
        }
    }

    private static void DrawVerticalLine(
        WorldGridViewportTransform transform,
        Rect viewport,
        float worldX,
        float drawMinZ,
        float drawMaxZ
    )
    {
        Vector2 lineStart =
            transform.WorldToViewport(
                new Vector2(
                    worldX,
                    drawMinZ
                ),
                viewport
            );

        Vector2 lineEnd =
            transform.WorldToViewport(
                new Vector2(
                    worldX,
                    drawMaxZ
                ),
                viewport
            );

        Handles.DrawLine(
            lineStart,
            lineEnd
        );
    }

    private static void DrawHorizontalLine(
        WorldGridViewportTransform transform,
        Rect viewport,
        float worldZ,
        float drawMinX,
        float drawMaxX
    )
    {
        Vector2 lineStart =
            transform.WorldToViewport(
                new Vector2(
                    drawMinX,
                    worldZ
                ),
                viewport
            );

        Vector2 lineEnd =
            transform.WorldToViewport(
                new Vector2(
                    drawMaxX,
                    worldZ
                ),
                viewport
            );

        Handles.DrawLine(
            lineStart,
            lineEnd
        );
    }
}
