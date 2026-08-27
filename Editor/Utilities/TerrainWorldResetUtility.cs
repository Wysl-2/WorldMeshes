using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TerrainWorldResetUtility
{
    // =====================================================
    // GENERATED ASSET PATHS
    // =====================================================

    private const string HeightmapRootFolder =
        WorldMeshesPaths.GeneratedHeightmaps;

    private const string ClipmapMeshFolder =
        WorldMeshesPaths.GeneratedClipmapMeshes;

    private const string CollisionMeshFolder =
        WorldMeshesPaths.GeneratedCollisionMeshes;

    /*
     * Legacy paths retained only so Reset Generated World can
     * clean projects created by the removed LOD0 preview system.
     */
    private const string LegacyBaseMeshFolder =
        WorldMeshesPaths.GeneratedMeshes +
        "/Base";

    private const string LegacyChunkMeshFolder =
        WorldMeshesPaths.GeneratedMeshes +
        "/Chunks";

    // =====================================================
    // RESET GENERATED WORLD
    // =====================================================

    public static void ResetGeneratedWorld(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot reset generated world: " +
                "WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Generated world reset must be performed " +
                "outside Play Mode."
            );

            return;
        }

        bool confirmed =
            EditorUtility.DisplayDialog(
                "Reset Generated World",

                "This will permanently delete derived runtime " +
                "terrain data:\n\n" +

                "• Generated clipmap mesh assets\n" +
                "• Generated runtime heightmap tiles + manifest\n" +
                "• Generated collision meshes + runtime data\n" +
                "• The generated WorldRoot scene hierarchy\n" +
                "• Stored runtime generation state\n\n" +

                "Legacy Base/ and Chunks/ preview-mesh folders " +
                "will also be removed if they still exist.\n\n" +

                "Authoring/, Configuration/, Materials/, and " +
                "Shaders/ are preserved.",

                "Reset Generated World",
                "Cancel"
            );

        if (!confirmed)
        {
            return;
        }

        bool removedWorldRoot =
            false;

        bool removedHeightmaps =
            false;

        bool removedClipmapMeshes =
            false;

        bool removedCollisionMeshes =
            false;

        bool removedLegacyBaseMeshes =
            false;

        bool removedLegacyChunkMeshes =
            false;

        // =================================================
        // SCENE HIERARCHY
        // =================================================

        Scene scene =
            SceneManager.GetActiveScene();

        if (
            scene.IsValid()
            &&
            scene.isLoaded
        )
        {
            foreach (
                GameObject rootObject
                in scene.GetRootGameObjects()
            )
            {
                if (
                    rootObject == null
                    ||
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

        // =================================================
        // GENERATED RUNTIME HEIGHTMAPS
        // =================================================

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

        // =================================================
        // GENERATED CLIPMAP GEOMETRY
        // =================================================

        if (
            AssetDatabase.IsValidFolder(
                ClipmapMeshFolder
            )
        )
        {
            removedClipmapMeshes =
                AssetDatabase.DeleteAsset(
                    ClipmapMeshFolder
                );
        }

        // =================================================
        // GENERATED COLLISION DATA
        // =================================================

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

        // =================================================
        // LEGACY PREVIEW DATA
        // =================================================

        if (
            AssetDatabase.IsValidFolder(
                LegacyBaseMeshFolder
            )
        )
        {
            removedLegacyBaseMeshes =
                AssetDatabase.DeleteAsset(
                    LegacyBaseMeshFolder
                );
        }

        if (
            AssetDatabase.IsValidFolder(
                LegacyChunkMeshFolder
            )
        )
        {
            removedLegacyChunkMeshes =
                AssetDatabase.DeleteAsset(
                    LegacyChunkMeshFolder
                );
        }

        // =================================================
        // STATE
        // =================================================

        TerrainGenerationStateUtility
            .ResetGeneratedState(
                worldSettings
            );

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject =
            worldSettings;

        Debug.Log(
            "Generated world reset complete.\n\n" +
            "Removed Derived Data:\n" +
            $"WorldRoot: {removedWorldRoot}\n" +
            $"Runtime Heightmaps: {removedHeightmaps}\n" +
            $"Clipmap Meshes: {removedClipmapMeshes}\n" +
            $"Collision Data: {removedCollisionMeshes}\n\n" +
            "Legacy Preview Cleanup:\n" +
            $"Base Mesh Folder: {removedLegacyBaseMeshes}\n" +
            $"Chunk Mesh Folder: {removedLegacyChunkMeshes}\n\n" +
            "Preserved:\n" +
            "Authoring/\n" +
            "Configuration/\n" +
            "Materials/\n" +
            "Shaders/"
        );
    }
}
