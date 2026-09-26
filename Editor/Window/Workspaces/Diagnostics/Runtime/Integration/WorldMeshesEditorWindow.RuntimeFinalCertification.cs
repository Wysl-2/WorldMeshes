using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private const string
        RuntimeHeightStreamingInvariantsPath =
            "Assets/WorldMeshes/Documentation/" +
            "RuntimeHeightStreamingInvariants.txt";

    [SerializeField]
    private bool showRuntimeFinalCertification;

    [SerializeField]
    private bool
        finalFullAndIncrementalBakeRegressionConfirmed;

    [SerializeField]
    private bool
        finalStreamingRepairAndMigrationConfirmed;

    [SerializeField]
    private bool
        finalCancellationResumeRegressionConfirmed;

    [SerializeField]
    private bool
        finalFaultRecoveryRegressionConfirmed;

    [SerializeField]
    private bool
        finalSceneSynchronizationRegressionConfirmed;

    [SerializeField]
    private bool
        finalLifecycleRegressionConfirmed;

    [SerializeField]
    private bool
        finalRuntimeAddressablesLookupLoadConfirmed;

    [SerializeField]
    private bool
        finalRuntimeResidencyBudgetConfirmed;

    [SerializeField]
    private TerrainSurfaceResidencyGateDecision
        finalSurfaceResidencyGateDecision =
            TerrainSurfaceResidencyGateDecision.NotEvaluated;

    private TerrainRuntimeFinalCertificationReport
        lastRuntimeFinalCertificationReport;

    private void DrawLegacyRuntimeFinalCertificationDiagnostics()
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
            "Final certification composes existing runtime-bake, recovery, " +
            "equivalence, Addressables, and readiness evidence. Fresh physical " +
            "integrity and Addressables configuration checks run only when the " +
            "certification report is explicitly refreshed.",
            MessageType.Info
        );

        EditorGUILayout.HelpBox(
            "The checkboxes below are explicit human acknowledgements for " +
            "regression matrices that cannot be inferred safely from one retained " +
            "last-result object. They do not execute tests or alter generated data.",
            MessageType.None
        );

        EditorGUI.BeginChangeCheck();

        finalFullAndIncrementalBakeRegressionConfirmed =
            EditorGUILayout.Toggle(
                "Full + Incremental Bake",
                finalFullAndIncrementalBakeRegressionConfirmed
            );

        finalStreamingRepairAndMigrationConfirmed =
            EditorGUILayout.Toggle(
                "Streaming Repair + Migration",
                finalStreamingRepairAndMigrationConfirmed
            );

        finalCancellationResumeRegressionConfirmed =
            EditorGUILayout.Toggle(
                "Cancellation + Resume Matrix",
                finalCancellationResumeRegressionConfirmed
            );

        finalFaultRecoveryRegressionConfirmed =
            EditorGUILayout.Toggle(
                "Fault Recovery Matrix",
                finalFaultRecoveryRegressionConfirmed
            );

        finalSceneSynchronizationRegressionConfirmed =
            EditorGUILayout.Toggle(
                "Scene Synchronization",
                finalSceneSynchronizationRegressionConfirmed
            );

        finalLifecycleRegressionConfirmed =
            EditorGUILayout.Toggle(
                "Play / Scene / Streamer Lifecycle",
                finalLifecycleRegressionConfirmed
            );

        finalRuntimeAddressablesLookupLoadConfirmed =
            EditorGUILayout.Toggle(
                "Runtime Addressables Lookup / Load",
                finalRuntimeAddressablesLookupLoadConfirmed
            );

        finalRuntimeResidencyBudgetConfirmed =
            EditorGUILayout.Toggle(
                "Runtime Residency / Budget Gate",
                finalRuntimeResidencyBudgetConfirmed
            );

        finalSurfaceResidencyGateDecision =
            (TerrainSurfaceResidencyGateDecision)
            EditorGUILayout.EnumPopup(
                "Surface Residency Gate",
                finalSurfaceResidencyGateDecision
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
                    .Evaluate(
                        worldSettings,
                        lastRuntimePersistenceValidationResult,
                        BuildRuntimeFinalCertificationManualEvidence()
                    );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (busy)
        {
            EditorGUILayout.HelpBox(
                "Final certification is unavailable while runtime bake or " +
                "validation work is active.",
                MessageType.None
            );
        }

        if (lastRuntimeFinalCertificationReport != null)
        {
            GUILayout.Space(8f);

            DrawRuntimeFinalCertificationReport(
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

    private TerrainRuntimeFinalCertificationManualEvidence
        BuildRuntimeFinalCertificationManualEvidence()
    {
        return
            new TerrainRuntimeFinalCertificationManualEvidence(
                finalFullAndIncrementalBakeRegressionConfirmed,
                finalStreamingRepairAndMigrationConfirmed,
                finalCancellationResumeRegressionConfirmed,
                finalFaultRecoveryRegressionConfirmed,
                finalSceneSynchronizationRegressionConfirmed,
                finalLifecycleRegressionConfirmed,
                finalRuntimeAddressablesLookupLoadConfirmed,
                finalRuntimeResidencyBudgetConfirmed,
                finalSurfaceResidencyGateDecision
            );
    }

    private void DrawRuntimeFinalCertificationReport(
        TerrainRuntimeFinalCertificationReport report
    )
    {
        GUILayout.Label(
            "Certification Result",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Final Result",
            report.OutcomeLabel
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
            "Manual Evidence",
            report.ManualEvidence.IsComplete
                ? "COMPLETE"
                : "INCOMPLETE"
        );

        EditorGUILayout.LabelField(
            "Surface Residency Gate",
            FormatSurfaceResidencyGateDecision(
                report.ManualEvidence.SurfaceResidencyGateDecision
            )
        );

        GUILayout.Space(6f);
        GUILayout.Label(
            "Fresh Current-State Checks",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Runtime Ready",
            report.Readiness != null
            && report.Readiness.IsReady
                ? "PASS"
                : "FAIL"
        );

        EditorGUILayout.LabelField(
            "Addressables Configuration",
            report.AddressablesValidation != null
            && report.AddressablesValidation.IsValid
                ? "PASS"
                : "FAIL"
        );

        EditorGUILayout.LabelField(
            "Height Entries Expected / Actual",
            report.HeightAddressablesScale != null
                ? report.HeightAddressablesScale
                    .ExpectedHeightEntryCount
                    .ToString("N0")
                    +
                    " / " +
                    report.HeightAddressablesScale
                    .ActualHeightEntryCount
                    .ToString("N0")
                : "Unavailable"
        );

        EditorGUILayout.LabelField(
            "Height Entry Match",
            report.HeightAddressablesEntriesMatch
                ? "PASS"
                : "FAIL"
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
            report.IsComplete
                ? MessageType.Info
                : report.HasFailure
                    ? MessageType.Error
                    : MessageType.Warning;

        EditorGUILayout.HelpBox(
            report.IsComplete
                ? "Final certification is complete for the recorded target configuration."
                : report.HasFailure
                    ? "Final certification found a failing current-state or retained regression result. Review the report before finalizing the runtime."
                    : "Final certification is incomplete. Run or acknowledge the remaining regression evidence, then refresh the report.",
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
                report.BuildDiagnosticReport();

            if (report.IsComplete)
            {
                Debug.Log(
                    diagnosticReport
                );
            }
            else if (report.HasFailure)
            {
                Debug.LogError(
                    diagnosticReport
                );
            }
            else
            {
                Debug.LogWarning(
                    diagnosticReport
                );
            }
        }
    }

    private static void DrawFinalCertificationEvidenceRow(
        string label,
        bool? passed
    )
    {
        EditorGUILayout.LabelField(
            label,
            !passed.HasValue
                ? "Not Run / Not Retained"
                : passed.Value
                    ? "PASS"
                    : "FAIL"
        );
    }

    private static string
        FormatSurfaceResidencyGateDecision(
            TerrainSurfaceResidencyGateDecision decision
        )
    {
        switch (decision)
        {
            case TerrainSurfaceResidencyGateDecision
                .NotRequiredForTargetConfiguration:
                return
                    "Surface multiresolution not required " +
                    "for target configuration";

            case TerrainSurfaceResidencyGateDecision
                .RequiredForTargetConfiguration:
                return
                    "Surface multiresolution required " +
                    "for target configuration";

            default:
                return "Not evaluated";
        }
    }

    private static string FormatFinalCertificationBytes(
        long bytes
    )
    {
        if (bytes <= 0L)
        {
            return "0 MiB";
        }

        double mib =
            bytes /
            (1024d * 1024d);

        return
            mib.ToString("N2") +
            " MiB";
    }
}
