using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public enum TerrainRuntimeEquivalencePhase
{
    Idle,
    AwaitingMutation,
    BakingNormal,
    CapturingNormal,
    RebuildingFull,
    CapturingFull,
    Comparing,
    Completed
}

public static class TerrainRuntimeEquivalenceScenarioRunner
{
    private sealed class ActiveRun
    {
        public TerrainRuntimeEquivalenceScenario scenario;

        public TerrainRuntimeEquivalenceBaseline baseline;

        public TerrainRuntimeEquivalenceSourceIdentity
            expectedSourceIdentity;

        public TerrainRuntimeEquivalenceValidationResult
            result;

        public Action<
            TerrainRuntimeEquivalenceValidationResult
        > completionCallback;
    }

    private static TerrainRuntimeEquivalenceBaseline baseline;

    private static ActiveRun activeRun;

    private static TerrainRuntimeEquivalenceValidationResult lastResult;

    private static TerrainRuntimeEquivalencePhase phase =
        TerrainRuntimeEquivalencePhase.Idle;

    public static TerrainRuntimeEquivalenceBaseline Baseline =>
        baseline;

    public static bool HasBaseline =>
        baseline != null;

    public static bool IsRunning =>
        activeRun != null;

    public static TerrainRuntimeEquivalencePhase Phase =>
        phase;

    public static TerrainRuntimeEquivalenceValidationResult LastResult =>
        lastResult;

    public static string PhaseLabel
    {
        get
        {
            switch (phase)
            {
                case TerrainRuntimeEquivalencePhase.AwaitingMutation:
                    return "Awaiting Representative Mutation";

                case TerrainRuntimeEquivalencePhase.BakingNormal:
                    return "Normal Runtime Bake";

                case TerrainRuntimeEquivalencePhase.CapturingNormal:
                    return "Capturing Complete Normal Dataset";

                case TerrainRuntimeEquivalencePhase.RebuildingFull:
                    return "Forced Full Rebuild";

                case TerrainRuntimeEquivalencePhase.CapturingFull:
                    return "Capturing Complete Full Dataset";

                case TerrainRuntimeEquivalencePhase.Comparing:
                    return "Comparing Complete Datasets";

                case TerrainRuntimeEquivalencePhase.Completed:
                    return "Completed";

                default:
                    return "Idle";
            }
        }
    }

    public static bool CaptureBaseline(
        TerrainRuntimeEquivalenceScenario scenario,
        out string message
    )
    {
        message = "";

        if (IsRunning)
        {
            message =
                "A Package 10.5 equivalence run is already active.";
            return false;
        }

        if (!UsesBaselineWorkflow(scenario))
        {
            message =
                "Select a representative authoring/settings scenario before capturing an equivalence baseline.";
            return false;
        }

        if (
            TerrainRuntimeEquivalenceValidationUtility
                .HasConflictingValidationActivity(
                    out message
                )
        )
        {
            return false;
        }

        TerrainRuntimeReadinessResult readiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        if (
            readiness == null
            || !readiness.IsReady
        )
        {
            message =
                readiness != null
                    ? "Capture the Package 10.5 baseline from Runtime Ready.\n\n" +
                        readiness.ErrorMessage
                    : "Runtime readiness could not be evaluated.";
            return false;
        }

        TerrainRuntimeEquivalenceSourceIdentity identity =
            TerrainRuntimeEquivalenceSnapshotUtility
                .CaptureSourceIdentity();

        if (identity == null)
        {
            message =
                "Could not capture the Package 10.5 source identity.";
            return false;
        }

        baseline =
            new TerrainRuntimeEquivalenceBaseline(
                scenario,
                identity
            );

        phase =
            TerrainRuntimeEquivalencePhase.AwaitingMutation;

        message =
            "Equivalence baseline captured for " +
            scenario +
            ". Make the representative change, do not bake it manually, then run Incremental + Full Equivalence.";

        return true;
    }

    public static bool RunIncrementalFullEquivalence(
        Action<TerrainRuntimeEquivalenceValidationResult>
            onCompleted = null
    )
    {
        if (IsRunning)
        {
            return false;
        }

        if (baseline == null)
        {
            lastResult =
                CreateBlockedResult(
                    TerrainRuntimeEquivalenceScenario.None,
                    "Capture an equivalence baseline before running a representative Incremental / Full comparison."
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        if (
            TerrainRuntimeEquivalenceValidationUtility
                .HasConflictingValidationActivity(
                    out string activityError
                )
        )
        {
            lastResult =
                CreateBlockedResult(
                    baseline.Scenario,
                    activityError
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        TerrainRuntimeEquivalenceSourceIdentity currentIdentity =
            TerrainRuntimeEquivalenceSnapshotUtility
                .CaptureSourceIdentity();

        TerrainRuntimeBakePlan currentPlan =
            TerrainRuntimeBakePlanner
                .BuildCurrentPlan();

        if (
            !TerrainRuntimeEquivalenceValidationUtility
                .TryValidateMutationAndPlan(
                    baseline,
                    currentIdentity,
                    currentPlan,
                    out string validationError
                )
        )
        {
            lastResult =
                CreateBlockedResult(
                    baseline.Scenario,
                    validationError
                );

            lastResult.Baseline =
                baseline;

            lastResult.NormalBakePlan =
                currentPlan;

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        ActiveRun run =
            new ActiveRun
            {
                scenario = baseline.Scenario,
                baseline = baseline,
                result =
                    new TerrainRuntimeEquivalenceValidationResult(
                        baseline.Scenario
                    ),
                completionCallback =
                    onCompleted
            };

        run.result.Baseline =
            baseline;

        run.result.NormalBakePlan =
            currentPlan;

        activeRun =
            run;

        phase =
            TerrainRuntimeEquivalencePhase.BakingNormal;

        bool started =
            TerrainRuntimeBakeValidationUtility
                .BakePendingChanges(
                    validation =>
                    {
                        if (
                            activeRun != run
                        )
                        {
                            return;
                        }

                        run.result.FirstBakeValidation =
                            validation;

                        if (
                            !TerrainRuntimeEquivalenceValidationUtility
                                .IsBakeValidationSuccessful(
                                    validation
                                )
                        )
                        {
                            FinishFailureFromBake(
                                run,
                                validation,
                                "The normal runtime bake was not certified by Package 10.1."
                            );
                            return;
                        }

                        phase =
                            TerrainRuntimeEquivalencePhase.CapturingNormal;

                        TerrainRuntimeEquivalenceSnapshot normalSnapshot =
                            TerrainRuntimeEquivalenceSnapshotUtility
                                .CaptureCompleteSnapshot(
                                    "Normal Bake"
                                );

                        run.result.FirstSnapshot =
                            normalSnapshot;

                        if (
                            normalSnapshot == null
                            || normalSnapshot.WasCancelled
                        )
                        {
                            FinishCancelled(
                                run,
                                "Complete normal-dataset fingerprint capture was cancelled."
                            );
                            return;
                        }

                        if (!normalSnapshot.IsComplete)
                        {
                            FinishFailure(
                                run,
                                "The complete normal-bake snapshot is incomplete. Full comparison was not started."
                            );
                            return;
                        }

                        run.expectedSourceIdentity =
                            normalSnapshot
                                .SourceIdentity;

                        TerrainRuntimeEquivalenceSourceIdentity beforeFull =
                            TerrainRuntimeEquivalenceSnapshotUtility
                                .CaptureSourceIdentity();

                        List<string> beforeFullDifferences =
                            new List<string>();

                        bool beforeFullMatches =
                            run.expectedSourceIdentity != null
                            && beforeFull != null
                            && run.expectedSourceIdentity
                                .ExactMatch(
                                    beforeFull,
                                    out beforeFullDifferences
                                );

                        if (!beforeFullMatches)
                        {
                            run.result
                                .SetSourceIdentityDifferences(
                                    beforeFullDifferences
                                );

                            FinishInvalidated(
                                run,
                                "Authoring or generation settings changed after the normal snapshot and before Full rebuild."
                            );
                            return;
                        }

                        run.result.Outcome =
                            TerrainRuntimeEquivalenceValidationOutcome
                                .IncrementalCaptured;

                        StartFullRebuild(
                            run
                        );
                    }
                );

        /*
         * Package 10.1 can reject synchronously and invoke the completion
         * callback before returning. Keep the return value truthful without
         * resurrecting an already-finished ActiveRun.
         */
        if (
            !started
            && activeRun == run
        )
        {
            TerrainRuntimeBakeValidationResult existing =
                TerrainRuntimeBakeValidationUtility
                    .LastResult;

            run.result.FirstBakeValidation =
                existing;

            FinishFailureFromBake(
                run,
                existing,
                "The normal runtime bake could not start."
            );
        }

        return started;
    }

    public static bool RunInitialBakeEquivalence(
        Action<TerrainRuntimeEquivalenceValidationResult>
            onCompleted = null
    )
    {
        if (IsRunning)
        {
            return false;
        }

        if (
            TerrainRuntimeEquivalenceValidationUtility
                .HasConflictingValidationActivity(
                    out string activityError
                )
        )
        {
            lastResult =
                CreateBlockedResult(
                    TerrainRuntimeEquivalenceScenario.InitialBake,
                    activityError
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner
                .BuildCurrentPlan();

        if (
            plan == null
            || plan.IsBlocked
            || !plan.IsInitialBake
            || !plan.HasWork
        )
        {
            lastResult =
                CreateBlockedResult(
                    TerrainRuntimeEquivalenceScenario.InitialBake,
                    "Initial Bake equivalence is available only when the canonical planner is already in a legitimate Initial Bake state. Package 10.5 will not delete a healthy generated runtime dataset merely to create this condition."
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        TerrainRuntimeEquivalenceSourceIdentity sourceIdentity =
            TerrainRuntimeEquivalenceSnapshotUtility
                .CaptureSourceIdentity();

        if (sourceIdentity == null)
        {
            lastResult =
                CreateBlockedResult(
                    TerrainRuntimeEquivalenceScenario.InitialBake,
                    "Could not capture initial authoring/settings identity."
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        ActiveRun run =
            new ActiveRun
            {
                scenario =
                    TerrainRuntimeEquivalenceScenario.InitialBake,
                expectedSourceIdentity =
                    sourceIdentity,
                result =
                    new TerrainRuntimeEquivalenceValidationResult(
                        TerrainRuntimeEquivalenceScenario.InitialBake
                    ),
                completionCallback =
                    onCompleted
            };

        run.result.NormalBakePlan =
            plan;

        activeRun =
            run;

        phase =
            TerrainRuntimeEquivalencePhase.BakingNormal;

        bool started =
            TerrainRuntimeBakeValidationUtility
                .BakePendingChanges(
                    validation =>
                    {
                        if (
                            activeRun != run
                        )
                        {
                            return;
                        }

                        run.result.FirstBakeValidation =
                            validation;

                        if (
                            !TerrainRuntimeEquivalenceValidationUtility
                                .IsBakeValidationSuccessful(
                                    validation
                                )
                        )
                        {
                            FinishFailureFromBake(
                                run,
                                validation,
                                "The canonical Initial Bake was not certified by Package 10.1."
                            );
                            return;
                        }

                        phase =
                            TerrainRuntimeEquivalencePhase.CapturingNormal;

                        TerrainRuntimeEquivalenceSnapshot initialSnapshot =
                            TerrainRuntimeEquivalenceSnapshotUtility
                                .CaptureCompleteSnapshot(
                                    "Initial Bake"
                                );

                        run.result.FirstSnapshot =
                            initialSnapshot;

                        if (
                            initialSnapshot == null
                            || initialSnapshot.WasCancelled
                        )
                        {
                            FinishCancelled(
                                run,
                                "Initial Bake complete-dataset fingerprint capture was cancelled."
                            );
                            return;
                        }

                        if (!initialSnapshot.IsComplete)
                        {
                            FinishFailure(
                                run,
                                "Initial Bake finished, but its complete runtime snapshot is invalid/incomplete."
                            );
                            return;
                        }

                        run.expectedSourceIdentity =
                            initialSnapshot
                                .SourceIdentity;

                        StartFullRebuild(
                            run
                        );
                    }
                );

        if (
            !started
            && activeRun == run
        )
        {
            TerrainRuntimeBakeValidationResult existing =
                TerrainRuntimeBakeValidationUtility
                    .LastResult;

            run.result.FirstBakeValidation =
                existing;

            FinishFailureFromBake(
                run,
                existing,
                "The canonical Initial Bake could not start."
            );
        }

        return started;
    }

    public static bool RunForcedFullHealthValidation(
        Action<TerrainRuntimeEquivalenceValidationResult>
            onCompleted = null
    )
    {
        if (IsRunning)
        {
            return false;
        }

        if (
            TerrainRuntimeEquivalenceValidationUtility
                .HasConflictingValidationActivity(
                    out string activityError
                )
        )
        {
            lastResult =
                CreateBlockedResult(
                    TerrainRuntimeEquivalenceScenario.ForcedFullRebuild,
                    activityError
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        TerrainRuntimeReadinessResult readiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        if (
            readiness == null
            || !readiness.IsReady
        )
        {
            lastResult =
                CreateBlockedResult(
                    TerrainRuntimeEquivalenceScenario.ForcedFullRebuild,
                    readiness != null
                        ? "Forced Full health validation starts from Runtime Ready.\n\n" +
                            readiness.ErrorMessage
                        : "Runtime readiness could not be evaluated."
                );

            Invoke(
                onCompleted,
                lastResult
            );

            return false;
        }

        ActiveRun run =
            new ActiveRun
            {
                scenario =
                    TerrainRuntimeEquivalenceScenario.ForcedFullRebuild,
                expectedSourceIdentity =
                    TerrainRuntimeEquivalenceSnapshotUtility
                        .CaptureSourceIdentity(),
                result =
                    new TerrainRuntimeEquivalenceValidationResult(
                        TerrainRuntimeEquivalenceScenario.ForcedFullRebuild
                    ),
                completionCallback =
                    onCompleted
            };

        activeRun =
            run;

        phase =
            TerrainRuntimeEquivalencePhase.RebuildingFull;

        StartFullRebuild(
            run
        );

        return true;
    }

    public static bool RequestCancel()
    {
        if (!IsRunning)
        {
            return false;
        }

        if (
            TerrainRuntimeBakePipeline
                .IsRunning
        )
        {
            return
                TerrainRuntimeBakePipeline
                    .RequestCancel();
        }

        return false;
    }

    public static bool ClearValidationState(
        out string message
    )
    {
        if (IsRunning)
        {
            message =
                "Cannot clear Package 10.5 state while validation is running.";
            return false;
        }

        baseline =
            null;

        lastResult =
            null;

        phase =
            TerrainRuntimeEquivalencePhase.Idle;

        message =
            "Package 10.5 validation state cleared.";

        return true;
    }

    private static void StartFullRebuild(
        ActiveRun run
    )
    {
        if (
            activeRun != run
        )
        {
            return;
        }

        TerrainRuntimeEquivalenceSourceIdentity currentIdentity =
            TerrainRuntimeEquivalenceSnapshotUtility
                .CaptureSourceIdentity();

        List<string> sourceDifferences =
            new List<string>();

        bool sourceMatches =
            run.expectedSourceIdentity != null
            && currentIdentity != null
            && run.expectedSourceIdentity
                .ExactMatch(
                    currentIdentity,
                    out sourceDifferences
                );

        if (!sourceMatches)
        {
            run.result
                .SetSourceIdentityDifferences(
                    sourceDifferences
                );

            FinishInvalidated(
                run,
                "Authoring or final generation settings changed before forced Full rebuild."
            );
            return;
        }

        phase =
            TerrainRuntimeEquivalencePhase.RebuildingFull;

        run.result.Outcome =
            TerrainRuntimeEquivalenceValidationOutcome
                .RebuildingFull;

        bool started =
            TerrainRuntimeBakePipeline
                .RebuildAllRuntimeData(
                    pipelineResult =>
                    {
                        if (
                            activeRun != run
                        )
                        {
                            return;
                        }

                        TerrainRuntimeBakeValidationResult validation =
                            TerrainRuntimeBakeValidationUtility
                                .ValidatePipelineResult(
                                    pipelineResult
                                );

                        run.result.FullBakeValidation =
                            validation;

                        run.result.FullExecutionCertified =
                            TerrainRuntimeEquivalenceValidationUtility
                                .IsFullExecutionCertified(
                                    validation
                                );

                        if (
                            !TerrainRuntimeEquivalenceValidationUtility
                                .IsBakeValidationSuccessful(
                                    validation
                                )
                            || !run.result
                                .FullExecutionCertified
                        )
                        {
                            FinishFailureFromBake(
                                run,
                                validation,
                                !run.result.FullExecutionCertified
                                    ? "The forced rebuild completed without proving Full Height / Surface / Collision execution over the complete coordinate spaces."
                                    : "The forced Full rebuild was not certified by Package 10.1."
                            );
                            return;
                        }

                        phase =
                            TerrainRuntimeEquivalencePhase.CapturingFull;

                        TerrainRuntimeEquivalenceSnapshot fullSnapshot =
                            TerrainRuntimeEquivalenceSnapshotUtility
                                .CaptureCompleteSnapshot(
                                    "Forced Full"
                                );

                        run.result.FullSnapshot =
                            fullSnapshot;

                        if (
                            fullSnapshot == null
                            || fullSnapshot.WasCancelled
                        )
                        {
                            FinishCancelled(
                                run,
                                "Complete Full-dataset fingerprint capture was cancelled."
                            );
                            return;
                        }

                        if (!fullSnapshot.IsComplete)
                        {
                            FinishFailure(
                                run,
                                "Forced Full rebuild completed, but its complete runtime snapshot is invalid/incomplete."
                            );
                            return;
                        }

                        List<string> fullSourceDifferences =
                            new List<string>();

                        bool fullSourceMatches =
                            run.expectedSourceIdentity != null
                            && fullSnapshot.SourceIdentity != null
                            && run.expectedSourceIdentity
                                .ExactMatch(
                                    fullSnapshot.SourceIdentity,
                                    out fullSourceDifferences
                                );

                        if (!fullSourceMatches)
                        {
                            run.result
                                .SetSourceIdentityDifferences(
                                    fullSourceDifferences
                                );

                            FinishInvalidated(
                                run,
                                "Authoring or final generation settings changed between the first snapshot and Full snapshot."
                            );
                            return;
                        }

                        if (
                            run.scenario ==
                            TerrainRuntimeEquivalenceScenario
                                .ForcedFullRebuild
                        )
                        {
                            FinishForcedFullHealth(
                                run
                            );
                            return;
                        }

                        phase =
                            TerrainRuntimeEquivalencePhase.Comparing;

                        run.result.Outcome =
                            TerrainRuntimeEquivalenceValidationOutcome
                                .Comparing;

                        TerrainRuntimeEquivalenceComparisonResult comparison =
                            TerrainRuntimeEquivalenceComparisonUtility
                                .Compare(
                                    run.result.FirstSnapshot,
                                    fullSnapshot,
                                    run.result.FirstBakeValidation
                                );

                        run.result.Comparison =
                            comparison;

                        run.result.FinalReadiness =
                            TerrainRuntimeReadinessUtility
                                .Evaluate(true);

                        bool finalHealthy =
                            IsFinalHealthValid(
                                run.result.FinalReadiness
                            );

                        bool equivalent =
                            comparison != null
                            && comparison.IsEquivalent;

                        if (
                            equivalent
                            && finalHealthy
                        )
                        {
                            bool warnings =
                                HasWarnings(
                                    run.result.FirstBakeValidation
                                )
                                || HasWarnings(
                                    run.result.FullBakeValidation
                                );

                            run.result.Outcome =
                                warnings
                                    ? TerrainRuntimeEquivalenceValidationOutcome
                                        .PassedWithWarnings
                                    : TerrainRuntimeEquivalenceValidationOutcome
                                        .Passed;

                            run.result.SummaryMessage =
                                "The complete runtime Height, Surface, Collision, and runtime-semantic Height range datasets produced by the normal bake exactly match a genuine forced Full rebuild from the same final authored/settings state.";

                            Finish(
                                run
                            );
                            return;
                        }

                        run.result.ErrorMessage =
                            !equivalent
                                ? "Complete generated runtime datasets differ between the normal bake and forced Full rebuild."
                                : "Dataset payloads match, but the final runtime health invariant failed.";

                        run.result.SummaryMessage =
                            "Incremental / Full equivalence certification failed. Preserve this report and investigate the listed coordinates before weakening comparison strictness.";

                        run.result.Outcome =
                            TerrainRuntimeEquivalenceValidationOutcome
                                .Failed;

                        Finish(
                            run
                        );
                    }
                );

        if (
            !started
            && activeRun == run
        )
        {
            TerrainRuntimeBakePipelineResult pipelineResult =
                TerrainRuntimeBakePipeline
                    .LastResult;

            TerrainRuntimeBakeValidationResult validation =
                TerrainRuntimeBakeValidationUtility
                    .ValidatePipelineResult(
                        pipelineResult
                    );

            run.result.FullBakeValidation =
                validation;

            run.result.FullExecutionCertified =
                TerrainRuntimeEquivalenceValidationUtility
                    .IsFullExecutionCertified(
                        validation
                    );

            FinishFailureFromBake(
                run,
                validation,
                "The forced Full rebuild could not start."
            );
        }
    }

    private static void FinishForcedFullHealth(
        ActiveRun run
    )
    {
        run.result.FinalReadiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        bool finalHealthy =
            IsFinalHealthValid(
                run.result.FinalReadiness
            );

        bool snapshotComplete =
            run.result.FullSnapshot != null
            && run.result.FullSnapshot.IsComplete;

        if (
            run.result.FullExecutionCertified
            && snapshotComplete
            && finalHealthy
        )
        {
            run.result.Outcome =
                HasWarnings(
                    run.result.FullBakeValidation
                )
                    ? TerrainRuntimeEquivalenceValidationOutcome
                        .PassedWithWarnings
                    : TerrainRuntimeEquivalenceValidationOutcome
                        .Passed;

            run.result.SummaryMessage =
                "Forced Full rebuild executed Full Height / Surface / Collision work, produced a complete runtime dataset, and finished Runtime Ready with no remaining work.";
        }
        else
        {
            run.result.Outcome =
                TerrainRuntimeEquivalenceValidationOutcome
                    .Failed;

            run.result.ErrorMessage =
                "Forced Full rebuild health certification failed.";

            run.result.SummaryMessage =
                "Rebuild All Runtime Data did not satisfy every Package 10.5 Full execution and final-health invariant.";
        }

        Finish(
            run
        );
    }

    private static bool IsFinalHealthValid(
        TerrainRuntimeReadinessResult readiness
    )
    {
        return
            readiness != null
            && readiness.IsReady
            && readiness.Plan != null
            && !readiness.Plan.IsBlocked
            && !readiness.Plan.HasWork
            && readiness.IntegrityAudit != null
            && readiness.IntegrityAudit.GeneratedDataValid
            && readiness.AddressablesValidation != null
            && readiness.AddressablesValidation.IsValid
            && readiness.HierarchyReadiness != null
            && readiness.HierarchyReadiness.IsReady;
    }

    private static bool HasWarnings(
        TerrainRuntimeBakeValidationResult validation
    )
    {
        return
            validation != null
            && validation.Outcome ==
                TerrainRuntimeBakeValidationOutcome
                    .PassedWithWarnings;
    }

    private static void FinishFailureFromBake(
        ActiveRun run,
        TerrainRuntimeBakeValidationResult validation,
        string message
    )
    {
        if (
            validation != null
            && validation.Outcome ==
                TerrainRuntimeBakeValidationOutcome
                    .Cancelled
        )
        {
            FinishCancelled(
                run,
                message
            );
            return;
        }

        FinishFailure(
            run,
            message +
            (
                validation != null
                && !string.IsNullOrEmpty(
                    validation.ErrorMessage
                )
                    ? "\n\n" +
                        validation.ErrorMessage
                    : ""
            )
        );
    }

    private static void FinishFailure(
        ActiveRun run,
        string error
    )
    {
        if (
            run == null
            || activeRun != run
        )
        {
            return;
        }

        run.result.Outcome =
            TerrainRuntimeEquivalenceValidationOutcome
                .Failed;

        run.result.ErrorMessage =
            error ?? "";

        run.result.FinalReadiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        run.result.SummaryMessage =
            "Package 10.5 did not certify equivalence.";

        Finish(
            run
        );
    }

    private static void FinishCancelled(
        ActiveRun run,
        string message
    )
    {
        if (
            run == null
            || activeRun != run
        )
        {
            return;
        }

        run.result.Outcome =
            TerrainRuntimeEquivalenceValidationOutcome
                .Cancelled;

        run.result.ErrorMessage =
            "";

        run.result.SummaryMessage =
            message ?? "Package 10.5 validation was cancelled.";

        run.result.FinalReadiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        Finish(
            run
        );
    }

    private static void FinishInvalidated(
        ActiveRun run,
        string message
    )
    {
        if (
            run == null
            || activeRun != run
        )
        {
            return;
        }

        run.result.Outcome =
            TerrainRuntimeEquivalenceValidationOutcome
                .ComparisonInvalidated;

        run.result.ErrorMessage =
            "";

        run.result.SummaryMessage =
            message ?? "Package 10.5 comparison source identity changed.";

        run.result.FinalReadiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        Finish(
            run
        );
    }

    private static void Finish(
        ActiveRun run
    )
    {
        if (
            run == null
            || activeRun != run
        )
        {
            return;
        }

        lastResult =
            run.result;

        activeRun =
            null;

        phase =
            TerrainRuntimeEquivalencePhase.Completed;

        /*
         * The pre-mutation baseline has served its purpose once a run reaches
         * a terminal result. Require an explicit fresh baseline for the next
         * representative authoring/settings scenario.
         */
        if (
            run.scenario !=
                TerrainRuntimeEquivalenceScenario
                    .InitialBake
            && run.scenario !=
                TerrainRuntimeEquivalenceScenario
                    .ForcedFullRebuild
        )
        {
            baseline =
                null;
        }

        Invoke(
            run.completionCallback,
            lastResult
        );
    }

    private static TerrainRuntimeEquivalenceValidationResult
        CreateBlockedResult(
            TerrainRuntimeEquivalenceScenario scenario,
            string message
        )
    {
        TerrainRuntimeEquivalenceValidationResult result =
            new TerrainRuntimeEquivalenceValidationResult(
                scenario
            );

        result.Outcome =
            TerrainRuntimeEquivalenceValidationOutcome
                .Blocked;

        result.ErrorMessage =
            message ?? "";

        result.SummaryMessage =
            "Package 10.5 validation did not run.";

        return result;
    }

    private static void Invoke(
        Action<TerrainRuntimeEquivalenceValidationResult> callback,
        TerrainRuntimeEquivalenceValidationResult result
    )
    {
        if (callback == null)
        {
            return;
        }

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

    private static bool UsesBaselineWorkflow(
        TerrainRuntimeEquivalenceScenario scenario
    )
    {
        switch (scenario)
        {
            case TerrainRuntimeEquivalenceScenario.SingleLocalEdit:
            case TerrainRuntimeEquivalenceScenario.MultiTileEdit:
            case TerrainRuntimeEquivalenceScenario.ModifierMove:
            case TerrainRuntimeEquivalenceScenario.ModifierDelete:
            case TerrainRuntimeEquivalenceScenario.ModifierEnable:
            case TerrainRuntimeEquivalenceScenario.ModifierDisable:
            case TerrainRuntimeEquivalenceScenario.MultipleEditsBeforeOneBake:
            case TerrainRuntimeEquivalenceScenario.SurfaceSettingsChange:
            case TerrainRuntimeEquivalenceScenario.CollisionResolutionChange:
                return true;

            default:
                return false;
        }
    }
}
