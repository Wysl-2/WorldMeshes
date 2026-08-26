using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // GRID VIEWPORT
    // =====================================================

    // Purely visual editor-grid size.
    // This has no relationship to terrain chunk size.
    private const float GridCellPixelSize = 64f;

    private Vector2 panOffset =
        Vector2.zero;

    private bool isPanning;

    private void DrawChunkGrid(
        Rect viewport
    )
    {
        EditorGUI.DrawRect(
            viewport,
            new Color(
                0.12f,
                0.12f,
                0.12f
            )
        );

        HandlePanning(
            viewport
        );

        GUI.BeginClip(
            viewport
        );

        Rect localViewport =
            new Rect(
                0f,
                0f,
                viewport.width,
                viewport.height
            );

        if (worldSettings != null)
        {
            DrawVisibleGrid(
                localViewport
            );
        }
        else
        {
            DrawMissingWorldSettingsMessage(
                localViewport
            );
        }

        GUI.EndClip();
    }

    private void DrawMissingWorldSettingsMessage(
        Rect viewport
    )
    {
        GUIStyle style =
            new GUIStyle(
                EditorStyles.centeredGreyMiniLabel
            );

        style.alignment =
            TextAnchor.MiddleCenter;

        GUI.Label(
            viewport,
            "Assign or create a WorldSettings asset.",
            style
        );
    }

    private void HandlePanning(
        Rect viewport
    )
    {
        Event e =
            Event.current;

        if (
            !viewport.Contains(
                e.mousePosition
            )
        )
        {
            return;
        }

        if (
            e.type ==
                EventType.MouseDown &&
            e.button == 2
        )
        {
            isPanning = true;

            e.Use();
        }

        if (
            e.type ==
                EventType.MouseUp &&
            e.button == 2
        )
        {
            isPanning = false;

            e.Use();
        }

        if (
            e.type ==
                EventType.MouseDrag &&
            isPanning
        )
        {
            panOffset +=
                e.delta;

            Repaint();

            e.Use();
        }
    }

    private void DrawVisibleGrid(
        Rect viewport
    )
    {
        if (worldSettings == null)
        {
            return;
        }

        // The viewport grid now gets its dimensions
        // DIRECTLY from WorldSettings.
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

        Handles.BeginGUI();

        float gridPixelWidth =
            gridWidth
            * GridCellPixelSize;

        float gridPixelHeight =
            gridHeight
            * GridCellPixelSize;

        // -------------------------
        // Visible grid range
        // -------------------------

        int minX =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    -panOffset.x
                    / GridCellPixelSize
                )
            );

        int maxX =
            Mathf.Min(
                gridWidth,
                Mathf.CeilToInt(
                    (
                        viewport.width
                        - panOffset.x
                    )
                    / GridCellPixelSize
                )
            );

        int minY =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    -panOffset.y
                    / GridCellPixelSize
                )
            );

        int maxY =
            Mathf.Min(
                gridHeight,
                Mathf.CeilToInt(
                    (
                        viewport.height
                        - panOffset.y
                    )
                    / GridCellPixelSize
                )
            );

        // -------------------------
        // Vertical lines
        // -------------------------

        for (
            int x = minX;
            x <= maxX;
            x++
        )
        {
            float xPosition =
                panOffset.x
                + x * GridCellPixelSize;

            float yStart =
                Mathf.Max(
                    0f,
                    panOffset.y
                );

            float yEnd =
                Mathf.Min(
                    viewport.height,
                    panOffset.y
                    + gridPixelHeight
                );

            Handles.DrawLine(
                new Vector2(
                    xPosition,
                    yStart
                ),
                new Vector2(
                    xPosition,
                    yEnd
                )
            );
        }

        // -------------------------
        // Horizontal lines
        // -------------------------

        for (
            int y = minY;
            y <= maxY;
            y++
        )
        {
            float yPosition =
                panOffset.y
                + y * GridCellPixelSize;

            float xStart =
                Mathf.Max(
                    0f,
                    panOffset.x
                );

            float xEnd =
                Mathf.Min(
                    viewport.width,
                    panOffset.x
                    + gridPixelWidth
                );

            Handles.DrawLine(
                new Vector2(
                    xStart,
                    yPosition
                ),
                new Vector2(
                    xEnd,
                    yPosition
                )
            );
        }

        Handles.EndGUI();
    }
}
