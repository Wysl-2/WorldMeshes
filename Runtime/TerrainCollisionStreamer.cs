using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Unity.Profiling;

[DisallowMultipleComponent]
public class TerrainCollisionStreamer :
    MonoBehaviour
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [Header("Source Data")]

    [SerializeField]
    private WorldSettings worldSettings;

    [SerializeField]
    private TerrainCollisionManifest collisionManifest;

    // =====================================================
    // STREAMING
    // =====================================================

    [Header("Streaming")]

    /*
     * Player/camera object whose world-space position determines
     * the collision chunk residency window.
     *
     * Stage A2 requires this explicitly. Falling back to the
     * Collision root transform would silently stream the wrong
     * part of the world if the target were forgotten.
     */
    [SerializeField]
    private Transform streamingTarget;

    /*
     * Square/Chebyshev radius measured in terrain chunks.
     *
     * radius = 3
     *
     * interior maximum:
     *
     * (3 * 2 + 1)^2
     * =
     * 7 x 7
     * =
     * 49 resident collision meshes.
     */
    [SerializeField]
    [Min(0)]
    private int residentRadius =
        3;

    [SerializeField]
    private bool logResidencyUpdates =
        true;

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    /*
     * Contains every Addressables request currently owned by
     * this streamer.
     *
     * An entry may be:
     *
     * - still loading
     * - successfully resident
     *
     * Failed entries are released and removed.
     */
    private readonly Dictionary<Vector2Int, ResidentCollisionMesh>
        trackedMeshes =
            new Dictionary<Vector2Int, ResidentCollisionMesh>();

    /*
     * Latest desired window around the current target chunk.
     *
     * This set may change while an asynchronous synchronization
     * is still running. The current load is allowed to finish;
     * stale results are released before the synchronization
     * commits.
     */
    private readonly HashSet<Vector2Int>
        desiredResidentCoordinates =
            new HashSet<Vector2Int>();

    private Coroutine synchronizationRoutine;

    private bool initialized;

    private bool streamingFailed;

    private bool hasCurrentTargetChunk;

    private Vector2Int currentTargetChunk;
    
    // =====================================================
    // PROFILER MARKERS
    // =====================================================
    
    private static readonly ProfilerMarker
        BeginResidencySyncProfilerMarker =
            new ProfilerMarker(
                "WorldMeshes.Collision.Residency.Begin"
            );

    private static readonly ProfilerMarker
        CommitResidencySyncProfilerMarker =
            new ProfilerMarker(
                "WorldMeshes.Collision.Residency.Commit"
            );


    // =====================================================
    // PUBLIC RUNTIME STATE
    // =====================================================

    public bool IsInitialized
    {
        get
        {
            return initialized;
        }
    }

    public bool HasFailure
    {
        get
        {
            return streamingFailed;
        }
    }

    public bool IsSynchronizing
    {
        get
        {
            return
                synchronizationRoutine !=
                null;
        }
    }

    public bool HasCurrentTargetChunk
    {
        get
        {
            return hasCurrentTargetChunk;
        }
    }

    public Vector2Int CurrentTargetChunk
    {
        get
        {
            return currentTargetChunk;
        }
    }

    public int ResidentRadius
    {
        get
        {
            return
                Mathf.Max(
                    0,
                    residentRadius
                );
        }
    }

    public int DesiredResidentCount
    {
        get
        {
            return
                desiredResidentCoordinates.Count;
        }
    }

    /*
     * Number of successfully loaded collision Mesh assets.
     */
    public int ResidentCount
    {
        get
        {
            int count =
                0;

            foreach (
                ResidentCollisionMesh resident
                in trackedMeshes.Values
            )
            {
                if (
                    IsResidentMeshUsable(
                        resident
                    )
                )
                {
                    count++;
                }
            }

            return count;
        }
    }

    /*
     * Number of Addressables operations still in progress.
     */
    public int LoadingCount
    {
        get
        {
            int count =
                0;

            foreach (
                ResidentCollisionMesh resident
                in trackedMeshes.Values
            )
            {
                if (
                    resident == null
                    ||
                    !resident.handle.IsValid()
                )
                {
                    continue;
                }

                if (!resident.handle.IsDone)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /*
     * Number of Addressables handles currently owned by the
     * streamer, including both loading and completed residents.
     */
    public int TrackedCount
    {
        get
        {
            return
                trackedMeshes.Count;
        }
    }

    // =====================================================
// EDITOR / HIERARCHY CONFIGURATION
// =====================================================

/*
 * Called by TerrainWorldHierarchyGenerator.
 *
 * WorldSettings and the prepared collision manifest are owned by
 * the generated terrain hierarchy and are synchronized
 * automatically.
 *
 * The streaming target is synchronized separately from the
 * TerrainClipmapController target so the Player reference has one
 * source of truth.
 */
    public bool Configure(
        WorldSettings settings,
        TerrainCollisionManifest manifest
    )
    {
        bool changed =
            false;

        if (
            worldSettings !=
            settings
        )
        {
            worldSettings =
                settings;

            changed =
                true;
        }

        if (
            collisionManifest !=
            manifest
        )
        {
            collisionManifest =
                manifest;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // SET STREAMING TARGET
    // =====================================================

    /*
     * Returns true only when the serialized reference changed.
     *
     * TerrainWorldHierarchyGenerator uses this so scene dirty state is
     * updated only when synchronization actually modifies the
     * component.
     */
    public bool SetStreamingTarget(
        Transform target
    )
    {
        if (
            streamingTarget ==
            target
        )
        {
            return false;
        }

        streamingTarget =
            target;

        return true;
    }

    // =====================================================
    // TRY GET RESIDENT MESH
    // =====================================================

    /*
     * Stage B will use this API when assigning meshes to the
     * MeshCollider pool.
     */
    public bool TryGetResidentMesh(
        Vector2Int coordinate,
        out Mesh mesh
    )
    {
        mesh =
            null;

        if (
            !trackedMeshes.TryGetValue(
                coordinate,
                out ResidentCollisionMesh resident
            )
            ||
            !IsResidentMeshUsable(
                resident
            )
        )
        {
            return false;
        }

        mesh =
            resident.mesh;

        return true;
    }

    // =====================================================
    // IS COORDINATE DESIRED
    // =====================================================

    public bool IsCoordinateDesired(
        Vector2Int coordinate
    )
    {
        return
            desiredResidentCoordinates.Contains(
                coordinate
            );
    }

    // =====================================================
    // ENABLE
    // =====================================================

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!InitializeStreamer())
        {
            enabled =
                false;

            return;
        }

        RefreshDesiredWindow(
            true
        );

        BeginSynchronizationIfNeeded();
    }

    // =====================================================
    // UPDATE
    // =====================================================

    private void Update()
    {
        if (
            !Application.isPlaying
            ||
            !initialized
            ||
            streamingFailed
        )
        {
            return;
        }

        /*
         * Rebuild the desired set only when the Player actually
         * enters a different terrain chunk.
         */
        RefreshDesiredWindow(
            false
        );

        /*
         * If a previous asynchronous load completed after the
         * target had already moved again, the latest desired set
         * may still contain missing meshes. Start the next
         * incremental synchronization here.
         */
        BeginSynchronizationIfNeeded();
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ShutdownStreamer();
    }

    // =====================================================
    // INITIALIZE
    // =====================================================

    private bool InitializeStreamer()
    {
        initialized =
            false;

        streamingFailed =
            false;

        hasCurrentTargetChunk =
            false;

        desiredResidentCoordinates.Clear();

        ReleaseAllTrackedMeshes();

        // -------------------------------------------------
        // WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "TerrainCollisionStreamer cannot initialize.\n\n" +

                "WorldSettings is not assigned.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // Manifest
        // -------------------------------------------------

        if (collisionManifest == null)
        {
            Debug.LogError(
                "TerrainCollisionStreamer cannot initialize.\n\n" +

                "TerrainCollisionManifest is not assigned.\n\n" +

                "Run Prepare Collision Meshes For Runtime first.",
                this
            );

            return false;
        }

        if (!collisionManifest.isComplete)
        {
            Debug.LogError(
                "TerrainCollisionStreamer cannot initialize.\n\n" +

                "TerrainCollisionManifest is marked incomplete.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // Target
        // -------------------------------------------------

        if (streamingTarget == null)
        {
            Debug.LogError(
                "TerrainCollisionStreamer cannot initialize.\n\n" +

                "Streaming Target is not assigned.\n\n" +

                "Assign the Player transform.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // Manifest/runtime compatibility
        // -------------------------------------------------

        if (
            !ValidateManifestAgainstWorldSettings(
                out string manifestError
            )
        )
        {
            Debug.LogError(
                "TerrainCollisionStreamer cannot initialize.\n\n" +
                manifestError,
                this
            );

            return false;
        }

        residentRadius =
            Mathf.Max(
                0,
                residentRadius
            );

        initialized =
            true;

        return true;
    }

    // =====================================================
    // VALIDATE MANIFEST AGAINST WORLD SETTINGS
    // =====================================================

    private bool ValidateManifestAgainstWorldSettings(
        out string error
    )
    {
        error =
            null;

        int expectedGridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        int expectedGridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        float expectedChunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        int expectedLOD0Resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        int expectedCollisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        float chunkSizeTolerance =
            Mathf.Max(
                0.0001f,
                expectedChunkSize *
                    0.000001f
            );

        if (
            collisionManifest.manifestVersion <= 0
            ||
            collisionManifest.collisionGeneratorVersion <= 0
        )
        {
            error =
                "The collision manifest contains an invalid " +
                "version.";

            return false;
        }

        if (
            collisionManifest.gridWidth !=
                expectedGridWidth
            ||
            collisionManifest.gridHeight !=
                expectedGridHeight
        )
        {
            error =
                "The collision manifest world grid does not " +
                "match WorldSettings.\n\n" +

                $"Manifest: " +
                $"{collisionManifest.gridWidth} x " +
                $"{collisionManifest.gridHeight}\n" +

                $"WorldSettings: " +
                $"{expectedGridWidth} x " +
                $"{expectedGridHeight}\n\n" +

                "Run Prepare Collision Meshes For Runtime again.";

            return false;
        }

        if (
            Mathf.Abs(
                collisionManifest.chunkSize -
                expectedChunkSize
            )
            >
            chunkSizeTolerance
        )
        {
            error =
                "The collision manifest chunk size does not " +
                "match WorldSettings.\n\n" +

                $"Manifest: {collisionManifest.chunkSize:R}\n" +
                $"WorldSettings: {expectedChunkSize:R}\n\n" +

                "Run Prepare Collision Meshes For Runtime again.";

            return false;
        }

        if (
            collisionManifest.lod0Resolution !=
                expectedLOD0Resolution
            ||
            collisionManifest.collisionResolution !=
                expectedCollisionResolution
        )
        {
            error =
                "The collision manifest resolution does not " +
                "match WorldSettings.\n\n" +

                $"Manifest LOD0: " +
                $"{collisionManifest.lod0Resolution}\n" +

                $"WorldSettings LOD0: " +
                $"{expectedLOD0Resolution}\n\n" +

                $"Manifest Collision: " +
                $"{collisionManifest.collisionResolution}\n" +

                $"WorldSettings Collision: " +
                $"{expectedCollisionResolution}\n\n" +

                "Run Prepare Collision Meshes For Runtime again.";

            return false;
        }

        if (
            collisionManifest
                .collisionMeshGenerationRevision
            !=
            worldSettings
                .collisionMeshGenerationRevision
        )
        {
            error =
                "The collision manifest was prepared from a " +
                "different collision-mesh generation revision.\n\n" +

                $"Manifest Revision: " +
                $"{collisionManifest.collisionMeshGenerationRevision}\n" +

                $"Current Revision: " +
                $"{worldSettings.collisionMeshGenerationRevision}\n\n" +

                "Run Prepare Collision Meshes For Runtime again.";

            return false;
        }

        if (
            collisionManifest
                .collisionSourceHeightmapGenerationRevision
            !=
            worldSettings
                .collisionSourceHeightmapGenerationRevision
        )
        {
            error =
                "The collision manifest heightmap source revision " +
                "does not match the current collision generation.\n\n" +

                $"Manifest Height Source: " +
                $"{collisionManifest.collisionSourceHeightmapGenerationRevision}\n" +

                $"Current Height Source: " +
                $"{worldSettings.collisionSourceHeightmapGenerationRevision}\n\n" +

                "Run Prepare Collision Meshes For Runtime again.";

            return false;
        }

        if (
            collisionManifest.regionChunkSpan <= 0
        )
        {
            error =
                "The collision manifest contains an invalid " +
                "Addressables region chunk span.";

            return false;
        }

        return true;
    }

    // =====================================================
    // REFRESH DESIRED WINDOW
    // =====================================================

    private void RefreshDesiredWindow(
        bool force
    )
    {
        if (
            streamingTarget == null
            ||
            collisionManifest == null
        )
        {
            return;
        }

        Vector2Int targetChunk =
            CalculateTargetChunk(
                streamingTarget.position
            );

        if (
            !force
            &&
            hasCurrentTargetChunk
            &&
            targetChunk ==
                currentTargetChunk
        )
        {
            return;
        }

        currentTargetChunk =
            targetChunk;

        hasCurrentTargetChunk =
            true;

        BuildDesiredResidentSet(
            targetChunk
        );
    }

    // =====================================================
    // TARGET WORLD POSITION -> CHUNK
    // =====================================================

    private Vector2Int CalculateTargetChunk(
        Vector3 worldPosition
    )
    {
        float chunkSize =
            Mathf.Max(
                0.01f,
                collisionManifest.chunkSize
            );

        int gridWidth =
            Mathf.Max(
                1,
                collisionManifest.gridWidth
            );

        int gridHeight =
            Mathf.Max(
                1,
                collisionManifest.gridHeight
            );

        float worldSizeX =
            gridWidth *
            chunkSize;

        float worldSizeZ =
            gridHeight *
            chunkSize;

        int chunkX =
            Mathf.FloorToInt(
                worldPosition.x /
                chunkSize
            );

        int chunkZ =
            Mathf.FloorToInt(
                worldPosition.z /
                chunkSize
            );

        /*
         * A point exactly on the maximum world boundary belongs
         * to the final generated collision chunk rather than a
         * non-existent chunk one index beyond the world.
         *
         * Positions truly outside the world remain outside so the
         * desired residency set naturally shrinks to the valid
         * intersection with the world grid.
         */
        if (
            Mathf.Abs(
                worldPosition.x -
                worldSizeX
            )
            <=
            0.0001f
        )
        {
            chunkX =
                gridWidth - 1;
        }

        if (
            Mathf.Abs(
                worldPosition.z -
                worldSizeZ
            )
            <=
            0.0001f
        )
        {
            chunkZ =
                gridHeight - 1;
        }

        return
            new Vector2Int(
                chunkX,
                chunkZ
            );
    }

    // =====================================================
    // BUILD DESIRED RESIDENT SET
    // =====================================================

    private void BuildDesiredResidentSet(
        Vector2Int targetChunk
    )
    {
        desiredResidentCoordinates.Clear();

        int radius =
            Mathf.Max(
                0,
                residentRadius
            );

        for (
            int chunkZ =
                targetChunk.y - radius;
            chunkZ <=
                targetChunk.y + radius;
            chunkZ++
        )
        {
            for (
                int chunkX =
                    targetChunk.x - radius;
                chunkX <=
                    targetChunk.x + radius;
                chunkX++
            )
            {
                if (
                    !collisionManifest
                        .IsChunkCoordinateValid(
                            chunkX,
                            chunkZ
                        )
                )
                {
                    continue;
                }

                desiredResidentCoordinates.Add(
                    new Vector2Int(
                        chunkX,
                        chunkZ
                    )
                );
            }
        }
    }

    // =====================================================
    // BEGIN SYNCHRONIZATION IF NEEDED
    // =====================================================

    private void BeginSynchronizationIfNeeded()
    {
        if (
            !initialized
            ||
            streamingFailed
            ||
            synchronizationRoutine !=
            null
            ||
            IsDesiredSetSatisfied()
        )
        {
            return;
        }
        
        Debug.Log(
            $"[Collision Profiler] RESIDENCY BEGIN | " +
            $"Frame {Time.frameCount} | " +
            $"Target Chunk " +
            $"({currentTargetChunk.x}, {currentTargetChunk.y})",
            this
        );


        using (
            BeginResidencySyncProfilerMarker.Auto()
        )
        {
            synchronizationRoutine =
                StartCoroutine(
                    SynchronizeResidencyRoutine()
                );
        }
    }


    // =====================================================
    // IS DESIRED SET SATISFIED
    // =====================================================

    private bool IsDesiredSetSatisfied()
    {
        /*
         * Every desired coordinate must be fully loaded.
         */
        foreach (
            Vector2Int coordinate
            in desiredResidentCoordinates
        )
        {
            if (
                !trackedMeshes.TryGetValue(
                    coordinate,
                    out ResidentCollisionMesh resident
                )
                ||
                !IsResidentMeshUsable(
                    resident
                )
            )
            {
                return false;
            }
        }

        /*
         * No stale coordinate may remain owned by this streamer.
         */
        foreach (
            Vector2Int coordinate
            in trackedMeshes.Keys
        )
        {
            if (
                !desiredResidentCoordinates.Contains(
                    coordinate
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================
    // SYNCHRONIZE RESIDENCY
    // =====================================================

    private IEnumerator SynchronizeResidencyRoutine()
{
    /*
     * Snapshot the desired set for this synchronization.
     *
     * The Player may move again while Addressables are still
     * loading. In that case desiredResidentCoordinates changes,
     * but this snapshot remains stable until all requests begun
     * by this pass have completed.
     */
    HashSet<Vector2Int> requestedCoordinates =
        new HashSet<Vector2Int>(
            desiredResidentCoordinates
        );

    List<Vector2Int> enteringCoordinates =
        new List<Vector2Int>();

    List<Vector2Int> newlyRequestedCoordinates =
        new List<Vector2Int>();

    int retainedCount =
        0;

    int loadedCount =
        0;

    // =================================================
    // RETAINED / ENTERING
    // =================================================

    foreach (
        Vector2Int coordinate
        in requestedCoordinates
    )
    {
        if (
            TryKeepExistingTrackedRequest(
                coordinate
            )
        )
        {
            retainedCount++;

            continue;
        }

        enteringCoordinates.Add(
            coordinate
        );
    }

    /*
     * Deterministic ordering makes logs/debugging easier.
     * All loads are still started before we wait for any one
     * of them, so Addressables can make progress concurrently.
     */
    enteringCoordinates.Sort(
        CompareCoordinatesForLoadOrder
    );

    // =================================================
    // BEGIN ALL ENTERING LOADS
    // =================================================

    foreach (
        Vector2Int coordinate
        in enteringCoordinates
    )
    {
        string address =
            collisionManifest
                .GetCollisionMeshAddress(
                    coordinate.x,
                    coordinate.y
                );

        AsyncOperationHandle<Mesh> handle;

        try
        {
            handle =
                Addressables
                    .LoadAssetAsync<Mesh>(
                        address
                    );
        }
        catch (
            System.Exception exception
        )
        {
            FailStreaming(
                "Failed to begin loading Addressable " +
                "collision mesh.\n\n" +

                $"Chunk: ({coordinate.x}, {coordinate.y})\n" +
                $"Address: {address}\n\n" +

                exception.Message,

                newlyRequestedCoordinates
            );

            yield break;
        }

        ResidentCollisionMesh resident =
            new ResidentCollisionMesh(
                coordinate,
                address,
                handle
            );

        trackedMeshes[
            coordinate
        ] =
            resident;

        newlyRequestedCoordinates.Add(
            coordinate
        );
    }

    // =================================================
    // COMPLETE ENTERING LOADS
    // =================================================

    foreach (
        Vector2Int coordinate
        in newlyRequestedCoordinates
    )
    {
        if (
            !trackedMeshes.TryGetValue(
                coordinate,
                out ResidentCollisionMesh resident
            )
            ||
            resident == null
        )
        {
            FailStreaming(
                "A newly requested collision mesh was not " +
                "present in the residency table.\n\n" +

                $"Chunk: ({coordinate.x}, {coordinate.y})",

                newlyRequestedCoordinates
            );

            yield break;
        }

        AsyncOperationHandle<Mesh> handle =
            resident.handle;

        if (!handle.IsDone)
        {
            yield return handle;
        }

        if (
            !handle.IsValid()
            ||
            handle.Status !=
                AsyncOperationStatus.Succeeded
            ||
            handle.Result == null
        )
        {
            FailStreaming(
                "Failed to load Addressable collision mesh.\n\n" +

                $"Chunk: ({coordinate.x}, {coordinate.y})\n" +
                $"Address: {resident.address}",

                newlyRequestedCoordinates
            );

            yield break;
        }

        Mesh loadedMesh =
            handle.Result;

        // ---------------------------------------------
        // Address -> coordinate sanity check
        // ---------------------------------------------

        string expectedMeshName =
            GetExpectedCollisionMeshName(
                coordinate
            );

        if (
            loadedMesh.name !=
            expectedMeshName
        )
        {
            FailStreaming(
                "Loaded collision mesh does not match the " +
                "requested coordinate.\n\n" +

                $"Chunk: ({coordinate.x}, {coordinate.y})\n" +
                $"Address: {resident.address}\n" +
                $"Expected Mesh: {expectedMeshName}\n" +
                $"Loaded Mesh: {loadedMesh.name}",

                newlyRequestedCoordinates
            );

            yield break;
        }

        resident.mesh =
            loadedMesh;

        loadedCount++;
    }

    // =================================================
    // COMMIT COMPLETED RESIDENCY PASS
    // =================================================
    
    Debug.Log(
        $"[Collision Profiler] RESIDENCY COMMIT | " +
        $"Frame {Time.frameCount} | " +
        $"Target Chunk " +
        $"({currentTargetChunk.x}, {currentTargetChunk.y})",
        this
    );

    using (
        CommitResidencySyncProfilerMarker.Auto()
    )
    {
        /*
         * Use the LATEST desired set here, not the snapshot.
         *
         * If the Player crossed another chunk boundary while
         * these loads were running, assets that have already
         * become stale are released immediately.
         */
        int releasedCount =
            ReleaseTrackedMeshesNotDesired();

        synchronizationRoutine =
            null;

        if (logResidencyUpdates)
        {
            LogResidencyUpdate(
                retainedCount,
                loadedCount,
                releasedCount
            );
        }
    }

    /*
     * Do not start another coroutine recursively.
     *
     * Update() will observe any meshes still missing from the
     * latest desired set and start the next pass on the next
     * frame.
     */
}

    // =====================================================
    // KEEP EXISTING TRACKED REQUEST
    // =====================================================

    private bool TryKeepExistingTrackedRequest(
        Vector2Int coordinate
    )
    {
        if (
            !trackedMeshes.TryGetValue(
                coordinate,
                out ResidentCollisionMesh resident
            )
            ||
            resident == null
        )
        {
            return false;
        }

        if (!resident.handle.IsValid())
        {
            trackedMeshes.Remove(
                coordinate
            );

            return false;
        }

        if (
            resident.handle.IsDone
            &&
            (
                resident.handle.Status !=
                    AsyncOperationStatus.Succeeded
                ||
                resident.handle.Result == null
            )
        )
        {
            ReleaseTrackedMesh(
                coordinate
            );

            return false;
        }

        if (
            resident.handle.IsDone
            &&
            resident.handle.Status ==
                AsyncOperationStatus.Succeeded
            &&
            resident.mesh == null
        )
        {
            resident.mesh =
                resident.handle.Result;
        }

        return true;
    }

    // =====================================================
    // LOAD ORDER
    // =====================================================

    private int CompareCoordinatesForLoadOrder(
        Vector2Int a,
        Vector2Int b
    )
    {
        int aDistance =
            Mathf.Max(
                Mathf.Abs(
                    a.x -
                    currentTargetChunk.x
                ),
                Mathf.Abs(
                    a.y -
                    currentTargetChunk.y
                )
            );

        int bDistance =
            Mathf.Max(
                Mathf.Abs(
                    b.x -
                    currentTargetChunk.x
                ),
                Mathf.Abs(
                    b.y -
                    currentTargetChunk.y
                )
            );

        int distanceComparison =
            aDistance.CompareTo(
                bDistance
            );

        if (distanceComparison != 0)
        {
            return
                distanceComparison;
        }

        int zComparison =
            a.y.CompareTo(
                b.y
            );

        if (zComparison != 0)
        {
            return
                zComparison;
        }

        return
            a.x.CompareTo(
                b.x
            );
    }

    // =====================================================
    // IS RESIDENT MESH USABLE
    // =====================================================

    private static bool IsResidentMeshUsable(
        ResidentCollisionMesh resident
    )
    {
        return
            resident != null
            &&
            resident.mesh != null
            &&
            resident.handle.IsValid()
            &&
            resident.handle.IsDone
            &&
            resident.handle.Status ==
                AsyncOperationStatus.Succeeded
            &&
            resident.handle.Result !=
                null;
    }

    // =====================================================
    // EXPECTED MESH NAME
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
    // RELEASE ONE TRACKED MESH
    // =====================================================

    private void ReleaseTrackedMesh(
        Vector2Int coordinate
    )
    {
        if (
            !trackedMeshes.TryGetValue(
                coordinate,
                out ResidentCollisionMesh resident
            )
        )
        {
            return;
        }

        if (
            resident != null
            &&
            resident.handle.IsValid()
        )
        {
            Addressables.Release(
                resident.handle
            );
        }

        trackedMeshes.Remove(
            coordinate
        );
    }

    // =====================================================
    // RELEASE TRACKED MESHES NOT DESIRED
    // =====================================================

    private int ReleaseTrackedMeshesNotDesired()
    {
        List<Vector2Int> leavingCoordinates =
            new List<Vector2Int>();

        foreach (
            Vector2Int coordinate
            in trackedMeshes.Keys
        )
        {
            if (
                !desiredResidentCoordinates.Contains(
                    coordinate
                )
            )
            {
                leavingCoordinates.Add(
                    coordinate
                );
            }
        }

        foreach (
            Vector2Int coordinate
            in leavingCoordinates
        )
        {
            ReleaseTrackedMesh(
                coordinate
            );
        }

        return
            leavingCoordinates.Count;
    }

    // =====================================================
    // RELEASE ALL TRACKED MESHES
    // =====================================================

    private void ReleaseAllTrackedMeshes()
    {
        List<Vector2Int> coordinates =
            new List<Vector2Int>(
                trackedMeshes.Keys
            );

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            ReleaseTrackedMesh(
                coordinate
            );
        }
    }

    // =====================================================
    // FAIL STREAMING
    // =====================================================

    private void FailStreaming(
        string message,
        List<Vector2Int> newlyRequestedCoordinates
    )
    {
        /*
         * Release the entire Stage A2 residency set after a fatal
         * Addressables failure. Continuing with a partially valid
         * set would make later Stage B physics behavior ambiguous.
         */
        if (
            newlyRequestedCoordinates !=
            null
        )
        {
            foreach (
                Vector2Int coordinate
                in newlyRequestedCoordinates
            )
            {
                if (
                    trackedMeshes.ContainsKey(
                        coordinate
                    )
                )
                {
                    ReleaseTrackedMesh(
                        coordinate
                    );
                }
            }
        }

        ReleaseAllTrackedMeshes();

        streamingFailed =
            true;

        synchronizationRoutine =
            null;

        Debug.LogError(
            "Terrain collision residency FAILED.\n\n" +
            message,
            this
        );
    }

    // =====================================================
    // SHUTDOWN
    // =====================================================

    private void ShutdownStreamer()
    {
        if (
            synchronizationRoutine !=
            null
        )
        {
            StopCoroutine(
                synchronizationRoutine
            );

            synchronizationRoutine =
                null;
        }

        ReleaseAllTrackedMeshes();

        desiredResidentCoordinates.Clear();

        initialized =
            false;

        streamingFailed =
            false;

        hasCurrentTargetChunk =
            false;
    }

    // =====================================================
    // LOG RESIDENCY UPDATE
    // =====================================================

    private void LogResidencyUpdate(
        int retainedCount,
        int loadedCount,
        int releasedCount
    )
    {
        Debug.Log(
            "Terrain collision residency updated.\n\n" +

            $"Target Chunk: " +
            $"({currentTargetChunk.x}, " +
            $"{currentTargetChunk.y})\n\n" +

            $"Resident Radius: " +
            $"{Mathf.Max(0, residentRadius)}\n" +

            $"Desired Meshes: " +
            $"{desiredResidentCoordinates.Count:N0}\n" +

            $"Resident Meshes: " +
            $"{ResidentCount:N0}\n" +

            $"Loading Meshes: " +
            $"{LoadingCount:N0}\n" +

            $"Tracked Handles: " +
            $"{trackedMeshes.Count:N0}\n\n" +

            "Residency Transition:\n" +
            $"Retained: {retainedCount:N0}\n" +
            $"Loaded: {loadedCount:N0}\n" +
            $"Released: {releasedCount:N0}",
            this
        );
    }

    // =====================================================
    // MANUAL DEBUG STATE
    // =====================================================

    [ContextMenu("Log Collision Residency State")]
    public void LogResidencyState()
    {
        string targetText =
            hasCurrentTargetChunk
                ? $"({currentTargetChunk.x}, " +
                  $"{currentTargetChunk.y})"
                : "Unavailable";

        Debug.Log(
            "Terrain collision residency state.\n\n" +

            $"Initialized: {initialized}\n" +
            $"Failure: {streamingFailed}\n" +
            $"Synchronizing: " +
            $"{synchronizationRoutine != null}\n\n" +

            $"Target Chunk: {targetText}\n" +

            $"Resident Radius: " +
            $"{Mathf.Max(0, residentRadius)}\n" +

            $"Desired Meshes: " +
            $"{desiredResidentCoordinates.Count:N0}\n" +

            $"Resident Meshes: " +
            $"{ResidentCount:N0}\n" +

            $"Loading Meshes: " +
            $"{LoadingCount:N0}\n" +

            $"Tracked Handles: " +
            $"{trackedMeshes.Count:N0}",
            this
        );
    }

    // =====================================================
    // RESIDENT COLLISION MESH
    // =====================================================

    private sealed class ResidentCollisionMesh
    {
        public readonly Vector2Int coordinate;

        public readonly string address;

        public readonly AsyncOperationHandle<Mesh>
            handle;

        public Mesh mesh;

        public ResidentCollisionMesh(
            Vector2Int coordinate,
            string address,
            AsyncOperationHandle<Mesh> handle
        )
        {
            this.coordinate =
                coordinate;

            this.address =
                address;

            this.handle =
                handle;

            mesh =
                null;
        }
    }
}
