using System;
using UnityEditor;
using UnityEngine;

/*
 * Surface-stage asset residency boundary.
 *
 * Callers invoke this only after the current surface output batch has passed
 * its AssetDatabase durability checkpoint. Runtime-height source assets and
 * persisted surface Texture2D assets that are no longer referenced may then
 * be unloaded before the next bounded analysis batch begins.
 */
internal static class TerrainSurfaceBakeResidencyUtility
{
    /*
     * EditorUtility.UnloadUnusedAssetsImmediate does not treat the active
     * managed execution stack as an asset root. Keep the long-lived Surface
     * build assets explicitly rooted while the unload boundary executes.
     */
    private static UnityEngine.Object[] unloadKeepAliveRoots;

    internal static bool TryReleaseUnusedAssets(
        WorldSettings worldSettings,
        TerrainHeightmapManifest heightManifest,
        TerrainSurfaceMaskManifest surfaceManifest,
        TerrainSurfaceSettings surfaceSettings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (unloadKeepAliveRoots != null)
        {
            errorMessage =
                "Surface bake unused-asset release is already in progress.";

            return false;
        }

        unloadKeepAliveRoots =
            new UnityEngine.Object[]
            {
                worldSettings,
                heightManifest,
                surfaceManifest,
                surfaceSettings
            };

        try
        {
            using TerrainRuntimeBakePerformanceScope releasePerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    "Surface.ResidencyRelease",
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    TerrainRuntimeBakePerformanceCategory.AssetDatabase
                );

            EditorUtility
                .UnloadUnusedAssetsImmediate();
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not release unused surface bake assets.\n\n" +
                exception.Message;

            return false;
        }
        finally
        {
            unloadKeepAliveRoots =
                null;
        }

        return true;
    }
}
