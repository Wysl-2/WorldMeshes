using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Package I5 production gradient derivation for node-based regional elevation.
 *
 * Package I2 topology/adjacency is authoritative. For each canonical topology
 * vertex this utility fits the local difference-form plane
 *
 *     dh ~= (dh/dx) * dx + (dh/dz) * dz
 *
 * with inverse-distance-squared weighted least squares. Degenerate or poorly
 * conditioned neighborhoods use a deterministic minimum-norm one-dimensional
 * solve along the dominant geometric direction.
 *
 * All accumulation and solving is performed in double precision. The returned
 * gradient field is derived only and never mutates or serializes source data.
 */
public static class TerrainNodeElevationGradientUtility
{
    /*
     * A and C are sums of weighted squared offsets where w=1/r^2, so their
     * scale is approximately the number of independent neighbor directions.
     * This relative threshold rejects nearly rank-one normal matrices without
     * treating ordinary irregular triangulations as singular.
     */
    public const double RelativeConditioningTolerance =
        1e-10;

    public static bool TryBuild(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        out TerrainNodeElevationGradientData gradientData,
        out string errorMessage)
    {
        gradientData = null;
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

        if (!source.TryValidateOutputData(out string sourceError))
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;
            return false;
        }

        if (topology.VertexCount != source.NodeCount)
        {
            errorMessage =
                "Regional elevation gradient derivation requires topology " +
                "vertex count to match the source node count.";
            return false;
        }

        if (topology.VertexCount == 0)
        {
            if (topology.Kind != TerrainNodeElevationTopologyKind.Empty)
            {
                errorMessage =
                    "A zero-vertex regional topology must use the Empty " +
                    "topology kind.";
                return false;
            }

            gradientData =
                new TerrainNodeElevationGradientData(
                    new TerrainNodeElevationGradient[0]);
            return true;
        }

        if (topology.Kind == TerrainNodeElevationTopologyKind.Empty)
        {
            errorMessage =
                "An Empty regional topology cannot contain vertices.";
            return false;
        }

        IReadOnlyList<TerrainElevationNode> sourceNodes =
            source.Nodes;

        TerrainNodeElevationGradient[] gradients =
            new TerrainNodeElevationGradient[topology.VertexCount];

        bool[] mappedSourceNodes =
            new bool[source.NodeCount];

        for (int vertexIndex = 0;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            TerrainNodeElevationTopologyVertex vertex =
                topology.Vertices[vertexIndex];

            int sourceNodeIndex =
                vertex.SourceNodeIndex;

            if (
                sourceNodeIndex < 0 ||
                sourceNodeIndex >= sourceNodes.Count)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} contains invalid source " +
                    $"node index {sourceNodeIndex}.";
                return false;
            }

            if (mappedSourceNodes[sourceNodeIndex])
            {
                errorMessage =
                    $"Topology source mapping duplicates source node index " +
                    $"{sourceNodeIndex}.";
                return false;
            }

            mappedSourceNodes[sourceNodeIndex] = true;

            TerrainElevationNode sourceNode =
                sourceNodes[sourceNodeIndex];

            if (sourceNode == null)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} maps to a null source node.";
                return false;
            }

            if (!sourceNode.TryGetStoredPositionXZInternal(
                out Vector2 storedPosition,
                out string positionError))
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} could not resolve valid " +
                    "source geometry. " +
                    positionError;
                return false;
            }

            if (storedPosition != vertex.PositionXZ)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} is stale relative to " +
                    "its mapped source node position.";
                return false;
            }
        }

        for (int sourceIndex = 0;
            sourceIndex < mappedSourceNodes.Length;
            sourceIndex++)
        {
            if (!mappedSourceNodes[sourceIndex])
            {
                errorMessage =
                    $"Topology does not map source node index {sourceIndex}.";
                return false;
            }
        }

        for (int vertexIndex = 0;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            if (!TryBuildVertexGradient(
                sourceNodes,
                topology,
                vertexIndex,
                out TerrainNodeElevationGradient gradient,
                out errorMessage))
            {
                return false;
            }

            gradients[vertexIndex] =
                gradient;
        }

        gradientData =
            new TerrainNodeElevationGradientData(
                gradients);

        return true;
    }

    private static bool TryBuildVertexGradient(
        IReadOnlyList<TerrainElevationNode> sourceNodes,
        TerrainNodeElevationTopology topology,
        int vertexIndex,
        out TerrainNodeElevationGradient gradient,
        out string errorMessage)
    {
        gradient =
            default(TerrainNodeElevationGradient);

        errorMessage = "";

        TerrainNodeElevationTopologyVertex centerVertex =
            topology.Vertices[vertexIndex];

        TerrainElevationNode centerNode =
            sourceNodes[centerVertex.SourceNodeIndex];

        Vector2 centerPosition =
            centerVertex.PositionXZ;

        double centerElevation =
            centerNode.Elevation;

        IReadOnlyList<int> neighbors =
            topology.GetVertexNeighbors(vertexIndex);

        if (neighbors == null || neighbors.Count == 0)
        {
            if (
                topology.Kind != TerrainNodeElevationTopologyKind.SinglePoint ||
                topology.VertexCount != 1)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} has no effective " +
                    "neighbors in a non-single-point topology.";
                return false;
            }

            gradient =
                new TerrainNodeElevationGradient(
                    0f,
                    0f);
            return true;
        }

        double a = 0.0;
        double b = 0.0;
        double c = 0.0;
        double d = 0.0;
        double e = 0.0;

        int previousNeighborIndex = -1;

        double minimumSeparation =
            TerrainNodeElevationGeometryUtility
                .MinimumTriangulationVertexSeparation;

        double minimumSeparationSquared =
            minimumSeparation * minimumSeparation;

        for (int neighborListIndex = 0;
            neighborListIndex < neighbors.Count;
            neighborListIndex++)
        {
            int neighborVertexIndex =
                neighbors[neighborListIndex];

            if (
                neighborVertexIndex < 0 ||
                neighborVertexIndex >= topology.VertexCount ||
                neighborVertexIndex == vertexIndex)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} contains invalid neighbor " +
                    $"index {neighborVertexIndex}.";
                return false;
            }

            if (neighborVertexIndex <= previousNeighborIndex)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} adjacency is not in the " +
                    "deterministic canonical order required by Package I5.";
                return false;
            }

            previousNeighborIndex =
                neighborVertexIndex;

            TerrainNodeElevationTopologyVertex neighborVertex =
                topology.Vertices[neighborVertexIndex];

            int neighborSourceIndex =
                neighborVertex.SourceNodeIndex;

            if (
                neighborSourceIndex < 0 ||
                neighborSourceIndex >= sourceNodes.Count)
            {
                errorMessage =
                    $"Topology neighbor vertex {neighborVertexIndex} contains " +
                    $"invalid source node index {neighborSourceIndex}.";
                return false;
            }

            TerrainElevationNode neighborNode =
                sourceNodes[neighborSourceIndex];

            double dx =
                (double)neighborVertex.PositionXZ.x -
                centerPosition.x;

            double dz =
                (double)neighborVertex.PositionXZ.y -
                centerPosition.y;

            double dh =
                (double)neighborNode.Elevation -
                centerElevation;

            double distanceSquared =
                dx * dx +
                dz * dz;

            if (
                !IsFinite(dx) ||
                !IsFinite(dz) ||
                !IsFinite(dh) ||
                !IsFinite(distanceSquared) ||
                distanceSquared < minimumSeparationSquared)
            {
                errorMessage =
                    $"Topology edge ({vertexIndex}, {neighborVertexIndex}) " +
                    "contains invalid or unsafe gradient geometry.";
                return false;
            }

            double weight =
                1.0 / distanceSquared;

            a += weight * dx * dx;
            b += weight * dx * dz;
            c += weight * dz * dz;
            d += weight * dx * dh;
            e += weight * dz * dh;

            if (
                !IsFinite(a) ||
                !IsFinite(b) ||
                !IsFinite(c) ||
                !IsFinite(d) ||
                !IsFinite(e))
            {
                errorMessage =
                    $"Gradient accumulation overflowed for topology vertex " +
                    $"{vertexIndex}.";
                return false;
            }
        }

        bool forceDirectionalSolve =
            topology.Kind == TerrainNodeElevationTopologyKind.LineSegment ||
            topology.Kind == TerrainNodeElevationTopologyKind.Collinear;

        double gradientX;
        double gradientZ;

        if (
            !forceDirectionalSolve &&
            IsWellConditioned(
                a,
                b,
                c,
                out double determinant))
        {
            gradientX =
                (d * c - b * e) /
                determinant;

            gradientZ =
                (a * e - b * d) /
                determinant;
        }
        else
        {
            if (!TrySolveDominantDirection(
                a,
                b,
                c,
                d,
                e,
                out gradientX,
                out gradientZ))
            {
                errorMessage =
                    $"Could not derive a stable directional gradient for " +
                    $"topology vertex {vertexIndex}.";
                return false;
            }
        }

        if (
            !IsFinite(gradientX) ||
            !IsFinite(gradientZ) ||
            gradientX > float.MaxValue ||
            gradientX < -float.MaxValue ||
            gradientZ > float.MaxValue ||
            gradientZ < -float.MaxValue)
        {
            errorMessage =
                $"Derived gradient for topology vertex {vertexIndex} is " +
                "non-finite or outside the supported float range.";
            return false;
        }

        gradient =
            new TerrainNodeElevationGradient(
                (float)gradientX,
                (float)gradientZ);

        return true;
    }

    private static bool IsWellConditioned(
        double a,
        double b,
        double c,
        out double determinant)
    {
        determinant =
            a * c -
            b * b;

        if (!IsFinite(determinant))
        {
            return false;
        }

        double matrixScale =
            Math.Max(
                1.0,
                Math.Max(
                    Math.Abs(a),
                    Math.Abs(c)));

        double determinantScale =
            matrixScale * matrixScale;

        return
            determinant >
            RelativeConditioningTolerance * determinantScale;
    }

    private static bool TrySolveDominantDirection(
        double a,
        double b,
        double c,
        double d,
        double e,
        out double gradientX,
        out double gradientZ)
    {
        gradientX = 0.0;
        gradientZ = 0.0;

        double trace =
            a + c;

        if (!IsFinite(trace) || trace <= RelativeConditioningTolerance)
        {
            return true;
        }

        /*
         * For a symmetric 2x2 matrix, half the atan2 of the doubled
         * off-diagonal and diagonal difference is the dominant principal-axis
         * angle. Its sign is arbitrary, but the final vector u*slope is sign
         * invariant because the fitted scalar slope changes with u.
         */
        double angle =
            0.5 * Math.Atan2(
                2.0 * b,
                a - c);

        double ux =
            Math.Cos(angle);

        double uz =
            Math.Sin(angle);

        double denominator =
            a * ux * ux +
            2.0 * b * ux * uz +
            c * uz * uz;

        double denominatorScale =
            Math.Max(
                1.0,
                Math.Max(
                    Math.Abs(a),
                    Math.Abs(c)));

        if (
            !IsFinite(denominator) ||
            denominator <=
                RelativeConditioningTolerance * denominatorScale)
        {
            return true;
        }

        double numerator =
            d * ux +
            e * uz;

        double slope =
            numerator /
            denominator;

        if (!IsFinite(slope))
        {
            return false;
        }

        gradientX =
            ux * slope;

        gradientZ =
            uz * slope;

        return
            IsFinite(gradientX) &&
            IsFinite(gradientZ);
    }

    private static bool IsFinite(
        double value)
    {
        return
            !double.IsNaN(value) &&
            !double.IsInfinity(value);
    }
}
