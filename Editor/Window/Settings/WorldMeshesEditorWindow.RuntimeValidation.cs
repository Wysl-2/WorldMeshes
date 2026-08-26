using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRuntimeValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Runtime validation is development tooling only.\n\n" +
            "Height-cache validation performs a full GPU readback " +
            "and exact comparison against the currently loaded " +
            "source heightmap tiles.\n\n" +
            "It never runs automatically during normal streaming.",
            MessageType.Info
        );

        // =====================================================
        // PLAY MODE REQUIRED
        // =====================================================

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode before validating the runtime " +
                "height cache.",
                MessageType.Warning
            );

            EditorGUI.BeginDisabledGroup(
                true
            );

            GUILayout.Button(
                "Validate Current Height Cache",
                GUILayout.ExpandWidth(true)
            );

            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();

            return;
        }

        // =====================================================
        // VALIDATOR
        // =====================================================

        TerrainHeightmapCacheValidator validator =
            FindRuntimeHeightmapCacheValidator();

        if (validator == null)
        {
            EditorGUILayout.HelpBox(
                "TerrainHeightmapCacheValidator was not found on " +
                "WorldRoot/Clipmap.\n\n" +
                "Exit Play Mode and run Sync World Hierarchy.",
                MessageType.Warning
            );

            EditorGUI.BeginDisabledGroup(
                true
            );

            GUILayout.Button(
                "Validate Current Height Cache",
                GUILayout.ExpandWidth(true)
            );

            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();

            return;
        }

        // =====================================================
        // CURRENT STATUS
        // =====================================================

        if (validator.IsValidating)
        {
            EditorGUILayout.HelpBox(
                "Height-cache validation is currently running.",
                MessageType.Info
            );
        }
        else if (
            validator.HasValidatedCurrentCache
        )
        {
            if (validator.LastValidationPassed)
            {
                EditorGUILayout.HelpBox(
                    "The current height cache passed validation.",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "The current height cache failed validation. " +
                    "See the Console for details.",
                    MessageType.Error
                );
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "The current height cache has not been manually " +
                "validated.",
                MessageType.None
            );
        }

        // =====================================================
        // VALIDATE
        // =====================================================

        EditorGUI.BeginDisabledGroup(
            validator.IsValidating
        );

        if (
            GUILayout.Button(
                "Validate Current Height Cache",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            validator.BeginValidation();

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndVertical();
    }

    private TerrainHeightmapCacheValidator
        FindRuntimeHeightmapCacheValidator()
    {
        GameObject worldRoot =
            GameObject.Find(
                TerrainWorldHierarchyGenerator
                    .WorldRootName
            );

        if (worldRoot == null)
        {
            return null;
        }

        Transform clipmapRoot =
            worldRoot.transform.Find(
                TerrainWorldHierarchyGenerator
                    .ClipmapRootName
            );

        if (clipmapRoot == null)
        {
            return null;
        }

        return
            clipmapRoot
                .GetComponent<TerrainHeightmapCacheValidator>();
    }
}
