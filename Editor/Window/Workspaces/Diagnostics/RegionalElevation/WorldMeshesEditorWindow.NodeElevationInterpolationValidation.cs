using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawNodeElevationInterpolationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Node Elevation Interpolation",
            EditorStyles.boldLabel
        );

        bool validationRunning =
            TerrainNodeElevationInterpolationValidationUtility
                .IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning
                ? "Running"
                : "Ready"
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            validationRunning
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Validate Node Elevation Interpolation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainNodeElevationInterpolationValidationUtility
                .ValidateNodeElevationInterpolation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package 3 validates the pure CPU regional-elevation field: " +
            "fixed power-2 inverse-distance weighting, exact-node behavior, " +
            "coincident-node averaging, global/world-edge behavior, numerical " +
            "stability, ordering/StableId independence, malformed-data " +
            "rejection, and side-effect-free evaluation.\n\n" +

            "The evaluator is not connected to terrain composition yet. " +
            "Visible terrain remains unchanged until a later package.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
