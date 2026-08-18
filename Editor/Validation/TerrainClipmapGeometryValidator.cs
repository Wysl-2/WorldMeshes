using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainClipmapGeometryValidator
{
    // =====================================================
    // VALIDATE
    // =====================================================

    public static bool ValidateClipmapGeometry(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Basic validation
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate clipmap geometry: " +
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
                "Clipmap geometry validation must be " +
                "performed outside Play Mode."
            );

            return false;
        }

        // -------------------------------------------------
        // Settings
        // -------------------------------------------------

        int resolution =
            Mathf.Max(
                8,
                worldSettings.clipmapCenterResolution
            );

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        float baseSpacing =
            worldSettings.ClipmapBaseSpacing;

        if (
            resolution % 4 != 0
        )
        {
            Debug.LogError(
                "Cannot validate clipmap geometry.\n\n" +

                "Clipmap Center Resolution must be " +
                "evenly divisible by 4."
            );

            return false;
        }

        if (
            baseSpacing <= 0f
            ||
            float.IsNaN(baseSpacing)
            ||
            float.IsInfinity(baseSpacing)
        )
        {
            Debug.LogError(
                "Cannot validate clipmap geometry.\n\n" +

                "Clipmap base spacing is invalid."
            );

            return false;
        }

        // -------------------------------------------------
        // Tolerances
        // -------------------------------------------------

        float positionTolerance =
            Mathf.Max(
                0.00001f,
                baseSpacing *
                0.00001f
            );

        float flatHeightTolerance =
            positionTolerance;

        float areaTolerance =
            Mathf.Max(
                0.0000001f,
                baseSpacing *
                baseSpacing *
                0.0000001f
            );

        // -------------------------------------------------
        // Expected topology
        // -------------------------------------------------

        long expectedCenterTriangles =
            2L *
            resolution *
            resolution;

        int holeQuadsPerSide =
            resolution /
            2
            +
            2;

        long expectedRingTriangles =
            2L
            *
            (
                (long)resolution *
                resolution
                -
                (long)holeQuadsPerSide *
                holeQuadsPerSide
            );

        /*
         * Four transition sides:
         *
         * resolution / 2 segments
         * 3 triangles per segment
         *
         * plus four corner quads:
         *
         * 4 * 2 triangles
         */

        long expectedStitchTriangles =
            6L *
            resolution
            +
            8L;

        int expectedAssetCount =
            1
            +
            Mathf.Max(
                0,
                levelCount - 1
            )
            *
            2;

        // -------------------------------------------------
        // Load and validate pieces
        // -------------------------------------------------

        List<MeshPiece> pieces =
            new List<MeshPiece>();

        // =================================================
        // CENTER
        // =================================================

        float centerDiameter =
            resolution *
            baseSpacing;

        if (
            !TryLoadAndValidatePiece(
                TerrainClipmapMeshGenerator
                    .GetCenterMeshPath(),

                "Center_LOD0",

                centerDiameter,

                expectedCenterTriangles,

                positionTolerance,
                flatHeightTolerance,
                areaTolerance,

                out MeshPiece centerPiece
            )
        )
        {
            return false;
        }

        pieces.Add(
            centerPiece
        );

        // =================================================
        // RINGS + STITCHES
        // =================================================

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            // ---------------------------------------------
            // Ring
            // ---------------------------------------------

            float levelSpacing =
                baseSpacing *
                Mathf.Pow(
                    2f,
                    level
                );

            float ringDiameter =
                resolution *
                levelSpacing;

            if (
                !TryLoadAndValidatePiece(
                    TerrainClipmapMeshGenerator
                        .GetRingMeshPath(
                            level
                        ),

                    $"Ring_LOD{level}",

                    ringDiameter,

                    expectedRingTriangles,

                    positionTolerance,
                    flatHeightTolerance,
                    areaTolerance,

                    out MeshPiece ringPiece
                )
            )
            {
                return false;
            }

            pieces.Add(
                ringPiece
            );

            // ---------------------------------------------
            // Stitch
            // ---------------------------------------------

            int fineLevel =
                level - 1;

            float fineSpacing =
                baseSpacing *
                Mathf.Pow(
                    2f,
                    fineLevel
                );

            /*
             * The stitch runs from:
             *
             * ± resolution / 2 fine samples
             *
             * to:
             *
             * ± (resolution / 2 + 2) fine samples
             *
             * therefore its total diameter is:
             *
             * (resolution + 4) * fineSpacing
             */

            float stitchDiameter =
                (
                    resolution +
                    4
                )
                *
                fineSpacing;

            if (
                !TryLoadAndValidatePiece(
                    TerrainClipmapMeshGenerator
                        .GetStitchMeshPath(
                            fineLevel,
                            level
                        ),

                    $"Stitch_LOD{fineLevel}_LOD{level}",

                    stitchDiameter,

                    expectedStitchTriangles,

                    positionTolerance,
                    flatHeightTolerance,
                    areaTolerance,

                    out MeshPiece stitchPiece
                )
            )
            {
                return false;
            }

            pieces.Add(
                stitchPiece
            );
        }

        // -------------------------------------------------
        // Asset count
        // -------------------------------------------------

        if (
            pieces.Count !=
            expectedAssetCount
        )
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"Expected Assets: " +
                $"{expectedAssetCount}\n" +

                $"Validated Assets: " +
                $"{pieces.Count}"
            );

            return false;
        }

        // =====================================================
        // COMBINED TOPOLOGY
        // =====================================================

        if (
            !ValidateCombinedTopology(
                pieces,

                resolution,
                levelCount,
                baseSpacing,
                positionTolerance,

                out TopologyStatistics statistics
            )
        )
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"Assets: " +
                $"{pieces.Count}\n" +

                $"Vertices: " +
                $"{statistics.totalVertices:N0}\n" +

                $"Triangles: " +
                $"{statistics.totalTriangles:N0}\n\n" +

                $"Outer Boundary Edges: " +
                $"{statistics.outerBoundaryEdgeCount:N0}\n" +

                $"Unexpected Boundary Edges: " +
                $"{statistics.unexpectedBoundaryEdgeCount:N0}\n" +

                $"Non-Manifold Edges: " +
                $"{statistics.nonManifoldEdgeCount:N0}\n" +

                $"Duplicate Triangles: " +
                $"{statistics.duplicateTriangleCount:N0}\n\n" +

                $"Expected Surface Area: " +
                $"{statistics.expectedSurfaceArea:R}\n" +

                $"Actual Triangle Area: " +
                $"{statistics.actualSurfaceArea:R}\n" +

                $"Area Difference: " +
                $"{statistics.areaDifference:R}\n\n" +

                $"First Failure:\n" +
                $"{statistics.firstFailure}"
            );

            return false;
        }

        // =====================================================
        // SUCCESS
        // =====================================================

        float outerDiameter =
            resolution *
            baseSpacing *
            Mathf.Pow(
                2f,
                levelCount - 1
            );

        long expectedTotalTriangles =
            expectedCenterTriangles
            +
            (
                levelCount - 1
            )
            *
            (
                expectedRingTriangles
                +
                expectedStitchTriangles
            );

        Debug.Log(
            "Clipmap geometry validation passed.\n\n" +

            $"Center Resolution: " +
            $"{resolution}\n" +

            $"LOD Levels: " +
            $"{levelCount}\n" +

            $"Base Vertex Spacing: " +
            $"{baseSpacing}\n\n" +

            $"Clipmap Assets: " +
            $"{pieces.Count}\n" +

            $"Vertices: " +
            $"{statistics.totalVertices:N0}\n" +

            $"Triangles: " +
            $"{statistics.totalTriangles:N0}\n" +

            $"Expected Triangles: " +
            $"{expectedTotalTriangles:N0}\n\n" +

            $"Outer Coverage: " +
            $"{outerDiameter} x " +
            $"{outerDiameter}\n\n" +

            $"Outer Boundary Edges: " +
            $"{statistics.outerBoundaryEdgeCount:N0}\n" +

            $"Unexpected Boundary Edges: 0\n" +
            $"Non-Manifold Edges: 0\n" +
            $"Duplicate Triangles: 0\n\n" +

            $"Expected Surface Area: " +
            $"{statistics.expectedSurfaceArea:R}\n" +

            $"Actual Triangle Area: " +
            $"{statistics.actualSurfaceArea:R}\n" +

            $"Area Difference: " +
            $"{statistics.areaDifference:R}\n\n" +

            "The center, rings, and transition meshes form " +
            "one continuous flat clipmap surface."
        );

        return true;
    }

    // =====================================================
    // LOAD + VALIDATE ONE PIECE
    // =====================================================

    private static bool TryLoadAndValidatePiece(
        string assetPath,
        string description,
        float expectedDiameter,
        long expectedTriangleCount,
        float positionTolerance,
        float heightTolerance,
        float areaTolerance,
        out MeshPiece piece
    )
    {
        piece =
            null;

        Mesh mesh =
            AssetDatabase
                .LoadAssetAtPath<Mesh>(
                    assetPath
                );

        if (mesh == null)
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"Missing mesh:\n" +
                $"{assetPath}"
            );

            return false;
        }

        if (!mesh.isReadable)
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"{description} is not readable.\n\n" +

                $"Asset:\n" +
                $"{assetPath}"
            );

            return false;
        }

        if (
            mesh.subMeshCount !=
            1
        )
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"{description} has an unexpected " +
                "submesh count.\n\n" +

                $"Expected: 1\n" +
                $"Actual: {mesh.subMeshCount}"
            );

            return false;
        }

        Vector3[] vertices =
            mesh.vertices;

        int[] triangles =
            mesh.triangles;

        // -------------------------------------------------
        // Basic counts
        // -------------------------------------------------

        if (vertices.Length == 0)
        {
            Debug.LogError(
                $"Clipmap mesh '{description}' " +
                "contains no vertices."
            );

            return false;
        }

        if (
            triangles.Length == 0
            ||
            triangles.Length %
                3
            !=
            0
        )
        {
            Debug.LogError(
                $"Clipmap mesh '{description}' " +
                "contains invalid triangle data."
            );

            return false;
        }

        long triangleCount =
            triangles.Length /
            3;

        if (
            triangleCount !=
            expectedTriangleCount
        )
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"Mesh: {description}\n\n" +

                $"Expected Triangles: " +
                $"{expectedTriangleCount:N0}\n" +

                $"Actual Triangles: " +
                $"{triangleCount:N0}\n\n" +

                $"Asset:\n" +
                $"{assetPath}"
            );

            return false;
        }

        // -------------------------------------------------
        // Bounds
        // -------------------------------------------------

        Bounds bounds =
            mesh.bounds;

        if (
            Mathf.Abs(
                bounds.center.x
            )
            >
            positionTolerance
            ||
            Mathf.Abs(
                bounds.center.y
            )
            >
            heightTolerance
            ||
            Mathf.Abs(
                bounds.center.z
            )
            >
            positionTolerance
        )
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"{description} is not centered on the " +
                "local origin.\n\n" +

                $"Bounds Center: " +
                $"{bounds.center}"
            );

            return false;
        }

        if (
            Mathf.Abs(
                bounds.size.x -
                expectedDiameter
            )
            >
            positionTolerance
            ||
            Mathf.Abs(
                bounds.size.z -
                expectedDiameter
            )
            >
            positionTolerance
            ||
            Mathf.Abs(
                bounds.size.y
            )
            >
            heightTolerance
        )
        {
            Debug.LogError(
                "Clipmap geometry validation failed.\n\n" +

                $"Mesh: {description}\n\n" +

                $"Expected X/Z Diameter: " +
                $"{expectedDiameter}\n" +

                $"Actual Bounds: " +
                $"{bounds.size}"
            );

            return false;
        }

        // -------------------------------------------------
        // Vertices
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

            if (!IsFinite(vertex))
            {
                Debug.LogError(
                    "Clipmap geometry validation failed.\n\n" +

                    $"Mesh: {description}\n" +

                    $"Vertex: {vertexIndex}\n" +

                    $"Invalid Position: " +
                    $"{vertex}"
                );

                return false;
            }

            if (
                Mathf.Abs(
                    vertex.y
                )
                >
                heightTolerance
            )
            {
                Debug.LogError(
                    "Clipmap geometry validation failed.\n\n" +

                    $"Mesh: {description}\n" +

                    $"Vertex: {vertexIndex}\n\n" +

                    "Generated clipmap geometry is expected " +
                    "to be flat before GPU displacement.\n\n" +

                    $"Vertex Position: " +
                    $"{vertex}"
                );

                return false;
            }
        }

        // -------------------------------------------------
        // Triangles
        // -------------------------------------------------

        for (
            int triangleOffset = 0;
            triangleOffset < triangles.Length;
            triangleOffset += 3
        )
        {
            int index0 =
                triangles[
                    triangleOffset
                ];

            int index1 =
                triangles[
                    triangleOffset + 1
                ];

            int index2 =
                triangles[
                    triangleOffset + 2
                ];

            if (
                index0 < 0
                ||
                index0 >= vertices.Length
                ||
                index1 < 0
                ||
                index1 >= vertices.Length
                ||
                index2 < 0
                ||
                index2 >= vertices.Length
            )
            {
                Debug.LogError(
                    "Clipmap geometry validation failed.\n\n" +

                    $"Mesh: {description}\n" +

                    $"Triangle: " +
                    $"{triangleOffset / 3}\n\n" +

                    "Triangle references an invalid vertex."
                );

                return false;
            }

            Vector3 p0 =
                vertices[
                    index0
                ];

            Vector3 p1 =
                vertices[
                    index1
                ];

            Vector3 p2 =
                vertices[
                    index2
                ];

            Vector3 cross =
                Vector3.Cross(
                    p1 - p0,
                    p2 - p0
                );

            if (
                cross.sqrMagnitude <=
                areaTolerance *
                areaTolerance
            )
            {
                Debug.LogError(
                    "Clipmap geometry validation failed.\n\n" +

                    $"Mesh: {description}\n" +

                    $"Triangle: " +
                    $"{triangleOffset / 3}\n\n" +

                    "Degenerate triangle detected."
                );

                return false;
            }

            /*
             * All generated clipmap triangles should face
             * upward on the flat XZ plane.
             */

            if (
                cross.y <=
                0f
            )
            {
                Debug.LogError(
                    "Clipmap geometry validation failed.\n\n" +

                    $"Mesh: {description}\n" +

                    $"Triangle: " +
                    $"{triangleOffset / 3}\n\n" +

                    "Triangle winding is not upward-facing."
                );

                return false;
            }
        }

        piece =
            new MeshPiece(
                description,
                assetPath,
                mesh
            );

        return true;
    }

    // =====================================================
    // VALIDATE COMBINED TOPOLOGY
    // =====================================================

    private static bool ValidateCombinedTopology(
        List<MeshPiece> pieces,
        int resolution,
        int levelCount,
        float baseSpacing,
        float positionTolerance,
        out TopologyStatistics statistics
    )
    {
        statistics =
            new TopologyStatistics();

        Dictionary<EdgeKey, int> edgeCounts =
            new Dictionary<EdgeKey, int>();

        HashSet<TriangleKey> uniqueTriangles =
            new HashSet<TriangleKey>();

        // -------------------------------------------------
        // Build combined topology
        // -------------------------------------------------

        foreach (
            MeshPiece piece
            in pieces
        )
        {
            Vector3[] vertices =
                piece.mesh.vertices;

            int[] triangles =
                piece.mesh.triangles;

            statistics.totalVertices +=
                vertices.Length;

            for (
                int offset = 0;
                offset < triangles.Length;
                offset += 3
            )
            {
                Vector3 p0 =
                    vertices[
                        triangles[offset]
                    ];

                Vector3 p1 =
                    vertices[
                        triangles[offset + 1]
                    ];

                Vector3 p2 =
                    vertices[
                        triangles[offset + 2]
                    ];

                VertexKey v0 =
                    new VertexKey(
                        p0,
                        positionTolerance
                    );

                VertexKey v1 =
                    new VertexKey(
                        p1,
                        positionTolerance
                    );

                VertexKey v2 =
                    new VertexKey(
                        p2,
                        positionTolerance
                    );

                // -----------------------------------------
                // Duplicate triangles
                // -----------------------------------------

                TriangleKey triangleKey =
                    new TriangleKey(
                        v0,
                        v1,
                        v2
                    );

                if (
                    !uniqueTriangles.Add(
                        triangleKey
                    )
                )
                {
                    statistics
                        .duplicateTriangleCount++;

                    SetFirstFailure(
                        statistics,

                        $"Duplicate triangle detected.\n\n" +

                        $"Mesh: {piece.description}\n" +

                        $"Triangle: {offset / 3}"
                    );
                }

                // -----------------------------------------
                // Edges
                // -----------------------------------------

                IncrementEdge(
                    edgeCounts,
                    new EdgeKey(
                        v0,
                        v1
                    )
                );

                IncrementEdge(
                    edgeCounts,
                    new EdgeKey(
                        v1,
                        v2
                    )
                );

                IncrementEdge(
                    edgeCounts,
                    new EdgeKey(
                        v2,
                        v0
                    )
                );

                // -----------------------------------------
                // Projected XZ area
                // -----------------------------------------

                double twiceArea =
                    Math.Abs(
                        (
                            (double)p1.x -
                            p0.x
                        )
                        *
                        (
                            (double)p2.z -
                            p0.z
                        )
                        -
                        (
                            (double)p1.z -
                            p0.z
                        )
                        *
                        (
                            (double)p2.x -
                            p0.x
                        )
                    );

                statistics.actualSurfaceArea +=
                    twiceArea *
                    0.5;

                statistics.totalTriangles++;
            }
        }

        // -------------------------------------------------
        // Expected outer surface
        // -------------------------------------------------

        double outerDiameter =
            resolution
            *
            (double)baseSpacing
            *
            Math.Pow(
                2.0,
                levelCount - 1
            );

        double outerRadius =
            outerDiameter /
            2.0;

        statistics.expectedSurfaceArea =
            outerDiameter *
            outerDiameter;

        statistics.areaDifference =
            Math.Abs(
                statistics.actualSurfaceArea -
                statistics.expectedSurfaceArea
            );

        double surfaceAreaTolerance =
            Math.Max(
                0.0001,
                statistics.expectedSurfaceArea *
                0.000001
            );

        // -------------------------------------------------
        // Edge manifold test
        // -------------------------------------------------

        foreach (
            KeyValuePair<EdgeKey, int> pair
            in edgeCounts
        )
        {
            int useCount =
                pair.Value;

            if (useCount == 1)
            {
                if (
                    IsOuterBoundaryEdge(
                        pair.Key,
                        outerRadius,
                        positionTolerance
                    )
                )
                {
                    statistics
                        .outerBoundaryEdgeCount++;
                }
                else
                {
                    statistics
                        .unexpectedBoundaryEdgeCount++;

                    SetFirstFailure(
                        statistics,

                        "An exposed edge exists inside the " +
                        "clipmap surface.\n\n" +

                        "This indicates a crack, hole, or " +
                        "LOD transition mismatch."
                    );
                }

                continue;
            }

            if (useCount == 2)
            {
                statistics
                    .internalEdgeCount++;

                continue;
            }

            /*
             * More than two triangles sharing the same edge
             * indicates overlapping or non-manifold geometry.
             */

            statistics
                .nonManifoldEdgeCount++;

            SetFirstFailure(
                statistics,

                "A clipmap edge is shared by more than " +
                "two triangles.\n\n" +

                $"Triangle Uses: {useCount}"
            );
        }

        // -------------------------------------------------
        // Expected outer boundary
        // -------------------------------------------------

        int expectedOuterBoundaryEdges =
            resolution *
            4;

        if (
            statistics.outerBoundaryEdgeCount !=
            expectedOuterBoundaryEdges
        )
        {
            SetFirstFailure(
                statistics,

                "The outer clipmap perimeter contains an " +
                "unexpected number of edges.\n\n" +

                $"Expected: " +
                $"{expectedOuterBoundaryEdges}\n" +

                $"Actual: " +
                $"{statistics.outerBoundaryEdgeCount}"
            );
        }

        // -------------------------------------------------
        // Surface area
        // -------------------------------------------------

        if (
            statistics.areaDifference >
            surfaceAreaTolerance
        )
        {
            SetFirstFailure(
                statistics,

                "The combined triangle area does not match " +
                "the expected clipmap coverage.\n\n" +

                $"Expected Area: " +
                $"{statistics.expectedSurfaceArea:R}\n" +

                $"Actual Area: " +
                $"{statistics.actualSurfaceArea:R}\n" +

                $"Difference: " +
                $"{statistics.areaDifference:R}"
            );
        }

        // -------------------------------------------------
        // Result
        // -------------------------------------------------

        return
            statistics.unexpectedBoundaryEdgeCount == 0
            &&
            statistics.nonManifoldEdgeCount == 0
            &&
            statistics.duplicateTriangleCount == 0
            &&
            statistics.outerBoundaryEdgeCount ==
                expectedOuterBoundaryEdges
            &&
            statistics.areaDifference <=
                surfaceAreaTolerance;
    }

    // =====================================================
    // OUTER BOUNDARY EDGE
    // =====================================================

    private static bool IsOuterBoundaryEdge(
        EdgeKey edge,
        double outerRadius,
        float tolerance
    )
    {
        long outerCoordinate =
            Quantize(
                (float)outerRadius,
                tolerance
            );

        bool positiveX =
            ApproximatelyQuantized(
                edge.a.x,
                outerCoordinate
            )
            &&
            ApproximatelyQuantized(
                edge.b.x,
                outerCoordinate
            );

        bool negativeX =
            ApproximatelyQuantized(
                edge.a.x,
                -outerCoordinate
            )
            &&
            ApproximatelyQuantized(
                edge.b.x,
                -outerCoordinate
            );

        bool positiveZ =
            ApproximatelyQuantized(
                edge.a.z,
                outerCoordinate
            )
            &&
            ApproximatelyQuantized(
                edge.b.z,
                outerCoordinate
            );

        bool negativeZ =
            ApproximatelyQuantized(
                edge.a.z,
                -outerCoordinate
            )
            &&
            ApproximatelyQuantized(
                edge.b.z,
                -outerCoordinate
            );

        return
            positiveX
            ||
            negativeX
            ||
            positiveZ
            ||
            negativeZ;
    }

    // =====================================================
    // EDGE COUNT
    // =====================================================

    private static void IncrementEdge(
        Dictionary<EdgeKey, int> edgeCounts,
        EdgeKey edge
    )
    {
        if (
            edgeCounts.TryGetValue(
                edge,
                out int currentCount
            )
        )
        {
            edgeCounts[
                edge
            ] =
                currentCount +
                1;
        }
        else
        {
            edgeCounts[
                edge
            ] =
                1;
        }
    }

    // =====================================================
    // FAILURE
    // =====================================================

    private static void SetFirstFailure(
        TopologyStatistics statistics,
        string message
    )
    {
        if (
            string.IsNullOrEmpty(
                statistics.firstFailure
            )
        )
        {
            statistics.firstFailure =
                message;
        }
    }

    // =====================================================
    // FINITE VECTOR
    // =====================================================

    private static bool IsFinite(
        Vector3 value
    )
    {
        return
            !float.IsNaN(value.x)
            &&
            !float.IsNaN(value.y)
            &&
            !float.IsNaN(value.z)
            &&
            !float.IsInfinity(value.x)
            &&
            !float.IsInfinity(value.y)
            &&
            !float.IsInfinity(value.z);
    }

    // =====================================================
    // QUANTIZATION
    // =====================================================

    private static long Quantize(
        float value,
        float tolerance
    )
    {
        return
            (long)Math.Round(
                value /
                tolerance
            );
    }

    private static bool ApproximatelyQuantized(
        long first,
        long second
    )
    {
        return
            Math.Abs(
                first -
                second
            )
            <=
            1L;
    }

    // =====================================================
    // MESH PIECE
    // =====================================================

    private sealed class MeshPiece
    {
        public readonly string description;

        public readonly string assetPath;

        public readonly Mesh mesh;

        public MeshPiece(
            string description,
            string assetPath,
            Mesh mesh
        )
        {
            this.description =
                description;

            this.assetPath =
                assetPath;

            this.mesh =
                mesh;
        }
    }

    // =====================================================
    // TOPOLOGY STATISTICS
    // =====================================================

    private sealed class TopologyStatistics
    {
        public long totalVertices;

        public long totalTriangles;

        public int outerBoundaryEdgeCount;

        public int unexpectedBoundaryEdgeCount;

        public int internalEdgeCount;

        public int nonManifoldEdgeCount;

        public int duplicateTriangleCount;

        public double expectedSurfaceArea;

        public double actualSurfaceArea;

        public double areaDifference;

        public string firstFailure;
    }

    // =====================================================
    // VERTEX KEY
    // =====================================================

    private struct VertexKey :
        IEquatable<VertexKey>,
        IComparable<VertexKey>
    {
        public long x;
        public long y;
        public long z;

        public VertexKey(
            Vector3 position,
            float tolerance
        )
        {
            x =
                Quantize(
                    position.x,
                    tolerance
                );

            y =
                Quantize(
                    position.y,
                    tolerance
                );

            z =
                Quantize(
                    position.z,
                    tolerance
                );
        }

        public int CompareTo(
            VertexKey other
        )
        {
            int result =
                x.CompareTo(
                    other.x
                );

            if (result != 0)
            {
                return result;
            }

            result =
                y.CompareTo(
                    other.y
                );

            if (result != 0)
            {
                return result;
            }

            return
                z.CompareTo(
                    other.z
                );
        }

        public bool Equals(
            VertexKey other
        )
        {
            return
                x == other.x
                &&
                y == other.y
                &&
                z == other.z;
        }

        public override bool Equals(
            object obj
        )
        {
            return
                obj is VertexKey other
                &&
                Equals(
                    other
                );
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash =
                    17;

                hash =
                    hash *
                    31
                    +
                    x.GetHashCode();

                hash =
                    hash *
                    31
                    +
                    y.GetHashCode();

                hash =
                    hash *
                    31
                    +
                    z.GetHashCode();

                return hash;
            }
        }
    }

    // =====================================================
    // EDGE KEY
    // =====================================================

    private struct EdgeKey :
        IEquatable<EdgeKey>
    {
        public VertexKey a;
        public VertexKey b;

        public EdgeKey(
            VertexKey first,
            VertexKey second
        )
        {
            if (
                first.CompareTo(
                    second
                )
                <=
                0
            )
            {
                a =
                    first;

                b =
                    second;
            }
            else
            {
                a =
                    second;

                b =
                    first;
            }
        }

        public bool Equals(
            EdgeKey other
        )
        {
            return
                a.Equals(
                    other.a
                )
                &&
                b.Equals(
                    other.b
                );
        }

        public override bool Equals(
            object obj
        )
        {
            return
                obj is EdgeKey other
                &&
                Equals(
                    other
                );
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return
                    a.GetHashCode() *
                    397
                    ^
                    b.GetHashCode();
            }
        }
    }

    // =====================================================
    // TRIANGLE KEY
    // =====================================================

    private struct TriangleKey :
        IEquatable<TriangleKey>
    {
        private VertexKey a;
        private VertexKey b;
        private VertexKey c;

        public TriangleKey(
            VertexKey first,
            VertexKey second,
            VertexKey third
        )
        {
            a =
                first;

            b =
                second;

            c =
                third;

            Sort(
                ref a,
                ref b,
                ref c
            );
        }

        private static void Sort(
            ref VertexKey a,
            ref VertexKey b,
            ref VertexKey c
        )
        {
            if (
                a.CompareTo(b) >
                0
            )
            {
                Swap(
                    ref a,
                    ref b
                );
            }

            if (
                b.CompareTo(c) >
                0
            )
            {
                Swap(
                    ref b,
                    ref c
                );
            }

            if (
                a.CompareTo(b) >
                0
            )
            {
                Swap(
                    ref a,
                    ref b
                );
            }
        }

        private static void Swap(
            ref VertexKey first,
            ref VertexKey second
        )
        {
            VertexKey temporary =
                first;

            first =
                second;

            second =
                temporary;
        }

        public bool Equals(
            TriangleKey other
        )
        {
            return
                a.Equals(other.a)
                &&
                b.Equals(other.b)
                &&
                c.Equals(other.c);
        }

        public override bool Equals(
            object obj
        )
        {
            return
                obj is TriangleKey other
                &&
                Equals(
                    other
                );
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash =
                    17;

                hash =
                    hash *
                    31
                    +
                    a.GetHashCode();

                hash =
                    hash *
                    31
                    +
                    b.GetHashCode();

                hash =
                    hash *
                    31
                    +
                    c.GetHashCode();

                return hash;
            }
        }
    }
}