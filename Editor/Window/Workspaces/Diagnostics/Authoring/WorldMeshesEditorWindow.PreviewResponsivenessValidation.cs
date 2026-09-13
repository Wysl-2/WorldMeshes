using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawPreviewResponsivenessValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Preview Responsiveness Validation",
                "Status",
                TerrainAuthoringPreviewValidationUtility
                    .IsRunning,
                "Validate Preview Responsiveness",
                "Runs the Stage 10 integration validation for the incremental Height Preview cache.\n\nThe validation checks slice addressing, dirty-tile batching, dirty-region mapping, stable cache identity, incremental height-range metadata, hierarchy-only rebinding, and authoring/runtime signature behavior.\n\nThe final PASS / FAIL / BLOCKED report is written to the Unity Console. The validation does not modify persistent terrain authoring data.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringPreviewValidationUtility
                .ValidatePreviewResponsiveness();
        }
    }
}
