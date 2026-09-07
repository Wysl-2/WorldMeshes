using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public enum TerrainCollisionGenerationOutcome
{
    NoWork,
    Completed,
    Cancelled,
    Failed,
    Blocked,
    StalePlan
}

/*
 * Immutable summary of one collision-generation invocation.
 *
 * Physical chunk assets can advance incrementally while the global collision
 * generation revision remains intentionally stale until every required chunk
 * has caught up to the same height/settings target.
 */
public sealed class TerrainCollisionGenerationResult
{
    private readonly ReadOnlyCollection<Vector2Int>
        requestedChunks;

    private readonly ReadOnlyCollection<Vector2Int>
        succeededChunks;

    private readonly ReadOnlyCollection<Vector2Int>
        failedChunks;

    private readonly ReadOnlyCollection<Vector2Int>
        unprocessedChunks;

    public TerrainCollisionGenerationOutcome Outcome
    {
        get;
        private set;
    }

    public TerrainRuntimeBakeWorkMode WorkMode
    {
        get;
        private set;
    }

    public IReadOnlyList<Vector2Int> RequestedChunks =>
        requestedChunks;

    public IReadOnlyList<Vector2Int> SucceededChunks =>
        succeededChunks;

    public IReadOnlyList<Vector2Int> FailedChunks =>
        failedChunks;

    public IReadOnlyList<Vector2Int> UnprocessedChunks =>
        unprocessedChunks;

    public int RequestedChunkCount =>
        requestedChunks.Count;

    public int SucceededChunkCount =>
        succeededChunks.Count;

    public int FailedChunkCount =>
        failedChunks.Count;

    public int UnprocessedChunkCount =>
        unprocessedChunks.Count;

    public int CreatedMeshCount
    {
        get;
        private set;
    }

    public int UpdatedMeshCount
    {
        get;
        private set;
    }

    public int RemovedMeshCount
    {
        get;
        private set;
    }

    public bool DatasetFinalized
    {
        get;
        private set;
    }

    public int CollisionGenerationRevisionBefore
    {
        get;
        private set;
    }

    public int CollisionGenerationRevisionAfter
    {
        get;
        private set;
    }

    public int SourceHeightmapGenerationRevisionBefore
    {
        get;
        private set;
    }

    public int SourceHeightmapGenerationRevisionAfter
    {
        get;
        private set;
    }

    public bool AddressablesConfigurationBecameDirty
    {
        get;
        private set;
    }

    public bool AddressablesContentBecameDirty
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

    internal TerrainCollisionGenerationResult(
        TerrainCollisionGenerationOutcome outcome,
        TerrainRuntimeBakeWorkMode workMode,
        IEnumerable<Vector2Int> requestedChunks,
        IEnumerable<Vector2Int> succeededChunks,
        IEnumerable<Vector2Int> failedChunks,
        IEnumerable<Vector2Int> unprocessedChunks,
        int createdMeshCount,
        int updatedMeshCount,
        int removedMeshCount,
        bool datasetFinalized,
        int collisionGenerationRevisionBefore,
        int collisionGenerationRevisionAfter,
        int sourceHeightmapGenerationRevisionBefore,
        int sourceHeightmapGenerationRevisionAfter,
        bool addressablesConfigurationBecameDirty,
        bool addressablesContentBecameDirty,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome =
            outcome;

        WorkMode =
            workMode;

        this.requestedChunks =
            CreateSortedCoordinates(
                requestedChunks
            );

        this.succeededChunks =
            CreateSortedCoordinates(
                succeededChunks
            );

        this.failedChunks =
            CreateSortedCoordinates(
                failedChunks
            );

        this.unprocessedChunks =
            CreateSortedCoordinates(
                unprocessedChunks
            );

        CreatedMeshCount =
            createdMeshCount;

        UpdatedMeshCount =
            updatedMeshCount;

        RemovedMeshCount =
            removedMeshCount;

        DatasetFinalized =
            datasetFinalized;

        CollisionGenerationRevisionBefore =
            collisionGenerationRevisionBefore;

        CollisionGenerationRevisionAfter =
            collisionGenerationRevisionAfter;

        SourceHeightmapGenerationRevisionBefore =
            sourceHeightmapGenerationRevisionBefore;

        SourceHeightmapGenerationRevisionAfter =
            sourceHeightmapGenerationRevisionAfter;

        AddressablesConfigurationBecameDirty =
            addressablesConfigurationBecameDirty;

        AddressablesContentBecameDirty =
            addressablesContentBecameDirty;

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
            "WorldMeshes Collision Generation Result"
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
            "Collision Generation Revision: " +
            CollisionGenerationRevisionBefore +
            " -> " +
            CollisionGenerationRevisionAfter
        );

        builder.AppendLine(
            "Source Height Revision: " +
            SourceHeightmapGenerationRevisionBefore +
            " -> " +
            SourceHeightmapGenerationRevisionAfter
        );

        builder.AppendLine(
            "Requested Chunks: " +
            RequestedChunkCount
        );

        builder.AppendLine(
            "Succeeded Chunks: " +
            SucceededChunkCount
        );

        builder.AppendLine(
            "Failed Chunks: " +
            FailedChunkCount
        );

        builder.AppendLine(
            "Unprocessed Chunks: " +
            UnprocessedChunkCount
        );

        builder.AppendLine(
            "Created Meshes: " +
            CreatedMeshCount
        );

        builder.AppendLine(
            "Updated Meshes: " +
            UpdatedMeshCount
        );

        builder.AppendLine(
            "Removed Meshes: " +
            RemovedMeshCount
        );

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

        AppendCoordinateSummary(
            builder,
            "Succeeded",
            succeededChunks
        );

        AppendCoordinateSummary(
            builder,
            "Failed",
            failedChunks
        );

        AppendCoordinateSummary(
            builder,
            "Unprocessed",
            unprocessedChunks
        );

        return
            builder.ToString();
    }

    private static void AppendCoordinateSummary(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        const int MaxDisplayedCoordinates =
            64;

        builder.AppendLine();
        builder.Append(
            label +
            " (" +
            coordinates.Count +
            "):"
        );

        if (coordinates.Count == 0)
        {
            builder.AppendLine();
            builder.Append(
                "None"
            );

            return;
        }

        builder.AppendLine();

        int displayCount =
            Mathf.Min(
                coordinates.Count,
                MaxDisplayedCoordinates
            );

        for (
            int index = 0;
            index < displayCount;
            index++
        )
        {
            if (index > 0)
            {
                builder.Append(
                    ", "
                );
            }

            Vector2Int coordinate =
                coordinates[index];

            builder.Append(
                "(" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")"
            );
        }

        if (
            coordinates.Count >
            displayCount
        )
        {
            builder.Append(
                ", ... +" +
                (
                    coordinates.Count -
                    displayCount
                ) +
                " more"
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
            return
                yComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }
}
