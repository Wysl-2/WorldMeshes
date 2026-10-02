using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

public sealed class TerrainRuntimeOutputFingerprintSnapshot
{
    private readonly ReadOnlyCollection<TerrainRuntimeOutputFingerprint> heightFingerprints;
    private readonly ReadOnlyCollection<TerrainRuntimeOutputFingerprint> heightStreamingFingerprints;
    private readonly ReadOnlyCollection<TerrainRuntimeOutputFingerprint> surfaceFingerprints;
    private readonly ReadOnlyCollection<TerrainRuntimeOutputFingerprint> collisionFingerprints;
    private readonly ReadOnlyCollection<string> issues;

    public IReadOnlyList<TerrainRuntimeOutputFingerprint> HeightFingerprints => heightFingerprints;
    public IReadOnlyList<TerrainRuntimeOutputFingerprint> HeightStreamingFingerprints => heightStreamingFingerprints;
    public IReadOnlyList<TerrainRuntimeOutputFingerprint> SurfaceFingerprints => surfaceFingerprints;
    public IReadOnlyList<TerrainRuntimeOutputFingerprint> CollisionFingerprints => collisionFingerprints;
    public IReadOnlyList<string> Issues => issues;

    public int ExpectedHeightAssetCount { get; private set; }
    public int ExpectedHeightStreamingAssetCount { get; private set; }
    public int ExpectedSurfaceAssetCount { get; private set; }
    public int ExpectedCollisionAssetCount { get; private set; }

    public int HeightAssetCount => heightFingerprints.Count;
    public int HeightStreamingAssetCount => heightStreamingFingerprints.Count;
    public int SurfaceAssetCount => surfaceFingerprints.Count;
    public int CollisionAssetCount => collisionFingerprints.Count;

    public string HeightDatasetFingerprint { get; private set; }
    public string HeightStreamingDatasetFingerprint { get; private set; }
    public string SurfaceDatasetFingerprint { get; private set; }
    public string CollisionDatasetFingerprint { get; private set; }

    public bool IsComplete { get; private set; }
    public bool WasCancelled { get; private set; }

    public long SourceBakeStateRevision { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }

    public string ErrorMessage { get; private set; }

    internal TerrainRuntimeOutputFingerprintSnapshot(
        IEnumerable<TerrainRuntimeOutputFingerprint> height,
        IEnumerable<TerrainRuntimeOutputFingerprint> heightStreaming,
        IEnumerable<TerrainRuntimeOutputFingerprint> surface,
        IEnumerable<TerrainRuntimeOutputFingerprint> collision,
        int expectedHeightAssetCount,
        int expectedHeightStreamingAssetCount,
        int expectedSurfaceAssetCount,
        int expectedCollisionAssetCount,
        string heightDatasetFingerprint,
        string heightStreamingDatasetFingerprint,
        string surfaceDatasetFingerprint,
        string collisionDatasetFingerprint,
        bool isComplete,
        bool wasCancelled,
        long sourceBakeStateRevision,
        IEnumerable<string> issues,
        string errorMessage
    )
    {
        heightFingerprints = new List<TerrainRuntimeOutputFingerprint>(
            height ?? new TerrainRuntimeOutputFingerprint[0]
        ).AsReadOnly();

        heightStreamingFingerprints = new List<TerrainRuntimeOutputFingerprint>(
            heightStreaming ?? new TerrainRuntimeOutputFingerprint[0]
        ).AsReadOnly();

        surfaceFingerprints = new List<TerrainRuntimeOutputFingerprint>(
            surface ?? new TerrainRuntimeOutputFingerprint[0]
        ).AsReadOnly();

        collisionFingerprints = new List<TerrainRuntimeOutputFingerprint>(
            collision ?? new TerrainRuntimeOutputFingerprint[0]
        ).AsReadOnly();

        this.issues = new List<string>(issues ?? new string[0]).AsReadOnly();

        ExpectedHeightAssetCount = Math.Max(0, expectedHeightAssetCount);
        ExpectedHeightStreamingAssetCount = Math.Max(0, expectedHeightStreamingAssetCount);
        ExpectedSurfaceAssetCount = Math.Max(0, expectedSurfaceAssetCount);
        ExpectedCollisionAssetCount = Math.Max(0, expectedCollisionAssetCount);

        HeightDatasetFingerprint = heightDatasetFingerprint ?? "";
        HeightStreamingDatasetFingerprint = heightStreamingDatasetFingerprint ?? "";
        SurfaceDatasetFingerprint = surfaceDatasetFingerprint ?? "";
        CollisionDatasetFingerprint = collisionDatasetFingerprint ?? "";

        IsComplete = isComplete;
        WasCancelled = wasCancelled;
        SourceBakeStateRevision = sourceBakeStateRevision;
        CapturedAtUtc = DateTime.UtcNow;
        ErrorMessage = errorMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Runtime Output Fingerprint Snapshot");
        builder.AppendLine("Complete: " + IsComplete);
        builder.AppendLine("Cancelled: " + WasCancelled);
        builder.AppendLine("Bake State Revision: " + SourceBakeStateRevision);
        builder.AppendLine();
        builder.AppendLine(
            "Heightmaps: " + HeightAssetCount + " / " + ExpectedHeightAssetCount
        );
        builder.AppendLine(
            "Height Streaming: " + HeightStreamingAssetCount + " / " + ExpectedHeightStreamingAssetCount
        );
        builder.AppendLine(
            "Surface Masks: " + SurfaceAssetCount + " / " + ExpectedSurfaceAssetCount
        );
        builder.AppendLine(
            "Collision Meshes: " + CollisionAssetCount + " / " + ExpectedCollisionAssetCount
        );
        builder.AppendLine();
        builder.AppendLine("Height Dataset Hash: " + GetHashLabel(HeightDatasetFingerprint));
        builder.AppendLine("Height Streaming Dataset Hash: " + GetHashLabel(HeightStreamingDatasetFingerprint));
        builder.AppendLine("Surface Dataset Hash: " + GetHashLabel(SurfaceDatasetFingerprint));
        builder.AppendLine("Collision Dataset Hash: " + GetHashLabel(CollisionDatasetFingerprint));

        if (issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Issues (" + issues.Count + "):");

            for (int index = 0; index < issues.Count; index++)
            {
                builder.AppendLine("- " + issues[index]);
            }
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine("Error: " + ErrorMessage);
        }

        return builder.ToString();
    }

    private static string GetHashLabel(string hash)
    {
        return string.IsNullOrEmpty(hash) ? "Unavailable" : hash;
    }
}
