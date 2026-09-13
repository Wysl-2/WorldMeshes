using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawNodeElevationInitializationValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Node Elevation Initialization",
                "Validation",
                TerrainNodeElevationInitializationValidationUtility
                    .IsRunning,
                "Validate Node Elevation Initialization",
                "Package 2 validates Four Corners and grid layout mathematics, world-boundary placement, initial elevation, unique StableIds, output determinism, SerializeReference persistence, source/modifier signature coexistence, and separation from committed heightfield initialization.\n\nThe validator uses temporary authoring data and does not modify the real regional elevation source or committed height tiles.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainNodeElevationInitializationValidationUtility
                .ValidateNodeElevationInitialization();
        }
    }
}
