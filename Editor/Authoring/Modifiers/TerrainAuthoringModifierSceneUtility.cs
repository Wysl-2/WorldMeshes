using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Small shared Scene View helpers for terrain modifier authoring.
 *
 * Scene View interaction should be local to the modifier being edited.
 * Global preview min/max values are intentionally used only as a fallback:
 * a distant mountain must not move or enlarge the Scene UI for an unrelated
 * stamp elsewhere in the world.
 */
public static class TerrainAuthoringModifierSceneUtility
{
    private const float MinimumFrameSize =
        1f;

    private const float MinimumVerticalFrameSize =
        10f;

    private const float MinimumInteractionLift =
        0.05f;

    private const float InteractionLiftSampleFraction =
        0.25f;

    private const float HeightDeltaDirectionEpsilon =
        0.0001f;

    private static readonly List<Vector2Int>
        localHeightTiles =
            new List<Vector2Int>();

    public static bool TryGetStampInteractionPlaneY(
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        out float planeY
    )
    {
        planeY =
            0f;

        if (
            worldSettings == null
            ||
            stamp == null
            ||
            !TryGetLocalPreviewHeightRange(
                worldSettings,
                stamp,
                out float minimumHeight,
                out float maximumHeight
            )
        )
        {
            return false;
        }

        float referenceHeight;

        if (
            stamp.HeightDelta >
                HeightDeltaDirectionEpsilon
        )
        {
            referenceHeight =
                minimumHeight;
        }
        else if (
            stamp.HeightDelta <
                -HeightDeltaDirectionEpsilon
        )
        {
            referenceHeight =
                maximumHeight;
        }
        else
        {
            referenceHeight =
                (
                    minimumHeight +
                    maximumHeight
                )
                *
                0.5f;
        }

        float sampleSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        float visualLift =
            Mathf.Max(
                MinimumInteractionLift,
                sampleSpacing *
                    InteractionLiftSampleFraction
            );

        planeY =
            referenceHeight +
            visualLift;

        return
            IsFinite(
                planeY
            );
    }

    public static bool TryGetLocalPreviewHeightRange(
        WorldSettings worldSettings,
        TerrainHeightModifier modifier,
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        if (
            worldSettings == null
            ||
            modifier == null
            ||
            !TerrainAuthoringPreviewService
                .CacheReady
        )
        {
            return false;
        }

        if (
            TerrainAuthoringPreviewService
                .GetWorldBoundsReadiness(
                    modifier.GetAffectedWorldBounds(),
                    1
                )
            !=
            TerrainAuthoringPreviewReadiness.Ready
        )
        {
            return false;
        }

        localHeightTiles.Clear();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                modifier.GetAffectedWorldBounds(),
                localHeightTiles,
                1
            );

        bool foundRange =
            false;

        for (
            int index = 0;
            index <
                localHeightTiles.Count;
            index++
        )
        {
            Vector2Int coordinate =
                localHeightTiles[
                    index
                ];

            if (
                !TerrainAuthoringPreviewService
                    .TryGetCompositeSliceRange(
                        coordinate.x,
                        coordinate.y,
                        out float tileMinimum,
                        out float tileMaximum
                    )
                ||
                !IsFinite(
                    tileMinimum
                )
                ||
                !IsFinite(
                    tileMaximum
                )
                ||
                tileMaximum <
                    tileMinimum
            )
            {
                continue;
            }

            minimumHeight =
                Mathf.Min(
                    minimumHeight,
                    tileMinimum
                );

            maximumHeight =
                Mathf.Max(
                    maximumHeight,
                    tileMaximum
                );

            foundRange =
                true;
        }

        localHeightTiles.Clear();

        return
            foundRange
            &&
            IsFinite(
                minimumHeight
            )
            &&
            IsFinite(
                maximumHeight
            )
            &&
            maximumHeight >=
                minimumHeight;
    }

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

        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        bool usableLocalRange =
            TryGetLocalPreviewHeightRange(
                worldSettings,
                modifier,
                out float previewMinimumHeight,
                out float previewMaximumHeight
            );

        if (!usableLocalRange)
        {
            previewMinimumHeight =
                TerrainAuthoringPreviewService
                    .MinimumPreviewHeight;

            previewMaximumHeight =
                TerrainAuthoringPreviewService
                    .MaximumPreviewHeight;

            usableLocalRange =
                TerrainAuthoringPreviewService
                    .CacheReady
                &&
                IsFinite(
                    previewMinimumHeight
                )
                &&
                IsFinite(
                    previewMaximumHeight
                )
                &&
                previewMaximumHeight >=
                    previewMinimumHeight;
        }

        center.y =
            usableLocalRange
                ? (
                    previewMinimumHeight +
                    previewMaximumHeight
                )
                *
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
            usableLocalRange
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

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }
}
