using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * Lightweight synchronization of generated runtime metadata into an existing
 * WorldMeshes scene hierarchy.
 *
 * This class is intentionally NOT a hierarchy generator. It never creates,
 * destroys, reparents, renames, repositions, or repairs generated objects.
 * Structural problems are reported as RepairRequired so the explicit
 * Setup / Repair World Hierarchy path remains the sole structural authority.
 */
public static class TerrainRuntimeSceneSynchronizer
{
    public static TerrainRuntimeSceneSynchronizationResult
        SynchronizePending(
            WorldSettings worldSettings
        )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        if (!snapshot.RuntimeSceneMetadataDirty)
        {
            return
                CreateResult(
                    TerrainRuntimeSceneSynchronizationOutcome
                        .NoWork,
                    snapshot.StateRevision,
                    summaryMessage:
                        "No runtime scene metadata synchronization is pending."
                );
        }

        return
            SynchronizeInternal(
                worldSettings,
                snapshot,
                true
            );
    }

    public static TerrainRuntimeSceneSynchronizationResult
        SynchronizeExistingHierarchy(
            WorldSettings worldSettings
        )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        return
            SynchronizeInternal(
                worldSettings,
                snapshot,
                snapshot.RuntimeSceneMetadataDirty
            );
    }

    private static TerrainRuntimeSceneSynchronizationResult
        SynchronizeInternal(
            WorldSettings worldSettings,
            TerrainRuntimeBakeStateSnapshot startSnapshot,
            bool acknowledgePendingState
        )
    {
        List<string> warnings =
            new List<string>();

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            return
                CreateResult(
                    TerrainRuntimeSceneSynchronizationOutcome
                        .Blocked,
                    startSnapshot.StateRevision,
                    warnings,
                    errorMessage:
                        "Runtime scene metadata synchronization must be performed outside Play Mode."
                );
        }

        if (worldSettings == null)
        {
            return
                CreateResult(
                    TerrainRuntimeSceneSynchronizationOutcome
                        .Blocked,
                    startSnapshot.StateRevision,
                    warnings,
                    errorMessage:
                        "WorldSettings is unavailable."
                );
        }

        if (
            !TerrainWorldSceneUtility
                .TryGetActiveScene(
                    out Scene scene,
                    out string sceneError
                )
        )
        {
            return
                CreateResult(
                    TerrainRuntimeSceneSynchronizationOutcome
                        .Failed,
                    startSnapshot.StateRevision,
                    warnings,
                    errorMessage:
                        sceneError
                );
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
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    worldRootError
                );
        }

        if (worldRoot == null)
        {
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    "The generated WorldRoot does not exist. Run Setup / Repair World Hierarchy."
                );
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
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    clipmapError
                );
        }

        if (clipmapRoot == null)
        {
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    "The generated WorldRoot/Clipmap hierarchy does not exist. Run Setup / Repair World Hierarchy."
                );
        }

        TerrainClipmapBoundsController[]
            boundsControllers =
                clipmapRoot
                    .GetComponents<
                        TerrainClipmapBoundsController
                    >();

        if (boundsControllers.Length != 1)
        {
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    "The generated Clipmap root must contain exactly one TerrainClipmapBoundsController. Run Setup / Repair World Hierarchy."
                );
        }

        TerrainHeightmapStreamer[]
            heightStreamers =
                clipmapRoot
                    .GetComponents<
                        TerrainHeightmapStreamer
                    >();

        if (heightStreamers.Length != 1)
        {
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    "The generated Clipmap root must contain exactly one TerrainHeightmapStreamer. Run Setup / Repair World Hierarchy."
                );
        }

        if (
            !ValidateClipmapStructure(
                clipmapRoot,
                worldSettings,
                out string structureError
            )
        )
        {
            return
                RepairResult(
                    startSnapshot.StateRevision,
                    warnings,
                    structureError
                );
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase
                .LoadAssetAtPath<
                    TerrainHeightmapManifest
                >(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (
            !IsHeightManifestStructurallyUsable(
                heightManifest,
                worldSettings,
                out string heightManifestError
            )
        )
        {
            return
                CreateResult(
                    TerrainRuntimeSceneSynchronizationOutcome
                        .Blocked,
                    startSnapshot.StateRevision,
                    warnings,
                    errorMessage:
                        heightManifestError
                );
        }

        TerrainClipmapBoundsController boundsController =
            boundsControllers[0];

        TerrainHeightmapStreamer heightStreamer =
            heightStreamers[0];

        /*
         * Resolve every CURRENT downstream dataset and any scene components it
         * requires before mutating serialized scene state. Stale/missing
         * downstream generations are warnings and are deliberately left
         * untouched; inconsistent current data is a hard preflight failure.
         */
        TerrainGenerationStateUtility
            .GenerationStatus surfaceStatus =
                TerrainGenerationStateUtility
                    .GetSurfaceMaskStatus(
                        worldSettings
                    );

        TerrainSurfaceMaskManifest surfaceManifest =
            null;

        if (
            surfaceStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            surfaceManifest =
                AssetDatabase
                    .LoadAssetAtPath<
                        TerrainSurfaceMaskManifest
                    >(
                        TerrainRuntimeSurfaceMaskAssetUtility
                            .SurfaceMaskManifestPath
                    );

            if (surfaceManifest == null)
            {
                return
                    CreateResult(
                        TerrainRuntimeSceneSynchronizationOutcome
                            .Failed,
                        startSnapshot.StateRevision,
                        warnings,
                        true,
                        false,
                        heightManifest.minimumTerrainHeight,
                        heightManifest.maximumTerrainHeight,
                        errorMessage:
                            "Surface generation reports Current but the runtime surface-mask manifest is missing."
                    );
            }
        }
        else
        {
            warnings.Add(
                "Surface-mask generation is " +
                TerrainGenerationStateUtility
                    .GetStatusLabel(surfaceStatus) +
                "; the existing scene surface-manifest reference was left unchanged."
            );
        }

        TerrainGenerationStateUtility
            .GenerationStatus collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        TerrainCollisionManifest collisionManifest =
            AssetDatabase
                .LoadAssetAtPath<
                    TerrainCollisionManifest
                >(
                    WorldMeshesPaths
                        .CollisionManifestAssetPath
                );

        bool collisionRuntimeCurrent =
            collisionStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            IsCollisionManifestCurrent(
                collisionManifest,
                worldSettings
            );

        TerrainCollisionStreamer collisionStreamer =
            null;

        if (collisionRuntimeCurrent)
        {
            if (
                !TerrainWorldSceneUtility
                    .TryFindCollisionRoot(
                        scene,
                        out Transform collisionRoot,
                        out string collisionRootError
                    )
            )
            {
                return
                    RepairResult(
                        startSnapshot.StateRevision,
                        warnings,
                        collisionRootError
                    );
            }

            if (collisionRoot == null)
            {
                return
                    RepairResult(
                        startSnapshot.StateRevision,
                        warnings,
                        "Current runtime collision data exists but WorldRoot/Collision is missing. Run Setup / Repair World Hierarchy."
                    );
            }

            TerrainCollisionStreamer[]
                collisionStreamers =
                    collisionRoot
                        .GetComponents<
                            TerrainCollisionStreamer
                        >();

            if (collisionStreamers.Length != 1)
            {
                return
                    RepairResult(
                        startSnapshot.StateRevision,
                        warnings,
                        "Current runtime collision data exists but the Collision root does not contain exactly one TerrainCollisionStreamer. Run Setup / Repair World Hierarchy."
                    );
            }

            TerrainCollisionColliderPool[] collisionColliderPools =
                collisionRoot
                    .GetComponents<
                        TerrainCollisionColliderPool
                    >();

            if (collisionColliderPools.Length != 1)
            {
                return
                    RepairResult(
                        startSnapshot.StateRevision,
                        warnings,
                        "Current runtime collision data exists but the Collision root does not contain exactly one TerrainCollisionColliderPool. Run Setup / Repair World Hierarchy."
                    );
            }

            collisionStreamer =
                collisionStreamers[0];
        }
        else
        {
            warnings.Add(
                collisionManifest == null
                    ? "Runtime collision manifest is not currently prepared; the existing scene collision reference was left unchanged."
                    : "Runtime collision data is not currently compatible with the generated collision state; the existing scene collision reference was left unchanged."
            );
        }

        HashSet<UnityEngine.Object>
            serializedChangedComponents =
                new HashSet<UnityEngine.Object>();

        bool boundsSerializedChanged =
            false;

        bool heightStreamerSerializedChanged =
            false;

        bool surfaceManifestSynchronized =
            false;

        bool surfaceManifestSerializedChanged =
            false;

        bool collisionStreamerSynchronized =
            false;

        bool collisionStreamerSerializedChanged =
            false;

        bool sceneMarkedDirty =
            false;

        bool persistentSceneDirtyCleared =
            false;

        bool boundsApplied =
            false;

        try
        {
            boundsSerializedChanged =
                boundsController.Configure(
                    heightManifest.minimumTerrainHeight,
                    heightManifest.maximumTerrainHeight
                );

            if (boundsSerializedChanged)
            {
                EditorUtility.SetDirty(
                    boundsController
                );

                serializedChangedComponents.Add(
                    boundsController
                );
            }

            /*
             * Preview systems can temporarily override renderer localBounds
             * without changing this component's serialized range. Always
             * restore the configured runtime range, even when Configure()
             * returned false.
             */
            boundsApplied =
                boundsController
                    .RestoreConfiguredBounds();

            if (!boundsApplied)
            {
                return
                    CreateResult(
                        TerrainRuntimeSceneSynchronizationOutcome
                            .Failed,
                        startSnapshot.StateRevision,
                        warnings,
                        true,
                        false,
                        heightManifest.minimumTerrainHeight,
                        heightManifest.maximumTerrainHeight,
                        boundsSerializedChanged,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        serializedChangedComponents.Count,
                        false,
                        false,
                        errorMessage:
                            "The configured runtime height range could not be applied to the existing clipmap renderers."
                    );
            }

            heightStreamerSerializedChanged =
                heightStreamer.Configure(
                    worldSettings,
                    heightManifest
                );

            if (heightStreamerSerializedChanged)
            {
                EditorUtility.SetDirty(
                    heightStreamer
                );

                serializedChangedComponents.Add(
                    heightStreamer
                );
            }

            if (surfaceManifest != null)
            {
                surfaceManifestSerializedChanged =
                    heightStreamer
                        .ConfigureSurfaceMaskManifest(
                            surfaceManifest
                        );

                surfaceManifestSynchronized =
                    true;

                if (surfaceManifestSerializedChanged)
                {
                    EditorUtility.SetDirty(
                        heightStreamer
                    );

                    serializedChangedComponents.Add(
                        heightStreamer
                    );
                }
            }

            if (collisionRuntimeCurrent)
            {
                collisionStreamerSerializedChanged =
                    collisionStreamer.Configure(
                        worldSettings,
                        collisionManifest
                    );

                collisionStreamerSynchronized =
                    true;

                if (collisionStreamerSerializedChanged)
                {
                    EditorUtility.SetDirty(
                        collisionStreamer
                    );

                    serializedChangedComponents.Add(
                        collisionStreamer
                    );
                }
            }

            if (serializedChangedComponents.Count > 0)
            {
                EditorSceneManager
                    .MarkSceneDirty(
                        scene
                    );

                sceneMarkedDirty =
                    true;
            }

            bool heightGenerationCurrent =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    )
                ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current;

            if (!heightGenerationCurrent)
            {
                warnings.Add(
                    "Runtime height bounds were applied from the structurally complete physical height dataset, but height generation is still Out of Date. RuntimeSceneMetadataDirty remains pending."
                );
            }

            bool mayAcknowledge =
                acknowledgePendingState
                &&
                heightGenerationCurrent
                &&
                boundsApplied;

            if (mayAcknowledge)
            {
                TerrainRuntimeBakeStateSnapshot currentSnapshot =
                    TerrainRuntimeBakeStateService
                        .GetSnapshot();

                if (
                    currentSnapshot.StateRevision !=
                    startSnapshot.StateRevision
                )
                {
                    warnings.Add(
                        "Persistent runtime bake state changed during scene synchronization. RuntimeSceneMetadataDirty was left pending so newer work is not acknowledged accidentally."
                    );

                    mayAcknowledge =
                        false;
                }
            }

            if (mayAcknowledge)
            {
                persistentSceneDirtyCleared =
                    TerrainRuntimeBakeStateService
                        .ClearRuntimeSceneMetadataDirty();

                if (!persistentSceneDirtyCleared)
                {
                    /*
                     * If the flag was already false, there is nothing to
                     * acknowledge. This can happen only through an external
                     * state mutation between the revision check and this call.
                     * Keep the result conservative.
                     */
                    TerrainRuntimeBakeStateSnapshot afterClearAttempt =
                        TerrainRuntimeBakeStateService
                            .GetSnapshot();

                    if (afterClearAttempt.RuntimeSceneMetadataDirty)
                    {
                        warnings.Add(
                            "Runtime scene synchronization completed, but the persistent scene-dirty flag could not be acknowledged."
                        );
                    }
                }
            }

            TerrainRuntimeSceneSynchronizationOutcome outcome =
                warnings.Count > 0
                    ? TerrainRuntimeSceneSynchronizationOutcome
                        .CompletedWithWarnings
                    : TerrainRuntimeSceneSynchronizationOutcome
                        .Completed;

            return
                CreateResult(
                    outcome,
                    startSnapshot.StateRevision,
                    warnings,
                    true,
                    true,
                    heightManifest.minimumTerrainHeight,
                    heightManifest.maximumTerrainHeight,
                    boundsSerializedChanged,
                    true,
                    heightStreamerSerializedChanged,
                    surfaceManifestSynchronized,
                    surfaceManifestSerializedChanged,
                    collisionStreamerSynchronized,
                    collisionStreamerSerializedChanged,
                    serializedChangedComponents.Count,
                    sceneMarkedDirty,
                    persistentSceneDirtyCleared,
                    summaryMessage:
                        "Existing runtime scene metadata was synchronized without rebuilding the generated hierarchy."
                );
        }
        catch (Exception exception)
        {
            return
                CreateResult(
                    TerrainRuntimeSceneSynchronizationOutcome
                        .Failed,
                    startSnapshot.StateRevision,
                    warnings,
                    true,
                    boundsApplied,
                    heightManifest.minimumTerrainHeight,
                    heightManifest.maximumTerrainHeight,
                    boundsSerializedChanged,
                    true,
                    heightStreamerSerializedChanged,
                    surfaceManifestSynchronized,
                    surfaceManifestSerializedChanged,
                    collisionStreamerSynchronized,
                    collisionStreamerSerializedChanged,
                    serializedChangedComponents.Count,
                    sceneMarkedDirty,
                    false,
                    errorMessage:
                        "Unexpected runtime scene synchronization failure.\n\n" +
                        exception.Message
                );
        }
    }

    // =====================================================
    // HEIGHT MANIFEST STRUCTURAL USABILITY
    // =====================================================

    private static bool IsHeightManifestStructurallyUsable(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (manifest == null)
        {
            errorMessage =
                "The runtime heightmap manifest is missing. Complete runtime height generation before synchronizing scene metadata.";

            return false;
        }

        if (!manifest.isComplete)
        {
            errorMessage =
                "The runtime heightmap manifest is incomplete. Complete or rebuild runtime heightmaps before synchronizing scene metadata.";

            return false;
        }

        if (
            manifest.compilerVersion !=
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion
        )
        {
            errorMessage =
                "The runtime heightmap manifest was produced by an incompatible compiler version. Rebuild runtime heightmaps first.";

            return false;
        }

        if (
            !manifest.HasValidHeightRange
            ||
            !manifest.HasCompleteTileHeightRanges
        )
        {
            errorMessage =
                "The runtime heightmap manifest does not contain complete valid height-range metadata. Rebuild runtime heightmaps first.";

            return false;
        }

        if (
            manifest.gridWidth !=
                Mathf.Max(1, worldSettings.gridWidth)
            ||
            manifest.gridHeight !=
                Mathf.Max(1, worldSettings.gridHeight)
            ||
            !Mathf.Approximately(
                manifest.chunkSize,
                Mathf.Max(0.01f, worldSettings.chunkSize)
            )
            ||
            manifest.heightfieldResolutionPerChunk !=
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            ||
            manifest.heightTileChunkSpan !=
                Mathf.Max(
                    1,
                    worldSettings
                        .heightTileChunkSpan
                )
            ||
            manifest.heightTileGridWidth !=
                worldSettings.HeightTileGridWidth
            ||
            manifest.heightTileGridHeight !=
                worldSettings.HeightTileGridHeight
            ||
            !Mathf.Approximately(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            )
            ||
            manifest.heightTileSamplesPerSide !=
                worldSettings.HeightTileSamplesPerSide
        )
        {
            errorMessage =
                "The runtime heightmap manifest layout does not match current WorldSettings. Rebuild runtime heightmaps and repair the generated hierarchy as required.";

            return false;
        }

        return true;
    }

    // =====================================================
    // COLLISION MANIFEST CURRENTNESS
    // =====================================================

    private static bool IsCollisionManifestCurrent(
        TerrainCollisionManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            ||
            worldSettings == null
            ||
            !manifest.isComplete
            ||
            manifest.manifestVersion <= 0
            ||
            manifest.collisionGeneratorVersion !=
                TerrainGenerationStateUtility
                    .CollisionGeneratorVersion
            ||
            manifest.gridWidth !=
                Mathf.Max(1, worldSettings.gridWidth)
            ||
            manifest.gridHeight !=
                Mathf.Max(1, worldSettings.gridHeight)
            ||
            !Mathf.Approximately(
                manifest.chunkSize,
                Mathf.Max(0.01f, worldSettings.chunkSize)
            )
            ||
            manifest.heightfieldResolutionPerChunk !=
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            ||
            manifest.collisionResolution !=
                Mathf.Max(
                    1,
                    worldSettings.collisionResolution
                )
            ||
            manifest.collisionMeshGenerationRevision !=
                worldSettings
                    .collisionMeshGenerationRevision
            ||
            manifest.collisionSourceHeightmapGenerationRevision !=
                worldSettings
                    .collisionSourceHeightmapGenerationRevision
            ||
            manifest.regionChunkSpan <= 0
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // STRUCTURAL CLIPMAP PREFLIGHT
    // =====================================================

    private static bool ValidateClipmapStructure(
        Transform clipmapRoot,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        if (
            !TryFindUniqueDirectChild(
                clipmapRoot,
                "Center_LOD0",
                out Transform center,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (center == null)
        {
            errorMessage =
                "The generated Clipmap hierarchy is missing Center_LOD0. Run Setup / Repair World Hierarchy.";

            return false;
        }

        Mesh expectedCenterMesh =
            AssetDatabase
                .LoadAssetAtPath<Mesh>(
                    TerrainClipmapMeshGenerator
                        .GetCenterMeshPath()
                );

        if (
            !HasExpectedRenderableMesh(
                center,
                expectedCenterMesh
            )
        )
        {
            errorMessage =
                "Center_LOD0 does not reference the expected generated clipmap center mesh. Run Setup / Repair World Hierarchy.";

            return false;
        }

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            string levelName =
                "LOD" +
                level;

            if (
                !TryFindUniqueDirectChild(
                    clipmapRoot,
                    levelName,
                    out Transform levelRoot,
                    out errorMessage
                )
            )
            {
                return false;
            }

            if (levelRoot == null)
            {
                errorMessage =
                    "The generated Clipmap hierarchy is missing " +
                    levelName +
                    ". Run Setup / Repair World Hierarchy.";

                return false;
            }

            string ringName =
                "Ring_LOD" +
                level;

            if (
                !TryFindUniqueDirectChild(
                    levelRoot,
                    ringName,
                    out Transform ring,
                    out errorMessage
                )
            )
            {
                return false;
            }

            if (ring == null)
            {
                errorMessage =
                    "The generated " +
                    levelName +
                    " hierarchy is missing " +
                    ringName +
                    ". Run Setup / Repair World Hierarchy.";

                return false;
            }

            Mesh expectedRingMesh =
                AssetDatabase
                    .LoadAssetAtPath<Mesh>(
                        TerrainClipmapMeshGenerator
                            .GetRingMeshPath(
                                level
                            )
                    );

            if (
                !HasExpectedRenderableMesh(
                    ring,
                    expectedRingMesh
                )
            )
            {
                errorMessage =
                    ringName +
                    " does not reference the expected generated ring mesh. Run Setup / Repair World Hierarchy.";

                return false;
            }

            int fineLevel =
                level - 1;

            string stitchName =
                "Stitch_LOD" +
                fineLevel +
                "_LOD" +
                level;

            if (
                !TryFindUniqueDirectChild(
                    levelRoot,
                    stitchName,
                    out Transform stitch,
                    out errorMessage
                )
            )
            {
                return false;
            }

            if (stitch == null)
            {
                errorMessage =
                    "The generated " +
                    levelName +
                    " hierarchy is missing " +
                    stitchName +
                    ". Run Setup / Repair World Hierarchy.";

                return false;
            }

            Mesh expectedStitchMesh =
                AssetDatabase
                    .LoadAssetAtPath<Mesh>(
                        TerrainClipmapMeshGenerator
                            .GetStitchMeshPath(
                                fineLevel,
                                level
                            )
                    );

            if (
                !HasExpectedRenderableMesh(
                    stitch,
                    expectedStitchMesh
                )
            )
            {
                errorMessage =
                    stitchName +
                    " does not reference the expected generated stitch mesh. Run Setup / Repair World Hierarchy.";

                return false;
            }
        }

        foreach (
            Transform child
            in clipmapRoot
        )
        {
            if (
                child == null
                ||
                !child.name.StartsWith(
                    "LOD",
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (
                !int.TryParse(
                    child.name.Substring(3),
                    out int level
                )
                ||
                level < 1
                ||
                level >= levelCount
            )
            {
                errorMessage =
                    "The generated Clipmap hierarchy contains an unexpected LOD group ('" +
                    child.name +
                    "'). Run Setup / Repair World Hierarchy.";

                return false;
            }
        }

        return true;
    }

    private static bool HasExpectedRenderableMesh(
        Transform target,
        Mesh expectedMesh
    )
    {
        if (
            target == null
            ||
            expectedMesh == null
        )
        {
            return false;
        }

        MeshFilter filter =
            target
                .GetComponent<MeshFilter>();

        MeshRenderer renderer =
            target
                .GetComponent<MeshRenderer>();

        return
            filter != null
            &&
            renderer != null
            &&
            filter.sharedMesh ==
                expectedMesh;
    }

    private static bool TryFindUniqueDirectChild(
        Transform parent,
        string childName,
        out Transform child,
        out string errorMessage
    )
    {
        child =
            null;

        errorMessage =
            "";

        int matches =
            0;

        foreach (
            Transform candidate
            in parent
        )
        {
            if (
                candidate == null
                ||
                candidate.name !=
                    childName
            )
            {
                continue;
            }

            matches++;

            if (child == null)
            {
                child =
                    candidate;
            }
        }

        if (matches <= 1)
        {
            return true;
        }

        child =
            null;

        errorMessage =
            "Multiple generated children named '" +
            childName +
            "' exist under '" +
            parent.name +
            "'. Run Setup / Repair World Hierarchy.";

        return false;
    }

    // =====================================================
    // RESULT HELPERS
    // =====================================================

    private static TerrainRuntimeSceneSynchronizationResult
        RepairResult(
            long sourceStateRevision,
            IEnumerable<string> warnings,
            string errorMessage
        )
    {
        return
            CreateResult(
                TerrainRuntimeSceneSynchronizationOutcome
                    .RepairRequired,
                sourceStateRevision,
                warnings,
                errorMessage:
                    string.IsNullOrEmpty(errorMessage)
                        ? "The generated runtime hierarchy requires Setup / Repair World Hierarchy."
                        : errorMessage
            );
    }

    private static TerrainRuntimeSceneSynchronizationResult
        CreateResult(
            TerrainRuntimeSceneSynchronizationOutcome outcome,
            long sourceStateRevision,
            IEnumerable<string> warnings = null,
            bool boundsAvailable = false,
            bool boundsApplied = false,
            float minimumTerrainHeight = 0f,
            float maximumTerrainHeight = 0f,
            bool boundsControllerSerializedChanged = false,
            bool heightStreamerSynchronized = false,
            bool heightStreamerSerializedChanged = false,
            bool surfaceManifestSynchronized = false,
            bool surfaceManifestSerializedChanged = false,
            bool collisionStreamerSynchronized = false,
            bool collisionStreamerSerializedChanged = false,
            int serializedComponentChangeCount = 0,
            bool sceneMarkedDirty = false,
            bool persistentSceneDirtyCleared = false,
            string errorMessage = "",
            string summaryMessage = ""
        )
    {
        return
            new TerrainRuntimeSceneSynchronizationResult(
                outcome,
                sourceStateRevision,
                boundsAvailable,
                boundsApplied,
                minimumTerrainHeight,
                maximumTerrainHeight,
                boundsControllerSerializedChanged,
                heightStreamerSynchronized,
                heightStreamerSerializedChanged,
                surfaceManifestSynchronized,
                surfaceManifestSerializedChanged,
                collisionStreamerSynchronized,
                collisionStreamerSerializedChanged,
                serializedComponentChangeCount,
                sceneMarkedDirty,
                persistentSceneDirtyCleared,
                warnings,
                errorMessage,
                summaryMessage
            );
    }
}
