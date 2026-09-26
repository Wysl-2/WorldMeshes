using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private TerrainRuntimeCertificationConfigurationSnapshot
        lastRuntimeResidencyConfiguration;

    [SerializeField]
    private TerrainRuntimeStressCertificationEvidence
        lastRuntimeStressCertificationEvidence;

    [SerializeField]
    private bool finalStreamingRepairMigrationMatrixConfirmed;

    [SerializeField]
    private bool finalCancellationResumeMatrixConfirmed;

    [SerializeField]
    private bool finalFaultRecoveryMatrixConfirmed;

    [SerializeField]
    private bool finalSceneSynchronizationRegressionAccepted;

    [SerializeField]
    private bool finalPlayModeSceneReloadLifecycleConfirmed;

    [SerializeField]
    private bool finalCollisionRuntimeAddressablesLoadConfirmed;

    [SerializeField]
    private bool finalAddressablesScaleReviewedAndAccepted;

    [System.NonSerialized]
    private TerrainRuntimeStressCertificationResult
        runtimeStressResultAlreadyCaptured;

    private void DrawRuntimeFinalCertificationDiagnostics()
    {
        showRuntimeFinalCertification =
            EditorGUILayout.Foldout(
                showRuntimeFinalCertification,
                "Final Runtime Certification",
                true
            );

        if (!showRuntimeFinalCertification)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "Final certification composes current readiness, Addressables scale, " +
            "existing bake/recovery evidence, captured runtime residency evidence, " +
            "and captured runtime stress/lifecycle evidence. Missing or stale " +
            "runtime evidence makes certification incomplete rather than treating " +
            "an old result as current.",
            MessageType.Info
        );

        TerrainHeightmapStreamer streamer =
            FindCertificationRuntimeHeightmapStreamer();

        DrawIntegratedRuntimeEvidenceSummary(
            streamer
        );

        GUILayout.Space(6f);
        GUILayout.Label(
            "Manual Matrix / Acceptance Evidence",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "These acknowledgements cover regression matrices or project-level " +
            "acceptance decisions that are not represented safely by one retained " +
            "automated last-result object.",
            MessageType.None
        );

        EditorGUI.BeginChangeCheck();

        finalStreamingRepairMigrationMatrixConfirmed =
            EditorGUILayout.Toggle(
                "Streaming Repair + Migration Matrix",
                finalStreamingRepairMigrationMatrixConfirmed
            );

        finalCancellationResumeMatrixConfirmed =
            EditorGUILayout.Toggle(
                "Cancellation + Resume Matrix",
                finalCancellationResumeMatrixConfirmed
            );

        finalFaultRecoveryMatrixConfirmed =
            EditorGUILayout.Toggle(
                "Fault Recovery Matrix",
                finalFaultRecoveryMatrixConfirmed
            );

        finalSceneSynchronizationRegressionAccepted =
            EditorGUILayout.Toggle(
                "Scene Synchronization Regression",
                finalSceneSynchronizationRegressionAccepted
            );

        finalPlayModeSceneReloadLifecycleConfirmed =
            EditorGUILayout.Toggle(
                "Play Mode / Scene Reload Lifecycle",
                finalPlayModeSceneReloadLifecycleConfirmed
            );

        finalCollisionRuntimeAddressablesLoadConfirmed =
            EditorGUILayout.Toggle(
                "Collision Runtime Addressables Load",
                finalCollisionRuntimeAddressablesLoadConfirmed
            );

        finalAddressablesScaleReviewedAndAccepted =
            EditorGUILayout.Toggle(
                "Addressables Scale Reviewed / Accepted",
                finalAddressablesScaleReviewedAndAccepted
            );

        if (EditorGUI.EndChangeCheck())
        {
            lastRuntimeFinalCertificationReport =
                null;
        }

        GUILayout.Space(6f);

        bool busy =
            EditorApplication.isPlayingOrWillChangePlaymode
            || TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating
            || TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
            || TerrainRuntimeBakeResumeValidationUtility.IsRunning
            || TerrainRuntimeEquivalenceScenarioRunner.IsRunning
            || TerrainRuntimeFaultRecoveryScenarioRunner.IsRunning
            || TerrainRuntimeFaultRecoveryScenarioRunner.IsRecoveryRunning;

        EditorGUI.BeginDisabledGroup(
            busy
            || streamer == null
        );

        if (
            GUILayout.Button(
                "Refresh Final Certification Report",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimeFinalCertificationReport =
                TerrainRuntimeFinalCertificationUtility
                    .EvaluateIntegrated(
                        worldSettings,
                        streamer,
                        lastRuntimePersistenceValidationResult,
                        BuildIntegratedRuntimeFinalCertificationManualEvidence(),
                        lastRuntimeResidencyBudgetResult,
                        lastRuntimeResidencyConfiguration,
                        lastRuntimeStressCertificationEvidence
                    );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (busy)
        {
            EditorGUILayout.HelpBox(
                "Final certification is unavailable while Play Mode, runtime bake, " +
                "or validation work is active.",
                MessageType.None
            );
        }
        else if (streamer == null)
        {
            EditorGUILayout.HelpBox(
                "The runtime Height streamer could not be resolved from the current " +
                "scene. Run Setup / Repair World Hierarchy before final certification.",
                MessageType.Warning
            );
        }

        if (lastRuntimeFinalCertificationReport != null)
        {
            GUILayout.Space(8f);

            DrawIntegratedRuntimeFinalCertificationReport(
                lastRuntimeFinalCertificationReport
            );
        }

        GUILayout.Space(6f);

        if (
            GUILayout.Button(
                "Open Runtime Height Streaming Invariants",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TextAsset documentation =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    RuntimeHeightStreamingInvariantsPath
                );

            if (documentation != null)
            {
                Selection.activeObject =
                    documentation;

                EditorGUIUtility.PingObject(
                    documentation
                );
            }
            else
            {
                Debug.LogWarning(
                    "Runtime Height streaming invariants documentation " +
                    "could not be found at " +
                    RuntimeHeightStreamingInvariantsPath +
                    "."
                );
            }
        }

        GUILayout.EndVertical();
    }

    private TerrainHeightmapStreamer FindCertificationRuntimeHeightmapStreamer()
    {
        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        if (streamer != null)
        {
            return streamer;
        }

        TerrainHeightmapStreamer[] candidates =
            Resources.FindObjectsOfTypeAll<TerrainHeightmapStreamer>();

        foreach (TerrainHeightmapStreamer candidate in candidates)
        {
            if (
                candidate == null
                || EditorUtility.IsPersistent(candidate)
                || !candidate.gameObject.scene.IsValid()
            )
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private void DrawIntegratedRuntimeEvidenceSummary(
        TerrainHeightmapStreamer streamer
    )
    {
        GUILayout.Label(
            "Automated Runtime Evidence",
            EditorStyles.boldLabel
        );

        TerrainRuntimeCertificationEvidenceState residencyState =
            EvaluateCapturedEvidenceState(
                lastRuntimeResidencyBudgetResult != null,
                lastRuntimeResidencyConfiguration,
                streamer,
                out string residencyReason
            );

        EditorGUILayout.LabelField(
            "Residency Evidence",
            residencyState.ToString()
        );

        if (
            residencyState != TerrainRuntimeCertificationEvidenceState.Current
            && !string.IsNullOrEmpty(residencyReason)
        )
        {
            EditorGUILayout.HelpBox(
                residencyReason,
                MessageType.Warning
            );
        }

        if (lastRuntimeResidencyBudgetResult != null)
        {
            EditorGUILayout.LabelField(
                "Height Budget Gate",
                lastRuntimeResidencyBudgetResult
                    .HeightBudgetStatus
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Combined Terrain Budget",
                lastRuntimeResidencyBudgetResult
                    .TerrainBudgetStatus
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Surface Residency Gate",
                FormatSurfaceResidencyGateDecision(
                    lastRuntimeResidencyBudgetResult
                        .SurfaceGateDecision
                )
            );
        }

        TerrainRuntimeCertificationEvidenceState stressState =
            EvaluateCapturedEvidenceState(
                lastRuntimeStressCertificationEvidence != null,
                lastRuntimeStressCertificationEvidence != null
                    ? lastRuntimeStressCertificationEvidence.Configuration
                    : null,
                streamer,
                out string stressReason
            );

        EditorGUILayout.LabelField(
            "Runtime Stress Evidence",
            stressState.ToString()
        );

        if (
            stressState != TerrainRuntimeCertificationEvidenceState.Current
            && !string.IsNullOrEmpty(stressReason)
        )
        {
            EditorGUILayout.HelpBox(
                stressReason,
                MessageType.Warning
            );
        }

        if (lastRuntimeStressCertificationEvidence != null)
        {
            EditorGUILayout.LabelField(
                "Runtime Stress Result",
                lastRuntimeStressCertificationEvidence
                    .OverallStatus
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Streamer Lifecycle",
                lastRuntimeStressCertificationEvidence
                    .StreamerLifecycleStatus
                    .ToString()
            );
        }
    }

    private TerrainRuntimeCertificationEvidenceState
        EvaluateCapturedEvidenceState(
            bool hasEvidence,
            TerrainRuntimeCertificationConfigurationSnapshot configuration,
            TerrainHeightmapStreamer streamer,
            out string reason
        )
    {
        if (!hasEvidence)
        {
            reason = "No completed evidence has been captured.";
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

    private TerrainRuntimeFinalCertificationIntegratedManualEvidence
        BuildIntegratedRuntimeFinalCertificationManualEvidence()
    {
        return
            new TerrainRuntimeFinalCertificationIntegratedManualEvidence(
                finalStreamingRepairMigrationMatrixConfirmed,
                finalCancellationResumeMatrixConfirmed,
                finalFaultRecoveryMatrixConfirmed,
                finalSceneSynchronizationRegressionAccepted,
                finalPlayModeSceneReloadLifecycleConfirmed,
                finalCollisionRuntimeAddressablesLoadConfirmed,
                finalAddressablesScaleReviewedAndAccepted
            );
    }

    private void DrawIntegratedRuntimeFinalCertificationReport(
        TerrainRuntimeFinalCertificationReport report
    )
    {
        GUILayout.Label(
            "Certification Result",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Final Result",
            report.IntegratedOutcomeLabel
        );

        EditorGUILayout.LabelField(
            "Current State",
            report.CurrentStatePassed
                ? "PASS"
                : "FAIL"
        );

        EditorGUILayout.LabelField(
            "Latest Regression Evidence",
            report.LatestRegressionEvidencePassed
                ? "PASS"
                : report.LatestRegressionEvidenceHasFailure
                    ? "FAIL"
                    : "INCOMPLETE"
        );

        EditorGUILayout.LabelField(
            "Automated Runtime Evidence",
            report.AutomatedRuntimeEvidenceComplete
                ? "PASS"
                : report.AutomatedRuntimeEvidenceHasFailure
                    ? "FAIL"
                    : "INCOMPLETE"
        );

        EditorGUILayout.LabelField(
            "Manual Evidence",
            report.IntegratedManualEvidence.IsComplete
                ? "COMPLETE"
                : "INCOMPLETE"
        );

        GUILayout.Space(6f);
        GUILayout.Label(
            "Height Residency Certification",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Evidence State",
            report.ResidencyEvidenceState.ToString()
        );

        if (report.ResidencyBudgetResult != null)
        {
            EditorGUILayout.LabelField(
                "Target Budget",
                FormatFinalCertificationBytes(
                    report.ResidencyBudgetResult.TerrainBudgetBytes
                )
            );

            EditorGUILayout.LabelField(
                "Height Conservative Upper Bound",
                FormatFinalCertificationBytes(
                    report.ResidencyBudgetResult
                        .HeightConservativeUpperBoundBytes
                )
            );

            EditorGUILayout.LabelField(
                "Height Headroom",
                FormatIntegratedCertificationSignedBytes(
                    report.ResidencyBudgetResult
                        .HeightBudgetHeadroomBytes
                )
            );

            EditorGUILayout.LabelField(
                "Height Target",
                report.ResidencyBudgetResult
                    .HeightBudgetStatus
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Combined Terrain Budget",
                report.ResidencyBudgetResult
                    .TerrainBudgetStatus
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Surface Residency Gate",
                FormatSurfaceResidencyGateDecision(
                    report.ResidencyBudgetResult
                        .SurfaceGateDecision
                )
            );
        }

        GUILayout.Space(6f);
        GUILayout.Label(
            "Runtime Stress / Lifecycle Certification",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Evidence State",
            report.StressEvidenceState.ToString()
        );

        if (report.StressCertificationEvidence != null)
        {
            TerrainRuntimeStressCertificationEvidence stress =
                report.StressCertificationEvidence;

            EditorGUILayout.LabelField(
                "Overall",
                stress.OverallStatus.ToString()
            );
            EditorGUILayout.LabelField(
                "Deferred Release",
                stress.DeferredReleaseStatus.ToString()
            );
            EditorGUILayout.LabelField(
                "Streamer Lifecycle",
                stress.StreamerLifecycleStatus.ToString()
            );
            EditorGUILayout.LabelField(
                "Final Restore",
                stress.FinalRestoreStatus.ToString()
            );
            EditorGUILayout.LabelField(
                "Peak Height Source / Bound",
                FormatFinalCertificationBytes(
                    stress.PeakHeightSourceBytes
                ) +
                " / " +
                FormatFinalCertificationBytes(
                    stress.HeightSourceUpperBoundBytes
                )
            );
            EditorGUILayout.LabelField(
                "Peak Surface Source / Bound",
                FormatFinalCertificationBytes(
                    stress.MaximumSurfaceSourceBytes
                ) +
                " / " +
                FormatFinalCertificationBytes(
                    stress.SurfaceSourceUpperBoundBytes
                )
            );
            EditorGUILayout.LabelField(
                "Priority Violations",
                stress.PriorityViolations.ToString()
            );
            EditorGUILayout.LabelField(
                "Duplicate Starts",
                stress.DuplicateStartViolations.ToString()
            );
            EditorGUILayout.LabelField(
                "Deferred Enqueued / Released",
                stress.DeferredSourcesEnqueued +
                " / " +
                stress.DeferredSourcesReleased
            );
        }

        GUILayout.Space(6f);
        GUILayout.Label(
            "Addressables Scale",
            EditorStyles.boldLabel
        );

        if (report.RuntimeAddressablesScale != null)
        {
            EditorGUILayout.LabelField(
                "Expected Managed Entries",
                report.RuntimeAddressablesScale
                    .ExpectedTotalManagedEntryCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Expected Bundles",
                report.RuntimeAddressablesScale
                    .ExpectedTotalBundleCount
                    .ToString("N0")
            );
        }

        if (report.HeightAddressablesScale != null)
        {
            EditorGUILayout.LabelField(
                "Measured Build Bundle Files",
                report.HeightAddressablesScale
                    .BuiltBundleFileCount >= 0
                    ? report.HeightAddressablesScale
                        .BuiltBundleFileCount
                        .ToString("N0")
                    : "Unavailable"
            );

            EditorGUILayout.LabelField(
                "Catalog Payload",
                report.HeightAddressablesScale
                    .CatalogPayloadBytes >= 0L
                    ? FormatFinalCertificationBytes(
                        report.HeightAddressablesScale
                            .CatalogPayloadBytes
                    )
                    : "Unavailable"
            );

            EditorGUILayout.LabelField(
                "Height Reconciliation",
                report.HeightAddressablesScale
                    .HeightConfigurationReconciliationSeconds
                    .ToString("0.00") +
                " s"
            );

            EditorGUILayout.LabelField(
                "Addressables Build",
                report.HeightAddressablesScale
                    .AddressablesBuildSeconds
                    .ToString("0.00") +
                " s"
            );
        }

        EditorGUILayout.LabelField(
            "Scale Reviewed / Accepted",
            report.IntegratedManualEvidence
                .AddressablesScaleReviewedAndAccepted
                ? "CONFIRMED"
                : "NOT CONFIRMED"
        );

        GUILayout.Space(6f);
        GUILayout.Label(
            "Latest Existing Regression Evidence",
            EditorStyles.boldLabel
        );

        DrawFinalCertificationEvidenceRow(
            "Pipeline Validation",
            report.LatestPipelineValidationPassed
        );
        DrawFinalCertificationEvidenceRow(
            "Persistence Validation",
            report.LatestPersistenceValidationPassed
        );
        DrawFinalCertificationEvidenceRow(
            "Cancellation / Resume",
            report.LatestResumeValidationPassed
        );
        DrawFinalCertificationEvidenceRow(
            "Fault Recovery",
            report.LatestFaultRecoveryValidationPassed
        );
        DrawFinalCertificationEvidenceRow(
            "Incremental / Full Equivalence",
            report.LatestEquivalenceValidationPassed
        );

        GUILayout.Space(6f);

        MessageType resultType =
            report.IntegratedIsComplete
                ? MessageType.Info
                : report.IntegratedHasFailure
                    ? MessageType.Error
                    : MessageType.Warning;

        EditorGUILayout.HelpBox(
            report.IntegratedIsComplete
                ? "Final certification is complete for the recorded target configuration. A Surface gate requiring multiresolution Surface is a valid completed Height-migration outcome."
                : report.IntegratedHasFailure
                    ? "Final certification found failing current-state, retained regression, or current automated runtime evidence."
                    : "Final certification is incomplete. Capture current B1/B2 evidence and complete the remaining regression/acceptance evidence.",
            resultType
        );

        if (!string.IsNullOrEmpty(report.AddressablesScaleError))
        {
            EditorGUILayout.HelpBox(
                report.AddressablesScaleError,
                MessageType.Warning
            );
        }

        if (
            GUILayout.Button(
                "Log Final Certification Report",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            string diagnosticReport =
                report.BuildIntegratedDiagnosticReport();

            if (report.IntegratedIsComplete)
            {
                Debug.Log(diagnosticReport);
            }
            else if (report.IntegratedHasFailure)
            {
                Debug.LogError(diagnosticReport);
            }
            else
            {
                Debug.LogWarning(diagnosticReport);
            }
        }
    }

    private void CaptureRuntimeStressCertificationEvidenceIfNeeded(
        TerrainHeightmapStreamer streamer,
        TerrainRuntimeStressCertificationResult result
    )
    {
        if (
            result == null
            || object.ReferenceEquals(
                result,
                runtimeStressResultAlreadyCaptured
            )
            || !TerrainRuntimeCertificationEvidenceUtility
                .IsTerminal(
                    result.OverallStatus
                )
        )
        {
            return;
        }

        TerrainRuntimeCertificationConfigurationSnapshot configuration =
            TerrainRuntimeCertificationConfigurationSnapshot
                .Capture(
                    worldSettings,
                    streamer
                );

        lastRuntimeStressCertificationEvidence =
            TerrainRuntimeStressCertificationEvidence
                .Capture(
                    result,
                    configuration
                );

        runtimeStressResultAlreadyCaptured =
            result;

        lastRuntimeFinalCertificationReport =
            null;
    }

    private void DrawCapturedRuntimeStressCertificationEvidence(
        TerrainHeightmapStreamer streamer
    )
    {
        if (lastRuntimeStressCertificationEvidence == null)
        {
            return;
        }

        GUILayout.Space(6f);
        GUILayout.Label(
            "Captured Stress Evidence",
            EditorStyles.boldLabel
        );

        TerrainRuntimeCertificationEvidenceState state =
            TerrainRuntimeCertificationEvidenceUtility
                .EvaluateState(
                    lastRuntimeStressCertificationEvidence.Configuration,
                    worldSettings,
                    streamer,
                    out string reason
                );

        EditorGUILayout.LabelField(
            "Evidence State",
            state.ToString()
        );

        EditorGUILayout.LabelField(
            "Captured Result",
            lastRuntimeStressCertificationEvidence
                .OverallStatus
                .ToString()
        );

        if (
            state != TerrainRuntimeCertificationEvidenceState.Current
            && !string.IsNullOrEmpty(reason)
        )
        {
            EditorGUILayout.HelpBox(
                reason,
                MessageType.Warning
            );
        }
    }

    private static string FormatIntegratedCertificationSignedBytes(
        long bytes
    )
    {
        return
            (bytes > 0L ? "+" : "") +
            FormatFinalCertificationBytes(bytes);
    }
}
