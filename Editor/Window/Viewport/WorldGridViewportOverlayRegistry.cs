using System.Collections.Generic;

internal static class WorldGridViewportOverlayRegistry
{
    private static readonly IWorldGridViewportOverlay[] RegisteredOverlays =
    {
        new WorldGridViewportClipmapBoundsOverlay(),
        new WorldGridViewportHeightCacheBoundsOverlay(),
        new WorldGridViewportColliderBoundsOverlay()
    };

    internal static IReadOnlyList<IWorldGridViewportOverlay> Overlays
    {
        get
        {
            return
                RegisteredOverlays;
        }
    }
}
