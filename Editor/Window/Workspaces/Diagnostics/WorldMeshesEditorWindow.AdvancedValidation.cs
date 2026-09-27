using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showAdvancedValidation;

    [SerializeField]
    private bool showAdvancedRuntimeBakeValidation;

    [SerializeField]
    private bool showAdvancedLiveStampValidation;

    [SerializeField]
    private bool showRuntimeHeightCompositionDiagnostics;

    [SerializeField]
    private bool showRuntimePipelineValidationDiagnostics;

    [SerializeField]
    private bool showRuntimePersistenceResumeDiagnostics;

    [SerializeField]
    private bool showRuntimeInvalidationDiagnostics;

    private void DrawAdvancedValidation()
    {
        showAdvancedValidation =
            EditorGUILayout.Foldout(
                showAdvancedValidation,
                "Advanced Validation",
                true
            );

        if (!showAdvancedValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawAdvancedRuntimeBakeValidation();

        DrawWorkspaceSectionGap();

        DrawAdvancedLiveStampValidation();
    }

    private void DrawAdvancedRuntimeBakeValidation()
    {
        showAdvancedRuntimeBakeValidation =
            EditorGUILayout.Foldout(
                showAdvancedRuntimeBakeValidation,
                "Runtime Bake",
                true
            );

        if (!showAdvancedRuntimeBakeValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        showRuntimeHeightCompositionDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeHeightCompositionDiagnostics,
                "Height Composition",
                true
            );

        if (showRuntimeHeightCompositionDiagnostics)
        {
            GUILayout.Space(5f);
            DrawRuntimeHeightCompositionValidationSettings();
        }

        DrawWorkspaceSectionGap();

        showRuntimePipelineValidationDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimePipelineValidationDiagnostics,
                "Runtime Pipeline",
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
                "Persistence / Resume",
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
                "Invalidation",
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

    private void DrawAdvancedLiveStampValidation()
    {
        showAdvancedLiveStampValidation =
            EditorGUILayout.Foldout(
                showAdvancedLiveStampValidation,
                "Live Stamp Integration",
                true
            );

        if (!showAdvancedLiveStampValidation)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawLiveStampValidationSettings();
    }
}
