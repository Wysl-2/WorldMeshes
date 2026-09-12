using System;
using UnityEngine;

/*
 * Pure CPU Triangulated Linear evaluation over Package I2 derived topology.
 *
 * No authoring state is mutated. Topology remains position-only; current source
 * elevations are resolved dynamically through topology SourceNodeIndex values.
 */
public static class TerrainNodeElevationLinearInterpolationUtility
{
    private const double BarycentricWeightTolerance =
        1e-9;

    private static readonly double HullDistanceTieToleranceSquared =
        (double)TerrainNodeElevationGeometryUtility
            .MinimumTriangulationVertexSeparation *
        TerrainNodeElevationGeometryUtility
            .MinimumTriangulationVertexSeparation;

    public static bool TryEvaluateHeight(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        Vector2 worldPositionXZ,
        out float height,
        out string errorMessage)
    {
        height = 0f;
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        if (topology == null)
        {
            errorMessage = "TerrainNodeElevationTopology is null.";
            return false;
        }

        if (!TerrainNodeElevationGeometryUtility.IsFinite(worldPositionXZ))
        {
            errorMessage =
                "Regional elevation sample position contains a non-finite value.";
            return false;
        }

        switch (topology.Kind)
        {
            case TerrainNodeElevationTopologyKind.Empty:
                errorMessage =
                    "TerrainNodeElevationSource contains no elevation nodes and " +
                    "does not define a Triangulated Linear regional surface.";
                return false;

            case TerrainNodeElevationTopologyKind.SinglePoint:
                if (topology.VertexCount != 1)
                {
                    errorMessage =
                        "Single-point regional topology does not contain exactly one vertex.";
                    return false;
                }

                return TryGetVertexElevation(
                    source,
                    topology,
                    0,
                    out height,
                    out errorMessage);

            case TerrainNodeElevationTopologyKind.LineSegment:
                if (topology.VertexCount != 2)
                {
                    errorMessage =
                        "Line-segment regional topology does not contain exactly two vertices.";
                    return false;
                }

                return TryEvaluateSegmentLinear(
                    source,
                    topology,
                    0,
                    1,
                    worldPositionXZ,
                    out height,
                    out _,
                    out errorMessage);

            case TerrainNodeElevationTopologyKind.Collinear:
                return TryEvaluateCollinearLinear(
                    source,
                    topology,
                    worldPositionXZ,
                    out height,
                    out errorMessage);

            case TerrainNodeElevationTopologyKind.Triangulated:
                if (topology.TriangleCount <= 0)
                {
                    errorMessage =
                        "Triangulated regional topology contains no triangles.";
                    return false;
                }

                if (topology.TryFindContainingTriangle(
                    worldPositionXZ,
                    out int triangleIndex))
                {
                    return TryEvaluateTriangleLinear(
                        source,
                        topology,
                        triangleIndex,
                        worldPositionXZ,
                        out height,
                        out errorMessage);
                }

                return TryEvaluateHullExterior(
                    source,
                    topology,
                    worldPositionXZ,
                    out height,
                    out _,
                    out errorMessage);

            default:
                errorMessage =
                    "Regional elevation topology contains an unsupported topology kind.";
                return false;
        }
    }

    internal static bool TryCalculateBarycentricWeights(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        out double weightA,
        out double weightB,
        out double weightC,
        out string errorMessage)
    {
        weightA = 0.0;
        weightB = 0.0;
        weightC = 0.0;
        errorMessage = "";

        if (
            !TerrainNodeElevationGeometryUtility.IsFinite(point) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(a) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(b) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(c))
        {
            errorMessage =
                "Barycentric interpolation received non-finite geometry.";
            return false;
        }

        if (
            TerrainNodeElevationGeometryUtility.IsEffectivelyCollinear(a, b, c))
        {
            errorMessage =
                "Barycentric interpolation received a degenerate triangle.";
            return false;
        }

        double denominator =
            TerrainNodeElevationGeometryUtility.Orientation(a, b, c);

        if (!IsFinite(denominator) || denominator == 0.0)
        {
            errorMessage =
                "Barycentric interpolation produced an invalid denominator.";
            return false;
        }

        weightA =
            TerrainNodeElevationGeometryUtility.Orientation(b, c, point) /
            denominator;

        weightB =
            TerrainNodeElevationGeometryUtility.Orientation(c, a, point) /
            denominator;

        weightC =
            TerrainNodeElevationGeometryUtility.Orientation(a, b, point) /
            denominator;

        if (
            !IsFinite(weightA) ||
            !IsFinite(weightB) ||
            !IsFinite(weightC))
        {
            errorMessage =
                "Barycentric interpolation produced non-finite weights.";
            return false;
        }

        if (!TryCorrectBarycentricWeight(ref weightA) ||
            !TryCorrectBarycentricWeight(ref weightB) ||
            !TryCorrectBarycentricWeight(ref weightC))
        {
            errorMessage =
                "Barycentric interpolation produced weights outside the triangle.";
            return false;
        }

        double sum = weightA + weightB + weightC;

        if (!IsFinite(sum) || Math.Abs(sum) <= BarycentricWeightTolerance)
        {
            errorMessage =
                "Barycentric interpolation produced an invalid weight sum.";
            return false;
        }

        if (Math.Abs(sum - 1.0) > BarycentricWeightTolerance)
        {
            if (Math.Abs(sum - 1.0) > 1e-6)
            {
                errorMessage =
                    "Barycentric interpolation produced an inconsistent weight sum.";
                return false;
            }

            weightA /= sum;
            weightB /= sum;
            weightC /= sum;
        }

        return true;
    }

    internal static bool TryProjectPointOntoSegment(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        out double t,
        out Vector2 projectedPoint,
        out double distanceSquared,
        out string errorMessage)
    {
        t = 0.0;
        projectedPoint = Vector2.zero;
        distanceSquared = 0.0;
        errorMessage = "";

        if (
            !TerrainNodeElevationGeometryUtility.IsFinite(point) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(a) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(b))
        {
            errorMessage =
                "Segment projection received non-finite geometry.";
            return false;
        }

        double abX = (double)b.x - a.x;
        double abY = (double)b.y - a.y;
        double lengthSquared = abX * abX + abY * abY;

        if (
            !IsFinite(lengthSquared) ||
            lengthSquared < HullDistanceTieToleranceSquared)
        {
            errorMessage =
                "Segment projection received an effectively degenerate segment.";
            return false;
        }

        double apX = (double)point.x - a.x;
        double apY = (double)point.y - a.y;

        double rawT =
            (apX * abX + apY * abY) /
            lengthSquared;

        if (!IsFinite(rawT))
        {
            errorMessage =
                "Segment projection produced a non-finite parameter.";
            return false;
        }

        t = Clamp01(rawT);

        double projectedX = a.x + abX * t;
        double projectedY = a.y + abY * t;
        double distanceX = (double)point.x - projectedX;
        double distanceY = (double)point.y - projectedY;

        distanceSquared =
            distanceX * distanceX +
            distanceY * distanceY;

        if (!IsFinite(distanceSquared))
        {
            errorMessage =
                "Segment projection produced a non-finite distance.";
            return false;
        }

        float projectedXFloat = (float)projectedX;
        float projectedYFloat = (float)projectedY;

        if (
            !IsFinite(projectedXFloat) ||
            !IsFinite(projectedYFloat))
        {
            errorMessage =
                "Segment projection could not be represented as a finite Vector2.";
            return false;
        }

        projectedPoint =
            new Vector2(projectedXFloat, projectedYFloat);

        return true;
    }

    internal static bool TryEvaluateTriangleLinear(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        int triangleIndex,
        Vector2 point,
        out float height,
        out string errorMessage)
    {
        height = 0f;
        errorMessage = "";

        if (
            topology == null ||
            triangleIndex < 0 ||
            triangleIndex >= topology.TriangleCount)
        {
            errorMessage =
                "Triangulated Linear evaluation received an invalid triangle index.";
            return false;
        }

        TerrainNodeElevationTopologyTriangle triangle =
            topology.Triangles[triangleIndex];

        if (!TryGetVertex(
            topology,
            triangle.VertexA,
            out TerrainNodeElevationTopologyVertex vertexA,
            out errorMessage) ||
            !TryGetVertex(
                topology,
                triangle.VertexB,
                out TerrainNodeElevationTopologyVertex vertexB,
                out errorMessage) ||
            !TryGetVertex(
                topology,
                triangle.VertexC,
                out TerrainNodeElevationTopologyVertex vertexC,
                out errorMessage))
        {
            return false;
        }

        if (point == vertexA.PositionXZ)
        {
            return TryGetSourceElevation(
                source,
                vertexA.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        if (point == vertexB.PositionXZ)
        {
            return TryGetSourceElevation(
                source,
                vertexB.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        if (point == vertexC.PositionXZ)
        {
            return TryGetSourceElevation(
                source,
                vertexC.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        if (!TryCalculateBarycentricWeights(
            point,
            vertexA.PositionXZ,
            vertexB.PositionXZ,
            vertexC.PositionXZ,
            out double weightA,
            out double weightB,
            out double weightC,
            out errorMessage))
        {
            return false;
        }

        if (!TryGetSourceElevation(
            source,
            vertexA.SourceNodeIndex,
            out float elevationA,
            out errorMessage) ||
            !TryGetSourceElevation(
                source,
                vertexB.SourceNodeIndex,
                out float elevationB,
                out errorMessage) ||
            !TryGetSourceElevation(
                source,
                vertexC.SourceNodeIndex,
                out float elevationC,
                out errorMessage))
        {
            return false;
        }

        double evaluatedHeight =
            weightA * elevationA +
            weightB * elevationB +
            weightC * elevationC;

        return TryConvertHeight(
            evaluatedHeight,
            out height,
            out errorMessage);
    }

    internal static bool TryEvaluateHullExterior(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        Vector2 point,
        out float height,
        out int selectedHullEdgeIndex,
        out string errorMessage)
    {
        height = 0f;
        selectedHullEdgeIndex = -1;
        errorMessage = "";

        if (
            topology == null ||
            topology.Kind != TerrainNodeElevationTopologyKind.Triangulated ||
            topology.HullEdgeCount <= 0)
        {
            errorMessage =
                "Triangulated regional topology does not contain a usable convex hull.";
            return false;
        }

        double bestDistanceSquared = double.PositiveInfinity;
        double bestT = 0.0;
        TerrainNodeElevationTopologyEdge bestEdge =
            default(TerrainNodeElevationTopologyEdge);

        for (int edgeIndex = 0; edgeIndex < topology.HullEdgeCount; edgeIndex++)
        {
            TerrainNodeElevationTopologyEdge edge =
                topology.HullEdges[edgeIndex];

            if (!TryGetVertex(
                topology,
                edge.VertexA,
                out TerrainNodeElevationTopologyVertex vertexA,
                out errorMessage) ||
                !TryGetVertex(
                    topology,
                    edge.VertexB,
                    out TerrainNodeElevationTopologyVertex vertexB,
                    out errorMessage))
            {
                return false;
            }

            if (!TryProjectPointOntoSegment(
                point,
                vertexA.PositionXZ,
                vertexB.PositionXZ,
                out double t,
                out _,
                out double distanceSquared,
                out errorMessage))
            {
                return false;
            }

            bool better =
                selectedHullEdgeIndex < 0 ||
                distanceSquared <
                    bestDistanceSquared - HullDistanceTieToleranceSquared;

            bool tied =
                selectedHullEdgeIndex >= 0 &&
                Math.Abs(distanceSquared - bestDistanceSquared) <=
                    HullDistanceTieToleranceSquared;

            if (better || (tied && edgeIndex < selectedHullEdgeIndex))
            {
                selectedHullEdgeIndex = edgeIndex;
                bestDistanceSquared = distanceSquared;
                bestT = t;
                bestEdge = edge;
            }
        }

        if (selectedHullEdgeIndex < 0)
        {
            errorMessage =
                "No valid convex-hull edge was available for exterior evaluation.";
            return false;
        }

        if (!TryGetVertexElevation(
            source,
            topology,
            bestEdge.VertexA,
            out float elevationA,
            out errorMessage) ||
            !TryGetVertexElevation(
                source,
                topology,
                bestEdge.VertexB,
                out float elevationB,
                out errorMessage))
        {
            return false;
        }

        double evaluatedHeight =
            (double)elevationA +
            ((double)elevationB - elevationA) * bestT;

        return TryConvertHeight(
            evaluatedHeight,
            out height,
            out errorMessage);
    }

    private static bool TryEvaluateSegmentLinear(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        int vertexIndexA,
        int vertexIndexB,
        Vector2 point,
        out float height,
        out double t,
        out string errorMessage)
    {
        height = 0f;
        t = 0.0;
        errorMessage = "";

        if (!TryGetVertex(
            topology,
            vertexIndexA,
            out TerrainNodeElevationTopologyVertex vertexA,
            out errorMessage) ||
            !TryGetVertex(
                topology,
                vertexIndexB,
                out TerrainNodeElevationTopologyVertex vertexB,
                out errorMessage))
        {
            return false;
        }

        if (point == vertexA.PositionXZ)
        {
            t = 0.0;
            return TryGetSourceElevation(
                source,
                vertexA.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        if (point == vertexB.PositionXZ)
        {
            t = 1.0;
            return TryGetSourceElevation(
                source,
                vertexB.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        if (!TryProjectPointOntoSegment(
            point,
            vertexA.PositionXZ,
            vertexB.PositionXZ,
            out t,
            out _,
            out _,
            out errorMessage))
        {
            return false;
        }

        if (!TryGetSourceElevation(
            source,
            vertexA.SourceNodeIndex,
            out float elevationA,
            out errorMessage) ||
            !TryGetSourceElevation(
                source,
                vertexB.SourceNodeIndex,
                out float elevationB,
                out errorMessage))
        {
            return false;
        }

        double evaluatedHeight =
            (double)elevationA +
            ((double)elevationB - elevationA) * t;

        return TryConvertHeight(
            evaluatedHeight,
            out height,
            out errorMessage);
    }

    private static bool TryEvaluateCollinearLinear(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        Vector2 point,
        out float height,
        out string errorMessage)
    {
        height = 0f;
        errorMessage = "";

        if (topology == null || topology.VertexCount < 3)
        {
            errorMessage =
                "Collinear regional topology does not contain enough vertices.";
            return false;
        }

        TerrainNodeElevationTopologyVertex first = topology.Vertices[0];
        TerrainNodeElevationTopologyVertex last =
            topology.Vertices[topology.VertexCount - 1];

        if (point == first.PositionXZ)
        {
            return TryGetSourceElevation(
                source,
                first.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        if (point == last.PositionXZ)
        {
            return TryGetSourceElevation(
                source,
                last.SourceNodeIndex,
                out height,
                out errorMessage);
        }

        double axisX = (double)last.PositionXZ.x - first.PositionXZ.x;
        double axisY = (double)last.PositionXZ.y - first.PositionXZ.y;
        double axisLengthSquared = axisX * axisX + axisY * axisY;

        if (
            !IsFinite(axisLengthSquared) ||
            axisLengthSquared < HullDistanceTieToleranceSquared)
        {
            errorMessage =
                "Collinear regional topology has an invalid common axis.";
            return false;
        }

        double sampleParameter =
            (((double)point.x - first.PositionXZ.x) * axisX +
             ((double)point.y - first.PositionXZ.y) * axisY) /
            axisLengthSquared;

        if (!IsFinite(sampleParameter))
        {
            errorMessage =
                "Collinear regional evaluation produced a non-finite projection.";
            return false;
        }

        if (sampleParameter <= 0.0)
        {
            return TryGetVertexElevation(
                source,
                topology,
                0,
                out height,
                out errorMessage);
        }

        if (sampleParameter >= 1.0)
        {
            return TryGetVertexElevation(
                source,
                topology,
                topology.VertexCount - 1,
                out height,
                out errorMessage);
        }

        double previousParameter = 0.0;

        for (int vertexIndex = 1; vertexIndex < topology.VertexCount; vertexIndex++)
        {
            TerrainNodeElevationTopologyVertex current =
                topology.Vertices[vertexIndex];

            if (point == current.PositionXZ)
            {
                return TryGetSourceElevation(
                    source,
                    current.SourceNodeIndex,
                    out height,
                    out errorMessage);
            }

            double currentParameter =
                (((double)current.PositionXZ.x - first.PositionXZ.x) * axisX +
                 ((double)current.PositionXZ.y - first.PositionXZ.y) * axisY) /
                axisLengthSquared;

            if (
                !IsFinite(currentParameter) ||
                currentParameter <= previousParameter)
            {
                errorMessage =
                    "Collinear topology vertex order is inconsistent with its common axis.";
                return false;
            }

            if (sampleParameter <= currentParameter)
            {
                double denominator = currentParameter - previousParameter;
                double localT =
                    (sampleParameter - previousParameter) /
                    denominator;

                localT = Clamp01(localT);

                if (!TryGetVertexElevation(
                    source,
                    topology,
                    vertexIndex - 1,
                    out float elevationA,
                    out errorMessage) ||
                    !TryGetVertexElevation(
                        source,
                        topology,
                        vertexIndex,
                        out float elevationB,
                        out errorMessage))
                {
                    return false;
                }

                double evaluatedHeight =
                    (double)elevationA +
                    ((double)elevationB - elevationA) * localT;

                return TryConvertHeight(
                    evaluatedHeight,
                    out height,
                    out errorMessage);
            }

            previousParameter = currentParameter;
        }

        return TryGetVertexElevation(
            source,
            topology,
            topology.VertexCount - 1,
            out height,
            out errorMessage);
    }

    private static bool TryGetVertexElevation(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        int vertexIndex,
        out float elevation,
        out string errorMessage)
    {
        elevation = 0f;
        errorMessage = "";

        if (!TryGetVertex(
            topology,
            vertexIndex,
            out TerrainNodeElevationTopologyVertex vertex,
            out errorMessage))
        {
            return false;
        }

        return TryGetSourceElevation(
            source,
            vertex.SourceNodeIndex,
            out elevation,
            out errorMessage);
    }

    private static bool TryGetVertex(
        TerrainNodeElevationTopology topology,
        int vertexIndex,
        out TerrainNodeElevationTopologyVertex vertex,
        out string errorMessage)
    {
        vertex = default(TerrainNodeElevationTopologyVertex);
        errorMessage = "";

        if (
            topology == null ||
            vertexIndex < 0 ||
            vertexIndex >= topology.VertexCount)
        {
            errorMessage =
                "Regional topology contains an invalid vertex index.";
            return false;
        }

        vertex = topology.Vertices[vertexIndex];
        return true;
    }

    private static bool TryGetSourceElevation(
        TerrainNodeElevationSource source,
        int sourceNodeIndex,
        out float elevation,
        out string errorMessage)
    {
        elevation = 0f;
        errorMessage = "";

        if (
            source == null ||
            sourceNodeIndex < 0 ||
            sourceNodeIndex >= source.NodeCount)
        {
            errorMessage =
                "Regional topology contains an invalid SourceNodeIndex mapping.";
            return false;
        }

        TerrainElevationNode node =
            source.Nodes[sourceNodeIndex];

        if (node == null)
        {
            errorMessage =
                "Regional topology maps to a null source elevation node.";
            return false;
        }

        elevation = node.Elevation;

        if (!IsFinite(elevation))
        {
            errorMessage =
                "Regional topology maps to a non-finite source elevation.";
            elevation = 0f;
            return false;
        }

        return true;
    }

    private static bool TryCorrectBarycentricWeight(
        ref double weight)
    {
        if (!IsFinite(weight))
        {
            return false;
        }

        if (weight < 0.0)
        {
            if (weight < -BarycentricWeightTolerance)
            {
                return false;
            }

            weight = 0.0;
        }
        else if (weight > 1.0)
        {
            if (weight > 1.0 + BarycentricWeightTolerance)
            {
                return false;
            }

            weight = 1.0;
        }

        return true;
    }

    private static bool TryConvertHeight(
        double value,
        out float height,
        out string errorMessage)
    {
        height = 0f;
        errorMessage = "";

        if (
            !IsFinite(value) ||
            value > float.MaxValue ||
            value < -float.MaxValue)
        {
            errorMessage =
                "Triangulated Linear interpolation produced a non-finite or " +
                "out-of-range height.";
            return false;
        }

        float result = (float)value;

        if (!IsFinite(result))
        {
            errorMessage =
                "Triangulated Linear interpolation could not be represented " +
                "as a finite float height.";
            return false;
        }

        height = result;
        return true;
    }

    private static double Clamp01(double value)
    {
        if (value <= 0.0)
        {
            return 0.0;
        }

        if (value >= 1.0)
        {
            return 1.0;
        }

        return value;
    }

    private static bool IsFinite(float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return
            !double.IsNaN(value) &&
            !double.IsInfinity(value);
    }
}
