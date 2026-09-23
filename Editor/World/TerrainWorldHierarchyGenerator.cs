using System.Collections.Generic;
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
        TerrainWorldSceneUtility.WorldRootName;

    public const string CollisionRootName =
        "Collision";

    public const string ClipmapRootName =
        TerrainWorldSceneUtility.ClipmapRootName;


    // =====================================================
    // GENERATED CHILD NAMES
    // =====================================================

    private const string ClipmapCenterName =
        "Center_LOD0";

    // =====================================================
    // PATHS
    // =====================================================

    private const string ClipmapTerrainMaterialPath =
        WorldMeshesPaths.ClipmapTerrainMaterialPath;

    // =====================================================
    // SYNC WORLD HIERARCHY
    // =====================================================

    public static void SyncWorldHierarchy(
        WorldSettings worldSettings
    )
    {
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

        // =================================================
        // CLIPMAP PREFLIGHT
        // =================================================

        if (
            !ValidateClipmapMeshes(
                worldSettings,
                out ClipmapMeshSet clipmapMeshes
            )
        )
        {
            return;
        }

        Material clipmapTerrainMaterial =
            AssetDatabase
                .LoadAssetAtPath<Material>(
                    ClipmapTerrainMaterialPath
                );

        if (clipmapTerrainMaterial == null)
        {
            Debug.LogError(
                "Cannot synchronize world hierarchy.\n\n" +
                "Clipmap terrain material could not be found:\n" +
                ClipmapTerrainMaterialPath
            );

            return;
        }

        // =================================================
        // HEIGHT RANGE
        // =================================================

        ResolveTerrainHeightRange(
            worldSettings,
            out float minimumTerrainHeight,
            out float maximumTerrainHeight,
            out string heightRangeSource
        );

        // =================================================
        // SCENE
        // =================================================

        if (
            !TerrainWorldSceneUtility
                .TryGetActiveScene(
                    out Scene scene,
                    out string sceneError
                )
        )
        {
            Debug.LogError(
                sceneError
            );

            return;
        }

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

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        float worldSizeX =
            worldSizeXZ.x;

        float worldSizeZ =
            worldSizeXZ.y;

        Vector3 clipmapCenterPosition =
            TerrainClipmapLayoutUtility
                .CalculateWorldCenterPosition(
                    worldSettings,
                    0f
                );

        // =================================================
        // WORLD ROOT
        // =================================================

        GameObject worldRoot =
            GetOrCreateUniqueWorldRoot(
                scene,
                out bool worldRootChanged
            );

        bool hierarchyChanged =
            worldRootChanged;

        hierarchyChanged |=
            SynchronizeTransform(
                worldRoot.transform,
                Vector3.zero,
                true
            );

        // =================================================
        // WORLD RUNTIME
        // =================================================

        TerrainWorldRuntime worldRuntime;

        hierarchyChanged |=
            SynchronizeWorldRuntime(
                worldRoot,
                out worldRuntime
            );

        // =================================================
        // COLLISION ROOT
        // =================================================

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

        // =================================================
        // CLIPMAP ROOT
        // =================================================

        Transform clipmapRoot =
            GetOrCreateUniqueDirectChild(
                worldRoot.transform,
                ClipmapRootName,
                out bool clipmapRootChanged
            );

        hierarchyChanged |=
            clipmapRootChanged;

        /*
         * Before Play Mode movement begins, keep the generated
         * clipmap centered over the complete world. The edit-mode
         * authoring preview uses this same hierarchy.
         */
        hierarchyChanged |=
            SynchronizeTransform(
                clipmapRoot,
                clipmapCenterPosition,
                true
            );

        // =================================================
        // CLIPMAP
        // =================================================

        hierarchyChanged |=
            SynchronizeClipmapHierarchy(
                clipmapRoot,
                worldSettings,
                clipmapMeshes,
                clipmapTerrainMaterial,
                minimumTerrainHeight,
                maximumTerrainHeight
            );

        // =================================================
        // COLLISION
        // =================================================

        hierarchyChanged |=
            SynchronizeCollisionStreamer(
                collisionRoot.gameObject,
                worldSettings
            );

        hierarchyChanged |=
            SynchronizeCollisionColliderPool(
                collisionRoot.gameObject,
                worldSettings
            );

        // =================================================
        // STREAMING SOURCE
        // =================================================

        hierarchyChanged |=
            SynchronizeStreamingSource(
                worldRuntime,
                clipmapRoot.gameObject,
                collisionRoot.gameObject
            );

        // =================================================
        // SAVE / SELECT
        // =================================================

        MarkSceneDirtyIfNeeded(
            scene,
            hierarchyChanged
        );

        Selection.activeGameObject =
            clipmapRoot.gameObject;

        /*
         * Mesh renderers may have been created, removed, or
         * replaced during hierarchy synchronization.
         */
        TerrainAuthoringPreviewService
            .NotifyClipmapHierarchyChanged();

        /*
         * Sync restores the generated clipmap to canonical
         * world-centered transforms. If transient Scene View
         * following is enabled, request a delayed reapplication
         * after all generated hierarchy changes are complete.
         *
         * This does not rebuild or reload the editor height cache.
         */
        TerrainAuthoringSceneViewController
            .RequestReapply();

        /*
         * Generated terrain renderers may have been created or
         * replaced during synchronization. Rebind transient
         * authoring visualization metadata afterward.
         */
        TerrainAuthoringVisualizationController
            .RequestReapply();

        /*
         * Generated clipmap renderers or mesh references may have
         * changed. Re-discover transient true-wireframe sources after
         * hierarchy synchronization completes.
         */
        TerrainAuthoringWireframeRenderer
            .RequestReapply();

        Debug.Log(
            "World hierarchy setup / repair complete.\n\n" +
            $"World Grid: {gridWidth} x {gridHeight}\n" +
            $"Chunk Size: {chunkSize}\n" +
            $"World Size: {worldSizeX} x {worldSizeZ}\n\n" +
            $"Clipmap Center: " +
            $"({clipmapCenterPosition.x}, " +
            $"{clipmapCenterPosition.y}, " +
            $"{clipmapCenterPosition.z})\n\n" +
            $"Terrain Height Range: " +
            $"{minimumTerrainHeight:R} -> " +
            $"{maximumTerrainHeight:R}\n" +
            $"Height Range Source: {heightRangeSource}\n\n" +
            $"Clipmap Levels: " +
            $"{worldSettings.clipmapLevelCount}\n\n" +
            "Hierarchy:\n" +
            $"{WorldRootName}\n" +
            $"├── {CollisionRootName} " +
            "[TerrainCollisionStreamer, TerrainCollisionColliderPool]\n" +
            $"└── {ClipmapRootName}"
        );
    }

    // =====================================================
    // APPLY STREAMING SOURCE
    // =====================================================

    public static void ApplyStreamingSource()
    {
        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Runtime streaming-source synchronization must be " +
                "performed outside Play Mode."
            );

            return;
        }

        if (
            !TerrainWorldSceneUtility
                .TryGetActiveScene(
                    out Scene scene,
                    out string sceneError
                )
        )
        {
            Debug.LogError(
                sceneError
            );

            return;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindWorldRoot(
                    scene,
                    out Transform worldRoot,
                    out string worldRootError
                )
        )
        {
            Debug.LogError(
                worldRootError
            );

            return;
        }

        if (worldRoot == null)
        {
            Debug.LogWarning(
                "Cannot apply the runtime streaming source because " +
                "WorldRoot does not exist.\n\n" +
                "Run Setup / Repair World Hierarchy first."
            );

            return;
        }

        TerrainWorldRuntime worldRuntime =
            worldRoot
                .GetComponent<TerrainWorldRuntime>();

        if (worldRuntime == null)
        {
            Debug.LogWarning(
                "Cannot apply the runtime streaming source because " +
                "TerrainWorldRuntime is missing from WorldRoot.\n\n" +
                "Run Setup / Repair World Hierarchy first."
            );

            return;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindClipmapRoot(
                    scene,
                    out Transform clipmapRoot,
                    out string clipmapError
                )
        )
        {
            Debug.LogError(
                clipmapError
            );

            return;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindCollisionRoot(
                    scene,
                    out Transform collisionRoot,
                    out string collisionError
                )
        )
        {
            Debug.LogError(
                collisionError
            );

            return;
        }

        if (
            clipmapRoot == null
            ||
            collisionRoot == null
        )
        {
            Debug.LogWarning(
                "Cannot apply the runtime streaming source because " +
                "the generated Clipmap or Collision root is " +
                "missing.\n\n" +
                "Run Setup / Repair World Hierarchy first."
            );

            return;
        }

        TerrainClipmapController clipmapController =
            clipmapRoot
                .GetComponent<TerrainClipmapController>();

        TerrainCollisionStreamer collisionStreamer =
            collisionRoot
                .GetComponent<TerrainCollisionStreamer>();

        if (
            clipmapController == null
            ||
            collisionStreamer == null
        )
        {
            Debug.LogWarning(
                "Cannot apply the runtime streaming source because " +
                "the generated Clipmap or Collision runtime " +
                "component is missing.\n\n" +
                "Run Setup / Repair World Hierarchy first."
            );

            return;
        }

        bool changed =
            SynchronizeStreamingSource(
                worldRuntime,
                clipmapRoot.gameObject,
                collisionRoot.gameObject
            );

        MarkSceneDirtyIfNeeded(
            scene,
            changed
        );

        string sourceName =
            worldRuntime.StreamingSource != null
                ? worldRuntime.StreamingSource.name
                : "None";

        Debug.Log(
            "Runtime streaming source applied.\n\n" +
            $"Streaming Source: {sourceName}"
        );
    }

    // =====================================================
    // WORLD RUNTIME
    // =====================================================

    private static bool SynchronizeWorldRuntime(
        GameObject worldRoot,
        out TerrainWorldRuntime worldRuntime
    )
    {
        bool changed =
            false;

        TerrainWorldRuntime[] runtimes =
            worldRoot
                .GetComponents<TerrainWorldRuntime>();

        if (runtimes.Length == 0)
        {
            worldRuntime =
                worldRoot
                    .AddComponent<TerrainWorldRuntime>();

            changed =
                true;
        }
        else
        {
            worldRuntime =
                runtimes[0];

            for (
                int index = 1;
                index < runtimes.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    runtimes[index]
                );

                changed =
                    true;
            }
        }

        if (!worldRuntime.enabled)
        {
            worldRuntime.enabled =
                true;

            changed =
                true;
        }

        return
            changed;
    }

    // =====================================================
    // STREAMING SOURCE
    // =====================================================

    private static bool SynchronizeStreamingSource(
        TerrainWorldRuntime worldRuntime,
        GameObject clipmapObject,
        GameObject collisionObject
    )
    {
        bool changed =
            false;

        Transform streamingSource =
            worldRuntime != null
                ? worldRuntime.StreamingSource
                : null;

        TerrainClipmapController clipmapController =
            clipmapObject != null
                ? clipmapObject
                    .GetComponent<TerrainClipmapController>()
                : null;

        if (
            clipmapController != null
            &&
            clipmapController.Target !=
                streamingSource
        )
        {
            clipmapController.Target =
                streamingSource;

            changed =
                true;
        }

        TerrainCollisionStreamer collisionStreamer =
            collisionObject != null
                ? collisionObject
                    .GetComponent<TerrainCollisionStreamer>()
                : null;

        if (collisionStreamer != null)
        {
            changed |=
                collisionStreamer.SetStreamingTarget(
                    streamingSource
                );
        }

        return
            changed;
    }

    // =====================================================
    // HEIGHT RANGE
    // =====================================================

    private static void ResolveTerrainHeightRange(
        WorldSettings worldSettings,
        out float minimumHeight,
        out float maximumHeight,
        out string source
    )
    {
        minimumHeight =
            0f;

        maximumHeight =
            0f;

        source =
            "Fallback 0 -> 0";

        TerrainGenerationStateEvaluationContext generationState =
            new TerrainGenerationStateEvaluationContext(
                worldSettings,
                TerrainGenerationStateEvaluationMode
                    .Operational
            );

        TerrainHeightmapManifest runtimeManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (
            runtimeManifest != null
            &&
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    generationState
                )
                ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            minimumHeight =
                runtimeManifest.minimumTerrainHeight;

            maximumHeight =
                runtimeManifest.maximumTerrainHeight;

            source =
                "Compiled Runtime Heightmap Manifest";

            return;
        }

        TerrainAuthoringHeightManifest authoringManifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        if (
            authoringManifest != null
            &&
            TerrainGenerationStateUtility
                .GetAuthoringHeightfieldStatus(
                    generationState
                )
                ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            minimumHeight =
                authoringManifest.minimumCommittedHeight;

            maximumHeight =
                authoringManifest.maximumCommittedHeight;

            source =
                "Committed Authoring Height Manifest";

            return;
        }

        Debug.LogWarning(
            "No current terrain height-range metadata is " +
            "available for clipmap bounds.\n\n" +
            "The hierarchy will use a temporary 0 -> 0 range.\n\n" +
            "Initialize the authoring heightfield or compile " +
            "current runtime heightmaps to restore authoritative " +
            "bounds."
        );
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
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        changed |=
            SynchronizeClipmapController(
                clipmapRoot.gameObject,
                worldSettings
            );

        changed |=
            SynchronizeHeightmapStreamer(
                clipmapRoot.gameObject,
                worldSettings
            );

        changed |=
            SynchronizeClipmapWorldBoundsController(
                clipmapRoot.gameObject,
                worldSettings
            );

        changed |=
            SynchronizeHeightmapCacheValidator(
                clipmapRoot.gameObject
            );

        changed |=
            SynchronizeClipmapBoundsController(
                clipmapRoot.gameObject,
                minimumTerrainHeight,
                maximumTerrainHeight
            );

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
                &&
                (
                    level < 1
                    ||
                    level >= levelCount
                )
            )
            {
                obsoleteObjects.Add(
                    child.gameObject
                );
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

        // -------------------------------------------------
        // Center LOD0
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Outer LODs
        // -------------------------------------------------

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
                    meshes.rings[level],
                    clipmapTerrainMaterial
                );

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
                    meshes.stitches[level],
                    clipmapTerrainMaterial
                );
        }

        TerrainClipmapWorldBoundsController
            worldBoundsController =
                clipmapRoot
                    .GetComponent<TerrainClipmapWorldBoundsController>();

        if (worldBoundsController != null)
        {
            /*
             * Generated renderers may have been created/replaced
             * after the component itself was configured.
             */
            worldBoundsController
                .InvalidateBinding();

            worldBoundsController
                .ApplyWorldBounds();
        }

        TerrainClipmapBoundsController boundsController =
            clipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController != null)
        {
            boundsController.ApplyBounds();
        }

        return
            changed;
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

            for (
                int index = 1;
                index < controllers.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    controllers[index]
                );

                changed =
                    true;
            }
        }

        if (!controller.enabled)
        {
            controller.enabled =
                true;

            changed =
                true;
        }

        changed |=
            controller.Configure(
                worldSettings
            );

        return
            changed;
    }

    // =====================================================
    // CLIPMAP WORLD BOUNDS CONTROLLER
    // =====================================================

    private static bool SynchronizeClipmapWorldBoundsController(
        GameObject clipmapObject,
        WorldSettings worldSettings
    )
    {
        bool changed =
            false;

        TerrainClipmapWorldBoundsController[] controllers =
            clipmapObject
                .GetComponents<TerrainClipmapWorldBoundsController>();

        TerrainClipmapWorldBoundsController controller;

        if (controllers.Length == 0)
        {
            controller =
                clipmapObject
                    .AddComponent<TerrainClipmapWorldBoundsController>();

            changed =
                true;
        }
        else
        {
            controller =
                controllers[0];

            for (
                int index = 1;
                index < controllers.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    controllers[index]
                );

                changed =
                    true;
            }
        }

        if (!controller.enabled)
        {
            controller.enabled =
                true;

            changed =
                true;
        }

        changed |=
            controller.Configure(
                worldSettings
            );

        return
            changed;
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

            for (
                int index = 1;
                index < streamers.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    streamers[index]
                );

                changed =
                    true;
            }
        }

        if (!streamer.enabled)
        {
            streamer.enabled =
                true;

            changed =
                true;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            Debug.LogWarning(
                "TerrainHeightmapStreamer could not be fully " +
                "configured because the runtime heightmap manifest " +
                "does not exist.\n\n" +
                "Open Runtime and run Bake Runtime Changes."
            );
        }
        else if (!manifest.isComplete)
        {
            Debug.LogWarning(
                "TerrainHeightmapStreamer could not be fully " +
                "configured because the runtime heightmap " +
                "manifest is incomplete.\n\n" +
                "Compile Runtime Heightmaps again."
            );
        }

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskManifestPath
                );

        if (surfaceManifest == null)
        {
            Debug.LogWarning(
                "TerrainHeightmapStreamer could not be fully configured because the baked runtime surface-mask manifest does not exist.\n\n" +
                "Bake Runtime Surface Masks and then run Setup / Repair World Hierarchy again."
            );
        }
        else if (!surfaceManifest.isComplete)
        {
            Debug.LogWarning(
                "TerrainHeightmapStreamer could not be fully configured because the runtime surface-mask manifest is incomplete.\n\n" +
                "Bake Runtime Surface Masks again."
            );
        }

        changed |=
            streamer.Configure(
                worldSettings,
                manifest
            );

        changed |=
            streamer.ConfigureSurfaceMaskManifest(
                surfaceManifest
            );

        return
            changed;
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

            for (
                int index = 1;
                index < validators.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    validators[index]
                );

                changed =
                    true;
            }
        }

        if (!validator.enabled)
        {
            validator.enabled =
                true;

            changed =
                true;
        }

        return
            changed;
    }

    // =====================================================
    // CLIPMAP BOUNDS
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

            for (
                int index = 1;
                index < controllers.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    controllers[index]
                );

                changed =
                    true;
            }
        }

        if (!controller.enabled)
        {
            controller.enabled =
                true;

            changed =
                true;
        }

        changed |=
            controller.Configure(
                minimumTerrainHeight,
                maximumTerrainHeight
            );

        return
            changed;
    }

    // =====================================================
    // DISPLACEMENT VALIDATOR
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

            for (
                int index = 1;
                index < validators.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    validators[index]
                );

                changed =
                    true;
            }
        }

        if (!validator.enabled)
        {
            validator.enabled =
                true;

            changed =
                true;
        }

        return
            changed;
    }

    // =====================================================
    // COLLISION STREAMER
    // =====================================================

    private static bool SynchronizeCollisionStreamer(
        GameObject collisionObject,
        WorldSettings worldSettings
    )
    {
        bool changed =
            false;

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

            for (
                int index = 1;
                index < streamers.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    streamers[index]
                );

                changed =
                    true;
            }
        }

        if (!streamer.enabled)
        {
            streamer.enabled =
                true;

            changed =
                true;
        }

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
                "Run Prepare Collision Meshes For Runtime and " +
                "then Sync World Hierarchy again."
            );
        }
        else if (!manifest.isComplete)
        {
            Debug.LogWarning(
                "TerrainCollisionStreamer could not be fully " +
                "configured because CollisionManifest is " +
                "marked incomplete."
            );
        }

        changed |=
            streamer.Configure(
                worldSettings,
                manifest
            );

        return
            changed;
    }

    // =====================================================
    // COLLISION COLLIDER POOL
    // =====================================================

    private static bool SynchronizeCollisionColliderPool(
        GameObject collisionObject,
        WorldSettings worldSettings
    )
    {
        bool changed =
            false;

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
                int index = 1;
                index < pools.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    pools[index]
                );

                changed =
                    true;
            }
        }

        if (!pool.enabled)
        {
            pool.enabled =
                true;

            changed =
                true;
        }

        changed |=
            pool.Configure(
                worldSettings,
                streamer
            );

        changed |=
            SynchronizeCollisionColliderSlots(
                collisionObject.transform,
                pool.RequiredSlotCount
            );

        return
            changed;
    }

    // =====================================================
    // COLLISION SLOTS
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

        return
            changed;
    }

    private static bool SynchronizeCollisionColliderSlot(
        GameObject slotObject
    )
    {
        bool changed =
            false;

        if (
            slotObject.transform.parent != null
            &&
            slotObject.layer !=
                slotObject.transform.parent
                    .gameObject.layer
        )
        {
            slotObject.layer =
                slotObject.transform.parent
                    .gameObject.layer;

            changed =
                true;
        }

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
                int index = 1;
                index < colliders.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    colliders[index]
                );

                changed =
                    true;
            }
        }

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

        return
            changed;
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
                int index = 1;
                index < filters.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    filters[index]
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
                int index = 1;
                index < renderers.Length;
                index++
            )
            {
                Object.DestroyImmediate(
                    renderers[index]
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

        return
            changed;
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
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

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

            meshSet.rings[level] =
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

            meshSet.stitches[level] =
                stitchMesh;
        }

        return true;
    }

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
                $"Asset:\n{assetPath}\n\n" +
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
                $"{description} contains no usable geometry."
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
                (long)
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
    // WORLD ROOT / CHILD HELPERS
    // =====================================================

    private static GameObject GetOrCreateUniqueWorldRoot(
        Scene scene,
        out bool changed
    )
    {
        changed =
            false;

        GameObject result =
            null;

        int bestGeneratedRootScore =
            -1;

        List<GameObject> duplicates =
            new List<GameObject>();

        foreach (
            GameObject root
            in scene.GetRootGameObjects()
        )
        {
            if (
                root == null
                ||
                root.name !=
                    WorldRootName
            )
            {
                continue;
            }

            int generatedRootScore =
                0;

            foreach (
                Transform child
                in root.transform
            )
            {
                if (
                    child != null
                    &&
                    (
                        child.name == ClipmapRootName
                        ||
                        child.name == CollisionRootName
                    )
                )
                {
                    generatedRootScore++;
                }
            }

            if (
                result == null
                ||
                generatedRootScore > bestGeneratedRootScore
            )
            {
                if (result != null)
                {
                    duplicates.Add(
                        result
                    );
                }

                result =
                    root;

                bestGeneratedRootScore =
                    generatedRootScore;
            }
            else
            {
                duplicates.Add(
                    root
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
            result =
                new GameObject(
                    WorldRootName
                );

            changed =
                true;
        }

        return
            result;
    }

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

        return
            result;
    }

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

        return
            changed;
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

        return
            int.TryParse(
                levelText,
                out level
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
    // CLIPMAP MESH SET
    // =====================================================

    private sealed class ClipmapMeshSet
    {
        public Mesh center;

        public readonly Dictionary<int, Mesh>
            rings =
                new Dictionary<int, Mesh>();

        /*
         * Keyed by coarse level:
         * 1 = LOD0 -> LOD1
         * 2 = LOD1 -> LOD2
         * ...
         */
        public readonly Dictionary<int, Mesh>
            stitches =
                new Dictionary<int, Mesh>();
    }
}
