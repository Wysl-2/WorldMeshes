using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawChunkMeshSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Chunk Meshes",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "synchronizing chunk meshes.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        int totalChunks =
            worldSettings.gridWidth *
            worldSettings.gridHeight;

        EditorGUILayout.LabelField(
            "World Grid",
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}"
        );

        EditorGUILayout.LabelField(
            "Chunk Size",
            worldSettings.chunkSize.ToString()
        );

        EditorGUILayout.LabelField(
            "LOD0 Resolution",
            worldSettings.lod0Resolution.ToString()
        );

        EditorGUILayout.LabelField(
            "Required Meshes",
            totalChunks.ToString("N0")
        );

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Synchronizes generated chunk meshes with the " +
            "current WorldSettings and LOD0 base mesh.\n\n" +
            "If the chunk size or LOD0 resolution has changed " +
            "since the previous successful synchronization, " +
            "all existing required chunk meshes are rebuilt.",
            MessageType.Info
        );

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Sync Chunk Meshes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainChunkMeshGenerator
                .SyncChunkMeshes(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }
}
