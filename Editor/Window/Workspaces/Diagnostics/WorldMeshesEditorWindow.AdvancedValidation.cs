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

        DrawRuntimeBakeExecutionValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeInvalidationValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakePersistenceResumeValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeFaultRecoveryValidation();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeEquivalenceValidation();
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
