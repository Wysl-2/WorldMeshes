using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainRuntimeBakeResumeValidationScenario
{
    None,
    HeightCancellation,
    HeightStreamingCancellation,
    SurfaceCancellation,
    CollisionCancellation,
    CancelDuringAddressables,
    CancelBeforeSceneSync,
    FailureAfterHeight,
    FailureAfterHeightStreaming,
    FailureAfterSurface,
    FailureAfterCollision,
    AddressablesFailureResume,
    SceneSyncFailureResume
}

public enum TerrainRuntimeBakeResumeValidationOutcome
{
    NotRun,
    Passed,
    Failed,
    Blocked
}

public sealed class TerrainRuntimeBakeResumeValidationResult
{
    private readonly ReadOnlyCollection<Vector2Int> repeatedHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> repeatedHeightStreamingCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> repeatedSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> repeatedCollisionCoordinates;

    public TerrainRuntimeBakeResumeValidationScenario Scenario { get; private set; }
    public TerrainRuntimeBakeResumeValidationOutcome Outcome { get; private set; }

    public TerrainRuntimeBakePipelineResult InterruptedPipelineResult { get; private set; }
    public TerrainRuntimeBakePlan PostInterruptionPlan { get; private set; }
    public TerrainRuntimeBakeStateSnapshot PostInterruptionState { get; private set; }
    public TerrainRuntimeBakePipelineResult ResumedPipelineResult { get; private set; }
    public TerrainRuntimeBakeValidationResult ResumedValidationResult { get; private set; }

    public bool InterruptionMatchedExpectation { get; private set; }
    public bool ResumeStartedAtExpectedWork { get; private set; }
    public bool UpstreamStageRepeated { get; private set; }
    public bool FinalPlanCurrent { get; private set; }

    public IReadOnlyList<Vector2Int> RepeatedHeightCoordinates => repeatedHeightCoordinates;
    public IReadOnlyList<Vector2Int> RepeatedHeightStreamingCoordinates => repeatedHeightStreamingCoordinates;
    public IReadOnlyList<Vector2Int> RepeatedSurfaceCoordinates => repeatedSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> RepeatedCollisionCoordinates => repeatedCollisionCoordinates;

    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    public bool Passed => Outcome == TerrainRuntimeBakeResumeValidationOutcome.Passed;

    internal TerrainRuntimeBakeResumeValidationResult(
        TerrainRuntimeBakeResumeValidationScenario scenario,
        TerrainRuntimeBakeResumeValidationOutcome outcome,
        TerrainRuntimeBakePipelineResult interruptedPipelineResult,
        TerrainRuntimeBakePlan postInterruptionPlan,
        TerrainRuntimeBakeStateSnapshot postInterruptionState,
        TerrainRuntimeBakePipelineResult resumedPipelineResult,
        TerrainRuntimeBakeValidationResult resumedValidationResult,
        bool interruptionMatchedExpectation,
        bool resumeStartedAtExpectedWork,
        bool upstreamStageRepeated,
        bool finalPlanCurrent,
        IEnumerable<Vector2Int> repeatedHeightCoordinates,
        IEnumerable<Vector2Int> repeatedHeightStreamingCoordinates,
        IEnumerable<Vector2Int> repeatedSurfaceCoordinates,
        IEnumerable<Vector2Int> repeatedCollisionCoordinates,
        string errorMessage,
        string summaryMessage
    )
    {
        Scenario = scenario;
        Outcome = outcome;
        InterruptedPipelineResult = interruptedPipelineResult;
        PostInterruptionPlan = postInterruptionPlan;
        PostInterruptionState = postInterruptionState;
        ResumedPipelineResult = resumedPipelineResult;
        ResumedValidationResult = resumedValidationResult;
        InterruptionMatchedExpectation = interruptionMatchedExpectation;
        ResumeStartedAtExpectedWork = resumeStartedAtExpectedWork;
        UpstreamStageRepeated = upstreamStageRepeated;
        FinalPlanCurrent = finalPlanCurrent;

        this.repeatedHeightCoordinates = Copy(repeatedHeightCoordinates).AsReadOnly();
        this.repeatedHeightStreamingCoordinates = Copy(repeatedHeightStreamingCoordinates).AsReadOnly();
        this.repeatedSurfaceCoordinates = Copy(repeatedSurfaceCoordinates).AsReadOnly();
        this.repeatedCollisionCoordinates = Copy(repeatedCollisionCoordinates).AsReadOnly();

        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Runtime Bake Resume Validation");
        builder.AppendLine("Scenario: " + Scenario);
        builder.AppendLine("Outcome: " + Outcome);
        builder.AppendLine();

        builder.AppendLine("Interrupted Pipeline:");
        if (InterruptedPipelineResult == null)
        {
            builder.AppendLine("  Unavailable");
        }
        else
        {
            builder.AppendLine("  Outcome: " + InterruptedPipelineResult.Outcome);
            builder.AppendLine("  Last Stage: " + InterruptedPipelineResult.LastStage);
            builder.AppendLine("  Failed Stage: " + InterruptedPipelineResult.FailedStage);
        }

        builder.AppendLine("  Interruption Matched Expectation: " + InterruptionMatchedExpectation);
        builder.AppendLine();

        builder.AppendLine("Post-Interruption Plan:");
        builder.AppendLine("  " + TerrainRuntimeBakePipelineResult.BuildPlanSummary(PostInterruptionPlan));

        if (PostInterruptionState != null)
        {
            builder.AppendLine("Post-Interruption Persistent State:");
            builder.AppendLine("  Revision: " + PostInterruptionState.StateRevision);
            builder.AppendLine("  Height Pending: " + PostInterruptionState.PendingHeightTileCount);
            builder.AppendLine("  Height Streaming Pending: " + PostInterruptionState.PendingHeightStreamingTileCount);
            builder.AppendLine("  Surface Pending: " + PostInterruptionState.PendingSurfaceTileCount);
            builder.AppendLine("  Collision Pending: " + PostInterruptionState.PendingCollisionChunkCount);
            builder.AppendLine("  Addressables Config Dirty: " + PostInterruptionState.AddressablesConfigurationDirty);
            builder.AppendLine("  Addressables Content Dirty: " + PostInterruptionState.AddressablesContentDirty);
            builder.AppendLine("  Runtime Scene Dirty: " + PostInterruptionState.RuntimeSceneMetadataDirty);
        }

        builder.AppendLine();
        builder.AppendLine("Resume:");
        builder.AppendLine("  Started At Expected Work: " + ResumeStartedAtExpectedWork);
        builder.AppendLine("  Upstream Stage Repeated: " + UpstreamStageRepeated);
        builder.AppendLine("  Repeated Height Coordinates: " + repeatedHeightCoordinates.Count);
        builder.AppendLine("  Repeated Height Streaming Coordinates: " + repeatedHeightStreamingCoordinates.Count);
        builder.AppendLine("  Repeated Surface Coordinates: " + repeatedSurfaceCoordinates.Count);
        builder.AppendLine("  Repeated Collision Coordinates: " + repeatedCollisionCoordinates.Count);

        if (ResumedPipelineResult != null)
        {
            builder.AppendLine("  Pipeline Outcome: " + ResumedPipelineResult.Outcome);
            builder.AppendLine("  Height Executed: " + ResumedPipelineResult.HeightStageExecuted);
            builder.AppendLine("  Height Streaming Executed: " + ResumedPipelineResult.HeightStreamingStageExecuted);
            builder.AppendLine("  Surface Executed: " + ResumedPipelineResult.SurfaceStageExecuted);
            builder.AppendLine("  Collision Executed: " + ResumedPipelineResult.CollisionStageExecuted);
            builder.AppendLine("  Addressables Executed: " + ResumedPipelineResult.AddressablesStageExecuted);
            builder.AppendLine("  Scene Sync Executed: " + ResumedPipelineResult.SceneSyncStageExecuted);
        }

        builder.AppendLine("  Final Plan Current: " + FinalPlanCurrent);

        if (ResumedValidationResult != null)
        {
            builder.AppendLine("  Package 10.1 Resume Validation: " + ResumedValidationResult.Outcome);
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
        builder.AppendLine("Result: " + (Passed ? "PASS" : "FAIL"));
        return builder.ToString();
    }

    private static List<Vector2Int> Copy(IEnumerable<Vector2Int> source)
    {
        List<Vector2Int> result = source != null
            ? new List<Vector2Int>(source)
            : new List<Vector2Int>();
        result.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
        return result;
    }
}
