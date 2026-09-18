using System.Collections.Generic;

internal static class WorldGridViewportOverlayRegistry
{
    private static readonly IWorldGridViewportOverlay[] RegisteredOverlays =
        new IWorldGridViewportOverlay[0];

    internal static IReadOnlyList<IWorldGridViewportOverlay> Overlays
    {
        get
        {
            return
                RegisteredOverlays;
        }
    }
}
