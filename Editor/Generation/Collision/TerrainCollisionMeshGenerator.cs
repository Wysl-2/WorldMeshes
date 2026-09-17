using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainCollisionMeshGenerator
{
    public const string CollisionMeshFolder =
        WorldMeshesPaths.GeneratedCollisionMeshes;

    private enum TerrainCollisionMeshWriteOutcome
    {
        Failed,
        Created,
        Updated
    }

    private sealed class CollisionGenerationTarget
    {
        public int heightGenerationRevision;
        public string heightSignature;
        public string collisionSettingsSignature;

        public int gridWidth;
        public int gridHeight;
        public float chunkSize;
        public int heightfieldResolutionPerChunk;
        public int heightTileChunkSpan;
        public int collisionResolution;
    }

    private sealed class PreparedCollisionMesh
    {
        public readonly Vector2Int coordinate;
        public readonly Mesh mesh;
        public readonly TerrainCollisionMeshWriteOutcome outcome;

        public PreparedCollisionMesh(
            Vector2Int coordinate,
            Mesh mesh,
            TerrainCollisionMeshWriteOutcome outcome
        )
        {
            this.coordinate = coordinate;
            this.mesh = mesh;
            this.outcome = outcome;
        }
    }

    private struct PersistentDirtyTransition
    {
        public bool configurationBecameDirty;
        public bool contentBecameDirty;
        public long stateRevisionAfter;
    }

    // =====================================================
    // EXPLICIT FULL REBUILD
    // =====================================================

    public static TerrainCollisionGenerationResult
        RebuildAllCollisionMeshes(
            WorldSettings worldSettings
        )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        List<Vector2Int> chunks =
            CollectAllCollisionChunks(
                worldSettings
            );

        return
            GenerateCollisionWork(
                worldSettings,
                TerrainRuntimeBakeWorkMode.Full,
                chunks,
                snapshot,
                null
            );
    }

    // =====================================================
    // PLANNED API
    // =====================================================

    public static TerrainCollisionGenerationResult
        GeneratePlannedCollisionMeshes(
            WorldSettings worldSettings,
            TerrainRuntimeBakePlan plan
        )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        if (plan == null)
        {
            return
                CreateSimpleResult(
                    TerrainCollisionGenerationOutcome.Failed,
                    TerrainRuntimeBakeWorkMode.None,
                    worldSettings,
                    "Collision bake plan is null."
                );
        }

        if (plan.IsBlocked)
        {
            return
                CreateSimpleResult(
                    TerrainCollisionGenerationOutcome.Blocked,
                    plan.CollisionWorkMode,
                    worldSettings,
                    string.IsNullOrEmpty(plan.BlockReason)
                        ? "The runtime bake plan is blocked."
                        : plan.BlockReason
                );
        }

        if (
            snapshot.StateRevision !=
            plan.SourceStateRevision
        )
        {
            return
                CreateSimpleResult(
                    TerrainCollisionGenerationOutcome.StalePlan,
                    plan.CollisionWorkMode,
                    worldSettings,
                    "Persistent runtime bake state changed after the collision plan was built."
                );
        }

        if (
            plan.CollisionWorkMode ==
            TerrainRuntimeBakeWorkMode.None
        )
        {
            return
                CreateSimpleResult(
                    TerrainCollisionGenerationOutcome.NoWork,
                    TerrainRuntimeBakeWorkMode.None,
                    worldSettings,
                    "",
                    "No collision generation work is required."
                );
        }

        if (worldSettings == null)
        {
            return
                CreateSimpleResult(
                    TerrainCollisionGenerationOutcome.Blocked,
                    plan.CollisionWorkMode,
                    worldSettings,
                    "WorldSettings is unavailable."
                );
        }

        string currentCollisionSignature =
            TerrainGenerationStateUtility
                .GetCurrentCollisionSettingsSignature(
                    worldSettings
                );

        if (
            !string.Equals(
                currentCollisionSignature,
                plan.CurrentCollisionSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            return
                CreateSimpleResult(
                    TerrainCollisionGenerationOutcome.StalePlan,
                    plan.CollisionWorkMode,
                    worldSettings,
                    "Collision settings changed after the runtime bake plan was built."
                );
        }

        List<Vector2Int> requestedChunks;

        if (
            plan.CollisionWorkMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            /*
             * A Full plan always targets the complete CURRENT grid rather than
             * trusting a stored list if layout state changed unexpectedly.
             */
            requestedChunks =
                CollectAllCollisionChunks(
                    worldSettings
                );
        }
        else
        {
            requestedChunks =
                CopySortedUniqueCoordinates(
                    plan.CollisionChunks
                );
        }

        return
            GenerateCollisionWork(
                worldSettings,
                plan.CollisionWorkMode,
                requestedChunks,
                snapshot,
                plan
            );
    }

    // =====================================================
    // SHARED GENERATION TRANSACTION
    // =====================================================

    private static TerrainCollisionGenerationResult
        GenerateCollisionWork(
            WorldSettings worldSettings,
            TerrainRuntimeBakeWorkMode workMode,
            List<Vector2Int> requestedChunks,
            TerrainRuntimeBakeStateSnapshot startSnapshot,
            TerrainRuntimeBakePlan sourcePlan
        )
    {
        using var profilerScope =
            WorldMeshesProfiler.RuntimeBakeCollisionGeneration.Auto();

        int revisionBefore =
            worldSettings != null
                ? worldSettings
                    .collisionMeshGenerationRevision
                : 0;

        int sourceHeightRevisionBefore =
            worldSettings != null
                ? worldSettings
                    .collisionSourceHeightmapGenerationRevision
                : -1;

        if (worldSettings == null)
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.Blocked,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "WorldSettings is unavailable.",
                    ""
                );
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.Blocked,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "Collision mesh generation must be performed outside Play Mode.",
                    ""
                );
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.Blocked,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "Runtime heightmaps are not current. Generate current runtime heightmaps first.",
                    ""
                );
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

        int heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                worldSettings
                    .heightfieldResolutionPerChunk
            );

        int collisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        int tileChunkSpan =
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            );

        int samplesPerTile =
            worldSettings
                .HeightTileSamplesPerSide;

        if (
            collisionResolution >
            heightfieldResolutionPerChunk
            ||
            heightfieldResolutionPerChunk %
                collisionResolution != 0
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.Blocked,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "Collision Resolution must be no greater than, and evenly divide, Heightfield Resolution / Chunk.",
                    ""
                );
        }

        if (
            workMode ==
                TerrainRuntimeBakeWorkMode.Incremental
            &&
            !AssetDatabase.IsValidFolder(
                CollisionMeshFolder
            )
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.StalePlan,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "Incremental collision generation cannot proceed because the generated collision mesh folder is missing. Rebuild the bake plan; collision should escalate to Full.",
                    ""
                );
        }

        requestedChunks =
            CopySortedUniqueCoordinates(
                requestedChunks
            );

        if (
            workMode ==
                TerrainRuntimeBakeWorkMode.Incremental
            &&
            requestedChunks.Count == 0
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.StalePlan,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "Incremental collision work contains no chunk coordinates.",
                    ""
                );
        }

        foreach (
            Vector2Int coordinate
            in requestedChunks
        )
        {
            if (
                !TerrainRuntimeBakeDependencyUtility
                    .IsCollisionChunkCoordinateValid(
                        worldSettings,
                        coordinate
                    )
            )
            {
                return
                    CreateResult(
                        TerrainCollisionGenerationOutcome.StalePlan,
                        workMode,
                        requestedChunks,
                        null,
                        null,
                        requestedChunks,
                        0,
                        0,
                        0,
                        false,
                        revisionBefore,
                        revisionBefore,
                        sourceHeightRevisionBefore,
                        sourceHeightRevisionBefore,
                        false,
                        false,
                        "Collision plan contains an invalid chunk coordinate: " +
                        "(" +
                        coordinate.x +
                        ", " +
                        coordinate.y +
                        ").",
                        ""
                    );
            }
        }

        if (
            sourcePlan != null
            &&
            TerrainRuntimeBakeStateService
                .GetSummary()
                .StateRevision !=
                sourcePlan.SourceStateRevision
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.StalePlan,
                    workMode,
                    requestedChunks,
                    null,
                    null,
                    requestedChunks,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    sourceHeightRevisionBefore,
                    sourceHeightRevisionBefore,
                    false,
                    false,
                    "Persistent runtime bake state changed during collision preflight.",
                    ""
                );
        }

        CollisionGenerationTarget target =
            CaptureTarget(
                worldSettings
            );

        if (
            workMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            EnsureFoldersExist();
        }

        int heightSampleStep =
            heightfieldResolutionPerChunk /
            collisionResolution;

        float collisionVertexSpacing =
            chunkSize /
            collisionResolution;

        List<Vector2Int> succeeded =
            new List<Vector2Int>();

        List<Vector2Int> failed =
            new List<Vector2Int>();

        int createdCount =
            0;

        int updatedCount =
            0;

        int removedCount =
            0;

        bool cancelled =
            false;

        string failureMessage =
            "";

        bool anyPhysicalContentChange =
            false;

        bool addressablesConfigurationRequired =
            false;

        long expectedStateRevision =
            startSnapshot.StateRevision;

        bool operationConfigurationBecameDirty =
            false;

        bool operationContentBecameDirty =
            false;

        bool staleDuringGeneration =
            false;

        string staleMessage =
            "";

        try
        {
            // =================================================
            // FULL-ONLY OBSOLETE ASSET CLEANUP
            // =================================================

            if (
                workMode ==
                TerrainRuntimeBakeWorkMode.Full
            )
            {
                Dictionary<Vector2Int, Mesh>
                    existingMeshes =
                        FindExistingCollisionMeshes();

                List<Vector2Int> existingCoordinates =
                    CopySortedUniqueCoordinates(
                        existingMeshes.Keys
                    );

                int obsoleteProgress =
                    0;

                foreach (
                    Vector2Int coordinate
                    in existingCoordinates
                )
                {
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

                    cancelled =
                        ShowProgress(
                            "Checking obsolete collision meshes",
                            "Chunk (" +
                            coordinate.x +
                            ", " +
                            coordinate.y +
                            ")",
                            obsoleteProgress,
                            Mathf.Max(
                                1,
                                existingCoordinates.Count +
                                requestedChunks.Count
                            )
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    Mesh existingMesh =
                        existingMeshes[
                            coordinate
                        ];

                    string assetPath =
                        AssetDatabase
                            .GetAssetPath(
                                existingMesh
                            );

                    if (
                        !AssetDatabase
                            .DeleteAsset(
                                assetPath
                            )
                    )
                    {
                        failureMessage =
                            "Could not remove obsolete collision mesh:\n" +
                            assetPath;

                        break;
                    }

                    removedCount++;
                    anyPhysicalContentChange =
                        true;

                    addressablesConfigurationRequired =
                        true;

                    obsoleteProgress++;
                }
            }

            // =================================================
            // REQUESTED CHUNK GENERATION
            // =================================================

            if (
                !cancelled
                &&
                string.IsNullOrEmpty(
                    failureMessage
                )
            )
            {
                Dictionary<
                    Vector2Int,
                    List<Vector2Int>
                > groups =
                    GroupChunksByHeightTile(
                        requestedChunks,
                        tileChunkSpan
                    );

                List<Vector2Int> tileCoordinates =
                    CopySortedUniqueCoordinates(
                        groups.Keys
                    );

                int completedChunkOperations =
                    0;

                foreach (
                    Vector2Int tileCoordinate
                    in tileCoordinates
                )
                {
                    if (
                        !TryLoadHeightTile(
                            tileCoordinate,
                            samplesPerTile,
                            out Texture2D heightTile,
                            out NativeArray<float> heightData,
                            out string heightTileError
                        )
                    )
                    {
                        List<Vector2Int> group =
                            groups[
                                tileCoordinate
                            ];

                        failed.AddRange(
                            group
                        );

                        failureMessage =
                            heightTileError;

                        break;
                    }

                    /*
                     * Keep the Texture2D local alive while its NativeArray view
                     * is used for every requested chunk in this tile.
                     */
                    if (heightTile == null)
                    {
                        failureMessage =
                            "Unexpected null runtime height tile.";

                        failed.AddRange(
                            groups[
                                tileCoordinate
                            ]
                        );

                        break;
                    }

                    List<Vector2Int> chunks =
                        groups[
                            tileCoordinate
                        ];

                    List<PreparedCollisionMesh> preparedBatch =
                        new List<PreparedCollisionMesh>();

                    foreach (
                        Vector2Int coordinate
                        in chunks
                    )
                    {
                        cancelled =
                            ShowProgress(
                                "Generating collision meshes",
                                "Chunk (" +
                                coordinate.x +
                                ", " +
                                coordinate.y +
                                ")\n" +
                                (
                                    completedChunkOperations +
                                    1
                                ) +
                                " / " +
                                requestedChunks.Count,
                                completedChunkOperations,
                                Mathf.Max(
                                    1,
                                    requestedChunks.Count
                                )
                            );

                        if (cancelled)
                        {
                            break;
                        }

                        int localChunkX =
                            coordinate.x %
                            tileChunkSpan;

                        int localChunkZ =
                            coordinate.y %
                            tileChunkSpan;

                        TerrainCollisionMeshWriteOutcome
                            writeOutcome =
                                GenerateOrUpdateCollisionMesh(
                                    coordinate.x,
                                    coordinate.y,
                                    localChunkX,
                                    localChunkZ,
                                    chunkSize,
                                    heightfieldResolutionPerChunk,
                                    collisionResolution,
                                    heightSampleStep,
                                    collisionVertexSpacing,
                                    samplesPerTile,
                                    heightData,
                                    out Mesh preparedMesh
                                );

                        if (
                            writeOutcome ==
                            TerrainCollisionMeshWriteOutcome
                                .Failed
                        )
                        {
                            failed.Add(
                                coordinate
                            );

                            failureMessage =
                                "Collision mesh generation failed for chunk (" +
                                coordinate.x +
                                ", " +
                                coordinate.y +
                                ").";

                            break;
                        }

                        preparedBatch.Add(
                            new PreparedCollisionMesh(
                                coordinate,
                                preparedMesh,
                                writeOutcome
                            )
                        );

                        anyPhysicalContentChange =
                            true;

                        if (
                            writeOutcome ==
                            TerrainCollisionMeshWriteOutcome
                                .Created
                        )
                        {
                            addressablesConfigurationRequired =
                                true;
                        }

                        completedChunkOperations++;
                    }

                    /*
                     * SaveAssets is global rather than Mesh-specific. If a
                     * later chunk failed after earlier chunks were prepared,
                     * do not try to persist only that prefix: the failing Mesh
                     * may also have been mutated in memory. Leave the complete
                     * prepared prefix pending and stop conservatively.
                     */
                    if (
                        !string.IsNullOrEmpty(
                            failureMessage
                        )
                    )
                    {
                        break;
                    }

                    if (preparedBatch.Count > 0)
                    {
                        if (
                            !TryPersistCollisionBatch(
                                preparedBatch,
                                out string persistenceError
                            )
                        )
                        {
                            foreach (
                                PreparedCollisionMesh prepared
                                in preparedBatch
                            )
                            {
                                failed.Add(
                                    prepared.coordinate
                                );
                            }

                            cancelled = false;
                            failureMessage =
                                persistenceError;

                            break;
                        }

                        bool batchCreatedAsset =
                            false;

                        foreach (
                            PreparedCollisionMesh prepared
                            in preparedBatch
                        )
                        {
                            succeeded.Add(
                                prepared.coordinate
                            );

                            if (
                                prepared.outcome ==
                                TerrainCollisionMeshWriteOutcome
                                    .Created
                            )
                            {
                                createdCount++;
                                batchCreatedAsset = true;
                            }
                            else
                            {
                                updatedCount++;
                            }
                        }

                        if (
                            workMode ==
                            TerrainRuntimeBakeWorkMode
                                .Incremental
                        )
                        {
                            if (
                                !TryAcknowledgeDurableIncrementalBatch(
                                    worldSettings,
                                    target,
                                    preparedBatch,
                                    batchCreatedAsset,
                                    ref expectedStateRevision,
                                    out PersistentDirtyTransition batchDirtyTransition,
                                    out string batchStaleMessage
                                )
                            )
                            {
                                staleDuringGeneration = true;
                                staleMessage =
                                    batchStaleMessage;
                            }
                            else
                            {
                                AccumulateDirtyTransition(
                                    batchDirtyTransition,
                                    ref operationConfigurationBecameDirty,
                                    ref operationContentBecameDirty
                                );
                            }
                        }

                        if (
                            !cancelled
                            &&
                            !staleDuringGeneration
                            &&
                            TerrainRuntimeBakeValidationHooks.ShouldCancelCoordinateStage(
                                TerrainRuntimeBakePipelineState.Collision,
                                succeeded.Count,
                                requestedChunks.Count
                            )
                        )
                        {
                            cancelled = true;
                        }
                    }

                    if (
                        cancelled
                        ||
                        staleDuringGeneration
                        ||
                        !string.IsNullOrEmpty(
                            failureMessage
                        )
                    )
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            EditorUtility
                .ClearProgressBar();
        }

        List<Vector2Int> unprocessed =
            CalculateUnprocessed(
                requestedChunks,
                succeeded,
                failed
            );

        // =====================================================
        // CANCELLED / FAILED / STALE PARTIAL OUTPUT
        // =====================================================

        if (
            cancelled
            ||
            staleDuringGeneration
            ||
            !string.IsNullOrEmpty(
                failureMessage
            )
        )
        {
            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation();

            /*
             * Incremental durable batches were already acknowledged at their
             * persistence boundary. This terminal mutation only preserves
             * conservative dirty state for physical changes that may not have
             * reached an acknowledgement boundary.
             */
            if (anyPhysicalContentChange)
            {
                mutation
                    .DirtyAddressablesContent();
            }

            if (addressablesConfigurationRequired)
            {
                mutation
                    .DirtyAddressablesConfiguration();
            }

            PersistentDirtyTransition dirtyTransition =
                ApplyMutationAndMeasureDirtyTransition(
                    mutation
                );

            AccumulateDirtyTransition(
                dirtyTransition,
                ref operationConfigurationBecameDirty,
                ref operationContentBecameDirty
            );

            TerrainCollisionGenerationOutcome outcome =
                staleDuringGeneration
                    ? TerrainCollisionGenerationOutcome
                        .StalePlan
                    : cancelled
                        ? TerrainCollisionGenerationOutcome
                            .Cancelled
                        : TerrainCollisionGenerationOutcome
                            .Failed;

            string errorMessage =
                staleDuringGeneration
                    ? staleMessage
                    : cancelled
                        ? ""
                        : failureMessage;

            string summary;

            if (staleDuringGeneration)
            {
                summary =
                    "Collision generation stopped because the target or persistent bake state changed at a durability boundary. Durable physical chunks remain persisted; any batch that could not be safely acknowledged remains pending.";
            }
            else if (cancelled)
            {
                summary =
                    "Collision generation was cancelled at a durability-safe boundary. Completed incremental batches were already persisted and acknowledged.";
            }
            else
            {
                summary =
                    "Collision generation stopped after a failure. Earlier durable incremental batches remain persisted and acknowledged; undurable work remains pending.";
            }

            return
                CreateResult(
                    outcome,
                    workMode,
                    requestedChunks,
                    succeeded,
                    failed,
                    unprocessed,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings
                        .collisionMeshGenerationRevision,
                    sourceHeightRevisionBefore,
                    worldSettings
                        .collisionSourceHeightmapGenerationRevision,
                    operationConfigurationBecameDirty,
                    operationContentBecameDirty,
                    errorMessage,
                    summary
                );
        }

        // =====================================================
        // FINALIZATION SAFETY
        // =====================================================

        using TerrainRuntimeBakePerformanceScope finalizePerformance =
            TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                "Collision.Finalize",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakePerformanceCategory.Finalize
            );

        if (
            !TargetStillMatches(
                worldSettings,
                target
            )
            ||
            TerrainRuntimeBakeStateService
                .GetSummary()
                .StateRevision !=
                expectedStateRevision
        )
        {
            TerrainRuntimeBakeStateMutation dirtyMutation =
                new TerrainRuntimeBakeStateMutation();

            if (anyPhysicalContentChange)
            {
                dirtyMutation
                    .DirtyAddressablesContent();
            }

            if (addressablesConfigurationRequired)
            {
                dirtyMutation
                    .DirtyAddressablesConfiguration();
            }

            PersistentDirtyTransition dirtyTransition =
                ApplyMutationAndMeasureDirtyTransition(
                    dirtyMutation
                );

            AccumulateDirtyTransition(
                dirtyTransition,
                ref operationConfigurationBecameDirty,
                ref operationContentBecameDirty
            );

            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.StalePlan,
                    workMode,
                    requestedChunks,
                    succeeded,
                    failed,
                    unprocessed,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings
                        .collisionMeshGenerationRevision,
                    sourceHeightRevisionBefore,
                    worldSettings
                        .collisionSourceHeightmapGenerationRevision,
                    operationConfigurationBecameDirty,
                    operationContentBecameDirty,
                    "Height generation, collision settings/layout, or persistent bake state changed before collision finalization.",
                    "Physical collision outputs were preserved, but collision generation was not marked current."
                );
        }

        bool stateRecorded =
            TerrainGenerationStateUtility
                .MarkCollisionMeshesGenerated(
                    worldSettings
                );

        if (!stateRecorded)
        {
            TerrainRuntimeBakeStateMutation dirtyMutation =
                new TerrainRuntimeBakeStateMutation();

            if (anyPhysicalContentChange)
            {
                dirtyMutation
                    .DirtyAddressablesContent();
            }

            if (addressablesConfigurationRequired)
            {
                dirtyMutation
                    .DirtyAddressablesConfiguration();
            }

            PersistentDirtyTransition dirtyTransition =
                ApplyMutationAndMeasureDirtyTransition(
                    dirtyMutation
                );

            AccumulateDirtyTransition(
                dirtyTransition,
                ref operationConfigurationBecameDirty,
                ref operationContentBecameDirty
            );

            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.Failed,
                    workMode,
                    requestedChunks,
                    succeeded,
                    failed,
                    unprocessed,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings
                        .collisionMeshGenerationRevision,
                    sourceHeightRevisionBefore,
                    worldSettings
                        .collisionSourceHeightmapGenerationRevision,
                    operationConfigurationBecameDirty,
                    operationContentBecameDirty,
                    "Collision meshes were generated, but collision generation state could not be recorded.",
                    "Physical collision outputs were preserved and remain conservatively pending."
                );
        }

        // =====================================================
        // FINAL PERSISTENT ACKNOWLEDGEMENT
        // =====================================================

        if (
            workMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            TerrainRuntimeBakeStateMutation finalMutation =
                new TerrainRuntimeBakeStateMutation()
                    .ClearAllCollisionChunks()
                    .ClearFullCollision();

            if (anyPhysicalContentChange)
            {
                finalMutation
                    .DirtyAddressablesContent();
            }

            if (addressablesConfigurationRequired)
            {
                finalMutation
                    .DirtyAddressablesConfiguration();
            }

            PersistentDirtyTransition finalDirtyTransition =
                ApplyMutationAndMeasureDirtyTransition(
                    finalMutation
                );

            AccumulateDirtyTransition(
                finalDirtyTransition,
                ref operationConfigurationBecameDirty,
                ref operationContentBecameDirty
            );
        }
        /*
         * Incremental durable batches already removed their own pending
         * coordinates and dirtied Addressables at each persistence boundary.
         * Avoid a redundant final persistent-state mutation.
         */

        using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
        {
            AssetDatabase.SaveAssets();
        }

        return
            CreateResult(
                TerrainCollisionGenerationOutcome.Completed,
                workMode,
                requestedChunks,
                succeeded,
                failed,
                unprocessed,
                createdCount,
                updatedCount,
                removedCount,
                true,
                revisionBefore,
                worldSettings
                    .collisionMeshGenerationRevision,
                sourceHeightRevisionBefore,
                worldSettings
                    .collisionSourceHeightmapGenerationRevision,
                operationConfigurationBecameDirty,
                operationContentBecameDirty,
                "",
                workMode ==
                    TerrainRuntimeBakeWorkMode.Full
                    ? "The complete collision mesh dataset was rebuilt and finalized."
                    : "The planned collision chunks were regenerated and the collision generation state was finalized."
            );
    }

    // =====================================================
    // HEIGHT TILE GROUPING / LOADING
    // =====================================================

    private static Dictionary<
        Vector2Int,
        List<Vector2Int>
    > GroupChunksByHeightTile(
        IEnumerable<Vector2Int> chunks,
        int tileChunkSpan
    )
    {
        Dictionary<
            Vector2Int,
            List<Vector2Int>
        > groups =
            new Dictionary<
                Vector2Int,
                List<Vector2Int>
            >();

        int safeSpan =
            Mathf.Max(
                1,
                tileChunkSpan
            );

        foreach (
            Vector2Int coordinate
            in chunks
        )
        {
            Vector2Int tileCoordinate =
                new Vector2Int(
                    coordinate.x /
                        safeSpan,
                    coordinate.y /
                        safeSpan
                );

            if (
                !groups.TryGetValue(
                    tileCoordinate,
                    out List<Vector2Int> group
                )
            )
            {
                group =
                    new List<Vector2Int>();

                groups.Add(
                    tileCoordinate,
                    group
                );
            }

            group.Add(
                coordinate
            );
        }

        foreach (
            List<Vector2Int> group
            in groups.Values
        )
        {
            group.Sort(
                CompareCoordinates
            );
        }

        return
            groups;
    }

    private static bool TryLoadHeightTile(
        Vector2Int tileCoordinate,
        int samplesPerTile,
        out Texture2D heightTile,
        out NativeArray<float> heightData,
        out string errorMessage
    )
    {
        using TerrainRuntimeBakePerformanceScope gatherPerformance =
            TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                "Collision.SourceGather",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakePerformanceCategory.Gather
            );

        heightTile =
            null;

        heightData =
            default;

        errorMessage =
            "";

        string heightTilePath =
            TerrainRuntimeHeightAssetUtility
                .GetHeightTilePath(
                    tileCoordinate.x,
                    tileCoordinate.y
                );

        heightTile =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    heightTilePath
                );

        if (heightTile == null)
        {
            errorMessage =
                "Required runtime height tile could not be loaded:\n" +
                heightTilePath;

            return false;
        }

        if (
            heightTile.width !=
                samplesPerTile
            ||
            heightTile.height !=
                samplesPerTile
            ||
            heightTile.format !=
                TextureFormat.RFloat
            ||
            !heightTile.isReadable
        )
        {
            errorMessage =
                "Runtime height tile is invalid:\n" +
                heightTilePath;

            return false;
        }

        try
        {
            heightData =
                heightTile
                    .GetPixelData<float>(
                        0
                    );
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not read runtime height tile:\n" +
                heightTilePath +
                "\n\n" +
                exception.Message;

            return false;
        }

        int expectedHeightCount =
            samplesPerTile *
            samplesPerTile;

        if (
            heightData.Length !=
            expectedHeightCount
        )
        {
            errorMessage =
                "Unexpected runtime height sample count.\n\n" +
                "Tile: (" +
                tileCoordinate.x +
                ", " +
                tileCoordinate.y +
                ")\n" +
                "Expected: " +
                expectedHeightCount +
                "\n" +
                "Actual: " +
                heightData.Length;

            return false;
        }

        return true;
    }

    // =====================================================
    // GENERATE / UPDATE ONE COLLISION MESH
    // =====================================================

    private static TerrainCollisionMeshWriteOutcome
        GenerateOrUpdateCollisionMesh(
            int chunkX,
            int chunkZ,
            int localChunkX,
            int localChunkZ,
            float chunkSize,
            int heightfieldResolutionPerChunk,
            int collisionResolution,
            int heightSampleStep,
            float collisionVertexSpacing,
            int heightSamplesPerTile,
            NativeArray<float> heightData,
            out Mesh preparedMesh
        )
    {
        preparedMesh =
            null;

        int verticesPerSide =
            collisionResolution +
            1;

        int vertexCount =
            verticesPerSide *
            verticesPerSide;

        int triangleIndexCount =
            collisionResolution *
            collisionResolution *
            6;

        Vector3[] vertices =
            new Vector3[
                vertexCount
            ];

        int[] triangles =
            new int[
                triangleIndexCount
            ];

        using TerrainRuntimeBakeTrackedMemoryLease collisionBufferMemory =
            TerrainRuntimeBakePerformanceDiagnostics.TrackTemporaryMemory(
                "Collision.VertexIndexBuffers",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakeTrackedMemoryCategory.CollisionBuffer,
                (long)vertexCount * 12L +
                (long)triangleIndexCount * sizeof(int)
            );

        using TerrainRuntimeBakePerformanceScope geometryPerformance =
            TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                "Collision.VertexIndexGeneration",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakePerformanceCategory.Generate
            );

        int sourceStartX =
            localChunkX *
            heightfieldResolutionPerChunk;

        int sourceStartZ =
            localChunkZ *
            heightfieldResolutionPerChunk;

        for (
            int z = 0;
            z <= collisionResolution;
            z++
        )
        {
            int sourceZ =
                sourceStartZ +
                z *
                heightSampleStep;

            for (
                int x = 0;
                x <= collisionResolution;
                x++
            )
            {
                int sourceX =
                    sourceStartX +
                    x *
                    heightSampleStep;

                if (
                    sourceX < 0
                    ||
                    sourceX >=
                        heightSamplesPerTile
                    ||
                    sourceZ < 0
                    ||
                    sourceZ >=
                        heightSamplesPerTile
                )
                {
                    Debug.LogError(
                        "Collision mesh attempted to read outside the runtime height tile.\n\n" +
                        "Chunk: (" +
                        chunkX +
                        ", " +
                        chunkZ +
                        ")\n" +
                        "Source Sample: (" +
                        sourceX +
                        ", " +
                        sourceZ +
                        ")"
                    );

                    return
                        TerrainCollisionMeshWriteOutcome
                            .Failed;
                }

                int sourceIndex =
                    sourceZ *
                    heightSamplesPerTile +
                    sourceX;

                float height =
                    heightData[
                        sourceIndex
                    ];

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
                        "Invalid height value encountered while generating collision mesh.\n\n" +
                        "Chunk: (" +
                        chunkX +
                        ", " +
                        chunkZ +
                        ")\n" +
                        "Source Sample: (" +
                        sourceX +
                        ", " +
                        sourceZ +
                        ")"
                    );

                    return
                        TerrainCollisionMeshWriteOutcome
                            .Failed;
                }

                int vertexIndex =
                    z *
                    verticesPerSide +
                    x;

                vertices[
                    vertexIndex
                ] =
                    new Vector3(
                        x *
                            collisionVertexSpacing,
                        height,
                        z *
                            collisionVertexSpacing
                    );
            }
        }

        int triangleIndex =
            0;

        for (
            int z = 0;
            z < collisionResolution;
            z++
        )
        {
            for (
                int x = 0;
                x < collisionResolution;
                x++
            )
            {
                int bottomLeft =
                    z *
                    verticesPerSide +
                    x;

                int bottomRight =
                    bottomLeft +
                    1;

                int topLeft =
                    bottomLeft +
                    verticesPerSide;

                int topRight =
                    topLeft +
                    1;

                triangles[
                    triangleIndex++
                ] =
                    bottomLeft;

                triangles[
                    triangleIndex++
                ] =
                    topLeft;

                triangles[
                    triangleIndex++
                ] =
                    bottomRight;

                triangles[
                    triangleIndex++
                ] =
                    bottomRight;

                triangles[
                    triangleIndex++
                ] =
                    topLeft;

                triangles[
                    triangleIndex++
                ] =
                    topRight;
            }
        }

        geometryPerformance?.Complete();

        using TerrainRuntimeBakePerformanceScope meshPerformance =
            TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                "Collision.MeshCreateUpload",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakePerformanceCategory.Commit
            );

        string assetPath =
            GetCollisionMeshPath(
                chunkX,
                chunkZ
            );

        Mesh mesh =
            AssetDatabase
                .LoadAssetAtPath<Mesh>(
                    assetPath
                );

        bool isNew =
            mesh == null;

        if (isNew)
        {
            mesh =
                new Mesh();
        }
        else
        {
            /*
             * Update the existing persistent Mesh object in place so its GUID,
             * Addressables entry, and existing references remain stable.
             */
            mesh.Clear(
                false
            );
        }

        mesh.name =
            GetCollisionMeshName(
                chunkX,
                chunkZ
            );

        mesh.indexFormat =
            vertexCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

        mesh.vertices =
            vertices;

        mesh.triangles =
            triangles;

        mesh.RecalculateBounds();

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
                "Generated collision mesh has incorrect horizontal bounds.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Expected X/Z Size: " +
                chunkSize +
                "\n" +
                "Actual Size: " +
                mesh.bounds.size
            );

            if (isNew)
            {
                UnityEngine.Object
                    .DestroyImmediate(
                        mesh
                    );
            }

            return
                TerrainCollisionMeshWriteOutcome
                    .Failed;
        }

        if (isNew)
        {
            try
            {
                using (WorldMeshesProfiler.AssetDatabaseCreateAsset.Auto())
                {
                    AssetDatabase.CreateAsset(
                        mesh,
                        assetPath
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not create collision mesh asset.\n\n" +
                    "Chunk: (" +
                    chunkX +
                    ", " +
                    chunkZ +
                    ")\n" +
                    "Asset: " +
                    assetPath +
                    "\n\n" +
                    exception.Message
                );

                UnityEngine.Object
                    .DestroyImmediate(
                        mesh
                    );

                return
                    TerrainCollisionMeshWriteOutcome
                        .Failed;
            }
        }

        meshPerformance?.Complete();

        bool collisionBakeSucceeded;

        using (
            TerrainRuntimeBakePerformanceScope cookPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    "Collision.ColliderBake",
                    TerrainRuntimeBakePipelineState.Collision,
                    TerrainRuntimeBakePerformanceCategory.Commit
                )
        )
        {
            collisionBakeSucceeded =
                BakeCollisionMesh(
                    mesh,
                    chunkX,
                    chunkZ
                );
        }

        if (!collisionBakeSucceeded)
        {
            if (isNew)
            {
                AssetDatabase.DeleteAsset(
                    assetPath
                );
            }

            return
                TerrainCollisionMeshWriteOutcome
                    .Failed;
        }

        EditorUtility.SetDirty(
            mesh
        );

        preparedMesh =
            mesh;

        return
            isNew
                ? TerrainCollisionMeshWriteOutcome
                    .Created
                : TerrainCollisionMeshWriteOutcome
                    .Updated;
    }

    private static bool TryPersistCollisionBatch(
        IReadOnlyList<PreparedCollisionMesh> preparedBatch,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            preparedBatch == null
            ||
            preparedBatch.Count == 0
        )
        {
            return true;
        }

        for (
            int index = 0;
            index < preparedBatch.Count;
            index++
        )
        {
            if (
                preparedBatch[index] == null
                ||
                preparedBatch[index].mesh == null
            )
            {
                errorMessage =
                    "Cannot persist a collision batch containing a null prepared Mesh.";

                return false;
            }
        }

        try
        {
            using TerrainRuntimeBakePerformanceScope commitPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    "Collision.AssetCommit",
                    TerrainRuntimeBakePipelineState.Collision,
                    TerrainRuntimeBakePerformanceCategory.Commit
                );

            using (WorldMeshesProfiler.RuntimeBakeCollisionSaveBatch.Auto())
            using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
            {
                AssetDatabase.SaveAssets();
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist the prepared collision mesh batch.\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }

    private static bool TryAcknowledgeDurableIncrementalBatch(
        WorldSettings worldSettings,
        CollisionGenerationTarget target,
        IReadOnlyList<PreparedCollisionMesh> durableBatch,
        bool batchCreatedAsset,
        ref long expectedStateRevision,
        out PersistentDirtyTransition dirtyTransition,
        out string staleMessage
    )
    {
        dirtyTransition =
            default;

        staleMessage =
            "";

        if (
            !TargetStillMatches(
                worldSettings,
                target
            )
        )
        {
            staleMessage =
                "Height generation or collision settings/layout changed after a collision batch was persisted. The durable batch was left pending.";

            return false;
        }

        TerrainRuntimeBakeStateSummary summary =
            TerrainRuntimeBakeStateService
                .GetSummary();

        if (
            summary.StateRevision !=
            expectedStateRevision
        )
        {
            staleMessage =
                "Persistent runtime bake state changed after a collision batch was persisted. The durable batch was left pending.";

            return false;
        }

        List<Vector2Int> durableCoordinates =
            new List<Vector2Int>(
                durableBatch.Count
            );

        for (
            int index = 0;
            index < durableBatch.Count;
            index++
        )
        {
            durableCoordinates.Add(
                durableBatch[index]
                    .coordinate
            );
        }

        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation()
                .RemoveCollisionChunks(
                    durableCoordinates
                )
                .DirtyAddressablesContent();

        if (batchCreatedAsset)
        {
            mutation
                .DirtyAddressablesConfiguration();
        }

        dirtyTransition =
            ApplyMutationAndMeasureDirtyTransition(
                mutation
            );

        expectedStateRevision =
            dirtyTransition
                .stateRevisionAfter;

        return true;
    }

    private static void AccumulateDirtyTransition(
        PersistentDirtyTransition transition,
        ref bool configurationBecameDirty,
        ref bool contentBecameDirty
    )
    {
        configurationBecameDirty |=
            transition
                .configurationBecameDirty;

        contentBecameDirty |=
            transition
                .contentBecameDirty;
    }

    private static bool BakeCollisionMesh(
        Mesh mesh,
        int chunkX,
        int chunkZ
    )
    {
        if (mesh == null)
        {
            Debug.LogError(
                "Cannot bake collision mesh physics data.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Mesh is null."
            );

            return false;
        }

        try
        {
            using (WorldMeshesProfiler.RuntimeBakeCollisionBakePhysics.Auto())
            {
                Physics.BakeMesh(
                    mesh.GetInstanceID(),
                    TerrainCollisionPhysicsSettings.Convex,
                    TerrainCollisionPhysicsSettings.CookingOptions
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Failed to pre-bake collision mesh physics data.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Mesh: " +
                mesh.name +
                "\n\n" +
                exception.Message
            );

            return false;
        }

        return true;
    }

    // =====================================================
    // TARGET / STATE SAFETY
    // =====================================================

    private static CollisionGenerationTarget CaptureTarget(
        WorldSettings worldSettings
    )
    {
        return
            new CollisionGenerationTarget
            {
                heightGenerationRevision =
                    worldSettings
                        .heightmapGenerationRevision,

                heightSignature =
                    worldSettings
                        .lastGeneratedHeightSignature ??
                    "",

                collisionSettingsSignature =
                    TerrainGenerationStateUtility
                        .GetCurrentCollisionSettingsSignature(
                            worldSettings
                        ),

                gridWidth =
                    Mathf.Max(
                        1,
                        worldSettings.gridWidth
                    ),

                gridHeight =
                    Mathf.Max(
                        1,
                        worldSettings.gridHeight
                    ),

                chunkSize =
                    Mathf.Max(
                        0.01f,
                        worldSettings.chunkSize
                    ),

                heightfieldResolutionPerChunk =
                    Mathf.Max(
                        1,
                        worldSettings
                            .heightfieldResolutionPerChunk
                    ),

                heightTileChunkSpan =
                    Mathf.Max(
                        1,
                        worldSettings
                            .heightTileChunkSpan
                    ),

                collisionResolution =
                    Mathf.Max(
                        1,
                        worldSettings
                            .collisionResolution
                    )
            };
    }

    private static bool TargetStillMatches(
        WorldSettings worldSettings,
        CollisionGenerationTarget target
    )
    {
        if (
            worldSettings == null
            ||
            target == null
        )
        {
            return false;
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            return false;
        }

        return
            worldSettings
                .heightmapGenerationRevision ==
                target.heightGenerationRevision
            &&
            string.Equals(
                worldSettings
                    .lastGeneratedHeightSignature ??
                    "",
                target.heightSignature,
                StringComparison.Ordinal
            )
            &&
            string.Equals(
                TerrainGenerationStateUtility
                    .GetCurrentCollisionSettingsSignature(
                        worldSettings
                    ),
                target.collisionSettingsSignature,
                StringComparison.Ordinal
            )
            &&
            Mathf.Max(
                1,
                worldSettings.gridWidth
            ) ==
            target.gridWidth
            &&
            Mathf.Max(
                1,
                worldSettings.gridHeight
            ) ==
            target.gridHeight
            &&
            Mathf.Approximately(
                Mathf.Max(
                    0.01f,
                    worldSettings.chunkSize
                ),
                target.chunkSize
            )
            &&
            Mathf.Max(
                1,
                worldSettings
                    .heightfieldResolutionPerChunk
            ) ==
            target
                .heightfieldResolutionPerChunk
            &&
            Mathf.Max(
                1,
                worldSettings
                    .heightTileChunkSpan
            ) ==
            target
                .heightTileChunkSpan
            &&
            Mathf.Max(
                1,
                worldSettings
                    .collisionResolution
            ) ==
            target
                .collisionResolution;
    }

    private static PersistentDirtyTransition
        ApplyMutationAndMeasureDirtyTransition(
            TerrainRuntimeBakeStateMutation mutation
        )
    {
        TerrainRuntimeBakeStateSummary before =
            TerrainRuntimeBakeStateService
                .GetSummary();

        TerrainRuntimeBakeStateService
            .ApplyMutation(
                mutation
            );

        TerrainRuntimeBakeStateSummary after =
            TerrainRuntimeBakeStateService
                .GetSummary();

        return
            new PersistentDirtyTransition
            {
                configurationBecameDirty =
                    !before
                        .AddressablesConfigurationDirty
                    &&
                    after
                        .AddressablesConfigurationDirty,

                contentBecameDirty =
                    !before
                        .AddressablesContentDirty
                    &&
                    after
                        .AddressablesContentDirty,

                stateRevisionAfter =
                    after
                        .StateRevision
            };
    }

    // =====================================================
    // COLLECTION HELPERS
    // =====================================================

    private static List<Vector2Int>
        CollectAllCollisionChunks(
            WorldSettings worldSettings
        )
    {
        List<Vector2Int> chunks =
            new List<Vector2Int>();

        if (worldSettings == null)
        {
            return
                chunks;
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
                chunks.Add(
                    new Vector2Int(
                        x,
                        z
                    )
                );
            }
        }

        return
            chunks;
    }

    private static List<Vector2Int>
        CopySortedUniqueCoordinates(
            IEnumerable<Vector2Int> source
        )
    {
        HashSet<Vector2Int> unique =
            source != null
                ? new HashSet<Vector2Int>(source)
                : new HashSet<Vector2Int>();

        List<Vector2Int> result =
            new List<Vector2Int>(
                unique
            );

        result.Sort(
            CompareCoordinates
        );

        return
            result;
    }

    private static List<Vector2Int>
        CalculateUnprocessed(
            IEnumerable<Vector2Int> requested,
            IEnumerable<Vector2Int> succeeded,
            IEnumerable<Vector2Int> failed
        )
    {
        HashSet<Vector2Int> completed =
            new HashSet<Vector2Int>();

        if (succeeded != null)
        {
            foreach (
                Vector2Int coordinate
                in succeeded
            )
            {
                completed.Add(
                    coordinate
                );
            }
        }

        if (failed != null)
        {
            foreach (
                Vector2Int coordinate
                in failed
            )
            {
                completed.Add(
                    coordinate
                );
            }
        }

        List<Vector2Int> unprocessed =
            new List<Vector2Int>();

        if (requested != null)
        {
            foreach (
                Vector2Int coordinate
                in requested
            )
            {
                if (
                    !completed.Contains(
                        coordinate
                    )
                )
                {
                    unprocessed.Add(
                        coordinate
                    );
                }
            }
        }

        unprocessed.Sort(
            CompareCoordinates
        );

        return
            unprocessed;
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison =
            left.y.CompareTo(
                right.y
            );

        if (yComparison != 0)
        {
            return
                yComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }

    // =====================================================
    // EXISTING COLLISION ASSETS
    // =====================================================

    private static Dictionary<Vector2Int, Mesh>
        FindExistingCollisionMeshes()
    {
        Dictionary<Vector2Int, Mesh> meshes =
            new Dictionary<Vector2Int, Mesh>();

        if (
            !AssetDatabase.IsValidFolder(
                CollisionMeshFolder
            )
        )
        {
            return
                meshes;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Mesh",
                new[]
                {
                    CollisionMeshFolder
                }
            );

        foreach (
            string guid
            in guids
        )
        {
            string path =
                AssetDatabase
                    .GUIDToAssetPath(
                        guid
                    );

            if (
                !TryGetCollisionCoordinates(
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

        return
            meshes;
    }

    // =====================================================
    // PATH / NAME
    // =====================================================

    public static string GetCollisionMeshPath(
        int chunkX,
        int chunkZ
    )
    {
        return
            CollisionMeshFolder +
            "/" +
            GetCollisionMeshName(
                chunkX,
                chunkZ
            ) +
            ".asset";
    }

    private static string GetCollisionMeshName(
        int chunkX,
        int chunkZ
    )
    {
        return
            "Chunk_" +
            chunkX +
            "_" +
            chunkZ +
            "_Collision";
    }

    private static bool TryGetCollisionCoordinates(
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

        string[] parts =
            fileName.Split(
                '_'
            );

        if (
            parts.Length != 4
            ||
            parts[0] != "Chunk"
            ||
            parts[3] != "Collision"
        )
        {
            return false;
        }

        return
            int.TryParse(
                parts[1],
                out x
            )
            &&
            int.TryParse(
                parts[2],
                out z
            );
    }

    // =====================================================
    // FOLDERS
    // =====================================================

    private static void EnsureFoldersExist()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Generated
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Root,
                "Generated"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.GeneratedMeshes
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Generated,
                "Meshes"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                CollisionMeshFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.GeneratedMeshes,
                "Collision"
            );
        }
    }

    // =====================================================
    // RESULT / LOGGING
    // =====================================================

    private static TerrainCollisionGenerationResult
        CreateSimpleResult(
            TerrainCollisionGenerationOutcome outcome,
            TerrainRuntimeBakeWorkMode mode,
            WorldSettings worldSettings,
            string errorMessage,
            string summaryMessage = ""
        )
    {
        int collisionRevision =
            worldSettings != null
                ? worldSettings
                    .collisionMeshGenerationRevision
                : 0;

        int sourceHeightRevision =
            worldSettings != null
                ? worldSettings
                    .collisionSourceHeightmapGenerationRevision
                : -1;

        return
            CreateResult(
                outcome,
                mode,
                null,
                null,
                null,
                null,
                0,
                0,
                0,
                false,
                collisionRevision,
                collisionRevision,
                sourceHeightRevision,
                sourceHeightRevision,
                false,
                false,
                errorMessage,
                summaryMessage
            );
    }

    private static TerrainCollisionGenerationResult
        CreateResult(
            TerrainCollisionGenerationOutcome outcome,
            TerrainRuntimeBakeWorkMode mode,
            IEnumerable<Vector2Int> requested,
            IEnumerable<Vector2Int> succeeded,
            IEnumerable<Vector2Int> failed,
            IEnumerable<Vector2Int> unprocessed,
            int createdCount,
            int updatedCount,
            int removedCount,
            bool datasetFinalized,
            int revisionBefore,
            int revisionAfter,
            int sourceHeightRevisionBefore,
            int sourceHeightRevisionAfter,
            bool addressablesConfigurationBecameDirty,
            bool addressablesContentBecameDirty,
            string errorMessage,
            string summaryMessage
        )
    {
        return
            new TerrainCollisionGenerationResult(
                outcome,
                mode,
                requested,
                succeeded,
                failed,
                unprocessed,
                createdCount,
                updatedCount,
                removedCount,
                datasetFinalized,
                revisionBefore,
                revisionAfter,
                sourceHeightRevisionBefore,
                sourceHeightRevisionAfter,
                addressablesConfigurationBecameDirty,
                addressablesContentBecameDirty,
                errorMessage,
                summaryMessage
            );
    }

    private static void LogResult(
        TerrainCollisionGenerationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError(
                "Collision generation returned no result."
            );

            return;
        }

        string report =
            result.BuildDiagnosticReport();

        if (
            result.Outcome ==
                TerrainCollisionGenerationOutcome
                    .Completed
            ||
            result.Outcome ==
                TerrainCollisionGenerationOutcome
                    .NoWork
        )
        {
            Debug.Log(
                report
            );
        }
        else if (
            result.Outcome ==
            TerrainCollisionGenerationOutcome
                .Cancelled
        )
        {
            Debug.LogWarning(
                report
            );
        }
        else
        {
            Debug.LogError(
                report
            );
        }
    }

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
                    "Terrain Collision Generation",
                    operation +
                    "\n\n" +
                    detail,
                    progress
                );
    }
}
