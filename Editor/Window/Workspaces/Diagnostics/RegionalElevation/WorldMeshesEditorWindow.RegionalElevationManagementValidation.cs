using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationManagementValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Node Management",
                "Validation",
                TerrainRegionalElevationManagementValidationUtility
                    .IsRunning,
                "Validate Regional Elevation Node Management",
                "Package 5 validates the production regional mutation service, StableId lookup, add/remove/edit/grid/source operations, no-op behavior, whole-world dirty sets, Undo/Redo tracking, interactive transactions, signature invariants, and protection of the real authoring state.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainRegionalElevationManagementValidationUtility
                .ValidateRegionalElevationManagement();
        }
    }
}
