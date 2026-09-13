using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawWorldHierarchySettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "World Hierarchy",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "creating the world hierarchy.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        int clipmapLevelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        int clipmapMeshObjects =
            1
            +
            Mathf.Max(
                0,
                clipmapLevelCount - 1
            )
            *
            2;

        EditorGUILayout.LabelField(
            "World Root",
            TerrainWorldHierarchyGenerator
                .WorldRootName
        );

        EditorGUILayout.LabelField(
            "World Grid",
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}"
        );

        EditorGUILayout.LabelField(
            "Visual Terrain",
            "Clipmap"
        );

        EditorGUILayout.LabelField(
            "Clipmap Levels",
            clipmapLevelCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Clipmap Mesh Objects",
            clipmapMeshObjects.ToString()
        );

        GUILayout.Space(8f);

        DrawWorldRuntimeStreamingSettings();

        GUILayout.Space(8f);

        EditorGUILayout.HelpBox(
            "WorldRoot stores the authoritative runtime streaming " +
            "source and contains two terrain systems:\n\n" +

            "Clipmap\n" +
            "The visual terrain renderer used at runtime and by " +
            "the edit-mode terrain authoring preview.\n\n" +

            "Collision\n" +
            "The runtime collision streamer and fixed collider pool.",
            MessageType.Info
        );

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Setup / Repair World Hierarchy creates or repairs the generated WorldRoot structure and reapplies its runtime streaming source. Normal terrain edits should use Runtime > Bake Runtime Changes instead.",
            MessageType.None
        );

        if (
            GUILayout.Button(
                "Setup / Repair World Hierarchy",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainWorldHierarchyGenerator
                .SyncWorldHierarchy(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }

    // =====================================================
    // RUNTIME STREAMING
    // =====================================================

    private void DrawWorldRuntimeStreamingSettings()
    {
        GUILayout.Label(
            "Runtime Streaming",
            EditorStyles.boldLabel
        );

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveWorldRoot(
                    out Transform worldRoot,
                    out string worldRootError
                )
        )
        {
            EditorGUILayout.HelpBox(
                worldRootError,
                MessageType.Error
            );

            return;
        }

        if (worldRoot == null)
        {
            EditorGUILayout.HelpBox(
                "No generated WorldRoot exists in the active scene. " +
                "Run Setup / Repair World Hierarchy before assigning " +
                "a runtime streaming source.",
                MessageType.Info
            );

            return;
        }

        TerrainWorldRuntime worldRuntime =
            worldRoot
                .GetComponent<TerrainWorldRuntime>();

        if (worldRuntime == null)
        {
            EditorGUILayout.HelpBox(
                "WorldRoot does not contain TerrainWorldRuntime. " +
                "Run Setup / Repair World Hierarchy to create and " +
                "configure the runtime world component.",
                MessageType.Warning
            );

            return;
        }

        EditorGUI.BeginChangeCheck();

        Transform selectedStreamingSource =
            (Transform)
            EditorGUILayout.ObjectField(
                "Streaming Source",
                worldRuntime.StreamingSource,
                typeof(Transform),
                true
            );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(
                worldRuntime,
                "Set World Streaming Source"
            );

            if (
                worldRuntime.SetStreamingSource(
                    selectedStreamingSource
                )
            )
            {
                EditorUtility.SetDirty(
                    worldRuntime
                );

                EditorSceneManager.MarkSceneDirty(
                    worldRuntime.gameObject.scene
                );
            }
        }

        EditorGUILayout.HelpBox(
            "This source is authoritative for runtime clipmap and " +
            "collision following. Generated target references are " +
            "derived from it.",
            MessageType.None
        );

        if (
            GUILayout.Button(
                "Apply Streaming Source",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainWorldHierarchyGenerator
                .ApplyStreamingSource();
        }
    }
}
