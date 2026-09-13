using UnityEditor;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawDiagnosticsWorkspace()
    {
        DrawWorkspaceHeader(
            "Diagnostics",
            "Validation and implementation diagnostics for authoring, " +
            "composition, preview responsiveness, and runtime systems."
        );

        DrawPreviewResponsivenessValidationSettings();

        DrawWorkspaceSectionGap();

        DrawModifierDataFoundationSettings();

        DrawWorkspaceSectionGap();

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

        DrawWorkspaceSectionGap();

        DrawTargetSurfaceBlendFoundationValidationSettings();

        DrawWorkspaceSectionGap();

        DrawMaxMinBlendValidationSettings();

        DrawWorkspaceSectionGap();

        DrawReplaceBlendValidationSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringChangePipelineSettings();

        DrawWorkspaceSectionGap();

        DrawGpuCompositorFoundationSettings();

        DrawWorkspaceSectionGap();

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

        DrawWorkspaceSectionGap();

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

        DrawWorkspaceSectionGap();

        DrawRuntimeHeightIncrementalDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeSceneSynchronizationDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeCollisionIncrementalDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeSurfaceIncrementalDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeAddressablesDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationSettings();
    }
}
