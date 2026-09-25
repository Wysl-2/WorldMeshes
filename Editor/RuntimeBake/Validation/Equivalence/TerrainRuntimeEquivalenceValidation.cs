using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public enum TerrainRuntimeEquivalenceScenario
{
    None,

    SingleLocalEdit,
    MultiTileEdit,
    ModifierMove,
    ModifierDelete,
    ModifierEnable,
    ModifierDisable,
    MultipleEditsBeforeOneBake,

    SurfaceSettingsChange,
    CollisionResolutionChange,

    InitialBake,
    ForcedFullRebuild
}

public enum TerrainRuntimeEquivalenceValidationOutcome
{
    NotRun,
    IncrementalCaptured,
    RebuildingFull,
    Comparing,
    Passed,
    PassedWithWarnings,
    Failed,
    Blocked,
    Cancelled,
    ComparisonInvalidated
}

public enum TerrainRuntimeEquivalenceDatasetKind
{
    Height,
    Surface,
    Collision,
    HeightManifest
}

public enum TerrainRuntimeEquivalenceDifferenceType
{
    MissingFromIncremental,
    MissingFromFull,
    UnexpectedIncremental,
    UnexpectedFull,
    PayloadMismatch,
    HeightRangeMetadataMismatch
}

public sealed class TerrainRuntimeEquivalenceSourceIdentity
{
    public int AuthoringRevision { get; private set; }
    public string AuthoringSignature { get; private set; }
    public string CommittedAuthoringContentHash { get; private set; }

    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }
    public float ChunkSize { get; private set; }

    public int HeightfieldResolutionPerChunk { get; private set; }
    public int HeightTileChunkSpan { get; private set; }
    public int HeightTileGridWidth { get; private set; }
    public int HeightTileGridHeight { get; private set; }
    public int HeightTileSamplesPerSide { get; private set; }

    public string SurfaceSettingsSignature { get; private set; }

    public int CollisionResolution { get; private set; }
    public string CollisionSettingsSignature { get; private set; }

    internal TerrainRuntimeEquivalenceSourceIdentity(
        int authoringRevision,
        string authoringSignature,
        string committedAuthoringContentHash,
        int gridWidth,
        int gridHeight,
        float chunkSize,
        int heightfieldResolutionPerChunk,
        int heightTileChunkSpan,
        int heightTileGridWidth,
        int heightTileGridHeight,
        int heightTileSamplesPerSide,
        string surfaceSettingsSignature,
        int collisionResolution,
        string collisionSettingsSignature
    )
    {
        AuthoringRevision = authoringRevision;
        AuthoringSignature = authoringSignature ?? "";
        CommittedAuthoringContentHash = committedAuthoringContentHash ?? "";

        GridWidth = gridWidth;
        GridHeight = gridHeight;
        ChunkSize = chunkSize;

        HeightfieldResolutionPerChunk =
            heightfieldResolutionPerChunk;

        HeightTileChunkSpan =
            heightTileChunkSpan;

        HeightTileGridWidth =
            heightTileGridWidth;

        HeightTileGridHeight =
            heightTileGridHeight;

        HeightTileSamplesPerSide =
            heightTileSamplesPerSide;

        SurfaceSettingsSignature =
            surfaceSettingsSignature ?? "";

        CollisionResolution =
            collisionResolution;

        CollisionSettingsSignature =
            collisionSettingsSignature ?? "";
    }

    public bool ExactMatch(
        TerrainRuntimeEquivalenceSourceIdentity other,
        out List<string> differences
    )
    {
        differences =
            new List<string>();

        if (other == null)
        {
            differences.Add(
                "Comparison source identity is unavailable."
            );
            return false;
        }

        AddDifference(
            differences,
            "Authoring Revision",
            AuthoringRevision,
            other.AuthoringRevision
        );

        AddDifference(
            differences,
            "Authoring Signature",
            AuthoringSignature,
            other.AuthoringSignature
        );

        AddDifference(
            differences,
            "Committed Authoring Content Hash",
            CommittedAuthoringContentHash,
            other.CommittedAuthoringContentHash
        );

        AddDifference(
            differences,
            "Grid Width",
            GridWidth,
            other.GridWidth
        );

        AddDifference(
            differences,
            "Grid Height",
            GridHeight,
            other.GridHeight
        );

        AddDifference(
            differences,
            "Chunk Size",
            ChunkSize,
            other.ChunkSize
        );

        AddDifference(
            differences,
            "Heightfield Resolution Per Chunk",
            HeightfieldResolutionPerChunk,
            other.HeightfieldResolutionPerChunk
        );

        AddDifference(
            differences,
            "Height Tile Chunk Span",
            HeightTileChunkSpan,
            other.HeightTileChunkSpan
        );

        AddDifference(
            differences,
            "Height Tile Grid Width",
            HeightTileGridWidth,
            other.HeightTileGridWidth
        );

        AddDifference(
            differences,
            "Height Tile Grid Height",
            HeightTileGridHeight,
            other.HeightTileGridHeight
        );

        AddDifference(
            differences,
            "Height Tile Samples Per Side",
            HeightTileSamplesPerSide,
            other.HeightTileSamplesPerSide
        );

        AddDifference(
            differences,
            "Surface Settings Signature",
            SurfaceSettingsSignature,
            other.SurfaceSettingsSignature
        );

        AddDifference(
            differences,
            "Collision Resolution",
            CollisionResolution,
            other.CollisionResolution
        );

        AddDifference(
            differences,
            "Collision Settings Signature",
            CollisionSettingsSignature,
            other.CollisionSettingsSignature
        );

        return differences.Count == 0;
    }

    public bool CoreTerrainLayoutMatches(
        TerrainRuntimeEquivalenceSourceIdentity other
    )
    {
        if (other == null)
        {
            return false;
        }

        return
            GridWidth == other.GridWidth
            && GridHeight == other.GridHeight
            && ChunkSize.Equals(other.ChunkSize)
            && HeightfieldResolutionPerChunk ==
                other.HeightfieldResolutionPerChunk
            && HeightTileChunkSpan ==
                other.HeightTileChunkSpan
            && HeightTileGridWidth ==
                other.HeightTileGridWidth
            && HeightTileGridHeight ==
                other.HeightTileGridHeight
            && HeightTileSamplesPerSide ==
                other.HeightTileSamplesPerSide;
    }

    public string BuildSummary()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "Authoring Revision " +
            AuthoringRevision
        );

        builder.Append(
            ", Authoring " +
            ShortHash(AuthoringSignature)
        );

        builder.Append(
            ", Grid " +
            GridWidth +
            "x" +
            GridHeight
        );

        builder.Append(
            ", Height Tiles " +
            HeightTileGridWidth +
            "x" +
            HeightTileGridHeight
        );

        builder.Append(
            ", Surface " +
            ShortHash(SurfaceSettingsSignature)
        );

        builder.Append(
            ", Collision Resolution " +
            CollisionResolution
        );

        builder.Append(
            ", Collision " +
            ShortHash(CollisionSettingsSignature)
        );

        return builder.ToString();
    }

    private static void AddDifference(
        List<string> output,
        string label,
        int left,
        int right
    )
    {
        if (left != right)
        {
            output.Add(
                label +
                " changed: " +
                left +
                " -> " +
                right
            );
        }
    }

    private static void AddDifference(
        List<string> output,
        string label,
        float left,
        float right
    )
    {
        if (!left.Equals(right))
        {
            output.Add(
                label +
                " changed: " +
                left.ToString(
                    "R",
                    CultureInfo.InvariantCulture
                ) +
                " -> " +
                right.ToString(
                    "R",
                    CultureInfo.InvariantCulture
                )
            );
        }
    }

    private static void AddDifference(
        List<string> output,
        string label,
        string left,
        string right
    )
    {
        if (
            !string.Equals(
                left ?? "",
                right ?? "",
                StringComparison.Ordinal
            )
        )
        {
            output.Add(
                label +
                " changed: " +
                ShortHash(left) +
                " -> " +
                ShortHash(right)
            );
        }
    }

    internal static string ShortHash(
        string value
    )
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        return
            value.Length <= 12
                ? value
                : value.Substring(0, 12) + "...";
    }
}

public sealed class TerrainRuntimeEquivalenceBaseline
{
    public TerrainRuntimeEquivalenceScenario Scenario { get; private set; }

    public TerrainRuntimeEquivalenceSourceIdentity SourceIdentity { get; private set; }

    public DateTime CapturedAtUtc { get; private set; }

    internal TerrainRuntimeEquivalenceBaseline(
        TerrainRuntimeEquivalenceScenario scenario,
        TerrainRuntimeEquivalenceSourceIdentity sourceIdentity
    )
    {
        Scenario = scenario;
        SourceIdentity = sourceIdentity;
        CapturedAtUtc = DateTime.UtcNow;
    }
}

public sealed class TerrainRuntimeEquivalenceUnexpectedAsset
{
    public TerrainRuntimeEquivalenceDatasetKind DatasetKind { get; private set; }

    public string AssetPath { get; private set; }

    public bool HasCoordinate { get; private set; }

    public Vector2Int Coordinate { get; private set; }

    internal TerrainRuntimeEquivalenceUnexpectedAsset(
        TerrainRuntimeEquivalenceDatasetKind datasetKind,
        string assetPath,
        bool hasCoordinate,
        Vector2Int coordinate
    )
    {
        DatasetKind = datasetKind;
        AssetPath = assetPath ?? "";
        HasCoordinate = hasCoordinate;
        Coordinate = coordinate;
    }
}

public sealed class TerrainRuntimeEquivalenceSnapshot
{
    private readonly ReadOnlyCollection<
        TerrainRuntimeEquivalenceUnexpectedAsset
    > unexpectedAssets;

    private readonly ReadOnlyCollection<string> issues;

    public string Label { get; private set; }

    public TerrainRuntimeOutputFingerprintSnapshot OutputSnapshot { get; private set; }

    public TerrainRuntimeEquivalenceSourceIdentity SourceIdentity { get; private set; }

    public TerrainRuntimeReadinessResult Readiness { get; private set; }

    public string HeightRangeMetadataFingerprint { get; private set; }

    public IReadOnlyList<
        TerrainRuntimeEquivalenceUnexpectedAsset
    > UnexpectedAssets => unexpectedAssets;

    public IReadOnlyList<string> Issues => issues;

    public DateTime CapturedAtUtc { get; private set; }

    public bool WasCancelled =>
        OutputSnapshot != null
        && OutputSnapshot.WasCancelled;

    public bool IsComplete =>
        OutputSnapshot != null
        && OutputSnapshot.IsComplete
        && SourceIdentity != null
        && Readiness != null
        && Readiness.IsReady
        && !string.IsNullOrEmpty(
            HeightRangeMetadataFingerprint
        )
        && unexpectedAssets.Count == 0
        && issues.Count == 0;

    public int HeightAssetCount =>
        OutputSnapshot != null
            ? OutputSnapshot.HeightAssetCount
            : 0;

    public int SurfaceAssetCount =>
        OutputSnapshot != null
            ? OutputSnapshot.SurfaceAssetCount
            : 0;

    public int CollisionAssetCount =>
        OutputSnapshot != null
            ? OutputSnapshot.CollisionAssetCount
            : 0;

    public int ExpectedHeightAssetCount =>
        OutputSnapshot != null
            ? OutputSnapshot.ExpectedHeightAssetCount
            : 0;

    public int ExpectedSurfaceAssetCount =>
        OutputSnapshot != null
            ? OutputSnapshot.ExpectedSurfaceAssetCount
            : 0;

    public int ExpectedCollisionAssetCount =>
        OutputSnapshot != null
            ? OutputSnapshot.ExpectedCollisionAssetCount
            : 0;

    internal TerrainRuntimeEquivalenceSnapshot(
        string label,
        TerrainRuntimeOutputFingerprintSnapshot outputSnapshot,
        TerrainRuntimeEquivalenceSourceIdentity sourceIdentity,
        TerrainRuntimeReadinessResult readiness,
        string heightRangeMetadataFingerprint,
        IEnumerable<TerrainRuntimeEquivalenceUnexpectedAsset>
            unexpectedAssets,
        IEnumerable<string> issues
    )
    {
        Label = label ?? "";
        OutputSnapshot = outputSnapshot;
        SourceIdentity = sourceIdentity;
        Readiness = readiness;

        HeightRangeMetadataFingerprint =
            heightRangeMetadataFingerprint ?? "";

        this.unexpectedAssets =
            new List<TerrainRuntimeEquivalenceUnexpectedAsset>(
                unexpectedAssets
                ?? new TerrainRuntimeEquivalenceUnexpectedAsset[0]
            ).AsReadOnly();

        this.issues =
            new List<string>(
                issues
                ?? new string[0]
            ).AsReadOnly();

        CapturedAtUtc = DateTime.UtcNow;
    }
}

public sealed class TerrainRuntimeEquivalenceDifference
{
    public TerrainRuntimeEquivalenceDatasetKind DatasetKind { get; private set; }

    public TerrainRuntimeEquivalenceDifferenceType DifferenceType { get; private set; }

    public bool HasCoordinate { get; private set; }

    public Vector2Int Coordinate { get; private set; }

    public string IncrementalAssetPath { get; private set; }

    public string FullAssetPath { get; private set; }

    public string IncrementalFingerprint { get; private set; }

    public string FullFingerprint { get; private set; }

    public bool WasRegeneratedIncrementally { get; private set; }

    public string Description { get; private set; }

    internal TerrainRuntimeEquivalenceDifference(
        TerrainRuntimeEquivalenceDatasetKind datasetKind,
        TerrainRuntimeEquivalenceDifferenceType differenceType,
        bool hasCoordinate,
        Vector2Int coordinate,
        string incrementalAssetPath,
        string fullAssetPath,
        string incrementalFingerprint,
        string fullFingerprint,
        bool wasRegeneratedIncrementally,
        string description
    )
    {
        DatasetKind = datasetKind;
        DifferenceType = differenceType;
        HasCoordinate = hasCoordinate;
        Coordinate = coordinate;

        IncrementalAssetPath =
            incrementalAssetPath ?? "";

        FullAssetPath =
            fullAssetPath ?? "";

        IncrementalFingerprint =
            incrementalFingerprint ?? "";

        FullFingerprint =
            fullFingerprint ?? "";

        WasRegeneratedIncrementally =
            wasRegeneratedIncrementally;

        Description =
            description ?? "";
    }
}

public sealed class TerrainRuntimeEquivalenceDatasetComparison
{
    private readonly ReadOnlyCollection<
        TerrainRuntimeEquivalenceDifference
    > differences;

    public TerrainRuntimeEquivalenceDatasetKind DatasetKind { get; private set; }

    public int ExpectedCount { get; private set; }

    public int IncrementalCapturedCount { get; private set; }

    public int FullCapturedCount { get; private set; }

    public int MatchingCount { get; private set; }

    public int MissingFromIncrementalCount { get; private set; }

    public int MissingFromFullCount { get; private set; }

    public int UnexpectedIncrementalCount { get; private set; }

    public int UnexpectedFullCount { get; private set; }

    public int PayloadMismatchCount { get; private set; }

    public IReadOnlyList<
        TerrainRuntimeEquivalenceDifference
    > Differences => differences;

    public bool ExactMatch =>
        MissingFromIncrementalCount == 0
        && MissingFromFullCount == 0
        && UnexpectedIncrementalCount == 0
        && UnexpectedFullCount == 0
        && PayloadMismatchCount == 0;

    internal TerrainRuntimeEquivalenceDatasetComparison(
        TerrainRuntimeEquivalenceDatasetKind datasetKind,
        int expectedCount,
        int incrementalCapturedCount,
        int fullCapturedCount,
        int matchingCount,
        int missingFromIncrementalCount,
        int missingFromFullCount,
        int unexpectedIncrementalCount,
        int unexpectedFullCount,
        int payloadMismatchCount,
        IEnumerable<TerrainRuntimeEquivalenceDifference>
            differences
    )
    {
        DatasetKind = datasetKind;

        ExpectedCount =
            Mathf.Max(0, expectedCount);

        IncrementalCapturedCount =
            Mathf.Max(0, incrementalCapturedCount);

        FullCapturedCount =
            Mathf.Max(0, fullCapturedCount);

        MatchingCount =
            Mathf.Max(0, matchingCount);

        MissingFromIncrementalCount =
            Mathf.Max(0, missingFromIncrementalCount);

        MissingFromFullCount =
            Mathf.Max(0, missingFromFullCount);

        UnexpectedIncrementalCount =
            Mathf.Max(0, unexpectedIncrementalCount);

        UnexpectedFullCount =
            Mathf.Max(0, unexpectedFullCount);

        PayloadMismatchCount =
            Mathf.Max(0, payloadMismatchCount);

        this.differences =
            new List<TerrainRuntimeEquivalenceDifference>(
                differences
                ?? new TerrainRuntimeEquivalenceDifference[0]
            ).AsReadOnly();
    }
}

public sealed class TerrainRuntimeEquivalenceComparisonResult
{
    public TerrainRuntimeEquivalenceDatasetComparison Height { get; private set; }

    public TerrainRuntimeEquivalenceDatasetComparison Surface { get; private set; }

    public TerrainRuntimeEquivalenceDatasetComparison Collision { get; private set; }

    public bool HeightStreamingMatches { get; private set; }

    public bool HeightRangeMetadataMatches { get; private set; }

    public int TotalExpected =>
        GetExpected(Height)
        + GetExpected(Surface)
        + GetExpected(Collision);

    public int TotalMatching =>
        GetMatching(Height)
        + GetMatching(Surface)
        + GetMatching(Collision);

    public int TotalMissing =>
        GetMissing(Height)
        + GetMissing(Surface)
        + GetMissing(Collision);

    public int TotalUnexpected =>
        GetUnexpected(Height)
        + GetUnexpected(Surface)
        + GetUnexpected(Collision);

    public int TotalPayloadMismatches =>
        GetPayloadMismatches(Height)
        + GetPayloadMismatches(Surface)
        + GetPayloadMismatches(Collision);

    public bool IsEquivalent =>
        Height != null
        && Height.ExactMatch
        && Surface != null
        && Surface.ExactMatch
        && Collision != null
        && Collision.ExactMatch
        && HeightStreamingMatches
        && HeightRangeMetadataMatches;

    internal TerrainRuntimeEquivalenceComparisonResult(
        TerrainRuntimeEquivalenceDatasetComparison height,
        TerrainRuntimeEquivalenceDatasetComparison surface,
        TerrainRuntimeEquivalenceDatasetComparison collision,
        bool heightStreamingMatches,
        bool heightRangeMetadataMatches
    )
    {
        Height = height;
        Surface = surface;
        Collision = collision;
        HeightStreamingMatches = heightStreamingMatches;

        HeightRangeMetadataMatches =
            heightRangeMetadataMatches;
    }

    private static int GetExpected(
        TerrainRuntimeEquivalenceDatasetComparison result
    )
    {
        return result != null
            ? result.ExpectedCount
            : 0;
    }

    private static int GetMatching(
        TerrainRuntimeEquivalenceDatasetComparison result
    )
    {
        return result != null
            ? result.MatchingCount
            : 0;
    }

    private static int GetMissing(
        TerrainRuntimeEquivalenceDatasetComparison result
    )
    {
        return result != null
            ? result.MissingFromIncrementalCount
                + result.MissingFromFullCount
            : 0;
    }

    private static int GetUnexpected(
        TerrainRuntimeEquivalenceDatasetComparison result
    )
    {
        return result != null
            ? result.UnexpectedIncrementalCount
                + result.UnexpectedFullCount
            : 0;
    }

    private static int GetPayloadMismatches(
        TerrainRuntimeEquivalenceDatasetComparison result
    )
    {
        return result != null
            ? result.PayloadMismatchCount
            : 0;
    }
}

public sealed class TerrainRuntimeEquivalenceValidationResult
{
    private const int MaxDifferencePreview =
        20;

    private ReadOnlyCollection<string>
        sourceIdentityDifferences;

    public TerrainRuntimeEquivalenceScenario Scenario { get; internal set; }

    public TerrainRuntimeEquivalenceValidationOutcome Outcome { get; internal set; }

    public TerrainRuntimeEquivalenceBaseline Baseline { get; internal set; }

    public TerrainRuntimeBakePlan NormalBakePlan { get; internal set; }

    public TerrainRuntimeBakeValidationResult FirstBakeValidation { get; internal set; }

    public TerrainRuntimeEquivalenceSnapshot FirstSnapshot { get; internal set; }

    public TerrainRuntimeBakeValidationResult FullBakeValidation { get; internal set; }

    public TerrainRuntimeEquivalenceSnapshot FullSnapshot { get; internal set; }

    public TerrainRuntimeEquivalenceComparisonResult Comparison { get; internal set; }

    public TerrainRuntimeReadinessResult FinalReadiness { get; internal set; }

    public bool FullExecutionCertified { get; internal set; }

    public IReadOnlyList<string> SourceIdentityDifferences =>
        sourceIdentityDifferences;

    public string ErrorMessage { get; internal set; }

    public string SummaryMessage { get; internal set; }

    public DateTime CreatedAtUtc { get; private set; }

    public bool Passed =>
        Outcome == TerrainRuntimeEquivalenceValidationOutcome.Passed
        || Outcome == TerrainRuntimeEquivalenceValidationOutcome.PassedWithWarnings;

    internal TerrainRuntimeEquivalenceValidationResult(
        TerrainRuntimeEquivalenceScenario scenario
    )
    {
        Scenario = scenario;
        Outcome =
            TerrainRuntimeEquivalenceValidationOutcome.NotRun;

        sourceIdentityDifferences =
            new List<string>().AsReadOnly();

        ErrorMessage = "";
        SummaryMessage = "";

        CreatedAtUtc = DateTime.UtcNow;
    }

    internal void SetSourceIdentityDifferences(
        IEnumerable<string> differences
    )
    {
        List<string> copy =
            new List<string>(
                differences
                ?? new string[0]
            );

        sourceIdentityDifferences =
            copy.AsReadOnly();
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Incremental / Full Equivalence Validation"
        );

        builder.AppendLine(
            "Scenario: " +
            Scenario
        );

        builder.AppendLine(
            "Outcome: " +
            Outcome
        );

        if (
            FirstSnapshot != null
            && FirstSnapshot.SourceIdentity != null
        )
        {
            builder.AppendLine();
            builder.AppendLine(
                "Source State:"
            );

            builder.AppendLine(
                "  " +
                FirstSnapshot
                    .SourceIdentity
                    .BuildSummary()
            );

            builder.AppendLine(
                "  Unchanged Through Full: " +
                (sourceIdentityDifferences.Count == 0)
            );
        }

        if (NormalBakePlan != null)
        {
            builder.AppendLine();
            builder.AppendLine(
                GetFirstPhaseLabel() +
                " Plan:"
            );

            builder.AppendLine(
                "  " +
                TerrainRuntimeBakePipelineResult
                    .BuildPlanSummary(
                        NormalBakePlan
                    )
            );
        }

        AppendBakeValidation(
            builder,
            GetFirstPhaseLabel(),
            FirstBakeValidation,
            FirstSnapshot
        );

        AppendBakeValidation(
            builder,
            "Forced Full",
            FullBakeValidation,
            FullSnapshot
        );

        if (
            FullBakeValidation != null
        )
        {
            builder.AppendLine(
                "  Full Execution Certified: " +
                FullExecutionCertified
            );
        }

        if (Comparison != null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Equivalence:"
            );

            AppendDataset(
                builder,
                Comparison.Height
            );

            AppendDataset(
                builder,
                Comparison.Surface
            );

            AppendDataset(
                builder,
                Comparison.Collision
            );

            builder.AppendLine(
                "  Height Streaming Pyramid: " +
                (
                    Comparison.HeightStreamingMatches
                        ? "MATCH"
                        : "DIFFERENT"
                )
            );

            builder.AppendLine(
                "  Height Range Metadata: " +
                (
                    Comparison
                        .HeightRangeMetadataMatches
                        ? "MATCH"
                        : "DIFFERENT"
                )
            );

            builder.AppendLine();
            builder.AppendLine(
                "  Total Expected: " +
                Comparison.TotalExpected
            );

            builder.AppendLine(
                "  Total Matching: " +
                Comparison.TotalMatching
            );

            builder.AppendLine(
                "  Total Missing: " +
                Comparison.TotalMissing
            );

            builder.AppendLine(
                "  Total Unexpected: " +
                Comparison.TotalUnexpected
            );

            builder.AppendLine(
                "  Payload Mismatches: " +
                Comparison.TotalPayloadMismatches
            );

            AppendDifferencePreview(
                builder,
                Comparison.Height
            );

            AppendDifferencePreview(
                builder,
                Comparison.Surface
            );

            AppendDifferencePreview(
                builder,
                Comparison.Collision
            );
        }

        if (sourceIdentityDifferences.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Source Identity Differences:"
            );

            int count =
                Mathf.Min(
                    MaxDifferencePreview,
                    sourceIdentityDifferences.Count
                );

            for (
                int index = 0;
                index < count;
                index++
            )
            {
                builder.AppendLine(
                    "- " +
                    sourceIdentityDifferences[index]
                );
            }

            if (
                sourceIdentityDifferences.Count >
                count
            )
            {
                builder.AppendLine(
                    "- ... " +
                    (
                        sourceIdentityDifferences.Count -
                        count
                    ) +
                    " additional differences omitted"
                );
            }
        }

        if (FinalReadiness != null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Final:"
            );

            builder.AppendLine(
                "  Height: " +
                FinalReadiness.HeightStatus
            );

            builder.AppendLine(
                "  Surface: " +
                FinalReadiness.SurfaceStatus
            );

            builder.AppendLine(
                "  Collision: " +
                FinalReadiness.CollisionStatus
            );

            builder.AppendLine(
                "  Generated Integrity: " +
                (
                    FinalReadiness.IntegrityAudit != null
                    && FinalReadiness.IntegrityAudit.GeneratedDataValid
                        ? "Valid"
                        : "Invalid"
                )
            );

            builder.AppendLine(
                "  Addressables: " +
                (
                    FinalReadiness.AddressablesValidation != null
                    && FinalReadiness.AddressablesValidation.IsValid
                        ? "Valid"
                        : "Needs Repair"
                )
            );

            builder.AppendLine(
                "  Hierarchy: " +
                (
                    FinalReadiness.HierarchyReadiness != null
                    && FinalReadiness.HierarchyReadiness.IsReady
                        ? "Valid"
                        : "Needs Repair"
                )
            );

            builder.AppendLine(
                "  Plan: " +
                (
                    FinalReadiness.Plan == null
                        ? "Unavailable"
                        : FinalReadiness.Plan.HasWork
                            ? TerrainRuntimeBakePipelineResult
                                .BuildPlanSummary(
                                    FinalReadiness.Plan
                                )
                            : "No Work"
                )
            );

            builder.AppendLine(
                "  Runtime Ready: " +
                FinalReadiness.IsReady
            );
        }

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

        builder.AppendLine();
        builder.AppendLine(
            "Result: " +
            GetResultLabel()
        );

        return builder.ToString();
    }

    private string GetFirstPhaseLabel()
    {
        return
            Scenario ==
                TerrainRuntimeEquivalenceScenario.InitialBake
                ? "Initial Bake"
                : "Normal Bake";
    }

    private static void AppendBakeValidation(
        StringBuilder builder,
        string label,
        TerrainRuntimeBakeValidationResult validation,
        TerrainRuntimeEquivalenceSnapshot snapshot
    )
    {
        if (
            validation == null
            && snapshot == null
        )
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            label + ":"
        );

        if (validation != null)
        {
            builder.AppendLine(
                "  Package 10.1: " +
                validation.Outcome
            );

            AppendStage(
                builder,
                "Height",
                validation.HeightValidation,
                snapshot != null
                    ? snapshot.ExpectedHeightAssetCount
                    : 0
            );

            AppendStage(
                builder,
                "Surface",
                validation.SurfaceValidation,
                snapshot != null
                    ? snapshot.ExpectedSurfaceAssetCount
                    : 0
            );

            AppendStage(
                builder,
                "Collision",
                validation.CollisionValidation,
                snapshot != null
                    ? snapshot.ExpectedCollisionAssetCount
                    : 0
            );

            builder.AppendLine(
                "  Addressables: " +
                (
                    validation.AddressablesValidation != null
                        ? validation
                            .AddressablesValidation
                            .ActualOperationMode
                            .ToString()
                        : "Unavailable"
                )
            );
        }

        if (snapshot != null)
        {
            builder.AppendLine(
                "  Complete Snapshot: " +
                snapshot.IsComplete
            );

            builder.AppendLine(
                "  Height Assets: " +
                snapshot.HeightAssetCount +
                " / " +
                snapshot.ExpectedHeightAssetCount
            );

            builder.AppendLine(
                "  Surface Assets: " +
                snapshot.SurfaceAssetCount +
                " / " +
                snapshot.ExpectedSurfaceAssetCount
            );

            builder.AppendLine(
                "  Collision Assets: " +
                snapshot.CollisionAssetCount +
                " / " +
                snapshot.ExpectedCollisionAssetCount
            );

            builder.AppendLine(
                "  Unexpected Assets: " +
                snapshot.UnexpectedAssets.Count
            );

            builder.AppendLine(
                "  Runtime Ready: " +
                (
                    snapshot.Readiness != null
                    && snapshot.Readiness.IsReady
                )
            );

            if (snapshot.Issues.Count > 0)
            {
                builder.AppendLine(
                    "  Snapshot Issues: " +
                    snapshot.Issues.Count
                );

                int issueCount =
                    Mathf.Min(
                        MaxDifferencePreview,
                        snapshot.Issues.Count
                    );

                for (
                    int index = 0;
                    index < issueCount;
                    index++
                )
                {
                    builder.AppendLine(
                        "    - " +
                        snapshot.Issues[index]
                    );
                }

                if (
                    snapshot.Issues.Count >
                    issueCount
                )
                {
                    builder.AppendLine(
                        "    - ... " +
                        (
                            snapshot.Issues.Count -
                            issueCount
                        ) +
                        " additional snapshot issues omitted"
                    );
                }
            }
        }
    }

    private static void AppendStage(
        StringBuilder builder,
        string label,
        TerrainRuntimeBakeStageValidationResult stage,
        int total
    )
    {
        if (stage == null)
        {
            builder.AppendLine(
                "  " +
                label +
                ": unavailable"
            );
            return;
        }

        builder.AppendLine(
            "  " +
            label +
            ": " +
            stage.ActualWorkMode +
            ", " +
            stage.SucceededCount +
            " / " +
            Mathf.Max(
                total,
                stage.ExpectedCount
            )
        );
    }

    private static void AppendDataset(
        StringBuilder builder,
        TerrainRuntimeEquivalenceDatasetComparison dataset
    )
    {
        if (dataset == null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "  " +
            dataset.DatasetKind +
            ":"
        );

        builder.AppendLine(
            "    Expected: " +
            dataset.ExpectedCount
        );

        builder.AppendLine(
            "    Incremental Captured: " +
            dataset.IncrementalCapturedCount
        );

        builder.AppendLine(
            "    Full Captured: " +
            dataset.FullCapturedCount
        );

        builder.AppendLine(
            "    Matching: " +
            dataset.MatchingCount
        );

        builder.AppendLine(
            "    Missing From Incremental: " +
            dataset.MissingFromIncrementalCount
        );

        builder.AppendLine(
            "    Missing From Full: " +
            dataset.MissingFromFullCount
        );

        builder.AppendLine(
            "    Unexpected Incremental: " +
            dataset.UnexpectedIncrementalCount
        );

        builder.AppendLine(
            "    Unexpected Full: " +
            dataset.UnexpectedFullCount
        );

        builder.AppendLine(
            "    Payload Mismatches: " +
            dataset.PayloadMismatchCount
        );

        builder.AppendLine(
            "    " +
            (
                dataset.ExactMatch
                    ? "MATCH"
                    : "DIFFERENT"
            )
        );
    }

    private static void AppendDifferencePreview(
        StringBuilder builder,
        TerrainRuntimeEquivalenceDatasetComparison dataset
    )
    {
        if (
            dataset == null
            || dataset.Differences.Count == 0
        )
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            dataset.DatasetKind +
            " Difference Preview:"
        );

        int count =
            Mathf.Min(
                MaxDifferencePreview,
                dataset.Differences.Count
            );

        for (
            int index = 0;
            index < count;
            index++
        )
        {
            TerrainRuntimeEquivalenceDifference difference =
                dataset.Differences[index];

            string coordinate =
                difference.HasCoordinate
                    ? "(" +
                        difference.Coordinate.x +
                        ", " +
                        difference.Coordinate.y +
                        ")"
                    : "<no coordinate>";

            builder.AppendLine(
                "- " +
                difference.DifferenceType +
                " " +
                coordinate
            );

            if (
                !string.IsNullOrEmpty(
                    difference.IncrementalAssetPath
                )
            )
            {
                builder.AppendLine(
                    "    Incremental: " +
                    difference.IncrementalAssetPath
                );
            }

            if (
                !string.IsNullOrEmpty(
                    difference.FullAssetPath
                )
            )
            {
                builder.AppendLine(
                    "    Full: " +
                    difference.FullAssetPath
                );
            }

            if (
                difference.DifferenceType ==
                TerrainRuntimeEquivalenceDifferenceType
                    .PayloadMismatch
            )
            {
                builder.AppendLine(
                    "    Incremental Fingerprint: " +
                    TerrainRuntimeEquivalenceSourceIdentity
                        .ShortHash(
                            difference
                                .IncrementalFingerprint
                        )
                );

                builder.AppendLine(
                    "    Full Fingerprint: " +
                    TerrainRuntimeEquivalenceSourceIdentity
                        .ShortHash(
                            difference
                                .FullFingerprint
                        )
                );

                builder.AppendLine(
                    "    Regenerated Incrementally: " +
                    difference
                        .WasRegeneratedIncrementally
                );
            }

            if (
                !string.IsNullOrEmpty(
                    difference.Description
                )
            )
            {
                builder.AppendLine(
                    "    " +
                    difference.Description
                );
            }
        }

        if (
            dataset.Differences.Count >
            count
        )
        {
            builder.AppendLine(
                "- ... " +
                (
                    dataset.Differences.Count -
                    count
                ) +
                " additional differences omitted"
            );
        }
    }

    private string GetResultLabel()
    {
        switch (Outcome)
        {
            case TerrainRuntimeEquivalenceValidationOutcome.Passed:
            case TerrainRuntimeEquivalenceValidationOutcome.PassedWithWarnings:
                return "PASS";

            case TerrainRuntimeEquivalenceValidationOutcome.IncrementalCaptured:
            case TerrainRuntimeEquivalenceValidationOutcome.RebuildingFull:
            case TerrainRuntimeEquivalenceValidationOutcome.Comparing:
                return "IN PROGRESS";

            case TerrainRuntimeEquivalenceValidationOutcome.ComparisonInvalidated:
                return "INVALIDATED";

            case TerrainRuntimeEquivalenceValidationOutcome.Cancelled:
                return "CANCELLED";

            case TerrainRuntimeEquivalenceValidationOutcome.Blocked:
                return "BLOCKED";

            default:
                return "FAIL";
        }
    }
}

public static class TerrainRuntimeEquivalenceSnapshotUtility
{
    private static readonly Regex HeightNamePattern =
        new Regex(
            @"^HeightTile_(-?\d+)_(-?\d+)\.asset$",
            RegexOptions.CultureInvariant
        );

    private static readonly Regex SurfaceNamePattern =
        new Regex(
            @"^SurfaceTile_(-?\d+)_(-?\d+)\.asset$",
            RegexOptions.CultureInvariant
        );

    private static readonly Regex CollisionNamePattern =
        new Regex(
            @"^Chunk_(-?\d+)_(-?\d+)_Collision\.asset$",
            RegexOptions.CultureInvariant
        );

    public static TerrainRuntimeEquivalenceSourceIdentity CaptureSourceIdentity(
        WorldSettings worldSettings = null,
        TerrainAuthoringData authoringData = null
    )
    {
        if (worldSettings == null)
        {
            worldSettings =
                AssetDatabase.LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths.WorldSettingsAssetPath
                );
        }

        if (authoringData == null)
        {
            authoringData =
                AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths.TerrainAuthoringDataAssetPath
                );
        }

        if (
            worldSettings == null
            || authoringData == null
        )
        {
            return null;
        }

        TerrainAuthoringHeightManifest authoringManifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        return
            new TerrainRuntimeEquivalenceSourceIdentity(
                authoringData.authoringRevision,
                TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        authoringData
                    ),
                authoringManifest != null
                    ? authoringManifest
                        .committedContentHash
                    : "",
                worldSettings.gridWidth,
                worldSettings.gridHeight,
                worldSettings.chunkSize,
                worldSettings
                    .heightfieldResolutionPerChunk,
                worldSettings
                    .heightTileChunkSpan,
                worldSettings
                    .HeightTileGridWidth,
                worldSettings
                    .HeightTileGridHeight,
                worldSettings
                    .HeightTileSamplesPerSide,
                surfaceSettings != null
                    ? TerrainSurfaceSignatureUtility
                        .GetSettingsSignature(
                            surfaceSettings
                        )
                    : "",
                worldSettings
                    .collisionResolution,
                TerrainGenerationStateUtility
                    .GetCurrentCollisionSettingsSignature(
                        worldSettings
                    )
            );
    }

    public static TerrainRuntimeEquivalenceSnapshot CaptureCompleteSnapshot(
        string label
    )
    {
        List<string> issues =
            new List<string>();

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (
            worldSettings == null
            || authoringData == null
        )
        {
            issues.Add(
                worldSettings == null
                    ? "WorldSettings is unavailable."
                    : "TerrainAuthoringData is unavailable."
            );

            return
                new TerrainRuntimeEquivalenceSnapshot(
                    label,
                    null,
                    null,
                    null,
                    "",
                    null,
                    issues
                );
        }

        TerrainRuntimeReadinessResult readiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(true);

        if (
            readiness == null
            || !readiness.IsReady
        )
        {
            issues.Add(
                readiness != null
                    ? "Runtime is not Ready before complete equivalence snapshot capture: " +
                        readiness.ErrorMessage
                    : "Runtime readiness could not be evaluated."
            );

            return
                new TerrainRuntimeEquivalenceSnapshot(
                    label,
                    null,
                    CaptureSourceIdentity(
                        worldSettings,
                        authoringData
                    ),
                    readiness,
                    "",
                    null,
                    issues
                );
        }

        TerrainRuntimeEquivalenceSourceIdentity before =
            CaptureSourceIdentity(
                worldSettings,
                authoringData
            );

        TerrainRuntimeOutputFingerprintSnapshot output =
            TerrainRuntimeOutputFingerprintUtility
                .CaptureCurrentSnapshot(
                    worldSettings
                );

        TerrainRuntimeEquivalenceSourceIdentity after =
            CaptureSourceIdentity(
                worldSettings,
                authoringData
            );

        if (
            before == null
            || after == null
        )
        {
            issues.Add(
                "Source identity could not be captured around output fingerprinting."
            );
        }
        else if (
            !before.ExactMatch(
                after,
                out List<string> identityDifferences
            )
        )
        {
            issues.Add(
                "Authoring or generation settings changed during complete snapshot capture."
            );

            for (
                int index = 0;
                index <
                    identityDifferences.Count;
                index++
            )
            {
                issues.Add(
                    identityDifferences[index]
                );
            }
        }

        if (
            output == null
            || !output.IsComplete
        )
        {
            issues.Add(
                output != null
                    ? output.WasCancelled
                        ? "Package 10.1 complete output fingerprint capture was cancelled."
                        : "Package 10.1 complete output fingerprint capture was incomplete."
                    : "Package 10.1 complete output fingerprint capture returned no snapshot."
            );

            if (output != null)
            {
                for (
                    int index = 0;
                    index < output.Issues.Count;
                    index++
                )
                {
                    issues.Add(
                        output.Issues[index]
                    );
                }

                if (
                    !string.IsNullOrEmpty(
                        output.ErrorMessage
                    )
                )
                {
                    issues.Add(
                        output.ErrorMessage
                    );
                }
            }
        }

        string heightRangeFingerprint =
            "";

        bool heightRangeCaptured =
            TryCaptureHeightRangeMetadataFingerprint(
                worldSettings,
                out heightRangeFingerprint,
                out string heightRangeIssue
            );

        if (!heightRangeCaptured)
        {
            heightRangeFingerprint =
                "";
        }

        if (
            !string.IsNullOrEmpty(
                heightRangeIssue
            )
        )
        {
            issues.Add(
                heightRangeIssue
            );
        }

        List<TerrainRuntimeEquivalenceUnexpectedAsset>
            unexpected =
                CollectUnexpectedAssets(
                    worldSettings
                );

        if (unexpected.Count > 0)
        {
            issues.Add(
                "Found " +
                unexpected.Count +
                " unexpected generated runtime asset(s) outside the current canonical coordinate domain."
            );
        }

        return
            new TerrainRuntimeEquivalenceSnapshot(
                label,
                output,
                after ?? before,
                readiness,
                heightRangeFingerprint,
                unexpected,
                issues
            );
    }

    /*
     * Height range metadata is runtime-semantic: Package 04 copies the
     * manifest global range into TerrainClipmapBoundsController, while the
     * per-tile ranges are the authoritative lightweight reduction source used
     * by incremental Height generation. Compare this semantic metadata without
     * including generation revisions/signatures.
     */
    private static bool TryCaptureHeightRangeMetadataFingerprint(
        WorldSettings worldSettings,
        out string fingerprint,
        out string issue
    )
    {
        fingerprint = "";
        issue = "";

        TerrainHeightmapManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
            );

        if (
            worldSettings == null
            || manifest == null
        )
        {
            issue =
                "Runtime Height manifest is unavailable for semantic range fingerprinting.";
            return false;
        }

        if (
            !manifest.isComplete
            || !manifest.HasCompleteTileHeightRanges
            || !manifest.HasValidHeightRange
        )
        {
            issue =
                "Runtime Height manifest range metadata is incomplete or invalid.";
            return false;
        }

        if (
            !manifest.TryCalculateGlobalHeightRange(
                out float reducedMinimum,
                out float reducedMaximum
            )
        )
        {
            issue =
                "Runtime Height manifest tile ranges could not be reduced to a global range.";
            return false;
        }

        if (
            !reducedMinimum.Equals(
                manifest.minimumTerrainHeight
            )
            || !reducedMaximum.Equals(
                manifest.maximumTerrainHeight
            )
        )
        {
            issue =
                "Runtime Height manifest global range does not exactly match the reduction of per-tile range metadata.";
            return false;
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "WorldMeshesRuntimeHeightRangeEquivalenceV1"
        );

        Append(
            builder,
            manifest.heightTileGridWidth
        );

        Append(
            builder,
            manifest.heightTileGridHeight
        );

        Append(
            builder,
            manifest.minimumTerrainHeight
        );

        Append(
            builder,
            manifest.maximumTerrainHeight
        );

        for (
            int z = 0;
            z < manifest.heightTileGridHeight;
            z++
        )
        {
            for (
                int x = 0;
                x < manifest.heightTileGridWidth;
                x++
            )
            {
                if (
                    !manifest.TryGetTileHeightRange(
                        x,
                        z,
                        out float minimum,
                        out float maximum
                    )
                )
                {
                    issue =
                        "Runtime Height manifest is missing tile range metadata at (" +
                        x +
                        ", " +
                        z +
                        ").";
                    return false;
                }

                Append(
                    builder,
                    x
                );

                Append(
                    builder,
                    z
                );

                Append(
                    builder,
                    minimum
                );

                Append(
                    builder,
                    maximum
                );
            }
        }

        using (
            SHA256 sha =
                SHA256.Create()
        )
        {
            byte[] bytes =
                Encoding.UTF8.GetBytes(
                    builder.ToString()
                );

            fingerprint =
                ToHex(
                    sha.ComputeHash(
                        bytes
                    )
                );
        }

        return true;
    }

    private static List<
        TerrainRuntimeEquivalenceUnexpectedAsset
    > CollectUnexpectedAssets(
        WorldSettings worldSettings
    )
    {
        List<TerrainRuntimeEquivalenceUnexpectedAsset>
            output =
                new List<
                    TerrainRuntimeEquivalenceUnexpectedAsset
                >();

        if (worldSettings == null)
        {
            return output;
        }

        HashSet<string> expectedHeight =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        HashSet<string> expectedSurface =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        HashSet<string> expectedCollision =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        for (
            int z = 0;
            z <
                Mathf.Max(
                    1,
                    worldSettings.HeightTileGridHeight
                );
            z++
        )
        {
            for (
                int x = 0;
                x <
                    Mathf.Max(
                        1,
                        worldSettings.HeightTileGridWidth
                    );
                x++
            )
            {
                expectedHeight.Add(
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTilePath(
                            x,
                            z
                        )
                );

                expectedSurface.Add(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .GetSurfaceTilePath(
                            x,
                            z
                        )
                );
            }
        }

        for (
            int z = 0;
            z <
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                );
            z++
        )
        {
            for (
                int x = 0;
                x <
                    Mathf.Max(
                        1,
                        worldSettings.gridWidth
                    );
                x++
            )
            {
                expectedCollision.Add(
                    TerrainCollisionMeshGenerator
                        .GetCollisionMeshPath(
                            x,
                            z
                        )
                );
            }
        }

        CollectUnexpectedInFolder(
            TerrainRuntimeEquivalenceDatasetKind.Height,
            TerrainRuntimeHeightAssetUtility
                .HeightmapTileFolder,
            "t:Texture2D",
            HeightNamePattern,
            expectedHeight,
            output
        );

        CollectUnexpectedInFolder(
            TerrainRuntimeEquivalenceDatasetKind.Surface,
            TerrainRuntimeSurfaceMaskAssetUtility
                .SurfaceMaskTileFolder,
            "t:Texture2D",
            SurfaceNamePattern,
            expectedSurface,
            output
        );

        CollectUnexpectedInFolder(
            TerrainRuntimeEquivalenceDatasetKind.Collision,
            TerrainCollisionMeshGenerator
                .CollisionMeshFolder,
            "t:Mesh",
            CollisionNamePattern,
            expectedCollision,
            output
        );

        output.Sort(
            CompareUnexpectedAssets
        );

        return output;
    }

    private static void CollectUnexpectedInFolder(
        TerrainRuntimeEquivalenceDatasetKind kind,
        string folder,
        string assetFilter,
        Regex canonicalNamePattern,
        HashSet<string> expectedPaths,
        List<TerrainRuntimeEquivalenceUnexpectedAsset>
            output
    )
    {
        if (
            string.IsNullOrEmpty(folder)
            || !AssetDatabase.IsValidFolder(
                folder
            )
        )
        {
            return;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                assetFilter,
                new[]
                {
                    folder
                }
            );

        for (
            int index = 0;
            index < guids.Length;
            index++
        )
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guids[index]
                );

            string fileName =
                Path.GetFileName(
                    path
                );

            Match match =
                canonicalNamePattern.Match(
                    fileName
                );

            if (!match.Success)
            {
                continue;
            }

            if (
                expectedPaths.Contains(
                    path
                )
            )
            {
                continue;
            }

            int x = 0;
            int z = 0;

            bool hasCoordinate =
                int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out x
                )
                &&
                int.TryParse(
                    match.Groups[2].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out z
                );

            output.Add(
                new TerrainRuntimeEquivalenceUnexpectedAsset(
                    kind,
                    path,
                    hasCoordinate,
                    hasCoordinate
                        ? new Vector2Int(
                            x,
                            z
                        )
                        : default
                )
            );
        }
    }

    private static int CompareUnexpectedAssets(
        TerrainRuntimeEquivalenceUnexpectedAsset left,
        TerrainRuntimeEquivalenceUnexpectedAsset right
    )
    {
        int kind =
            left.DatasetKind.CompareTo(
                right.DatasetKind
            );

        if (kind != 0)
        {
            return kind;
        }

        if (
            left.HasCoordinate
            && right.HasCoordinate
        )
        {
            int coordinate =
                TerrainRuntimeBakeStageValidationResult
                    .CompareCoordinates(
                        left.Coordinate,
                        right.Coordinate
                    );

            if (coordinate != 0)
            {
                return coordinate;
            }
        }

        return
            string.CompareOrdinal(
                left.AssetPath,
                right.AssetPath
            );
    }

    private static void Append(
        StringBuilder builder,
        int value
    )
    {
        builder.Append('|');
        builder.Append(
            value.ToString(
                CultureInfo.InvariantCulture
            )
        );
    }

    private static void Append(
        StringBuilder builder,
        float value
    )
    {
        builder.Append('|');
        builder.Append(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture
            )
        );
    }

    private static string ToHex(
        byte[] bytes
    )
    {
        if (
            bytes == null
            || bytes.Length == 0
        )
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder(
                bytes.Length * 2
            );

        for (
            int index = 0;
            index < bytes.Length;
            index++
        )
        {
            builder.Append(
                bytes[index].ToString(
                    "x2",
                    CultureInfo.InvariantCulture
                )
            );
        }

        return builder.ToString();
    }
}

public static class TerrainRuntimeEquivalenceComparisonUtility
{
    public static TerrainRuntimeEquivalenceComparisonResult Compare(
        TerrainRuntimeEquivalenceSnapshot incremental,
        TerrainRuntimeEquivalenceSnapshot full,
        TerrainRuntimeBakeValidationResult incrementalValidation
    )
    {
        if (
            incremental == null
            || full == null
        )
        {
            return null;
        }

        TerrainRuntimeOutputFingerprintComparison baseComparison =
            TerrainRuntimeOutputFingerprintUtility
                .Compare(
                    incremental.OutputSnapshot,
                    full.OutputSnapshot
                );

        if (baseComparison == null)
        {
            return null;
        }

        TerrainRuntimeEquivalenceDatasetComparison height =
            BuildDatasetComparison(
                TerrainRuntimeEquivalenceDatasetKind.Height,
                baseComparison.Height,
                incremental,
                full,
                incrementalValidation != null
                    ? incrementalValidation.HeightValidation
                    : null
            );

        TerrainRuntimeEquivalenceDatasetComparison surface =
            BuildDatasetComparison(
                TerrainRuntimeEquivalenceDatasetKind.Surface,
                baseComparison.Surface,
                incremental,
                full,
                incrementalValidation != null
                    ? incrementalValidation.SurfaceValidation
                    : null
            );

        TerrainRuntimeEquivalenceDatasetComparison collision =
            BuildDatasetComparison(
                TerrainRuntimeEquivalenceDatasetKind.Collision,
                baseComparison.Collision,
                incremental,
                full,
                incrementalValidation != null
                    ? incrementalValidation.CollisionValidation
                    : null
            );

        bool heightRangeMatch =
            !string.IsNullOrEmpty(
                incremental.HeightRangeMetadataFingerprint
            )
            && !string.IsNullOrEmpty(
                full.HeightRangeMetadataFingerprint
            )
            && string.Equals(
                incremental.HeightRangeMetadataFingerprint,
                full.HeightRangeMetadataFingerprint,
                StringComparison.Ordinal
            );

        if (!heightRangeMatch)
        {
            List<TerrainRuntimeEquivalenceDifference>
                heightDifferences =
                    new List<
                        TerrainRuntimeEquivalenceDifference
                    >(
                        height.Differences
                    );

            heightDifferences.Add(
                new TerrainRuntimeEquivalenceDifference(
                    TerrainRuntimeEquivalenceDatasetKind
                        .HeightManifest,
                    TerrainRuntimeEquivalenceDifferenceType
                        .HeightRangeMetadataMismatch,
                    false,
                    default,
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath,
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath,
                    incremental
                        .HeightRangeMetadataFingerprint,
                    full
                        .HeightRangeMetadataFingerprint,
                    false,
                    "Runtime-semantic Height manifest global/per-tile range metadata differs between the normal bake result and Full rebuild."
                )
            );

            height =
                new TerrainRuntimeEquivalenceDatasetComparison(
                    height.DatasetKind,
                    height.ExpectedCount,
                    height.IncrementalCapturedCount,
                    height.FullCapturedCount,
                    height.MatchingCount,
                    height.MissingFromIncrementalCount,
                    height.MissingFromFullCount,
                    height.UnexpectedIncrementalCount,
                    height.UnexpectedFullCount,
                    height.PayloadMismatchCount,
                    heightDifferences
                );
        }

        return
            new TerrainRuntimeEquivalenceComparisonResult(
                height,
                surface,
                collision,
                baseComparison.HeightStreamingExactMatch,
                heightRangeMatch
            );
    }

    private static TerrainRuntimeEquivalenceDatasetComparison
        BuildDatasetComparison(
            TerrainRuntimeEquivalenceDatasetKind kind,
            TerrainRuntimeOutputFingerprintStreamComparison stream,
            TerrainRuntimeEquivalenceSnapshot incremental,
            TerrainRuntimeEquivalenceSnapshot full,
            TerrainRuntimeBakeStageValidationResult incrementalStage
        )
    {
        List<TerrainRuntimeEquivalenceDifference>
            differences =
                new List<
                    TerrainRuntimeEquivalenceDifference
                >();

        IReadOnlyList<TerrainRuntimeOutputFingerprint>
            incrementalFingerprints =
                GetFingerprints(
                    incremental,
                    kind
                );

        IReadOnlyList<TerrainRuntimeOutputFingerprint>
            fullFingerprints =
                GetFingerprints(
                    full,
                    kind
                );

        Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint>
            incrementalByCoordinate =
                ToDictionary(
                    incrementalFingerprints
                );

        Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint>
            fullByCoordinate =
                ToDictionary(
                    fullFingerprints
                );

        HashSet<Vector2Int> regeneratedIncrementally =
            incrementalStage != null
                ? new HashSet<Vector2Int>(
                    incrementalStage
                        .SucceededCoordinates
                )
                : new HashSet<Vector2Int>();

        if (stream != null)
        {
            for (
                int index = 0;
                index < stream.Unexpected.Count;
                index++
            )
            {
                Vector2Int coordinate =
                    stream.Unexpected[index];

                fullByCoordinate.TryGetValue(
                    coordinate,
                    out TerrainRuntimeOutputFingerprint
                        fullFingerprint
                );

                differences.Add(
                    new TerrainRuntimeEquivalenceDifference(
                        kind,
                        TerrainRuntimeEquivalenceDifferenceType
                            .MissingFromIncremental,
                        true,
                        coordinate,
                        "",
                        fullFingerprint != null
                            ? fullFingerprint.AssetPath
                            : "",
                        "",
                        fullFingerprint != null
                            ? fullFingerprint.PayloadHash
                            : "",
                        regeneratedIncrementally.Contains(
                            coordinate
                        ),
                        "Expected output is present after Full rebuild but was absent from the complete normal-bake snapshot."
                    )
                );
            }

            for (
                int index = 0;
                index < stream.Missing.Count;
                index++
            )
            {
                Vector2Int coordinate =
                    stream.Missing[index];

                incrementalByCoordinate.TryGetValue(
                    coordinate,
                    out TerrainRuntimeOutputFingerprint
                        incrementalFingerprint
                );

                differences.Add(
                    new TerrainRuntimeEquivalenceDifference(
                        kind,
                        TerrainRuntimeEquivalenceDifferenceType
                            .MissingFromFull,
                        true,
                        coordinate,
                        incrementalFingerprint != null
                            ? incrementalFingerprint.AssetPath
                            : "",
                        "",
                        incrementalFingerprint != null
                            ? incrementalFingerprint.PayloadHash
                            : "",
                        "",
                        regeneratedIncrementally.Contains(
                            coordinate
                        ),
                        "Expected output was present after the normal bake but absent after Full rebuild."
                    )
                );
            }

            for (
                int index = 0;
                index <
                    stream.PayloadMismatches.Count;
                index++
            )
            {
                Vector2Int coordinate =
                    stream.PayloadMismatches[index];

                incrementalByCoordinate.TryGetValue(
                    coordinate,
                    out TerrainRuntimeOutputFingerprint
                        incrementalFingerprint
                );

                fullByCoordinate.TryGetValue(
                    coordinate,
                    out TerrainRuntimeOutputFingerprint
                        fullFingerprint
                );

                differences.Add(
                    new TerrainRuntimeEquivalenceDifference(
                        kind,
                        TerrainRuntimeEquivalenceDifferenceType
                            .PayloadMismatch,
                        true,
                        coordinate,
                        incrementalFingerprint != null
                            ? incrementalFingerprint.AssetPath
                            : "",
                        fullFingerprint != null
                            ? fullFingerprint.AssetPath
                            : "",
                        incrementalFingerprint != null
                            ? incrementalFingerprint.PayloadHash
                            : "",
                        fullFingerprint != null
                            ? fullFingerprint.PayloadHash
                            : "",
                        regeneratedIncrementally.Contains(
                            coordinate
                        ),
                        regeneratedIncrementally.Contains(
                            coordinate
                        )
                            ? "This output WAS regenerated by the normal bake; investigate incremental-vs-Full generation determinism."
                            : "This output was NOT regenerated by the normal bake; investigate invalidation/dependency coverage."
                    )
                );
            }
        }

        List<TerrainRuntimeEquivalenceUnexpectedAsset>
            unexpectedIncremental =
                GetUnexpected(
                    incremental,
                    kind
                );

        List<TerrainRuntimeEquivalenceUnexpectedAsset>
            unexpectedFull =
                GetUnexpected(
                    full,
                    kind
                );

        AppendUnexpectedDifferences(
            differences,
            kind,
            unexpectedIncremental,
            TerrainRuntimeEquivalenceDifferenceType
                .UnexpectedIncremental
        );

        AppendUnexpectedDifferences(
            differences,
            kind,
            unexpectedFull,
            TerrainRuntimeEquivalenceDifferenceType
                .UnexpectedFull
        );

        int expected =
            GetExpectedCount(
                incremental,
                kind
            );

        int incrementalCaptured =
            incrementalFingerprints != null
                ? incrementalFingerprints.Count
                : 0;

        int fullCaptured =
            fullFingerprints != null
                ? fullFingerprints.Count
                : 0;

        return
            new TerrainRuntimeEquivalenceDatasetComparison(
                kind,
                expected,
                incrementalCaptured,
                fullCaptured,
                stream != null
                    ? stream.Matching.Count
                    : 0,
                stream != null
                    ? stream.Unexpected.Count
                    : 0,
                stream != null
                    ? stream.Missing.Count
                    : 0,
                unexpectedIncremental.Count,
                unexpectedFull.Count,
                stream != null
                    ? stream.PayloadMismatches.Count
                    : 0,
                differences
            );
    }

    private static IReadOnlyList<
        TerrainRuntimeOutputFingerprint
    > GetFingerprints(
        TerrainRuntimeEquivalenceSnapshot snapshot,
        TerrainRuntimeEquivalenceDatasetKind kind
    )
    {
        if (
            snapshot == null
            || snapshot.OutputSnapshot == null
        )
        {
            return
                new TerrainRuntimeOutputFingerprint[0];
        }

        switch (kind)
        {
            case TerrainRuntimeEquivalenceDatasetKind.Height:
                return
                    snapshot.OutputSnapshot
                        .HeightFingerprints;

            case TerrainRuntimeEquivalenceDatasetKind.Surface:
                return
                    snapshot.OutputSnapshot
                        .SurfaceFingerprints;

            case TerrainRuntimeEquivalenceDatasetKind.Collision:
                return
                    snapshot.OutputSnapshot
                        .CollisionFingerprints;

            default:
                return
                    new TerrainRuntimeOutputFingerprint[0];
        }
    }

    private static int GetExpectedCount(
        TerrainRuntimeEquivalenceSnapshot snapshot,
        TerrainRuntimeEquivalenceDatasetKind kind
    )
    {
        if (snapshot == null)
        {
            return 0;
        }

        switch (kind)
        {
            case TerrainRuntimeEquivalenceDatasetKind.Height:
                return
                    snapshot.ExpectedHeightAssetCount;

            case TerrainRuntimeEquivalenceDatasetKind.Surface:
                return
                    snapshot.ExpectedSurfaceAssetCount;

            case TerrainRuntimeEquivalenceDatasetKind.Collision:
                return
                    snapshot.ExpectedCollisionAssetCount;

            default:
                return 0;
        }
    }

    private static Dictionary<
        Vector2Int,
        TerrainRuntimeOutputFingerprint
    > ToDictionary(
        IReadOnlyList<
            TerrainRuntimeOutputFingerprint
        > source
    )
    {
        Dictionary<
            Vector2Int,
            TerrainRuntimeOutputFingerprint
        > result =
            new Dictionary<
                Vector2Int,
                TerrainRuntimeOutputFingerprint
            >();

        if (source == null)
        {
            return result;
        }

        for (
            int index = 0;
            index < source.Count;
            index++
        )
        {
            TerrainRuntimeOutputFingerprint fingerprint =
                source[index];

            if (fingerprint != null)
            {
                result[
                    fingerprint.Coordinate
                ] =
                    fingerprint;
            }
        }

        return result;
    }

    private static List<
        TerrainRuntimeEquivalenceUnexpectedAsset
    > GetUnexpected(
        TerrainRuntimeEquivalenceSnapshot snapshot,
        TerrainRuntimeEquivalenceDatasetKind kind
    )
    {
        List<
            TerrainRuntimeEquivalenceUnexpectedAsset
        > result =
            new List<
                TerrainRuntimeEquivalenceUnexpectedAsset
            >();

        if (snapshot == null)
        {
            return result;
        }

        for (
            int index = 0;
            index < snapshot.UnexpectedAssets.Count;
            index++
        )
        {
            TerrainRuntimeEquivalenceUnexpectedAsset asset =
                snapshot.UnexpectedAssets[index];

            if (
                asset != null
                && asset.DatasetKind == kind
            )
            {
                result.Add(
                    asset
                );
            }
        }

        return result;
    }

    private static void AppendUnexpectedDifferences(
        List<TerrainRuntimeEquivalenceDifference> differences,
        TerrainRuntimeEquivalenceDatasetKind kind,
        IEnumerable<TerrainRuntimeEquivalenceUnexpectedAsset> assets,
        TerrainRuntimeEquivalenceDifferenceType differenceType
    )
    {
        if (assets == null)
        {
            return;
        }

        foreach (
            TerrainRuntimeEquivalenceUnexpectedAsset asset
            in assets
        )
        {
            if (asset == null)
            {
                continue;
            }

            differences.Add(
                new TerrainRuntimeEquivalenceDifference(
                    kind,
                    differenceType,
                    asset.HasCoordinate,
                    asset.Coordinate,
                    differenceType ==
                        TerrainRuntimeEquivalenceDifferenceType
                            .UnexpectedIncremental
                        ? asset.AssetPath
                        : "",
                    differenceType ==
                        TerrainRuntimeEquivalenceDifferenceType
                            .UnexpectedFull
                        ? asset.AssetPath
                        : "",
                    "",
                    "",
                    false,
                    "Generated asset exists outside the current canonical output coordinate/path domain."
                )
            );
        }
    }
}

public static class TerrainRuntimeEquivalenceValidationUtility
{
    public static bool IsBakeValidationSuccessful(
        TerrainRuntimeBakeValidationResult validation
    )
    {
        return
            validation != null
            && (
                validation.Outcome ==
                    TerrainRuntimeBakeValidationOutcome.Passed
                || validation.Outcome ==
                    TerrainRuntimeBakeValidationOutcome
                        .PassedWithWarnings
            );
    }

    public static bool IsFullExecutionCertified(
        TerrainRuntimeBakeValidationResult validation
    )
    {
        if (
            !IsBakeValidationSuccessful(
                validation
            )
            || validation.PipelineResult == null
            || validation.PipelineResult.Mode !=
                TerrainRuntimeBakePipelineMode
                    .RebuildAll
        )
        {
            return false;
        }

        return
            IsFullStageCertified(
                validation.HeightValidation
            )
            && IsFullStageCertified(
                validation.SurfaceValidation
            )
            && IsFullStageCertified(
                validation.CollisionValidation
            );
    }

    public static bool TryValidateMutationAndPlan(
        TerrainRuntimeEquivalenceBaseline baseline,
        TerrainRuntimeEquivalenceSourceIdentity current,
        TerrainRuntimeBakePlan plan,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            baseline == null
            || baseline.SourceIdentity == null
        )
        {
            errorMessage =
                "Capture an equivalence baseline before making the representative edit.";
            return false;
        }

        if (current == null)
        {
            errorMessage =
                "Current source identity is unavailable.";
            return false;
        }

        if (plan == null)
        {
            errorMessage =
                "Current runtime bake plan is unavailable.";
            return false;
        }

        if (plan.IsBlocked)
        {
            errorMessage =
                "Current runtime bake plan is blocked: " +
                plan.BlockReason;
            return false;
        }

        if (!plan.HasWork)
        {
            errorMessage =
                "No runtime work is pending after the representative mutation.";
            return false;
        }

        if (
            !baseline.SourceIdentity
                .CoreTerrainLayoutMatches(
                    current
                )
        )
        {
            errorMessage =
                "Core terrain layout changed after the equivalence baseline. Use a dedicated layout validation rather than Incremental / Full equivalence.";
            return false;
        }

        TerrainRuntimeEquivalenceScenario scenario =
            baseline.Scenario;

        switch (scenario)
        {
            case TerrainRuntimeEquivalenceScenario.SingleLocalEdit:
            case TerrainRuntimeEquivalenceScenario.ModifierMove:
            case TerrainRuntimeEquivalenceScenario.ModifierDelete:
            case TerrainRuntimeEquivalenceScenario.ModifierEnable:
            case TerrainRuntimeEquivalenceScenario.ModifierDisable:
            case TerrainRuntimeEquivalenceScenario.MultipleEditsBeforeOneBake:
            case TerrainRuntimeEquivalenceScenario.MultiTileEdit:
                return
                    ValidateAuthoringScenario(
                        baseline,
                        current,
                        plan,
                        scenario,
                        out errorMessage
                    );

            case TerrainRuntimeEquivalenceScenario.SurfaceSettingsChange:
                return
                    ValidateSurfaceSettingsScenario(
                        baseline,
                        current,
                        plan,
                        out errorMessage
                    );

            case TerrainRuntimeEquivalenceScenario.CollisionResolutionChange:
                return
                    ValidateCollisionSettingsScenario(
                        baseline,
                        current,
                        plan,
                        out errorMessage
                    );

            default:
                errorMessage =
                    "The selected scenario does not use the baseline + normal-bake equivalence workflow.";
                return false;
        }
    }

    public static bool HasConflictingValidationActivity(
        out string errorMessage
    )
    {
        errorMessage = "";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage =
                "Package 10.5 validation must run outside Play Mode.";
            return true;
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            errorMessage =
                "The unified runtime bake pipeline is already running.";
            return true;
        }

        if (
            TerrainRuntimeBakeValidationUtility
                .IsValidationBakeRunning
        )
        {
            errorMessage =
                "A Package 10.1 validation bake is already running.";
            return true;
        }

        if (
            TerrainRuntimeBakeResumeValidationUtility
                .IsRunning
        )
        {
            errorMessage =
                "A Package 10.2 persistence/resume validation is active.";
            return true;
        }

        if (
            TerrainRuntimeInvalidationScenarioRunner
                .AggregationActive
        )
        {
            errorMessage =
                "A Package 10.3 invalidation aggregation session is active.";
            return true;
        }

        if (
            TerrainRuntimeFaultRecoveryScenarioRunner
                .HasActiveFault
            || TerrainRuntimeFaultRecoveryScenarioRunner
                .IsRecoveryRunning
        )
        {
            errorMessage =
                "A Package 10.4 fault/recovery scenario is active.";
            return true;
        }

        if (
            TerrainRuntimeFaultInjectionUtility
                .HasOrphanedValidationSession()
        )
        {
            errorMessage =
                "Package 10.4 validation quarantine requires restoration before Package 10.5 can run.";
            return true;
        }

        if (
            TerrainSurfaceMaskCompiler
                .IsGenerating
        )
        {
            errorMessage =
                "Independent Surface generation is active.";
            return true;
        }

        return false;
    }

    private static bool ValidateAuthoringScenario(
        TerrainRuntimeEquivalenceBaseline baseline,
        TerrainRuntimeEquivalenceSourceIdentity current,
        TerrainRuntimeBakePlan plan,
        TerrainRuntimeEquivalenceScenario scenario,
        out string errorMessage
    )
    {
        errorMessage = "";

        TerrainRuntimeEquivalenceSourceIdentity before =
            baseline.SourceIdentity;

        if (
            string.Equals(
                before.AuthoringSignature,
                current.AuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "The authored terrain identity did not change after the baseline.";
            return false;
        }

        if (
            !string.Equals(
                before.SurfaceSettingsSignature,
                current.SurfaceSettingsSignature,
                StringComparison.Ordinal
            )
            || !string.Equals(
                before.CollisionSettingsSignature,
                current.CollisionSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Surface or Collision settings changed during an authoring-only equivalence scenario.";
            return false;
        }

        if (
            plan.HeightWorkMode !=
                TerrainRuntimeBakeWorkMode.Incremental
            || plan.SurfaceWorkMode !=
                TerrainRuntimeBakeWorkMode.Incremental
            || plan.CollisionWorkMode !=
                TerrainRuntimeBakeWorkMode.Incremental
        )
        {
            errorMessage =
                "The representative authoring scenario did not produce the expected Incremental Height / Surface / Collision plan. Actual: " +
                TerrainRuntimeBakePipelineResult
                    .BuildPlanSummary(
                        plan
                    );
            return false;
        }

        if (
            plan.HeightTileCount <= 0
            || plan.SurfaceTileCount <= 0
            || plan.CollisionChunkCount <= 0
        )
        {
            errorMessage =
                "The incremental plan contains an empty required terrain workset.";
            return false;
        }

        if (
            scenario ==
                TerrainRuntimeEquivalenceScenario.MultiTileEdit
            && plan.HeightTileCount <= 1
        )
        {
            errorMessage =
                "MultiTileEdit requires more than one planned Height tile.";
            return false;
        }

        return true;
    }

    private static bool ValidateSurfaceSettingsScenario(
        TerrainRuntimeEquivalenceBaseline baseline,
        TerrainRuntimeEquivalenceSourceIdentity current,
        TerrainRuntimeBakePlan plan,
        out string errorMessage
    )
    {
        errorMessage = "";

        TerrainRuntimeEquivalenceSourceIdentity before =
            baseline.SourceIdentity;

        if (
            !string.Equals(
                before.AuthoringSignature,
                current.AuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Authored terrain changed during SurfaceSettingsChange.";
            return false;
        }

        if (
            string.Equals(
                before.SurfaceSettingsSignature,
                current.SurfaceSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Surface settings signature did not change after the baseline.";
            return false;
        }

        if (
            !string.Equals(
                before.CollisionSettingsSignature,
                current.CollisionSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Collision settings also changed during SurfaceSettingsChange.";
            return false;
        }

        if (
            plan.HeightWorkMode !=
                TerrainRuntimeBakeWorkMode.None
            || plan.SurfaceWorkMode !=
                TerrainRuntimeBakeWorkMode.Full
            || plan.CollisionWorkMode !=
                TerrainRuntimeBakeWorkMode.None
        )
        {
            errorMessage =
                "SurfaceSettingsChange did not produce Height None / Surface Full / Collision None. Actual: " +
                TerrainRuntimeBakePipelineResult
                    .BuildPlanSummary(
                        plan
                    );
            return false;
        }

        return true;
    }

    private static bool ValidateCollisionSettingsScenario(
        TerrainRuntimeEquivalenceBaseline baseline,
        TerrainRuntimeEquivalenceSourceIdentity current,
        TerrainRuntimeBakePlan plan,
        out string errorMessage
    )
    {
        errorMessage = "";

        TerrainRuntimeEquivalenceSourceIdentity before =
            baseline.SourceIdentity;

        if (
            !string.Equals(
                before.AuthoringSignature,
                current.AuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Authored terrain changed during CollisionResolutionChange.";
            return false;
        }

        if (
            !string.Equals(
                before.SurfaceSettingsSignature,
                current.SurfaceSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Surface settings also changed during CollisionResolutionChange.";
            return false;
        }

        if (
            before.CollisionResolution ==
                current.CollisionResolution
            || string.Equals(
                before.CollisionSettingsSignature,
                current.CollisionSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Collision resolution/settings signature did not change after the baseline.";
            return false;
        }

        if (
            plan.HeightWorkMode !=
                TerrainRuntimeBakeWorkMode.None
            || plan.SurfaceWorkMode !=
                TerrainRuntimeBakeWorkMode.None
            || plan.CollisionWorkMode !=
                TerrainRuntimeBakeWorkMode.Full
        )
        {
            errorMessage =
                "CollisionResolutionChange did not produce Height None / Surface None / Collision Full. Actual: " +
                TerrainRuntimeBakePipelineResult
                    .BuildPlanSummary(
                        plan
                    );
            return false;
        }

        return true;
    }

    private static bool IsFullStageCertified(
        TerrainRuntimeBakeStageValidationResult stage
    )
    {
        return
            stage != null
            && stage.Evaluated
            && stage.StageExecuted
            && stage.Passed
            && stage.ExpectedWorkMode ==
                TerrainRuntimeBakeWorkMode.Full
            && stage.ActualWorkMode ==
                TerrainRuntimeBakeWorkMode.Full
            && stage.ExpectedCount > 0
            && stage.RequestedCount ==
                stage.ExpectedCount
            && stage.SucceededCount ==
                stage.ExpectedCount
            && stage.FailedCount == 0
            && stage.UnprocessedCount == 0;
    }
}
