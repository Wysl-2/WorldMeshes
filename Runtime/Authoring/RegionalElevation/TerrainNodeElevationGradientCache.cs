using UnityEngine;

/*
 * Demand-driven cache for Package I5 derived node gradients.
 *
 * Unlike TerrainNodeElevationTopologyCache, this cache depends on both source
 * XZ geometry and elevation. StableId, interpolation mode, selection, and
 * authoringRevision are intentionally not part of the cache key.
 */
public sealed class TerrainNodeElevationGradientCache
{
    private TerrainNodeElevationSource cachedSource;
    private TerrainNodeElevationTopology cachedTopology;
    private Vector2[] cachedSourcePositions;
    private float[] cachedSourceElevations;
    private TerrainNodeElevationGradientData cachedGradientData;
    private int rebuildCount;

    public int RebuildCount =>
        rebuildCount;

    public bool HasCachedGradients =>
        cachedGradientData != null;

    public void Clear()
    {
        cachedSource = null;
        cachedTopology = null;
        cachedSourcePositions = null;
        cachedSourceElevations = null;
        cachedGradientData = null;
    }

    public bool TryGetOrBuild(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        out TerrainNodeElevationGradientData gradientData,
        out string errorMessage)
    {
        gradientData = null;
        errorMessage = "";

        if (!TryCaptureDependencies(
            source,
            out Vector2[] currentPositions,
            out float[] currentElevations,
            out errorMessage))
        {
            Clear();
            return false;
        }

        if (topology == null)
        {
            Clear();
            errorMessage = "TerrainNodeElevationTopology is null.";
            return false;
        }

        if (
            ReferenceEquals(source, cachedSource) &&
            ReferenceEquals(topology, cachedTopology) &&
            PositionsMatch(currentPositions, cachedSourcePositions) &&
            ElevationsMatch(currentElevations, cachedSourceElevations) &&
            cachedGradientData != null)
        {
            gradientData = cachedGradientData;
            return true;
        }

        cachedSource = null;
        cachedTopology = null;
        cachedSourcePositions = null;
        cachedSourceElevations = null;
        cachedGradientData = null;
        rebuildCount++;

        if (!TerrainNodeElevationGradientUtility.TryBuild(
            source,
            topology,
            out TerrainNodeElevationGradientData rebuilt,
            out errorMessage))
        {
            return false;
        }

        cachedSource = source;
        cachedTopology = topology;
        cachedSourcePositions = currentPositions;
        cachedSourceElevations = currentElevations;
        cachedGradientData = rebuilt;
        gradientData = rebuilt;
        return true;
    }

    private static bool TryCaptureDependencies(
        TerrainNodeElevationSource source,
        out Vector2[] positions,
        out float[] elevations,
        out string errorMessage)
    {
        positions = null;
        elevations = null;
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        if (!source.TryValidateOutputData(out string sourceError))
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;
            return false;
        }

        int nodeCount = source.NodeCount;
        Vector2[] capturedPositions = new Vector2[nodeCount];
        float[] capturedElevations = new float[nodeCount];

        for (int index = 0; index < nodeCount; index++)
        {
            TerrainElevationNode node = source.Nodes[index];

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
                    $"Regional elevation node index {index} has invalid " +
                    "gradient geometry. " +
                    positionError;
                return false;
            }

            capturedPositions[index] = positionXZ;
            capturedElevations[index] = node.Elevation;
        }

        positions = capturedPositions;
        elevations = capturedElevations;
        return true;
    }

    private static bool PositionsMatch(
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

    private static bool ElevationsMatch(
        float[] a,
        float[] b)
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
