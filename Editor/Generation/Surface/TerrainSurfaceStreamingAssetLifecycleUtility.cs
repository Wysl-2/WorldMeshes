using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

public static class TerrainSurfaceStreamingAssetLifecycleUtility
{
    public static bool EnsureTargetFolders(
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> descriptors,
        out string errorMessage
    )
    {
        errorMessage = "";

        try
        {
            EnsureFolder(
                WorldMeshesPaths.Root,
                "Generated",
                WorldMeshesPaths.Generated
            );

            EnsureFolder(
                WorldMeshesPaths.Generated,
                "SurfaceMasks",
                WorldMeshesPaths.GeneratedSurfaceMasks
            );

            EnsureFolder(
                WorldMeshesPaths.GeneratedSurfaceMasks,
                "Streaming",
                WorldMeshesPaths.SurfaceMaskStreaming
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
                TerrainSurfaceStreamingLevelDescriptor descriptor =
                    descriptors[index];

                string folder =
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .GetStreamingStrideFolder(
                            descriptor.SampleStride
                        );

                if (AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                AssetDatabase.CreateFolder(
                    WorldMeshesPaths.SurfaceMaskStreaming,
                    $"Stride_{descriptor.SampleStride}"
                );
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not prepare runtime Surface streaming output folders.\n\n" +
                exception.Message;

            return false;
        }
    }

    public static bool DeleteObsoleteOutputs(
        TerrainSurfaceMaskManifest manifest,
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> descriptors,
        out int removedAssetCount,
        out string errorMessage
    )
    {
        removedAssetCount = 0;
        errorMessage = "";

        if (manifest == null)
        {
            errorMessage =
                "The authoritative Surface manifest is unavailable while removing obsolete Surface streaming outputs.";

            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.SurfaceMaskStreaming
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
                    WorldMeshesPaths.SurfaceMaskStreaming
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
                !TerrainRuntimeSurfaceMaskAssetUtility
                    .TryGetStreamingSurfaceTileIdentity(
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
                !expectedStrides.Contains(stride)
                || tileX < 0
                || tileZ < 0
                || tileX >= manifest.tileGridWidth
                || tileZ >= manifest.tileGridHeight;

            if (obsolete)
            {
                obsoleteAssets.Add(path);
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
                if (!AssetDatabase.DeleteAsset(obsoleteAssets[index]))
                {
                    errorMessage =
                        "Could not remove obsolete Surface streaming asset:\n" +
                        obsoleteAssets[index];

                    return false;
                }

                removedAssetCount++;
            }

            string[] subFolders =
                AssetDatabase.GetSubFolders(
                    WorldMeshesPaths.SurfaceMaskStreaming
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
                    Path.GetFileName(folder);

                if (
                    !TryParseGeneratedStrideFolder(
                        folderName,
                        out int stride
                    )
                    || expectedStrides.Contains(stride)
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
                    AssetDatabase.GetSubFolders(folder);

                if (
                    remainingAssets.Length > 0
                    || nestedFolders.Length > 0
                )
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(folder);
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not remove obsolete runtime Surface streaming outputs.\n\n" +
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
        if (AssetDatabase.IsValidFolder(fullPath))
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
        stride = 0;

        if (
            string.IsNullOrEmpty(folderName)
            || !folderName.StartsWith(
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
            && stride > 1
            && TerrainSurfaceStreamingPyramidPolicy
                .IsPowerOfTwo(stride);
    }
}
