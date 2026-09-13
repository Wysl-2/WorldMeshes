using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainRuntimeBakePipelineResult
        lastRuntimeBakePipelineResult;

    private void DrawRuntimeBakePipelineDiagnostics()
    {
        TerrainRuntimeBakePlan currentPlan =
            GetRuntimeBakeDiagnosticsPlan();

        TerrainRuntimeBakePlan initialPlan =
            TerrainRuntimeBakePipeline.InitialPlan;

        TerrainRuntimeBakePlan activePlan =
            TerrainRuntimeBakePipeline.CurrentPlan;

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Unified Runtime Bake Pipeline",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Is Running",
            TerrainRuntimeBakePipeline.IsRunning
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Pipeline Mode",
            TerrainRuntimeBakePipeline.CurrentMode.ToString()
        );

        EditorGUILayout.LabelField(
            "Current State",
            TerrainRuntimeBakePipeline.CurrentState.ToString()
        );

        EditorGUILayout.LabelField(
            "Current Stage",
            TerrainRuntimeBakePipeline.CurrentStageLabel
        );

        EditorGUILayout.LabelField(
            "Cancel Requested",
            TerrainRuntimeBakePipeline.CancelRequested
                ? "Yes"
                : "No"
        );

        Rect progressRect =
            EditorGUILayout.GetControlRect(
                false,
                18f
            );

        EditorGUI.ProgressBar(
            progressRect,
            TerrainRuntimeBakePipeline.CurrentOverallProgress,
            TerrainRuntimeBakePipeline.CurrentStageLabel
        );

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Initial Plan",
            TerrainRuntimeBakePipelineResult.BuildPlanSummary(
                initialPlan
            )
        );

        EditorGUILayout.LabelField(
            "Current / Next Work",
            TerrainRuntimeBakePipelineResult.BuildPlanSummary(
                TerrainRuntimeBakePipeline.IsRunning
                    ? activePlan
                    : currentPlan
            )
        );

        if (currentPlan != null && currentPlan.IsBlocked)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                currentPlan.BlockReason,
                MessageType.Warning
            );
        }

        GUILayout.Space(5f);

        bool startDisabled =
            TerrainRuntimeBakePipeline.IsRunning
            ||
            TerrainSurfaceMaskCompiler.IsGenerating;

        EditorGUI.BeginDisabledGroup(
            startDisabled
        );

        if (
            GUILayout.Button(
                "Bake Pending Changes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeBakePipeline.BakePendingChanges(
                    OnRuntimeBakePipelineCompleted
                );

            if (!started)
            {
                lastRuntimeBakePipelineResult =
                    TerrainRuntimeBakePipeline.LastResult;
            }

            Repaint();
        }

        if (
            GUILayout.Button(
                "Rebuild All Runtime Data",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started =
                TerrainRuntimeBakePipeline.RebuildAllRuntimeData(
                    OnRuntimeBakePipelineCompleted
                );

            if (!started)
            {
                lastRuntimeBakePipelineResult =
                    TerrainRuntimeBakePipeline.LastResult;
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !TerrainRuntimeBakePipeline.IsRunning
            ||
            TerrainRuntimeBakePipeline.CancelRequested
        );

        if (
            GUILayout.Button(
                "Request Cancel",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeBakePipeline.RequestCancel();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        TerrainRuntimeBakePipelineResult displayResult =
            lastRuntimeBakePipelineResult
            ??
            TerrainRuntimeBakePipeline.LastResult;

        if (displayResult != null)
        {
            GUILayout.Space(5f);

            EditorGUILayout.LabelField(
                "Last Outcome",
                displayResult.Outcome.ToString()
            );

            EditorGUILayout.LabelField(
                "Last Duration",
                displayResult.DurationSeconds.ToString("0.00") +
                " seconds"
            );

            EditorGUILayout.LabelField(
                "Last Failed Stage",
                displayResult.FailedStage ==
                    TerrainRuntimeBakePipelineState.Idle
                        ? "None"
                        : displayResult.FailedStage.ToString()
            );

            EditorGUILayout.HelpBox(
                !string.IsNullOrEmpty(displayResult.ErrorMessage)
                    ? displayResult.ErrorMessage
                    : displayResult.SummaryMessage,
                GetRuntimeBakePipelineMessageType(
                    displayResult.Outcome
                )
            );
        }

        EditorGUILayout.HelpBox(
            "Package 08 orchestrates the existing runtime Height, Surface, Collision, Addressables, and targeted Scene Sync stages. Each planner-driven stage receives a fresh TerrainRuntimeBakePlan. Successful work is preserved if a later stage fails or is cancelled.",
            MessageType.None
        );

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            Repaint();
        }

        GUILayout.EndVertical();
    }

    private void OnRuntimeBakePipelineCompleted(
        TerrainRuntimeBakePipelineResult result
    )
    {
        lastRuntimeBakePipelineResult =
            result;

        LogRuntimeBakePipelineResult(
            result
        );

        Repaint();
    }

    private static void LogRuntimeBakePipelineResult(
        TerrainRuntimeBakePipelineResult result
    )
    {
        if (result == null)
        {
            Debug.LogError(
                "Unified runtime bake pipeline returned no result."
            );

            return;
        }

        string report =
            result.BuildDiagnosticReport();

        switch (result.Outcome)
        {
            case TerrainRuntimeBakePipelineOutcome.NoWork:
            case TerrainRuntimeBakePipelineOutcome.Completed:
                Debug.Log(
                    report
                );
                break;

            case TerrainRuntimeBakePipelineOutcome.CompletedWithWarnings:
            case TerrainRuntimeBakePipelineOutcome.Cancelled:
            case TerrainRuntimeBakePipelineOutcome.Blocked:
                Debug.LogWarning(
                    report
                );
                break;

            default:
                Debug.LogError(
                    report
                );
                break;
        }
    }

    private static MessageType GetRuntimeBakePipelineMessageType(
        TerrainRuntimeBakePipelineOutcome outcome
    )
    {
        switch (outcome)
        {
            case TerrainRuntimeBakePipelineOutcome.NoWork:
            case TerrainRuntimeBakePipelineOutcome.Completed:
                return MessageType.Info;

            case TerrainRuntimeBakePipelineOutcome.CompletedWithWarnings:
            case TerrainRuntimeBakePipelineOutcome.Cancelled:
            case TerrainRuntimeBakePipelineOutcome.Blocked:
                return MessageType.Warning;

            default:
                return MessageType.Error;
        }
    }
}
