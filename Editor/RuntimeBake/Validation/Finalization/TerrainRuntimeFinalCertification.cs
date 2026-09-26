using System;
using System.Text;
using UnityEditor;

public enum TerrainSurfaceResidencyGateDecision
{
    NotEvaluated,
    NotRequiredForTargetConfiguration,
    RequiredForTargetConfiguration
}

public readonly struct TerrainRuntimeFinalCertificationManualEvidence
{
    public bool FullAndIncrementalBakeRegressionConfirmed { get; }
    public bool StreamingRepairAndMigrationConfirmed { get; }
    public bool CancellationResumeRegressionConfirmed { get; }
    public bool FaultRecoveryRegressionConfirmed { get; }
    public bool SceneSynchronizationRegressionConfirmed { get; }
    public bool LifecycleRegressionConfirmed { get; }
    public bool RuntimeAddressablesLookupLoadConfirmed { get; }
    public bool RuntimeResidencyBudgetConfirmed { get; }

    public TerrainSurfaceResidencyGateDecision
        SurfaceResidencyGateDecision { get; }

    public bool IsComplete =>
        FullAndIncrementalBakeRegressionConfirmed
        && StreamingRepairAndMigrationConfirmed
        && CancellationResumeRegressionConfirmed
        && FaultRecoveryRegressionConfirmed
        && SceneSynchronizationRegressionConfirmed
        && LifecycleRegressionConfirmed
        && RuntimeAddressablesLookupLoadConfirmed
        && RuntimeResidencyBudgetConfirmed
        && SurfaceResidencyGateDecision !=
            TerrainSurfaceResidencyGateDecision.NotEvaluated;

    public TerrainRuntimeFinalCertificationManualEvidence(
        bool fullAndIncrementalBakeRegressionConfirmed,
        bool streamingRepairAndMigrationConfirmed,
        bool cancellationResumeRegressionConfirmed,
        bool faultRecoveryRegressionConfirmed,
        bool sceneSynchronizationRegressionConfirmed,
        bool lifecycleRegressionConfirmed,
        bool runtimeAddressablesLookupLoadConfirmed,
        bool runtimeResidencyBudgetConfirmed,
        TerrainSurfaceResidencyGateDecision
            surfaceResidencyGateDecision
    )
    {
        FullAndIncrementalBakeRegressionConfirmed =
            fullAndIncrementalBakeRegressionConfirmed;
        StreamingRepairAndMigrationConfirmed =
            streamingRepairAndMigrationConfirmed;
        CancellationResumeRegressionConfirmed =
            cancellationResumeRegressionConfirmed;
        FaultRecoveryRegressionConfirmed =
            faultRecoveryRegressionConfirmed;
        SceneSynchronizationRegressionConfirmed =
            sceneSynchronizationRegressionConfirmed;
        LifecycleRegressionConfirmed =
            lifecycleRegressionConfirmed;
        RuntimeAddressablesLookupLoadConfirmed =
            runtimeAddressablesLookupLoadConfirmed;
        RuntimeResidencyBudgetConfirmed =
            runtimeResidencyBudgetConfirmed;
        SurfaceResidencyGateDecision =
            surfaceResidencyGateDecision;
    }
}

public sealed partial class TerrainRuntimeFinalCertificationReport
{
    public DateTime CreatedAtUtc { get; internal set; }

    public TerrainRuntimeReadinessResult Readiness { get; internal set; }

    public TerrainRuntimeAddressablesValidationResult
        AddressablesValidation { get; internal set; }

    public TerrainRuntimeAddressablesScaleReport
        RuntimeAddressablesScale { get; internal set; }

    public TerrainHeightAddressablesScaleReport
        HeightAddressablesScale { get; internal set; }

    public bool AddressablesScaleAvailable { get; internal set; }
    public string AddressablesScaleError { get; internal set; }

    public bool HeightAddressablesEntriesMatch { get; internal set; }
    public bool BuildScaleMeasurementsAvailable { get; internal set; }

    public bool? LatestPipelineValidationPassed { get; internal set; }
    public bool? LatestPersistenceValidationPassed { get; internal set; }
    public bool? LatestResumeValidationPassed { get; internal set; }
    public bool? LatestFaultRecoveryValidationPassed { get; internal set; }
    public bool? LatestEquivalenceValidationPassed { get; internal set; }

    public TerrainRuntimeFinalCertificationManualEvidence
        ManualEvidence { get; internal set; }

    public bool CurrentStatePassed =>
        Readiness != null
        && Readiness.IsReady
        && AddressablesValidation != null
        && AddressablesValidation.IsValid
        && AddressablesScaleAvailable
        && HeightAddressablesEntriesMatch
        && BuildScaleMeasurementsAvailable;

    public bool LatestRegressionEvidenceComplete =>
        LatestPipelineValidationPassed.HasValue
        && LatestPersistenceValidationPassed.HasValue
        && LatestResumeValidationPassed.HasValue
        && LatestFaultRecoveryValidationPassed.HasValue
        && LatestEquivalenceValidationPassed.HasValue;

    public bool LatestRegressionEvidenceHasFailure =>
        IsExplicitFailure(LatestPipelineValidationPassed)
        || IsExplicitFailure(LatestPersistenceValidationPassed)
        || IsExplicitFailure(LatestResumeValidationPassed)
        || IsExplicitFailure(LatestFaultRecoveryValidationPassed)
        || IsExplicitFailure(LatestEquivalenceValidationPassed);

    public bool LatestRegressionEvidencePassed =>
        LatestRegressionEvidenceComplete
        && !LatestRegressionEvidenceHasFailure;

    public bool IsComplete =>
        CurrentStatePassed
        && LatestRegressionEvidencePassed
        && ManualEvidence.IsComplete;

    public bool HasFailure =>
        !CurrentStatePassed
        || LatestRegressionEvidenceHasFailure;

    public string OutcomeLabel
    {
        get
        {
            if (IsComplete)
            {
                return "PASS";
            }

            if (HasFailure)
            {
                return "FAIL";
            }

            return "INCOMPLETE";
        }
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Final Certification"
        );

        builder.AppendLine(
            "Created UTC: " +
            CreatedAtUtc.ToString("u")
        );

        builder.AppendLine();
        builder.AppendLine("Current Runtime State");

        builder.AppendLine(
            "  Runtime Ready: " +
            (
                Readiness != null
                && Readiness.IsReady
                    ? "PASS"
                    : "FAIL"
            )
        );

        builder.AppendLine(
            "  Addressables Configuration: " +
            (
                AddressablesValidation != null
                && AddressablesValidation.IsValid
                    ? "PASS"
                    : "FAIL"
            )
        );

        builder.AppendLine(
            "  Height Addressable Entries: " +
            (
                HeightAddressablesEntriesMatch
                    ? "PASS"
                    : "FAIL"
            )
        );

        builder.AppendLine(
            "  Addressables Scale: " +
            (
                AddressablesScaleAvailable
                    ? "Available"
                    : "Unavailable"
            )
        );

        builder.AppendLine(
            "  Build Scale Measurements: " +
            (
                BuildScaleMeasurementsAvailable
                    ? "Available"
                    : "Unavailable"
            )
        );

        if (!string.IsNullOrEmpty(AddressablesScaleError))
        {
            builder.AppendLine(
                "  Scale Error: " +
                AddressablesScaleError
            );
        }

        AppendAddressablesScale(builder);

        builder.AppendLine();
        builder.AppendLine(
            "Latest Existing Regression Evidence"
        );

        AppendEvidence(
            builder,
            "Pipeline Validation",
            LatestPipelineValidationPassed
        );

        AppendEvidence(
            builder,
            "Persistence Validation",
            LatestPersistenceValidationPassed
        );

        AppendEvidence(
            builder,
            "Cancellation / Resume Validation",
            LatestResumeValidationPassed
        );

        AppendEvidence(
            builder,
            "Fault Recovery Validation",
            LatestFaultRecoveryValidationPassed
        );

        AppendEvidence(
            builder,
            "Incremental / Full Equivalence",
            LatestEquivalenceValidationPassed
        );

        builder.AppendLine();
        builder.AppendLine(
            "Manual Regression Evidence"
        );

        AppendManualEvidence(
            builder,
            "Full + Incremental Bake Regression",
            ManualEvidence.FullAndIncrementalBakeRegressionConfirmed
        );

        AppendManualEvidence(
            builder,
            "Streaming Repair + Migration",
            ManualEvidence.StreamingRepairAndMigrationConfirmed
        );

        AppendManualEvidence(
            builder,
            "Cancellation + Resume Matrix",
            ManualEvidence.CancellationResumeRegressionConfirmed
        );

        AppendManualEvidence(
            builder,
            "Fault Recovery Matrix",
            ManualEvidence.FaultRecoveryRegressionConfirmed
        );

        AppendManualEvidence(
            builder,
            "Scene Synchronization Regression",
            ManualEvidence.SceneSynchronizationRegressionConfirmed
        );

        AppendManualEvidence(
            builder,
            "Play / Scene / Streamer Lifecycle",
            ManualEvidence.LifecycleRegressionConfirmed
        );

        AppendManualEvidence(
            builder,
            "Runtime Addressables Lookup / Load",
            ManualEvidence.RuntimeAddressablesLookupLoadConfirmed
        );

        AppendManualEvidence(
            builder,
            "Runtime Residency / Budget Gate",
            ManualEvidence.RuntimeResidencyBudgetConfirmed
        );

        builder.AppendLine(
            "  Surface Residency Gate: " +
            FormatSurfaceGate(
                ManualEvidence.SurfaceResidencyGateDecision
            )
        );

        builder.AppendLine();
        builder.AppendLine(
            "Current State: " +
            (CurrentStatePassed ? "PASS" : "FAIL")
        );

        builder.AppendLine(
            "Latest Regression Evidence: " +
            (
                LatestRegressionEvidencePassed
                    ? "PASS"
                    : LatestRegressionEvidenceHasFailure
                        ? "FAIL"
                        : "INCOMPLETE"
            )
        );

        builder.AppendLine(
            "Manual Evidence: " +
            (
                ManualEvidence.IsComplete
                    ? "COMPLETE"
                    : "INCOMPLETE"
            )
        );

        builder.AppendLine();
        builder.AppendLine(
            "FINAL RESULT: " +
            OutcomeLabel
        );

        return builder.ToString();
    }

    private void AppendAddressablesScale(
        StringBuilder builder
    )
    {
        if (RuntimeAddressablesScale != null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Addressables Logical Scale"
            );

            builder.AppendLine(
                "  Expected Height Entries: " +
                RuntimeAddressablesScale.ExpectedHeightEntryCount
            );

            builder.AppendLine(
                "  Expected Surface Entries: " +
                RuntimeAddressablesScale.ExpectedSurfaceEntryCount
            );

            builder.AppendLine(
                "  Expected Collision Mesh Entries: " +
                RuntimeAddressablesScale.ExpectedCollisionMeshEntryCount
            );

            builder.AppendLine(
                "  Expected Collision Marker Entries: " +
                RuntimeAddressablesScale.ExpectedCollisionMarkerEntryCount
            );

            builder.AppendLine(
                "  Expected Total Managed Entries: " +
                RuntimeAddressablesScale.ExpectedTotalManagedEntryCount
            );

            builder.AppendLine(
                "  Expected Total Bundles: " +
                RuntimeAddressablesScale.ExpectedTotalBundleCount
            );
        }

        if (HeightAddressablesScale != null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Height Addressables Scale"
            );

            builder.AppendLine(
                "  Expected / Actual Entries: " +
                HeightAddressablesScale.ExpectedHeightEntryCount +
                " / " +
                HeightAddressablesScale.ActualHeightEntryCount
            );

            builder.AppendLine(
                "  Height Representations: " +
                HeightAddressablesScale.RepresentationLevelCount
            );

            builder.AppendLine(
                "  Height Packing Regions: " +
                HeightAddressablesScale.PackingRegionCount
            );

            builder.AppendLine(
                "  Expected Height Bundles: " +
                HeightAddressablesScale.ExpectedHeightBundleCount
            );

            builder.AppendLine(
                "  Measured Build Bundle Files: " +
                FormatMeasurement(
                    HeightAddressablesScale.BuiltBundleFileCount
                )
            );

            builder.AppendLine(
                "  Catalog Payload Bytes: " +
                FormatMeasurement(
                    HeightAddressablesScale.CatalogPayloadBytes
                )
            );

            builder.AppendLine(
                "  Height Reconciliation: " +
                HeightAddressablesScale
                    .HeightConfigurationReconciliationSeconds
                    .ToString("0.00") +
                " s"
            );

            builder.AppendLine(
                "  Addressables Build: " +
                HeightAddressablesScale
                    .AddressablesBuildSeconds
                    .ToString("0.00") +
                " s"
            );
        }
    }

    private static void AppendEvidence(
        StringBuilder builder,
        string label,
        bool? passed
    )
    {
        builder.AppendLine(
            "  " +
            label +
            ": " +
            (
                !passed.HasValue
                    ? "NOT RUN / NOT RETAINED"
                    : passed.Value
                        ? "PASS"
                        : "FAIL"
            )
        );
    }

    private static void AppendManualEvidence(
        StringBuilder builder,
        string label,
        bool confirmed
    )
    {
        builder.AppendLine(
            "  " +
            label +
            ": " +
            (
                confirmed
                    ? "CONFIRMED"
                    : "NOT CONFIRMED"
            )
        );
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
                    "Multiresolution Surface Not Required " +
                    "For Target Configuration";

            case TerrainSurfaceResidencyGateDecision
                .RequiredForTargetConfiguration:
                return
                    "Multiresolution Surface Required " +
                    "For Target Configuration";

            default:
                return "Not Evaluated";
        }
    }

    private static string FormatMeasurement(
        long value
    )
    {
        return
            value >= 0L
                ? value.ToString("N0")
                : "Unavailable";
    }

    private static bool IsExplicitFailure(
        bool? value
    )
    {
        return
            value.HasValue
            && !value.Value;
    }
}

public static partial class TerrainRuntimeFinalCertificationUtility
{
    public static TerrainRuntimeFinalCertificationReport
        Evaluate(
            WorldSettings worldSettings,
            TerrainRuntimeBakePersistenceValidationResult
                persistenceValidation,
            TerrainRuntimeFinalCertificationManualEvidence
                manualEvidence
        )
    {
        TerrainRuntimeFinalCertificationReport report =
            new TerrainRuntimeFinalCertificationReport
            {
                CreatedAtUtc =
                    DateTime.UtcNow,

                ManualEvidence =
                    manualEvidence
            };

        if (worldSettings == null)
        {
            report.AddressablesScaleError =
                "WorldSettings is unavailable.";

            return report;
        }

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        report.Readiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(
                    worldSettings,
                    authoringData,
                    true
                );

        report.AddressablesValidation =
            report.Readiness != null
                ? report.Readiness.AddressablesValidation
                : null;

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        TerrainAddressablesOperationStats stats =
            new TerrainAddressablesOperationStats();

        report.AddressablesScaleAvailable =
            TerrainRuntimeAddressablesScaleUtility
                .TryPopulateStats(
                    worldSettings,
                    heightManifest,
                    surfaceManifest,
                    stats,
                    out string scaleError
                );

        report.AddressablesScaleError =
            scaleError ?? "";

        if (report.AddressablesScaleAvailable)
        {
            report.RuntimeAddressablesScale =
                TerrainRuntimeAddressablesScaleUtility
                    .CreateReport(
                        stats
                    );
        }

        TerrainRuntimeAddressablesResult
            lastAddressablesResult =
                TerrainRuntimeBakePipeline.LastResult != null
                    ? TerrainRuntimeBakePipeline
                        .LastResult
                        .AddressablesResult
                    : null;

        string buildOutputPath =
            lastAddressablesResult != null
                ? lastAddressablesResult.BuildOutputPath
                : "";

        double buildDuration =
            lastAddressablesResult != null
                ? lastAddressablesResult.BuildDuration
                : 0d;

        report.HeightAddressablesScale =
            TerrainHeightAddressablesScaleUtility
                .CreateReport(
                    stats,
                    buildOutputPath,
                    buildDuration
                );

        report.HeightAddressablesEntriesMatch =
            report.HeightAddressablesScale != null
            && report.HeightAddressablesScale.ExpectedHeightEntryCount > 0
            && report.HeightAddressablesScale.ExpectedHeightEntryCount ==
                report.HeightAddressablesScale.ActualHeightEntryCount;

        report.BuildScaleMeasurementsAvailable =
            report.HeightAddressablesScale != null
            && report.HeightAddressablesScale.BuiltBundleFileCount >= 0
            && report.HeightAddressablesScale.CatalogPayloadBytes >= 0L;

        TerrainRuntimeBakeValidationResult
            pipelineValidation =
                TerrainRuntimeBakeValidationUtility.LastResult;

        if (pipelineValidation != null)
        {
            report.LatestPipelineValidationPassed =
                (
                    pipelineValidation.Outcome ==
                        TerrainRuntimeBakeValidationOutcome.Passed
                    ||
                    pipelineValidation.Outcome ==
                        TerrainRuntimeBakeValidationOutcome.PassedWithWarnings
                )
                &&
                pipelineValidation.FinalPlanCurrent;
        }

        if (persistenceValidation != null)
        {
            report.LatestPersistenceValidationPassed =
                persistenceValidation.OverallPassed;
        }

        TerrainRuntimeBakeResumeValidationResult
            resumeValidation =
                TerrainRuntimeBakeResumeValidationUtility.LastResult;

        if (resumeValidation != null)
        {
            report.LatestResumeValidationPassed =
                resumeValidation.Passed;
        }

        TerrainRuntimeFaultRecoveryValidationResult
            faultRecoveryValidation =
                TerrainRuntimeFaultRecoveryScenarioRunner.LastResult;

        if (faultRecoveryValidation != null)
        {
            report.LatestFaultRecoveryValidationPassed =
                faultRecoveryValidation.Passed;
        }

        TerrainRuntimeEquivalenceValidationResult
            equivalenceValidation =
                TerrainRuntimeEquivalenceScenarioRunner.LastResult;

        if (equivalenceValidation != null)
        {
            report.LatestEquivalenceValidationPassed =
                equivalenceValidation.Passed;
        }

        return report;
    }
}
