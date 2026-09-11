using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    [SerializeField]
    private string selectedRegionalNodeStableId = "";

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

    [System.NonSerialized]
    private TerrainAuthoringData regionalManagementBoundAuthoringData;

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
            selectedRegionalNodeStableId = "";
            EditorGUILayout.HelpBox(
                "No regional elevation source is currently active. Use the Regional Elevation setup section above to initialize a Four Corners or grid layout.",
                MessageType.Info);
            GUILayout.EndVertical();
            return;
        }

        if (!(regionalSource is TerrainNodeElevationSource nodeSource))
        {
            selectedRegionalNodeStableId = "";
            EditorGUILayout.LabelField("Source", regionalSource.GetType().Name);
            EditorGUILayout.HelpBox(
                "Package 5 manages TerrainNodeElevationSource only. This future/unsupported regional source is left unchanged.",
                MessageType.Warning);
            GUILayout.EndVertical();
            return;
        }

        EditorGUILayout.LabelField("Source", "Elevation Nodes");
        EditorGUILayout.LabelField("Nodes", nodeSource.NodeCount.ToString("N0"));
        EditorGUILayout.HelpBox(
            "Regional node interpolation is global. Any output-affecting node edit currently recomposes and invalidates the complete logical height world.",
            MessageType.Info);

        TerrainElevationNode selectedNode =
            FindRegionalNodeByStableId(nodeSource, selectedRegionalNodeStableId, out int selectedIndex);

        if (selectedNode == null)
        {
            selectedRegionalNodeStableId = "";
        }

        GUILayout.Space(6f);
        GUILayout.Label("Node List", EditorStyles.boldLabel);

        regionalNodeListScroll = EditorGUILayout.BeginScrollView(
            regionalNodeListScroll,
            GUILayout.MinHeight(90f),
            GUILayout.MaxHeight(180f));

        IReadOnlyList<TerrainElevationNode> nodes = nodeSource.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            bool selected = node.StableId == selectedRegionalNodeStableId;
            Vector2 position = node.PositionXZ;
            string shortId = ShortStableId(node.StableId);
            string label =
                $"[{index}] {shortId}   X {position.x:R}   Z {position.y:R}   H {node.Elevation:R}";

            bool newSelected = GUILayout.Toggle(
                selected,
                label,
                "Button",
                GUILayout.ExpandWidth(true));

            if (newSelected && !selected)
            {
                selectedRegionalNodeStableId = node.StableId;
                selectedNode = node;
                selectedIndex = index;
            }
        }

        EditorGUILayout.EndScrollView();

        selectedNode = FindRegionalNodeByStableId(
            nodeSource,
            selectedRegionalNodeStableId,
            out selectedIndex);

        if (selectedNode != null)
        {
            GUILayout.Space(7f);
            GUILayout.Label("Selected Node", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Index", selectedIndex.ToString());
            EditorGUILayout.LabelField("StableId", selectedNode.StableId);

            Vector2 currentPosition = selectedNode.PositionXZ;
            float requestedX = EditorGUILayout.DelayedFloatField("Position X", currentPosition.x);
            float requestedZ = EditorGUILayout.DelayedFloatField("Position Z", currentPosition.y);
            float requestedElevation = EditorGUILayout.DelayedFloatField("Elevation", selectedNode.Elevation);

            Vector2 requestedPosition = new Vector2(requestedX, requestedZ);
            if (requestedPosition != currentPosition || requestedElevation != selectedNode.Elevation)
            {
                if (!TerrainRegionalElevationService.SetNode(
                    terrainAuthoringData,
                    worldSettings,
                    selectedNode.StableId,
                    requestedPosition,
                    requestedElevation,
                    out string editError))
                {
                    Debug.LogError("Regional elevation node edit failed.\n\n" + editError);
                }
                else
                {
                    Repaint();
                }
            }

            EditorGUI.BeginDisabledGroup(nodeSource.NodeCount <= 1);
            if (GUILayout.Button("Remove Selected Node", GUILayout.ExpandWidth(true)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Remove Regional Elevation Node",
                    "Remove the selected regional elevation node? This operation can be undone.",
                    "Remove",
                    "Cancel");

                if (confirmed)
                {
                    string removeId = selectedNode.StableId;
                    if (TerrainRegionalElevationService.RemoveNode(
                        terrainAuthoringData,
                        worldSettings,
                        removeId,
                        out string removeError))
                    {
                        selectedRegionalNodeStableId = "";
                        Repaint();
                    }
                    else
                    {
                        Debug.LogError("Regional elevation node removal failed.\n\n" + removeError);
                    }
                }
            }
            EditorGUI.EndDisabledGroup();

            if (nodeSource.NodeCount <= 1)
            {
                EditorGUILayout.HelpBox(
                    "The final node cannot be removed individually. Use Remove Regional Elevation below to remove the complete source.",
                    MessageType.Info);
            }
        }

        GUILayout.Space(8f);
        GUILayout.Label("Add Node", EditorStyles.boldLabel);

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
                selectedRegionalNodeStableId = stableId;
                Repaint();
            }
            else
            {
                Debug.LogError("Regional elevation node creation failed.\n\n" + addError);
            }
        }

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

        EditorGUI.BeginDisabledGroup(!gridValid || !IsFiniteRegionalElevationInput(managementGridInitialElevation));
        if (GUILayout.Button("Replace Existing Nodes With Grid...", GUILayout.ExpandWidth(true)))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Regional Elevation Node Layout",
                "This will replace all existing regional elevation nodes with a newly generated grid. Existing node positions, elevations, and StableIds will be removed. This operation can be undone.",
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
                    selectedRegionalNodeStableId = "";
                    Repaint();
                }
                else
                {
                    Debug.LogError("Regional elevation grid replacement failed.\n\n" + replaceError);
                }
            }
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(8f);
        GUILayout.Label("Regional Source", EditorStyles.boldLabel);

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
                    selectedRegionalNodeStableId = "";
                    inputRegionalElevationMethod = RegionalElevationSetupMethod.None;
                    Repaint();
                }
                else
                {
                    Debug.LogError("Regional elevation removal failed.\n\n" + sourceError);
                }
            }
        }

        GUILayout.EndVertical();
    }

    private void EnsureRegionalManagementBinding()
    {
        if (regionalManagementBoundAuthoringData == terrainAuthoringData)
        {
            return;
        }

        regionalManagementBoundAuthoringData = terrainAuthoringData;
        selectedRegionalNodeStableId = "";

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
