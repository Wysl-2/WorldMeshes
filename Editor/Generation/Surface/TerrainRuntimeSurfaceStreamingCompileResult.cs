using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainRuntimeSurfaceStreamingCompileOutcome
{
    Completed,
    NoWork,
    Cancelled,
    Failed,
    Blocked,
    StalePlan,
    StaleSource
}

public sealed class TerrainRuntimeSurfaceStreamingCompileResult
{
    private readonly ReadOnlyCollection<Vector2Int>
        requestedFamilies;

    private readonly ReadOnlyCollection<Vector2Int>
        succeededFamilies;

    private readonly ReadOnlyCollection<Vector2Int>
        failedFamilies;

    private readonly ReadOnlyCollection<Vector2Int>
        unprocessedFamilies;

    public TerrainRuntimeSurfaceStreamingCompileOutcome Outcome { get; private set; }
    public TerrainRuntimeBakeWorkMode WorkMode { get; private set; }

    public IReadOnlyList<Vector2Int> RequestedFamilies =>
        requestedFamilies;

    public IReadOnlyList<Vector2Int> SucceededFamilies =>
        succeededFamilies;

    public IReadOnlyList<Vector2Int> FailedFamilies =>
        failedFamilies;

    public IReadOnlyList<Vector2Int> UnprocessedFamilies =>
        unprocessedFamilies;

    public int RequestedFamilyCount =>
        requestedFamilies.Count;

    public int CompletedFamilyCount =>
        succeededFamilies.Count;

    public int FailedFamilyCount =>
        failedFamilies.Count;

    public int UnprocessedFamilyCount =>
        unprocessedFamilies.Count;

    public int CreatedAssetCount { get; private set; }
    public int UpdatedAssetCount { get; private set; }
    public int RemovedAssetCount { get; private set; }
    public bool DatasetFinalized { get; private set; }
    public int StreamingGenerationRevisionBefore { get; private set; }
    public int StreamingGenerationRevisionAfter { get; private set; }
    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    internal TerrainRuntimeSurfaceStreamingCompileResult(
        TerrainRuntimeSurfaceStreamingCompileOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        IEnumerable<Vector2Int> requestedFamilies,
        IEnumerable<Vector2Int> succeededFamilies,
        IEnumerable<Vector2Int> failedFamilies,
        IEnumerable<Vector2Int> unprocessedFamilies,
        int createdAssetCount,
        int updatedAssetCount,
        int removedAssetCount,
        bool datasetFinalized,
        int streamingGenerationRevisionBefore,
        int streamingGenerationRevisionAfter,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome = outcome;
        WorkMode = workMode;

        this.requestedFamilies =
            CreateReadOnlyCoordinates(requestedFamilies);

        this.succeededFamilies =
            CreateReadOnlyCoordinates(succeededFamilies);

        this.failedFamilies =
            CreateReadOnlyCoordinates(failedFamilies);

        this.unprocessedFamilies =
            CreateReadOnlyCoordinates(unprocessedFamilies);

        CreatedAssetCount = System.Math.Max(0, createdAssetCount);
        UpdatedAssetCount = System.Math.Max(0, updatedAssetCount);
        RemovedAssetCount = System.Math.Max(0, removedAssetCount);
        DatasetFinalized = datasetFinalized;
        StreamingGenerationRevisionBefore = System.Math.Max(0, streamingGenerationRevisionBefore);
        StreamingGenerationRevisionAfter = System.Math.Max(0, streamingGenerationRevisionAfter);
        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Runtime Surface Streaming Compile Result");
        builder.AppendLine("Outcome: " + Outcome);
        builder.AppendLine("Work Mode: " + WorkMode);
        builder.AppendLine("Dataset Finalized: " + DatasetFinalized);
        builder.AppendLine(
            "Streaming Generation Revision: " +
            StreamingGenerationRevisionBefore +
            " -> " +
            StreamingGenerationRevisionAfter
        );
        builder.AppendLine("Requested Tile Families: " + RequestedFamilyCount);
        builder.AppendLine("Succeeded Tile Families: " + CompletedFamilyCount);
        builder.AppendLine("Failed Tile Families: " + FailedFamilyCount);
        builder.AppendLine("Unprocessed Tile Families: " + UnprocessedFamilyCount);
        builder.AppendLine("Created Assets: " + CreatedAssetCount);
        builder.AppendLine("Updated Assets: " + UpdatedAssetCount);
        builder.AppendLine("Removed Assets: " + RemovedAssetCount);

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

    private static ReadOnlyCollection<Vector2Int> CreateReadOnlyCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        HashSet<Vector2Int> unique =
            source != null
                ? new HashSet<Vector2Int>(source)
                : new HashSet<Vector2Int>();

        List<Vector2Int> sorted =
            new List<Vector2Int>(unique);

        sorted.Sort(CompareCoordinates);

        return sorted.AsReadOnly();
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison =
            left.y.CompareTo(right.y);

        return yComparison != 0
            ? yComparison
            : left.x.CompareTo(right.x);
    }
}
