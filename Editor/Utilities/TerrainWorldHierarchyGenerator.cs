using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TerrainWorldHierarchyGenerator
{
    // =====================================================
    // ROOT NAMES
    // =====================================================

    public const string WorldRootName =
        "WorldRoot";

    public const string PreviewRootName =
        "Preview";

    public const string CollisionRootName =
        "Collision";

    public const string ClipmapRootName =
        "Clipmap";

    // =====================================================
    // GENERATED CHILD NAMES
    // =====================================================

    private const string LOD0ChildName =
        "LOD0";

    private const string ClipmapCenterName =
        "Center_LOD0";

    // =====================================================
    // PATHS
    // =====================================================

    private const string BaseMeshPath =
        WorldMeshesPaths.BaseMeshAssetPath;

    private const string ChunkMeshFolder =
        WorldMeshesPaths.GeneratedChunkMeshes;

    private const string PreviewTerrainMaterialPath =
        WorldMeshesPaths.PreviewTerrainMaterialPath;

    private const string ClipmapTerrainMaterialPath =
        WorldMeshesPaths.ClipmapTerrainMaterialPath;

    // =====================================================
    // TOLERANCES
    // =====================================================

    private const float SizeTolerance =
        0.001f;



// =====================================================
// SYNC WORLD HIERARCHY
// =====================================================

public static void SyncWorldHierarchy(
    WorldSettings worldSettings
)
{
    // -------------------------------------------------
    // Validate basic state
    // -------------------------------------------------

    if (worldSettings == null)
    {
        Debug.LogError(
            "Cannot synchronize world hierarchy: " +
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
            "World hierarchy synchronization must be " +
            "performed outside Play Mode."
        );

        return;
    }

    // =====================================================
    // PREFLIGHT GENERATED DATA
    // =====================================================

    if (
        !ValidateChunkMeshes(
            worldSettings,
            out Dictionary<Vector2Int, Mesh>
                previewMeshes
        )
    )
    {
        return;
    }

    if (
        !ValidateClipmapMeshes(
            worldSettings,
            out ClipmapMeshSet clipmapMeshes
        )
    )
    {
        return;
    }

    // =====================================================
    // TERRAIN HEIGHT RANGE
    // =====================================================

    if (
        !TryCalculatePreviewHeightRange(
            previewMeshes,
            out float minimumTerrainHeight,
            out float maximumTerrainHeight
        )
    )
    {
        return;
    }

    // =====================================================
    // MATERIALS
    // =====================================================

    Material previewTerrainMaterial =
        AssetDatabase.LoadAssetAtPath<Material>(
            PreviewTerrainMaterialPath
        );

    if (previewTerrainMaterial == null)
    {
        Debug.LogError(
            "Cannot synchronize world hierarchy.\n\n" +

            "Preview terrain material could not be found:\n" +
            $"{PreviewTerrainMaterialPath}"
        );

        return;
    }

    Material clipmapTerrainMaterial =
        AssetDatabase.LoadAssetAtPath<Material>(
            ClipmapTerrainMaterialPath
        );

    if (clipmapTerrainMaterial == null)
    {
        Debug.LogError(
            "Cannot synchronize world hierarchy.\n\n" +

            "Clipmap terrain material could not be found:\n" +
            $"{ClipmapTerrainMaterialPath}"
        );

        return;
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
            "No valid active scene is available."
        );

        return;
    }

    // -------------------------------------------------
    // World layout
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

    float worldSizeX =
        gridWidth *
        chunkSize;

    float worldSizeZ =
        gridHeight *
        chunkSize;

    Vector3 clipmapCenterPosition =
        new Vector3(
            worldSizeX * 0.5f,
            0f,
            worldSizeZ * 0.5f
        );

    // =====================================================
    // WORLD ROOT
    // =====================================================

    if (
        !TryGetWorldRoot(
            scene,
            out GameObject worldRoot
        )
    )
    {
        return;
    }

    bool hierarchyChanged =
        false;

    if (worldRoot == null)
    {
        worldRoot =
            new GameObject(
                WorldRootName
            );

        hierarchyChanged =
            true;
    }

    hierarchyChanged |=
        SynchronizeTransform(
            worldRoot.transform,
            Vector3.zero,
            true
        );

    // =====================================================
    // REMOVE LEGACY STRUCTURE
    // =====================================================

    int legacyChunkCount =
        RemoveLegacyDirectChunkObjects(
            worldRoot.transform
        );

    if (legacyChunkCount > 0)
    {
        hierarchyChanged =
            true;
    }

    // =====================================================
    // TOP-LEVEL GENERATED ROOTS
    // =====================================================

    Transform previewRoot =
        GetOrCreateUniqueDirectChild(
            worldRoot.transform,
            PreviewRootName,
            out bool previewRootChanged
        );

    hierarchyChanged |=
        previewRootChanged;

    hierarchyChanged |=
        SynchronizeTransform(
            previewRoot,
            Vector3.zero,
            true
        );

    Transform collisionRoot =
        GetOrCreateUniqueDirectChild(
            worldRoot.transform,
            CollisionRootName,
            out bool collisionRootChanged
        );

    hierarchyChanged |=
        collisionRootChanged;

    hierarchyChanged |=
        SynchronizeTransform(
            collisionRoot,
            Vector3.zero,
            true
        );

    Transform clipmapRoot =
        GetOrCreateUniqueDirectChild(
            worldRoot.transform,
            ClipmapRootName,
            out bool clipmapRootChanged
        );

    hierarchyChanged |=
        clipmapRootChanged;

    /*
     * Clipmap meshes are generated around their local origin.
     *
     * The authoritative terrain world begins at (0, 0) and
     * extends to:
     *
     * gridWidth  * chunkSize
     * gridHeight * chunkSize
     *
     * Therefore the stationary clipmap root is positioned at
     * the center of that world so its centered geometry spans
     * the terrain correctly.
     */

    hierarchyChanged |=
        SynchronizeTransform(
            clipmapRoot,
            clipmapCenterPosition,
            true
        );

    // =====================================================
    // SYNCHRONIZE BRANCHES
    // =====================================================

    HierarchySyncStats previewStats =
        new HierarchySyncStats();

    bool cancelled =
        false;

    try
    {
        // -------------------------------------------------
        // Preview
        // -------------------------------------------------

        if (
            !SynchronizePreviewHierarchy(
                previewRoot,
                gridWidth,
                gridHeight,
                chunkSize,
                previewMeshes,
                previewTerrainMaterial,
                previewStats,
                out bool previewChanged,
                out cancelled
            )
        )
        {
            hierarchyChanged |=
                previewChanged;

            if (cancelled)
            {
                MarkSceneDirtyIfNeeded(
                    scene,
                    hierarchyChanged
                );

                LogCancelled();

                return;
            }

            return;
        }

        hierarchyChanged |=
            previewChanged;

        // -------------------------------------------------
        // Clipmap
        // -------------------------------------------------

        /*
         * Synchronize Clipmap first so TerrainClipmapController
         * exists before the collision streamer resolves its target.
         */
        hierarchyChanged |=
            SynchronizeClipmapHierarchy(
                clipmapRoot,
                worldSettings,
                clipmapMeshes,
                clipmapTerrainMaterial,
                minimumTerrainHeight,
                maximumTerrainHeight
            );

        // -------------------------------------------------
        // Collision runtime streamer
        // -------------------------------------------------

        hierarchyChanged |=
            SynchronizeCollisionStreamer(
                collisionRoot.gameObject,
                clipmapRoot.gameObject,
                worldSettings
            );

        // -------------------------------------------------
        // Collision collider pool
        // -------------------------------------------------

        hierarchyChanged |=
            SynchronizeCollisionColliderPool(
                collisionRoot.gameObject,
                worldSettings
            );
    }
    finally
    {
        EditorUtility.ClearProgressBar();
    }

    // =====================================================
    // SAVE SCENE STATE
    // =====================================================

    MarkSceneDirtyIfNeeded(
        scene,
        hierarchyChanged
    );

    Selection.activeGameObject =
        clipmapRoot.gameObject;

    // =====================================================
    // COMPLETE
    // =====================================================

    Debug.Log(
        "World hierarchy synchronization complete.\n\n" +

        $"World Grid: " +
        $"{gridWidth} x {gridHeight}\n" +

        $"Chunk Size: " +
        $"{chunkSize}\n" +

        $"World Size: " +
        $"{worldSizeX} x {worldSizeZ}\n\n" +

        $"Clipmap Center: " +
        $"({clipmapCenterPosition.x}, " +
        $"{clipmapCenterPosition.y}, " +
        $"{clipmapCenterPosition.z})\n\n" +

        $"Preview Material: " +
        $"{previewTerrainMaterial.name}\n" +

        $"Clipmap Material: " +
        $"{clipmapTerrainMaterial.name}\n\n" +

        $"Legacy Chunks Removed: " +
        $"{legacyChunkCount}\n\n" +

        "Preview\n" +
        $"Created: {previewStats.created}\n" +
        $"Updated: {previewStats.updated}\n" +
        $"Removed: {previewStats.removed}\n" +
        $"Unchanged: {previewStats.unchanged}\n\n" +

        "Collision\n" +
        "Runtime Mesh Residency: TerrainCollisionStreamer\n" +
        "Active Physics: TerrainCollisionColliderPool\n" +
        $"Collider Slots: " +
        $"{TerrainCollisionColliderPool.CalculateRequiredSlotCount(TerrainCollisionColliderPool.DefaultActiveRadius)}\n" +
        "Static Per-Chunk Objects: None\n\n" +

        $"Clipmap Levels: " +
        $"{worldSettings.clipmapLevelCount}\n\n" +

        "Hierarchy:\n" +

        $"{WorldRootName}\n" +
        $"├── {PreviewRootName}\n" +
        $"├── {CollisionRootName} " +
        "[TerrainCollisionStreamer, TerrainCollisionColliderPool]\n" +
        $"└── {ClipmapRootName}"
    );
}


    // =====================================================
    // PREVIEW HIERARCHY
    // =====================================================

    private static bool SynchronizePreviewHierarchy(
        Transform previewRoot,
        int gridWidth,
        int gridHeight,
        float chunkSize,
        Dictionary<Vector2Int, Mesh> previewMeshes,
        Material terrainMaterial,
        HierarchySyncStats stats,
        out bool changed,
        out bool cancelled
    )
    {
        changed =
            false;

        cancelled =
            false;

        Dictionary<Vector2Int, GameObject>
            existingChunks =
                FindExistingChunkObjects(
                    previewRoot,
                    out List<GameObject> duplicates
                );

        // -------------------------------------------------
        // Duplicate generated chunks
        // -------------------------------------------------

        foreach (
            GameObject duplicate
            in duplicates
        )
        {
            Object.DestroyImmediate(
                duplicate
            );

            stats.removed++;

            changed =
                true;
        }

        // -------------------------------------------------
        // Obsolete chunks
        // -------------------------------------------------

        foreach (
            KeyValuePair<Vector2Int, GameObject> pair
            in existingChunks
        )
        {
            Vector2Int coordinate =
                pair.Key;

            bool outsideGrid =
                coordinate.x < 0
                ||
                coordinate.y < 0
                ||
                coordinate.x >= gridWidth
                ||
                coordinate.y >= gridHeight;

            if (!outsideGrid)
            {
                continue;
            }

            Object.DestroyImmediate(
                pair.Value
            );

            stats.removed++;

            changed =
                true;
        }

        // -------------------------------------------------
        // Required chunks
        // -------------------------------------------------

        int totalChunks =
            gridWidth *
            gridHeight;

        int currentChunk =
            0;

        for (
            int z = 0;
            z < gridHeight;
            z++
        )
        {
            for (
                int x = 0;
                x < gridWidth;
                x++
            )
            {
                cancelled =
                    ShowProgress(
                        "Synchronizing Preview",
                        $"Chunk ({x}, {z})",
                        currentChunk,
                        totalChunks
                    );

                if (cancelled)
                {
                    return false;
                }

                Vector2Int coordinate =
                    new Vector2Int(
                        x,
                        z
                    );

                Mesh renderMesh =
                    previewMeshes[
                        coordinate
                    ];

                if (
                    !existingChunks.TryGetValue(
                        coordinate,
                        out GameObject chunkObject
                    )
                    ||
                    chunkObject == null
                )
                {
                    CreatePreviewChunkObject(
                        previewRoot,
                        coordinate,
                        chunkSize,
                        renderMesh,
                        terrainMaterial
                    );

                    stats.created++;

                    changed =
                        true;
                }
                else
                {
                    bool chunkChanged =
                        SynchronizePreviewChunkObject(
                            chunkObject,
                            coordinate,
                            chunkSize,
                            renderMesh,
                            terrainMaterial
                        );

                    if (chunkChanged)
                    {
                        stats.updated++;

                        changed =
                            true;
                    }
                    else
                    {
                        stats.unchanged++;
                    }
                }

                currentChunk++;
            }
        }

        return true;
    }

    // =====================================================
    // CREATE PREVIEW CHUNK
    // =====================================================

    private static GameObject CreatePreviewChunkObject(
        Transform previewRoot,
        Vector2Int coordinate,
        float chunkSize,
        Mesh renderMesh,
        Material terrainMaterial
    )
    {
        GameObject chunkObject =
            new GameObject(
                GetChunkObjectName(
                    coordinate.x,
                    coordinate.y
                )
            );

        chunkObject.transform.SetParent(
            previewRoot,
            false
        );

        SynchronizePreviewChunkObject(
            chunkObject,
            coordinate,
            chunkSize,
            renderMesh,
            terrainMaterial
        );

        return chunkObject;
    }

    // =====================================================
    // SYNCHRONIZE PREVIEW CHUNK
    // =====================================================

    private static bool SynchronizePreviewChunkObject(
        GameObject chunkObject,
        Vector2Int coordinate,
        float chunkSize,
        Mesh renderMesh,
        Material terrainMaterial
    )
    {
        bool changed =
            false;

        // -------------------------------------------------
        // Chunk transform
        // -------------------------------------------------

        string expectedName =
            GetChunkObjectName(
                coordinate.x,
                coordinate.y
            );

        if (
            chunkObject.name !=
            expectedName
        )
        {
            chunkObject.name =
                expectedName;

            changed =
                true;
        }

        changed |=
            SynchronizeTransform(
                chunkObject.transform,
                GetChunkPosition(
                    coordinate,
                    chunkSize
                ),
                false
            );

        // -------------------------------------------------
        // Remove legacy Collision child if present
        // -------------------------------------------------

        changed |=
            RemoveDirectChildrenNamed(
                chunkObject.transform,
                CollisionRootName
            )
            >
            0;

        // -------------------------------------------------
        // LOD0 child
        // -------------------------------------------------

        Transform lod0Transform =
            GetOrCreateUniqueDirectChild(
                chunkObject.transform,
                LOD0ChildName,
                out bool lod0CreatedOrCleaned
            );

        changed |=
            lod0CreatedOrCleaned;

        changed |=
            SynchronizeTransform(
                lod0Transform,
                Vector3.zero,
                true
            );

        changed |=
            SynchronizeRenderableMeshObject(
                lod0Transform.gameObject,
                renderMesh,
                terrainMaterial
            );

        return changed;
    }
    
    // =====================================================
// COLLISION STREAMER
// =====================================================

private static bool SynchronizeCollisionStreamer(
    GameObject collisionObject,
    GameObject clipmapObject,
    WorldSettings worldSettings
)
{
    bool changed =
        false;

    // -------------------------------------------------
    // Component
    // -------------------------------------------------

    TerrainCollisionStreamer[] streamers =
        collisionObject
            .GetComponents<TerrainCollisionStreamer>();

    TerrainCollisionStreamer streamer;

    if (streamers.Length == 0)
    {
        streamer =
            collisionObject
                .AddComponent<TerrainCollisionStreamer>();

        changed =
            true;
    }
    else
    {
        streamer =
            streamers[0];

        /*
         * The generated hierarchy owns this component.
         * Keep exactly one streamer on the Collision root.
         */
        for (
            int i = 1;
            i < streamers.Length;
            i++
        )
        {
            Object.DestroyImmediate(
                streamers[i]
            );

            changed =
                true;
        }
    }

    // -------------------------------------------------
    // Enable component
    // -------------------------------------------------

    if (!streamer.enabled)
    {
        streamer.enabled =
            true;

        changed =
            true;
    }

    // -------------------------------------------------
    // Prepared collision manifest
    // -------------------------------------------------

    TerrainCollisionManifest manifest =
        AssetDatabase
            .LoadAssetAtPath<TerrainCollisionManifest>(
                WorldMeshesPaths
                    .CollisionManifestAssetPath
            );

    if (manifest == null)
    {
        Debug.LogWarning(
            "TerrainCollisionStreamer could not be fully " +
            "configured because the collision runtime " +
            "manifest does not exist.\n\n" +

            "Run 'Prepare Collision Meshes For Runtime' " +
            "and then Sync World Hierarchy again."
        );
    }
    else if (!manifest.isComplete)
    {
        Debug.LogWarning(
            "TerrainCollisionStreamer could not be fully " +
            "configured because CollisionManifest is " +
            "marked incomplete.\n\n" +

            "Run 'Prepare Collision Meshes For Runtime' " +
            "again and then Sync World Hierarchy."
        );
    }

    // -------------------------------------------------
    // Source data
    // -------------------------------------------------

    changed |=
        streamer.Configure(
            worldSettings,
            manifest
        );

    // -------------------------------------------------
    // Streaming target
    // -------------------------------------------------

    /*
     * TerrainClipmapController already owns the Player/follow
     * target used by the moving visual terrain.
     *
     * Reuse that same Transform for collision residency instead
     * of requiring a second manually maintained Player reference.
     */
    TerrainClipmapController clipmapController =
        clipmapObject != null
            ? clipmapObject
                .GetComponent<TerrainClipmapController>()
            : null;

    Transform streamingTarget =
        clipmapController != null
            ? clipmapController.Target
            : null;

    if (clipmapController == null)
    {
        Debug.LogWarning(
            "TerrainCollisionStreamer could not resolve the " +
            "terrain movement target because " +
            "TerrainClipmapController is missing from the " +
            "Clipmap root.\n\n" +

            "Run Sync World Hierarchy again."
        );
    }
    else if (streamingTarget == null)
    {
        Debug.LogWarning(
            "TerrainCollisionStreamer could not resolve its " +
            "Streaming Target because TerrainClipmapController " +
            "does not have a Target assigned.\n\n" +

            "Assign the Player Transform to the Clipmap " +
            "controller Target field, then run Sync World " +
            "Hierarchy again."
        );
    }

    changed |=
        streamer.SetStreamingTarget(
            streamingTarget
        );

    return changed;
}

private static bool SynchronizeCollisionColliderPool(
    GameObject collisionObject,
    WorldSettings worldSettings
)
{
    bool changed =
        false;

    // -------------------------------------------------
    // TerrainCollisionStreamer dependency
    // -------------------------------------------------

    TerrainCollisionStreamer streamer =
        collisionObject
            .GetComponent<TerrainCollisionStreamer>();

    if (streamer == null)
    {
        Debug.LogError(
            "Cannot synchronize collision collider pool.\n\n" +
            "TerrainCollisionStreamer is missing from the " +
            "Collision root."
        );

        return false;
    }

    // -------------------------------------------------
    // Component
    // -------------------------------------------------

    TerrainCollisionColliderPool[] pools =
        collisionObject
            .GetComponents<TerrainCollisionColliderPool>();

    TerrainCollisionColliderPool pool;

    if (pools.Length == 0)
    {
        pool =
            collisionObject
                .AddComponent<TerrainCollisionColliderPool>();

        changed =
            true;
    }
    else
    {
        pool =
            pools[0];

        for (
            int i = 1;
            i < pools.Length;
            i++
        )
        {
            Object.DestroyImmediate(
                pools[i]
            );

            changed =
                true;
        }
    }

    // -------------------------------------------------
    // Enable
    // -------------------------------------------------

    if (!pool.enabled)
    {
        pool.enabled =
            true;

        changed =
            true;
    }

    // -------------------------------------------------
    // Configure
    // -------------------------------------------------

    changed |=
        pool.Configure(
            worldSettings,
            streamer
        );

    // -------------------------------------------------
    // Fixed collider slots
    // -------------------------------------------------

    changed |=
        SynchronizeCollisionColliderSlots(
            collisionObject.transform,
            pool.RequiredSlotCount
        );

    return changed;
}

// =====================================================
// COLLISION COLLIDER SLOTS
// =====================================================

private static bool SynchronizeCollisionColliderSlots(
    Transform collisionRoot,
    int requiredSlotCount
)
{
    bool changed =
        false;

    int safeRequiredSlotCount =
        Mathf.Max(
            1,
            requiredSlotCount
        );

    // -------------------------------------------------
    // Remove obsolete generated slots
    // -------------------------------------------------

    List<GameObject> obsoleteSlots =
        new List<GameObject>();

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

        if (
            slotIndex >=
            safeRequiredSlotCount
        )
        {
            obsoleteSlots.Add(
                child.gameObject
            );
        }
    }

    foreach (
        GameObject obsoleteSlot
        in obsoleteSlots
    )
    {
        Object.DestroyImmediate(
            obsoleteSlot
        );

        changed =
            true;
    }

    // -------------------------------------------------
    // Required slots
    // -------------------------------------------------

    for (
        int slotIndex = 0;
        slotIndex < safeRequiredSlotCount;
        slotIndex++
    )
    {
        string slotName =
            TerrainCollisionColliderPool
                .GetColliderSlotName(
                    slotIndex
                );

        Transform slotTransform =
            GetOrCreateUniqueDirectChild(
                collisionRoot,
                slotName,
                out bool slotCreatedOrCleaned
            );

        changed |=
            slotCreatedOrCleaned;

        changed |=
            SynchronizeTransform(
                slotTransform,
                Vector3.zero,
                true
            );

        changed |=
            SynchronizeCollisionColliderSlot(
                slotTransform.gameObject
            );
    }

    return changed;
}

// =====================================================
// ONE COLLISION COLLIDER SLOT
// =====================================================

private static bool SynchronizeCollisionColliderSlot(
    GameObject slotObject
)
{
    bool changed =
        false;

    // -------------------------------------------------
    // Layer follows Collision root
    // -------------------------------------------------

    if (
        slotObject.transform.parent != null
        &&
        slotObject.layer !=
            slotObject.transform.parent.gameObject.layer
    )
    {
        slotObject.layer =
            slotObject.transform.parent.gameObject.layer;

        changed =
            true;
    }

    // -------------------------------------------------
    // Exactly one MeshCollider
    // -------------------------------------------------

    MeshCollider[] colliders =
        slotObject
            .GetComponents<MeshCollider>();

    MeshCollider meshCollider;

    if (colliders.Length == 0)
    {
        meshCollider =
            slotObject
                .AddComponent<MeshCollider>();

        changed =
            true;
    }
    else
    {
        meshCollider =
            colliders[0];

        for (
            int i = 1;
            i < colliders.Length;
            i++
        )
        {
            Object.DestroyImmediate(
                colliders[i]
            );

            changed =
                true;
        }
    }

    // -------------------------------------------------
    // Edit-mode default state
    // -------------------------------------------------

    if (meshCollider.enabled)
    {
        meshCollider.enabled =
            false;

        changed =
            true;
    }

    if (meshCollider.sharedMesh != null)
    {
        meshCollider.sharedMesh =
            null;

        changed =
            true;
    }

    if (meshCollider.convex)
    {
        meshCollider.convex =
            false;

        changed =
            true;
    }

    if (meshCollider.isTrigger)
    {
        meshCollider.isTrigger =
            false;

        changed =
            true;
    }

    return changed;
}

    

    // =====================================================
    // CLIPMAP HIERARCHY
    // =====================================================

    private static bool SynchronizeClipmapHierarchy(
        Transform clipmapRoot,
        WorldSettings worldSettings,
        ClipmapMeshSet meshes,
        Material clipmapTerrainMaterial,
        float minimumTerrainHeight,
        float maximumTerrainHeight
    )
    {
        bool changed =
            false;

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        // =====================================================
        // CLIPMAP CONTROLLER
        // =====================================================

        changed |=
            SynchronizeClipmapController(
                clipmapRoot.gameObject,
                worldSettings
            );

        // =====================================================
        // HEIGHTMAP STREAMER
        // =====================================================

        changed |=
            SynchronizeHeightmapStreamer(
                clipmapRoot.gameObject,
                worldSettings
            );

        // =====================================================
        // HEIGHTMAP CACHE VALIDATOR
        // =====================================================

        changed |=
            SynchronizeHeightmapCacheValidator(
                clipmapRoot.gameObject
            );

        // =====================================================
        // DISPLACEMENT BOUNDS
        // =====================================================

        changed |=
            SynchronizeClipmapBoundsController(
                clipmapRoot.gameObject,
                minimumTerrainHeight,
                maximumTerrainHeight
            );

        // =====================================================
        // DISPLACEMENT VALIDATOR
        // =====================================================

        changed |=
            SynchronizeClipmapDisplacementValidator(
                clipmapRoot.gameObject
            );

        // -------------------------------------------------
        // Remove obsolete LOD groups
        // -------------------------------------------------

        List<GameObject> obsoleteObjects =
            new List<GameObject>();

        foreach (
            Transform child
            in clipmapRoot
        )
        {
            if (
                TryGetLODGroupLevel(
                    child.name,
                    out int level
                )
            )
            {
                if (
                    level < 1
                    ||
                    level >= levelCount
                )
                {
                    obsoleteObjects.Add(
                        child.gameObject
                    );
                }
            }
        }

        foreach (
            GameObject obsolete
            in obsoleteObjects
        )
        {
            Object.DestroyImmediate(
                obsolete
            );

            changed =
                true;
        }

        // =====================================================
        // CENTER LOD0
        // =====================================================

        Transform centerTransform =
            GetOrCreateUniqueDirectChild(
                clipmapRoot,
                ClipmapCenterName,
                out bool centerChanged
            );

        changed |=
            centerChanged;

        changed |=
            SynchronizeTransform(
                centerTransform,
                Vector3.zero,
                true
            );

        changed |=
            SynchronizeRenderableMeshObject(
                centerTransform.gameObject,
                meshes.center,
                clipmapTerrainMaterial
            );

        // =====================================================
        // OUTER LEVELS
        // =====================================================

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            string levelName =
                GetClipmapLODGroupName(
                    level
                );

            Transform levelTransform =
                GetOrCreateUniqueDirectChild(
                    clipmapRoot,
                    levelName,
                    out bool levelChanged
                );

            changed |=
                levelChanged;

            changed |=
                SynchronizeTransform(
                    levelTransform,
                    Vector3.zero,
                    true
                );

            string ringName =
                GetClipmapRingObjectName(
                    level
                );

            string stitchName =
                GetClipmapStitchObjectName(
                    level - 1,
                    level
                );

            // ---------------------------------------------
            // Remove obsolete generated children
            // ---------------------------------------------

            List<GameObject> obsoleteLevelChildren =
                new List<GameObject>();

            foreach (
                Transform child
                in levelTransform
            )
            {
                bool generatedClipmapChild =
                    child.name.StartsWith(
                        "Ring_LOD"
                    )
                    ||
                    child.name.StartsWith(
                        "Stitch_LOD"
                    );

                if (
                    generatedClipmapChild
                    &&
                    child.name != ringName
                    &&
                    child.name != stitchName
                )
                {
                    obsoleteLevelChildren.Add(
                        child.gameObject
                    );
                }
            }

            foreach (
                GameObject obsolete
                in obsoleteLevelChildren
            )
            {
                Object.DestroyImmediate(
                    obsolete
                );

                changed =
                    true;
            }

            // ---------------------------------------------
            // Ring
            // ---------------------------------------------

            Transform ringTransform =
                GetOrCreateUniqueDirectChild(
                    levelTransform,
                    ringName,
                    out bool ringChanged
                );

            changed |=
                ringChanged;

            changed |=
                SynchronizeTransform(
                    ringTransform,
                    Vector3.zero,
                    true
                );

            changed |=
                SynchronizeRenderableMeshObject(
                    ringTransform.gameObject,
                    meshes.rings[
                        level
                    ],
                    clipmapTerrainMaterial
                );

            // ---------------------------------------------
            // Stitch
            // ---------------------------------------------

            Transform stitchTransform =
                GetOrCreateUniqueDirectChild(
                    levelTransform,
                    stitchName,
                    out bool stitchChanged
                );

            changed |=
                stitchChanged;

            changed |=
                SynchronizeTransform(
                    stitchTransform,
                    Vector3.zero,
                    true
                );

            changed |=
                SynchronizeRenderableMeshObject(
                    stitchTransform.gameObject,
                    meshes.stitches[
                        level
                    ],
                    clipmapTerrainMaterial
                );
        }

        return changed;
    }
    
    // =====================================================
// CLIPMAP CONTROLLER
// =====================================================

    private static bool SynchronizeClipmapController(
        GameObject clipmapObject,
        WorldSettings worldSettings
    )
    {
        bool changed =
            false;

        TerrainClipmapController[] controllers =
            clipmapObject
                .GetComponents<TerrainClipmapController>();

        TerrainClipmapController controller;

        // -------------------------------------------------
        // Create if missing
        // -------------------------------------------------

        if (controllers.Length == 0)
        {
            controller =
                clipmapObject
                    .AddComponent<TerrainClipmapController>();

            changed =
                true;
        }
        else
        {
            controller =
                controllers[0];

            // ---------------------------------------------
            // Remove duplicates
            // ---------------------------------------------

            for (
                int i = 1;
                i < controllers.Length;
                i++
            )
            {
                Object.DestroyImmediate(
                    controllers[i]
                );

                changed =
                    true;
            }
        }

        // -------------------------------------------------
        // Enable component
        // -------------------------------------------------

        if (!controller.enabled)
        {
            controller.enabled =
                true;

            changed =
                true;
        }

        // -------------------------------------------------
        // Configure
        // -------------------------------------------------

        changed |=
            controller.Configure(
                worldSettings
            );

        return changed;
    }


    // =====================================================
    // HEIGHTMAP STREAMER
    // =====================================================

    private static bool SynchronizeHeightmapStreamer(
        GameObject clipmapObject,
        WorldSettings worldSettings
    )
    {
        bool changed =
            false;

        TerrainHeightmapStreamer[] streamers =
            clipmapObject
                .GetComponents<TerrainHeightmapStreamer>();

        TerrainHeightmapStreamer streamer;

        // -------------------------------------------------
        // Create if missing
        // -------------------------------------------------

        if (streamers.Length == 0)
        {
            streamer =
                clipmapObject
                    .AddComponent<TerrainHeightmapStreamer>();

            changed =
                true;
        }
        else
        {
            streamer =
                streamers[0];

            // ---------------------------------------------
            // Remove duplicates
            // ---------------------------------------------

            for (
                int i = 1;
                i < streamers.Length;
                i++
            )
            {
                Object.DestroyImmediate(
                    streamers[i]
                );

                changed =
                    true;
            }
        }

        // -------------------------------------------------
        // Enable component
        // -------------------------------------------------

        if (!streamer.enabled)
        {
            streamer.enabled =
                true;

            changed =
                true;
        }

        // -------------------------------------------------
        // Heightmap manifest
        // -------------------------------------------------

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainHeightmapGenerator
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            Debug.LogWarning(
                "TerrainHeightmapStreamer could not be fully " +
                "configured because the generated heightmap " +
                "manifest does not exist.\n\n" +

                "Generate the heightmaps and run " +
                "Sync World Hierarchy again."
            );
        }

        // -------------------------------------------------
        // Configure runtime component
        // -------------------------------------------------

        changed |=
            streamer.Configure(
                worldSettings,
                manifest
            );

        return changed;
    }
    
    // =====================================================
    // HEIGHTMAP CACHE VALIDATOR
    // =====================================================

    private static bool SynchronizeHeightmapCacheValidator(
        GameObject clipmapObject
    )
    {
        bool changed =
            false;

        TerrainHeightmapCacheValidator[] validators =
            clipmapObject
                .GetComponents<TerrainHeightmapCacheValidator>();

        TerrainHeightmapCacheValidator validator;

        // -------------------------------------------------
        // Create if missing
        // -------------------------------------------------

        if (validators.Length == 0)
        {
            validator =
                clipmapObject
                    .AddComponent<TerrainHeightmapCacheValidator>();

            changed =
                true;
        }
        else
        {
            validator =
                validators[0];

            // ---------------------------------------------
            // Remove duplicates
            // ---------------------------------------------

            for (
                int i = 1;
                i < validators.Length;
                i++
            )
            {
                Object.DestroyImmediate(
                    validators[i]
                );

                changed =
                    true;
            }
        }

        // -------------------------------------------------
        // Enable component
        // -------------------------------------------------

        if (!validator.enabled)
        {
            validator.enabled =
                true;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // RENDERABLE MESH OBJECT
    // =====================================================

    private static bool SynchronizeRenderableMeshObject(
        GameObject gameObject,
        Mesh mesh,
        Material material
    )
    {
        bool changed =
            false;

        // -------------------------------------------------
        // MeshFilter
        // -------------------------------------------------

        MeshFilter[] filters =
            gameObject
                .GetComponents<MeshFilter>();

        MeshFilter meshFilter;

        if (filters.Length == 0)
        {
            meshFilter =
                gameObject
                    .AddComponent<MeshFilter>();

            changed =
                true;
        }
        else
        {
            meshFilter =
                filters[0];

            for (
                int i = 1;
                i < filters.Length;
                i++
            )
            {
                Object.DestroyImmediate(
                    filters[i]
                );

                changed =
                    true;
            }
        }

        if (
            meshFilter.sharedMesh !=
            mesh
        )
        {
            meshFilter.sharedMesh =
                mesh;

            changed =
                true;
        }

        // -------------------------------------------------
        // MeshRenderer
        // -------------------------------------------------

        MeshRenderer[] renderers =
            gameObject
                .GetComponents<MeshRenderer>();

        MeshRenderer meshRenderer;

        if (renderers.Length == 0)
        {
            meshRenderer =
                gameObject
                    .AddComponent<MeshRenderer>();

            changed =
                true;
        }
        else
        {
            meshRenderer =
                renderers[0];

            for (
                int i = 1;
                i < renderers.Length;
                i++
            )
            {
                Object.DestroyImmediate(
                    renderers[i]
                );

                changed =
                    true;
            }
        }

        if (
            meshRenderer.sharedMaterial !=
            material
        )
        {
            meshRenderer.sharedMaterial =
                material;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // VALIDATE PREVIEW CHUNK MESHES
    // =====================================================

    private static bool ValidateChunkMeshes(
        WorldSettings worldSettings,
        out Dictionary<Vector2Int, Mesh> chunkMeshes
    )
    {
        chunkMeshes =
            new Dictionary<Vector2Int, Mesh>();

        // -------------------------------------------------
        // Base mesh
        // -------------------------------------------------

        Mesh baseMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                BaseMeshPath
            );

        if (baseMesh == null)
        {
            Debug.LogError(
                "Cannot synchronize Preview hierarchy.\n\n" +

                "LOD0 base mesh does not exist."
            );

            return false;
        }

        // -------------------------------------------------
        // Base dimensions
        // -------------------------------------------------

        if (
            Mathf.Abs(
                baseMesh.bounds.size.x -
                worldSettings.chunkSize
            )
            >
            SizeTolerance
            ||
            Mathf.Abs(
                baseMesh.bounds.size.z -
                worldSettings.chunkSize
            )
            >
            SizeTolerance
        )
        {
            Debug.LogError(
                "Cannot synchronize Preview hierarchy.\n\n" +

                "The LOD0 base mesh does not match " +
                "WorldSettings.chunkSize."
            );

            return false;
        }

        // -------------------------------------------------
        // Base dependency state
        // -------------------------------------------------

        string currentBaseMeshHash =
            AssetDatabase
                .GetAssetDependencyHash(
                    BaseMeshPath
                )
                .ToString();

        if (
            worldSettings.lastSyncedBaseMeshHash
            !=
            currentBaseMeshHash
        )
        {
            Debug.LogError(
                "Cannot synchronize Preview hierarchy.\n\n" +

                "Generated LOD0 chunk meshes are not " +
                "synchronized with the current base mesh.\n\n" +

                "Run 'Sync Chunk Meshes' first."
            );

            return false;
        }

        // -------------------------------------------------
        // Generated meshes
        // -------------------------------------------------

        chunkMeshes =
            FindGeneratedChunkMeshes();

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

        int missingCount =
            0;

        int obsoleteCount =
            0;

        for (
            int z = 0;
            z < gridHeight;
            z++
        )
        {
            for (
                int x = 0;
                x < gridWidth;
                x++
            )
            {
                if (
                    !chunkMeshes.ContainsKey(
                        new Vector2Int(
                            x,
                            z
                        )
                    )
                )
                {
                    missingCount++;
                }
            }
        }

        foreach (
            Vector2Int coordinate
            in chunkMeshes.Keys
        )
        {
            if (
                coordinate.x < 0
                ||
                coordinate.y < 0
                ||
                coordinate.x >= gridWidth
                ||
                coordinate.y >= gridHeight
            )
            {
                obsoleteCount++;
            }
        }

        if (
            missingCount > 0
            ||
            obsoleteCount > 0
        )
        {
            Debug.LogError(
                "Cannot synchronize Preview hierarchy.\n\n" +

                "Generated chunk meshes do not match " +
                "the current world grid.\n\n" +

                $"Missing: {missingCount}\n" +
                $"Obsolete: {obsoleteCount}\n\n" +

                "Run 'Sync Chunk Meshes' first."
            );

            return false;
        }

        return true;
    }
    
    // =====================================================
    // VALIDATE CLIPMAP MESHES
    // =====================================================

    private static bool ValidateClipmapMeshes(
        WorldSettings worldSettings,
        out ClipmapMeshSet meshSet
    )
    {
        meshSet =
            new ClipmapMeshSet();

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        // -------------------------------------------------
        // Center
        // -------------------------------------------------

        string centerPath =
            TerrainClipmapMeshGenerator
                .GetCenterMeshPath();

        if (
            !TryLoadValidClipmapMesh(
                centerPath,
                "Clipmap Center LOD0",
                out meshSet.center
            )
        )
        {
            return false;
        }

        // -------------------------------------------------
        // Outer levels
        // -------------------------------------------------

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            string ringPath =
                TerrainClipmapMeshGenerator
                    .GetRingMeshPath(
                        level
                    );

            if (
                !TryLoadValidClipmapMesh(
                    ringPath,
                    $"Clipmap Ring LOD{level}",
                    out Mesh ringMesh
                )
            )
            {
                return false;
            }

            meshSet.rings[
                level
            ] =
                ringMesh;

            string stitchPath =
                TerrainClipmapMeshGenerator
                    .GetStitchMeshPath(
                        level - 1,
                        level
                    );

            if (
                !TryLoadValidClipmapMesh(
                    stitchPath,
                    $"Clipmap Stitch LOD{level - 1} -> LOD{level}",
                    out Mesh stitchMesh
                )
            )
            {
                return false;
            }

            /*
             * The coarse level number uniquely identifies
             * each stitch mesh.
             */
            meshSet.stitches[
                level
            ] =
                stitchMesh;
        }

        return true;
    }

    // =====================================================
    // LOAD / VALIDATE ONE CLIPMAP MESH
    // =====================================================

    private static bool TryLoadValidClipmapMesh(
        string assetPath,
        string description,
        out Mesh mesh
    )
    {
        mesh =
            AssetDatabase
                .LoadAssetAtPath<Mesh>(
                    assetPath
                );

        if (mesh == null)
        {
            Debug.LogError(
                "Cannot synchronize Clipmap hierarchy.\n\n" +

                $"{description} is missing.\n\n" +

                $"Asset:\n" +
                $"{assetPath}\n\n" +

                "Generate or regenerate the clipmap meshes first."
            );

            return false;
        }

        if (
            mesh.vertexCount <= 0
            ||
            mesh.subMeshCount <= 0
        )
        {
            Debug.LogError(
                "Cannot synchronize Clipmap hierarchy.\n\n" +

                $"{description} contains no usable geometry.\n\n" +

                $"Asset:\n" +
                $"{assetPath}"
            );

            return false;
        }

        long indexCount =
            0L;

        for (
            int subMeshIndex = 0;
            subMeshIndex < mesh.subMeshCount;
            subMeshIndex++
        )
        {
            indexCount +=
                mesh.GetIndexCount(
                    subMeshIndex
                );
        }

        if (indexCount <= 0)
        {
            Debug.LogError(
                "Cannot synchronize Clipmap hierarchy.\n\n" +

                $"{description} contains no triangle indices."
            );

            return false;
        }

        return true;
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
                WorldRootName
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
                $"Multiple '{WorldRootName}' objects exist " +
                "in the active scene.\n\n" +

                "Remove or rename duplicate WorldRoot objects " +
                "before synchronizing."
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

    private static Transform GetOrCreateUniqueDirectChild(
        Transform parent,
        string childName,
        out bool changed
    )
    {
        changed =
            false;

        Transform result =
            null;

        List<GameObject> duplicates =
            new List<GameObject>();

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

            if (result == null)
            {
                result =
                    child;
            }
            else
            {
                duplicates.Add(
                    child.gameObject
                );
            }
        }

        foreach (
            GameObject duplicate
            in duplicates
        )
        {
            Object.DestroyImmediate(
                duplicate
            );

            changed =
                true;
        }

        if (result == null)
        {
            GameObject childObject =
                new GameObject(
                    childName
                );

            result =
                childObject.transform;

            result.SetParent(
                parent,
                false
            );

            changed =
                true;
        }

        return result;
    }

    // =====================================================
    // SYNCHRONIZE TRANSFORM
    // =====================================================

    private static bool SynchronizeTransform(
        Transform transform,
        Vector3 localPosition,
        bool activeSelf
    )
    {
        bool changed =
            false;

        if (
            transform.localPosition !=
            localPosition
        )
        {
            transform.localPosition =
                localPosition;

            changed =
                true;
        }

        if (
            transform.localRotation !=
            Quaternion.identity
        )
        {
            transform.localRotation =
                Quaternion.identity;

            changed =
                true;
        }

        if (
            transform.localScale !=
            Vector3.one
        )
        {
            transform.localScale =
                Vector3.one;

            changed =
                true;
        }

        if (
            transform.gameObject.activeSelf !=
            activeSelf
        )
        {
            transform.gameObject.SetActive(
                activeSelf
            );

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // REMOVE LEGACY DIRECT CHUNKS
    // =====================================================

    private static int RemoveLegacyDirectChunkObjects(
        Transform worldRoot
    )
    {
        List<GameObject> legacyChunks =
            new List<GameObject>();

        foreach (
            Transform child
            in worldRoot
        )
        {
            if (
                TryGetChunkCoordinatesFromObjectName(
                    child.name,
                    out _,
                    out _
                )
            )
            {
                legacyChunks.Add(
                    child.gameObject
                );
            }
        }

        foreach (
            GameObject legacyChunk
            in legacyChunks
        )
        {
            Object.DestroyImmediate(
                legacyChunk
            );
        }

        return
            legacyChunks.Count;
    }

    // =====================================================
    // REMOVE NAMED DIRECT CHILDREN
    // =====================================================

    private static int RemoveDirectChildrenNamed(
        Transform parent,
        string childName
    )
    {
        List<GameObject> objects =
            new List<GameObject>();

        foreach (
            Transform child
            in parent
        )
        {
            if (
                child.name ==
                childName
            )
            {
                objects.Add(
                    child.gameObject
                );
            }
        }

        foreach (
            GameObject child
            in objects
        )
        {
            Object.DestroyImmediate(
                child
            );
        }

        return
            objects.Count;
    }

    // =====================================================
    // EXISTING CHUNK OBJECTS
    // =====================================================

    private static Dictionary<Vector2Int, GameObject>
        FindExistingChunkObjects(
            Transform parent,
            out List<GameObject> duplicates
        )
    {
        Dictionary<Vector2Int, GameObject> chunks =
            new Dictionary<Vector2Int, GameObject>();

        duplicates =
            new List<GameObject>();

        foreach (
            Transform child
            in parent
        )
        {
            if (
                !TryGetChunkCoordinatesFromObjectName(
                    child.name,
                    out int x,
                    out int z
                )
            )
            {
                continue;
            }

            Vector2Int coordinate =
                new Vector2Int(
                    x,
                    z
                );

            if (
                chunks.ContainsKey(
                    coordinate
                )
            )
            {
                duplicates.Add(
                    child.gameObject
                );

                continue;
            }

            chunks[
                coordinate
            ] =
                child.gameObject;
        }

        return chunks;
    }

    // =====================================================
    // GENERATED PREVIEW MESHES
    // =====================================================

    private static Dictionary<Vector2Int, Mesh>
        FindGeneratedChunkMeshes()
    {
        Dictionary<Vector2Int, Mesh> meshes =
            new Dictionary<Vector2Int, Mesh>();

        if (
            !AssetDatabase.IsValidFolder(
                ChunkMeshFolder
            )
        )
        {
            return meshes;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Mesh",
                new[]
                {
                    ChunkMeshFolder
                }
            );

        foreach (
            string guid
            in guids
        )
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            if (
                !TryGetChunkCoordinatesFromMeshPath(
                    path,
                    out int x,
                    out int z
                )
            )
            {
                continue;
            }

            Mesh mesh =
                AssetDatabase
                    .LoadAssetAtPath<Mesh>(
                        path
                    );

            if (mesh == null)
            {
                continue;
            }

            meshes[
                new Vector2Int(
                    x,
                    z
                )
            ] =
                mesh;
        }

        return meshes;
    }

    // =====================================================
    // CHUNK POSITION
    // =====================================================

    private static Vector3 GetChunkPosition(
        Vector2Int coordinate,
        float chunkSize
    )
    {
        return new Vector3(
            coordinate.x *
            chunkSize,

            0f,

            coordinate.y *
            chunkSize
        );
    }

    // =====================================================
    // CHUNK NAMES
    // =====================================================

    private static string GetChunkObjectName(
        int x,
        int z
    )
    {
        return
            $"Chunk_{x}_{z}";
    }

    // =====================================================
    // CLIPMAP NAMES
    // =====================================================

    private static string GetClipmapLODGroupName(
        int level
    )
    {
        return
            $"LOD{level}";
    }

    private static string GetClipmapRingObjectName(
        int level
    )
    {
        return
            $"Ring_LOD{level}";
    }

    private static string GetClipmapStitchObjectName(
        int fineLevel,
        int coarseLevel
    )
    {
        return
            $"Stitch_LOD" +
            $"{fineLevel}_LOD{coarseLevel}";
    }

    // =====================================================
    // PARSE LOD GROUP
    // =====================================================

    private static bool TryGetLODGroupLevel(
        string objectName,
        out int level
    )
    {
        level =
            0;

        if (
            string.IsNullOrEmpty(
                objectName
            )
            ||
            !objectName.StartsWith(
                "LOD"
            )
        )
        {
            return false;
        }

        string levelText =
            objectName.Substring(
                3
            );

        return int.TryParse(
            levelText,
            out level
        );
    }

    // =====================================================
    // PARSE CHUNK OBJECT NAME
    // =====================================================

    private static bool
        TryGetChunkCoordinatesFromObjectName(
            string objectName,
            out int x,
            out int z
        )
    {
        x =
            0;

        z =
            0;

        /*
         * Expected:
         *
         * Chunk_12_7
         */

        string[] parts =
            objectName.Split(
                '_'
            );

        if (parts.Length != 3)
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
    // PARSE CHUNK MESH NAME
    // =====================================================

    private static bool
        TryGetChunkCoordinatesFromMeshPath(
            string assetPath,
            out int x,
            out int z
        )
    {
        x =
            0;

        z =
            0;

        string fileName =
            Path.GetFileNameWithoutExtension(
                assetPath
            );

        /*
         * Expected:
         *
         * Chunk_12_7_LOD0
         */

        string[] parts =
            fileName.Split(
                '_'
            );

        if (parts.Length != 4)
        {
            return false;
        }

        if (
            parts[0] !=
            "Chunk"
            ||
            parts[3] !=
            "LOD0"
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
    // PROGRESS
    // =====================================================

    private static bool ShowProgress(
        string operation,
        string item,
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
                    "Synchronizing World Hierarchy",

                    $"{operation}\n" +
                    $"{item}",

                    progress
                );
    }

    // =====================================================
    // SCENE DIRTY
    // =====================================================

    private static void MarkSceneDirtyIfNeeded(
        Scene scene,
        bool changed
    )
    {
        if (!changed)
        {
            return;
        }

        EditorSceneManager.MarkSceneDirty(
            scene
        );
    }

    // =====================================================
    // CANCEL LOG
    // =====================================================

    private static void LogCancelled()
    {
        Debug.LogWarning(
            "World hierarchy synchronization cancelled.\n\n" +

            "Any completed hierarchy changes were preserved.\n\n" +

            "Run Sync World Hierarchy again to finish."
        );
    }

    // =====================================================
    // CLIPMAP MESH SET
    // =====================================================

    private sealed class ClipmapMeshSet
    {
        public Mesh center;

        public readonly Dictionary<int, Mesh> rings =
            new Dictionary<int, Mesh>();

        /*
         * Keyed by the COARSE level.
         *
         * Example:
         *
         * key 1 =
         * LOD0 -> LOD1 stitch
         *
         * key 2 =
         * LOD1 -> LOD2 stitch
         */
        public readonly Dictionary<int, Mesh> stitches =
            new Dictionary<int, Mesh>();
    }

    // =====================================================
    // SYNC STATISTICS
    // =====================================================

    private sealed class HierarchySyncStats
    {
        public int created;

        public int updated;

        public int removed;

        public int unchanged;
    }
    
    // =====================================================
    // CALCULATE PREVIEW TERRAIN HEIGHT RANGE
    // =====================================================

    private static bool TryCalculatePreviewHeightRange(
        Dictionary<Vector2Int, Mesh> previewMeshes,
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        long verticesChecked =
            0L;

        foreach (
            KeyValuePair<Vector2Int, Mesh> pair
            in previewMeshes
        )
        {
            Mesh mesh =
                pair.Value;

            if (
                mesh == null
                ||
                mesh.vertexCount <= 0
            )
            {
                Debug.LogError(
                    "Cannot calculate clipmap displacement bounds.\n\n" +

                    $"Preview mesh at chunk " +
                    $"({pair.Key.x}, {pair.Key.y}) " +
                    $"is missing or contains no vertices."
                );

                return false;
            }

            Vector3[] vertices;

            try
            {
                vertices =
                    mesh.vertices;
            }
            catch (
                System.Exception exception
            )
            {
                Debug.LogError(
                    "Cannot calculate clipmap displacement bounds.\n\n" +

                    $"Could not read Preview mesh vertices for " +
                    $"chunk ({pair.Key.x}, {pair.Key.y}).\n\n" +

                    exception.Message
                );

                return false;
            }

            for (
                int i = 0;
                i < vertices.Length;
                i++
            )
            {
                float height =
                    vertices[i].y;

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
                        "Cannot calculate clipmap displacement bounds.\n\n" +

                        $"Preview mesh chunk " +
                        $"({pair.Key.x}, {pair.Key.y}) " +
                        $"contains an invalid vertex height."
                    );

                    return false;
                }

                minimumHeight =
                    Mathf.Min(
                        minimumHeight,
                        height
                    );

                maximumHeight =
                    Mathf.Max(
                        maximumHeight,
                        height
                    );

                verticesChecked++;
            }
        }

        if (
            verticesChecked <= 0
            ||
            float.IsInfinity(
                minimumHeight
            )
            ||
            float.IsInfinity(
                maximumHeight
            )
        )
        {
            Debug.LogError(
                "Cannot calculate clipmap displacement bounds.\n\n" +

                "No valid Preview terrain vertices were found."
            );

            return false;
        }

        return true;
    }
    
    // =====================================================
    // CLIPMAP DISPLACEMENT VALIDATOR
    // =====================================================

    private static bool SynchronizeClipmapDisplacementValidator(
        GameObject clipmapObject
    )
    {
        bool changed =
            false;

        TerrainClipmapDisplacementValidator[] validators =
            clipmapObject
                .GetComponents<TerrainClipmapDisplacementValidator>();

        TerrainClipmapDisplacementValidator validator;

        // -------------------------------------------------
        // Create if missing
        // -------------------------------------------------

        if (validators.Length == 0)
        {
            validator =
                clipmapObject
                    .AddComponent<TerrainClipmapDisplacementValidator>();

            changed =
                true;
        }
        else
        {
            validator =
                validators[0];

            // ---------------------------------------------
            // Remove duplicates
            // ---------------------------------------------

            for (
                int i = 1;
                i < validators.Length;
                i++
            )
            {
                Object.DestroyImmediate(
                    validators[i]
                );

                changed =
                    true;
            }
        }

        // -------------------------------------------------
        // Enable
        // -------------------------------------------------

        if (!validator.enabled)
        {
            validator.enabled =
                true;

            changed =
                true;
        }

        return changed;
    }
    
    // =====================================================
    // CLIPMAP BOUNDS CONTROLLER
    // =====================================================

        private static bool SynchronizeClipmapBoundsController(
            GameObject clipmapObject,
            float minimumTerrainHeight,
            float maximumTerrainHeight
        )
        {
            bool changed =
                false;

            TerrainClipmapBoundsController[] controllers =
                clipmapObject
                    .GetComponents<TerrainClipmapBoundsController>();

            TerrainClipmapBoundsController controller;

            // -------------------------------------------------
            // Create if missing
            // -------------------------------------------------

            if (controllers.Length == 0)
            {
                controller =
                    clipmapObject
                        .AddComponent<TerrainClipmapBoundsController>();

                changed =
                    true;
            }
            else
            {
                controller =
                    controllers[0];

                // ---------------------------------------------
                // Remove duplicates
                // ---------------------------------------------

                for (
                    int i = 1;
                    i < controllers.Length;
                    i++
                )
                {
                    Object.DestroyImmediate(
                        controllers[i]
                    );

                    changed =
                        true;
                }
            }

            // -------------------------------------------------
            // Enable
            // -------------------------------------------------

            if (!controller.enabled)
            {
                controller.enabled =
                    true;

                changed =
                    true;
            }

            // -------------------------------------------------
            // Height range
            // -------------------------------------------------

            changed |=
                controller.Configure(
                    minimumTerrainHeight,
                    maximumTerrainHeight
                );

            return changed;
        }
}