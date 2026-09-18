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

    public bool ZoomAtViewportPoint(
        Vector2 viewportPosition,
        Rect viewport,
        float zoomFactor,
        float minimumPixelsPerMeter,
        float maximumPixelsPerMeter
    )
    {
        if (
            !initialized
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
            ||
            !IsFinite(viewportPosition)
            ||
            !IsFinite(zoomFactor)
            ||
            zoomFactor <= 0f
        )
        {
            return false;
        }

        float safeMinimum =
            SanitizePixelsPerMeter(
                minimumPixelsPerMeter
            );

        float safeMaximum =
            SanitizePixelsPerMeter(
                maximumPixelsPerMeter
            );

        if (safeMaximum < safeMinimum)
        {
            safeMaximum =
                safeMinimum;
        }

        Vector2 worldBefore =
            ViewportToWorld(
                viewportPosition,
                viewport
            );

        float currentPixelsPerMeter =
            GetSafePixelsPerMeter();

        float requestedPixelsPerMeter =
            currentPixelsPerMeter *
            zoomFactor;

        if (!IsFinite(requestedPixelsPerMeter))
        {
            requestedPixelsPerMeter =
                zoomFactor > 1f
                    ? safeMaximum
                    : safeMinimum;
        }

        float newPixelsPerMeter =
            Mathf.Clamp(
                requestedPixelsPerMeter,
                safeMinimum,
                safeMaximum
            );

        if (
            Mathf.Approximately(
                newPixelsPerMeter,
                currentPixelsPerMeter
            )
        )
        {
            return false;
        }

        pixelsPerMeter =
            newPixelsPerMeter;

        Vector2 worldAfter =
            ViewportToWorld(
                viewportPosition,
                viewport
            );

        Vector2 centerAdjustment =
            worldBefore -
            worldAfter;

        if (IsFinite(centerAdjustment))
        {
            viewCenterWorldXZ +=
                centerAdjustment;
        }

        return true;
    }

    public bool FrameWorld(
        Vector2 worldSizeXZ,
        Rect viewport,
        float paddingPixels
    )
    {
        if (
            !IsValidWorldSize(
                worldSizeXZ
            )
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return false;
        }

        viewCenterWorldXZ =
            worldSizeXZ *
                0.5f;

        pixelsPerMeter =
            CalculateFramePixelsPerMeter(
                worldSizeXZ,
                viewport,
                paddingPixels
            );

        initialized =
            true;

        return true;
    }

    public static float CalculateFramePixelsPerMeter(
        Vector2 worldSizeXZ,
        Rect viewport,
        float paddingPixels
    )
    {
        if (
            !IsValidWorldSize(
                worldSizeXZ
            )
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return
                MinimumPixelsPerMeter;
        }

        float safePadding =
            IsFinite(paddingPixels)
                ? Mathf.Max(
                    0f,
                    paddingPixels
                )
                : 0f;

        float usableWidth =
            Mathf.Max(
                1f,
                viewport.width -
                    safePadding *
                    2f
            );

        float usableHeight =
            Mathf.Max(
                1f,
                viewport.height -
                    safePadding *
                    2f
            );

        float scaleX =
            usableWidth /
            worldSizeXZ.x;

        float scaleZ =
            usableHeight /
            worldSizeXZ.y;

        return
            SanitizePixelsPerMeter(
                Mathf.Min(
                    scaleX,
                    scaleZ
                )
            );
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

    private static bool IsValidWorldSize(
        Vector2 value
    )
    {
        return
            IsFinite(value)
            &&
            value.x > 0f
            &&
            value.y > 0f;
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
