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
        DrawRuntimeHeightRangeMetadataValidation();

        EditorGUILayout.HelpBox(
            "Additional generated-output regression validation will be presented here as the existing runtime output validators are consolidated.",
            MessageType.None
        );
    }

    private void DrawRuntimeHeightRangeMetadataValidation()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Height Range Metadata",
            EditorStyles.boldLabel
        );

        if (manifest == null)
        {
            EditorGUILayout.LabelField(
                "Manifest",
                "Not Generated"
            );
        }
        else
        {
            EditorGUILayout.LabelField(
                "Manifest Complete",
                manifest.isComplete ? "Yes" : "No"
            );

            EditorGUILayout.LabelField(
                "Expected Range Records",
                manifest.ExpectedTileHeightRangeCount.ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Stored Range Records",
                manifest.TileHeightRangeCount.ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Valid Range Records",
                manifest.ValidTileHeightRangeCount.ToString("N0")
            );

            if (manifest.HasValidHeightRange)
            {
                EditorGUILayout.LabelField(
                    "Global Range",
                    manifest.minimumTerrainHeight.ToString("R") +
                    " -> " +
                    manifest.maximumTerrainHeight.ToString("R")
                );
            }
        }

        GUILayout.Space(5f);

        bool validationDisabled =
            worldSettings == null
            || TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating
            || EditorApplication.isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(validationDisabled);

        if (
            GUILayout.Button(
                "Validate Runtime Height Range Metadata",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeHeightRangeMetadataValidator.Validate(
                worldSettings,
                true
            );
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "This explicit output-integrity check compares runtime height-range metadata against the physical generated height textures. It can scan every runtime height tile and only runs when requested.",
            MessageType.None
        );

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
            "Runs the focused preview, residency, analysis-window, responsiveness, and streaming-integration checks sequentially. Detailed assertion output remains in the Unity Console."
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

    private void DrawValidationSuite(
        string title,
        string buttonLabel,
        TerrainValidationSuiteRunner suiteRunner,
        string description
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
            IsValidationSuiteRunning()
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
                && authoringCompositionValidationRunner.IsRunning);
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
        DrawRegionalElevationFoundationSettings();
        DrawWorkspaceSectionGap();
        DrawNodeElevationInitializationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawNodeElevationInterpolationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationCompositionValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationManagementValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationSceneToolValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationMultiSelectValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationInterpolationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationTriangulationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationTriangulatedLinearValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationTriangulatedLinearGpuValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationSmoothGradientValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationTriangulatedSmoothCpuValidationSettings();
        DrawWorkspaceSectionGap();
        DrawRegionalElevationTriangulatedSmoothGpuValidationSettings();
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
        DrawStampRotationFoundationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampSourceOrientationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampSourceRemapValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampFalloffValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampLibraryFoundationValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampLibrarySyncValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampAssetDefaultsValidationSettings();
        DrawWorkspaceSectionGap();
        DrawBlendModeAuthoringUXDefaultsValidationSettings();
        DrawWorkspaceSectionGap();
        DrawStampLibraryBrowserValidationSettings();
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
        DrawRuntimeValidationSettings();
    }
}
