using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampLibraryBrowserValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Library Browser Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainHeightStampLibraryBrowserValidationUtility
                .IsScheduled
            ||
            TerrainHeightStampLibraryBrowserValidationUtility
                .IsRunning;

        string status =
            TerrainHeightStampLibraryBrowserValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainHeightStampLibraryBrowserValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainHeightStampLibraryBrowserValidationUtility
                        .LastFailedCount >
                        0
                        ? "Failed"
                        :
                        TerrainHeightStampLibraryBrowserValidationUtility
                            .LastPassedCount >
                            0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainHeightStampLibraryBrowserValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainHeightStampLibraryBrowserValidationUtility
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
                "Validate Stamp Library Browser",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightStampLibraryBrowserValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainHeightStampLibraryBrowserValidationUtility
                .LastSummary,
            TerrainHeightStampLibraryBrowserValidationUtility
                .LastFailedCount >
                0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
