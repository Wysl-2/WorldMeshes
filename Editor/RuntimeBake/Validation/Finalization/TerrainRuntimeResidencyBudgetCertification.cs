using System;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class TerrainRuntimeResidencyBudgetResult
{
    [SerializeField]
    private TerrainRuntimeValidationStatus terrainBudgetStatus;

    [SerializeField]
    private TerrainSurfaceResidencyGateDecision surfaceGateDecision;

    [SerializeField]
    private long terrainBudgetBytes;

    [SerializeField]
    private long surfaceBudgetBytes;

    [SerializeField]
    private long currentTerrainBytes;

    [SerializeField]
    private long conservativeTerrainUpperBoundBytes;

    [SerializeField]
    private long terrainBudgetHeadroomBytes;

    [SerializeField]
    private long heightActiveGpuBytes;

    [SerializeField]
    private long heightStagingGpuBytes;

    [SerializeField]
    private long heightCurrentSourceBytes;

    [SerializeField]
    private long heightObservedPeakSourceBytes;

    [SerializeField]
    private long heightSourceUpperBoundBytes;

    [SerializeField]
    private long heightConservativeUpperBoundBytes;

    [SerializeField]
    private long surfaceActiveGpuBytes;

    [SerializeField]
    private long surfaceStagingGpuBytes;

    [SerializeField]
    private int surfaceResidentSourceCount;

    [SerializeField]
    private long surfaceCurrentSourceBytes;

    [SerializeField]
    private long surfaceSourceUpperBoundBytes;

    [SerializeField]
    private long surfaceConservativeUpperBoundBytes;

    [SerializeField]
    private int legacyHeightCacheWidth;

    [SerializeField]
    private int legacyHeightCacheHeight;

    [SerializeField]
    private long legacyHeightGpuBytes;

    [SerializeField]
    private long legacyHeightSteadySourceBytes;

    [SerializeField]
    private long legacyHeightTransitionSourceUpperBoundBytes;

    [SerializeField]
    private long legacyHeightConservativeUpperBoundBytes;

    [SerializeField]
    private long heightGpuSavingsBytes;

    [SerializeField]
    private float heightGpuSavingsPercent;

    [SerializeField]
    private long heightUpperBoundSavingsBytes;

    [SerializeField]
    private float heightUpperBoundSavingsPercent;

    [SerializeField]
    private float surfaceSharePercent;

    [SerializeField]
    private bool surfaceIsDominant;

    public TerrainRuntimeValidationStatus TerrainBudgetStatus =>
        terrainBudgetStatus;

    public TerrainSurfaceResidencyGateDecision SurfaceGateDecision =>
        surfaceGateDecision;

    public long TerrainBudgetBytes => terrainBudgetBytes;
    public long SurfaceBudgetBytes => surfaceBudgetBytes;
    public long CurrentTerrainBytes => currentTerrainBytes;

    public long ConservativeTerrainUpperBoundBytes =>
        conservativeTerrainUpperBoundBytes;

    public long TerrainBudgetHeadroomBytes =>
        terrainBudgetHeadroomBytes;

    public long HeightActiveGpuBytes => heightActiveGpuBytes;
    public long HeightStagingGpuBytes => heightStagingGpuBytes;
    public long HeightCurrentSourceBytes => heightCurrentSourceBytes;

    public long HeightObservedPeakSourceBytes =>
        heightObservedPeakSourceBytes;

    public long HeightSourceUpperBoundBytes =>
        heightSourceUpperBoundBytes;

    public long HeightConservativeUpperBoundBytes =>
        heightConservativeUpperBoundBytes;

    public long HeightGpuCacheBytes =>
        heightActiveGpuBytes +
        heightStagingGpuBytes;

    public long HeightCurrentLogicalBytes =>
        HeightGpuCacheBytes +
        heightCurrentSourceBytes;

    public long HeightObservedPeakLogicalBytes =>
        HeightGpuCacheBytes +
        Math.Max(
            heightCurrentSourceBytes,
            heightObservedPeakSourceBytes
        );

    public long SurfaceActiveGpuBytes => surfaceActiveGpuBytes;
    public long SurfaceStagingGpuBytes => surfaceStagingGpuBytes;
    public int SurfaceResidentSourceCount => surfaceResidentSourceCount;
    public long SurfaceCurrentSourceBytes => surfaceCurrentSourceBytes;

    public long SurfaceSourceUpperBoundBytes =>
        surfaceSourceUpperBoundBytes;

    public long SurfaceConservativeUpperBoundBytes =>
        surfaceConservativeUpperBoundBytes;

    public long SurfaceGpuCacheBytes =>
        surfaceActiveGpuBytes +
        surfaceStagingGpuBytes;

    public long SurfaceCurrentLogicalBytes =>
        SurfaceGpuCacheBytes +
        surfaceCurrentSourceBytes;

    public int LegacyHeightCacheWidth => legacyHeightCacheWidth;
    public int LegacyHeightCacheHeight => legacyHeightCacheHeight;
    public long LegacyHeightGpuBytes => legacyHeightGpuBytes;

    public long LegacyHeightSteadySourceBytes =>
        legacyHeightSteadySourceBytes;

    public long LegacyHeightTransitionSourceUpperBoundBytes =>
        legacyHeightTransitionSourceUpperBoundBytes;

    public long LegacyHeightConservativeUpperBoundBytes =>
        legacyHeightConservativeUpperBoundBytes;

    public long HeightGpuSavingsBytes => heightGpuSavingsBytes;
    public float HeightGpuSavingsPercent => heightGpuSavingsPercent;

    public long HeightUpperBoundSavingsBytes =>
        heightUpperBoundSavingsBytes;

    public float HeightUpperBoundSavingsPercent =>
        heightUpperBoundSavingsPercent;

    public float SurfaceSharePercent => surfaceSharePercent;
    public bool SurfaceIsDominant => surfaceIsDominant;

    public bool TerrainBudgetConfigured =>
        terrainBudgetBytes > 0L;

    public bool SurfaceBudgetConfigured =>
        surfaceBudgetBytes > 0L;

    internal TerrainRuntimeResidencyBudgetResult(
        TerrainRuntimeValidationStatus terrainBudgetStatus,
        TerrainSurfaceResidencyGateDecision surfaceGateDecision,
        long terrainBudgetBytes,
        long surfaceBudgetBytes,
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        long heightGpuSavingsBytes,
        float heightGpuSavingsPercent,
        long heightUpperBoundSavingsBytes,
        float heightUpperBoundSavingsPercent,
        float surfaceSharePercent,
        bool surfaceIsDominant
    )
    {
        this.terrainBudgetStatus = terrainBudgetStatus;
        this.surfaceGateDecision = surfaceGateDecision;
        this.terrainBudgetBytes = terrainBudgetBytes;
        this.surfaceBudgetBytes = surfaceBudgetBytes;
        currentTerrainBytes = snapshot.CurrentTerrainLogicalBytes;
        conservativeTerrainUpperBoundBytes =
            snapshot.ConservativeTerrainUpperBoundBytes;

        terrainBudgetHeadroomBytes =
            terrainBudgetBytes > 0L
                ? terrainBudgetBytes -
                    conservativeTerrainUpperBoundBytes
                : 0L;

        heightActiveGpuBytes = snapshot.HeightActiveGpuBytes;
        heightStagingGpuBytes = snapshot.HeightStagingGpuBytes;
        heightCurrentSourceBytes = snapshot.HeightCurrentSourceBytes;
        heightObservedPeakSourceBytes =
            snapshot.HeightObservedPeakSourceBytes;
        heightSourceUpperBoundBytes =
            snapshot.HeightSourceUpperBoundBytes;
        heightConservativeUpperBoundBytes =
            snapshot.HeightConservativeUpperBoundBytes;

        surfaceActiveGpuBytes =
            snapshot.Surface.EstimatedActiveGpuBytes;
        surfaceStagingGpuBytes =
            snapshot.Surface.EstimatedStagingGpuBytes;
        surfaceResidentSourceCount =
            snapshot.Surface.ResidentSourceCount;
        surfaceCurrentSourceBytes =
            snapshot.Surface.EstimatedCurrentSourceBytes;
        surfaceSourceUpperBoundBytes =
            snapshot.Surface.EstimatedSourceUpperBoundBytes;
        surfaceConservativeUpperBoundBytes =
            snapshot.Surface.EstimatedConservativeUpperBoundBytes;

        legacyHeightCacheWidth = snapshot.LegacyHeight.CacheWidth;
        legacyHeightCacheHeight = snapshot.LegacyHeight.CacheHeight;
        legacyHeightGpuBytes =
            snapshot.LegacyHeight.EstimatedGpuCacheBytes;
        legacyHeightSteadySourceBytes =
            snapshot.LegacyHeight.EstimatedSteadySourceBytes;
        legacyHeightTransitionSourceUpperBoundBytes =
            snapshot.LegacyHeight.EstimatedTransitionSourceUpperBoundBytes;
        legacyHeightConservativeUpperBoundBytes =
            snapshot.LegacyHeight.EstimatedConservativeUpperBoundBytes;

        this.heightGpuSavingsBytes = heightGpuSavingsBytes;
        this.heightGpuSavingsPercent = heightGpuSavingsPercent;
        this.heightUpperBoundSavingsBytes = heightUpperBoundSavingsBytes;
        this.heightUpperBoundSavingsPercent =
            heightUpperBoundSavingsPercent;
        this.surfaceSharePercent = surfaceSharePercent;
        this.surfaceIsDominant = surfaceIsDominant;
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "Runtime Terrain Residency Budget Evaluation"
        );
        builder.AppendLine();
        builder.AppendLine(
            "Accounting: raw/logical texture payload estimates; physical GPU-driver allocation is not measured."
        );
        builder.AppendLine();

        builder.AppendLine("Height:");
        AppendBytes(builder, "  Active GPU", heightActiveGpuBytes);
        AppendBytes(builder, "  Staging GPU", heightStagingGpuBytes);
        AppendBytes(builder, "  Current Source", heightCurrentSourceBytes);
        AppendBytes(builder, "  Observed Source Peak", heightObservedPeakSourceBytes);
        AppendBytes(builder, "  Source Upper Bound", heightSourceUpperBoundBytes);
        AppendBytes(builder, "  Current Logical Residency", HeightCurrentLogicalBytes);
        AppendBytes(builder, "  Observed Peak Logical Residency", HeightObservedPeakLogicalBytes);
        AppendBytes(builder, "  Conservative Upper Bound", heightConservativeUpperBoundBytes);
        builder.AppendLine();

        builder.AppendLine("Legacy Height Baseline:");
        builder.AppendLine(
            $"  Cache Grid: {legacyHeightCacheWidth} x {legacyHeightCacheHeight}"
        );
        AppendBytes(builder, "  GPU Cache Payload", legacyHeightGpuBytes);
        AppendBytes(builder, "  Steady Source", legacyHeightSteadySourceBytes);
        AppendBytes(
            builder,
            "  Transition Source Upper Bound",
            legacyHeightTransitionSourceUpperBoundBytes
        );
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            legacyHeightConservativeUpperBoundBytes
        );
        builder.AppendLine(
            $"  GPU Cache Reduction: {FormatSignedBytes(heightGpuSavingsBytes)} ({heightGpuSavingsPercent:N2}%)"
        );
        builder.AppendLine(
            $"  Conservative Reduction: {FormatSignedBytes(heightUpperBoundSavingsBytes)} ({heightUpperBoundSavingsPercent:N2}%)"
        );
        builder.AppendLine();

        builder.AppendLine("Surface:");
        AppendBytes(builder, "  Active GPU", surfaceActiveGpuBytes);
        AppendBytes(builder, "  Staging GPU", surfaceStagingGpuBytes);
        builder.AppendLine(
            $"  Resident Source Pages: {surfaceResidentSourceCount:N0}"
        );
        AppendBytes(builder, "  Current Source", surfaceCurrentSourceBytes);
        AppendBytes(builder, "  Source Upper Bound", surfaceSourceUpperBoundBytes);
        AppendBytes(builder, "  Current Logical Residency", SurfaceCurrentLogicalBytes);
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            surfaceConservativeUpperBoundBytes
        );
        builder.AppendLine(
            $"  Terrain Upper-Bound Share: {surfaceSharePercent:N2}%"
        );
        builder.AppendLine(
            "  Dominant Residency Domain: " +
            (surfaceIsDominant ? "Surface" : "Height")
        );
        builder.AppendLine();

        builder.AppendLine("Terrain:");
        AppendBytes(builder, "  Current", currentTerrainBytes);
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            conservativeTerrainUpperBoundBytes
        );

        if (TerrainBudgetConfigured)
        {
            AppendBytes(builder, "  Budget", terrainBudgetBytes);
            builder.AppendLine(
                "  Headroom / Excess: " +
                FormatSignedBytes(terrainBudgetHeadroomBytes)
            );
        }
        else
        {
            builder.AppendLine("  Budget: Not configured");
        }

        if (SurfaceBudgetConfigured)
        {
            AppendBytes(builder, "  Surface Budget", surfaceBudgetBytes);
        }
        else
        {
            builder.AppendLine("  Surface Budget: Not configured");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Terrain Budget: " +
            terrainBudgetStatus
        );
        builder.AppendLine(
            "Surface Gate: " +
            FormatSurfaceGate(surfaceGateDecision)
        );

        return builder.ToString().TrimEnd();
    }

    private static void AppendBytes(
        StringBuilder builder,
        string label,
        long bytes
    )
    {
        builder.AppendLine(
            label + ": " +
            FormatBytes(bytes)
        );
    }

    private static string FormatBytes(
        long bytes
    )
    {
        return
            (
                bytes /
                (1024d * 1024d)
            ).ToString("N2") +
            " MiB";
    }

    private static string FormatSignedBytes(
        long bytes
    )
    {
        string sign =
            bytes > 0L
                ? "+"
                : "";

        return
            sign +
            FormatBytes(bytes);
    }

    private static string FormatSurfaceGate(
        TerrainSurfaceResidencyGateDecision decision
    )
    {
        switch (decision)
        {
            case TerrainSurfaceResidencyGateDecision
                .NotRequiredForTargetConfiguration:
                return
                    "Multiresolution Surface Not Required For Target Configuration";

            case TerrainSurfaceResidencyGateDecision
                .RequiredForTargetConfiguration:
                return
                    "Multiresolution Surface Required For Target Configuration";

            default:
                return "Not Evaluated";
        }
    }
}

public static class TerrainRuntimeResidencyBudgetUtility
{
    public static TerrainRuntimeResidencyBudgetResult Evaluate(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        long terrainBudgetBytes,
        long surfaceBudgetBytes
    )
    {
        terrainBudgetBytes =
            Math.Max(
                0L,
                terrainBudgetBytes
            );

        surfaceBudgetBytes =
            Math.Max(
                0L,
                surfaceBudgetBytes
            );

        TerrainRuntimeValidationStatus terrainStatus =
            TerrainRuntimeValidationStatus.NotRun;

        if (terrainBudgetBytes > 0L)
        {
            terrainStatus =
                snapshot.ConservativeTerrainUpperBoundBytes <=
                    terrainBudgetBytes
                    ? TerrainRuntimeValidationStatus.Passed
                    : TerrainRuntimeValidationStatus.Failed;
        }

        TerrainSurfaceResidencyGateDecision surfaceDecision =
            EvaluateSurfaceGate(
                snapshot,
                terrainBudgetBytes,
                surfaceBudgetBytes
            );

        long heightGpuSavingsBytes =
            snapshot.LegacyHeight.EstimatedGpuCacheBytes -
            snapshot.HeightGpuCacheBytes;

        float heightGpuSavingsPercent =
            CalculateSavingsPercent(
                snapshot.LegacyHeight.EstimatedGpuCacheBytes,
                snapshot.HeightGpuCacheBytes
            );

        long heightUpperBoundSavingsBytes =
            snapshot.LegacyHeight.EstimatedConservativeUpperBoundBytes -
            snapshot.HeightConservativeUpperBoundBytes;

        float heightUpperBoundSavingsPercent =
            CalculateSavingsPercent(
                snapshot.LegacyHeight.EstimatedConservativeUpperBoundBytes,
                snapshot.HeightConservativeUpperBoundBytes
            );

        float surfaceSharePercent =
            snapshot.ConservativeTerrainUpperBoundBytes > 0L
                ? (float)(
                    snapshot.Surface.EstimatedConservativeUpperBoundBytes /
                    (double)snapshot.ConservativeTerrainUpperBoundBytes *
                    100d
                )
                : 0f;

        bool surfaceIsDominant =
            snapshot.Surface.EstimatedConservativeUpperBoundBytes >
            snapshot.HeightConservativeUpperBoundBytes;

        return
            new TerrainRuntimeResidencyBudgetResult(
                terrainStatus,
                surfaceDecision,
                terrainBudgetBytes,
                surfaceBudgetBytes,
                snapshot,
                heightGpuSavingsBytes,
                heightGpuSavingsPercent,
                heightUpperBoundSavingsBytes,
                heightUpperBoundSavingsPercent,
                surfaceSharePercent,
                surfaceIsDominant
            );
    }

    private static TerrainSurfaceResidencyGateDecision
        EvaluateSurfaceGate(
            TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
            long terrainBudgetBytes,
            long surfaceBudgetBytes
        )
    {
        if (
            surfaceBudgetBytes > 0L
            &&
            snapshot.Surface.EstimatedConservativeUpperBoundBytes >
                surfaceBudgetBytes
        )
        {
            return
                TerrainSurfaceResidencyGateDecision
                    .RequiredForTargetConfiguration;
        }

        if (terrainBudgetBytes > 0L)
        {
            if (
                snapshot.HeightConservativeUpperBoundBytes >
                    terrainBudgetBytes
            )
            {
                return
                    TerrainSurfaceResidencyGateDecision
                        .NotEvaluated;
            }

            if (
                snapshot.ConservativeTerrainUpperBoundBytes >
                    terrainBudgetBytes
            )
            {
                return
                    TerrainSurfaceResidencyGateDecision
                        .RequiredForTargetConfiguration;
            }

            return
                TerrainSurfaceResidencyGateDecision
                    .NotRequiredForTargetConfiguration;
        }

        if (surfaceBudgetBytes > 0L)
        {
            return
                TerrainSurfaceResidencyGateDecision
                    .NotRequiredForTargetConfiguration;
        }

        return
            TerrainSurfaceResidencyGateDecision
                .NotEvaluated;
    }

    private static float CalculateSavingsPercent(
        long legacyBytes,
        long currentBytes
    )
    {
        if (legacyBytes <= 0L)
        {
            return 0f;
        }

        return
            (float)(
                (
                    legacyBytes -
                    currentBytes
                ) /
                (double)legacyBytes *
                100d
            );
    }
}
