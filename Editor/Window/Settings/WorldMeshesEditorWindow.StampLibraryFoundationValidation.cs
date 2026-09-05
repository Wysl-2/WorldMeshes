using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampLibraryFoundationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Library Foundation Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainHeightStampLibraryFoundationValidationUtility
                .IsScheduled
            ||
            TerrainHeightStampLibraryFoundationValidationUtility
                .IsRunning;

        string status =
            TerrainHeightStampLibraryFoundationValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainHeightStampLibraryFoundationValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainHeightStampLibraryFoundationValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainHeightStampLibraryFoundationValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainHeightStampLibraryFoundationValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainHeightStampLibraryFoundationValidationUtility
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
                "Validate Stamp Library Foundation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightStampLibraryFoundationValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainHeightStampLibraryFoundationValidationUtility
                .LastSummary,
            TerrainHeightStampLibraryFoundationValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
