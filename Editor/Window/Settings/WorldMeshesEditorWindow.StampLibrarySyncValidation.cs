using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampLibrarySyncValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Library Import / Sync Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainHeightStampLibrarySyncValidationUtility
                .IsScheduled
            ||
            TerrainHeightStampLibrarySyncValidationUtility
                .IsRunning;

        string status =
            TerrainHeightStampLibrarySyncValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainHeightStampLibrarySyncValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainHeightStampLibrarySyncValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainHeightStampLibrarySyncValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainHeightStampLibrarySyncValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainHeightStampLibrarySyncValidationUtility
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
                "Validate Stamp Library Import / Sync",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightStampLibrarySyncValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainHeightStampLibrarySyncValidationUtility
                .LastSummary,
            TerrainHeightStampLibrarySyncValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
