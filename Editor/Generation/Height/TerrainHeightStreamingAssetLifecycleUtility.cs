using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

public static class TerrainHeightStreamingAssetLifecycleUtility
{
    public static bool EnsureTargetFolders(
        IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        try
        {
            EnsureFolder(
                WorldMeshesPaths.Root,
                "Generated",
                WorldMeshesPaths.Generated
            );

            EnsureFolder(
                WorldMeshesPaths.Generated,
                "Heightmaps",
                WorldMeshesPaths.GeneratedHeightmaps
            );

            EnsureFolder(
                WorldMeshesPaths.GeneratedHeightmaps,
                "Streaming",
                WorldMeshesPaths.HeightmapStreaming
            );

            if (descriptors == null)
            {
                return true;
            }

            for (
                int index = 0;
                index < descriptors.Count;
                index++
            )
            {
                TerrainHeightStreamingLevelDescriptor descriptor =
                    descriptors[index];

                string folder =
                    TerrainRuntimeHeightAssetUtility
                        .GetStreamingStrideFolder(
                            descriptor.SampleStride
                        );

                if (
                    AssetDatabase.IsValidFolder(
                        folder
                    )
                )
                {
                    continue;
                }

                AssetDatabase.CreateFolder(
                    WorldMeshesPaths.HeightmapStreaming,
                    $"Stride_{descriptor.SampleStride}"
                );
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not prepare runtime height-streaming output folders.\n\n" +
                exception.Message;

            return false;
        }
    }

    public static bool DeleteObsoleteOutputs(
        WorldSettings worldSettings,
        IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors,
        out int removedAssetCount,
        out string errorMessage
    )
    {
        removedAssetCount =
            0;

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null while removing obsolete height-streaming outputs.";

            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.HeightmapStreaming
            )
        )
        {
            return true;
        }

        HashSet<int> expectedStrides =
            new HashSet<int>();

        if (descriptors != null)
        {
            for (
                int index = 0;
                index < descriptors.Count;
                index++
            )
            {
                expectedStrides.Add(
                    descriptors[index].SampleStride
                );
            }
        }

        List<string> obsoleteAssets =
            new List<string>();

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Texture2D",
                new[]
                {
                    WorldMeshesPaths.HeightmapStreaming
                }
            );

        for (
            int index = 0;
            index < guids.Length;
            index++
        )
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guids[index]
                );

            if (
                !TerrainRuntimeHeightAssetUtility
                    .TryGetStreamingHeightTileIdentity(
                        path,
                        out int stride,
                        out int tileX,
                        out int tileZ
                    )
            )
            {
                continue;
            }

            bool obsolete =
                !expectedStrides.Contains(
                    stride
                )
                ||
                tileX < 0
                ||
                tileZ < 0
                ||
                tileX >=
                    worldSettings.HeightTileGridWidth
                ||
                tileZ >=
                    worldSettings.HeightTileGridHeight;

            if (obsolete)
            {
                obsoleteAssets.Add(
                    path
                );
            }
        }

        try
        {
            for (
                int index = 0;
                index < obsoleteAssets.Count;
                index++
            )
            {
                if (
                    !AssetDatabase.DeleteAsset(
                        obsoleteAssets[index]
                    )
                )
                {
                    errorMessage =
                        "Could not remove obsolete height-streaming asset:\n" +
                        obsoleteAssets[index];

                    return false;
                }

                removedAssetCount++;
            }

            string[] subFolders =
                AssetDatabase.GetSubFolders(
                    WorldMeshesPaths.HeightmapStreaming
                );

            for (
                int index = 0;
                index < subFolders.Length;
                index++
            )
            {
                string folder =
                    subFolders[index];

                string folderName =
                    Path.GetFileName(
                        folder
                    );

                if (
                    !TryParseGeneratedStrideFolder(
                        folderName,
                        out int stride
                    )
                    ||
                    expectedStrides.Contains(
                        stride
                    )
                )
                {
                    continue;
                }

                string[] remainingAssets =
                    AssetDatabase.FindAssets(
                        "",
                        new[]
                        {
                            folder
                        }
                    );

                string[] nestedFolders =
                    AssetDatabase.GetSubFolders(
                        folder
                    );

                if (
                    remainingAssets.Length > 0
                    ||
                    nestedFolders.Length > 0
                )
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(
                    folder
                );
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not remove obsolete runtime height-streaming outputs.\n\n" +
                exception.Message;

            return false;
        }
    }

    private static void EnsureFolder(
        string parent,
        string childName,
        string fullPath
    )
    {
        if (
            AssetDatabase.IsValidFolder(
                fullPath
            )
        )
        {
            return;
        }

        AssetDatabase.CreateFolder(
            parent,
            childName
        );
    }

    private static bool TryParseGeneratedStrideFolder(
        string folderName,
        out int stride
    )
    {
        stride =
            0;

        if (
            string.IsNullOrEmpty(
                folderName
            )
            ||
            !folderName.StartsWith(
                "Stride_",
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        string strideText =
            folderName.Substring(
                "Stride_".Length
            );

        return
            int.TryParse(
                strideText,
                out stride
            )
            &&
            stride > 1
            &&
            TerrainHeightStreamingPyramidPolicy
                .IsPowerOfTwo(
                    stride
                );
    }
}
