using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainRuntimeBakePersistenceValidationOutcome
{
    NotRun,
    Passed,
    Failed,
    Blocked
}

public sealed class TerrainRuntimeBakePersistenceValidationResult
{
    private readonly ReadOnlyCollection<Vector2Int> missingHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> missingSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> missingCollisionCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedCollisionCoordinates;

    public TerrainRuntimeBakePersistenceValidationOutcome Outcome { get; private set; }
    public bool CheckpointFound { get; private set; }
    public bool CheckpointReadable { get; private set; }
    public bool FormatCompatible { get; private set; }

    public long ExpectedStateRevision { get; private set; }
    public long CurrentStateRevision { get; private set; }

    public int ExpectedHeightCount { get; private set; }
    public int CurrentHeightCount { get; private set; }
    public int ExpectedSurfaceCount { get; private set; }
    public int CurrentSurfaceCount { get; private set; }
    public int ExpectedCollisionCount { get; private set; }
    public int CurrentCollisionCount { get; private set; }

    public bool StateRevisionMatches { get; private set; }
    public bool HeightCoordinatesMatch { get; private set; }
    public bool SurfaceCoordinatesMatch { get; private set; }
    public bool CollisionCoordinatesMatch { get; private set; }
    public bool FullFlagsMatch { get; private set; }
    public bool AddressablesFlagsMatch { get; private set; }
    public bool RuntimeSceneFlagMatches { get; private set; }
    public bool AuthoringSignatureMatches { get; private set; }
    public bool CurrentAuthoringSignatureMatches { get; private set; }
    public bool SurfaceSettingsSignatureMatches { get; private set; }
    public bool CollisionSettingsSignatureMatches { get; private set; }

    public IReadOnlyList<Vector2Int> MissingHeightCoordinates => missingHeightCoordinates;
    public IReadOnlyList<Vector2Int> UnexpectedHeightCoordinates => unexpectedHeightCoordinates;
    public IReadOnlyList<Vector2Int> MissingSurfaceCoordinates => missingSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> UnexpectedSurfaceCoordinates => unexpectedSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> MissingCollisionCoordinates => missingCollisionCoordinates;
    public IReadOnlyList<Vector2Int> UnexpectedCollisionCoordinates => unexpectedCollisionCoordinates;

    public bool OverallPassed => Outcome == TerrainRuntimeBakePersistenceValidationOutcome.Passed;

    public string WarningMessage { get; private set; }
    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    internal TerrainRuntimeBakePersistenceValidationResult(
        TerrainRuntimeBakePersistenceValidationOutcome outcome,
        bool checkpointFound,
        bool checkpointReadable,
        bool formatCompatible,
        long expectedStateRevision,
        long currentStateRevision,
        int expectedHeightCount,
        int currentHeightCount,
        int expectedSurfaceCount,
        int currentSurfaceCount,
        int expectedCollisionCount,
        int currentCollisionCount,
        bool stateRevisionMatches,
        IEnumerable<Vector2Int> missingHeight,
        IEnumerable<Vector2Int> unexpectedHeight,
        IEnumerable<Vector2Int> missingSurface,
        IEnumerable<Vector2Int> unexpectedSurface,
        IEnumerable<Vector2Int> missingCollision,
        IEnumerable<Vector2Int> unexpectedCollision,
        bool fullFlagsMatch,
        bool addressablesFlagsMatch,
        bool runtimeSceneFlagMatches,
        bool authoringSignatureMatches,
        bool currentAuthoringSignatureMatches,
        bool surfaceSettingsSignatureMatches,
        bool collisionSettingsSignatureMatches,
        string warningMessage,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome = outcome;
        CheckpointFound = checkpointFound;
        CheckpointReadable = checkpointReadable;
        FormatCompatible = formatCompatible;

        ExpectedStateRevision = expectedStateRevision;
        CurrentStateRevision = currentStateRevision;

        ExpectedHeightCount = expectedHeightCount;
        CurrentHeightCount = currentHeightCount;
        ExpectedSurfaceCount = expectedSurfaceCount;
        CurrentSurfaceCount = currentSurfaceCount;
        ExpectedCollisionCount = expectedCollisionCount;
        CurrentCollisionCount = currentCollisionCount;

        StateRevisionMatches = stateRevisionMatches;

        missingHeightCoordinates = Copy(missingHeight).AsReadOnly();
        unexpectedHeightCoordinates = Copy(unexpectedHeight).AsReadOnly();
        missingSurfaceCoordinates = Copy(missingSurface).AsReadOnly();
        unexpectedSurfaceCoordinates = Copy(unexpectedSurface).AsReadOnly();
        missingCollisionCoordinates = Copy(missingCollision).AsReadOnly();
        unexpectedCollisionCoordinates = Copy(unexpectedCollision).AsReadOnly();

        HeightCoordinatesMatch =
            missingHeightCoordinates.Count == 0 && unexpectedHeightCoordinates.Count == 0;
        SurfaceCoordinatesMatch =
            missingSurfaceCoordinates.Count == 0 && unexpectedSurfaceCoordinates.Count == 0;
        CollisionCoordinatesMatch =
            missingCollisionCoordinates.Count == 0 && unexpectedCollisionCoordinates.Count == 0;

        FullFlagsMatch = fullFlagsMatch;
        AddressablesFlagsMatch = addressablesFlagsMatch;
        RuntimeSceneFlagMatches = runtimeSceneFlagMatches;
        AuthoringSignatureMatches = authoringSignatureMatches;
        CurrentAuthoringSignatureMatches = currentAuthoringSignatureMatches;
        SurfaceSettingsSignatureMatches = surfaceSettingsSignatureMatches;
        CollisionSettingsSignatureMatches = collisionSettingsSignatureMatches;

        WarningMessage = warningMessage ?? "";
        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Runtime Bake Persistence Validation");
        builder.AppendLine("Outcome: " + Outcome);
        builder.AppendLine("Checkpoint Found: " + CheckpointFound);
        builder.AppendLine("Checkpoint Readable: " + CheckpointReadable);
        builder.AppendLine("Format Compatible: " + FormatCompatible);
        builder.AppendLine();

        builder.AppendLine("State Revision:");
        builder.AppendLine("  Expected: " + ExpectedStateRevision);
        builder.AppendLine("  Current: " + CurrentStateRevision);
        builder.AppendLine("  " + (StateRevisionMatches ? "MATCH" : "MISMATCH"));
        builder.AppendLine();

        AppendCoordinateSection(
            builder,
            "Height Pending",
            ExpectedHeightCount,
            CurrentHeightCount,
            missingHeightCoordinates,
            unexpectedHeightCoordinates
        );

        AppendCoordinateSection(
            builder,
            "Surface Pending",
            ExpectedSurfaceCount,
            CurrentSurfaceCount,
            missingSurfaceCoordinates,
            unexpectedSurfaceCoordinates
        );

        AppendCoordinateSection(
            builder,
            "Collision Pending",
            ExpectedCollisionCount,
            CurrentCollisionCount,
            missingCollisionCoordinates,
            unexpectedCollisionCoordinates
        );

        builder.AppendLine("Full Rebuild Flags: " + (FullFlagsMatch ? "MATCH" : "MISMATCH"));
        builder.AppendLine("Addressables Dirty Flags: " + (AddressablesFlagsMatch ? "MATCH" : "MISMATCH"));
        builder.AppendLine("Runtime Scene Dirty Flag: " + (RuntimeSceneFlagMatches ? "MATCH" : "MISMATCH"));
        builder.AppendLine("Observed Authoring Signature: " + (AuthoringSignatureMatches ? "MATCH" : "MISMATCH"));
        builder.AppendLine("Current Authoring Signature: " + (CurrentAuthoringSignatureMatches ? "MATCH" : "MISMATCH"));
        builder.AppendLine("Surface Settings Signature: " + (SurfaceSettingsSignatureMatches ? "MATCH" : "MISMATCH"));
        builder.AppendLine("Collision Settings Signature: " + (CollisionSettingsSignatureMatches ? "MATCH" : "MISMATCH"));

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
        builder.AppendLine("Result: " + (OverallPassed ? "PASS" : "FAIL"));
        return builder.ToString();
    }

    private static void AppendCoordinateSection(
        StringBuilder builder,
        string label,
        int expectedCount,
        int currentCount,
        IReadOnlyList<Vector2Int> missing,
        IReadOnlyList<Vector2Int> unexpected
    )
    {
        builder.AppendLine(label + ":");
        builder.AppendLine("  Expected: " + expectedCount);
        builder.AppendLine("  Current: " + currentCount);
        builder.AppendLine("  Missing: " + missing.Count);
        builder.AppendLine("  Unexpected: " + unexpected.Count);
        builder.AppendLine("  " + (missing.Count == 0 && unexpected.Count == 0 ? "MATCH" : "MISMATCH"));
        builder.AppendLine();
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
