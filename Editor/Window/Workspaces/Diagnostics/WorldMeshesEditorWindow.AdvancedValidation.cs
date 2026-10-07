using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showAdvancedValidation;

    [SerializeField]
    private bool showAdvancedRuntimeBakeValidation;

    [SerializeField]
    private bool showAdvancedLiveStampValidation;

    private void DrawAdvancedValidation()
    {
        showAdvancedValidation =
            EditorGUILayout.Foldout(
                showAdvancedValidation,
                "Advanced Validation",
                true
            );

        if (!showAdvancedValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawAdvancedRuntimeBakeValidation();

        DrawWorkspaceSectionGap();

        DrawAdvancedLiveStampValidation();
    }

    private void DrawAdvancedRuntimeBakeValidation()
    {
        showAdvancedRuntimeBakeValidation =
            EditorGUILayout.Foldout(
                showAdvancedRuntimeBakeValidation,
                "Runtime Bake",
                true
            );

        if (!showAdvancedRuntimeBakeValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeBakeExecutionValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeInvalidationValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakePersistenceResumeValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeFaultRecoveryValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeEquivalenceValidation();
    }

    private void DrawAdvancedLiveStampValidation()
    {
        showAdvancedLiveStampValidation =
            EditorGUILayout.Foldout(
                showAdvancedLiveStampValidation,
                "Live Stamp Integration",
                true
            );

        if (!showAdvancedLiveStampValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawLiveStampValidationSettings();
    }

    // =====================================================
    // RUNTIME BAKE EXECUTION
    // =====================================================

    [SerializeField]
    private bool showRuntimeBakeExecutionValidation;

    private TerrainRuntimeBakeValidationResult lastRuntimePipelineValidationResult;

    private void DrawRuntimeBakeExecutionValidation()
    {
        showRuntimeBakeExecutionValidation =
            EditorGUILayout.Foldout(
                showRuntimeBakeExecutionValidation,
                "Bake Execution",
                true
            );

        if (!showRuntimeBakeExecutionValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainRuntimeBakePlan currentPlan =
            GetRuntimeBakeDiagnosticsPlan();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Bake Execution Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Validates the observed stage execution of the most recent canonical Runtime Bake result against its captured stage plans. Normal Runtime baking remains owned by the Runtime workspace.",
            MessageType.None
        );

        EditorGUILayout.LabelField(
            "Current Bake Plan",
            TerrainRuntimeBakePipelineResult.BuildPlanSummary(currentPlan)
        );

        EditorGUI.BeginDisabledGroup(
            TerrainRuntimeBakePipeline.IsRunning
            || TerrainRuntimeBakePipeline.LastResult == null
        );

        if (
            GUILayout.Button(
                "Validate Last Runtime Bake Result",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimePipelineValidationResult =
                TerrainRuntimeBakeValidationUtility.ValidatePipelineResult(
                    TerrainRuntimeBakePipeline.LastResult
                );

            LogRuntimePipelineValidationResult(
                lastRuntimePipelineValidationResult
            );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        DrawRuntimePipelineValidationResultSummary();
        DrawRuntimePipelineTimingSummary();

        if (
            TerrainRuntimeBakePipeline.IsRunning
        )
        {
            Repaint();
        }

        GUILayout.EndVertical();
    }

    private void DrawRuntimePipelineValidationResultSummary()
    {
        TerrainRuntimeBakeValidationResult result =
            lastRuntimePipelineValidationResult
            ?? TerrainRuntimeBakeValidationUtility.LastResult;

        if (result == null)
        {
            return;
        }

        GUILayout.Space(8f);
        GUILayout.Label("Last Validation", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Outcome", result.Outcome.ToString());
        EditorGUILayout.LabelField("Final Plan Current", result.FinalPlanCurrent ? "Yes" : "No");

        DrawRuntimeStageValidationRow("Heightmaps", result.HeightValidation);
        DrawRuntimeStageValidationRow("Height Streaming", result.HeightStreamingValidation);
        DrawRuntimeStageValidationRow("Surface Masks", result.SurfaceValidation);
        DrawRuntimeStageValidationRow("Collision", result.CollisionValidation);

        if (result.AddressablesValidation != null)
        {
            EditorGUILayout.LabelField(
                "Addressables",
                GetValidationPassLabel(
                    result.AddressablesValidation.Evaluated,
                    result.AddressablesValidation.Passed
                ) +
                " - Expected " + result.AddressablesValidation.ExpectedOperationMode +
                ", Actual " + result.AddressablesValidation.ActualOperationMode
            );
        }

        if (result.SceneValidation != null)
        {
            EditorGUILayout.LabelField(
                "Runtime Scene",
                GetValidationPassLabel(
                    result.SceneValidation.Evaluated,
                    result.SceneValidation.Passed
                ) +
                " - " + result.SceneValidation.ActualOutcome
            );
        }

        EditorGUILayout.HelpBox(
            !string.IsNullOrEmpty(result.ErrorMessage)
                ? result.ErrorMessage
                : result.SummaryMessage,
            GetRuntimePipelineValidationMessageType(result.Outcome)
        );

        EditorGUILayout.HelpBox(
            "Raw duplicate coordinate submission is not observable because existing Height/Surface/Collision result contracts expose canonical unique coordinate sets. Execution validation does not change generator behavior solely for this diagnostic metric.",
            MessageType.None
        );
    }

    private static void DrawRuntimeStageValidationRow(
        string label,
        TerrainRuntimeBakeStageValidationResult result
    )
    {
        if (result == null || !result.Evaluated)
        {
            EditorGUILayout.LabelField(label, "Not evaluated");
            return;
        }

        EditorGUILayout.LabelField(
            label,
            (result.Passed ? "PASS" : "FAIL") +
            " - Expected " + result.ExpectedCount +
            ", Requested " + result.RequestedCount +
            ", Succeeded " + result.SucceededCount +
            ", Missing " + result.MissingRequested.Count +
            ", Unexpected " + result.UnexpectedRequested.Count
        );
    }

    private void DrawRuntimePipelineTimingSummary()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        if (result == null)
        {
            return;
        }

        GUILayout.Space(8f);
        GUILayout.Label("Last Pipeline Stage Timings", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Heightmaps", result.HeightDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Height Streaming", result.HeightStreamingDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Surface Masks", result.SurfaceDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Collision", result.CollisionDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Addressables", result.AddressablesDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Runtime Scene", result.SceneSyncDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Total", result.DurationSeconds.ToString("0.00") + " s");
    }

    private static void LogRuntimePipelineValidationResult(
        TerrainRuntimeBakeValidationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError("Runtime pipeline validation returned no result.");
            return;
        }

        string report = result.BuildDiagnosticReport();

        switch (result.Outcome)
        {
            case TerrainRuntimeBakeValidationOutcome.Passed:
                Debug.Log(report);
                break;

            case TerrainRuntimeBakeValidationOutcome.PassedWithWarnings:
            case TerrainRuntimeBakeValidationOutcome.Cancelled:
            case TerrainRuntimeBakeValidationOutcome.Blocked:
                Debug.LogWarning(report);
                break;

            default:
                Debug.LogError(report);
                break;
        }
    }

    private static string GetValidationPassLabel(
        bool evaluated,
        bool passed
    )
    {
        if (!evaluated)
        {
            return "Not evaluated";
        }

        return passed ? "PASS" : "FAIL";
    }

    private static MessageType GetRuntimePipelineValidationMessageType(
        TerrainRuntimeBakeValidationOutcome outcome
    )
    {
        switch (outcome)
        {
            case TerrainRuntimeBakeValidationOutcome.Passed:
                return MessageType.Info;

            case TerrainRuntimeBakeValidationOutcome.PassedWithWarnings:
            case TerrainRuntimeBakeValidationOutcome.Cancelled:
            case TerrainRuntimeBakeValidationOutcome.Blocked:
                return MessageType.Warning;

            default:
                return MessageType.Error;
        }
    }

    // =====================================================
    // RUNTIME BAKE INVALIDATION
    // =====================================================

    [SerializeField]
    private TerrainRuntimeInvalidationScenario
        invalidationModifierScenario =
            TerrainRuntimeInvalidationScenario.ModifierMove;

    [SerializeField]
    private TerrainRuntimeInvalidationScenario
        invalidationAggregationScenario =
            TerrainRuntimeInvalidationScenario.MultipleEdits;

    [SerializeField]
    private TerrainRuntimeInvalidationScenario
        invalidationSettingsScenario =
            TerrainRuntimeInvalidationScenario.SurfaceSettingsChange;

    private string invalidationValidationUiMessage = "";

    [SerializeField]
    private bool showRuntimeBakeInvalidationValidation;

    private void DrawRuntimeBakeInvalidationValidation()
    {
        showRuntimeBakeInvalidationValidation =
            EditorGUILayout.Foldout(
                showRuntimeBakeInvalidationValidation,
                "Invalidation",
                true
            );

        if (!showRuntimeBakeInvalidationValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        GUILayout.BeginVertical(
            EditorStyles.helpBox
        );

        GUILayout.Label(
            "Invalidation Scenario Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Derives expected authoring footprints independently from the Runtime Bake planner, expands downstream Runtime output dependencies, then compares persistent bake state and the current TerrainRuntimeBakePlan.",
            MessageType.Info
        );

        TerrainRuntimeBakePlan currentPlan =
            GetRuntimeBakeDiagnosticsPlan();

        EditorGUILayout.LabelField(
            "Current Runtime State",
            currentPlan == null
                ? "Unavailable"
                : currentPlan.IsBlocked
                    ? "Blocked"
                    : currentPlan.HasWork
                        ? "Changes Pending"
                        : "Ready"
        );

        if (currentPlan != null)
        {
            EditorGUILayout.LabelField(
                "Current Plan",
                TerrainRuntimeBakePipelineResult
                    .BuildPlanSummary(
                        currentPlan
                    )
            );
        }

        DrawModifierInvalidationValidation();
        DrawAggregationInvalidationValidation();
        DrawSettingsInvalidationValidation();

        TerrainRuntimeInvalidationValidationResult lastResult =
            TerrainRuntimeInvalidationScenarioRunner.LastResult;

        if (lastResult != null)
        {
            GUILayout.Space(6f);

            EditorGUILayout.LabelField(
                "Last Result",
                lastResult.Outcome.ToString()
            );

            EditorGUILayout.HelpBox(
                lastResult.SummaryMessage,
                lastResult.Passed
                    ? MessageType.Info
                    : MessageType.Error
            );

            if (
                GUILayout.Button(
                    "Log Last Invalidation Report"
                )
            )
            {
                Debug.Log(
                    lastResult.BuildDiagnosticReport()
                );
            }

            EditorGUI.BeginDisabledGroup(
                !lastResult.Passed
                || TerrainRuntimeBakePipeline.IsRunning
                || TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
                || TerrainRuntimeBakeResumeValidationUtility.IsRunning
                || TerrainSurfaceMaskCompiler.IsGenerating
            );

            if (
                GUILayout.Button(
                    "Bake Current Invalidation + Validate Execution"
                )
            )
            {
                bool started =
                    TerrainRuntimeInvalidationScenarioRunner
                        .BakeCurrentInvalidationAndValidate(
                            result =>
                            {
                                if (result != null)
                                {
                                    Debug.Log(
                                        result.BuildDiagnosticReport()
                                    );
                                }

                                Repaint();
                            }
                        );

                if (!started)
                {
                    invalidationValidationUiMessage =
                        "Runtime Bake execution validation could not start.";
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        if (!string.IsNullOrEmpty(invalidationValidationUiMessage))
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                invalidationValidationUiMessage,
                MessageType.Warning
            );
        }

        GUILayout.Space(4f);

        if (
            GUILayout.Button(
                "Clear Invalidation Validation Session"
            )
        )
        {
            TerrainRuntimeInvalidationScenarioRunner
                .ClearSession();

            invalidationValidationUiMessage = "";
        }

        GUILayout.EndVertical();
    }

    private void DrawModifierInvalidationValidation()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Guided Modifier Mutation",
            EditorStyles.boldLabel
        );

        TerrainRuntimeInvalidationModifierBaseline baseline =
            TerrainRuntimeInvalidationScenarioRunner.ModifierBaseline;

        EditorGUILayout.LabelField(
            "Captured Baseline",
            baseline != null
                ? (
                    baseline.ModifierType +
                    " / " +
                    baseline.StableId
                )
                : "None"
        );

        if (
            GUILayout.Button(
                "Capture Selected Modifier Baseline"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .CaptureSelectedModifierBaseline(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Baseline captured. Perform one modifier edit through the normal WorldMeshes authoring UI/tool, then validate it below.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        invalidationModifierScenario =
            (TerrainRuntimeInvalidationScenario)
            EditorGUILayout.EnumPopup(
                "Scenario",
                invalidationModifierScenario
            );

        EditorGUILayout.HelpBox(
            "Capture -> perform the real move/scale/rotate/Height Delta/enable/disable/delete edit -> Validate. For Undo or Redo, capture immediately before the real Unity Undo/Redo transition, perform it, then validate.",
            MessageType.None
        );

        if (
            GUILayout.Button(
                "Validate Current Modifier Mutation"
            )
        )
        {
            TerrainRuntimeInvalidationValidationResult result =
                TerrainRuntimeInvalidationScenarioRunner
                    .ValidateModifierMutation(
                        invalidationModifierScenario
                    );

            invalidationValidationUiMessage =
                result != null
                    ? result.SummaryMessage
                    : "No result was produced.";

            if (result != null)
            {
                Debug.Log(
                    result.BuildDiagnosticReport()
                );
            }
        }
    }

    private void DrawAggregationInvalidationValidation()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Multiple-Edit Union / Deduplication",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Aggregation Active",
            TerrainRuntimeInvalidationScenarioRunner
                .AggregationActive
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Recorded Mutations",
            TerrainRuntimeInvalidationScenarioRunner
                .AggregationMutationCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Expected Height Union",
            TerrainRuntimeInvalidationScenarioRunner
                .AggregatedHeightCoordinateCount
                .ToString()
        );

        if (
            GUILayout.Button(
                "Begin Aggregation Baseline From Selected Modifier"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .BeginAggregation(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Aggregation started. Perform one normal modifier edit, record it, then repeat without baking.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        if (
            GUILayout.Button(
                "Record Current Modifier Mutation Into Union"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .AccumulateCurrentModifierMutation(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Mutation footprint added to the expected union. Perform another edit or validate the aggregate.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        invalidationAggregationScenario =
            (TerrainRuntimeInvalidationScenario)
            EditorGUILayout.EnumPopup(
                "Aggregation Scenario",
                invalidationAggregationScenario
            );

        if (
            GUILayout.Button(
                "Validate Aggregated Pending Work"
            )
        )
        {
            TerrainRuntimeInvalidationValidationResult result =
                TerrainRuntimeInvalidationScenarioRunner
                    .ValidateAggregation(
                        invalidationAggregationScenario
                    );

            invalidationValidationUiMessage =
                result != null
                    ? result.SummaryMessage
                    : "No result was produced.";

            if (result != null)
            {
                Debug.Log(
                    result.BuildDiagnosticReport()
                );
            }
        }
    }

    private void DrawSettingsInvalidationValidation()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Guided Settings / Structural Mutation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Settings Baseline",
            TerrainRuntimeInvalidationScenarioRunner
                .HasSettingsBaseline
                ? "Captured"
                : "None"
        );

        if (
            GUILayout.Button(
                "Capture Settings Baseline"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .CaptureSettingsBaseline(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Settings baseline captured. Change one supported setting through the normal WorldMeshes UI, then validate.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        invalidationSettingsScenario =
            (TerrainRuntimeInvalidationScenario)
            EditorGUILayout.EnumPopup(
                "Settings Scenario",
                invalidationSettingsScenario
            );

        EditorGUILayout.HelpBox(
            "Supported guided settings scenarios: SurfaceSettingsChange, CollisionSettingsChange, WorldLayoutChange, HeightTileSpanChange. Structural tests can require a Full rebuild; restore the setting through the normal UI and bake back to Ready when testing is complete.",
            MessageType.Warning
        );

        if (
            GUILayout.Button(
                "Validate Current Settings Invalidation"
            )
        )
        {
            TerrainRuntimeInvalidationValidationResult result =
                TerrainRuntimeInvalidationScenarioRunner
                    .ValidateSettingsMutation(
                        invalidationSettingsScenario
                    );

            invalidationValidationUiMessage =
                result != null
                    ? result.SummaryMessage
                    : "No result was produced.";

            if (result != null)
            {
                Debug.Log(
                    result.BuildDiagnosticReport()
                );
            }
        }
    }

    // =====================================================
    // RUNTIME BAKE PERSISTENCE / RESUME
    // =====================================================

    private TerrainRuntimeBakePersistenceValidationResult
        lastRuntimePersistenceValidationResult;

    private TerrainRuntimeBakeResumeValidationResult
        lastRuntimeResumeValidationResult;

    [SerializeField]
    private bool showRuntimeBakePersistenceResumeValidation;

    private void DrawRuntimeBakePersistenceResumeValidation()
    {
        showRuntimeBakePersistenceResumeValidation =
            EditorGUILayout.Foldout(
                showRuntimeBakePersistenceResumeValidation,
                "Persistence / Resume",
                true
            );

        if (!showRuntimeBakePersistenceResumeValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainRuntimeBakeStateSummary snapshot =
            GetRuntimeBakeDiagnosticsSummary();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Persistence + Resume Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Validates that pending Runtime Bake work survives editor lifecycle events and that cancelled/failed incremental bakes resume without repeating safely acknowledged work. Validation hooks are transient, editor-only, and disabled during normal Runtime baking.",
            MessageType.None
        );

        GUILayout.Label("Current Persistent Bake State", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("State Revision", snapshot.StateRevision.ToString());
        EditorGUILayout.LabelField("Pending Height Tiles", snapshot.PendingHeightTileCount.ToString());
        EditorGUILayout.LabelField("Pending Height Streaming Tiles", snapshot.PendingHeightStreamingTileCount.ToString());
        EditorGUILayout.LabelField("Pending Surface Tiles", snapshot.PendingSurfaceTileCount.ToString());
        EditorGUILayout.LabelField("Pending Collision Chunks", snapshot.PendingCollisionChunkCount.ToString());
        EditorGUILayout.LabelField("Full Height", snapshot.FullHeightRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Full Height Streaming", snapshot.FullHeightStreamingRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Full Surface", snapshot.FullSurfaceRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Full Collision", snapshot.FullCollisionRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Addressables Configuration", snapshot.AddressablesConfigurationDirty ? "Dirty" : "Current");
        EditorGUILayout.LabelField("Addressables Content", snapshot.AddressablesContentDirty ? "Dirty" : "Current");
        EditorGUILayout.LabelField("Runtime Scene Metadata", snapshot.RuntimeSceneMetadataDirty ? "Dirty" : "Current");

        GUILayout.Space(6f);
        GUILayout.Label("Persistence Checkpoint", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Checkpoint",
            TerrainRuntimeBakePersistenceValidationUtility.CheckpointExists
                ? "Captured"
                : "Not Captured"
        );

        bool busy =
            TerrainRuntimeBakeResumeValidationUtility.IsRunning
            || TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating;

        EditorGUI.BeginDisabledGroup(busy || EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button("Capture Persistence Checkpoint"))
        {
            if (
                TerrainRuntimeBakePersistenceValidationUtility.CaptureCheckpoint(
                    out TerrainRuntimeBakePersistenceCheckpoint checkpoint,
                    out string errorMessage
                )
            )
            {
                Debug.Log(
                    "WorldMeshes runtime bake persistence checkpoint captured.\n" +
                    "State Revision: " + checkpoint.stateRevision + "\n" +
                    "Height Pending: " + checkpoint.pendingHeightTiles.Count + "\n" +
                    "Height Streaming Pending: " + checkpoint.pendingHeightStreamingTiles.Count + "\n" +
                    "Surface Pending: " + checkpoint.pendingSurfaceTiles.Count + "\n" +
                    "Collision Pending: " + checkpoint.pendingCollisionChunks.Count
                );
            }
            else
            {
                Debug.LogError(errorMessage);
            }

            Repaint();
        }

        if (GUILayout.Button("Compare Current State To Checkpoint"))
        {
            lastRuntimePersistenceValidationResult =
                TerrainRuntimeBakePersistenceValidationUtility
                    .CompareCurrentStateToCheckpoint();

            LogRuntimePersistenceValidationResult(
                lastRuntimePersistenceValidationResult
            );

            Repaint();
        }

        if (GUILayout.Button("Clear Persistence Checkpoint"))
        {
            if (
                !TerrainRuntimeBakePersistenceValidationUtility.ClearCheckpoint(
                    out string errorMessage
                )
            )
            {
                Debug.LogError(errorMessage);
            }

            lastRuntimePersistenceValidationResult = null;
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (lastRuntimePersistenceValidationResult != null)
        {
            EditorGUILayout.HelpBox(
                lastRuntimePersistenceValidationResult.SummaryMessage,
                lastRuntimePersistenceValidationResult.OverallPassed
                    ? MessageType.Info
                    : MessageType.Warning
            );
        }

        EditorGUILayout.HelpBox(
            "Assembly reload test: capture a checkpoint, trigger a normal script reload/recompile, return here, then compare.\n\nEditor-window test: capture, close this WorldMeshes window, reopen it, then compare.\n\nUnity restart test: capture, close Unity normally, reopen the project, then compare. The restart step is intentionally manual.",
            MessageType.None
        );

        GUILayout.Space(8f);
        GUILayout.Label("Cancellation + Resume", EditorStyles.boldLabel);

        DrawResumeValidationButton(
            "Validate Height Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.HeightCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Height Streaming Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.HeightStreamingCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Surface Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.SurfaceCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Collision Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.CollisionCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Cancel During Addressables",
            TerrainRuntimeBakeResumeValidationScenario.CancelDuringAddressables,
            busy
        );

        DrawResumeValidationButton(
            "Validate Cancel Before Scene Sync",
            TerrainRuntimeBakeResumeValidationScenario.CancelBeforeSceneSync,
            busy
        );

        GUILayout.Space(6f);
        GUILayout.Label("Controlled Failure + Resume", EditorStyles.boldLabel);

        DrawResumeValidationButton(
            "Validate Failure After Height",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeight,
            busy
        );

        DrawResumeValidationButton(
            "Validate Failure After Height Streaming",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeightStreaming,
            busy
        );

        DrawResumeValidationButton(
            "Validate Failure After Surface",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterSurface,
            busy
        );

        DrawResumeValidationButton(
            "Validate Failure After Collision",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterCollision,
            busy
        );

        DrawResumeValidationButton(
            "Validate Addressables Failure Resume",
            TerrainRuntimeBakeResumeValidationScenario.AddressablesFailureResume,
            busy
        );

        DrawResumeValidationButton(
            "Validate Scene Sync Failure Resume",
            TerrainRuntimeBakeResumeValidationScenario.SceneSyncFailureResume,
            busy
        );

        if (TerrainRuntimeBakeResumeValidationUtility.IsRunning)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Scenario",
                TerrainRuntimeBakeResumeValidationUtility.CurrentScenario.ToString()
            );
            EditorGUILayout.LabelField(
                "Phase",
                TerrainRuntimeBakeResumeValidationUtility.CurrentPhase
            );
            EditorGUILayout.LabelField(
                "Pipeline Stage",
                TerrainRuntimeBakePipeline.CurrentStageLabel
            );

            Rect progressRect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.ProgressBar(
                progressRect,
                TerrainRuntimeBakePipeline.CurrentOverallProgress,
                TerrainRuntimeBakePipeline.CurrentStageLabel
            );

            Repaint();
        }

        TerrainRuntimeBakeResumeValidationResult displayResume =
            lastRuntimeResumeValidationResult
            ?? TerrainRuntimeBakeResumeValidationUtility.LastResult;

        if (displayResume != null)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Last Scenario", displayResume.Scenario.ToString());
            EditorGUILayout.LabelField("Last Outcome", displayResume.Outcome.ToString());
            EditorGUILayout.LabelField(
                "Repeated Height / Streaming / Surface / Collision",
                displayResume.RepeatedHeightCoordinates.Count + " / " +
                displayResume.RepeatedHeightStreamingCoordinates.Count + " / " +
                displayResume.RepeatedSurfaceCoordinates.Count + " / " +
                displayResume.RepeatedCollisionCoordinates.Count
            );
            EditorGUILayout.LabelField("Upstream Stage Repeated", displayResume.UpstreamStageRepeated ? "Yes" : "No");
            EditorGUILayout.LabelField("Final Plan Current", displayResume.FinalPlanCurrent ? "Yes" : "No");

            EditorGUILayout.HelpBox(
                !string.IsNullOrEmpty(displayResume.ErrorMessage)
                    ? displayResume.ErrorMessage
                    : displayResume.SummaryMessage,
                displayResume.Passed
                    ? MessageType.Info
                    : MessageType.Warning
            );

            if (GUILayout.Button("Log Last Resume Validation Report"))
            {
                LogRuntimeResumeValidationResult(displayResume);
            }
        }

        EditorGUILayout.HelpBox(
            "Prepare pending Incremental work manually before individual cancellation/failure tests. Persistence / Resume validation does not move, delete, or toggle terrain modifiers, alter settings, or damage generated assets, Addressables configuration, or hierarchy objects.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private void DrawResumeValidationButton(
        string label,
        TerrainRuntimeBakeResumeValidationScenario scenario,
        bool busy
    )
    {
        EditorGUI.BeginDisabledGroup(
            busy || EditorApplication.isPlayingOrWillChangePlaymode
        );

        if (GUILayout.Button(label))
        {
            bool started =
                TerrainRuntimeBakeResumeValidationUtility.StartScenario(
                    scenario,
                    OnRuntimeResumeValidationCompleted
                );

            if (!started)
            {
                lastRuntimeResumeValidationResult =
                    TerrainRuntimeBakeResumeValidationUtility.LastResult;

                if (lastRuntimeResumeValidationResult != null)
                {
                    LogRuntimeResumeValidationResult(
                        lastRuntimeResumeValidationResult
                    );
                }
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();
    }

    private void OnRuntimeResumeValidationCompleted(
        TerrainRuntimeBakeResumeValidationResult result
    )
    {
        lastRuntimeResumeValidationResult = result;
        LogRuntimeResumeValidationResult(result);
        Repaint();
    }

    private static void LogRuntimePersistenceValidationResult(
        TerrainRuntimeBakePersistenceValidationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError("Runtime bake persistence validation returned no result.");
            return;
        }

        string report = result.BuildDiagnosticReport();

        if (result.OverallPassed)
        {
            Debug.Log(report);
        }
        else if (result.Outcome == TerrainRuntimeBakePersistenceValidationOutcome.Blocked)
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }

    private static void LogRuntimeResumeValidationResult(
        TerrainRuntimeBakeResumeValidationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError("Runtime bake resume validation returned no result.");
            return;
        }

        string report = result.BuildDiagnosticReport();

        if (result.Passed)
        {
            Debug.Log(report);
        }
        else if (result.Outcome == TerrainRuntimeBakeResumeValidationOutcome.Blocked)
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }

    // =====================================================
    // RUNTIME BAKE FAULT RECOVERY
    // =====================================================

    [SerializeField]
    private bool showRuntimeFaultRecoveryValidation;

    [SerializeField]
    private TerrainRuntimeFaultRecoveryScenario
        runtimeFaultRecoveryScenario =
            TerrainRuntimeFaultRecoveryScenario
                .MissingHeightTexture;

    private string runtimeFaultRecoveryMessage =
        "";

    private MessageType runtimeFaultRecoveryMessageType =
        MessageType.None;

    private void DrawRuntimeBakeFaultRecoveryValidation()
    {
        showRuntimeFaultRecoveryValidation =
            EditorGUILayout.Foldout(
                showRuntimeFaultRecoveryValidation,
                "Fault Recovery",
                true
            );

        if (!showRuntimeFaultRecoveryValidation)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "These tests deliberately damage generated runtime data, generated runtime Addressables configuration, or the generated runtime scene hierarchy and then exercise the canonical recovery path.\n\nStart from Runtime Ready. Temporary quarantine data is stored under Library/WorldMeshes/Validation/FaultRecovery. Do not enter Play Mode while a destructive scenario is active.",
            MessageType.Warning
        );

        bool active =
            TerrainRuntimeFaultRecoveryScenarioRunner
                .HasActiveFault;

        bool running =
            TerrainRuntimeFaultRecoveryScenarioRunner
                .IsRecoveryRunning;

        bool orphaned =
            TerrainRuntimeFaultInjectionUtility
                .HasOrphanedValidationSession();

        EditorGUILayout.LabelField(
            "Active Fault",
            active
                ? TerrainRuntimeFaultRecoveryScenarioRunner
                    .ActiveScenario
                    .ToString()
                : "None"
        );

        EditorGUILayout.LabelField(
            "Validation Quarantine",
            orphaned
                ? "Recovery / cleanup required"
                : "Clear"
        );

        runtimeFaultRecoveryScenario =
            (TerrainRuntimeFaultRecoveryScenario)
            EditorGUILayout.EnumPopup(
                "Scenario",
                runtimeFaultRecoveryScenario
            );

        TerrainRuntimeFaultExpectation expectation =
            TerrainRuntimeFaultExpectation
                .ForScenario(
                    runtimeFaultRecoveryScenario
                );

        if (expectation != null)
        {
            EditorGUILayout.LabelField(
                "Recovery Authority",
                expectation.RecoveryAuthority.ToString()
            );

            EditorGUILayout.HelpBox(
                expectation.Description,
                MessageType.None
            );
        }

        GUILayout.Space(4f);

        EditorGUI.BeginDisabledGroup(
            runtimeFaultRecoveryScenario ==
                TerrainRuntimeFaultRecoveryScenario.None
            || active
            || running
            || orphaned
        );

        if (
            GUILayout.Button(
                "Inject + Validate Fault",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeFaultRecoveryValidationResult result =
                TerrainRuntimeFaultRecoveryScenarioRunner
                    .InjectAndValidate(
                        runtimeFaultRecoveryScenario
                    );

            HandleRuntimeFaultRecoveryResult(result);
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !active
            || running
        );

        if (
            GUILayout.Button(
                "Run Canonical Recovery",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeFaultRecoveryScenarioRunner
                    .RunCanonicalRecovery(
                        result =>
                        {
                            HandleRuntimeFaultRecoveryResult(result);
                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeFaultRecoveryMessage =
                    "Canonical recovery started.";
                runtimeFaultRecoveryMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        if (
            GUILayout.Button(
                "Restore Active Fault Directly",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool restored =
                TerrainRuntimeFaultRecoveryScenarioRunner
                    .RestoreActiveFault(
                        out string message
                    );

            runtimeFaultRecoveryMessage = message;
            runtimeFaultRecoveryMessageType =
                restored
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !orphaned
            || active
            || running
        );

        if (
            GUILayout.Button(
                "Restore Validation Quarantine",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool restored =
                TerrainRuntimeFaultInjectionUtility
                    .RestoreOrphanedValidationSession(
                        out string message
                    );

            runtimeFaultRecoveryMessage = message;
            runtimeFaultRecoveryMessageType =
                restored
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            active || running
        );

        if (
            GUILayout.Button(
                "Force Fresh Runtime Integrity Audit",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeReadinessResult readiness =
                TerrainRuntimeReadinessUtility
                    .Evaluate(true);

            runtimeFaultRecoveryMessage =
                BuildRuntimeFaultReadinessSummary(
                    readiness
                );

            runtimeFaultRecoveryMessageType =
                readiness != null
                && readiness.IsReady
                    ? MessageType.Info
                    : MessageType.Warning;
        }

        EditorGUI.EndDisabledGroup();

        TerrainRuntimeFaultRecoveryValidationResult last =
            TerrainRuntimeFaultRecoveryScenarioRunner
                .LastResult;

        if (last != null)
        {
            GUILayout.Space(6f);

            EditorGUILayout.LabelField(
                "Last Scenario",
                last.Scenario.ToString()
            );

            EditorGUILayout.LabelField(
                "Last Outcome",
                last.Outcome.ToString()
            );

            if (
                GUILayout.Button(
                    "Log Last Fault Recovery Report",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                LogRuntimeFaultRecoveryResult(last);
            }
        }

        if (!string.IsNullOrEmpty(runtimeFaultRecoveryMessage))
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                runtimeFaultRecoveryMessage,
                runtimeFaultRecoveryMessageType
            );
        }

        GUILayout.EndVertical();
    }

    private void HandleRuntimeFaultRecoveryResult(
        TerrainRuntimeFaultRecoveryValidationResult result
    )
    {
        if (result == null)
        {
            runtimeFaultRecoveryMessage =
                "Fault recovery validation returned no result.";
            runtimeFaultRecoveryMessageType =
                MessageType.Warning;
            return;
        }

        runtimeFaultRecoveryMessage =
            !string.IsNullOrEmpty(result.SummaryMessage)
                ? result.SummaryMessage
                : result.Outcome.ToString();

        runtimeFaultRecoveryMessageType =
            result.Passed
                ? MessageType.Info
                : result.Outcome ==
                    TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected
                    ? MessageType.Warning
                    : MessageType.Error;

        LogRuntimeFaultRecoveryResult(result);
    }

    private static void LogRuntimeFaultRecoveryResult(
        TerrainRuntimeFaultRecoveryValidationResult result
    )
    {
        if (result == null)
        {
            return;
        }

        string report =
            result.BuildDiagnosticReport();

        if (result.Passed)
        {
            Debug.Log(report);
        }
        else if (
            result.Outcome ==
                TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected
            || result.Outcome ==
                TerrainRuntimeFaultRecoveryValidationOutcome.Blocked
        )
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }

    private static string BuildRuntimeFaultReadinessSummary(
        TerrainRuntimeReadinessResult readiness
    )
    {
        if (readiness == null)
        {
            return "Runtime readiness is unavailable.";
        }

        return
            "Runtime Ready: " + readiness.IsReady +
            "\nHeight: " +
            TerrainGenerationStateUtility.GetStatusLabel(
                readiness.HeightStatus
            ) +
            "\nSurface: " +
            TerrainGenerationStateUtility.GetStatusLabel(
                readiness.SurfaceStatus
            ) +
            "\nCollision: " +
            TerrainGenerationStateUtility.GetStatusLabel(
                readiness.CollisionStatus
            ) +
            "\nAddressables: " +
            (
                readiness.AddressablesValidation != null
                && readiness.AddressablesValidation.IsValid
                    ? "Valid"
                    : "Needs Repair"
            ) +
            "\nHierarchy: " +
            (
                readiness.HierarchyReadiness != null
                && readiness.HierarchyReadiness.IsReady
                    ? "Valid"
                    : "Needs Repair"
            ) +
            "\nPlan: " +
            (
                readiness.Plan == null
                    ? "Unavailable"
                    : readiness.Plan.HasWork
                        ? TerrainRuntimeBakePipelineResult
                            .BuildPlanSummary(
                                readiness.Plan
                            )
                        : "No Work"
            );
    }

    // =====================================================
    // RUNTIME BAKE EQUIVALENCE
    // =====================================================

    [SerializeField]
    private bool showRuntimeEquivalenceValidation;

    [SerializeField]
    private TerrainRuntimeEquivalenceScenario
        runtimeEquivalenceScenario =
            TerrainRuntimeEquivalenceScenario
                .SingleLocalEdit;

    private string runtimeEquivalenceMessage =
        "";

    private MessageType runtimeEquivalenceMessageType =
        MessageType.None;

    private void DrawRuntimeBakeEquivalenceValidation()
    {
        showRuntimeEquivalenceValidation =
            EditorGUILayout.Foldout(
                showRuntimeEquivalenceValidation,
                "Incremental / Full Equivalence",
                true
            );

        if (!showRuntimeEquivalenceValidation)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "Performs a normal smallest-correct Runtime Bake, fingerprints the complete generated Height / Surface / Collision dataset, then performs Rebuild All Runtime Data and fingerprints the complete dataset again. A representative run can be substantially slower than routine validation because it intentionally includes a genuine Full rebuild and complete-world output comparison.",
            MessageType.Warning
        );

        bool running =
            TerrainRuntimeEquivalenceScenarioRunner
                .IsRunning;

        bool hasBaseline =
            TerrainRuntimeEquivalenceScenarioRunner
                .HasBaseline;

        EditorGUILayout.LabelField(
            "Phase",
            TerrainRuntimeEquivalenceScenarioRunner
                .PhaseLabel
        );

        EditorGUILayout.LabelField(
            "Baseline",
            hasBaseline
                ? TerrainRuntimeEquivalenceScenarioRunner
                    .Baseline
                    .Scenario
                    .ToString()
                : "None"
        );

        runtimeEquivalenceScenario =
            (TerrainRuntimeEquivalenceScenario)
            EditorGUILayout.EnumPopup(
                "Scenario",
                runtimeEquivalenceScenario
            );

        GUILayout.Space(4f);

        EditorGUI.BeginDisabledGroup(
            running
            || !UsesRuntimeEquivalenceBaseline(
                runtimeEquivalenceScenario
            )
        );

        if (
            GUILayout.Button(
                "Capture Equivalence Baseline",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool captured =
                TerrainRuntimeEquivalenceScenarioRunner
                    .CaptureBaseline(
                        runtimeEquivalenceScenario,
                        out string message
                    );

            runtimeEquivalenceMessage =
                message;

            runtimeEquivalenceMessageType =
                captured
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (hasBaseline)
        {
            EditorGUILayout.HelpBox(
                "Baseline captured. Make the representative authoring/settings change now. Do NOT use the normal Runtime bake button. Then click Run Incremental + Full Equivalence.",
                MessageType.Info
            );
        }

        EditorGUI.BeginDisabledGroup(
            running
            || !hasBaseline
        );

        if (
            GUILayout.Button(
                "Run Incremental + Full Equivalence",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeEquivalenceScenarioRunner
                    .RunIncrementalFullEquivalence(
                        result =>
                        {
                            HandleRuntimeEquivalenceResult(
                                result
                            );

                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeEquivalenceMessage =
                    "Incremental / Full Equivalence validation started.";

                runtimeEquivalenceMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Additional Equivalence Scenarios",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            running
        );

        if (
            GUILayout.Button(
                "Run Forced Full Health Validation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeEquivalenceScenarioRunner
                    .RunForcedFullHealthValidation(
                        result =>
                        {
                            HandleRuntimeEquivalenceResult(
                                result
                            );

                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeEquivalenceMessage =
                    "Forced Full health validation started.";

                runtimeEquivalenceMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        if (
            GUILayout.Button(
                "Run Initial Bake vs Full Equivalence",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeEquivalenceScenarioRunner
                    .RunInitialBakeEquivalence(
                        result =>
                        {
                            HandleRuntimeEquivalenceResult(
                                result
                            );

                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeEquivalenceMessage =
                    "Initial Bake vs Full equivalence validation started.";

                runtimeEquivalenceMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "Initial Bake validation never deletes a healthy generated runtime dataset. It only runs when the canonical planner is already in a legitimate Initial Bake state.",
            MessageType.None
        );

        if (running)
        {
            EditorGUI.BeginDisabledGroup(
                !TerrainRuntimeBakePipeline
                    .IsRunning
            );

            if (
                GUILayout.Button(
                    "Request Validation Cancel",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                bool requested =
                    TerrainRuntimeEquivalenceScenarioRunner
                        .RequestCancel();

                runtimeEquivalenceMessage =
                    requested
                        ? "Cancellation requested at the canonical pipeline boundary."
                        : "Cancellation could not be requested in the current validation phase.";

                runtimeEquivalenceMessageType =
                    requested
                        ? MessageType.Warning
                        : MessageType.Error;

                Repaint();
            }

            EditorGUI.EndDisabledGroup();
        }

        TerrainRuntimeEquivalenceValidationResult last =
            TerrainRuntimeEquivalenceScenarioRunner
                .LastResult;

        if (last != null)
        {
            GUILayout.Space(6f);

            EditorGUILayout.LabelField(
                "Last Scenario",
                last.Scenario.ToString()
            );

            EditorGUILayout.LabelField(
                "Last Outcome",
                last.Outcome.ToString()
            );

            if (last.Comparison != null)
            {
                EditorGUILayout.LabelField(
                    "Total Expected",
                    last.Comparison
                        .TotalExpected
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Total Matching",
                    last.Comparison
                        .TotalMatching
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Missing",
                    last.Comparison
                        .TotalMissing
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Unexpected",
                    last.Comparison
                        .TotalUnexpected
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Payload Mismatches",
                    last.Comparison
                        .TotalPayloadMismatches
                        .ToString()
                );
            }

            if (
                GUILayout.Button(
                    "Log Last Equivalence Report",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                LogRuntimeEquivalenceResult(
                    last
                );
            }
        }

        EditorGUI.BeginDisabledGroup(
            running
        );

        if (
            GUILayout.Button(
                "Clear Equivalence Validation State",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool cleared =
                TerrainRuntimeEquivalenceScenarioRunner
                    .ClearValidationState(
                        out string message
                    );

            runtimeEquivalenceMessage =
                message;

            runtimeEquivalenceMessageType =
                cleared
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (
            !string.IsNullOrEmpty(
                runtimeEquivalenceMessage
            )
        )
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                runtimeEquivalenceMessage,
                runtimeEquivalenceMessageType
            );
        }

        GUILayout.EndVertical();
    }

    private void HandleRuntimeEquivalenceResult(
        TerrainRuntimeEquivalenceValidationResult result
    )
    {
        if (result == null)
        {
            runtimeEquivalenceMessage =
                "Incremental / Full Equivalence returned no validation result.";

            runtimeEquivalenceMessageType =
                MessageType.Error;

            return;
        }

        runtimeEquivalenceMessage =
            !string.IsNullOrEmpty(
                result.SummaryMessage
            )
                ? result.SummaryMessage
                : result.Outcome.ToString();

        runtimeEquivalenceMessageType =
            result.Passed
                ? MessageType.Info
                : result.Outcome ==
                    TerrainRuntimeEquivalenceValidationOutcome.Cancelled
                    || result.Outcome ==
                        TerrainRuntimeEquivalenceValidationOutcome
                            .ComparisonInvalidated
                    || result.Outcome ==
                        TerrainRuntimeEquivalenceValidationOutcome
                            .Blocked
                        ? MessageType.Warning
                        : MessageType.Error;

        LogRuntimeEquivalenceResult(
            result
        );
    }

    private static void LogRuntimeEquivalenceResult(
        TerrainRuntimeEquivalenceValidationResult result
    )
    {
        if (result == null)
        {
            return;
        }

        string report =
            result.BuildDiagnosticReport();

        if (result.Passed)
        {
            Debug.Log(
                report
            );
        }
        else if (
            result.Outcome ==
                TerrainRuntimeEquivalenceValidationOutcome
                    .Cancelled
            || result.Outcome ==
                TerrainRuntimeEquivalenceValidationOutcome
                    .ComparisonInvalidated
            || result.Outcome ==
                TerrainRuntimeEquivalenceValidationOutcome
                    .Blocked
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

    private static bool UsesRuntimeEquivalenceBaseline(
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

    // =====================================================
    // LIVE STAMP INTEGRATION
    // =====================================================

    private void DrawLiveStampValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Live Stamp Integration Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Status",
            TerrainLiveStampValidationUtility.StatusLabel
        );

        EditorGUILayout.HelpBox(
            TerrainLiveStampValidationUtility.StatusMessage,
            TerrainLiveStampValidationUtility.Status ==
                TerrainLiveStampValidationStatus.Failed
                ? MessageType.Error
                : TerrainLiveStampValidationUtility.Status ==
                    TerrainLiveStampValidationStatus.Passed
                    ? MessageType.Info
                    : MessageType.None
        );

        bool busy =
            TerrainLiveStampValidationUtility.IsBusy;

        GUILayout.Space(4f);

        GUILayout.Label(
            "Controlled Test Stamp",
            EditorStyles.miniBoldLabel
        );

        int controlledCount =
            TerrainLiveStampValidationUtility.ControlledStampCount;

        if (controlledCount > 0)
        {
            string[] labels =
                new string[controlledCount];

            for (
                int index = 0;
                index < controlledCount;
                index++
            )
            {
                labels[index] =
                    TerrainLiveStampValidationUtility
                        .GetControlledStampLabel(index);
            }

            int selected =
                TerrainLiveStampValidationUtility
                    .SelectedControlledIndex;

            int newSelected =
                EditorGUILayout.Popup(
                    "Selected",
                    selected,
                    labels
                );

            if (newSelected != selected)
            {
                TerrainLiveStampValidationUtility
                    .SelectedControlledIndex =
                    newSelected;
            }
        }
        else
        {
            EditorGUILayout.LabelField(
                "Selected",
                "None"
            );
        }

        TerrainHeightStampAsset stampAsset =
            (TerrainHeightStampAsset)
            EditorGUILayout.ObjectField(
                "Test Stamp Asset",
                TerrainLiveStampValidationUtility
                    .PendingStampAsset,
                typeof(TerrainHeightStampAsset),
                false
            );

        if (
            stampAsset !=
            TerrainLiveStampValidationUtility
                .PendingStampAsset
        )
        {
            TerrainLiveStampValidationUtility
                .PendingStampAsset =
                stampAsset;
        }

        Vector2 position =
            TerrainLiveStampValidationUtility
                .PendingPositionXZ;

        Vector2 newPosition =
            new Vector2(
                EditorGUILayout.FloatField(
                    "Position X",
                    position.x
                ),
                EditorGUILayout.FloatField(
                    "Position Z",
                    position.y
                )
            );

        if (newPosition != position)
        {
            TerrainLiveStampValidationUtility
                .PendingPositionXZ =
                newPosition;
        }

        Vector2 size =
            TerrainLiveStampValidationUtility
                .PendingSizeXZ;

        Vector2 newSize =
            new Vector2(
                EditorGUILayout.FloatField(
                    "Size X",
                    size.x
                ),
                EditorGUILayout.FloatField(
                    "Size Z",
                    size.y
                )
            );

        if (newSize != size)
        {
            TerrainLiveStampValidationUtility
                .PendingSizeXZ =
                newSize;
        }

        float heightDelta =
            EditorGUILayout.FloatField(
                "Height Delta",
                TerrainLiveStampValidationUtility
                    .PendingHeightDelta
            );

        if (
            !Mathf.Approximately(
                heightDelta,
                TerrainLiveStampValidationUtility
                    .PendingHeightDelta
            )
        )
        {
            TerrainLiveStampValidationUtility
                .PendingHeightDelta =
                heightDelta;
        }

        float falloff =
            EditorGUILayout.Slider(
                "Falloff",
                TerrainLiveStampValidationUtility
                    .PendingFalloff,
                0f,
                1f
            );

        if (
            !Mathf.Approximately(
                falloff,
                TerrainLiveStampValidationUtility
                    .PendingFalloff
            )
        )
        {
            TerrainLiveStampValidationUtility
                .PendingFalloff =
                falloff;
        }

        bool enabled =
            EditorGUILayout.Toggle(
                "Enabled",
                TerrainLiveStampValidationUtility
                    .PendingEnabled
            );

        if (
            enabled !=
            TerrainLiveStampValidationUtility
                .PendingEnabled
        )
        {
            TerrainLiveStampValidationUtility
                .PendingEnabled =
                enabled;
        }

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            busy
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Add Test Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestAddTestStamp();
        }

        EditorGUI.BeginDisabledGroup(
            controlledCount <= 0
        );

        if (
            GUILayout.Button(
                "Move Test Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestMoveTestStamp();
        }

        if (
            GUILayout.Button(
                "Apply Test Parameters",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestApplyParameters();
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Move Earlier"))
        {
            TerrainLiveStampValidationUtility
                .RequestReorderEarlier();
        }

        if (GUILayout.Button("Move Later"))
        {
            TerrainLiveStampValidationUtility
                .RequestReorderLater();
        }

        GUILayout.EndHorizontal();

        if (
            GUILayout.Button(
                "Remove Test Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestRemoveTestStamp();
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Undo Test Edit"))
        {
            TerrainLiveStampValidationUtility
                .RequestUndo();
        }

        if (GUILayout.Button("Redo Test Edit"))
        {
            TerrainLiveStampValidationUtility
                .RequestRedo();
        }

        GUILayout.EndHorizontal();

        if (
            GUILayout.Button(
                "Validate Selected Shared Borders",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestValidateSelectedBorders();
        }

        EditorGUI.EndDisabledGroup();

        if (
            GUILayout.Button(
                "Center Pending Position In World",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .ResetPendingPositionToWorldCenter();
        }

        if (
            GUILayout.Button(
                "Reset Test State",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestResetTestState();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(6f);

        TerrainLiveStampValidationBaseline baseline =
            TerrainLiveStampValidationUtility.LastBaseline;

        if (baseline != null)
        {
            GUILayout.Label(
                "Last Baseline",
                EditorStyles.miniBoldLabel
            );

            EditorGUILayout.LabelField(
                "Selected LOD0 Texture ID",
                baseline.CacheTextureId.ToString()
            );

            EditorGUILayout.LabelField(
                "Full Cache Builds",
                baseline.FullCacheBuilds.ToString()
            );

            EditorGUILayout.LabelField(
                "Display Page Updates",
                baseline
                    .TotalIncrementalSliceUpdates
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Shared Compositor Pages",
                baseline
                    .TotalCompositeTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Renderer Bindings",
                baseline
                    .RendererBindingCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Authoring Revision",
                baseline
                    .AuthoringRevision
                    .ToString()
            );
        }

        TerrainLiveStampValidationReport report =
            TerrainLiveStampValidationUtility.LastReport;

        if (report != null)
        {
            GUILayout.Space(5f);

            GUILayout.Label(
                "Last Transaction",
                EditorStyles.miniBoldLabel
            );

            EditorGUILayout.LabelField(
                "Operation",
                report.Operation
            );

            EditorGUILayout.LabelField(
                "Logical Geographic Dirty Tiles",
                report
                    .ExpectedDirtyTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Display Representation Updates",
                report
                    .ActualIncrementalSliceCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Shared Composited Pages",
                report
                    .ActualCompositeTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Modifiers Considered",
                report
                    .ModifierConsideredCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Modifiers Dispatched",
                report
                    .ModifierDispatchCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Compute Dispatches",
                report
                    .ComputeDispatchCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Dirty Tile Set"
            );

            EditorGUILayout.TextArea(
                report.DirtyTileSummary,
                GUILayout.MinHeight(34f)
            );

            EditorGUILayout.HelpBox(
                report.Details,
                report.Passed
                    ? MessageType.Info
                    : MessageType.Error
            );
        }

        GUILayout.Space(5f);

        GUILayout.Label(
            "Shared Border Readback",
            EditorStyles.miniBoldLabel
        );

        EditorGUILayout.HelpBox(
            TerrainLiveStampValidationUtility
                .BorderValidationSummary,
            TerrainLiveStampValidationUtility
                .BorderValidationPassed
                ? MessageType.Info
                : MessageType.None
        );

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "This is a controlled integration-validation workflow, not "
            +
            "the production modifier authoring UI. Persistent modifier "
            +
            "changes are routed through TerrainAuthoringModifierService. "
            +
            "Each requested edit automatically captures cache/signature/"
            +
            "revision diagnostics and checks the settled incremental "
            +
            "preview transaction.\n\n"
            +
            "Visual deformation, visualization-mode agreement, and crack "
            +
            "inspection still require Scene View inspection.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
