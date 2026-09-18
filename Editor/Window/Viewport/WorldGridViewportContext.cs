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

    public WorldGridViewportExecutionState ExecutionState
    {
        get;
    }

    public bool IsPlaying
    {
        get
        {
            return
                ExecutionState ==
                WorldGridViewportExecutionState.PlayMode;
        }
    }

    public bool IsTransitioning
    {
        get
        {
            return
                ExecutionState ==
                    WorldGridViewportExecutionState.EnteringPlayMode
                ||
                ExecutionState ==
                    WorldGridViewportExecutionState.ExitingPlayMode;
        }
    }

    public bool IsPaused
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
        WorldGridViewportExecutionState executionState,
        bool isPaused,
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

        ExecutionState =
            executionState;

        IsPaused =
            isPaused;

        IsMouseOverViewport =
            isMouseOverViewport;

        MouseWorldXZ =
            mouseWorldXZ;

        HasMouseWorldPosition =
            hasMouseWorldPosition;
    }
}
