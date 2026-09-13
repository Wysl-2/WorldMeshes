using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampAssetDefaultsValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Asset Defaults Validation",
                TerrainHeightStampAssetDefaultsValidationUtility
                    .IsScheduled,
                TerrainHeightStampAssetDefaultsValidationUtility
                    .IsRunning,
                TerrainHeightStampAssetDefaultsValidationUtility
                    .LastPassedCount,
                TerrainHeightStampAssetDefaultsValidationUtility
                    .LastFailedCount,
                "Validate Stamp Asset Defaults",
                TerrainHeightStampAssetDefaultsValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainHeightStampAssetDefaultsValidationUtility
                .RequestValidation();
        }
    }
}
