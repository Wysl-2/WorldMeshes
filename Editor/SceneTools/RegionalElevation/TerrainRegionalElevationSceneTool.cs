using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package 7 SceneView authoring layer for TerrainNodeElevationSource.
 *
 * Selection is a transient StableId set with one primary node. One selected
 * node retains the Package 6 XZ/elevation handles; multiple selected nodes use
 * one group XZ handle. All terrain mutation remains service-owned.
 */
[InitializeOnLoad]
public static class TerrainRegionalElevationSceneTool
{
    public enum DragMode
    {
        None,
        MoveXZ,
        Elevation,
        GroupMoveXZ
    }

    private enum MarqueeSelectionMode
    {
        Replace,
        Add,
        Toggle
    }

    private const string EnabledEditorPrefsKey =
        "WorldMeshes.RegionalElevation.SceneTool.Enabled";

    private const float MarkerSizeMultiplier = 0.08f;
    private const float MarkerPickSizeMultiplier = 0.12f;
    private const float MoveHandleSizeMultiplier = 0.18f;
    private const float ElevationHandleSizeMultiplier = 0.11f;
    private const float ElevationHandleOffsetMultiplier = 0.45f;
    private const float LabelOffsetMultiplier = 0.18f;
    private const float MarqueeDragThresholdPixels = 4f;

    private static bool callbackAttached;
    private static bool enabled;
    private static WorldSettings worldSettings;
    private static TerrainAuthoringData authoringData;
    private static DragMode activeDragMode;
    private static string activeDragStableId = "";
    private static Vector2 activeGroupGestureDelta = Vector2.zero;
    private static string contextStatus = "No authoring context is assigned.";

    private static bool marqueeActive;
    private static bool marqueeDragged;
    private static int marqueeControlId;
    private static Vector2 marqueeStartGui;
    private static Vector2 marqueeCurrentGui;
    private static MarqueeSelectionMode marqueeMode;
    private static readonly List<string> marqueeSelectionBefore =
        new List<string>();
    private static string marqueePrimaryBefore = "";

    private static readonly List<TerrainElevationNode> selectedNodesBuffer =
        new List<TerrainElevationNode>();
    private static readonly List<string> selectedIdsBuffer =
        new List<string>();
    private static readonly HashSet<string> selectedIdSetBuffer =
        new HashSet<string>();
    private static readonly List<string> marqueeIdsBuffer =
        new List<string>();

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
    public static bool HasActiveMarquee => marqueeActive;
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
            CancelActiveInteraction();
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

        CancelActiveInteraction();
        worldSettings = newWorldSettings;
        authoringData = newAuthoringData;
        TerrainRegionalElevationSelectionState.ValidateSelection(authoringData);
        RefreshContextStatus();
        SceneView.RepaintAll();
    }

    public static void ClearContext()
    {
        CancelActiveInteraction();
        worldSettings = null;
        authoringData = null;
        contextStatus = "No authoring context is assigned.";
        SceneView.RepaintAll();
    }

    internal static Vector3 GetNodeScenePosition(Vector2 positionXZ, float elevation)
    {
        return new Vector3(positionXZ.x, elevation, positionXZ.y);
    }

    internal static Vector2 GetPositionXZFromScenePosition(Vector3 scenePosition)
    {
        return new Vector2(scenePosition.x, scenePosition.z);
    }

    internal static Vector2 ClampPositionXZToWorld(
        WorldSettings settings,
        Vector2 positionXZ)
    {
        if (settings == null)
        {
            return positionXZ;
        }

        Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
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
            marqueeActive ||
            TerrainRegionalElevationService.HasActiveInteractiveEdit;

        CancelActiveInteraction();
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
            contextStatus = "The active regional elevation source is not TerrainNodeElevationSource.";
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
                CancelActiveInteraction();
            }
            return;
        }

        TerrainRegionalElevationSelectionState.ValidateSelection(authoringData);

        int defaultControlId = GUIUtility.GetControlID(FocusType.Passive);
        if (Event.current != null && Event.current.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(defaultControlId);
        }

        string primaryStableId =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(authoringData);

        selectedIdSetBuffer.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            authoringData,
            selectedIdSetBuffer);

        selectedNodesBuffer.Clear();
        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            bool selected = selectedIdSetBuffer.Contains(node.StableId);
            bool primary = selected && node.StableId == primaryStableId;

            DrawNodeMarker(node, selected, primary);
            if (selected)
            {
                selectedNodesBuffer.Add(node);
            }
        }

        if (selectedNodesBuffer.Count == 1)
        {
            DrawSingleNodeHandles(selectedNodesBuffer[0]);
        }
        else if (selectedNodesBuffer.Count > 1)
        {
            DrawGroupMoveHandle(selectedNodesBuffer);
        }

        HandleMarqueeInput(source, defaultControlId);
        HandleActiveDragLifecycle();
        DrawMarqueeOverlay();
    }

    private static void DrawNodeMarker(
        TerrainElevationNode node,
        bool selected,
        bool primary)
    {
        Vector3 scenePosition = GetNodeScenePosition(node.PositionXZ, node.Elevation);
        float handleSize = HandleUtility.GetHandleSize(scenePosition);
        Color previousColor = Handles.color;

        if (primary)
        {
            Handles.color = Handles.selectedColor;
        }
        else if (selected)
        {
            Handles.color = Handles.yAxisColor;
        }
        else
        {
            Handles.color = Handles.centerColor;
        }

        bool drawFullVisualization =
            activeDragMode == DragMode.None || selected;

        if (drawFullVisualization)
        {
            Handles.DrawLine(
                new Vector3(scenePosition.x, 0f, scenePosition.z),
                scenePosition);
        }

        /* Keep every node button in the IMGUI control sequence during drags. */
        bool nodePressed = Handles.Button(
            scenePosition,
            Quaternion.identity,
            handleSize * MarkerSizeMultiplier,
            handleSize * MarkerPickSizeMultiplier,
            Handles.SphereHandleCap);

        if (!marqueeActive &&
            activeDragMode == DragMode.None &&
            !TerrainRegionalElevationService.HasActiveInteractiveEdit &&
            nodePressed)
        {
            ApplyNodeClickSelection(node.StableId, Event.current);
        }

        if (drawFullVisualization)
        {
            Handles.Label(
                scenePosition + Vector3.up * handleSize * LabelOffsetMultiplier,
                FormatElevationLabel(node.Elevation));
        }

        Handles.color = previousColor;
    }

    private static void ApplyNodeClickSelection(string stableId, Event current)
    {
        bool shift = current != null && current.shift;
        bool toggle = current != null && (current.control || current.command);
        bool success;

        if (toggle)
        {
            success = TerrainRegionalElevationSelectionState.ToggleNode(
                authoringData,
                stableId,
                out _);
        }
        else if (shift)
        {
            success = TerrainRegionalElevationSelectionState.AddNode(
                authoringData,
                stableId,
                out _);
        }
        else
        {
            success = TerrainRegionalElevationSelectionState.SelectOnly(
                authoringData,
                stableId,
                out _);
        }

        if (success)
        {
            RepaintWorldMeshesWindows();
            SceneView.RepaintAll();
        }
    }

    private static void DrawSingleNodeHandles(TerrainElevationNode node)
    {
        Vector3 nodePosition = GetNodeScenePosition(node.PositionXZ, node.Elevation);
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
            if (BeginSingleDragIfNeeded(
                node.StableId,
                DragMode.MoveXZ,
                "Move Regional Elevation Node"))
            {
                Vector2 requestedXZ = ClampPositionXZToWorld(
                    worldSettings,
                    GetPositionXZFromScenePosition(requestedMovePosition));

                if (!TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                    requestedXZ,
                    out string moveError))
                {
                    Debug.LogError("Regional elevation Scene move failed.\n\n" + moveError);
                    CancelActiveInteraction();
                }
            }
        }

        float elevationOffset = handleSize * ElevationHandleOffsetMultiplier;
        Vector3 elevationHandlePosition = nodePosition + Vector3.up * elevationOffset;

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
            if (BeginSingleDragIfNeeded(
                node.StableId,
                DragMode.Elevation,
                "Set Regional Elevation Node Height"))
            {
                float requestedElevation =
                    SanitizeSceneElevation(requestedElevationPosition.y - elevationOffset);

                if (!TerrainRegionalElevationService.UpdateInteractiveNodeElevation(
                    requestedElevation,
                    out string elevationError))
                {
                    Debug.LogError("Regional elevation Scene height edit failed.\n\n" + elevationError);
                    CancelActiveInteraction();
                }
            }
        }
    }

    private static void DrawGroupMoveHandle(IReadOnlyList<TerrainElevationNode> selectedNodes)
    {
        Vector2 pivotXZ = TerrainRegionalElevationGroupUtility.CalculateAveragePositionXZ(selectedNodes);
        float pivotElevation = TerrainRegionalElevationGroupUtility.CalculateAverageElevation(selectedNodes);
        Vector3 pivot = GetNodeScenePosition(pivotXZ, pivotElevation);
        float handleSize = HandleUtility.GetHandleSize(pivot);

        EditorGUI.BeginChangeCheck();
        Vector3 requestedScenePosition = Handles.Slider2D(
            pivot,
            Vector3.up,
            Vector3.right,
            Vector3.forward,
            handleSize * MoveHandleSizeMultiplier,
            Handles.RectangleHandleCap,
            Vector2.zero,
            true);
        bool changed = EditorGUI.EndChangeCheck();

        if (!changed)
        {
            return;
        }

        if (!BeginGroupDragIfNeeded())
        {
            return;
        }

        Vector2 requestedPivotXZ = GetPositionXZFromScenePosition(requestedScenePosition);
        Vector2 requestedIncrement = requestedPivotXZ - pivotXZ;
        Vector2 legalIncrement = TerrainRegionalElevationGroupUtility.ClampCommonDeltaToWorld(
            worldSettings,
            selectedNodes,
            requestedIncrement);

        activeGroupGestureDelta += legalIncrement;

        if (!TerrainRegionalElevationService.UpdateInteractiveNodeGroupPositionDelta(
            activeGroupGestureDelta,
            out string groupError))
        {
            Debug.LogError("Regional elevation Scene group move failed.\n\n" + groupError);
            CancelActiveInteraction();
        }
    }

    private static bool BeginSingleDragIfNeeded(
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
        activeGroupGestureDelta = Vector2.zero;
        return true;
    }

    private static bool BeginGroupDragIfNeeded()
    {
        if (activeDragMode != DragMode.None)
        {
            return activeDragMode == DragMode.GroupMoveXZ;
        }

        selectedIdsBuffer.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            authoringData,
            selectedIdsBuffer);

        if (selectedIdsBuffer.Count < 2)
        {
            return false;
        }

        if (!TerrainRegionalElevationService.BeginInteractiveNodeGroupEdit(
            authoringData,
            worldSettings,
            selectedIdsBuffer,
            "Move Regional Elevation Nodes",
            out string errorMessage))
        {
            Debug.LogError("Could not begin regional elevation group drag.\n\n" + errorMessage);
            return false;
        }

        activeDragMode = DragMode.GroupMoveXZ;
        activeDragStableId = "";
        activeGroupGestureDelta = Vector2.zero;
        return true;
    }

    private static void HandleMarqueeInput(
        TerrainNodeElevationSource source,
        int defaultControlId)
    {
        Event current = Event.current;
        if (current == null)
        {
            return;
        }

        if (marqueeActive)
        {
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                CancelMarquee();
                current.Use();
                return;
            }

            if (current.rawType == EventType.MouseDrag && current.button == 0)
            {
                marqueeCurrentGui = current.mousePosition;
                marqueeDragged =
                    marqueeDragged ||
                    (marqueeCurrentGui - marqueeStartGui).sqrMagnitude >=
                        MarqueeDragThresholdPixels * MarqueeDragThresholdPixels;
                SceneView.RepaintAll();
                current.Use();
                return;
            }

            if (current.rawType == EventType.MouseUp && current.button == 0)
            {
                marqueeCurrentGui = current.mousePosition;
                CompleteMarquee(source);
                GUIUtility.hotControl = 0;
                current.Use();
                return;
            }

            return;
        }

        if (activeDragMode != DragMode.None ||
            TerrainRegionalElevationService.HasActiveInteractiveEdit)
        {
            return;
        }

        if (current.type != EventType.MouseDown ||
            current.button != 0 ||
            current.alt)
        {
            return;
        }

        if (HandleUtility.nearestControl != defaultControlId)
        {
            return;
        }

        marqueeActive = true;
        marqueeDragged = false;
        marqueeControlId = defaultControlId;
        marqueeStartGui = current.mousePosition;
        marqueeCurrentGui = current.mousePosition;
        marqueeMode = GetMarqueeMode(current);

        marqueeSelectionBefore.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            authoringData,
            marqueeSelectionBefore);
        marqueePrimaryBefore =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(authoringData);

        GUIUtility.hotControl = marqueeControlId;
        current.Use();
    }

    private static MarqueeSelectionMode GetMarqueeMode(Event current)
    {
        if (current != null && (current.control || current.command))
        {
            return MarqueeSelectionMode.Toggle;
        }

        if (current != null && current.shift)
        {
            return MarqueeSelectionMode.Add;
        }

        return MarqueeSelectionMode.Replace;
    }

    private static void CompleteMarquee(TerrainNodeElevationSource source)
    {
        if (!marqueeDragged)
        {
            if (marqueeMode == MarqueeSelectionMode.Replace)
            {
                TerrainRegionalElevationSelectionState.ClearSelection(authoringData, out _);
            }

            ClearMarqueeState();
            RepaintWorldMeshesWindows();
            SceneView.RepaintAll();
            return;
        }

        Rect rectangle = TerrainRegionalElevationGroupUtility.NormalizeGuiRect(
            marqueeStartGui,
            marqueeCurrentGui);

        marqueeIdsBuffer.Clear();
        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            Vector2 guiPoint = HandleUtility.WorldToGUIPoint(
                GetNodeScenePosition(node.PositionXZ, node.Elevation));

            if (TerrainRegionalElevationGroupUtility.ContainsGuiPoint(rectangle, guiPoint))
            {
                marqueeIdsBuffer.Add(node.StableId);
            }
        }

        if (marqueeIdsBuffer.Count > 0)
        {
            if (marqueeMode == MarqueeSelectionMode.Replace)
            {
                TerrainRegionalElevationSelectionState.SetSelection(
                    authoringData,
                    marqueeIdsBuffer,
                    marqueeIdsBuffer[marqueeIdsBuffer.Count - 1],
                    out _);
            }
            else
            {
                HashSet<string> combined =
                    new HashSet<string>(selectedIdSetBuffer);
                string requestedPrimary =
                    TerrainRegionalElevationSelectionState.GetPrimaryStableId(authoringData);

                for (int index = 0; index < marqueeIdsBuffer.Count; index++)
                {
                    string stableId = marqueeIdsBuffer[index];
                    if (marqueeMode == MarqueeSelectionMode.Add)
                    {
                        combined.Add(stableId);
                        requestedPrimary = stableId;
                    }
                    else if (combined.Contains(stableId))
                    {
                        combined.Remove(stableId);
                        if (requestedPrimary == stableId)
                        {
                            requestedPrimary = "";
                        }
                    }
                    else
                    {
                        combined.Add(stableId);
                        requestedPrimary = stableId;
                    }
                }

                TerrainRegionalElevationSelectionState.SetSelection(
                    authoringData,
                    combined,
                    requestedPrimary,
                    out _);
            }
        }
        else if (marqueeMode == MarqueeSelectionMode.Replace)
        {
            TerrainRegionalElevationSelectionState.ClearSelection(authoringData, out _);
        }

        ClearMarqueeState();
        RepaintWorldMeshesWindows();
        SceneView.RepaintAll();
    }

    private static void CancelMarquee()
    {
        TerrainRegionalElevationSelectionState.SetSelection(
            authoringData,
            marqueeSelectionBefore,
            marqueePrimaryBefore,
            out _);

        if (GUIUtility.hotControl == marqueeControlId)
        {
            GUIUtility.hotControl = 0;
        }

        ClearMarqueeState();
        RepaintWorldMeshesWindows();
        SceneView.RepaintAll();
    }

    private static void ClearMarqueeState()
    {
        marqueeActive = false;
        marqueeDragged = false;
        marqueeControlId = 0;
        marqueeStartGui = Vector2.zero;
        marqueeCurrentGui = Vector2.zero;
        marqueeSelectionBefore.Clear();
        marqueePrimaryBefore = "";
    }

    private static void DrawMarqueeOverlay()
    {
        if (!marqueeActive || !marqueeDragged || Event.current == null ||
            Event.current.type != EventType.Repaint)
        {
            return;
        }

        Rect rect = TerrainRegionalElevationGroupUtility.NormalizeGuiRect(
            marqueeStartGui,
            marqueeCurrentGui);

        Handles.BeginGUI();
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        Handles.EndGUI();
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

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
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

        bool success;
        string errorMessage;

        if (activeDragMode == DragMode.GroupMoveXZ)
        {
            success = TerrainRegionalElevationService.CommitInteractiveNodeGroupEdit(
                out errorMessage);
        }
        else
        {
            success = TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out errorMessage);
        }

        if (!success)
        {
            Debug.LogError("Regional elevation Scene drag commit failed.\n\n" + errorMessage);
            CancelServiceEditForCurrentMode();
        }

        ClearDragState();
        RepaintWorldMeshesWindows();
        SceneView.RepaintAll();
    }

    private static void CancelActiveDrag()
    {
        if (activeDragMode != DragMode.None ||
            TerrainRegionalElevationService.HasActiveInteractiveEdit)
        {
            CancelServiceEditForCurrentMode();
        }

        ClearDragState();
        RepaintWorldMeshesWindows();
        SceneView.RepaintAll();
    }

    private static void CancelServiceEditForCurrentMode()
    {
        if (activeDragMode == DragMode.GroupMoveXZ ||
            TerrainRegionalElevationService.HasActiveInteractiveGroupEdit)
        {
            TerrainRegionalElevationService.CancelInteractiveNodeGroupEdit(out _);
        }
        else
        {
            TerrainRegionalElevationService.CancelInteractiveNodeEdit(out _);
        }
    }

    private static void CancelActiveInteraction()
    {
        if (marqueeActive)
        {
            CancelMarquee();
        }

        CancelActiveDrag();
    }

    private static void ClearDragState()
    {
        activeDragMode = DragMode.None;
        activeDragStableId = "";
        activeGroupGestureDelta = Vector2.zero;
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
        RepaintWorldMeshesWindows();
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
        CancelActiveInteraction();

        if (callbackAttached)
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            callbackAttached = false;
        }
    }

    private static void OnEditorQuitting()
    {
        CancelActiveInteraction();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            CancelActiveInteraction();
        }
    }
}
