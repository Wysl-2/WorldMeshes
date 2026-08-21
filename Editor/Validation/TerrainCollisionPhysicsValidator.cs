using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TerrainCollisionPhysicsValidator
{
    // =====================================================
    // SETTINGS
    // =====================================================

    private const float TransformTolerance =
        0.001f;

    private const float RotationToleranceDegrees =
        0.001f;

    private const float RuntimeSeamTolerance =
        0.001f;

    private const float RaycastHorizontalTolerance =
        0.001f;

    private const float RaycastVerticalMargin =
        10f;

    private const int SeamRaycastSamplesPerEdge =
        8;

    private static readonly Vector2[]
        ChunkRaycastSampleFractions =
        {
            new Vector2(0.25f, 0.25f),
            new Vector2(0.75f, 0.25f),
            new Vector2(0.50f, 0.50f),
            new Vector2(0.25f, 0.75f),
            new Vector2(0.75f, 0.75f)
        };

    // =====================================================
    // VALIDATE RUNTIME COLLISION PHYSICS
    // =====================================================

    public static bool ValidateRuntimeCollisionPhysics(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Basic state
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics: " +
                "WorldSettings is null."
            );

            return false;
        }

        if (!EditorApplication.isPlaying)
        {
            Debug.LogError(
                "Runtime terrain collision physics validation " +
                "must be performed in Play Mode."
            );

            return false;
        }

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
                "Cannot validate runtime terrain collision physics.\n\n" +

                "Collision mesh generation state is not current.\n\n" +

                $"Collision State: " +
                $"{TerrainGenerationStateUtility.GetStatusLabel(collisionStatus)}"
            );

            return false;
        }

        // -------------------------------------------------
        // Scene / generated hierarchy
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
                "Cannot validate runtime terrain collision physics.\n\n" +
                "No valid active scene is available."
            );

            return false;
        }

        if (
            !TryGetWorldRoot(
                scene,
                out GameObject worldRoot
            )
        )
        {
            return false;
        }

        if (worldRoot == null)
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                $"'{TerrainWorldHierarchyGenerator.WorldRootName}' " +
                "does not exist.\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        if (
            !worldRoot.activeInHierarchy
            ||
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
                "Cannot validate runtime terrain collision physics.\n\n" +
                "WorldRoot must be active and use an identity transform."
            );

            return false;
        }

        if (
            !TryFindUniqueDirectChild(
                worldRoot.transform,
                TerrainWorldHierarchyGenerator.CollisionRootName,
                out Transform collisionRoot
            )
        )
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                "Could not find exactly one Collision root beneath " +
                "WorldRoot.\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        if (
            !collisionRoot.gameObject.activeInHierarchy
            ||
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
                "Cannot validate runtime terrain collision physics.\n\n" +
                "The Collision root must be active and use an " +
                "identity local transform."
            );

            return false;
        }

        // -------------------------------------------------
        // Runtime components
        // -------------------------------------------------

        TerrainCollisionStreamer[] streamers =
            collisionRoot
                .GetComponents<TerrainCollisionStreamer>();

        if (streamers.Length != 1)
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                "Expected exactly one TerrainCollisionStreamer on " +
                "the Collision root.\n" +

                $"Found: {streamers.Length}\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        TerrainCollisionColliderPool[] pools =
            collisionRoot
                .GetComponents<TerrainCollisionColliderPool>();

        if (pools.Length != 1)
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                "Expected exactly one TerrainCollisionColliderPool " +
                "on the Collision root.\n" +

                $"Found: {pools.Length}\n\n" +

                "Run Sync World Hierarchy first."
            );

            return false;
        }

        TerrainCollisionStreamer streamer =
            streamers[0];

        TerrainCollisionColliderPool pool =
            pools[0];

        if (
            !streamer.enabled
            ||
            !streamer.IsInitialized
            ||
            streamer.HasFailure
        )
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                "TerrainCollisionStreamer is not in a valid " +
                "initialized state.\n\n" +

                $"Enabled: {streamer.enabled}\n" +
                $"Initialized: {streamer.IsInitialized}\n" +
                $"Failure: {streamer.HasFailure}"
            );

            return false;
        }

        if (
            !pool.enabled
            ||
            !pool.IsInitialized
            ||
            pool.HasFailure
        )
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                "TerrainCollisionColliderPool is not in a valid " +
                "initialized state.\n\n" +

                $"Enabled: {pool.enabled}\n" +
                $"Initialized: {pool.IsInitialized}\n" +
                $"Failure: {pool.HasFailure}"
            );

            return false;
        }

        // -------------------------------------------------
        // Wait for a stable streaming state
        // -------------------------------------------------

        if (
            !streamer.HasCurrentTargetChunk
            ||
            !pool.HasAppliedCenterChunk
            ||
            streamer.IsSynchronizing
            ||
            streamer.LoadingCount > 0
            ||
            pool.AppliedCenterChunk !=
                streamer.CurrentTargetChunk
        )
        {
            Debug.LogWarning(
                "Runtime terrain collision physics is not ready " +
                "for validation yet.\n\n" +

                $"Streamer Target Available: " +
                $"{streamer.HasCurrentTargetChunk}\n" +

                $"Collider Window Available: " +
                $"{pool.HasAppliedCenterChunk}\n" +

                $"Streamer Synchronizing: " +
                $"{streamer.IsSynchronizing}\n" +

                $"Loading Meshes: " +
                $"{streamer.LoadingCount}\n\n" +

                "Wait for collision streaming to finish and run " +
                "the validation again."
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

        Vector2Int targetChunk =
            streamer.CurrentTargetChunk;

        HashSet<Vector2Int> expectedResidentCoordinates =
            BuildExpectedWindow(
                targetChunk,
                streamer.ResidentRadius,
                gridWidth,
                gridHeight
            );

        HashSet<Vector2Int> expectedActiveCoordinates =
            BuildExpectedWindow(
                pool.AppliedCenterChunk,
                pool.ActiveRadius,
                gridWidth,
                gridHeight
            );

        // =====================================================
        // PHASE 1
        // RESIDENCY
        // =====================================================

        if (
            !ValidateResidency(
                streamer,
                expectedResidentCoordinates,
                gridWidth,
                gridHeight,
                out Dictionary<Vector2Int, Mesh>
                    residentMeshes,
                out string residencyFailure
            )
        )
        {
            Debug.LogError(
                "Runtime terrain collision physics validation " +
                "failed during residency validation.\n\n" +
                residencyFailure
            );

            return false;
        }

        // =====================================================
        // PHASE 2
        // ACTIVE COLLIDER WINDOW
        // =====================================================

        if (
            !ValidateActiveColliderWindow(
                collisionRoot,
                pool,
                streamer,
                expectedActiveCoordinates,
                residentMeshes,
                gridWidth,
                gridHeight,
                chunkSize,
                collisionResolution,
                out Dictionary<Vector2Int, MeshCollider>
                    activeColliders,
                out string activeFailure
            )
        )
        {
            Debug.LogError(
                "Runtime terrain collision physics validation " +
                "failed during active-collider validation.\n\n" +
                activeFailure
            );

            return false;
        }

        // =====================================================
        // PHASE 3
        // PHYSICS RAYCASTS
        // =====================================================

        /*
         * TerrainCollisionColliderPool moves slot transforms and
         * assigns MeshCollider.sharedMesh at runtime. Flush any
         * pending Transform changes before making manual physics
         * queries.
         */
        Physics.SyncTransforms();

        if (
            !ValidatePhysicsRaycasts(
                activeColliders,
                chunkSize,
                out int colliderRaycastCount,
                out int sceneRaycastCount,
                out string raycastFailure
            )
        )
        {
            Debug.LogError(
                "Runtime terrain collision physics validation " +
                "failed during raycast validation.\n\n" +
                raycastFailure
            );

            return false;
        }

        // =====================================================
        // PHASE 4
        // ACTIVE RUNTIME SEAMS
        // =====================================================

        if (
            !ValidateRuntimeSeams(
                activeColliders,
                chunkSize,
                collisionResolution,
                out int seamPairCount,
                out int seamVertexComparisonCount,
                out int seamRaycastCount,
                out float maximumSeamDifference,
                out string seamFailure
            )
        )
        {
            Debug.LogError(
                "Runtime terrain collision physics validation " +
                "failed during active seam validation.\n\n" +
                seamFailure
            );

            return false;
        }

        // =====================================================
        // SUCCESS
        // =====================================================

        int residentDiameter =
            streamer.ResidentRadius *
            2 +
            1;

        int activeDiameter =
            pool.ActiveRadius *
            2 +
            1;

        int expectedInteriorResidentRetained =
            Mathf.Max(
                0,
                residentDiameter - 1
            ) *
            residentDiameter;

        int expectedInteriorActiveRetained =
            Mathf.Max(
                0,
                activeDiameter - 1
            ) *
            activeDiameter;

        Debug.Log(
            "Runtime terrain collision physics validation passed.\n\n" +

            $"Target Chunk: " +
            $"({targetChunk.x}, {targetChunk.y})\n\n" +

            $"Resident Radius: {streamer.ResidentRadius}\n" +
            $"Expected Resident Meshes: " +
            $"{expectedResidentCoordinates.Count:N0}\n" +
            $"Resident Meshes: {residentMeshes.Count:N0}\n" +
            $"Tracked Handles: {streamer.TrackedCount:N0}\n\n" +

            $"Active Radius: {pool.ActiveRadius}\n" +
            $"Expected Active Colliders: " +
            $"{expectedActiveCoordinates.Count:N0}\n" +
            $"Active Colliders: {activeColliders.Count:N0}\n" +
            $"Fixed Collider Slots: {pool.RequiredSlotCount:N0}\n\n" +

            $"Collider-Specific Raycasts: " +
            $"{colliderRaycastCount:N0}\n" +
            $"Scene Physics Raycasts: " +
            $"{sceneRaycastCount:N0}\n\n" +

            $"Active Seam Pairs: {seamPairCount:N0}\n" +
            $"Seam Vertices Compared: " +
            $"{seamVertexComparisonCount:N0}\n" +
            $"Seam Physics Raycasts: " +
            $"{seamRaycastCount:N0}\n" +
            $"Maximum Runtime Seam Difference: " +
            $"{maximumSeamDifference:R}\n\n" +

            "Interior one-chunk axis transition expectations:\n" +
            $"Residency: " +
            $"{expectedInteriorResidentRetained} retained / " +
            $"{residentDiameter} entering / " +
            $"{residentDiameter} leaving\n" +
            $"Physics: " +
            $"{expectedInteriorActiveRetained} retained / " +
            $"{activeDiameter} assigned / " +
            $"{activeDiameter} cleared\n\n" +

            "The currently streamed collision window is resident, " +
            "correctly assigned to the fixed collider pool, " +
            "queryable through PhysX, and seamless across every " +
            "active neighboring collider edge."
        );

        return true;
    }

    // =====================================================
    // VALIDATE RESIDENCY
    // =====================================================

    private static bool ValidateResidency(
        TerrainCollisionStreamer streamer,
        HashSet<Vector2Int> expectedResidentCoordinates,
        int gridWidth,
        int gridHeight,
        out Dictionary<Vector2Int, Mesh> residentMeshes,
        out string failure
    )
    {
        residentMeshes =
            new Dictionary<Vector2Int, Mesh>();

        failure =
            null;

        if (
            streamer.DesiredResidentCount !=
            expectedResidentCoordinates.Count
        )
        {
            failure =
                "Desired resident-mesh count does not match the " +
                "expected clipped residency window.\n\n" +

                $"Expected: {expectedResidentCoordinates.Count}\n" +
                $"Actual: {streamer.DesiredResidentCount}";

            return false;
        }

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

                bool expected =
                    expectedResidentCoordinates.Contains(
                        coordinate
                    );

                bool desired =
                    streamer.IsCoordinateDesired(
                        coordinate
                    );

                bool resident =
                    streamer.TryGetResidentMesh(
                        coordinate,
                        out Mesh mesh
                    );

                if (desired != expected)
                {
                    failure =
                        "Desired residency set contains an unexpected " +
                        "coordinate or is missing a required one.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Expected Desired: {expected}\n" +
                        $"Actual Desired: {desired}";

                    return false;
                }

                if (resident != expected)
                {
                    failure =
                        "Resident collision-mesh set does not match " +
                        "the expected residency window.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Expected Resident: {expected}\n" +
                        $"Actual Resident: {resident}";

                    return false;
                }

                if (resident)
                {
                    if (mesh == null)
                    {
                        failure =
                            "A resident collision coordinate returned " +
                            "a null Mesh.\n\n" +
                            $"Chunk: ({chunkX}, {chunkZ})";

                        return false;
                    }

                    string expectedMeshName =
                        GetExpectedCollisionMeshName(
                            coordinate
                        );

                    if (mesh.name != expectedMeshName)
                    {
                        failure =
                            "A resident collision Mesh does not match " +
                            "its world coordinate.\n\n" +

                            $"Chunk: ({chunkX}, {chunkZ})\n" +
                            $"Expected Mesh: {expectedMeshName}\n" +
                            $"Actual Mesh: {mesh.name}";

                        return false;
                    }

                    residentMeshes[
                        coordinate
                    ] =
                        mesh;
                }
            }
        }

        if (
            residentMeshes.Count !=
            expectedResidentCoordinates.Count
            ||
            streamer.ResidentCount !=
            expectedResidentCoordinates.Count
            ||
            streamer.TrackedCount !=
            expectedResidentCoordinates.Count
        )
        {
            failure =
                "Stable collision residency counts do not match.\n\n" +

                $"Expected: {expectedResidentCoordinates.Count}\n" +
                $"Enumerated Resident: {residentMeshes.Count}\n" +
                $"Streamer ResidentCount: {streamer.ResidentCount}\n" +
                $"Streamer TrackedCount: {streamer.TrackedCount}";

            return false;
        }

        return true;
    }

    // =====================================================
    // VALIDATE ACTIVE COLLIDER WINDOW
    // =====================================================

    private static bool ValidateActiveColliderWindow(
        Transform collisionRoot,
        TerrainCollisionColliderPool pool,
        TerrainCollisionStreamer streamer,
        HashSet<Vector2Int> expectedActiveCoordinates,
        Dictionary<Vector2Int, Mesh> residentMeshes,
        int gridWidth,
        int gridHeight,
        float chunkSize,
        int collisionResolution,
        out Dictionary<Vector2Int, MeshCollider> activeColliders,
        out string failure
    )
    {
        activeColliders =
            new Dictionary<Vector2Int, MeshCollider>();

        failure =
            null;

        if (
            pool.DesiredActiveCount !=
            expectedActiveCoordinates.Count
            ||
            pool.ActiveColliderCount !=
            expectedActiveCoordinates.Count
        )
        {
            failure =
                "Active collider counts do not match the expected " +
                "clipped physics window.\n\n" +

                $"Expected: {expectedActiveCoordinates.Count}\n" +
                $"Desired: {pool.DesiredActiveCount}\n" +
                $"Active: {pool.ActiveColliderCount}";

            return false;
        }

        HashSet<MeshCollider> uniqueActiveColliders =
            new HashSet<MeshCollider>();

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

                bool expectedActive =
                    expectedActiveCoordinates.Contains(
                        coordinate
                    );

                bool active =
                    pool.TryGetActiveCollider(
                        coordinate,
                        out MeshCollider meshCollider
                    );

                if (active != expectedActive)
                {
                    failure =
                        "Active collider set does not match the " +
                        "expected physics window.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Expected Active: {expectedActive}\n" +
                        $"Actual Active: {active}";

                    return false;
                }

                if (!active)
                {
                    continue;
                }

                if (
                    meshCollider == null
                    ||
                    !meshCollider.enabled
                    ||
                    meshCollider.sharedMesh == null
                )
                {
                    failure =
                        "An active collider coordinate returned an " +
                        "invalid MeshCollider.\n\n" +
                        $"Chunk: ({chunkX}, {chunkZ})";

                    return false;
                }

                if (!uniqueActiveColliders.Add(meshCollider))
                {
                    failure =
                        "The same MeshCollider slot is assigned to " +
                        "multiple active chunk coordinates.\n\n" +
                        $"Chunk: ({chunkX}, {chunkZ})";

                    return false;
                }

                if (
                    meshCollider.transform.parent !=
                    collisionRoot
                )
                {
                    failure =
                        "An active collider slot is not a direct child " +
                        "of the Collision root.\n\n" +
                        $"Slot: {meshCollider.gameObject.name}";

                    return false;
                }

                Vector3 expectedLocalPosition =
                    new Vector3(
                        chunkX *
                            chunkSize,
                        0f,
                        chunkZ *
                            chunkSize
                    );

                if (
                    !VectorMatches(
                        meshCollider.transform.localPosition,
                        expectedLocalPosition
                    )
                    ||
                    !RotationMatches(
                        meshCollider.transform.localRotation,
                        Quaternion.identity
                    )
                    ||
                    !VectorMatches(
                        meshCollider.transform.localScale,
                        Vector3.one
                    )
                )
                {
                    failure =
                        "An active collider slot has the wrong world-grid " +
                        "transform.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Slot: {meshCollider.gameObject.name}\n" +
                        $"Expected Local Position: {expectedLocalPosition}\n" +
                        $"Actual Local Position: " +
                        $"{meshCollider.transform.localPosition}";

                    return false;
                }

                if (
                    meshCollider.convex
                    ||
                    meshCollider.isTrigger
                )
                {
                    failure =
                        "An active terrain MeshCollider has invalid " +
                        "physics settings.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Convex: {meshCollider.convex}\n" +
                        $"Is Trigger: {meshCollider.isTrigger}";

                    return false;
                }

                if (
                    meshCollider.GetComponent<Rigidbody>() !=
                    null
                )
                {
                    failure =
                        "Terrain collider slots must remain static and " +
                        "must not contain a Rigidbody.\n\n" +
                        $"Slot: {meshCollider.gameObject.name}";

                    return false;
                }

                if (
                    meshCollider.GetComponent<MeshFilter>() !=
                    null
                    ||
                    meshCollider.GetComponent<MeshRenderer>() !=
                    null
                )
                {
                    failure =
                        "Terrain collider slots must not contain render " +
                        "components.\n\n" +
                        $"Slot: {meshCollider.gameObject.name}";

                    return false;
                }

                if (
                    !residentMeshes.TryGetValue(
                        coordinate,
                        out Mesh residentMesh
                    )
                    ||
                    residentMesh == null
                )
                {
                    failure =
                        "An active physics chunk is not backed by a " +
                        "resident collision Mesh.\n\n" +
                        $"Chunk: ({chunkX}, {chunkZ})";

                    return false;
                }

                if (
                    meshCollider.sharedMesh !=
                    residentMesh
                )
                {
                    failure =
                        "An active MeshCollider is not using the Mesh " +
                        "owned by TerrainCollisionStreamer.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Resident Mesh: {residentMesh.name}\n" +
                        $"Collider Mesh: {meshCollider.sharedMesh.name}";

                    return false;
                }

                int expectedVertexCount =
                    (collisionResolution + 1) *
                    (collisionResolution + 1);

                if (
                    meshCollider.sharedMesh.vertexCount !=
                    expectedVertexCount
                )
                {
                    failure =
                        "An active collision Mesh has an unexpected " +
                        "vertex count.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Expected: {expectedVertexCount:N0}\n" +
                        $"Actual: " +
                        $"{meshCollider.sharedMesh.vertexCount:N0}";

                    return false;
                }

                string expectedMeshName =
                    GetExpectedCollisionMeshName(
                        coordinate
                    );

                if (
                    meshCollider.sharedMesh.name !=
                    expectedMeshName
                )
                {
                    failure =
                        "An active collider slot contains the wrong " +
                        "collision Mesh.\n\n" +

                        $"Chunk: ({chunkX}, {chunkZ})\n" +
                        $"Expected Mesh: {expectedMeshName}\n" +
                        $"Actual Mesh: {meshCollider.sharedMesh.name}";

                    return false;
                }

                activeColliders[
                    coordinate
                ] =
                    meshCollider;
            }
        }

        // -------------------------------------------------
        // Fixed slot hierarchy
        // -------------------------------------------------

        int generatedSlotCount =
            0;

        HashSet<int> slotIndices =
            new HashSet<int>();

        HashSet<MeshCollider> activeColliderSet =
            new HashSet<MeshCollider>(
                activeColliders.Values
            );

        foreach (
            Transform child
            in collisionRoot
        )
        {
            if (
                !TerrainCollisionColliderPool
                    .TryGetColliderSlotIndex(
                        child.name,
                        out int slotIndex
                    )
            )
            {
                continue;
            }

            generatedSlotCount++;

            if (
                slotIndex < 0
                ||
                slotIndex >=
                    pool.RequiredSlotCount
                ||
                !slotIndices.Add(
                    slotIndex
                )
            )
            {
                failure =
                    "The fixed collider-slot hierarchy contains an " +
                    "invalid or duplicate slot index.\n\n" +
                    $"Object: {child.name}";

                return false;
            }

            MeshCollider[] slotColliders =
                child.GetComponents<MeshCollider>();

            if (slotColliders.Length != 1)
            {
                failure =
                    "A fixed collider slot does not contain exactly " +
                    "one MeshCollider.\n\n" +

                    $"Object: {child.name}\n" +
                    $"MeshColliders: {slotColliders.Length}";

                return false;
            }

            MeshCollider slotCollider =
                slotColliders[0];

            bool slotIsActive =
                activeColliderSet.Contains(
                    slotCollider
                );

            if (slotIsActive)
            {
                if (
                    !slotCollider.enabled
                    ||
                    slotCollider.sharedMesh == null
                )
                {
                    failure =
                        "A slot used by the active physics window is " +
                        "not enabled with a Mesh assigned.\n\n" +
                        $"Object: {child.name}";

                    return false;
                }
            }
            else
            {
                if (
                    slotCollider.enabled
                    ||
                    slotCollider.sharedMesh != null
                )
                {
                    failure =
                        "An unused collider slot is not completely " +
                        "inactive.\n\n" +

                        $"Object: {child.name}\n" +
                        $"Enabled: {slotCollider.enabled}\n" +
                        $"Shared Mesh: " +
                        $"{(slotCollider.sharedMesh != null ? slotCollider.sharedMesh.name : "None")}";

                    return false;
                }
            }
        }

        if (
            generatedSlotCount !=
            pool.RequiredSlotCount
        )
        {
            failure =
                "Fixed collider-slot count does not match the pool " +
                "configuration.\n\n" +

                $"Expected: {pool.RequiredSlotCount}\n" +
                $"Found: {generatedSlotCount}";

            return false;
        }

        if (
            activeColliders.Count !=
            expectedActiveCoordinates.Count
        )
        {
            failure =
                "Enumerated active collider count does not match the " +
                "expected active window.\n\n" +

                $"Expected: {expectedActiveCoordinates.Count}\n" +
                $"Actual: {activeColliders.Count}";

            return false;
        }

        return true;
    }

    // =====================================================
    // VALIDATE PHYSICS RAYCASTS
    // =====================================================

    private static bool ValidatePhysicsRaycasts(
        Dictionary<Vector2Int, MeshCollider> activeColliders,
        float chunkSize,
        out int colliderRaycastCount,
        out int sceneRaycastCount,
        out string failure
    )
    {
        colliderRaycastCount =
            0;

        sceneRaycastCount =
            0;

        failure =
            null;

        foreach (
            KeyValuePair<Vector2Int, MeshCollider> pair
            in activeColliders
        )
        {
            Vector2Int coordinate =
                pair.Key;

            MeshCollider meshCollider =
                pair.Value;

            foreach (
                Vector2 sampleFraction
                in ChunkRaycastSampleFractions
            )
            {
                float worldX =
                    (
                        coordinate.x +
                        sampleFraction.x
                    ) *
                    chunkSize;

                float worldZ =
                    (
                        coordinate.y +
                        sampleFraction.y
                    ) *
                    chunkSize;

                if (
                    !TryRaycastSpecificCollider(
                        meshCollider,
                        worldX,
                        worldZ,
                        out Ray ray,
                        out float maxDistance,
                        out RaycastHit colliderHit
                    )
                )
                {
                    failure =
                        "A collider-specific downward raycast missed " +
                        "an active terrain collider.\n\n" +

                        $"Chunk: " +
                        $"({coordinate.x}, {coordinate.y})\n" +
                        $"Slot: {meshCollider.gameObject.name}\n" +
                        $"Sample Fraction: {sampleFraction}";

                    return false;
                }

                colliderRaycastCount++;

                if (
                    Mathf.Abs(
                        colliderHit.point.x -
                        worldX
                    ) >
                    RaycastHorizontalTolerance
                    ||
                    Mathf.Abs(
                        colliderHit.point.z -
                        worldZ
                    ) >
                    RaycastHorizontalTolerance
                )
                {
                    failure =
                        "A collider-specific raycast returned an " +
                        "unexpected horizontal hit position.\n\n" +

                        $"Chunk: " +
                        $"({coordinate.x}, {coordinate.y})\n" +
                        $"Expected XZ: ({worldX:R}, {worldZ:R})\n" +
                        $"Hit: {colliderHit.point}";

                    return false;
                }

                RaycastHit[] sceneHits =
                    Physics.RaycastAll(
                        ray,
                        maxDistance,
                        ~0,
                        QueryTriggerInteraction.Ignore
                    );

                sceneRaycastCount++;

                bool foundExpectedCollider =
                    false;

                for (
                    int hitIndex = 0;
                    hitIndex < sceneHits.Length;
                    hitIndex++
                )
                {
                    if (
                        sceneHits[hitIndex].collider ==
                        meshCollider
                    )
                    {
                        foundExpectedCollider =
                            true;

                        break;
                    }
                }

                if (!foundExpectedCollider)
                {
                    failure =
                        "A scene Physics.RaycastAll query did not " +
                        "contain the expected active terrain " +
                        "MeshCollider.\n\n" +

                        $"Chunk: " +
                        $"({coordinate.x}, {coordinate.y})\n" +
                        $"Slot: {meshCollider.gameObject.name}\n" +
                        $"Scene Hits: {sceneHits.Length}";

                    return false;
                }
            }
        }

        return true;
    }

    // =====================================================
    // VALIDATE RUNTIME SEAMS
    // =====================================================

    private static bool ValidateRuntimeSeams(
        Dictionary<Vector2Int, MeshCollider> activeColliders,
        float chunkSize,
        int collisionResolution,
        out int seamPairCount,
        out int seamVertexComparisonCount,
        out int seamRaycastCount,
        out float maximumSeamDifference,
        out string failure
    )
    {
        seamPairCount =
            0;

        seamVertexComparisonCount =
            0;

        seamRaycastCount =
            0;

        maximumSeamDifference =
            0f;

        failure =
            null;

        foreach (
            KeyValuePair<Vector2Int, MeshCollider> pair
            in activeColliders
        )
        {
            Vector2Int coordinate =
                pair.Key;

            MeshCollider colliderA =
                pair.Value;

            Vector2Int rightCoordinate =
                coordinate +
                Vector2Int.right;

            if (
                activeColliders.TryGetValue(
                    rightCoordinate,
                    out MeshCollider rightCollider
                )
            )
            {
                if (
                    !ValidateRuntimeSeamPair(
                        coordinate,
                        colliderA,
                        rightCoordinate,
                        rightCollider,
                        true,
                        chunkSize,
                        collisionResolution,
                        ref seamVertexComparisonCount,
                        ref seamRaycastCount,
                        ref maximumSeamDifference,
                        out failure
                    )
                )
                {
                    return false;
                }

                seamPairCount++;
            }

            Vector2Int forwardCoordinate =
                coordinate +
                Vector2Int.up;

            if (
                activeColliders.TryGetValue(
                    forwardCoordinate,
                    out MeshCollider forwardCollider
                )
            )
            {
                if (
                    !ValidateRuntimeSeamPair(
                        coordinate,
                        colliderA,
                        forwardCoordinate,
                        forwardCollider,
                        false,
                        chunkSize,
                        collisionResolution,
                        ref seamVertexComparisonCount,
                        ref seamRaycastCount,
                        ref maximumSeamDifference,
                        out failure
                    )
                )
                {
                    return false;
                }

                seamPairCount++;
            }
        }

        return true;
    }

    // =====================================================
    // VALIDATE ONE RUNTIME SEAM PAIR
    // =====================================================

    private static bool ValidateRuntimeSeamPair(
        Vector2Int coordinateA,
        MeshCollider colliderA,
        Vector2Int coordinateB,
        MeshCollider colliderB,
        bool neighborAlongX,
        float chunkSize,
        int collisionResolution,
        ref int seamVertexComparisonCount,
        ref int seamRaycastCount,
        ref float maximumSeamDifference,
        out string failure
    )
    {
        failure =
            null;

        Mesh meshA =
            colliderA.sharedMesh;

        Mesh meshB =
            colliderB.sharedMesh;

        if (
            meshA == null
            ||
            meshB == null
        )
        {
            failure =
                "A runtime seam pair contains an active collider " +
                "without a Mesh.";

            return false;
        }

        int verticesPerSide =
            collisionResolution +
            1;

        int expectedVertexCount =
            verticesPerSide *
            verticesPerSide;

        if (
            meshA.vertexCount !=
                expectedVertexCount
            ||
            meshB.vertexCount !=
                expectedVertexCount
        )
        {
            failure =
                "A runtime seam pair contains a Mesh with an " +
                "unexpected topology.\n\n" +

                $"Chunk A: ({coordinateA.x}, {coordinateA.y})\n" +
                $"Chunk B: ({coordinateB.x}, {coordinateB.y})";

            return false;
        }

        Vector3[] verticesA;
        Vector3[] verticesB;

        try
        {
            verticesA =
                meshA.vertices;

            verticesB =
                meshB.vertices;
        }
        catch (
            System.Exception exception
        )
        {
            failure =
                "Could not read runtime collision Mesh vertices " +
                "while validating active seams.\n\n" +
                exception.Message;

            return false;
        }

        // -------------------------------------------------
        // Exact shared boundary vertices in world space
        // -------------------------------------------------

        for (
            int edgeIndex = 0;
            edgeIndex <= collisionResolution;
            edgeIndex++
        )
        {
            int indexA;
            int indexB;

            if (neighborAlongX)
            {
                indexA =
                    edgeIndex *
                    verticesPerSide +
                    collisionResolution;

                indexB =
                    edgeIndex *
                    verticesPerSide;
            }
            else
            {
                indexA =
                    collisionResolution *
                    verticesPerSide +
                    edgeIndex;

                indexB =
                    edgeIndex;
            }

            Vector3 worldA =
                colliderA.transform.TransformPoint(
                    verticesA[indexA]
                );

            Vector3 worldB =
                colliderB.transform.TransformPoint(
                    verticesB[indexB]
                );

            float difference =
                Vector3.Distance(
                    worldA,
                    worldB
                );

            maximumSeamDifference =
                Mathf.Max(
                    maximumSeamDifference,
                    difference
                );

            seamVertexComparisonCount++;

            if (
                difference >
                RuntimeSeamTolerance
            )
            {
                failure =
                    "Neighboring active collider Meshes do not share " +
                    "the same world-space boundary vertex.\n\n" +

                    $"Chunk A: ({coordinateA.x}, {coordinateA.y})\n" +
                    $"Chunk B: ({coordinateB.x}, {coordinateB.y})\n" +
                    $"Boundary Vertex: {edgeIndex}\n" +
                    $"Difference: {difference:R}\n" +
                    $"Tolerance: {RuntimeSeamTolerance:R}\n" +
                    $"World A: {worldA}\n" +
                    $"World B: {worldB}";

                return false;
            }
        }

        // -------------------------------------------------
        // Physics immediately to both sides of the seam
        // -------------------------------------------------

        float seamInset =
            Mathf.Min(
                0.01f,
                chunkSize *
                    0.0001f
            );

        for (
            int sampleIndex = 0;
            sampleIndex < SeamRaycastSamplesPerEdge;
            sampleIndex++
        )
        {
            float t =
                (
                    sampleIndex +
                    0.5f
                ) /
                SeamRaycastSamplesPerEdge;

            float worldXA;
            float worldZA;
            float worldXB;
            float worldZB;

            if (neighborAlongX)
            {
                float seamX =
                    coordinateB.x *
                    chunkSize;

                float worldZ =
                    (
                        coordinateA.y +
                        t
                    ) *
                    chunkSize;

                worldXA =
                    seamX -
                    seamInset;

                worldZA =
                    worldZ;

                worldXB =
                    seamX +
                    seamInset;

                worldZB =
                    worldZ;
            }
            else
            {
                float seamZ =
                    coordinateB.y *
                    chunkSize;

                float worldX =
                    (
                        coordinateA.x +
                        t
                    ) *
                    chunkSize;

                worldXA =
                    worldX;

                worldZA =
                    seamZ -
                    seamInset;

                worldXB =
                    worldX;

                worldZB =
                    seamZ +
                    seamInset;
            }

            if (
                !TryRaycastSpecificCollider(
                    colliderA,
                    worldXA,
                    worldZA,
                    out _,
                    out _,
                    out _
                )
            )
            {
                failure =
                    "Physics raycast missed immediately inside one " +
                    "side of an active terrain seam.\n\n" +

                    $"Chunk A: ({coordinateA.x}, {coordinateA.y})\n" +
                    $"Chunk B: ({coordinateB.x}, {coordinateB.y})\n" +
                    $"Sample: {sampleIndex}";

                return false;
            }

            seamRaycastCount++;

            if (
                !TryRaycastSpecificCollider(
                    colliderB,
                    worldXB,
                    worldZB,
                    out _,
                    out _,
                    out _
                )
            )
            {
                failure =
                    "Physics raycast missed immediately inside the " +
                    "neighboring side of an active terrain seam.\n\n" +

                    $"Chunk A: ({coordinateA.x}, {coordinateA.y})\n" +
                    $"Chunk B: ({coordinateB.x}, {coordinateB.y})\n" +
                    $"Sample: {sampleIndex}";

                return false;
            }

            seamRaycastCount++;
        }

        return true;
    }

    // =====================================================
    // RAYCAST ONE SPECIFIC COLLIDER AT WORLD XZ
    // =====================================================

    private static bool TryRaycastSpecificCollider(
        MeshCollider meshCollider,
        float worldX,
        float worldZ,
        out Ray ray,
        out float maxDistance,
        out RaycastHit hit
    )
    {
        Bounds bounds =
            meshCollider.bounds;

        float originY =
            bounds.max.y +
            RaycastVerticalMargin;

        maxDistance =
            Mathf.Max(
                RaycastVerticalMargin *
                    2f,
                bounds.size.y +
                    RaycastVerticalMargin *
                    2f
            );

        ray =
            new Ray(
                new Vector3(
                    worldX,
                    originY,
                    worldZ
                ),
                Vector3.down
            );

        return
            meshCollider.Raycast(
                ray,
                out hit,
                maxDistance
            );
    }

    // =====================================================
    // EXPECTED CLIPPED WINDOW
    // =====================================================

    private static HashSet<Vector2Int> BuildExpectedWindow(
        Vector2Int centerChunk,
        int radius,
        int gridWidth,
        int gridHeight
    )
    {
        HashSet<Vector2Int> coordinates =
            new HashSet<Vector2Int>();

        int safeRadius =
            Mathf.Max(
                0,
                radius
            );

        for (
            int chunkZ =
                centerChunk.y -
                safeRadius;
            chunkZ <=
                centerChunk.y +
                safeRadius;
            chunkZ++
        )
        {
            for (
                int chunkX =
                    centerChunk.x -
                    safeRadius;
                chunkX <=
                    centerChunk.x +
                    safeRadius;
                chunkX++
            )
            {
                if (
                    chunkX < 0
                    ||
                    chunkZ < 0
                    ||
                    chunkX >= gridWidth
                    ||
                    chunkZ >= gridHeight
                )
                {
                    continue;
                }

                coordinates.Add(
                    new Vector2Int(
                        chunkX,
                        chunkZ
                    )
                );
            }
        }

        return coordinates;
    }

    // =====================================================
    // EXPECTED COLLISION MESH NAME
    // =====================================================

    private static string GetExpectedCollisionMeshName(
        Vector2Int coordinate
    )
    {
        return
            $"Chunk_{coordinate.x}_" +
            $"{coordinate.y}_Collision";
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
                TerrainWorldHierarchyGenerator.WorldRootName
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

        if (matchingRoots > 1)
        {
            Debug.LogError(
                "Cannot validate runtime terrain collision physics.\n\n" +

                $"Multiple '{TerrainWorldHierarchyGenerator.WorldRootName}' " +
                "objects exist in the active scene."
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

        int matchCount =
            0;

        foreach (
            Transform child
            in parent
        )
        {
            if (child.name != childName)
            {
                continue;
            }

            matchCount++;

            if (result == null)
            {
                result =
                    child;
            }
        }

        return
            matchCount == 1;
    }

    // =====================================================
    // VECTOR MATCH
    // =====================================================

    private static bool VectorMatches(
        Vector3 a,
        Vector3 b
    )
    {
        return
            Mathf.Abs(a.x - b.x) <=
                TransformTolerance
            &&
            Mathf.Abs(a.y - b.y) <=
                TransformTolerance
            &&
            Mathf.Abs(a.z - b.z) <=
                TransformTolerance;
    }

    // =====================================================
    // ROTATION MATCH
    // =====================================================

    private static bool RotationMatches(
        Quaternion a,
        Quaternion b
    )
    {
        return
            Quaternion.Angle(
                a,
                b
            ) <=
            RotationToleranceDegrees;
    }
}
