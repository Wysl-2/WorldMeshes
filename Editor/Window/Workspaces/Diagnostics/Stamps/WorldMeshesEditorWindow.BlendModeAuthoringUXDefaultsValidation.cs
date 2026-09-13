using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawBlendModeAuthoringUXDefaultsValidationSettings()
    {
        bool requested =
            DrawValidationResultAction(
                "Blend Mode Authoring UX + Defaults Validation",
                TerrainBlendModeAuthoringUXDefaultsValidationUtility
                    .IsScheduled,
                TerrainBlendModeAuthoringUXDefaultsValidationUtility
                    .IsRunning,
                TerrainBlendModeAuthoringUXDefaultsValidationUtility
                    .LastPassedCount,
                TerrainBlendModeAuthoringUXDefaultsValidationUtility
                    .LastFailedCount,
                "Validate Blend Mode UX + Defaults",
                TerrainBlendModeAuthoringUXDefaultsValidationUtility
                    .LastSummary,
                "Validates Version 1 creation-default compatibility, Version 2 blend/target defaults, mode-aware field rules, all four default creation modes, copy-on-placement independence, Stamp Asset reassignment, duplication, mode switching, signatures, dirty regions, and Undo/Redo."
            );

        if (requested)
        {
            TerrainBlendModeAuthoringUXDefaultsValidationUtility
                .RequestValidation();
        }
    }
}
