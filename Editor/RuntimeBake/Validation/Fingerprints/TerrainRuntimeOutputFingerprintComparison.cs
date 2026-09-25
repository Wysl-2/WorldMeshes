using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public sealed class TerrainRuntimeOutputFingerprintStreamComparison
{
    private readonly ReadOnlyCollection<Vector2Int> matching;
    private readonly ReadOnlyCollection<Vector2Int> missing;
    private readonly ReadOnlyCollection<Vector2Int> unexpected;
    private readonly ReadOnlyCollection<Vector2Int> payloadMismatches;

    public string StreamName { get; private set; }

    public IReadOnlyList<Vector2Int> Matching => matching;
    public IReadOnlyList<Vector2Int> Missing => missing;
    public IReadOnlyList<Vector2Int> Unexpected => unexpected;
    public IReadOnlyList<Vector2Int> PayloadMismatches => payloadMismatches;

    public bool ExactMatch =>
        Missing.Count == 0
        && Unexpected.Count == 0
        && PayloadMismatches.Count == 0;

    internal TerrainRuntimeOutputFingerprintStreamComparison(
        string streamName,
        IEnumerable<Vector2Int> matching,
        IEnumerable<Vector2Int> missing,
        IEnumerable<Vector2Int> unexpected,
        IEnumerable<Vector2Int> payloadMismatches
    )
    {
        StreamName = streamName ?? "";
        this.matching = TerrainRuntimeBakeStageValidationResult.CreateSortedCoordinates(matching);
        this.missing = TerrainRuntimeBakeStageValidationResult.CreateSortedCoordinates(missing);
        this.unexpected = TerrainRuntimeBakeStageValidationResult.CreateSortedCoordinates(unexpected);
        this.payloadMismatches = TerrainRuntimeBakeStageValidationResult.CreateSortedCoordinates(payloadMismatches);
    }
}

public sealed class TerrainRuntimeOutputFingerprintComparison
{
    public TerrainRuntimeOutputFingerprintSnapshot Baseline { get; private set; }
    public TerrainRuntimeOutputFingerprintSnapshot Current { get; private set; }

    public TerrainRuntimeOutputFingerprintStreamComparison Height { get; private set; }
    public TerrainRuntimeOutputFingerprintStreamComparison Surface { get; private set; }
    public TerrainRuntimeOutputFingerprintStreamComparison Collision { get; private set; }

    public bool HeightStreamingExactMatch { get; private set; }

    public bool BaselineComplete => Baseline != null && Baseline.IsComplete;
    public bool CurrentComplete => Current != null && Current.IsComplete;

    public bool ExactMatch =>
        BaselineComplete
        && CurrentComplete
        && Height != null && Height.ExactMatch
        && HeightStreamingExactMatch
        && Surface != null && Surface.ExactMatch
        && Collision != null && Collision.ExactMatch;

    internal TerrainRuntimeOutputFingerprintComparison(
        TerrainRuntimeOutputFingerprintSnapshot baseline,
        TerrainRuntimeOutputFingerprintSnapshot current,
        TerrainRuntimeOutputFingerprintStreamComparison height,
        TerrainRuntimeOutputFingerprintStreamComparison surface,
        TerrainRuntimeOutputFingerprintStreamComparison collision
    )
    {
        Baseline = baseline;
        Current = current;
        Height = height;
        Surface = surface;
        Collision = collision;

        HeightStreamingExactMatch =
            baseline != null
            && current != null
            && baseline.ExpectedHeightStreamingAssetCount == current.ExpectedHeightStreamingAssetCount
            && baseline.HeightStreamingAssetCount == current.HeightStreamingAssetCount
            && baseline.HeightStreamingAssetCount == baseline.ExpectedHeightStreamingAssetCount
            && string.Equals(
                baseline.HeightStreamingDatasetFingerprint,
                current.HeightStreamingDatasetFingerprint,
                StringComparison.Ordinal
            );
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Runtime Output Fingerprint Comparison");
        builder.AppendLine("Baseline Complete: " + BaselineComplete);
        builder.AppendLine("Current Complete: " + CurrentComplete);

        AppendStream(builder, Height);

        builder.AppendLine();
        builder.AppendLine("Height Streaming:");
        builder.AppendLine(
            "  Baseline: " +
            (Baseline != null ? Baseline.HeightStreamingAssetCount : 0) +
            " / " +
            (Baseline != null ? Baseline.ExpectedHeightStreamingAssetCount : 0)
        );
        builder.AppendLine(
            "  Current: " +
            (Current != null ? Current.HeightStreamingAssetCount : 0) +
            " / " +
            (Current != null ? Current.ExpectedHeightStreamingAssetCount : 0)
        );
        builder.AppendLine("  " + (HeightStreamingExactMatch ? "MATCH" : "DIFFERENT"));

        AppendStream(builder, Surface);
        AppendStream(builder, Collision);

        builder.AppendLine();
        builder.Append("Result: " + (ExactMatch ? "EXACT MATCH" : "DIFFERENCES FOUND"));

        return builder.ToString();
    }

    private static void AppendStream(
        StringBuilder builder,
        TerrainRuntimeOutputFingerprintStreamComparison stream
    )
    {
        builder.AppendLine();

        if (stream == null)
        {
            builder.AppendLine("Unavailable stream comparison");
            return;
        }

        builder.AppendLine(stream.StreamName + ":");
        builder.AppendLine("  Matching: " + stream.Matching.Count);
        builder.AppendLine("  Missing: " + stream.Missing.Count);
        builder.AppendLine("  Unexpected: " + stream.Unexpected.Count);
        builder.AppendLine("  Payload Mismatches: " + stream.PayloadMismatches.Count);
        builder.AppendLine("  " + (stream.ExactMatch ? "MATCH" : "DIFFERENT"));
    }
}
