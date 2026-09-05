using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampAssetDefaultsValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Asset Defaults Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainHeightStampAssetDefaultsValidationUtility
                .IsScheduled
            ||
            TerrainHeightStampAssetDefaultsValidationUtility
                .IsRunning;

        string status =
            TerrainHeightStampAssetDefaultsValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainHeightStampAssetDefaultsValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainHeightStampAssetDefaultsValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainHeightStampAssetDefaultsValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainHeightStampAssetDefaultsValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainHeightStampAssetDefaultsValidationUtility
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
                "Validate Stamp Asset Defaults",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightStampAssetDefaultsValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainHeightStampAssetDefaultsValidationUtility
                .LastSummary,
            TerrainHeightStampAssetDefaultsValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
