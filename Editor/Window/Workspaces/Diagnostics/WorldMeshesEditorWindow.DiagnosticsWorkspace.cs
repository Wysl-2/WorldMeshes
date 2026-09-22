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

    private TerrainRuntimeBakeStateSummary
        runtimeBakeDiagnosticsSummary;

    private bool
        runtimeBakeDiagnosticsSummaryEvaluated;

    private TerrainGenerationStateEvaluationResult
        runtimeBakeDiagnosticsGenerationState;

    private bool
        runtimeBakeDiagnosticsGenerationStateEvaluated;

    private WorldSettings
        runtimeBakeDiagnosticsTrackedWorldSettings;

    private TerrainAuthoringData
        runtimeBakeDiagnosticsTrackedAuthoringData;

    private int
        runtimeBakeDiagnosticsWorldSettingsDirtyCount =
            int.MinValue;

    private int
        runtimeBakeDiagnosticsAuthoringDataDirtyCount =
            int.MinValue;

    private long
        runtimeBakeDiagnosticsAuthoringRevision =
            long.MinValue;

    [SerializeField]
    private bool showRuntimeOutputDiagnostics;

    private void DrawDiagnosticsWorkspace()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

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

        DrawStreamingPreviewDiagnostics();

        DrawWorkspaceSectionGap();

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

    /*
     * Diagnostics data persists across IMGUI Layout/Repaint/input passes.
     * It is invalidated by authoritative bake-state changes, Undo/Redo,
     * project changes, or inexpensive input revision/dirty checks.
     */
    private void InitializeRuntimeBakeDiagnosticsCache()
    {
        TerrainRuntimeBakeStateService.StateChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        TerrainRuntimeBakeStateService.StateChanged +=
            HandleRuntimeBakeDiagnosticsInvalidated;

        Undo.undoRedoPerformed -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        Undo.undoRedoPerformed +=
            HandleRuntimeBakeDiagnosticsInvalidated;

        EditorApplication.projectChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        EditorApplication.projectChanged +=
            HandleRuntimeBakeDiagnosticsInvalidated;

        ResetRuntimeBakeDiagnosticsInputTracking();
        InvalidateRuntimeBakeDiagnostics();
    }

    private void ShutdownRuntimeBakeDiagnosticsCache()
    {
        TerrainRuntimeBakeStateService.StateChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        Undo.undoRedoPerformed -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        EditorApplication.projectChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        InvalidateRuntimeBakeDiagnostics();
        ResetRuntimeBakeDiagnosticsInputTracking();
    }

    private void HandleRuntimeBakeDiagnosticsInvalidated()
    {
        InvalidateRuntimeBakeDiagnostics();
        Repaint();
    }

    private void InvalidateRuntimeBakeDiagnostics()
    {
        runtimeBakeDiagnosticsPlan =
            null;

        runtimeBakeDiagnosticsPlanEvaluated =
            false;

        runtimeBakeDiagnosticsSummary =
            null;

        runtimeBakeDiagnosticsSummaryEvaluated =
            false;

        runtimeBakeDiagnosticsGenerationState =
            default(TerrainGenerationStateEvaluationResult);

        runtimeBakeDiagnosticsGenerationStateEvaluated =
            false;
    }

    private void ResetRuntimeBakeDiagnosticsInputTracking()
    {
        runtimeBakeDiagnosticsTrackedWorldSettings =
            null;

        runtimeBakeDiagnosticsTrackedAuthoringData =
            null;

        runtimeBakeDiagnosticsWorldSettingsDirtyCount =
            int.MinValue;

        runtimeBakeDiagnosticsAuthoringDataDirtyCount =
            int.MinValue;

        runtimeBakeDiagnosticsAuthoringRevision =
            long.MinValue;
    }

    private void RefreshRuntimeBakeDiagnosticsInputState()
    {
        int worldSettingsDirtyCount =
            worldSettings != null
                ? EditorUtility.GetDirtyCount(
                    worldSettings
                )
                : -1;

        int authoringDataDirtyCount =
            terrainAuthoringData != null
                ? EditorUtility.GetDirtyCount(
                    terrainAuthoringData
                )
                : -1;

        long authoringRevision =
            terrainAuthoringData != null
                ? terrainAuthoringData.authoringRevision
                : long.MinValue;

        if (
            runtimeBakeDiagnosticsTrackedWorldSettings ==
                worldSettings
            &&
            runtimeBakeDiagnosticsTrackedAuthoringData ==
                terrainAuthoringData
            &&
            runtimeBakeDiagnosticsWorldSettingsDirtyCount ==
                worldSettingsDirtyCount
            &&
            runtimeBakeDiagnosticsAuthoringDataDirtyCount ==
                authoringDataDirtyCount
            &&
            runtimeBakeDiagnosticsAuthoringRevision ==
                authoringRevision
        )
        {
            return;
        }

        runtimeBakeDiagnosticsTrackedWorldSettings =
            worldSettings;

        runtimeBakeDiagnosticsTrackedAuthoringData =
            terrainAuthoringData;

        runtimeBakeDiagnosticsWorldSettingsDirtyCount =
            worldSettingsDirtyCount;

        runtimeBakeDiagnosticsAuthoringDataDirtyCount =
            authoringDataDirtyCount;

        runtimeBakeDiagnosticsAuthoringRevision =
            authoringRevision;

        InvalidateRuntimeBakeDiagnostics();
    }

    private TerrainRuntimeBakePlan
        GetRuntimeBakeDiagnosticsPlan()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

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

    private TerrainRuntimeBakeStateSummary
        GetRuntimeBakeDiagnosticsSummary()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

        if (!runtimeBakeDiagnosticsSummaryEvaluated)
        {
            runtimeBakeDiagnosticsSummary =
                TerrainRuntimeBakeStateService
                    .GetSummary();

            runtimeBakeDiagnosticsSummaryEvaluated =
                true;
        }

        return
            runtimeBakeDiagnosticsSummary;
    }

    /*
     * Routine Diagnostics display code shares one short-lived generation-state
     * evaluation, then retains only the immutable status results. Repaint and
     * Layout passes therefore do not recalculate authoring signatures until
     * the existing Diagnostics invalidation path observes a real input change.
     */
    private TerrainGenerationStateEvaluationResult
        GetRuntimeBakeDiagnosticsGenerationState()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

        if (!runtimeBakeDiagnosticsGenerationStateEvaluated)
        {
            runtimeBakeDiagnosticsGenerationState =
                TerrainGenerationStateUtility
                    .EvaluateGenerationState(
                        worldSettings,
                        terrainAuthoringData
                    );

            runtimeBakeDiagnosticsGenerationStateEvaluated =
                true;
        }

        return
            runtimeBakeDiagnosticsGenerationState;
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
