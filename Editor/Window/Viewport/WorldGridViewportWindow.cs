using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

public class WorldGridViewportWindow : EditorWindow
{
    // =====================================================
    // GRID VIEWPORT
    // =====================================================

    private const float DefaultChunkPixelSize =
        64f;

    private const float ToolbarHeight =
        22f;

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

    private const float DefaultInformationPanelWidth =
        240f;

    private const float MinimumInformationPanelWidth =
        160f;

    private const float MinimumMapViewportWidth =
        180f;

    private const float InformationSplitterWidth =
        5f;

    private const double ContinuousRepaintInterval =
        1.0 / 20.0;

    private static readonly Color SplitterColor =
        new Color(
            0f,
            0f,
            0f,
            0.35f
        );

    private static readonly string[] LatticeDisplayNames =
    {
        "None",
        "World Chunks",
        "Height Tiles"
    };

    [SerializeField]
    private WorldSettings worldSettings;

    [SerializeField]
    private WorldGridViewportTransform viewportTransform =
        new WorldGridViewportTransform();

    [SerializeField]
    private WorldGridViewportLattice lattice =
        WorldGridViewportLattice.WorldChunks;

    [SerializeField]
    private WorldGridViewportOverlayState overlayState =
        new WorldGridViewportOverlayState();

    [SerializeField]
    private bool informationPanelExpanded =
        true;

    [SerializeField]
    private float informationPanelWidth =
        DefaultInformationPanelWidth;

    private bool isPanning;

    private bool isResizingInformationPanel;

    private bool mouseInsideWindow;

    private WorldGridViewportExecutionState executionState;

    private bool requiresContinuousRepaint;

    private double lastContinuousRepaintTime;

    private WorldGridViewportSelection hoveredSelection;

    private WorldGridViewportSelection selectedSelection;

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
                420f,
                240f
            );

        wantsMouseMove =
            true;

        wantsMouseEnterLeaveWindow =
            true;

        executionState =
            ResolveInitialExecutionState();

        requiresContinuousRepaint =
            false;

        lastContinuousRepaintTime =
            0d;

        EnsureWorldSettingsLoaded();
        EnsureViewportTransformInitialized();

        if (overlayState == null)
        {
            overlayState =
                new WorldGridViewportOverlayState();
        }

        Undo.undoRedoPerformed -=
            HandleUndoRedo;

        Undo.undoRedoPerformed +=
            HandleUndoRedo;

        EditorApplication.playModeStateChanged -=
            HandlePlayModeStateChanged;

        EditorApplication.playModeStateChanged +=
            HandlePlayModeStateChanged;

        EditorApplication.pauseStateChanged -=
            HandlePauseStateChanged;

        EditorApplication.pauseStateChanged +=
            HandlePauseStateChanged;

        EditorApplication.update -=
            HandleEditorUpdate;

        EditorApplication.update +=
            HandleEditorUpdate;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -=
            HandleUndoRedo;

        EditorApplication.playModeStateChanged -=
            HandlePlayModeStateChanged;

        EditorApplication.pauseStateChanged -=
            HandlePauseStateChanged;

        EditorApplication.update -=
            HandleEditorUpdate;

        requiresContinuousRepaint =
            false;

        lastContinuousRepaintTime =
            0d;
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

    private void HandlePlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
                executionState =
                    WorldGridViewportExecutionState
                        .EnteringPlayMode;
                break;

            case PlayModeStateChange.EnteredPlayMode:
                executionState =
                    WorldGridViewportExecutionState
                        .PlayMode;
                break;

            case PlayModeStateChange.ExitingPlayMode:
                executionState =
                    WorldGridViewportExecutionState
                        .ExitingPlayMode;
                break;

            case PlayModeStateChange.EnteredEditMode:
                executionState =
                    WorldGridViewportExecutionState
                        .EditMode;
                break;
        }

        InvalidateContinuousRepaint();
        Repaint();
    }

    private void HandlePauseStateChanged(
        PauseState state
    )
    {
        _ =
            state;

        InvalidateContinuousRepaint();
        Repaint();
    }

    private void HandleEditorUpdate()
    {
        if (!requiresContinuousRepaint)
        {
            return;
        }

        double currentTime =
            EditorApplication.timeSinceStartup;

        if (
            currentTime -
                lastContinuousRepaintTime <
            ContinuousRepaintInterval
        )
        {
            return;
        }

        lastContinuousRepaintTime =
            currentTime;

        Repaint();
    }

    private static WorldGridViewportExecutionState
        ResolveInitialExecutionState()
    {
        if (EditorApplication.isPlaying)
        {
            return
                WorldGridViewportExecutionState
                    .PlayMode;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            return
                WorldGridViewportExecutionState
                    .EnteringPlayMode;
        }

        return
            WorldGridViewportExecutionState
                .EditMode;
    }

    private void InvalidateContinuousRepaint()
    {
        requiresContinuousRepaint =
            false;

        lastContinuousRepaintTime =
            0d;
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
            out Rect toolbar,
            out Rect cornerArea,
            out Rect horizontalRuler,
            out Rect verticalRuler,
            out Rect viewport,
            out Rect informationSplitter,
            out Rect informationPanel,
            out Rect statusBar
        );

        HandleInformationPanelResize(
            informationSplitter
        );

        HandleFrameWorldShortcut(
            viewport
        );

        HandleZoom(
            viewport
        );

        HandlePanning(
            viewport
        );

        WorldGridViewportContext context =
            CreateContext(
                viewport
            );

        requiresContinuousRepaint =
            false;

        DrawViewport(
            context
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

        DrawInformationPanelSplitter(
            informationSplitter
        );

        DrawToolbar(
            toolbar,
            context
        );

        if (informationPanelExpanded)
        {
            WorldGridViewportInfoPanel.Draw(
                informationPanel,
                context,
                lattice
            );
        }

        DrawStatusBar(
            statusBar,
            context
        );
    }

    private void CalculateLayout(
        out Rect toolbar,
        out Rect cornerArea,
        out Rect horizontalRuler,
        out Rect verticalRuler,
        out Rect viewport,
        out Rect informationSplitter,
        out Rect informationPanel,
        out Rect statusBar
    )
    {
        float safeWindowWidth =
            Mathf.Max(
                0f,
                position.width
            );

        float safeWindowHeight =
            Mathf.Max(
                0f,
                position.height
            );

        float mainContentTop =
            Mathf.Min(
                ToolbarHeight,
                safeWindowHeight
            );

        float statusHeight =
            Mathf.Min(
                StatusBarHeight,
                Mathf.Max(
                    0f,
                    safeWindowHeight -
                        mainContentTop
                )
            );

        float mainContentHeight =
            Mathf.Max(
                0f,
                safeWindowHeight -
                    mainContentTop -
                    statusHeight
            );

        float splitterWidth =
            informationPanelExpanded
                ? InformationSplitterWidth
                : 0f;

        float panelWidth =
            informationPanelExpanded
                ? GetClampedInformationPanelWidth()
                : 0f;

        if (informationPanelExpanded)
        {
            informationPanelWidth =
                panelWidth;
        }

        float mapRegionWidth =
            Mathf.Max(
                0f,
                safeWindowWidth -
                    splitterWidth -
                    panelWidth
            );

        float rulerHeight =
            Mathf.Min(
                WorldGridViewportRuler
                    .HorizontalRulerHeight,
                mainContentHeight
            );

        float verticalRulerWidth =
            Mathf.Min(
                WorldGridViewportRuler
                    .VerticalRulerWidth,
                mapRegionWidth
            );

        float viewportWidth =
            Mathf.Max(
                0f,
                mapRegionWidth -
                    verticalRulerWidth
            );

        float viewportHeight =
            Mathf.Max(
                0f,
                mainContentHeight -
                    rulerHeight
            );

        toolbar =
            new Rect(
                0f,
                0f,
                safeWindowWidth,
                mainContentTop
            );

        cornerArea =
            new Rect(
                0f,
                mainContentTop,
                verticalRulerWidth,
                rulerHeight
            );

        horizontalRuler =
            new Rect(
                verticalRulerWidth,
                mainContentTop,
                viewportWidth,
                rulerHeight
            );

        verticalRuler =
            new Rect(
                0f,
                mainContentTop +
                    rulerHeight,
                verticalRulerWidth,
                viewportHeight
            );

        viewport =
            new Rect(
                verticalRulerWidth,
                mainContentTop +
                    rulerHeight,
                viewportWidth,
                viewportHeight
            );

        informationSplitter =
            new Rect(
                mapRegionWidth,
                mainContentTop,
                splitterWidth,
                mainContentHeight
            );

        informationPanel =
            new Rect(
                mapRegionWidth +
                    splitterWidth,
                mainContentTop,
                panelWidth,
                mainContentHeight
            );

        statusBar =
            new Rect(
                0f,
                safeWindowHeight -
                    statusHeight,
                safeWindowWidth,
                statusHeight
            );
    }

    private float GetClampedInformationPanelWidth()
    {
        float maximumWidth =
            Mathf.Max(
                0f,
                position.width -
                    WorldGridViewportRuler
                        .VerticalRulerWidth -
                    MinimumMapViewportWidth -
                    InformationSplitterWidth
            );

        if (maximumWidth <= 0f)
        {
            return 0f;
        }

        float minimumWidth =
            Mathf.Min(
                MinimumInformationPanelWidth,
                maximumWidth
            );

        float requestedWidth =
            float.IsNaN(
                informationPanelWidth
            )
            ||
            float.IsInfinity(
                informationPanelWidth
            )
                ? DefaultInformationPanelWidth
                : informationPanelWidth;

        return
            Mathf.Clamp(
                requestedWidth,
                minimumWidth,
                maximumWidth
            );
    }

    private void HandleInformationPanelResize(
        Rect splitter
    )
    {
        Event e =
            Event.current;

        if (!informationPanelExpanded)
        {
            isResizingInformationPanel =
                false;

            return;
        }

        if (
            e.type ==
                EventType.MouseDown
            &&
            e.button == 0
            &&
            splitter.Contains(
                e.mousePosition
            )
        )
        {
            isResizingInformationPanel =
                true;

            e.Use();

            return;
        }

        if (
            e.type ==
                EventType.MouseUp
            &&
            e.button == 0
            &&
            isResizingInformationPanel
        )
        {
            isResizingInformationPanel =
                false;

            e.Use();

            return;
        }

        if (
            e.type ==
                EventType.MouseDrag
            &&
            e.button == 0
            &&
            isResizingInformationPanel
        )
        {
            informationPanelWidth =
                position.width -
                e.mousePosition.x -
                InformationSplitterWidth *
                    0.5f;

            informationPanelWidth =
                GetClampedInformationPanelWidth();

            Repaint();

            e.Use();
        }
    }

    private void DrawInformationPanelSplitter(
        Rect splitter
    )
    {
        if (
            !informationPanelExpanded
            ||
            splitter.width <= 0f
            ||
            splitter.height <= 0f
        )
        {
            return;
        }

        EditorGUIUtility.AddCursorRect(
            splitter,
            MouseCursor.ResizeHorizontal
        );

        EditorGUI.DrawRect(
            splitter,
            SplitterColor
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

    private WorldGridViewportContext CreateContext(
        Rect viewport
    )
    {
        Event e =
            Event.current;

        bool isMouseOverViewport =
            mouseInsideWindow
            &&
            viewport.Contains(
                e.mousePosition
            );

        bool hasMouseWorldPosition =
            isMouseOverViewport
            &&
            worldSettings != null
            &&
            viewportTransform != null
            &&
            viewportTransform.IsInitialized
            &&
            viewport.width > 0f
            &&
            viewport.height > 0f;

        Vector2 mouseWorldXZ =
            hasMouseWorldPosition
                ? viewportTransform.ViewportToWorld(
                    e.mousePosition,
                    viewport
                )
                : Vector2.zero;

        return
            new WorldGridViewportContext(
                worldSettings,
                viewportTransform,
                viewport,
                executionState,
                EditorApplication.isPaused,
                isMouseOverViewport,
                mouseWorldXZ,
                hasMouseWorldPosition
            );
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

    private void DrawToolbar(
        Rect toolbar,
        WorldGridViewportContext context
    )
    {
        if (
            toolbar.width <= 0f
            ||
            toolbar.height <= 0f
        )
        {
            return;
        }

        GUI.Box(
            toolbar,
            GUIContent.none,
            EditorStyles.toolbar
        );

        float x =
            toolbar.x +
            6f;

        Rect gridLabelRect =
            new Rect(
                x,
                toolbar.y,
                32f,
                toolbar.height
            );

        GUI.Label(
            gridLabelRect,
            "Grid:",
            EditorStyles.miniLabel
        );

        x +=
            gridLabelRect.width +
            3f;

        Rect gridPopupRect =
            new Rect(
                x,
                toolbar.y +
                    1f,
                116f,
                Mathf.Max(
                    0f,
                    toolbar.height -
                        2f
                )
            );

        int selectedLattice =
            EditorGUI.Popup(
                gridPopupRect,
                (int)lattice,
                LatticeDisplayNames,
                EditorStyles.toolbarPopup
            );

        if (
            selectedLattice !=
            (int)lattice
        )
        {
            lattice =
                (WorldGridViewportLattice)
                selectedLattice;

            Repaint();
        }

        x +=
            gridPopupRect.width +
            6f;

        Rect overlaysRect =
            new Rect(
                x,
                toolbar.y +
                    1f,
                82f,
                Mathf.Max(
                    0f,
                    toolbar.height -
                        2f
                )
            );

        if (
            GUI.Button(
                overlaysRect,
                new GUIContent(
                    "Overlays",
                    "Enable or disable viewport display overlays"
                ),
                EditorStyles.toolbarDropDown
            )
        )
        {
            ShowOverlaysMenu(
                context
            );
        }

        x +=
            overlaysRect.width +
            6f;

        Rect infoRect =
            new Rect(
                x,
                toolbar.y +
                    1f,
                48f,
                Mathf.Max(
                    0f,
                    toolbar.height -
                        2f
                )
            );

        bool newInformationPanelExpanded =
            GUI.Toggle(
                infoRect,
                informationPanelExpanded,
                new GUIContent(
                    "Info",
                    "Show or hide the information panel"
                ),
                EditorStyles.toolbarButton
            );

        if (
            newInformationPanelExpanded !=
            informationPanelExpanded
        )
        {
            informationPanelExpanded =
                newInformationPanelExpanded;

            isResizingInformationPanel =
                false;

            Repaint();
        }
    }

    private void ShowOverlaysMenu(
        WorldGridViewportContext context
    )
    {
        GenericMenu menu =
            new GenericMenu();

        IReadOnlyList<IWorldGridViewportOverlay> overlays =
            WorldGridViewportOverlayRegistry
                .Overlays;

        if (overlays.Count == 0)
        {
            menu.AddDisabledItem(
                new GUIContent(
                    "No overlays available"
                )
            );

            menu.ShowAsContext();

            return;
        }

        for (
            int i = 0;
            i < overlays.Count;
            i++
        )
        {
            IWorldGridViewportOverlay overlay =
                overlays[i];

            if (overlay == null)
            {
                continue;
            }

            string overlayId =
                overlay.Id;

            bool userEnabled =
                overlayState.IsUserEnabled(
                    overlayId
                );

            GUIContent item =
                new GUIContent(
                    overlay.DisplayName
                );

            if (!overlay.IsAvailable(context))
            {
                menu.AddDisabledItem(
                    item,
                    userEnabled
                );

                continue;
            }

            bool nextEnabled =
                !userEnabled;

            menu.AddItem(
                item,
                userEnabled,
                () =>
                {
                    overlayState.SetUserEnabled(
                        overlayId,
                        nextEnabled
                    );

                    Repaint();
                }
            );
        }

        menu.ShowAsContext();
    }

    private void DrawViewport(
        WorldGridViewportContext context
    )
    {
        Rect viewport =
            context.ViewportRect;

        if (
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return;
        }

        EditorGUI.DrawRect(
            viewport,
            new Color(
                0.12f,
                0.12f,
                0.12f
            )
        );

        if (worldSettings == null)
        {
            DrawMissingWorldSettingsMessage(
                viewport
            );

            return;
        }

        EnsureViewportTransformInitialized();

        WorldGridViewportLatticeRenderer.Draw(
            lattice,
            context
        );

        DrawEnabledOverlays(
            context
        );

        DrawSelectionFoundation(
            context
        );
    }

    private void DrawEnabledOverlays(
        WorldGridViewportContext context
    )
    {
        IReadOnlyList<IWorldGridViewportOverlay> overlays =
            WorldGridViewportOverlayRegistry
                .Overlays;

        for (
            int i = 0;
            i < overlays.Count;
            i++
        )
        {
            IWorldGridViewportOverlay overlay =
                overlays[i];

            if (
                overlay == null
                ||
                !overlayState.IsUserEnabled(
                    overlay.Id
                )
                ||
                !overlay.IsAvailable(
                    context
                )
            )
            {
                continue;
            }

            if (
                overlay.RequiresContinuousRepaint(
                    context
                )
            )
            {
                requiresContinuousRepaint =
                    true;
            }

            overlay.Draw(
                context
            );
        }
    }

    private void DrawSelectionFoundation(
        WorldGridViewportContext context
    )
    {
        /*
         * Selection state is intentionally dormant in this framework package.
         * Future overlays or editing tools may populate hoveredSelection and
         * selectedSelection without coupling selection to one lattice type.
         */
        _ =
            context;

        _ =
            hoveredSelection;

        _ =
            selectedSelection;
    }

    private void DrawStatusBar(
        Rect statusBar,
        WorldGridViewportContext context
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

        if (context.HasMouseWorldPosition)
        {
            coordinateText =
                "X: " +
                context.MouseWorldXZ.x.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture
                ) +
                " m    Z: " +
                context.MouseWorldXZ.y.ToString(
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
                statusBar.x +
                    6f,
                statusBar.y,
                Mathf.Max(
                    0f,
                    statusBar.width -
                        12f
                ),
                statusBar.height
            ),
            coordinateText,
            labelStyle
        );
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
                EventType.MouseUp
            &&
            e.button == 2
            &&
            isPanning
        )
        {
            isPanning =
                false;

            e.Use();

            return;
        }

        if (
            e.type ==
                EventType.MouseDown
            &&
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

            isPanning =
                true;

            e.Use();

            return;
        }

        if (
            e.type ==
                EventType.MouseDrag
            &&
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
}
