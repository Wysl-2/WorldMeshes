using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStagedTransitionValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Staged Window Transitions",
                "Validation",
                TerrainAuthoringStagedTransitionValidationUtility
                    .IsRunning,
                "Validate Staged Window Transitions",
                "Package 03 validates active/staging edit-mode height-cache transition semantics.\n\nThe validation covers deterministic retained/entering/leaving classification, cache-local slice remapping, staging slice readiness, committed-base materialization, final composition, retained final GPU reuse, retained reuse eligibility, incomplete activation rejection, temporary staging resource cleanup, live active-cache isolation, and persistent authoring-state safety.\n\nTemporary validation caches are never bound to the live clipmap and the automated validator does not move the Scene View. The final PASS / FAIL / BLOCKED report is written to the Unity Console.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringStagedTransitionValidationUtility
                .ValidateStagedTransitions();
        }
    }
}
