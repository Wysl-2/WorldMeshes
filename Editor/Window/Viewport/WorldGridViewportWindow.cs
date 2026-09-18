using UnityEditor;
using UnityEngine;

public class WorldGridViewportWindow : EditorWindow
{
    // =====================================================
    // GRID VIEWPORT
    // =====================================================

    /*
     * Initial presentation scale only.
     *
     * Grid geometry is defined in world meters through
     * WorldSettings.chunkSize. This value only preserves the
     * familiar initial appearance of approximately 64 pixels per
     * terrain chunk before future zoom controls are introduced.
     */
    private const float DefaultChunkPixelSize =
        64f;

    [SerializeField]
    private WorldSettings worldSettings;

    [SerializeField]
    private WorldGridViewportTransform viewportTransform =
        new WorldGridViewportTransform();

    private bool isPanning;

    [MenuItem(
        "Tools/WorldMeshes/World Grid Viewport",
        false,
        1
    )]
    public static void ShowWindow()
    {
        GetWindow<WorldGridViewportWindow>(
            "World Grid"
        );
    }

    internal static void RepaintOpenWindows()
    {
        WorldGridViewportWindow[] windows =
            Resources.FindObjectsOfTypeAll<WorldGridViewportWindow>();

        for (
            int i = 0;
            i < windows.Length;
            i++
        )
        {
            WorldGridViewportWindow window =
                windows[i];

            if (window == null)
            {
                continue;
            }

            window.EnsureWorldSettingsLoaded();
            window.EnsureViewportTransformInitialized();
            window.Repaint();
        }
    }

    private void OnEnable()
    {
        minSize =
            new Vector2(
                200f,
                200f
            );

        EnsureWorldSettingsLoaded();
        EnsureViewportTransformInitialized();

        Undo.undoRedoPerformed -=
            HandleUndoRedo;

        Undo.undoRedoPerformed +=
            HandleUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -=
            HandleUndoRedo;
    }

    private void OnProjectChange()
    {
        EnsureWorldSettingsLoaded();
        EnsureViewportTransformInitialized();

        Repaint();
    }

    private void HandleUndoRedo()
    {
        EnsureWorldSettingsLoaded();
        EnsureViewportTransformInitialized();

        Repaint();
    }

    private void EnsureWorldSettingsLoaded()
    {
        if (worldSettings != null)
        {
            return;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );
    }

    private void EnsureViewportTransformInitialized()
    {
        if (viewportTransform == null)
        {
            viewportTransform =
                new WorldGridViewportTransform();
        }

        if (
            viewportTransform.IsInitialized
            ||
            worldSettings == null
        )
        {
            return;
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        float defaultPixelsPerMeter =
            DefaultChunkPixelSize /
            chunkSize;

        viewportTransform.Initialize(
            worldSizeXZ *
                0.5f,
            defaultPixelsPerMeter
        );
    }

    private void OnGUI()
    {
        float viewportWidth =
            Mathf.Max(
                0f,
                position.width -
                    WorldGridViewportRuler
                        .VerticalRulerWidth
            );

        float viewportHeight =
            Mathf.Max(
                0f,
                position.height -
                    WorldGridViewportRuler
                        .HorizontalRulerHeight
            );

        Rect cornerArea =
            new Rect(
                0f,
                0f,
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                WorldGridViewportRuler
                    .HorizontalRulerHeight
            );

        Rect horizontalRuler =
            new Rect(
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                0f,
                viewportWidth,
                WorldGridViewportRuler
                    .HorizontalRulerHeight
            );

        Rect verticalRuler =
            new Rect(
                0f,
                WorldGridViewportRuler
                    .HorizontalRulerHeight,
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                viewportHeight
            );

        Rect viewport =
            new Rect(
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                WorldGridViewportRuler
                    .HorizontalRulerHeight,
                viewportWidth,
                viewportHeight
            );

        DrawChunkGrid(
            viewport
        );

        WorldGridViewportRuler.Draw(
            cornerArea,
            horizontalRuler,
            verticalRuler,
            viewport,
            viewportTransform
        );
    }

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

        if (worldSettings != null)
        {
            EnsureViewportTransformInitialized();

            DrawVisibleGrid(
                viewport
            );
        }
        else
        {
            DrawMissingWorldSettingsMessage(
                viewport
            );
        }
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
            e.type ==
                EventType.MouseUp &&
            e.button == 2 &&
            isPanning
        )
        {
            isPanning = false;

            e.Use();

            return;
        }

        if (
            e.type ==
                EventType.MouseDown &&
            e.button == 2
        )
        {
            if (
                !viewport.Contains(
                    e.mousePosition
                )
            )
            {
                return;
            }

            isPanning = true;

            e.Use();

            return;
        }

        if (
            e.type ==
                EventType.MouseDrag &&
            isPanning
        )
        {
            EnsureViewportTransformInitialized();

            if (
                viewportTransform != null
                &&
                viewportTransform.IsInitialized
            )
            {
                viewportTransform.PanByPixels(
                    e.delta
                );

                Repaint();
            }

            e.Use();
        }
    }

    private void DrawVisibleGrid(
        Rect viewport
    )
    {
        if (
            worldSettings == null
            ||
            viewportTransform == null
            ||
            !viewportTransform.IsInitialized
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return;
        }

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

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        Vector2 topLeftWorld =
            viewportTransform.ViewportToWorld(
                new Vector2(
                    viewport.xMin,
                    viewport.yMin
                ),
                viewport
            );

        Vector2 bottomRightWorld =
            viewportTransform.ViewportToWorld(
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

        int minX =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    drawMinX /
                    chunkSize
                ),
                0,
                gridWidth
            );

        int maxX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    drawMaxX /
                    chunkSize
                ),
                0,
                gridWidth
            );

        int minZ =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    drawMinZ /
                    chunkSize
                ),
                0,
                gridHeight
            );

        int maxZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    drawMaxZ /
                    chunkSize
                ),
                0,
                gridHeight
            );

        Handles.BeginGUI();

        for (
            int x = minX;
            x <= maxX;
            x++
        )
        {
            float worldX =
                x *
                chunkSize;

            Vector2 lineStart =
                viewportTransform.WorldToViewport(
                    new Vector2(
                        worldX,
                        drawMinZ
                    ),
                    viewport
                );

            Vector2 lineEnd =
                viewportTransform.WorldToViewport(
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

        for (
            int z = minZ;
            z <= maxZ;
            z++
        )
        {
            float worldZ =
                z *
                chunkSize;

            Vector2 lineStart =
                viewportTransform.WorldToViewport(
                    new Vector2(
                        drawMinX,
                        worldZ
                    ),
                    viewport
                );

            Vector2 lineEnd =
                viewportTransform.WorldToViewport(
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

        Handles.EndGUI();
    }
}
