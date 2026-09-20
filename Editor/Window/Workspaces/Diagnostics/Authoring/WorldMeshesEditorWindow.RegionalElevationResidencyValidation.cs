using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationResidencyValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Residency",
                "Validation",
                TerrainAuthoringRegionalElevationResidencyValidationUtility
                    .IsRunning,
                "Validate Regional Elevation Residency",
                "Package 06 validates compact regional-elevation invalidation with active-resident-only preview work.\n\nThe validation covers WholeWorld logical counts without complete-world tile materialization, active-window intersection, bounded future scopes, conservative scope merging, regional/modifier dirty union, no residency expansion, Package 05 authoring-generation reuse, resident-only publication semantics, Package 03A bounded-residency regression, and live regional-residency diagnostics.\n\nExisting Regional Elevation CPU/GPU composition validators remain responsible for interpolation numerical parity and regional-before-modifier composition correctness.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringRegionalElevationResidencyValidationUtility
                .ValidateRegionalElevationResidency();
        }
    }
}
