using UnityEditor;
using UnityEngine;

public static class TerrainCollisionSeamValidator
{
    // =====================================================
    // TOLERANCES
    // =====================================================

    /*
     * The generated collision meshes should normally
     * produce exact shared-edge matches.
     *
     * A small tolerance is still used to avoid treating
     * insignificant floating-point differences as errors.
     */

    private const float PositionTolerance =
        0.0001f;

    private const float HeightTolerance =
        0.0001f;

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
    // VALIDATE COLLISION SEAMS
    // =====================================================

    public static bool ValidateCollisionSeams(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate collision mesh seams: " +
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
                "Collision mesh seam validation must be " +
                "performed outside Play Mode."
            );

            return false;
        }

        // -------------------------------------------------
        // Generation state
        // -------------------------------------------------

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        if (
            collisionStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot validate collision mesh seams.\n\n" +

                "The generated collision meshes are not " +
                "current.\n\n" +

                $"Collision State: " +
                $"{TerrainGenerationStateUtility.GetStatusLabel(collisionStatus)}\n\n" +

                "Generate or regenerate the collision " +
                "meshes first."
            );

            return false;
        }

        // -------------------------------------------------
        // Current layout
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

        int collisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        float sampleSpacing =
            chunkSize /
            collisionResolution;

        int samplesPerEdge =
            collisionResolution + 1;

        // -------------------------------------------------
        // Validate every mesh before seam comparisons
        // -------------------------------------------------

        if (
            !ValidateAllCollisionMeshes(
                gridWidth,
                gridHeight,

                chunkSize,
                collisionResolution,
                sampleSpacing,

                out bool meshValidationCancelled
            )
        )
        {
            if (meshValidationCancelled)
            {
                Debug.LogWarning(
                    "Collision mesh seam validation was " +
                    "cancelled.\n\n" +

                    "No assets were modified."
                );
            }

            return false;
        }

        // -------------------------------------------------
        // Compare seams
        // -------------------------------------------------

        if (
            !CompareAllSharedEdges(
                gridWidth,
                gridHeight,

                chunkSize,
                collisionResolution,
                sampleSpacing,

                out ValidationStatistics statistics,
                out bool comparisonCancelled
            )
        )
        {
            if (comparisonCancelled)
            {
                Debug.LogWarning(
                    "Collision mesh seam validation was " +
                    "cancelled.\n\n" +

                    "No assets were modified."
                );
            }

            return false;
        }

        // -------------------------------------------------
        // Failure
        // -------------------------------------------------

        if (
            statistics.mismatchedSampleCount > 0
        )
        {
            Debug.LogError(
                "Collision mesh seam validation failed.\n\n" +

                $"World Grid: " +
                $"{gridWidth} x {gridHeight}\n" +

                $"Collision Resolution: " +
                $"{collisionResolution}\n" +

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
                $"{statistics.heightMismatchCount:N0}\n\n" +

                $"Maximum Position Difference: " +
                $"{statistics.maximumPositionDifference:R}\n" +

                $"Maximum Height Difference: " +
                $"{statistics.maximumHeightDifference:R}\n\n" +

                $"First Mismatch:\n" +
                $"{statistics.firstMismatch}"
            );

            return false;
        }

        // -------------------------------------------------
        // Success
        // -------------------------------------------------

        Debug.Log(
            "Collision mesh seam validation passed.\n\n" +

            $"World Grid: " +
            $"{gridWidth} x {gridHeight}\n" +

            $"Collision Resolution: " +
            $"{collisionResolution}\n" +

            $"Samples Per Edge: " +
            $"{samplesPerEdge}\n\n" +

            $"Boundary Pairs: " +
            $"{statistics.boundaryPairCount:N0}\n" +

            $"Shared Vertices Compared: " +
            $"{statistics.sampleComparisonCount:N0}\n\n" +

            $"Position Mismatches: 0\n" +
            $"Height Mismatches: 0\n\n" +

            $"Maximum Position Difference: " +
            $"{statistics.maximumPositionDifference:R}\n" +

            $"Maximum Height Difference: " +
            $"{statistics.maximumHeightDifference:R}\n\n" +

            "All shared collision mesh edges match."
        );

        return true;
    }

    // =====================================================
    // VALIDATE ALL COLLISION MESHES
    // =====================================================

    private static bool ValidateAllCollisionMeshes(
        int gridWidth,
        int gridHeight,

        float chunkSize,
        int collisionResolution,
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
                            "Validating collision meshes",

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

                    Mesh mesh =
                        LoadCollisionMesh(
                            chunkX,
                            chunkZ
                        );

                    if (mesh == null)
                    {
                        return false;
                    }

                    if (
                        !ValidateCollisionMesh(
                            mesh,

                            chunkX,
                            chunkZ,

                            chunkSize,
                            collisionResolution,
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
    // VALIDATE ONE COLLISION MESH
    // =====================================================

    private static bool ValidateCollisionMesh(
        Mesh mesh,

        int chunkX,
        int chunkZ,

        float chunkSize,
        int collisionResolution,
        float sampleSpacing
    )
    {
        // -------------------------------------------------
        // Readability
        // -------------------------------------------------

        if (!mesh.isReadable)
        {
            Debug.LogError(
                $"Collision mesh " +
                $"({chunkX}, {chunkZ}) is not readable."
            );

            return false;
        }

        // -------------------------------------------------
        // Vertex count
        // -------------------------------------------------

        int verticesPerSide =
            collisionResolution +
            1;

        int expectedVertexCount =
            verticesPerSide *
            verticesPerSide;

        if (
            mesh.vertexCount !=
            expectedVertexCount
        )
        {
            Debug.LogError(
                $"Invalid collision mesh vertex count for " +
                $"chunk ({chunkX}, {chunkZ}).\n\n" +

                $"Expected: " +
                $"{expectedVertexCount:N0}\n" +

                $"Actual: " +
                $"{mesh.vertexCount:N0}"
            );

            return false;
        }

        // -------------------------------------------------
        // Triangle/index count
        // -------------------------------------------------

        long expectedIndexCount =
            (long)collisionResolution *
            collisionResolution *
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
                $"Invalid collision mesh index count for " +
                $"chunk ({chunkX}, {chunkZ}).\n\n" +

                $"Expected: " +
                $"{expectedIndexCount:N0}\n" +

                $"Actual: " +
                $"{actualIndexCount:N0}"
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
                $"Invalid horizontal bounds for collision " +
                $"mesh ({chunkX}, {chunkZ}).\n\n" +

                $"Expected X/Z Size: " +
                $"{chunkSize}\n" +

                $"Actual Bounds: " +
                $"{mesh.bounds.size}"
            );

            return false;
        }

        // -------------------------------------------------
        // Validate regular X/Z grid
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
            Vector3 vertex =
                vertices[
                    vertexIndex
                ];

            // ---------------------------------------------
            // Finite
            // ---------------------------------------------

            if (!IsFinite(vertex))
            {
                Debug.LogError(
                    $"Collision mesh " +
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
            // Sample coordinate
            // ---------------------------------------------

            if (
                !TryGetVertexSampleCoordinates(
                    vertex,

                    collisionResolution,
                    sampleSpacing,
                    topologyTolerance,

                    out int sampleX,
                    out int sampleZ
                )
            )
            {
                Debug.LogError(
                    $"Collision mesh " +
                    $"({chunkX}, {chunkZ}) contains a " +
                    "vertex that does not align with the " +
                    "expected collision sample grid.\n\n" +

                    $"Vertex Index: " +
                    $"{vertexIndex}\n" +

                    $"Position: " +
                    $"{vertex}"
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
                    $"Collision mesh " +
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
                    $"Collision mesh " +
                    $"({chunkX}, {chunkZ}) is missing an " +
                    "expected collision sample vertex."
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
        int collisionResolution,
        float sampleSpacing,

        out ValidationStatistics statistics,
        out bool cancelled
    )
    {
        statistics =
            new ValidationStatistics();

        cancelled =
            false;

        int xBoundaryCount =
            Mathf.Max(
                0,
                gridWidth - 1
            )
            *
            gridHeight;

        int zBoundaryCount =
            gridWidth
            *
            Mathf.Max(
                0,
                gridHeight - 1
            );

        int totalBoundaryPairs =
            xBoundaryCount +
            zBoundaryCount;

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
                            "Comparing X-axis collision seams",

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
                        LoadCollisionMesh(
                            chunkX,
                            chunkZ
                        );

                    Mesh rightMesh =
                        LoadCollisionMesh(
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

                            collisionResolution,
                            sampleSpacing,

                            chunkX,
                            chunkZ,

                            out EdgeData leftEdge
                        )
                        ||
                        !TryExtractEdge(
                            rightMesh,

                            EdgeSide.Left,

                            collisionResolution,
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
                     * Convert the right chunk's local
                     * coordinates into the left chunk's
                     * coordinate space.
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
                            "Comparing Z-axis collision seams",

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
                        LoadCollisionMesh(
                            chunkX,
                            chunkZ
                        );

                    Mesh upperMesh =
                        LoadCollisionMesh(
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

                            collisionResolution,
                            sampleSpacing,

                            chunkX,
                            chunkZ,

                            out EdgeData lowerEdge
                        )
                        ||
                        !TryExtractEdge(
                            upperMesh,

                            EdgeSide.Back,

                            collisionResolution,
                            sampleSpacing,

                            chunkX,
                            chunkZ + 1,

                            out EdgeData upperEdge
                        )
                    )
                    {
                        return false;
                    }

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

            Vector3 secondPosition =
                secondEdge.positions[
                    sampleIndex
                ]
                +
                secondPositionOffset;

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

            // ---------------------------------------------
            // Maximums
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

            // ---------------------------------------------
            // Mismatch tests
            // ---------------------------------------------

            bool positionMismatch =
                positionDifference >
                PositionTolerance;

            bool heightMismatch =
                heightDifference >
                HeightTolerance;

            if (positionMismatch)
            {
                statistics.positionMismatchCount++;
            }

            if (heightMismatch)
            {
                statistics.heightMismatchCount++;
            }

            bool sampleMismatch =
                positionMismatch ||
                heightMismatch;

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
                        $"{heightDifference:R}";
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

        int collisionResolution,
        float sampleSpacing,

        int chunkX,
        int chunkZ,

        out EdgeData edgeData
    )
    {
        int samplesPerEdge =
            collisionResolution +
            1;

        edgeData =
            new EdgeData(
                samplesPerEdge
            );

        Vector3[] vertices =
            mesh.vertices;

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

                    collisionResolution,
                    sampleSpacing,
                    topologyTolerance,

                    out int sampleX,
                    out int sampleZ
                )
            )
            {
                Debug.LogError(
                    $"Collision mesh " +
                    $"({chunkX}, {chunkZ}) contains an " +
                    "invalid grid vertex."
                );

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
                        sampleX ==
                        collisionResolution;

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
                        sampleZ ==
                        collisionResolution;

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
                    $"Collision mesh " +
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

            occupied[
                edgeSampleIndex
            ] =
                true;
        }

        // -------------------------------------------------
        // Ensure every edge sample exists
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
                    $"Collision mesh " +
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

        int collisionResolution,
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
            sampleX > collisionResolution ||
            sampleZ < 0 ||
            sampleZ > collisionResolution
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
    // LOAD COLLISION MESH
    // =====================================================

    private static Mesh LoadCollisionMesh(
        int chunkX,
        int chunkZ
    )
    {
        string meshPath =
            TerrainCollisionMeshGenerator
                .GetCollisionMeshPath(
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
                "Could not load collision mesh:\n" +
                meshPath
            );
        }

        return mesh;
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
                    "Collision Mesh Seam Validation",

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

        public EdgeData(
            int sampleCount
        )
        {
            positions =
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

        public float maximumPositionDifference;

        public float maximumHeightDifference;

        public string firstMismatch;
    }
}