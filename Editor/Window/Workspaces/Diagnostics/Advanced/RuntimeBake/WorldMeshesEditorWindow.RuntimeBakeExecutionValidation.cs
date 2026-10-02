using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showRuntimeBakeExecutionValidation;

    private TerrainRuntimeBakeValidationResult lastRuntimePipelineValidationResult;

    private void DrawRuntimeBakeExecutionValidation()
    {
        showRuntimeBakeExecutionValidation =
            EditorGUILayout.Foldout(
                showRuntimeBakeExecutionValidation,
                "Bake Execution",
                true
            );

        if (!showRuntimeBakeExecutionValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainRuntimeBakePlan currentPlan =
            GetRuntimeBakeDiagnosticsPlan();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Bake Execution Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Validates the observed stage execution of the most recent canonical Runtime Bake result against its captured stage plans. Normal Runtime baking remains owned by the Runtime workspace.",
            MessageType.None
        );

        EditorGUILayout.LabelField(
            "Current Bake Plan",
            TerrainRuntimeBakePipelineResult.BuildPlanSummary(currentPlan)
        );

        EditorGUI.BeginDisabledGroup(
            TerrainRuntimeBakePipeline.IsRunning
            || TerrainRuntimeBakePipeline.LastResult == null
        );

        if (
            GUILayout.Button(
                "Validate Last Runtime Bake Result",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimePipelineValidationResult =
                TerrainRuntimeBakeValidationUtility.ValidatePipelineResult(
                    TerrainRuntimeBakePipeline.LastResult
                );

            LogRuntimePipelineValidationResult(
                lastRuntimePipelineValidationResult
            );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        DrawRuntimePipelineValidationResultSummary();
        DrawRuntimePipelineTimingSummary();

        if (
            TerrainRuntimeBakePipeline.IsRunning
        )
        {
            Repaint();
        }

        GUILayout.EndVertical();
    }

    private void DrawRuntimePipelineValidationResultSummary()
    {
        TerrainRuntimeBakeValidationResult result =
            lastRuntimePipelineValidationResult
            ?? TerrainRuntimeBakeValidationUtility.LastResult;

        if (result == null)
        {
            return;
        }

        GUILayout.Space(8f);
        GUILayout.Label("Last Validation", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Outcome", result.Outcome.ToString());
        EditorGUILayout.LabelField("Final Plan Current", result.FinalPlanCurrent ? "Yes" : "No");

        DrawRuntimeStageValidationRow("Heightmaps", result.HeightValidation);
        DrawRuntimeStageValidationRow("Height Streaming", result.HeightStreamingValidation);
        DrawRuntimeStageValidationRow("Surface Masks", result.SurfaceValidation);
        DrawRuntimeStageValidationRow("Collision", result.CollisionValidation);

        if (result.AddressablesValidation != null)
        {
            EditorGUILayout.LabelField(
                "Addressables",
                GetValidationPassLabel(
                    result.AddressablesValidation.Evaluated,
                    result.AddressablesValidation.Passed
                ) +
                " - Expected " + result.AddressablesValidation.ExpectedOperationMode +
                ", Actual " + result.AddressablesValidation.ActualOperationMode
            );
        }

        if (result.SceneValidation != null)
        {
            EditorGUILayout.LabelField(
                "Runtime Scene",
                GetValidationPassLabel(
                    result.SceneValidation.Evaluated,
                    result.SceneValidation.Passed
                ) +
                " - " + result.SceneValidation.ActualOutcome
            );
        }

        EditorGUILayout.HelpBox(
            !string.IsNullOrEmpty(result.ErrorMessage)
                ? result.ErrorMessage
                : result.SummaryMessage,
            GetRuntimePipelineValidationMessageType(result.Outcome)
        );

        EditorGUILayout.HelpBox(
            "Raw duplicate coordinate submission is not observable because existing Height/Surface/Collision result contracts expose canonical unique coordinate sets. Execution validation does not change generator behavior solely for this diagnostic metric.",
            MessageType.None
        );
    }

    private static void DrawRuntimeStageValidationRow(
        string label,
        TerrainRuntimeBakeStageValidationResult result
    )
    {
        if (result == null || !result.Evaluated)
        {
            EditorGUILayout.LabelField(label, "Not evaluated");
            return;
        }

        EditorGUILayout.LabelField(
            label,
            (result.Passed ? "PASS" : "FAIL") +
            " - Expected " + result.ExpectedCount +
            ", Requested " + result.RequestedCount +
            ", Succeeded " + result.SucceededCount +
            ", Missing " + result.MissingRequested.Count +
            ", Unexpected " + result.UnexpectedRequested.Count
        );
    }

    private void DrawRuntimePipelineTimingSummary()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        if (result == null)
        {
            return;
        }

        GUILayout.Space(8f);
        GUILayout.Label("Last Pipeline Stage Timings", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Heightmaps", result.HeightDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Height Streaming", result.HeightStreamingDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Surface Masks", result.SurfaceDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Collision", result.CollisionDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Addressables", result.AddressablesDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Runtime Scene", result.SceneSyncDurationSeconds.ToString("0.00") + " s");
        EditorGUILayout.LabelField("Total", result.DurationSeconds.ToString("0.00") + " s");
    }

    private static void LogRuntimePipelineValidationResult(
        TerrainRuntimeBakeValidationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError("Runtime pipeline validation returned no result.");
            return;
        }

        string report = result.BuildDiagnosticReport();

        switch (result.Outcome)
        {
            case TerrainRuntimeBakeValidationOutcome.Passed:
                Debug.Log(report);
                break;

            case TerrainRuntimeBakeValidationOutcome.PassedWithWarnings:
            case TerrainRuntimeBakeValidationOutcome.Cancelled:
            case TerrainRuntimeBakeValidationOutcome.Blocked:
                Debug.LogWarning(report);
                break;

            default:
                Debug.LogError(report);
                break;
        }
    }

    private static string GetValidationPassLabel(
        bool evaluated,
        bool passed
    )
    {
        if (!evaluated)
        {
            return "Not evaluated";
        }

        return passed ? "PASS" : "FAIL";
    }

    private static MessageType GetRuntimePipelineValidationMessageType(
        TerrainRuntimeBakeValidationOutcome outcome
    )
    {
        switch (outcome)
        {
            case TerrainRuntimeBakeValidationOutcome.Passed:
                return MessageType.Info;

            case TerrainRuntimeBakeValidationOutcome.PassedWithWarnings:
            case TerrainRuntimeBakeValidationOutcome.Cancelled:
            case TerrainRuntimeBakeValidationOutcome.Blocked:
                return MessageType.Warning;

            default:
                return MessageType.Error;
        }
    }
}
