using System;
using UnityEngine;

/*
 * Deterministic double-precision geometric predicates shared by the regional
 * elevation triangulation foundation.
 */
public static class TerrainNodeElevationGeometryUtility
{
    public const float MinimumTriangulationVertexSeparation =
        0.001f;

    private const double PredicateRelativeTolerance =
        1e-12;

    public static bool IsFinite(Vector2 value)
    {
        return
            IsFinite(value.x) &&
            IsFinite(value.y);
    }

    public static double Orientation(
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        return Orientation(
            a.x,
            a.y,
            b.x,
            b.y,
            c.x,
            c.y);
    }

    public static bool IsCounterClockwise(
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        double orientation = Orientation(a, b, c);
        double tolerance = OrientationTolerance(
            a.x,
            a.y,
            b.x,
            b.y,
            c.x,
            c.y);

        return orientation > tolerance;
    }

    public static bool IsEffectivelyCollinear(
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        double orientation = Orientation(a, b, c);
        double tolerance = OrientationTolerance(
            a.x,
            a.y,
            b.x,
            b.y,
            c.x,
            c.y);

        return Math.Abs(orientation) <= tolerance;
    }

    public static bool IsPointInTriangleInclusive(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        if (
            !IsFinite(point) ||
            !IsFinite(a) ||
            !IsFinite(b) ||
            !IsFinite(c))
        {
            return false;
        }

        double orientation = Orientation(a, b, c);
        double triangleTolerance = OrientationTolerance(
            a.x,
            a.y,
            b.x,
            b.y,
            c.x,
            c.y);

        if (Math.Abs(orientation) <= triangleTolerance)
        {
            return false;
        }

        double ab = Orientation(a, b, point);
        double bc = Orientation(b, c, point);
        double ca = Orientation(c, a, point);

        double tolerance = Math.Max(
            OrientationTolerance(
                a.x,
                a.y,
                b.x,
                b.y,
                point.x,
                point.y),
            Math.Max(
                OrientationTolerance(
                    b.x,
                    b.y,
                    c.x,
                    c.y,
                    point.x,
                    point.y),
                OrientationTolerance(
                    c.x,
                    c.y,
                    a.x,
                    a.y,
                    point.x,
                    point.y)));

        if (orientation > 0.0)
        {
            return
                ab >= -tolerance &&
                bc >= -tolerance &&
                ca >= -tolerance;
        }

        return
            ab <= tolerance &&
            bc <= tolerance &&
            ca <= tolerance;
    }

    internal static double Orientation(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy)
    {
        return
            (bx - ax) * (cy - ay) -
            (by - ay) * (cx - ax);
    }

    internal static double OrientationTolerance(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy)
    {
        double scale = MaxPairwiseAxisDelta(
            ax,
            ay,
            bx,
            by,
            cx,
            cy);

        double scaleSquared =
            Math.Max(1.0, scale * scale);

        return PredicateRelativeTolerance * scaleSquared;
    }

    internal static double InCircleDeterminant(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double px,
        double py)
    {
        double adx = ax - px;
        double ady = ay - py;
        double bdx = bx - px;
        double bdy = by - py;
        double cdx = cx - px;
        double cdy = cy - py;

        double abdet =
            adx * bdy -
            bdx * ady;

        double bcdet =
            bdx * cdy -
            cdx * bdy;

        double cadet =
            cdx * ady -
            adx * cdy;

        double alift =
            adx * adx +
            ady * ady;

        double blift =
            bdx * bdx +
            bdy * bdy;

        double clift =
            cdx * cdx +
            cdy * cdy;

        return
            alift * bcdet +
            blift * cadet +
            clift * abdet;
    }

    internal static double InCircleTolerance(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double px,
        double py)
    {
        double scale = Math.Max(
            MaxPairwiseAxisDelta(
                ax,
                ay,
                bx,
                by,
                cx,
                cy),
            Math.Max(
                Math.Abs(px - ax),
                Math.Max(
                    Math.Abs(py - ay),
                    Math.Max(
                        Math.Abs(px - bx),
                        Math.Max(
                            Math.Abs(py - by),
                            Math.Max(
                                Math.Abs(px - cx),
                                Math.Abs(py - cy)))))));

        double scaleSquared =
            Math.Max(1.0, scale * scale);

        double scaleFourth =
            scaleSquared * scaleSquared;

        return PredicateRelativeTolerance * scaleFourth;
    }

    internal static bool IsInsideCircumcircle(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double px,
        double py)
    {
        double determinant = InCircleDeterminant(
            ax,
            ay,
            bx,
            by,
            cx,
            cy,
            px,
            py);

        double tolerance = InCircleTolerance(
            ax,
            ay,
            bx,
            by,
            cx,
            cy,
            px,
            py);

        return determinant > tolerance;
    }

    internal static bool AreCocircular(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double px,
        double py)
    {
        double determinant = InCircleDeterminant(
            ax,
            ay,
            bx,
            by,
            cx,
            cy,
            px,
            py);

        double tolerance = InCircleTolerance(
            ax,
            ay,
            bx,
            by,
            cx,
            cy,
            px,
            py);

        return Math.Abs(determinant) <= tolerance;
    }

    internal static bool IsFinite(float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }

    internal static bool IsFinite(double value)
    {
        return
            !double.IsNaN(value) &&
            !double.IsInfinity(value);
    }

    private static double MaxPairwiseAxisDelta(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy)
    {
        return Math.Max(
            Math.Max(
                Math.Abs(ax - bx),
                Math.Abs(ay - by)),
            Math.Max(
                Math.Max(
                    Math.Abs(ax - cx),
                    Math.Abs(ay - cy)),
                Math.Max(
                    Math.Abs(bx - cx),
                    Math.Abs(by - cy))));
    }
}
