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
        authoringPreviewCoreValidationRunner;

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
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
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
                manifest.isComplete
                    ? "Yes"
                    : "No"
            );

            EditorGUILayout.LabelField(
                "Expected Range Records",
                manifest.ExpectedTileHeightRangeCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Stored Range Records",
                manifest.TileHeightRangeCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Valid Range Records",
                manifest.ValidTileHeightRangeCount
                    .ToString("N0")
            );

            if (manifest.HasValidHeightRange)
            {
                EditorGUILayout.LabelField(
                    "Global Range",
                    manifest.minimumTerrainHeight
                        .ToString("R") +
                    " -> " +
                    manifest.maximumTerrainHeight
                        .ToString("R")
                );
            }
        }

        GUILayout.Space(5f);

        bool validationDisabled =
            worldSettings == null
            || TerrainRuntimeBakePipeline.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating
            || EditorApplication
                .isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(
            validationDisabled
        );

        if (
            GUILayout.Button(
                "Validate Runtime Height Range Metadata",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeHeightRangeMetadataValidator
                .Validate(
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

        TerrainValidationSuiteRunner suiteRunner =
            GetAuthoringPreviewCoreValidationRunner();

        DrawAuthoringPreviewCoreValidationSuite(
            suiteRunner
        );

        DrawWorkspaceSectionGap();

        EditorGUI.BeginDisabledGroup(
            suiteRunner.IsRunning
        );

        DrawStreamingRegressionValidationSettings();

        DrawWorkspaceSectionGap();

        DrawPreviewResponsivenessValidationSettings();

        DrawWorkspaceSectionGap();

        DrawWindowCacheFoundationValidationSettings();

        DrawWorkspaceSectionGap();

        DrawSceneViewResidencyValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStagedTransitionValidationSettings();

        DrawWorkspaceSectionGap();

        DrawResidencySizeRecoveryValidationSettings();

        DrawWorkspaceSectionGap();

        DrawIncrementalStreamingValidationSettings();

        DrawWorkspaceSectionGap();

        DrawModifierResidencyValidationSettings();

        DrawWorkspaceSectionGap();

        DrawRegionalElevationResidencyValidationSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringChangePipelineSettings();

        EditorGUI.EndDisabledGroup();
    }

    private TerrainValidationSuiteRunner
        GetAuthoringPreviewCoreValidationRunner()
    {
        if (authoringPreviewCoreValidationRunner == null)
        {
            authoringPreviewCoreValidationRunner =
                new TerrainValidationSuiteRunner(
                    new[]
                    {
                        new TerrainValidationSuiteRunner.Case(
                            "Height Cache Window",
                            () =>
                            {
                                if (
                                    TerrainAuthoringPreviewCacheValidationUtility
                                        .IsRunning
                                )
                                {
                                    return false;
                                }

                                TerrainAuthoringPreviewCacheValidationUtility
                                    .RequestValidation();

                                return
                                    TerrainAuthoringPreviewCacheValidationUtility
                                        .IsRunning;
                            },
                            () =>
                                TerrainAuthoringPreviewCacheValidationUtility
                                    .IsRunning,
                            () =>
                                TerrainAuthoringPreviewCacheValidationUtility
                                    .LastRunSummary
                        ),
                        new TerrainValidationSuiteRunner.Case(
                            "Staged Window Transitions",
                            () =>
                            {
                                if (
                                    TerrainAuthoringStagedTransitionValidationUtility
                                        .IsRunning
                                )
                                {
                                    return false;
                                }

                                TerrainAuthoringStagedTransitionValidationUtility
                                    .RequestValidation();

                                return
                                    TerrainAuthoringStagedTransitionValidationUtility
                                        .IsRunning;
                            },
                            () =>
                                TerrainAuthoringStagedTransitionValidationUtility
                                    .IsRunning,
                            () =>
                                TerrainAuthoringStagedTransitionValidationUtility
                                    .LastRunSummary
                        )
                    }
                );
        }

        return
            authoringPreviewCoreValidationRunner;
    }

    private void DrawAuthoringPreviewCoreValidationSuite(
        TerrainValidationSuiteRunner suiteRunner
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Authoring Preview Core Checks",
            EditorStyles.boldLabel
        );

        DrawValidationRunSummary(
            suiteRunner.Summary
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            suiteRunner.IsRunning
            || Application.isPlaying
            || EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Run Core Preview Checks",
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

        EditorGUILayout.HelpBox(
            "Runs the Height Cache Window and Staged Window Transition checks sequentially. Detailed assertion output remains in the Unity Console.",
            MessageType.None
        );

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
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            caseName,
            EditorStyles.boldLabel
        );

        DrawValidationRunSummary(
            summary
        );

        GUILayout.EndVertical();
    }

    private bool IsValidationSuiteRunning()
    {
        return
            authoringPreviewCoreValidationRunner != null
            && authoringPreviewCoreValidationRunner.IsRunning;
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

        DrawModifierDataFoundationSettings();

        DrawWorkspaceSectionGap();

        DrawTargetSurfaceBlendFoundationValidationSettings();

        DrawWorkspaceSectionGap();

        DrawMaxMinBlendValidationSettings();

        DrawWorkspaceSectionGap();

        DrawReplaceBlendValidationSettings();

        DrawWorkspaceSectionGap();

        DrawGpuCompositorFoundationSettings();
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
