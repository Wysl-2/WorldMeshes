using System;
using UnityEngine;

[Serializable]
public sealed class WorldGridViewportTransform
{
    private const float MinimumPixelsPerMeter =
        0.0001f;

    [SerializeField]
    private Vector2 viewCenterWorldXZ =
        Vector2.zero;

    [SerializeField]
    private float pixelsPerMeter =
        1f;

    [SerializeField]
    private bool initialized;

    public bool IsInitialized
    {
        get
        {
            return
                initialized;
        }
    }

    public Vector2 ViewCenterWorldXZ
    {
        get
        {
            return
                viewCenterWorldXZ;
        }
    }

    public float PixelsPerMeter
    {
        get
        {
            return
                GetSafePixelsPerMeter();
        }
    }

    public void Initialize(
        Vector2 worldCenterXZ,
        float initialPixelsPerMeter
    )
    {
        viewCenterWorldXZ =
            IsFinite(worldCenterXZ)
                ? worldCenterXZ
                : Vector2.zero;

        pixelsPerMeter =
            SanitizePixelsPerMeter(
                initialPixelsPerMeter
            );

        initialized =
            true;
    }

    public Vector2 WorldToViewport(
        Vector2 worldPositionXZ,
        Rect viewport
    )
    {
        float safePixelsPerMeter =
            GetSafePixelsPerMeter();

        Vector2 deltaWorld =
            worldPositionXZ -
            viewCenterWorldXZ;

        return
            new Vector2(
                viewport.center.x +
                    deltaWorld.x *
                    safePixelsPerMeter,

                viewport.center.y -
                    deltaWorld.y *
                    safePixelsPerMeter
            );
    }

    public Vector2 ViewportToWorld(
        Vector2 viewportPosition,
        Rect viewport
    )
    {
        float safePixelsPerMeter =
            GetSafePixelsPerMeter();

        float deltaViewportX =
            viewportPosition.x -
            viewport.center.x;

        float deltaViewportY =
            viewportPosition.y -
            viewport.center.y;

        return
            new Vector2(
                viewCenterWorldXZ.x +
                    deltaViewportX /
                    safePixelsPerMeter,

                viewCenterWorldXZ.y -
                    deltaViewportY /
                    safePixelsPerMeter
            );
    }

    public void PanByPixels(
        Vector2 pixelDelta
    )
    {
        float safePixelsPerMeter =
            GetSafePixelsPerMeter();

        Vector2 worldDelta =
            new Vector2(
                pixelDelta.x /
                    safePixelsPerMeter,

                -pixelDelta.y /
                    safePixelsPerMeter
            );

        viewCenterWorldXZ -=
            worldDelta;
    }

    private float GetSafePixelsPerMeter()
    {
        float safePixelsPerMeter =
            SanitizePixelsPerMeter(
                pixelsPerMeter
            );

        if (safePixelsPerMeter != pixelsPerMeter)
        {
            pixelsPerMeter =
                safePixelsPerMeter;
        }

        return
            safePixelsPerMeter;
    }

    private static float SanitizePixelsPerMeter(
        float value
    )
    {
        if (
            !IsFinite(value)
            ||
            value < MinimumPixelsPerMeter
        )
        {
            return
                MinimumPixelsPerMeter;
        }

        return
            value;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }

    private static bool IsFinite(
        Vector2 value
    )
    {
        return
            IsFinite(value.x)
            &&
            IsFinite(value.y);
    }
}
