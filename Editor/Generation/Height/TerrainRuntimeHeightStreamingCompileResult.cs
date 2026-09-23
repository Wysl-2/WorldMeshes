using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainRuntimeHeightStreamingCompileOutcome
{
    NoWork,
    Completed,
    Cancelled,
    Failed,
    Blocked,
    StalePlan
}

public sealed class TerrainRuntimeHeightStreamingCompileResult
{
    private readonly ReadOnlyCollection<Vector2Int>
        requestedTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        succeededTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        failedTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        unprocessedTiles;

    public TerrainRuntimeHeightStreamingCompileOutcome Outcome
    {
        get;
        private set;
    }

    public TerrainRuntimeBakeWorkMode WorkMode
    {
        get;
        private set;
    }

    public IReadOnlyList<Vector2Int> RequestedTiles =>
        requestedTiles;

    public IReadOnlyList<Vector2Int> SucceededTiles =>
        succeededTiles;

    public IReadOnlyList<Vector2Int> FailedTiles =>
        failedTiles;

    public IReadOnlyList<Vector2Int> UnprocessedTiles =>
        unprocessedTiles;

    public int RequestedTileCount =>
        requestedTiles.Count;

    public int SucceededTileCount =>
        succeededTiles.Count;

    public int FailedTileCount =>
        failedTiles.Count;

    public int UnprocessedTileCount =>
        unprocessedTiles.Count;

    public int CreatedAssetCount
    {
        get;
        private set;
    }

    public int UpdatedAssetCount
    {
        get;
        private set;
    }

    public int RemovedAssetCount
    {
        get;
        private set;
    }

    public bool DatasetFinalized
    {
        get;
        private set;
    }

    public int StreamingGenerationRevisionBefore
    {
        get;
        private set;
    }

    public int StreamingGenerationRevisionAfter
    {
        get;
        private set;
    }

    public string ErrorMessage
    {
        get;
        private set;
    }

    public string SummaryMessage
    {
        get;
        private set;
    }

    internal TerrainRuntimeHeightStreamingCompileResult(
        TerrainRuntimeHeightStreamingCompileOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        IEnumerable<Vector2Int> requestedTiles,
        IEnumerable<Vector2Int> succeededTiles,
        IEnumerable<Vector2Int> failedTiles,
        IEnumerable<Vector2Int> unprocessedTiles,
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
        Outcome =
            outcome;

        WorkMode =
            workMode;

        this.requestedTiles =
            CreateSortedCoordinates(
                requestedTiles
            );

        this.succeededTiles =
            CreateSortedCoordinates(
                succeededTiles
            );

        this.failedTiles =
            CreateSortedCoordinates(
                failedTiles
            );

        this.unprocessedTiles =
            CreateSortedCoordinates(
                unprocessedTiles
            );

        CreatedAssetCount =
            Mathf.Max(
                0,
                createdAssetCount
            );

        UpdatedAssetCount =
            Mathf.Max(
                0,
                updatedAssetCount
            );

        RemovedAssetCount =
            Mathf.Max(
                0,
                removedAssetCount
            );

        DatasetFinalized =
            datasetFinalized;

        StreamingGenerationRevisionBefore =
            Mathf.Max(
                0,
                streamingGenerationRevisionBefore
            );

        StreamingGenerationRevisionAfter =
            Mathf.Max(
                0,
                streamingGenerationRevisionAfter
            );

        ErrorMessage =
            errorMessage ??
            "";

        SummaryMessage =
            summaryMessage ??
            "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Height Streaming Compile Result"
        );

        builder.AppendLine(
            "Outcome: " +
            Outcome
        );

        builder.AppendLine(
            "Work Mode: " +
            WorkMode
        );

        builder.AppendLine(
            "Dataset Finalized: " +
            DatasetFinalized
        );

        builder.AppendLine(
            "Streaming Generation Revision: " +
            StreamingGenerationRevisionBefore +
            " -> " +
            StreamingGenerationRevisionAfter
        );

        builder.AppendLine(
            "Requested Tiles: " +
            RequestedTileCount
        );

        builder.AppendLine(
            "Succeeded Tiles: " +
            SucceededTileCount
        );

        builder.AppendLine(
            "Failed Tiles: " +
            FailedTileCount
        );

        builder.AppendLine(
            "Unprocessed Tiles: " +
            UnprocessedTileCount
        );

        builder.AppendLine(
            "Created Assets: " +
            CreatedAssetCount
        );

        builder.AppendLine(
            "Updated Assets: " +
            UpdatedAssetCount
        );

        builder.AppendLine(
            "Removed Assets: " +
            RemovedAssetCount
        );

        if (!string.IsNullOrEmpty(SummaryMessage))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Summary: " +
                SummaryMessage
            );
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Error: " +
                ErrorMessage
            );
        }

        return
            builder.ToString();
    }

    private static ReadOnlyCollection<Vector2Int>
        CreateSortedCoordinates(
            IEnumerable<Vector2Int> source
        )
    {
        HashSet<Vector2Int> unique =
            source != null
                ? new HashSet<Vector2Int>(source)
                : new HashSet<Vector2Int>();

        List<Vector2Int> sorted =
            new List<Vector2Int>(
                unique
            );

        sorted.Sort(
            CompareCoordinates
        );

        return
            sorted.AsReadOnly();
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison =
            left.y.CompareTo(
                right.y
            );

        if (yComparison != 0)
        {
            return yComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }
}
