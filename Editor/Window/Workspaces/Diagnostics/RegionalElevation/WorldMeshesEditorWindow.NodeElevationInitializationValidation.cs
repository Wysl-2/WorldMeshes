using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawNodeElevationInitializationValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Node Elevation Initialization",
            EditorStyles.boldLabel
        );

        bool validationRunning =
            TerrainNodeElevationInitializationValidationUtility
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
                "Validate Node Elevation Initialization",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainNodeElevationInitializationValidationUtility
                .ValidateNodeElevationInitialization();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Package 2 validates Four Corners and grid layout mathematics, " +
            "world-boundary placement, initial elevation, unique StableIds, " +
            "output determinism, SerializeReference persistence, source/" +
            "modifier signature coexistence, and separation from committed " +
            "heightfield initialization.\n\n" +

            "The validator uses temporary authoring data and does not modify " +
            "the real regional elevation source or committed height tiles.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
