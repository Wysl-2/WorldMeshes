using System;
using System.Text;

public enum TerrainRuntimeCapacityStatus
{
    NotConfigured,
    WithinBudget,
    ExceedsBudget
}

public enum TerrainSurfaceScalingRecommendation
{
    NotEvaluated,
    NotRequiredForTarget,
    RecommendedForTarget
}

public sealed class TerrainRuntimeCapacityResult
{
    public TerrainRuntimeCapacityStatus TerrainStatus { get; }
    public TerrainRuntimeCapacityStatus HeightStatus { get; }
    public TerrainRuntimeCapacityStatus SurfaceStatus { get; }

    public TerrainSurfaceScalingRecommendation
        SurfaceScalingRecommendation { get; }

    public string SurfaceScalingReason { get; }

    public long TerrainBudgetBytes { get; }
    public long SurfaceBudgetBytes { get; }
    public long CurrentTerrainBytes { get; }

    public long ConservativeTerrainUpperBoundBytes { get; }
    public long TerrainBudgetHeadroomBytes { get; }

    public long HeightActiveGpuBytes { get; }
    public long HeightStagingGpuBytes { get; }
    public long HeightCurrentSourceBytes { get; }
    public long HeightObservedPeakSourceBytes { get; }
    public long HeightSourceUpperBoundBytes { get; }
    public long HeightConservativeUpperBoundBytes { get; }

    public long HeightGpuCacheBytes =>
        HeightActiveGpuBytes +
        HeightStagingGpuBytes;

    public long HeightCurrentLogicalBytes =>
        HeightGpuCacheBytes +
        HeightCurrentSourceBytes;

    public long HeightObservedPeakLogicalBytes =>
        HeightGpuCacheBytes +
        Math.Max(
            HeightCurrentSourceBytes,
            HeightObservedPeakSourceBytes
        );

    public long HeightBudgetHeadroomBytes { get; }

    public int SurfaceLodCount { get; }
    public long SurfaceActiveGpuBytes { get; }
    public long SurfaceStagingGpuBytes { get; }
    public int SurfaceTransientSourceCount { get; }
    public long SurfaceCurrentSourceBytes { get; }
    public long SurfaceObservedPeakSourceBytes { get; }
    public long SurfaceSourceUpperBoundBytes { get; }
    public long SurfaceConservativeUpperBoundBytes { get; }

    public long SurfaceGpuCacheBytes =>
        SurfaceActiveGpuBytes +
        SurfaceStagingGpuBytes;

    public long SurfaceCurrentLogicalBytes =>
        SurfaceGpuCacheBytes +
        SurfaceCurrentSourceBytes;

    public long SurfaceObservedPeakLogicalBytes =>
        SurfaceGpuCacheBytes +
        Math.Max(
            SurfaceCurrentSourceBytes,
            SurfaceObservedPeakSourceBytes
        );

    public long SurfaceBudgetHeadroomBytes { get; }

    public int LegacyHeightCacheWidth { get; }
    public int LegacyHeightCacheHeight { get; }
    public long LegacyHeightGpuBytes { get; }
    public long LegacyHeightSteadySourceBytes { get; }
    public long LegacyHeightTransitionSourceUpperBoundBytes { get; }
    public long LegacyHeightConservativeUpperBoundBytes { get; }

    public long HeightGpuSavingsBytes { get; }
    public float HeightGpuSavingsPercent { get; }
    public long HeightUpperBoundSavingsBytes { get; }
    public float HeightUpperBoundSavingsPercent { get; }

    public int LegacySurfaceCacheWidth { get; }
    public int LegacySurfaceCacheHeight { get; }
    public long LegacySurfaceNativePageBytes { get; }
    public long LegacySurfaceGpuBytes { get; }
    public long LegacySurfaceSteadySourceBytes { get; }
    public long LegacySurfaceTransitionSourceUpperBoundBytes { get; }
    public long LegacySurfaceConservativeUpperBoundBytes { get; }

    public long SurfaceGpuSavingsBytes { get; }
    public float SurfaceGpuSavingsPercent { get; }
    public long SurfaceUpperBoundSavingsBytes { get; }
    public float SurfaceUpperBoundSavingsPercent { get; }

    public float SurfaceSharePercent { get; }
    public bool SurfaceIsDominant { get; }

    public bool TerrainBudgetConfigured =>
        TerrainBudgetBytes > 0L;

    public bool SurfaceBudgetConfigured =>
        SurfaceBudgetBytes > 0L;

    internal TerrainRuntimeCapacityResult(
        TerrainRuntimeCapacityStatus terrainStatus,
        TerrainRuntimeCapacityStatus heightStatus,
        TerrainRuntimeCapacityStatus surfaceStatus,
        TerrainSurfaceScalingRecommendation
            surfaceScalingRecommendation,
        string surfaceScalingReason,
        long terrainBudgetBytes,
        long surfaceBudgetBytes,
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        long heightGpuSavingsBytes,
        float heightGpuSavingsPercent,
        long heightUpperBoundSavingsBytes,
        float heightUpperBoundSavingsPercent,
        long surfaceGpuSavingsBytes,
        float surfaceGpuSavingsPercent,
        long surfaceUpperBoundSavingsBytes,
        float surfaceUpperBoundSavingsPercent,
        float surfaceSharePercent,
        bool surfaceIsDominant
    )
    {
        TerrainStatus = terrainStatus;
        HeightStatus = heightStatus;
        SurfaceStatus = surfaceStatus;
        SurfaceScalingRecommendation =
            surfaceScalingRecommendation;
        SurfaceScalingReason =
            surfaceScalingReason ?? string.Empty;

        TerrainBudgetBytes = terrainBudgetBytes;
        SurfaceBudgetBytes = surfaceBudgetBytes;
        CurrentTerrainBytes =
            snapshot.CurrentTerrainLogicalBytes;
        ConservativeTerrainUpperBoundBytes =
            snapshot.ConservativeTerrainUpperBoundBytes;

        TerrainBudgetHeadroomBytes =
            terrainBudgetBytes > 0L
                ? terrainBudgetBytes -
                    ConservativeTerrainUpperBoundBytes
                : 0L;

        HeightActiveGpuBytes = snapshot.HeightActiveGpuBytes;
        HeightStagingGpuBytes = snapshot.HeightStagingGpuBytes;
        HeightCurrentSourceBytes = snapshot.HeightCurrentSourceBytes;
        HeightObservedPeakSourceBytes =
            snapshot.HeightObservedPeakSourceBytes;
        HeightSourceUpperBoundBytes =
            snapshot.HeightSourceUpperBoundBytes;
        HeightConservativeUpperBoundBytes =
            snapshot.HeightConservativeUpperBoundBytes;

        HeightBudgetHeadroomBytes =
            terrainBudgetBytes > 0L
                ? terrainBudgetBytes -
                    HeightConservativeUpperBoundBytes
                : 0L;

        SurfaceLodCount =
            snapshot.Surface.LodCount;
        SurfaceActiveGpuBytes =
            snapshot.Surface.EstimatedActiveGpuBytes;
        SurfaceStagingGpuBytes =
            snapshot.Surface.EstimatedStagingGpuBytes;
        SurfaceTransientSourceCount =
            snapshot.Surface.TransientSourceCount;
        SurfaceCurrentSourceBytes =
            snapshot.Surface.EstimatedCurrentSourceBytes;
        SurfaceObservedPeakSourceBytes =
            snapshot.Surface.EstimatedObservedPeakSourceBytes;
        SurfaceSourceUpperBoundBytes =
            snapshot.Surface.EstimatedSourceUpperBoundBytes;
        SurfaceConservativeUpperBoundBytes =
            snapshot.Surface.EstimatedConservativeUpperBoundBytes;

        SurfaceBudgetHeadroomBytes =
            surfaceBudgetBytes > 0L
                ? surfaceBudgetBytes -
                    SurfaceConservativeUpperBoundBytes
                : 0L;

        LegacyHeightCacheWidth = snapshot.LegacyHeight.CacheWidth;
        LegacyHeightCacheHeight = snapshot.LegacyHeight.CacheHeight;
        LegacyHeightGpuBytes =
            snapshot.LegacyHeight.EstimatedGpuCacheBytes;
        LegacyHeightSteadySourceBytes =
            snapshot.LegacyHeight.EstimatedSteadySourceBytes;
        LegacyHeightTransitionSourceUpperBoundBytes =
            snapshot.LegacyHeight.EstimatedTransitionSourceUpperBoundBytes;
        LegacyHeightConservativeUpperBoundBytes =
            snapshot.LegacyHeight.EstimatedConservativeUpperBoundBytes;

        HeightGpuSavingsBytes = heightGpuSavingsBytes;
        HeightGpuSavingsPercent = heightGpuSavingsPercent;
        HeightUpperBoundSavingsBytes = heightUpperBoundSavingsBytes;
        HeightUpperBoundSavingsPercent =
            heightUpperBoundSavingsPercent;

        LegacySurfaceCacheWidth =
            snapshot.LegacySurface.CacheWidth;
        LegacySurfaceCacheHeight =
            snapshot.LegacySurface.CacheHeight;
        LegacySurfaceNativePageBytes =
            snapshot.LegacySurface.NativePageBytes;
        LegacySurfaceGpuBytes =
            snapshot.LegacySurface.EstimatedGpuCacheBytes;
        LegacySurfaceSteadySourceBytes =
            snapshot.LegacySurface.EstimatedSteadySourceBytes;
        LegacySurfaceTransitionSourceUpperBoundBytes =
            snapshot.LegacySurface
                .EstimatedTransitionSourceUpperBoundBytes;
        LegacySurfaceConservativeUpperBoundBytes =
            snapshot.LegacySurface
                .EstimatedConservativeUpperBoundBytes;

        SurfaceGpuSavingsBytes = surfaceGpuSavingsBytes;
        SurfaceGpuSavingsPercent = surfaceGpuSavingsPercent;
        SurfaceUpperBoundSavingsBytes =
            surfaceUpperBoundSavingsBytes;
        SurfaceUpperBoundSavingsPercent =
            surfaceUpperBoundSavingsPercent;

        SurfaceSharePercent = surfaceSharePercent;
        SurfaceIsDominant = surfaceIsDominant;
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "Runtime Terrain Capacity Analysis"
        );
        builder.AppendLine();
        builder.AppendLine(
            "Accounting: raw/logical texture payload estimates; physical GPU-driver allocation is not measured."
        );
        builder.AppendLine();

        builder.AppendLine("Height:");
        AppendBytes(builder, "  Active GPU", HeightActiveGpuBytes);
        AppendBytes(builder, "  Staging GPU", HeightStagingGpuBytes);
        AppendBytes(builder, "  Current Source", HeightCurrentSourceBytes);
        AppendBytes(
            builder,
            "  Observed Source Peak",
            HeightObservedPeakSourceBytes
        );
        AppendBytes(
            builder,
            "  Source Upper Bound",
            HeightSourceUpperBoundBytes
        );
        AppendBytes(
            builder,
            "  Current Logical Residency",
            HeightCurrentLogicalBytes
        );
        AppendBytes(
            builder,
            "  Observed Peak Logical Residency",
            HeightObservedPeakLogicalBytes
        );
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            HeightConservativeUpperBoundBytes
        );
        AppendTarget(
            builder,
            "  Target",
            TerrainBudgetConfigured,
            TerrainBudgetBytes
        );
        AppendHeadroom(
            builder,
            "  Headroom / Excess",
            TerrainBudgetConfigured,
            HeightBudgetHeadroomBytes
        );
        builder.AppendLine(
            "  Height Target: " +
            FormatStatus(HeightStatus)
        );
        builder.AppendLine();

        builder.AppendLine("Legacy Height Baseline:");
        builder.AppendLine(
            $"  Cache Grid: {LegacyHeightCacheWidth} x {LegacyHeightCacheHeight}"
        );
        AppendBytes(
            builder,
            "  GPU Cache Payload",
            LegacyHeightGpuBytes
        );
        AppendBytes(
            builder,
            "  Steady Source",
            LegacyHeightSteadySourceBytes
        );
        AppendBytes(
            builder,
            "  Transition Source Upper Bound",
            LegacyHeightTransitionSourceUpperBoundBytes
        );
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            LegacyHeightConservativeUpperBoundBytes
        );
        builder.AppendLine(
            $"  GPU Cache Reduction: {FormatSignedBytes(HeightGpuSavingsBytes)} ({HeightGpuSavingsPercent:N2}%)"
        );
        builder.AppendLine(
            $"  Conservative Reduction: {FormatSignedBytes(HeightUpperBoundSavingsBytes)} ({HeightUpperBoundSavingsPercent:N2}%)"
        );
        builder.AppendLine();

        builder.AppendLine("Surface:");
        builder.AppendLine(
            $"  LOD States: {SurfaceLodCount:N0}"
        );
        AppendBytes(builder, "  Active GPU", SurfaceActiveGpuBytes);
        AppendBytes(builder, "  Staging GPU", SurfaceStagingGpuBytes);
        builder.AppendLine(
            $"  Transient Source Pages: {SurfaceTransientSourceCount:N0}"
        );
        AppendBytes(
            builder,
            "  Current Source",
            SurfaceCurrentSourceBytes
        );
        AppendBytes(
            builder,
            "  Observed Source Peak",
            SurfaceObservedPeakSourceBytes
        );
        AppendBytes(
            builder,
            "  Source Upper Bound",
            SurfaceSourceUpperBoundBytes
        );
        AppendBytes(
            builder,
            "  Current Logical Residency",
            SurfaceCurrentLogicalBytes
        );
        AppendBytes(
            builder,
            "  Observed Peak Logical Residency",
            SurfaceObservedPeakLogicalBytes
        );
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            SurfaceConservativeUpperBoundBytes
        );
        AppendTarget(
            builder,
            "  Target",
            SurfaceBudgetConfigured,
            SurfaceBudgetBytes
        );
        AppendHeadroom(
            builder,
            "  Headroom / Excess",
            SurfaceBudgetConfigured,
            SurfaceBudgetHeadroomBytes
        );
        builder.AppendLine(
            "  Surface Target: " +
            FormatStatus(SurfaceStatus)
        );
        builder.AppendLine(
            $"  Terrain Upper-Bound Share: {SurfaceSharePercent:N2}%"
        );
        builder.AppendLine(
            "  Dominant Component: " +
            (SurfaceIsDominant ? "Surface" : "Height")
        );
        builder.AppendLine();

        builder.AppendLine("Legacy Surface Baseline:");
        builder.AppendLine(
            $"  Cache Grid: {LegacySurfaceCacheWidth} x {LegacySurfaceCacheHeight}"
        );
        AppendBytes(
            builder,
            "  Native Page Payload",
            LegacySurfaceNativePageBytes
        );
        AppendBytes(
            builder,
            "  GPU Cache Payload",
            LegacySurfaceGpuBytes
        );
        AppendBytes(
            builder,
            "  Steady Source",
            LegacySurfaceSteadySourceBytes
        );
        AppendBytes(
            builder,
            "  Transition Source Upper Bound",
            LegacySurfaceTransitionSourceUpperBoundBytes
        );
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            LegacySurfaceConservativeUpperBoundBytes
        );
        builder.AppendLine(
            $"  GPU Cache Reduction: {FormatSignedBytes(SurfaceGpuSavingsBytes)} ({SurfaceGpuSavingsPercent:N2}%)"
        );
        builder.AppendLine(
            $"  Conservative Reduction: {FormatSignedBytes(SurfaceUpperBoundSavingsBytes)} ({SurfaceUpperBoundSavingsPercent:N2}%)"
        );
        builder.AppendLine();

        builder.AppendLine("Terrain:");
        AppendBytes(builder, "  Current", CurrentTerrainBytes);
        AppendBytes(
            builder,
            "  Conservative Upper Bound",
            ConservativeTerrainUpperBoundBytes
        );
        AppendTarget(
            builder,
            "  Target",
            TerrainBudgetConfigured,
            TerrainBudgetBytes
        );
        AppendHeadroom(
            builder,
            "  Headroom / Excess",
            TerrainBudgetConfigured,
            TerrainBudgetHeadroomBytes
        );
        builder.AppendLine(
            "  Combined Terrain: " +
            FormatStatus(TerrainStatus)
        );
        builder.AppendLine(
            "  Surface Scaling: " +
            FormatRecommendation(
                SurfaceScalingRecommendation
            )
        );

        if (!string.IsNullOrEmpty(SurfaceScalingReason))
        {
            builder.AppendLine(
                "  Recommendation: " +
                SurfaceScalingReason
            );
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatStatus(
        TerrainRuntimeCapacityStatus status
    )
    {
        switch (status)
        {
            case TerrainRuntimeCapacityStatus.WithinBudget:
                return "Within Budget";

            case TerrainRuntimeCapacityStatus.ExceedsBudget:
                return "Exceeds Target";

            default:
                return "Not Configured";
        }
    }

    public static string FormatRecommendation(
        TerrainSurfaceScalingRecommendation recommendation
    )
    {
        switch (recommendation)
        {
            case TerrainSurfaceScalingRecommendation
                .NotRequiredForTarget:
                return "Not Required For Target";

            case TerrainSurfaceScalingRecommendation
                .RecommendedForTarget:
                return "Recommended For Target";

            default:
                return "Not Evaluated";
        }
    }

    private static void AppendTarget(
        StringBuilder builder,
        string label,
        bool configured,
        long bytes
    )
    {
        if (configured)
        {
            AppendBytes(builder, label, bytes);
        }
        else
        {
            builder.AppendLine(
                label + ": Not configured"
            );
        }
    }

    private static void AppendHeadroom(
        StringBuilder builder,
        string label,
        bool configured,
        long bytes
    )
    {
        builder.AppendLine(
            label + ": " +
            (
                configured
                    ? FormatSignedBytes(bytes)
                    : "Not configured"
            )
        );
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
        return
            (bytes > 0L ? "+" : "") +
            FormatBytes(bytes);
    }
}

public static class TerrainRuntimeCapacityUtility
{
    public static TerrainRuntimeCapacityResult Evaluate(
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

        TerrainRuntimeCapacityStatus terrainStatus =
            EvaluateStatus(
                snapshot.ConservativeTerrainUpperBoundBytes,
                terrainBudgetBytes
            );

        TerrainRuntimeCapacityStatus heightStatus =
            EvaluateStatus(
                snapshot.HeightConservativeUpperBoundBytes,
                terrainBudgetBytes
            );

        TerrainRuntimeCapacityStatus surfaceStatus =
            EvaluateStatus(
                snapshot.Surface.EstimatedConservativeUpperBoundBytes,
                surfaceBudgetBytes
            );

        TerrainSurfaceScalingRecommendation recommendation;
        string recommendationReason;

        EvaluateSurfaceScalingRecommendation(
            snapshot,
            terrainBudgetBytes,
            surfaceBudgetBytes,
            out recommendation,
            out recommendationReason
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

        long surfaceGpuSavingsBytes =
            snapshot.LegacySurface.EstimatedGpuCacheBytes -
            snapshot.Surface.EstimatedGpuCacheBytes;

        float surfaceGpuSavingsPercent =
            CalculateSavingsPercent(
                snapshot.LegacySurface.EstimatedGpuCacheBytes,
                snapshot.Surface.EstimatedGpuCacheBytes
            );

        long surfaceUpperBoundSavingsBytes =
            snapshot.LegacySurface
                .EstimatedConservativeUpperBoundBytes -
            snapshot.Surface
                .EstimatedConservativeUpperBoundBytes;

        float surfaceUpperBoundSavingsPercent =
            CalculateSavingsPercent(
                snapshot.LegacySurface
                    .EstimatedConservativeUpperBoundBytes,
                snapshot.Surface
                    .EstimatedConservativeUpperBoundBytes
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
            new TerrainRuntimeCapacityResult(
                terrainStatus,
                heightStatus,
                surfaceStatus,
                recommendation,
                recommendationReason,
                terrainBudgetBytes,
                surfaceBudgetBytes,
                snapshot,
                heightGpuSavingsBytes,
                heightGpuSavingsPercent,
                heightUpperBoundSavingsBytes,
                heightUpperBoundSavingsPercent,
                surfaceGpuSavingsBytes,
                surfaceGpuSavingsPercent,
                surfaceUpperBoundSavingsBytes,
                surfaceUpperBoundSavingsPercent,
                surfaceSharePercent,
                surfaceIsDominant
            );
    }

    private static TerrainRuntimeCapacityStatus EvaluateStatus(
        long conservativeBytes,
        long budgetBytes
    )
    {
        if (budgetBytes <= 0L)
        {
            return
                TerrainRuntimeCapacityStatus.NotConfigured;
        }

        return
            conservativeBytes <= budgetBytes
                ? TerrainRuntimeCapacityStatus.WithinBudget
                : TerrainRuntimeCapacityStatus.ExceedsBudget;
    }

    private static void EvaluateSurfaceScalingRecommendation(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        long terrainBudgetBytes,
        long surfaceBudgetBytes,
        out TerrainSurfaceScalingRecommendation recommendation,
        out string reason
    )
    {
        if (
            surfaceBudgetBytes > 0L
            && snapshot.Surface.EstimatedConservativeUpperBoundBytes >
                surfaceBudgetBytes
        )
        {
            recommendation =
                TerrainSurfaceScalingRecommendation
                    .RecommendedForTarget;

            reason =
                "Surface residency exceeds the configured Surface target. " +
                "Further Surface scaling is recommended for this target.";

            return;
        }

        if (terrainBudgetBytes > 0L)
        {
            if (
                snapshot.HeightConservativeUpperBoundBytes >
                    terrainBudgetBytes
            )
            {
                recommendation =
                    TerrainSurfaceScalingRecommendation.NotEvaluated;

                reason =
                    "Height residency alone exceeds the terrain target, so " +
                    "Surface scaling is not the limiting capacity decision " +
                    "for this configuration.";

                return;
            }

            if (
                snapshot.ConservativeTerrainUpperBoundBytes >
                    terrainBudgetBytes
            )
            {
                recommendation =
                    TerrainSurfaceScalingRecommendation
                        .RecommendedForTarget;

                reason =
                    "Height residency fits the terrain target, but combined " +
                    "Height and Surface residency exceeds it. Further Surface " +
                    "scaling is recommended for this target.";

                return;
            }

            recommendation =
                TerrainSurfaceScalingRecommendation
                    .NotRequiredForTarget;

            reason =
                "The conservative combined terrain residency fits the " +
                "configured terrain target.";

            return;
        }

        if (surfaceBudgetBytes > 0L)
        {
            recommendation =
                TerrainSurfaceScalingRecommendation
                    .NotRequiredForTarget;

            reason =
                "Surface residency fits the configured Surface target.";

            return;
        }

        recommendation =
            TerrainSurfaceScalingRecommendation.NotEvaluated;

        reason =
            "Configure a terrain or Surface target to evaluate whether " +
            "additional Surface scaling is useful.";
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
