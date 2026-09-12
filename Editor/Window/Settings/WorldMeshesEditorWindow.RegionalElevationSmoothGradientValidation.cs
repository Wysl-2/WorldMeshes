using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationSmoothGradientValidationSettings()
    {
        GUILayout.Label(
            "Regional Elevation Smooth Gradients",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Package I5 derives deterministic per-topology-vertex dh/dx and dh/dz values from I2 adjacency. Validation covers planar exactness, degenerate layouts, conditioning fallback, cache dependencies, identity independence, and real-state safety without enabling Smooth interpolation.",
            MessageType.None);

        EditorGUI.BeginDisabledGroup(
            TerrainRegionalElevationSmoothGradientValidationUtility.IsRunning);

        if (GUILayout.Button("Validate Smooth Gradients"))
        {
            TerrainRegionalElevationSmoothGradientValidationUtility
                .ValidateSmoothGradients();
        }

        EditorGUI.EndDisabledGroup();
    }
}
