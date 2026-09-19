using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawSceneViewResidencyValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Scene View Residency",
                "Validation",
                TerrainAuthoringSceneViewResidencyValidationUtility
                    .IsRunning,
                "Validate Scene View Residency",
                "Package 02 validates the Scene View driven edit-mode height-cache residency foundation.\n\nThe validation covers shader-compatible world-to-tile addressing, one-native-sample safety padding, one-tile guard expansion, world-edge/corner fitting, small-world behavior, bounded large-world scaling, active PreviewService residency metadata, local GPU memory accounting, containment queries, and persistent authoring-state safety.\n\nThe automated validation does not move the Scene View or request a different resident cache. Manual navigation checks are documented in the Package 02 README. The final PASS / FAIL / BLOCKED report is written to the Unity Console.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringSceneViewResidencyValidationUtility
                .ValidateSceneViewResidency();
        }
    }
}
