using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawGenerationStateSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Generation State",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings to view " +
                "terrain generation state.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Current states
        // -------------------------------------------------

        TerrainGenerationStateUtility.GenerationStatus
            chunkStatus =
                TerrainGenerationStateUtility
                    .GetChunkMeshStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            heightmapStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            applicationStatus =
                TerrainGenerationStateUtility
                    .GetHeightApplicationStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        // -------------------------------------------------
        // Display
        // -------------------------------------------------

        EditorGUILayout.LabelField(
            "Chunk Meshes",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    chunkStatus
                )
        );

        EditorGUILayout.LabelField(
            "Heightmaps",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    heightmapStatus
                )
        );

        string applicationLabel =
            applicationStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.NotGenerated

                ? "Not Applied"

                : TerrainGenerationStateUtility
                    .GetStatusLabel(
                        applicationStatus
                    );

        EditorGUILayout.LabelField(
            "Height Applied",
            applicationLabel
        );

        EditorGUILayout.LabelField(
            "Collision Meshes",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    collisionStatus
                )
        );

        // -------------------------------------------------
        // Revision information
        // -------------------------------------------------

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Chunk Revision",
            worldSettings
                .chunkMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Heightmap Revision",
            worldSettings
                .heightmapGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Applied Chunk Revision",
            worldSettings
                .appliedChunkMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Applied Height Revision",
            worldSettings
                .appliedHeightmapGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Collision Revision",
            worldSettings
                .collisionMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Collision Height Source",
            worldSettings
                .collisionSourceHeightmapGenerationRevision
                .ToString()
        );

        // -------------------------------------------------
        // Explanation
        // -------------------------------------------------

        GUILayout.Space(5f);

        if (
            applicationStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "The generated terrain meshes do not contain " +
                "the current combination of chunk-mesh and " +
                "heightmap revisions.\n\n" +

                "Apply the current heightmaps to the chunk " +
                "meshes again.",
                MessageType.Warning
            );
        }
        else if (
            chunkStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "Chunk meshes are out of date with the " +
                "current WorldSettings or LOD0 base mesh.",
                MessageType.Warning
            );
        }
        else if (
            heightmapStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "Heightmaps are out of date with the current " +
                "world or height-generation settings.",
                MessageType.Warning
            );
        }
        else if (
            chunkStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            heightmapStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            applicationStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            EditorGUILayout.HelpBox(
                "The LOD0 terrain generation pipeline is " +
                "current.",
                MessageType.Info
            );
        }

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Generation State tracks which successful " +
            "generation revisions produced the current " +
            "terrain.\n\n" +

            "The validation tools remain responsible for " +
            "checking the actual generated asset contents.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
