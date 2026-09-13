using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainSurfaceMaskGenerationOutcome
{
    NoWork,
    Completed,
    Cancelled,
    Failed,
    Blocked,
    StalePlan
}

public sealed class TerrainSurfaceMaskGenerationResult
{
    private readonly ReadOnlyCollection<Vector2Int> requestedTiles;
    private readonly ReadOnlyCollection<Vector2Int> succeededTiles;
    private readonly ReadOnlyCollection<Vector2Int> failedTiles;
    private readonly ReadOnlyCollection<Vector2Int> unprocessedTiles;

    public TerrainSurfaceMaskGenerationOutcome Outcome { get; private set; }
    public TerrainRuntimeBakeWorkMode WorkMode { get; private set; }

    public IReadOnlyList<Vector2Int> RequestedTiles => requestedTiles;
    public IReadOnlyList<Vector2Int> SucceededTiles => succeededTiles;
    public IReadOnlyList<Vector2Int> FailedTiles => failedTiles;
    public IReadOnlyList<Vector2Int> UnprocessedTiles => unprocessedTiles;

    public int RequestedTileCount => requestedTiles.Count;
    public int SucceededTileCount => succeededTiles.Count;
    public int FailedTileCount => failedTiles.Count;
    public int UnprocessedTileCount => unprocessedTiles.Count;

    public int CreatedTileCount { get; private set; }
    public int UpdatedTileCount { get; private set; }
    public int RemovedTileCount { get; private set; }

    public bool DatasetFinalized { get; private set; }

    public int SurfaceGenerationRevisionBefore { get; private set; }
    public int SurfaceGenerationRevisionAfter { get; private set; }

    public int SourceHeightGenerationRevisionBefore { get; private set; }
    public int SourceHeightGenerationRevisionAfter { get; private set; }

    public bool AddressablesConfigurationBecameDirty { get; private set; }
    public bool AddressablesContentBecameDirty { get; private set; }

    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    internal TerrainSurfaceMaskGenerationResult(
        TerrainSurfaceMaskGenerationOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        IEnumerable<Vector2Int> requestedTiles,
        IEnumerable<Vector2Int> succeededTiles,
        IEnumerable<Vector2Int> failedTiles,
        IEnumerable<Vector2Int> unprocessedTiles,
        int createdTileCount,
        int updatedTileCount,
        int removedTileCount,
        bool datasetFinalized,
        int surfaceGenerationRevisionBefore,
        int surfaceGenerationRevisionAfter,
        int sourceHeightGenerationRevisionBefore,
        int sourceHeightGenerationRevisionAfter,
        bool addressablesConfigurationBecameDirty,
        bool addressablesContentBecameDirty,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome = outcome;
        WorkMode = workMode;

        this.requestedTiles = CreateSortedCoordinates(requestedTiles);
        this.succeededTiles = CreateSortedCoordinates(succeededTiles);
        this.failedTiles = CreateSortedCoordinates(failedTiles);
        this.unprocessedTiles = CreateSortedCoordinates(unprocessedTiles);

        CreatedTileCount = createdTileCount;
        UpdatedTileCount = updatedTileCount;
        RemovedTileCount = removedTileCount;

        DatasetFinalized = datasetFinalized;

        SurfaceGenerationRevisionBefore = surfaceGenerationRevisionBefore;
        SurfaceGenerationRevisionAfter = surfaceGenerationRevisionAfter;

        SourceHeightGenerationRevisionBefore = sourceHeightGenerationRevisionBefore;
        SourceHeightGenerationRevisionAfter = sourceHeightGenerationRevisionAfter;

        AddressablesConfigurationBecameDirty = addressablesConfigurationBecameDirty;
        AddressablesContentBecameDirty = addressablesContentBecameDirty;

        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Runtime Surface Mask Generation Result");
        builder.AppendLine("Outcome: " + Outcome);
        builder.AppendLine("Work Mode: " + WorkMode);
        builder.AppendLine("Dataset Finalized: " + DatasetFinalized);

        builder.AppendLine(
            "Surface Generation Revision: " +
            SurfaceGenerationRevisionBefore +
            " -> " +
            SurfaceGenerationRevisionAfter
        );

        builder.AppendLine(
            "Source Height Revision: " +
            SourceHeightGenerationRevisionBefore +
            " -> " +
            SourceHeightGenerationRevisionAfter
        );

        builder.AppendLine("Requested Tiles: " + RequestedTileCount);
        builder.AppendLine("Succeeded Tiles: " + SucceededTileCount);
        builder.AppendLine("Failed Tiles: " + FailedTileCount);
        builder.AppendLine("Unprocessed Tiles: " + UnprocessedTileCount);

        builder.AppendLine("Created Tiles: " + CreatedTileCount);
        builder.AppendLine("Updated Tiles: " + UpdatedTileCount);
        builder.AppendLine("Removed Tiles: " + RemovedTileCount);

        builder.AppendLine(
            "Addressables Configuration Became Dirty: " +
            AddressablesConfigurationBecameDirty
        );

        builder.AppendLine(
            "Addressables Content Became Dirty: " +
            AddressablesContentBecameDirty
        );

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

        AppendCoordinateSummary(builder, "Succeeded", succeededTiles);
        AppendCoordinateSummary(builder, "Failed", failedTiles);
        AppendCoordinateSummary(builder, "Unprocessed", unprocessedTiles);

        return builder.ToString();
    }

    private static void AppendCoordinateSummary(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        const int MaxDisplayedCoordinates = 64;

        builder.AppendLine();
        builder.Append(label + " (" + coordinates.Count + "):");

        if (coordinates.Count == 0)
        {
            builder.AppendLine();
            builder.Append("None");
            return;
        }

        builder.AppendLine();

        int displayCount = Mathf.Min(coordinates.Count, MaxDisplayedCoordinates);

        for (int index = 0; index < displayCount; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            Vector2Int coordinate = coordinates[index];

            builder.Append(
                "(" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")"
            );
        }

        if (coordinates.Count > displayCount)
        {
            builder.Append(
                ", ... +" +
                (coordinates.Count - displayCount) +
                " more"
            );
        }
    }

    private static ReadOnlyCollection<Vector2Int> CreateSortedCoordinates(
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

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison = left.y.CompareTo(right.y);

        if (yComparison != 0)
        {
            return yComparison;
        }

        return left.x.CompareTo(right.x);
    }
}
