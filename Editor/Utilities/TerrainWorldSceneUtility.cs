using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * Shared editor-side discovery helpers for the generated WorldMeshes
 * scene hierarchy.
 *
 * This utility deliberately does not create, move, or mutate scene
 * objects. It only resolves the authoritative WorldRoot / Clipmap /
 * Collision hierarchy and reports ambiguous scene state.
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

    public const string CollisionRootName =
        "Collision";

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

    public static bool TryFindClipmapRoot(
        Scene scene,
        out Transform clipmapRoot,
        out string errorMessage
    )
    {
        return
            TryFindUniqueWorldChild(
                scene,
                ClipmapRootName,
                out clipmapRoot,
                out errorMessage
            );
    }

    // =====================================================
    // FIND ACTIVE COLLISION ROOT
    // =====================================================

    public static bool TryFindActiveCollisionRoot(
        out Transform collisionRoot,
        out string errorMessage
    )
    {
        collisionRoot =
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
            TryFindCollisionRoot(
                scene,
                out collisionRoot,
                out errorMessage
            );
    }

    // =====================================================
    // FIND COLLISION ROOT
    // =====================================================

    public static bool TryFindCollisionRoot(
        Scene scene,
        out Transform collisionRoot,
        out string errorMessage
    )
    {
        return
            TryFindUniqueWorldChild(
                scene,
                CollisionRootName,
                out collisionRoot,
                out errorMessage
            );
    }

    // =====================================================
    // SHARED DIRECT CHILD LOOKUP
    // =====================================================

    private static bool TryFindUniqueWorldChild(
        Scene scene,
        string childName,
        out Transform child,
        out string errorMessage
    )
    {
        child =
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

        int matchingChildCount =
            0;

        foreach (
            Transform candidate
            in worldRoot
        )
        {
            if (
                candidate == null
                ||
                candidate.name !=
                    childName
            )
            {
                continue;
            }

            matchingChildCount++;

            if (child == null)
            {
                child =
                    candidate;
            }
        }

        if (matchingChildCount <= 1)
        {
            return true;
        }

        child =
            null;

        errorMessage =
            $"Multiple '{childName}' objects exist directly " +
            $"under '{WorldRootName}'.\n\n" +
            "Run Setup / Repair World Hierarchy after removing or " +
            "renaming duplicate generated roots.";

        return false;
    }
}
