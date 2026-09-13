using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawReplaceBlendValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Replace Blend Validation",
                TerrainReplaceBlendValidationUtility
                    .IsScheduled,
                TerrainReplaceBlendValidationUtility
                    .IsRunning,
                TerrainReplaceBlendValidationUtility
                    .LastPassedCount,
                TerrainReplaceBlendValidationUtility
                    .LastFailedCount,
                "Validate Replace Blend Mode",
                TerrainReplaceBlendValidationUtility
                    .LastSummary,
                "Validates Replace target-surface composition, source/influence separation, falloff and footprint shapes, remapping/orientation/smoothing, cross-tile seams, mixed-mode ordering, conservative range metadata, runtime parity, central blend-mode mutations, dirty regions, enable/disable, and Undo/Redo."
            );

        if (requested)
        {
            TerrainReplaceBlendValidationUtility
                .RequestValidation();
        }
    }
}
