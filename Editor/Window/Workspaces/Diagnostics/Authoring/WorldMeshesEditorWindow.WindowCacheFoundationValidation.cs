using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawWindowCacheFoundationValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Height Cache Window Foundation",
                "Validation",
                TerrainAuthoringPreviewCacheValidationUtility
                    .IsRunning,
                "Validate Height Cache Window Foundation",
                "Package 01 validates the window-capable edit-mode Height Preview cache.\n\nThe validation covers cache-window value semantics, fixed-size world-boundary fitting, non-zero-origin partial cache construction, world/cache-local slice addressing, resident height ranges, world coverage, GPU memory estimation, and persistent authoring-state safety.\n\nThe temporary validation cache is not bound to the live clipmap. The final PASS / FAIL / BLOCKED report is written to the Unity Console.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringPreviewCacheValidationUtility
                .ValidateWindowCacheFoundation();
        }
    }
}
