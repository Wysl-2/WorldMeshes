using System;
using UnityEditor;
using UnityEngine;

internal sealed class WorldGridViewportColliderBoundsOverlay :
    IWorldGridViewportOverlay,
    IWorldGridViewportOverlayInvalidationSource
{
    private const string OverlayId =
        "collider-bounds";

    private const float LineThickness =
        2f;

    private static readonly Color BoundsColor =
        new Color(
            0.38f,
            1f,
            0.46f,
            1f
        );

    private readonly WorldGridViewportColliderBoundsProvider provider =
        new WorldGridViewportColliderBoundsProvider();

    public event Action<string> OverlayInvalidated;

    internal WorldGridViewportColliderBoundsOverlay()
    {
        provider.BoundsChanged +=
            HandleBoundsChanged;
    }

    public string Id
    {
        get
        {
            return
                OverlayId;
        }
    }

    public string DisplayName
    {
        get
        {
            return
                "Collider Bounds";
        }
    }

    public bool IsAvailable(
        WorldGridViewportContext context
    )
    {
        if (
            context.Transform == null
            ||
            !context.Transform.IsInitialized
            ||
            context.ViewportRect.width <= 0f
            ||
            context.ViewportRect.height <= 0f
            ||
            context.IsTransitioning
            ||
            context.ExecutionState !=
                WorldGridViewportExecutionState.PlayMode
        )
        {
            return false;
        }

        return
            provider.TryGetBounds(
                context,
                out _,
                out _
            );
    }

    public bool RequiresContinuousRepaint(
        WorldGridViewportContext context
    )
    {
        _ =
            context;

        return false;
    }

    public void Draw(
        WorldGridViewportContext context
    )
    {
        if (
            context.Transform == null
            ||
            !context.Transform.IsInitialized
            ||
            context.ExecutionState !=
                WorldGridViewportExecutionState.PlayMode
            ||
            !provider.TryGetBounds(
                context,
                out Vector2 minimumXZ,
                out Vector2 maximumXZ
            )
        )
        {
            return;
        }

        Vector2 firstPoint =
            context.Transform.WorldToViewport(
                minimumXZ,
                context.ViewportRect
            );

        Vector2 secondPoint =
            context.Transform.WorldToViewport(
                maximumXZ,
                context.ViewportRect
            );

        float xMin =
            Mathf.Min(
                firstPoint.x,
                secondPoint.x
            );

        float xMax =
            Mathf.Max(
                firstPoint.x,
                secondPoint.x
            );

        float yMin =
            Mathf.Min(
                firstPoint.y,
                secondPoint.y
            );

        float yMax =
            Mathf.Max(
                firstPoint.y,
                secondPoint.y
            );

        if (
            !IsFinite(xMin)
            ||
            !IsFinite(xMax)
            ||
            !IsFinite(yMin)
            ||
            !IsFinite(yMax)
        )
        {
            return;
        }

        float halfThickness =
            LineThickness *
            0.5f;

        DrawClippedEdge(
            new Rect(
                xMin,
                yMin - halfThickness,
                Mathf.Max(
                    0f,
                    xMax - xMin
                ),
                LineThickness
            ),
            context.ViewportRect
        );

        DrawClippedEdge(
            new Rect(
                xMin,
                yMax - halfThickness,
                Mathf.Max(
                    0f,
                    xMax - xMin
                ),
                LineThickness
            ),
            context.ViewportRect
        );

        DrawClippedEdge(
            new Rect(
                xMin - halfThickness,
                yMin,
                LineThickness,
                Mathf.Max(
                    0f,
                    yMax - yMin
                )
            ),
            context.ViewportRect
        );

        DrawClippedEdge(
            new Rect(
                xMax - halfThickness,
                yMin,
                LineThickness,
                Mathf.Max(
                    0f,
                    yMax - yMin
                )
            ),
            context.ViewportRect
        );
    }

    private void HandleBoundsChanged()
    {
        OverlayInvalidated?.Invoke(
            OverlayId
        );
    }

    private static void DrawClippedEdge(
        Rect edge,
        Rect viewport
    )
    {
        float xMin =
            Mathf.Max(
                edge.xMin,
                viewport.xMin
            );

        float xMax =
            Mathf.Min(
                edge.xMax,
                viewport.xMax
            );

        float yMin =
            Mathf.Max(
                edge.yMin,
                viewport.yMin
            );

        float yMax =
            Mathf.Min(
                edge.yMax,
                viewport.yMax
            );

        if (
            xMax <= xMin
            ||
            yMax <= yMin
        )
        {
            return;
        }

        EditorGUI.DrawRect(
            Rect.MinMaxRect(
                xMin,
                yMin,
                xMax,
                yMax
            ),
            BoundsColor
        );
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
