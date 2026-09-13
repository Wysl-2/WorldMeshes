using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationTriangulatedSmoothGpuValidationSettings()
    {
        GUILayout.Label(
            "Regional Elevation Triangulated Smooth GPU",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Package I7 validation compares production Triangulated Smooth GPU composition against the Package I6 CPU reference, including node/interior/seam/hull parity, degenerate layouts, cache invalidation and reuse, production composition, conservative elevation range, and IDW/Linear regression safety.",
            MessageType.None);

        EditorGUI.BeginDisabledGroup(
            TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility.IsRunning);

        if (GUILayout.Button("Validate Triangulated Smooth GPU"))
        {
            TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility
                .ValidateTriangulatedSmoothGpu();
        }

        EditorGUI.EndDisabledGroup();
    }
}
