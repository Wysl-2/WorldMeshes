using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationCompositionValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Regional Elevation Composition",
            EditorStyles.boldLabel
        );

        bool validationRunning =
            TerrainRegionalElevationCompositionValidationUtility
                .IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning
                ? "Running"
                : "Ready"
        );

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            validationRunning
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Validate Regional Elevation Composition",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRegionalElevationCompositionValidationUtility
                .ValidateRegionalElevationComposition();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Package 4 validates the shared regional GPU pass, CPU/GPU IDW " +
            "parity, absolute regional semantics, regional-before-modifier " +
            "ordering, Flat-only source-mode support, conservative preview " +
            "ranges, whole-world preview/runtime scheduling, and persistent " +
            "authoring-state safety.\n\n" +

            "The validator uses transient GPU fixtures and temporary in-memory " +
            "authoring data. It does not rewrite committed authoring height " +
            "tiles or the real regional node layout.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
