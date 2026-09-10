using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawReplaceBlendValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Replace Blend Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainReplaceBlendValidationUtility
                .IsScheduled
            ||
            TerrainReplaceBlendValidationUtility
                .IsRunning;

        string status =
            TerrainReplaceBlendValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainReplaceBlendValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainReplaceBlendValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainReplaceBlendValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainReplaceBlendValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainReplaceBlendValidationUtility
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
                "Validate Replace Blend Mode",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainReplaceBlendValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainReplaceBlendValidationUtility
                .LastSummary,
            TerrainReplaceBlendValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        EditorGUILayout.HelpBox(
            "Validates Replace target-surface composition, source/influence " +
            "separation, falloff and footprint shapes, remapping/orientation/" +
            "smoothing, cross-tile seams, mixed-mode ordering, conservative " +
            "range metadata, runtime parity, central blend-mode mutations, " +
            "dirty regions, enable/disable, and Undo/Redo.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }
}
