using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public sealed class TerrainRuntimeInvalidationValidationResult
{
    private readonly ReadOnlyCollection<Vector2Int> missingHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> missingSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> missingCollisionCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedCollisionCoordinates;

    public TerrainRuntimeInvalidationScenario Scenario { get; private set; }
    public TerrainRuntimeInvalidationValidationOutcome Outcome { get; private set; }

    public TerrainRuntimeInvalidationExpectation Expectation { get; private set; }
    public TerrainRuntimeBakeStateSnapshot ActualState { get; private set; }
    public TerrainRuntimeBakePlan ActualPlan { get; private set; }

    public IReadOnlyList<Vector2Int> MissingHeightCoordinates => missingHeightCoordinates;
    public IReadOnlyList<Vector2Int> UnexpectedHeightCoordinates => unexpectedHeightCoordinates;
    public IReadOnlyList<Vector2Int> MissingSurfaceCoordinates => missingSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> UnexpectedSurfaceCoordinates => unexpectedSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> MissingCollisionCoordinates => missingCollisionCoordinates;
    public IReadOnlyList<Vector2Int> UnexpectedCollisionCoordinates => unexpectedCollisionCoordinates;

    public bool PersistentStateMatched { get; private set; }
    public bool PlannerMatchedExpectedInvalidation { get; private set; }
    public bool UnexpectedFullEscalation { get; private set; }
    public bool MissingRequiredFullEscalation { get; private set; }

    public TerrainRuntimeBakeValidationResult BakeValidationResult { get; private set; }

    public string WarningMessage { get; private set; }
    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    public bool Passed =>
        Outcome == TerrainRuntimeInvalidationValidationOutcome.Passed
        || Outcome == TerrainRuntimeInvalidationValidationOutcome.PassedWithWarnings;

    internal TerrainRuntimeInvalidationValidationResult(
        TerrainRuntimeInvalidationScenario scenario,
        TerrainRuntimeInvalidationValidationOutcome outcome,
        TerrainRuntimeInvalidationExpectation expectation,
        TerrainRuntimeBakeStateSnapshot actualState,
        TerrainRuntimeBakePlan actualPlan,
        IEnumerable<Vector2Int> missingHeight,
        IEnumerable<Vector2Int> unexpectedHeight,
        IEnumerable<Vector2Int> missingSurface,
        IEnumerable<Vector2Int> unexpectedSurface,
        IEnumerable<Vector2Int> missingCollision,
        IEnumerable<Vector2Int> unexpectedCollision,
        bool persistentStateMatched,
        bool plannerMatched,
        bool unexpectedFullEscalation,
        bool missingRequiredFullEscalation,
        string warningMessage,
        string errorMessage,
        string summaryMessage
    )
    {
        Scenario = scenario;
        Outcome = outcome;
        Expectation = expectation;
        ActualState = actualState;
        ActualPlan = actualPlan;

        missingHeightCoordinates = CopyCoordinates(missingHeight);
        unexpectedHeightCoordinates = CopyCoordinates(unexpectedHeight);
        missingSurfaceCoordinates = CopyCoordinates(missingSurface);
        unexpectedSurfaceCoordinates = CopyCoordinates(unexpectedSurface);
        missingCollisionCoordinates = CopyCoordinates(missingCollision);
        unexpectedCollisionCoordinates = CopyCoordinates(unexpectedCollision);

        PersistentStateMatched = persistentStateMatched;
        PlannerMatchedExpectedInvalidation = plannerMatched;
        UnexpectedFullEscalation = unexpectedFullEscalation;
        MissingRequiredFullEscalation = missingRequiredFullEscalation;

        WarningMessage = warningMessage ?? "";
        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    internal void AttachBakeValidation(
        TerrainRuntimeBakeValidationResult validation
    )
    {
        BakeValidationResult = validation;

        if (validation == null)
        {
            Outcome = TerrainRuntimeInvalidationValidationOutcome.Failed;
            ErrorMessage =
                "The Package 10.1 bake validation produced no result.";
            return;
        }

        bool bakePassed =
            validation.Outcome == TerrainRuntimeBakeValidationOutcome.Passed
            || validation.Outcome == TerrainRuntimeBakeValidationOutcome.PassedWithWarnings;

        if (!bakePassed)
        {
            Outcome = TerrainRuntimeInvalidationValidationOutcome.Failed;
            ErrorMessage =
                "Package 10.1 did not validate the regenerated work after invalidation.";
            return;
        }

        SummaryMessage =
            "Invalidation state, planner output, and Package 10.1 regenerated-work validation all passed.";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Runtime Invalidation Validation");
        builder.AppendLine("Scenario: " + Scenario);
        builder.AppendLine("Outcome: " + Outcome);

        if (Expectation != null)
        {
            builder.AppendLine();

            AppendStage(
                builder,
                "Height",
                Expectation.ExpectedHeightMode,
                ActualPlan != null
                    ? ActualPlan.HeightWorkMode
                    : TerrainRuntimeBakeWorkMode.None,
                Expectation.PlanHeightCoordinates.Count,
                ActualPlan != null
                    ? ActualPlan.HeightTileCount
                    : 0,
                missingHeightCoordinates.Count,
                unexpectedHeightCoordinates.Count
            );

            AppendStage(
                builder,
                "Surface",
                Expectation.ExpectedSurfaceMode,
                ActualPlan != null
                    ? ActualPlan.SurfaceWorkMode
                    : TerrainRuntimeBakeWorkMode.None,
                Expectation.PlanSurfaceCoordinates.Count,
                ActualPlan != null
                    ? ActualPlan.SurfaceTileCount
                    : 0,
                missingSurfaceCoordinates.Count,
                unexpectedSurfaceCoordinates.Count
            );

            AppendStage(
                builder,
                "Collision",
                Expectation.ExpectedCollisionMode,
                ActualPlan != null
                    ? ActualPlan.CollisionWorkMode
                    : TerrainRuntimeBakeWorkMode.None,
                Expectation.PlanCollisionCoordinates.Count,
                ActualPlan != null
                    ? ActualPlan.CollisionChunkCount
                    : 0,
                missingCollisionCoordinates.Count,
                unexpectedCollisionCoordinates.Count
            );

            builder.AppendLine(
                "Persistent State: " +
                (PersistentStateMatched ? "PASS" : "FAIL")
            );

            builder.AppendLine(
                "Planner: " +
                (PlannerMatchedExpectedInvalidation ? "PASS" : "FAIL")
            );

            builder.AppendLine(
                "Unexpected Full Escalation: " +
                UnexpectedFullEscalation
            );

            builder.AppendLine(
                "Missing Required Full Escalation: " +
                MissingRequiredFullEscalation
            );

            if (ActualState != null)
            {
                builder.AppendLine();
                builder.AppendLine("Persistent Pending:");
                builder.AppendLine(
                    "  Height: " +
                    ActualState.PendingHeightTileCount
                );
                builder.AppendLine(
                    "  Surface: " +
                    ActualState.PendingSurfaceTileCount
                );
                builder.AppendLine(
                    "  Collision: " +
                    ActualState.PendingCollisionChunkCount
                );
                builder.AppendLine(
                    "  Full Height / Surface / Collision: " +
                    ActualState.FullHeightRebuildRequired + " / " +
                    ActualState.FullSurfaceRebuildRequired + " / " +
                    ActualState.FullCollisionRebuildRequired
                );
                builder.AppendLine(
                    "  Addressables Config / Content: " +
                    ActualState.AddressablesConfigurationDirty + " / " +
                    ActualState.AddressablesContentDirty
                );
                builder.AppendLine(
                    "  Runtime Scene Dirty: " +
                    ActualState.RuntimeSceneMetadataDirty
                );
            }

            if (ActualPlan != null)
            {
                builder.AppendLine();
                builder.AppendLine(
                    "Plan: " +
                    TerrainRuntimeBakePipelineResult.BuildPlanSummary(
                        ActualPlan
                    )
                );
            }
        }

        if (BakeValidationResult != null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Package 10.1 Bake Validation: " +
                BakeValidationResult.Outcome
            );
        }

        AppendCoordinatePreview(builder, "Missing Height", missingHeightCoordinates);
        AppendCoordinatePreview(builder, "Unexpected Height", unexpectedHeightCoordinates);
        AppendCoordinatePreview(builder, "Missing Surface", missingSurfaceCoordinates);
        AppendCoordinatePreview(builder, "Unexpected Surface", unexpectedSurfaceCoordinates);
        AppendCoordinatePreview(builder, "Missing Collision", missingCollisionCoordinates);
        AppendCoordinatePreview(builder, "Unexpected Collision", unexpectedCollisionCoordinates);

        if (!string.IsNullOrEmpty(WarningMessage))
        {
            builder.AppendLine();
            builder.AppendLine("Warning: " + WarningMessage);
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine("Error: " + ErrorMessage);
        }

        if (!string.IsNullOrEmpty(SummaryMessage))
        {
            builder.AppendLine();
            builder.AppendLine("Summary: " + SummaryMessage);
        }

        builder.AppendLine();
        builder.AppendLine("Result: " + (Passed ? "PASS" : "FAIL"));

        return builder.ToString();
    }

    private static void AppendStage(
        StringBuilder builder,
        string label,
        TerrainRuntimeBakeWorkMode expectedMode,
        TerrainRuntimeBakeWorkMode actualMode,
        int expectedCount,
        int actualCount,
        int missingCount,
        int unexpectedCount
    )
    {
        builder.AppendLine(label + ":");
        builder.AppendLine("  Expected Mode: " + expectedMode);
        builder.AppendLine("  Actual Mode: " + actualMode);
        builder.AppendLine("  Expected Coordinates: " + expectedCount);
        builder.AppendLine("  Actual Coordinates: " + actualCount);
        builder.AppendLine("  Missing: " + missingCount);
        builder.AppendLine("  Unexpected: " + unexpectedCount);
        builder.AppendLine(
            "  " +
            (
                expectedMode == actualMode
                && missingCount == 0
                && unexpectedCount == 0
                    ? "PASS"
                    : "FAIL"
            )
        );
        builder.AppendLine();
    }

    private static void AppendCoordinatePreview(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        if (coordinates == null || coordinates.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(label + " (" + coordinates.Count + "):");

        int limit = Mathf.Min(20, coordinates.Count);

        for (int index = 0; index < limit; index++)
        {
            Vector2Int coordinate = coordinates[index];

            builder.AppendLine(
                "  (" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")"
            );
        }

        if (coordinates.Count > limit)
        {
            builder.AppendLine(
                "  ... " +
                (coordinates.Count - limit) +
                " additional coordinates omitted"
            );
        }
    }

    private static ReadOnlyCollection<Vector2Int> CopyCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        List<Vector2Int> result =
            source != null
                ? new List<Vector2Int>(new HashSet<Vector2Int>(source))
                : new List<Vector2Int>();

        result.Sort(TerrainRuntimeInvalidationExpectation.CompareCoordinates);
        return result.AsReadOnly();
    }
}
