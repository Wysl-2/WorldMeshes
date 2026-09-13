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
    private bool showRuntimeHeightCompositionDiagnostics;

    [SerializeField]
    private bool showRuntimeBakeStateDiagnostics;

    [SerializeField]
    private bool showRuntimeBakePlanDiagnostics;

    [SerializeField]
    private bool showRuntimeBakePipelineDiagnostics;

    [SerializeField]
    private bool showRuntimePipelineValidationDiagnostics;

    [SerializeField]
    private bool showRuntimePersistenceResumeDiagnostics;

    [SerializeField]
    private bool showRuntimeInvalidationDiagnostics;

    private TerrainRuntimeBakePlan
        runtimeBakeDiagnosticsPlan;

    private bool
        runtimeBakeDiagnosticsPlanEvaluated;

    private TerrainRuntimeBakeStateSnapshot
        runtimeBakeDiagnosticsSnapshot;

    private bool
        runtimeBakeDiagnosticsSnapshotEvaluated;

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

        ResetRuntimeBakeDiagnosticsFrameCache();

        if (!showRuntimeBakeDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        showRuntimeHeightCompositionDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeHeightCompositionDiagnostics,
                "Height Composition Validation",
                true
            );

        if (showRuntimeHeightCompositionDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimeHeightCompositionValidationSettings();
        }

        DrawWorkspaceSectionGap();

        showRuntimeBakeStateDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeBakeStateDiagnostics,
                "Bake State",
                true
            );

        if (showRuntimeBakeStateDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimeBakeStateDiagnostics();
        }

        DrawWorkspaceSectionGap();

        showRuntimeBakePlanDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeBakePlanDiagnostics,
                "Bake Plan",
                true
            );

        if (showRuntimeBakePlanDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimeBakePlanDiagnostics();
        }

        DrawWorkspaceSectionGap();

        showRuntimeBakePipelineDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeBakePipelineDiagnostics,
                "Unified Runtime Bake Pipeline",
                true
            );

        if (showRuntimeBakePipelineDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimeBakePipelineDiagnostics();
        }

        DrawWorkspaceSectionGap();

        showRuntimePipelineValidationDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimePipelineValidationDiagnostics,
                "Runtime Pipeline Validation",
                true
            );

        if (showRuntimePipelineValidationDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimePipelineValidationDiagnostics();
        }

        DrawWorkspaceSectionGap();

        showRuntimePersistenceResumeDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimePersistenceResumeDiagnostics,
                "Persistence + Resume Validation",
                true
            );

        if (showRuntimePersistenceResumeDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimePersistenceResumeValidationDiagnostics();
        }

        DrawWorkspaceSectionGap();

        showRuntimeInvalidationDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeInvalidationDiagnostics,
                "Invalidation Scenario Validation",
                true
            );

        if (showRuntimeInvalidationDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimeInvalidationValidationDiagnostics();
        }

        DrawWorkspaceSectionGap();

        DrawRuntimeFaultRecoveryValidationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeEquivalenceValidationDiagnostics();
    }

    private void ResetRuntimeBakeDiagnosticsFrameCache()
    {
        runtimeBakeDiagnosticsPlan =
            null;

        runtimeBakeDiagnosticsPlanEvaluated =
            false;

        runtimeBakeDiagnosticsSnapshot =
            null;

        runtimeBakeDiagnosticsSnapshotEvaluated =
            false;
    }

    private TerrainRuntimeBakePlan
        GetRuntimeBakeDiagnosticsPlan()
    {
        if (!runtimeBakeDiagnosticsPlanEvaluated)
        {
            runtimeBakeDiagnosticsPlan =
                TerrainRuntimeBakePlanner
                    .BuildPlan(
                        worldSettings,
                        terrainAuthoringData
                    );

            runtimeBakeDiagnosticsPlanEvaluated =
                true;
        }

        return
            runtimeBakeDiagnosticsPlan;
    }

    private TerrainRuntimeBakeStateSnapshot
        GetRuntimeBakeDiagnosticsSnapshot()
    {
        if (!runtimeBakeDiagnosticsSnapshotEvaluated)
        {
            runtimeBakeDiagnosticsSnapshot =
                TerrainRuntimeBakeStateService
                    .GetSnapshot();

            runtimeBakeDiagnosticsSnapshotEvaluated =
                true;
        }

        return
            runtimeBakeDiagnosticsSnapshot;
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
