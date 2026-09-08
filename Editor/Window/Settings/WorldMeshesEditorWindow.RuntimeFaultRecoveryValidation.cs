using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showRuntimeFaultRecoveryValidation;

    [SerializeField]
    private TerrainRuntimeFaultRecoveryScenario
        runtimeFaultRecoveryScenario =
            TerrainRuntimeFaultRecoveryScenario
                .MissingHeightTexture;

    private string runtimeFaultRecoveryMessage =
        "";

    private MessageType runtimeFaultRecoveryMessageType =
        MessageType.None;

    private void DrawRuntimeFaultRecoveryValidationDiagnostics()
    {
        showRuntimeFaultRecoveryValidation =
            EditorGUILayout.Foldout(
                showRuntimeFaultRecoveryValidation,
                "Fault Recovery Validation",
                true
            );

        if (!showRuntimeFaultRecoveryValidation)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "These tests deliberately damage generated runtime data, generated runtime Addressables configuration, or the generated runtime scene hierarchy and then exercise the canonical recovery path.\n\nStart from Runtime Ready. Temporary quarantine data is stored under Library/WorldMeshes/Validation/FaultRecovery. Do not enter Play Mode while a destructive scenario is active.",
            MessageType.Warning
        );

        bool active =
            TerrainRuntimeFaultRecoveryScenarioRunner
                .HasActiveFault;

        bool running =
            TerrainRuntimeFaultRecoveryScenarioRunner
                .IsRecoveryRunning;

        bool orphaned =
            TerrainRuntimeFaultInjectionUtility
                .HasOrphanedValidationSession();

        EditorGUILayout.LabelField(
            "Active Fault",
            active
                ? TerrainRuntimeFaultRecoveryScenarioRunner
                    .ActiveScenario
                    .ToString()
                : "None"
        );

        EditorGUILayout.LabelField(
            "Validation Quarantine",
            orphaned
                ? "Recovery / cleanup required"
                : "Clear"
        );

        runtimeFaultRecoveryScenario =
            (TerrainRuntimeFaultRecoveryScenario)
            EditorGUILayout.EnumPopup(
                "Scenario",
                runtimeFaultRecoveryScenario
            );

        TerrainRuntimeFaultExpectation expectation =
            TerrainRuntimeFaultExpectation
                .ForScenario(
                    runtimeFaultRecoveryScenario
                );

        if (expectation != null)
        {
            EditorGUILayout.LabelField(
                "Recovery Authority",
                expectation.RecoveryAuthority.ToString()
            );

            EditorGUILayout.HelpBox(
                expectation.Description,
                MessageType.None
            );
        }

        GUILayout.Space(4f);

        EditorGUI.BeginDisabledGroup(
            runtimeFaultRecoveryScenario ==
                TerrainRuntimeFaultRecoveryScenario.None
            || active
            || running
            || orphaned
        );

        if (
            GUILayout.Button(
                "Inject + Validate Fault",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeFaultRecoveryValidationResult result =
                TerrainRuntimeFaultRecoveryScenarioRunner
                    .InjectAndValidate(
                        runtimeFaultRecoveryScenario
                    );

            HandleRuntimeFaultRecoveryResult(result);
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !active
            || running
        );

        if (
            GUILayout.Button(
                "Run Canonical Recovery",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeFaultRecoveryScenarioRunner
                    .RunCanonicalRecovery(
                        result =>
                        {
                            HandleRuntimeFaultRecoveryResult(result);
                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeFaultRecoveryMessage =
                    "Canonical recovery started.";
                runtimeFaultRecoveryMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        if (
            GUILayout.Button(
                "Restore Active Fault Without Certification",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool restored =
                TerrainRuntimeFaultRecoveryScenarioRunner
                    .RestoreActiveFault(
                        out string message
                    );

            runtimeFaultRecoveryMessage = message;
            runtimeFaultRecoveryMessageType =
                restored
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !orphaned
            || active
            || running
        );

        if (
            GUILayout.Button(
                "Restore Validation Quarantine",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool restored =
                TerrainRuntimeFaultInjectionUtility
                    .RestoreOrphanedValidationSession(
                        out string message
                    );

            runtimeFaultRecoveryMessage = message;
            runtimeFaultRecoveryMessageType =
                restored
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            active || running
        );

        if (
            GUILayout.Button(
                "Force Fresh Runtime Integrity Audit",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeReadinessResult readiness =
                TerrainRuntimeReadinessUtility
                    .Evaluate(true);

            runtimeFaultRecoveryMessage =
                BuildRuntimeFaultReadinessSummary(
                    readiness
                );

            runtimeFaultRecoveryMessageType =
                readiness != null
                && readiness.IsReady
                    ? MessageType.Info
                    : MessageType.Warning;
        }

        EditorGUI.EndDisabledGroup();

        TerrainRuntimeFaultRecoveryValidationResult last =
            TerrainRuntimeFaultRecoveryScenarioRunner
                .LastResult;

        if (last != null)
        {
            GUILayout.Space(6f);

            EditorGUILayout.LabelField(
                "Last Scenario",
                last.Scenario.ToString()
            );

            EditorGUILayout.LabelField(
                "Last Outcome",
                last.Outcome.ToString()
            );

            if (
                GUILayout.Button(
                    "Log Last Fault Recovery Report",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                LogRuntimeFaultRecoveryResult(last);
            }
        }

        if (!string.IsNullOrEmpty(runtimeFaultRecoveryMessage))
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                runtimeFaultRecoveryMessage,
                runtimeFaultRecoveryMessageType
            );
        }

        GUILayout.EndVertical();
    }

    private void HandleRuntimeFaultRecoveryResult(
        TerrainRuntimeFaultRecoveryValidationResult result
    )
    {
        if (result == null)
        {
            runtimeFaultRecoveryMessage =
                "Fault recovery validation returned no result.";
            runtimeFaultRecoveryMessageType =
                MessageType.Warning;
            return;
        }

        runtimeFaultRecoveryMessage =
            !string.IsNullOrEmpty(result.SummaryMessage)
                ? result.SummaryMessage
                : result.Outcome.ToString();

        runtimeFaultRecoveryMessageType =
            result.Passed
                ? MessageType.Info
                : result.Outcome ==
                    TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected
                    ? MessageType.Warning
                    : MessageType.Error;

        LogRuntimeFaultRecoveryResult(result);
    }

    private static void LogRuntimeFaultRecoveryResult(
        TerrainRuntimeFaultRecoveryValidationResult result
    )
    {
        if (result == null)
        {
            return;
        }

        string report =
            result.BuildDiagnosticReport();

        if (result.Passed)
        {
            Debug.Log(report);
        }
        else if (
            result.Outcome ==
                TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected
            || result.Outcome ==
                TerrainRuntimeFaultRecoveryValidationOutcome.Blocked
        )
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }

    private static string BuildRuntimeFaultReadinessSummary(
        TerrainRuntimeReadinessResult readiness
    )
    {
        if (readiness == null)
        {
            return "Runtime readiness is unavailable.";
        }

        return
            "Runtime Ready: " + readiness.IsReady +
            "\nHeight: " +
            TerrainGenerationStateUtility.GetStatusLabel(
                readiness.HeightStatus
            ) +
            "\nSurface: " +
            TerrainGenerationStateUtility.GetStatusLabel(
                readiness.SurfaceStatus
            ) +
            "\nCollision: " +
            TerrainGenerationStateUtility.GetStatusLabel(
                readiness.CollisionStatus
            ) +
            "\nAddressables: " +
            (
                readiness.AddressablesValidation != null
                && readiness.AddressablesValidation.IsValid
                    ? "Valid"
                    : "Needs Repair"
            ) +
            "\nHierarchy: " +
            (
                readiness.HierarchyReadiness != null
                && readiness.HierarchyReadiness.IsReady
                    ? "Valid"
                    : "Needs Repair"
            ) +
            "\nPlan: " +
            (
                readiness.Plan == null
                    ? "Unavailable"
                    : readiness.Plan.HasWork
                        ? TerrainRuntimeBakePipelineResult
                            .BuildPlanSummary(
                                readiness.Plan
                            )
                        : "No Work"
            );
    }
}
