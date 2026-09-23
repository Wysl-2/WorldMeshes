using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

public enum TerrainRuntimeBakePipelineMode
{
    None,
    PendingChanges,
    RebuildAll
}

public enum TerrainRuntimeBakePipelineState
{
    Idle,
    Preflight,
    Heightmaps,
    HeightStreaming,
    SurfaceMasks,
    Collision,
    Addressables,
    SceneSync,
    Finalizing,
    Completed,
    Failed,
    Cancelled,
    Blocked
}

public enum TerrainRuntimeBakePipelineOutcome
{
    NoWork,
    Completed,
    CompletedWithWarnings,
    Cancelled,
    Failed,
    Blocked
}

public sealed class TerrainRuntimeBakePipelineResult
{
    private readonly ReadOnlyCollection<string> warningMessages;

    public TerrainRuntimeBakePipelineOutcome Outcome { get; private set; }
    public TerrainRuntimeBakePipelineMode Mode { get; private set; }
    public TerrainRuntimeBakePipelineState FinalState { get; private set; }
    public TerrainRuntimeBakePipelineState LastStage { get; private set; }
    public TerrainRuntimeBakePipelineState FailedStage { get; private set; }

    public TerrainRuntimeBakePlan InitialPlan { get; private set; }

    public TerrainRuntimeBakePlan HeightPlan { get; private set; }
    public TerrainRuntimeBakePlan HeightStreamingPlan { get; private set; }
    public TerrainRuntimeBakePlan SurfacePlan { get; private set; }
    public TerrainRuntimeBakePlan CollisionPlan { get; private set; }
    public TerrainRuntimeBakePlan AddressablesPlan { get; private set; }
    public TerrainRuntimeBakePlan SceneSyncPlan { get; private set; }

    public TerrainRuntimeBakePlan FinalPlan { get; private set; }

    public bool HeightStageExecuted { get; private set; }
    public bool HeightStreamingStageExecuted { get; private set; }
    public bool SurfaceStageExecuted { get; private set; }
    public bool CollisionStageExecuted { get; private set; }
    public bool AddressablesStageExecuted { get; private set; }
    public bool SceneSyncStageExecuted { get; private set; }

    public TerrainRuntimeHeightCompileResult HeightResult { get; private set; }
    public TerrainRuntimeHeightStreamingCompileResult HeightStreamingResult { get; private set; }
    public TerrainSurfaceMaskGenerationResult SurfaceResult { get; private set; }
    public TerrainCollisionGenerationResult CollisionResult { get; private set; }
    public TerrainRuntimeAddressablesResult AddressablesResult { get; private set; }
    public TerrainRuntimeSceneSynchronizationResult SceneSyncResult { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public double HeightDurationSeconds { get; private set; }
    public double HeightStreamingDurationSeconds { get; private set; }
    public double SurfaceDurationSeconds { get; private set; }
    public double CollisionDurationSeconds { get; private set; }
    public double AddressablesDurationSeconds { get; private set; }
    public double SceneSyncDurationSeconds { get; private set; }
    public double DurationSeconds { get; private set; }

    public IReadOnlyList<string> WarningMessages => warningMessages;

    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }
    public TerrainRuntimeBakeDiagnosticsSnapshot Diagnostics { get; private set; }

    internal TerrainRuntimeBakePipelineResult(
        TerrainRuntimeBakePipelineOutcome outcome,
        TerrainRuntimeBakePipelineMode mode,
        TerrainRuntimeBakePipelineState finalState,
        TerrainRuntimeBakePipelineState lastStage,
        TerrainRuntimeBakePipelineState failedStage,
        TerrainRuntimeBakePlan initialPlan,
        TerrainRuntimeBakePlan heightPlan,
        TerrainRuntimeBakePlan heightStreamingPlan,
        TerrainRuntimeBakePlan surfacePlan,
        TerrainRuntimeBakePlan collisionPlan,
        TerrainRuntimeBakePlan addressablesPlan,
        TerrainRuntimeBakePlan sceneSyncPlan,
        TerrainRuntimeBakePlan finalPlan,
        bool heightStageExecuted,
        bool heightStreamingStageExecuted,
        bool surfaceStageExecuted,
        bool collisionStageExecuted,
        bool addressablesStageExecuted,
        bool sceneSyncStageExecuted,
        TerrainRuntimeHeightCompileResult heightResult,
        TerrainRuntimeHeightStreamingCompileResult heightStreamingResult,
        TerrainSurfaceMaskGenerationResult surfaceResult,
        TerrainCollisionGenerationResult collisionResult,
        TerrainRuntimeAddressablesResult addressablesResult,
        TerrainRuntimeSceneSynchronizationResult sceneSyncResult,
        DateTime startedAtUtc,
        double heightDurationSeconds,
        double heightStreamingDurationSeconds,
        double surfaceDurationSeconds,
        double collisionDurationSeconds,
        double addressablesDurationSeconds,
        double sceneSyncDurationSeconds,
        double durationSeconds,
        IEnumerable<string> warningMessages,
        string errorMessage,
        string summaryMessage,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics = null
    )
    {
        Outcome = outcome;
        Mode = mode;
        FinalState = finalState;
        LastStage = lastStage;
        FailedStage = failedStage;

        InitialPlan = initialPlan;
        HeightPlan = heightPlan;
        HeightStreamingPlan = heightStreamingPlan;
        SurfacePlan = surfacePlan;
        CollisionPlan = collisionPlan;
        AddressablesPlan = addressablesPlan;
        SceneSyncPlan = sceneSyncPlan;
        FinalPlan = finalPlan;

        HeightStageExecuted = heightStageExecuted;
        HeightStreamingStageExecuted = heightStreamingStageExecuted;
        SurfaceStageExecuted = surfaceStageExecuted;
        CollisionStageExecuted = collisionStageExecuted;
        AddressablesStageExecuted = addressablesStageExecuted;
        SceneSyncStageExecuted = sceneSyncStageExecuted;

        HeightResult = heightResult;
        HeightStreamingResult = heightStreamingResult;
        SurfaceResult = surfaceResult;
        CollisionResult = collisionResult;
        AddressablesResult = addressablesResult;
        SceneSyncResult = sceneSyncResult;

        StartedAtUtc = startedAtUtc;

        HeightDurationSeconds = Math.Max(0d, heightDurationSeconds);
        HeightStreamingDurationSeconds = Math.Max(0d, heightStreamingDurationSeconds);
        SurfaceDurationSeconds = Math.Max(0d, surfaceDurationSeconds);
        CollisionDurationSeconds = Math.Max(0d, collisionDurationSeconds);
        AddressablesDurationSeconds = Math.Max(0d, addressablesDurationSeconds);
        SceneSyncDurationSeconds = Math.Max(0d, sceneSyncDurationSeconds);
        DurationSeconds = Math.Max(0d, durationSeconds);

        List<string> warnings = warningMessages != null
            ? new List<string>(warningMessages)
            : new List<string>();

        this.warningMessages = warnings.AsReadOnly();

        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
        Diagnostics = diagnostics;
    }

    public string BuildDiagnosticReport()
    {
        if (Diagnostics != null)
        {
            return
                TerrainRuntimeBakeReportFormatter.BuildTextReport(
                    this,
                    Diagnostics.Level
                );
        }

        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Unified Runtime Bake Result");
        builder.AppendLine("Outcome: " + Outcome);
        builder.AppendLine("Mode: " + Mode);
        builder.AppendLine("Final State: " + FinalState);
        builder.AppendLine("Last Stage: " + LastStage);

        if (FailedStage != TerrainRuntimeBakePipelineState.Idle)
        {
            builder.AppendLine("Failed Stage: " + FailedStage);
        }

        builder.AppendLine();
        builder.AppendLine("Initial Plan:");
        builder.AppendLine("  " + BuildPlanSummary(InitialPlan));

        if (InitialPlan != null && InitialPlan.IsBlocked && !string.IsNullOrEmpty(InitialPlan.BlockReason))
        {
            builder.AppendLine("  Block Reason: " + InitialPlan.BlockReason);
        }

        builder.AppendLine();
        AppendHeightSummary(builder);
        AppendHeightStreamingSummary(builder);
        AppendSurfaceSummary(builder);
        AppendCollisionSummary(builder);
        AppendAddressablesSummary(builder);
        AppendSceneSummary(builder);

        builder.AppendLine();
        builder.AppendLine("Stage Timings:");
        builder.AppendLine("  Heightmaps: " + HeightDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Height Streaming: " + HeightStreamingDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Surface Masks: " + SurfaceDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Collision: " + CollisionDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Addressables: " + AddressablesDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Scene Sync: " + SceneSyncDurationSeconds.ToString("0.00") + " seconds");
        builder.AppendLine("  Total: " + DurationSeconds.ToString("0.00") + " seconds");

        builder.AppendLine();
        builder.AppendLine("Remaining Runtime Bake Work: " + GetRemainingWorkLabel(FinalPlan));

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

        return builder.ToString();
    }

    public static string BuildPlanSummary(TerrainRuntimeBakePlan plan)
    {
        if (plan == null)
        {
            return "Unavailable";
        }

        StringBuilder builder = new StringBuilder();

        builder.Append(
            "Height " + plan.HeightWorkMode + " (" + plan.HeightTileCount + " tiles), "
        );

        builder.Append(
            "Height Streaming " + plan.HeightStreamingWorkMode + " (" +
            plan.HeightStreamingTileCount + " tiles), "
        );

        builder.Append(
            "Surface " + plan.SurfaceWorkMode + " (" + plan.SurfaceTileCount + " tiles), "
        );

        builder.Append(
            "Collision " + plan.CollisionWorkMode + " (" + plan.CollisionChunkCount + " chunks), "
        );

        builder.Append(
            "Addressables " + GetAddressablesPlanLabel(plan) + ", "
        );

        builder.Append(
            "Scene Sync " + (plan.RuntimeSceneMetadataUpdateRequired ? "Required" : "None")
        );

        if (plan.IsBlocked)
        {
            builder.Append(", BLOCKED");
        }

        return builder.ToString();
    }

    private void AppendHeightSummary(StringBuilder builder)
    {
        if (!HeightStageExecuted)
        {
            builder.AppendLine("Height: Skipped");
            return;
        }

        if (HeightResult == null)
        {
            builder.AppendLine("Height: No result");
            return;
        }

        builder.AppendLine(
            "Height: " + HeightResult.Outcome + ", " + HeightResult.WorkMode + ", " +
            HeightResult.SucceededTileCount + " / " + HeightResult.RequestedTileCount + " tiles"
        );
    }

    private void AppendHeightStreamingSummary(StringBuilder builder)
    {
        if (!HeightStreamingStageExecuted)
        {
            builder.AppendLine("Height Streaming: Skipped");
            return;
        }

        if (HeightStreamingResult == null)
        {
            builder.AppendLine("Height Streaming: No result");
            return;
        }

        builder.AppendLine(
            "Height Streaming: " + HeightStreamingResult.Outcome + ", " +
            HeightStreamingResult.WorkMode + ", " +
            HeightStreamingResult.SucceededTileCount + " / " +
            HeightStreamingResult.RequestedTileCount + " tiles"
        );
    }

    private void AppendSurfaceSummary(StringBuilder builder)
    {
        if (!SurfaceStageExecuted)
        {
            builder.AppendLine("Surface: Skipped");
            return;
        }

        if (SurfaceResult == null)
        {
            builder.AppendLine("Surface: No result");
            return;
        }

        builder.AppendLine(
            "Surface: " + SurfaceResult.Outcome + ", " + SurfaceResult.WorkMode + ", " +
            SurfaceResult.SucceededTileCount + " / " + SurfaceResult.RequestedTileCount + " tiles"
        );
    }

    private void AppendCollisionSummary(StringBuilder builder)
    {
        if (!CollisionStageExecuted)
        {
            builder.AppendLine("Collision: Skipped");
            return;
        }

        if (CollisionResult == null)
        {
            builder.AppendLine("Collision: No result");
            return;
        }

        builder.AppendLine(
            "Collision: " + CollisionResult.Outcome + ", " + CollisionResult.WorkMode + ", " +
            CollisionResult.SucceededChunkCount + " / " + CollisionResult.RequestedChunkCount + " chunks"
        );
    }

    private void AppendAddressablesSummary(StringBuilder builder)
    {
        if (!AddressablesStageExecuted)
        {
            builder.AppendLine("Addressables: Skipped");
            return;
        }

        if (AddressablesResult == null)
        {
            builder.AppendLine("Addressables: No result");
            return;
        }

        builder.AppendLine(
            "Addressables: " + AddressablesResult.Outcome + ", " + AddressablesResult.OperationMode +
            ", Content Built=" + AddressablesResult.ContentBuildPerformed +
            ", Markers Regenerated=" + AddressablesResult.CollisionMarkersRegenerated
        );
    }

    private void AppendSceneSummary(StringBuilder builder)
    {
        if (!SceneSyncStageExecuted)
        {
            builder.AppendLine("Scene Sync: Skipped");
            return;
        }

        if (SceneSyncResult == null)
        {
            builder.AppendLine("Scene Sync: No result");
            return;
        }

        builder.AppendLine("Scene Sync: " + SceneSyncResult.Outcome);
    }

    private static string GetRemainingWorkLabel(TerrainRuntimeBakePlan finalPlan)
    {
        if (finalPlan == null)
        {
            return "Unavailable";
        }

        return finalPlan.HasWork
            ? BuildPlanSummary(finalPlan)
            : "None";
    }

    private static string GetAddressablesPlanLabel(TerrainRuntimeBakePlan plan)
    {
        if (plan == null)
        {
            return "Unavailable";
        }

        if (plan.AddressablesConfigurationRequired)
        {
            return "Configure + Build";
        }

        if (plan.AddressablesContentBuildRequired)
        {
            return "Content Only";
        }

        return "None";
    }
}
