using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeBakeResumeValidationUtility
{
    private sealed class ActiveValidation
    {
        public TerrainRuntimeBakeResumeValidationScenario scenario;
        public TerrainRuntimeBakePlan initialPlan;
        public Action<TerrainRuntimeBakeResumeValidationResult> completionCallback;
    }

    private static ActiveValidation activeValidation;
    private static TerrainRuntimeBakeResumeValidationResult lastResult;
    private static string currentPhase = "Idle";

    public static bool IsRunning => activeValidation != null;
    public static string CurrentPhase => currentPhase;
    public static TerrainRuntimeBakeResumeValidationScenario CurrentScenario =>
        activeValidation != null
            ? activeValidation.scenario
            : TerrainRuntimeBakeResumeValidationScenario.None;
    public static TerrainRuntimeBakeResumeValidationResult LastResult => lastResult;

    public static bool StartScenario(
        TerrainRuntimeBakeResumeValidationScenario scenario,
        Action<TerrainRuntimeBakeResumeValidationResult> onCompleted = null
    )
    {
        if (scenario == TerrainRuntimeBakeResumeValidationScenario.None)
        {
            PublishBlocked(scenario, "No resume validation scenario was selected.", onCompleted);
            return false;
        }

        if (IsRunning)
        {
            PublishBlocked(scenario, "A resume validation scenario is already running.", onCompleted);
            return false;
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            PublishBlocked(scenario, "The unified runtime bake pipeline is already running.", onCompleted);
            return false;
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            PublishBlocked(scenario, "Independent Surface generation is already running.", onCompleted);
            return false;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            PublishBlocked(scenario, "Resume validation must run outside Play Mode.", onCompleted);
            return false;
        }

        TerrainRuntimeBakePlan initialPlan = TerrainRuntimeBakePlanner.BuildCurrentPlan();

        if (initialPlan == null)
        {
            PublishBlocked(scenario, "The current runtime bake plan is unavailable.", onCompleted);
            return false;
        }

        if (initialPlan.IsBlocked)
        {
            PublishBlocked(
                scenario,
                string.IsNullOrEmpty(initialPlan.BlockReason)
                    ? "The current runtime bake plan is blocked."
                    : initialPlan.BlockReason,
                onCompleted
            );
            return false;
        }

        if (!ValidatePreconditions(scenario, initialPlan, out string preconditionError))
        {
            PublishBlocked(scenario, preconditionError, onCompleted);
            return false;
        }

        TerrainRuntimeBakeValidationHooks.ClearActiveHooks();
        ConfigureScenarioHook(scenario);

        activeValidation = new ActiveValidation
        {
            scenario = scenario,
            initialPlan = initialPlan,
            completionCallback = onCompleted
        };

        currentPhase = "Initial interrupted bake";

        bool started = TerrainRuntimeBakePipeline.BakePendingChanges(
            OnInterruptedPipelineCompleted
        );

        if (!started && activeValidation != null)
        {
            TerrainRuntimeBakeValidationHooks.ClearActiveHooks();
            FinishActiveFailure(
                "The canonical runtime bake pipeline did not start the interruption scenario.",
                TerrainRuntimeBakePipeline.LastResult,
                null,
                null,
                null
            );
        }

        return started;
    }

    private static void OnInterruptedPipelineCompleted(
        TerrainRuntimeBakePipelineResult interruptedResult
    )
    {
        if (activeValidation == null)
        {
            TerrainRuntimeBakeValidationHooks.ClearActiveHooks();
            return;
        }

        TerrainRuntimeBakeResumeValidationScenario scenario =
            activeValidation.scenario;

        TerrainRuntimeBakeValidationHooks.ClearActiveHooks();
        currentPhase = "Inspecting remaining plan";

        bool expectedInterruption = InterruptionMatchesExpectation(
            scenario,
            interruptedResult
        );

        TerrainRuntimeBakeStateSnapshot postState =
            TerrainRuntimeBakeStateService.GetSnapshot();

        TerrainRuntimeBakePlan postPlan =
            TerrainRuntimeBakePlanner.BuildCurrentPlan();

        if (!expectedInterruption)
        {
            FinishActiveFailure(
                "The validation hook did not produce the expected interrupted pipeline outcome.",
                interruptedResult,
                postPlan,
                postState,
                null
            );
            return;
        }

        if (postPlan == null || postPlan.IsBlocked)
        {
            FinishActiveFailure(
                postPlan != null && !string.IsNullOrEmpty(postPlan.BlockReason)
                    ? "The post-interruption bake plan is blocked: " + postPlan.BlockReason
                    : "The post-interruption bake plan is unavailable.",
                interruptedResult,
                postPlan,
                postState,
                null
            );
            return;
        }

        currentPhase = "Resuming remaining work";

        bool resumeStarted = TerrainRuntimeBakePipeline.BakePendingChanges(
            resumedResult => OnResumedPipelineCompleted(
                scenario,
                interruptedResult,
                postPlan,
                postState,
                resumedResult
            )
        );

        if (!resumeStarted && activeValidation != null)
        {
            FinishActiveFailure(
                "The canonical runtime bake pipeline did not start the resume pass.",
                interruptedResult,
                postPlan,
                postState,
                TerrainRuntimeBakePipeline.LastResult
            );
        }
    }

    private static void OnResumedPipelineCompleted(
        TerrainRuntimeBakeResumeValidationScenario scenario,
        TerrainRuntimeBakePipelineResult interruptedResult,
        TerrainRuntimeBakePlan postPlan,
        TerrainRuntimeBakeStateSnapshot postState,
        TerrainRuntimeBakePipelineResult resumedResult
    )
    {
        if (activeValidation == null)
        {
            return;
        }

        currentPhase = "Final validation";

        TerrainRuntimeBakeValidationResult resumedValidation =
            TerrainRuntimeBakeValidationUtility.ValidatePipelineResult(
                resumedResult
            );

        List<Vector2Int> repeatedHeight = FindRepeatedAcknowledgedCoordinates(
            interruptedResult != null && interruptedResult.HeightResult != null
                ? interruptedResult.HeightResult.SucceededTiles
                : null,
            postState != null ? postState.PendingHeightTiles : null,
            resumedResult != null && resumedResult.HeightResult != null
                ? resumedResult.HeightResult.RequestedTiles
                : null
        );

        List<Vector2Int> repeatedHeightStreaming = FindRepeatedAcknowledgedCoordinates(
            interruptedResult != null && interruptedResult.HeightStreamingResult != null
                ? interruptedResult.HeightStreamingResult.SucceededTiles
                : null,
            postState != null ? postState.PendingHeightStreamingTiles : null,
            resumedResult != null && resumedResult.HeightStreamingResult != null
                ? resumedResult.HeightStreamingResult.RequestedTiles
                : null
        );

        List<Vector2Int> repeatedSurface = FindRepeatedAcknowledgedCoordinates(
            interruptedResult != null && interruptedResult.SurfaceResult != null
                ? interruptedResult.SurfaceResult.SucceededTiles
                : null,
            postState != null ? postState.PendingSurfaceTiles : null,
            resumedResult != null && resumedResult.SurfaceResult != null
                ? resumedResult.SurfaceResult.RequestedTiles
                : null
        );

        List<Vector2Int> repeatedCollision = FindRepeatedAcknowledgedCoordinates(
            interruptedResult != null && interruptedResult.CollisionResult != null
                ? interruptedResult.CollisionResult.SucceededChunks
                : null,
            postState != null ? postState.PendingCollisionChunks : null,
            resumedResult != null && resumedResult.CollisionResult != null
                ? resumedResult.CollisionResult.RequestedChunks
                : null
        );

        bool resumeStartedAtExpectedWork =
            ValidateEarliestRequiredWork(postPlan, resumedResult);

        bool upstreamRepeated =
            DetectUpstreamStageRepetition(postPlan, resumedResult);

        bool finalPlanCurrent =
            resumedResult != null
            && resumedResult.FinalPlan != null
            && !resumedResult.FinalPlan.IsBlocked
            && !resumedResult.FinalPlan.HasWork;

        bool resumedValidationPassed =
            resumedValidation != null
            && (
                resumedValidation.Outcome == TerrainRuntimeBakeValidationOutcome.Passed
                || resumedValidation.Outcome == TerrainRuntimeBakeValidationOutcome.PassedWithWarnings
            );

        bool passed =
            resumeStartedAtExpectedWork
            && !upstreamRepeated
            && repeatedHeight.Count == 0
            && repeatedHeightStreaming.Count == 0
            && repeatedSurface.Count == 0
            && repeatedCollision.Count == 0
            && finalPlanCurrent
            && resumedValidationPassed;

        TerrainRuntimeBakeResumeValidationResult result =
            new TerrainRuntimeBakeResumeValidationResult(
                scenario,
                passed
                    ? TerrainRuntimeBakeResumeValidationOutcome.Passed
                    : TerrainRuntimeBakeResumeValidationOutcome.Failed,
                interruptedResult,
                postPlan,
                postState,
                resumedResult,
                resumedValidation,
                true,
                resumeStartedAtExpectedWork,
                upstreamRepeated,
                finalPlanCurrent,
                repeatedHeight,
                repeatedHeightStreaming,
                repeatedSurface,
                repeatedCollision,
                passed
                    ? ""
                    : "The resumed bake did not exactly preserve the expected remaining-work semantics.",
                passed
                    ? "The interruption preserved acknowledged work and the resumed bake processed only the remaining required work."
                    : "Resume validation found repeated, missing, or incorrectly sequenced work."
            );

        FinishActive(result);
    }

    private static void ConfigureScenarioHook(
        TerrainRuntimeBakeResumeValidationScenario scenario
    )
    {
        switch (scenario)
        {
            case TerrainRuntimeBakeResumeValidationScenario.HeightCancellation:
                TerrainRuntimeBakeValidationHooks.ConfigureCoordinateCancellation(
                    TerrainRuntimeBakePipelineState.Heightmaps,
                    0.25f
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.HeightStreamingCancellation:
                TerrainRuntimeBakeValidationHooks.ConfigureCoordinateCancellation(
                    TerrainRuntimeBakePipelineState.HeightStreaming,
                    0.25f
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.SurfaceCancellation:
                TerrainRuntimeBakeValidationHooks.ConfigureCoordinateCancellation(
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    0.25f
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.CollisionCancellation:
                TerrainRuntimeBakeValidationHooks.ConfigureCoordinateCancellation(
                    TerrainRuntimeBakePipelineState.Collision,
                    0.25f
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.CancelDuringAddressables:
                TerrainRuntimeBakeValidationHooks.ConfigureCancelDuringAddressables();
                break;

            case TerrainRuntimeBakeResumeValidationScenario.CancelBeforeSceneSync:
                TerrainRuntimeBakeValidationHooks.ConfigureCancelBeforeSceneSync();
                break;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeight:
                TerrainRuntimeBakeValidationHooks.ConfigureFailureBeforeStage(
                    TerrainRuntimeBakePipelineState.HeightStreaming
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeightStreaming:
                TerrainRuntimeBakeValidationHooks.ConfigureFailureBeforeStage(
                    TerrainRuntimeBakePipelineState.SurfaceMasks
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterSurface:
                TerrainRuntimeBakeValidationHooks.ConfigureFailureBeforeStage(
                    TerrainRuntimeBakePipelineState.Collision
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterCollision:
            case TerrainRuntimeBakeResumeValidationScenario.AddressablesFailureResume:
                TerrainRuntimeBakeValidationHooks.ConfigureFailureBeforeStage(
                    TerrainRuntimeBakePipelineState.Addressables
                );
                break;

            case TerrainRuntimeBakeResumeValidationScenario.SceneSyncFailureResume:
                TerrainRuntimeBakeValidationHooks.ConfigureFailureBeforeStage(
                    TerrainRuntimeBakePipelineState.SceneSync
                );
                break;
        }
    }

    private static bool ValidatePreconditions(
        TerrainRuntimeBakeResumeValidationScenario scenario,
        TerrainRuntimeBakePlan plan,
        out string errorMessage
    )
    {
        errorMessage = "";

        switch (scenario)
        {
            case TerrainRuntimeBakeResumeValidationScenario.HeightCancellation:
                if (plan.HeightWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.HeightTileCount < 2)
                {
                    errorMessage = "Height cancellation validation requires at least two pending Incremental Height tiles.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.HeightStreamingCancellation:
                if (plan.HeightStreamingWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.HeightStreamingTileCount < 2)
                {
                    errorMessage = "Height Streaming cancellation validation requires at least two pending Incremental Height Streaming tiles.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.SurfaceCancellation:
                if (plan.SurfaceWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.SurfaceTileCount < 2)
                {
                    errorMessage = "Surface cancellation validation requires at least two pending Incremental Surface tiles.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.CollisionCancellation:
                if (plan.CollisionWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.CollisionChunkCount < 2)
                {
                    errorMessage = "Collision cancellation validation requires at least two pending Incremental Collision chunks.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeight:
                if (plan.HeightWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.HeightTileCount == 0)
                {
                    errorMessage = "Failure-after-Height validation requires pending Incremental Height work.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeightStreaming:
                if (plan.HeightStreamingWorkMode == TerrainRuntimeBakeWorkMode.None)
                {
                    errorMessage = "Failure-after-Height-Streaming validation requires pending Height Streaming work.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterSurface:
                if (plan.SurfaceWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.SurfaceTileCount == 0)
                {
                    errorMessage = "Failure-after-Surface validation requires pending Incremental Surface work.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.FailureAfterCollision:
                if (plan.CollisionWorkMode != TerrainRuntimeBakeWorkMode.Incremental || plan.CollisionChunkCount == 0)
                {
                    errorMessage = "Failure-after-Collision validation requires pending Incremental Collision work.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.CancelDuringAddressables:
            case TerrainRuntimeBakeResumeValidationScenario.AddressablesFailureResume:
                if (
                    !plan.AddressablesConfigurationRequired
                    && !plan.AddressablesContentBuildRequired
                    && plan.HeightWorkMode == TerrainRuntimeBakeWorkMode.None
                    && plan.HeightStreamingWorkMode == TerrainRuntimeBakeWorkMode.None
                    && plan.SurfaceWorkMode == TerrainRuntimeBakeWorkMode.None
                    && plan.CollisionWorkMode == TerrainRuntimeBakeWorkMode.None
                )
                {
                    errorMessage = "Addressables resume validation requires pending Addressables work or upstream generated-data work that will dirty Addressables.";
                    return false;
                }
                return true;

            case TerrainRuntimeBakeResumeValidationScenario.CancelBeforeSceneSync:
            case TerrainRuntimeBakeResumeValidationScenario.SceneSyncFailureResume:
                if (!plan.HasWork)
                {
                    errorMessage = "Scene-boundary resume validation requires pending runtime bake work.";
                    return false;
                }
                return true;

            default:
                errorMessage = "Unsupported resume validation scenario.";
                return false;
        }
    }

    private static bool InterruptionMatchesExpectation(
        TerrainRuntimeBakeResumeValidationScenario scenario,
        TerrainRuntimeBakePipelineResult result
    )
    {
        if (result == null)
        {
            return false;
        }

        switch (scenario)
        {
            case TerrainRuntimeBakeResumeValidationScenario.HeightCancellation:
            case TerrainRuntimeBakeResumeValidationScenario.HeightStreamingCancellation:
            case TerrainRuntimeBakeResumeValidationScenario.SurfaceCancellation:
            case TerrainRuntimeBakeResumeValidationScenario.CollisionCancellation:
            case TerrainRuntimeBakeResumeValidationScenario.CancelDuringAddressables:
            case TerrainRuntimeBakeResumeValidationScenario.CancelBeforeSceneSync:
                return result.Outcome == TerrainRuntimeBakePipelineOutcome.Cancelled;

            default:
                return result.Outcome == TerrainRuntimeBakePipelineOutcome.Failed;
        }
    }

    private static List<Vector2Int> FindRepeatedAcknowledgedCoordinates(
        IEnumerable<Vector2Int> succeeded,
        IEnumerable<Vector2Int> remainingPending,
        IEnumerable<Vector2Int> resumedRequested
    )
    {
        HashSet<Vector2Int> pending = remainingPending != null
            ? new HashSet<Vector2Int>(remainingPending)
            : new HashSet<Vector2Int>();

        HashSet<Vector2Int> acknowledged = new HashSet<Vector2Int>();

        if (succeeded != null)
        {
            foreach (Vector2Int coordinate in succeeded)
            {
                if (!pending.Contains(coordinate))
                {
                    acknowledged.Add(coordinate);
                }
            }
        }

        List<Vector2Int> repeated = new List<Vector2Int>();

        if (resumedRequested != null)
        {
            foreach (Vector2Int coordinate in resumedRequested)
            {
                if (acknowledged.Contains(coordinate))
                {
                    repeated.Add(coordinate);
                }
            }
        }

        repeated.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
        return repeated;
    }

    private static bool ValidateEarliestRequiredWork(
        TerrainRuntimeBakePlan expectedPlan,
        TerrainRuntimeBakePipelineResult resumedResult
    )
    {
        if (expectedPlan == null || resumedResult == null)
        {
            return false;
        }

        if (expectedPlan.HeightWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return resumedResult.HeightPlan != null
                && resumedResult.HeightPlan.HeightWorkMode == expectedPlan.HeightWorkMode
                && SetsEqual(expectedPlan.HeightTiles, resumedResult.HeightPlan.HeightTiles);
        }

        if (expectedPlan.HeightStreamingWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return resumedResult.HeightStreamingPlan != null
                && resumedResult.HeightStreamingPlan.HeightStreamingWorkMode == expectedPlan.HeightStreamingWorkMode
                && SetsEqual(expectedPlan.HeightStreamingTiles, resumedResult.HeightStreamingPlan.HeightStreamingTiles);
        }

        if (expectedPlan.SurfaceWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return resumedResult.SurfacePlan != null
                && resumedResult.SurfacePlan.SurfaceWorkMode == expectedPlan.SurfaceWorkMode
                && SetsEqual(expectedPlan.SurfaceTiles, resumedResult.SurfacePlan.SurfaceTiles);
        }

        if (expectedPlan.CollisionWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return resumedResult.CollisionPlan != null
                && resumedResult.CollisionPlan.CollisionWorkMode == expectedPlan.CollisionWorkMode
                && SetsEqual(expectedPlan.CollisionChunks, resumedResult.CollisionPlan.CollisionChunks);
        }

        if (
            expectedPlan.AddressablesConfigurationRequired
            || expectedPlan.AddressablesContentBuildRequired
        )
        {
            return resumedResult.AddressablesPlan != null
                && TerrainRuntimeAddressablesUtility.GetOperationMode(resumedResult.AddressablesPlan)
                    == TerrainRuntimeAddressablesUtility.GetOperationMode(expectedPlan)
                && resumedResult.AddressablesStageExecuted;
        }

        if (expectedPlan.RuntimeSceneMetadataUpdateRequired)
        {
            return resumedResult.SceneSyncStageExecuted;
        }

        return resumedResult.Outcome == TerrainRuntimeBakePipelineOutcome.NoWork
            || (
                resumedResult.FinalPlan != null
                && !resumedResult.FinalPlan.HasWork
            );
    }

    private static bool DetectUpstreamStageRepetition(
        TerrainRuntimeBakePlan expectedPlan,
        TerrainRuntimeBakePipelineResult resumedResult
    )
    {
        if (expectedPlan == null || resumedResult == null)
        {
            return true;
        }

        if (expectedPlan.HeightWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return false;
        }

        if (resumedResult.HeightStageExecuted)
        {
            return true;
        }

        if (expectedPlan.HeightStreamingWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return false;
        }

        if (resumedResult.HeightStreamingStageExecuted)
        {
            return true;
        }

        if (expectedPlan.SurfaceWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return false;
        }

        if (resumedResult.SurfaceStageExecuted)
        {
            return true;
        }

        if (expectedPlan.CollisionWorkMode != TerrainRuntimeBakeWorkMode.None)
        {
            return false;
        }

        if (resumedResult.CollisionStageExecuted)
        {
            return true;
        }

        if (
            expectedPlan.AddressablesConfigurationRequired
            || expectedPlan.AddressablesContentBuildRequired
        )
        {
            return false;
        }

        if (resumedResult.AddressablesStageExecuted)
        {
            return true;
        }

        return false;
    }

    private static bool SetsEqual(
        IEnumerable<Vector2Int> left,
        IEnumerable<Vector2Int> right
    )
    {
        HashSet<Vector2Int> leftSet = left != null
            ? new HashSet<Vector2Int>(left)
            : new HashSet<Vector2Int>();

        HashSet<Vector2Int> rightSet = right != null
            ? new HashSet<Vector2Int>(right)
            : new HashSet<Vector2Int>();

        return leftSet.SetEquals(rightSet);
    }

    private static void PublishBlocked(
        TerrainRuntimeBakeResumeValidationScenario scenario,
        string errorMessage,
        Action<TerrainRuntimeBakeResumeValidationResult> callback
    )
    {
        TerrainRuntimeBakeResumeValidationResult result =
            new TerrainRuntimeBakeResumeValidationResult(
                scenario,
                TerrainRuntimeBakeResumeValidationOutcome.Blocked,
                null,
                TerrainRuntimeBakePlanner.BuildCurrentPlan(),
                TerrainRuntimeBakeStateService.GetSnapshot(),
                null,
                null,
                false,
                false,
                false,
                false,
                null,
                null,
                null,
                null,
                errorMessage,
                "Resume validation did not start."
            );

        lastResult = result;
        currentPhase = "Idle";

        try
        {
            callback?.Invoke(result);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void FinishActiveFailure(
        string errorMessage,
        TerrainRuntimeBakePipelineResult interruptedResult,
        TerrainRuntimeBakePlan postPlan,
        TerrainRuntimeBakeStateSnapshot postState,
        TerrainRuntimeBakePipelineResult resumedResult
    )
    {
        if (activeValidation == null)
        {
            return;
        }

        TerrainRuntimeBakeValidationHooks.ClearActiveHooks();

        TerrainRuntimeBakeResumeValidationResult result =
            new TerrainRuntimeBakeResumeValidationResult(
                activeValidation.scenario,
                TerrainRuntimeBakeResumeValidationOutcome.Failed,
                interruptedResult,
                postPlan,
                postState,
                resumedResult,
                resumedResult != null
                    ? TerrainRuntimeBakeValidationUtility.ValidatePipelineResult(resumedResult)
                    : null,
                false,
                false,
                false,
                false,
                null,
                null,
                null,
                null,
                errorMessage,
                "Resume validation did not complete successfully."
            );

        FinishActive(result);
    }

    private static void FinishActive(
        TerrainRuntimeBakeResumeValidationResult result
    )
    {
        Action<TerrainRuntimeBakeResumeValidationResult> callback =
            activeValidation != null
                ? activeValidation.completionCallback
                : null;

        TerrainRuntimeBakeValidationHooks.ClearActiveHooks();
        activeValidation = null;
        currentPhase = "Idle";
        lastResult = result;

        try
        {
            callback?.Invoke(result);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
