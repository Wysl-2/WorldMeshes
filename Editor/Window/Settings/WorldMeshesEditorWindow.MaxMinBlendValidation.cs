using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawMaxMinBlendValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Max / Min Blend Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainMaxMinBlendValidationUtility
                .IsScheduled
            ||
            TerrainMaxMinBlendValidationUtility
                .IsRunning;

        string status =
            TerrainMaxMinBlendValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainMaxMinBlendValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainMaxMinBlendValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainMaxMinBlendValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainMaxMinBlendValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainMaxMinBlendValidationUtility
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
                "Validate Max / Min Blend Modes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainMaxMinBlendValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainMaxMinBlendValidationUtility
                .LastSummary,
            TerrainMaxMinBlendValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        EditorGUILayout.HelpBox(
            "Validates Additive regression behavior, Max/Min target-surface " +
            "composition, falloff, remapping/orientation, ordered stacking, " +
            "cross-tile seams, conservative range metadata, runtime parity, " +
            "blend-mode mutations, dirty regions, enable/disable, and Undo/Redo.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }
}
