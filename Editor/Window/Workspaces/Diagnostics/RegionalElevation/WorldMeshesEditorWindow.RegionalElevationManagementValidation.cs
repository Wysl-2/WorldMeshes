using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationManagementValidationSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        GUILayout.Label("Regional Elevation Node Management", EditorStyles.boldLabel);

        bool running = TerrainRegionalElevationManagementValidationUtility.IsRunning;
        EditorGUILayout.LabelField("Validation", running ? "Running" : "Ready");
        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            running || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button("Validate Regional Elevation Node Management", GUILayout.ExpandWidth(true)))
        {
            TerrainRegionalElevationManagementValidationUtility.ValidateRegionalElevationManagement();
        }

        EditorGUI.EndDisabledGroup();
        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package 5 validates the production regional mutation service, StableId lookup, add/remove/edit/grid/source operations, no-op behavior, whole-world dirty sets, Undo/Redo tracking, interactive transactions, signature invariants, and protection of the real authoring state.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
