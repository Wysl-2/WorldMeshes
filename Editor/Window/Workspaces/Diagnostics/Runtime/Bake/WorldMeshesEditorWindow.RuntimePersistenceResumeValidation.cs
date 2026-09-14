using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private TerrainRuntimeBakePersistenceValidationResult
        lastRuntimePersistenceValidationResult;

    private TerrainRuntimeBakeResumeValidationResult
        lastRuntimeResumeValidationResult;

    private void DrawRuntimePersistenceResumeValidationDiagnostics()
    {
        TerrainRuntimeBakeStateSummary snapshot =
            GetRuntimeBakeDiagnosticsSummary();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Persistence + Resume Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Package 10.2 validates that pending work survives editor lifecycle events and that cancelled/failed incremental bakes resume without repeating safely acknowledged work. Validation hooks are transient, editor-only, and disabled during normal Runtime baking.",
            MessageType.None
        );

        GUILayout.Label("Current Persistent Bake State", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("State Revision", snapshot.StateRevision.ToString());
        EditorGUILayout.LabelField("Pending Height Tiles", snapshot.PendingHeightTileCount.ToString());
        EditorGUILayout.LabelField("Pending Surface Tiles", snapshot.PendingSurfaceTileCount.ToString());
        EditorGUILayout.LabelField("Pending Collision Chunks", snapshot.PendingCollisionChunkCount.ToString());
        EditorGUILayout.LabelField("Full Height", snapshot.FullHeightRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Full Surface", snapshot.FullSurfaceRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Full Collision", snapshot.FullCollisionRebuildRequired ? "Required" : "No");
        EditorGUILayout.LabelField("Addressables Configuration", snapshot.AddressablesConfigurationDirty ? "Dirty" : "Current");
        EditorGUILayout.LabelField("Addressables Content", snapshot.AddressablesContentDirty ? "Dirty" : "Current");
        EditorGUILayout.LabelField("Runtime Scene Metadata", snapshot.RuntimeSceneMetadataDirty ? "Dirty" : "Current");

        GUILayout.Space(6f);
        GUILayout.Label("Persistence Checkpoint", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Checkpoint",
            TerrainRuntimeBakePersistenceValidationUtility.CheckpointExists
                ? "Captured"
                : "Not Captured"
        );

        bool busy =
            TerrainRuntimeBakeResumeValidationUtility.IsRunning
            || TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating;

        EditorGUI.BeginDisabledGroup(busy || EditorApplication.isPlayingOrWillChangePlaymode);

        if (GUILayout.Button("Capture Persistence Checkpoint"))
        {
            if (
                TerrainRuntimeBakePersistenceValidationUtility.CaptureCheckpoint(
                    out TerrainRuntimeBakePersistenceCheckpoint checkpoint,
                    out string errorMessage
                )
            )
            {
                Debug.Log(
                    "WorldMeshes runtime bake persistence checkpoint captured.\n" +
                    "State Revision: " + checkpoint.stateRevision + "\n" +
                    "Height Pending: " + checkpoint.pendingHeightTiles.Count + "\n" +
                    "Surface Pending: " + checkpoint.pendingSurfaceTiles.Count + "\n" +
                    "Collision Pending: " + checkpoint.pendingCollisionChunks.Count
                );
            }
            else
            {
                Debug.LogError(errorMessage);
            }

            Repaint();
        }

        if (GUILayout.Button("Compare Current State To Checkpoint"))
        {
            lastRuntimePersistenceValidationResult =
                TerrainRuntimeBakePersistenceValidationUtility
                    .CompareCurrentStateToCheckpoint();

            LogRuntimePersistenceValidationResult(
                lastRuntimePersistenceValidationResult
            );

            Repaint();
        }

        if (GUILayout.Button("Clear Persistence Checkpoint"))
        {
            if (
                !TerrainRuntimeBakePersistenceValidationUtility.ClearCheckpoint(
                    out string errorMessage
                )
            )
            {
                Debug.LogError(errorMessage);
            }

            lastRuntimePersistenceValidationResult = null;
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (lastRuntimePersistenceValidationResult != null)
        {
            EditorGUILayout.HelpBox(
                lastRuntimePersistenceValidationResult.SummaryMessage,
                lastRuntimePersistenceValidationResult.OverallPassed
                    ? MessageType.Info
                    : MessageType.Warning
            );
        }

        EditorGUILayout.HelpBox(
            "Assembly reload test: capture a checkpoint, trigger a normal script reload/recompile, return here, then compare.\n\nEditor-window test: capture, close this WorldMeshes window, reopen it, then compare.\n\nUnity restart test: capture, close Unity normally, reopen the project, then compare. The restart step is intentionally manual.",
            MessageType.None
        );

        GUILayout.Space(8f);
        GUILayout.Label("Cancellation + Resume", EditorStyles.boldLabel);

        DrawResumeValidationButton(
            "Validate Height Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.HeightCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Surface Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.SurfaceCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Collision Cancellation + Resume",
            TerrainRuntimeBakeResumeValidationScenario.CollisionCancellation,
            busy
        );

        DrawResumeValidationButton(
            "Validate Cancel During Addressables",
            TerrainRuntimeBakeResumeValidationScenario.CancelDuringAddressables,
            busy
        );

        DrawResumeValidationButton(
            "Validate Cancel Before Scene Sync",
            TerrainRuntimeBakeResumeValidationScenario.CancelBeforeSceneSync,
            busy
        );

        GUILayout.Space(6f);
        GUILayout.Label("Controlled Failure + Resume", EditorStyles.boldLabel);

        DrawResumeValidationButton(
            "Validate Failure After Height",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterHeight,
            busy
        );

        DrawResumeValidationButton(
            "Validate Failure After Surface",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterSurface,
            busy
        );

        DrawResumeValidationButton(
            "Validate Failure After Collision",
            TerrainRuntimeBakeResumeValidationScenario.FailureAfterCollision,
            busy
        );

        DrawResumeValidationButton(
            "Validate Addressables Failure Resume",
            TerrainRuntimeBakeResumeValidationScenario.AddressablesFailureResume,
            busy
        );

        DrawResumeValidationButton(
            "Validate Scene Sync Failure Resume",
            TerrainRuntimeBakeResumeValidationScenario.SceneSyncFailureResume,
            busy
        );

        if (TerrainRuntimeBakeResumeValidationUtility.IsRunning)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Scenario",
                TerrainRuntimeBakeResumeValidationUtility.CurrentScenario.ToString()
            );
            EditorGUILayout.LabelField(
                "Phase",
                TerrainRuntimeBakeResumeValidationUtility.CurrentPhase
            );
            EditorGUILayout.LabelField(
                "Pipeline Stage",
                TerrainRuntimeBakePipeline.CurrentStageLabel
            );

            Rect progressRect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.ProgressBar(
                progressRect,
                TerrainRuntimeBakePipeline.CurrentOverallProgress,
                TerrainRuntimeBakePipeline.CurrentStageLabel
            );

            Repaint();
        }

        TerrainRuntimeBakeResumeValidationResult displayResume =
            lastRuntimeResumeValidationResult
            ?? TerrainRuntimeBakeResumeValidationUtility.LastResult;

        if (displayResume != null)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Last Scenario", displayResume.Scenario.ToString());
            EditorGUILayout.LabelField("Last Outcome", displayResume.Outcome.ToString());
            EditorGUILayout.LabelField(
                "Repeated Height / Surface / Collision",
                displayResume.RepeatedHeightCoordinates.Count + " / " +
                displayResume.RepeatedSurfaceCoordinates.Count + " / " +
                displayResume.RepeatedCollisionCoordinates.Count
            );
            EditorGUILayout.LabelField("Upstream Stage Repeated", displayResume.UpstreamStageRepeated ? "Yes" : "No");
            EditorGUILayout.LabelField("Final Plan Current", displayResume.FinalPlanCurrent ? "Yes" : "No");

            EditorGUILayout.HelpBox(
                !string.IsNullOrEmpty(displayResume.ErrorMessage)
                    ? displayResume.ErrorMessage
                    : displayResume.SummaryMessage,
                displayResume.Passed
                    ? MessageType.Info
                    : MessageType.Warning
            );

            if (GUILayout.Button("Log Last Resume Validation Report"))
            {
                LogRuntimeResumeValidationResult(displayResume);
            }
        }

        EditorGUILayout.HelpBox(
            "Prepare pending Incremental work manually before individual cancellation/failure tests. Package 10.2 does not move/delete/toggle terrain modifiers, alter settings, or damage generated assets, Addressables configuration, or hierarchy objects.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private void DrawResumeValidationButton(
        string label,
        TerrainRuntimeBakeResumeValidationScenario scenario,
        bool busy
    )
    {
        EditorGUI.BeginDisabledGroup(
            busy || EditorApplication.isPlayingOrWillChangePlaymode
        );

        if (GUILayout.Button(label))
        {
            bool started =
                TerrainRuntimeBakeResumeValidationUtility.StartScenario(
                    scenario,
                    OnRuntimeResumeValidationCompleted
                );

            if (!started)
            {
                lastRuntimeResumeValidationResult =
                    TerrainRuntimeBakeResumeValidationUtility.LastResult;

                if (lastRuntimeResumeValidationResult != null)
                {
                    LogRuntimeResumeValidationResult(
                        lastRuntimeResumeValidationResult
                    );
                }
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();
    }

    private void OnRuntimeResumeValidationCompleted(
        TerrainRuntimeBakeResumeValidationResult result
    )
    {
        lastRuntimeResumeValidationResult = result;
        LogRuntimeResumeValidationResult(result);
        Repaint();
    }

    private static void LogRuntimePersistenceValidationResult(
        TerrainRuntimeBakePersistenceValidationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError("Runtime bake persistence validation returned no result.");
            return;
        }

        string report = result.BuildDiagnosticReport();

        if (result.OverallPassed)
        {
            Debug.Log(report);
        }
        else if (result.Outcome == TerrainRuntimeBakePersistenceValidationOutcome.Blocked)
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }

    private static void LogRuntimeResumeValidationResult(
        TerrainRuntimeBakeResumeValidationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError("Runtime bake resume validation returned no result.");
            return;
        }

        string report = result.BuildDiagnosticReport();

        if (result.Passed)
        {
            Debug.Log(report);
        }
        else if (result.Outcome == TerrainRuntimeBakeResumeValidationOutcome.Blocked)
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }
}
