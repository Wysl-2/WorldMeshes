using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampFalloffValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Falloff Shape / Profile Validation",
                TerrainStampFalloffValidationUtility
                    .IsScheduled,
                TerrainStampFalloffValidationUtility
                    .IsRunning,
                TerrainStampFalloffValidationUtility
                    .LastPassedCount,
                TerrainStampFalloffValidationUtility
                    .LastFailedCount,
                "Validate Stamp Falloff Shape / Profile",
                TerrainStampFalloffValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainStampFalloffValidationUtility
                .RequestValidation();
        }
    }
}
