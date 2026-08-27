using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TerrainWorldResetUtility
{
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

                "This will permanently delete all derived terrain " +
                "data under:\n\n" +

                $"{WorldMeshesPaths.Generated}\n\n" +

                "This includes generated clipmap geometry, runtime " +
                "heightmaps, collision meshes, manifests, bake " +
                "markers, and other generated terrain data.\n\n" +

                "The generated WorldRoot scene hierarchy and stored " +
                "runtime generation state will also be removed.\n\n" +

                "Authoring data, configuration, materials, and " +
                "shaders are preserved.",

                "Reset Generated World",
                "Cancel"
            );

        if (!confirmed)
        {
            return;
        }

        bool removedWorldRoot =
            RemoveWorldRoot();

        bool removedGeneratedData =
            RemoveGeneratedData();

        TerrainGenerationStateUtility
            .ResetGeneratedState(
                worldSettings
            );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject =
            worldSettings;

        Debug.Log(
            "Generated world reset complete.\n\n" +
            $"WorldRoot Removed: {removedWorldRoot}\n" +
            $"Generated Data Removed: {removedGeneratedData}\n\n" +
            "Preserved:\n" +
            "Authoring/\n" +
            "Configuration/\n" +
            "Materials/\n" +
            "Shaders/"
        );
    }

    // =====================================================
    // REMOVE WORLD ROOT
    // =====================================================

    private static bool RemoveWorldRoot()
    {
        Scene scene =
            SceneManager.GetActiveScene();

        if (
            !scene.IsValid()
            ||
            !scene.isLoaded
        )
        {
            return false;
        }

        bool removed =
            false;

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

            removed =
                true;
        }

        if (removed)
        {
            EditorSceneManager.MarkSceneDirty(
                scene
            );
        }

        return removed;
    }

    // =====================================================
    // REMOVE GENERATED DATA
    // =====================================================

    private static bool RemoveGeneratedData()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Generated
            )
        )
        {
            return false;
        }

        return
            AssetDatabase.DeleteAsset(
                WorldMeshesPaths.Generated
            );
    }
}
