using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TerrainRuntimeHierarchyReadinessResult
{
    public int WorldRootCount { get; internal set; }
    public int ClipmapRootCount { get; internal set; }
    public int CollisionRootCount { get; internal set; }
    public int BoundsControllerCount { get; internal set; }
    public int HeightStreamerCount { get; internal set; }
    public int CollisionStreamerCount { get; internal set; }
    public int CollisionColliderPoolCount { get; internal set; }

    public bool IsReady { get; internal set; }
    public bool RepairRequired => !IsReady;
    public string ErrorMessage { get; internal set; }
}

public static class TerrainRuntimeHierarchyReadinessUtility
{
    public static bool TryGetReadiness(
        out string message
    )
    {
        TerrainRuntimeHierarchyReadinessResult result =
            Evaluate();

        message = result.ErrorMessage ?? "";
        return result.IsReady;
    }

    public static TerrainRuntimeHierarchyReadinessResult Evaluate()
    {
        TerrainRuntimeHierarchyReadinessResult result =
            new TerrainRuntimeHierarchyReadinessResult
            {
                ErrorMessage = ""
            };

        if (
            !TerrainWorldSceneUtility.TryGetActiveScene(
                out Scene scene,
                out string sceneError
            )
        )
        {
            result.ErrorMessage = sceneError;
            return result;
        }

        result.WorldRootCount =
            CountSceneRoots(
                scene,
                TerrainWorldSceneUtility.WorldRootName
            );

        if (
            !TerrainWorldSceneUtility.TryFindWorldRoot(
                scene,
                out Transform worldRoot,
                out string worldError
            )
        )
        {
            result.ErrorMessage = worldError;
            return result;
        }

        if (worldRoot == null)
        {
            result.ErrorMessage =
                "WorldRoot is missing from the active scene.";
            return result;
        }

        result.ClipmapRootCount =
            CountDirectChildren(
                worldRoot,
                TerrainWorldSceneUtility.ClipmapRootName
            );

        result.CollisionRootCount =
            CountDirectChildren(
                worldRoot,
                TerrainWorldSceneUtility.CollisionRootName
            );

        if (
            !TerrainWorldSceneUtility.TryFindClipmapRoot(
                scene,
                out Transform clipmapRoot,
                out string clipmapError
            )
        )
        {
            result.ErrorMessage = clipmapError;
            return result;
        }

        if (clipmapRoot == null)
        {
            result.ErrorMessage =
                "WorldRoot/Clipmap is missing from the active scene.";
            return result;
        }

        result.BoundsControllerCount =
            clipmapRoot
                .GetComponents<TerrainClipmapBoundsController>()
                .Length;

        if (result.BoundsControllerCount != 1)
        {
            result.ErrorMessage =
                "WorldRoot/Clipmap must contain exactly one TerrainClipmapBoundsController.";
            return result;
        }

        result.HeightStreamerCount =
            clipmapRoot
                .GetComponents<TerrainHeightmapStreamer>()
                .Length;

        if (result.HeightStreamerCount != 1)
        {
            result.ErrorMessage =
                "WorldRoot/Clipmap must contain exactly one TerrainHeightmapStreamer.";
            return result;
        }

        if (
            !TerrainWorldSceneUtility.TryFindCollisionRoot(
                scene,
                out Transform collisionRoot,
                out string collisionError
            )
        )
        {
            result.ErrorMessage = collisionError;
            return result;
        }

        if (collisionRoot == null)
        {
            result.ErrorMessage =
                "WorldRoot/Collision is missing from the active scene.";
            return result;
        }

        result.CollisionStreamerCount =
            collisionRoot
                .GetComponents<TerrainCollisionStreamer>()
                .Length;

        if (result.CollisionStreamerCount != 1)
        {
            result.ErrorMessage =
                "WorldRoot/Collision must contain exactly one TerrainCollisionStreamer.";
            return result;
        }

        result.CollisionColliderPoolCount =
            collisionRoot
                .GetComponents<TerrainCollisionColliderPool>()
                .Length;

        if (result.CollisionColliderPoolCount != 1)
        {
            result.ErrorMessage =
                "WorldRoot/Collision must contain exactly one TerrainCollisionColliderPool.";
            return result;
        }

        result.IsReady = true;
        return result;
    }

    private static int CountSceneRoots(
        Scene scene,
        string name
    )
    {
        int count = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == name)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountDirectChildren(
        Transform parent,
        string name
    )
    {
        int count = 0;

        if (parent == null)
        {
            return count;
        }

        foreach (Transform child in parent)
        {
            if (child != null && child.name == name)
            {
                count++;
            }
        }

        return count;
    }
}
