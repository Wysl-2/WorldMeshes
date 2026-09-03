using UnityEditor;
using UnityEngine;

/*
 * Configures both synchronized runtime terrain streams before optionally
 * building Addressables player content.
 */
public static class TerrainRuntimeAddressablesUtility
{
    public static bool PrepareTerrainStreamingAssets()
    {
        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot prepare runtime terrain Addressables because WorldSettings is unavailable."
            );

            return false;
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot prepare runtime terrain Addressables because Runtime Heightmaps are not current."
            );

            return false;
        }

        if (
            TerrainGenerationStateUtility
                .GetSurfaceMaskStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot prepare runtime terrain Addressables because Runtime Surface Masks are not current."
            );

            return false;
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskManifestPath
                );

        if (
            heightManifest == null
            ||
            !heightManifest.isComplete
        )
        {
            Debug.LogError(
                "Cannot prepare runtime terrain Addressables because the runtime heightmap manifest is missing or incomplete."
            );

            return false;
        }

        if (
            surfaceManifest == null
            ||
            !surfaceManifest.isComplete
        )
        {
            Debug.LogError(
                "Cannot prepare runtime terrain Addressables because the surface-mask manifest is missing or incomplete.\n\n" +
                "Bake Runtime Surface Masks first."
            );

            return false;
        }

        if (
            !TerrainHeightmapAddressablesUtility
                .PrepareHeightmapTilesForRuntime(
                    heightManifest
                )
        )
        {
            return false;
        }

        if (
            !TerrainSurfaceMaskAddressablesUtility
                .PrepareSurfaceMaskTilesForRuntime(
                    surfaceManifest
                )
        )
        {
            return false;
        }

        return true;
    }

    public static bool PrepareAndBuildTerrainStreamingAssets()
    {
        if (!PrepareTerrainStreamingAssets())
        {
            return false;
        }

        return
            TerrainHeightmapAddressablesUtility
                .BuildAddressablesContent();
    }
}
