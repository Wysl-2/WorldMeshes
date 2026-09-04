using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampRotationFoundationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Rotation Foundation Validation",
            EditorStyles.boldLabel
        );

        bool busy =
            TerrainStampRotationFoundationValidationUtility
                .IsScheduled
            ||
            TerrainStampRotationFoundationValidationUtility
                .IsRunning;

        string status =
            TerrainStampRotationFoundationValidationUtility
                .IsScheduled
                ? "Scheduled"
                :
                TerrainStampRotationFoundationValidationUtility
                    .IsRunning
                    ? "Running"
                    :
                    TerrainStampRotationFoundationValidationUtility
                        .LastFailedCount > 0
                        ? "Failed"
                        :
                        TerrainStampRotationFoundationValidationUtility
                            .LastPassedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            TerrainStampRotationFoundationValidationUtility
                .LastPassedCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            TerrainStampRotationFoundationValidationUtility
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
                "Validate Stamp Rotation Foundation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainStampRotationFoundationValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            TerrainStampRotationFoundationValidationUtility
                .LastSummary,
            TerrainStampRotationFoundationValidationUtility
                .LastFailedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        DrawStampRotationSceneToolTestControls();

        GUILayout.EndVertical();
    }

    private void DrawStampRotationSceneToolTestControls()
    {
        GUILayout.Space(
            8f
        );

        GUILayout.Label(
            "Scene Tool Rotation Test",
            EditorStyles.miniBoldLabel
        );

        if (
            !TerrainAuthoringModifierContextUtility
                .TryLoadDefault(
                    out WorldSettings worldSettings,
                    out TerrainAuthoringData authoringData,
                    out string contextError
                )
        )
        {
            EditorGUILayout.HelpBox(
                contextError,
                MessageType.Warning
            );

            return;
        }

        TerrainAuthoringModifierSelection
            .EnsureValidSelection(
                authoringData
            );

        if (
            !TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    authoringData,
                    out TerrainHeightModifier selectedModifier,
                    out _
                )
            ||
            !(selectedModifier is TerrainStampModifier stamp)
        )
        {
            EditorGUILayout.HelpBox(
                "Select a terrain stamp to test the oriented Scene tool.",
                MessageType.Info
            );

            return;
        }

        EditorGUILayout.LabelField(
            "Current Rotation",
            $"{stamp.RotationDegrees:0.##}°"
        );

        bool editingBlocked =
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(
            editingBlocked
        );

        GUILayout.BeginHorizontal();

        DrawDiagnosticRotationButton(
            authoringData,
            worldSettings,
            stamp,
            "0°",
            0f
        );

        DrawDiagnosticRotationButton(
            authoringData,
            worldSettings,
            stamp,
            "30°",
            30f
        );

        DrawDiagnosticRotationButton(
            authoringData,
            worldSettings,
            stamp,
            "45°",
            45f
        );

        DrawDiagnosticRotationButton(
            authoringData,
            worldSettings,
            stamp,
            "90°",
            90f
        );

        GUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "Diagnostics only. These fixed-angle buttons use the normal " +
            "TerrainAuthoringModifierService rotation mutation so Package 2 " +
            "can be tested before Package 3 adds production rotation controls. " +
            "They support normal Undo/Redo.",
            MessageType.None
        );
    }

    private void DrawDiagnosticRotationButton(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        string label,
        float rotationDegrees
    )
    {
        if (
            !GUILayout.Button(
                label
            )
        )
        {
            return;
        }

        if (
            !TerrainAuthoringModifierService
                .SetStampRotationDegrees(
                    authoringData,
                    worldSettings,
                    stamp.StableId,
                    rotationDegrees,
                    out string errorMessage
                )
        )
        {
            Debug.LogError(
                "Could not set diagnostic terrain stamp rotation.\n\n" +
                errorMessage
            );

            return;
        }

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        SceneView.RepaintAll();

        Repaint();
    }
}
