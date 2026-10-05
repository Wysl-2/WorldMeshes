using UnityEngine;

/*
 * Shared Height sampling coverage for one clipmap LOD.
 *
 * The utility owns only world-space coverage mathematics required by Height
 * sampling. It has no runtime streamer state, Editor state, Addressables
 * dependency, GPU resources, or scene ownership.
 */
public static class TerrainHeightClipmapCoverageUtility
{
    public static bool TryCalculateRequiredWorldBounds(
        WorldSettings worldSettings,
        TerrainClipmapLayout layout,
        int level,
        float fineSampleSpacing,
        float coarseSampleSpacing,
        out Vector2 minimumXZ,
        out Vector2 maximumXZ,
        out string errorMessage
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (
            layout == null
            ||
            !layout.IsValid
        )
        {
            errorMessage =
                "TerrainClipmapLayout is unavailable or invalid.";

            return false;
        }

        if (
            level < 0
            ||
            level >= layout.LevelCount
        )
        {
            errorMessage =
                "The requested clipmap level is outside the active layout.";

            return false;
        }

        if (
            !IsFinitePositive(
                fineSampleSpacing
            )
        )
        {
            errorMessage =
                $"LOD{level} Height sample spacing is invalid.";

            return false;
        }

        bool hasCoarseLevel =
            level <
                layout.LevelCount -
                1;

        if (
            hasCoarseLevel
            &&
            !IsFinitePositive(
                coarseSampleSpacing
            )
        )
        {
            errorMessage =
                $"LOD{level + 1} coarse Height sample spacing is invalid.";

            return false;
        }

        Vector3 anchor =
            layout.GetAnchor(
                level
            );

        float halfExtent =
            TerrainClipmapTopologyUtility
                .GetLODHalfExtent(
                    worldSettings,
                    level
                );

        if (
            !IsFinitePositive(
                halfExtent
            )
        )
        {
            errorMessage =
                $"LOD{level} clipmap extent is invalid.";

            return false;
        }

        float minimumX =
            anchor.x -
            halfExtent;

        float maximumX =
            anchor.x +
            halfExtent;

        float minimumZ =
            anchor.z -
            halfExtent;

        float maximumZ =
            anchor.z +
            halfExtent;

        float normalMargin =
            fineSampleSpacing;

        if (hasCoarseLevel)
        {
            Vector3 coarseAnchor =
                layout.GetAnchor(
                    level + 1
                );

            float stitchHalfExtent =
                halfExtent +
                fineSampleSpacing *
                2f;

            minimumX =
                Mathf.Min(
                    minimumX,
                    coarseAnchor.x -
                        stitchHalfExtent
                );

            maximumX =
                Mathf.Max(
                    maximumX,
                    coarseAnchor.x +
                        stitchHalfExtent
                );

            minimumZ =
                Mathf.Min(
                    minimumZ,
                    coarseAnchor.z -
                        stitchHalfExtent
                );

            maximumZ =
                Mathf.Max(
                    maximumZ,
                    coarseAnchor.z +
                        stitchHalfExtent
                );

            normalMargin =
                Mathf.Max(
                    normalMargin,
                    coarseSampleSpacing
                );
        }

        minimumX -=
            normalMargin;

        maximumX +=
            normalMargin;

        minimumZ -=
            normalMargin;

        maximumZ +=
            normalMargin;

        if (
            !IsFinite(
                minimumX
            )
            ||
            !IsFinite(
                maximumX
            )
            ||
            !IsFinite(
                minimumZ
            )
            ||
            !IsFinite(
                maximumZ
            )
            ||
            minimumX >
                maximumX
            ||
            minimumZ >
                maximumZ
        )
        {
            errorMessage =
                $"LOD{level} Height sampling bounds are invalid.";

            return false;
        }

        minimumXZ =
            new Vector2(
                minimumX,
                minimumZ
            );

        maximumXZ =
            new Vector2(
                maximumX,
                maximumZ
            );

        return true;
    }

    private static bool IsFinitePositive(
        float value
    )
    {
        return
            IsFinite(
                value
            )
            &&
            value > 0f;
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
