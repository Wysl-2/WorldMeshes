using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum TerrainNodeElevationTopologyKind
{
    Empty = 0,
    SinglePoint = 1,
    LineSegment = 2,
    Collinear = 3,
    Triangulated = 4
}

public struct TerrainNodeElevationTopologyVertex
{
    public Vector2 PositionXZ { get; private set; }
    public int SourceNodeIndex { get; private set; }

    internal TerrainNodeElevationTopologyVertex(
        Vector2 positionXZ,
        int sourceNodeIndex)
    {
        PositionXZ = positionXZ;
        SourceNodeIndex = sourceNodeIndex;
    }
}

public struct TerrainNodeElevationTopologyEdge :
    IEquatable<TerrainNodeElevationTopologyEdge>,
    IComparable<TerrainNodeElevationTopologyEdge>
{
    public int VertexA { get; private set; }
    public int VertexB { get; private set; }
    public int TriangleReferenceCount { get; private set; }

    internal TerrainNodeElevationTopologyEdge(
        int vertexA,
        int vertexB,
        int triangleReferenceCount)
    {
        if (vertexA <= vertexB)
        {
            VertexA = vertexA;
            VertexB = vertexB;
        }
        else
        {
            VertexA = vertexB;
            VertexB = vertexA;
        }

        TriangleReferenceCount = triangleReferenceCount;
    }

    public bool Equals(TerrainNodeElevationTopologyEdge other)
    {
        return
            VertexA == other.VertexA &&
            VertexB == other.VertexB;
    }

    public override bool Equals(object obj)
    {
        return
            obj is TerrainNodeElevationTopologyEdge other &&
            Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return
                (VertexA * 397) ^
                VertexB;
        }
    }

    public int CompareTo(TerrainNodeElevationTopologyEdge other)
    {
        int aCompare = VertexA.CompareTo(other.VertexA);
        if (aCompare != 0)
        {
            return aCompare;
        }

        return VertexB.CompareTo(other.VertexB);
    }

    public static bool operator ==(
        TerrainNodeElevationTopologyEdge left,
        TerrainNodeElevationTopologyEdge right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        TerrainNodeElevationTopologyEdge left,
        TerrainNodeElevationTopologyEdge right)
    {
        return !left.Equals(right);
    }
}

public struct TerrainNodeElevationTopologyTriangle
{
    public int VertexA { get; private set; }
    public int VertexB { get; private set; }
    public int VertexC { get; private set; }

    public int NeighborAcrossAB { get; private set; }
    public int NeighborAcrossBC { get; private set; }
    public int NeighborAcrossCA { get; private set; }

    internal TerrainNodeElevationTopologyTriangle(
        int vertexA,
        int vertexB,
        int vertexC,
        int neighborAcrossAB,
        int neighborAcrossBC,
        int neighborAcrossCA)
    {
        VertexA = vertexA;
        VertexB = vertexB;
        VertexC = vertexC;
        NeighborAcrossAB = neighborAcrossAB;
        NeighborAcrossBC = neighborAcrossBC;
        NeighborAcrossCA = neighborAcrossCA;
    }
}

/*
 * Immutable derived geometry for one TerrainNodeElevationSource layout.
 *
 * This data is rebuilt from node XZ positions. It is intentionally not
 * serialized and carries no node elevation, StableId, selection, or authoring
 * revision state.
 */
public sealed class TerrainNodeElevationTopology
{
    private static readonly IReadOnlyList<int> EmptyNeighborList =
        Array.AsReadOnly(new int[0]);

    private readonly ReadOnlyCollection<TerrainNodeElevationTopologyVertex>
        vertices;

    private readonly ReadOnlyCollection<TerrainNodeElevationTopologyTriangle>
        triangles;

    private readonly ReadOnlyCollection<TerrainNodeElevationTopologyEdge>
        edges;

    private readonly ReadOnlyCollection<int>
        hullVertexIndices;

    private readonly ReadOnlyCollection<TerrainNodeElevationTopologyEdge>
        hullEdges;

    private readonly ReadOnlyCollection<int>[]
        vertexNeighbors;

    public TerrainNodeElevationTopologyKind Kind { get; private set; }

    public IReadOnlyList<TerrainNodeElevationTopologyVertex> Vertices =>
        vertices;

    public IReadOnlyList<TerrainNodeElevationTopologyTriangle> Triangles =>
        triangles;

    public IReadOnlyList<TerrainNodeElevationTopologyEdge> Edges =>
        edges;

    public IReadOnlyList<int> HullVertexIndices =>
        hullVertexIndices;

    public IReadOnlyList<TerrainNodeElevationTopologyEdge> HullEdges =>
        hullEdges;

    public int VertexCount =>
        vertices.Count;

    public int TriangleCount =>
        triangles.Count;

    public int EdgeCount =>
        edges.Count;

    public int HullVertexCount =>
        hullVertexIndices.Count;

    public int HullEdgeCount =>
        hullEdges.Count;

    internal TerrainNodeElevationTopology(
        TerrainNodeElevationTopologyKind kind,
        TerrainNodeElevationTopologyVertex[] vertices,
        TerrainNodeElevationTopologyTriangle[] triangles,
        TerrainNodeElevationTopologyEdge[] edges,
        int[] hullVertexIndices,
        TerrainNodeElevationTopologyEdge[] hullEdges,
        int[][] vertexNeighbors)
    {
        Kind = kind;

        this.vertices =
            Array.AsReadOnly(
                vertices ??
                new TerrainNodeElevationTopologyVertex[0]);

        this.triangles =
            Array.AsReadOnly(
                triangles ??
                new TerrainNodeElevationTopologyTriangle[0]);

        this.edges =
            Array.AsReadOnly(
                edges ??
                new TerrainNodeElevationTopologyEdge[0]);

        this.hullVertexIndices =
            Array.AsReadOnly(
                hullVertexIndices ??
                new int[0]);

        this.hullEdges =
            Array.AsReadOnly(
                hullEdges ??
                new TerrainNodeElevationTopologyEdge[0]);

        int neighborCount =
            this.vertices.Count;

        this.vertexNeighbors =
            new ReadOnlyCollection<int>[neighborCount];

        for (int index = 0; index < neighborCount; index++)
        {
            int[] sourceNeighbors =
                vertexNeighbors != null &&
                index < vertexNeighbors.Length &&
                vertexNeighbors[index] != null
                    ? vertexNeighbors[index]
                    : new int[0];

            this.vertexNeighbors[index] =
                Array.AsReadOnly(sourceNeighbors);
        }
    }

    public IReadOnlyList<int> GetVertexNeighbors(
        int vertexIndex)
    {
        if (vertexIndex < 0 || vertexIndex >= vertexNeighbors.Length)
        {
            return EmptyNeighborList;
        }

        return vertexNeighbors[vertexIndex];
    }

    /*
     * Linear search is deliberate in Package I2. Spatial acceleration belongs
     * to later performance work after triangulated interpolation is active.
     */
    public bool TryFindContainingTriangle(
        Vector2 positionXZ,
        out int triangleIndex)
    {
        triangleIndex = -1;

        if (
            Kind != TerrainNodeElevationTopologyKind.Triangulated ||
            !TerrainNodeElevationGeometryUtility.IsFinite(positionXZ))
        {
            return false;
        }

        for (int index = 0; index < triangles.Count; index++)
        {
            TerrainNodeElevationTopologyTriangle triangle =
                triangles[index];

            Vector2 a = vertices[triangle.VertexA].PositionXZ;
            Vector2 b = vertices[triangle.VertexB].PositionXZ;
            Vector2 c = vertices[triangle.VertexC].PositionXZ;

            if (TerrainNodeElevationGeometryUtility.IsPointInTriangleInclusive(
                positionXZ,
                a,
                b,
                c))
            {
                /*
                 * Triangles are canonically sorted, so the first match is the
                 * lowest deterministic triangle index for shared edges/vertices.
                 */
                triangleIndex = index;
                return true;
            }
        }

        return false;
    }
}
