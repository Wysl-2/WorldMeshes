using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package 6 SceneView authoring layer for TerrainNodeElevationSource.
 *
 * This class owns visualization, selection and handle lifecycle only. All
 * output-affecting node mutation is delegated to TerrainRegionalElevationService.
 */
[InitializeOnLoad]
public static class TerrainRegionalElevationSceneTool
{
    public enum DragMode
    {
        None,
        MoveXZ,
        Elevation
    }

    private const string EnabledEditorPrefsKey =
        "WorldMeshes.RegionalElevation.SceneTool.Enabled";

    private const float MarkerSizeMultiplier = 0.08f;
    private const float MarkerPickSizeMultiplier = 0.12f;
    private const float MoveHandleSizeMultiplier = 0.18f;
    private const float ElevationHandleSizeMultiplier = 0.11f;
    private const float ElevationHandleOffsetMultiplier = 0.45f;
    private const float LabelOffsetMultiplier = 0.18f;

    private static bool callbackAttached;
    private static bool enabled;
    private static WorldSettings worldSettings;
    private static TerrainAuthoringData authoringData;
    private static DragMode activeDragMode;
    private static string activeDragStableId = "";
    private static string contextStatus = "No authoring context is assigned.";

    static TerrainRegionalElevationSceneTool()
    {
        enabled = EditorPrefs.GetBool(EnabledEditorPrefsKey, false);

        TerrainRegionalElevationSelectionState.SelectionChanged +=
            OnSelectionChanged;

        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += OnEditorQuitting;
        EditorApplication.delayCall += TryLoadDefaultContext;

        ApplyCallbackState();
    }

    public static bool Enabled
    {
        get => enabled;
        set => SetEnabled(value);
    }

    public static DragMode ActiveDragMode => activeDragMode;
    public static string ActiveDragStableId => activeDragStableId;
    public static bool HasValidContext => TryResolveNodeSource(out _, out _);
    public static string ContextStatus => contextStatus;

    public static void SetEnabled(bool value)
    {
        if (enabled == value)
        {
            return;
        }

        if (!value)
        {
            CancelActiveDrag();
        }

        enabled = value;
        EditorPrefs.SetBool(EnabledEditorPrefsKey, value);
        ApplyCallbackState();
        SceneView.RepaintAll();
    }

    public static void SetContext(
        WorldSettings newWorldSettings,
        TerrainAuthoringData newAuthoringData)
    {
        bool changed =
            worldSettings != newWorldSettings ||
            authoringData != newAuthoringData;

        if (!changed)
        {
            RefreshContextStatus();
            return;
        }

        CancelActiveDrag();
        worldSettings = newWorldSettings;
        authoringData = newAuthoringData;
        TerrainRegionalElevationSelectionState.ValidateSelection(authoringData);
        RefreshContextStatus();
        SceneView.RepaintAll();
    }

    public static void ClearContext()
    {
        CancelActiveDrag();
        worldSettings = null;
        authoringData = null;
        contextStatus = "No authoring context is assigned.";
        SceneView.RepaintAll();
    }

    internal static Vector3 GetNodeScenePosition(
        Vector2 positionXZ,
        float elevation)
    {
        return new Vector3(
            positionXZ.x,
            elevation,
            positionXZ.y);
    }

    internal static Vector2 GetPositionXZFromScenePosition(
        Vector3 scenePosition)
    {
        return new Vector2(
            scenePosition.x,
            scenePosition.z);
    }

    internal static Vector2 ClampPositionXZToWorld(
        WorldSettings settings,
        Vector2 positionXZ)
    {
        if (settings == null)
        {
            return positionXZ;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);

        return new Vector2(
            Mathf.Clamp(positionXZ.x, 0f, worldSize.x),
            Mathf.Clamp(positionXZ.y, 0f, worldSize.y));
    }

    internal static float SanitizeSceneElevation(float elevation)
    {
        return elevation;
    }

    internal static bool CancelForLifecycleOrContextLoss()
    {
        bool hadActiveEdit =
            activeDragMode != DragMode.None ||
            TerrainRegionalElevationService.HasActiveInteractiveEdit;

        CancelActiveDrag();
        return hadActiveEdit;
    }

    private static void ApplyCallbackState()
    {
        if (enabled && !callbackAttached)
        {
            SceneView.duringSceneGui += OnSceneGUI;
            callbackAttached = true;
            return;
        }

        if (!enabled && callbackAttached)
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            callbackAttached = false;
        }
    }

    private static void TryLoadDefaultContext()
    {
        if (worldSettings == null)
        {
            worldSettings = AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath);
        }

        if (authoringData == null)
        {
            authoringData = AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath);
        }

        RefreshContextStatus();
    }

    private static void RefreshContextStatus()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            contextStatus = "Regional elevation Scene authoring is unavailable in Play Mode.";
            return;
        }

        if (worldSettings == null || authoringData == null)
        {
            contextStatus = "WorldSettings or TerrainAuthoringData is not assigned.";
            return;
        }

        if (authoringData.sourceMode != TerrainHeightSourceMode.Flat)
        {
            contextStatus = "Regional elevation Scene authoring currently requires a Flat committed height source.";
            return;
        }

        TerrainRegionalElevationSource source = authoringData.RegionalElevationSource;
        if (source == null)
        {
            contextStatus = "No regional elevation source is active.";
            return;
        }

        if (!(source is TerrainNodeElevationSource nodeSource))
        {
            contextStatus =
                "The active regional elevation source is not TerrainNodeElevationSource.";
            return;
        }

        if (!nodeSource.TryValidateNodeStableIds(out string identityError))
        {
            contextStatus = "Regional node identity is invalid. " + identityError;
            return;
        }

        if (!nodeSource.TryValidateOutputData(out string outputError))
        {
            contextStatus = "Regional node output data is invalid. " + outputError;
            return;
        }

        if (nodeSource.NodeCount <= 0)
        {
            contextStatus = "The regional node source contains no nodes.";
            return;
        }

        contextStatus = $"Ready - {nodeSource.NodeCount:N0} regional elevation nodes.";
    }

    private static bool TryResolveNodeSource(
        out TerrainNodeElevationSource source,
        out string errorMessage)
    {
        source = null;
        errorMessage = "";

        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage = "Regional elevation Scene authoring is unavailable in Play Mode.";
            contextStatus = errorMessage;
            return false;
        }

        if (worldSettings == null || authoringData == null)
        {
            errorMessage = "WorldSettings or TerrainAuthoringData is not assigned.";
            contextStatus = errorMessage;
            return false;
        }

        if (authoringData.sourceMode != TerrainHeightSourceMode.Flat)
        {
            errorMessage = "Regional elevation Scene authoring currently requires a Flat committed height source.";
            contextStatus = errorMessage;
            return false;
        }

        if (!(authoringData.RegionalElevationSource is TerrainNodeElevationSource nodeSource))
        {
            errorMessage = authoringData.RegionalElevationSource == null
                ? "No regional elevation source is active."
                : "The active regional elevation source is not TerrainNodeElevationSource.";
            contextStatus = errorMessage;
            return false;
        }

        if (!nodeSource.TryValidateNodeStableIds(out errorMessage) ||
            !nodeSource.TryValidateOutputData(out errorMessage) ||
            nodeSource.NodeCount <= 0)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "The regional node source contains no nodes.";
            }

            contextStatus = errorMessage;
            return false;
        }

        source = nodeSource;
        contextStatus = $"Ready - {nodeSource.NodeCount:N0} regional elevation nodes.";
        return true;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!enabled)
        {
            return;
        }

        if (!TryResolveNodeSource(out TerrainNodeElevationSource source, out _))
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
            {
                CancelActiveDrag();
            }
            return;
        }

        string selectedStableId =
            TerrainRegionalElevationSelectionState.GetSelectedNodeStableId(authoringData);

        TerrainElevationNode selectedNode = null;
        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            bool selected = node.StableId == selectedStableId;
            DrawNodeMarker(node, selected);

            if (selected)
            {
                selectedNode = node;
            }
        }

        if (selectedNode == null && !string.IsNullOrEmpty(selectedStableId))
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
            {
                CancelActiveDrag();
            }
            TerrainRegionalElevationSelectionState.ForceClearSelection(authoringData);
        }

        if (selectedNode != null)
        {
            DrawSelectedNodeHandles(selectedNode);
        }

        HandleActiveDragLifecycle();
    }

    private static void DrawNodeMarker(
        TerrainElevationNode node,
        bool selected)
    {
        Vector3 scenePosition =
            GetNodeScenePosition(node.PositionXZ, node.Elevation);

        float handleSize = HandleUtility.GetHandleSize(scenePosition);
        Color previousColor = Handles.color;
        Handles.color = selected ? Handles.selectedColor : Handles.centerColor;

        bool drawFullVisualization =
            activeDragMode == DragMode.None || selected;

        if (drawFullVisualization)
        {
            Handles.DrawLine(
                new Vector3(scenePosition.x, 0f, scenePosition.z),
                scenePosition);
        }

        /*
         * Always allocate the node selection control, even while another
         * regional handle owns the active drag. Unity IMGUI control IDs are
         * order-dependent; conditionally removing every node button after a
         * drag begins shifts the selected Slider2D/Slider control ID and can
         * cause GUIUtility.hotControl to lose the gesture after one sample.
         *
         * Selection is suppressed while dragging, but the control itself stays
         * in the Layout/Repaint/control-ID sequence.
         */
        bool nodePressed =
            Handles.Button(
                scenePosition,
                Quaternion.identity,
                handleSize * MarkerSizeMultiplier,
                handleSize * MarkerPickSizeMultiplier,
                Handles.SphereHandleCap);

        if (
            activeDragMode == DragMode.None &&
            nodePressed)
        {
            if (TerrainRegionalElevationSelectionState.TrySelectNode(
                authoringData,
                node.StableId,
                out _))
            {
                RepaintWorldMeshesWindows();
                SceneView.RepaintAll();
            }
        }

        /*
         * Labels and long reference lines are visual-only. During an active
         * drag keep them for the selected node but suppress them for the other
         * nodes to reduce SceneView repaint cost without changing control-ID
         * allocation.
         */
        if (drawFullVisualization)
        {
            Handles.Label(
                scenePosition + Vector3.up * handleSize * LabelOffsetMultiplier,
                FormatElevationLabel(node.Elevation));
        }

        Handles.color = previousColor;
    }

    private static void DrawSelectedNodeHandles(
        TerrainElevationNode node)
    {
        Vector3 nodePosition =
            GetNodeScenePosition(node.PositionXZ, node.Elevation);

        float handleSize = HandleUtility.GetHandleSize(nodePosition);

        EditorGUI.BeginChangeCheck();
        Vector3 requestedMovePosition = Handles.Slider2D(
            nodePosition,
            Vector3.up,
            Vector3.right,
            Vector3.forward,
            handleSize * MoveHandleSizeMultiplier,
            Handles.RectangleHandleCap,
            Vector2.zero,
            true);
        bool moveChanged = EditorGUI.EndChangeCheck();

        if (moveChanged)
        {
            if (BeginDragIfNeeded(node.StableId, DragMode.MoveXZ, "Move Regional Elevation Node"))
            {
                Vector2 requestedXZ = ClampPositionXZToWorld(
                    worldSettings,
                    GetPositionXZFromScenePosition(requestedMovePosition));

                if (!TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                    requestedXZ,
                    out string moveError))
                {
                    Debug.LogError("Regional elevation Scene move failed.\n\n" + moveError);
                    CancelActiveDrag();
                }
            }
        }

        float elevationOffset = handleSize * ElevationHandleOffsetMultiplier;
        Vector3 elevationHandlePosition =
            nodePosition + Vector3.up * elevationOffset;

        EditorGUI.BeginChangeCheck();
        Vector3 requestedElevationPosition = Handles.Slider(
            elevationHandlePosition,
            Vector3.up,
            handleSize * ElevationHandleSizeMultiplier,
            Handles.ConeHandleCap,
            0f);
        bool elevationChanged = EditorGUI.EndChangeCheck();

        if (elevationChanged)
        {
            if (BeginDragIfNeeded(node.StableId, DragMode.Elevation, "Set Regional Elevation Node Height"))
            {
                float requestedElevation =
                    SanitizeSceneElevation(requestedElevationPosition.y - elevationOffset);

                if (!TerrainRegionalElevationService.UpdateInteractiveNodeElevation(
                    requestedElevation,
                    out string elevationError))
                {
                    Debug.LogError("Regional elevation Scene height edit failed.\n\n" + elevationError);
                    CancelActiveDrag();
                }
            }
        }
    }

    private static bool BeginDragIfNeeded(
        string stableId,
        DragMode requestedMode,
        string undoLabel)
    {
        if (activeDragMode != DragMode.None)
        {
            return
                activeDragMode == requestedMode &&
                activeDragStableId == stableId;
        }

        if (!TerrainRegionalElevationService.BeginInteractiveNodeEdit(
            authoringData,
            worldSettings,
            stableId,
            undoLabel,
            out string errorMessage))
        {
            Debug.LogError("Could not begin regional elevation Scene drag.\n\n" + errorMessage);
            return false;
        }

        activeDragMode = requestedMode;
        activeDragStableId = stableId;
        return true;
    }

    private static void HandleActiveDragLifecycle()
    {
        if (activeDragMode == DragMode.None)
        {
            return;
        }

        Event current = Event.current;
        if (current == null)
        {
            return;
        }

        if (
            current.type == EventType.KeyDown &&
            current.keyCode == KeyCode.Escape)
        {
            CancelActiveDrag();
            current.Use();
            return;
        }

        if (current.rawType == EventType.MouseUp)
        {
            CommitActiveDrag();
        }
    }

    private static void CommitActiveDrag()
    {
        if (activeDragMode == DragMode.None)
        {
            return;
        }

        if (!TerrainRegionalElevationService.CommitInteractiveNodeEdit(out string errorMessage))
        {
            Debug.LogError("Regional elevation Scene drag commit failed.\n\n" + errorMessage);
            TerrainRegionalElevationService.CancelInteractiveNodeEdit(out _);
        }

        ClearDragState();
        RepaintWorldMeshesWindows();
        SceneView.RepaintAll();
    }

    private static void CancelActiveDrag()
    {
        if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
        {
            TerrainRegionalElevationService.CancelInteractiveNodeEdit(out _);
        }

        ClearDragState();
        RepaintWorldMeshesWindows();
        SceneView.RepaintAll();
    }

    private static void ClearDragState()
    {
        activeDragMode = DragMode.None;
        activeDragStableId = "";
    }

    private static string FormatElevationLabel(float elevation)
    {
        float rounded = Mathf.Round(elevation);
        if (Mathf.Abs(elevation - rounded) < 0.05f)
        {
            return rounded.ToString("0") + " m";
        }

        return elevation.ToString("0.0") + " m";
    }

    private static void OnSelectionChanged()
    {
        SceneView.RepaintAll();
    }

    private static void RepaintWorldMeshesWindows()
    {
        WorldMeshesEditorWindow[] windows =
            Resources.FindObjectsOfTypeAll<WorldMeshesEditorWindow>();

        for (int index = 0; index < windows.Length; index++)
        {
            if (windows[index] != null)
            {
                windows[index].Repaint();
            }
        }
    }

    private static void OnBeforeAssemblyReload()
    {
        CancelActiveDrag();

        if (callbackAttached)
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            callbackAttached = false;
        }
    }

    private static void OnEditorQuitting()
    {
        CancelActiveDrag();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            CancelActiveDrag();
        }
    }
}
