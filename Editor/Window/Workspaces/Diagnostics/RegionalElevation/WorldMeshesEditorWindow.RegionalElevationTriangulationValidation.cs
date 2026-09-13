using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationTriangulationValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Regional Elevation Triangulation",
                "Validation",
                TerrainRegionalElevationTriangulationValidationUtility
                    .IsRunning,
                "Validate Regional Elevation Triangulation",
                "Package I2 validation covers deterministic derived Delaunay topology, canonical CCW triangles/edges, adjacency, convex hull ordering, coincident-vertex policy, point containment, geometry-only cache invalidation, IDW regression, and project-state non-mutation.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainRegionalElevationTriangulationValidationUtility
                .ValidateRegionalElevationTriangulation();
        }
    }
}
