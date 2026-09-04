using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawStampRotationFoundationValidationSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        GUILayout.Label(
            "Stamp Rotation Foundation Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainStampRotationFoundationValidationUtility.IsScheduled
            || TerrainStampRotationFoundationValidationUtility.IsRunning;

        string status =
            TerrainStampRotationFoundationValidationUtility.IsScheduled
                ? "Scheduled"
                : TerrainStampRotationFoundationValidationUtility.IsRunning
                    ? "Running"
                    : TerrainStampRotationFoundationValidationUtility.LastFailedCount > 0
                        ? "Failed"
                        : TerrainStampRotationFoundationValidationUtility.LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField("Status", status);
        EditorGUILayout.LabelField(
            "Passed",
            TerrainStampRotationFoundationValidationUtility.LastPassedCount.ToString()
        );
        EditorGUILayout.LabelField(
            "Failed",
            TerrainStampRotationFoundationValidationUtility.LastFailedCount.ToString()
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            busy
            || Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
        );

        if (GUILayout.Button("Validate Stamp Rotation Foundation", GUILayout.ExpandWidth(true)))
        {
            TerrainStampRotationFoundationValidationUtility.RequestValidation();
        }

        EditorGUI.EndDisabledGroup();
        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            TerrainStampRotationFoundationValidationUtility.LastSummary,
            TerrainStampRotationFoundationValidationUtility.LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
