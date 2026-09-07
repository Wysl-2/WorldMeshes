using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/*
 * Read-only Package 10.1 comparison for one coordinate-based runtime stage.
 *
 * Height, Surface, and Collision all expose canonical unique coordinate sets.
 * This validator compares those exposed sets exactly and separately validates
 * the requested -> succeeded/failed/unprocessed partition.
 */
public sealed class TerrainRuntimeBakeStageValidationResult
{
    private readonly ReadOnlyCollection<Vector2Int> expectedCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> requestedCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> succeededCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> failedCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unprocessedCoordinates;

    private readonly ReadOnlyCollection<Vector2Int> missingRequested;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedRequested;
    private readonly ReadOnlyCollection<Vector2Int> missingSucceeded;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedSucceeded;
    private readonly ReadOnlyCollection<Vector2Int> missingClassifications;
    private readonly ReadOnlyCollection<Vector2Int> unexpectedClassifications;
    private readonly ReadOnlyCollection<Vector2Int> overlappingClassifications;

    public string StageName { get; private set; }

    public bool Evaluated { get; private set; }
    public bool StageExecuted { get; private set; }
    public bool CompletedSuccessfully { get; private set; }

    public TerrainRuntimeBakeWorkMode ExpectedWorkMode { get; private set; }
    public TerrainRuntimeBakeWorkMode ActualWorkMode { get; private set; }

    public IReadOnlyList<Vector2Int> ExpectedCoordinates => expectedCoordinates;
    public IReadOnlyList<Vector2Int> RequestedCoordinates => requestedCoordinates;
    public IReadOnlyList<Vector2Int> SucceededCoordinates => succeededCoordinates;
    public IReadOnlyList<Vector2Int> FailedCoordinates => failedCoordinates;
    public IReadOnlyList<Vector2Int> UnprocessedCoordinates => unprocessedCoordinates;

    public IReadOnlyList<Vector2Int> MissingRequested => missingRequested;
    public IReadOnlyList<Vector2Int> UnexpectedRequested => unexpectedRequested;
    public IReadOnlyList<Vector2Int> MissingSucceeded => missingSucceeded;
    public IReadOnlyList<Vector2Int> UnexpectedSucceeded => unexpectedSucceeded;
    public IReadOnlyList<Vector2Int> MissingClassifications => missingClassifications;
    public IReadOnlyList<Vector2Int> UnexpectedClassifications => unexpectedClassifications;
    public IReadOnlyList<Vector2Int> OverlappingClassifications => overlappingClassifications;

    public int ExpectedCount => expectedCoordinates.Count;
    public int RequestedCount => requestedCoordinates.Count;
    public int SucceededCount => succeededCoordinates.Count;
    public int FailedCount => failedCoordinates.Count;
    public int UnprocessedCount => unprocessedCoordinates.Count;

    public int CreatedCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int RemovedCount { get; private set; }

    public bool ExactRequestedMatch { get; private set; }
    public bool ExactSucceededMatch { get; private set; }
    public bool ResultPartitionConsistent { get; private set; }
    public bool WorkModeMatches { get; private set; }

    /*
     * Packages 03/05/06 canonicalize their exposed coordinate collections into
     * HashSet-backed sorted lists before Package 10.1 observes them. Raw duplicate
     * submission is therefore intentionally reported as unavailable rather than
     * changing generator behavior solely for diagnostics.
     */
    public bool RawDuplicateObservationAvailable => false;

    public string RawDuplicateObservationNote =>
        "Exposed requested/result coordinates are canonical unique sets. Raw duplicate submission is not observable through the current result contract.";

    public bool Passed { get; private set; }
    public string ErrorMessage { get; private set; }

    internal TerrainRuntimeBakeStageValidationResult(
        string stageName,
        bool evaluated,
        bool stageExecuted,
        bool completedSuccessfully,
        TerrainRuntimeBakeWorkMode expectedWorkMode,
        TerrainRuntimeBakeWorkMode actualWorkMode,
        IEnumerable<Vector2Int> expected,
        IEnumerable<Vector2Int> requested,
        IEnumerable<Vector2Int> succeeded,
        IEnumerable<Vector2Int> failed,
        IEnumerable<Vector2Int> unprocessed,
        int createdCount,
        int updatedCount,
        int removedCount,
        string sourceErrorMessage = ""
    )
    {
        StageName = stageName ?? "";
        Evaluated = evaluated;
        StageExecuted = stageExecuted;
        CompletedSuccessfully = completedSuccessfully;
        ExpectedWorkMode = expectedWorkMode;
        ActualWorkMode = actualWorkMode;

        expectedCoordinates = CreateSortedCoordinates(expected);
        requestedCoordinates = CreateSortedCoordinates(requested);
        succeededCoordinates = CreateSortedCoordinates(succeeded);
        failedCoordinates = CreateSortedCoordinates(failed);
        unprocessedCoordinates = CreateSortedCoordinates(unprocessed);

        CreatedCount = Mathf.Max(0, createdCount);
        UpdatedCount = Mathf.Max(0, updatedCount);
        RemovedCount = Mathf.Max(0, removedCount);

        HashSet<Vector2Int> expectedSet = new HashSet<Vector2Int>(expectedCoordinates);
        HashSet<Vector2Int> requestedSet = new HashSet<Vector2Int>(requestedCoordinates);
        HashSet<Vector2Int> succeededSet = new HashSet<Vector2Int>(succeededCoordinates);
        HashSet<Vector2Int> failedSet = new HashSet<Vector2Int>(failedCoordinates);
        HashSet<Vector2Int> unprocessedSet = new HashSet<Vector2Int>(unprocessedCoordinates);

        missingRequested = Difference(expectedSet, requestedSet);
        unexpectedRequested = Difference(requestedSet, expectedSet);
        missingSucceeded = Difference(expectedSet, succeededSet);
        unexpectedSucceeded = Difference(succeededSet, expectedSet);

        HashSet<Vector2Int> classifiedUnion = new HashSet<Vector2Int>(succeededSet);
        classifiedUnion.UnionWith(failedSet);
        classifiedUnion.UnionWith(unprocessedSet);

        missingClassifications = Difference(requestedSet, classifiedUnion);
        unexpectedClassifications = Difference(classifiedUnion, requestedSet);
        overlappingClassifications = FindClassificationOverlap(
            succeededSet,
            failedSet,
            unprocessedSet
        );

        ExactRequestedMatch =
            missingRequested.Count == 0
            && unexpectedRequested.Count == 0;

        ExactSucceededMatch =
            missingSucceeded.Count == 0
            && unexpectedSucceeded.Count == 0;

        ResultPartitionConsistent =
            missingClassifications.Count == 0
            && unexpectedClassifications.Count == 0
            && overlappingClassifications.Count == 0;

        WorkModeMatches =
            !stageExecuted
            || expectedWorkMode == actualWorkMode;

        ErrorMessage = sourceErrorMessage ?? "";

        Passed = CalculatePassed();
    }

    private bool CalculatePassed()
    {
        if (!Evaluated)
        {
            return false;
        }

        if (ExpectedCount == 0 && !StageExecuted)
        {
            return true;
        }

        if (!StageExecuted)
        {
            return false;
        }

        if (!WorkModeMatches || !ExactRequestedMatch || !ResultPartitionConsistent)
        {
            return false;
        }

        /*
         * Cancellation/failure can legitimately leave only a subset succeeded.
         * Exact succeeded == expected is required only for a successfully
         * completed stage.
         */
        if (CompletedSuccessfully && !ExactSucceededMatch)
        {
            return false;
        }

        return true;
    }

    private static ReadOnlyCollection<Vector2Int> Difference(
        HashSet<Vector2Int> left,
        HashSet<Vector2Int> right
    )
    {
        HashSet<Vector2Int> difference = new HashSet<Vector2Int>(left);
        difference.ExceptWith(right);
        return CreateSortedCoordinates(difference);
    }

    private static ReadOnlyCollection<Vector2Int> FindClassificationOverlap(
        HashSet<Vector2Int> succeeded,
        HashSet<Vector2Int> failed,
        HashSet<Vector2Int> unprocessed
    )
    {
        HashSet<Vector2Int> overlap = new HashSet<Vector2Int>();

        foreach (Vector2Int coordinate in succeeded)
        {
            if (failed.Contains(coordinate) || unprocessed.Contains(coordinate))
            {
                overlap.Add(coordinate);
            }
        }

        foreach (Vector2Int coordinate in failed)
        {
            if (unprocessed.Contains(coordinate))
            {
                overlap.Add(coordinate);
            }
        }

        return CreateSortedCoordinates(overlap);
    }

    internal static ReadOnlyCollection<Vector2Int> CreateSortedCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        HashSet<Vector2Int> unique =
            source != null
                ? new HashSet<Vector2Int>(source)
                : new HashSet<Vector2Int>();

        List<Vector2Int> sorted = new List<Vector2Int>(unique);
        sorted.Sort(CompareCoordinates);
        return sorted.AsReadOnly();
    }

    internal static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison = left.y.CompareTo(right.y);
        return yComparison != 0
            ? yComparison
            : left.x.CompareTo(right.x);
    }
}
