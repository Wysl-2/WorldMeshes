using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampLibraryFoundationValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Library Foundation Validation",
                TerrainHeightStampLibraryFoundationValidationUtility
                    .IsScheduled,
                TerrainHeightStampLibraryFoundationValidationUtility
                    .IsRunning,
                TerrainHeightStampLibraryFoundationValidationUtility
                    .LastPassedCount,
                TerrainHeightStampLibraryFoundationValidationUtility
                    .LastFailedCount,
                "Validate Stamp Library Foundation",
                TerrainHeightStampLibraryFoundationValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainHeightStampLibraryFoundationValidationUtility
                .RequestValidation();
        }
    }
}
