using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawModifierResidencyValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Modifier Residency",
                "Validation",
                TerrainAuthoringModifierResidencyValidationUtility
                    .IsRunning,
                "Validate Modifier Residency",
                "Package 05 validates world-global modifier authoring with resident-only GPU recomposition.\n\nThe validation covers deterministic resident/nonresident dirty partitioning, nonresident logical dirt, resident-only update publication, monotonic preview authoring generation, staging generation capture/rejection, cancellation-vs-failure semantics, interactive streaming deferral, nonresident authoring acknowledgement, Ready / Loading / OutsideWorld / PreviewUnavailable classification, tile-specific dirty readiness, committed-base invalidation, readiness query side-effect safety, Package 03A bounded-residency regression, live diagnostics, and persistent authoring-state safety.\n\nThe validator does not move the Scene View and does not intentionally allocate a world-sized GPU cache. The final PASS / FAIL / BLOCKED report is written to the Unity Console.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringModifierResidencyValidationUtility
                .ValidateModifierResidency();
        }
    }
}
