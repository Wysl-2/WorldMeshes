using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampLibrarySyncValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Library Import / Sync Validation",
                TerrainHeightStampLibrarySyncValidationUtility
                    .IsScheduled,
                TerrainHeightStampLibrarySyncValidationUtility
                    .IsRunning,
                TerrainHeightStampLibrarySyncValidationUtility
                    .LastPassedCount,
                TerrainHeightStampLibrarySyncValidationUtility
                    .LastFailedCount,
                "Validate Stamp Library Import / Sync",
                TerrainHeightStampLibrarySyncValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainHeightStampLibrarySyncValidationUtility
                .RequestValidation();
        }
    }
}
