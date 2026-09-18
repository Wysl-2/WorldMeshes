using System.Globalization;
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

    private const float StatusBarHeight =
        22f;

    private const float FrameWorldPadding =
        24f;

    private const float ZoomBase =
        1.1f;

    private const float MinimumFrameScaleMultiplier =
        0.25f;

    private const float MaximumFrameScaleMultiplier =
        16f;

    private const float DetailedChunkPixelSize =
        1024f;

    private const float FitButtonInset =
        3f;

    [SerializeField]
    private WorldSettings worldSettings;

    [SerializeField]
    private WorldGridViewportTransform viewportTransform =
        new WorldGridViewportTransform();

    private bool isPanning;

    private bool mouseInsideWindow;

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

        wantsMouseMove =
            true;

        wantsMouseEnterLeaveWindow =
            true;

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
        HandleMousePresence();

        CalculateLayout(
            out Rect cornerArea,
            out Rect horizontalRuler,
            out Rect verticalRuler,
            out Rect viewport,
            out Rect statusBar
        );

        HandleFrameWorldShortcut(
            viewport
        );

        HandleZoom(
            viewport
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

        DrawFitWorldButton(
            cornerArea,
            viewport
        );

        DrawStatusBar(
            statusBar,
            viewport
        );
    }

    private void CalculateLayout(
        out Rect cornerArea,
        out Rect horizontalRuler,
        out Rect verticalRuler,
        out Rect viewport,
        out Rect statusBar
    )
    {
        float contentHeight =
            Mathf.Max(
                0f,
                position.height -
                    StatusBarHeight
            );

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
                contentHeight -
                    WorldGridViewportRuler
                        .HorizontalRulerHeight
            );

        cornerArea =
            new Rect(
                0f,
                0f,
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                WorldGridViewportRuler
                    .HorizontalRulerHeight
            );

        horizontalRuler =
            new Rect(
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                0f,
                viewportWidth,
                WorldGridViewportRuler
                    .HorizontalRulerHeight
            );

        verticalRuler =
            new Rect(
                0f,
                WorldGridViewportRuler
                    .HorizontalRulerHeight,
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                viewportHeight
            );

        viewport =
            new Rect(
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                WorldGridViewportRuler
                    .HorizontalRulerHeight,
                viewportWidth,
                viewportHeight
            );

        statusBar =
            new Rect(
                0f,
                contentHeight,
                Mathf.Max(
                    0f,
                    position.width
                ),
                Mathf.Min(
                    StatusBarHeight,
                    Mathf.Max(
                        0f,
                        position.height
                    )
                )
            );
    }

    private void HandleMousePresence()
    {
        Event e =
            Event.current;

        if (
            e.type ==
                EventType.MouseEnterWindow
        )
        {
            mouseInsideWindow =
                true;

            Repaint();

            return;
        }

        if (
            e.type ==
                EventType.MouseLeaveWindow
        )
        {
            mouseInsideWindow =
                false;

            Repaint();

            return;
        }

        if (
            e.type ==
                EventType.MouseMove
        )
        {
            mouseInsideWindow =
                true;

            Repaint();
        }
    }

    private void HandleFrameWorldShortcut(
        Rect viewport
    )
    {
        Event e =
            Event.current;

        if (
            e.type !=
                EventType.KeyDown
            ||
            e.keyCode !=
                KeyCode.F
            ||
            e.modifiers !=
                EventModifiers.None
            ||
            focusedWindow != this
            ||
            EditorGUIUtility.editingTextField
        )
        {
            return;
        }

        FrameWorld(
            viewport
        );

        e.Use();
    }

    private void HandleZoom(
        Rect viewport
    )
    {
        Event e =
            Event.current;

        if (
            e.type !=
                EventType.ScrollWheel
            ||
            !viewport.Contains(
                e.mousePosition
            )
        )
        {
            return;
        }

        EnsureWorldSettingsLoaded();
        EnsureViewportTransformInitialized();

        if (
            viewportTransform == null
            ||
            !viewportTransform.IsInitialized
            ||
            !TryCalculateZoomLimits(
                viewport,
                out float minimumPixelsPerMeter,
                out float maximumPixelsPerMeter
            )
        )
        {
            return;
        }

        float zoomFactor =
            Mathf.Pow(
                ZoomBase,
                -e.delta.y
            );

        if (
            viewportTransform.ZoomAtViewportPoint(
                e.mousePosition,
                viewport,
                zoomFactor,
                minimumPixelsPerMeter,
                maximumPixelsPerMeter
            )
        )
        {
            Repaint();
        }

        e.Use();
    }

    private bool TryCalculateZoomLimits(
        Rect viewport,
        out float minimumPixelsPerMeter,
        out float maximumPixelsPerMeter
    )
    {
        minimumPixelsPerMeter =
            0f;

        maximumPixelsPerMeter =
            0f;

        if (
            worldSettings == null
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return false;
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        float framePixelsPerMeter =
            WorldGridViewportTransform
                .CalculateFramePixelsPerMeter(
                    worldSizeXZ,
                    viewport,
                    FrameWorldPadding
                );

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        float detailedChunkPixelsPerMeter =
            DetailedChunkPixelSize /
            chunkSize;

        minimumPixelsPerMeter =
            framePixelsPerMeter *
            MinimumFrameScaleMultiplier;

        maximumPixelsPerMeter =
            Mathf.Max(
                detailedChunkPixelsPerMeter,
                framePixelsPerMeter *
                    MaximumFrameScaleMultiplier
            );

        if (
            float.IsNaN(minimumPixelsPerMeter)
            ||
            float.IsInfinity(minimumPixelsPerMeter)
            ||
            minimumPixelsPerMeter <= 0f
            ||
            float.IsNaN(maximumPixelsPerMeter)
            ||
            float.IsInfinity(maximumPixelsPerMeter)
            ||
            maximumPixelsPerMeter <= 0f
        )
        {
            return false;
        }

        if (
            maximumPixelsPerMeter <
            minimumPixelsPerMeter
        )
        {
            maximumPixelsPerMeter =
                minimumPixelsPerMeter;
        }

        return true;
    }

    private void FrameWorld(
        Rect viewport
    )
    {
        EnsureWorldSettingsLoaded();

        if (
            worldSettings == null
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return;
        }

        if (viewportTransform == null)
        {
            viewportTransform =
                new WorldGridViewportTransform();
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            viewportTransform.FrameWorld(
                worldSizeXZ,
                viewport,
                FrameWorldPadding
            )
        )
        {
            Repaint();
        }
    }

    private void DrawFitWorldButton(
        Rect cornerArea,
        Rect viewport
    )
    {
        float buttonWidth =
            Mathf.Max(
                0f,
                cornerArea.width -
                    FitButtonInset *
                    2f
            );

        float buttonHeight =
            Mathf.Max(
                0f,
                cornerArea.height -
                    FitButtonInset *
                    2f
            );

        if (
            buttonWidth <= 0f
            ||
            buttonHeight <= 0f
        )
        {
            return;
        }

        Rect buttonRect =
            new Rect(
                cornerArea.x +
                    FitButtonInset,
                cornerArea.y +
                    FitButtonInset,
                buttonWidth,
                buttonHeight
            );

        EditorGUI.BeginDisabledGroup(
            worldSettings == null
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        );

        if (
            GUI.Button(
                buttonRect,
                new GUIContent(
                    "Fit",
                    "Frame World"
                ),
                EditorStyles.toolbarButton
            )
        )
        {
            FrameWorld(
                viewport
            );
        }

        EditorGUI.EndDisabledGroup();
    }

    private void DrawStatusBar(
        Rect statusBar,
        Rect viewport
    )
    {
        if (
            statusBar.width <= 0f
            ||
            statusBar.height <= 0f
        )
        {
            return;
        }

        GUI.Box(
            statusBar,
            GUIContent.none,
            EditorStyles.toolbar
        );

        string coordinateText =
            "X: --    Z: --";

        Event e =
            Event.current;

        if (
            mouseInsideWindow
            &&
            worldSettings != null
            &&
            viewportTransform != null
            &&
            viewportTransform.IsInitialized
            &&
            viewport.Contains(
                e.mousePosition
            )
        )
        {
            Vector2 worldPosition =
                viewportTransform.ViewportToWorld(
                    e.mousePosition,
                    viewport
                );

            coordinateText =
                "X: " +
                worldPosition.x.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture
                ) +
                " m    Z: " +
                worldPosition.y.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture
                ) +
                " m";
        }

        GUIStyle labelStyle =
            new GUIStyle(
                EditorStyles.miniLabel
            );

        labelStyle.alignment =
            TextAnchor.MiddleLeft;

        GUI.Label(
            new Rect(
                statusBar.x + 6f,
                statusBar.y,
                Mathf.Max(
                    0f,
                    statusBar.width - 12f
                ),
                statusBar.height
            ),
            coordinateText,
            labelStyle
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
