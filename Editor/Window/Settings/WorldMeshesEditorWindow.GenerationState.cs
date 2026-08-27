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

        // =================================================
        // AUTHORING
        // =================================================

        string authoringState =
            "Not Initialized";

        if (authoringManifest != null)
        {
            if (!authoringManifest.isComplete)
            {
                authoringState =
                    "Incomplete";
            }
            else if (
                authoringManifest.manifestVersion !=
                TerrainAuthoringHeightManifest
                    .CurrentVersion
            )
            {
                authoringState =
                    "Out of Date";
            }
            else
            {
                authoringState =
                    "Complete";
            }
        }

        EditorGUILayout.LabelField(
            "Authoring Heightfield",
            authoringState
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
            authoringManifest == null
            ||
            !authoringManifest.isComplete
            ||
            authoringManifest.manifestVersion !=
                TerrainAuthoringHeightManifest
                    .CurrentVersion
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

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Visual terrain is no longer represented by " +
            "generated LOD0 chunk meshes. The clipmap is the " +
            "single visual terrain representation for runtime " +
            "and the upcoming edit-mode authoring preview.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
