public enum TerrainValidationRunState
{
    NotRun,
    Running,
    Passed,
    Failed,
    Blocked
}

public sealed class TerrainValidationRunSummary
{
    public TerrainValidationRunState State { get; }

    public int PassedCount { get; }

    public int FailedCount { get; }

    public int BlockedCount { get; }

    public string Summary { get; }

    public bool IsTerminal =>
        State == TerrainValidationRunState.Passed
        || State == TerrainValidationRunState.Failed
        || State == TerrainValidationRunState.Blocked;

    private TerrainValidationRunSummary(
        TerrainValidationRunState state,
        int passedCount,
        int failedCount,
        int blockedCount,
        string summary
    )
    {
        State =
            state;

        PassedCount =
            System.Math.Max(
                0,
                passedCount
            );

        FailedCount =
            System.Math.Max(
                0,
                failedCount
            );

        BlockedCount =
            System.Math.Max(
                0,
                blockedCount
            );

        Summary =
            summary ?? "";
    }

    public static TerrainValidationRunSummary CreateNotRun()
    {
        return
            new TerrainValidationRunSummary(
                TerrainValidationRunState.NotRun,
                0,
                0,
                0,
                "Not run."
            );
    }

    public static TerrainValidationRunSummary CreateRunning(
        string summary
    )
    {
        return
            new TerrainValidationRunSummary(
                TerrainValidationRunState.Running,
                0,
                0,
                0,
                summary
            );
    }

    public static TerrainValidationRunSummary CreateCompleted(
        int passedCount,
        int failedCount,
        int blockedCount,
        string summary
    )
    {
        TerrainValidationRunState state;

        if (failedCount > 0)
        {
            state =
                TerrainValidationRunState.Failed;
        }
        else if (blockedCount > 0)
        {
            state =
                TerrainValidationRunState.Blocked;
        }
        else
        {
            state =
                TerrainValidationRunState.Passed;
        }

        return
            new TerrainValidationRunSummary(
                state,
                passedCount,
                failedCount,
                blockedCount,
                summary
            );
    }
}
