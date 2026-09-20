using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawIncrementalStreamingValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Incremental Streaming",
                "Validation",
                TerrainAuthoringIncrementalStreamingValidationUtility
                    .IsRunning,
                "Validate Incremental Streaming",
                "Package 04 validates bounded editor-update streaming semantics.\n\nThe validation covers work budgets and persistent cursors, duplicate request coalescing, latest-target-wins usefulness, guard-based prefetch and reversal, coverage-critical transitions, progress accounting, cancellation-vs-failure semantics, the Package 03A 24x24 -> 12x12 recovery classification, live streaming diagnostics, and persistent authoring-state safety.\n\nThe validator does not move the Scene View and does not allocate a giant synthetic GPU cache. The final PASS / FAIL / BLOCKED report is written to the Unity Console.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringIncrementalStreamingValidationUtility
                .ValidateIncrementalStreaming();
        }
    }
}
