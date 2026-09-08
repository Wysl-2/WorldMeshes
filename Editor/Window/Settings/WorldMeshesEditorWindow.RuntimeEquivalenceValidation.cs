using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showRuntimeEquivalenceValidation;

    [SerializeField]
    private TerrainRuntimeEquivalenceScenario
        runtimeEquivalenceScenario =
            TerrainRuntimeEquivalenceScenario
                .SingleLocalEdit;

    private string runtimeEquivalenceMessage =
        "";

    private MessageType runtimeEquivalenceMessageType =
        MessageType.None;

    private void DrawRuntimeEquivalenceValidationDiagnostics()
    {
        showRuntimeEquivalenceValidation =
            EditorGUILayout.Foldout(
                showRuntimeEquivalenceValidation,
                "Incremental / Full Equivalence",
                true
            );

        if (!showRuntimeEquivalenceValidation)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "Package 10.5 performs a normal smallest-correct runtime bake, fingerprints the COMPLETE generated Height / Surface / Collision dataset, then performs Rebuild All Runtime Data and fingerprints the complete dataset again.\n\nA representative run can be substantially slower than Packages 10.1-10.4 because it intentionally includes a genuine Full rebuild and complete-world fingerprint capture.",
            MessageType.Warning
        );

        bool running =
            TerrainRuntimeEquivalenceScenarioRunner
                .IsRunning;

        bool hasBaseline =
            TerrainRuntimeEquivalenceScenarioRunner
                .HasBaseline;

        EditorGUILayout.LabelField(
            "Phase",
            TerrainRuntimeEquivalenceScenarioRunner
                .PhaseLabel
        );

        EditorGUILayout.LabelField(
            "Baseline",
            hasBaseline
                ? TerrainRuntimeEquivalenceScenarioRunner
                    .Baseline
                    .Scenario
                    .ToString()
                : "None"
        );

        runtimeEquivalenceScenario =
            (TerrainRuntimeEquivalenceScenario)
            EditorGUILayout.EnumPopup(
                "Scenario",
                runtimeEquivalenceScenario
            );

        GUILayout.Space(4f);

        EditorGUI.BeginDisabledGroup(
            running
            || !UsesRuntimeEquivalenceBaseline(
                runtimeEquivalenceScenario
            )
        );

        if (
            GUILayout.Button(
                "Capture Equivalence Baseline",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool captured =
                TerrainRuntimeEquivalenceScenarioRunner
                    .CaptureBaseline(
                        runtimeEquivalenceScenario,
                        out string message
                    );

            runtimeEquivalenceMessage =
                message;

            runtimeEquivalenceMessageType =
                captured
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (hasBaseline)
        {
            EditorGUILayout.HelpBox(
                "Baseline captured. Make the representative authoring/settings change now. Do NOT use the normal Runtime bake button. Then click Run Incremental + Full Equivalence.",
                MessageType.Info
            );
        }

        EditorGUI.BeginDisabledGroup(
            running
            || !hasBaseline
        );

        if (
            GUILayout.Button(
                "Run Incremental + Full Equivalence",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeEquivalenceScenarioRunner
                    .RunIncrementalFullEquivalence(
                        result =>
                        {
                            HandleRuntimeEquivalenceResult(
                                result
                            );

                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeEquivalenceMessage =
                    "Package 10.5 equivalence validation started.";

                runtimeEquivalenceMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Canonical Workflow Certification",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            running
        );

        if (
            GUILayout.Button(
                "Run Forced Full Health Validation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeEquivalenceScenarioRunner
                    .RunForcedFullHealthValidation(
                        result =>
                        {
                            HandleRuntimeEquivalenceResult(
                                result
                            );

                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeEquivalenceMessage =
                    "Forced Full health validation started.";

                runtimeEquivalenceMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        if (
            GUILayout.Button(
                "Run Initial Bake vs Full Equivalence",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeEquivalenceScenarioRunner
                    .RunInitialBakeEquivalence(
                        result =>
                        {
                            HandleRuntimeEquivalenceResult(
                                result
                            );

                            Repaint();
                        }
                    );

            if (started)
            {
                runtimeEquivalenceMessage =
                    "Initial Bake vs Full equivalence validation started.";

                runtimeEquivalenceMessageType =
                    MessageType.Info;
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "Initial Bake validation never deletes a healthy generated runtime dataset. It only runs when the canonical planner is already in a legitimate Initial Bake state.",
            MessageType.None
        );

        if (running)
        {
            EditorGUI.BeginDisabledGroup(
                !TerrainRuntimeBakePipeline
                    .IsRunning
            );

            if (
                GUILayout.Button(
                    "Request Validation Cancel",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                bool requested =
                    TerrainRuntimeEquivalenceScenarioRunner
                        .RequestCancel();

                runtimeEquivalenceMessage =
                    requested
                        ? "Cancellation requested at the canonical pipeline boundary."
                        : "Cancellation could not be requested in the current validation phase.";

                runtimeEquivalenceMessageType =
                    requested
                        ? MessageType.Warning
                        : MessageType.Error;

                Repaint();
            }

            EditorGUI.EndDisabledGroup();
        }

        TerrainRuntimeEquivalenceValidationResult last =
            TerrainRuntimeEquivalenceScenarioRunner
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

            if (last.Comparison != null)
            {
                EditorGUILayout.LabelField(
                    "Total Expected",
                    last.Comparison
                        .TotalExpected
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Total Matching",
                    last.Comparison
                        .TotalMatching
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Missing",
                    last.Comparison
                        .TotalMissing
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Unexpected",
                    last.Comparison
                        .TotalUnexpected
                        .ToString()
                );

                EditorGUILayout.LabelField(
                    "Payload Mismatches",
                    last.Comparison
                        .TotalPayloadMismatches
                        .ToString()
                );
            }

            if (
                GUILayout.Button(
                    "Log Last Equivalence Report",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                LogRuntimeEquivalenceResult(
                    last
                );
            }
        }

        EditorGUI.BeginDisabledGroup(
            running
        );

        if (
            GUILayout.Button(
                "Clear Package 10.5 Validation State",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool cleared =
                TerrainRuntimeEquivalenceScenarioRunner
                    .ClearValidationState(
                        out string message
                    );

            runtimeEquivalenceMessage =
                message;

            runtimeEquivalenceMessageType =
                cleared
                    ? MessageType.Info
                    : MessageType.Error;

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (
            !string.IsNullOrEmpty(
                runtimeEquivalenceMessage
            )
        )
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                runtimeEquivalenceMessage,
                runtimeEquivalenceMessageType
            );
        }

        GUILayout.EndVertical();
    }

    private void HandleRuntimeEquivalenceResult(
        TerrainRuntimeEquivalenceValidationResult result
    )
    {
        if (result == null)
        {
            runtimeEquivalenceMessage =
                "Package 10.5 returned no validation result.";

            runtimeEquivalenceMessageType =
                MessageType.Error;

            return;
        }

        runtimeEquivalenceMessage =
            !string.IsNullOrEmpty(
                result.SummaryMessage
            )
                ? result.SummaryMessage
                : result.Outcome.ToString();

        runtimeEquivalenceMessageType =
            result.Passed
                ? MessageType.Info
                : result.Outcome ==
                    TerrainRuntimeEquivalenceValidationOutcome.Cancelled
                    || result.Outcome ==
                        TerrainRuntimeEquivalenceValidationOutcome
                            .ComparisonInvalidated
                    || result.Outcome ==
                        TerrainRuntimeEquivalenceValidationOutcome
                            .Blocked
                        ? MessageType.Warning
                        : MessageType.Error;

        LogRuntimeEquivalenceResult(
            result
        );
    }

    private static void LogRuntimeEquivalenceResult(
        TerrainRuntimeEquivalenceValidationResult result
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
            Debug.Log(
                report
            );
        }
        else if (
            result.Outcome ==
                TerrainRuntimeEquivalenceValidationOutcome
                    .Cancelled
            || result.Outcome ==
                TerrainRuntimeEquivalenceValidationOutcome
                    .ComparisonInvalidated
            || result.Outcome ==
                TerrainRuntimeEquivalenceValidationOutcome
                    .Blocked
        )
        {
            Debug.LogWarning(
                report
            );
        }
        else
        {
            Debug.LogError(
                report
            );
        }
    }

    private static bool UsesRuntimeEquivalenceBaseline(
        TerrainRuntimeEquivalenceScenario scenario
    )
    {
        switch (scenario)
        {
            case TerrainRuntimeEquivalenceScenario.SingleLocalEdit:
            case TerrainRuntimeEquivalenceScenario.MultiTileEdit:
            case TerrainRuntimeEquivalenceScenario.ModifierMove:
            case TerrainRuntimeEquivalenceScenario.ModifierDelete:
            case TerrainRuntimeEquivalenceScenario.ModifierEnable:
            case TerrainRuntimeEquivalenceScenario.ModifierDisable:
            case TerrainRuntimeEquivalenceScenario.MultipleEditsBeforeOneBake:
            case TerrainRuntimeEquivalenceScenario.SurfaceSettingsChange:
            case TerrainRuntimeEquivalenceScenario.CollisionResolutionChange:
                return true;

            default:
                return false;
        }
    }
}
