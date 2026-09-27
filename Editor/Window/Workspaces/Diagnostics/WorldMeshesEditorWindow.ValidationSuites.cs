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

        EditorGUILayout.HelpBox(
            "Deep generated-output regression validation will be presented here after the existing runtime output validators are consolidated. Use System Health > Refresh Full Health Check for explicit structural output integrity verification.",
            MessageType.None
        );
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
