using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightmapValidator
{
    // =====================================================
    // VALIDATION
    // =====================================================

    private const float EdgeTolerance =
        0.000001f;

    private const float SettingsFloatTolerance =
        0.0001f;

    // =====================================================
    // VALIDATE HEIGHTMAPS
    // =====================================================

    public static bool ValidateHeightmaps(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate heightmaps: " +
                "WorldSettings is null."
            );

            return false;
        }

        // -------------------------------------------------
        // Load manifest
        // -------------------------------------------------

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath
                    <TerrainHeightmapManifest>(
                        TerrainHeightmapGenerator
                            .HeightmapManifestPath
                    );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot validate heightmaps.\n\n" +
                "Heightmap manifest does not exist.\n\n" +
                "Generate the heightmaps first."
            );

            return false;
        }

        // -------------------------------------------------
        // Manifest completeness
        // -------------------------------------------------

        if (!manifest.isComplete)
        {
            Debug.LogError(
                "Cannot validate heightmaps.\n\n" +
                "The heightmap manifest is marked " +
                "as incomplete.\n\n" +
                "Generate the heightmaps again."
            );

            return false;
        }

        // -------------------------------------------------
        // Manifest vs current WorldSettings
        // -------------------------------------------------

        if (
            !ManifestMatchesWorldSettings(
                manifest,
                worldSettings
            )
        )
        {
            Debug.LogError(
                "Cannot validate heightmaps.\n\n" +
                "The generated heightmaps do not match " +
                "the current saved WorldSettings.\n\n" +
                "Generate the heightmaps again."
            );

            return false;
        }

        // -------------------------------------------------
        // Expected layout
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Load and validate every tile
        // -------------------------------------------------

        Dictionary<Vector2Int, Texture2D> tiles =
            new Dictionary<Vector2Int, Texture2D>();

        int invalidTileCount =
            0;

        int invalidHeightSampleCount =
            0;

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
                    TerrainHeightmapGenerator
                        .GetHeightTilePath(
                            tileX,
                            tileZ
                        );

                Texture2D texture =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            path
                        );

                // -----------------------------------------
                // Missing asset
                // -----------------------------------------

                if (texture == null)
                {
                    Debug.LogError(
                        "Missing heightmap tile:\n" +
                        path
                    );

                    invalidTileCount++;

                    continue;
                }

                bool tileValid =
                    true;

                // -----------------------------------------
                // Dimensions
                // -----------------------------------------

                if (
                    texture.width !=
                        samplesPerSide
                    ||
                    texture.height !=
                        samplesPerSide
                )
                {
                    Debug.LogError(
                        $"Invalid dimensions for " +
                        $"heightmap tile " +
                        $"({tileX}, {tileZ}).\n\n" +

                        $"Expected: " +
                        $"{samplesPerSide} x " +
                        $"{samplesPerSide}\n" +

                        $"Actual: " +
                        $"{texture.width} x " +
                        $"{texture.height}"
                    );

                    tileValid =
                        false;
                }

                // -----------------------------------------
                // Texture format
                // -----------------------------------------

                if (
                    texture.format !=
                    TextureFormat.RFloat
                )
                {
                    Debug.LogError(
                        $"Invalid texture format for " +
                        $"heightmap tile " +
                        $"({tileX}, {tileZ}).\n\n" +

                        $"Expected: " +
                        $"{TextureFormat.RFloat}\n" +

                        $"Actual: " +
                        $"{texture.format}"
                    );

                    tileValid =
                        false;
                }

                // -----------------------------------------
                // Stop before reading malformed tile
                // -----------------------------------------

                if (!tileValid)
                {
                    invalidTileCount++;

                    continue;
                }

                // -----------------------------------------
                // Read raw height data
                // -----------------------------------------

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
                        $"Could not read heightmap tile " +
                        $"({tileX}, {tileZ}).\n\n" +
                        exception.Message
                    );

                    invalidTileCount++;

                    continue;
                }

                // -----------------------------------------
                // Sample count
                // -----------------------------------------

                if (
                    heightData.Length !=
                    expectedSampleCount
                )
                {
                    Debug.LogError(
                        $"Invalid sample count for " +
                        $"heightmap tile " +
                        $"({tileX}, {tileZ}).\n\n" +

                        $"Expected: " +
                        $"{expectedSampleCount:N0}\n" +

                        $"Actual: " +
                        $"{heightData.Length:N0}"
                    );

                    invalidTileCount++;

                    continue;
                }

                // -----------------------------------------
                // Validate individual heights
                // -----------------------------------------

                bool containsInvalidHeight =
                    false;

                for (
                    int i = 0;
                    i < heightData.Length;
                    i++
                )
                {
                    float height =
                        heightData[i];

                    if (
                        float.IsNaN(
                            height
                        )
                        ||
                        float.IsInfinity(
                            height
                        )
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
                        $"Heightmap tile " +
                        $"({tileX}, {tileZ}) contains " +
                        "invalid height values."
                    );

                    invalidTileCount++;

                    continue;
                }

                // -----------------------------------------
                // Valid tile
                // -----------------------------------------

                tiles[
                    new Vector2Int(
                        tileX,
                        tileZ
                    )
                ] =
                    texture;
            }
        }

        // -------------------------------------------------
        // Invalid tile set
        // -------------------------------------------------

        if (
            invalidTileCount > 0 ||
            tiles.Count != expectedTileCount
        )
        {
            Debug.LogError(
                "Heightmap validation failed.\n\n" +

                $"Expected Tiles: " +
                $"{expectedTileCount}\n" +

                $"Valid Tiles: " +
                $"{tiles.Count}\n" +

                $"Invalid Tiles: " +
                $"{invalidTileCount}\n" +

                $"Invalid Height Samples: " +
                $"{invalidHeightSampleCount:N0}"
            );

            return false;
        }

        // -------------------------------------------------
        // Edge validation statistics
        // -------------------------------------------------

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

        // =================================================
        // HORIZONTAL NEIGHBORS
        //
        // A right edge vs B left edge
        // =================================================

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
                Texture2D leftTile =
                    tiles[
                        new Vector2Int(
                            tileX,
                            tileZ
                        )
                    ];

                Texture2D rightTile =
                    tiles[
                        new Vector2Int(
                            tileX + 1,
                            tileZ
                        )
                    ];

                NativeArray<float> leftData =
                    leftTile.GetPixelData<float>(
                        0
                    );

                NativeArray<float> rightData =
                    rightTile.GetPixelData<float>(
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
                        samplesPerSide
                        +
                        (
                            samplesPerSide -
                            1
                        );

                    int rightIndex =
                        sampleZ *
                        samplesPerSide;

                    float leftHeight =
                        leftData[
                            leftIndex
                        ];

                    float rightHeight =
                        rightData[
                            rightIndex
                        ];

                    float difference =
                        Mathf.Abs(
                            leftHeight -
                            rightHeight
                        );

                    maximumEdgeDifference =
                        Mathf.Max(
                            maximumEdgeDifference,
                            difference
                        );

                    edgeSampleComparisonCount++;

                    if (
                        difference >
                        EdgeTolerance
                    )
                    {
                        edgeMismatchCount++;

                        if (firstMismatch == null)
                        {
                            firstMismatch =
                                "Horizontal boundary:\n" +
                                $"Tile ({tileX}, {tileZ}) " +
                                "right edge\n" +
                                "vs\n" +
                                $"Tile ({tileX + 1}, " +
                                $"{tileZ}) left edge\n\n" +

                                $"Edge Sample Z: " +
                                $"{sampleZ}\n" +

                                $"Left Height: " +
                                $"{leftHeight:R}\n" +

                                $"Right Height: " +
                                $"{rightHeight:R}\n" +

                                $"Difference: " +
                                $"{difference:R}";
                        }
                    }
                }
            }
        }

        // =================================================
        // VERTICAL NEIGHBORS
        //
        // A upper Z edge vs B lower Z edge
        // =================================================

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
                Texture2D lowerTile =
                    tiles[
                        new Vector2Int(
                            tileX,
                            tileZ
                        )
                    ];

                Texture2D upperTile =
                    tiles[
                        new Vector2Int(
                            tileX,
                            tileZ + 1
                        )
                    ];

                NativeArray<float> lowerData =
                    lowerTile.GetPixelData<float>(
                        0
                    );

                NativeArray<float> upperData =
                    upperTile.GetPixelData<float>(
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
                        samplesPerSide
                        +
                        sampleX;

                    int upperIndex =
                        sampleX;

                    float lowerHeight =
                        lowerData[
                            lowerIndex
                        ];

                    float upperHeight =
                        upperData[
                            upperIndex
                        ];

                    float difference =
                        Mathf.Abs(
                            lowerHeight -
                            upperHeight
                        );

                    maximumEdgeDifference =
                        Mathf.Max(
                            maximumEdgeDifference,
                            difference
                        );

                    edgeSampleComparisonCount++;

                    if (
                        difference >
                        EdgeTolerance
                    )
                    {
                        edgeMismatchCount++;

                        if (firstMismatch == null)
                        {
                            firstMismatch =
                                "Vertical boundary:\n" +
                                $"Tile ({tileX}, {tileZ}) " +
                                "upper Z edge\n" +
                                "vs\n" +
                                $"Tile ({tileX}, " +
                                $"{tileZ + 1}) lower Z edge\n\n" +

                                $"Edge Sample X: " +
                                $"{sampleX}\n" +

                                $"Lower Height: " +
                                $"{lowerHeight:R}\n" +

                                $"Upper Height: " +
                                $"{upperHeight:R}\n" +

                                $"Difference: " +
                                $"{difference:R}";
                        }
                    }
                }
            }
        }

        // -------------------------------------------------
        // Edge failure
        // -------------------------------------------------

        if (edgeMismatchCount > 0)
        {
            Debug.LogError(
                "Heightmap tile seam validation failed.\n\n" +

                $"Boundary Pairs: " +
                $"{boundaryPairCount}\n" +

                $"Edge Samples Compared: " +
                $"{edgeSampleComparisonCount:N0}\n" +

                $"Mismatched Samples: " +
                $"{edgeMismatchCount:N0}\n" +

                $"Maximum Difference: " +
                $"{maximumEdgeDifference:R}\n\n" +

                $"Tolerance: " +
                $"{EdgeTolerance:R}\n\n" +

                $"First Mismatch:\n" +
                $"{firstMismatch}"
            );

            return false;
        }

        // -------------------------------------------------
        // Success
        // -------------------------------------------------

        Debug.Log(
            "Heightmap validation passed.\n\n" +

            $"Tile Grid: " +
            $"{tileGridWidth} x " +
            $"{tileGridHeight}\n" +

            $"Tiles Validated: " +
            $"{tiles.Count}\n" +

            $"Samples Per Tile: " +
            $"{samplesPerSide} x " +
            $"{samplesPerSide}\n\n" +

            $"Boundary Pairs: " +
            $"{boundaryPairCount}\n" +

            $"Edge Samples Compared: " +
            $"{edgeSampleComparisonCount:N0}\n" +

            $"Mismatched Samples: 0\n" +

            $"Maximum Edge Difference: " +
            $"{maximumEdgeDifference:R}\n\n" +

            "The tiled heightmap set is valid and " +
            "ready to be applied to terrain meshes."
        );

        return true;
    }

    // =====================================================
    // MANIFEST VALIDATION
    // =====================================================

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
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.chunkSize,
                worldSettings.chunkSize
            )
        )
        {
            return false;
        }

        if (
            manifest.lod0Resolution !=
            worldSettings.lod0Resolution
        )
        {
            return false;
        }

        if (
            manifest.heightTileChunkSpan !=
            worldSettings.heightTileChunkSpan
        )
        {
            return false;
        }

        if (
            manifest.heightTileGridWidth !=
            worldSettings.HeightTileGridWidth
            ||
            manifest.heightTileGridHeight !=
            worldSettings.HeightTileGridHeight
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            )
        )
        {
            return false;
        }

        if (
            manifest.heightTileSamplesPerSide !=
            worldSettings.HeightTileSamplesPerSide
        )
        {
            return false;
        }

        if (
            manifest.heightSeed !=
            worldSettings.heightSeed
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.heightNoiseScale,
                worldSettings.heightNoiseScale
            )
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.heightBaseHeight,
                worldSettings.heightBaseHeight
            )
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.heightAmplitude,
                worldSettings.heightAmplitude
            )
        )
        {
            return false;
        }

        if (
            manifest.heightOctaves !=
            worldSettings.heightOctaves
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.heightPersistence,
                worldSettings.heightPersistence
            )
        )
        {
            return false;
        }

        if (
            !FloatMatches(
                manifest.heightLacunarity,
                worldSettings.heightLacunarity
            )
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // FLOAT COMPARISON
    // =====================================================

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