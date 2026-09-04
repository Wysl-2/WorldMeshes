using UnityEngine;

/*
 * Shared runtime-safe mathematics for the persistent terrain-stamp transform.
 *
 * Rotation is a positive Unity world-Y rotation around PositionXZ. XZ values
 * are represented as Vector2(x, z).
 */
public static class TerrainStampTransformUtility
{
    private const float FullTurnDegrees = 360f;
    private const float HalfTurnDegrees = 180f;
    private const float ComponentSnapEpsilon = 0.000001f;

    public static float NormalizeRotationDegrees(float rotationDegrees)
    {
        if (!IsFinite(rotationDegrees))
        {
            return 0f;
        }

        float normalized =
            Mathf.Repeat(rotationDegrees + HalfTurnDegrees, FullTurnDegrees)
            - HalfTurnDegrees;

        if (!IsFinite(normalized))
        {
            return 0f;
        }

        if (Mathf.Abs(normalized) <= ComponentSnapEpsilon)
        {
            return 0f;
        }

        return normalized;
    }

    public static void GetWorldAxes(
        float rotationDegrees,
        out Vector2 rightXZ,
        out Vector2 forwardXZ
    )
    {
        float radians =
            NormalizeRotationDegrees(rotationDegrees) * Mathf.Deg2Rad;

        float sine = SnapUnitComponent(Mathf.Sin(radians));
        float cosine = SnapUnitComponent(Mathf.Cos(radians));

        /*
         * Matches Quaternion.Euler(0, rotationDegrees, 0):
         * local +X -> ( cos, -sin ) in world XZ
         * local +Z -> ( sin,  cos ) in world XZ
         */
        rightXZ = new Vector2(cosine, -sine);
        forwardXZ = new Vector2(sine, cosine);
    }

    public static Vector2 WorldToLocalXZ(
        Vector2 worldXZ,
        Vector2 centerXZ,
        float rotationDegrees
    )
    {
        GetWorldAxes(
            rotationDegrees,
            out Vector2 rightXZ,
            out Vector2 forwardXZ
        );

        Vector2 delta = SanitizeVector2(worldXZ) - SanitizeVector2(centerXZ);

        return new Vector2(
            Vector2.Dot(delta, rightXZ),
            Vector2.Dot(delta, forwardXZ)
        );
    }

    public static Vector2 LocalToWorldXZ(
        Vector2 localXZ,
        Vector2 centerXZ,
        float rotationDegrees
    )
    {
        GetWorldAxes(
            rotationDegrees,
            out Vector2 rightXZ,
            out Vector2 forwardXZ
        );

        Vector2 safeLocal = SanitizeVector2(localXZ);

        return SanitizeVector2(centerXZ)
            + rightXZ * safeLocal.x
            + forwardXZ * safeLocal.y;
    }

    public static void GetWorldCorners(
        Vector2 centerXZ,
        Vector2 sizeXZ,
        float rotationDegrees,
        out Vector2 negativeXNegativeZ,
        out Vector2 positiveXNegativeZ,
        out Vector2 positiveXPositiveZ,
        out Vector2 negativeXPositiveZ
    )
    {
        Vector2 halfSize = SanitizeSize(sizeXZ) * 0.5f;

        negativeXNegativeZ = LocalToWorldXZ(
            new Vector2(-halfSize.x, -halfSize.y),
            centerXZ,
            rotationDegrees
        );

        positiveXNegativeZ = LocalToWorldXZ(
            new Vector2(halfSize.x, -halfSize.y),
            centerXZ,
            rotationDegrees
        );

        positiveXPositiveZ = LocalToWorldXZ(
            new Vector2(halfSize.x, halfSize.y),
            centerXZ,
            rotationDegrees
        );

        negativeXPositiveZ = LocalToWorldXZ(
            new Vector2(-halfSize.x, halfSize.y),
            centerXZ,
            rotationDegrees
        );
    }

    public static Bounds CalculateWorldAabb(
        Vector2 centerXZ,
        Vector2 sizeXZ,
        float rotationDegrees
    )
    {
        Vector2 safeCenter = SanitizeVector2(centerXZ);
        Vector2 safeSize = SanitizeSize(sizeXZ);

        GetWorldAxes(
            rotationDegrees,
            out Vector2 rightXZ,
            out Vector2 forwardXZ
        );

        float halfWidth = safeSize.x * 0.5f;
        float halfDepth = safeSize.y * 0.5f;

        float worldHalfExtentX =
            Mathf.Abs(rightXZ.x) * halfWidth
            + Mathf.Abs(forwardXZ.x) * halfDepth;

        float worldHalfExtentZ =
            Mathf.Abs(rightXZ.y) * halfWidth
            + Mathf.Abs(forwardXZ.y) * halfDepth;

        return new Bounds(
            new Vector3(safeCenter.x, 0f, safeCenter.y),
            new Vector3(
                worldHalfExtentX * 2f,
                0f,
                worldHalfExtentZ * 2f
            )
        );
    }

    private static Vector2 SanitizeVector2(Vector2 value)
    {
        return new Vector2(
            IsFinite(value.x) ? value.x : 0f,
            IsFinite(value.y) ? value.y : 0f
        );
    }

    private static Vector2 SanitizeSize(Vector2 value)
    {
        Vector2 safe = SanitizeVector2(value);
        return new Vector2(Mathf.Abs(safe.x), Mathf.Abs(safe.y));
    }

    private static float SnapUnitComponent(float value)
    {
        if (!IsFinite(value))
        {
            return 0f;
        }

        if (Mathf.Abs(value) <= ComponentSnapEpsilon)
        {
            return 0f;
        }

        if (Mathf.Abs(value - 1f) <= ComponentSnapEpsilon)
        {
            return 1f;
        }

        if (Mathf.Abs(value + 1f) <= ComponentSnapEpsilon)
        {
            return -1f;
        }

        return value;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
