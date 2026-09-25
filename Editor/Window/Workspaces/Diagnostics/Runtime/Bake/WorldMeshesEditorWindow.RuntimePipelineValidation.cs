using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainRuntimeBakeValidationResult lastRuntimePipelineValidationResult;
    private TerrainRuntimeOutputFingerprintSnapshot runtimeFingerprintBaseline;
    private TerrainRuntimeOutputFingerprintComparison lastRuntimeFingerprintComparison;

    private void DrawRuntimePipelineValidationDiagnostics()
    {
        TerrainRuntimeBakePlan currentPlan =
            GetRuntimeBakeDiagnosticsPlan();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Pipeline Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Package 10.1 measures the canonical Package 08 pipeline without changing planner/generator policy. Full output fingerprint capture is explicit and read-only.",
            MessageType.None
        );

        EditorGUILayout.LabelField(
            "Current Bake Plan",
            TerrainRuntimeBakePipelineResult.BuildPlanSummary(currentPlan)
        );

        EditorGUILayout.LabelField(
            "Validation Bake Running",
            TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
                ? "Yes"
                : "No"
        );

        GUILayout.Space(5f);

        bool bakeDisabled =
            TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
            || TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating
            || EditorApplication.isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(bakeDisabled);

        if (
            GUILayout.Button(
                "Bake Pending Changes + Validate",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool started = TerrainRuntimeBakeValidationUtility.BakePendingChanges(
                OnRuntimePipelineValidationCompleted
            );

            if (!started)
            {
                lastRuntimePipelineValidationResult =
                    TerrainRuntimeBakeValidationUtility.LastResult;
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            TerrainRuntimeBakePipeline.IsRunning
            || TerrainRuntimeBakePipeline.LastResult == null
        );

        if (
            GUILayout.Button(
                "Validate Last Unified Pipeline Result",
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

        bool fingerprintDisabled =
            TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating
            || EditorApplication.isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(fingerprintDisabled);

        if (
            GUILayout.Button(
                "Capture Runtime Output Fingerprints",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            runtimeFingerprintBaseline =
                TerrainRuntimeOutputFingerprintUtility.CaptureCurrentSnapshot(
                    worldSettings
                );

            lastRuntimeFingerprintComparison = null;
            LogRuntimeFingerprintSnapshot(runtimeFingerprintBaseline);
            Repaint();
        }

        EditorGUI.BeginDisabledGroup(runtimeFingerprintBaseline == null);

        if (
            GUILayout.Button(
                "Compare Current Output To Captured Baseline",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeOutputFingerprintSnapshot current =
                TerrainRuntimeOutputFingerprintUtility.CaptureCurrentSnapshot(
                    worldSettings
                );

            lastRuntimeFingerprintComparison =
                TerrainRuntimeOutputFingerprintUtility.Compare(
                    runtimeFingerprintBaseline,
                    current
                );

            LogRuntimeFingerprintComparison(lastRuntimeFingerprintComparison);
            Repaint();
        }

        if (
            GUILayout.Button(
                "Clear Captured Fingerprint Baseline",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            runtimeFingerprintBaseline = null;
            lastRuntimeFingerprintComparison = null;
            Repaint();
        }

        EditorGUI.EndDisabledGroup();
        EditorGUI.EndDisabledGroup();

        DrawRuntimePipelineValidationResultSummary();
        DrawRuntimePipelineTimingSummary();
        DrawRuntimeFingerprintSummary();

        if (
            TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
            || TerrainRuntimeBakePipeline.IsRunning
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
            "Raw duplicate coordinate submission is not observable because existing Height/Surface/Collision result contracts expose canonical unique coordinate sets. Package 10.1 does not change generator behavior solely for this diagnostic metric.",
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

    private void DrawRuntimeFingerprintSummary()
    {
        if (runtimeFingerprintBaseline == null)
        {
            return;
        }

        GUILayout.Space(8f);
        GUILayout.Label("Captured Runtime Output", EditorStyles.boldLabel);

        EditorGUILayout.LabelField(
            "Complete",
            runtimeFingerprintBaseline.IsComplete ? "Yes" : "No"
        );

        EditorGUILayout.LabelField(
            "Heightmaps",
            runtimeFingerprintBaseline.HeightAssetCount +
            " / " + runtimeFingerprintBaseline.ExpectedHeightAssetCount
        );

        EditorGUILayout.LabelField(
            "Height Streaming",
            runtimeFingerprintBaseline.HeightStreamingAssetCount +
            " / " + runtimeFingerprintBaseline.ExpectedHeightStreamingAssetCount
        );

        EditorGUILayout.LabelField(
            "Surface Masks",
            runtimeFingerprintBaseline.SurfaceAssetCount +
            " / " + runtimeFingerprintBaseline.ExpectedSurfaceAssetCount
        );

        EditorGUILayout.LabelField(
            "Collision",
            runtimeFingerprintBaseline.CollisionAssetCount +
            " / " + runtimeFingerprintBaseline.ExpectedCollisionAssetCount
        );

        DrawRuntimeFingerprintHash(
            "Height Dataset Hash",
            runtimeFingerprintBaseline.HeightDatasetFingerprint
        );

        DrawRuntimeFingerprintHash(
            "Height Streaming Dataset Hash",
            runtimeFingerprintBaseline.HeightStreamingDatasetFingerprint
        );

        DrawRuntimeFingerprintHash(
            "Surface Dataset Hash",
            runtimeFingerprintBaseline.SurfaceDatasetFingerprint
        );

        DrawRuntimeFingerprintHash(
            "Collision Dataset Hash",
            runtimeFingerprintBaseline.CollisionDatasetFingerprint
        );

        if (runtimeFingerprintBaseline.Issues.Count > 0)
        {
            EditorGUILayout.HelpBox(
                "Fingerprint baseline is incomplete. Issues: " +
                runtimeFingerprintBaseline.Issues.Count +
                ". See the Console report for details.",
                MessageType.Warning
            );
        }

        if (lastRuntimeFingerprintComparison != null)
        {
            GUILayout.Space(5f);
            GUILayout.Label("Baseline Comparison", EditorStyles.boldLabel);

            DrawRuntimeFingerprintComparisonRow(
                lastRuntimeFingerprintComparison.Height
            );

            DrawRuntimeFingerprintComparisonRow(
                lastRuntimeFingerprintComparison.Surface
            );

            DrawRuntimeFingerprintComparisonRow(
                lastRuntimeFingerprintComparison.Collision
            );

            EditorGUILayout.HelpBox(
                lastRuntimeFingerprintComparison.ExactMatch
                    ? "Current generated runtime output exactly matches the captured fingerprint baseline."
                    : "Current generated runtime output differs from the captured baseline. See the Console report for stream counts.",
                lastRuntimeFingerprintComparison.ExactMatch
                    ? MessageType.Info
                    : MessageType.Warning
            );
        }
    }

    private static void DrawRuntimeFingerprintHash(
        string label,
        string hash
    )
    {
        EditorGUILayout.LabelField(label);
        EditorGUILayout.SelectableLabel(
            string.IsNullOrEmpty(hash) ? "Unavailable" : hash,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight)
        );
    }

    private static void DrawRuntimeFingerprintComparisonRow(
        TerrainRuntimeOutputFingerprintStreamComparison stream
    )
    {
        if (stream == null)
        {
            return;
        }

        EditorGUILayout.LabelField(
            stream.StreamName,
            "Matching " + stream.Matching.Count +
            ", Missing " + stream.Missing.Count +
            ", Unexpected " + stream.Unexpected.Count +
            ", Mismatched " + stream.PayloadMismatches.Count
        );
    }

    private void OnRuntimePipelineValidationCompleted(
        TerrainRuntimeBakeValidationResult result
    )
    {
        lastRuntimePipelineValidationResult = result;
        LogRuntimePipelineValidationResult(result);
        Repaint();
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

    private static void LogRuntimeFingerprintSnapshot(
        TerrainRuntimeOutputFingerprintSnapshot snapshot
    )
    {
        if (snapshot == null)
        {
            Debug.LogError("Runtime output fingerprint capture returned no snapshot.");
            return;
        }

        string report = snapshot.BuildDiagnosticReport();

        if (snapshot.IsComplete)
        {
            Debug.Log(report);
        }
        else
        {
            Debug.LogWarning(report);
        }
    }

    private static void LogRuntimeFingerprintComparison(
        TerrainRuntimeOutputFingerprintComparison comparison
    )
    {
        if (comparison == null)
        {
            Debug.LogError("Runtime output fingerprint comparison returned no result.");
            return;
        }

        string report = comparison.BuildDiagnosticReport();

        if (comparison.ExactMatch)
        {
            Debug.Log(report);
        }
        else
        {
            Debug.LogWarning(report);
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
