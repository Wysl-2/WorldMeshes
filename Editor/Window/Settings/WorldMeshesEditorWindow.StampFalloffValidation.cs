using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampFalloffValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Falloff Shape / Profile Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainStampFalloffValidationUtility
                .IsScheduled
            ||
            TerrainStampFalloffValidationUtility
                .IsRunning;

        string status =
            TerrainStampFalloffValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainStampFalloffValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainStampFalloffValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainStampFalloffValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainStampFalloffValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainStampFalloffValidationUtility
                .LastFailedCount
                .ToString()
        );

        GUILayout.Space(5f);

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
                "Validate Stamp Falloff Shape / Profile",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainStampFalloffValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            TerrainStampFalloffValidationUtility
                .LastSummary,
            TerrainStampFalloffValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
