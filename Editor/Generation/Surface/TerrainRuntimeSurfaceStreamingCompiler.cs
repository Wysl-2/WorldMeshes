using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeSurfaceStreamingCompiler
{
    private const int PersistenceBatchFamilyCount =
        4;

    private static bool isGenerating;

    public static bool IsGenerating =>
        isGenerating;

    public static TerrainRuntimeSurfaceStreamingCompileResult RebuildAllSurfaceStreaming(
        WorldSettings worldSettings
    )
    {
        return CompileInternal(
            worldSettings,
            TerrainRuntimeBakeWorkMode.Full,
            null,
            null,
            false
        );
    }

    public static TerrainRuntimeSurfaceStreamingCompileResult CompileFullSurfaceStreamingWork(
        WorldSettings worldSettings
    )
    {
        return CompileInternal(
            worldSettings,
            TerrainRuntimeBakeWorkMode.Full,
            null,
            null,
            true
        );
    }

    public static TerrainRuntimeSurfaceStreamingCompileResult CompilePlannedSurfaceStreamingWork(
        WorldSettings worldSettings,
        TerrainRuntimeBakePlan plan
    )
    {
        TerrainSurfaceMaskManifest manifest =
            LoadManifest();

        int revision =
            manifest != null
                ? Mathf.Max(0, manifest.streamingGenerationRevision)
                : 0;

        if (plan == null)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                TerrainRuntimeBakeWorkMode.None,
                revision,
                "Surface Streaming compilation received a null bake plan."
            );
        }

        if (plan.IsBlocked)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                plan.SurfaceStreamingWorkMode,
                revision,
                string.IsNullOrEmpty(plan.BlockReason)
                    ? "The runtime bake plan is blocked."
                    : plan.BlockReason
            );
        }

        if (
            plan.SurfaceStreamingWorkMode ==
                TerrainRuntimeBakeWorkMode.None
        )
        {
            return new TerrainRuntimeSurfaceStreamingCompileResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.NoWork,
                TerrainRuntimeBakeWorkMode.None,
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
                "",
                "No Surface Streaming work is required."
            );
        }

        TerrainRuntimeBakeStateSummary summary =
            TerrainRuntimeBakeStateService.GetSummary();

        if (
            summary.StateRevision !=
                plan.SourceStateRevision
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.StalePlan,
                plan.SurfaceStreamingWorkMode,
                revision,
                "Persistent runtime bake state changed after the Surface Streaming plan was created."
            );
        }

        return CompileInternal(
            worldSettings,
            plan.SurfaceStreamingWorkMode,
            plan.SurfaceStreamingTiles,
            plan,
            true
        );
    }

    private static TerrainRuntimeSurfaceStreamingCompileResult CompileInternal(
        WorldSettings worldSettings,
        TerrainRuntimeBakeWorkMode workMode,
        IReadOnlyList<Vector2Int> plannedCoordinates,
        TerrainRuntimeBakePlan sourcePlan,
        bool allowPipelineOwnership
    )
    {
        TerrainSurfaceMaskManifest manifest =
            LoadManifest();

        int revisionBefore =
            manifest != null
                ? Mathf.Max(0, manifest.streamingGenerationRevision)
                : 0;

        if (isGenerating)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                "Surface Streaming generation is already in progress."
            );
        }

        if (workMode == TerrainRuntimeBakeWorkMode.None)
        {
            return new TerrainRuntimeSurfaceStreamingCompileResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.NoWork,
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
                "No Surface Streaming work is required."
            );
        }

        if (worldSettings == null)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                "WorldSettings is unavailable."
            );
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                "Surface Streaming generation must run outside Play Mode."
            );
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            bool pipelineOwnsCall =
                allowPipelineOwnership
                && TerrainRuntimeBakePipeline.CurrentState ==
                    TerrainRuntimeBakePipelineState.SurfaceStreaming;

            if (!pipelineOwnsCall)
            {
                return CreateTerminalResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                    workMode,
                    revisionBefore,
                    "Surface Streaming generation is unavailable while another unified Runtime Bake stage is running."
                );
            }
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                "Surface Streaming generation is unavailable while authoritative Surface generation is running."
            );
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(worldSettings) !=
                TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                "Authoritative runtime Surface masks are not current. Rebuild Surface Masks first."
            );
        }

        if (
            manifest == null
            || !manifest.isComplete
            || manifest.compilerVersion !=
                TerrainSurfaceMaskManifest.CurrentCompilerVersion
            || manifest.channelLayoutVersion !=
                TerrainSurfaceMaskManifest.CurrentChannelLayoutVersion
            || manifest.surfaceMaskGenerationRevision <= 0
            || string.IsNullOrEmpty(manifest.surfaceGenerationSignature)
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                "The authoritative Surface manifest is missing, incomplete, or incompatible."
            );
        }

        if (
            !TerrainGenerationStateUtility.TryBuildCurrentSurfaceStreamingTarget(
                worldSettings,
                manifest,
                out List<TerrainSurfaceStreamingLevelDescriptor> targetDescriptors,
                out string targetSignature,
                out string targetError
            )
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                workMode,
                revisionBefore,
                targetError
            );
        }

        if (
            workMode == TerrainRuntimeBakeWorkMode.Incremental
            && !TerrainGenerationStateUtility.IsSurfaceStreamingTargetMetadataCompatible(
                manifest,
                worldSettings
            )
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.StalePlan,
                workMode,
                revisionBefore,
                "The existing Surface Streaming pyramid is not compatible with incremental repair. A full rebuild is required."
            );
        }

        List<Vector2Int> requestedFamilies =
            BuildRequestedCoordinates(
                manifest,
                workMode,
                plannedCoordinates,
                targetDescriptors.Count > 0,
                out string coordinateError
            );

        if (requestedFamilies == null)
        {
            RequireFullRepair();

            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.StalePlan,
                workMode,
                revisionBefore,
                coordinateError
            );
        }

        if (
            !TerrainSurfaceStreamingAssetLifecycleUtility.EnsureTargetFolders(
                targetDescriptors,
                out string folderError
            )
        )
        {
            if (workMode == TerrainRuntimeBakeWorkMode.Full)
            {
                RequireFullRepair();
            }

            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                workMode,
                revisionBefore,
                folderError
            );
        }

        int sourceSurfaceRevision =
            manifest.surfaceMaskGenerationRevision;

        string sourceSurfaceSignature =
            manifest.surfaceGenerationSignature;

        long expectedStateRevision =
            sourcePlan != null
                ? sourcePlan.SourceStateRevision
                : TerrainRuntimeBakeStateService.GetSummary().StateRevision;

        if (workMode == TerrainRuntimeBakeWorkMode.Full)
        {
            if (!manifest.TrySetStreamingLevelDescriptors(targetDescriptors))
            {
                RequireFullRepair();

                return CreateTerminalResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    workMode,
                    revisionBefore,
                    "The Surface manifest rejected the target streaming descriptor set."
                );
            }

            manifest.streamingPyramidIsComplete = false;
            manifest.streamingPyramidCompilerVersion =
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion;
            manifest.streamingPyramidPolicyVersion =
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion;
            manifest.streamingSourceSurfaceMaskGenerationRevision = -1;
            manifest.streamingSourceSurfaceGenerationSignature = "";
            manifest.streamingGenerationSignature = targetSignature;

            if (!TrySaveManifest(manifest, out string manifestPrepareError))
            {
                RequireFullRepair();

                return CreateTerminalResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    workMode,
                    revisionBefore,
                    manifestPrepareError
                );
            }
        }

        Dictionary<int, byte[]> reusableDerivedBuffers =
            new Dictionary<int, byte[]>();

        List<Texture2D> dirtyOutputs =
            new List<Texture2D>();

        List<Vector2Int> succeededFamilies =
            new List<Vector2Int>();

        List<Vector2Int> failedFamilies =
            new List<Vector2Int>();

        List<Vector2Int> unprocessedFamilies =
            new List<Vector2Int>();

        List<TerrainSurfaceStreamingFamilyWriteResult> preparedBatch =
            new List<TerrainSurfaceStreamingFamilyWriteResult>();

        int createdAssetCount = 0;
        int updatedAssetCount = 0;
        int removedAssetCount = 0;
        bool cancelled = false;
        bool stalePlan = false;
        bool staleSource = false;
        bool persistenceFailed = false;
        string errorMessage = "";

        isGenerating = true;

        try
        {
            for (
                int requestIndex = 0;
                requestIndex < requestedFamilies.Count;
                requestIndex++
            )
            {
                if (preparedBatch.Count == 0)
                {
                    if (
                        sourcePlan != null
                        && TerrainRuntimeBakeStateService.GetSummary().StateRevision !=
                            expectedStateRevision
                    )
                    {
                        stalePlan = true;
                        errorMessage =
                            "Persistent runtime bake state changed while Surface Streaming generation was running.";
                        AddRemainingCoordinates(
                            requestedFamilies,
                            requestIndex,
                            unprocessedFamilies
                        );
                        break;
                    }

                    bool userCancelled =
                        TerrainRuntimeBakePipeline.CancelRequested
                        || EditorUtility.DisplayCancelableProgressBar(
                            workMode == TerrainRuntimeBakeWorkMode.Full
                                ? "Rebuild Surface Streaming"
                                : "Compile Surface Streaming",
                            $"Surface streaming family {requestIndex + 1} / {requestedFamilies.Count}",
                            requestedFamilies.Count > 0
                                ? (float)requestIndex / requestedFamilies.Count
                                : 1f
                        );

                    if (userCancelled)
                    {
                        cancelled = true;
                        AddRemainingCoordinates(
                            requestedFamilies,
                            requestIndex,
                            unprocessedFamilies
                        );
                        break;
                    }
                }

                if (
                    !SourceStillCurrent(
                        worldSettings,
                        manifest,
                        sourceSurfaceRevision,
                        sourceSurfaceSignature
                    )
                )
                {
                    staleSource = true;
                    errorMessage =
                        "Authoritative Surface generation changed while Surface Streaming generation was running.";
                    AddRemainingCoordinates(
                        requestedFamilies,
                        requestIndex,
                        unprocessedFamilies
                    );
                    break;
                }

                Vector2Int coordinate =
                    requestedFamilies[requestIndex];

                Texture2D nativeTexture =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(
                            coordinate.x,
                            coordinate.y
                        )
                    );

                TerrainSurfaceStreamingFamilyWriteResult familyResult;

                try
                {
                    familyResult =
                        TerrainSurfaceStreamingPyramidGenerator
                            .PrepareFamilyFromExistingNativeTexture(
                                worldSettings,
                                manifest,
                                coordinate,
                                targetDescriptors,
                                nativeTexture,
                                reusableDerivedBuffers,
                                dirtyOutputs
                            );
                }
                finally
                {
                    if (nativeTexture != null)
                    {
                        Resources.UnloadAsset(nativeTexture);
                    }
                }

                createdAssetCount +=
                    familyResult != null
                        ? familyResult.CreatedAssetCount
                        : 0;

                updatedAssetCount +=
                    familyResult != null
                        ? familyResult.UpdatedAssetCount
                        : 0;

                bool prepared =
                    familyResult != null
                    && familyResult.Attempted
                    && familyResult.PreparedComplete;

                if (!prepared)
                {
                    failedFamilies.Add(coordinate);
                    errorMessage =
                        familyResult != null
                            ? familyResult.ErrorMessage
                            : "Surface Streaming generation returned no family result.";

                    AddRemainingCoordinates(
                        requestedFamilies,
                        requestIndex + 1,
                        unprocessedFamilies
                    );

                    if (
                        !PersistPreparedBatch(
                            preparedBatch,
                            dirtyOutputs,
                            workMode,
                            sourcePlan,
                            ref expectedStateRevision,
                            succeededFamilies,
                            out string persistenceError
                        )
                    )
                    {
                        persistenceFailed = true;
                        errorMessage = persistenceError;
                    }

                    break;
                }

                preparedBatch.Add(familyResult);

                bool batchBoundaryReached =
                    preparedBatch.Count >= PersistenceBatchFamilyCount
                    || requestIndex == requestedFamilies.Count - 1;

                if (batchBoundaryReached)
                {
                    if (
                        !PersistPreparedBatch(
                            preparedBatch,
                            dirtyOutputs,
                            workMode,
                            sourcePlan,
                            ref expectedStateRevision,
                            succeededFamilies,
                            out string persistenceError
                        )
                    )
                    {
                        persistenceFailed = true;
                        errorMessage = persistenceError;
                        AddRemainingCoordinates(
                            requestedFamilies,
                            requestIndex + 1,
                            unprocessedFamilies
                        );
                        break;
                    }
                }
            }

            if (persistenceFailed)
            {
                RequireFullRepair();

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    errorMessage,
                    "Surface Streaming durability could not be proven. A full derived-data repair is required."
                );
            }

            if (cancelled)
            {
                if (workMode == TerrainRuntimeBakeWorkMode.Full)
                {
                    RequireFullRepair();
                }

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Cancelled,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "",
                    workMode == TerrainRuntimeBakeWorkMode.Full
                        ? "Surface Streaming generation was cancelled. The full rebuild remains pending."
                        : "Surface Streaming generation was cancelled at a durability-safe batch boundary. Completed families were preserved."
                );
            }

            if (stalePlan)
            {
                if (workMode == TerrainRuntimeBakeWorkMode.Full)
                {
                    RequireFullRepair();
                }

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.StalePlan,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    errorMessage,
                    "Surface Streaming generation stopped because persistent bake state changed."
                );
            }

            if (staleSource)
            {
                RequireFullRepair();

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.StaleSource,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    errorMessage,
                    "The captured native Surface source changed before Surface Streaming could be finalized."
                );
            }

            if (failedFamilies.Count > 0)
            {
                if (workMode == TerrainRuntimeBakeWorkMode.Full)
                {
                    RequireFullRepair();
                }

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    errorMessage,
                    workMode == TerrainRuntimeBakeWorkMode.Full
                        ? "Surface Streaming generation failed. The full rebuild remains pending."
                        : "One or more Surface Streaming families failed. Durable successful families were preserved and the failed work remains pending."
                );
            }

            if (
                sourcePlan != null
                && TerrainRuntimeBakeStateService.GetSummary().StateRevision !=
                    expectedStateRevision
            )
            {
                if (workMode == TerrainRuntimeBakeWorkMode.Full)
                {
                    RequireFullRepair();
                }

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.StalePlan,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "Persistent runtime bake state changed before Surface Streaming finalization.",
                    "The derived data was not published as current."
                );
            }

            if (
                !SourceStillCurrent(
                    worldSettings,
                    manifest,
                    sourceSurfaceRevision,
                    sourceSurfaceSignature
                )
            )
            {
                RequireFullRepair();

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.StaleSource,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "Authoritative Surface generation changed before Surface Streaming finalization.",
                    "The derived data was not published as current."
                );
            }

            if (
                workMode == TerrainRuntimeBakeWorkMode.Incremental
                && !TerrainGenerationStateUtility.IsSurfaceStreamingTargetMetadataCompatible(
                    manifest,
                    worldSettings
                )
            )
            {
                RequireFullRepair();

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.StalePlan,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "Surface Streaming representation metadata changed before finalization.",
                    "A full Surface Streaming repair is required."
                );
            }

            if (workMode == TerrainRuntimeBakeWorkMode.Full)
            {
                if (
                    !TerrainSurfaceStreamingAssetLifecycleUtility.DeleteObsoleteOutputs(
                        manifest,
                        targetDescriptors,
                        out removedAssetCount,
                        out string cleanupError
                    )
                )
                {
                    RequireFullRepair();

                    return CreateResult(
                        TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                        workMode,
                        requestedFamilies,
                        succeededFamilies,
                        failedFamilies,
                        unprocessedFamilies,
                        createdAssetCount,
                        updatedAssetCount,
                        removedAssetCount,
                        false,
                        revisionBefore,
                        manifest.streamingGenerationRevision,
                        cleanupError,
                        "Surface Streaming generation completed its target representations but obsolete-output cleanup failed."
                    );
                }

                AssetDatabase.SaveAssets();
            }

            if (!manifest.TrySetStreamingLevelDescriptors(targetDescriptors))
            {
                RequireFullRepair();

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "The Surface manifest rejected the final streaming descriptor set.",
                    "Surface Streaming remains pending."
                );
            }

            int previousRevision =
                Mathf.Max(0, manifest.streamingGenerationRevision);

            bool previousComplete =
                manifest.streamingPyramidIsComplete;

            int previousCompilerVersion =
                manifest.streamingPyramidCompilerVersion;

            int previousPolicyVersion =
                manifest.streamingPyramidPolicyVersion;

            int previousSourceRevision =
                manifest.streamingSourceSurfaceMaskGenerationRevision;

            string previousSourceSignature =
                manifest.streamingSourceSurfaceGenerationSignature;

            string previousGenerationSignature =
                manifest.streamingGenerationSignature;

            manifest.streamingPyramidCompilerVersion =
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion;
            manifest.streamingPyramidPolicyVersion =
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion;
            manifest.streamingSourceSurfaceMaskGenerationRevision =
                sourceSurfaceRevision;
            manifest.streamingSourceSurfaceGenerationSignature =
                sourceSurfaceSignature;
            manifest.streamingGenerationSignature =
                targetSignature;
            manifest.streamingGenerationRevision =
                previousRevision + 1;
            manifest.streamingPyramidIsComplete = true;

            if (!TrySaveManifest(manifest, out string finalizationError))
            {
                manifest.streamingPyramidIsComplete =
                    workMode == TerrainRuntimeBakeWorkMode.Incremental
                        ? previousComplete
                        : false;
                manifest.streamingPyramidCompilerVersion =
                    previousCompilerVersion;
                manifest.streamingPyramidPolicyVersion =
                    previousPolicyVersion;
                manifest.streamingSourceSurfaceMaskGenerationRevision =
                    workMode == TerrainRuntimeBakeWorkMode.Incremental
                        ? previousSourceRevision
                        : -1;
                manifest.streamingSourceSurfaceGenerationSignature =
                    workMode == TerrainRuntimeBakeWorkMode.Incremental
                        ? previousSourceSignature
                        : "";
                manifest.streamingGenerationSignature =
                    previousGenerationSignature;
                manifest.streamingGenerationRevision =
                    previousRevision;

                TrySaveManifest(manifest, out _);
                RequireFullRepair();

                return CreateResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    workMode,
                    requestedFamilies,
                    succeededFamilies,
                    failedFamilies,
                    unprocessedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    previousRevision,
                    finalizationError,
                    "Derived Surface assets were written, but Surface Streaming manifest finalization failed."
                );
            }

            TerrainRuntimeBakeStateMutation completionMutation =
                new TerrainRuntimeBakeStateMutation()
                    .DirtyAddressablesContent();

            if (workMode == TerrainRuntimeBakeWorkMode.Full)
            {
                completionMutation
                    .ClearAllSurfaceStreamingTiles()
                    .ClearFullSurfaceStreaming();
            }
            else
            {
                completionMutation.RemoveSurfaceStreamingTiles(
                    succeededFamilies
                );
            }

            TerrainRuntimeBakeStateService.ApplyMutation(
                completionMutation
            );

            return CreateResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Completed,
                workMode,
                requestedFamilies,
                succeededFamilies,
                failedFamilies,
                unprocessedFamilies,
                createdAssetCount,
                updatedAssetCount,
                removedAssetCount,
                true,
                revisionBefore,
                manifest.streamingGenerationRevision,
                "",
                workMode == TerrainRuntimeBakeWorkMode.Full
                    ? "The complete Surface Streaming pyramid was rebuilt and finalized."
                    : "Planned Surface Streaming families were repaired and the derived dataset was finalized against the current native Surface generation."
            );
        }
        catch (Exception exception)
        {
            if (dirtyOutputs.Count > 0)
            {
                try
                {
                    AssetDatabase.SaveAssets();
                    ReleasePersistedOutputs(dirtyOutputs);
                }
                catch
                {
                    // Dirty Editor assets may remain resident when durability is uncertain.
                }
            }

            RequireFullRepair();

            return CreateResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                workMode,
                requestedFamilies,
                succeededFamilies,
                failedFamilies,
                unprocessedFamilies,
                createdAssetCount,
                updatedAssetCount,
                removedAssetCount,
                false,
                revisionBefore,
                manifest != null
                    ? manifest.streamingGenerationRevision
                    : revisionBefore,
                "Surface Streaming generation failed with an exception.\n\n" +
                exception.Message,
                "Authoritative Surface data remains valid; the derived Surface Streaming dataset requires repair."
            );
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isGenerating = false;
        }
    }

    private static TerrainSurfaceMaskManifest LoadManifest()
    {
        return AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
            TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
        );
    }

    private static List<Vector2Int> BuildRequestedCoordinates(
        TerrainSurfaceMaskManifest manifest,
        TerrainRuntimeBakeWorkMode workMode,
        IReadOnlyList<Vector2Int> plannedCoordinates,
        bool physicalDerivedDataRequired,
        out string errorMessage
    )
    {
        errorMessage = "";

        List<Vector2Int> output =
            new List<Vector2Int>();

        if (!physicalDerivedDataRequired)
        {
            return output;
        }

        if (manifest == null)
        {
            errorMessage =
                "The Surface manifest is unavailable while building Surface Streaming work coordinates.";
            return null;
        }

        if (workMode == TerrainRuntimeBakeWorkMode.Full)
        {
            for (int tileZ = 0; tileZ < manifest.tileGridHeight; tileZ++)
            {
                for (int tileX = 0; tileX < manifest.tileGridWidth; tileX++)
                {
                    output.Add(new Vector2Int(tileX, tileZ));
                }
            }

            return output;
        }

        if (plannedCoordinates == null)
        {
            errorMessage =
                "Incremental Surface Streaming work has no planned coordinates.";
            return null;
        }

        HashSet<Vector2Int> unique =
            new HashSet<Vector2Int>();

        for (int index = 0; index < plannedCoordinates.Count; index++)
        {
            Vector2Int coordinate =
                plannedCoordinates[index];

            if (!manifest.IsTileCoordinateValid(coordinate.x, coordinate.y))
            {
                errorMessage =
                    "Surface Streaming work contains a coordinate outside the current Surface tile layout: " +
                    coordinate;
                return null;
            }

            unique.Add(coordinate);
        }

        output.AddRange(unique);
        output.Sort(CompareCoordinates);

        return output;
    }

    private static bool PersistPreparedBatch(
        List<TerrainSurfaceStreamingFamilyWriteResult> preparedBatch,
        List<Texture2D> dirtyOutputs,
        TerrainRuntimeBakeWorkMode workMode,
        TerrainRuntimeBakePlan sourcePlan,
        ref long expectedStateRevision,
        List<Vector2Int> succeededFamilies,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            (preparedBatch == null || preparedBatch.Count == 0)
            && (dirtyOutputs == null || dirtyOutputs.Count == 0)
        )
        {
            return true;
        }

        if (!TryPersistPreparedBatch(out errorMessage))
        {
            return false;
        }

        ReleasePersistedOutputs(dirtyOutputs);

        if (preparedBatch == null || preparedBatch.Count == 0)
        {
            return true;
        }

        List<Vector2Int> durableCoordinates =
            new List<Vector2Int>(preparedBatch.Count);

        bool outputChanged =
            false;

        for (int index = 0; index < preparedBatch.Count; index++)
        {
            TerrainSurfaceStreamingFamilyWriteResult result =
                preparedBatch[index];

            if (
                result == null
                || !result.Attempted
                || !result.PreparedComplete
            )
            {
                continue;
            }

            durableCoordinates.Add(result.Coordinate);
            outputChanged |= result.OutputMayHaveChanged;
            succeededFamilies.Add(result.Coordinate);
        }

        if (
            workMode == TerrainRuntimeBakeWorkMode.Incremental
            && sourcePlan != null
            && durableCoordinates.Count > 0
        )
        {
            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation()
                    .RemoveSurfaceStreamingTiles(
                        durableCoordinates
                    );

            if (outputChanged)
            {
                mutation.DirtyAddressablesContent();
            }

            TerrainRuntimeBakeStateService.ApplyMutation(mutation);

            expectedStateRevision =
                TerrainRuntimeBakeStateService.GetSummary().StateRevision;
        }

        preparedBatch.Clear();
        return true;
    }

    private static bool SourceStillCurrent(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        int sourceSurfaceRevision,
        string sourceSurfaceSignature
    )
    {
        return
            worldSettings != null
            && manifest != null
            && manifest.isComplete
            && TerrainGenerationStateUtility.GetSurfaceMaskStatus(worldSettings) ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && manifest.surfaceMaskGenerationRevision == sourceSurfaceRevision
            && string.Equals(
                manifest.surfaceGenerationSignature ?? "",
                sourceSurfaceSignature ?? "",
                StringComparison.Ordinal
            );
    }

    private static bool TryPersistPreparedBatch(
        out string errorMessage
    )
    {
        errorMessage = "";

        try
        {
            AssetDatabase.SaveAssets();
            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist the current Surface Streaming output batch.\n\n" +
                exception.Message;
            return false;
        }
    }

    private static void ReleasePersistedOutputs(
        List<Texture2D> dirtyOutputs
    )
    {
        if (dirtyOutputs == null)
        {
            return;
        }

        for (int index = 0; index < dirtyOutputs.Count; index++)
        {
            Texture2D texture =
                dirtyOutputs[index];

            if (texture == null)
            {
                continue;
            }

            try
            {
                Resources.UnloadAsset(texture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "WorldMeshes could not unload a persisted Surface streaming texture after a durability checkpoint.\n\n" +
                    exception.Message
                );
            }
        }

        dirtyOutputs.Clear();
    }

    private static bool TrySaveManifest(
        TerrainSurfaceMaskManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (manifest == null)
        {
            errorMessage =
                "The Surface manifest is unavailable.";
            return false;
        }

        try
        {
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssetIfDirty(manifest);
            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist Surface Streaming manifest metadata.\n\n" +
                exception.Message;
            return false;
        }
    }

    private static void RequireFullRepair()
    {
        TerrainRuntimeBakeStateService.ApplyMutation(
            new TerrainRuntimeBakeStateMutation()
                .RequireFullSurfaceStreaming()
                .DirtyAddressablesContent()
        );
    }

    private static void AddRemainingCoordinates(
        IReadOnlyList<Vector2Int> source,
        int startIndex,
        ICollection<Vector2Int> output
    )
    {
        if (source == null || output == null)
        {
            return;
        }

        for (
            int index = Mathf.Max(0, startIndex);
            index < source.Count;
            index++
        )
        {
            output.Add(source[index]);
        }
    }

    private static TerrainRuntimeSurfaceStreamingCompileResult CreateTerminalResult(
        TerrainRuntimeSurfaceStreamingCompileOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        int revision,
        string errorMessage
    )
    {
        return new TerrainRuntimeSurfaceStreamingCompileResult(
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
            errorMessage,
            ""
        );
    }

    private static TerrainRuntimeSurfaceStreamingCompileResult CreateResult(
        TerrainRuntimeSurfaceStreamingCompileOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        IEnumerable<Vector2Int> requestedFamilies,
        IEnumerable<Vector2Int> succeededFamilies,
        IEnumerable<Vector2Int> failedFamilies,
        IEnumerable<Vector2Int> unprocessedFamilies,
        int createdAssetCount,
        int updatedAssetCount,
        int removedAssetCount,
        bool datasetFinalized,
        int revisionBefore,
        int revisionAfter,
        string errorMessage,
        string summaryMessage
    )
    {
        return new TerrainRuntimeSurfaceStreamingCompileResult(
            outcome,
            workMode,
            requestedFamilies,
            succeededFamilies,
            failedFamilies,
            unprocessedFamilies,
            createdAssetCount,
            updatedAssetCount,
            removedAssetCount,
            datasetFinalized,
            revisionBefore,
            revisionAfter,
            errorMessage,
            summaryMessage
        );
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison =
            left.y.CompareTo(right.y);

        return yComparison != 0
            ? yComparison
            : left.x.CompareTo(right.x);
    }
}
