using System;
using System.Text;
using UnityEditor;
using UnityEngine;

public enum TerrainRuntimeCertificationEvidenceState
{
    Missing,
    Current,
    Stale
}

[Serializable]
public sealed class TerrainRuntimeCertificationConfigurationSnapshot
{
    [SerializeField] private int gridWidth;
    [SerializeField] private int gridHeight;
    [SerializeField] private float chunkSize;
    [SerializeField] private int heightfieldResolutionPerChunk;
    [SerializeField] private int heightTileChunkSpan;
    [SerializeField] private int heightStreamingMaximumStride;
    [SerializeField] private int clipmapCenterResolution;
    [SerializeField] private int clipmapLevelCount;
    [SerializeField] private int clipmapBaseSampleStep;
    [SerializeField] private int[] clipmapLodOuterResolutions = Array.Empty<int>();

    [SerializeField] private string generatedHeightSignature = "";
    [SerializeField] private int heightGenerationRevision;
    [SerializeField] private int streamingPyramidCompilerVersion;
    [SerializeField] private int streamingSourceHeightGenerationRevision;
    [SerializeField] private int streamingGenerationRevision;
    [SerializeField] private string streamingGenerationSignature = "";

    [SerializeField] private string generatedSurfaceSignature = "";
    [SerializeField] private int surfaceGenerationRevision;
    [SerializeField] private int surfaceSourceHeightGenerationRevision;
    [SerializeField] private int surfaceCompilerVersion;
    [SerializeField] private int surfaceManifestGenerationRevision;
    [SerializeField] private int surfaceManifestSourceHeightGenerationRevision;
    [SerializeField] private string surfaceGenerationSignature = "";

    [SerializeField] private int runtimeGuardTileCount;
    [SerializeField] private int maxConcurrentHeightPageLoads;
    [SerializeField] private int maxHeightPageUploadsPerFrame;
    [SerializeField] private long capturedUtcTicks;

    public DateTime CapturedAtUtc =>
        capturedUtcTicks > 0L
            ? new DateTime(capturedUtcTicks, DateTimeKind.Utc)
            : DateTime.MinValue;

    public static TerrainRuntimeCertificationConfigurationSnapshot Capture(
        WorldSettings worldSettings,
        TerrainHeightmapStreamer streamer
    )
    {
        if (
            worldSettings == null
            || streamer == null
        )
        {
            return null;
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (
            heightManifest == null
            || surfaceManifest == null
        )
        {
            return null;
        }

        int[] outerResolutions =
            worldSettings.clipmapLODOuterResolutions != null
                ? (int[])worldSettings.clipmapLODOuterResolutions.Clone()
                : Array.Empty<int>();

        return
            new TerrainRuntimeCertificationConfigurationSnapshot
            {
                gridWidth = worldSettings.gridWidth,
                gridHeight = worldSettings.gridHeight,
                chunkSize = worldSettings.chunkSize,
                heightfieldResolutionPerChunk =
                    worldSettings.heightfieldResolutionPerChunk,
                heightTileChunkSpan =
                    worldSettings.heightTileChunkSpan,
                heightStreamingMaximumStride =
                    worldSettings.heightStreamingMaximumStride,
                clipmapCenterResolution =
                    worldSettings.clipmapCenterResolution,
                clipmapLevelCount =
                    worldSettings.clipmapLevelCount,
                clipmapBaseSampleStep =
                    worldSettings.clipmapBaseSampleStep,
                clipmapLodOuterResolutions =
                    outerResolutions,

                generatedHeightSignature =
                    worldSettings.lastGeneratedHeightSignature ?? "",
                heightGenerationRevision =
                    worldSettings.heightmapGenerationRevision,
                streamingPyramidCompilerVersion =
                    heightManifest.streamingPyramidCompilerVersion,
                streamingSourceHeightGenerationRevision =
                    heightManifest.streamingSourceHeightmapGenerationRevision,
                streamingGenerationRevision =
                    heightManifest.streamingGenerationRevision,
                streamingGenerationSignature =
                    heightManifest.streamingGenerationSignature ?? "",

                generatedSurfaceSignature =
                    worldSettings.lastGeneratedSurfaceSignature ?? "",
                surfaceGenerationRevision =
                    worldSettings.surfaceMaskGenerationRevision,
                surfaceSourceHeightGenerationRevision =
                    worldSettings.surfaceSourceHeightmapGenerationRevision,
                surfaceCompilerVersion =
                    surfaceManifest.compilerVersion,
                surfaceManifestGenerationRevision =
                    surfaceManifest.surfaceMaskGenerationRevision,
                surfaceManifestSourceHeightGenerationRevision =
                    surfaceManifest.sourceHeightmapGenerationRevision,
                surfaceGenerationSignature =
                    surfaceManifest.surfaceGenerationSignature ?? "",

                runtimeGuardTileCount =
                    streamer.RuntimeGuardTileCount,
                maxConcurrentHeightPageLoads =
                    streamer.MaxConcurrentHeightPageLoads,
                maxHeightPageUploadsPerFrame =
                    streamer.MaxHeightPageUploadsPerFrame,
                capturedUtcTicks =
                    DateTime.UtcNow.Ticks
            };
    }

    public bool MatchesCurrentConfiguration(
        WorldSettings worldSettings,
        TerrainHeightmapStreamer streamer,
        out string reason
    )
    {
        TerrainRuntimeCertificationConfigurationSnapshot current =
            Capture(
                worldSettings,
                streamer
            );

        if (current == null)
        {
            reason =
                "The current WorldSettings, runtime streamer, or generated Height/Surface manifests are unavailable.";

            return false;
        }

        return
            Matches(
                current,
                out reason
            );
    }

    private bool Matches(
        TerrainRuntimeCertificationConfigurationSnapshot current,
        out string reason
    )
    {
        if (gridWidth != current.gridWidth || gridHeight != current.gridHeight)
        {
            reason = "World grid dimensions changed.";
            return false;
        }

        if (!Mathf.Approximately(chunkSize, current.chunkSize))
        {
            reason = "World chunk size changed.";
            return false;
        }

        if (
            heightfieldResolutionPerChunk != current.heightfieldResolutionPerChunk
            || heightTileChunkSpan != current.heightTileChunkSpan
        )
        {
            reason = "Native Height page geometry changed.";
            return false;
        }

        if (heightStreamingMaximumStride != current.heightStreamingMaximumStride)
        {
            reason = "Maximum Height streaming stride changed.";
            return false;
        }

        if (
            clipmapCenterResolution != current.clipmapCenterResolution
            || clipmapLevelCount != current.clipmapLevelCount
            || clipmapBaseSampleStep != current.clipmapBaseSampleStep
            || !IntArraysEqual(
                clipmapLodOuterResolutions,
                current.clipmapLodOuterResolutions
            )
        )
        {
            reason = "Clipmap topology changed.";
            return false;
        }

        if (
            !string.Equals(
                generatedHeightSignature,
                current.generatedHeightSignature,
                StringComparison.Ordinal
            )
            || heightGenerationRevision != current.heightGenerationRevision
            || streamingPyramidCompilerVersion != current.streamingPyramidCompilerVersion
            || streamingSourceHeightGenerationRevision != current.streamingSourceHeightGenerationRevision
            || streamingGenerationRevision != current.streamingGenerationRevision
            || !string.Equals(
                streamingGenerationSignature,
                current.streamingGenerationSignature,
                StringComparison.Ordinal
            )
        )
        {
            reason = "Generated Height or Height streaming-pyramid identity changed.";
            return false;
        }

        if (
            !string.Equals(
                generatedSurfaceSignature,
                current.generatedSurfaceSignature,
                StringComparison.Ordinal
            )
            || surfaceGenerationRevision != current.surfaceGenerationRevision
            || surfaceSourceHeightGenerationRevision != current.surfaceSourceHeightGenerationRevision
            || surfaceCompilerVersion != current.surfaceCompilerVersion
            || surfaceManifestGenerationRevision != current.surfaceManifestGenerationRevision
            || surfaceManifestSourceHeightGenerationRevision != current.surfaceManifestSourceHeightGenerationRevision
            || !string.Equals(
                surfaceGenerationSignature,
                current.surfaceGenerationSignature,
                StringComparison.Ordinal
            )
        )
        {
            reason = "Generated Surface-mask identity changed.";
            return false;
        }

        if (runtimeGuardTileCount != current.runtimeGuardTileCount)
        {
            reason = "Runtime guard-tile count changed.";
            return false;
        }

        if (maxConcurrentHeightPageLoads != current.maxConcurrentHeightPageLoads)
        {
            reason = "Height page-load concurrency changed.";
            return false;
        }

        if (maxHeightPageUploadsPerFrame != current.maxHeightPageUploadsPerFrame)
        {
            reason = "Height page-upload limit changed.";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool IntArraysEqual(
        int[] a,
        int[] b
    )
    {
        int aLength = a != null ? a.Length : 0;
        int bLength = b != null ? b.Length : 0;

        if (aLength != bLength)
        {
            return false;
        }

        for (int i = 0; i < aLength; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}

[Serializable]
public sealed class TerrainRuntimeStressCertificationEvidence
{
    [SerializeField] private TerrainRuntimeCertificationConfigurationSnapshot configuration;
    [SerializeField] private long capturedUtcTicks;

    [SerializeField] private TerrainRuntimeValidationStatus boundaryStressStatus;
    [SerializeField] private TerrainRuntimeValidationStatus continuousMovementStatus;
    [SerializeField] private TerrainRuntimeValidationStatus rapidSupersessionStatus;
    [SerializeField] private TerrainRuntimeValidationStatus repeatedTransitionStatus;
    [SerializeField] private TerrainRuntimeValidationStatus deferredReleaseStatus;
    [SerializeField] private TerrainRuntimeValidationStatus streamerLifecycleStatus;
    [SerializeField] private TerrainRuntimeValidationStatus finalRestoreStatus;
    [SerializeField] private TerrainRuntimeValidationStatus overallStatus;

    [SerializeField] private float worldWidth;
    [SerializeField] private float worldHeight;
    [SerializeField] private float clipmapDiameter;
    [SerializeField] private int heightLodCount;
    [SerializeField] private int maximumHeightSampleStride;
    [SerializeField] private int nativeHeightPageSamplesPerSide;
    [SerializeField] private long heightSourceUpperBoundBytes;
    [SerializeField] private long surfaceSourceUpperBoundBytes;

    [SerializeField] private int peakActiveHeightLoads;
    [SerializeField] private int peakHeightTransientSources;
    [SerializeField] private long peakHeightSourceBytes;
    [SerializeField] private int maximumSurfaceResidentSources;
    [SerializeField] private long maximumSurfaceSourceBytes;
    [SerializeField] private int priorityViolations;
    [SerializeField] private int duplicateStartViolations;

    [SerializeField] private long deferredSourcesEnqueued;
    [SerializeField] private long deferredSourcesReleased;
    [SerializeField] private long deferredSourcesForcedReleased;
    [SerializeField] private long deferredFenceFallbacks;
    [SerializeField] private int repeatedTransitionsCompleted;
    [SerializeField] private int streamerLifecycleCyclesCompleted;
    [SerializeField] private string failureReason = "";
    [SerializeField] private string deferredReleaseNote = "";

    public TerrainRuntimeCertificationConfigurationSnapshot Configuration => configuration;
    public DateTime CapturedAtUtc =>
        capturedUtcTicks > 0L
            ? new DateTime(capturedUtcTicks, DateTimeKind.Utc)
            : DateTime.MinValue;

    public TerrainRuntimeValidationStatus BoundaryStressStatus => boundaryStressStatus;
    public TerrainRuntimeValidationStatus ContinuousMovementStatus => continuousMovementStatus;
    public TerrainRuntimeValidationStatus RapidSupersessionStatus => rapidSupersessionStatus;
    public TerrainRuntimeValidationStatus RepeatedTransitionStatus => repeatedTransitionStatus;
    public TerrainRuntimeValidationStatus DeferredReleaseStatus => deferredReleaseStatus;
    public TerrainRuntimeValidationStatus StreamerLifecycleStatus => streamerLifecycleStatus;
    public TerrainRuntimeValidationStatus FinalRestoreStatus => finalRestoreStatus;
    public TerrainRuntimeValidationStatus OverallStatus => overallStatus;

    public float WorldWidth => worldWidth;
    public float WorldHeight => worldHeight;
    public float ClipmapDiameter => clipmapDiameter;
    public int HeightLodCount => heightLodCount;
    public int MaximumHeightSampleStride => maximumHeightSampleStride;
    public int NativeHeightPageSamplesPerSide => nativeHeightPageSamplesPerSide;
    public long HeightSourceUpperBoundBytes => heightSourceUpperBoundBytes;
    public long SurfaceSourceUpperBoundBytes => surfaceSourceUpperBoundBytes;

    public int PeakActiveHeightLoads => peakActiveHeightLoads;
    public int PeakHeightTransientSources => peakHeightTransientSources;
    public long PeakHeightSourceBytes => peakHeightSourceBytes;
    public int MaximumSurfaceResidentSources => maximumSurfaceResidentSources;
    public long MaximumSurfaceSourceBytes => maximumSurfaceSourceBytes;
    public int PriorityViolations => priorityViolations;
    public int DuplicateStartViolations => duplicateStartViolations;

    public long DeferredSourcesEnqueued => deferredSourcesEnqueued;
    public long DeferredSourcesReleased => deferredSourcesReleased;
    public long DeferredSourcesForcedReleased => deferredSourcesForcedReleased;
    public long DeferredFenceFallbacks => deferredFenceFallbacks;
    public int RepeatedTransitionsCompleted => repeatedTransitionsCompleted;
    public int StreamerLifecycleCyclesCompleted => streamerLifecycleCyclesCompleted;
    public string FailureReason => failureReason;
    public string DeferredReleaseNote => deferredReleaseNote;

    public static TerrainRuntimeStressCertificationEvidence Capture(
        TerrainRuntimeStressCertificationResult result,
        TerrainRuntimeCertificationConfigurationSnapshot configuration
    )
    {
        if (result == null)
        {
            return null;
        }

        return
            new TerrainRuntimeStressCertificationEvidence
            {
                configuration = configuration,
                capturedUtcTicks = DateTime.UtcNow.Ticks,

                boundaryStressStatus = result.BoundaryStressStatus,
                continuousMovementStatus = result.ContinuousMovementStatus,
                rapidSupersessionStatus = result.RapidSupersessionStatus,
                repeatedTransitionStatus = result.RepeatedTransitionStatus,
                deferredReleaseStatus = result.DeferredReleaseStatus,
                streamerLifecycleStatus = result.StreamerLifecycleStatus,
                finalRestoreStatus = result.FinalRestoreStatus,
                overallStatus = result.OverallStatus,

                worldWidth = result.WorldWidth,
                worldHeight = result.WorldHeight,
                clipmapDiameter = result.ClipmapDiameter,
                heightLodCount = result.HeightLodCount,
                maximumHeightSampleStride = result.MaximumHeightSampleStride,
                nativeHeightPageSamplesPerSide = result.NativeHeightPageSamplesPerSide,
                heightSourceUpperBoundBytes = result.HeightSourceUpperBoundBytes,
                surfaceSourceUpperBoundBytes = result.SurfaceSourceUpperBoundBytes,

                peakActiveHeightLoads = result.PeakActiveHeightLoads,
                peakHeightTransientSources = result.PeakHeightTransientSources,
                peakHeightSourceBytes = result.PeakHeightSourceBytes,
                maximumSurfaceResidentSources = result.MaximumSurfaceResidentSources,
                maximumSurfaceSourceBytes = result.MaximumSurfaceSourceBytes,
                priorityViolations = result.PriorityViolations,
                duplicateStartViolations = result.DuplicateStartViolations,

                deferredSourcesEnqueued = result.DeferredSourcesEnqueued,
                deferredSourcesReleased = result.DeferredSourcesReleased,
                deferredSourcesForcedReleased = result.DeferredSourcesForcedReleased,
                deferredFenceFallbacks = result.DeferredFenceFallbacks,
                repeatedTransitionsCompleted = result.RepeatedTransitionsCompleted,
                streamerLifecycleCyclesCompleted = result.StreamerLifecycleCyclesCompleted,
                failureReason = result.FailureReason ?? "",
                deferredReleaseNote = result.DeferredReleaseNote ?? ""
            };
    }
}

public readonly struct TerrainRuntimeFinalCertificationIntegratedManualEvidence
{
    public bool StreamingRepairAndMigrationMatrixConfirmed { get; }
    public bool CancellationResumeMatrixConfirmed { get; }
    public bool FaultRecoveryMatrixConfirmed { get; }
    public bool SceneSynchronizationRegressionConfirmed { get; }
    public bool PlayModeSceneReloadLifecycleConfirmed { get; }
    public bool CollisionRuntimeAddressablesLoadConfirmed { get; }
    public bool AddressablesScaleReviewedAndAccepted { get; }

    public bool IsComplete =>
        StreamingRepairAndMigrationMatrixConfirmed
        && CancellationResumeMatrixConfirmed
        && FaultRecoveryMatrixConfirmed
        && SceneSynchronizationRegressionConfirmed
        && PlayModeSceneReloadLifecycleConfirmed
        && CollisionRuntimeAddressablesLoadConfirmed
        && AddressablesScaleReviewedAndAccepted;

    public TerrainRuntimeFinalCertificationIntegratedManualEvidence(
        bool streamingRepairAndMigrationMatrixConfirmed,
        bool cancellationResumeMatrixConfirmed,
        bool faultRecoveryMatrixConfirmed,
        bool sceneSynchronizationRegressionConfirmed,
        bool playModeSceneReloadLifecycleConfirmed,
        bool collisionRuntimeAddressablesLoadConfirmed,
        bool addressablesScaleReviewedAndAccepted
    )
    {
        StreamingRepairAndMigrationMatrixConfirmed =
            streamingRepairAndMigrationMatrixConfirmed;
        CancellationResumeMatrixConfirmed =
            cancellationResumeMatrixConfirmed;
        FaultRecoveryMatrixConfirmed =
            faultRecoveryMatrixConfirmed;
        SceneSynchronizationRegressionConfirmed =
            sceneSynchronizationRegressionConfirmed;
        PlayModeSceneReloadLifecycleConfirmed =
            playModeSceneReloadLifecycleConfirmed;
        CollisionRuntimeAddressablesLoadConfirmed =
            collisionRuntimeAddressablesLoadConfirmed;
        AddressablesScaleReviewedAndAccepted =
            addressablesScaleReviewedAndAccepted;
    }
}

public static class TerrainRuntimeCertificationEvidenceUtility
{
    public static TerrainRuntimeCertificationEvidenceState EvaluateState(
        TerrainRuntimeCertificationConfigurationSnapshot configuration,
        WorldSettings worldSettings,
        TerrainHeightmapStreamer streamer,
        out string reason
    )
    {
        if (configuration == null)
        {
            reason = "No certification configuration has been captured.";
            return TerrainRuntimeCertificationEvidenceState.Missing;
        }

        if (
            configuration.MatchesCurrentConfiguration(
                worldSettings,
                streamer,
                out reason
            )
        )
        {
            reason = "";
            return TerrainRuntimeCertificationEvidenceState.Current;
        }

        return TerrainRuntimeCertificationEvidenceState.Stale;
    }

    public static bool IsTerminal(
        TerrainRuntimeValidationStatus status
    )
    {
        return
            status == TerrainRuntimeValidationStatus.Passed
            || status == TerrainRuntimeValidationStatus.Failed
            || status == TerrainRuntimeValidationStatus.Inconclusive;
    }
}

public sealed partial class TerrainRuntimeFinalCertificationReport
{
    public TerrainRuntimeCertificationEvidenceState ResidencyEvidenceState { get; internal set; }
    public TerrainRuntimeCertificationEvidenceState StressEvidenceState { get; internal set; }
    public string ResidencyEvidenceStateReason { get; internal set; } = "";
    public string StressEvidenceStateReason { get; internal set; } = "";

    public TerrainRuntimeResidencyBudgetResult ResidencyBudgetResult { get; internal set; }
    public TerrainRuntimeStressCertificationEvidence StressCertificationEvidence { get; internal set; }

    public TerrainRuntimeFinalCertificationIntegratedManualEvidence
        IntegratedManualEvidence { get; internal set; }

    public bool HeightResidencyTargetPassed =>
        ResidencyEvidenceState == TerrainRuntimeCertificationEvidenceState.Current
        && ResidencyBudgetResult != null
        && ResidencyBudgetResult.HeightBudgetStatus == TerrainRuntimeValidationStatus.Passed;

    public bool SurfaceResidencyGateComplete =>
        ResidencyEvidenceState == TerrainRuntimeCertificationEvidenceState.Current
        && ResidencyBudgetResult != null
        && ResidencyBudgetResult.SurfaceGateDecision !=
            TerrainSurfaceResidencyGateDecision.NotEvaluated;

    public bool RuntimeStressPassed =>
        StressEvidenceState == TerrainRuntimeCertificationEvidenceState.Current
        && StressCertificationEvidence != null
        && StressCertificationEvidence.OverallStatus ==
            TerrainRuntimeValidationStatus.Passed;

    public bool RuntimeStreamerLifecyclePassed =>
        StressEvidenceState == TerrainRuntimeCertificationEvidenceState.Current
        && StressCertificationEvidence != null
        && StressCertificationEvidence.StreamerLifecycleStatus ==
            TerrainRuntimeValidationStatus.Passed
        && StressCertificationEvidence.DeferredReleaseStatus ==
            TerrainRuntimeValidationStatus.Passed
        && StressCertificationEvidence.FinalRestoreStatus ==
            TerrainRuntimeValidationStatus.Passed;

    public bool AutomatedRuntimeEvidenceComplete =>
        HeightResidencyTargetPassed
        && SurfaceResidencyGateComplete
        && RuntimeStressPassed
        && RuntimeStreamerLifecyclePassed;

    public bool AutomatedRuntimeEvidenceHasFailure =>
        (
            ResidencyEvidenceState == TerrainRuntimeCertificationEvidenceState.Current
            && ResidencyBudgetResult != null
            && ResidencyBudgetResult.HeightBudgetStatus ==
                TerrainRuntimeValidationStatus.Failed
        )
        ||
        (
            StressEvidenceState == TerrainRuntimeCertificationEvidenceState.Current
            && StressCertificationEvidence != null
            && StressCertificationEvidence.OverallStatus ==
                TerrainRuntimeValidationStatus.Failed
        );

    public bool IntegratedIsComplete =>
        CurrentStatePassed
        && LatestRegressionEvidencePassed
        && AutomatedRuntimeEvidenceComplete
        && IntegratedManualEvidence.IsComplete;

    public bool IntegratedHasFailure =>
        !CurrentStatePassed
        || LatestRegressionEvidenceHasFailure
        || AutomatedRuntimeEvidenceHasFailure;

    public string IntegratedOutcomeLabel =>
        IntegratedIsComplete
            ? "PASS"
            : IntegratedHasFailure
                ? "FAIL"
                : "INCOMPLETE";

    public string BuildIntegratedDiagnosticReport()
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
        builder.AppendLine("Target Configuration Evidence");
        AppendEvidenceState(
            builder,
            "Residency Evidence",
            ResidencyEvidenceState,
            ResidencyEvidenceStateReason
        );
        AppendEvidenceState(
            builder,
            "Runtime Stress Evidence",
            StressEvidenceState,
            StressEvidenceStateReason
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
            (HeightAddressablesEntriesMatch ? "PASS" : "FAIL")
        );
        builder.AppendLine(
            "  Build Scale Measurements: " +
            (BuildScaleMeasurementsAvailable ? "AVAILABLE" : "UNAVAILABLE")
        );

        AppendIntegratedResidencyReport(builder);
        AppendIntegratedStressReport(builder);

        builder.AppendLine();
        builder.AppendLine("Addressables Scale");
        AppendAddressablesScale(builder);
        AppendManualEvidence(
            builder,
            "Scale Reviewed / Accepted",
            IntegratedManualEvidence.AddressablesScaleReviewedAndAccepted
        );

        builder.AppendLine();
        builder.AppendLine("Runtime Addressables Evidence");
        builder.AppendLine(
            "  Height Runtime Reacquire: " +
            (RuntimeStreamerLifecyclePassed ? "PASS" : "INCOMPLETE")
        );
        builder.AppendLine(
            "  Surface Runtime Reacquire: " +
            (RuntimeStreamerLifecyclePassed ? "PASS" : "INCOMPLETE")
        );
        AppendManualEvidence(
            builder,
            "Collision Runtime Load",
            IntegratedManualEvidence.CollisionRuntimeAddressablesLoadConfirmed
        );

        builder.AppendLine();
        builder.AppendLine("Latest Existing Regression Evidence");
        AppendEvidence(builder, "Pipeline Validation", LatestPipelineValidationPassed);
        AppendEvidence(builder, "Persistence Validation", LatestPersistenceValidationPassed);
        AppendEvidence(builder, "Cancellation / Resume Validation", LatestResumeValidationPassed);
        AppendEvidence(builder, "Fault Recovery Validation", LatestFaultRecoveryValidationPassed);
        AppendEvidence(builder, "Incremental / Full Equivalence", LatestEquivalenceValidationPassed);

        builder.AppendLine();
        builder.AppendLine("Manual Regression / Acceptance Evidence");
        AppendManualEvidence(
            builder,
            "Streaming Repair + Migration Matrix",
            IntegratedManualEvidence.StreamingRepairAndMigrationMatrixConfirmed
        );
        AppendManualEvidence(
            builder,
            "Cancellation + Resume Matrix",
            IntegratedManualEvidence.CancellationResumeMatrixConfirmed
        );
        AppendManualEvidence(
            builder,
            "Fault Recovery Matrix",
            IntegratedManualEvidence.FaultRecoveryMatrixConfirmed
        );
        AppendManualEvidence(
            builder,
            "Scene Synchronization Regression",
            IntegratedManualEvidence.SceneSynchronizationRegressionConfirmed
        );
        AppendManualEvidence(
            builder,
            "Play Mode / Scene Reload Lifecycle",
            IntegratedManualEvidence.PlayModeSceneReloadLifecycleConfirmed
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
            "Automated Runtime Evidence: " +
            (
                AutomatedRuntimeEvidenceComplete
                    ? "PASS"
                    : AutomatedRuntimeEvidenceHasFailure
                        ? "FAIL"
                        : "INCOMPLETE"
            )
        );
        builder.AppendLine(
            "Manual Evidence: " +
            (IntegratedManualEvidence.IsComplete ? "COMPLETE" : "INCOMPLETE")
        );
        builder.AppendLine();
        builder.AppendLine(
            "FINAL RESULT: " +
            IntegratedOutcomeLabel
        );

        return builder.ToString();
    }

    private void AppendIntegratedResidencyReport(
        StringBuilder builder
    )
    {
        builder.AppendLine();
        builder.AppendLine("Height Residency Certification");

        if (ResidencyBudgetResult == null)
        {
            builder.AppendLine("  Evidence: MISSING");
            return;
        }

        builder.AppendLine(
            "  Evidence State: " +
            ResidencyEvidenceState
        );
        AppendIntegratedBytes(
            builder,
            "  Target Budget",
            ResidencyBudgetResult.TerrainBudgetBytes
        );
        AppendIntegratedBytes(
            builder,
            "  Height Conservative Upper Bound",
            ResidencyBudgetResult.HeightConservativeUpperBoundBytes
        );
        builder.AppendLine(
            "  Height Headroom: " +
            FormatIntegratedSignedBytes(
                ResidencyBudgetResult.HeightBudgetHeadroomBytes
            )
        );
        AppendIntegratedBytes(
            builder,
            "  Legacy Conservative Upper Bound",
            ResidencyBudgetResult.LegacyHeightConservativeUpperBoundBytes
        );
        builder.AppendLine(
            "  Conservative Reduction: " +
            ResidencyBudgetResult.HeightUpperBoundSavingsPercent.ToString("N2") +
            "%"
        );
        builder.AppendLine(
            "  Height Target: " +
            ResidencyBudgetResult.HeightBudgetStatus
        );

        builder.AppendLine();
        builder.AppendLine("Surface Residency Decision");
        AppendIntegratedBytes(
            builder,
            "  Surface Conservative Upper Bound",
            ResidencyBudgetResult.SurfaceConservativeUpperBoundBytes
        );
        AppendIntegratedBytes(
            builder,
            "  Combined Terrain Upper Bound",
            ResidencyBudgetResult.ConservativeTerrainUpperBoundBytes
        );
        builder.AppendLine(
            "  Combined Terrain Budget: " +
            ResidencyBudgetResult.TerrainBudgetStatus
        );
        builder.AppendLine(
            "  Surface Gate: " +
            FormatSurfaceGate(
                ResidencyBudgetResult.SurfaceGateDecision
            )
        );
    }

    private void AppendIntegratedStressReport(
        StringBuilder builder
    )
    {
        builder.AppendLine();
        builder.AppendLine("Runtime Stress / Lifecycle Certification");

        if (StressCertificationEvidence == null)
        {
            builder.AppendLine("  Evidence: MISSING");
            return;
        }

        TerrainRuntimeStressCertificationEvidence evidence =
            StressCertificationEvidence;

        builder.AppendLine("  Evidence State: " + StressEvidenceState);
        builder.AppendLine("  Overall: " + evidence.OverallStatus);
        builder.AppendLine("  Boundary: " + evidence.BoundaryStressStatus);
        builder.AppendLine("  Continuous Movement: " + evidence.ContinuousMovementStatus);
        builder.AppendLine("  Rapid Supersession: " + evidence.RapidSupersessionStatus);
        builder.AppendLine("  Repeated Transitions: " + evidence.RepeatedTransitionStatus);
        builder.AppendLine("  Deferred Release: " + evidence.DeferredReleaseStatus);
        builder.AppendLine("  Streamer Lifecycle: " + evidence.StreamerLifecycleStatus);
        builder.AppendLine("  Final Restore: " + evidence.FinalRestoreStatus);
        AppendIntegratedBytes(builder, "  Peak Height Source", evidence.PeakHeightSourceBytes);
        AppendIntegratedBytes(builder, "  Height Source Bound", evidence.HeightSourceUpperBoundBytes);
        AppendIntegratedBytes(builder, "  Peak Surface Source", evidence.MaximumSurfaceSourceBytes);
        AppendIntegratedBytes(builder, "  Surface Source Bound", evidence.SurfaceSourceUpperBoundBytes);
        builder.AppendLine("  Priority Violations: " + evidence.PriorityViolations);
        builder.AppendLine("  Duplicate Starts: " + evidence.DuplicateStartViolations);
        builder.AppendLine(
            "  Deferred Enqueued / Released: " +
            evidence.DeferredSourcesEnqueued +
            " / " +
            evidence.DeferredSourcesReleased
        );
        builder.AppendLine(
            "  Forced Releases: " +
            evidence.DeferredSourcesForcedReleased
        );
        builder.AppendLine(
            "  Lifecycle Cycles: " +
            evidence.StreamerLifecycleCyclesCompleted
        );

        if (!string.IsNullOrEmpty(evidence.FailureReason))
        {
            builder.AppendLine(
                "  Failure: " +
                evidence.FailureReason
            );
        }
    }


    private static void AppendIntegratedBytes(
        StringBuilder builder,
        string label,
        long bytes
    )
    {
        builder.AppendLine(
            label + ": " +
            FormatIntegratedBytes(bytes)
        );
    }

    private static string FormatIntegratedBytes(
        long bytes
    )
    {
        return
            (bytes / (1024d * 1024d)).ToString("N2") +
            " MiB";
    }

    private static string FormatIntegratedSignedBytes(
        long bytes
    )
    {
        return
            (bytes > 0L ? "+" : "") +
            FormatIntegratedBytes(bytes);
    }

    private static void AppendEvidenceState(
        StringBuilder builder,
        string label,
        TerrainRuntimeCertificationEvidenceState state,
        string reason
    )
    {
        builder.AppendLine(
            "  " + label + ": " + state.ToString().ToUpperInvariant()
        );

        if (
            state != TerrainRuntimeCertificationEvidenceState.Current
            && !string.IsNullOrEmpty(reason)
        )
        {
            builder.AppendLine(
                "    " + reason.Replace("\n", "\n    ")
            );
        }
    }
}

public static partial class TerrainRuntimeFinalCertificationUtility
{
    public static TerrainRuntimeFinalCertificationReport EvaluateIntegrated(
        WorldSettings worldSettings,
        TerrainHeightmapStreamer streamer,
        TerrainRuntimeBakePersistenceValidationResult persistenceValidation,
        TerrainRuntimeFinalCertificationIntegratedManualEvidence manualEvidence,
        TerrainRuntimeResidencyBudgetResult residencyBudgetResult,
        TerrainRuntimeCertificationConfigurationSnapshot residencyConfiguration,
        TerrainRuntimeStressCertificationEvidence stressEvidence
    )
    {
        TerrainRuntimeFinalCertificationManualEvidence compatibilityEvidence =
            new TerrainRuntimeFinalCertificationManualEvidence(
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                TerrainSurfaceResidencyGateDecision.NotRequiredForTargetConfiguration
            );

        TerrainRuntimeFinalCertificationReport report =
            Evaluate(
                worldSettings,
                persistenceValidation,
                compatibilityEvidence
            );

        report.IntegratedManualEvidence =
            manualEvidence;
        report.ResidencyBudgetResult =
            residencyBudgetResult;
        report.StressCertificationEvidence =
            stressEvidence;

        report.ResidencyEvidenceState =
            EvaluateResidencyEvidenceState(
                residencyBudgetResult,
                residencyConfiguration,
                worldSettings,
                streamer,
                out string residencyReason
            );

        report.ResidencyEvidenceStateReason =
            residencyReason;

        report.StressEvidenceState =
            EvaluateStressEvidenceState(
                stressEvidence,
                worldSettings,
                streamer,
                out string stressReason
            );

        report.StressEvidenceStateReason =
            stressReason;

        return report;
    }

    private static TerrainRuntimeCertificationEvidenceState
        EvaluateResidencyEvidenceState(
            TerrainRuntimeResidencyBudgetResult result,
            TerrainRuntimeCertificationConfigurationSnapshot configuration,
            WorldSettings worldSettings,
            TerrainHeightmapStreamer streamer,
            out string reason
        )
    {
        if (result == null)
        {
            reason = "No runtime residency evaluation has been captured.";
            return TerrainRuntimeCertificationEvidenceState.Missing;
        }

        return
            TerrainRuntimeCertificationEvidenceUtility
                .EvaluateState(
                    configuration,
                    worldSettings,
                    streamer,
                    out reason
                );
    }

    private static TerrainRuntimeCertificationEvidenceState
        EvaluateStressEvidenceState(
            TerrainRuntimeStressCertificationEvidence evidence,
            WorldSettings worldSettings,
            TerrainHeightmapStreamer streamer,
            out string reason
        )
    {
        if (evidence == null)
        {
            reason = "No runtime stress certification has been captured.";
            return TerrainRuntimeCertificationEvidenceState.Missing;
        }

        return
            TerrainRuntimeCertificationEvidenceUtility
                .EvaluateState(
                    evidence.Configuration,
                    worldSettings,
                    streamer,
                    out reason
                );
    }
}
