using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationTriangulatedLinearGpuValidationSettings()
    {
        GUILayout.Label(
            "Regional Elevation Triangulated Linear GPU",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Package I4 validation compares the production Triangulated Linear GPU shader against Package I3 CPU semantics, checks topology cache behavior, verifies the existing IDW GPU path, and confirms failure boundaries remain explicit.",
            MessageType.None);

        EditorGUI.BeginDisabledGroup(
            TerrainRegionalElevationTriangulatedLinearGpuValidationUtility.IsRunning);

        if (GUILayout.Button("Validate Triangulated Linear GPU"))
        {
            TerrainRegionalElevationTriangulatedLinearGpuValidationUtility
                .ValidateTriangulatedLinearGpu();
        }

        EditorGUI.EndDisabledGroup();
    }
}
