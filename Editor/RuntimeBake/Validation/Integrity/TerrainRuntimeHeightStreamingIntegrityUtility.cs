using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class TerrainRuntimeHeightStreamingIntegrityUtility
{
    internal static TerrainRuntimeGeneratedDataIntegrityResult Validate(
        WorldSettings worldSettings
    )
    {
        TerrainRuntimeGeneratedDataIntegrityResult result =
            new TerrainRuntimeGeneratedDataIntegrityResult
            {
                DatasetName = "Height Streaming"
            };

        if (worldSettings == null)
        {
            result.Errors.Add("WorldSettings is unavailable.");
            return result;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        result.ManifestPresent = manifest != null;
        result.ManifestComplete =
            manifest != null
            && manifest.streamingPyramidIsComplete;

        result.ManifestCompatible =
            manifest != null
            && TerrainGenerationStateUtility
                .IsHeightStreamingManifestCurrent(
                    manifest,
                    worldSettings,
                    worldSettings.heightmapGenerationRevision
                );

        List<int> derivedStrides = new List<int>();

        if (
            !TerrainHeightStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                derivedStrides,
                out string policyError
            )
        )
        {
            result.Errors.Add(policyError);
            return result;
        }

        int width = Mathf.Max(1, worldSettings.HeightTileGridWidth);
        int height = Mathf.Max(1, worldSettings.HeightTileGridHeight);

        result.ExpectedAssetCount =
            width * height * derivedStrides.Count;

        HashSet<int> expectedStrides =
            new HashSet<int>(derivedStrides);

        for (int strideIndex = 0; strideIndex < derivedStrides.Count; strideIndex++)
        {
            int stride = derivedStrides[strideIndex];

            if (
                manifest == null
                || !manifest.TryGetStreamingLevelDescriptor(
                    stride,
                    out TerrainHeightStreamingLevelDescriptor actualDescriptor
                )
                || !TerrainHeightStreamingPyramidPolicy.TryBuildLevelDescriptor(
                    worldSettings,
                    stride,
                    out TerrainHeightStreamingLevelDescriptor expectedDescriptor,
                    out _
                )
                || actualDescriptor.SampleStride != expectedDescriptor.SampleStride
                || actualDescriptor.SamplesPerSide != expectedDescriptor.SamplesPerSide
                || !Mathf.Approximately(
                    actualDescriptor.SampleSpacing,
                    expectedDescriptor.SampleSpacing
                )
            )
            {
                result.ManifestCompatible = false;
            }

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    string path =
                        TerrainRuntimeHeightAssetUtility
                            .GetStreamingHeightTilePath(
                                stride,
                                x,
                                z
                            );

                    System.Type assetType =
                        AssetDatabase.GetMainAssetTypeAtPath(path);

                    if (assetType == typeof(Texture2D))
                    {
                        result.PresentAssetCount++;
                        continue;
                    }

                    Vector2Int coordinate =
                        new Vector2Int(x, z);

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
                TerrainRuntimeHeightAssetUtility.HeightmapStreamingFolder
            )
        )
        {
            string[] guids =
                AssetDatabase.FindAssets(
                    "t:Texture2D",
                    new[]
                    {
                        TerrainRuntimeHeightAssetUtility.HeightmapStreamingFolder
                    }
                );

            for (int index = 0; index < guids.Length; index++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[index]);

                if (
                    !TerrainRuntimeHeightAssetUtility
                        .TryGetStreamingHeightTileIdentity(
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
                    expectedStrides.Contains(stride)
                    && x >= 0
                    && z >= 0
                    && x < width
                    && z < height
                )
                {
                    continue;
                }

                result.RecordUnexpectedAsset(
                    new Vector2Int(x, z),
                    path
                );
            }
        }

        return result;
    }
}
