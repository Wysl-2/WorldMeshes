using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
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

        TerrainAuthoringHeightManifest authoringManifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        worldSettings,
                        terrainAuthoringData
                    );

        TerrainGenerationStateUtility.GenerationStatus
            heightmapStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        EditorGUILayout.LabelField(
            "Authoring Heightfield",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    authoringStatus
                )
        );

        EditorGUILayout.LabelField(
            "Runtime Heightmaps",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    heightmapStatus
                )
        );

        EditorGUILayout.LabelField(
            "Collision Meshes",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    collisionStatus
                )
        );

        // =================================================
        // REVISIONS
        // =================================================

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Authoring Revision",
            terrainAuthoringData != null
                ? terrainAuthoringData
                    .authoringRevision
                    .ToString()
                : "-"
        );

        EditorGUILayout.LabelField(
            "Committed Height Revision",
            authoringManifest != null
                ? authoringManifest
                    .committedHeightRevision
                    .ToString()
                : "-"
        );

        EditorGUILayout.LabelField(
            "Runtime Heightmap Revision",
            worldSettings
                .heightmapGenerationRevision
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

        // =================================================
        // WARNINGS
        // =================================================

        GUILayout.Space(5f);

        if (
            authoringStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            EditorGUILayout.HelpBox(
                "The committed authoring heightfield is not " +
                "current. Reinitialize it before compiling " +
                "runtime heightmaps.",
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
                "Runtime heightmaps are out of date with the " +
                "current authored terrain or heightfield layout.",
                MessageType.Warning
            );
        }
        else if (
            collisionStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "Collision meshes are out of date with the " +
                "current runtime heightmaps or collision settings.",
                MessageType.Warning
            );
        }
        else if (
            heightmapStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            collisionStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            EditorGUILayout.HelpBox(
                "The generated runtime terrain data is current.",
                MessageType.Info
            );
        }


        GUILayout.EndVertical();
    }
}
