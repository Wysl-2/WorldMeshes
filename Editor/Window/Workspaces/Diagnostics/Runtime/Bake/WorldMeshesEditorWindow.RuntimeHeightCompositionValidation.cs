using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeHeightCompositionValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(
                true
            )
        );

        GUILayout.Label(
            "Runtime Height Composition Validation",
            EditorStyles.boldLabel
        );

        bool validationBusy =
            TerrainRuntimeHeightCompositionValidationUtility
                .IsRunning;

        bool validationScheduled =
            TerrainRuntimeHeightCompositionValidationUtility
                .IsScheduled;

        EditorGUILayout.LabelField(
            "Validation",
            validationScheduled
                ? "Scheduled"
                :
                validationBusy
                    ? "Running"
                    :
                    TerrainRuntimeHeightCompositionValidationUtility
                        .LastSummary
        );

        if (worldSettings != null)
        {
            TerrainGenerationStateUtility.GenerationStatus
                heightmapStatus =
                    TerrainGenerationStateUtility
                        .GetHeightmapStatus(
                            worldSettings
                        );

            TerrainGenerationStateUtility.GenerationStatus
                collisionStatus =
                    TerrainGenerationStateUtility
                        .GetCollisionMeshStatus(
                            worldSettings
                        );

            EditorGUILayout.LabelField(
                "Runtime Heightmaps",
                TerrainGenerationStateUtility
                    .GetStatusLabel(
                        heightmapStatus
                    )
            );

            EditorGUILayout.LabelField(
                "Collision",
                TerrainGenerationStateUtility
                    .GetStatusLabel(
                        collisionStatus
                    )
            );
        }

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            validationBusy
            ||
            validationScheduled
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Validate Runtime Height Composition",
                GUILayout.ExpandWidth(
                    true
                )
            )
        )
        {
            TerrainRuntimeHeightCompositionValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Start from Runtime Ready with at least one enabled in-world " +
            "height modifier. If runtime data is pending, open Runtime and " +
            "run Bake Runtime Changes before this validation.\n\n" +
            "Validation compares modifier-affected generated tiles " +
            "against the live editor composite, verifies unaffected " +
            "generated tiles against committed base data, checks the " +
            "exact manifest height range, and checks generated shared " +
            "tile borders.\n\n" +
            "Play Mode appearance and collision shape should still be " +
            "confirmed visually after baking.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
