using UnityEditor;
using UnityEngine;

public static class TerrainMeshSeamValidator
{
    // =====================================================
    // PATHS
    // =====================================================

    private const string ChunkMeshFolder =
        WorldMeshesPaths.GeneratedChunkMeshes;

    // =====================================================
    // TOLERANCES
    // =====================================================

    /*
     * The generated terrain should normally produce
     * differences of exactly zero.
     *
     * Small tolerances are still used to avoid treating
     * harmless floating-point differences as failures.
     */

    private const float SettingsFloatTolerance =
        0.0001f;

    private const float PositionTolerance =
        0.0001f;

    private const float HeightTolerance =
        0.0001f;

    private const float NormalTolerance =
        0.0001f;

    private const float NormalMagnitudeTolerance =
        0.002f;

    // =====================================================
    // EDGE
    // =====================================================

    private enum EdgeSide
    {
        Left,
        Right,
        Back,
        Forward
    }

    // =====================================================
    // VALIDATE MESH SEAMS
    // =====================================================

    public static bool ValidateMeshSeams(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate terrain mesh seams: " +
                "WorldSettings is null."
            );

            return false;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Terrain mesh seam validation must be " +
                "performed outside Play Mode."
            );

            return false;
        }

        // -------------------------------------------------
        // Current world layout
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

        float sampleSpacing =
            chunkSize /
            resolution;

        int samplesPerEdge =
            resolution + 1;

        // -------------------------------------------------
        // Verify chunk generation state
        // -------------------------------------------------

        bool chunkSizeMatches =
            Mathf.Abs(
                worldSettings.lastSyncedChunkSize -
                chunkSize
            )
            <=
            SettingsFloatTolerance;

        bool resolutionMatches =
            worldSettings.lastSyncedLOD0Resolution ==
            resolution;

        if (
            !chunkSizeMatches ||
            !resolutionMatches
        )
        {
            Debug.LogError(
                "Cannot validate terrain mesh seams.\n\n" +

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

                "Run Sync Chunk Meshes and then apply " +
                "the heightmaps again."
            );

            return false;
        }

        // -------------------------------------------------
        // Validate every mesh before comparing seams
        // -------------------------------------------------

        if (
            !ValidateAllChunkMeshes(
                gridWidth,
                gridHeight,
                chunkSize,
                resolution,
                sampleSpacing,
                out bool meshValidationCancelled
            )
        )
        {
            if (meshValidationCancelled)
            {
                Debug.LogWarning(
                    "Terrain mesh seam validation " +
                    "was cancelled.\n\n" +

                    "No assets were modified."
                );
            }

            return false;
        }

        // -------------------------------------------------
        // Compare shared edges
        // -------------------------------------------------

        if (
            !CompareAllSharedEdges(
                gridWidth,
                gridHeight,
                chunkSize,
                resolution,
                sampleSpacing,

                out ValidationStatistics statistics,
                out bool comparisonCancelled
            )
        )
        {
            if (comparisonCancelled)
            {
                Debug.LogWarning(
                    "Terrain mesh seam validation " +
                    "was cancelled.\n\n" +

                    "No assets were modified."
                );

                return false;
            }

            return false;
        }

        // -------------------------------------------------
        // Failed seams
        // -------------------------------------------------

        if (
            statistics.mismatchedSampleCount > 0
        )
        {
            Debug.LogError(
                "Terrain mesh seam validation failed.\n\n" +

                $"World Grid: " +
                $"{gridWidth} x {gridHeight}\n" +

                $"LOD0 Resolution: " +
                $"{resolution}\n" +

                $"Samples Per Edge: " +
                $"{samplesPerEdge}\n\n" +

                $"Boundary Pairs: " +
                $"{statistics.boundaryPairCount:N0}\n" +

                $"Shared Vertices Compared: " +
                $"{statistics.sampleComparisonCount:N0}\n\n" +

                $"Mismatched Shared Vertices: " +
                $"{statistics.mismatchedSampleCount:N0}\n" +

                $"Position Mismatches: " +
                $"{statistics.positionMismatchCount:N0}\n" +

                $"Height Mismatches: " +
                $"{statistics.heightMismatchCount:N0}\n" +

                $"Normal Mismatches: " +
                $"{statistics.normalMismatchCount:N0}\n\n" +

                $"Maximum Position Difference: " +
                $"{statistics.maximumPositionDifference:R}\n" +

                $"Maximum Height Difference: " +
                $"{statistics.maximumHeightDifference:R}\n" +

                $"Maximum Normal Difference: " +
                $"{statistics.maximumNormalDifference:R}\n" +

                $"Maximum Normal Angle: " +
                $"{statistics.maximumNormalAngle:R} degrees\n\n" +

                $"First Mismatch:\n" +
                $"{statistics.firstMismatch}"
            );

            return false;
        }

        // -------------------------------------------------
        // Success
        // -------------------------------------------------

        Debug.Log(
            "Terrain mesh seam validation passed.\n\n" +

            $"World Grid: " +
            $"{gridWidth} x {gridHeight}\n" +

            $"LOD0 Resolution: " +
            $"{resolution}\n" +

            $"Samples Per Edge: " +
            $"{samplesPerEdge}\n\n" +

            $"Boundary Pairs: " +
            $"{statistics.boundaryPairCount:N0}\n" +

            $"Shared Vertices Compared: " +
            $"{statistics.sampleComparisonCount:N0}\n\n" +

            $"Position Mismatches: 0\n" +
            $"Height Mismatches: 0\n" +
            $"Normal Mismatches: 0\n\n" +

            $"Maximum Position Difference: " +
            $"{statistics.maximumPositionDifference:R}\n" +

            $"Maximum Height Difference: " +
            $"{statistics.maximumHeightDifference:R}\n" +

            $"Maximum Normal Difference: " +
            $"{statistics.maximumNormalDifference:R}\n" +

            $"Maximum Normal Angle: " +
            $"{statistics.maximumNormalAngle:R} degrees\n\n" +

            "All shared terrain chunk edges have " +
            "matching geometry and normals."
        );

        return true;
    }

    // =====================================================
    // VALIDATE ALL CHUNK MESHES
    // =====================================================

    private static bool ValidateAllChunkMeshes(
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
                            "Validating terrain chunk meshes",

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
                            "Cannot validate terrain seams.\n\n" +

                            "Required chunk mesh is missing:\n" +
                            meshPath
                        );

                        return false;
                    }

                    if (
                        !ValidateChunkMesh(
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
    // VALIDATE ONE CHUNK
    // =====================================================

    private static bool ValidateChunkMesh(
        Mesh mesh,

        int chunkX,
        int chunkZ,

        float chunkSize,
        int resolution,
        float sampleSpacing
    )
    {
        // -------------------------------------------------
        // Readability
        // -------------------------------------------------

        if (!mesh.isReadable)
        {
            Debug.LogError(
                $"Terrain chunk " +
                $"({chunkX}, {chunkZ}) is not readable."
            );

            return false;
        }

        // -------------------------------------------------
        // Expected vertex count
        // -------------------------------------------------

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
                $"Invalid vertex count for terrain chunk " +
                $"({chunkX}, {chunkZ}).\n\n" +

                $"Expected: " +
                $"{expectedVertexCount:N0}\n" +

                $"Actual: " +
                $"{mesh.vertexCount:N0}"
            );

            return false;
        }

        // -------------------------------------------------
        // Horizontal bounds
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
            ) >
            boundsTolerance
            ||
            Mathf.Abs(
                mesh.bounds.size.z -
                chunkSize
            ) >
            boundsTolerance
        )
        {
            Debug.LogError(
                $"Invalid horizontal bounds for terrain " +
                $"chunk ({chunkX}, {chunkZ}).\n\n" +

                $"Expected X/Z Size: " +
                $"{chunkSize}\n" +

                $"Actual Bounds: " +
                $"{mesh.bounds.size}"
            );

            return false;
        }

        // -------------------------------------------------
        // Vertices and normals
        // -------------------------------------------------

        Vector3[] vertices =
            mesh.vertices;

        Vector3[] normals =
            mesh.normals;

        if (
            normals.Length !=
            vertices.Length
        )
        {
            Debug.LogError(
                $"Terrain chunk " +
                $"({chunkX}, {chunkZ}) does not contain " +
                "one normal per vertex.\n\n" +

                $"Vertices: " +
                $"{vertices.Length:N0}\n" +

                $"Normals: " +
                $"{normals.Length:N0}"
            );

            return false;
        }

        /*
         * Mesh.normals is a per-vertex array, so each
         * vertex must have a corresponding normal.
         */

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

        // -------------------------------------------------
        // Validate every vertex
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

            Vector3 normal =
                normals[
                    vertexIndex
                ];

            // ---------------------------------------------
            // Finite vertex
            // ---------------------------------------------

            if (!IsFinite(vertex))
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains an " +
                    "invalid vertex.\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Position: " +
                    $"{vertex}"
                );

                return false;
            }

            // ---------------------------------------------
            // Finite normal
            // ---------------------------------------------

            if (!IsFinite(normal))
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains an " +
                    "invalid normal.\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Normal: " +
                    $"{normal}"
                );

                return false;
            }

            // ---------------------------------------------
            // Normal magnitude
            // ---------------------------------------------

            float normalMagnitudeSquared =
                normal.sqrMagnitude;

            if (
                Mathf.Abs(
                    normalMagnitudeSquared -
                    1f
                )
                >
                NormalMagnitudeTolerance
            )
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains a " +
                    "non-normalized vertex normal.\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Normal: " +
                    $"{normal}\n" +

                    $"Squared Magnitude: " +
                    $"{normalMagnitudeSquared:R}"
                );

                return false;
            }

            // ---------------------------------------------
            // X/Z sample coordinate
            // ---------------------------------------------

            if (
                !TryGetVertexSampleCoordinates(
                    vertex,

                    resolution,
                    sampleSpacing,
                    topologyTolerance,

                    out int sampleX,
                    out int sampleZ
                )
            )
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains a " +
                    "vertex that does not align with the " +
                    "expected terrain sample grid.\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Position: " +
                    $"{vertex}"
                );

                return false;
            }

            // ---------------------------------------------
            // Unique grid point
            // ---------------------------------------------

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
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains " +
                    "multiple vertices mapped to sample " +
                    $"({sampleX}, {sampleZ})."
                );

                return false;
            }

            occupiedSamples[
                sampleIndex
            ] =
                true;
        }

        // -------------------------------------------------
        // Ensure every expected grid point exists
        // -------------------------------------------------

        for (
            int sampleIndex = 0;
            sampleIndex < occupiedSamples.Length;
            sampleIndex++
        )
        {
            if (
                !occupiedSamples[
                    sampleIndex
                ]
            )
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) is missing an " +
                    "expected terrain sample vertex."
                );

                return false;
            }
        }

        return true;
    }

    // =====================================================
    // COMPARE ALL SHARED EDGES
    // =====================================================

    private static bool CompareAllSharedEdges(
        int gridWidth,
        int gridHeight,

        float chunkSize,
        int resolution,
        float sampleSpacing,

        out ValidationStatistics statistics,
        out bool cancelled
    )
    {
        statistics =
            new ValidationStatistics();

        cancelled =
            false;

        int horizontalBoundaryCount =
            Mathf.Max(
                0,
                gridWidth - 1
            )
            *
            gridHeight;

        int verticalBoundaryCount =
            gridWidth
            *
            Mathf.Max(
                0,
                gridHeight - 1
            );

        int totalBoundaryPairs =
            horizontalBoundaryCount +
            verticalBoundaryCount;

        int currentBoundary =
            0;

        try
        {
            // =================================================
            // X-AXIS NEIGHBORS
            //
            // Left chunk right edge
            // vs
            // Right chunk left edge
            // =================================================

            for (
                int chunkZ = 0;
                chunkZ < gridHeight;
                chunkZ++
            )
            {
                for (
                    int chunkX = 0;
                    chunkX < gridWidth - 1;
                    chunkX++
                )
                {
                    cancelled =
                        ShowProgress(
                            "Comparing X-axis chunk seams",

                            $"Chunk " +
                            $"({chunkX}, {chunkZ}) " +
                            "↔ " +
                            $"({chunkX + 1}, {chunkZ})",

                            currentBoundary,
                            totalBoundaryPairs
                        );

                    if (cancelled)
                    {
                        return false;
                    }

                    Mesh leftMesh =
                        LoadChunkMesh(
                            chunkX,
                            chunkZ
                        );

                    Mesh rightMesh =
                        LoadChunkMesh(
                            chunkX + 1,
                            chunkZ
                        );

                    if (
                        leftMesh == null ||
                        rightMesh == null
                    )
                    {
                        return false;
                    }

                    if (
                        !TryExtractEdge(
                            leftMesh,

                            EdgeSide.Right,

                            resolution,
                            sampleSpacing,

                            chunkX,
                            chunkZ,

                            out EdgeData leftEdge
                        )
                        ||
                        !TryExtractEdge(
                            rightMesh,

                            EdgeSide.Left,

                            resolution,
                            sampleSpacing,

                            chunkX + 1,
                            chunkZ,

                            out EdgeData rightEdge
                        )
                    )
                    {
                        return false;
                    }

                    /*
                     * Convert the right chunk's left edge
                     * into the left chunk's local space.
                     */

                    Vector3 secondPositionOffset =
                        new Vector3(
                            chunkSize,
                            0f,
                            0f
                        );

                    CompareEdgePair(
                        leftEdge,
                        rightEdge,

                        secondPositionOffset,

                        chunkX,
                        chunkZ,

                        chunkX + 1,
                        chunkZ,

                        "X",

                        statistics
                    );

                    currentBoundary++;
                }
            }

            // =================================================
            // Z-AXIS NEIGHBORS
            //
            // Lower-Z chunk forward edge
            // vs
            // Upper-Z chunk back edge
            // =================================================

            for (
                int chunkZ = 0;
                chunkZ < gridHeight - 1;
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
                            "Comparing Z-axis chunk seams",

                            $"Chunk " +
                            $"({chunkX}, {chunkZ}) " +
                            "↔ " +
                            $"({chunkX}, {chunkZ + 1})",

                            currentBoundary,
                            totalBoundaryPairs
                        );

                    if (cancelled)
                    {
                        return false;
                    }

                    Mesh lowerMesh =
                        LoadChunkMesh(
                            chunkX,
                            chunkZ
                        );

                    Mesh upperMesh =
                        LoadChunkMesh(
                            chunkX,
                            chunkZ + 1
                        );

                    if (
                        lowerMesh == null ||
                        upperMesh == null
                    )
                    {
                        return false;
                    }

                    if (
                        !TryExtractEdge(
                            lowerMesh,

                            EdgeSide.Forward,

                            resolution,
                            sampleSpacing,

                            chunkX,
                            chunkZ,

                            out EdgeData lowerEdge
                        )
                        ||
                        !TryExtractEdge(
                            upperMesh,

                            EdgeSide.Back,

                            resolution,
                            sampleSpacing,

                            chunkX,
                            chunkZ + 1,

                            out EdgeData upperEdge
                        )
                    )
                    {
                        return false;
                    }

                    /*
                     * Convert the upper chunk's back edge
                     * into the lower chunk's local space.
                     */

                    Vector3 secondPositionOffset =
                        new Vector3(
                            0f,
                            0f,
                            chunkSize
                        );

                    CompareEdgePair(
                        lowerEdge,
                        upperEdge,

                        secondPositionOffset,

                        chunkX,
                        chunkZ,

                        chunkX,
                        chunkZ + 1,

                        "Z",

                        statistics
                    );

                    currentBoundary++;
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
    // COMPARE ONE EDGE PAIR
    // =====================================================

    private static void CompareEdgePair(
        EdgeData firstEdge,
        EdgeData secondEdge,

        Vector3 secondPositionOffset,

        int firstChunkX,
        int firstChunkZ,

        int secondChunkX,
        int secondChunkZ,

        string axis,

        ValidationStatistics statistics
    )
    {
        statistics.boundaryPairCount++;

        int sampleCount =
            firstEdge.positions.Length;

        for (
            int sampleIndex = 0;
            sampleIndex < sampleCount;
            sampleIndex++
        )
        {
            Vector3 firstPosition =
                firstEdge.positions[
                    sampleIndex
                ];

            /*
             * Align the neighboring chunk's local
             * position into the first chunk's local space.
             */

            Vector3 secondPosition =
                secondEdge.positions[
                    sampleIndex
                ]
                +
                secondPositionOffset;

            Vector3 firstNormal =
                firstEdge.normals[
                    sampleIndex
                ];

            Vector3 secondNormal =
                secondEdge.normals[
                    sampleIndex
                ];

            // ---------------------------------------------
            // Differences
            // ---------------------------------------------

            float positionDifference =
                Vector3.Distance(
                    firstPosition,
                    secondPosition
                );

            float heightDifference =
                Mathf.Abs(
                    firstPosition.y -
                    secondPosition.y
                );

            float normalDifference =
                Vector3.Distance(
                    firstNormal,
                    secondNormal
                );

            float normalAngle =
                Vector3.Angle(
                    firstNormal,
                    secondNormal
                );

            // ---------------------------------------------
            // Maximum values
            // ---------------------------------------------

            statistics.maximumPositionDifference =
                Mathf.Max(
                    statistics.maximumPositionDifference,
                    positionDifference
                );

            statistics.maximumHeightDifference =
                Mathf.Max(
                    statistics.maximumHeightDifference,
                    heightDifference
                );

            statistics.maximumNormalDifference =
                Mathf.Max(
                    statistics.maximumNormalDifference,
                    normalDifference
                );

            statistics.maximumNormalAngle =
                Mathf.Max(
                    statistics.maximumNormalAngle,
                    normalAngle
                );

            // ---------------------------------------------
            // Individual mismatch tests
            // ---------------------------------------------

            bool positionMismatch =
                positionDifference >
                PositionTolerance;

            bool heightMismatch =
                heightDifference >
                HeightTolerance;

            bool normalMismatch =
                normalDifference >
                NormalTolerance;

            if (positionMismatch)
            {
                statistics.positionMismatchCount++;
            }

            if (heightMismatch)
            {
                statistics.heightMismatchCount++;
            }

            if (normalMismatch)
            {
                statistics.normalMismatchCount++;
            }

            // ---------------------------------------------
            // Sample mismatch
            // ---------------------------------------------

            bool sampleMismatch =
                positionMismatch ||
                heightMismatch ||
                normalMismatch;

            if (sampleMismatch)
            {
                statistics.mismatchedSampleCount++;

                if (
                    statistics.firstMismatch ==
                    null
                )
                {
                    statistics.firstMismatch =
                        $"Axis: {axis}\n" +

                        $"Chunks: " +
                        $"({firstChunkX}, " +
                        $"{firstChunkZ}) " +
                        "↔ " +
                        $"({secondChunkX}, " +
                        $"{secondChunkZ})\n" +

                        $"Edge Sample: " +
                        $"{sampleIndex}\n\n" +

                        $"First Position: " +
                        $"{firstPosition}\n" +

                        $"Second Aligned Position: " +
                        $"{secondPosition}\n" +

                        $"Position Difference: " +
                        $"{positionDifference:R}\n" +

                        $"Height Difference: " +
                        $"{heightDifference:R}\n\n" +

                        $"First Normal: " +
                        $"{firstNormal}\n" +

                        $"Second Normal: " +
                        $"{secondNormal}\n" +

                        $"Normal Difference: " +
                        $"{normalDifference:R}\n" +

                        $"Normal Angle: " +
                        $"{normalAngle:R} degrees";
                }
            }

            statistics.sampleComparisonCount++;
        }
    }

    // =====================================================
    // EXTRACT EDGE
    // =====================================================

    private static bool TryExtractEdge(
        Mesh mesh,

        EdgeSide edgeSide,

        int resolution,
        float sampleSpacing,

        int chunkX,
        int chunkZ,

        out EdgeData edgeData
    )
    {
        int samplesPerEdge =
            resolution + 1;

        edgeData =
            new EdgeData(
                samplesPerEdge
            );

        Vector3[] vertices =
            mesh.vertices;

        Vector3[] normals =
            mesh.normals;

        bool[] occupied =
            new bool[
                samplesPerEdge
            ];

        float topologyTolerance =
            Mathf.Max(
                0.0001f,
                sampleSpacing *
                0.001f
            );

        // -------------------------------------------------
        // Find edge vertices
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

            if (
                !TryGetVertexSampleCoordinates(
                    vertex,

                    resolution,
                    sampleSpacing,
                    topologyTolerance,

                    out int sampleX,
                    out int sampleZ
                )
            )
            {
                return false;
            }

            bool belongsToEdge =
                false;

            int edgeSampleIndex =
                0;

            switch (edgeSide)
            {
                case EdgeSide.Left:
                {
                    belongsToEdge =
                        sampleX == 0;

                    edgeSampleIndex =
                        sampleZ;

                    break;
                }

                case EdgeSide.Right:
                {
                    belongsToEdge =
                        sampleX == resolution;

                    edgeSampleIndex =
                        sampleZ;

                    break;
                }

                case EdgeSide.Back:
                {
                    belongsToEdge =
                        sampleZ == 0;

                    edgeSampleIndex =
                        sampleX;

                    break;
                }

                case EdgeSide.Forward:
                {
                    belongsToEdge =
                        sampleZ == resolution;

                    edgeSampleIndex =
                        sampleX;

                    break;
                }
            }

            if (!belongsToEdge)
            {
                continue;
            }

            if (
                occupied[
                    edgeSampleIndex
                ]
            )
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) contains " +
                    $"multiple vertices on the same " +
                    $"{edgeSide} edge sample.\n\n" +

                    $"Edge Sample: " +
                    $"{edgeSampleIndex}"
                );

                return false;
            }

            edgeData.positions[
                edgeSampleIndex
            ] =
                vertex;

            edgeData.normals[
                edgeSampleIndex
            ] =
                normals[
                    vertexIndex
                ];

            occupied[
                edgeSampleIndex
            ] =
                true;
        }

        // -------------------------------------------------
        // Make sure every edge sample exists
        // -------------------------------------------------

        for (
            int sampleIndex = 0;
            sampleIndex < occupied.Length;
            sampleIndex++
        )
        {
            if (
                !occupied[
                    sampleIndex
                ]
            )
            {
                Debug.LogError(
                    $"Terrain chunk " +
                    $"({chunkX}, {chunkZ}) is missing " +
                    $"a {edgeSide} edge vertex.\n\n" +

                    $"Edge Sample: " +
                    $"{sampleIndex}"
                );

                return false;
            }
        }

        return true;
    }

    // =====================================================
    // VERTEX -> LOCAL SAMPLE COORDINATE
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
            )
            >
            tolerance
            ||
            Mathf.Abs(
                vertex.z -
                expectedZ
            )
            >
            tolerance
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // LOAD CHUNK
    // =====================================================

    private static Mesh LoadChunkMesh(
        int chunkX,
        int chunkZ
    )
    {
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
                "Could not load terrain chunk mesh:\n" +
                meshPath
            );
        }

        return mesh;
    }

    // =====================================================
    // PATH
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
    // FINITE VECTOR
    // =====================================================

    private static bool IsFinite(
        Vector3 value
    )
    {
        return
            !float.IsNaN(
                value.x
            )
            &&
            !float.IsNaN(
                value.y
            )
            &&
            !float.IsNaN(
                value.z
            )
            &&
            !float.IsInfinity(
                value.x
            )
            &&
            !float.IsInfinity(
                value.y
            )
            &&
            !float.IsInfinity(
                value.z
            );
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
                    "Terrain Mesh Seam Validation",

                    operation +
                    "\n\n" +
                    detail,

                    progress
                );
    }

    // =====================================================
    // EDGE DATA
    // =====================================================

    private sealed class EdgeData
    {
        public readonly Vector3[] positions;

        public readonly Vector3[] normals;

        public EdgeData(
            int sampleCount
        )
        {
            positions =
                new Vector3[
                    sampleCount
                ];

            normals =
                new Vector3[
                    sampleCount
                ];
        }
    }

    // =====================================================
    // VALIDATION STATISTICS
    // =====================================================

    private sealed class ValidationStatistics
    {
        public int boundaryPairCount;

        public int sampleComparisonCount;

        public int mismatchedSampleCount;

        public int positionMismatchCount;

        public int heightMismatchCount;

        public int normalMismatchCount;

        public float maximumPositionDifference;

        public float maximumHeightDifference;

        public float maximumNormalDifference;

        public float maximumNormalAngle;

        public string firstMismatch;
    }
}