using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationCompositionValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Composition",
                "Validation",
                TerrainRegionalElevationCompositionValidationUtility
                    .IsRunning,
                "Validate Regional Elevation Composition",
                "Package 4 validates the shared regional GPU pass, CPU/GPU IDW parity, absolute regional semantics, regional-before-modifier ordering, Flat-only source-mode support, conservative preview ranges, whole-world preview/runtime scheduling, and persistent authoring-state safety.\n\nThe validator uses transient GPU fixtures and temporary in-memory authoring data. It does not rewrite committed authoring height tiles or the real regional node layout.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainRegionalElevationCompositionValidationUtility
                .ValidateRegionalElevationComposition();
        }
    }
}
