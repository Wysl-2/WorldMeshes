using UnityEngine;

public static class TerrainStampFalloffUtility
{
    public static TerrainStampFalloffShape SanitizeShape(
        TerrainStampFalloffShape value
    )
    {
        switch (value)
        {
            case TerrainStampFalloffShape.Rectangle:
            case TerrainStampFalloffShape.Ellipse:
                return value;

            default:
                return TerrainStampFalloffShape.Rectangle;
        }
    }

    public static TerrainStampFalloffProfile SanitizeProfile(
        TerrainStampFalloffProfile value
    )
    {
        switch (value)
        {
            case TerrainStampFalloffProfile.Smooth:
            case TerrainStampFalloffProfile.Linear:
            case TerrainStampFalloffProfile.Sharp:
                return value;

            default:
                return TerrainStampFalloffProfile.Smooth;
        }
    }

    public static float SanitizeAmount(
        float value
    )
    {
        if (
            float.IsNaN(value)
            ||
            float.IsInfinity(value)
        )
        {
            return 0f;
        }

        return Mathf.Clamp01(value);
    }

    public static float Evaluate(
        Vector2 stampUV,
        TerrainStampFalloffShape shape,
        TerrainStampFalloffProfile profile,
        float falloff
    )
    {
        if (
            !IsFinite(stampUV.x)
            ||
            !IsFinite(stampUV.y)
            ||
            stampUV.x < 0f
            ||
            stampUV.x > 1f
            ||
            stampUV.y < 0f
            ||
            stampUV.y > 1f
        )
        {
            return 0f;
        }

        TerrainStampFalloffShape safeShape =
            SanitizeShape(shape);

        TerrainStampFalloffProfile safeProfile =
            SanitizeProfile(profile);

        float edgeDistance01;

        if (safeShape == TerrainStampFalloffShape.Ellipse)
        {
            Vector2 centered =
                (stampUV - new Vector2(0.5f, 0.5f)) * 2f;

            float ellipseRadius =
                centered.magnitude;

            if (ellipseRadius > 1f)
            {
                return 0f;
            }

            edgeDistance01 =
                Mathf.Clamp01(
                    1f - ellipseRadius
                );
        }
        else
        {
            float nearestEdge =
                Mathf.Min(
                    Mathf.Min(
                        stampUV.x,
                        1f - stampUV.x
                    ),
                    Mathf.Min(
                        stampUV.y,
                        1f - stampUV.y
                    )
                );

            edgeDistance01 =
                Mathf.Clamp01(
                    nearestEdge * 2f
                );
        }

        float safeFalloff =
            SanitizeAmount(falloff);

        if (safeFalloff <= 0f)
        {
            return 1f;
        }

        float transition =
            Mathf.Clamp01(
                edgeDistance01 / safeFalloff
            );

        return EvaluateProfile(
            transition,
            safeProfile
        );
    }

    public static float EvaluateProfile(
        float transition,
        TerrainStampFalloffProfile profile
    )
    {
        float t =
            Mathf.Clamp01(
                IsFinite(transition)
                    ? transition
                    : 0f
            );

        switch (SanitizeProfile(profile))
        {
            case TerrainStampFalloffProfile.Linear:
                return t;

            case TerrainStampFalloffProfile.Sharp:
            {
                float inverse =
                    1f - t;

                return Mathf.Clamp01(
                    1f - inverse * inverse
                );
            }

            default:
                return Mathf.Clamp01(
                    t * t * (3f - 2f * t)
                );
        }
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
}
