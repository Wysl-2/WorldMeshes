using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationSceneToolValidationSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        GUILayout.Label("Regional Elevation Scene Tool", EditorStyles.boldLabel);

        bool validationRunning =
            TerrainRegionalElevationSceneToolValidationUtility.IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning ? "Running" : "Ready");

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            validationRunning ||
            Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button(
            "Validate Regional Elevation Scene Tool",
            GUILayout.ExpandWidth(true)))
        {
            TerrainRegionalElevationSceneToolValidationUtility
                .ValidateRegionalElevationSceneTool();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package 6 validation covers Scene coordinate mapping, tool-level world clamping, " +
            "single StableId selection, Package 5 interactive XZ/elevation transactions, " +
            "whole-world live-preview dirty classification, commit/cancel/no-op semantics, " +
            "and tool lifecycle cancellation. Actual pointer hit-testing and handle feel are " +
            "verified with the README manual SceneView checklist.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
