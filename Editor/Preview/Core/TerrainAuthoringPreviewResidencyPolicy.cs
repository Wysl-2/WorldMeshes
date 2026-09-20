using UnityEngine;

internal enum TerrainAuthoringPreviewResidencySizeHealth
{
    Unavailable,
    Healthy,
    Undersized,
    Oversized
}

internal readonly struct TerrainAuthoringPreviewResidencyDecision
{
    public TerrainHeightCacheWindow RequiredWindow { get; }

    public TerrainHeightCacheWindow DesiredWindow { get; }

    public bool HasActiveWindow { get; }

    public TerrainHeightCacheWindow ActiveWindow { get; }

    public bool ActiveCoversRequired { get; }

    public TerrainAuthoringPreviewResidencySizeHealth SizeHealth { get; }

    public bool TransitionRequired { get; }

    public bool TransitionIsCoverageCritical { get; }

    public TerrainHeightCacheWindow TargetWindow { get; }

    public int SizeToleranceTiles { get; }

    internal TerrainAuthoringPreviewResidencyDecision(
        TerrainHeightCacheWindow requiredWindow,
        TerrainHeightCacheWindow desiredWindow,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        bool activeCoversRequired,
        TerrainAuthoringPreviewResidencySizeHealth sizeHealth,
        bool transitionRequired,
        bool transitionIsCoverageCritical,
        TerrainHeightCacheWindow targetWindow,
        int sizeToleranceTiles
    )
    {
        RequiredWindow =
            requiredWindow;

        DesiredWindow =
            desiredWindow;

        HasActiveWindow =
            hasActiveWindow;

        ActiveWindow =
            activeWindow;

        ActiveCoversRequired =
            activeCoversRequired;

        SizeHealth =
            sizeHealth;

        TransitionRequired =
            transitionRequired;

        TransitionIsCoverageCritical =
            transitionIsCoverageCritical;

        TargetWindow =
            targetWindow;

        SizeToleranceTiles =
            sizeToleranceTiles;
    }
}

/*
 * Pure Package 03A residency policy.
 *
 * RequiredWindow is the correctness boundary. DesiredWindow is the guarded
 * resident target. Active coverage safety and active size health are
 * deliberately independent so a cache that still covers the clipmap may be
 * compacted without blocking Scene View placement.
 */
internal static class TerrainAuthoringPreviewResidencyPolicy
{
    internal const int DefaultResidentSizeToleranceTiles =
        1;

    internal static bool TryEvaluate(
        TerrainHeightCacheWindow requiredWindow,
        TerrainHeightCacheWindow desiredWindow,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        out TerrainAuthoringPreviewResidencyDecision decision,
        out string errorMessage,
        int sizeToleranceTiles = DefaultResidentSizeToleranceTiles
    )
    {
        decision =
            default;

        errorMessage =
            "";

        if (!requiredWindow.IsValid)
        {
            errorMessage =
                "The required height-cache residency window is invalid.";

            return false;
        }

        if (!desiredWindow.IsValid)
        {
            errorMessage =
                "The desired height-cache residency window is invalid.";

            return false;
        }

        if (!desiredWindow.Contains(requiredWindow))
        {
            errorMessage =
                "The desired resident window does not contain the complete " +
                "sample-safe required window.";

            return false;
        }

        if (
            hasActiveWindow
            &&
            !activeWindow.IsValid
        )
        {
            errorMessage =
                "The supplied active height-cache residency window is invalid.";

            return false;
        }

        int tolerance =
            Mathf.Max(
                0,
                sizeToleranceTiles
            );

        bool activeCoversRequired =
            hasActiveWindow
            &&
            activeWindow.Contains(
                requiredWindow
            );

        TerrainAuthoringPreviewResidencySizeHealth sizeHealth =
            EvaluateSizeHealth(
                hasActiveWindow,
                activeWindow,
                desiredWindow,
                tolerance
            );

        bool transitionIsCoverageCritical =
            !hasActiveWindow
            ||
            !activeCoversRequired;

        bool transitionRequired =
            transitionIsCoverageCritical
            ||
            sizeHealth ==
                TerrainAuthoringPreviewResidencySizeHealth.Oversized
            ||
            sizeHealth ==
                TerrainAuthoringPreviewResidencySizeHealth.Undersized;

        decision =
            new TerrainAuthoringPreviewResidencyDecision(
                requiredWindow,
                desiredWindow,
                hasActiveWindow,
                activeWindow,
                activeCoversRequired,
                sizeHealth,
                transitionRequired,
                transitionIsCoverageCritical,
                desiredWindow,
                tolerance
            );

        return true;
    }

    internal static TerrainAuthoringPreviewResidencySizeHealth EvaluateSizeHealth(
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        TerrainHeightCacheWindow desiredWindow,
        int sizeToleranceTiles = DefaultResidentSizeToleranceTiles
    )
    {
        if (
            !hasActiveWindow
            ||
            !activeWindow.IsValid
            ||
            !desiredWindow.IsValid
        )
        {
            return
                TerrainAuthoringPreviewResidencySizeHealth.Unavailable;
        }

        int tolerance =
            Mathf.Max(
                0,
                sizeToleranceTiles
            );

        bool oversized =
            activeWindow.Width >
                desiredWindow.Width + tolerance
            ||
            activeWindow.Height >
                desiredWindow.Height + tolerance;

        if (oversized)
        {
            return
                TerrainAuthoringPreviewResidencySizeHealth.Oversized;
        }

        bool undersized =
            activeWindow.Width + tolerance <
                desiredWindow.Width
            ||
            activeWindow.Height + tolerance <
                desiredWindow.Height;

        if (undersized)
        {
            return
                TerrainAuthoringPreviewResidencySizeHealth.Undersized;
        }

        return
            TerrainAuthoringPreviewResidencySizeHealth.Healthy;
    }

    internal static bool TrySelectPreferredBuildWindow(
        Vector2Int worldGridSize,
        bool hasRequestedWindow,
        TerrainHeightCacheWindow requestedWindow,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        bool hasDesiredWindow,
        TerrainHeightCacheWindow desiredWindow,
        out TerrainHeightCacheWindow buildWindow
    )
    {
        buildWindow =
            default;

        if (
            hasRequestedWindow
            &&
            IsWindowInsideWorldGrid(
                requestedWindow,
                worldGridSize
            )
        )
        {
            buildWindow =
                requestedWindow;

            return true;
        }

        bool activeValid =
            hasActiveWindow
            &&
            IsWindowInsideWorldGrid(
                activeWindow,
                worldGridSize
            );

        bool desiredValid =
            hasDesiredWindow
            &&
            IsWindowInsideWorldGrid(
                desiredWindow,
                worldGridSize
            );

        if (
            activeValid
            &&
            desiredValid
        )
        {
            TerrainAuthoringPreviewResidencySizeHealth sizeHealth =
                EvaluateSizeHealth(
                    true,
                    activeWindow,
                    desiredWindow
                );

            buildWindow =
                sizeHealth ==
                    TerrainAuthoringPreviewResidencySizeHealth.Healthy
                    ? activeWindow
                    : desiredWindow;

            return true;
        }

        if (desiredValid)
        {
            buildWindow =
                desiredWindow;

            return true;
        }

        if (activeValid)
        {
            buildWindow =
                activeWindow;

            return true;
        }

        return false;
    }

    internal static bool IsWindowInsideWorldGrid(
        TerrainHeightCacheWindow window,
        Vector2Int worldGridSize
    )
    {
        return
            window.IsValid
            &&
            worldGridSize.x > 0
            &&
            worldGridSize.y > 0
            &&
            window.OriginTile.x >= 0
            &&
            window.OriginTile.y >= 0
            &&
            window.MaximumExclusive.x <=
                worldGridSize.x
            &&
            window.MaximumExclusive.y <=
                worldGridSize.y;
    }
}
