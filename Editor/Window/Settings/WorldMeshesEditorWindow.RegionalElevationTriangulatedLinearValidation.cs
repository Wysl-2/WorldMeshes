using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationTriangulatedLinearValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true));

        GUILayout.Label(
            "Regional Elevation Triangulated Linear CPU",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Validates Package I3 CPU barycentric interpolation, hull exterior projection, one/two/collinear policies, topology-cache reuse, IDW regression, and CPU/GPU capability boundaries.",
            MessageType.Info);

        EditorGUI.BeginDisabledGroup(
            TerrainRegionalElevationTriangulatedLinearValidationUtility.IsRunning);

        if (GUILayout.Button("Validate Triangulated Linear CPU"))
        {
            TerrainRegionalElevationTriangulatedLinearValidationUtility
                .ValidateTriangulatedLinearCpu();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndVertical();
    }
}
