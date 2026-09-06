using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeHeightCompiler
{
    private enum TileWriteOutcome
    {
        Failed,
        Created,
        Updated
    }

    private struct CompileTarget
    {
        public int AuthoringRevision;
        public string AuthoringSignature;
        public string AuthoringContentHash;
        public int GridWidth;
        public int GridHeight;
        public float ChunkSize;
        public int HeightfieldResolutionPerChunk;
        public int HeightTileChunkSpan;
        public int HeightTileGridWidth;
        public int HeightTileGridHeight;
        public float HeightTileWorldSize;
        public int HeightTileSamplesPerSide;
    }

    // =====================================================
    // LEGACY FULL-COMPILE API
    // =====================================================

    /*
     * Preserve the existing manual workflow: this API always performs a full
     * runtime height rebuild. Package 08 will later make planned baking the
     * normal user-facing path.
     */
    public static void CompileRuntimeHeightmaps(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        TerrainRuntimeHeightCompileResult result =
            RebuildAllRuntimeHeightmaps(
                worldSettings,
                authoringData
            );

        LogResult(
            result
        );
    }

    // =====================================================
    // PLANNED HEIGHT WORK
    // =====================================================

    public static TerrainRuntimeHeightCompileResult
        CompilePlannedHeightWork(
            WorldSettings worldSettings,
            TerrainAuthoringData authoringData,
            TerrainRuntimeBakePlan plan
        )
    {
        int revisionBefore =
            worldSettings != null
                ? worldSettings.heightmapGenerationRevision
                : 0;

        if (plan == null)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    TerrainRuntimeBakeWorkMode.None,
                    revisionBefore,
                    "Runtime height compilation received a null bake plan."
                );
        }

        if (plan.IsBlocked)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    plan.HeightWorkMode,
                    revisionBefore,
                    plan.BlockReason
                );
        }

        TerrainRuntimeBakeStateSnapshot stateSnapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        if (
            stateSnapshot.StateRevision !=
            plan.SourceStateRevision
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    plan.HeightWorkMode,
                    revisionBefore,
                    "Persistent runtime bake state changed after the plan " +
                    "was created. Build a new bake plan before compiling."
                );
        }

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    plan.HeightWorkMode,
                    revisionBefore,
                    worldSettings == null
                        ? "WorldSettings is unavailable."
                        : "TerrainAuthoringData is unavailable."
                );
        }

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                currentAuthoringSignature
            )
            ||
            !string.Equals(
                currentAuthoringSignature,
                plan.CurrentAuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    plan.HeightWorkMode,
                    revisionBefore,
                    "The current authoring signature no longer matches the " +
                    "bake plan. Build a new plan before compiling."
                );
        }

        if (
            plan.HeightWorkMode ==
            TerrainRuntimeBakeWorkMode.None
        )
        {
            return
                new TerrainRuntimeHeightCompileResult(
                    TerrainRuntimeHeightCompileOutcome.NoWork,
                    TerrainRuntimeBakeWorkMode.None,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0,
                    0,
                    false,
                    revisionBefore,
                    revisionBefore,
                    "",
                    "The current bake plan contains no runtime height work."
                );
        }

        List<Vector2Int> requestedTiles =
            new List<Vector2Int>();

        if (
            plan.HeightWorkMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            HashSet<Vector2Int> allTiles =
                new HashSet<Vector2Int>();

            TerrainRuntimeBakeDependencyUtility
                .CollectAllHeightTiles(
                    worldSettings,
                    allTiles
                );

            requestedTiles.AddRange(
                allTiles
            );

            requestedTiles.Sort(
                CompareCoordinates
            );
        }
        else
        {
            HashSet<Vector2Int> validatedTiles =
                new HashSet<Vector2Int>();

            if (
                !TerrainRuntimeBakeDependencyUtility
                    .TryCopyValidHeightTiles(
                        worldSettings,
                        plan.HeightTiles,
                        validatedTiles,
                        out string coordinateError
                    )
            )
            {
                return
                    CreateTerminalResult(
                        TerrainRuntimeHeightCompileOutcome.StalePlan,
                        plan.HeightWorkMode,
                        revisionBefore,
                        coordinateError
                    );
            }

            if (validatedTiles.Count == 0)
            {
                return
                    CreateTerminalResult(
                        TerrainRuntimeHeightCompileOutcome.StalePlan,
                        plan.HeightWorkMode,
                        revisionBefore,
                        "The incremental height plan contains no tiles."
                    );
            }

            requestedTiles.AddRange(
                validatedTiles
            );

            requestedTiles.Sort(
                CompareCoordinates
            );
        }

        return
            ExecuteCompile(
                worldSettings,
                authoringData,
                plan.HeightWorkMode,
                requestedTiles,
                true,
                plan.SourceStateRevision,
                plan.CurrentAuthoringSignature
            );
    }

    // =====================================================
    // EXPLICIT FULL REBUILD
    // =====================================================

    public static TerrainRuntimeHeightCompileResult
        RebuildAllRuntimeHeightmaps(
            WorldSettings worldSettings,
            TerrainAuthoringData authoringData
        )
    {
        int revisionBefore =
            worldSettings != null
                ? worldSettings.heightmapGenerationRevision
                : 0;

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    TerrainRuntimeBakeWorkMode.Full,
                    revisionBefore,
                    worldSettings == null
                        ? "WorldSettings is null."
                        : "TerrainAuthoringData is null."
                );
        }

        HashSet<Vector2Int> allTiles =
            new HashSet<Vector2Int>();

        TerrainRuntimeBakeDependencyUtility
            .CollectAllHeightTiles(
                worldSettings,
                allTiles
            );

        List<Vector2Int> requestedTiles =
            new List<Vector2Int>(
                allTiles
            );

        requestedTiles.Sort(
            CompareCoordinates
        );

        return
            ExecuteCompile(
                worldSettings,
                authoringData,
                TerrainRuntimeBakeWorkMode.Full,
                requestedTiles,
                false,
                -1,
                ""
            );
    }

    // =====================================================
    // SHARED COMPILE CORE
    // =====================================================

    private static TerrainRuntimeHeightCompileResult ExecuteCompile(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainRuntimeBakeWorkMode workMode,
        IReadOnlyList<Vector2Int> requestedTiles,
        bool enforcePlanIdentity,
        long planStateRevision,
        string plannedAuthoringSignature
    )
    {
        int revisionBefore =
            worldSettings != null
                ? worldSettings.heightmapGenerationRevision
                : 0;

        List<Vector2Int> succeededTiles =
            new List<Vector2Int>();

        List<Vector2Int> failedTiles =
            new List<Vector2Int>();

        List<Vector2Int> unprocessedTiles =
            new List<Vector2Int>();

        int createdCount =
            0;

        int updatedCount =
            0;

        int removedCount =
            0;

        bool addressablesConfigurationDirty =
            false;

        string firstError =
            "";

        if (
            workMode !=
                TerrainRuntimeBakeWorkMode.Incremental
            &&
            workMode !=
                TerrainRuntimeBakeWorkMode.Full
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Failed,
                    workMode,
                    revisionBefore,
                    "Runtime height compiler received an unsupported work mode."
                );
        }

        if (
            requestedTiles == null
            ||
            requestedTiles.Count == 0
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Failed,
                    workMode,
                    revisionBefore,
                    "Runtime height compiler received no requested tiles."
                );
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    workMode,
                    revisionBefore,
                    "Runtime heightmaps must be compiled outside Play Mode."
                );
        }

        if (
            !SystemInfo.SupportsTextureFormat(
                TextureFormat.RFloat
            )
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    workMode,
                    revisionBefore,
                    "TextureFormat.RFloat is not supported by the current " +
                    "graphics device."
                );
        }

        if (
            !TerrainAuthoringStateUtility
                .TryValidateCommittedHeightfield(
                    worldSettings,
                    authoringData,
                    out _,
                    out string authoringContentHash,
                    out string authoringValidationError
                )
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    workMode,
                    revisionBefore,
                    "The committed authoring heightfield is invalid.\n\n" +
                    authoringValidationError
                );
        }

        string authoringSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                authoringSignature
            )
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Blocked,
                    workMode,
                    revisionBefore,
                    "The current authoring signature could not be calculated."
                );
        }

        if (
            enforcePlanIdentity
            &&
            !string.Equals(
                authoringSignature,
                plannedAuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    workMode,
                    revisionBefore,
                    "The authoring signature changed before height compilation " +
                    "started. Build a new bake plan."
                );
        }

        CompileTarget target =
            CaptureTarget(
                worldSettings,
                authoringData,
                authoringSignature,
                authoringContentHash
            );

        if (
            enforcePlanIdentity
            &&
            TerrainRuntimeBakeStateService
                .GetSnapshot()
                .StateRevision !=
                    planStateRevision
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    workMode,
                    revisionBefore,
                    "Persistent runtime bake state changed before height " +
                    "compilation started. Build a new bake plan."
                );
        }

        EnsureFoldersExist();

        TerrainHeightmapManifest runtimeManifest;

        if (
            workMode ==
            TerrainRuntimeBakeWorkMode.Incremental
        )
        {
            runtimeManifest =
                AssetDatabase
                    .LoadAssetAtPath<TerrainHeightmapManifest>(
                        TerrainRuntimeHeightAssetUtility
                            .HeightmapManifestPath
                    );

            if (
                !ValidateIncrementalManifest(
                    runtimeManifest,
                    worldSettings,
                    out string manifestError
                )
            )
            {
                return
                    CreateTerminalResult(
                        TerrainRuntimeHeightCompileOutcome.StalePlan,
                        workMode,
                        revisionBefore,
                        manifestError
                    );
            }
        }
        else
        {
            runtimeManifest =
                GetOrCreateRuntimeManifest();

            if (runtimeManifest == null)
            {
                return
                    CreateTerminalResult(
                        TerrainRuntimeHeightCompileOutcome.Failed,
                        workMode,
                        revisionBefore,
                        "The runtime heightmap manifest could not be created."
                    );
            }
        }

        Dictionary<Vector2Int, Texture2D> existingTiles =
            null;

        List<KeyValuePair<Vector2Int, Texture2D>> obsoleteTiles =
            new List<KeyValuePair<Vector2Int, Texture2D>>();

        if (
            workMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            existingTiles =
                FindExistingRuntimeHeightTiles();

            foreach (
                KeyValuePair<Vector2Int, Texture2D> pair
                in existingTiles
            )
            {
                Vector2Int coordinate =
                    pair.Key;

                if (
                    coordinate.x < 0
                    ||
                    coordinate.y < 0
                    ||
                    coordinate.x >=
                        worldSettings.HeightTileGridWidth
                    ||
                    coordinate.y >=
                        worldSettings.HeightTileGridHeight
                )
                {
                    obsoleteTiles.Add(
                        pair
                    );
                }
            }
        }

        int totalOperations =
            requestedTiles.Count +
            obsoleteTiles.Count;

        int currentOperation =
            0;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        float[] compiledHeightData =
            new float[
                samplesPerSide *
                samplesPerSide
            ];

        TerrainRuntimeHeightCompositionContext
            compositionContext =
                new TerrainRuntimeHeightCompositionContext();

        if (
            !compositionContext.TryPrepare(
                worldSettings,
                authoringData,
                out string compositionPreparationError
            )
        )
        {
            compositionContext.Dispose();

            return
                CreateTerminalResult(
                    TerrainRuntimeHeightCompileOutcome.Failed,
                    workMode,
                    revisionBefore,
                    "The modifier composition context could not be prepared.\n\n" +
                    compositionPreparationError
                );
        }

        /*
         * Full rebuilds invalidate structural completeness only after every
         * read-only preflight and composition-context preparation succeeds.
         * An ordinary preflight failure therefore does not unnecessarily
         * invalidate the previously generated dataset.
         */
        if (
            workMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            PrepareManifestForFullRebuild(
                runtimeManifest,
                worldSettings
            );

            if (
                !TrySaveManifest(
                    runtimeManifest,
                    out string manifestPrepareError
                )
            )
            {
                compositionContext.Dispose();

                return
                    CreateTerminalResult(
                        TerrainRuntimeHeightCompileOutcome.Failed,
                        workMode,
                        revisionBefore,
                        manifestPrepareError
                    );
            }
        }

        bool cancelled =
            false;

        bool manifestBecameUnsafe =
            false;

        try
        {
            for (
                int requestIndex = 0;
                requestIndex < requestedTiles.Count;
                requestIndex++
            )
            {
                Vector2Int coordinate =
                    requestedTiles[requestIndex];

                cancelled =
                    ShowProgress(
                        workMode ==
                            TerrainRuntimeBakeWorkMode.Incremental
                            ? "Compiling planned runtime height tiles"
                            : "Rebuilding runtime heightmap tiles",
                        $"Tile ({coordinate.x}, {coordinate.y}) " +
                        $"{requestIndex + 1} / {requestedTiles.Count}",
                        currentOperation,
                        totalOperations
                    );

                if (cancelled)
                {
                    AddRemainingCoordinates(
                        requestedTiles,
                        requestIndex,
                        unprocessedTiles
                    );

                    break;
                }

                TileWriteOutcome writeOutcome =
                    CompileOneTile(
                        compositionContext,
                        coordinate.x,
                        coordinate.y,
                        samplesPerSide,
                        compiledHeightData,
                        out float tileMinimumHeight,
                        out float tileMaximumHeight,
                        out bool outputMayHaveChanged,
                        out string tileError
                    );

                if (
                    writeOutcome ==
                    TileWriteOutcome.Failed
                )
                {
                    failedTiles.Add(
                        coordinate
                    );

                    if (string.IsNullOrEmpty(firstError))
                    {
                        firstError =
                            tileError;
                    }

                    if (outputMayHaveChanged)
                    {
                        manifestBecameUnsafe =
                            true;

                        runtimeManifest.isComplete =
                            false;

                        TrySaveManifest(
                            runtimeManifest,
                            out _
                        );

                        AddRemainingCoordinates(
                            requestedTiles,
                            requestIndex + 1,
                            unprocessedTiles
                        );

                        break;
                    }

                    currentOperation++;
                    continue;
                }

                if (
                    !runtimeManifest.SetTileHeightRange(
                        coordinate.x,
                        coordinate.y,
                        tileMinimumHeight,
                        tileMaximumHeight
                    )
                )
                {
                    failedTiles.Add(
                        coordinate
                    );

                    if (string.IsNullOrEmpty(firstError))
                    {
                        firstError =
                            $"Could not store height range metadata for " +
                            $"runtime tile ({coordinate.x}, {coordinate.y}).";
                    }

                    runtimeManifest.isComplete =
                        false;

                    TrySaveManifest(
                        runtimeManifest,
                        out _
                    );

                    manifestBecameUnsafe =
                        true;

                    AddRemainingCoordinates(
                        requestedTiles,
                        requestIndex + 1,
                        unprocessedTiles
                    );

                    break;
                }

                if (
                    workMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                )
                {
                    string rangeSaveError =
                        "";

                    bool rangeValid =
                        runtimeManifest
                            .TryRecalculateGlobalHeightRange(
                                out _,
                                out _
                            );

                    bool rangeSaved =
                        rangeValid
                        &&
                        TrySaveManifest(
                            runtimeManifest,
                            out rangeSaveError
                        );

                    if (
                        !rangeValid
                        ||
                        !rangeSaved
                    )
                    {
                        failedTiles.Add(
                            coordinate
                        );

                        if (string.IsNullOrEmpty(firstError))
                        {
                            firstError =
                                string.IsNullOrEmpty(rangeSaveError)
                                    ? "Could not recalculate the runtime " +
                                      "height global range."
                                    : rangeSaveError;
                        }

                        runtimeManifest.isComplete =
                            false;

                        TrySaveManifest(
                            runtimeManifest,
                            out _
                        );

                        manifestBecameUnsafe =
                            true;

                        AddRemainingCoordinates(
                            requestedTiles,
                            requestIndex + 1,
                            unprocessedTiles
                        );

                        break;
                    }
                }

                succeededTiles.Add(
                    coordinate
                );

                if (
                    writeOutcome ==
                    TileWriteOutcome.Created
                )
                {
                    createdCount++;

                    addressablesConfigurationDirty =
                        true;
                }
                else
                {
                    updatedCount++;
                }

                currentOperation++;
            }

            if (
                workMode ==
                    TerrainRuntimeBakeWorkMode.Full
                &&
                !cancelled
                &&
                failedTiles.Count == 0
                &&
                !manifestBecameUnsafe
            )
            {
                foreach (
                    KeyValuePair<Vector2Int, Texture2D> pair
                    in obsoleteTiles
                )
                {
                    Vector2Int coordinate =
                        pair.Key;

                    cancelled =
                        ShowProgress(
                            "Removing obsolete runtime heightmap tiles",
                            $"Tile ({coordinate.x}, {coordinate.y})",
                            currentOperation,
                            totalOperations
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    string path =
                        AssetDatabase.GetAssetPath(
                            pair.Value
                        );

                    if (
                        !string.IsNullOrEmpty(path)
                        &&
                        AssetDatabase.DeleteAsset(
                            path
                        )
                    )
                    {
                        removedCount++;

                        addressablesConfigurationDirty =
                            true;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(firstError))
                        {
                            firstError =
                                "Could not remove obsolete runtime heightmap " +
                                "asset:\n" +
                                path;
                        }

                        break;
                    }

                    currentOperation++;
                }
            }
        }
        finally
        {
            compositionContext.Dispose();

            EditorUtility.ClearProgressBar();
        }

        bool obsoleteCleanupFailed =
            workMode ==
                TerrainRuntimeBakeWorkMode.Full
            &&
            !cancelled
            &&
            !string.IsNullOrEmpty(firstError)
            &&
            failedTiles.Count == 0
            &&
            succeededTiles.Count == requestedTiles.Count;

        if (
            cancelled
            ||
            failedTiles.Count > 0
            ||
            manifestBecameUnsafe
            ||
            obsoleteCleanupFailed
        )
        {
            if (
                workMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                &&
                !manifestBecameUnsafe
            )
            {
                AcknowledgePartialIncrementalSuccess(
                    succeededTiles,
                    createdCount > 0
                );
            }
            else if (
                succeededTiles.Count > 0
                ||
                removedCount > 0
            )
            {
                MarkHeightOutputsDirty(
                    addressablesConfigurationDirty
                );
            }

            TerrainRuntimeHeightCompileOutcome outcome =
                cancelled
                    ? TerrainRuntimeHeightCompileOutcome.Cancelled
                    : TerrainRuntimeHeightCompileOutcome.Failed;

            return
                new TerrainRuntimeHeightCompileResult(
                    outcome,
                    workMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings.heightmapGenerationRevision,
                    firstError,
                    cancelled
                        ? "Runtime height compilation was cancelled. " +
                          "Successfully committed incremental tiles remain " +
                          "usable and acknowledged."
                        : "Runtime height compilation did not complete. " +
                          "Successfully committed incremental tiles remain " +
                          "usable when the manifest stayed structurally valid."
                );
        }

        if (
            succeededTiles.Count !=
            requestedTiles.Count
        )
        {
            return
                new TerrainRuntimeHeightCompileResult(
                    TerrainRuntimeHeightCompileOutcome.Failed,
                    workMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings.heightmapGenerationRevision,
                    "Not every requested runtime height tile completed.",
                    "The runtime height dataset was not finalized."
                );
        }

        if (
            enforcePlanIdentity
            &&
            TerrainRuntimeBakeStateService
                .GetSnapshot()
                .StateRevision !=
                    planStateRevision
        )
        {
            MarkHeightOutputsDirty(
                addressablesConfigurationDirty
            );

            return
                new TerrainRuntimeHeightCompileResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    workMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings.heightmapGenerationRevision,
                    "Persistent bake state changed while height compilation " +
                    "was running. The generated tiles were not acknowledged " +
                    "as satisfying the newer state.",
                    "Build a new bake plan before continuing."
                );
        }

        if (
            !TargetStillCurrent(
                worldSettings,
                authoringData,
                target,
                out string targetError
            )
        )
        {
            MarkHeightOutputsDirty(
                addressablesConfigurationDirty
            );

            return
                new TerrainRuntimeHeightCompileResult(
                    TerrainRuntimeHeightCompileOutcome.StalePlan,
                    workMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings.heightmapGenerationRevision,
                    targetError,
                    "The target authoring/layout identity changed before " +
                    "height finalization. Generated tiles remain on disk but " +
                    "were not marked current."
                );
        }

        if (
            !runtimeManifest.HasCompleteTileHeightRanges
            ||
            !runtimeManifest
                .TryRecalculateGlobalHeightRange(
                    out _,
                    out _
                )
        )
        {
            runtimeManifest.isComplete =
                false;

            TrySaveManifest(
                runtimeManifest,
                out _
            );

            MarkHeightOutputsDirty(
                addressablesConfigurationDirty
            );

            return
                new TerrainRuntimeHeightCompileResult(
                    TerrainRuntimeHeightCompileOutcome.Failed,
                    workMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings.heightmapGenerationRevision,
                    "Runtime height per-tile range metadata is incomplete or " +
                    "invalid, so the dataset cannot be finalized.",
                    "A full height rebuild will be required."
                );
        }

        UpdateManifestLayoutAndSource(
            runtimeManifest,
            worldSettings,
            target
        );

        runtimeManifest.isComplete =
            true;

        if (
            !TrySaveManifest(
                runtimeManifest,
                out string finalManifestError
            )
        )
        {
            MarkHeightOutputsDirty(
                addressablesConfigurationDirty
            );

            return
                new TerrainRuntimeHeightCompileResult(
                    TerrainRuntimeHeightCompileOutcome.Failed,
                    workMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    revisionBefore,
                    worldSettings.heightmapGenerationRevision,
                    finalManifestError,
                    "Runtime height source metadata could not be finalized."
                );
        }

        TerrainGenerationStateUtility
            .MarkHeightmapsCompiled(
                worldSettings,
                target.AuthoringSignature
            );

        AssetDatabase.SaveAssets();

        if (
            workMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation()
                    .ClearAllHeightTiles()
                    .ClearFullHeight()
                    .DirtyAddressablesContent()
                    .DirtyRuntimeSceneMetadata();

            if (addressablesConfigurationDirty)
            {
                mutation
                    .DirtyAddressablesConfiguration();
            }

            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
        }
        else
        {
            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation()
                    .RemoveHeightTiles(
                        succeededTiles
                    )
                    .DirtyAddressablesContent()
                    .DirtyRuntimeSceneMetadata();

            if (addressablesConfigurationDirty)
            {
                mutation
                    .DirtyAddressablesConfiguration();
            }

            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
        }

        Selection.activeObject =
            runtimeManifest;

        return
            new TerrainRuntimeHeightCompileResult(
                TerrainRuntimeHeightCompileOutcome.Completed,
                workMode,
                requestedTiles,
                succeededTiles,
                failedTiles,
                unprocessedTiles,
                createdCount,
                updatedCount,
                removedCount,
                true,
                revisionBefore,
                worldSettings.heightmapGenerationRevision,
                "",
                workMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                    ? "Planned runtime height tiles compiled and the complete " +
                      "height dataset was finalized."
                    : "The complete runtime height dataset was rebuilt and " +
                      "finalized."
            );
    }

    // =====================================================
    // INCREMENTAL PARTIAL ACKNOWLEDGEMENT
    // =====================================================

    private static void AcknowledgePartialIncrementalSuccess(
        IReadOnlyList<Vector2Int> succeededTiles,
        bool addressablesConfigurationDirty
    )
    {
        if (
            succeededTiles == null
            ||
            succeededTiles.Count == 0
        )
        {
            return;
        }

        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation()
                .RemoveHeightTiles(
                    succeededTiles
                )
                .DirtyAddressablesContent()
                .DirtyRuntimeSceneMetadata();

        if (addressablesConfigurationDirty)
        {
            mutation
                .DirtyAddressablesConfiguration();
        }

        TerrainRuntimeBakeStateService
            .ApplyMutation(
                mutation
            );
    }

    private static void MarkHeightOutputsDirty(
        bool addressablesConfigurationDirty
    )
    {
        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation()
                .DirtyAddressablesContent()
                .DirtyRuntimeSceneMetadata();

        if (addressablesConfigurationDirty)
        {
            mutation
                .DirtyAddressablesConfiguration();
        }

        TerrainRuntimeBakeStateService
            .ApplyMutation(
                mutation
            );
    }

    // =====================================================
    // TARGET CAPTURE / REVALIDATION
    // =====================================================

    private static CompileTarget CaptureTarget(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        string authoringSignature,
        string authoringContentHash
    )
    {
        return
            new CompileTarget
            {
                AuthoringRevision =
                    authoringData.authoringRevision,

                AuthoringSignature =
                    authoringSignature,

                AuthoringContentHash =
                    authoringContentHash,

                GridWidth =
                    worldSettings.gridWidth,

                GridHeight =
                    worldSettings.gridHeight,

                ChunkSize =
                    worldSettings.chunkSize,

                HeightfieldResolutionPerChunk =
                    worldSettings.heightfieldResolutionPerChunk,

                HeightTileChunkSpan =
                    worldSettings.heightTileChunkSpan,

                HeightTileGridWidth =
                    worldSettings.HeightTileGridWidth,

                HeightTileGridHeight =
                    worldSettings.HeightTileGridHeight,

                HeightTileWorldSize =
                    worldSettings.HeightTileWorldSize,

                HeightTileSamplesPerSide =
                    worldSettings.HeightTileSamplesPerSide
            };
    }

    private static bool TargetStillCurrent(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        CompileTarget target,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            errorMessage =
                "The height compile target lost its authoring context.";

            return false;
        }

        if (
            authoringData.authoringRevision !=
                target.AuthoringRevision
            ||
            !LayoutMatchesTarget(
                worldSettings,
                target
            )
        )
        {
            errorMessage =
                "The authoring revision or heightfield layout changed while " +
                "runtime heightmaps were compiling.";

            return false;
        }

        string currentSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            !string.Equals(
                currentSignature,
                target.AuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "The overall authoring signature changed while runtime " +
                "heightmaps were compiling.";

            return false;
        }

        if (
            !TerrainAuthoringStateUtility
                .TryValidateCommittedHeightfield(
                    worldSettings,
                    authoringData,
                    out _,
                    out string currentContentHash,
                    out string validationError
                )
        )
        {
            errorMessage =
                "The committed authoring heightfield became invalid before " +
                "runtime height finalization.\n\n" +
                validationError;

            return false;
        }

        if (
            !string.Equals(
                currentContentHash,
                target.AuthoringContentHash,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "The committed authoring height content changed while runtime " +
                "heightmaps were compiling.";

            return false;
        }

        return true;
    }

    private static bool LayoutMatchesTarget(
        WorldSettings worldSettings,
        CompileTarget target
    )
    {
        return
            worldSettings.gridWidth ==
                target.GridWidth
            &&
            worldSettings.gridHeight ==
                target.GridHeight
            &&
            Mathf.Approximately(
                worldSettings.chunkSize,
                target.ChunkSize
            )
            &&
            worldSettings.heightfieldResolutionPerChunk ==
                target.HeightfieldResolutionPerChunk
            &&
            worldSettings.heightTileChunkSpan ==
                target.HeightTileChunkSpan
            &&
            worldSettings.HeightTileGridWidth ==
                target.HeightTileGridWidth
            &&
            worldSettings.HeightTileGridHeight ==
                target.HeightTileGridHeight
            &&
            Mathf.Approximately(
                worldSettings.HeightTileWorldSize,
                target.HeightTileWorldSize
            )
            &&
            worldSettings.HeightTileSamplesPerSide ==
                target.HeightTileSamplesPerSide;
    }

    // =====================================================
    // COMPILE ONE TILE
    // =====================================================

    private static TileWriteOutcome CompileOneTile(
        TerrainRuntimeHeightCompositionContext compositionContext,
        int tileX,
        int tileZ,
        int samplesPerSide,
        float[] compiledHeightData,
        out float minimumHeight,
        out float maximumHeight,
        out bool outputMayHaveChanged,
        out string errorMessage
    )
    {
        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        outputMayHaveChanged =
            false;

        errorMessage =
            "";

        if (compositionContext == null)
        {
            errorMessage =
                "Runtime height composition context is null.";

            return
                TileWriteOutcome.Failed;
        }

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        if (
            compiledHeightData == null
            ||
            compiledHeightData.Length !=
                expectedSampleCount
        )
        {
            errorMessage =
                "Runtime height compilation received an invalid reusable " +
                "sample buffer.";

            return
                TileWriteOutcome.Failed;
        }

        string sourcePath =
            TerrainAuthoringStateUtility
                .GetAuthoringHeightTilePath(
                    tileX,
                    tileZ
                );

        Texture2D sourceTexture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    sourcePath
                );

        if (sourceTexture == null)
        {
            errorMessage =
                "Authoring height tile disappeared during runtime " +
                "compilation:\n" +
                sourcePath;

            return
                TileWriteOutcome.Failed;
        }

        if (
            sourceTexture.width !=
                samplesPerSide
            ||
            sourceTexture.height !=
                samplesPerSide
        )
        {
            errorMessage =
                $"Authoring height tile ({tileX}, {tileZ}) has unexpected " +
                $"dimensions. Expected {samplesPerSide} x {samplesPerSide}, " +
                $"actual {sourceTexture.width} x {sourceTexture.height}.";

            return
                TileWriteOutcome.Failed;
        }

        Vector2Int coordinate =
            new Vector2Int(
                tileX,
                tileZ
            );

        if (
            compositionContext.RequiresComposition(
                coordinate
            )
        )
        {
            if (
                !compositionContext
                    .TryComposeCommittedTile(
                        sourceTexture,
                        coordinate,
                        compiledHeightData,
                        out string compositionError
                    )
            )
            {
                errorMessage =
                    $"Could not compose runtime height tile " +
                    $"({tileX}, {tileZ}).\n\n" +
                    compositionError;

                return
                    TileWriteOutcome.Failed;
            }
        }
        else
        {
            try
            {
                var sourceHeightData =
                    sourceTexture.GetPixelData<float>(
                        0
                    );

                if (
                    sourceHeightData.Length !=
                        expectedSampleCount
                )
                {
                    errorMessage =
                        $"Authoring height tile ({tileX}, {tileZ}) contains " +
                        "an unexpected sample count.";

                    return
                        TileWriteOutcome.Failed;
                }

                for (
                    int index = 0;
                    index < sourceHeightData.Length;
                    index++
                )
                {
                    compiledHeightData[index] =
                        sourceHeightData[index];
                }
            }
            catch (
                Exception exception
            )
            {
                errorMessage =
                    $"Could not read authoring height tile " +
                    $"({tileX}, {tileZ}).\n\n" +
                    exception.Message;

                return
                    TileWriteOutcome.Failed;
            }
        }

        for (
            int index = 0;
            index < compiledHeightData.Length;
            index++
        )
        {
            float height =
                compiledHeightData[index];

            if (!IsFinite(height))
            {
                errorMessage =
                    $"Compiled runtime height tile ({tileX}, {tileZ}) " +
                    "contains a non-finite height sample.";

                return
                    TileWriteOutcome.Failed;
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
        }

        if (
            !IsFinite(minimumHeight)
            ||
            !IsFinite(maximumHeight)
            ||
            maximumHeight < minimumHeight
        )
        {
            errorMessage =
                $"Compiled runtime height tile ({tileX}, {tileZ}) produced " +
                "an invalid height range.";

            return
                TileWriteOutcome.Failed;
        }

        return
            SaveOrUpdateRuntimeHeightTile(
                tileX,
                tileZ,
                samplesPerSide,
                compiledHeightData,
                out outputMayHaveChanged,
                out errorMessage
            );
    }

    // =====================================================
    // SAVE / UPDATE RUNTIME TILE
    // =====================================================

    private static TileWriteOutcome SaveOrUpdateRuntimeHeightTile(
        int tileX,
        int tileZ,
        int samplesPerSide,
        float[] heightData,
        out bool outputMayHaveChanged,
        out string errorMessage
    )
    {
        outputMayHaveChanged =
            false;

        errorMessage =
            "";

        string assetPath =
            TerrainRuntimeHeightAssetUtility
                .GetHeightTilePath(
                    tileX,
                    tileZ
                );

        Texture2D existingTexture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    assetPath
                );

        if (existingTexture == null)
        {
            Texture2D texture =
                null;

            try
            {
                texture =
                    new Texture2D(
                        samplesPerSide,
                        samplesPerSide,
                        TextureFormat.RFloat,
                        false,
                        true
                    );

                texture.name =
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTileName(
                            tileX,
                            tileZ
                        );

                texture.wrapMode =
                    TextureWrapMode.Clamp;

                texture.filterMode =
                    FilterMode.Point;

                texture.SetPixelData(
                    heightData,
                    0
                );

                texture.Apply(
                    false,
                    false
                );

                AssetDatabase.CreateAsset(
                    texture,
                    assetPath
                );

                outputMayHaveChanged =
                    true;

                AssetDatabase.SaveAssetIfDirty(
                    texture
                );

                return
                    TileWriteOutcome.Created;
            }
            catch (
                Exception exception
            )
            {
                outputMayHaveChanged =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            assetPath
                        )
                    !=
                    null;

                if (
                    texture != null
                    &&
                    !outputMayHaveChanged
                )
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            texture
                        );
                }

                errorMessage =
                    "Could not create runtime heightmap texture:\n" +
                    assetPath +
                    "\n\n" +
                    exception.Message;

                return
                    TileWriteOutcome.Failed;
            }
        }

        try
        {
            outputMayHaveChanged =
                true;

            bool reinitialized =
                existingTexture.Reinitialize(
                    samplesPerSide,
                    samplesPerSide,
                    TextureFormat.RFloat,
                    false
                );

            if (!reinitialized)
            {
                errorMessage =
                    "Could not reinitialize existing runtime heightmap " +
                    "texture:\n" +
                    assetPath;

                return
                    TileWriteOutcome.Failed;
            }

            existingTexture.name =
                TerrainRuntimeHeightAssetUtility
                    .GetHeightTileName(
                        tileX,
                        tileZ
                    );

            existingTexture.wrapMode =
                TextureWrapMode.Clamp;

            existingTexture.filterMode =
                FilterMode.Point;

            existingTexture.SetPixelData(
                heightData,
                0
            );

            existingTexture.Apply(
                false,
                false
            );

            EditorUtility.SetDirty(
                existingTexture
            );

            AssetDatabase.SaveAssetIfDirty(
                existingTexture
            );

            return
                TileWriteOutcome.Updated;
        }
        catch (
            Exception exception
        )
        {
            errorMessage =
                "Could not update runtime heightmap texture:\n" +
                assetPath +
                "\n\n" +
                exception.Message;

            return
                TileWriteOutcome.Failed;
        }
    }

    // =====================================================
    // MANIFEST
    // =====================================================

    private static TerrainHeightmapManifest GetOrCreateRuntimeManifest()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (manifest != null)
        {
            return manifest;
        }

        manifest =
            ScriptableObject
                .CreateInstance<TerrainHeightmapManifest>();

        manifest.name =
            "HeightmapManifest";

        AssetDatabase.CreateAsset(
            manifest,
            TerrainRuntimeHeightAssetUtility
                .HeightmapManifestPath
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return manifest;
    }

    private static void PrepareManifestForFullRebuild(
        TerrainHeightmapManifest runtimeManifest,
        WorldSettings worldSettings
    )
    {
        UpdateManifestLayout(
            runtimeManifest,
            worldSettings
        );

        runtimeManifest.compilerVersion =
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion;

        runtimeManifest.isComplete =
            false;

        runtimeManifest.InitializeTileHeightRanges(
            worldSettings.HeightTileGridWidth,
            worldSettings.HeightTileGridHeight
        );
    }

    private static void UpdateManifestLayoutAndSource(
        TerrainHeightmapManifest runtimeManifest,
        WorldSettings worldSettings,
        CompileTarget target
    )
    {
        UpdateManifestLayout(
            runtimeManifest,
            worldSettings
        );

        runtimeManifest.compilerVersion =
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion;

        runtimeManifest.sourceAuthoringRevision =
            target.AuthoringRevision;

        runtimeManifest.sourceAuthoringSignature =
            target.AuthoringSignature;

        runtimeManifest.sourceAuthoringContentHash =
            target.AuthoringContentHash;
    }

    private static void UpdateManifestLayout(
        TerrainHeightmapManifest runtimeManifest,
        WorldSettings worldSettings
    )
    {
        runtimeManifest.gridWidth =
            worldSettings.gridWidth;

        runtimeManifest.gridHeight =
            worldSettings.gridHeight;

        runtimeManifest.chunkSize =
            worldSettings.chunkSize;

        runtimeManifest.heightfieldResolutionPerChunk =
            worldSettings.heightfieldResolutionPerChunk;

        runtimeManifest.heightTileChunkSpan =
            worldSettings.heightTileChunkSpan;

        runtimeManifest.heightTileGridWidth =
            worldSettings.HeightTileGridWidth;

        runtimeManifest.heightTileGridHeight =
            worldSettings.HeightTileGridHeight;

        runtimeManifest.heightTileWorldSize =
            worldSettings.HeightTileWorldSize;

        runtimeManifest.heightTileSamplesPerSide =
            worldSettings.HeightTileSamplesPerSide;
    }

    private static bool ValidateIncrementalManifest(
        TerrainHeightmapManifest runtimeManifest,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (runtimeManifest == null)
        {
            errorMessage =
                "Incremental runtime height compilation requires an existing " +
                "heightmap manifest. Build a new bake plan; a full rebuild is " +
                "required.";

            return false;
        }

        if (!runtimeManifest.isComplete)
        {
            errorMessage =
                "The runtime heightmap manifest is structurally incomplete. " +
                "Incremental height compilation is unsafe; a full rebuild is " +
                "required.";

            return false;
        }

        if (
            runtimeManifest.compilerVersion !=
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion
        )
        {
            errorMessage =
                "The runtime height compiler version changed. Incremental " +
                "height compilation is unsafe; a full rebuild is required.";

            return false;
        }

        if (
            !ManifestLayoutMatchesWorld(
                runtimeManifest,
                worldSettings
            )
        )
        {
            errorMessage =
                "The runtime heightmap manifest layout does not match current " +
                "WorldSettings. A full rebuild is required.";

            return false;
        }

        if (
            !runtimeManifest.HasCompleteTileHeightRanges
            ||
            !runtimeManifest.HasValidHeightRange
        )
        {
            errorMessage =
                "The runtime heightmap manifest does not contain complete " +
                "per-tile range metadata. A full rebuild is required.";

            return false;
        }

        return true;
    }

    private static bool ManifestLayoutMatchesWorld(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            ||
            worldSettings == null
        )
        {
            return false;
        }

        return
            manifest.gridWidth ==
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
            &&
            manifest.gridHeight ==
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                )
            &&
            Mathf.Approximately(
                manifest.chunkSize,
                Mathf.Max(
                    0.01f,
                    worldSettings.chunkSize
                )
            )
            &&
            manifest.heightfieldResolutionPerChunk ==
                Mathf.Max(
                    1,
                    worldSettings.heightfieldResolutionPerChunk
                )
            &&
            manifest.heightTileChunkSpan ==
                Mathf.Max(
                    1,
                    worldSettings.heightTileChunkSpan
                )
            &&
            manifest.heightTileGridWidth ==
                worldSettings.HeightTileGridWidth
            &&
            manifest.heightTileGridHeight ==
                worldSettings.HeightTileGridHeight
            &&
            Mathf.Approximately(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            )
            &&
            manifest.heightTileSamplesPerSide ==
                worldSettings.HeightTileSamplesPerSide;
    }

    private static bool TrySaveManifest(
        TerrainHeightmapManifest manifest,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (manifest == null)
        {
            errorMessage =
                "Runtime heightmap manifest is null.";

            return false;
        }

        try
        {
            EditorUtility.SetDirty(
                manifest
            );

            AssetDatabase.SaveAssetIfDirty(
                manifest
            );

            return true;
        }
        catch (
            Exception exception
        )
        {
            errorMessage =
                "Could not save the runtime heightmap manifest.\n\n" +
                exception.Message;

            return false;
        }
    }

    // =====================================================
    // EXISTING GENERATED TILES - FULL REBUILD ONLY
    // =====================================================

    private static Dictionary<Vector2Int, Texture2D>
        FindExistingRuntimeHeightTiles()
    {
        Dictionary<Vector2Int, Texture2D> tiles =
            new Dictionary<Vector2Int, Texture2D>();

        if (
            !AssetDatabase.IsValidFolder(
                TerrainRuntimeHeightAssetUtility
                    .HeightmapTileFolder
            )
        )
        {
            return tiles;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Texture2D",
                new[]
                {
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapTileFolder
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
                !TerrainRuntimeHeightAssetUtility
                    .TryGetHeightTileCoordinates(
                        path,
                        out int tileX,
                        out int tileZ
                    )
            )
            {
                continue;
            }

            Texture2D texture =
                AssetDatabase
                    .LoadAssetAtPath<Texture2D>(
                        path
                    );

            if (texture == null)
            {
                continue;
            }

            tiles[
                new Vector2Int(
                    tileX,
                    tileZ
                )
            ] =
                texture;
        }

        return tiles;
    }

    // =====================================================
    // FOLDERS / PROGRESS / RESULT HELPERS
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
                WorldMeshesPaths.GeneratedHeightmaps
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Generated,
                "Heightmaps"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.HeightmapTiles
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.GeneratedHeightmaps,
                "Tiles"
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
                    "Compile Runtime Terrain Heightmaps",
                    operation +
                    "\n\n" +
                    detail,
                    progress
                );
    }

    private static void AddRemainingCoordinates(
        IReadOnlyList<Vector2Int> source,
        int startIndex,
        ICollection<Vector2Int> output
    )
    {
        if (
            source == null
            ||
            output == null
        )
        {
            return;
        }

        for (
            int index = Mathf.Max(0, startIndex);
            index < source.Count;
            index++
        )
        {
            output.Add(
                source[index]
            );
        }
    }

    private static TerrainRuntimeHeightCompileResult
        CreateTerminalResult(
            TerrainRuntimeHeightCompileOutcome outcome,
            TerrainRuntimeBakeWorkMode workMode,
            int revision,
            string message
        )
    {
        return
            new TerrainRuntimeHeightCompileResult(
                outcome,
                workMode,
                null,
                null,
                null,
                null,
                0,
                0,
                0,
                false,
                revision,
                revision,
                message,
                message
            );
    }

    private static void LogResult(
        TerrainRuntimeHeightCompileResult result
    )
    {
        if (result == null)
        {
            Debug.LogError(
                "Runtime height compilation returned no result."
            );

            return;
        }

        string report =
            result.BuildDiagnosticReport();

        switch (result.Outcome)
        {
            case TerrainRuntimeHeightCompileOutcome.Completed:
            case TerrainRuntimeHeightCompileOutcome.NoWork:
                Debug.Log(
                    report
                );
                break;

            case TerrainRuntimeHeightCompileOutcome.Cancelled:
                Debug.LogWarning(
                    report
                );
                break;

            default:
                Debug.LogError(
                    report
                );
                break;
        }
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
            return yComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }
}
