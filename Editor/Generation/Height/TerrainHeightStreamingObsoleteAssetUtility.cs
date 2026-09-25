using UnityEditor;
using UnityEngine;

internal static class TerrainHeightStreamingObsoleteAssetUtility
{
    internal static int RemoveUnexpectedStreamingAssets(
        WorldSettings worldSettings
    )
    {
        if (
            worldSettings == null
            || !AssetDatabase.IsValidFolder(
                TerrainRuntimeHeightAssetUtility.HeightmapStreamingFolder
            )
        )
        {
            return 0;
        }

        int removed = 0;
        string[] guids = AssetDatabase.FindAssets(
            "t:Texture2D",
            new[] { TerrainRuntimeHeightAssetUtility.HeightmapStreamingFolder }
        );

        for (int index = 0; index < guids.Length; index++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[index]);

            if (
                !TerrainRuntimeHeightAssetUtility.TryGetStreamingHeightTileIdentity(
                    path,
                    out int stride,
                    out int tileX,
                    out int tileZ
                )
            )
            {
                continue;
            }

            bool expected =
                TerrainHeightStreamingPyramidPolicy.IsDerivedStrideSupported(
                    worldSettings,
                    stride
                )
                && tileX >= 0
                && tileZ >= 0
                && tileX < worldSettings.HeightTileGridWidth
                && tileZ < worldSettings.HeightTileGridHeight;

            if (!expected && AssetDatabase.DeleteAsset(path))
            {
                removed++;
            }
        }

        return removed;
    }
}
