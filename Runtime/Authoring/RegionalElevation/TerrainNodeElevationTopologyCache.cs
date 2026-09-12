using UnityEngine;

/*
 * Disposable geometry-only cache for derived regional node topology.
 *
 * Cache validity intentionally ignores elevation, StableId, interpolation
 * mode, selection, and authoringRevision. Source-order XZ positions are the
 * complete cache key because topology vertices map back to source node indices.
 */
public sealed class TerrainNodeElevationTopologyCache
{
    private TerrainNodeElevationSource cachedSource;
    private Vector2[] cachedSourcePositions;
    private TerrainNodeElevationTopology cachedTopology;
    private int rebuildCount;

    public int RebuildCount =>
        rebuildCount;

    public bool HasCachedTopology =>
        cachedTopology != null;

    public void Clear()
    {
        cachedSource = null;
        cachedSourcePositions = null;
        cachedTopology = null;
    }

    public bool TryGetOrBuild(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        topology = null;
        errorMessage = "";

        if (!TerrainNodeElevationTriangulationUtility.TryCaptureSourceGeometry(
            source,
            out Vector2[] currentPositions,
            out errorMessage))
        {
            cachedSource = null;
            cachedSourcePositions = null;
            cachedTopology = null;
            return false;
        }

        if (
            ReferenceEquals(source, cachedSource) &&
            GeometryMatches(currentPositions, cachedSourcePositions) &&
            cachedTopology != null)
        {
            topology = cachedTopology;
            return true;
        }

        cachedSource = null;
        cachedSourcePositions = null;
        cachedTopology = null;
        rebuildCount++;

        if (!TerrainNodeElevationTriangulationUtility.TryBuild(
            source,
            out TerrainNodeElevationTopology rebuilt,
            out errorMessage))
        {
            return false;
        }

        cachedSource = source;
        cachedSourcePositions = currentPositions;
        cachedTopology = rebuilt;
        topology = rebuilt;
        return true;
    }

    private static bool GeometryMatches(
        Vector2[] a,
        Vector2[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Length; index++)
        {
            if (a[index] != b[index])
            {
                return false;
            }
        }

        return true;
    }
}
