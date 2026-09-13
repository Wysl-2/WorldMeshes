using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationSceneToolValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Scene Tool",
                "Validation",
                TerrainRegionalElevationSceneToolValidationUtility
                    .IsRunning,
                "Validate Regional Elevation Scene Tool",
                "Package 6 validation covers Scene coordinate mapping, tool-level world clamping, single StableId selection, Package 5 interactive XZ/elevation transactions, whole-world live-preview dirty classification, commit/cancel/no-op semantics, and tool lifecycle cancellation. Actual pointer hit-testing and handle feel are verified with the README manual SceneView checklist.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainRegionalElevationSceneToolValidationUtility
                .ValidateRegionalElevationSceneTool();
        }
    }
}
