using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampSourceRemapValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Source Remapping Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainStampSourceRemapValidationUtility
                .IsScheduled
            ||
            TerrainStampSourceRemapValidationUtility
                .IsRunning;

        string status =
            TerrainStampSourceRemapValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainStampSourceRemapValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainStampSourceRemapValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainStampSourceRemapValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainStampSourceRemapValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainStampSourceRemapValidationUtility
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
                "Validate Stamp Source Remapping",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainStampSourceRemapValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainStampSourceRemapValidationUtility
                .LastSummary,
            TerrainStampSourceRemapValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
