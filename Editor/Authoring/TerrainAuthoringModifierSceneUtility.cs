using UnityEditor;
using UnityEngine;

/*
 * Small shared Scene View helpers for terrain modifier authoring.
 *
 * Stage 15B only frames modifiers. Stage 15C can reuse this utility for
 * modifier Scene View interaction without making the editor window own
 * Scene View camera behavior.
 */
public static class TerrainAuthoringModifierSceneUtility
{
    private const float MinimumFrameSize =
        1f;

    private const float MinimumVerticalFrameSize =
        10f;

    public static bool TryFrameModifier(
        TerrainHeightModifier modifier,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (modifier == null)
        {
            errorMessage =
                "No terrain modifier is selected.";

            return false;
        }

        SceneView sceneView =
            SceneView.lastActiveSceneView;

        if (
            sceneView == null
            &&
            SceneView.sceneViews != null
            &&
            SceneView.sceneViews.Count > 0
        )
        {
            sceneView =
                SceneView.sceneViews[0]
                as SceneView;
        }

        if (sceneView == null)
        {
            errorMessage =
                "No Scene View is available to frame the selected modifier.";

            return false;
        }

        Bounds affectedBounds =
            modifier.GetAffectedWorldBounds();

        Vector3 center =
            affectedBounds.center;

        float previewMinimumHeight =
            TerrainAuthoringPreviewService
                .MinimumPreviewHeight;

        float previewMaximumHeight =
            TerrainAuthoringPreviewService
                .MaximumPreviewHeight;

        bool usablePreviewRange =
            TerrainAuthoringPreviewService
                .CacheReady
            &&
            !float.IsNaN(
                previewMinimumHeight
            )
            &&
            !float.IsInfinity(
                previewMinimumHeight
            )
            &&
            !float.IsNaN(
                previewMaximumHeight
            )
            &&
            !float.IsInfinity(
                previewMaximumHeight
            )
            &&
            previewMaximumHeight >=
                previewMinimumHeight;

        center.y =
            usablePreviewRange
                ? (
                    previewMinimumHeight +
                    previewMaximumHeight
                ) *
                0.5f
                : 0f;

        float sizeX =
            Mathf.Max(
                MinimumFrameSize,
                Mathf.Abs(
                    affectedBounds.size.x
                )
            );

        float sizeZ =
            Mathf.Max(
                MinimumFrameSize,
                Mathf.Abs(
                    affectedBounds.size.z
                )
            );

        float horizontalSize =
            Mathf.Max(
                sizeX,
                sizeZ
            );

        float previewHeightSpan =
            usablePreviewRange
                ? Mathf.Max(
                    0f,
                    previewMaximumHeight -
                    previewMinimumHeight
                )
                : 0f;

        float sizeY =
            Mathf.Max(
                MinimumVerticalFrameSize,
                horizontalSize *
                    0.25f,
                previewHeightSpan
            );

        Bounds frameBounds =
            new Bounds(
                center,
                new Vector3(
                    sizeX,
                    sizeY,
                    sizeZ
                )
            );

        sceneView.Frame(
            frameBounds,
            false
        );

        sceneView.Focus();
        sceneView.Repaint();

        return true;
    }
}
