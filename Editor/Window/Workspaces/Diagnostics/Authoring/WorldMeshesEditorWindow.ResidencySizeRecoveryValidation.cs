using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawResidencySizeRecoveryValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Residency Size Recovery",
                "Validation",
                TerrainAuthoringResidencySizeRecoveryValidationUtility
                    .IsRunning,
                "Validate Residency Size Recovery",
                "Package 03A validates resident-size health independently from coverage safety.\n\nThe validation reproduces the observed 24x24 active -> 12x12 desired regression synthetically, checks one-tile size tolerance, material over/undersize recovery, coverage-critical moves, edge fitting, small-world behavior, 24x24 -> 12x12 staged classification, rebuild target selection, size stability, live residency diagnostics, and persistent authoring-state safety.\n\nThe automated validation does not move the Scene View and does not allocate a real 24x24 GPU cache. The final PASS / FAIL / BLOCKED report is written to the Unity Console.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringResidencySizeRecoveryValidationUtility
                .ValidateResidencySizeRecovery();
        }
    }
}
