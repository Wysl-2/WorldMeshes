using UnityEngine;

public readonly struct WorldGridViewportContext
{
    public WorldSettings WorldSettings
    {
        get;
    }

    public WorldGridViewportTransform Transform
    {
        get;
    }

    public Rect ViewportRect
    {
        get;
    }

    public bool IsPlaying
    {
        get;
    }

    public bool IsMouseOverViewport
    {
        get;
    }

    public Vector2 MouseWorldXZ
    {
        get;
    }

    public bool HasMouseWorldPosition
    {
        get;
    }

    public WorldGridViewportContext(
        WorldSettings worldSettings,
        WorldGridViewportTransform transform,
        Rect viewportRect,
        bool isPlaying,
        bool isMouseOverViewport,
        Vector2 mouseWorldXZ,
        bool hasMouseWorldPosition
    )
    {
        WorldSettings =
            worldSettings;

        Transform =
            transform;

        ViewportRect =
            viewportRect;

        IsPlaying =
            isPlaying;

        IsMouseOverViewport =
            isMouseOverViewport;

        MouseWorldXZ =
            mouseWorldXZ;

        HasMouseWorldPosition =
            hasMouseWorldPosition;
    }
}
