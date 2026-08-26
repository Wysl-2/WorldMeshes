using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightApplicator
{
    // =====================================================
    // PATHS
    // =====================================================

    private const string ChunkMeshFolder =
        WorldMeshesPaths.GeneratedChunkMeshes;

    // =====================================================
    // VALIDATION
    // =====================================================

    private const float SettingsFloatTolerance =
        0.0001f;

    /*
     * Number of height tiles kept as CPU float arrays
     * while applying terrain.
     *
     * Processing chunks by height tile means a 3 x 3
     * neighborhood is enough for the current terrain
     * and its immediate neighbors.
     */
    private const int HeightTileCacheCapacity =
        9;

    // =====================================================
    // APPLY HEIGHTMAPS
    // =====================================================

    public static void ApplyHeightmaps(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot apply heightmaps: " +
                "WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Terrain height must be applied " +
                "outside Play Mode."
            );

            return;
        }

        // -------------------------------------------------
        // Current settings
        // -------------------------------------------------

        int gridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        int gridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        int resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        int tileChunkSpan =
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            );

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerTile =
            worldSettings.HeightTileSamplesPerSide;

        int tileIntervals =
            samplesPerTile - 1;

        float sampleSpacing =
            chunkSize /
            resolution;

        int worldIntervalsX =
            gridWidth *
            resolution;

        int worldIntervalsZ =
            gridHeight *
            resolution;

        int totalChunks =
            gridWidth *
            gridHeight;

        // -------------------------------------------------
        // Verify chunk-generation state
        // -------------------------------------------------

        if (
            Mathf.Abs(
                worldSettings.lastSyncedChunkSize -
                chunkSize
            ) >
            SettingsFloatTolerance
            ||
            worldSettings.lastSyncedLOD0Resolution !=
            resolution
        )
        {
            Debug.LogError(
                "Cannot apply heightmaps.\n\n" +

                "The generated chunk meshes are not " +
                "synchronized with the current " +
                "WorldSettings.\n\n" +

                $"Current Chunk Size: " +
                $"{chunkSize}\n" +

                $"Last Synced Chunk Size: " +
                $"{worldSettings.lastSyncedChunkSize}\n\n" +

                $"Current LOD0 Resolution: " +
                $"{resolution}\n" +

                $"Last Synced LOD0 Resolution: " +
                $"{worldSettings.lastSyncedLOD0Resolution}\n\n" +

                "Run Sync Chunk Meshes first."
            );

            return;
        }

        // -------------------------------------------------
        // Verify heightmap manifest
        // -------------------------------------------------

        if (
            !ValidateHeightmapManifest(
                worldSettings
            )
        )
        {
            return;
        }

        // -------------------------------------------------
        // Verify heightmap tile assets
        // -------------------------------------------------

        if (
            !ValidateHeightmapTileAssets(
                worldSettings,
                out bool heightValidationCancelled
            )
        )
        {
            if (heightValidationCancelled)
            {
                Debug.LogWarning(
                    "Terrain height application cancelled " +
                    "during heightmap validation.\n\n" +

                    "No terrain mesh assets were changed."
                );
            }

            return;
        }

        // -------------------------------------------------
        // Verify chunk meshes BEFORE modifying anything
        // -------------------------------------------------

        if (
            !ValidateChunkMeshes(
                gridWidth,
                gridHeight,
                chunkSize,
                resolution,
                sampleSpacing,
                out bool chunkValidationCancelled
            )
        )
        {
            if (chunkValidationCancelled)
            {
                Debug.LogWarning(
                    "Terrain height application cancelled " +
                    "during chunk mesh validation.\n\n" +

                    "No terrain mesh assets were changed."
                );
            }

            return;
        }

        // -------------------------------------------------
        // Height tile cache
        // -------------------------------------------------

        HeightTileCache heightCache =
            new HeightTileCache(
                tileGridWidth,
                tileGridHeight,
                samplesPerTile,
                tileIntervals,
                worldIntervalsX,
                worldIntervalsZ,
                HeightTileCacheCapacity
            );

        // -------------------------------------------------
        // Apply
        // -------------------------------------------------

        int appliedCount =
            0;

        int failedCount =
            0;

        bool cancelled =
            false;

        int currentChunk =
            0;

        try
        {
            /*
             * Process terrain grouped by height tile.
             *
             * This keeps the current height tile and its
             * neighbors hot in the small height-data cache.
             */

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
                    for (
                        int localChunkZ = 0;
                        localChunkZ < tileChunkSpan;
                        localChunkZ++
                    )
                    {
                        for (
                            int localChunkX = 0;
                            localChunkX < tileChunkSpan;
                            localChunkX++
                        )
                        {
                            int chunkX =
                                tileX *
                                tileChunkSpan +
                                localChunkX;

                            int chunkZ =
                                tileZ *
                                tileChunkSpan +
                                localChunkZ;

                            /*
                             * Final height tiles can extend
                             * beyond the actual world grid.
                             */
                            if (
                                chunkX >= gridWidth ||
                                chunkZ >= gridHeight
                            )
                            {
                                continue;
                            }

                            cancelled =
                                ShowProgress(
                                    "Applying Heightmaps",

                                    $"Chunk " +
                                    $"({chunkX}, {chunkZ})\n" +

                                    $"{currentChunk + 1} / " +
                                    $"{totalChunks}",

                                    currentChunk,
                                    totalChunks
                                );

                            if (cancelled)
                            {
                                break;
                            }

                            bool success =
                                ApplyHeightToChunk(
                                    chunkX,
                                    chunkZ,

                                    chunkSize,
                                    resolution,
                                    sampleSpacing,

                                    worldIntervalsX,
                                    worldIntervalsZ,

                                    heightCache
                                );

                            if (!success)
                            {
                                failedCount++;

                                break;
                            }

                            appliedCount++;
                            currentChunk++;
                        }

                        if (
                            cancelled ||
                            failedCount > 0
                        )
                        {
                            break;
                        }
                    }

                    if (
                        cancelled ||
                        failedCount > 0
                    )
                    {
                        break;
                    }
                }

                if (
                    cancelled ||
                    failedCount > 0
                )
                {
                    break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // -------------------------------------------------
        // Cancelled
        // -------------------------------------------------

        if (cancelled)
        {
            Debug.LogWarning(
                "Terrain height application cancelled.\n\n" +

                $"Applied: " +
                $"{appliedCount} / " +
                $"{totalChunks}\n\n" +

                "Chunks already processed were saved.\n" +
                "Run Apply Heightmaps again to complete " +
                "the remaining terrain."
            );

            return;
        }

        // -------------------------------------------------
        // Failed
        // -------------------------------------------------

        if (failedCount > 0)
        {
            Debug.LogError(
                "Terrain height application stopped " +
                "because a chunk could not be updated.\n\n" +

                $"Successfully Applied: " +
                $"{appliedCount}\n" +

                $"Failed: " +
                $"{failedCount}\n\n" +

                "Chunks processed before the failure " +
                "were saved."
            );

            return;
        }

        // -------------------------------------------------
        // Final save
        // -------------------------------------------------

                AssetDatabase.SaveAssets();

        // -------------------------------------------------
        // Record successful height application state
        // -------------------------------------------------

                bool generationStateRecorded =
                    TerrainGenerationStateUtility
                        .MarkHeightApplicationSuccessful(
                            worldSettings
                        );

                if (!generationStateRecorded)
                {
                    Debug.LogError(
                        "Terrain meshes were updated, but the " +
                        "height-application generation state could not " +
                        "be recorded."
                    );

                    return;
                }

                AssetDatabase.SaveAssets();

        // -------------------------------------------------
        // Complete
        // -------------------------------------------------

        Debug.Log(
            "Terrain height application complete.\n\n" +

            $"World Grid: " +
            $"{gridWidth} x " +
            $"{gridHeight}\n" +

            $"Chunk Size: " +
            $"{chunkSize}\n" +

            $"LOD0 Resolution: " +
            $"{resolution}\n\n" +

            $"Height Tile Grid: " +
            $"{tileGridWidth} x " +
            $"{tileGridHeight}\n" +

            $"Height Tile Samples: " +
            $"{samplesPerTile} x " +
            $"{samplesPerTile}\n" +

            $"Sample Spacing: " +
            $"{sampleSpacing}\n\n" +

            $"Chunks Applied: " +
            $"{appliedCount}\n\n" +

            "Terrain vertex heights and global " +
            "heightfield normals were applied successfully."
        );
    }

    // =====================================================
    // APPLY ONE CHUNK
    // =====================================================

    private static bool ApplyHeightToChunk(
        int chunkX,
        int chunkZ,

        float chunkSize,
        int resolution,
        float sampleSpacing,

        int worldIntervalsX,
        int worldIntervalsZ,

        HeightTileCache heightCache
    )
    {
        string meshPath =
            GetChunkMeshPath(
                chunkX,
                chunkZ
            );

        Mesh mesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                meshPath
            );

        if (mesh == null)
        {
            Debug.LogError(
                "Could not load terrain chunk mesh:\n" +
                meshPath
            );

            return false;
        }

        Vector3[] vertices =
            mesh.vertices;

        Vector3[] normals =
            new Vector3[
                vertices.Length
            ];

        float topologyTolerance =
            Mathf.Max(
                0.0001f,
                sampleSpacing *
                0.001f
            );

        // -------------------------------------------------
        // Process vertices
        // -------------------------------------------------

        for (
            int vertexIndex = 0;
            vertexIndex < vertices.Length;
            vertexIndex++
        )
        {
            Vector3 vertex =
                vertices[
                    vertexIndex
                ];

            // ---------------------------------------------
            // Convert local X/Z into mesh sample indices
            // ---------------------------------------------

            if (
                !TryGetVertexSampleCoordinates(
                    vertex,
                    resolution,
                    sampleSpacing,
                    topologyTolerance,

                    out int localSampleX,
                    out int localSampleZ
                )
            )
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains a " +
                    "vertex that is not aligned with the " +
                    "expected terrain sample grid.\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Vertex Position: " +
                    $"{vertex}"
                );

                return false;
            }

            // ---------------------------------------------
            // Global heightfield coordinate
            // ---------------------------------------------

            int globalSampleX =
                chunkX *
                resolution +
                localSampleX;

            int globalSampleZ =
                chunkZ *
                resolution +
                localSampleZ;

            // ---------------------------------------------
            // Height
            // ---------------------------------------------

            if (
                !heightCache.TryGetHeight(
                    globalSampleX,
                    globalSampleZ,
                    out float height
                )
            )
            {
                Debug.LogError(
                    "Could not read height data for " +
                    $"terrain chunk " +
                    $"({chunkX}, {chunkZ}).\n\n" +

                    $"Global Sample: " +
                    $"({globalSampleX}, " +
                    $"{globalSampleZ})"
                );

                return false;
            }

            vertex.y =
                height;

            vertices[
                vertexIndex
            ] =
                vertex;

            // ---------------------------------------------
            // Seam-safe normal
            // ---------------------------------------------

            if (
                !TryCalculateHeightfieldNormal(
                    globalSampleX,
                    globalSampleZ,

                    height,

                    worldIntervalsX,
                    worldIntervalsZ,

                    sampleSpacing,

                    heightCache,

                    out Vector3 normal
                )
            )
            {
                Debug.LogError(
                    "Could not calculate terrain normal " +
                    $"for chunk " +
                    $"({chunkX}, {chunkZ}).\n\n" +

                    $"Global Sample: " +
                    $"({globalSampleX}, " +
                    $"{globalSampleZ})"
                );

                return false;
            }

            normals[
                vertexIndex
            ] =
                normal;
        }

        // -------------------------------------------------
        // Update mesh
        // -------------------------------------------------

        mesh.vertices =
            vertices;

        /*
         * Do NOT use RecalculateNormals here.
         *
         * Normals have already been calculated from the
         * global tiled heightfield so shared chunk-edge
         * vertices receive the same normal.
         */

        mesh.SetNormals(
            normals
        );

        /*
         * Tangents depend on positions, normals and UVs.
         */
        mesh.RecalculateTangents();

        /*
         * Height changes alter the mesh's Y bounds.
         */
        mesh.RecalculateBounds();

        // -------------------------------------------------
        // Verify
        // -------------------------------------------------

        if (
            mesh.vertexCount !=
            vertices.Length
        )
        {
            Debug.LogError(
                $"Terrain chunk " +
                $"({chunkX}, {chunkZ}) changed vertex " +
                "count unexpectedly during height " +
                "application."
            );

            return false;
        }

        Vector3[] resultingNormals =
            mesh.normals;

        if (
            resultingNormals.Length !=
            mesh.vertexCount
        )
        {
            Debug.LogError(
                $"Terrain chunk " +
                $"({chunkX}, {chunkZ}) does not contain " +
                "one normal per vertex after height " +
                "application."
            );

            return false;
        }

        float boundsTolerance =
            Mathf.Max(
                0.001f,
                chunkSize *
                0.00001f
            );

        bool horizontalBoundsMatch =
            Mathf.Abs(
                mesh.bounds.size.x -
                chunkSize
            ) <= boundsTolerance
            &&
            Mathf.Abs(
                mesh.bounds.size.z -
                chunkSize
            ) <= boundsTolerance;

        if (!horizontalBoundsMatch)
        {
            Debug.LogError(
                $"Terrain chunk " +
                $"({chunkX}, {chunkZ}) has unexpected " +
                "horizontal bounds after height " +
                "application.\n\n" +

                $"Expected X/Z Size: " +
                $"{chunkSize}\n" +

                $"Actual Size: " +
                $"{mesh.bounds.size}"
            );

            return false;
        }

        // -------------------------------------------------
        // Save asset
        // -------------------------------------------------

        EditorUtility.SetDirty(
            mesh
        );

        AssetDatabase.SaveAssetIfDirty(
            mesh
        );

        return true;
    }

    // =====================================================
    // HEIGHTFIELD NORMAL
    // =====================================================

    private static bool TryCalculateHeightfieldNormal(
        int sampleX,
        int sampleZ,

        float centerHeight,

        int worldIntervalsX,
        int worldIntervalsZ,

        float sampleSpacing,

        HeightTileCache heightCache,

        out Vector3 normal
    )
    {
        normal =
            Vector3.up;

        if (
            sampleSpacing <= 0f
        )
        {
            return false;
        }

        float slopeX;
        float slopeZ;

        // =================================================
        // X DERIVATIVE
        // =================================================

        if (sampleX <= 0)
        {
            if (
                !heightCache.TryGetHeight(
                    sampleX + 1,
                    sampleZ,
                    out float rightHeight
                )
            )
            {
                return false;
            }

            slopeX =
                (
                    rightHeight -
                    centerHeight
                )
                /
                sampleSpacing;
        }
        else if (
            sampleX >=
            worldIntervalsX
        )
        {
            if (
                !heightCache.TryGetHeight(
                    sampleX - 1,
                    sampleZ,
                    out float leftHeight
                )
            )
            {
                return false;
            }

            slopeX =
                (
                    centerHeight -
                    leftHeight
                )
                /
                sampleSpacing;
        }
        else
        {
            if (
                !heightCache.TryGetHeight(
                    sampleX - 1,
                    sampleZ,
                    out float leftHeight
                )
                ||
                !heightCache.TryGetHeight(
                    sampleX + 1,
                    sampleZ,
                    out float rightHeight
                )
            )
            {
                return false;
            }

            slopeX =
                (
                    rightHeight -
                    leftHeight
                )
                /
                (
                    2f *
                    sampleSpacing
                );
        }

        // =================================================
        // Z DERIVATIVE
        // =================================================

        if (sampleZ <= 0)
        {
            if (
                !heightCache.TryGetHeight(
                    sampleX,
                    sampleZ + 1,
                    out float forwardHeight
                )
            )
            {
                return false;
            }

            slopeZ =
                (
                    forwardHeight -
                    centerHeight
                )
                /
                sampleSpacing;
        }
        else if (
            sampleZ >=
            worldIntervalsZ
        )
        {
            if (
                !heightCache.TryGetHeight(
                    sampleX,
                    sampleZ - 1,
                    out float backwardHeight
                )
            )
            {
                return false;
            }

            slopeZ =
                (
                    centerHeight -
                    backwardHeight
                )
                /
                sampleSpacing;
        }
        else
        {
            if (
                !heightCache.TryGetHeight(
                    sampleX,
                    sampleZ - 1,
                    out float backwardHeight
                )
                ||
                !heightCache.TryGetHeight(
                    sampleX,
                    sampleZ + 1,
                    out float forwardHeight
                )
            )
            {
                return false;
            }

            slopeZ =
                (
                    forwardHeight -
                    backwardHeight
                )
                /
                (
                    2f *
                    sampleSpacing
                );
        }

        // -------------------------------------------------
        // Heightfield normal
        // -------------------------------------------------

        Vector3 heightfieldNormal =
            new Vector3(
                -slopeX,
                1f,
                -slopeZ
            );

        if (
            float.IsNaN(
                heightfieldNormal.x
            )
            ||
            float.IsNaN(
                heightfieldNormal.y
            )
            ||
            float.IsNaN(
                heightfieldNormal.z
            )
            ||
            float.IsInfinity(
                heightfieldNormal.x
            )
            ||
            float.IsInfinity(
                heightfieldNormal.y
            )
            ||
            float.IsInfinity(
                heightfieldNormal.z
            )
        )
        {
            return false;
        }

        normal =
            heightfieldNormal.normalized;

        return true;
    }

    // =====================================================
    // VERTEX -> SAMPLE COORDINATE
    // =====================================================

    private static bool TryGetVertexSampleCoordinates(
        Vector3 vertex,

        int resolution,
        float sampleSpacing,
        float tolerance,

        out int sampleX,
        out int sampleZ
    )
    {
        sampleX =
            0;

        sampleZ =
            0;

        if (
            sampleSpacing <= 0f
        )
        {
            return false;
        }

        sampleX =
            Mathf.RoundToInt(
                vertex.x /
                sampleSpacing
            );

        sampleZ =
            Mathf.RoundToInt(
                vertex.z /
                sampleSpacing
            );

        if (
            sampleX < 0 ||
            sampleX > resolution ||
            sampleZ < 0 ||
            sampleZ > resolution
        )
        {
            return false;
        }

        float expectedX =
            sampleX *
            sampleSpacing;

        float expectedZ =
            sampleZ *
            sampleSpacing;

        if (
            Mathf.Abs(
                vertex.x -
                expectedX
            ) > tolerance
            ||
            Mathf.Abs(
                vertex.z -
                expectedZ
            ) > tolerance
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // VALIDATE CHUNK MESH SET
    // =====================================================

    private static bool ValidateChunkMeshes(
        int gridWidth,
        int gridHeight,

        float chunkSize,
        int resolution,
        float sampleSpacing,

        out bool cancelled
    )
    {
        cancelled =
            false;

        int totalChunks =
            gridWidth *
            gridHeight;

        int currentChunk =
            0;

        try
        {
            for (
                int chunkZ = 0;
                chunkZ < gridHeight;
                chunkZ++
            )
            {
                for (
                    int chunkX = 0;
                    chunkX < gridWidth;
                    chunkX++
                )
                {
                    cancelled =
                        ShowProgress(
                            "Validating Chunk Meshes",

                            $"Chunk " +
                            $"({chunkX}, {chunkZ})\n" +

                            $"{currentChunk + 1} / " +
                            $"{totalChunks}",

                            currentChunk,
                            totalChunks
                        );

                    if (cancelled)
                    {
                        return false;
                    }

                    string meshPath =
                        GetChunkMeshPath(
                            chunkX,
                            chunkZ
                        );

                    Mesh mesh =
                        AssetDatabase
                            .LoadAssetAtPath<Mesh>(
                                meshPath
                            );

                    if (mesh == null)
                    {
                        Debug.LogError(
                            "Cannot apply heightmaps.\n\n" +

                            "Required chunk mesh is missing:\n" +
                            meshPath +
                            "\n\nRun Sync Chunk Meshes first."
                        );

                        return false;
                    }

                    if (
                        !ValidateChunkMeshTopology(
                            mesh,

                            chunkX,
                            chunkZ,

                            chunkSize,
                            resolution,
                            sampleSpacing
                        )
                    )
                    {
                        return false;
                    }

                    currentChunk++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return true;
    }

    // =====================================================
    // VALIDATE ONE CHUNK TOPOLOGY
    // =====================================================

    private static bool ValidateChunkMeshTopology(
        Mesh mesh,

        int chunkX,
        int chunkZ,

        float chunkSize,
        int resolution,
        float sampleSpacing
    )
    {
        if (!mesh.isReadable)
        {
            Debug.LogError(
                $"Terrain chunk " +
                $"({chunkX}, {chunkZ}) is not readable."
            );

            return false;
        }

        int verticesPerSide =
            resolution + 1;

        int expectedVertexCount =
            verticesPerSide *
            verticesPerSide;

        if (
            mesh.vertexCount !=
            expectedVertexCount
        )
        {
            Debug.LogError(
                $"Invalid terrain topology for chunk " +
                $"({chunkX}, {chunkZ}).\n\n" +

                $"Expected Vertices: " +
                $"{expectedVertexCount:N0}\n" +

                $"Actual Vertices: " +
                $"{mesh.vertexCount:N0}\n\n" +

                "Run Sync Chunk Meshes first."
            );

            return false;
        }

        // -------------------------------------------------
        // Horizontal dimensions
        // -------------------------------------------------

        float boundsTolerance =
            Mathf.Max(
                0.001f,
                chunkSize *
                0.00001f
            );

        if (
            Mathf.Abs(
                mesh.bounds.size.x -
                chunkSize
            ) > boundsTolerance
            ||
            Mathf.Abs(
                mesh.bounds.size.z -
                chunkSize
            ) > boundsTolerance
        )
        {
            Debug.LogError(
                $"Invalid terrain dimensions for chunk " +
                $"({chunkX}, {chunkZ}).\n\n" +

                $"Expected X/Z Size: " +
                $"{chunkSize}\n" +

                $"Actual Bounds Size: " +
                $"{mesh.bounds.size}\n\n" +

                "Run Sync Chunk Meshes first."
            );

            return false;
        }

        // -------------------------------------------------
        // Triangle/index topology
        // -------------------------------------------------

        long expectedIndexCount =
            (long)resolution *
            resolution *
            6L;

        long actualIndexCount =
            0L;

        for (
            int subMeshIndex = 0;
            subMeshIndex < mesh.subMeshCount;
            subMeshIndex++
        )
        {
            actualIndexCount +=
                mesh.GetIndexCount(
                    subMeshIndex
                );
        }

        if (
            actualIndexCount !=
            expectedIndexCount
        )
        {
            Debug.LogError(
                $"Invalid triangle topology for chunk " +
                $"({chunkX}, {chunkZ}).\n\n" +

                $"Expected Triangle Indices: " +
                $"{expectedIndexCount:N0}\n" +

                $"Actual Triangle Indices: " +
                $"{actualIndexCount:N0}"
            );

            return false;
        }

        // -------------------------------------------------
        // Verify regular X/Z sample grid
        // -------------------------------------------------

        Vector3[] vertices =
            mesh.vertices;

        bool[] occupiedSamples =
            new bool[
                expectedVertexCount
            ];

        float topologyTolerance =
            Mathf.Max(
                0.0001f,
                sampleSpacing *
                0.001f
            );

        for (
            int vertexIndex = 0;
            vertexIndex < vertices.Length;
            vertexIndex++
        )
        {
            if (
                !TryGetVertexSampleCoordinates(
                    vertices[
                        vertexIndex
                    ],

                    resolution,
                    sampleSpacing,
                    topologyTolerance,

                    out int sampleX,
                    out int sampleZ
                )
            )
            {
                Debug.LogError(
                    $"Invalid terrain vertex layout for " +
                    $"chunk ({chunkX}, {chunkZ}).\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Position: " +
                    $"{vertices[vertexIndex]}\n\n" +

                    "The X/Z coordinates do not align " +
                    "with the expected terrain sample grid."
                );

                return false;
            }

            int sampleIndex =
                sampleZ *
                verticesPerSide +
                sampleX;

            if (
                occupiedSamples[
                    sampleIndex
                ]
            )
            {
                Debug.LogError(
                    $"Invalid terrain topology for chunk " +
                    $"({chunkX}, {chunkZ}).\n\n" +

                    $"More than one vertex maps to sample " +
                    $"({sampleX}, {sampleZ})."
                );

                return false;
            }

            occupiedSamples[
                sampleIndex
            ] =
                true;
        }

        return true;
    }

    // =====================================================
    // VALIDATE HEIGHTMAP TILE ASSETS
    // =====================================================

    private static bool ValidateHeightmapTileAssets(
        WorldSettings worldSettings,
        out bool cancelled
    )
    {
        cancelled =
            false;

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        int totalTiles =
            tileGridWidth *
            tileGridHeight;

        int currentTile =
            0;

        try
        {
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
                    cancelled =
                        ShowProgress(
                            "Validating Heightmap Tiles",

                            $"Tile " +
                            $"({tileX}, {tileZ})\n" +

                            $"{currentTile + 1} / " +
                            $"{totalTiles}",

                            currentTile,
                            totalTiles
                        );

                    if (cancelled)
                    {
                        return false;
                    }

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
                            "Required heightmap tile " +
                            "is missing:\n" +
                            path
                        );

                        return false;
                    }

                    bool valid =
                        true;

                    if (
                        texture.width !=
                            samplesPerSide
                        ||
                        texture.height !=
                            samplesPerSide
                    )
                    {
                        Debug.LogError(
                            $"Invalid heightmap dimensions " +
                            $"for tile " +
                            $"({tileX}, {tileZ}).\n\n" +

                            $"Expected: " +
                            $"{samplesPerSide} x " +
                            $"{samplesPerSide}\n" +

                            $"Actual: " +
                            $"{texture.width} x " +
                            $"{texture.height}"
                        );

                        valid =
                            false;
                    }

                    if (
                        texture.format !=
                        TextureFormat.RFloat
                    )
                    {
                        Debug.LogError(
                            $"Invalid heightmap format for " +
                            $"tile ({tileX}, {tileZ}).\n\n" +

                            $"Expected: RFloat\n" +
                            $"Actual: {texture.format}"
                        );

                        valid =
                            false;
                    }

                    if (!texture.isReadable)
                    {
                        Debug.LogError(
                            $"Heightmap tile " +
                            $"({tileX}, {tileZ}) is not readable."
                        );

                        valid =
                            false;
                    }

                    if (valid)
                    {
                        try
                        {
                            var pixelData =
                                texture
                                    .GetPixelData<float>(
                                        0
                                    );

                            if (
                                pixelData.Length !=
                                expectedSampleCount
                            )
                            {
                                Debug.LogError(
                                    $"Invalid sample count " +
                                    $"for heightmap tile " +
                                    $"({tileX}, {tileZ}).\n\n" +

                                    $"Expected: " +
                                    $"{expectedSampleCount:N0}\n" +

                                    $"Actual: " +
                                    $"{pixelData.Length:N0}"
                                );

                                valid =
                                    false;
                            }
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

                            valid =
                                false;
                        }
                    }

                    /*
                     * The texture is only being inspected
                     * here. The height cache will load it
                     * again later if needed.
                     */

                    Resources.UnloadAsset(
                        texture
                    );

                    if (!valid)
                    {
                        return false;
                    }

                    currentTile++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return true;
    }

    // =====================================================
    // HEIGHTMAP MANIFEST
    // =====================================================

    private static bool ValidateHeightmapManifest(
    WorldSettings worldSettings
)
{
    TerrainHeightmapManifest manifest =
        AssetDatabase
            .LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
            );

    // -------------------------------------------------
    // Manifest exists
    // -------------------------------------------------

    if (manifest == null)
    {
        Debug.LogError(
            "Cannot apply heightmaps.\n\n" +

            "Runtime heightmap manifest does not exist.\n\n" +

            "Compile the runtime heightmaps first."
        );

        return false;
    }

    // -------------------------------------------------
    // Manifest is complete
    // -------------------------------------------------

    if (!manifest.isComplete)
    {
        Debug.LogError(
            "Cannot apply heightmaps.\n\n" +

            "The runtime heightmap manifest is marked " +
            "as incomplete.\n\n" +

            "Compile the runtime heightmaps again."
        );

        return false;
    }

    // -------------------------------------------------
    // Compiler version
    // -------------------------------------------------

    if (
        manifest.compilerVersion !=
        TerrainGenerationStateUtility
            .RuntimeHeightCompilerVersion
    )
    {
        Debug.LogError(
            "Cannot apply heightmaps.\n\n" +

            "The runtime heightmaps were produced by an " +
            "out-of-date heightmap compiler.\n\n" +

            "Compile the runtime heightmaps again."
        );

        return false;
    }

    // -------------------------------------------------
    // World / heightfield layout
    // -------------------------------------------------

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
        Debug.LogError(
            "Cannot apply heightmaps.\n\n" +

            "The compiled runtime heightmap layout does not " +
            "match the current WorldSettings.\n\n" +

            "Reinitialize the authoring heightfield if the " +
            "heightfield layout changed, then compile the " +
            "runtime heightmaps again."
        );

        return false;
    }

    // -------------------------------------------------
    // Authoring -> runtime generation state
    // -------------------------------------------------

    TerrainGenerationStateUtility.GenerationStatus
        heightmapStatus =
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                );

    if (
        heightmapStatus !=
        TerrainGenerationStateUtility
            .GenerationStatus.Current
    )
    {
        Debug.LogError(
            "Cannot apply heightmaps.\n\n" +

            "The compiled runtime heightmaps are not current " +
            "with the committed authoring heightfield.\n\n" +

            $"Heightmap State: " +
            $"{TerrainGenerationStateUtility.GetStatusLabel(heightmapStatus)}\n\n" +

            "Compile the runtime heightmaps again before " +
            "applying them to the chunk meshes."
        );

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

    // =====================================================
    // CHUNK PATH
    // =====================================================

    private static string GetChunkMeshPath(
        int chunkX,
        int chunkZ
    )
    {
        return
            $"{ChunkMeshFolder}/" +
            $"Chunk_{chunkX}_{chunkZ}_LOD0.asset";
    }

    // =====================================================
    // PROGRESS
    // =====================================================

    private static bool ShowProgress(
        string operation,
        string detail,
        int current,
        int total
    )
    {
        float progress =
            total > 0
                ? (float)current /
                  total
                : 1f;

        return
            EditorUtility
                .DisplayCancelableProgressBar(
                    "Terrain Height Application",

                    operation +
                    "\n\n" +
                    detail,

                    progress
                );
    }

    // =====================================================
    // HEIGHT TILE CACHE
    // =====================================================

    private sealed class HeightTileCache
    {
        private readonly int tileGridWidth;
        private readonly int tileGridHeight;

        private readonly int samplesPerSide;
        private readonly int intervalsPerTile;

        private readonly int worldIntervalsX;
        private readonly int worldIntervalsZ;

        private readonly int capacity;

        private readonly Dictionary
            <Vector2Int, CacheEntry> cache =
                new Dictionary
                    <Vector2Int, CacheEntry>();

        private readonly LinkedList<Vector2Int>
            accessOrder =
                new LinkedList<Vector2Int>();

        // -------------------------------------------------
        // Constructor
        // -------------------------------------------------

        public HeightTileCache(
            int tileGridWidth,
            int tileGridHeight,

            int samplesPerSide,
            int intervalsPerTile,

            int worldIntervalsX,
            int worldIntervalsZ,

            int capacity
        )
        {
            this.tileGridWidth =
                tileGridWidth;

            this.tileGridHeight =
                tileGridHeight;

            this.samplesPerSide =
                samplesPerSide;

            this.intervalsPerTile =
                intervalsPerTile;

            this.worldIntervalsX =
                worldIntervalsX;

            this.worldIntervalsZ =
                worldIntervalsZ;

            this.capacity =
                Mathf.Max(
                    1,
                    capacity
                );
        }

        // =================================================
        // SAMPLE GLOBAL HEIGHT
        // =================================================

        public bool TryGetHeight(
            int globalSampleX,
            int globalSampleZ,
            out float height
        )
        {
            height =
                0f;

            // ---------------------------------------------
            // World bounds
            // ---------------------------------------------

            if (
                globalSampleX < 0 ||
                globalSampleZ < 0 ||
                globalSampleX >
                    worldIntervalsX ||
                globalSampleZ >
                    worldIntervalsZ
            )
            {
                return false;
            }

            // ---------------------------------------------
            // Global sample -> height tile
            // ---------------------------------------------

            int tileX =
                globalSampleX /
                intervalsPerTile;

            int tileZ =
                globalSampleZ /
                intervalsPerTile;

            /*
             * The final world sample can lie exactly on
             * the outer boundary of the final tile.
             *
             * Integer division then produces one index
             * beyond the tile grid, so clamp it back to
             * the last real tile.
             */

            tileX =
                Mathf.Clamp(
                    tileX,
                    0,
                    tileGridWidth - 1
                );

            tileZ =
                Mathf.Clamp(
                    tileZ,
                    0,
                    tileGridHeight - 1
                );

            // ---------------------------------------------
            // Tile-local sample coordinate
            // ---------------------------------------------

            int localSampleX =
                globalSampleX -
                tileX *
                intervalsPerTile;

            int localSampleZ =
                globalSampleZ -
                tileZ *
                intervalsPerTile;

            if (
                localSampleX < 0 ||
                localSampleX >=
                    samplesPerSide ||
                localSampleZ < 0 ||
                localSampleZ >=
                    samplesPerSide
            )
            {
                return false;
            }

            // ---------------------------------------------
            // Tile data
            // ---------------------------------------------

            Vector2Int tileCoordinate =
                new Vector2Int(
                    tileX,
                    tileZ
                );

            if (
                !TryGetTileData(
                    tileCoordinate,
                    out float[] tileData
                )
            )
            {
                return false;
            }

            int sampleIndex =
                localSampleZ *
                samplesPerSide +
                localSampleX;

            if (
                sampleIndex < 0 ||
                sampleIndex >=
                    tileData.Length
            )
            {
                return false;
            }

            height =
                tileData[
                    sampleIndex
                ];

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
                Debug.LogError(
                    "Invalid height value encountered.\n\n" +

                    $"Global Sample: " +
                    $"({globalSampleX}, " +
                    $"{globalSampleZ})\n" +

                    $"Height Tile: " +
                    $"({tileX}, {tileZ})\n" +

                    $"Height: {height}"
                );

                return false;
            }

            return true;
        }

        // =================================================
        // GET TILE DATA
        // =================================================

        private bool TryGetTileData(
            Vector2Int coordinate,
            out float[] tileData
        )
        {
            tileData =
                null;

            // ---------------------------------------------
            // Already cached
            // ---------------------------------------------

            if (
                cache.TryGetValue(
                    coordinate,
                    out CacheEntry existingEntry
                )
            )
            {
                Touch(
                    existingEntry
                );

                tileData =
                    existingEntry.data;

                return true;
            }

            // ---------------------------------------------
            // Load heightmap asset
            // ---------------------------------------------

            string path =
                TerrainRuntimeHeightAssetUtility
                    .GetHeightTilePath(
                        coordinate.x,
                        coordinate.y
                    );

            Texture2D texture =
                AssetDatabase
                    .LoadAssetAtPath<Texture2D>(
                        path
                    );

            if (texture == null)
            {
                Debug.LogError(
                    "Could not load heightmap tile:\n" +
                    path
                );

                return false;
            }

            float[] copiedData =
                null;

            try
            {
                if (
                    texture.width !=
                        samplesPerSide
                    ||
                    texture.height !=
                        samplesPerSide
                    ||
                    texture.format !=
                        TextureFormat.RFloat
                    ||
                    !texture.isReadable
                )
                {
                    Debug.LogError(
                        "Heightmap tile became invalid " +
                        "during terrain application:\n" +
                        path
                    );

                    return false;
                }

                var pixelData =
                    texture
                        .GetPixelData<float>(
                            0
                        );

                int expectedCount =
                    samplesPerSide *
                    samplesPerSide;

                if (
                    pixelData.Length !=
                    expectedCount
                )
                {
                    Debug.LogError(
                        "Unexpected heightmap sample " +
                        "count:\n" +
                        path
                    );

                    return false;
                }

                copiedData =
                    new float[
                        pixelData.Length
                    ];

                /*
                 * GetPixelData points directly into the
                 * texture's CPU data.
                 *
                 * Copy it before unloading the Texture2D
                 * so the cached height data remains valid.
                 */

                for (
                    int i = 0;
                    i < pixelData.Length;
                    i++
                )
                {
                    copiedData[i] =
                        pixelData[i];
                }
            }
            catch (
                System.Exception exception
            )
            {
                Debug.LogError(
                    "Could not read heightmap tile:\n" +
                    path +
                    "\n\n" +
                    exception.Message
                );

                return false;
            }
            finally
            {
                Resources.UnloadAsset(
                    texture
                );
            }

            // ---------------------------------------------
            // Evict old data if necessary
            // ---------------------------------------------

            while (
                cache.Count >=
                capacity
            )
            {
                EvictLeastRecentlyUsed();
            }

            // ---------------------------------------------
            // Add cache entry
            // ---------------------------------------------

            LinkedListNode<Vector2Int> node =
                accessOrder.AddFirst(
                    coordinate
                );

            CacheEntry entry =
                new CacheEntry(
                    copiedData,
                    node
                );

            cache.Add(
                coordinate,
                entry
            );

            tileData =
                copiedData;

            return true;
        }

        // =================================================
        // TOUCH LRU ENTRY
        // =================================================

        private void Touch(
            CacheEntry entry
        )
        {
            accessOrder.Remove(
                entry.node
            );

            accessOrder.AddFirst(
                entry.node
            );
        }

        // =================================================
        // EVICT
        // =================================================

        private void EvictLeastRecentlyUsed()
        {
            LinkedListNode<Vector2Int> node =
                accessOrder.Last;

            if (node == null)
            {
                return;
            }

            Vector2Int coordinate =
                node.Value;

            accessOrder.RemoveLast();

            cache.Remove(
                coordinate
            );
        }

        // =================================================
        // CACHE ENTRY
        // =================================================

        private sealed class CacheEntry
        {
            public readonly float[] data;

            public readonly
                LinkedListNode<Vector2Int> node;

            public CacheEntry(
                float[] data,
                LinkedListNode<Vector2Int> node
            )
            {
                this.data =
                    data;

                this.node =
                    node;
            }
        }
    }
}