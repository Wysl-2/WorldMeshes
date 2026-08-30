using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawModifierDataFoundationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Modifier Data Foundation",
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

        EditorGUILayout.LabelField(
            "Modifiers",
            authoringData
                .HeightModifierCount
                .ToString()
        );

        bool identitiesValid =
            authoringData
                .TryValidateModifierStableIds(
                    out string identityError
                );

        EditorGUILayout.LabelField(
            "Stable IDs",
            identitiesValid
                ? "Valid"
                : "Needs Repair"
        );

        if (!identitiesValid)
        {
            EditorGUILayout.HelpBox(
                identityError,
                MessageType.Warning
            );
        }

        bool validationRunning =
            TerrainModifierDataValidationUtility
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
                "Validate Modifier Data Foundation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainModifierDataValidationUtility
                .ValidateModifierDataFoundation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Stage 11 defines persistent modifier data only. " +
            "Terrain composition and authoring handles are not " +
            "implemented yet.\n\n" +

            "The validator creates temporary test assets, checks " +
            "SerializeReference persistence, ordering, stable IDs, " +
            "duplicate-ID repair, affected bounds, stamp references, " +
            "and overall/committed signature behavior, then deletes " +
            "the temporary assets.\n\n" +

            "The final validation report is written to the Unity Console.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
