using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationMultiSelectValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Multi-Select",
                "Validation",
                TerrainRegionalElevationMultiSelectValidationUtility
                    .IsRunning,
                "Validate Regional Elevation Multi-Select",
                "Package 7 validation covers StableId multi-selection and primary fallback, marquee/group math, common-delta world clamping, atomic Set/Raise/Lower operations, interactive group commit/cancel/no-op semantics, selection locking, and common/mixed elevation summaries. Actual Scene pointer hit-testing and marquee feel are verified with the README manual checklist.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainRegionalElevationMultiSelectValidationUtility
                .ValidateRegionalElevationMultiSelect();
        }
    }
}
