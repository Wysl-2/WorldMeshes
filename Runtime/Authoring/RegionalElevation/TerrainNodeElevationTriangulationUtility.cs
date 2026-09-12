using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Builds deterministic, derived Delaunay topology from regional elevation
 * node XZ positions. No persistent authoring state is mutated.
 */
public static class TerrainNodeElevationTriangulationUtility
{
    private struct DoublePoint
    {
        public double X;
        public double Y;
        public Vector2 PositionXZ;
        public int SourceNodeIndex;

        public DoublePoint(
            double x,
            double y,
            Vector2 positionXZ,
            int sourceNodeIndex)
        {
            X = x;
            Y = y;
            PositionXZ = positionXZ;
            SourceNodeIndex = sourceNodeIndex;
        }
    }

    private struct TempTriangle
    {
        public int A;
        public int B;
        public int C;

        public TempTriangle(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private struct CanonicalTriangle : IEquatable<CanonicalTriangle>, IComparable<CanonicalTriangle>
    {
        public int A;
        public int B;
        public int C;

        public CanonicalTriangle(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        public bool Equals(CanonicalTriangle other)
        {
            return A == other.A && B == other.B && C == other.C;
        }

        public override bool Equals(object obj)
        {
            return obj is CanonicalTriangle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = A;
                hash = hash * 397 ^ B;
                hash = hash * 397 ^ C;
                return hash;
            }
        }

        public int CompareTo(CanonicalTriangle other)
        {
            int compare = A.CompareTo(other.A);
            if (compare != 0)
            {
                return compare;
            }

            compare = B.CompareTo(other.B);
            if (compare != 0)
            {
                return compare;
            }

            return C.CompareTo(other.C);
        }
    }

    private struct EdgeKey : IEquatable<EdgeKey>, IComparable<EdgeKey>
    {
        public int A;
        public int B;

        public EdgeKey(int a, int b)
        {
            if (a <= b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(EdgeKey other)
        {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (A * 397) ^ B;
            }
        }

        public int CompareTo(EdgeKey other)
        {
            int compare = A.CompareTo(other.A);
            return compare != 0 ? compare : B.CompareTo(other.B);
        }
    }

    private sealed class EdgeUse
    {
        public int TriangleIndex;
        public int SideIndex;

        public EdgeUse(int triangleIndex, int sideIndex)
        {
            TriangleIndex = triangleIndex;
            SideIndex = sideIndex;
        }
    }

    public static bool TryBuild(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        topology = null;
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        if (!TryCaptureCanonicalVertices(
            source,
            out TerrainNodeElevationTopologyVertex[] vertices,
            out errorMessage))
        {
            return false;
        }

        if (vertices.Length == 0)
        {
            topology = CreateDegenerateTopology(
                TerrainNodeElevationTopologyKind.Empty,
                vertices,
                new int[0],
                new TerrainNodeElevationTopologyEdge[0]);
            return true;
        }

        if (vertices.Length == 1)
        {
            topology = CreateDegenerateTopology(
                TerrainNodeElevationTopologyKind.SinglePoint,
                vertices,
                new[] { 0 },
                new TerrainNodeElevationTopologyEdge[0]);
            return true;
        }

        if (vertices.Length == 2)
        {
            TerrainNodeElevationTopologyEdge[] lineEdges =
            {
                new TerrainNodeElevationTopologyEdge(0, 1, 0)
            };

            topology = CreateDegenerateTopology(
                TerrainNodeElevationTopologyKind.LineSegment,
                vertices,
                new[] { 0, 1 },
                lineEdges);
            return true;
        }

        if (AreAllVerticesCollinear(vertices))
        {
            TerrainNodeElevationTopologyEdge[] chainEdges =
                new TerrainNodeElevationTopologyEdge[vertices.Length - 1];

            int[] hull = new int[vertices.Length];

            for (int index = 0; index < vertices.Length; index++)
            {
                hull[index] = index;

                if (index + 1 < vertices.Length)
                {
                    chainEdges[index] =
                        new TerrainNodeElevationTopologyEdge(
                            index,
                            index + 1,
                            0);
                }
            }

            topology = CreateDegenerateTopology(
                TerrainNodeElevationTopologyKind.Collinear,
                vertices,
                hull,
                chainEdges);
            return true;
        }

        return TryBuildTriangulatedTopology(
            vertices,
            out topology,
            out errorMessage);
    }

    internal static bool TryCaptureSourceGeometry(
        TerrainNodeElevationSource source,
        out Vector2[] positions,
        out string errorMessage)
    {
        positions = null;
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        Vector2[] captured = new Vector2[nodes.Count];

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];

            if (node == null)
            {
                errorMessage =
                    $"Regional elevation node index {index} is null.";
                return false;
            }

            if (!node.TryGetStoredPositionXZInternal(
                out Vector2 positionXZ,
                out string positionError))
            {
                errorMessage =
                    $"Regional elevation node index {index} has invalid geometry. " +
                    positionError;
                return false;
            }

            captured[index] = positionXZ;
        }

        positions = captured;
        return true;
    }

    private static bool TryCaptureCanonicalVertices(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopologyVertex[] vertices,
        out string errorMessage)
    {
        vertices = null;
        errorMessage = "";

        if (!TryCaptureSourceGeometry(
            source,
            out Vector2[] sourcePositions,
            out errorMessage))
        {
            return false;
        }

        TerrainNodeElevationTopologyVertex[] canonical =
            new TerrainNodeElevationTopologyVertex[sourcePositions.Length];

        for (int index = 0; index < sourcePositions.Length; index++)
        {
            canonical[index] =
                new TerrainNodeElevationTopologyVertex(
                    sourcePositions[index],
                    index);
        }

        Array.Sort(
            canonical,
            CompareVertices);

        double minimumSeparation =
            TerrainNodeElevationGeometryUtility.MinimumTriangulationVertexSeparation;

        double minimumSeparationSquared =
            minimumSeparation * minimumSeparation;

        for (int first = 0; first < canonical.Length; first++)
        {
            Vector2 a = canonical[first].PositionXZ;

            for (int second = first + 1; second < canonical.Length; second++)
            {
                Vector2 b = canonical[second].PositionXZ;

                double dx =
                    (double)b.x - a.x;

                if (dx >= minimumSeparation)
                {
                    break;
                }

                double dy =
                    (double)b.y - a.y;

                if (Math.Abs(dy) >= minimumSeparation)
                {
                    continue;
                }

                double distanceSquared =
                    dx * dx +
                    dy * dy;

                if (distanceSquared < minimumSeparationSquared)
                {
                    errorMessage =
                        "Regional elevation triangulation requires unique XZ " +
                        "vertices. Nodes at source indices " +
                        canonical[first].SourceNodeIndex +
                        " and " +
                        canonical[second].SourceNodeIndex +
                        " are coincident or closer than the minimum " +
                        "triangulation separation of " +
                        TerrainNodeElevationGeometryUtility
                            .MinimumTriangulationVertexSeparation
                            .ToString("R") +
                        " world units.";

                    return false;
                }
            }
        }

        vertices = canonical;
        return true;
    }

    private static int CompareVertices(
        TerrainNodeElevationTopologyVertex a,
        TerrainNodeElevationTopologyVertex b)
    {
        int xCompare =
            a.PositionXZ.x.CompareTo(b.PositionXZ.x);

        if (xCompare != 0)
        {
            return xCompare;
        }

        int zCompare =
            a.PositionXZ.y.CompareTo(b.PositionXZ.y);

        if (zCompare != 0)
        {
            return zCompare;
        }

        return a.SourceNodeIndex.CompareTo(b.SourceNodeIndex);
    }

    private static bool AreAllVerticesCollinear(
        TerrainNodeElevationTopologyVertex[] vertices)
    {
        Vector2 a = vertices[0].PositionXZ;
        Vector2 b = vertices[vertices.Length - 1].PositionXZ;

        for (int index = 1; index < vertices.Length - 1; index++)
        {
            if (!TerrainNodeElevationGeometryUtility.IsEffectivelyCollinear(
                a,
                b,
                vertices[index].PositionXZ))
            {
                return false;
            }
        }

        return true;
    }

    private static TerrainNodeElevationTopology CreateDegenerateTopology(
        TerrainNodeElevationTopologyKind kind,
        TerrainNodeElevationTopologyVertex[] vertices,
        int[] hullVertices,
        TerrainNodeElevationTopologyEdge[] edges)
    {
        int[][] neighbors =
            BuildVertexNeighbors(
                vertices.Length,
                edges);

        return new TerrainNodeElevationTopology(
            kind,
            vertices,
            new TerrainNodeElevationTopologyTriangle[0],
            edges,
            hullVertices,
            edges,
            neighbors);
    }

    private static bool TryBuildTriangulatedTopology(
        TerrainNodeElevationTopologyVertex[] vertices,
        out TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        topology = null;
        errorMessage = "";

        List<DoublePoint> points =
            new List<DoublePoint>(vertices.Length + 3);

        for (int index = 0; index < vertices.Length; index++)
        {
            Vector2 position = vertices[index].PositionXZ;
            points.Add(
                new DoublePoint(
                    position.x,
                    position.y,
                    position,
                    vertices[index].SourceNodeIndex));
        }

        if (!AppendSuperTriangle(
            points,
            vertices.Length,
            out int superA,
            out int superB,
            out int superC,
            out errorMessage))
        {
            return false;
        }

        List<TempTriangle> workingTriangles =
            new List<TempTriangle>();

        if (!TryCreateCcwTriangle(
            points,
            superA,
            superB,
            superC,
            out TempTriangle superTriangle))
        {
            errorMessage =
                "Could not construct the deterministic triangulation super triangle.";
            return false;
        }

        workingTriangles.Add(superTriangle);

        for (int pointIndex = 0; pointIndex < vertices.Length; pointIndex++)
        {
            List<int> badTriangleIndices =
                new List<int>();

            Dictionary<EdgeKey, int> boundaryCounts =
                new Dictionary<EdgeKey, int>();

            DoublePoint point = points[pointIndex];

            for (int triangleIndex = 0;
                triangleIndex < workingTriangles.Count;
                triangleIndex++)
            {
                TempTriangle triangle = workingTriangles[triangleIndex];
                DoublePoint a = points[triangle.A];
                DoublePoint b = points[triangle.B];
                DoublePoint c = points[triangle.C];

                if (!TerrainNodeElevationGeometryUtility.IsInsideCircumcircle(
                    a.X,
                    a.Y,
                    b.X,
                    b.Y,
                    c.X,
                    c.Y,
                    point.X,
                    point.Y))
                {
                    continue;
                }

                badTriangleIndices.Add(triangleIndex);
                IncrementEdgeCount(boundaryCounts, triangle.A, triangle.B);
                IncrementEdgeCount(boundaryCounts, triangle.B, triangle.C);
                IncrementEdgeCount(boundaryCounts, triangle.C, triangle.A);
            }

            if (badTriangleIndices.Count == 0)
            {
                errorMessage =
                    "Delaunay triangulation could not locate a circumcircle " +
                    $"cavity for canonical vertex {pointIndex}.";
                return false;
            }

            for (int index = badTriangleIndices.Count - 1; index >= 0; index--)
            {
                workingTriangles.RemoveAt(badTriangleIndices[index]);
            }

            List<EdgeKey> boundaryEdges =
                new List<EdgeKey>();

            foreach (KeyValuePair<EdgeKey, int> pair in boundaryCounts)
            {
                if (pair.Value == 1)
                {
                    boundaryEdges.Add(pair.Key);
                }
            }

            boundaryEdges.Sort();

            for (int edgeIndex = 0; edgeIndex < boundaryEdges.Count; edgeIndex++)
            {
                EdgeKey edge = boundaryEdges[edgeIndex];

                if (TryCreateCcwTriangle(
                    points,
                    edge.A,
                    edge.B,
                    pointIndex,
                    out TempTriangle triangle))
                {
                    workingTriangles.Add(triangle);
                }
            }
        }

        List<CanonicalTriangle> finalTriangles =
            new List<CanonicalTriangle>();

        HashSet<CanonicalTriangle> uniqueTriangles =
            new HashSet<CanonicalTriangle>();

        for (int index = 0; index < workingTriangles.Count; index++)
        {
            TempTriangle triangle = workingTriangles[index];

            if (
                triangle.A >= vertices.Length ||
                triangle.B >= vertices.Length ||
                triangle.C >= vertices.Length)
            {
                continue;
            }

            if (!TryNormalizeTriangle(
                vertices,
                triangle.A,
                triangle.B,
                triangle.C,
                out CanonicalTriangle normalized))
            {
                continue;
            }

            if (uniqueTriangles.Add(normalized))
            {
                finalTriangles.Add(normalized);
            }
        }

        if (finalTriangles.Count == 0)
        {
            errorMessage =
                "Delaunay triangulation produced no valid triangles for a " +
                "non-collinear node layout.";
            return false;
        }

        finalTriangles.Sort();

        if (!CanonicalizeCocircularDiagonals(
            vertices,
            finalTriangles,
            out errorMessage))
        {
            return false;
        }

        if (!TryBuildFinalTopologyData(
            vertices,
            finalTriangles,
            out TerrainNodeElevationTopologyTriangle[] topologyTriangles,
            out TerrainNodeElevationTopologyEdge[] edges,
            out int[] hullVertices,
            out TerrainNodeElevationTopologyEdge[] hullEdges,
            out int[][] vertexNeighbors,
            out errorMessage))
        {
            return false;
        }

        topology =
            new TerrainNodeElevationTopology(
                TerrainNodeElevationTopologyKind.Triangulated,
                vertices,
                topologyTriangles,
                edges,
                hullVertices,
                hullEdges,
                vertexNeighbors);

        return true;
    }

    private static bool AppendSuperTriangle(
        List<DoublePoint> points,
        int realPointCount,
        out int superA,
        out int superB,
        out int superC,
        out string errorMessage)
    {
        superA = -1;
        superB = -1;
        superC = -1;
        errorMessage = "";

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        for (int index = 0; index < realPointCount; index++)
        {
            DoublePoint point = points[index];
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        double span = Math.Max(maxX - minX, maxY - minY);
        if (!TerrainNodeElevationGeometryUtility.IsFinite(span) || span <= 0.0)
        {
            errorMessage = "Triangulation bounds are invalid.";
            return false;
        }

        double centerX = (minX + maxX) * 0.5;
        double centerY = (minY + maxY) * 0.5;
        double extent = Math.Max(1.0, span) * 32.0;

        double ax = centerX - extent;
        double ay = centerY - extent;
        double bx = centerX + extent;
        double by = centerY - extent;
        double cx = centerX;
        double cy = centerY + extent;

        if (
            !TerrainNodeElevationGeometryUtility.IsFinite(ax) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(ay) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(bx) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(by) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(cx) ||
            !TerrainNodeElevationGeometryUtility.IsFinite(cy))
        {
            errorMessage = "Triangulation super-triangle geometry is non-finite.";
            return false;
        }

        superA = points.Count;
        points.Add(new DoublePoint(ax, ay, Vector2.zero, -1));

        superB = points.Count;
        points.Add(new DoublePoint(bx, by, Vector2.zero, -1));

        superC = points.Count;
        points.Add(new DoublePoint(cx, cy, Vector2.zero, -1));

        return true;
    }

    private static bool TryCreateCcwTriangle(
        List<DoublePoint> points,
        int a,
        int b,
        int c,
        out TempTriangle triangle)
    {
        triangle = default(TempTriangle);

        DoublePoint pa = points[a];
        DoublePoint pb = points[b];
        DoublePoint pc = points[c];

        double orientation =
            TerrainNodeElevationGeometryUtility.Orientation(
                pa.X,
                pa.Y,
                pb.X,
                pb.Y,
                pc.X,
                pc.Y);

        double tolerance =
            TerrainNodeElevationGeometryUtility.OrientationTolerance(
                pa.X,
                pa.Y,
                pb.X,
                pb.Y,
                pc.X,
                pc.Y);

        if (Math.Abs(orientation) <= tolerance)
        {
            return false;
        }

        triangle =
            orientation > 0.0
                ? new TempTriangle(a, b, c)
                : new TempTriangle(a, c, b);

        return true;
    }

    private static void IncrementEdgeCount(
        Dictionary<EdgeKey, int> counts,
        int a,
        int b)
    {
        EdgeKey edge = new EdgeKey(a, b);

        counts.TryGetValue(edge, out int count);
        counts[edge] = count + 1;
    }

    private static bool TryNormalizeTriangle(
        TerrainNodeElevationTopologyVertex[] vertices,
        int a,
        int b,
        int c,
        out CanonicalTriangle normalized)
    {
        normalized = default(CanonicalTriangle);

        if (a == b || b == c || c == a)
        {
            return false;
        }

        Vector2 pa = vertices[a].PositionXZ;
        Vector2 pb = vertices[b].PositionXZ;
        Vector2 pc = vertices[c].PositionXZ;

        double orientation =
            TerrainNodeElevationGeometryUtility.Orientation(pa, pb, pc);

        double tolerance =
            TerrainNodeElevationGeometryUtility.OrientationTolerance(
                pa.x,
                pa.y,
                pb.x,
                pb.y,
                pc.x,
                pc.y);

        if (Math.Abs(orientation) <= tolerance)
        {
            return false;
        }

        if (orientation < 0.0)
        {
            int swap = b;
            b = c;
            c = swap;
        }

        if (b < a && b < c)
        {
            int oldA = a;
            a = b;
            b = c;
            c = oldA;
        }
        else if (c < a && c < b)
        {
            int oldA = a;
            a = c;
            c = b;
            b = oldA;
        }

        normalized =
            new CanonicalTriangle(a, b, c);

        return true;
    }

    private static bool CanonicalizeCocircularDiagonals(
        TerrainNodeElevationTopologyVertex[] vertices,
        List<CanonicalTriangle> triangles,
        out string errorMessage)
    {
        errorMessage = "";

        int safetyLimit = Math.Max(
            32,
            triangles.Count * triangles.Count * 4 + 32);

        for (int iteration = 0; iteration < safetyLimit; iteration++)
        {
            Dictionary<EdgeKey, List<int>> edgeUses =
                BuildTriangleEdgeUses(triangles);

            List<EdgeKey> edges =
                new List<EdgeKey>(edgeUses.Keys);

            edges.Sort();

            bool flipped = false;

            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                EdgeKey currentEdge = edges[edgeIndex];
                List<int> uses = edgeUses[currentEdge];

                if (uses.Count != 2)
                {
                    continue;
                }

                CanonicalTriangle first = triangles[uses[0]];
                CanonicalTriangle second = triangles[uses[1]];

                int oppositeFirst = GetOppositeVertex(first, currentEdge);
                int oppositeSecond = GetOppositeVertex(second, currentEdge);

                if (
                    oppositeFirst < 0 ||
                    oppositeSecond < 0 ||
                    oppositeFirst == oppositeSecond)
                {
                    continue;
                }

                EdgeKey alternateEdge =
                    new EdgeKey(oppositeFirst, oppositeSecond);

                if (alternateEdge.CompareTo(currentEdge) >= 0)
                {
                    continue;
                }

                if (edgeUses.ContainsKey(alternateEdge))
                {
                    continue;
                }

                Vector2 a = vertices[currentEdge.A].PositionXZ;
                Vector2 b = vertices[currentEdge.B].PositionXZ;
                Vector2 c = vertices[oppositeFirst].PositionXZ;
                Vector2 d = vertices[oppositeSecond].PositionXZ;

                if (!AreOppositeSides(a, b, c, d) ||
                    !AreOppositeSides(c, d, a, b))
                {
                    continue;
                }

                if (!TerrainNodeElevationGeometryUtility.AreCocircular(
                    a.x,
                    a.y,
                    b.x,
                    b.y,
                    c.x,
                    c.y,
                    d.x,
                    d.y))
                {
                    continue;
                }

                if (!TryNormalizeTriangle(
                    vertices,
                    oppositeFirst,
                    oppositeSecond,
                    currentEdge.A,
                    out CanonicalTriangle replacementFirst) ||
                    !TryNormalizeTriangle(
                        vertices,
                        oppositeSecond,
                        oppositeFirst,
                        currentEdge.B,
                        out CanonicalTriangle replacementSecond))
                {
                    continue;
                }

                int highIndex = Math.Max(uses[0], uses[1]);
                int lowIndex = Math.Min(uses[0], uses[1]);

                triangles.RemoveAt(highIndex);
                triangles.RemoveAt(lowIndex);
                triangles.Add(replacementFirst);
                triangles.Add(replacementSecond);

                RemoveDuplicateTriangles(triangles);
                triangles.Sort();

                flipped = true;
                break;
            }

            if (!flipped)
            {
                return true;
            }
        }

        errorMessage =
            "Deterministic cocircular Delaunay tie resolution did not converge.";
        return false;
    }

    private static bool AreOppositeSides(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d)
    {
        double cSide =
            TerrainNodeElevationGeometryUtility.Orientation(a, b, c);

        double dSide =
            TerrainNodeElevationGeometryUtility.Orientation(a, b, d);

        double cTolerance =
            TerrainNodeElevationGeometryUtility.OrientationTolerance(
                a.x,
                a.y,
                b.x,
                b.y,
                c.x,
                c.y);

        double dTolerance =
            TerrainNodeElevationGeometryUtility.OrientationTolerance(
                a.x,
                a.y,
                b.x,
                b.y,
                d.x,
                d.y);

        return
            (cSide > cTolerance && dSide < -dTolerance) ||
            (cSide < -cTolerance && dSide > dTolerance);
    }

    private static Dictionary<EdgeKey, List<int>> BuildTriangleEdgeUses(
        List<CanonicalTriangle> triangles)
    {
        Dictionary<EdgeKey, List<int>> result =
            new Dictionary<EdgeKey, List<int>>();

        for (int index = 0; index < triangles.Count; index++)
        {
            CanonicalTriangle triangle = triangles[index];
            AddTriangleEdgeUse(result, new EdgeKey(triangle.A, triangle.B), index);
            AddTriangleEdgeUse(result, new EdgeKey(triangle.B, triangle.C), index);
            AddTriangleEdgeUse(result, new EdgeKey(triangle.C, triangle.A), index);
        }

        return result;
    }

    private static void AddTriangleEdgeUse(
        Dictionary<EdgeKey, List<int>> uses,
        EdgeKey edge,
        int triangleIndex)
    {
        if (!uses.TryGetValue(edge, out List<int> list))
        {
            list = new List<int>();
            uses.Add(edge, list);
        }

        list.Add(triangleIndex);
    }

    private static int GetOppositeVertex(
        CanonicalTriangle triangle,
        EdgeKey edge)
    {
        if (triangle.A != edge.A && triangle.A != edge.B)
        {
            return triangle.A;
        }

        if (triangle.B != edge.A && triangle.B != edge.B)
        {
            return triangle.B;
        }

        if (triangle.C != edge.A && triangle.C != edge.B)
        {
            return triangle.C;
        }

        return -1;
    }

    private static void RemoveDuplicateTriangles(
        List<CanonicalTriangle> triangles)
    {
        HashSet<CanonicalTriangle> unique =
            new HashSet<CanonicalTriangle>();

        for (int index = triangles.Count - 1; index >= 0; index--)
        {
            if (!unique.Add(triangles[index]))
            {
                triangles.RemoveAt(index);
            }
        }
    }

    private static bool TryBuildFinalTopologyData(
        TerrainNodeElevationTopologyVertex[] vertices,
        List<CanonicalTriangle> triangles,
        out TerrainNodeElevationTopologyTriangle[] finalTriangles,
        out TerrainNodeElevationTopologyEdge[] finalEdges,
        out int[] hullVertices,
        out TerrainNodeElevationTopologyEdge[] hullEdges,
        out int[][] vertexNeighbors,
        out string errorMessage)
    {
        finalTriangles = null;
        finalEdges = null;
        hullVertices = null;
        hullEdges = null;
        vertexNeighbors = null;
        errorMessage = "";

        int triangleCount = triangles.Count;
        int[,] neighbors = new int[triangleCount, 3];

        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            neighbors[triangleIndex, 0] = -1;
            neighbors[triangleIndex, 1] = -1;
            neighbors[triangleIndex, 2] = -1;
        }

        Dictionary<EdgeKey, EdgeUse> firstEdgeUse =
            new Dictionary<EdgeKey, EdgeUse>();

        Dictionary<EdgeKey, int> edgeReferenceCounts =
            new Dictionary<EdgeKey, int>();

        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            CanonicalTriangle triangle = triangles[triangleIndex];

            if (!RegisterFinalEdge(
                new EdgeKey(triangle.A, triangle.B),
                triangleIndex,
                0,
                firstEdgeUse,
                edgeReferenceCounts,
                neighbors,
                out errorMessage) ||
                !RegisterFinalEdge(
                    new EdgeKey(triangle.B, triangle.C),
                    triangleIndex,
                    1,
                    firstEdgeUse,
                    edgeReferenceCounts,
                    neighbors,
                    out errorMessage) ||
                !RegisterFinalEdge(
                    new EdgeKey(triangle.C, triangle.A),
                    triangleIndex,
                    2,
                    firstEdgeUse,
                    edgeReferenceCounts,
                    neighbors,
                    out errorMessage))
            {
                return false;
            }
        }

        List<EdgeKey> sortedEdges =
            new List<EdgeKey>(edgeReferenceCounts.Keys);

        sortedEdges.Sort();

        finalEdges =
            new TerrainNodeElevationTopologyEdge[sortedEdges.Count];

        HashSet<EdgeKey> boundaryEdgeSet =
            new HashSet<EdgeKey>();

        for (int edgeIndex = 0; edgeIndex < sortedEdges.Count; edgeIndex++)
        {
            EdgeKey edge = sortedEdges[edgeIndex];
            int referenceCount = edgeReferenceCounts[edge];

            if (referenceCount <= 0 || referenceCount > 2)
            {
                errorMessage =
                    "Triangulation produced an invalid triangle reference count " +
                    $"for edge ({edge.A}, {edge.B}).";
                return false;
            }

            finalEdges[edgeIndex] =
                new TerrainNodeElevationTopologyEdge(
                    edge.A,
                    edge.B,
                    referenceCount);

            if (referenceCount == 1)
            {
                boundaryEdgeSet.Add(edge);
            }
        }

        finalTriangles =
            new TerrainNodeElevationTopologyTriangle[triangleCount];

        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            CanonicalTriangle triangle = triangles[triangleIndex];

            finalTriangles[triangleIndex] =
                new TerrainNodeElevationTopologyTriangle(
                    triangle.A,
                    triangle.B,
                    triangle.C,
                    neighbors[triangleIndex, 0],
                    neighbors[triangleIndex, 1],
                    neighbors[triangleIndex, 2]);
        }

        if (!TryOrderBoundaryHull(
            vertices,
            boundaryEdgeSet,
            out hullVertices,
            out errorMessage))
        {
            return false;
        }

        hullEdges =
            new TerrainNodeElevationTopologyEdge[hullVertices.Length];

        for (int index = 0; index < hullVertices.Length; index++)
        {
            int next = (index + 1) % hullVertices.Length;
            EdgeKey edge =
                new EdgeKey(
                    hullVertices[index],
                    hullVertices[next]);

            if (!boundaryEdgeSet.Contains(edge))
            {
                errorMessage =
                    "Derived convex hull contains an edge that is not a " +
                    "triangulation boundary edge.";
                return false;
            }

            hullEdges[index] =
                new TerrainNodeElevationTopologyEdge(
                    edge.A,
                    edge.B,
                    1);
        }

        if (hullEdges.Length != boundaryEdgeSet.Count)
        {
            errorMessage =
                "Triangulation boundary edges do not form one deterministic convex hull.";
            return false;
        }

        vertexNeighbors =
            BuildVertexNeighbors(
                vertices.Length,
                finalEdges);

        return true;
    }

    private static bool RegisterFinalEdge(
        EdgeKey edge,
        int triangleIndex,
        int sideIndex,
        Dictionary<EdgeKey, EdgeUse> firstEdgeUse,
        Dictionary<EdgeKey, int> edgeReferenceCounts,
        int[,] neighbors,
        out string errorMessage)
    {
        errorMessage = "";

        edgeReferenceCounts.TryGetValue(edge, out int referenceCount);
        referenceCount++;
        edgeReferenceCounts[edge] = referenceCount;

        if (referenceCount > 2)
        {
            errorMessage =
                "Triangulation produced a non-manifold edge at vertices " +
                $"({edge.A}, {edge.B}).";
            return false;
        }

        if (!firstEdgeUse.TryGetValue(edge, out EdgeUse firstUse))
        {
            firstEdgeUse.Add(
                edge,
                new EdgeUse(triangleIndex, sideIndex));
            return true;
        }

        neighbors[triangleIndex, sideIndex] = firstUse.TriangleIndex;
        neighbors[firstUse.TriangleIndex, firstUse.SideIndex] = triangleIndex;
        return true;
    }

    private static bool TryOrderBoundaryHull(
        TerrainNodeElevationTopologyVertex[] vertices,
        HashSet<EdgeKey> boundaryEdges,
        out int[] hullVertices,
        out string errorMessage)
    {
        hullVertices = null;
        errorMessage = "";

        if (boundaryEdges == null || boundaryEdges.Count < 3)
        {
            errorMessage =
                "A non-collinear triangulation produced too few boundary edges.";
            return false;
        }

        Dictionary<int, List<int>> adjacency =
            new Dictionary<int, List<int>>();

        foreach (EdgeKey edge in boundaryEdges)
        {
            AddBoundaryNeighbor(adjacency, edge.A, edge.B);
            AddBoundaryNeighbor(adjacency, edge.B, edge.A);
        }

        int startVertex = int.MaxValue;

        foreach (KeyValuePair<int, List<int>> pair in adjacency)
        {
            if (pair.Value.Count != 2)
            {
                errorMessage =
                    "Triangulation boundary does not form one closed manifold hull.";
                return false;
            }

            pair.Value.Sort();
            startVertex = Math.Min(startVertex, pair.Key);
        }

        if (startVertex == int.MaxValue)
        {
            errorMessage = "Triangulation boundary contains no vertices.";
            return false;
        }

        List<int> startNeighbors = adjacency[startVertex];

        if (!TryWalkBoundaryCycle(
            adjacency,
            startVertex,
            startNeighbors[0],
            boundaryEdges.Count,
            out List<int> firstCycle) ||
            !TryWalkBoundaryCycle(
                adjacency,
                startVertex,
                startNeighbors[1],
                boundaryEdges.Count,
                out List<int> secondCycle))
        {
            errorMessage =
                "Triangulation boundary edges could not be ordered into a closed hull.";
            return false;
        }

        double firstArea = SignedPolygonArea(vertices, firstCycle);
        double secondArea = SignedPolygonArea(vertices, secondCycle);

        List<int> selected;

        if (firstArea > 0.0 && secondArea < 0.0)
        {
            selected = firstCycle;
        }
        else if (secondArea > 0.0 && firstArea < 0.0)
        {
            selected = secondCycle;
        }
        else
        {
            errorMessage =
                "Triangulation boundary hull orientation is degenerate.";
            return false;
        }

        hullVertices = selected.ToArray();
        return true;
    }

    private static void AddBoundaryNeighbor(
        Dictionary<int, List<int>> adjacency,
        int vertex,
        int neighbor)
    {
        if (!adjacency.TryGetValue(vertex, out List<int> neighbors))
        {
            neighbors = new List<int>();
            adjacency.Add(vertex, neighbors);
        }

        if (!neighbors.Contains(neighbor))
        {
            neighbors.Add(neighbor);
        }
    }

    private static bool TryWalkBoundaryCycle(
        Dictionary<int, List<int>> adjacency,
        int startVertex,
        int firstNeighbor,
        int expectedEdgeCount,
        out List<int> cycle)
    {
        cycle = new List<int>();
        cycle.Add(startVertex);

        int previous = startVertex;
        int current = firstNeighbor;

        for (int step = 0; step < expectedEdgeCount; step++)
        {
            if (current == startVertex)
            {
                return cycle.Count == expectedEdgeCount;
            }

            if (cycle.Contains(current))
            {
                return false;
            }

            cycle.Add(current);

            if (!adjacency.TryGetValue(current, out List<int> neighbors) ||
                neighbors.Count != 2)
            {
                return false;
            }

            int next =
                neighbors[0] == previous
                    ? neighbors[1]
                    : neighbors[0];

            previous = current;
            current = next;
        }

        return current == startVertex && cycle.Count == expectedEdgeCount;
    }

    private static double SignedPolygonArea(
        TerrainNodeElevationTopologyVertex[] vertices,
        List<int> cycle)
    {
        double twiceArea = 0.0;

        for (int index = 0; index < cycle.Count; index++)
        {
            Vector2 current = vertices[cycle[index]].PositionXZ;
            Vector2 next = vertices[cycle[(index + 1) % cycle.Count]].PositionXZ;

            twiceArea +=
                (double)current.x * next.y -
                (double)current.y * next.x;
        }

        return twiceArea * 0.5;
    }

    private static int[][] BuildVertexNeighbors(
        int vertexCount,
        TerrainNodeElevationTopologyEdge[] edges)
    {
        SortedSet<int>[] neighborSets =
            new SortedSet<int>[vertexCount];

        for (int index = 0; index < vertexCount; index++)
        {
            neighborSets[index] = new SortedSet<int>();
        }

        for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            TerrainNodeElevationTopologyEdge edge = edges[edgeIndex];

            if (
                edge.VertexA < 0 ||
                edge.VertexA >= vertexCount ||
                edge.VertexB < 0 ||
                edge.VertexB >= vertexCount ||
                edge.VertexA == edge.VertexB)
            {
                continue;
            }

            neighborSets[edge.VertexA].Add(edge.VertexB);
            neighborSets[edge.VertexB].Add(edge.VertexA);
        }

        int[][] result = new int[vertexCount][];

        for (int index = 0; index < vertexCount; index++)
        {
            result[index] = new int[neighborSets[index].Count];
            neighborSets[index].CopyTo(result[index]);
        }

        return result;
    }
}
