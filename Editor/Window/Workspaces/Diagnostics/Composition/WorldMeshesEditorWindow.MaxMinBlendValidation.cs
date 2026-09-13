using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawMaxMinBlendValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Max / Min Blend Validation",
                TerrainMaxMinBlendValidationUtility
                    .IsScheduled,
                TerrainMaxMinBlendValidationUtility
                    .IsRunning,
                TerrainMaxMinBlendValidationUtility
                    .LastPassedCount,
                TerrainMaxMinBlendValidationUtility
                    .LastFailedCount,
                "Validate Max / Min Blend Modes",
                TerrainMaxMinBlendValidationUtility
                    .LastSummary,
                "Validates Additive regression behavior, Max/Min target-surface composition, falloff, remapping/orientation, ordered stacking, cross-tile seams, conservative range metadata, runtime parity, blend-mode mutations, dirty regions, enable/disable, and Undo/Redo."
            );

        if (requested)
        {
            TerrainMaxMinBlendValidationUtility
                .RequestValidation();
        }
    }
}
