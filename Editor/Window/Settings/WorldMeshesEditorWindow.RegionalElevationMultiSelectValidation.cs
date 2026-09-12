using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationMultiSelectValidationSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        GUILayout.Label("Regional Elevation Multi-Select", EditorStyles.boldLabel);

        bool validationRunning =
            TerrainRegionalElevationMultiSelectValidationUtility.IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning ? "Running" : "Ready");

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            validationRunning ||
            Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button(
            "Validate Regional Elevation Multi-Select",
            GUILayout.ExpandWidth(true)))
        {
            TerrainRegionalElevationMultiSelectValidationUtility
                .ValidateRegionalElevationMultiSelect();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package 7 validation covers StableId multi-selection and primary fallback, " +
            "marquee/group math, common-delta world clamping, atomic Set/Raise/Lower " +
            "operations, interactive group commit/cancel/no-op semantics, selection locking, " +
            "and common/mixed elevation summaries. Actual Scene pointer hit-testing and " +
            "marquee feel are verified with the README manual checklist.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
