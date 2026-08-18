using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TerrainCollisionPhysicsValidator
{
    // =====================================================
    // SETTINGS
    // =====================================================

    private const int SampleQuadsPerAxis =
        8;

    private const float PhysicsPositionTolerance =
        0.001f;

    private const float PhysicsHeightTolerance =
        0.001f;

    private const float TransformTolerance =
        0.0001f;

    private const float RotationToleranceDegrees =
        0.001f;

    // =====================================================
    // VALIDATE
    // =====================================================

    public static bool ValidateCollisionPhysics(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate terrain collision physics: " +
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
                "Terrain collision physics validation must " +
                "be performed outside Play Mode."
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
                "Cannot validate terrain collision physics.\n\n" +

                "Collision mesh generation state is not " +
                "current.\n\n" +

                $"Collision State: " +
                $"{TerrainGenerationStateUtility.GetStatusLabel(collisionStatus)}"
            );

            return false;
        }

        // -------------------------------------------------
        // Scene
        // -------------------------------------------------

        Scene scene =
            SceneManager.GetActiveScene();

        if (
            !scene.IsValid()
            ||
            !scene.isLoaded
        )
        {
            Debug.LogError(
                "Cannot validate terrain collision physics.\n\n" +

                "No valid active scene is available."
            );

            return false;
        }

        // -------------------------------------------------
        // Layout
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

        // =====================================================
        // PHASE 1
        // HIERARCHY
        // =====================================================

        if (
            !ValidateCollisionHierarchy(
                scene,

                gridWidth,
                gridHeight,
                chunkSize,

                out Dictionary<Vector2Int, CollisionChunkData>
                    collisionChunks
            )
        )
        {
            return false;
        }

        // =====================================================
        // PHASE 2
        // PHYSICS
        // =====================================================

        bool physicsResult =
            ValidatePhysicsQueries(
                collisionChunks,

                gridWidth,
                gridHeight,
                chunkSize,
                collisionResolution,

                out PhysicsValidationStatistics statistics,
                out bool cancelled
            );

        if (cancelled)
        {
            Debug.LogWarning(
                "Terrain collision physics validation " +
                "cancelled.\n\n" +

                "No terrain objects or generated assets " +
                "were modified."
            );

            return false;
        }

        if (!physicsResult)
        {
            Debug.LogError(
                "Terrain collision physics validation failed.\n\n" +

                $"World Grid: " +
                $"{gridWidth} x {gridHeight}\n" +

                $"Collision Resolution: " +
                $"{collisionResolution}\n\n" +

                $"Chunks Validated: " +
                $"{collisionChunks.Count:N0}\n" +

                $"Physics Raycasts: " +
                $"{statistics.raycastCount:N0}\n\n" +

                $"Raycast Misses: " +
                $"{statistics.raycastMissCount:N0}\n" +

                $"Position Mismatches: " +
                $"{statistics.positionMismatchCount:N0}\n" +

                $"Height Mismatches: " +
                $"{statistics.heightMismatchCount:N0}\n\n" +

                $"Maximum Position Difference: " +
                $"{statistics.maximumPositionDifference:R}\n" +

                $"Maximum Height Difference: " +
                $"{statistics.maximumHeightDifference:R}\n\n" +

                $"First Failure:\n" +
                $"{statistics.firstFailure}"
            );

            return false;
        }

        // =====================================================
        // SUCCESS
        // =====================================================

        Debug.Log(
            "Terrain collision physics validation passed.\n\n" +

            $"World Grid: " +
            $"{gridWidth} x {gridHeight}\n" +

            $"Collision Resolution: " +
            $"{collisionResolution}\n\n" +

            $"Chunks Validated: " +
            $"{collisionChunks.Count:N0}\n" +

            $"Physics Raycasts: " +
            $"{statistics.raycastCount:N0}\n\n" +

            $"Raycast Misses: 0\n" +
            $"Position Mismatches: 0\n" +
            $"Height Mismatches: 0\n\n" +

            $"Maximum Position Difference: " +
            $"{statistics.maximumPositionDifference:R}\n" +

            $"Maximum Height Difference: " +
            $"{statistics.maximumHeightDifference:R}\n\n" +

            "Every tested collision-mesh triangle produced " +
            "the expected MeshCollider raycast surface.\n\n" +

            "Collision chunk objects remained disabled " +
            "during validation."
        );

        return true;
    }

    // =====================================================
    // VALIDATE COLLISION HIERARCHY
    // =====================================================

    private static bool ValidateCollisionHierarchy(
        Scene scene,

        int gridWidth,
        int gridHeight,
        float chunkSize,

        out Dictionary<Vector2Int, CollisionChunkData>
            collisionChunks
    )
    {
        collisionChunks =
            new Dictionary<Vector2Int, CollisionChunkData>();

        // -------------------------------------------------
        // WorldRoot
        // -------------------------------------------------

        if (
            !TryGetWorldRoot(
                scene,
                out GameObject worldRoot
            )
        )
        {
            return false;
        }

        if (!worldRoot.activeSelf)
        {
            Debug.LogError(
                "Terrain collision hierarchy is invalid.\n\n" +

                $"{TerrainWorldHierarchyGenerator.WorldRootName} " +
                "must be active."
            );

            return false;
        }

        if (
            !VectorMatches(
                worldRoot.transform.localPosition,
                Vector3.zero
            )
            ||
            !RotationMatches(
                worldRoot.transform.localRotation,
                Quaternion.identity
            )
            ||
            !VectorMatches(
                worldRoot.transform.localScale,
                Vector3.one
            )
        )
        {
            Debug.LogError(
                "Terrain collision hierarchy is invalid.\n\n" +

                "WorldRoot does not use the expected " +
                "identity transform."
            );

            return false;
        }

        // -------------------------------------------------
        // Collision root
        // -------------------------------------------------

        if (
            !TryFindUniqueDirectChild(
                worldRoot.transform,

                TerrainWorldHierarchyGenerator
                    .CollisionRootName,

                out Transform collisionRoot
            )
        )
        {
            Debug.LogError(
                "Terrain collision hierarchy is invalid.\n\n" +

                $"Could not find exactly one " +
                $"'{TerrainWorldHierarchyGenerator.CollisionRootName}' " +
                $"object beneath " +
                $"'{TerrainWorldHierarchyGenerator.WorldRootName}'.\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        if (
            !collisionRoot
                .gameObject
                .activeSelf
        )
        {
            Debug.LogError(
                "Terrain collision hierarchy is invalid.\n\n" +

                "The Collision root must be locally active."
            );

            return false;
        }

        if (
            !VectorMatches(
                collisionRoot.localPosition,
                Vector3.zero
            )
            ||
            !RotationMatches(
                collisionRoot.localRotation,
                Quaternion.identity
            )
            ||
            !VectorMatches(
                collisionRoot.localScale,
                Vector3.one
            )
        )
        {
            Debug.LogError(
                "Terrain collision hierarchy is invalid.\n\n" +

                "The Collision root does not use an " +
                "identity local transform."
            );

            return false;
        }

        // -------------------------------------------------
        // Discover chunk objects
        // -------------------------------------------------

        Dictionary<Vector2Int, Transform>
            chunkTransforms =
                new Dictionary<Vector2Int, Transform>();

        foreach (
            Transform child
            in collisionRoot
        )
        {
            if (
                !TryGetChunkCoordinates(
                    child.name,
                    out int chunkX,
                    out int chunkZ
                )
            )
            {
                continue;
            }

            Vector2Int coordinate =
                new Vector2Int(
                    chunkX,
                    chunkZ
                );

            if (
                chunkTransforms.ContainsKey(
                    coordinate
                )
            )
            {
                Debug.LogError(
                    "Terrain collision hierarchy is invalid.\n\n" +

                    $"Duplicate collision chunk:\n" +
                    $"Chunk_{chunkX}_{chunkZ}"
                );

                return false;
            }

            bool outsideGrid =
                chunkX < 0
                ||
                chunkZ < 0
                ||
                chunkX >= gridWidth
                ||
                chunkZ >= gridHeight;

            if (outsideGrid)
            {
                Debug.LogError(
                    "Terrain collision hierarchy is invalid.\n\n" +

                    "Collision contains a chunk outside the " +
                    "current world grid:\n\n" +

                    $"Chunk_{chunkX}_{chunkZ}"
                );

                return false;
            }

            chunkTransforms[
                coordinate
            ] =
                child;
        }

        int expectedChunkCount =
            gridWidth *
            gridHeight;

        if (
            chunkTransforms.Count !=
            expectedChunkCount
        )
        {
            Debug.LogError(
                "Terrain collision hierarchy is invalid.\n\n" +

                $"Expected Collision Chunks: " +
                $"{expectedChunkCount}\n" +

                $"Found: " +
                $"{chunkTransforms.Count}\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        // -------------------------------------------------
        // Validate each chunk
        // -------------------------------------------------

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
                Vector2Int coordinate =
                    new Vector2Int(
                        chunkX,
                        chunkZ
                    );

                if (
                    !chunkTransforms.TryGetValue(
                        coordinate,
                        out Transform chunkTransform
                    )
                )
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Missing:\n" +
                        $"Chunk_{chunkX}_{chunkZ}"
                    );

                    return false;
                }

                // -----------------------------------------
                // Transform
                // -----------------------------------------

                Vector3 expectedPosition =
                    new Vector3(
                        chunkX *
                        chunkSize,

                        0f,

                        chunkZ *
                        chunkSize
                    );

                if (
                    !VectorMatches(
                        chunkTransform.localPosition,
                        expectedPosition
                    )
                    ||
                    !RotationMatches(
                        chunkTransform.localRotation,
                        Quaternion.identity
                    )
                    ||
                    !VectorMatches(
                        chunkTransform.localScale,
                        Vector3.one
                    )
                )
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "Collision chunk transform does not " +
                        "match the expected world-grid transform."
                    );

                    return false;
                }

                // -----------------------------------------
                // Streaming active state
                // -----------------------------------------

                if (
                    chunkTransform
                        .gameObject
                        .activeSelf
                )
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "Collision chunk roots are expected " +
                        "to be disabled by default."
                    );

                    return false;
                }

                // -----------------------------------------
                // No visual components
                // -----------------------------------------

                if (
                    chunkTransform
                        .GetComponent<MeshFilter>()
                    !=
                    null
                    ||
                    chunkTransform
                        .GetComponent<MeshRenderer>()
                    !=
                    null
                )
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "Collision chunks should contain physics " +
                        "components only."
                    );

                    return false;
                }

                // -----------------------------------------
                // MeshCollider
                // -----------------------------------------

                MeshCollider[] colliders =
                    chunkTransform
                        .GetComponents<MeshCollider>();

                if (
                    colliders.Length !=
                    1
                )
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "Expected exactly one MeshCollider.\n" +

                        $"Found: " +
                        $"{colliders.Length}"
                    );

                    return false;
                }

                MeshCollider meshCollider =
                    colliders[
                        0
                    ];

                if (!meshCollider.enabled)
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "MeshCollider component is disabled."
                    );

                    return false;
                }

                if (meshCollider.convex)
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "Terrain MeshCollider must be non-convex."
                    );

                    return false;
                }

                if (meshCollider.isTrigger)
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "Terrain MeshCollider must not be a trigger."
                    );

                    return false;
                }

                // -----------------------------------------
                // Expected mesh
                // -----------------------------------------

                string expectedMeshPath =
                    TerrainCollisionMeshGenerator
                        .GetCollisionMeshPath(
                            chunkX,
                            chunkZ
                        );

                Mesh expectedMesh =
                    AssetDatabase
                        .LoadAssetAtPath<Mesh>(
                            expectedMeshPath
                        );

                if (expectedMesh == null)
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Expected collision mesh is missing:\n" +
                        $"{expectedMeshPath}"
                    );

                    return false;
                }

                if (
                    meshCollider.sharedMesh !=
                    expectedMesh
                )
                {
                    Debug.LogError(
                        "Terrain collision hierarchy is invalid.\n\n" +

                        $"Chunk: " +
                        $"({chunkX}, {chunkZ})\n\n" +

                        "MeshCollider does not reference the " +
                        "expected generated collision mesh.\n\n" +

                        $"Expected:\n" +
                        $"{expectedMeshPath}\n\n" +

                        $"Actual:\n" +
                        $"{AssetDatabase.GetAssetPath(meshCollider.sharedMesh)}"
                    );

                    return false;
                }

                if (!expectedMesh.isReadable)
                {
                    Debug.LogError(
                        "Terrain collision physics validation " +
                        "requires readable collision meshes.\n\n" +

                        $"Asset:\n" +
                        $"{expectedMeshPath}"
                    );

                    return false;
                }

                // -----------------------------------------
                // Record
                // -----------------------------------------

                collisionChunks[
                    coordinate
                ] =
                    new CollisionChunkData(
                        expectedMesh,
                        meshCollider,
                        chunkTransform
                    );
            }
        }

        return true;
    }

    // =====================================================
    // PHYSICS QUERIES
    // =====================================================

    private static bool ValidatePhysicsQueries(
        Dictionary<Vector2Int, CollisionChunkData>
            collisionChunks,

        int gridWidth,
        int gridHeight,

        float chunkSize,
        int collisionResolution,

        out PhysicsValidationStatistics statistics,
        out bool cancelled
    )
    {
        statistics =
            new PhysicsValidationStatistics();

        cancelled =
            false;

        int totalChunks =
            gridWidth *
            gridHeight;

        int currentChunk =
            0;

        GameObject testObject =
            null;

        try
        {
            // -------------------------------------------------
            // Temporary active collider
            // -------------------------------------------------

            testObject =
                EditorUtility
                    .CreateGameObjectWithHideFlags(
                        "__TerrainCollisionPhysicsValidator",

                        HideFlags.HideAndDontSave,

                        typeof(MeshCollider)
                    );

            MeshCollider testCollider =
                testObject
                    .GetComponent<MeshCollider>();

            testCollider.enabled =
                true;

            testCollider.convex =
                false;

            testCollider.isTrigger =
                false;

            int sampleCountPerAxis =
                Mathf.Min(
                    SampleQuadsPerAxis,
                    collisionResolution
                );

            // =================================================
            // CHUNKS
            // =================================================

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
                            "Testing MeshCollider physics",

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

                    Vector2Int coordinate =
                        new Vector2Int(
                            chunkX,
                            chunkZ
                        );

                    CollisionChunkData chunkData =
                        collisionChunks[
                            coordinate
                        ];

                    Mesh mesh =
                        chunkData.mesh;

                    // -----------------------------------------
                    // Configure temporary collider
                    // -----------------------------------------

                    testCollider.sharedMesh =
                        null;

                    testCollider.cookingOptions =
                        chunkData
                            .sourceCollider
                            .cookingOptions;

                    testObject.transform.position =
                        chunkData
                            .chunkTransform
                            .position;

                    testObject.transform.rotation =
                        chunkData
                            .chunkTransform
                            .rotation;

                    testObject.transform.localScale =
                        chunkData
                            .chunkTransform
                            .lossyScale;

                    testCollider.sharedMesh =
                        mesh;

                    Physics.SyncTransforms();

                    // -----------------------------------------
                    // Mesh data
                    // -----------------------------------------

                    Vector3[] vertices =
                        mesh.vertices;

                    int[] triangles =
                        mesh.triangles;

                    int expectedTriangleIndexCount =
                        collisionResolution *
                        collisionResolution *
                        6;

                    if (
                        triangles.Length !=
                        expectedTriangleIndexCount
                    )
                    {
                        RecordFailure(
                            statistics,

                            $"Chunk: " +
                            $"({chunkX}, {chunkZ})\n\n" +

                            "Collision triangle topology does " +
                            "not match the expected regular grid."
                        );

                        return false;
                    }

                    // =================================================
                    // SAMPLE QUADS
                    // =================================================

                    for (
                        int sampleZ = 0;
                        sampleZ < sampleCountPerAxis;
                        sampleZ++
                    )
                    {
                        int quadZ =
                            GetSampleQuadIndex(
                                sampleZ,
                                sampleCountPerAxis,
                                collisionResolution
                            );

                        for (
                            int sampleX = 0;
                            sampleX < sampleCountPerAxis;
                            sampleX++
                        )
                        {
                            int quadX =
                                GetSampleQuadIndex(
                                    sampleX,
                                    sampleCountPerAxis,
                                    collisionResolution
                                );

                            int quadIndex =
                                quadZ *
                                collisionResolution
                                +
                                quadX;

                            int triangleOffset =
                                quadIndex *
                                6;

                            TestTrianglePhysics(
                                chunkX,
                                chunkZ,

                                vertices,
                                triangles,

                                triangleOffset,

                                testObject.transform,
                                testCollider,

                                chunkSize,

                                statistics
                            );

                            TestTrianglePhysics(
                                chunkX,
                                chunkZ,

                                vertices,
                                triangles,

                                triangleOffset + 3,

                                testObject.transform,
                                testCollider,

                                chunkSize,

                                statistics
                            );
                        }
                    }

                    currentChunk++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            if (testObject != null)
            {
                Object.DestroyImmediate(
                    testObject
                );
            }
        }

        return
            statistics.raycastMissCount == 0
            &&
            statistics.positionMismatchCount == 0
            &&
            statistics.heightMismatchCount == 0;
    }

    // =====================================================
    // TEST ONE TRIANGLE
    // =====================================================

    private static void TestTrianglePhysics(
        int chunkX,
        int chunkZ,

        Vector3[] vertices,
        int[] triangles,

        int triangleIndexOffset,

        Transform colliderTransform,
        MeshCollider meshCollider,

        float chunkSize,

        PhysicsValidationStatistics statistics
    )
    {
        if (
            triangleIndexOffset < 0
            ||
            triangleIndexOffset + 2 >=
                triangles.Length
        )
        {
            statistics.raycastMissCount++;

            RecordFailure(
                statistics,

                $"Chunk: ({chunkX}, {chunkZ})\n\n" +

                "Triangle index offset is outside the " +
                "mesh index buffer."
            );

            return;
        }

        int index0 =
            triangles[
                triangleIndexOffset
            ];

        int index1 =
            triangles[
                triangleIndexOffset + 1
            ];

        int index2 =
            triangles[
                triangleIndexOffset + 2
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
            statistics.raycastMissCount++;

            RecordFailure(
                statistics,

                $"Chunk: ({chunkX}, {chunkZ})\n\n" +

                "A triangle references an invalid vertex."
            );

            return;
        }

        // -------------------------------------------------
        // Triangle centroid
        // -------------------------------------------------

        Vector3 localCentroid =
            (
                vertices[index0]
                +
                vertices[index1]
                +
                vertices[index2]
            )
            /
            3f;

        Vector3 expectedWorldPoint =
            colliderTransform
                .TransformPoint(
                    localCentroid
                );

        // -------------------------------------------------
        // Downward ray
        // -------------------------------------------------

        float rayStartDistance =
            Mathf.Max(
                10f,
                chunkSize
            );

        Vector3 rayOrigin =
            expectedWorldPoint
            +
            Vector3.up *
            rayStartDistance;

        Ray ray =
            new Ray(
                rayOrigin,
                Vector3.down
            );

        float rayDistance =
            rayStartDistance *
            2f;

        statistics.raycastCount++;

        if (
            !meshCollider.Raycast(
                ray,
                out RaycastHit hit,
                rayDistance
            )
        )
        {
            statistics.raycastMissCount++;

            RecordFailure(
                statistics,

                $"Chunk: ({chunkX}, {chunkZ})\n" +

                $"Triangle Index Offset: " +
                $"{triangleIndexOffset}\n\n" +

                $"Expected Point: " +
                $"{expectedWorldPoint}\n\n" +

                "MeshCollider.Raycast did not hit the " +
                "expected surface."
            );

            return;
        }

        // -------------------------------------------------
        // Differences
        // -------------------------------------------------

        float positionDifference =
            Vector3.Distance(
                expectedWorldPoint,
                hit.point
            );

        float heightDifference =
            Mathf.Abs(
                expectedWorldPoint.y -
                hit.point.y
            );

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

        bool positionMismatch =
            positionDifference >
            PhysicsPositionTolerance;

        bool heightMismatch =
            heightDifference >
            PhysicsHeightTolerance;

        if (positionMismatch)
        {
            statistics.positionMismatchCount++;
        }

        if (heightMismatch)
        {
            statistics.heightMismatchCount++;
        }

        if (
            positionMismatch
            ||
            heightMismatch
        )
        {
            RecordFailure(
                statistics,

                $"Chunk: ({chunkX}, {chunkZ})\n" +

                $"Triangle Index Offset: " +
                $"{triangleIndexOffset}\n\n" +

                $"Expected Mesh Point: " +
                $"{expectedWorldPoint}\n" +

                $"Physics Hit Point: " +
                $"{hit.point}\n\n" +

                $"Position Difference: " +
                $"{positionDifference:R}\n" +

                $"Height Difference: " +
                $"{heightDifference:R}"
            );
        }
    }

    // =====================================================
    // SAMPLE QUAD INDEX
    // =====================================================

    private static int GetSampleQuadIndex(
        int sampleIndex,
        int sampleCount,
        int collisionResolution
    )
    {
        float normalizedPosition =
            (
                sampleIndex +
                0.5f
            )
            /
            sampleCount;

        int quadIndex =
            Mathf.FloorToInt(
                normalizedPosition *
                collisionResolution
            );

        return
            Mathf.Clamp(
                quadIndex,
                0,
                collisionResolution - 1
            );
    }

    // =====================================================
    // WORLD ROOT
    // =====================================================

    private static bool TryGetWorldRoot(
        Scene scene,
        out GameObject worldRoot
    )
    {
        worldRoot =
            null;

        int matchingRoots =
            0;

        foreach (
            GameObject rootObject
            in scene.GetRootGameObjects()
        )
        {
            if (
                rootObject.name !=
                TerrainWorldHierarchyGenerator
                    .WorldRootName
            )
            {
                continue;
            }

            matchingRoots++;

            if (worldRoot == null)
            {
                worldRoot =
                    rootObject;
            }
        }

        if (matchingRoots == 0)
        {
            Debug.LogError(
                "Cannot validate terrain collision physics.\n\n" +

                $"'{TerrainWorldHierarchyGenerator.WorldRootName}' " +
                "does not exist.\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        if (matchingRoots > 1)
        {
            Debug.LogError(
                "Cannot validate terrain collision physics.\n\n" +

                $"Multiple " +
                $"'{TerrainWorldHierarchyGenerator.WorldRootName}' " +
                "objects exist."
            );

            worldRoot =
                null;

            return false;
        }

        return true;
    }

    // =====================================================
    // UNIQUE DIRECT CHILD
    // =====================================================

    private static bool TryFindUniqueDirectChild(
        Transform parent,
        string childName,
        out Transform result
    )
    {
        result =
            null;

        int count =
            0;

        foreach (
            Transform child
            in parent
        )
        {
            if (
                child.name !=
                childName
            )
            {
                continue;
            }

            count++;

            if (result == null)
            {
                result =
                    child;
            }
        }

        return
            count == 1
            &&
            result != null;
    }

    // =====================================================
    // PARSE CHUNK NAME
    // =====================================================

    private static bool TryGetChunkCoordinates(
        string objectName,
        out int x,
        out int z
    )
    {
        x =
            0;

        z =
            0;

        string[] parts =
            objectName.Split(
                '_'
            );

        if (
            parts.Length !=
            3
        )
        {
            return false;
        }

        if (
            parts[0] !=
            "Chunk"
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[1],
                out x
            )
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[2],
                out z
            )
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // TRANSFORM COMPARISON
    // =====================================================

    private static bool VectorMatches(
        Vector3 first,
        Vector3 second
    )
    {
        return
            Vector3.Distance(
                first,
                second
            )
            <=
            TransformTolerance;
    }

    private static bool RotationMatches(
        Quaternion first,
        Quaternion second
    )
    {
        return
            Quaternion.Angle(
                first,
                second
            )
            <=
            RotationToleranceDegrees;
    }

    // =====================================================
    // FIRST FAILURE
    // =====================================================

    private static void RecordFailure(
        PhysicsValidationStatistics statistics,
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
                    "Terrain Collision Physics Validation",

                    operation +
                    "\n\n" +
                    detail,

                    progress
                );
    }

    // =====================================================
    // COLLISION CHUNK DATA
    // =====================================================

    private sealed class CollisionChunkData
    {
        public readonly Mesh mesh;

        public readonly MeshCollider sourceCollider;

        public readonly Transform chunkTransform;

        public CollisionChunkData(
            Mesh mesh,
            MeshCollider sourceCollider,
            Transform chunkTransform
        )
        {
            this.mesh =
                mesh;

            this.sourceCollider =
                sourceCollider;

            this.chunkTransform =
                chunkTransform;
        }
    }

    // =====================================================
    // STATISTICS
    // =====================================================

    private sealed class PhysicsValidationStatistics
    {
        public int raycastCount;

        public int raycastMissCount;

        public int positionMismatchCount;

        public int heightMismatchCount;

        public float maximumPositionDifference;

        public float maximumHeightDifference;

        public string firstFailure;
    }
}