using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationTriangulatedSmoothCpuValidationSettings()
    {
        GUILayout.Label(
            "Regional Elevation Triangulated Smooth CPU",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Package I6 validates the reduced-HCT CPU reference surface: exact node heights/gradients, planar precision, C1 seams, degenerate layouts, hull behavior, cache dependencies, and IDW/Linear regression safety. Smooth GPU composition remains deferred to I7.",
            MessageType.None);

        EditorGUI.BeginDisabledGroup(
            TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility.IsRunning);

        if (GUILayout.Button("Validate Triangulated Smooth CPU"))
        {
            TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility
                .ValidateTriangulatedSmoothCpu();
        }

        EditorGUI.EndDisabledGroup();
    }
}
