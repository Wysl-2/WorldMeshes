using System;
using System.Collections.Generic;
using UnityEngine;

public interface IWorldGridViewportOverlay
{
    string Id
    {
        get;
    }

    string DisplayName
    {
        get;
    }

    bool IsAvailable(
        WorldGridViewportContext context
    );

    bool RequiresContinuousRepaint(
        WorldGridViewportContext context
    );

    void Draw(
        WorldGridViewportContext context
    );
}

[Serializable]
internal sealed class WorldGridViewportOverlayState
{
    [SerializeField]
    private List<string> enabledOverlayIds =
        new List<string>();

    internal bool IsUserEnabled(
        string overlayId
    )
    {
        return
            !string.IsNullOrEmpty(
                overlayId
            )
            &&
            enabledOverlayIds != null
            &&
            enabledOverlayIds.Contains(
                overlayId
            );
    }

    internal void SetUserEnabled(
        string overlayId,
        bool enabled
    )
    {
        if (string.IsNullOrEmpty(overlayId))
        {
            return;
        }

        if (enabledOverlayIds == null)
        {
            enabledOverlayIds =
                new List<string>();
        }

        bool currentlyEnabled =
            enabledOverlayIds.Contains(
                overlayId
            );

        if (enabled == currentlyEnabled)
        {
            return;
        }

        if (enabled)
        {
            enabledOverlayIds.Add(
                overlayId
            );
        }
        else
        {
            enabledOverlayIds.Remove(
                overlayId
            );
        }
    }
}
