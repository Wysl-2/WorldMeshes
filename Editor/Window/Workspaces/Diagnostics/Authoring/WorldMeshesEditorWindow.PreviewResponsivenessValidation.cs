using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawPreviewResponsivenessValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Preview Responsiveness Validation",
            EditorStyles.boldLabel
        );

        bool isRunning =
            TerrainAuthoringPreviewValidationUtility
                .IsRunning;

        EditorGUILayout.LabelField(
            "Status",
            isRunning
                ? "Running"
                : "Ready"
        );

        EditorGUI.BeginDisabledGroup(
            isRunning
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Validate Preview Responsiveness",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringPreviewValidationUtility
                .ValidatePreviewResponsiveness();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Runs the Stage 10 integration validation for the " +
            "incremental Height Preview cache.\n\n" +

            "The validation checks slice addressing, dirty-tile " +
            "batching, dirty-region mapping, stable cache identity, " +
            "incremental height-range metadata, hierarchy-only " +
            "rebinding, and authoring/runtime signature behavior.\n\n" +

            "The final PASS / FAIL / BLOCKED report is written to " +
            "the Unity Console. The validation does not modify " +
            "persistent terrain authoring data.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
