using System.Text;

public enum TerrainRuntimeSurfaceStreamingCompileOutcome
{
    Completed,
    Cancelled,
    Failed,
    Blocked,
    StaleSource
}

public sealed class TerrainRuntimeSurfaceStreamingCompileResult
{
    public TerrainRuntimeSurfaceStreamingCompileOutcome Outcome { get; private set; }
    public int RequestedFamilyCount { get; private set; }
    public int CompletedFamilyCount { get; private set; }
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
        int requestedFamilyCount,
        int completedFamilyCount,
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
        RequestedFamilyCount = System.Math.Max(0, requestedFamilyCount);
        CompletedFamilyCount = System.Math.Max(0, completedFamilyCount);
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
        builder.AppendLine("Dataset Finalized: " + DatasetFinalized);
        builder.AppendLine(
            "Streaming Generation Revision: " +
            StreamingGenerationRevisionBefore +
            " -> " +
            StreamingGenerationRevisionAfter
        );
        builder.AppendLine("Requested Tile Families: " + RequestedFamilyCount);
        builder.AppendLine("Completed Tile Families: " + CompletedFamilyCount);
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
}
