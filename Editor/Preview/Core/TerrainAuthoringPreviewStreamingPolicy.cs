using UnityEngine;

/*
 * Pure streamed-residency policy.
 *
 * This layer decides whether current staging still serves the newest
 * residency intent and when active guard headroom should begin prefetching.
 * It owns no GPU resources, AssetDatabase work, hierarchy mutation, or
 * editor callbacks. Ordinary work is always expressed in bounded local
 * windows; logical world size only constrains where those windows may fit.
 */
internal static class TerrainAuthoringPreviewStreamingPolicy
{
    internal const int DefaultPrefetchThresholdTiles =
        1;

    internal static bool IsSameResidencyIntent(
        bool hasCurrentRequired,
        TerrainHeightCacheWindow currentRequired,
        bool hasCurrentDesired,
        TerrainHeightCacheWindow currentDesired,
        TerrainHeightCacheWindow newRequired,
        TerrainHeightCacheWindow newDesired
    )
    {
        return
            hasCurrentRequired
            &&
            hasCurrentDesired
            &&
            currentRequired ==
                newRequired
            &&
            currentDesired ==
                newDesired;
    }

    internal static bool IsStagingTargetUseful(
        TerrainHeightCacheWindow stagingTarget,
        TerrainHeightCacheWindow latestRequired,
        TerrainHeightCacheWindow latestDesired
    )
    {
        if (
            !stagingTarget.IsValid
            ||
            !latestRequired.IsValid
            ||
            !latestDesired.IsValid
            ||
            !stagingTarget.Contains(
                latestRequired
            )
        )
        {
            return false;
        }

        TerrainAuthoringPreviewResidencySizeHealth sizeHealth =
            TerrainAuthoringPreviewResidencyPolicy
                .EvaluateSizeHealth(
                    true,
                    stagingTarget,
                    latestDesired
                );

        return
            sizeHealth ==
                TerrainAuthoringPreviewResidencySizeHealth.Healthy;
    }

    internal static bool TryCalculatePrefetchTarget(
        TerrainHeightCacheWindow activeWindow,
        TerrainHeightCacheWindow requiredWindow,
        TerrainHeightCacheWindow desiredWindow,
        Vector2Int worldGridSize,
        out TerrainHeightCacheWindow prefetchTarget,
        int thresholdTiles = DefaultPrefetchThresholdTiles
    )
    {
        prefetchTarget =
            default;

        if (
            !activeWindow.IsValid
            ||
            !requiredWindow.IsValid
            ||
            !desiredWindow.IsValid
            ||
            worldGridSize.x <= 0
            ||
            worldGridSize.y <= 0
            ||
            !activeWindow.Contains(
                requiredWindow
            )
        )
        {
            return false;
        }

        TerrainAuthoringPreviewResidencySizeHealth sizeHealth =
            TerrainAuthoringPreviewResidencyPolicy
                .EvaluateSizeHealth(
                    true,
                    activeWindow,
                    desiredWindow
                );

        if (
            sizeHealth !=
                TerrainAuthoringPreviewResidencySizeHealth.Healthy
        )
        {
            return false;
        }

        int threshold =
            Mathf.Max(
                0,
                thresholdTiles
            );

        Vector2Int activeMaximum =
            activeWindow.MaximumExclusive;

        Vector2Int requiredMaximum =
            requiredWindow.MaximumExclusive;

        int leftHeadroom =
            requiredWindow.OriginTile.x -
            activeWindow.OriginTile.x;

        int rightHeadroom =
            activeMaximum.x -
            requiredMaximum.x;

        int bottomHeadroom =
            requiredWindow.OriginTile.y -
            activeWindow.OriginTile.y;

        int topHeadroom =
            activeMaximum.y -
            requiredMaximum.y;

        int minimumHeadroom =
            Mathf.Min(
                Mathf.Min(
                    leftHeadroom,
                    rightHeadroom
                ),
                Mathf.Min(
                    bottomHeadroom,
                    topHeadroom
                )
            );

        if (minimumHeadroom > threshold)
        {
            return false;
        }

        if (
            TerrainHeightCacheWindow.TryFitToWorld(
                desiredWindow.OriginTile,
                activeWindow.Size,
                worldGridSize,
                out TerrainHeightCacheWindow stableSizeTarget
            )
            &&
            stableSizeTarget.Contains(
                requiredWindow
            )
            &&
            stableSizeTarget !=
                activeWindow
        )
        {
            prefetchTarget =
                stableSizeTarget;

            return true;
        }

        /*
         * Active dimensions are already size-healthy. If fitting those same
         * dimensions at the latest desired origin does not move the window,
         * there is no meaningful prefetch target. Do not shrink/grow merely
         * to chase a one-tile tolerated desired-size fluctuation.
         */
        return false;
    }

    internal static bool IsActiveComfortablySufficient(
        TerrainHeightCacheWindow activeWindow,
        TerrainHeightCacheWindow requiredWindow,
        TerrainHeightCacheWindow desiredWindow,
        Vector2Int worldGridSize,
        int thresholdTiles = DefaultPrefetchThresholdTiles
    )
    {
        if (
            !activeWindow.IsValid
            ||
            !activeWindow.Contains(
                requiredWindow
            )
        )
        {
            return false;
        }

        TerrainAuthoringPreviewResidencySizeHealth sizeHealth =
            TerrainAuthoringPreviewResidencyPolicy
                .EvaluateSizeHealth(
                    true,
                    activeWindow,
                    desiredWindow
                );

        if (
            sizeHealth !=
                TerrainAuthoringPreviewResidencySizeHealth.Healthy
        )
        {
            return false;
        }

        return
            !TryCalculatePrefetchTarget(
                activeWindow,
                requiredWindow,
                desiredWindow,
                worldGridSize,
                out _,
                thresholdTiles
            );
    }

    internal static int CalculateBudgetedEndIndex(
        int cursor,
        int count,
        int maximumUnits
    )
    {
        int safeCursor =
            Mathf.Clamp(
                cursor,
                0,
                Mathf.Max(
                    0,
                    count
                )
            );

        int safeBudget =
            Mathf.Max(
                1,
                maximumUnits
            );

        return
            Mathf.Min(
                Mathf.Max(
                    0,
                    count
                ),
                safeCursor +
                    safeBudget
            );
    }
}
