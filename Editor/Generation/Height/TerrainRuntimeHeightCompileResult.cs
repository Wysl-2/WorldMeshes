using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainRuntimeHeightCompileOutcome
{
    NoWork,
    Completed,
    Cancelled,
    Failed,
    Blocked,
    StalePlan
}

public sealed class TerrainRuntimeHeightCompileResult
{
    private readonly ReadOnlyCollection<Vector2Int>
        requestedTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        succeededTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        failedTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        unprocessedTiles;

    public TerrainRuntimeHeightCompileOutcome Outcome
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

    public int CreatedTileCount
    {
        get;
        private set;
    }

    public int UpdatedTileCount
    {
        get;
        private set;
    }

    public int RemovedTileCount
    {
        get;
        private set;
    }

    public bool DatasetFinalized
    {
        get;
        private set;
    }

    public int HeightGenerationRevisionBefore
    {
        get;
        private set;
    }

    public int HeightGenerationRevisionAfter
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

    public TerrainHeightStreamingCompileSummary HeightStreaming
    {
        get;
        private set;
    }

    internal TerrainRuntimeHeightCompileResult(
        TerrainRuntimeHeightCompileOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        IEnumerable<Vector2Int> requestedTiles,
        IEnumerable<Vector2Int> succeededTiles,
        IEnumerable<Vector2Int> failedTiles,
        IEnumerable<Vector2Int> unprocessedTiles,
        int createdTileCount,
        int updatedTileCount,
        int removedTileCount,
        bool datasetFinalized,
        int heightGenerationRevisionBefore,
        int heightGenerationRevisionAfter,
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

        CreatedTileCount =
            Mathf.Max(
                0,
                createdTileCount
            );

        UpdatedTileCount =
            Mathf.Max(
                0,
                updatedTileCount
            );

        RemovedTileCount =
            Mathf.Max(
                0,
                removedTileCount
            );

        DatasetFinalized =
            datasetFinalized;

        HeightGenerationRevisionBefore =
            heightGenerationRevisionBefore;

        HeightGenerationRevisionAfter =
            heightGenerationRevisionAfter;

        ErrorMessage =
            errorMessage ??
            "";

        SummaryMessage =
            summaryMessage ??
            "";

        HeightStreaming =
            TerrainHeightStreamingCompileSummary
                .NotEvaluated;
    }

    internal void SetHeightStreamingSummary(
        TerrainHeightStreamingCompileSummary summary
    )
    {
        HeightStreaming =
            summary ??
            TerrainHeightStreamingCompileSummary
                .NotEvaluated;
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Height Compile Result"
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
            "Height Generation Revision: " +
            HeightGenerationRevisionBefore +
            " -> " +
            HeightGenerationRevisionAfter
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
            "Created Tiles: " +
            CreatedTileCount
        );

        builder.AppendLine(
            "Updated Tiles: " +
            UpdatedTileCount
        );

        builder.AppendLine(
            "Removed Tiles: " +
            RemovedTileCount
        );

        AppendHeightStreamingSummary(
            builder
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

        AppendCoordinates(
            builder,
            "Succeeded",
            succeededTiles
        );

        AppendCoordinates(
            builder,
            "Failed",
            failedTiles
        );

        AppendCoordinates(
            builder,
            "Unprocessed",
            unprocessedTiles
        );

        return
            builder.ToString();
    }

    private void AppendHeightStreamingSummary(
        StringBuilder builder
    )
    {
        if (
            HeightStreaming == null
            ||
            !HeightStreaming.Evaluated
        )
        {
            return;
        }

        builder.AppendLine();

        builder.AppendLine(
            "Height Streaming Pyramid"
        );

        builder.AppendLine(
            "Generation Enabled: " +
            HeightStreaming.GenerationEnabled
        );

        builder.AppendLine(
            "Target Strides: " +
            (
                HeightStreaming.TargetStrides.Count > 0
                    ? string.Join(
                        ", ",
                        HeightStreaming.TargetStrides
                    )
                    : "None"
            )
        );

        builder.AppendLine(
            "Requested Families: " +
            HeightStreaming.RequestedFamilyCount
        );

        builder.AppendLine(
            "Complete Families: " +
            HeightStreaming.CompleteFamilyCount
        );

        builder.AppendLine(
            "Incomplete Families: " +
            HeightStreaming.IncompleteFamilyCount
        );

        builder.AppendLine(
            "Created Derived Assets: " +
            HeightStreaming.CreatedAssetCount
        );

        builder.AppendLine(
            "Updated Derived Assets: " +
            HeightStreaming.UpdatedAssetCount
        );

        builder.AppendLine(
            "Removed Derived Assets: " +
            HeightStreaming.RemovedAssetCount
        );

        builder.AppendLine(
            "Streaming Dataset Finalized: " +
            HeightStreaming.DatasetFinalized
        );

        if (
            !string.IsNullOrEmpty(
                HeightStreaming.FirstError
            )
        )
        {
            builder.AppendLine(
                "First Streaming Error: " +
                HeightStreaming.FirstError
            );
        }
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

    private static void AppendCoordinates(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        builder.AppendLine();

        builder.Append(
            label
        );

        builder.Append(
            " ("
        );

        builder.Append(
            coordinates.Count
        );

        builder.AppendLine(
            "):"
        );

        if (coordinates.Count == 0)
        {
            builder.AppendLine(
                "None"
            );

            return;
        }

        for (
            int index = 0;
            index < coordinates.Count;
            index++
        )
        {
            Vector2Int coordinate =
                coordinates[index];

            builder.Append(
                "(" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")"
            );

            if (index < coordinates.Count - 1)
            {
                builder.Append(
                    ", "
                );
            }
        }

        builder.AppendLine();
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
