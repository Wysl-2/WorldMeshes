using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationInterpolationValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Interpolation Mode",
                "Validation",
                TerrainRegionalElevationInterpolationModeValidationUtility
                    .IsRunning,
                "Validate Interpolation Mode Foundation",
                "Package I1 validation covers serialized enum/default compatibility, unchanged IDW CPU results, mode-aware deterministic signatures/snapshots, service transaction/no-op/Undo/Redo behavior, and explicit rejection of invalid or not-yet-implemented interpolation during CPU/GPU preparation.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainRegionalElevationInterpolationModeValidationUtility
                .ValidateInterpolationModeFoundation();
        }
    }
}
