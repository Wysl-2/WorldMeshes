using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawBlendModeAuthoringUXDefaultsValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Blend Mode Authoring UX + Defaults Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .IsScheduled
            ||
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .IsRunning;

        string status =
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainBlendModeAuthoringUXDefaultsValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainBlendModeAuthoringUXDefaultsValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainBlendModeAuthoringUXDefaultsValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
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
                "Validate Blend Mode UX + Defaults",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .LastSummary,
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        EditorGUILayout.HelpBox(
            "Validates Version 1 creation-default compatibility, Version 2 " +
            "blend/target defaults, mode-aware field rules, all four default " +
            "creation modes, copy-on-placement independence, Stamp Asset " +
            "reassignment, duplication, mode switching, signatures, dirty " +
            "regions, and Undo/Redo.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }
}
