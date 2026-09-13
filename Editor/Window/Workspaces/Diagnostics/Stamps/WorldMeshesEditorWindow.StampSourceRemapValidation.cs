using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampSourceRemapValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Stamp Source Remapping Validation",
                TerrainStampSourceRemapValidationUtility
                    .IsScheduled,
                TerrainStampSourceRemapValidationUtility
                    .IsRunning,
                TerrainStampSourceRemapValidationUtility
                    .LastPassedCount,
                TerrainStampSourceRemapValidationUtility
                    .LastFailedCount,
                "Validate Stamp Source Remapping",
                TerrainStampSourceRemapValidationUtility
                    .LastSummary,
                null
            );

        if (requested)
        {
            TerrainStampSourceRemapValidationUtility
                .RequestValidation();
        }
    }
}
