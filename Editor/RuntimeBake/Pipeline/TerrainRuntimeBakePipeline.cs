using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package 08 orchestration layer.
 *
 * This class deliberately owns no generation state. Each lower-level stage
 * remains responsible for its own assets, manifests, dirty queues, revisions,
 * partial-success semantics, and cancellation behavior.
 */
public static class TerrainRuntimeBakePipeline
{
    private sealed class ActiveRun
    {
        public TerrainRuntimeBakePipelineMode mode;

        public WorldSettings worldSettings;
        public TerrainAuthoringData authoringData;

        public TerrainRuntimeBakePlan initialPlan;
        public TerrainRuntimeBakePlan currentPlan;

        // Package 10.1 preserves the exact fresh plan used at each stage.
        public TerrainRuntimeBakePlan heightPlan;
        public TerrainRuntimeBakePlan surfacePlan;
        public TerrainRuntimeBakePlan collisionPlan;
        public TerrainRuntimeBakePlan addressablesPlan;
        public TerrainRuntimeBakePlan sceneSyncPlan;

        public TerrainRuntimeBakePlan finalPlan;

        public TerrainRuntimeHeightCompileResult heightResult;
        public TerrainSurfaceMaskGenerationResult surfaceResult;
        public TerrainCollisionGenerationResult collisionResult;
        public TerrainRuntimeAddressablesResult addressablesResult;
        public TerrainRuntimeSceneSynchronizationResult sceneSyncResult;

        public bool heightStageExecuted;
        public bool surfaceStageExecuted;
        public bool collisionStageExecuted;
        public bool addressablesStageExecuted;
        public bool sceneSyncStageExecuted;

        public double heightDurationSeconds;
        public double surfaceDurationSeconds;
        public double collisionDurationSeconds;
        public double addressablesDurationSeconds;
        public double sceneSyncDurationSeconds;
        public double surfaceStageStartedAtEditorTime;

        public bool cancelRequested;
        public bool terminal;
        public bool surfaceCallbackConsumed;
        public bool completedWithWarnings;

        public TerrainRuntimeBakePipelineState lastStage =
            TerrainRuntimeBakePipelineState.Idle;

        public DateTime startedAtUtc;
        public double startedAtEditorTime;

        public Action<TerrainRuntimeBakePipelineResult>
            completionCallback;

        public readonly List<string> warnings =
            new List<string>();
    }

    private static ActiveRun activeRun;

    private static TerrainRuntimeBakePipelineState currentState =
        TerrainRuntimeBakePipelineState.Idle;

    private static TerrainRuntimeBakePipelineMode currentMode =
        TerrainRuntimeBakePipelineMode.None;

    private static TerrainRuntimeBakePipelineResult lastResult;

    // =====================================================
    // PUBLIC STATUS
    // =====================================================

    public static bool IsRunning =>
        activeRun != null
        &&
        !activeRun.terminal;

    public static TerrainRuntimeBakePipelineState CurrentState =>
        currentState;

    public static TerrainRuntimeBakePipelineMode CurrentMode =>
        currentMode;

    public static TerrainRuntimeBakePlan InitialPlan =>
        activeRun != null
            ? activeRun.initialPlan
            : lastResult != null
                ? lastResult.InitialPlan
                : null;

    public static TerrainRuntimeBakePlan CurrentPlan =>
        activeRun != null
            ? activeRun.currentPlan
            : lastResult != null
                ? lastResult.FinalPlan
                : null;

    public static TerrainRuntimeBakePipelineResult LastResult =>
        lastResult;

    public static bool CancelRequested =>
        activeRun != null
        &&
        activeRun.cancelRequested;

    public static string CurrentStageLabel
    {
        get
        {
            switch (currentState)
            {
                case TerrainRuntimeBakePipelineState.Preflight:
                    return "Preflight";

                case TerrainRuntimeBakePipelineState.Heightmaps:
                    return "Runtime Heightmaps";

                case TerrainRuntimeBakePipelineState.SurfaceMasks:
                    return "Surface Masks";

                case TerrainRuntimeBakePipelineState.Collision:
                    return "Collision Meshes";

                case TerrainRuntimeBakePipelineState.Addressables:
                    return "Addressables";

                case TerrainRuntimeBakePipelineState.SceneSync:
                    return "Runtime Scene Synchronization";

                case TerrainRuntimeBakePipelineState.Finalizing:
                    return "Final Validation";

                case TerrainRuntimeBakePipelineState.Completed:
                    return "Completed";

                case TerrainRuntimeBakePipelineState.Failed:
                    return "Failed";

                case TerrainRuntimeBakePipelineState.Cancelled:
                    return "Cancelled";

                case TerrainRuntimeBakePipelineState.Blocked:
                    return "Blocked";

                default:
                    return "Idle";
            }
        }
    }

    public static float CurrentOverallProgress
    {
        get
        {
            switch (currentState)
            {
                case TerrainRuntimeBakePipelineState.Preflight:
                    return 0.05f;

                case TerrainRuntimeBakePipelineState.Heightmaps:
                    return 0.20f;

                case TerrainRuntimeBakePipelineState.SurfaceMasks:
                    return 0.40f;

                case TerrainRuntimeBakePipelineState.Collision:
                    return 0.60f;

                case TerrainRuntimeBakePipelineState.Addressables:
                    return 0.80f;

                case TerrainRuntimeBakePipelineState.SceneSync:
                    return 0.95f;

                case TerrainRuntimeBakePipelineState.Finalizing:
                    return 0.98f;

                case TerrainRuntimeBakePipelineState.Completed:
                    return 1.00f;

                default:
                    return 0.00f;
            }
        }
    }

    // =====================================================
    // PUBLIC COMMANDS
    // =====================================================

    public static bool BakePendingChanges(
        Action<TerrainRuntimeBakePipelineResult> onCompleted = null
    )
    {
        return TryStart(
            TerrainRuntimeBakePipelineMode.PendingChanges,
            onCompleted
        );
    }

    public static bool RebuildAllRuntimeData(
        Action<TerrainRuntimeBakePipelineResult> onCompleted = null
    )
    {
        return TryStart(
            TerrainRuntimeBakePipelineMode.RebuildAll,
            onCompleted
        );
    }

    /*
     * Package-level cancellation is boundary-safe only. Lower-level stages keep
     * ownership of their own cancelable progress. If requested while Surface
     * is running, the pipeline stops before the next stage unless Package 06's
     * own cancel action terminates Surface earlier.
     */
    public static bool RequestCancel()
    {
        if (!IsRunning)
        {
            return false;
        }

        activeRun.cancelRequested =
            true;


        return true;
    }

    // =====================================================
    // START / PREFLIGHT
    // =====================================================

    private static bool TryStart(
        TerrainRuntimeBakePipelineMode mode,
        Action<TerrainRuntimeBakePipelineResult> onCompleted
    )
    {
        if (IsRunning)
        {
            InvokeRejectedCallback(
                mode,
                onCompleted,
                "A unified runtime bake pipeline is already running."
            );

            Debug.LogWarning(
                "WorldMeshes unified runtime bake start was rejected because another pipeline run is already active."
            );

            return false;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            PublishImmediateBlockedResult(
                mode,
                onCompleted,
                "Unified runtime baking cannot start while entering or running Play Mode."
            );

            return false;
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            PublishImmediateBlockedResult(
                mode,
                onCompleted,
                "Unified runtime baking cannot start while an independent runtime surface-mask generation operation is already running."
            );

            return false;
        }

        ActiveRun run =
            new ActiveRun
            {
                mode = mode,
                startedAtUtc = DateTime.UtcNow,
                startedAtEditorTime = EditorApplication.timeSinceStartup,
                completionCallback = onCompleted
            };

        activeRun =
            run;

        currentMode =
            mode;

        SetState(
            run,
            TerrainRuntimeBakePipelineState.Preflight,
            true
        );

        Schedule(
            run,
            ExecutePreflight
        );

        return true;
    }

    private static void ExecutePreflight(
        ActiveRun run
    )
    {
        if (!PrepareStageBoundary(run))
        {
            return;
        }

        run.worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        run.authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (run.worldSettings == null)
        {
            FinishBlocked(
                run,
                TerrainRuntimeBakePipelineState.Preflight,
                "WorldSettings is unavailable."
            );

            return;
        }

        if (run.authoringData == null)
        {
            FinishBlocked(
                run,
                TerrainRuntimeBakePipelineState.Preflight,
                "TerrainAuthoringData is unavailable."
            );

            return;
        }

        TerrainRuntimeBakePlan plan =
            BuildFreshPlan(
                run
            );

        if (plan == null)
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.Preflight,
                "Runtime bake preflight could not build a bake plan."
            );

            return;
        }

        run.initialPlan =
            plan;

        if (plan.IsBlocked)
        {
            FinishBlocked(
                run,
                TerrainRuntimeBakePipelineState.Preflight,
                string.IsNullOrEmpty(plan.BlockReason)
                    ? "The runtime bake plan is blocked."
                    : plan.BlockReason
            );

            return;
        }

        if (
            run.mode ==
                TerrainRuntimeBakePipelineMode.PendingChanges
            &&
            !plan.HasWork
        )
        {
            run.finalPlan =
                plan;

            Finish(
                run,
                TerrainRuntimeBakePipelineOutcome.NoWork,
                TerrainRuntimeBakePipelineState.Completed,
                TerrainRuntimeBakePipelineState.Idle,
                "",
                "No runtime bake changes are pending."
            );

            return;
        }

        Schedule(
            run,
            ExecuteHeightStage
        );
    }

    // =====================================================
    // HEIGHT
    // =====================================================

    private static void ExecuteHeightStage(
        ActiveRun run
    )
    {
        SetState(
            run,
            TerrainRuntimeBakePipelineState.Heightmaps,
            true
        );

        if (!PrepareStageBoundary(run))
        {
            return;
        }

        TerrainRuntimeBakePlan plan =
            BuildFreshPlan(
                run
            );

        run.heightPlan =
            plan;

        if (!ValidateStagePlan(run, plan))
        {
            return;
        }

        if (
            run.mode ==
                TerrainRuntimeBakePipelineMode.PendingChanges
            &&
            plan.HeightWorkMode ==
                TerrainRuntimeBakeWorkMode.None
        )
        {
            Schedule(
                run,
                ExecuteSurfaceStage
            );

            return;
        }

        run.heightStageExecuted =
            true;

        double stageStartedAt =
            EditorApplication.timeSinceStartup;

        TerrainRuntimeHeightCompileResult result;

        try
        {
            if (
                run.mode ==
                    TerrainRuntimeBakePipelineMode.RebuildAll
            )
            {
                result =
                    TerrainRuntimeHeightCompiler.RebuildAllRuntimeHeightmaps(
                        run.worldSettings,
                        run.authoringData
                    );
            }
            else
            {
                result =
                    TerrainRuntimeHeightCompiler.CompilePlannedHeightWork(
                        run.worldSettings,
                        run.authoringData,
                        plan
                    );
            }
        }
        finally
        {
            run.heightDurationSeconds =
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    stageStartedAt
                );
        }

        run.heightResult =
            result;

        if (result == null)
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.Heightmaps,
                "Runtime height generation returned no result."
            );

            return;
        }

        switch (result.Outcome)
        {
            case TerrainRuntimeHeightCompileOutcome.Completed:
            case TerrainRuntimeHeightCompileOutcome.NoWork:
                Schedule(
                    run,
                    ExecuteSurfaceStage
                );
                return;

            case TerrainRuntimeHeightCompileOutcome.Cancelled:
                FinishCancelled(
                    run,
                    TerrainRuntimeBakePipelineState.Heightmaps,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime height generation was cancelled."
                    )
                );
                return;

            case TerrainRuntimeHeightCompileOutcome.Blocked:
                FinishBlocked(
                    run,
                    TerrainRuntimeBakePipelineState.Heightmaps,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime height generation was blocked."
                    )
                );
                return;

            default:
                FinishFailed(
                    run,
                    TerrainRuntimeBakePipelineState.Heightmaps,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime height generation did not complete. Outcome: " +
                        result.Outcome
                    )
                );
                return;
        }
    }

    // =====================================================
    // SURFACE
    // =====================================================

    private static void ExecuteSurfaceStage(
        ActiveRun run
    )
    {
        SetState(
            run,
            TerrainRuntimeBakePipelineState.SurfaceMasks,
            true
        );

        if (!PrepareStageBoundary(run))
        {
            return;
        }

        TerrainRuntimeBakePlan plan =
            BuildFreshPlan(
                run
            );

        run.surfacePlan =
            plan;

        if (!ValidateStagePlan(run, plan))
        {
            return;
        }

        if (
            TerrainRuntimeBakeValidationHooks.TryConsumeFailureBeforeStage(
                TerrainRuntimeBakePipelineState.SurfaceMasks,
                out string validationFailureMessage
            )
        )
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.SurfaceMasks,
                validationFailureMessage
            );
            return;
        }

        if (
            run.mode ==
                TerrainRuntimeBakePipelineMode.PendingChanges
            &&
            plan.SurfaceWorkMode ==
                TerrainRuntimeBakeWorkMode.None
        )
        {
            Schedule(
                run,
                ExecuteCollisionStage
            );

            return;
        }

        run.surfaceStageExecuted =
            true;

        run.surfaceCallbackConsumed =
            false;

        run.surfaceStageStartedAtEditorTime =
            EditorApplication.timeSinceStartup;

        bool started;

        try
        {
            if (
                run.mode ==
                    TerrainRuntimeBakePipelineMode.RebuildAll
            )
            {
                started =
                    TerrainSurfaceMaskCompiler.RebuildAllSurfaceMasks(
                        run.worldSettings,
                        result =>
                            OnSurfaceStageCompleted(
                                run,
                                result
                            )
                    );
            }
            else
            {
                started =
                    TerrainSurfaceMaskCompiler.GeneratePlannedSurfaceMasks(
                        run.worldSettings,
                        plan,
                        result =>
                            OnSurfaceStageCompleted(
                                run,
                                result
                            )
                    );
            }
        }
        catch
        {
            run.surfaceDurationSeconds =
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    run.surfaceStageStartedAtEditorTime
                );

            throw;
        }

        /*
         * Package 06 invokes the callback synchronously for immediate terminal
         * outcomes. A false return with no callback is therefore an unexpected
         * contract failure and must not leave the pipeline hanging.
         */
        if (
            !started
            &&
            !run.surfaceCallbackConsumed
            &&
            IsActive(run)
        )
        {
            run.surfaceDurationSeconds =
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    run.surfaceStageStartedAtEditorTime
                );

            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.SurfaceMasks,
                "Runtime surface-mask generation did not start and produced no terminal result."
            );
        }
    }

    private static void OnSurfaceStageCompleted(
        ActiveRun run,
        TerrainSurfaceMaskGenerationResult result
    )
    {
        if (
            !IsActive(run)
            ||
            run.surfaceCallbackConsumed
        )
        {
            return;
        }

        run.surfaceCallbackConsumed =
            true;

        run.surfaceDurationSeconds =
            Math.Max(
                0d,
                EditorApplication.timeSinceStartup -
                run.surfaceStageStartedAtEditorTime
            );

        run.surfaceResult =
            result;

        if (result == null)
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.SurfaceMasks,
                "Runtime surface-mask generation returned no result."
            );

            return;
        }

        switch (result.Outcome)
        {
            case TerrainSurfaceMaskGenerationOutcome.Completed:
            case TerrainSurfaceMaskGenerationOutcome.NoWork:
                Schedule(
                    run,
                    ExecuteCollisionStage
                );
                return;

            case TerrainSurfaceMaskGenerationOutcome.Cancelled:
                FinishCancelled(
                    run,
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime surface-mask generation was cancelled."
                    )
                );
                return;

            case TerrainSurfaceMaskGenerationOutcome.Blocked:
                FinishBlocked(
                    run,
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime surface-mask generation was blocked."
                    )
                );
                return;

            default:
                FinishFailed(
                    run,
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime surface-mask generation did not complete. Outcome: " +
                        result.Outcome
                    )
                );
                return;
        }
    }

    // =====================================================
    // COLLISION
    // =====================================================

    private static void ExecuteCollisionStage(
        ActiveRun run
    )
    {
        SetState(
            run,
            TerrainRuntimeBakePipelineState.Collision,
            true
        );

        if (!PrepareStageBoundary(run))
        {
            return;
        }

        TerrainRuntimeBakePlan plan =
            BuildFreshPlan(
                run
            );

        run.collisionPlan =
            plan;

        if (!ValidateStagePlan(run, plan))
        {
            return;
        }

        if (
            TerrainRuntimeBakeValidationHooks.TryConsumeFailureBeforeStage(
                TerrainRuntimeBakePipelineState.Collision,
                out string validationFailureMessage
            )
        )
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.Collision,
                validationFailureMessage
            );
            return;
        }

        if (
            run.mode ==
                TerrainRuntimeBakePipelineMode.PendingChanges
            &&
            plan.CollisionWorkMode ==
                TerrainRuntimeBakeWorkMode.None
        )
        {
            Schedule(
                run,
                ExecuteAddressablesStage
            );

            return;
        }

        run.collisionStageExecuted =
            true;

        double stageStartedAt =
            EditorApplication.timeSinceStartup;

        TerrainCollisionGenerationResult result;

        try
        {
            if (
                run.mode ==
                    TerrainRuntimeBakePipelineMode.RebuildAll
            )
            {
                result =
                    TerrainCollisionMeshGenerator.RebuildAllCollisionMeshes(
                        run.worldSettings
                    );
            }
            else
            {
                result =
                    TerrainCollisionMeshGenerator.GeneratePlannedCollisionMeshes(
                        run.worldSettings,
                        plan
                    );
            }
        }
        finally
        {
            run.collisionDurationSeconds =
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    stageStartedAt
                );
        }

        run.collisionResult =
            result;

        if (result == null)
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.Collision,
                "Runtime collision generation returned no result."
            );

            return;
        }

        switch (result.Outcome)
        {
            case TerrainCollisionGenerationOutcome.Completed:
            case TerrainCollisionGenerationOutcome.NoWork:
                Schedule(
                    run,
                    ExecuteAddressablesStage
                );
                return;

            case TerrainCollisionGenerationOutcome.Cancelled:
                FinishCancelled(
                    run,
                    TerrainRuntimeBakePipelineState.Collision,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime collision generation was cancelled."
                    )
                );
                return;

            case TerrainCollisionGenerationOutcome.Blocked:
                FinishBlocked(
                    run,
                    TerrainRuntimeBakePipelineState.Collision,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime collision generation was blocked."
                    )
                );
                return;

            default:
                FinishFailed(
                    run,
                    TerrainRuntimeBakePipelineState.Collision,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime collision generation did not complete. Outcome: " +
                        result.Outcome
                    )
                );
                return;
        }
    }

    // =====================================================
    // ADDRESSABLES
    // =====================================================

    private static void ExecuteAddressablesStage(
        ActiveRun run
    )
    {
        SetState(
            run,
            TerrainRuntimeBakePipelineState.Addressables,
            true
        );

        if (!PrepareStageBoundary(run))
        {
            return;
        }

        TerrainRuntimeBakePlan plan =
            BuildFreshPlan(
                run
            );

        run.addressablesPlan =
            plan;

        if (!ValidateStagePlan(run, plan))
        {
            return;
        }

        if (
            TerrainRuntimeBakeValidationHooks.TryConsumeFailureBeforeStage(
                TerrainRuntimeBakePipelineState.Addressables,
                out string validationFailureMessage
            )
        )
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.Addressables,
                validationFailureMessage
            );
            return;
        }

        if (
            !plan.AddressablesConfigurationRequired
            &&
            !plan.AddressablesContentBuildRequired
        )
        {
            Schedule(
                run,
                ExecuteSceneSyncStage
            );

            return;
        }

        run.addressablesStageExecuted =
            true;

        if (
            TerrainRuntimeBakeValidationHooks
                .ShouldRequestCancelDuringAddressables()
        )
        {
            /*
             * Package 10.2 validation models a cancellation request while the
             * synchronous Addressables operation is active. The current safe
             * operation still completes; Package 08 consumes cancellation at
             * the next stage boundary rather than aborting BuildPlayerContent.
             */
            run.cancelRequested = true;
        }

        double stageStartedAt =
            EditorApplication.timeSinceStartup;

        TerrainRuntimeAddressablesResult result;

        try
        {
            result =
                TerrainRuntimeAddressablesUtility.ProcessPlannedRuntimeContent(
                    run.worldSettings,
                    plan
                );
        }
        finally
        {
            run.addressablesDurationSeconds =
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    stageStartedAt
                );
        }

        run.addressablesResult =
            result;

        if (result == null)
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.Addressables,
                "Runtime Addressables processing returned no result."
            );

            return;
        }

        switch (result.Outcome)
        {
            case TerrainRuntimeAddressablesOutcome.Completed:
            case TerrainRuntimeAddressablesOutcome.NoWork:
                Schedule(
                    run,
                    ExecuteSceneSyncStage
                );
                return;

            case TerrainRuntimeAddressablesOutcome.Cancelled:
                FinishCancelled(
                    run,
                    TerrainRuntimeBakePipelineState.Addressables,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime Addressables processing was cancelled."
                    )
                );
                return;

            case TerrainRuntimeAddressablesOutcome.Blocked:
                FinishBlocked(
                    run,
                    TerrainRuntimeBakePipelineState.Addressables,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime Addressables processing was blocked."
                    )
                );
                return;

            default:
                FinishFailed(
                    run,
                    TerrainRuntimeBakePipelineState.Addressables,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime Addressables processing did not complete. Outcome: " +
                        result.Outcome
                    )
                );
                return;
        }
    }

    // =====================================================
    // SCENE SYNC
    // =====================================================

    private static void ExecuteSceneSyncStage(
        ActiveRun run
    )
    {
        SetState(
            run,
            TerrainRuntimeBakePipelineState.SceneSync,
            true
        );

        if (
            TerrainRuntimeBakeValidationHooks
                .ShouldRequestCancelBeforeSceneSync()
        )
        {
            run.cancelRequested = true;
        }

        if (!PrepareStageBoundary(run))
        {
            return;
        }

        TerrainRuntimeBakePlan plan =
            BuildFreshPlan(
                run
            );

        run.sceneSyncPlan =
            plan;

        if (!ValidateStagePlan(run, plan))
        {
            return;
        }

        if (
            TerrainRuntimeBakeValidationHooks.TryConsumeFailureBeforeStage(
                TerrainRuntimeBakePipelineState.SceneSync,
                out string validationFailureMessage
            )
        )
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.SceneSync,
                validationFailureMessage
            );
            return;
        }

        if (
            run.mode ==
                TerrainRuntimeBakePipelineMode.PendingChanges
            &&
            !plan.RuntimeSceneMetadataUpdateRequired
        )
        {
            Schedule(
                run,
                FinalizeSuccessfulRun
            );

            return;
        }

        run.sceneSyncStageExecuted =
            true;

        double stageStartedAt =
            EditorApplication.timeSinceStartup;

        TerrainRuntimeSceneSynchronizationResult result;

        try
        {
            result =
                run.mode == TerrainRuntimeBakePipelineMode.RebuildAll
                    ? TerrainRuntimeSceneSynchronizer.SynchronizeExistingHierarchy(
                        run.worldSettings
                    )
                    : TerrainRuntimeSceneSynchronizer.SynchronizePending(
                        run.worldSettings
                    );
        }
        finally
        {
            run.sceneSyncDurationSeconds =
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    stageStartedAt
                );
        }

        run.sceneSyncResult =
            result;

        if (result == null)
        {
            FinishFailed(
                run,
                TerrainRuntimeBakePipelineState.SceneSync,
                "Runtime scene synchronization returned no result."
            );

            return;
        }

        switch (result.Outcome)
        {
            case TerrainRuntimeSceneSynchronizationOutcome.NoWork:
            case TerrainRuntimeSceneSynchronizationOutcome.Completed:
                Schedule(
                    run,
                    FinalizeSuccessfulRun
                );
                return;

            case TerrainRuntimeSceneSynchronizationOutcome.CompletedWithWarnings:
                run.completedWithWarnings =
                    true;

                AddWarnings(
                    run,
                    result.WarningMessages
                );

                Schedule(
                    run,
                    FinalizeSuccessfulRun
                );
                return;

            case TerrainRuntimeSceneSynchronizationOutcome.RepairRequired:
                FinishFailed(
                    run,
                    TerrainRuntimeBakePipelineState.SceneSync,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime data was generated successfully, but the existing runtime scene hierarchy requires structural repair. Run Setup / Repair World Hierarchy."
                    )
                );
                return;

            case TerrainRuntimeSceneSynchronizationOutcome.Blocked:
                FinishBlocked(
                    run,
                    TerrainRuntimeBakePipelineState.SceneSync,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime scene synchronization was blocked."
                    )
                );
                return;

            default:
                FinishFailed(
                    run,
                    TerrainRuntimeBakePipelineState.SceneSync,
                    GetStageMessage(
                        result.ErrorMessage,
                        result.SummaryMessage,
                        "Runtime scene synchronization failed."
                    )
                );
                return;
        }
    }

    // =====================================================
    // FINAL VALIDATION
    // =====================================================

    private static void FinalizeSuccessfulRun(
        ActiveRun run
    )
    {
        SetState(
            run,
            TerrainRuntimeBakePipelineState.Finalizing,
            false
        );

        if (!PrepareStageBoundary(run))
        {
            return;
        }

        TerrainRuntimeBakePlan finalPlan =
            BuildFreshPlan(
                run
            );

        run.finalPlan =
            finalPlan;

        if (finalPlan == null)
        {
            FinishFailed(
                run,
                run.lastStage,
                "Final runtime bake validation could not build a current bake plan."
            );

            return;
        }

        if (finalPlan.IsBlocked)
        {
            FinishFailed(
                run,
                run.lastStage,
                "Final runtime bake validation is blocked: " +
                finalPlan.BlockReason
            );

            return;
        }

        if (finalPlan.HasWork)
        {
            FinishFailed(
                run,
                run.lastStage,
                "Unified runtime bake stages completed, but current runtime bake work still remains: " +
                TerrainRuntimeBakePipelineResult.BuildPlanSummary(
                    finalPlan
                )
            );

            return;
        }

        TerrainRuntimeBakePipelineOutcome outcome =
            run.completedWithWarnings
                ? TerrainRuntimeBakePipelineOutcome.CompletedWithWarnings
                : TerrainRuntimeBakePipelineOutcome.Completed;

        Finish(
            run,
            outcome,
            TerrainRuntimeBakePipelineState.Completed,
            TerrainRuntimeBakePipelineState.Idle,
            "",
            run.completedWithWarnings
                ? "Runtime changes were baked successfully with scene synchronization warnings."
                : "Runtime changes were baked successfully."
        );
    }

    // =====================================================
    // PLAN / STAGE HELPERS
    // =====================================================

    private static TerrainRuntimeBakePlan BuildFreshPlan(
        ActiveRun run
    )
    {
        if (
            run == null
            ||
            run.worldSettings == null
            ||
            run.authoringData == null
        )
        {
            return null;
        }

        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner.BuildPlan(
                run.worldSettings,
                run.authoringData
            );

        run.currentPlan =
            plan;


        return plan;
    }

    private static bool ValidateStagePlan(
        ActiveRun run,
        TerrainRuntimeBakePlan plan
    )
    {
        if (plan == null)
        {
            FinishFailed(
                run,
                run.lastStage,
                "Could not build a fresh runtime bake plan before the current stage."
            );

            return false;
        }

        if (plan.IsBlocked)
        {
            FinishBlocked(
                run,
                run.lastStage,
                string.IsNullOrEmpty(plan.BlockReason)
                    ? "The current runtime bake plan is blocked."
                    : plan.BlockReason
            );

            return false;
        }

        return true;
    }

    private static bool PrepareStageBoundary(
        ActiveRun run
    )
    {
        if (!IsActive(run))
        {
            return false;
        }

        if (run.cancelRequested)
        {
            FinishCancelled(
                run,
                run.lastStage,
                "Unified runtime bake cancellation was requested before the next stage began."
            );

            return false;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            FinishBlocked(
                run,
                run.lastStage,
                "Unified runtime baking stopped because Unity entered or began entering Play Mode."
            );

            return false;
        }

        return true;
    }

    private static void Schedule(
        ActiveRun run,
        Action<ActiveRun> action
    )
    {
        EditorApplication.delayCall +=
            () =>
            {
                if (!IsActive(run))
                {
                    return;
                }

                try
                {
                    action(
                        run
                    );
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        exception
                    );

                    FinishFailed(
                        run,
                        run.lastStage,
                        "Unexpected exception during unified runtime bake: " +
                        exception.Message
                    );
                }
            };
    }

    private static bool IsActive(
        ActiveRun run
    )
    {
        return
            run != null
            &&
            !run.terminal
            &&
            ReferenceEquals(
                activeRun,
                run
            );
    }

    private static void SetState(
        ActiveRun run,
        TerrainRuntimeBakePipelineState state,
        bool markAsLastStage
    )
    {
        if (!IsActive(run))
        {
            return;
        }

        currentState =
            state;

        currentMode =
            run.mode;

        if (markAsLastStage)
        {
            run.lastStage =
                state;
        }

    }

    // =====================================================
    // TERMINAL RESULTS
    // =====================================================

    private static void FinishCancelled(
        ActiveRun run,
        TerrainRuntimeBakePipelineState stage,
        string message
    )
    {
        Finish(
            run,
            TerrainRuntimeBakePipelineOutcome.Cancelled,
            TerrainRuntimeBakePipelineState.Cancelled,
            stage,
            "",
            message
        );
    }

    private static void FinishBlocked(
        ActiveRun run,
        TerrainRuntimeBakePipelineState stage,
        string message
    )
    {
        Finish(
            run,
            TerrainRuntimeBakePipelineOutcome.Blocked,
            TerrainRuntimeBakePipelineState.Blocked,
            stage,
            message,
            "Unified runtime baking was blocked before all required stages completed."
        );
    }

    private static void FinishFailed(
        ActiveRun run,
        TerrainRuntimeBakePipelineState stage,
        string message
    )
    {
        Finish(
            run,
            TerrainRuntimeBakePipelineOutcome.Failed,
            TerrainRuntimeBakePipelineState.Failed,
            stage,
            message,
            "Unified runtime baking stopped during " +
            GetStageLabel(stage) +
            ". Successfully completed earlier work was preserved."
        );
    }

    private static void Finish(
        ActiveRun run,
        TerrainRuntimeBakePipelineOutcome outcome,
        TerrainRuntimeBakePipelineState terminalState,
        TerrainRuntimeBakePipelineState failedStage,
        string errorMessage,
        string summaryMessage
    )
    {
        if (!IsActive(run))
        {
            return;
        }

        run.terminal =
            true;

        if (
            run.finalPlan == null
            &&
            run.worldSettings != null
            &&
            run.authoringData != null
        )
        {
            try
            {
                run.finalPlan =
                    TerrainRuntimeBakePlanner.BuildPlan(
                        run.worldSettings,
                        run.authoringData
                    );
            }
            catch (Exception exception)
            {
                Debug.LogException(
                    exception
                );
            }
        }

        double duration =
            Math.Max(
                0d,
                EditorApplication.timeSinceStartup -
                run.startedAtEditorTime
            );

        TerrainRuntimeBakePipelineResult result =
            new TerrainRuntimeBakePipelineResult(
                outcome,
                run.mode,
                terminalState,
                run.lastStage,
                failedStage,
                run.initialPlan,
                run.heightPlan,
                run.surfacePlan,
                run.collisionPlan,
                run.addressablesPlan,
                run.sceneSyncPlan,
                run.finalPlan,
                run.heightStageExecuted,
                run.surfaceStageExecuted,
                run.collisionStageExecuted,
                run.addressablesStageExecuted,
                run.sceneSyncStageExecuted,
                run.heightResult,
                run.surfaceResult,
                run.collisionResult,
                run.addressablesResult,
                run.sceneSyncResult,
                run.startedAtUtc,
                run.heightDurationSeconds,
                run.surfaceDurationSeconds,
                run.collisionDurationSeconds,
                run.addressablesDurationSeconds,
                run.sceneSyncDurationSeconds,
                duration,
                run.warnings,
                errorMessage,
                summaryMessage
            );

        Action<TerrainRuntimeBakePipelineResult> callback =
            run.completionCallback;

        activeRun =
            null;

        currentState =
            terminalState;

        currentMode =
            run.mode;

        lastResult =
            result;


        if (callback != null)
        {
            try
            {
                callback(
                    result
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(
                    exception
                );
            }
        }
    }

    private static void PublishImmediateBlockedResult(
        TerrainRuntimeBakePipelineMode mode,
        Action<TerrainRuntimeBakePipelineResult> callback,
        string errorMessage
    )
    {
        TerrainRuntimeBakePipelineResult result =
            CreateImmediateBlockedResult(
                mode,
                errorMessage
            );

        currentMode =
            mode;

        currentState =
            TerrainRuntimeBakePipelineState.Blocked;

        lastResult =
            result;


        if (callback != null)
        {
            try
            {
                callback(
                    result
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(
                    exception
                );
            }
        }
    }

    private static void InvokeRejectedCallback(
        TerrainRuntimeBakePipelineMode mode,
        Action<TerrainRuntimeBakePipelineResult> callback,
        string errorMessage
    )
    {
        if (callback == null)
        {
            return;
        }

        try
        {
            callback(
                CreateImmediateBlockedResult(
                    mode,
                    errorMessage
                )
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(
                exception
            );
        }
    }

    private static TerrainRuntimeBakePipelineResult CreateImmediateBlockedResult(
        TerrainRuntimeBakePipelineMode mode,
        string errorMessage
    )
    {
        return
            new TerrainRuntimeBakePipelineResult(
                TerrainRuntimeBakePipelineOutcome.Blocked,
                mode,
                TerrainRuntimeBakePipelineState.Blocked,
                TerrainRuntimeBakePipelineState.Preflight,
                TerrainRuntimeBakePipelineState.Preflight,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                DateTime.UtcNow,
                0d,
                0d,
                0d,
                0d,
                0d,
                0d,
                null,
                errorMessage,
                "Unified runtime bake start was blocked."
            );
    }

    // =====================================================
    // MISC HELPERS
    // =====================================================

    private static string GetStageMessage(
        string errorMessage,
        string summaryMessage,
        string fallback
    )
    {
        if (!string.IsNullOrEmpty(errorMessage))
        {
            return errorMessage;
        }

        if (!string.IsNullOrEmpty(summaryMessage))
        {
            return summaryMessage;
        }

        return fallback;
    }

    private static void AddWarnings(
        ActiveRun run,
        IReadOnlyList<string> warnings
    )
    {
        if (
            run == null
            ||
            warnings == null
        )
        {
            return;
        }

        for (
            int index = 0;
            index < warnings.Count;
            index++
        )
        {
            string warning =
                warnings[index];

            if (!string.IsNullOrEmpty(warning))
            {
                run.warnings.Add(
                    warning
                );
            }
        }
    }

    private static string GetStageLabel(
        TerrainRuntimeBakePipelineState stage
    )
    {
        switch (stage)
        {
            case TerrainRuntimeBakePipelineState.Heightmaps:
                return "Runtime Heightmaps";

            case TerrainRuntimeBakePipelineState.SurfaceMasks:
                return "Surface Masks";

            case TerrainRuntimeBakePipelineState.Collision:
                return "Collision Meshes";

            case TerrainRuntimeBakePipelineState.Addressables:
                return "Addressables";

            case TerrainRuntimeBakePipelineState.SceneSync:
                return "Runtime Scene Synchronization";

            case TerrainRuntimeBakePipelineState.Preflight:
                return "Preflight";

            default:
                return stage.ToString();
        }
    }

}
