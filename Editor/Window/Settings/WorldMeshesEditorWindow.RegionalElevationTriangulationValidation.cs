using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationTriangulationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true));

        GUILayout.Label(
            "Regional Elevation Triangulation",
            EditorStyles.boldLabel);

        bool validationRunning =
            TerrainRegionalElevationTriangulationValidationUtility.IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning ? "Running" : "Ready");

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            validationRunning ||
            Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button(
            "Validate Regional Elevation Triangulation",
            GUILayout.ExpandWidth(true)))
        {
            TerrainRegionalElevationTriangulationValidationUtility
                .ValidateRegionalElevationTriangulation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package I2 validation covers deterministic derived Delaunay topology, " +
            "canonical CCW triangles/edges, adjacency, convex hull ordering, " +
            "coincident-vertex policy, point containment, geometry-only cache " +
            "invalidation, IDW regression, and project-state non-mutation.",
            MessageType.Info);

        GUILayout.EndVertical();
    }
}
