using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class TerrainRuntimeSurfaceStreamingIntegrityUtility
{
    internal static TerrainRuntimeGeneratedDataIntegrityResult Validate(
        WorldSettings worldSettings
    )
    {
        TerrainRuntimeGeneratedDataIntegrityResult result =
            new TerrainRuntimeGeneratedDataIntegrityResult
            {
                DatasetName = "Surface Streaming"
            };

        if (worldSettings == null)
        {
            result.Errors.Add("WorldSettings is unavailable.");
            return result;
        }

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        result.ManifestPresent =
            manifest != null;

        result.ManifestComplete =
            manifest != null
            && manifest.streamingPyramidIsComplete;

        result.ManifestCompatible =
            manifest != null
            && TerrainGenerationStateUtility
                .IsSurfaceStreamingManifestCurrent(
                    manifest,
                    worldSettings
                );

        List<int> derivedStrides =
            new List<int>();

        if (
            !TerrainSurfaceStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                manifest,
                derivedStrides,
                out string policyError
            )
        )
        {
            result.Errors.Add(policyError);
            return result;
        }

        int width =
            manifest != null
                ? Mathf.Max(0, manifest.tileGridWidth)
                : 0;

        int height =
            manifest != null
                ? Mathf.Max(0, manifest.tileGridHeight)
                : 0;

        result.ExpectedAssetCount =
            width
            * height
            * derivedStrides.Count;

        HashSet<int> expectedStrides =
            new HashSet<int>(
                derivedStrides
            );

        for (
            int strideIndex = 0;
            strideIndex < derivedStrides.Count;
            strideIndex++
        )
        {
            int stride =
                derivedStrides[strideIndex];

            if (
                manifest == null
                ||
                !manifest.TryGetStreamingLevelDescriptor(
                    stride,
                    out TerrainSurfaceStreamingLevelDescriptor actualDescriptor
                )
                ||
                !TerrainSurfaceStreamingPyramidPolicy.TryBuildLevelDescriptor(
                    worldSettings,
                    manifest,
                    stride,
                    out TerrainSurfaceStreamingLevelDescriptor expectedDescriptor,
                    out _
                )
                ||
                actualDescriptor.SampleStride !=
                    expectedDescriptor.SampleStride
                ||
                actualDescriptor.SamplesPerSide !=
                    expectedDescriptor.SamplesPerSide
                ||
                actualDescriptor.TileGridWidth !=
                    expectedDescriptor.TileGridWidth
                ||
                actualDescriptor.TileGridHeight !=
                    expectedDescriptor.TileGridHeight
                ||
                actualDescriptor.TextureFormat !=
                    expectedDescriptor.TextureFormat
                ||
                !Mathf.Approximately(
                    actualDescriptor.SampleSpacing,
                    expectedDescriptor.SampleSpacing
                )
                ||
                !Mathf.Approximately(
                    actualDescriptor.TileWorldSize,
                    expectedDescriptor.TileWorldSize
                )
            )
            {
                result.ManifestCompatible =
                    false;
            }

            for (
                int z = 0;
                z < height;
                z++
            )
            {
                for (
                    int x = 0;
                    x < width;
                    x++
                )
                {
                    string path =
                        TerrainRuntimeSurfaceMaskAssetUtility
                            .GetStreamingSurfaceTilePath(
                                stride,
                                x,
                                z
                            );

                    System.Type assetType =
                        AssetDatabase
                            .GetMainAssetTypeAtPath(
                                path
                            );

                    if (
                        assetType ==
                        typeof(Texture2D)
                    )
                    {
                        result.PresentAssetCount++;
                        continue;
                    }

                    Vector2Int coordinate =
                        new Vector2Int(
                            x,
                            z
                        );

                    if (assetType == null)
                    {
                        result.RecordMissingAsset(
                            coordinate,
                            path
                        );
                    }
                    else
                    {
                        result.RecordWrongAssetType(
                            coordinate,
                            path,
                            assetType
                        );
                    }
                }
            }
        }

        if (
            AssetDatabase.IsValidFolder(
                TerrainRuntimeSurfaceMaskAssetUtility
                    .SurfaceMaskStreamingFolder
            )
        )
        {
            string[] guids =
                AssetDatabase.FindAssets(
                    "t:Texture2D",
                    new[]
                    {
                        TerrainRuntimeSurfaceMaskAssetUtility
                            .SurfaceMaskStreamingFolder
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
                            out int x,
                            out int z
                        )
                )
                {
                    continue;
                }

                if (
                    expectedStrides.Contains(
                        stride
                    )
                    &&
                    x >= 0
                    &&
                    z >= 0
                    &&
                    x < width
                    &&
                    z < height
                )
                {
                    continue;
                }

                result.RecordUnexpectedAsset(
                    new Vector2Int(
                        x,
                        z
                    ),
                    path
                );
            }
        }

        return result;
    }
}
