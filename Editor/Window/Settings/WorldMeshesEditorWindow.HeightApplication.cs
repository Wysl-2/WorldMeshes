using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawHeightApplicationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Height Application",
            EditorStyles.boldLabel
        );

        // -------------------------------------------------
        // No WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "applying or validating terrain height.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Current layout
        // -------------------------------------------------

        int totalChunks =
            worldSettings.gridWidth *
            worldSettings.gridHeight;

        int samplesPerChunkSide =
            worldSettings.lod0Resolution +
            1;

        EditorGUILayout.LabelField(
            "Terrain Chunks",
            totalChunks.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Mesh Resolution",
            $"{worldSettings.lod0Resolution} x " +
            $"{worldSettings.lod0Resolution}"
        );

        EditorGUILayout.LabelField(
            "Vertices Per Side",
            samplesPerChunkSide.ToString()
        );

        EditorGUILayout.LabelField(
            "Height Tile Grid",
            $"{worldSettings.HeightTileGridWidth} x " +
            $"{worldSettings.HeightTileGridHeight}"
        );

        // -------------------------------------------------
        // Apply
        // -------------------------------------------------

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Applies the generated heightmap data to every " +
            "LOD0 terrain chunk mesh.\n\n" +

            "Vertex normals are calculated from the global " +
            "heightfield so neighboring chunks use matching " +
            "normals along shared boundaries.",
            MessageType.Info
        );

        if (
            GUILayout.Button(
                "Apply Heightmaps to Chunk Meshes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightApplicator
                .ApplyHeightmaps(
                    worldSettings
                );
        }

        // -------------------------------------------------
        // Seam validation
        // -------------------------------------------------

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Validates every shared terrain chunk edge.\n\n" +

            "The validator compares corresponding boundary " +
            "vertex positions, heights, and normals between " +
            "neighboring chunk mesh assets.",
            MessageType.Info
        );

        if (
            GUILayout.Button(
                "Validate Chunk Mesh Seams",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainMeshSeamValidator
                .ValidateMeshSeams(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }
}
