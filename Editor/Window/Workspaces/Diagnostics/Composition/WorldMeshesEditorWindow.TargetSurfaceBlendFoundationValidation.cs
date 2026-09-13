using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawTargetSurfaceBlendFoundationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Target-Surface Blend Foundation Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .IsScheduled
            ||
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .IsRunning;

        string status =
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainTargetSurfaceBlendFoundationValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainTargetSurfaceBlendFoundationValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainTargetSurfaceBlendFoundationValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainTargetSurfaceBlendFoundationValidationUtility
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
                "Validate Target-Surface Blend Foundation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .LastSummary,
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        EditorGUILayout.HelpBox(
            "Validates the Package 1 target-surface data foundation, central " +
            "service mutations, dirty regions, duplication, Undo/Redo, and unchanged " +
            "Additive GPU output. Max/Min behavior is validated separately.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }
}
