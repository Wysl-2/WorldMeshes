using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationInterpolationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true));

        GUILayout.Label(
            "Regional Elevation Interpolation Mode",
            EditorStyles.boldLabel);

        bool validationRunning =
            TerrainRegionalElevationInterpolationModeValidationUtility.IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning ? "Running" : "Ready");

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            validationRunning ||
            Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button(
            "Validate Interpolation Mode Foundation",
            GUILayout.ExpandWidth(true)))
        {
            TerrainRegionalElevationInterpolationModeValidationUtility
                .ValidateInterpolationModeFoundation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package I1 validation covers serialized enum/default compatibility, unchanged IDW CPU results, " +
            "mode-aware deterministic signatures/snapshots, service transaction/no-op/Undo/Redo behavior, " +
            "and explicit rejection of invalid or not-yet-implemented interpolation during CPU/GPU preparation.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
