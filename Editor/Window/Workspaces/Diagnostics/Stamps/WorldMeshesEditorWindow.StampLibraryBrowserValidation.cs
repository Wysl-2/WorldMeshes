using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampLibraryBrowserValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Library Browser Validation",
                TerrainHeightStampLibraryBrowserValidationUtility
                    .IsScheduled,
                TerrainHeightStampLibraryBrowserValidationUtility
                    .IsRunning,
                TerrainHeightStampLibraryBrowserValidationUtility
                    .LastPassedCount,
                TerrainHeightStampLibraryBrowserValidationUtility
                    .LastFailedCount,
                "Validate Stamp Library Browser",
                TerrainHeightStampLibraryBrowserValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainHeightStampLibraryBrowserValidationUtility
                .RequestValidation();
        }
    }
}
