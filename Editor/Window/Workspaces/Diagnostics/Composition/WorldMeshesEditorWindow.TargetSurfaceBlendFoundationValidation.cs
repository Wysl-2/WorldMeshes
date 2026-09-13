using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawTargetSurfaceBlendFoundationValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Target-Surface Blend Foundation Validation",
                TerrainTargetSurfaceBlendFoundationValidationUtility
                    .IsScheduled,
                TerrainTargetSurfaceBlendFoundationValidationUtility
                    .IsRunning,
                TerrainTargetSurfaceBlendFoundationValidationUtility
                    .LastPassedCount,
                TerrainTargetSurfaceBlendFoundationValidationUtility
                    .LastFailedCount,
                "Validate Target-Surface Blend Foundation",
                TerrainTargetSurfaceBlendFoundationValidationUtility
                    .LastSummary,
                "Validates the Package 1 target-surface data foundation, central service mutations, dirty regions, duplication, Undo/Redo, and unchanged Additive GPU output. Max/Min behavior is validated separately."
            );

        if (requested)
        {
            TerrainTargetSurfaceBlendFoundationValidationUtility
                .RequestValidation();
        }
    }
}
