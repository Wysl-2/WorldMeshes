using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showValidationSuites;

    [SerializeField]
    private bool showOutputIntegrityValidation;

    [SerializeField]
    private bool showAuthoringPreviewValidation;

    [SerializeField]
    private bool showCompositionValidation;

    [SerializeField]
    private bool showRegionalElevationValidation;

    [SerializeField]
    private bool showStampValidation;

    [SerializeField]
    private bool showRuntimeStreamingValidation;

    private TerrainValidationSuiteRunner
        authoringPreviewValidationRunner;

    private TerrainValidationSuiteRunner
        authoringCompositionValidationRunner;

    private TerrainValidationSuiteRunner
        regionalElevationValidationRunner;

    private TerrainValidationSuiteRunner
        stampValidationRunner;

    private TerrainValidationSuiteRunner
        runtimeOutputValidationRunner;

    private TerrainRuntimeStreamingValidationSession
        runtimeStreamingValidationSession;

    private TerrainValidationRunSummary runtimeCollisionPhysicsSummary =
        TerrainValidationRunSummary.CreateNotRun();

    private bool authoringStreamingStressCaptureActive;
    private long authoringStreamingStressBaselineCreateCount;
    private long authoringStreamingStressBaselineDisposeCount;
    private int authoringStreamingStressBaselineLiveCount;

    private string authoringStreamingStressSummary =
        "No resource stress capture is active.";

    private MessageType authoringStreamingStressMessageType =
        MessageType.None;

    private void DrawValidationSuites()
    {
        showValidationSuites =
            EditorGUILayout.Foldout(
                showValidationSuites,
                "Validation",
                true
            );

        if (!showValidationSuites)
        {
            return;
        }

        GUILayout.Space(5f);
        DrawOutputIntegrityValidation();
        DrawWorkspaceSectionGap();
        DrawAuthoringPreviewValidation();
        DrawWorkspaceSectionGap();
        DrawCompositionValidation();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationValidation();
        DrawWorkspaceSectionGap();
        DrawStampValidation();
        DrawWorkspaceSectionGap();
        DrawRuntimeStreamingValidation();
    }

    private void DrawOutputIntegrityValidation()
    {
        showOutputIntegrityValidation =
            EditorGUILayout.Foldout(
                showOutputIntegrityValidation,
                "Output Integrity",
                true
            );

        if (!showOutputIntegrityValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawValidationSuite(
            "Runtime Output Validation",
            "Run Runtime Output Validation",
            GetRuntimeOutputValidationRunner(),
            "Runs explicit deep generated-output checks for Height content, Height and Surface streaming pyramids, physical height-range metadata, composed runtime Height, collision seams, and clipmap geometry. These checks can scan large generated datasets and only run when requested.",
            TerrainRuntimeBakePipeline.IsRunning
                || TerrainSurfaceMaskCompiler.IsGenerating
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
        );

        DrawWorkspaceSectionGap();
        DrawRuntimeCollisionPhysicsValidation();
    }

    private void DrawRuntimeCollisionPhysicsValidation()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Collision Physics",
            EditorStyles.boldLabel
        );

        DrawValidationRunSummary(
            runtimeCollisionPhysicsSummary
        );

        EditorGUILayout.HelpBox(
            "Runs the existing collision physics, raycast, transform, and runtime seam checks against the live generated hierarchy. This validation is available only in Play Mode.",
            MessageType.None
        );

        EditorGUI.BeginDisabledGroup(
            !EditorApplication.isPlaying
            || worldSettings == null
            || IsValidationSuiteRunning()
        );

        if (
            GUILayout.Button(
                "Validate Runtime Collision Physics",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            runtimeCollisionPhysicsSummary =
                TerrainValidationRunSummary.CreateRunning(
                    "Runtime Collision Physics is running."
                );

            try
            {
                bool passed =
                    TerrainCollisionPhysicsValidator
                        .ValidateRuntimeCollisionPhysics(
                            worldSettings
                        );

                runtimeCollisionPhysicsSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        passed ? 1 : 0,
                        passed ? 0 : 1,
                        0,
                        passed
                            ? "Runtime Collision Physics passed."
                            : "Runtime Collision Physics failed. See the Unity Console for details."
                    );
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);

                runtimeCollisionPhysicsSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        0,
                        1,
                        0,
                        "Runtime Collision Physics failed with an exception.\n\n" + exception
                    );
            }

            Repaint();
        }

        EditorGUI.EndDisabledGroup();
        GUILayout.EndVertical();
    }

    private void DrawAuthoringPreviewValidation()
    {
        showAuthoringPreviewValidation =
            EditorGUILayout.Foldout(
                showAuthoringPreviewValidation,
                "Authoring Preview & Streaming",
                true
            );

        if (!showAuthoringPreviewValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawValidationSuite(
            "Authoring Preview Regression",
            "Run Authoring Preview Regression",
            GetAuthoringPreviewValidationRunner(),
            "Runs the focused preview, residency, analysis-window, responsiveness, and streaming-policy checks sequentially. Detailed assertion output remains in the Unity Console."
        );

        DrawWorkspaceSectionGap();
        DrawManualAuthoringStreamingStress();
    }

    private void DrawCompositionValidation()
    {
        showCompositionValidation =
            EditorGUILayout.Foldout(
                showCompositionValidation,
                "Modifier Composition",
                true
            );

        if (!showCompositionValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawValidationSuite(
            "Authoring Composition Regression",
            "Run Composition Regression",
            GetAuthoringCompositionValidationRunner(),
            "Runs modifier data, mutation, GPU compositor, and blend-mode validation sequentially. Detailed assertion output remains in the Unity Console."
        );
    }

    private TerrainValidationSuiteRunner
        GetAuthoringPreviewValidationRunner()
    {
        if (authoringPreviewValidationRunner == null)
        {
            authoringPreviewValidationRunner =
                TerrainAuthoringPreviewRegressionSuite.CreateRunner();
        }

        return authoringPreviewValidationRunner;
    }

    private TerrainValidationSuiteRunner
        GetAuthoringCompositionValidationRunner()
    {
        if (authoringCompositionValidationRunner == null)
        {
            authoringCompositionValidationRunner =
                TerrainAuthoringCompositionRegressionSuite.CreateRunner();
        }

        return authoringCompositionValidationRunner;
    }

    private TerrainValidationSuiteRunner
        GetRegionalElevationValidationRunner()
    {
        if (regionalElevationValidationRunner == null)
        {
            regionalElevationValidationRunner =
                TerrainRegionalElevationRegressionSuite.CreateRunner();
        }

        return regionalElevationValidationRunner;
    }

    private TerrainValidationSuiteRunner
        GetStampValidationRunner()
    {
        if (stampValidationRunner == null)
        {
            stampValidationRunner =
                TerrainStampRegressionSuite.CreateRunner();
        }

        return stampValidationRunner;
    }

    private TerrainValidationSuiteRunner
        GetRuntimeOutputValidationRunner()
    {
        if (runtimeOutputValidationRunner == null)
        {
            runtimeOutputValidationRunner =
                TerrainRuntimeOutputValidationSuite.CreateRunner();
        }

        return runtimeOutputValidationRunner;
    }

    private TerrainRuntimeStreamingValidationSession
        GetRuntimeStreamingValidationSession()
    {
        if (runtimeStreamingValidationSession == null)
        {
            runtimeStreamingValidationSession =
                new TerrainRuntimeStreamingValidationSession(
                    Repaint
                );
        }

        return runtimeStreamingValidationSession;
    }

    private void ShutdownRuntimeStreamingValidationSession()
    {
        if (runtimeStreamingValidationSession == null)
        {
            return;
        }

        runtimeStreamingValidationSession.Shutdown();
        runtimeStreamingValidationSession = null;
    }

    private void DrawValidationSuite(
        string title,
        string buttonLabel,
        TerrainValidationSuiteRunner suiteRunner,
        string description,
        bool runDisabled = false
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            title,
            EditorStyles.boldLabel
        );

        DrawValidationRunSummary(
            suiteRunner.Summary
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            runDisabled
            || IsValidationSuiteRunning()
            || Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                buttonLabel,
                GUILayout.ExpandWidth(true)
            )
        )
        {
            suiteRunner.Start();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(6f);

        for (
            int index = 0;
            index < suiteRunner.CaseCount;
            index++
        )
        {
            DrawValidationSuiteCaseSummary(
                suiteRunner.GetCaseName(index),
                suiteRunner.GetCaseSummary(index)
            );
        }

        if (!string.IsNullOrEmpty(description))
        {
            EditorGUILayout.HelpBox(
                description,
                MessageType.None
            );
        }

        GUILayout.EndVertical();
    }

    private static void DrawValidationRunSummary(
        TerrainValidationRunSummary summary
    )
    {
        if (summary == null)
        {
            return;
        }

        EditorGUILayout.LabelField(
            "Status",
            summary.State.ToString()
        );

        EditorGUILayout.LabelField(
            "Passed",
            summary.PassedCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            summary.FailedCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Blocked",
            summary.BlockedCount.ToString()
        );

        if (!string.IsNullOrEmpty(summary.Summary))
        {
            EditorGUILayout.HelpBox(
                summary.Summary,
                summary.State == TerrainValidationRunState.Failed
                    ? MessageType.Error
                    : summary.State == TerrainValidationRunState.Blocked
                        ? MessageType.Warning
                        : MessageType.Info
            );
        }
    }

    private static void DrawValidationSuiteCaseSummary(
        string caseName,
        TerrainValidationRunSummary summary
    )
    {
        if (summary == null)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.LabelField(
            caseName,
            summary.State.ToString()
        );

        EditorGUILayout.LabelField(
            "Counts",
            summary.PassedCount.ToString() +
            " passed / " +
            summary.FailedCount.ToString() +
            " failed / " +
            summary.BlockedCount.ToString() +
            " blocked"
        );

        if (
            (summary.State == TerrainValidationRunState.Failed
                || summary.State == TerrainValidationRunState.Blocked)
            && !string.IsNullOrEmpty(summary.Summary)
        )
        {
            EditorGUILayout.HelpBox(
                summary.Summary,
                summary.State == TerrainValidationRunState.Failed
                    ? MessageType.Error
                    : MessageType.Warning
            );
        }

        GUILayout.EndVertical();
    }

    private bool IsValidationSuiteRunning()
    {
        return
            (authoringPreviewValidationRunner != null
                && authoringPreviewValidationRunner.IsRunning)
            ||
            (authoringCompositionValidationRunner != null
                && authoringCompositionValidationRunner.IsRunning)
            ||
            (regionalElevationValidationRunner != null
                && regionalElevationValidationRunner.IsRunning)
            ||
            (stampValidationRunner != null
                && stampValidationRunner.IsRunning)
            ||
            (runtimeOutputValidationRunner != null
                && runtimeOutputValidationRunner.IsRunning)
            ||
            (runtimeStreamingValidationSession != null
                && runtimeStreamingValidationSession.IsRunning);
    }

    private void DrawManualAuthoringStreamingStress()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Manual Streaming Stress",
            EditorStyles.boldLabel
        );

        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();

        EditorGUILayout.LabelField(
            "Capture",
            authoringStreamingStressCaptureActive
                ? "Active"
                : "Inactive"
        );

        EditorGUILayout.LabelField(
            "Current Created",
            snapshot.CacheCreateCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Current Disposed",
            snapshot.CacheDisposeCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Current Live",
            snapshot.CacheLiveCount.ToString("N0")
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            IsValidationSuiteRunning()
            || Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Begin Resource Stress Capture",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            authoringStreamingStressBaselineCreateCount =
                snapshot.CacheCreateCount;

            authoringStreamingStressBaselineDisposeCount =
                snapshot.CacheDisposeCount;

            authoringStreamingStressBaselineLiveCount =
                snapshot.CacheLiveCount;

            authoringStreamingStressCaptureActive =
                true;

            authoringStreamingStressSummary =
                "Capture started. Move rapidly through the world, reverse direction, perform distant jumps, allow transitions to complete or cancel, then let streaming settle and validate the capture.";

            authoringStreamingStressMessageType =
                MessageType.Info;
        }

        EditorGUI.BeginDisabledGroup(
            !authoringStreamingStressCaptureActive
        );

        if (
            GUILayout.Button(
                "Validate Resource Stress Capture",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            ValidateAuthoringStreamingStressCapture(
                snapshot
            );
        }

        EditorGUI.EndDisabledGroup();
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            authoringStreamingStressSummary,
            authoringStreamingStressMessageType
        );

        EditorGUILayout.HelpBox(
            "Manual lifecycle / ownership checks:\n\n" +
            "• Start staging, then trigger compilation. Streaming should pause while the healthy active cache remains resident, then current intent should be reevaluated.\n\n" +
            "• Start staging, then trigger assembly/domain reload. Editor preview resources should be released and reconstructed without stranded caches.\n\n" +
            "• Disable Height Preview during staging. Live cache count should settle at 0. Re-enable and allow a fresh local cache to settle at 1.\n\n" +
            "• Change active scene during staging. Previous-scene active/staging resources must not survive.\n\n" +
            "• Switch between Scene Views during staging, then close/reopen views. Ownership generation should change and obsolete targets must not return.\n\n" +
            "• Enter Play Mode during staging. Editor live cache count should reach 0 while runtime streaming owns terrain residency. Returning to Edit Mode should reconstruct one local editor cache when Height Preview is enabled.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private void ValidateAuthoringStreamingStressCapture(
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot
    )
    {
        if (
            snapshot.IsStreaming
            || snapshot.HasStagingWindow
        )
        {
            authoringStreamingStressSummary =
                "BLOCKED - streaming has not settled yet. Wait until no staging cache remains, then validate again.";

            authoringStreamingStressMessageType =
                MessageType.Warning;

            return;
        }

        long createdDelta =
            snapshot.CacheCreateCount -
            authoringStreamingStressBaselineCreateCount;

        long disposedDelta =
            snapshot.CacheDisposeCount -
            authoringStreamingStressBaselineDisposeCount;

        int expectedLive =
            snapshot.CacheReady
                ? 1
                : 0;

        bool passed =
            snapshot.CacheLiveCount == expectedLive
            && snapshot.CacheLiveCount <= 1;

        authoringStreamingStressSummary =
            (passed ? "PASS" : "FAIL") +
            $" - Created delta={createdDelta:N0}; " +
            $"Disposed delta={disposedDelta:N0}; " +
            $"Live before={authoringStreamingStressBaselineLiveCount:N0}; " +
            $"Live after={snapshot.CacheLiveCount:N0}; " +
            $"Expected settled live={expectedLive:N0}.";

        authoringStreamingStressMessageType =
            passed
                ? MessageType.Info
                : MessageType.Error;

        if (passed)
        {
            authoringStreamingStressCaptureActive =
                false;
        }
    }

    private void DrawRegionalElevationValidation()
    {
        showRegionalElevationValidation =
            EditorGUILayout.Foldout(
                showRegionalElevationValidation,
                "Regional Elevation",
                true
            );

        if (!showRegionalElevationValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawValidationSuite(
            "Regional Elevation Regression",
            "Run Regional Elevation Regression",
            GetRegionalElevationValidationRunner(),
            "Runs Regional Elevation data, initialization, interpolation, triangulation, CPU/GPU parity, composition, management, multi-selection, and Scene tool checks sequentially. Detailed assertion output remains in the Unity Console."
        );
    }

    private void DrawStampValidation()
    {
        showStampValidation =
            EditorGUILayout.Foldout(
                showStampValidation,
                "Stamps",
                true
            );

        if (!showStampValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawValidationSuite(
            "Stamp Regression",
            "Run Stamp Regression",
            GetStampValidationRunner(),
            "Runs stamp defaults, library, synchronization, browser, rotation, source orientation/remapping, falloff, and authoring-default checks sequentially. Live Stamp integration remains under Advanced Validation."
        );
    }

    private void DrawRuntimeStreamingValidation()
    {
        showRuntimeStreamingValidation =
            EditorGUILayout.Foldout(
                showRuntimeStreamingValidation,
                "Runtime Streaming",
                true
            );

        if (!showRuntimeStreamingValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainRuntimeStreamingValidationSession session =
            GetRuntimeStreamingValidationSession();

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to run Runtime Streaming validation.",
                MessageType.Info
            );
        }

        DrawRuntimeStreamingFocusedValidation(
            session
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeStreamingStressTest(
            session
        );
    }

    private void DrawRuntimeStreamingFocusedValidation(
        TerrainRuntimeStreamingValidationSession session
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Streaming Validation",
            EditorStyles.boldLabel
        );

        DrawRuntimeValidationStatus(
            "Overall",
            session.ValidationStatus,
            session.ValidationSummary
        );

        DrawRuntimeValidationStatus(
            "Height Cache",
            session.HeightCacheStatus,
            session.HeightCacheSummary
        );

        DrawRuntimeValidationStatus(
            "Cross-Resolution",
            session.CrossResolutionStatus,
            session.CrossResolutionSummary
        );

        DrawRuntimeValidationStatus(
            "Renderer / Height / Surface / Stitch",
            session.MultiresolutionStatus,
            session.MultiresolutionSummary
        );

        DrawRuntimeValidationStatus(
            "Independent Anchor Stress",
            session.IndependentAnchorStatus,
            session.IndependentAnchorSummary
        );

        DrawRuntimeValidationStatus(
            "Terrain Scheduler Stress",
            session.SchedulerStressStatus,
            session.SchedulerStressSummary
        );

        EditorGUI.BeginDisabledGroup(
            !EditorApplication.isPlaying
            || IsValidationSuiteRunning()
        );

        if (
            GUILayout.Button(
                "Validate Runtime Streaming",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            session.BeginValidation();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "Runs Height cache and cross-resolution checks, Height/Surface renderer and stitch validation, independent-anchor validation, and combined Height/Surface scheduler stress sequentially. Validation components are attached to the generated Clipmap only for the duration of this explicit Play Mode session.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private void DrawRuntimeStreamingStressTest(
        TerrainRuntimeStreamingValidationSession session
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Streaming Stress Test",
            EditorStyles.boldLabel
        );

        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        if (streamer != null)
        {
            TerrainHeightDeferredReleaseDiagnosticsSnapshot deferred =
                streamer.GetHeightDeferredReleaseDiagnostics();

            GUILayout.Label(
                "Deferred Height Source Releases",
                EditorStyles.boldLabel
            );

            EditorGUILayout.LabelField(
                "Pending Releases",
                deferred.PendingCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Pending Payload",
                FormatDiagnosticsBytes(
                    deferred.EstimatedPendingSourceBytes
                )
            );

            EditorGUILayout.LabelField(
                "Peak Pending Releases",
                deferred.PeakPendingCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Peak Pending Payload",
                FormatDiagnosticsBytes(
                    deferred.PeakEstimatedPendingSourceBytes
                )
            );

            EditorGUILayout.LabelField(
                "Enqueued / Released",
                $"{deferred.EnqueuedCount} / {deferred.ReleasedCount}"
            );

            EditorGUILayout.LabelField(
                "Forced Releases",
                deferred.ForcedReleaseCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Fence Fallbacks",
                deferred.FenceFallbackCount.ToString()
            );

            GUILayout.Space(6f);
        }

        EditorGUILayout.LabelField(
            "Overall Status",
            session.StressStatus.ToString()
        );

        TerrainRuntimeStreamingStressResult result =
            session.StressResult;

        if (result != null)
        {
            EditorGUILayout.LabelField(
                "Boundary Stress",
                result.BoundaryStressStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Continuous Movement",
                result.ContinuousMovementStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Rapid Supersession",
                result.RapidSupersessionStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Repeated Transitions",
                result.RepeatedTransitionStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Deferred Release",
                result.DeferredReleaseStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Streamer Lifecycle",
                result.StreamerLifecycleStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Final Restore",
                result.FinalRestoreStatus.ToString()
            );

            EditorGUILayout.HelpBox(
                result.BuildDiagnosticReport(),
                MessageTypeForRuntimeValidationStatus(
                    result.OverallStatus
                )
            );
        }
        else if (!string.IsNullOrEmpty(session.StressSummary))
        {
            EditorGUILayout.HelpBox(
                session.StressSummary,
                MessageTypeForRuntimeValidationStatus(
                    session.StressStatus
                )
            );
        }

        EditorGUI.BeginDisabledGroup(
            !EditorApplication.isPlaying
            || IsValidationSuiteRunning()
        );

        if (
            GUILayout.Button(
                "Run Runtime Streaming Stress Test",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            session.BeginStressTest();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "Runs the explicit boundary, continuous-movement, rapid-request, repeated-transition, deferred-release, and streamer-lifecycle stress workflow. The temporary runtime validation component is removed after the result is captured.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static void DrawRuntimeValidationStatus(
        string label,
        TerrainRuntimeValidationStatus status,
        string summary
    )
    {
        EditorGUILayout.LabelField(
            label,
            status.ToString()
        );

        if (
            status != TerrainRuntimeValidationStatus.NotRun
            && !string.IsNullOrEmpty(summary)
        )
        {
            EditorGUILayout.HelpBox(
                summary,
                MessageTypeForRuntimeValidationStatus(status)
            );
        }
    }

    private static MessageType MessageTypeForRuntimeValidationStatus(
        TerrainRuntimeValidationStatus status
    )
    {
        switch (status)
        {
            case TerrainRuntimeValidationStatus.Failed:
                return MessageType.Error;

            case TerrainRuntimeValidationStatus.Running:
            case TerrainRuntimeValidationStatus.Passed:
                return MessageType.Info;

            case TerrainRuntimeValidationStatus.Inconclusive:
                return MessageType.Warning;

            default:
                return MessageType.None;
        }
    }
}
