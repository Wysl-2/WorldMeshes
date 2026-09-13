using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawNodeElevationInterpolationValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Node Elevation Interpolation",
                "Validation",
                TerrainNodeElevationInterpolationValidationUtility
                    .IsRunning,
                "Validate Node Elevation Interpolation",
                "Package 3 validates the pure CPU regional-elevation field: fixed power-2 inverse-distance weighting, exact-node behavior, coincident-node averaging, global/world-edge behavior, numerical stability, ordering/StableId independence, malformed-data rejection, and side-effect-free evaluation.\n\nThe evaluator is not connected to terrain composition yet. Visible terrain remains unchanged until a later package.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainNodeElevationInterpolationValidationUtility
                .ValidateNodeElevationInterpolation();
        }
    }
}
