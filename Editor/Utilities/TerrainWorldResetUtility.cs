using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TerrainWorldResetUtility
{
    // =====================================================
    // GENERATED ASSET PATHS
    // =====================================================

    private const string BaseMeshPath =
        WorldMeshesPaths.BaseMeshAssetPath;

    private const string ChunkMeshFolder =
        WorldMeshesPaths.GeneratedChunkMeshes;

    private const string HeightmapRootFolder =
        WorldMeshesPaths.GeneratedHeightmaps;

    private const string CollisionMeshFolder =
        WorldMeshesPaths.GeneratedCollisionMeshes;

    // =====================================================
    // RESET GENERATED WORLD
    // =====================================================

    public static void ResetGeneratedWorld(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot reset generated world: " +
                "WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Generated world reset must be performed " +
                "outside Play Mode."
            );

            return;
        }

        // -------------------------------------------------
        // Confirmation
        // -------------------------------------------------

        bool confirmed =
            EditorUtility.DisplayDialog(
                "Reset Generated World",

                "This will permanently delete all currently " +
                "generated terrain data:\n\n" +

                "• LOD0 base mesh\n" +
                "• All generated chunk mesh assets\n" +
                "• All generated heightmap tiles\n" +
                "• Heightmap generation manifest\n" +
                "• The WorldRoot scene hierarchy\n" +
                "• Stored generation synchronization state\n\n" +

                "The WorldSettings asset and all current " +
                "world, chunk, and height-generation settings " +
                "will be preserved.\n\n" +

                "This operation cannot automatically restore " +
                "deleted generated assets.",

                "Reset Generated World",
                "Cancel"
            );

        if (!confirmed)
        {
            return;
        }

        // -------------------------------------------------
        // Statistics
        // -------------------------------------------------

        bool removedWorldRoot =
            false;

        bool removedBaseMesh =
            false;

        bool removedChunkMeshes =
            false;

        bool removedHeightmaps =
            false;
        
        bool removedCollisionMeshes =
            false;

        // =====================================================
        // REMOVE GENERATED SCENE HIERARCHY
        // =====================================================

        Scene scene =
            SceneManager.GetActiveScene();

        if (
            scene.IsValid() &&
            scene.isLoaded
        )
        {
            GameObject[] rootObjects =
                scene.GetRootGameObjects();

            foreach (
                GameObject rootObject
                in rootObjects
            )
            {
                if (
                    rootObject == null ||
                    rootObject.name !=
                        TerrainWorldHierarchyGenerator
                            .WorldRootName
                )
                {
                    continue;
                }

                Object.DestroyImmediate(
                    rootObject
                );

                removedWorldRoot =
                    true;
            }

            if (removedWorldRoot)
            {
                EditorSceneManager.MarkSceneDirty(
                    scene
                );
            }
        }

        // =====================================================
        // DELETE GENERATED CHUNK MESHES
        // =====================================================

        /*
         * Delete the entire generated chunk mesh folder.
         *
         * This removes both flat and height-deformed
         * generated chunk mesh assets.
         *
         * Deleting the whole folder also guarantees that
         * stale, malformed, duplicated, or incorrectly
         * named generated chunk assets cannot survive
         * the reset.
         */

        if (
            AssetDatabase.IsValidFolder(
                ChunkMeshFolder
            )
        )
        {
            removedChunkMeshes =
                AssetDatabase.DeleteAsset(
                    ChunkMeshFolder
                );
        }

        // =====================================================
        // DELETE GENERATED HEIGHTMAP DATA
        // =====================================================

        /*
         * Delete the entire generated heightmap folder.
         *
         * This removes:
         *
         * HeightmapManifest.asset
         *
         * and
         *
         * Tiles/
         *     HeightTile_x_z.asset
         *
         * The height-generation PARAMETERS stored in
         * WorldSettings are preserved.
         */

        if (
            AssetDatabase.IsValidFolder(
                HeightmapRootFolder
            )
        )
        {
            removedHeightmaps =
                AssetDatabase.DeleteAsset(
                    HeightmapRootFolder
                );
        }
        
        // =====================================================
        // DELETE GENERATED COLLISION MESHES
        // =====================================================

                if (
                    AssetDatabase.IsValidFolder(
                        CollisionMeshFolder
                    )
                )
                {
                    removedCollisionMeshes =
                        AssetDatabase.DeleteAsset(
                            CollisionMeshFolder
                        );
                }

        // =====================================================
        // DELETE LOD0 BASE MESH
        // =====================================================

        Object baseMeshAsset =
            AssetDatabase.LoadMainAssetAtPath(
                BaseMeshPath
            );

        if (baseMeshAsset != null)
        {
            removedBaseMesh =
                AssetDatabase.DeleteAsset(
                    BaseMeshPath
                );
        }

        // =====================================================
        // RESET GENERATED STATE
        // =====================================================

        /*
         * Preserve all authoring settings:
         *
         * worldSettings.gridWidth
         * worldSettings.gridHeight
         * worldSettings.chunkSize
         * worldSettings.lod0Resolution
         *
         * worldSettings.heightTileChunkSpan
         * worldSettings.heightSeed
         * worldSettings.heightNoiseScale
         * worldSettings.heightBaseHeight
         * worldSettings.heightAmplitude
         * worldSettings.heightOctaves
         * worldSettings.heightPersistence
         * worldSettings.heightLacunarity
         *
         * Reset only the state describing generated assets.
         */

        TerrainGenerationStateUtility
            .ResetGeneratedState(
                worldSettings
            );

        // =====================================================
        // SAVE
        // =====================================================

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        AssetDatabase.SaveAssets();

        AssetDatabase.Refresh();

        // =====================================================
        // SELECTION
        // =====================================================

        Selection.activeObject =
            worldSettings;

        // =====================================================
        // COMPLETE
        // =====================================================

        Debug.Log(
            "Generated world reset complete.\n\n" +

            "Removed Generated Data:\n" +

            $"WorldRoot: " +
            $"{removedWorldRoot}\n" +

            $"LOD0 Base Mesh: " +
            $"{removedBaseMesh}\n" +

            $"Chunk Mesh Folder: " +
            $"{removedChunkMeshes}\n" +

            $"Heightmap Folder: " +
            $"{removedHeightmaps}\n\n" +
            
            $"Collision Mesh Folder: " +
            $"{removedCollisionMeshes}\n" +

            "Preserved World Settings:\n" +

            $"Grid: " +
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}\n" +

            $"Chunk Size: " +
            $"{worldSettings.chunkSize}\n" +

            $"LOD0 Resolution: " +
            $"{worldSettings.lod0Resolution}\n\n" +

            "Preserved Height Settings:\n" +

            $"Tile Chunk Span: " +
            $"{worldSettings.heightTileChunkSpan}\n" +

            $"Seed: " +
            $"{worldSettings.heightSeed}\n" +

            $"Noise Scale: " +
            $"{worldSettings.heightNoiseScale}\n" +

            $"Base Height: " +
            $"{worldSettings.heightBaseHeight}\n" +

            $"Height Amplitude: " +
            $"{worldSettings.heightAmplitude}\n" +

            $"Octaves: " +
            $"{worldSettings.heightOctaves}\n" +

            $"Persistence: " +
            $"{worldSettings.heightPersistence}\n" +

            $"Lacunarity: " +
            $"{worldSettings.heightLacunarity}\n\n" +

            "Generated synchronization state has been reset."
        );
    }
}