using UnityEditor;
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
            "Persistent Preview Chunks",
            "0"
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

        EditorGUILayout.HelpBox(
            "WorldRoot now contains two terrain systems:\n\n" +

            "Clipmap\n" +
            "The single visual terrain representation. It is " +
            "used by the runtime renderer and will also be used " +
            "by the edit-mode terrain authoring preview.\n\n" +

            "Collision\n" +
            "A TerrainCollisionStreamer and fixed collider pool " +
            "load generated collision meshes at runtime.\n\n" +

            "The old WorldRoot/Preview branch and generated LOD0 " +
            "chunk renderers are obsolete. Sync World Hierarchy " +
            "automatically removes an existing legacy Preview " +
            "branch from the active scene.",
            MessageType.Info
        );

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Sync World Hierarchy",
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
}
