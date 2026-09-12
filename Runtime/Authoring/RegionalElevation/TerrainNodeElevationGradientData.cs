using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/*
 * One derived local elevation gradient for a canonical regional topology
 * vertex. The two components represent dh/dx and dh/dz in authoring world
 * space. Gradient identity is the topology vertex index; no StableId or other
 * persistent authoring identity is duplicated here.
 */
public struct TerrainNodeElevationGradient
{
    public float GradientX { get; private set; }
    public float GradientZ { get; private set; }

    public Vector2 GradientXZ =>
        new Vector2(
            GradientX,
            GradientZ);

    internal TerrainNodeElevationGradient(
        float gradientX,
        float gradientZ)
    {
        GradientX = gradientX;
        GradientZ = gradientZ;
    }
}

/*
 * Immutable derived gradient field indexed exactly like
 * TerrainNodeElevationTopology.Vertices.
 *
 * This data is intentionally transient. Persistent TerrainElevationNode data
 * remains limited to identity, XZ position, and elevation.
 */
public sealed class TerrainNodeElevationGradientData
{
    private readonly ReadOnlyCollection<TerrainNodeElevationGradient>
        gradients;

    public IReadOnlyList<TerrainNodeElevationGradient> Gradients =>
        gradients;

    public int GradientCount =>
        gradients.Count;

    internal TerrainNodeElevationGradientData(
        TerrainNodeElevationGradient[] gradients)
    {
        this.gradients =
            Array.AsReadOnly(
                gradients ??
                new TerrainNodeElevationGradient[0]);
    }

    public bool TryGetGradient(
        int topologyVertexIndex,
        out TerrainNodeElevationGradient gradient)
    {
        gradient =
            default(TerrainNodeElevationGradient);

        if (
            topologyVertexIndex < 0 ||
            topologyVertexIndex >= gradients.Count)
        {
            return false;
        }

        gradient =
            gradients[topologyVertexIndex];

        return true;
    }
}
