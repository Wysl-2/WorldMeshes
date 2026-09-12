using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    [SerializeField]
    private Vector2 regionalNodeListScroll = Vector2.zero;

    [SerializeField]
    private float addRegionalNodeX;

    [SerializeField]
    private float addRegionalNodeZ;

    [SerializeField]
    private float addRegionalNodeElevation;

    [SerializeField]
    private int managementGridDivisionsX = 3;

    [SerializeField]
    private int managementGridDivisionsZ = 3;

    [SerializeField]
    private float managementGridInitialElevation;

    [SerializeField]
    private float batchRegionalSetElevation;

    [SerializeField]
    private float batchRegionalElevationOffset = 25f;

    [System.NonSerialized]
    private TerrainAuthoringData regionalManagementBoundAuthoringData;

    private readonly List<string> regionalManagementSelectedIds =
        new List<string>();

    private readonly HashSet<string> regionalManagementSelectedSet =
        new HashSet<string>();

    private void DrawRegionalElevationManagementSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        GUILayout.Label("Regional Elevation Node Management", EditorStyles.boldLabel);

        if (worldSettings == null || terrainAuthoringData == null)
        {
            EditorGUILayout.HelpBox(
                "Assign WorldSettings and TerrainAuthoringData before managing regional elevation nodes.",
                MessageType.Warning);
            GUILayout.EndVertical();
            return;
        }

        EnsureRegionalManagementBinding();

        TerrainRegionalElevationSource regionalSource =
            terrainAuthoringData.RegionalElevationSource;

        if (regionalSource == null)
        {
            TerrainRegionalElevationSelectionState.ForceClearSelection(terrainAuthoringData);
            EditorGUILayout.HelpBox(
                "No regional elevation source is currently active. Use the Regional Elevation setup section above to initialize a Four Corners or grid layout.",
                MessageType.Info);
            GUILayout.EndVertical();
            return;
        }

        if (!(regionalSource is TerrainNodeElevationSource nodeSource))
        {
            TerrainRegionalElevationSelectionState.ForceClearSelection(terrainAuthoringData);
            EditorGUILayout.LabelField("Source", regionalSource.GetType().Name);
            EditorGUILayout.HelpBox(
                "Regional node management supports TerrainNodeElevationSource only. This unsupported/future source is left unchanged.",
                MessageType.Warning);
            GUILayout.EndVertical();
            return;
        }

        TerrainRegionalElevationSelectionState.ValidateSelection(terrainAuthoringData);
        bool interactiveEditActive = TerrainRegionalElevationService.HasActiveInteractiveEdit;

        int selectedCount =
            TerrainRegionalElevationSelectionState.GetSelectedCount(terrainAuthoringData);
        string primaryStableId =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(terrainAuthoringData);

        EditorGUILayout.LabelField("Source", "Elevation Nodes");
        EditorGUILayout.LabelField("Nodes", nodeSource.NodeCount.ToString("N0"));
        EditorGUILayout.LabelField("Selected", selectedCount.ToString("N0"));
        EditorGUILayout.LabelField(
            "Primary",
            string.IsNullOrEmpty(primaryStableId)
                ? "None"
                : ShortStableId(primaryStableId));

        EditorGUILayout.HelpBox(
            "Regional node interpolation is global. Any output-affecting node edit currently recomposes and invalidates the complete logical height world.",
            MessageType.Info);

        DrawRegionalNodeSelectionList(nodeSource, interactiveEditActive);

        primaryStableId =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(terrainAuthoringData);
        selectedCount =
            TerrainRegionalElevationSelectionState.GetSelectedCount(terrainAuthoringData);

        TerrainElevationNode primaryNode =
            FindRegionalNodeByStableId(nodeSource, primaryStableId, out int primaryIndex);

        if (primaryNode != null)
        {
            DrawRegionalPrimaryNodeInspector(
                nodeSource,
                primaryNode,
                primaryIndex,
                selectedCount,
                interactiveEditActive);
        }

        if (selectedCount > 0)
        {
            DrawRegionalBatchElevationSettings(interactiveEditActive);
        }

        DrawRegionalAddNodeSettings(interactiveEditActive);
        DrawRegionalReplaceGridSettings(interactiveEditActive);
        DrawRegionalSourceManagementSettings(interactiveEditActive);

        GUILayout.EndVertical();
    }

    private void DrawRegionalNodeSelectionList(
        TerrainNodeElevationSource nodeSource,
        bool interactiveEditActive)
    {
        GUILayout.Space(6f);
        GUILayout.Label("Node List", EditorStyles.boldLabel);

        if (interactiveEditActive)
        {
            EditorGUILayout.HelpBox(
                "Scene node editing is active. Selection and other regional mutations are temporarily locked until the gesture commits or cancels.",
                MessageType.None);
        }

        regionalNodeListScroll = EditorGUILayout.BeginScrollView(
            regionalNodeListScroll,
            GUILayout.MinHeight(90f),
            GUILayout.MaxHeight(180f));

        regionalManagementSelectedSet.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            terrainAuthoringData,
            regionalManagementSelectedSet);
        string primaryStableId =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(terrainAuthoringData);

        IReadOnlyList<TerrainElevationNode> nodes = nodeSource.Nodes;
        EditorGUI.BeginDisabledGroup(interactiveEditActive);

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            bool selected = regionalManagementSelectedSet.Contains(node.StableId);
            bool primary = node.StableId == primaryStableId;

            Vector2 position = node.PositionXZ;
            string label =
                $"[{index}] {(primary ? "*" : " ")} {ShortStableId(node.StableId)}   " +
                $"X {position.x:R}   Z {position.y:R}   H {node.Elevation:R}";

            bool requestedSelected = GUILayout.Toggle(
                selected,
                label,
                "Button",
                GUILayout.ExpandWidth(true));

            if (requestedSelected == selected)
            {
                continue;
            }

            Event current = Event.current;
            bool shift = current != null && current.shift;
            bool toggle = current != null && (current.control || current.command);

            bool success;
            if (toggle)
            {
                success = TerrainRegionalElevationSelectionState.ToggleNode(
                    terrainAuthoringData,
                    node.StableId,
                    out _);
            }
            else if (shift)
            {
                success = TerrainRegionalElevationSelectionState.AddNode(
                    terrainAuthoringData,
                    node.StableId,
                    out _);
            }
            else
            {
                success = TerrainRegionalElevationSelectionState.SelectOnly(
                    terrainAuthoringData,
                    node.StableId,
                    out _);
            }

            if (success)
            {
                SceneView.RepaintAll();
            }
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndScrollView();
    }

    private void DrawRegionalPrimaryNodeInspector(
        TerrainNodeElevationSource nodeSource,
        TerrainElevationNode primaryNode,
        int primaryIndex,
        int selectedCount,
        bool interactiveEditActive)
    {
        GUILayout.Space(7f);
        GUILayout.Label(
            selectedCount > 1 ? "Primary Node" : "Selected Node",
            EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Index", primaryIndex.ToString());
        EditorGUILayout.LabelField("StableId", primaryNode.StableId);

        Vector2 currentPosition = primaryNode.PositionXZ;

        /*
         * Preserve the Package 6 UI-conflict fix: while the Scene service owns
         * an interactive transaction, do not keep DelayedFloatField controls
         * alive with stale values that can masquerade as a discrete user edit.
         */
        if (interactiveEditActive)
        {
            EditorGUILayout.LabelField("Position X", currentPosition.x.ToString("R"));
            EditorGUILayout.LabelField("Position Z", currentPosition.y.ToString("R"));
            EditorGUILayout.LabelField("Elevation", primaryNode.Elevation.ToString("R"));
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            float requestedX = EditorGUILayout.DelayedFloatField(
                "Position X",
                currentPosition.x);
            bool xChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            float requestedZ = EditorGUILayout.DelayedFloatField(
                "Position Z",
                currentPosition.y);
            bool zChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            float requestedElevation = EditorGUILayout.DelayedFloatField(
                "Elevation",
                primaryNode.Elevation);
            bool elevationChanged = EditorGUI.EndChangeCheck();

            if (xChanged || zChanged || elevationChanged)
            {
                Vector2 requestedPosition = currentPosition;
                if (xChanged)
                {
                    requestedPosition.x = requestedX;
                }
                if (zChanged)
                {
                    requestedPosition.y = requestedZ;
                }

                float finalElevation =
                    elevationChanged ? requestedElevation : primaryNode.Elevation;

                if (!TerrainRegionalElevationService.SetNode(
                    terrainAuthoringData,
                    worldSettings,
                    primaryNode.StableId,
                    requestedPosition,
                    finalElevation,
                    out string editError))
                {
                    Debug.LogError("Regional elevation node edit failed.\n\n" + editError);
                }
                else
                {
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
        }

        EditorGUI.BeginDisabledGroup(
            interactiveEditActive || nodeSource.NodeCount <= 1);

        string removeLabel = selectedCount > 1
            ? "Remove Primary Node"
            : "Remove Selected Node";

        if (GUILayout.Button(removeLabel, GUILayout.ExpandWidth(true)))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Regional Elevation Node",
                "Remove the primary regional elevation node? This operation can be undone.",
                "Remove",
                "Cancel");

            if (confirmed)
            {
                string removeId = primaryNode.StableId;
                if (TerrainRegionalElevationService.RemoveNode(
                    terrainAuthoringData,
                    worldSettings,
                    removeId,
                    out string removeError))
                {
                    TerrainRegionalElevationSelectionState.RemoveNode(
                        terrainAuthoringData,
                        removeId,
                        out _);
                    TerrainRegionalElevationSelectionState.ValidateSelection(terrainAuthoringData);
                    Repaint();
                    SceneView.RepaintAll();
                }
                else
                {
                    Debug.LogError("Regional elevation node removal failed.\n\n" + removeError);
                }
            }
        }

        EditorGUI.EndDisabledGroup();
    }

    private void DrawRegionalBatchElevationSettings(bool interactiveEditActive)
    {
        GUILayout.Space(8f);
        GUILayout.Label("Batch Elevation", EditorStyles.boldLabel);

        if (!TerrainRegionalElevationGroupUtility.TryBuildSelectionSummary(
            terrainAuthoringData,
            out TerrainRegionalElevationSelectionSummary summary,
            out string summaryError))
        {
            EditorGUILayout.HelpBox(summaryError, MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Selected Nodes", summary.Count.ToString("N0"));
        EditorGUILayout.LabelField(
            "Elevation",
            summary.HasCommonElevation
                ? summary.CommonElevation.ToString("0.###") + " m"
                : "Mixed");

        if (summary.Count > 0)
        {
            EditorGUILayout.LabelField(
                "Range",
                $"{summary.MinimumElevation:0.###} m .. {summary.MaximumElevation:0.###} m");
        }

        regionalManagementSelectedIds.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            terrainAuthoringData,
            regionalManagementSelectedIds);

        EditorGUI.BeginDisabledGroup(interactiveEditActive || summary.Count <= 0);

        GUILayout.BeginHorizontal();
        batchRegionalSetElevation = EditorGUILayout.FloatField(
            "Set Elevation",
            batchRegionalSetElevation);
        if (GUILayout.Button("Set", GUILayout.Width(70f)))
        {
            ApplyRegionalBatchSetElevation(regionalManagementSelectedIds);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        batchRegionalElevationOffset = EditorGUILayout.FloatField(
            "Offset",
            batchRegionalElevationOffset);
        if (GUILayout.Button("Raise", GUILayout.Width(70f)))
        {
            ApplyRegionalBatchOffset(regionalManagementSelectedIds, Mathf.Abs(batchRegionalElevationOffset));
        }
        if (GUILayout.Button("Lower", GUILayout.Width(70f)))
        {
            ApplyRegionalBatchOffset(regionalManagementSelectedIds, -Mathf.Abs(batchRegionalElevationOffset));
        }
        GUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
    }

    private void ApplyRegionalBatchSetElevation(IReadOnlyList<string> stableIds)
    {
        if (!TerrainRegionalElevationService.SetNodesElevation(
            terrainAuthoringData,
            worldSettings,
            stableIds,
            batchRegionalSetElevation,
            out string errorMessage))
        {
            Debug.LogError("Regional elevation batch set failed.\n\n" + errorMessage);
            return;
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private void ApplyRegionalBatchOffset(
        IReadOnlyList<string> stableIds,
        float delta)
    {
        if (!TerrainRegionalElevationService.OffsetNodesElevation(
            terrainAuthoringData,
            worldSettings,
            stableIds,
            delta,
            out string errorMessage))
        {
            Debug.LogError("Regional elevation batch offset failed.\n\n" + errorMessage);
            return;
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private void DrawRegionalAddNodeSettings(bool interactiveEditActive)
    {
        GUILayout.Space(8f);
        GUILayout.Label("Add Node", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(interactiveEditActive);
        addRegionalNodeX = EditorGUILayout.FloatField("Position X", addRegionalNodeX);
        addRegionalNodeZ = EditorGUILayout.FloatField("Position Z", addRegionalNodeZ);
        addRegionalNodeElevation = EditorGUILayout.FloatField("Elevation", addRegionalNodeElevation);

        if (GUILayout.Button("Add Node", GUILayout.ExpandWidth(true)))
        {
            if (TerrainRegionalElevationService.AddNode(
                terrainAuthoringData,
                worldSettings,
                new Vector2(addRegionalNodeX, addRegionalNodeZ),
                addRegionalNodeElevation,
                out string stableId,
                out string addError))
            {
                TerrainRegionalElevationSelectionState.SelectOnly(
                    terrainAuthoringData,
                    stableId,
                    out _);
                Repaint();
                SceneView.RepaintAll();
            }
            else
            {
                Debug.LogError("Regional elevation node creation failed.\n\n" + addError);
            }
        }

        EditorGUI.EndDisabledGroup();
    }

    private void DrawRegionalReplaceGridSettings(bool interactiveEditActive)
    {
        GUILayout.Space(8f);
        GUILayout.Label("Replace Layout With Grid", EditorStyles.boldLabel);

        managementGridDivisionsX = Mathf.Max(
            1,
            EditorGUILayout.IntField("Grid Divisions X", managementGridDivisionsX));
        managementGridDivisionsZ = Mathf.Max(
            1,
            EditorGUILayout.IntField("Grid Divisions Z", managementGridDivisionsZ));
        managementGridInitialElevation = EditorGUILayout.FloatField(
            "Initial Elevation",
            managementGridInitialElevation);

        bool gridValid = TerrainNodeElevationLayoutUtility.TryGetGeneratedNodeCounts(
            managementGridDivisionsX,
            managementGridDivisionsZ,
            out int nodeCountX,
            out int nodeCountZ,
            out int totalNodeCount,
            out string gridError);

        if (gridValid)
        {
            EditorGUILayout.LabelField(
                "Generated Nodes",
                $"{nodeCountX} x {nodeCountZ} = {totalNodeCount:N0}");
        }
        else
        {
            EditorGUILayout.HelpBox(gridError, MessageType.Error);
        }

        EditorGUI.BeginDisabledGroup(
            interactiveEditActive ||
            !gridValid ||
            !IsFiniteRegionalElevationInput(managementGridInitialElevation));

        if (GUILayout.Button("Replace Existing Nodes With Grid...", GUILayout.ExpandWidth(true)))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Regional Elevation Node Layout",
                "This will replace all existing regional elevation nodes with a newly generated grid. Existing node positions, elevations, StableIds, and the current transient selection will be removed. This operation can be undone.",
                "Replace",
                "Cancel");

            if (confirmed)
            {
                if (TerrainRegionalElevationService.ReplaceNodeLayoutWithGrid(
                    terrainAuthoringData,
                    worldSettings,
                    managementGridDivisionsX,
                    managementGridDivisionsZ,
                    managementGridInitialElevation,
                    out string replaceError))
                {
                    TerrainRegionalElevationSelectionState.ForceClearSelection(terrainAuthoringData);
                    Repaint();
                    SceneView.RepaintAll();
                }
                else
                {
                    Debug.LogError("Regional elevation grid replacement failed.\n\n" + replaceError);
                }
            }
        }

        EditorGUI.EndDisabledGroup();
    }

    private void DrawRegionalSourceManagementSettings(bool interactiveEditActive)
    {
        GUILayout.Space(8f);
        GUILayout.Label("Regional Source", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(interactiveEditActive);
        if (GUILayout.Button("Remove Regional Elevation...", GUILayout.ExpandWidth(true)))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Regional Elevation",
                "This will remove the complete node regional elevation source. The committed base heightfield and ordinary height modifiers are preserved. This operation can be undone.",
                "Remove",
                "Cancel");

            if (confirmed)
            {
                if (TerrainRegionalElevationService.RemoveRegionalElevationSource(
                    terrainAuthoringData,
                    worldSettings,
                    out string sourceError))
                {
                    TerrainRegionalElevationSelectionState.ForceClearSelection(terrainAuthoringData);
                    inputRegionalElevationMethod = RegionalElevationSetupMethod.None;
                    Repaint();
                    SceneView.RepaintAll();
                }
                else
                {
                    Debug.LogError("Regional elevation removal failed.\n\n" + sourceError);
                }
            }
        }
        EditorGUI.EndDisabledGroup();
    }

    private void EnsureRegionalManagementBinding()
    {
        if (regionalManagementBoundAuthoringData == terrainAuthoringData)
        {
            return;
        }

        regionalManagementBoundAuthoringData = terrainAuthoringData;
        TerrainRegionalElevationSelectionState.ValidateSelection(terrainAuthoringData);

        if (worldSettings != null)
        {
            Vector2 worldCenter =
                TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings) * 0.5f;
            addRegionalNodeX = worldCenter.x;
            addRegionalNodeZ = worldCenter.y;
        }
    }

    private static TerrainElevationNode FindRegionalNodeByStableId(
        TerrainNodeElevationSource source,
        string stableId,
        out int index)
    {
        index = -1;
        if (source == null || string.IsNullOrEmpty(stableId))
        {
            return null;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int current = 0; current < nodes.Count; current++)
        {
            TerrainElevationNode node = nodes[current];
            if (node != null && node.StableId == stableId)
            {
                index = current;
                return node;
            }
        }

        return null;
    }

    private static string ShortStableId(string stableId)
    {
        if (string.IsNullOrEmpty(stableId))
        {
            return "<no-id>";
        }

        return stableId.Length <= 8 ? stableId : stableId.Substring(0, 8);
    }
}
