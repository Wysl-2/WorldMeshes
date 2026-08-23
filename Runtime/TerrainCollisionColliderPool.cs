using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public class TerrainCollisionColliderPool :
    MonoBehaviour
{
    // =====================================================
    // DEFAULTS / GENERATED SLOT NAMES
    // =====================================================

    public const int DefaultActiveRadius =
        2;

    public const string ColliderSlotNamePrefix =
        "ColliderSlot_";

    // =====================================================
    // SOURCE DATA
    // =====================================================

    [Header("Source Data")]

    [SerializeField]
    private WorldSettings worldSettings;

    [SerializeField]
    private TerrainCollisionStreamer collisionStreamer;

    // =====================================================
    // ACTIVE PHYSICS WINDOW
    // =====================================================

    [Header("Active Physics Window")]

    /*
     * Square/Chebyshev radius measured in terrain chunks.
     *
     * radius = 2
     *
     * interior maximum:
     *
     * (2 * 2 + 1)^2
     * =
     * 5 x 5
     * =
     * 25 active MeshColliders.
     *
     * The collision streamer currently keeps a larger 7 x 7
     * resident Mesh window (radius 3), leaving one complete
     * preloaded chunk ring around this active physics window.
     */
    [SerializeField]
    [Min(0)]
    private int activeRadius =
        DefaultActiveRadius;

    [SerializeField]
    private bool logPoolUpdates =
        true;

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private readonly List<ColliderSlot>
        colliderSlots =
            new List<ColliderSlot>();

    private readonly Dictionary<Vector2Int, ColliderSlot>
        activeSlotsByCoordinate =
            new Dictionary<Vector2Int, ColliderSlot>();

    private readonly HashSet<Vector2Int>
        desiredActiveCoordinates =
            new HashSet<Vector2Int>();

    private bool initialized;

    private bool poolFailed;

    private bool hasAppliedCenterChunk;

    private Vector2Int appliedCenterChunk;
    
    // =====================================================
    // PROFILER MARKERS
    // =====================================================
    
    private static readonly ProfilerMarker
        ApplyActiveSetProfilerMarker =
            new ProfilerMarker(
                "WorldMeshes.Collision.ApplyActiveSet"
            );
    
    // =====================================================
    // PUBLIC STATE
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
            return poolFailed;
        }
    }

    public bool HasAppliedCenterChunk
    {
        get
        {
            return hasAppliedCenterChunk;
        }
    }

    public Vector2Int AppliedCenterChunk
    {
        get
        {
            return appliedCenterChunk;
        }
    }

    public int ActiveRadius
    {
        get
        {
            return
                Mathf.Max(
                    0,
                    activeRadius
                );
        }
    }

    public int RequiredSlotCount
    {
        get
        {
            return
                CalculateRequiredSlotCount(
                    ActiveRadius
                );
        }
    }

    public int ActiveColliderCount
    {
        get
        {
            return
                activeSlotsByCoordinate.Count;
        }
    }

    public int DesiredActiveCount
    {
        get
        {
            return
                desiredActiveCoordinates.Count;
        }
    }
    
    public float MaximumSafeSingleFrameMovementDistance
    {
        get
        {
            if (worldSettings == null)
            {
                return 0f;
            }

            /*
             * Limiting one controller movement step to at most one
             * collision-chunk width guarantees that a single frame
             * cannot skip across more than one chunk boundary on
             * either horizontal axis.
             *
             * CanTargetMoveTo() is the actual safety gate.
             * This value simply prevents extremely high movement
             * speeds from repeatedly proposing an impossible
             * multi-chunk jump and stalling forever.
             */
            return
                Mathf.Max(
                    0.01f,
                    worldSettings.chunkSize
                );
        }
    }

    // =====================================================
    // SLOT COUNT
    // =====================================================

    public static int CalculateRequiredSlotCount(
        int radius
    )
    {
        int safeRadius =
            Mathf.Max(
                0,
                radius
            );

        int diameter =
            safeRadius *
            2
            +
            1;

        return
            diameter *
            diameter;
    }

    // =====================================================
    // SLOT NAME
    // =====================================================

    public static string GetColliderSlotName(
        int slotIndex
    )
    {
        return
            ColliderSlotNamePrefix +
            Mathf.Max(
                0,
                slotIndex
            ).ToString("D2");
    }

    // =====================================================
    // PARSE SLOT NAME
    // =====================================================

    public static bool TryGetColliderSlotIndex(
        string objectName,
        out int slotIndex
    )
    {
        slotIndex =
            -1;

        if (
            string.IsNullOrEmpty(
                objectName
            )
            ||
            !objectName.StartsWith(
                ColliderSlotNamePrefix
            )
        )
        {
            return false;
        }

        string indexText =
            objectName.Substring(
                ColliderSlotNamePrefix.Length
            );

        return
            int.TryParse(
                indexText,
                out slotIndex
            )
            &&
            slotIndex >= 0;
    }

    // =====================================================
    // HIERARCHY CONFIGURATION
    // =====================================================

    public bool Configure(
        WorldSettings settings,
        TerrainCollisionStreamer streamer
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
            collisionStreamer !=
            streamer
        )
        {
            collisionStreamer =
                streamer;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // TRY GET ACTIVE COLLIDER
    // =====================================================

    public bool TryGetActiveCollider(
        Vector2Int coordinate,
        out MeshCollider meshCollider
    )
    {
        meshCollider =
            null;

        if (
            !activeSlotsByCoordinate.TryGetValue(
                coordinate,
                out ColliderSlot slot
            )
            ||
            slot == null
            ||
            slot.meshCollider == null
            ||
            !slot.meshCollider.enabled
            ||
            slot.meshCollider.sharedMesh == null
        )
        {
            return false;
        }

        meshCollider =
            slot.meshCollider;

        return true;
    }
    
    public bool CanTargetMoveTo(
    Vector3 targetWorldPosition
)
{
    /*
     * Stage C movement safety invariant:
     *
     * The Player may not advance the collision streaming center
     * by more than one terrain chunk beyond the last completely
     * applied collider window.
     *
     * With:
     *
     * active radius   = 2
     * resident radius = 3
     *
     * a one-chunk axis OR diagonal move guarantees that every
     * collider in the old 5 x 5 active window is still contained
     * by the new 7 x 7 resident window while Addressables loads
     * and the collider pool catches up.
     *
     * This prevents the Player from outrunning collision
     * residency during asynchronous streaming.
     */

    if (
        !Application.isPlaying
        ||
        !initialized
        ||
        poolFailed
        ||
        worldSettings == null
        ||
        collisionStreamer == null
        ||
        !collisionStreamer.enabled
        ||
        !collisionStreamer.IsInitialized
        ||
        collisionStreamer.HasFailure
        ||
        !collisionStreamer.HasCurrentTargetChunk
        ||
        !hasAppliedCenterChunk
    )
    {
        return false;
    }

    if (
        !TryCalculateMovementTargetChunk(
            targetWorldPosition,
            out Vector2Int targetChunk,
            out bool targetInsideWorld
        )
    )
    {
        return false;
    }

    /*
     * The streamer's desired/resident center is allowed to be
     * at most one chunk ahead of the last collider window that
     * PhysX has completely received.
     *
     * A larger difference means some other system moved the
     * streaming target without respecting this movement gate.
     */
    if (
        GetChunkDistance(
            collisionStreamer.CurrentTargetChunk,
            appliedCenterChunk
        ) > 1
    )
    {
        return false;
    }

    /*
     * Never commit a Player move that would make the actual
     * target more than one chunk ahead of the completely
     * applied collider center.
     *
     * Once the new resident meshes finish loading,
     * TerrainCollisionColliderPool.LateUpdate() advances
     * appliedCenterChunk and the next chunk transition becomes
     * available.
     */
    if (
        GetChunkDistance(
            targetChunk,
            appliedCenterChunk
        ) > 1
    )
    {
        return false;
    }

    /*
     * Outside the authoritative world there is intentionally no
     * terrain collision to require.
     *
     * We still enforce the one-chunk streaming-center rule above
     * so residency/collider lifetime transitions remain orderly
     * while moving across or back through the world boundary.
     */
    if (!targetInsideWorld)
    {
        return true;
    }

    /*
     * An in-world target must already lie over an active,
     * enabled MeshCollider.
     *
     * Because the active window is a contiguous square and the
     * Player is already inside the previous safe window, this
     * also keeps the horizontal movement path on represented
     * terrain.
     */
    return
        TryGetActiveCollider(
            targetChunk,
            out _
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

        if (!InitializePool())
        {
            enabled =
                false;
        }
    }

    // =====================================================
    // LATE UPDATE
    // =====================================================

    private void LateUpdate()
    {
        if (
            !Application.isPlaying
            ||
            !initialized
            ||
            poolFailed
            ||
            collisionStreamer == null
            ||
            !collisionStreamer.IsInitialized
            ||
            collisionStreamer.HasFailure
            ||
            !collisionStreamer.HasCurrentTargetChunk
        )
        {
            return;
        }

        Vector2Int targetChunk =
            collisionStreamer
                .CurrentTargetChunk;

        if (
            hasAppliedCenterChunk
            &&
            targetChunk ==
                appliedCenterChunk
        )
        {
            return;
        }

        BuildDesiredActiveSet(
            targetChunk
        );

        /*
         * Do not partially move the physics window.
         *
         * The previous active collider set remains intact until
         * every Mesh needed by the new 5 x 5 window is already
         * resident in TerrainCollisionStreamer.
         */
        if (!AreAllDesiredMeshesResident())
        {
            return;
        }

        ApplyDesiredActiveSet(
            targetChunk
        );
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

        ShutdownPool();
    }

    // =====================================================
    // INITIALIZE
    // =====================================================

    private bool InitializePool()
    {
        initialized =
            false;

        poolFailed =
            false;

        hasAppliedCenterChunk =
            false;

        desiredActiveCoordinates.Clear();
        activeSlotsByCoordinate.Clear();
        colliderSlots.Clear();

        if (worldSettings == null)
        {
            Debug.LogError(
                "TerrainCollisionColliderPool cannot initialize.\n\n" +
                "WorldSettings is not assigned.",
                this
            );

            return false;
        }

        if (collisionStreamer == null)
        {
            Debug.LogError(
                "TerrainCollisionColliderPool cannot initialize.\n\n" +
                "TerrainCollisionStreamer is not assigned.",
                this
            );

            return false;
        }

        activeRadius =
            Mathf.Max(
                0,
                activeRadius
            );

        /*
         * Stage C requires one complete resident preload ring around
         * the active physics window.
         *
         * Example:
         *
         * active radius   2 -> 5 x 5 active colliders
         * resident radius 3 -> 7 x 7 resident meshes
         *
         * After a one-chunk axis or diagonal center transition,
         * every collider belonging to the previous active set still
         * has its Mesh retained by the new resident set until the
         * pool completes its atomic reassignment.
         */
        int minimumResidentRadius =
            activeRadius +
            1;

        if (
            collisionStreamer.ResidentRadius <
            minimumResidentRadius
        )
        {
            Debug.LogError(
                "TerrainCollisionColliderPool cannot initialize.\n\n" +

                $"Active Radius: {activeRadius}\n" +
                $"Resident Radius: " +
                $"{collisionStreamer.ResidentRadius}\n" +
                $"Minimum Safe Resident Radius: " +
                $"{minimumResidentRadius}\n\n" +

                "Collision Stage C requires at least one complete " +
                "resident preload ring outside the active physics " +
                "window.\n\n" +

                "Resident Radius must be at least " +
                "Active Radius + 1.",
                this
            );

            return false;
        }

        if (!CollectColliderSlots())
        {
            return false;
        }

        foreach (
            ColliderSlot slot
            in colliderSlots
        )
        {
            ResetSlot(
                slot
            );
        }

        initialized =
            true;

        return true;
    }

    // =====================================================
    // COLLECT FIXED SLOTS
    // =====================================================

    private bool CollectColliderSlots()
    {
        int requiredSlotCount =
            RequiredSlotCount;

        ColliderSlot[] indexedSlots =
            new ColliderSlot[
                requiredSlotCount
            ];

        foreach (
            Transform child
            in transform
        )
        {
            if (
                !TryGetColliderSlotIndex(
                    child.name,
                    out int slotIndex
                )
            )
            {
                continue;
            }

            if (
                slotIndex < 0
                ||
                slotIndex >=
                    requiredSlotCount
            )
            {
                continue;
            }

            if (
                indexedSlots[
                    slotIndex
                ] != null
            )
            {
                Debug.LogError(
                    "TerrainCollisionColliderPool cannot initialize.\n\n" +

                    $"Multiple collider slots use index " +
                    $"{slotIndex}.\n\n" +

                    "Run Sync World Hierarchy again.",
                    this
                );

                return false;
            }

            MeshCollider[] colliders =
                child
                    .GetComponents<MeshCollider>();

            if (colliders.Length != 1)
            {
                Debug.LogError(
                    "TerrainCollisionColliderPool cannot initialize.\n\n" +

                    $"{child.name} must contain exactly one " +
                    "MeshCollider.\n\n" +

                    "Run Sync World Hierarchy again.",
                    this
                );

                return false;
            }

            indexedSlots[
                slotIndex
            ] =
                new ColliderSlot(
                    slotIndex,
                    child,
                    colliders[0]
                );
        }

        for (
            int slotIndex = 0;
            slotIndex < requiredSlotCount;
            slotIndex++
        )
        {
            ColliderSlot slot =
                indexedSlots[
                    slotIndex
                ];

            if (slot == null)
            {
                Debug.LogError(
                    "TerrainCollisionColliderPool cannot initialize.\n\n" +

                    $"Missing collider slot: " +
                    $"{GetColliderSlotName(slotIndex)}\n\n" +

                    "Run Sync World Hierarchy again.",
                    this
                );

                return false;
            }

            colliderSlots.Add(
                slot
            );
        }

        return true;
    }

    // =====================================================
    // BUILD DESIRED ACTIVE SET
    // =====================================================

    private void BuildDesiredActiveSet(
        Vector2Int targetChunk
    )
    {
        desiredActiveCoordinates.Clear();

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

        int radius =
            ActiveRadius;

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

                desiredActiveCoordinates.Add(
                    new Vector2Int(
                        chunkX,
                        chunkZ
                    )
                );
            }
        }
    }

    // =====================================================
    // ARE ALL DESIRED MESHES RESIDENT
    // =====================================================

    private bool AreAllDesiredMeshesResident()
    {
        foreach (
            Vector2Int coordinate
            in desiredActiveCoordinates
        )
        {
            if (
                !collisionStreamer
                    .TryGetResidentMesh(
                        coordinate,
                        out Mesh mesh
                    )
                ||
                mesh == null
            )
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================
    // APPLY DESIRED ACTIVE SET
    // =====================================================

    private void ApplyDesiredActiveSet(
    Vector2Int targetChunk
)
{
    
    Debug.Log(
        $"[Collision Profiler] APPLY ACTIVE SET | " +
        $"Frame {Time.frameCount} | " +
        $"Target Chunk ({targetChunk.x}, {targetChunk.y})",
        this
    );
    
    using (
        ApplyActiveSetProfilerMarker.Auto()
    )
    {
        List<ColliderSlot> freeSlots =
            new List<ColliderSlot>();

        List<Vector2Int> leavingCoordinates =
            new List<Vector2Int>();

        // -------------------------------------------------
        // Leaving coordinates
        // -------------------------------------------------

        foreach (
            KeyValuePair<Vector2Int, ColliderSlot> pair
            in activeSlotsByCoordinate
        )
        {
            if (
                !desiredActiveCoordinates.Contains(
                    pair.Key
                )
            )
            {
                leavingCoordinates.Add(
                    pair.Key
                );
            }
        }

        foreach (
            Vector2Int coordinate
            in leavingCoordinates
        )
        {
            ColliderSlot slot =
                activeSlotsByCoordinate[
                    coordinate
                ];

            ClearSlot(
                slot
            );

            activeSlotsByCoordinate.Remove(
                coordinate
            );

            freeSlots.Add(
                slot
            );
        }

        // -------------------------------------------------
        // Already-unused slots
        // -------------------------------------------------

        foreach (
            ColliderSlot slot
            in colliderSlots
        )
        {
            if (slot.hasCoordinate)
            {
                continue;
            }

            if (!freeSlots.Contains(slot))
            {
                freeSlots.Add(
                    slot
                );
            }
        }

        // -------------------------------------------------
        // Retained coordinates
        // -------------------------------------------------

        int retainedCount =
            0;

        foreach (
            Vector2Int coordinate
            in desiredActiveCoordinates
        )
        {
            if (
                !activeSlotsByCoordinate.TryGetValue(
                    coordinate,
                    out ColliderSlot slot
                )
            )
            {
                continue;
            }

            if (
                !collisionStreamer
                    .TryGetResidentMesh(
                        coordinate,
                        out Mesh residentMesh
                    )
                ||
                residentMesh == null
            )
            {
                FailPool(
                    "A retained active collider lost its " +
                    "resident Mesh.\n\n" +
                    $"Chunk: ({coordinate.x}, {coordinate.y})"
                );

                return;
            }

            EnsureSlotAssignment(
                slot,
                coordinate,
                residentMesh
            );

            retainedCount++;
        }

        // -------------------------------------------------
        // Entering coordinates
        // -------------------------------------------------

        List<Vector2Int> enteringCoordinates =
            new List<Vector2Int>();

        foreach (
            Vector2Int coordinate
            in desiredActiveCoordinates
        )
        {
            if (
                !activeSlotsByCoordinate.ContainsKey(
                    coordinate
                )
            )
            {
                enteringCoordinates.Add(
                    coordinate
                );
            }
        }

        enteringCoordinates.Sort(
            CompareCoordinatesForAssignment
        );

        freeSlots.Sort(
            CompareSlotsByIndex
        );

        if (
            freeSlots.Count <
            enteringCoordinates.Count
        )
        {
            FailPool(
                "The fixed collider pool does not contain enough " +
                "free slots for the desired active window.\n\n" +

                $"Free Slots: {freeSlots.Count}\n" +
                $"Entering Chunks: {enteringCoordinates.Count}\n" +
                $"Required Slots: {RequiredSlotCount}"
            );

            return;
        }

        int assignedCount =
            0;

        for (
            int i = 0;
            i < enteringCoordinates.Count;
            i++
        )
        {
            Vector2Int coordinate =
                enteringCoordinates[i];

            ColliderSlot slot =
                freeSlots[i];

            if (
                !collisionStreamer
                    .TryGetResidentMesh(
                        coordinate,
                        out Mesh mesh
                    )
                ||
                mesh == null
            )
            {
                FailPool(
                    "A desired collision Mesh was no longer " +
                    "resident while applying the collider pool.\n\n" +
                    $"Chunk: ({coordinate.x}, {coordinate.y})"
                );

                return;
            }

            AssignSlot(
                slot,
                coordinate,
                mesh
            );

            activeSlotsByCoordinate[
                coordinate
            ] =
                slot;

            assignedCount++;
        }

        appliedCenterChunk =
            targetChunk;

        hasAppliedCenterChunk =
            true;

        /*
         * Slot transforms only change when the Player crosses a
         * chunk boundary. Synchronize once after the whole batch
         * so physics sees the new fixed-window placement together.
         */
        Physics.SyncTransforms();

        if (logPoolUpdates)
        {
            LogPoolUpdate(
                retainedCount,
                assignedCount,
                leavingCoordinates.Count
            );
        }
    }
}

    // =====================================================
    // ASSIGN SLOT
    // =====================================================

    private void AssignSlot(
        ColliderSlot slot,
        Vector2Int coordinate,
        Mesh mesh
    )
    {
        slot.meshCollider.enabled =
            false;

        slot.meshCollider.sharedMesh =
            null;

        slot.transform.localPosition =
            GetChunkLocalPosition(
                coordinate
            );

        slot.transform.localRotation =
            Quaternion.identity;

        slot.transform.localScale =
            Vector3.one;

        slot.meshCollider.convex =
            TerrainCollisionPhysicsSettings.Convex;

        slot.meshCollider.isTrigger =
            false;

        /*
         * Must exactly match the options used by
         * Physics.BakeMesh() during editor generation.
         *
         * Set this before assigning sharedMesh so Unity can
         * reuse the pre-baked PhysX representation.
         */
        slot.meshCollider.cookingOptions =
            TerrainCollisionPhysicsSettings
                .CookingOptions;

        slot.meshCollider.sharedMesh =
            mesh;

        slot.meshCollider.enabled =
            true;

        slot.coordinate =
            coordinate;

        slot.hasCoordinate =
            true;
    }

    // =====================================================
    // ENSURE RETAINED SLOT ASSIGNMENT
    // =====================================================

    private void EnsureSlotAssignment(
        ColliderSlot slot,
        Vector2Int coordinate,
        Mesh mesh
    )
    {
        Vector3 expectedPosition =
            GetChunkLocalPosition(
                coordinate
            );

        if (
            slot.transform.localPosition !=
            expectedPosition
        )
        {
            slot.transform.localPosition =
                expectedPosition;
        }

        if (
            slot.meshCollider.sharedMesh !=
            mesh
        )
        {
            slot.meshCollider.enabled =
                false;

            slot.meshCollider.sharedMesh =
                null;

            slot.meshCollider.convex =
                TerrainCollisionPhysicsSettings
                    .Convex;

            slot.meshCollider.isTrigger =
                false;

            slot.meshCollider.cookingOptions =
                TerrainCollisionPhysicsSettings
                    .CookingOptions;

            slot.meshCollider.sharedMesh =
                mesh;
        }

        slot.meshCollider.convex =
            TerrainCollisionPhysicsSettings
                .Convex;

        slot.meshCollider.cookingOptions =
            TerrainCollisionPhysicsSettings
                .CookingOptions;

        slot.meshCollider.isTrigger =
            false;

        slot.meshCollider.enabled =
            true;

        slot.coordinate =
            coordinate;

        slot.hasCoordinate =
            true;
    }

    // =====================================================
    // CLEAR SLOT
    // =====================================================

    private static void ClearSlot(
        ColliderSlot slot
    )
    {
        if (slot == null)
        {
            return;
        }

        if (slot.meshCollider != null)
        {
            slot.meshCollider.enabled =
                false;

            slot.meshCollider.sharedMesh =
                null;
        }

        if (slot.transform != null)
        {
            slot.transform.localPosition =
                Vector3.zero;

            slot.transform.localRotation =
                Quaternion.identity;

            slot.transform.localScale =
                Vector3.one;
        }

        slot.coordinate =
            default;

        slot.hasCoordinate =
            false;
    }

    // =====================================================
    // RESET SLOT
    // =====================================================

    private static void ResetSlot(
        ColliderSlot slot
    )
    {
        ClearSlot(
            slot
        );

        if (
            slot != null
            &&
            slot.meshCollider != null
        )
        {
            slot.meshCollider.convex =
                TerrainCollisionPhysicsSettings.Convex;

            slot.meshCollider.isTrigger =
                false;

            slot.meshCollider.cookingOptions =
                TerrainCollisionPhysicsSettings
                    .CookingOptions;
        }
    }

    // =====================================================
    // CHUNK LOCAL POSITION
    // =====================================================

    private Vector3 GetChunkLocalPosition(
        Vector2Int coordinate
    )
    {
        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        return
            new Vector3(
                coordinate.x *
                    chunkSize,
                0f,
                coordinate.y *
                    chunkSize
            );
    }
    
    private bool TryCalculateMovementTargetChunk(
    Vector3 worldPosition,
    out Vector2Int targetChunk,
    out bool insideWorld
)
{
    targetChunk =
        default;

    insideWorld =
        false;

    if (
        worldSettings == null
        ||
        !IsFinite(
            worldPosition
        )
    )
    {
        return false;
    }

    float chunkSize =
        Mathf.Max(
            0.01f,
            worldSettings.chunkSize
        );

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

    float worldSizeX =
        gridWidth *
        chunkSize;

    float worldSizeZ =
        gridHeight *
        chunkSize;

    insideWorld =
        worldPosition.x >= 0f
        &&
        worldPosition.z >= 0f
        &&
        worldPosition.x <= worldSizeX
        &&
        worldPosition.z <= worldSizeZ;

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
     * Match TerrainCollisionStreamer's maximum-boundary rule.
     *
     * A position exactly on the world's maximum X/Z edge belongs
     * to the final generated terrain chunk rather than a
     * non-existent chunk one index beyond the world.
     */
    if (
        Mathf.Abs(
            worldPosition.x -
            worldSizeX
        ) <=
        0.0001f
    )
    {
        chunkX =
            gridWidth -
            1;
    }

    if (
        Mathf.Abs(
            worldPosition.z -
            worldSizeZ
        ) <=
        0.0001f
    )
    {
        chunkZ =
            gridHeight -
            1;
    }

    targetChunk =
        new Vector2Int(
            chunkX,
            chunkZ
        );

    return true;
}


private static int GetChunkDistance(
    Vector2Int a,
    Vector2Int b
)
{
    /*
     * The collision windows are square/Chebyshev windows, so
     * diagonal adjacency counts as one chunk transition.
     */
    return
        Mathf.Max(
            Mathf.Abs(
                a.x -
                b.x
            ),
            Mathf.Abs(
                a.y -
                b.y
            )
        );
}


private static bool IsFinite(
    float value
)
{
    return
        !float.IsNaN(
            value
        )
        &&
        !float.IsInfinity(
            value
        );
}


private static bool IsFinite(
    Vector3 value
)
{
    return
        IsFinite(
            value.x
        )
        &&
        IsFinite(
            value.y
        )
        &&
        IsFinite(
            value.z
        );
}

    // =====================================================
    // ASSIGNMENT ORDER
    // =====================================================

    private int CompareCoordinatesForAssignment(
        Vector2Int a,
        Vector2Int b
    )
    {
        int aDistance =
            Mathf.Max(
                Mathf.Abs(
                    a.x -
                    collisionStreamer.CurrentTargetChunk.x
                ),
                Mathf.Abs(
                    a.y -
                    collisionStreamer.CurrentTargetChunk.y
                )
            );

        int bDistance =
            Mathf.Max(
                Mathf.Abs(
                    b.x -
                    collisionStreamer.CurrentTargetChunk.x
                ),
                Mathf.Abs(
                    b.y -
                    collisionStreamer.CurrentTargetChunk.y
                )
            );

        int distanceComparison =
            aDistance.CompareTo(
                bDistance
            );

        if (distanceComparison != 0)
        {
            return distanceComparison;
        }

        int zComparison =
            a.y.CompareTo(
                b.y
            );

        if (zComparison != 0)
        {
            return zComparison;
        }

        return
            a.x.CompareTo(
                b.x
            );
    }

    private static int CompareSlotsByIndex(
        ColliderSlot a,
        ColliderSlot b
    )
    {
        return
            a.index.CompareTo(
                b.index
            );
    }

    // =====================================================
    // FAIL POOL
    // =====================================================

    private void FailPool(
        string message
    )
    {
        poolFailed =
            true;

        foreach (
            ColliderSlot slot
            in colliderSlots
        )
        {
            ResetSlot(
                slot
            );
        }

        activeSlotsByCoordinate.Clear();
        desiredActiveCoordinates.Clear();

        Debug.LogError(
            "Terrain collision collider pool FAILED.\n\n" +
            message,
            this
        );
    }

    // =====================================================
    // SHUTDOWN
    // =====================================================

    private void ShutdownPool()
    {
        foreach (
            ColliderSlot slot
            in colliderSlots
        )
        {
            ResetSlot(
                slot
            );
        }

        activeSlotsByCoordinate.Clear();
        desiredActiveCoordinates.Clear();
        colliderSlots.Clear();

        initialized =
            false;

        poolFailed =
            false;

        hasAppliedCenterChunk =
            false;
    }

    // =====================================================
    // LOG UPDATE
    // =====================================================

    private void LogPoolUpdate(
        int retainedCount,
        int assignedCount,
        int clearedCount
    )
    {
        Debug.Log(
            "Terrain collision collider pool updated.\n\n" +

            $"Target Chunk: " +
            $"({appliedCenterChunk.x}, " +
            $"{appliedCenterChunk.y})\n\n" +

            $"Active Radius: {ActiveRadius}\n" +
            $"Desired Active Colliders: " +
            $"{desiredActiveCoordinates.Count:N0}\n" +
            $"Active Colliders: " +
            $"{activeSlotsByCoordinate.Count:N0}\n" +
            $"Fixed Slots: {colliderSlots.Count:N0}\n\n" +

            "Collider Transition:\n" +
            $"Retained: {retainedCount:N0}\n" +
            $"Assigned: {assignedCount:N0}\n" +
            $"Cleared: {clearedCount:N0}",
            this
        );
    }

    // =====================================================
    // MANUAL STATE LOG
    // =====================================================

    [ContextMenu("Log Collision Collider Pool State")]
    public void LogPoolState()
    {
        string centerText =
            hasAppliedCenterChunk
                ? $"({appliedCenterChunk.x}, " +
                  $"{appliedCenterChunk.y})"
                : "Unavailable";

        Debug.Log(
            "Terrain collision collider pool state.\n\n" +

            $"Initialized: {initialized}\n" +
            $"Failure: {poolFailed}\n" +
            $"Applied Center Chunk: {centerText}\n\n" +

            $"Active Radius: {ActiveRadius}\n" +
            $"Required Slots: {RequiredSlotCount:N0}\n" +
            $"Collected Slots: {colliderSlots.Count:N0}\n" +
            $"Desired Active Colliders: " +
            $"{desiredActiveCoordinates.Count:N0}\n" +
            $"Active Colliders: " +
            $"{activeSlotsByCoordinate.Count:N0}",
            this
        );
    }

    // =====================================================
    // COLLIDER SLOT
    // =====================================================

    private sealed class ColliderSlot
    {
        public readonly int index;

        public readonly Transform transform;

        public readonly MeshCollider meshCollider;

        public Vector2Int coordinate;

        public bool hasCoordinate;

        public ColliderSlot(
            int index,
            Transform transform,
            MeshCollider meshCollider
        )
        {
            this.index =
                index;

            this.transform =
                transform;

            this.meshCollider =
                meshCollider;

            coordinate =
                default;

            hasCoordinate =
                false;
        }
    }
}
