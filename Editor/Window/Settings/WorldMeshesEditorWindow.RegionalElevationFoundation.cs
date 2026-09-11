using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRegionalElevationFoundationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Regional Elevation Foundation",
            EditorStyles.boldLabel
        );

        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (authoringData == null)
        {
            EditorGUILayout.HelpBox(
                "TerrainAuthoringData could not be loaded.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        TerrainRegionalElevationSource regionalSource =
            authoringData.RegionalElevationSource;

        EditorGUILayout.LabelField(
            "Regional Source",
            regionalSource != null
                ? regionalSource.GetType().Name
                : "None"
        );

        TerrainNodeElevationSource nodeSource =
            regionalSource as
            TerrainNodeElevationSource;

        if (nodeSource != null)
        {
            EditorGUILayout.LabelField(
                "Nodes",
                nodeSource.NodeCount.ToString()
            );

            bool identitiesValid =
                nodeSource.TryValidateNodeStableIds(
                    out string identityError
                );

            bool outputValid =
                nodeSource.TryValidateOutputData(
                    out string outputError
                );

            EditorGUILayout.LabelField(
                "Stable IDs",
                identitiesValid
                    ? "Valid"
                    : "Needs Repair"
            );

            EditorGUILayout.LabelField(
                "Output Data",
                outputValid
                    ? "Valid"
                    : "Invalid"
            );

            if (!identitiesValid)
            {
                EditorGUILayout.HelpBox(
                    identityError,
                    MessageType.Warning
                );
            }

            if (!outputValid)
            {
                EditorGUILayout.HelpBox(
                    outputError,
                    MessageType.Error
                );
            }
        }

        bool validationRunning =
            TerrainRegionalElevationFoundationValidationUtility
                .IsRunning;

        EditorGUILayout.LabelField(
            "Validation",
            validationRunning
                ? "Running"
                : "Ready"
        );

        GUILayout.Space(
            5f
        );

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
                "Validate Regional Elevation Foundation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRegionalElevationFoundationValidationUtility
                .ValidateRegionalElevationFoundation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Package 1 defines persistent regional-elevation source and " +
            "node data only. Regional elevation does not change terrain " +
            "yet.\n\n" +

            "The validator uses temporary assets to check null-source " +
            "compatibility, SerializeReference persistence, node ordering, " +
            "stable-ID generation/repair, malformed-data rejection, and " +
            "overall-signature behavior.\n\n" +

            "The final validation report is written to the Unity Console.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
