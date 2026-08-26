using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightmapValidator
{
    private const float EdgeTolerance =
        0.000001f;

    private const float SettingsFloatTolerance =
        0.0001f;

    // =====================================================
    // VALIDATE RUNTIME HEIGHTMAPS
    // =====================================================

    public static bool ValidateHeightmaps(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps: " +
                "WorldSettings is null."
            );

            return false;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps.\n\n" +
                "Runtime heightmap manifest does not exist.\n\n" +
                "Compile the runtime heightmaps first."
            );

            return false;
        }

        if (!manifest.isComplete)
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps.\n\n" +
                "The runtime heightmap manifest is marked incomplete.\n\n" +
                "Compile the runtime heightmaps again."
            );

            return false;
        }

        if (
            manifest.compilerVersion !=
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion
        )
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps.\n\n" +
                "The runtime heightmap compiler version is out of date.\n\n" +
                "Compile the runtime heightmaps again."
            );

            return false;
        }

        if (
            !ManifestMatchesWorldSettings(
                manifest,
                worldSettings
            )
        )
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps.\n\n" +
                "The generated heightmap layout does not match " +
                "the current saved WorldSettings.\n\n" +
                "Compile the runtime heightmaps again."
            );

            return false;
        }

        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (authoringData == null)
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps.\n\n" +
                "TerrainAuthoringData could not be loaded."
            );

            return false;
        }

        if (
            !TerrainAuthoringStateUtility
                .TryValidateCommittedHeightfield(
                    worldSettings,
                    authoringData,
                    out TerrainAuthoringHeightManifest
                        authoringManifest,
                    out string currentAuthoringContentHash,
                    out string authoringValidationError
                )
        )
        {
            Debug.LogError(
                "Cannot validate runtime heightmaps because the " +
                "committed authoring heightfield is invalid.\n\n" +
                authoringValidationError
            );

            return false;
        }

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility
                .GetCurrentAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            manifest.sourceAuthoringRevision !=
                authoringData.authoringRevision
            ||
            manifest.sourceAuthoringSignature !=
                currentAuthoringSignature
            ||
            manifest.sourceAuthoringContentHash !=
                currentAuthoringContentHash
        )
        {
            Debug.LogError(
                "Runtime heightmap validation failed.\n\n" +
                "The runtime heightmaps were compiled from an older " +
                "authoring state.\n\n" +
                "Compile the runtime heightmaps again."
            );

            return false;
        }

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        int expectedTileCount =
            tileGridWidth *
            tileGridHeight;

        Dictionary<Vector2Int, Texture2D> tiles =
            new Dictionary<Vector2Int, Texture2D>();

        int invalidTileCount =
            0;

        int invalidHeightSampleCount =
            0;

        // =================================================
        // LOAD + VALIDATE EVERY RUNTIME TILE
        // =================================================

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                string path =
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTilePath(
                            tileX,
                            tileZ
                        );

                Texture2D texture =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            path
                        );

                if (texture == null)
                {
                    Debug.LogError(
                        "Missing runtime heightmap tile:\n" +
                        path
                    );

                    invalidTileCount++;

                    continue;
                }

                bool tileValid =
                    true;

                if (
                    texture.width != samplesPerSide
                    ||
                    texture.height != samplesPerSide
                )
                {
                    Debug.LogError(
                        $"Invalid dimensions for runtime heightmap " +
                        $"tile ({tileX}, {tileZ}).\n\n" +
                        $"Expected: {samplesPerSide} x {samplesPerSide}\n" +
                        $"Actual: {texture.width} x {texture.height}"
                    );

                    tileValid =
                        false;
                }

                if (
                    texture.format !=
                    TextureFormat.RFloat
                )
                {
                    Debug.LogError(
                        $"Invalid texture format for runtime heightmap " +
                        $"tile ({tileX}, {tileZ}).\n\n" +
                        $"Expected: {TextureFormat.RFloat}\n" +
                        $"Actual: {texture.format}"
                    );

                    tileValid =
                        false;
                }

                if (!tileValid)
                {
                    invalidTileCount++;

                    continue;
                }

                NativeArray<float> heightData;

                try
                {
                    heightData =
                        texture.GetPixelData<float>(
                            0
                        );
                }
                catch (
                    System.Exception exception
                )
                {
                    Debug.LogError(
                        $"Could not read runtime heightmap tile " +
                        $"({tileX}, {tileZ}).\n\n" +
                        exception.Message
                    );

                    invalidTileCount++;

                    continue;
                }

                if (
                    heightData.Length !=
                    expectedSampleCount
                )
                {
                    Debug.LogError(
                        $"Invalid sample count for runtime heightmap " +
                        $"tile ({tileX}, {tileZ}).\n\n" +
                        $"Expected: {expectedSampleCount:N0}\n" +
                        $"Actual: {heightData.Length:N0}"
                    );

                    invalidTileCount++;

                    continue;
                }

                bool containsInvalidHeight =
                    false;

                for (
                    int index = 0;
                    index < heightData.Length;
                    index++
                )
                {
                    float height =
                        heightData[index];

                    if (
                        float.IsNaN(height)
                        ||
                        float.IsInfinity(height)
                    )
                    {
                        invalidHeightSampleCount++;

                        containsInvalidHeight =
                            true;
                    }
                }

                if (containsInvalidHeight)
                {
                    Debug.LogError(
                        $"Runtime heightmap tile " +
                        $"({tileX}, {tileZ}) contains " +
                        "invalid height values."
                    );

                    invalidTileCount++;

                    continue;
                }

                tiles[
                    new Vector2Int(
                        tileX,
                        tileZ
                    )
                ] =
                    texture;
            }
        }

        if (
            invalidTileCount > 0
            ||
            tiles.Count != expectedTileCount
        )
        {
            Debug.LogError(
                "Runtime heightmap validation failed.\n\n" +
                $"Expected Tiles: {expectedTileCount}\n" +
                $"Valid Tiles: {tiles.Count}\n" +
                $"Invalid Tiles: {invalidTileCount}\n" +
                $"Invalid Height Samples: " +
                $"{invalidHeightSampleCount:N0}"
            );

            return false;
        }

        // =================================================
        // SEAM VALIDATION
        // =================================================

        int boundaryPairCount =
            0;

        int edgeSampleComparisonCount =
            0;

        int edgeMismatchCount =
            0;

        float maximumEdgeDifference =
            0f;

        string firstMismatch =
            null;

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth - 1;
                tileX++
            )
            {
                NativeArray<float> leftData =
                    tiles[
                        new Vector2Int(
                            tileX,
                            tileZ
                        )
                    ]
                    .GetPixelData<float>(
                        0
                    );

                NativeArray<float> rightData =
                    tiles[
                        new Vector2Int(
                            tileX + 1,
                            tileZ
                        )
                    ]
                    .GetPixelData<float>(
                        0
                    );

                boundaryPairCount++;

                for (
                    int sampleZ = 0;
                    sampleZ < samplesPerSide;
                    sampleZ++
                )
                {
                    int leftIndex =
                        sampleZ *
                        samplesPerSide +
                        (
                            samplesPerSide -
                            1
                        );

                    int rightIndex =
                        sampleZ *
                        samplesPerSide;

                    RecordEdgeComparison(
                        $"Horizontal boundary: " +
                        $"Tile ({tileX}, {tileZ}) right edge vs " +
                        $"Tile ({tileX + 1}, {tileZ}) left edge, " +
                        $"sample Z {sampleZ}",
                        leftData[leftIndex],
                        rightData[rightIndex],
                        ref edgeSampleComparisonCount,
                        ref edgeMismatchCount,
                        ref maximumEdgeDifference,
                        ref firstMismatch
                    );
                }
            }
        }

        for (
            int tileZ = 0;
            tileZ < tileGridHeight - 1;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                NativeArray<float> lowerData =
                    tiles[
                        new Vector2Int(
                            tileX,
                            tileZ
                        )
                    ]
                    .GetPixelData<float>(
                        0
                    );

                NativeArray<float> upperData =
                    tiles[
                        new Vector2Int(
                            tileX,
                            tileZ + 1
                        )
                    ]
                    .GetPixelData<float>(
                        0
                    );

                boundaryPairCount++;

                for (
                    int sampleX = 0;
                    sampleX < samplesPerSide;
                    sampleX++
                )
                {
                    int lowerIndex =
                        (
                            samplesPerSide -
                            1
                        )
                        *
                        samplesPerSide +
                        sampleX;

                    int upperIndex =
                        sampleX;

                    RecordEdgeComparison(
                        $"Vertical boundary: " +
                        $"Tile ({tileX}, {tileZ}) upper edge vs " +
                        $"Tile ({tileX}, {tileZ + 1}) lower edge, " +
                        $"sample X {sampleX}",
                        lowerData[lowerIndex],
                        upperData[upperIndex],
                        ref edgeSampleComparisonCount,
                        ref edgeMismatchCount,
                        ref maximumEdgeDifference,
                        ref firstMismatch
                    );
                }
            }
        }

        if (edgeMismatchCount > 0)
        {
            Debug.LogError(
                "Runtime heightmap tile seam validation failed.\n\n" +
                $"Boundary Pairs: {boundaryPairCount}\n" +
                $"Edge Samples Compared: " +
                $"{edgeSampleComparisonCount:N0}\n" +
                $"Mismatched Samples: {edgeMismatchCount:N0}\n" +
                $"Maximum Difference: {maximumEdgeDifference:R}\n\n" +
                $"Tolerance: {EdgeTolerance:R}\n\n" +
                $"First Mismatch:\n{firstMismatch}"
            );

            return false;
        }

        Debug.Log(
            "Runtime heightmap validation passed.\n\n" +
            $"Tile Grid: {tileGridWidth} x {tileGridHeight}\n" +
            $"Tiles Validated: {tiles.Count}\n" +
            $"Samples Per Tile: " +
            $"{samplesPerSide} x {samplesPerSide}\n\n" +
            $"Authoring Revision: " +
            $"{authoringData.authoringRevision}\n" +
            $"Authoring Content Hash: " +
            $"{authoringManifest.committedContentHash}\n\n" +
            $"Boundary Pairs: {boundaryPairCount}\n" +
            $"Edge Samples Compared: " +
            $"{edgeSampleComparisonCount:N0}\n" +
            "Mismatched Samples: 0\n" +
            $"Maximum Edge Difference: " +
            $"{maximumEdgeDifference:R}"
        );

        return true;
    }

    private static void RecordEdgeComparison(
        string description,
        float a,
        float b,
        ref int comparisonCount,
        ref int mismatchCount,
        ref float maximumDifference,
        ref string firstMismatch
    )
    {
        float difference =
            Mathf.Abs(
                a - b
            );

        maximumDifference =
            Mathf.Max(
                maximumDifference,
                difference
            );

        comparisonCount++;

        if (
            difference <=
            EdgeTolerance
        )
        {
            return;
        }

        mismatchCount++;

        if (firstMismatch == null)
        {
            firstMismatch =
                description +
                "\n\n" +
                $"A: {a:R}\n" +
                $"B: {b:R}\n" +
                $"Difference: {difference:R}";
        }
    }

    private static bool ManifestMatchesWorldSettings(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest.gridWidth !=
                worldSettings.gridWidth
            ||
            manifest.gridHeight !=
                worldSettings.gridHeight
            ||
            !FloatMatches(
                manifest.chunkSize,
                worldSettings.chunkSize
            )
            ||
            manifest.lod0Resolution !=
                worldSettings.lod0Resolution
            ||
            manifest.heightTileChunkSpan !=
                worldSettings.heightTileChunkSpan
            ||
            manifest.heightTileGridWidth !=
                worldSettings.HeightTileGridWidth
            ||
            manifest.heightTileGridHeight !=
                worldSettings.HeightTileGridHeight
            ||
            !FloatMatches(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            )
            ||
            manifest.heightTileSamplesPerSide !=
                worldSettings.HeightTileSamplesPerSide
        )
        {
            return false;
        }

        return true;
    }

    private static bool FloatMatches(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a - b
            )
            <=
            SettingsFloatTolerance;
    }
}
