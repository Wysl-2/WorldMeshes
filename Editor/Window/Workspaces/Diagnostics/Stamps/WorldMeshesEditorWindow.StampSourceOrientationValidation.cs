using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampSourceOrientationValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Source Orientation Validation",
                TerrainStampSourceOrientationValidationUtility
                    .IsScheduled,
                TerrainStampSourceOrientationValidationUtility
                    .IsRunning,
                TerrainStampSourceOrientationValidationUtility
                    .LastPassedCount,
                TerrainStampSourceOrientationValidationUtility
                    .LastFailedCount,
                "Validate Stamp Source Orientation",
                TerrainStampSourceOrientationValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainStampSourceOrientationValidationUtility
                .RequestValidation();
        }
    }
}
