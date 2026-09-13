using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampSourceOrientationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Source Orientation Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainStampSourceOrientationValidationUtility
                .IsScheduled
            ||
            TerrainStampSourceOrientationValidationUtility
                .IsRunning;

        string status =
            TerrainStampSourceOrientationValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainStampSourceOrientationValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainStampSourceOrientationValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainStampSourceOrientationValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainStampSourceOrientationValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainStampSourceOrientationValidationUtility
                .LastFailedCount
                .ToString()
        );

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            busy
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Validate Stamp Source Orientation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainStampSourceOrientationValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainStampSourceOrientationValidationUtility
                .LastSummary,
            TerrainStampSourceOrientationValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
