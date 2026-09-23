using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeHeightStreamingCompiler
{
    private const int HeightStreamingPersistenceBatchFamilyCount =
        4;

    public static TerrainRuntimeHeightStreamingCompileResult
        CompilePlannedHeightStreamingWork(
            WorldSettings worldSettings,
            TerrainRuntimeBakePlan plan
        )
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        int revisionBefore =
            manifest != null
                ? Mathf.Max(
                    0,
                    manifest.streamingGenerationRevision
                )
                : 0;

        if (plan == null)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.StalePlan,
                    TerrainRuntimeBakeWorkMode.None,
                    revisionBefore,
                    "Height Streaming compilation received a null bake plan."
                );
        }

        if (plan.IsBlocked)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.Blocked,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    plan.BlockReason
                );
        }

        if (
            plan.HeightStreamingWorkMode ==
                TerrainRuntimeBakeWorkMode.None
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.NoWork,
                    TerrainRuntimeBakeWorkMode.None,
                    revisionBefore,
                    "No Height Streaming work is required."
                );
        }

        if (worldSettings == null)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.Blocked,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    "WorldSettings is unavailable."
                );
        }

        TerrainRuntimeBakeStateSummary initialState =
            TerrainRuntimeBakeStateService
                .GetSummary();

        if (
            initialState.StateRevision !=
                plan.SourceStateRevision
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.StalePlan,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    "Persistent runtime bake state changed after the Height Streaming plan was created."
                );
        }

        TerrainGenerationStateEvaluationContext generationState =
            new TerrainGenerationStateEvaluationContext(
                worldSettings,
                TerrainGenerationStateEvaluationMode.Operational
            );

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    generationState
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.Blocked,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    "Standalone Height Streaming generation requires the authoritative runtime height dataset to be current."
                );
        }

        if (
            manifest == null
            ||
            !manifest.isComplete
            ||
            manifest.compilerVersion !=
                TerrainGenerationStateUtility
                    .RuntimeHeightCompilerVersion
        )
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.Blocked,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    "The authoritative runtime height manifest is unavailable or incompatible."
                );
        }

        List<Vector2Int> requestedTiles =
            BuildRequestedCoordinates(
                worldSettings,
                plan,
                out string coordinateError
            );

        if (requestedTiles == null)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.StalePlan,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    coordinateError
                );
        }

        if (requestedTiles.Count == 0)
        {
            return
                CreateTerminalResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.NoWork,
                    plan.HeightStreamingWorkMode,
                    revisionBefore,
                    "The Height Streaming plan contains no coordinates."
                );
        }

        TerrainHeightStreamingCompileContext compileContext =
            TerrainHeightStreamingCompileContext.Create(
                worldSettings,
                manifest,
                plan.HeightStreamingWorkMode,
                requestedTiles.Count
            );

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        float[] reusableNativeBuffer =
            new float[
                samplesPerSide *
                samplesPerSide
            ];

        List<Vector2Int> succeededTiles =
            new List<Vector2Int>();

        List<Vector2Int> failedTiles =
            new List<Vector2Int>();

        List<Vector2Int> unprocessedTiles =
            new List<Vector2Int>();

        List<TerrainHeightStreamingFamilyWriteResult>
            preparedBatch =
                new List<TerrainHeightStreamingFamilyWriteResult>(
                    HeightStreamingPersistenceBatchFamilyCount
                );

        long expectedStateRevision =
            plan.SourceStateRevision;

        bool cancelled =
            false;

        bool staleDuringGeneration =
            false;

        string firstError =
            "";

        try
        {
            for (
                int requestIndex = 0;
                requestIndex < requestedTiles.Count;
                requestIndex++
            )
            {
                if (preparedBatch.Count == 0)
                {
                    cancelled =
                        EditorUtility
                            .DisplayCancelableProgressBar(
                                "Compile Runtime Height Streaming",
                                $"Height streaming families " +
                                $"{requestIndex + 1} - " +
                                $"{Mathf.Min(requestIndex + HeightStreamingPersistenceBatchFamilyCount, requestedTiles.Count)} " +
                                $"/ {requestedTiles.Count}",
                                requestedTiles.Count > 0
                                    ? (float)requestIndex /
                                      requestedTiles.Count
                                    : 1f
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
                }

                TerrainRuntimeBakeStateSummary currentState =
                    TerrainRuntimeBakeStateService
                        .GetSummary();

                if (
                    currentState.StateRevision !=
                        expectedStateRevision
                )
                {
                    staleDuringGeneration =
                        true;

                    firstError =
                        "Persistent runtime bake state changed while Height Streaming generation was running.";

                    AddRemainingCoordinates(
                        requestedTiles,
                        requestIndex,
                        unprocessedTiles
                    );

                    break;
                }

                Vector2Int coordinate =
                    requestedTiles[requestIndex];

                string nativePath =
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTilePath(
                            coordinate.x,
                            coordinate.y
                        );

                Texture2D nativeTexture =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            nativePath
                        );

                TerrainHeightStreamingFamilyWriteResult family =
                    compileContext
                        .PrepareFamilyFromExistingNativeTexture(
                            coordinate,
                            nativeTexture,
                            reusableNativeBuffer
                        );

                preparedBatch.Add(
                    family
                );

                bool batchBoundaryReached =
                    preparedBatch.Count >=
                        HeightStreamingPersistenceBatchFamilyCount
                    ||
                    requestIndex ==
                        requestedTiles.Count - 1;

                if (!batchBoundaryReached)
                {
                    continue;
                }

                if (
                    !TryPersistPreparedBatch(
                        out string persistenceError
                    )
                )
                {
                    firstError =
                        persistenceError;

                    AddRemainingCoordinates(
                        requestedTiles,
                        requestIndex + 1,
                        unprocessedTiles
                    );

                    RequireFullRepair();

                    break;
                }

                compileContext.RecordDurableBatch(
                    preparedBatch
                );

                List<Vector2Int> durableSuccessfulCoordinates =
                    new List<Vector2Int>();

                for (
                    int batchIndex = 0;
                    batchIndex < preparedBatch.Count;
                    batchIndex++
                )
                {
                    TerrainHeightStreamingFamilyWriteResult result =
                        preparedBatch[batchIndex];

                    if (
                        result != null
                        &&
                        result.Attempted
                        &&
                        result.PreparedComplete
                    )
                    {
                        succeededTiles.Add(
                            result.Coordinate
                        );

                        durableSuccessfulCoordinates.Add(
                            result.Coordinate
                        );
                    }
                    else
                    {
                        Vector2Int failedCoordinate =
                            result != null
                                ? result.Coordinate
                                : coordinate;

                        failedTiles.Add(
                            failedCoordinate
                        );

                        if (
                            string.IsNullOrEmpty(
                                firstError
                            )
                            &&
                            result != null
                        )
                        {
                            firstError =
                                result.ErrorMessage;
                        }
                    }
                }

                if (
                    plan.HeightStreamingWorkMode ==
                        TerrainRuntimeBakeWorkMode.Incremental
                    &&
                    durableSuccessfulCoordinates.Count > 0
                )
                {
                    TerrainRuntimeBakeStateMutation mutation =
                        new TerrainRuntimeBakeStateMutation()
                            .RemoveHeightStreamingTiles(
                                durableSuccessfulCoordinates
                            );

                    TerrainRuntimeBakeStateService
                        .ApplyMutation(
                            mutation
                        );

                    expectedStateRevision =
                        TerrainRuntimeBakeStateService
                            .GetSummary()
                            .StateRevision;
                }

                preparedBatch.Clear();

                if (
                    TerrainRuntimeBakeValidationHooks
                        .ShouldCancelCoordinateStage(
                            TerrainRuntimeBakePipelineState.HeightStreaming,
                            succeededTiles.Count,
                            requestedTiles.Count
                        )
                )
                {
                    cancelled =
                        true;

                    AddRemainingCoordinates(
                        requestedTiles,
                        requestIndex + 1,
                        unprocessedTiles
                    );

                    break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (
            staleDuringGeneration
            ||
            cancelled
        )
        {
            if (
                plan.HeightStreamingWorkMode ==
                    TerrainRuntimeBakeWorkMode.Full
            )
            {
                RequireFullRepair();
            }

            TerrainHeightStreamingCompileSummary interruptedSummary =
                compileContext.CreateSummary();

            return
                new TerrainRuntimeHeightStreamingCompileResult(
                    staleDuringGeneration
                        ? TerrainRuntimeHeightStreamingCompileOutcome.StalePlan
                        : TerrainRuntimeHeightStreamingCompileOutcome.Cancelled,
                    plan.HeightStreamingWorkMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    interruptedSummary.CreatedAssetCount,
                    interruptedSummary.UpdatedAssetCount,
                    interruptedSummary.RemovedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    staleDuringGeneration
                        ? firstError
                        : "",
                    staleDuringGeneration
                        ? "Height Streaming generation stopped because persistent bake state changed."
                        : "Height Streaming generation was cancelled at a durability-safe batch boundary."
                );
        }

        if (failedTiles.Count > 0)
        {
            RequireFullRepair();

            TerrainHeightStreamingCompileSummary failedSummary =
                compileContext.CreateSummary();

            return
                new TerrainRuntimeHeightStreamingCompileResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.Failed,
                    plan.HeightStreamingWorkMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    failedSummary.CreatedAssetCount,
                    failedSummary.UpdatedAssetCount,
                    failedSummary.RemovedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    string.IsNullOrEmpty(firstError)
                        ? "One or more Height Streaming families could not be generated."
                        : firstError,
                    "Authoritative runtime Height remains valid; Height Streaming requires repair."
                );
        }

        compileContext
            .FinalizeAfterAuthoritativeSourceConfirmed(
                worldSettings
            );

        TerrainHeightStreamingCompileSummary summary =
            compileContext.CreateSummary();

        if (!summary.DatasetFinalized)
        {
            RequireFullRepair();

            return
                new TerrainRuntimeHeightStreamingCompileResult(
                    TerrainRuntimeHeightStreamingCompileOutcome.Failed,
                    plan.HeightStreamingWorkMode,
                    requestedTiles,
                    succeededTiles,
                    failedTiles,
                    unprocessedTiles,
                    summary.CreatedAssetCount,
                    summary.UpdatedAssetCount,
                    summary.RemovedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    string.IsNullOrEmpty(summary.FirstError)
                        ? "Height Streaming generated physical data but could not finalize its dataset metadata."
                        : summary.FirstError,
                    "Height Streaming remains incomplete and will require a full repair."
                );
        }

        TerrainRuntimeBakeStateMutation completionMutation =
            new TerrainRuntimeBakeStateMutation();

        if (
            plan.HeightStreamingWorkMode ==
                TerrainRuntimeBakeWorkMode.Full
        )
        {
            completionMutation
                .ClearAllHeightStreamingTiles()
                .ClearFullHeightStreaming();
        }
        else
        {
            completionMutation
                .RemoveHeightStreamingTiles(
                    succeededTiles
                );
        }

        TerrainRuntimeBakeStateService
            .ApplyMutation(
                completionMutation
            );

        return
            new TerrainRuntimeHeightStreamingCompileResult(
                TerrainRuntimeHeightStreamingCompileOutcome.Completed,
                plan.HeightStreamingWorkMode,
                requestedTiles,
                succeededTiles,
                failedTiles,
                unprocessedTiles,
                summary.CreatedAssetCount,
                summary.UpdatedAssetCount,
                summary.RemovedAssetCount,
                true,
                revisionBefore,
                manifest.streamingGenerationRevision,
                "",
                plan.HeightStreamingWorkMode ==
                    TerrainRuntimeBakeWorkMode.Full
                    ? "The complete Height Streaming pyramid was rebuilt from authoritative runtime Height."
                    : "Planned Height Streaming families were repaired from authoritative runtime Height."
            );
    }

    private static List<Vector2Int> BuildRequestedCoordinates(
        WorldSettings worldSettings,
        TerrainRuntimeBakePlan plan,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        HashSet<Vector2Int> coordinates =
            new HashSet<Vector2Int>();

        if (
            plan.HeightStreamingWorkMode ==
                TerrainRuntimeBakeWorkMode.Full
        )
        {
            TerrainRuntimeBakeDependencyUtility
                .CollectAllHeightTiles(
                    worldSettings,
                    coordinates
                );
        }
        else if (
            !TerrainRuntimeBakeDependencyUtility
                .TryCopyValidHeightTiles(
                    worldSettings,
                    plan.HeightStreamingTiles,
                    coordinates,
                    out errorMessage
                )
        )
        {
            return null;
        }

        List<Vector2Int> sorted =
            new List<Vector2Int>(
                coordinates
            );

        sorted.Sort(
            CompareCoordinates
        );

        return sorted;
    }

    private static bool TryPersistPreparedBatch(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        try
        {
            AssetDatabase.SaveAssets();

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist the Height Streaming durability batch.\n\n" +
                exception.Message;

            return false;
        }
    }

    private static void RequireFullRepair()
    {
        TerrainRuntimeBakeStateService
            .ApplyMutation(
                new TerrainRuntimeBakeStateMutation()
                    .RequireFullHeightStreaming()
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

    private static TerrainRuntimeHeightStreamingCompileResult
        CreateTerminalResult(
            TerrainRuntimeHeightStreamingCompileOutcome outcome,
            TerrainRuntimeBakeWorkMode workMode,
            int revision,
            string message
        )
    {
        return
            new TerrainRuntimeHeightStreamingCompileResult(
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
                outcome ==
                    TerrainRuntimeHeightStreamingCompileOutcome.NoWork
                    ? ""
                    : message,
                message
            );
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
}
