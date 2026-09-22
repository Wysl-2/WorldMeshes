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

    private const int FullPersistenceChunkThreshold =
        512;

    private const double CollisionProgressRefreshIntervalSeconds =
        0.10d;

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

    private sealed class ExistingCollisionMeshRecord
    {
        public readonly Mesh mesh;
        public readonly string assetPath;

        public ExistingCollisionMeshRecord(
            Mesh mesh,
            string assetPath
        )
        {
            this.mesh =
                mesh;

            this.assetPath =
                assetPath;
        }
    }

    private sealed class CollisionProgressReporter
    {
        private double nextRefreshAt;
        private string lastOperation =
            "";
        private bool hasDisplayed;

        public bool ReportIfDue(
            string operation,
            Vector2Int coordinate,
            int current,
            int total,
            bool includeOrdinal,
            bool force
        )
        {
            string safeOperation =
                operation ??
                "";

            double now =
                EditorApplication
                    .timeSinceStartup;

            bool operationChanged =
                !hasDisplayed
                ||
                !string.Equals(
                    lastOperation,
                    safeOperation,
                    StringComparison.Ordinal
                );

            if (
                !force
                &&
                hasDisplayed
                &&
                !operationChanged
                &&
                now < nextRefreshAt
            )
            {
                return false;
            }

            int safeTotal =
                Mathf.Max(
                    1,
                    total
                );

            int displayOrdinal =
                Mathf.Clamp(
                    current + 1,
                    1,
                    safeTotal
                );

            string detail =
                "Chunk (" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")";

            if (includeOrdinal)
            {
                detail +=
                    "\n" +
                    displayOrdinal +
                    " / " +
                    safeTotal;
            }

            float progress =
                total > 0
                    ? Mathf.Clamp01(
                        (float)current /
                        total
                    )
                    : 1f;

            bool cancelled =
                EditorUtility
                    .DisplayCancelableProgressBar(
                        "Terrain Collision Generation",
                        safeOperation +
                        "\n\n" +
                        detail,
                        progress
                    );

            hasDisplayed =
                true;

            lastOperation =
                safeOperation;

            nextRefreshAt =
                now +
                CollisionProgressRefreshIntervalSeconds;

            return cancelled;
        }
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

        if (
            !TryCreateCollisionTriangleTopology(
                collisionResolution,
                out int[] collisionTopology,
                out string topologyError
            )
        )
        {
            return
                CreateResult(
                    TerrainCollisionGenerationOutcome.Failed,
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
                    topologyError,
                    ""
                );
        }

        using TerrainRuntimeBakeTrackedMemoryLease collisionTopologyMemory =
            TerrainRuntimeBakePerformanceDiagnostics.TrackTemporaryMemory(
                "Collision.TopologyBuffer",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakeTrackedMemoryCategory.CollisionBuffer,
                (long)collisionTopology.Length * sizeof(int)
            );

        List<Vector2Int> succeeded =
            new List<Vector2Int>();

        List<Vector2Int> failed =
            new List<Vector2Int>();

        List<PreparedCollisionMesh> pendingFullPersistence =
            new List<PreparedCollisionMesh>(
                FullPersistenceChunkThreshold
            );

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

        Dictionary<
            Vector2Int,
            ExistingCollisionMeshRecord
        > fullExistingMeshes =
            null;

        CollisionProgressReporter progressReporter =
            new CollisionProgressReporter();

        int generationProgressTotal =
            Mathf.Max(
                1,
                requestedChunks.Count
            );

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
                fullExistingMeshes =
                    FindExistingCollisionMeshes();

                List<Vector2Int> existingCoordinates =
                    CopySortedUniqueCoordinates(
                        fullExistingMeshes.Keys
                    );

                int obsoleteProgress =
                    0;

                int obsoleteProgressTotal =
                    Mathf.Max(
                        1,
                        existingCoordinates.Count +
                        requestedChunks.Count
                    );

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
                        progressReporter
                            .ReportIfDue(
                                "Checking obsolete collision meshes",
                                coordinate,
                                obsoleteProgress,
                                obsoleteProgressTotal,
                                false,
                                false
                            );

                    if (cancelled)
                    {
                        break;
                    }

                    ExistingCollisionMeshRecord existingRecord =
                        fullExistingMeshes[
                            coordinate
                        ];

                    string assetPath =
                        existingRecord.assetPath;

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

                    fullExistingMeshes.Remove(
                        coordinate
                    );

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
                            progressReporter
                                .ReportIfDue(
                                    "Generating collision meshes",
                                    coordinate,
                                    completedChunkOperations,
                                    generationProgressTotal,
                                    true,
                                    false
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
                                PrepareCollisionMesh(
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
                                    collisionTopology,
                                    fullExistingMeshes,
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
                     * The current prepared batch does not become approved
                     * Full persistence work until every Mesh is prepared and
                     * the complete C01 PhysX batch has cooked successfully.
                     * Previously approved Full batches may remain in the
                     * separate pendingFullPersistence accumulator and can be
                     * targeted-saved safely if this current batch terminates
                     * abnormally.
                     */
                    if (
                        !string.IsNullOrEmpty(
                            failureMessage
                        )
                    )
                    {
                        break;
                    }

                    if (cancelled)
                    {
                        CleanupCreatedPreparedCollisionMeshes(
                            preparedBatch
                        );

                        break;
                    }

                    if (preparedBatch.Count > 0)
                    {
                        Vector2Int lastPreparedCoordinate =
                            preparedBatch[
                                preparedBatch.Count -
                                1
                            ]
                            .coordinate;

                        int forcedProgressCurrent =
                            Mathf.Max(
                                0,
                                completedChunkOperations -
                                1
                            );

                        cancelled =
                            progressReporter
                                .ReportIfDue(
                                    "Generating collision meshes",
                                    lastPreparedCoordinate,
                                    forcedProgressCurrent,
                                    generationProgressTotal,
                                    true,
                                    true
                                );

                        if (cancelled)
                        {
                            CleanupCreatedPreparedCollisionMeshes(
                                preparedBatch
                            );

                            break;
                        }

                        if (
                            !TryBakePreparedCollisionBatch(
                                preparedBatch,
                                out string physicsBakeError
                            )
                        )
                        {
                            CleanupCreatedPreparedCollisionMeshes(
                                preparedBatch
                            );

                            foreach (
                                PreparedCollisionMesh prepared
                                in preparedBatch
                            )
                            {
                                failed.Add(
                                    prepared.coordinate
                                );
                            }

                            cancelled =
                                false;

                            failureMessage =
                                physicsBakeError;

                            break;
                        }

                        MarkPreparedCollisionBatchDirty(
                            preparedBatch
                        );

                        if (
                            workMode ==
                            TerrainRuntimeBakeWorkMode
                                .Incremental
                        )
                        {
                            if (
                                !TryPersistIncrementalCollisionBatch(
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

                                cancelled =
                                    false;

                                failureMessage =
                                    persistenceError;

                                break;
                            }

                            bool batchCreatedAsset =
                                CommitPersistedCollisionMeshes(
                                    preparedBatch,
                                    succeeded,
                                    ref createdCount,
                                    ref updatedCount
                                );

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
                                staleDuringGeneration =
                                    true;

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
                        else if (
                            workMode ==
                            TerrainRuntimeBakeWorkMode
                                .Full
                        )
                        {
                            if (
                                !TryAddPreparedBatchToFullPersistence(
                                    pendingFullPersistence,
                                    preparedBatch,
                                    out string accumulationError
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

                                failureMessage =
                                    accumulationError;

                                break;
                            }

                            if (
                                pendingFullPersistence.Count >=
                                FullPersistenceChunkThreshold
                            )
                            {
                                if (
                                    !TryPersistFullCollisionCheckpoint(
                                        pendingFullPersistence,
                                        out string persistenceError
                                    )
                                )
                                {
                                    failureMessage =
                                        persistenceError;

                                    break;
                                }

                                CommitPersistedCollisionMeshes(
                                    pendingFullPersistence,
                                    succeeded,
                                    ref createdCount,
                                    ref updatedCount
                                );

                                pendingFullPersistence.Clear();
                            }
                        }

                        if (
                            !cancelled
                            &&
                            !staleDuringGeneration
                            &&
                            TerrainRuntimeBakeValidationHooks.ShouldCancelCoordinateStage(
                                TerrainRuntimeBakePipelineState.Collision,
                                completedChunkOperations,
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

        if (
            !cancelled
            &&
            !staleDuringGeneration
            &&
            string.IsNullOrEmpty(
                failureMessage
            )
            &&
            workMode ==
            TerrainRuntimeBakeWorkMode.Full
            &&
            pendingFullPersistence.Count > 0
        )
        {
            if (
                !TryPersistFullCollisionCheckpoint(
                    pendingFullPersistence,
                    out string finalPersistenceError
                )
            )
            {
                failureMessage =
                    finalPersistenceError;
            }
            else
            {
                CommitPersistedCollisionMeshes(
                    pendingFullPersistence,
                    succeeded,
                    ref createdCount,
                    ref updatedCount
                );

                pendingFullPersistence.Clear();
            }
        }

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
            if (
                workMode ==
                TerrainRuntimeBakeWorkMode.Full
                &&
                pendingFullPersistence.Count > 0
            )
            {
                List<PreparedCollisionMesh> recoveredMeshes =
                    new List<PreparedCollisionMesh>(
                        pendingFullPersistence.Count
                    );

                bool recoverySucceeded =
                    TryPersistPendingFullMeshesIndividually(
                        pendingFullPersistence,
                        recoveredMeshes,
                        out string recoveryError
                    );

                if (recoveredMeshes.Count > 0)
                {
                    CommitPersistedCollisionMeshes(
                        recoveredMeshes,
                        succeeded,
                        ref createdCount,
                        ref updatedCount
                    );

                    pendingFullPersistence.RemoveRange(
                        0,
                        recoveredMeshes.Count
                    );
                }

                if (!recoverySucceeded)
                {
                    foreach (
                        PreparedCollisionMesh pending
                        in pendingFullPersistence
                    )
                    {
                        failed.Add(
                            pending.coordinate
                        );
                    }

                    if (staleDuringGeneration)
                    {
                        staleMessage =
                            CombineDiagnosticMessages(
                                staleMessage,
                                recoveryError
                            );
                    }
                    else if (cancelled)
                    {
                        cancelled =
                            false;

                        failureMessage =
                            recoveryError;
                    }
                    else
                    {
                        failureMessage =
                            CombineDiagnosticMessages(
                                failureMessage,
                                recoveryError
                            );
                    }
                }
            }

            List<Vector2Int> partialUnprocessed =
                CalculateUnprocessed(
                    requestedChunks,
                    succeeded,
                    failed
                );

            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation();

            /*
             * Incremental durable batches were already acknowledged at their
             * persistence boundary. Full checkpoints only make physical Mesh
             * assets durable; Full pending state remains conservative until a
             * completely successful finalization.
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
                    "Collision generation stopped because the target or persistent bake state changed. Durable physical chunks remain persisted; work that could not be safely persisted or finalized remains pending.";
            }
            else if (cancelled)
            {
                summary =
                    workMode ==
                        TerrainRuntimeBakeWorkMode.Full
                        ? "Collision generation was cancelled. Approved Full-rebuild meshes were persisted individually where required; unfinished work remains pending."
                        : "Collision generation was cancelled at a durability-safe boundary. Completed incremental batches were already persisted and acknowledged.";
            }
            else
            {
                summary =
                    workMode ==
                        TerrainRuntimeBakeWorkMode.Full
                        ? "Collision generation stopped after a failure. Earlier Full persistence checkpoints remain durable; unfinished or unsuccessfully recovered work remains pending."
                        : "Collision generation stopped after a failure. Earlier durable incremental batches remain persisted and acknowledged; undurable work remains pending.";
            }

            return
                CreateResult(
                    outcome,
                    workMode,
                    requestedChunks,
                    succeeded,
                    failed,
                    partialUnprocessed,
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

        List<Vector2Int> unprocessed =
            CalculateUnprocessed(
                requestedChunks,
                succeeded,
                failed
            );

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
    // COLLISION TOPOLOGY
    // =====================================================

    private static bool TryCreateCollisionTriangleTopology(
        int collisionResolution,
        out int[] topology,
        out string errorMessage
    )
    {
        topology =
            null;

        errorMessage =
            "";

        if (collisionResolution < 1)
        {
            errorMessage =
                "Collision topology requires a collision resolution of at least 1.";

            return false;
        }

        try
        {
            using TerrainRuntimeBakePerformanceScope topologyPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    "Collision.TopologyGeneration",
                    TerrainRuntimeBakePipelineState.Collision,
                    TerrainRuntimeBakePerformanceCategory.Generate
                );

            int verticesPerSide;
            int vertexCount;
            int triangleIndexCount;

            checked
            {
                verticesPerSide =
                    collisionResolution +
                    1;

                vertexCount =
                    verticesPerSide *
                    verticesPerSide;

                triangleIndexCount =
                    collisionResolution *
                    collisionResolution *
                    6;
            }

            int[] generatedTopology =
                new int[
                    triangleIndexCount
                ];

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

                    if (
                        bottomLeft < 0
                        ||
                        topRight >=
                            vertexCount
                    )
                    {
                        errorMessage =
                            "Collision topology generated an out-of-range vertex index.";

                        return false;
                    }

                    generatedTopology[
                        triangleIndex++
                    ] =
                        bottomLeft;

                    generatedTopology[
                        triangleIndex++
                    ] =
                        topLeft;

                    generatedTopology[
                        triangleIndex++
                    ] =
                        bottomRight;

                    generatedTopology[
                        triangleIndex++
                    ] =
                        bottomRight;

                    generatedTopology[
                        triangleIndex++
                    ] =
                        topLeft;

                    generatedTopology[
                        triangleIndex++
                    ] =
                        topRight;
                }
            }

            if (
                triangleIndex !=
                generatedTopology.Length
            )
            {
                errorMessage =
                    "Collision topology generation produced an unexpected index count.\n\n" +
                    "Expected: " +
                    generatedTopology.Length +
                    "\n" +
                    "Actual: " +
                    triangleIndex;

                return false;
            }

            topology =
                generatedTopology;

            return true;
        }
        catch (OverflowException exception)
        {
            errorMessage =
                "Collision topology size overflowed the supported integer range.\n\n" +
                exception.Message;

            return false;
        }
        catch (OutOfMemoryException exception)
        {
            errorMessage =
                "Could not allocate the collision topology buffer.\n\n" +
                exception.Message;

            return false;
        }
    }

    // =====================================================
    // COLLISION SOURCE WINDOW
    // =====================================================

    private static bool TryValidateCollisionSourceWindow(
        int chunkX,
        int chunkZ,
        int localChunkX,
        int localChunkZ,
        int heightfieldResolutionPerChunk,
        int collisionResolution,
        int heightSampleStep,
        int heightSamplesPerTile,
        out int sourceStartX,
        out int sourceStartZ,
        out string errorMessage
    )
    {
        sourceStartX =
            0;

        sourceStartZ =
            0;

        errorMessage =
            "";

        if (
            heightfieldResolutionPerChunk < 1
            ||
            collisionResolution < 1
            ||
            heightSampleStep < 1
            ||
            heightSamplesPerTile < 1
            ||
            localChunkX < 0
            ||
            localChunkZ < 0
        )
        {
            errorMessage =
                "Collision source-window validation received invalid layout inputs.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Local Chunk: (" +
                localChunkX +
                ", " +
                localChunkZ +
                ")\n" +
                "Heightfield Resolution Per Chunk: " +
                heightfieldResolutionPerChunk +
                "\n" +
                "Collision Resolution: " +
                collisionResolution +
                "\n" +
                "Height Sample Step: " +
                heightSampleStep +
                "\n" +
                "Height Samples Per Tile: " +
                heightSamplesPerTile;

            return false;
        }

        long sampledExtent =
            (long)collisionResolution *
            heightSampleStep;

        if (
            sampledExtent !=
            heightfieldResolutionPerChunk
        )
        {
            errorMessage =
                "Collision source-window sampling extent does not match one heightfield chunk.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Heightfield Resolution Per Chunk: " +
                heightfieldResolutionPerChunk +
                "\n" +
                "Collision Resolution: " +
                collisionResolution +
                "\n" +
                "Height Sample Step: " +
                heightSampleStep +
                "\n" +
                "Sampled Extent: " +
                sampledExtent;

            return false;
        }

        long sourceStartXLong =
            (long)localChunkX *
            heightfieldResolutionPerChunk;

        long sourceStartZLong =
            (long)localChunkZ *
            heightfieldResolutionPerChunk;

        long sourceEndXLong =
            sourceStartXLong +
            sampledExtent;

        long sourceEndZLong =
            sourceStartZLong +
            sampledExtent;

        if (
            sourceStartXLong < 0L
            ||
            sourceStartZLong < 0L
            ||
            sourceEndXLong <
                sourceStartXLong
            ||
            sourceEndZLong <
                sourceStartZLong
            ||
            sourceEndXLong >=
                heightSamplesPerTile
            ||
            sourceEndZLong >=
                heightSamplesPerTile
        )
        {
            errorMessage =
                "Collision chunk source window lies outside the runtime height tile.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Local Chunk: (" +
                localChunkX +
                ", " +
                localChunkZ +
                ")\n" +
                "Source Start: (" +
                sourceStartXLong +
                ", " +
                sourceStartZLong +
                ")\n" +
                "Source End: (" +
                sourceEndXLong +
                ", " +
                sourceEndZLong +
                ")\n" +
                "Height Samples Per Tile: " +
                heightSamplesPerTile +
                "\n" +
                "Heightfield Resolution Per Chunk: " +
                heightfieldResolutionPerChunk +
                "\n" +
                "Collision Resolution: " +
                collisionResolution +
                "\n" +
                "Height Sample Step: " +
                heightSampleStep;

            return false;
        }

        if (
            sourceStartXLong >
                int.MaxValue
            ||
            sourceStartZLong >
                int.MaxValue
        )
        {
            errorMessage =
                "Collision source-window start exceeds the supported integer index range.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Source Start: (" +
                sourceStartXLong +
                ", " +
                sourceStartZLong +
                ")";

            return false;
        }

        sourceStartX =
            (int)sourceStartXLong;

        sourceStartZ =
            (int)sourceStartZLong;

        return true;
    }

    // =====================================================
    // DIRECT COLLISION BOUNDS
    // =====================================================

    private static bool TryCreateCollisionBounds(
        int chunkX,
        int chunkZ,
        float chunkSize,
        int collisionResolution,
        float collisionVertexSpacing,
        float minimumHeight,
        float maximumHeight,
        out Bounds bounds,
        out string errorMessage
    )
    {
        bounds =
            default;

        errorMessage =
            "";

        if (
            float.IsNaN(
                minimumHeight
            )
            ||
            float.IsInfinity(
                minimumHeight
            )
            ||
            float.IsNaN(
                maximumHeight
            )
            ||
            float.IsInfinity(
                maximumHeight
            )
            ||
            maximumHeight <
                minimumHeight
        )
        {
            errorMessage =
                "Cannot calculate collision Mesh bounds from invalid vertical extrema.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Minimum Height: " +
                minimumHeight +
                "\n" +
                "Maximum Height: " +
                maximumHeight;

            return false;
        }

        float horizontalExtent =
            collisionResolution *
            collisionVertexSpacing;

        if (
            float.IsNaN(
                horizontalExtent
            )
            ||
            float.IsInfinity(
                horizontalExtent
            )
            ||
            horizontalExtent <= 0f
        )
        {
            errorMessage =
                "Cannot calculate collision Mesh bounds from an invalid horizontal extent.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Collision Resolution: " +
                collisionResolution +
                "\n" +
                "Collision Vertex Spacing: " +
                collisionVertexSpacing +
                "\n" +
                "Calculated Horizontal Extent: " +
                horizontalExtent;

            return false;
        }

        float verticalExtent =
            maximumHeight -
            minimumHeight;

        if (
            float.IsNaN(
                verticalExtent
            )
            ||
            float.IsInfinity(
                verticalExtent
            )
            ||
            verticalExtent < 0f
        )
        {
            errorMessage =
                "Cannot calculate collision Mesh bounds from an invalid vertical extent.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Minimum Height: " +
                minimumHeight +
                "\n" +
                "Maximum Height: " +
                maximumHeight +
                "\n" +
                "Calculated Vertical Extent: " +
                verticalExtent;

            return false;
        }

        float horizontalCenter =
            horizontalExtent *
            0.5f;

        float verticalCenter =
            minimumHeight +
            verticalExtent *
            0.5f;

        if (
            float.IsNaN(
                horizontalCenter
            )
            ||
            float.IsInfinity(
                horizontalCenter
            )
            ||
            float.IsNaN(
                verticalCenter
            )
            ||
            float.IsInfinity(
                verticalCenter
            )
        )
        {
            errorMessage =
                "Cannot calculate collision Mesh bounds from an invalid center.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Calculated Horizontal Center: " +
                horizontalCenter +
                "\n" +
                "Calculated Vertical Center: " +
                verticalCenter;

            return false;
        }

        float boundsTolerance =
            Mathf.Max(
                0.001f,
                chunkSize *
                    0.00001f
            );

        if (
            float.IsNaN(
                boundsTolerance
            )
            ||
            float.IsInfinity(
                boundsTolerance
            )
            ||
            boundsTolerance < 0f
            ||
            Mathf.Abs(
                horizontalExtent -
                chunkSize
            ) >
                boundsTolerance
        )
        {
            errorMessage =
                "Generated collision geometry has an unexpected horizontal extent.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Collision Resolution: " +
                collisionResolution +
                "\n" +
                "Collision Vertex Spacing: " +
                collisionVertexSpacing +
                "\n" +
                "Expected Chunk Size: " +
                chunkSize +
                "\n" +
                "Calculated Horizontal Extent: " +
                horizontalExtent;

            return false;
        }

        Vector3 boundsCenter =
            new Vector3(
                horizontalCenter,
                verticalCenter,
                horizontalCenter
            );

        Vector3 boundsSize =
            new Vector3(
                horizontalExtent,
                verticalExtent,
                horizontalExtent
            );

        if (
            float.IsNaN(
                boundsSize.x
            )
            ||
            float.IsInfinity(
                boundsSize.x
            )
            ||
            float.IsNaN(
                boundsSize.y
            )
            ||
            float.IsInfinity(
                boundsSize.y
            )
            ||
            float.IsNaN(
                boundsSize.z
            )
            ||
            float.IsInfinity(
                boundsSize.z
            )
        )
        {
            errorMessage =
                "Calculated collision Mesh bounds size is invalid.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")";

            return false;
        }

        bounds =
            new Bounds(
                boundsCenter,
                boundsSize
            );

        return true;
    }

    // =====================================================
    // GENERATE / UPDATE ONE COLLISION MESH
    // =====================================================

    private static TerrainCollisionMeshWriteOutcome
        PrepareCollisionMesh(
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
            int[] collisionTopology,
            Dictionary<
                Vector2Int,
                ExistingCollisionMeshRecord
            > fullExistingMeshes,
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

        long expectedTriangleIndexCount =
            (long)collisionResolution *
            collisionResolution *
            6L;

        if (
            collisionTopology == null
            ||
            collisionTopology.Length !=
                expectedTriangleIndexCount
        )
        {
            Debug.LogError(
                "Collision topology does not match the requested collision resolution.\n\n" +
                "Chunk: (" +
                chunkX +
                ", " +
                chunkZ +
                ")\n" +
                "Resolution: " +
                collisionResolution +
                "\n" +
                "Expected Indices: " +
                expectedTriangleIndexCount +
                "\n" +
                "Actual Indices: " +
                (
                    collisionTopology != null
                        ? collisionTopology.Length
                        : 0
                )
            );

            return
                TerrainCollisionMeshWriteOutcome
                    .Failed;
        }

        if (
            !TryValidateCollisionSourceWindow(
                chunkX,
                chunkZ,
                localChunkX,
                localChunkZ,
                heightfieldResolutionPerChunk,
                collisionResolution,
                heightSampleStep,
                heightSamplesPerTile,
                out int sourceStartX,
                out int sourceStartZ,
                out string sourceWindowError
            )
        )
        {
            Debug.LogError(
                sourceWindowError
            );

            return
                TerrainCollisionMeshWriteOutcome
                    .Failed;
        }

        Vector3[] vertices =
            new Vector3[
                vertexCount
            ];

        using TerrainRuntimeBakeRepeatedMemoryScope collisionBufferMemory =
            TerrainRuntimeBakePerformanceDiagnostics.TrackRepeatedTemporaryMemory(
                "Collision.VertexBuffer",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakeTrackedMemoryCategory.CollisionBuffer,
                (long)vertexCount * 12L
            );

        float minimumHeight =
            float.PositiveInfinity;

        float maximumHeight =
            float.NegativeInfinity;

        using (
            TerrainRuntimeBakeAggregatedPerformanceScope geometryPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginAggregatedOperation(
                    "Collision.VertexGeneration",
                    TerrainRuntimeBakePipelineState.Collision,
                    TerrainRuntimeBakePerformanceCategory.Generate
                )
        )
        {
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

                int sourceRowStart =
                    sourceZ *
                    heightSamplesPerTile;

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

                    int sourceIndex =
                        sourceRowStart +
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
        }

        if (
            !TryCreateCollisionBounds(
                chunkX,
                chunkZ,
                chunkSize,
                collisionResolution,
                collisionVertexSpacing,
                minimumHeight,
                maximumHeight,
                out Bounds calculatedBounds,
                out string boundsError
            )
        )
        {
            Debug.LogError(
                boundsError
            );

            return
                TerrainCollisionMeshWriteOutcome
                    .Failed;
        }

        Mesh mesh =
            null;

        bool isNew =
            false;

        using (
            TerrainRuntimeBakeAggregatedPerformanceScope meshPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginAggregatedOperation(
                    "Collision.MeshCreateUpload",
                    TerrainRuntimeBakePipelineState.Collision,
                    TerrainRuntimeBakePerformanceCategory.Commit
                )
        )
        {
            string assetPath =
                GetCollisionMeshPath(
                    chunkX,
                    chunkZ
                );

            if (fullExistingMeshes != null)
            {
                Vector2Int coordinate =
                    new Vector2Int(
                        chunkX,
                        chunkZ
                    );

                if (
                    fullExistingMeshes.TryGetValue(
                        coordinate,
                        out ExistingCollisionMeshRecord existingRecord
                    )
                    &&
                    existingRecord != null
                    &&
                    existingRecord.mesh != null
                    &&
                    string.Equals(
                        existingRecord.assetPath,
                        assetPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    mesh =
                        existingRecord.mesh;
                }
            }
            else
            {
                mesh =
                    AssetDatabase
                        .LoadAssetAtPath<Mesh>(
                            assetPath
                        );
            }

            isNew =
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

            mesh.subMeshCount =
                1;

            mesh.SetTriangles(
                collisionTopology,
                0,
                false
            );

            mesh.bounds =
                calculatedBounds;

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
        }

        preparedMesh =
            mesh;

        return
            isNew
                ? TerrainCollisionMeshWriteOutcome
                    .Created
                : TerrainCollisionMeshWriteOutcome
                    .Updated;
    }

    private static bool TryPersistIncrementalCollisionBatch(
        IReadOnlyList<PreparedCollisionMesh> preparedBatch,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryValidatePreparedCollisionMeshes(
                preparedBatch,
                "incremental collision batch",
                out errorMessage
            )
        )
        {
            return false;
        }

        if (preparedBatch.Count == 0)
        {
            return true;
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
                "Could not persist the prepared incremental collision mesh batch.\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }

    private static bool TryPersistFullCollisionCheckpoint(
        IReadOnlyList<PreparedCollisionMesh> pendingMeshes,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryValidatePreparedCollisionMeshes(
                pendingMeshes,
                "Full collision persistence checkpoint",
                out errorMessage
            )
        )
        {
            return false;
        }

        if (pendingMeshes.Count == 0)
        {
            return true;
        }

        try
        {
            using TerrainRuntimeBakePerformanceScope commitPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    "Collision.FullAssetCheckpoint",
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
                "Could not persist a Full collision checkpoint containing " +
                pendingMeshes.Count +
                " Meshes.\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }

    private static bool TryPersistPendingFullMeshesIndividually(
        IReadOnlyList<PreparedCollisionMesh> pendingMeshes,
        List<PreparedCollisionMesh> successfullyPersisted,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (successfullyPersisted == null)
        {
            errorMessage =
                "Full collision recovery requires a destination collection for successfully persisted Meshes.";

            return false;
        }

        successfullyPersisted.Clear();

        if (
            !TryValidatePreparedCollisionMeshes(
                pendingMeshes,
                "Full collision recovery set",
                out errorMessage
            )
        )
        {
            return false;
        }

        if (pendingMeshes.Count == 0)
        {
            return true;
        }

        using TerrainRuntimeBakePerformanceScope recoveryPerformance =
            TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                "Collision.FullAssetRecovery",
                TerrainRuntimeBakePipelineState.Collision,
                TerrainRuntimeBakePerformanceCategory.Commit
            );

        using (WorldMeshesProfiler.RuntimeBakeCollisionSaveBatch.Auto())
        {
            for (
                int index = 0;
                index < pendingMeshes.Count;
                index++
            )
            {
                PreparedCollisionMesh prepared =
                    pendingMeshes[index];

                try
                {
                    using (
                        WorldMeshesProfiler
                            .AssetDatabaseSaveAssetIfDirty
                            .Auto()
                    )
                    {
                        AssetDatabase.SaveAssetIfDirty(
                            prepared.mesh
                        );
                    }
                }
                catch (Exception exception)
                {
                    errorMessage =
                        "Could not individually persist approved Full collision Mesh during recovery.\n\n" +
                        "Chunk: (" +
                        prepared.coordinate.x +
                        ", " +
                        prepared.coordinate.y +
                        ")\n" +
                        "Successfully Recovered Before Failure: " +
                        successfullyPersisted.Count +
                        " / " +
                        pendingMeshes.Count +
                        "\n\n" +
                        exception.Message;

                    return false;
                }

                successfullyPersisted.Add(
                    prepared
                );
            }
        }

        return true;
    }

    private static bool TryValidatePreparedCollisionMeshes(
        IReadOnlyList<PreparedCollisionMesh> preparedMeshes,
        string description,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (preparedMeshes == null)
        {
            errorMessage =
                "Cannot persist a null " +
                description +
                ".";

            return false;
        }

        for (
            int index = 0;
            index < preparedMeshes.Count;
            index++
        )
        {
            if (
                preparedMeshes[index] == null
                ||
                preparedMeshes[index].mesh == null
            )
            {
                errorMessage =
                    "Cannot persist " +
                    description +
                    " containing a null prepared Mesh.";

                return false;
            }
        }

        return true;
    }

    private static bool TryAddPreparedBatchToFullPersistence(
        List<PreparedCollisionMesh> pendingFullPersistence,
        IReadOnlyList<PreparedCollisionMesh> preparedBatch,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (pendingFullPersistence == null)
        {
            errorMessage =
                "Full collision persistence accumulator is unavailable.";

            return false;
        }

        if (
            !TryValidatePreparedCollisionMeshes(
                preparedBatch,
                "prepared Full collision batch",
                out errorMessage
            )
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < preparedBatch.Count;
            index++
        )
        {
            pendingFullPersistence.Add(
                preparedBatch[index]
            );
        }

        return true;
    }

    private static bool CommitPersistedCollisionMeshes(
        IReadOnlyList<PreparedCollisionMesh> persistedMeshes,
        List<Vector2Int> succeeded,
        ref int createdCount,
        ref int updatedCount
    )
    {
        bool createdAnyAsset =
            false;

        if (
            persistedMeshes == null
            ||
            succeeded == null
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < persistedMeshes.Count;
            index++
        )
        {
            PreparedCollisionMesh prepared =
                persistedMeshes[index];

            if (prepared == null)
            {
                continue;
            }

            succeeded.Add(
                prepared.coordinate
            );

            if (
                prepared.outcome ==
                TerrainCollisionMeshWriteOutcome.Created
            )
            {
                createdCount++;
                createdAnyAsset =
                    true;
            }
            else if (
                prepared.outcome ==
                TerrainCollisionMeshWriteOutcome.Updated
            )
            {
                updatedCount++;
            }
        }

        return
            createdAnyAsset;
    }

    private static string CombineDiagnosticMessages(
        string primary,
        string secondary
    )
    {
        if (string.IsNullOrEmpty(primary))
        {
            return
                secondary ??
                "";
        }

        if (string.IsNullOrEmpty(secondary))
        {
            return
                primary;
        }

        return
            primary +
            "\n\n" +
            secondary;
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

    private static bool TryBakePreparedCollisionBatch(
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

        List<Mesh> meshes =
            new List<Mesh>(
                preparedBatch.Count
            );

        for (
            int index = 0;
            index < preparedBatch.Count;
            index++
        )
        {
            PreparedCollisionMesh prepared =
                preparedBatch[
                    index
                ];

            if (
                prepared == null
                ||
                prepared.mesh == null
            )
            {
                errorMessage =
                    "Cannot pre-bake a collision batch containing a null prepared Mesh.";

                return false;
            }

            meshes.Add(
                prepared.mesh
            );
        }

        bool bakeSucceeded;

        using (
            TerrainRuntimeBakePerformanceScope cookPerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    "Collision.ColliderBakeBatch",
                    TerrainRuntimeBakePipelineState.Collision,
                    TerrainRuntimeBakePerformanceCategory.Commit
                )
        )
        using (
            WorldMeshesProfiler
                .RuntimeBakeCollisionBakePhysics
                .Auto()
        )
        {
            bakeSucceeded =
                TerrainCollisionPhysicsBatchBaker
                    .TryBakeMeshes(
                        meshes,
                        out string batchError
                    );

            if (!bakeSucceeded)
            {
                errorMessage =
                    "Could not pre-bake the prepared collision Mesh batch.\n\n" +
                    batchError;
            }
        }

        return
            bakeSucceeded;
    }

    private static void MarkPreparedCollisionBatchDirty(
        IReadOnlyList<PreparedCollisionMesh> preparedBatch
    )
    {
        if (preparedBatch == null)
        {
            return;
        }

        for (
            int index = 0;
            index < preparedBatch.Count;
            index++
        )
        {
            PreparedCollisionMesh prepared =
                preparedBatch[
                    index
                ];

            if (
                prepared == null
                ||
                prepared.mesh == null
            )
            {
                continue;
            }

            EditorUtility.SetDirty(
                prepared.mesh
            );
        }
    }

    private static void CleanupCreatedPreparedCollisionMeshes(
        IReadOnlyList<PreparedCollisionMesh> preparedBatch
    )
    {
        if (preparedBatch == null)
        {
            return;
        }

        for (
            int index = 0;
            index < preparedBatch.Count;
            index++
        )
        {
            PreparedCollisionMesh prepared =
                preparedBatch[
                    index
                ];

            if (
                prepared == null
                ||
                prepared.mesh == null
                ||
                prepared.outcome !=
                    TerrainCollisionMeshWriteOutcome
                        .Created
            )
            {
                continue;
            }

            string assetPath =
                AssetDatabase
                    .GetAssetPath(
                        prepared.mesh
                    );

            if (
                string.IsNullOrEmpty(
                    assetPath
                )
            )
            {
                Debug.LogError(
                    "Could not clean up a newly created collision Mesh after an incomplete physics batch because its asset path is unavailable.\n\n" +
                    "Chunk: (" +
                    prepared.coordinate.x +
                    ", " +
                    prepared.coordinate.y +
                    ")"
                );

                continue;
            }

            try
            {
                if (
                    !AssetDatabase
                        .DeleteAsset(
                            assetPath
                        )
                )
                {
                    Debug.LogError(
                        "Could not clean up a newly created collision Mesh after an incomplete physics batch.\n\n" +
                        "Chunk: (" +
                        prepared.coordinate.x +
                        ", " +
                        prepared.coordinate.y +
                        ")\n" +
                        "Asset: " +
                        assetPath
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not clean up a newly created collision Mesh after an incomplete physics batch.\n\n" +
                    "Chunk: (" +
                    prepared.coordinate.x +
                    ", " +
                    prepared.coordinate.y +
                    ")\n" +
                    "Asset: " +
                    assetPath +
                    "\n\n" +
                    exception.Message
                );
            }
        }
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

    private static Dictionary<
        Vector2Int,
        ExistingCollisionMeshRecord
    > FindExistingCollisionMeshes()
    {
        Dictionary<
            Vector2Int,
            ExistingCollisionMeshRecord
        > meshes =
            new Dictionary<
                Vector2Int,
                ExistingCollisionMeshRecord
            >();

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

            Vector2Int coordinate =
                new Vector2Int(
                    x,
                    z
                );

            ExistingCollisionMeshRecord candidate =
                new ExistingCollisionMeshRecord(
                    mesh,
                    path
                );

            if (
                !meshes.TryGetValue(
                    coordinate,
                    out ExistingCollisionMeshRecord existingRecord
                )
            )
            {
                meshes.Add(
                    coordinate,
                    candidate
                );

                continue;
            }

            string expectedPath =
                GetCollisionMeshPath(
                    x,
                    z
                );

            bool existingIsCanonical =
                existingRecord != null
                &&
                string.Equals(
                    existingRecord.assetPath,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase
                );

            bool candidateIsCanonical =
                string.Equals(
                    path,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase
                );

            if (
                !existingIsCanonical
                &&
                candidateIsCanonical
            )
            {
                meshes[
                    coordinate
                ] =
                    candidate;
            }
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

}
