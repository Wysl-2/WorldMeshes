using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
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

        int totalChunks =
            worldSettings.gridWidth *
            worldSettings.gridHeight;

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
            "Preview Chunks",
            totalChunks.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Collision Mesh Assets",
            totalChunks.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Collision Scene Chunks",
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
            "Synchronizes three terrain representations beneath " +
            "WorldRoot:\n\n" +

            "Preview\n" +
            "Generated LOD0 terrain chunks used for editor preview " +
            "and future terrain editing.\n\n" +

            "Collision\n" +
            "A TerrainCollisionStreamer on the Collision root loads " +
            "generated collision Mesh assets through Addressables. " +
            "No per-chunk collision GameObjects are created by the " +
            "hierarchy generator.\n\n" +

            "Clipmap\n" +
            "The center, LOD rings, and transition stitch meshes " +
            "used by the runtime terrain renderer.",
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
