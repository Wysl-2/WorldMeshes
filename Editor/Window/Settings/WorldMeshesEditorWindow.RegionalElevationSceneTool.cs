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

        string selectedStableId =
            TerrainRegionalElevationSelectionState.GetSelectedNodeStableId(
                terrainAuthoringData);

        EditorGUILayout.LabelField(
            "Status",
            TerrainRegionalElevationSceneTool.ContextStatus);

        EditorGUILayout.LabelField(
            "Selected Node",
            string.IsNullOrEmpty(selectedStableId)
                ? "None"
                : ShortStableId(selectedStableId));

        EditorGUILayout.LabelField(
            "Active Drag",
            TerrainRegionalElevationSceneTool.ActiveDragMode.ToString());

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "When enabled, every active regional elevation node is drawn in the Scene view. " +
            "Click a node to select it. The selected node has an XZ planar movement handle " +
            "and a separate vertical elevation handle. XZ Scene dragging is constrained to " +
            "the logical world bounds; Package 5 numeric editing remains permissive for " +
            "finite out-of-world coordinates.\n\n" +
            "A complete handle drag is one Package 5 interactive transaction. Terrain preview " +
            "can recompose during the drag, while authoringRevision and runtime invalidation " +
            "are finalized only when the gesture commits.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
