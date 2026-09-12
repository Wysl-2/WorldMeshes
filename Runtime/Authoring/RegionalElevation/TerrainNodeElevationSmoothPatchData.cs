using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/*
 * One cubic triangular Bernstein-Bezier patch.
 *
 * Coefficient order is fixed for CPU/GPU parity:
 * B300, B030, B003, B210, B201, B120, B021, B102, B012, B111.
 */
public struct TerrainNodeElevationSmoothCubicPatch
{
    public float B300 { get; private set; }
    public float B030 { get; private set; }
    public float B003 { get; private set; }
    public float B210 { get; private set; }
    public float B201 { get; private set; }
    public float B120 { get; private set; }
    public float B021 { get; private set; }
    public float B102 { get; private set; }
    public float B012 { get; private set; }
    public float B111 { get; private set; }

    internal TerrainNodeElevationSmoothCubicPatch(
        float b300,
        float b030,
        float b003,
        float b210,
        float b201,
        float b120,
        float b021,
        float b102,
        float b012,
        float b111)
    {
        B300 = b300;
        B030 = b030;
        B003 = b003;
        B210 = b210;
        B201 = b201;
        B120 = b120;
        B021 = b021;
        B102 = b102;
        B012 = b012;
        B111 = b111;
    }

    internal float GetCoefficient(int index)
    {
        switch (index)
        {
            case 0: return B300;
            case 1: return B030;
            case 2: return B003;
            case 3: return B210;
            case 4: return B201;
            case 5: return B120;
            case 6: return B021;
            case 7: return B102;
            case 8: return B012;
            case 9: return B111;
            default: return float.NaN;
        }
    }
}

/*
 * One reduced-HCT macro-triangle corresponding exactly to one I2 topology
 * triangle. Subpatch order is fixed as ABG, BCG, CAG where G is the derived
 * centroid of the original topology triangle.
 */
public struct TerrainNodeElevationSmoothTrianglePatch
{
    public TerrainNodeElevationSmoothCubicPatch ABG { get; private set; }
    public TerrainNodeElevationSmoothCubicPatch BCG { get; private set; }
    public TerrainNodeElevationSmoothCubicPatch CAG { get; private set; }

    internal TerrainNodeElevationSmoothTrianglePatch(
        TerrainNodeElevationSmoothCubicPatch abg,
        TerrainNodeElevationSmoothCubicPatch bcg,
        TerrainNodeElevationSmoothCubicPatch cag)
    {
        ABG = abg;
        BCG = bcg;
        CAG = cag;
    }

    internal TerrainNodeElevationSmoothCubicPatch GetSubpatch(int index)
    {
        switch (index)
        {
            case 0: return ABG;
            case 1: return BCG;
            case 2: return CAG;
            default: return default(TerrainNodeElevationSmoothCubicPatch);
        }
    }
}

/*
 * Immutable derived Package I6 patch field. Normal triangulated topology has
 * exactly one macro-patch per I2 topology triangle. Degenerate topologies keep
 * zero macro-patches and are evaluated through explicit 0D/1D Smooth rules.
 */
public sealed class TerrainNodeElevationSmoothPatchData
{
    private readonly ReadOnlyCollection<TerrainNodeElevationSmoothTrianglePatch>
        trianglePatches;

    public IReadOnlyList<TerrainNodeElevationSmoothTrianglePatch> TrianglePatches =>
        trianglePatches;

    public int PatchCount =>
        trianglePatches.Count;

    internal TerrainNodeElevationSmoothPatchData(
        TerrainNodeElevationSmoothTrianglePatch[] trianglePatches)
    {
        this.trianglePatches =
            Array.AsReadOnly(
                trianglePatches ??
                new TerrainNodeElevationSmoothTrianglePatch[0]);
    }

    public bool TryGetTrianglePatch(
        int triangleIndex,
        out TerrainNodeElevationSmoothTrianglePatch patch)
    {
        patch =
            default(TerrainNodeElevationSmoothTrianglePatch);

        if (
            triangleIndex < 0 ||
            triangleIndex >= trianglePatches.Count)
        {
            return false;
        }

        patch =
            trianglePatches[triangleIndex];

        return true;
    }
}
