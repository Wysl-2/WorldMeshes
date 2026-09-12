using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationSceneToolSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        GUILayout.Label("Regional Elevation Scene Tool", EditorStyles.boldLabel);

        TerrainRegionalElevationSceneTool.SetContext(
            worldSettings,
            terrainAuthoringData);

        bool enabled = TerrainRegionalElevationSceneTool.Enabled;
        bool requestedEnabled = EditorGUILayout.Toggle("Scene Tool", enabled);
        if (requestedEnabled != enabled)
        {
            TerrainRegionalElevationSceneTool.SetEnabled(requestedEnabled);
        }

        int selectedCount =
            TerrainRegionalElevationSelectionState.GetSelectedCount(terrainAuthoringData);
        string primaryStableId =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(terrainAuthoringData);

        EditorGUILayout.LabelField(
            "Status",
            TerrainRegionalElevationSceneTool.ContextStatus);

        EditorGUILayout.LabelField(
            "Selected Nodes",
            selectedCount.ToString("N0"));

        EditorGUILayout.LabelField(
            "Primary Node",
            string.IsNullOrEmpty(primaryStableId)
                ? "None"
                : ShortStableId(primaryStableId));

        EditorGUILayout.LabelField(
            "Active Drag",
            TerrainRegionalElevationSceneTool.ActiveDragMode.ToString());

        EditorGUILayout.LabelField(
            "Marquee",
            TerrainRegionalElevationSceneTool.HasActiveMarquee
                ? "Active"
                : "Inactive");

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Selection: click = select one, Shift-click = add, Ctrl/Command-click = toggle. " +
            "Drag from empty Scene space to marquee-select nodes; the same modifiers add or toggle the marquee result.\n\n" +
            "A single selected node keeps the Package 6 XZ and elevation handles. Multiple selected nodes use one group XZ handle at the average selection pivot. Group movement applies one common world-bounded delta so relative node spacing is preserved.\n\n" +
            "A complete group drag is one regional interactive transaction. Node data updates on drag samples, full-world terrain preview remains throttled, and revision/runtime invalidation are finalized only at commit.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
