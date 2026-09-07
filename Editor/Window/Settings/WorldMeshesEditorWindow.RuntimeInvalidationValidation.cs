using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private TerrainRuntimeInvalidationScenario
        invalidationModifierScenario =
            TerrainRuntimeInvalidationScenario.ModifierMove;

    [SerializeField]
    private TerrainRuntimeInvalidationScenario
        invalidationAggregationScenario =
            TerrainRuntimeInvalidationScenario.MultipleEdits;

    [SerializeField]
    private TerrainRuntimeInvalidationScenario
        invalidationSettingsScenario =
            TerrainRuntimeInvalidationScenario.SurfaceSettingsChange;

    private string invalidationValidationUiMessage = "";

    private void DrawRuntimeInvalidationValidationDiagnostics()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox
        );

        GUILayout.Label(
            "Invalidation Scenario Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Package 10.3 derives expected authoring footprints independently from the runtime bake planner, expands downstream dependencies through Package 02 utilities, then compares persistent bake state and the current TerrainRuntimeBakePlan.",
            MessageType.Info
        );

        TerrainRuntimeBakePlan currentPlan =
            TerrainRuntimeBakePlanner.BuildCurrentPlan();

        EditorGUILayout.LabelField(
            "Current Runtime State",
            currentPlan == null
                ? "Unavailable"
                : currentPlan.IsBlocked
                    ? "Blocked"
                    : currentPlan.HasWork
                        ? "Changes Pending"
                        : "Ready"
        );

        if (currentPlan != null)
        {
            EditorGUILayout.LabelField(
                "Current Plan",
                TerrainRuntimeBakePipelineResult
                    .BuildPlanSummary(
                        currentPlan
                    )
            );
        }

        DrawModifierInvalidationValidation();
        DrawAggregationInvalidationValidation();
        DrawSettingsInvalidationValidation();

        TerrainRuntimeInvalidationValidationResult lastResult =
            TerrainRuntimeInvalidationScenarioRunner.LastResult;

        if (lastResult != null)
        {
            GUILayout.Space(6f);

            EditorGUILayout.LabelField(
                "Last Result",
                lastResult.Outcome.ToString()
            );

            EditorGUILayout.HelpBox(
                lastResult.SummaryMessage,
                lastResult.Passed
                    ? MessageType.Info
                    : MessageType.Error
            );

            if (
                GUILayout.Button(
                    "Log Last Invalidation Report"
                )
            )
            {
                Debug.Log(
                    lastResult.BuildDiagnosticReport()
                );
            }

            EditorGUI.BeginDisabledGroup(
                !lastResult.Passed
                || TerrainRuntimeBakePipeline.IsRunning
                || TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
                || TerrainRuntimeBakeResumeValidationUtility.IsRunning
                || TerrainSurfaceMaskCompiler.IsGenerating
            );

            if (
                GUILayout.Button(
                    "Bake Current Invalidation + Package 10.1 Validate"
                )
            )
            {
                bool started =
                    TerrainRuntimeInvalidationScenarioRunner
                        .BakeCurrentInvalidationAndValidate(
                            result =>
                            {
                                if (result != null)
                                {
                                    Debug.Log(
                                        result.BuildDiagnosticReport()
                                    );
                                }

                                Repaint();
                            }
                        );

                if (!started)
                {
                    invalidationValidationUiMessage =
                        "The Package 10.1 validation bake could not start.";
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        if (!string.IsNullOrEmpty(invalidationValidationUiMessage))
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                invalidationValidationUiMessage,
                MessageType.Warning
            );
        }

        GUILayout.Space(4f);

        if (
            GUILayout.Button(
                "Clear Invalidation Validation Session"
            )
        )
        {
            TerrainRuntimeInvalidationScenarioRunner
                .ClearSession();

            invalidationValidationUiMessage = "";
        }

        GUILayout.EndVertical();
    }

    private void DrawModifierInvalidationValidation()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Guided Modifier Mutation",
            EditorStyles.boldLabel
        );

        TerrainRuntimeInvalidationModifierBaseline baseline =
            TerrainRuntimeInvalidationScenarioRunner.ModifierBaseline;

        EditorGUILayout.LabelField(
            "Captured Baseline",
            baseline != null
                ? (
                    baseline.ModifierType +
                    " / " +
                    baseline.StableId
                )
                : "None"
        );

        if (
            GUILayout.Button(
                "Capture Selected Modifier Baseline"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .CaptureSelectedModifierBaseline(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Baseline captured. Perform one modifier edit through the normal WorldMeshes authoring UI/tool, then validate it below.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        invalidationModifierScenario =
            (TerrainRuntimeInvalidationScenario)
            EditorGUILayout.EnumPopup(
                "Scenario",
                invalidationModifierScenario
            );

        EditorGUILayout.HelpBox(
            "Capture -> perform the real move/scale/rotate/Height Delta/enable/disable/delete edit -> Validate. For Undo or Redo, capture immediately before the real Unity Undo/Redo transition, perform it, then validate.",
            MessageType.None
        );

        if (
            GUILayout.Button(
                "Validate Current Modifier Mutation"
            )
        )
        {
            TerrainRuntimeInvalidationValidationResult result =
                TerrainRuntimeInvalidationScenarioRunner
                    .ValidateModifierMutation(
                        invalidationModifierScenario
                    );

            invalidationValidationUiMessage =
                result != null
                    ? result.SummaryMessage
                    : "No result was produced.";

            if (result != null)
            {
                Debug.Log(
                    result.BuildDiagnosticReport()
                );
            }
        }
    }

    private void DrawAggregationInvalidationValidation()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Multiple-Edit Union / Deduplication",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Aggregation Active",
            TerrainRuntimeInvalidationScenarioRunner
                .AggregationActive
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Recorded Mutations",
            TerrainRuntimeInvalidationScenarioRunner
                .AggregationMutationCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Expected Height Union",
            TerrainRuntimeInvalidationScenarioRunner
                .AggregatedHeightCoordinateCount
                .ToString()
        );

        if (
            GUILayout.Button(
                "Begin Aggregation Baseline From Selected Modifier"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .BeginAggregation(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Aggregation started. Perform one normal modifier edit, record it, then repeat without baking.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        if (
            GUILayout.Button(
                "Record Current Modifier Mutation Into Union"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .AccumulateCurrentModifierMutation(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Mutation footprint added to the expected union. Perform another edit or validate the aggregate.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        invalidationAggregationScenario =
            (TerrainRuntimeInvalidationScenario)
            EditorGUILayout.EnumPopup(
                "Aggregation Scenario",
                invalidationAggregationScenario
            );

        if (
            GUILayout.Button(
                "Validate Aggregated Pending Work"
            )
        )
        {
            TerrainRuntimeInvalidationValidationResult result =
                TerrainRuntimeInvalidationScenarioRunner
                    .ValidateAggregation(
                        invalidationAggregationScenario
                    );

            invalidationValidationUiMessage =
                result != null
                    ? result.SummaryMessage
                    : "No result was produced.";

            if (result != null)
            {
                Debug.Log(
                    result.BuildDiagnosticReport()
                );
            }
        }
    }

    private void DrawSettingsInvalidationValidation()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Guided Settings / Structural Mutation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Settings Baseline",
            TerrainRuntimeInvalidationScenarioRunner
                .HasSettingsBaseline
                ? "Captured"
                : "None"
        );

        if (
            GUILayout.Button(
                "Capture Settings Baseline"
            )
        )
        {
            if (
                TerrainRuntimeInvalidationScenarioRunner
                    .CaptureSettingsBaseline(
                        out string errorMessage
                    )
            )
            {
                invalidationValidationUiMessage =
                    "Settings baseline captured. Change one supported setting through the normal WorldMeshes UI, then validate.";
            }
            else
            {
                invalidationValidationUiMessage =
                    errorMessage;
            }
        }

        invalidationSettingsScenario =
            (TerrainRuntimeInvalidationScenario)
            EditorGUILayout.EnumPopup(
                "Settings Scenario",
                invalidationSettingsScenario
            );

        EditorGUILayout.HelpBox(
            "Supported guided settings scenarios: SurfaceSettingsChange, CollisionSettingsChange, WorldLayoutChange, HeightTileSpanChange. Structural tests can require a Full rebuild; restore the setting through the normal UI and bake back to Ready when testing is complete.",
            MessageType.Warning
        );

        if (
            GUILayout.Button(
                "Validate Current Settings Invalidation"
            )
        )
        {
            TerrainRuntimeInvalidationValidationResult result =
                TerrainRuntimeInvalidationScenarioRunner
                    .ValidateSettingsMutation(
                        invalidationSettingsScenario
                    );

            invalidationValidationUiMessage =
                result != null
                    ? result.SummaryMessage
                    : "No result was produced.";

            if (result != null)
            {
                Debug.Log(
                    result.BuildDiagnosticReport()
                );
            }
        }
    }
}
