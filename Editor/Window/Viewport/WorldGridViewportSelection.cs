using UnityEngine;

public sealed class WorldGridViewportSelection
{
    public string ProviderId
    {
        get;
    }

    public string ItemId
    {
        get;
    }

    public string DisplayName
    {
        get;
    }

    /*
     * Rect uses X as world X and Y as world Z.
     */
    public Rect WorldBoundsXZ
    {
        get;
    }

    public WorldGridViewportSelection(
        string providerId,
        string itemId,
        string displayName,
        Rect worldBoundsXZ
    )
    {
        ProviderId =
            providerId ??
            "";

        ItemId =
            itemId ??
            "";

        DisplayName =
            displayName ??
            "";

        WorldBoundsXZ =
            worldBoundsXZ;
    }
}
