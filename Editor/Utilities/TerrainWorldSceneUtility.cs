using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * Shared editor-side discovery helpers for the generated WorldMeshes
 * scene hierarchy.
 *
 * This utility deliberately does not create, move, or mutate scene
 * objects. It only resolves the authoritative WorldRoot / Clipmap
 * hierarchy and reports ambiguous scene state.
 *
 * The current height-preview service uses it now. The upcoming
 * TerrainAuthoringSceneViewController can use the same lookup path
 * without duplicating hierarchy traversal logic.
 */
public static class TerrainWorldSceneUtility
{
    // =====================================================
    // GENERATED ROOT NAMES
    // =====================================================

    public const string WorldRootName =
        "WorldRoot";

    public const string ClipmapRootName =
        "Clipmap";

    // =====================================================
    // ACTIVE SCENE
    // =====================================================

    public static bool TryGetActiveScene(
        out Scene scene,
        out string errorMessage
    )
    {
        scene =
            SceneManager.GetActiveScene();

        errorMessage =
            "";

        if (
            !scene.IsValid()
            ||
            !scene.isLoaded
        )
        {
            errorMessage =
                "No valid loaded active scene is available.";

            return false;
        }

        return true;
    }

    // =====================================================
    // FIND ACTIVE WORLD ROOT
    // =====================================================

    public static bool TryFindActiveWorldRoot(
        out Transform worldRoot,
        out string errorMessage
    )
    {
        worldRoot =
            null;

        if (
            !TryGetActiveScene(
                out Scene scene,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            TryFindWorldRoot(
                scene,
                out worldRoot,
                out errorMessage
            );
    }

    // =====================================================
    // FIND WORLD ROOT
    // =====================================================

    /*
     * Returns true when the scene lookup itself is valid.
     *
     * A missing WorldRoot is not an error:
     *
     *     true + worldRoot == null
     *
     * means the hierarchy simply has not been generated yet.
     *
     * Duplicate WorldRoot objects are treated as an error because
     * editor services must never guess which generated world owns
     * terrain authoring state.
     */
    public static bool TryFindWorldRoot(
        Scene scene,
        out Transform worldRoot,
        out string errorMessage
    )
    {
        worldRoot =
            null;

        errorMessage =
            "";

        if (
            !scene.IsValid()
            ||
            !scene.isLoaded
        )
        {
            errorMessage =
                "The requested scene is invalid or is not loaded.";

            return false;
        }

        int matchingRootCount =
            0;

        foreach (
            GameObject rootObject
            in scene.GetRootGameObjects()
        )
        {
            if (
                rootObject == null
                ||
                rootObject.name !=
                    WorldRootName
            )
            {
                continue;
            }

            matchingRootCount++;

            if (worldRoot == null)
            {
                worldRoot =
                    rootObject.transform;
            }
        }

        if (matchingRootCount <= 1)
        {
            return true;
        }

        worldRoot =
            null;

        errorMessage =
            $"Multiple '{WorldRootName}' objects exist in " +
            "the active scene.\n\n" +
            "Remove or rename duplicate WorldRoot objects before " +
            "using WorldMeshes terrain authoring tools.";

        return false;
    }

    // =====================================================
    // FIND ACTIVE CLIPMAP ROOT
    // =====================================================

    public static bool TryFindActiveClipmapRoot(
        out Transform clipmapRoot,
        out string errorMessage
    )
    {
        clipmapRoot =
            null;

        if (
            !TryGetActiveScene(
                out Scene scene,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            TryFindClipmapRoot(
                scene,
                out clipmapRoot,
                out errorMessage
            );
    }

    // =====================================================
    // FIND CLIPMAP ROOT
    // =====================================================

    /*
     * Returns true when the hierarchy lookup is unambiguous.
     *
     * Missing WorldRoot / Clipmap is represented by:
     *
     *     true + clipmapRoot == null
     *
     * Duplicate generated roots are an error.
     */
    public static bool TryFindClipmapRoot(
        Scene scene,
        out Transform clipmapRoot,
        out string errorMessage
    )
    {
        clipmapRoot =
            null;

        errorMessage =
            "";

        if (
            !TryFindWorldRoot(
                scene,
                out Transform worldRoot,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (worldRoot == null)
        {
            return true;
        }

        int matchingClipmapCount =
            0;

        foreach (
            Transform child
            in worldRoot
        )
        {
            if (
                child == null
                ||
                child.name !=
                    ClipmapRootName
            )
            {
                continue;
            }

            matchingClipmapCount++;

            if (clipmapRoot == null)
            {
                clipmapRoot =
                    child;
            }
        }

        if (matchingClipmapCount <= 1)
        {
            return true;
        }

        clipmapRoot =
            null;

        errorMessage =
            $"Multiple '{ClipmapRootName}' objects exist directly " +
            $"under '{WorldRootName}'.\n\n" +
            "Run Sync World Hierarchy after removing or renaming " +
            "duplicate generated clipmap roots.";

        return false;
    }
}
