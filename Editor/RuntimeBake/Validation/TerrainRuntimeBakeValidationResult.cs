using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainRuntimeBakeValidationOutcome
{
    NotRun,
    Passed,
    PassedWithWarnings,
    Cancelled,
    Failed,
    Blocked
}

public sealed class TerrainRuntimeAddressablesStageValidationResult
{
    public bool Evaluated { get; private set; }
    public bool StageExecuted { get; private set; }

    public bool ExpectedConfigurationRequired { get; private set; }
    public bool ExpectedContentBuildRequired { get; private set; }
    public TerrainRuntimeAddressablesOperationMode ExpectedOperationMode { get; private set; }

    public TerrainRuntimeAddressablesOutcome ActualOutcome { get; private set; }
    public TerrainRuntimeAddressablesOperationMode ActualOperationMode { get; private set; }

    public bool ConfigurationWasRequired { get; private set; }
    public bool ConfigurationPerformed { get; private set; }
    public bool ContentBuildWasRequired { get; private set; }
    public bool ContentBuildPerformed { get; private set; }

    public bool HeightConfigurationChanged { get; private set; }
    public bool SurfaceConfigurationChanged { get; private set; }
    public bool CollisionConfigurationChanged { get; private set; }
    public bool CollisionRuntimeMetadataUpdated { get; private set; }

    public int CollisionMarkersRegenerated { get; private set; }
    public int CollisionMarkersReused { get; private set; }
    public int CollisionMarkersRemoved { get; private set; }

    public int EntriesCreated { get; private set; }
    public int EntriesMoved { get; private set; }
    public int EntriesRemoved { get; private set; }
    public int AddressesUpdated { get; private set; }
    public int LabelsUpdated { get; private set; }
    public int GroupsCreated { get; private set; }
    public int SchemasCreatedOrChanged { get; private set; }

    public bool Passed { get; private set; }
    public string ErrorMessage { get; private set; }

    internal TerrainRuntimeAddressablesStageValidationResult(
        TerrainRuntimeBakePlan plan,
        bool stageExecuted,
        TerrainRuntimeAddressablesResult result
    )
    {
        Evaluated = plan != null;
        StageExecuted = stageExecuted;

        ExpectedConfigurationRequired =
            plan != null && plan.AddressablesConfigurationRequired;

        ExpectedContentBuildRequired =
            plan != null && plan.AddressablesContentBuildRequired;

        ExpectedOperationMode = GetExpectedMode(plan);

        if (result != null)
        {
            ActualOutcome = result.Outcome;
            ActualOperationMode = result.OperationMode;
            ConfigurationWasRequired = result.ConfigurationWasRequired;
            ConfigurationPerformed = result.ConfigurationPerformed;
            ContentBuildWasRequired = result.ContentBuildWasRequired;
            ContentBuildPerformed = result.ContentBuildPerformed;

            HeightConfigurationChanged = result.HeightConfigurationChanged;
            SurfaceConfigurationChanged = result.SurfaceConfigurationChanged;
            CollisionConfigurationChanged = result.CollisionConfigurationChanged;
            CollisionRuntimeMetadataUpdated = result.CollisionRuntimeMetadataUpdated;

            CollisionMarkersRegenerated = result.CollisionMarkersRegenerated;
            CollisionMarkersReused = result.CollisionMarkersReused;
            CollisionMarkersRemoved = result.CollisionMarkersRemoved;

            EntriesCreated = result.EntriesCreated;
            EntriesMoved = result.EntriesMoved;
            EntriesRemoved = result.EntriesRemoved;
            AddressesUpdated = result.AddressesUpdated;
            LabelsUpdated = result.LabelsUpdated;
            GroupsCreated = result.GroupsCreated;
            SchemasCreatedOrChanged = result.SchemasCreatedOrChanged;
        }
        else
        {
            ActualOutcome = TerrainRuntimeAddressablesOutcome.NoWork;
            ActualOperationMode = TerrainRuntimeAddressablesOperationMode.None;
        }

        ErrorMessage = result != null ? result.ErrorMessage : "";
        Passed = CalculatePassed(result);
    }

    private bool CalculatePassed(TerrainRuntimeAddressablesResult result)
    {
        if (!Evaluated)
        {
            return false;
        }

        bool expectedWork =
            ExpectedConfigurationRequired || ExpectedContentBuildRequired;

        if (!expectedWork)
        {
            return !StageExecuted && result == null;
        }

        if (!StageExecuted || result == null)
        {
            return false;
        }

        if (ActualOperationMode != ExpectedOperationMode)
        {
            return false;
        }

        if (
            ConfigurationWasRequired != ExpectedConfigurationRequired
            || ContentBuildWasRequired != ExpectedContentBuildRequired
        )
        {
            return false;
        }

        bool completed =
            result.Outcome == TerrainRuntimeAddressablesOutcome.Completed
            || result.Outcome == TerrainRuntimeAddressablesOutcome.NoWork;

        if (completed)
        {
            if (ExpectedConfigurationRequired && !ConfigurationPerformed)
            {
                return false;
            }

            if (ExpectedContentBuildRequired && !ContentBuildPerformed)
            {
                return false;
            }
        }

        return true;
    }

    private static TerrainRuntimeAddressablesOperationMode GetExpectedMode(
        TerrainRuntimeBakePlan plan
    )
    {
        if (plan == null)
        {
            return TerrainRuntimeAddressablesOperationMode.None;
        }

        if (plan.AddressablesConfigurationRequired)
        {
            return TerrainRuntimeAddressablesOperationMode.ConfigureAndBuild;
        }

        if (plan.AddressablesContentBuildRequired)
        {
            return TerrainRuntimeAddressablesOperationMode.ContentOnly;
        }

        return TerrainRuntimeAddressablesOperationMode.None;
    }
}

public sealed class TerrainRuntimeSceneStageValidationResult
{
    public bool Evaluated { get; private set; }
    public bool StageExecuted { get; private set; }
    public bool ExpectedExecution { get; private set; }
    public bool PlanRequestedMetadataUpdate { get; private set; }

    public TerrainRuntimeSceneSynchronizationOutcome ActualOutcome { get; private set; }

    public bool BoundsApplied { get; private set; }
    public bool HeightStreamerSynchronized { get; private set; }
    public bool SurfaceManifestSynchronized { get; private set; }
    public bool CollisionStreamerSynchronized { get; private set; }
    public int SerializedComponentChangeCount { get; private set; }
    public bool SceneMarkedDirty { get; private set; }
    public bool RuntimeSceneMetadataDirtyCleared { get; private set; }

    public bool Passed { get; private set; }
    public string ErrorMessage { get; private set; }

    internal TerrainRuntimeSceneStageValidationResult(
        TerrainRuntimeBakePipelineMode pipelineMode,
        TerrainRuntimeBakePlan plan,
        bool stageExecuted,
        TerrainRuntimeSceneSynchronizationResult result
    )
    {
        Evaluated = plan != null;
        StageExecuted = stageExecuted;
        PlanRequestedMetadataUpdate =
            plan != null && plan.RuntimeSceneMetadataUpdateRequired;

        ExpectedExecution =
            plan != null
            && (
                pipelineMode == TerrainRuntimeBakePipelineMode.RebuildAll
                || plan.RuntimeSceneMetadataUpdateRequired
            );

        if (result != null)
        {
            ActualOutcome = result.Outcome;
            BoundsApplied = result.BoundsApplied;
            HeightStreamerSynchronized = result.HeightStreamerSynchronized;
            SurfaceManifestSynchronized = result.SurfaceManifestSynchronized;
            CollisionStreamerSynchronized = result.CollisionStreamerSynchronized;
            SerializedComponentChangeCount = result.SerializedComponentChangeCount;
            SceneMarkedDirty = result.SceneMarkedDirty;
            RuntimeSceneMetadataDirtyCleared = result.PersistentSceneDirtyCleared;
            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            ActualOutcome = TerrainRuntimeSceneSynchronizationOutcome.NoWork;
            ErrorMessage = "";
        }

        Passed = CalculatePassed(result);
    }

    private bool CalculatePassed(TerrainRuntimeSceneSynchronizationResult result)
    {
        if (!Evaluated)
        {
            return false;
        }

        if (!ExpectedExecution)
        {
            return !StageExecuted && result == null;
        }

        if (!StageExecuted || result == null)
        {
            return false;
        }

        return
            result.Outcome == TerrainRuntimeSceneSynchronizationOutcome.NoWork
            || result.Outcome == TerrainRuntimeSceneSynchronizationOutcome.Completed
            || result.Outcome == TerrainRuntimeSceneSynchronizationOutcome.CompletedWithWarnings
            || result.Outcome == TerrainRuntimeSceneSynchronizationOutcome.RepairRequired
            || result.Outcome == TerrainRuntimeSceneSynchronizationOutcome.Blocked
            || result.Outcome == TerrainRuntimeSceneSynchronizationOutcome.Failed;
    }
}

public sealed class TerrainRuntimeBakeValidationResult
{
    private const int MaxMismatchPreview = 20;

    private readonly ReadOnlyCollection<string> warningMessages;

    public TerrainRuntimeBakeValidationOutcome Outcome { get; private set; }
    public TerrainRuntimeBakePipelineResult PipelineResult { get; private set; }
    public TerrainRuntimeBakePlan InitialPlan { get; private set; }

    public TerrainRuntimeBakeStageValidationResult HeightValidation { get; private set; }
    public TerrainRuntimeBakeStageValidationResult SurfaceValidation { get; private set; }
    public TerrainRuntimeBakeStageValidationResult CollisionValidation { get; private set; }
    public TerrainRuntimeAddressablesStageValidationResult AddressablesValidation { get; private set; }
    public TerrainRuntimeSceneStageValidationResult SceneValidation { get; private set; }

    public bool FinalPlanCurrent { get; private set; }

    public TerrainRuntimeOutputFingerprintSnapshot FingerprintSnapshot { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyList<string> WarningMessages => warningMessages;

    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    internal TerrainRuntimeBakeValidationResult(
        TerrainRuntimeBakeValidationOutcome outcome,
        TerrainRuntimeBakePipelineResult pipelineResult,
        TerrainRuntimeBakePlan initialPlan,
        TerrainRuntimeBakeStageValidationResult heightValidation,
        TerrainRuntimeBakeStageValidationResult surfaceValidation,
        TerrainRuntimeBakeStageValidationResult collisionValidation,
        TerrainRuntimeAddressablesStageValidationResult addressablesValidation,
        TerrainRuntimeSceneStageValidationResult sceneValidation,
        bool finalPlanCurrent,
        TerrainRuntimeOutputFingerprintSnapshot fingerprintSnapshot,
        IEnumerable<string> warnings,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome = outcome;
        PipelineResult = pipelineResult;
        InitialPlan = initialPlan;
        HeightValidation = heightValidation;
        SurfaceValidation = surfaceValidation;
        CollisionValidation = collisionValidation;
        AddressablesValidation = addressablesValidation;
        SceneValidation = sceneValidation;
        FinalPlanCurrent = finalPlanCurrent;
        FingerprintSnapshot = fingerprintSnapshot;
        CreatedAtUtc = DateTime.UtcNow;

        warningMessages = new List<string>(warnings ?? new string[0]).AsReadOnly();
        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Runtime Bake Pipeline Validation");
        builder.AppendLine("Validation Outcome: " + Outcome);

        if (PipelineResult != null)
        {
            builder.AppendLine("Pipeline Outcome: " + PipelineResult.Outcome);
            builder.AppendLine("Pipeline Mode: " + PipelineResult.Mode);
            builder.AppendLine(
                "Remaining Work: " +
                (
                    PipelineResult.FinalPlan == null
                        ? "Unavailable"
                        : PipelineResult.FinalPlan.HasWork
                            ? TerrainRuntimeBakePipelineResult.BuildPlanSummary(PipelineResult.FinalPlan)
                            : "None"
                )
            );
        }

        AppendCoordinateStage(builder, "Heightmaps", HeightValidation);
        AppendCoordinateStage(builder, "Surface Masks", SurfaceValidation);
        AppendCoordinateStage(builder, "Collision", CollisionValidation);
        AppendAddressables(builder);
        AppendScene(builder);
        AppendTimings(builder);

        builder.AppendLine();
        builder.AppendLine("Final Plan Current: " + FinalPlanCurrent);
        builder.AppendLine(
            "Raw Duplicate Observation: Not available before current result canonicalization."
        );

        if (warningMessages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings (" + warningMessages.Count + "):");

            for (int index = 0; index < warningMessages.Count; index++)
            {
                builder.AppendLine("- " + warningMessages[index]);
            }
        }

        if (!string.IsNullOrEmpty(SummaryMessage))
        {
            builder.AppendLine();
            builder.AppendLine("Summary: " + SummaryMessage);
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine("Error: " + ErrorMessage);
        }

        builder.AppendLine();
        builder.Append("Result: " + GetResultLabel());

        return builder.ToString();
    }

    private void AppendCoordinateStage(
        StringBuilder builder,
        string label,
        TerrainRuntimeBakeStageValidationResult result
    )
    {
        builder.AppendLine();
        builder.AppendLine(label + ":");

        if (result == null || !result.Evaluated)
        {
            builder.AppendLine("  Not evaluated");
            return;
        }

        builder.AppendLine("  Expected Mode: " + result.ExpectedWorkMode);
        builder.AppendLine("  Actual Mode: " + result.ActualWorkMode);
        builder.AppendLine("  Stage Executed: " + result.StageExecuted);
        builder.AppendLine("  Expected: " + result.ExpectedCount);
        builder.AppendLine("  Requested: " + result.RequestedCount);
        builder.AppendLine("  Succeeded: " + result.SucceededCount);
        builder.AppendLine("  Failed: " + result.FailedCount);
        builder.AppendLine("  Unprocessed: " + result.UnprocessedCount);
        builder.AppendLine("  Created / Updated / Removed: " +
            result.CreatedCount + " / " + result.UpdatedCount + " / " + result.RemovedCount);
        builder.AppendLine("  Missing Requested: " + result.MissingRequested.Count);
        builder.AppendLine("  Unexpected Requested: " + result.UnexpectedRequested.Count);
        builder.AppendLine("  Missing Succeeded: " + result.MissingSucceeded.Count);
        builder.AppendLine("  Unexpected Succeeded: " + result.UnexpectedSucceeded.Count);
        builder.AppendLine("  Missing Classifications: " + result.MissingClassifications.Count);
        builder.AppendLine("  Unexpected Classifications: " + result.UnexpectedClassifications.Count);
        builder.AppendLine("  Classification Overlap: " + result.OverlappingClassifications.Count);
        builder.AppendLine("  " + (result.Passed ? "PASS" : "FAIL"));

        AppendMismatchPreview(builder, "Missing Requested", result.MissingRequested);
        AppendMismatchPreview(builder, "Unexpected Requested", result.UnexpectedRequested);
        AppendMismatchPreview(builder, "Missing Succeeded", result.MissingSucceeded);
        AppendMismatchPreview(builder, "Unexpected Succeeded", result.UnexpectedSucceeded);
        AppendMismatchPreview(builder, "Missing Classifications", result.MissingClassifications);
        AppendMismatchPreview(builder, "Unexpected Classifications", result.UnexpectedClassifications);
        AppendMismatchPreview(builder, "Classification Overlap", result.OverlappingClassifications);
    }

    private void AppendAddressables(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Addressables:");

        if (AddressablesValidation == null || !AddressablesValidation.Evaluated)
        {
            builder.AppendLine("  Not evaluated");
            return;
        }

        builder.AppendLine("  Expected Mode: " + AddressablesValidation.ExpectedOperationMode);
        builder.AppendLine("  Actual Mode: " + AddressablesValidation.ActualOperationMode);
        builder.AppendLine("  Stage Executed: " + AddressablesValidation.StageExecuted);
        builder.AppendLine("  Configuration Required: " + AddressablesValidation.ExpectedConfigurationRequired);
        builder.AppendLine("  Configuration Performed: " + AddressablesValidation.ConfigurationPerformed);
        builder.AppendLine("  Content Build Required: " + AddressablesValidation.ExpectedContentBuildRequired);
        builder.AppendLine("  Content Build Performed: " + AddressablesValidation.ContentBuildPerformed);
        builder.AppendLine("  Height Configuration Changed: " + AddressablesValidation.HeightConfigurationChanged);
        builder.AppendLine("  Surface Configuration Changed: " + AddressablesValidation.SurfaceConfigurationChanged);
        builder.AppendLine("  Collision Configuration Changed: " + AddressablesValidation.CollisionConfigurationChanged);
        builder.AppendLine("  Collision Runtime Metadata Updated: " + AddressablesValidation.CollisionRuntimeMetadataUpdated);
        builder.AppendLine("  Collision Markers Regenerated: " + AddressablesValidation.CollisionMarkersRegenerated);
        builder.AppendLine("  Collision Markers Reused: " + AddressablesValidation.CollisionMarkersReused);
        builder.AppendLine("  Collision Markers Removed: " + AddressablesValidation.CollisionMarkersRemoved);
        builder.AppendLine("  Entries Created / Moved / Removed: " +
            AddressablesValidation.EntriesCreated + " / " +
            AddressablesValidation.EntriesMoved + " / " +
            AddressablesValidation.EntriesRemoved);
        builder.AppendLine("  Addresses Updated: " + AddressablesValidation.AddressesUpdated);
        builder.AppendLine("  Labels Updated: " + AddressablesValidation.LabelsUpdated);
        builder.AppendLine("  Groups Created: " + AddressablesValidation.GroupsCreated);
        builder.AppendLine("  Schemas Created / Changed: " + AddressablesValidation.SchemasCreatedOrChanged);
        builder.AppendLine("  " + (AddressablesValidation.Passed ? "PASS" : "FAIL"));
    }

    private void AppendScene(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Runtime Scene:");

        if (SceneValidation == null || !SceneValidation.Evaluated)
        {
            builder.AppendLine("  Not evaluated");
            return;
        }

        builder.AppendLine("  Expected Execution: " + SceneValidation.ExpectedExecution);
        builder.AppendLine("  Plan Requested Metadata Update: " + SceneValidation.PlanRequestedMetadataUpdate);
        builder.AppendLine("  Stage Executed: " + SceneValidation.StageExecuted);
        builder.AppendLine("  Outcome: " + SceneValidation.ActualOutcome);
        builder.AppendLine("  Bounds Applied: " + SceneValidation.BoundsApplied);
        builder.AppendLine("  Height Streamer Synchronized: " + SceneValidation.HeightStreamerSynchronized);
        builder.AppendLine("  Surface Manifest Synchronized: " + SceneValidation.SurfaceManifestSynchronized);
        builder.AppendLine("  Collision Streamer Synchronized: " + SceneValidation.CollisionStreamerSynchronized);
        builder.AppendLine("  Serialized Components Changed: " + SceneValidation.SerializedComponentChangeCount);
        builder.AppendLine("  Scene Marked Dirty: " + SceneValidation.SceneMarkedDirty);
        builder.AppendLine("  RuntimeSceneMetadataDirty Cleared: " + SceneValidation.RuntimeSceneMetadataDirtyCleared);
        builder.AppendLine("  " + (SceneValidation.Passed ? "PASS" : "FAIL"));
    }

    private void AppendTimings(StringBuilder builder)
    {
        if (PipelineResult == null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Stage Timings:");
        builder.AppendLine("  Heightmaps: " + PipelineResult.HeightDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Surface Masks: " + PipelineResult.SurfaceDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Collision: " + PipelineResult.CollisionDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Addressables: " + PipelineResult.AddressablesDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Scene Sync: " + PipelineResult.SceneSyncDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Total: " + PipelineResult.DurationSeconds.ToString("0.00") + " seconds");
    }

    private static void AppendMismatchPreview(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        if (coordinates == null || coordinates.Count == 0)
        {
            return;
        }

        int count = Mathf.Min(MaxMismatchPreview, coordinates.Count);
        builder.Append("    " + label + ": ");

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            Vector2Int coordinate = coordinates[index];
            builder.Append("(" + coordinate.x + ", " + coordinate.y + ")");
        }

        if (coordinates.Count > count)
        {
            builder.Append(", ... " + (coordinates.Count - count) + " additional coordinates omitted");
        }

        builder.AppendLine();
    }

    private string GetResultLabel()
    {
        switch (Outcome)
        {
            case TerrainRuntimeBakeValidationOutcome.Passed:
                return "PASS";
            case TerrainRuntimeBakeValidationOutcome.PassedWithWarnings:
                return "PASS WITH WARNINGS";
            case TerrainRuntimeBakeValidationOutcome.Cancelled:
                return "CANCELLED";
            case TerrainRuntimeBakeValidationOutcome.Blocked:
                return "BLOCKED";
            case TerrainRuntimeBakeValidationOutcome.Failed:
                return "FAIL";
            default:
                return "NOT RUN";
        }
    }
}
