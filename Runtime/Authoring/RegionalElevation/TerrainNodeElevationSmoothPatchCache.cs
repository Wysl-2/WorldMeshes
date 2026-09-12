using UnityEngine;

/*
 * Demand-driven cache for Package I6 reduced-HCT patch data.
 *
 * Numerical validity depends on current source geometry/elevation plus the I2
 * topology and I5 gradient field supplied to the build. StableId, interpolation
 * mode, editor selection, authoringRevision, and committed terrain revision are
 * intentionally absent from the cache key.
 */
public sealed class TerrainNodeElevationSmoothPatchCache
{
    private TerrainNodeElevationSource cachedSource;
    private TerrainNodeElevationTopology cachedTopology;
    private TerrainNodeElevationGradientData cachedGradients;
    private Vector2[] cachedSourcePositions;
    private float[] cachedSourceElevations;
    private TerrainNodeElevationSmoothPatchData cachedPatchData;
    private int rebuildCount;

    public int RebuildCount =>
        rebuildCount;

    public bool HasCachedPatches =>
        cachedPatchData != null;

    public void Clear()
    {
        cachedSource = null;
        cachedTopology = null;
        cachedGradients = null;
        cachedSourcePositions = null;
        cachedSourceElevations = null;
        cachedPatchData = null;
    }

    public bool TryGetOrBuild(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        out TerrainNodeElevationSmoothPatchData patchData,
        out string errorMessage)
    {
        patchData = null;
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

        if (gradients == null)
        {
            Clear();
            errorMessage = "TerrainNodeElevationGradientData is null.";
            return false;
        }

        if (
            ReferenceEquals(source, cachedSource) &&
            ReferenceEquals(topology, cachedTopology) &&
            ReferenceEquals(gradients, cachedGradients) &&
            PositionsMatch(currentPositions, cachedSourcePositions) &&
            ElevationsMatch(currentElevations, cachedSourceElevations) &&
            cachedPatchData != null)
        {
            patchData = cachedPatchData;
            return true;
        }

        cachedSource = null;
        cachedTopology = null;
        cachedGradients = null;
        cachedSourcePositions = null;
        cachedSourceElevations = null;
        cachedPatchData = null;
        rebuildCount++;

        if (!TerrainNodeElevationSmoothPatchUtility.TryBuild(
            source,
            topology,
            gradients,
            out TerrainNodeElevationSmoothPatchData rebuilt,
            out errorMessage))
        {
            return false;
        }

        cachedSource = source;
        cachedTopology = topology;
        cachedGradients = gradients;
        cachedSourcePositions = currentPositions;
        cachedSourceElevations = currentElevations;
        cachedPatchData = rebuilt;
        patchData = rebuilt;
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

        Vector2[] capturedPositions =
            new Vector2[source.NodeCount];

        float[] capturedElevations =
            new float[source.NodeCount];

        for (int index = 0; index < source.NodeCount; index++)
        {
            TerrainElevationNode node =
                source.Nodes[index];

            if (node == null)
            {
                errorMessage =
                    $"Regional elevation node index {index} is null.";
                return false;
            }

            if (!node.TryGetStoredPositionXZInternal(
                out capturedPositions[index],
                out string positionError))
            {
                errorMessage =
                    $"Regional elevation node index {index} has invalid " +
                    "Smooth geometry. " +
                    positionError;
                return false;
            }

            capturedElevations[index] =
                node.Elevation;
        }

        positions = capturedPositions;
        elevations = capturedElevations;
        return true;
    }

    private static bool PositionsMatch(Vector2[] a, Vector2[] b)
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

    private static bool ElevationsMatch(float[] a, float[] b)
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
