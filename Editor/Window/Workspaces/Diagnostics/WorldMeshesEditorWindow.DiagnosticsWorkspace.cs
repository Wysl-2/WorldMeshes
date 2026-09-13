using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showAuthoringDiagnostics;

    [SerializeField]
    private bool showRegionalElevationDiagnostics;

    [SerializeField]
    private bool showCompositionDiagnostics;

    [SerializeField]
    private bool showStampDiagnostics;

    [SerializeField]
    private bool showRuntimeBakeDiagnostics;

    [SerializeField]
    private bool showRuntimeOutputDiagnostics;

    private void DrawDiagnosticsWorkspace()
    {
        DrawWorkspaceHeader(
            "Diagnostics",
            "Validation and implementation diagnostics for authoring, " +
            "composition, preview responsiveness, and runtime systems."
        );

        DrawDiagnosticsToolbar();

        DrawWorkspaceSectionGap();

        DrawAuthoringDiagnosticsGroup();

        DrawWorkspaceSectionGap();

        DrawRegionalElevationDiagnosticsGroup();

        DrawWorkspaceSectionGap();

        DrawCompositionDiagnosticsGroup();

        DrawWorkspaceSectionGap();

        DrawStampDiagnosticsGroup();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeDiagnosticsGroup();

        DrawWorkspaceSectionGap();

        DrawRuntimeOutputDiagnosticsGroup();
    }

    private void DrawDiagnosticsToolbar()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Expand All"))
        {
            showAuthoringDiagnostics = true;
            showRegionalElevationDiagnostics = true;
            showCompositionDiagnostics = true;
            showStampDiagnostics = true;
            showRuntimeBakeDiagnostics = true;
            showRuntimeOutputDiagnostics = true;
        }

        if (GUILayout.Button("Collapse All"))
        {
            showAuthoringDiagnostics = false;
            showRegionalElevationDiagnostics = false;
            showCompositionDiagnostics = false;
            showStampDiagnostics = false;
            showRuntimeBakeDiagnostics = false;
            showRuntimeOutputDiagnostics = false;
        }

        GUILayout.EndHorizontal();
    }

    private void DrawAuthoringDiagnosticsGroup()
    {
        showAuthoringDiagnostics =
            EditorGUILayout.Foldout(
                showAuthoringDiagnostics,
                "Authoring & Preview",
                true
            );

        if (!showAuthoringDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawPreviewResponsivenessValidationSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringChangePipelineSettings();

        DrawWorkspaceSectionGap();

        DrawGpuCompositorFoundationSettings();
    }

    private void DrawRegionalElevationDiagnosticsGroup()
    {
        showRegionalElevationDiagnostics =
            EditorGUILayout.Foldout(
                showRegionalElevationDiagnostics,
                "Regional Elevation",
                true
            );

        if (!showRegionalElevationDiagnostics)
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

    private void DrawCompositionDiagnosticsGroup()
    {
        showCompositionDiagnostics =
            EditorGUILayout.Foldout(
                showCompositionDiagnostics,
                "Modifier Composition & Blending",
                true
            );

        if (!showCompositionDiagnostics)
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
    }

    private void DrawStampDiagnosticsGroup()
    {
        showStampDiagnostics =
            EditorGUILayout.Foldout(
                showStampDiagnostics,
                "Stamps & Library",
                true
            );

        if (!showStampDiagnostics)
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

        DrawWorkspaceSectionGap();

        DrawLiveStampValidationSettings();
    }

    private void DrawRuntimeBakeDiagnosticsGroup()
    {
        showRuntimeBakeDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeBakeDiagnostics,
                "Runtime Bake Pipeline",
                true
            );

        if (!showRuntimeBakeDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeHeightCompositionValidationSettings();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeStateDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakePlanDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakePipelineDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimePipelineValidationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimePersistenceResumeValidationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeInvalidationValidationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeFaultRecoveryValidationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeEquivalenceValidationDiagnostics();
    }

    private void DrawRuntimeOutputDiagnosticsGroup()
    {
        showRuntimeOutputDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeOutputDiagnostics,
                "Runtime Outputs & Integration",
                true
            );

        if (!showRuntimeOutputDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeHeightIncrementalDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeSurfaceIncrementalDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeCollisionIncrementalDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeSceneSynchronizationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeAddressablesDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationSettings();
    }
}
