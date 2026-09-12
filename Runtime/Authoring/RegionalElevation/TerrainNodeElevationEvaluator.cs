using System.Collections.Generic;
using UnityEngine;

/*
 * Pure CPU evaluator for node-based regional elevation.
 *
 * Package I3 adds Triangulated Linear CPU evaluation over Package I2 derived
 * topology. IDW mathematics remain unchanged. No authoring state is mutated.
 */
public static class TerrainNodeElevationEvaluator
{
    /*
     * Samples within one millimetre of a node position are treated as exact
     * matches by the legacy IDW path. This constant and behavior are preserved.
     */
    public const float ExactNodeDistance =
        0.001f;

    public const float ExactNodeDistanceSquared =
        ExactNodeDistance *
        ExactNodeDistance;

    private static readonly TerrainNodeElevationTopologyCache
        triangulatedLinearTopologyCache =
            new TerrainNodeElevationTopologyCache();

    private static readonly TerrainNodeElevationTopologyCache
        triangulatedSmoothTopologyCache =
            new TerrainNodeElevationTopologyCache();

    private static readonly TerrainNodeElevationGradientCache
        triangulatedSmoothGradientCache =
            new TerrainNodeElevationGradientCache();

    private static readonly TerrainNodeElevationSmoothPatchCache
        triangulatedSmoothPatchCache =
            new TerrainNodeElevationSmoothPatchCache();

    internal static int TriangulatedLinearTopologyRebuildCount =>
        triangulatedLinearTopologyCache.RebuildCount;

    internal static int TriangulatedSmoothTopologyRebuildCount =>
        triangulatedSmoothTopologyCache.RebuildCount;

    internal static int TriangulatedSmoothGradientRebuildCount =>
        triangulatedSmoothGradientCache.RebuildCount;

    internal static int TriangulatedSmoothPatchRebuildCount =>
        triangulatedSmoothPatchCache.RebuildCount;

    internal static void ClearTriangulatedLinearTopologyCache()
    {
        triangulatedLinearTopologyCache.Clear();
    }

    internal static void ClearTriangulatedSmoothCaches()
    {
        triangulatedSmoothTopologyCache.Clear();
        triangulatedSmoothGradientCache.Clear();
        triangulatedSmoothPatchCache.Clear();
    }

    public static bool TryEvaluateHeight(
        TerrainNodeElevationSource source,
        Vector2 worldPositionXZ,
        out float height,
        out string errorMessage
    )
    {
        height = 0f;
        errorMessage = "";

        if (source == null)
        {
            errorMessage =
                "TerrainNodeElevationSource is null.";
            return false;
        }

        if (
            !IsFinite(worldPositionXZ.x) ||
            !IsFinite(worldPositionXZ.y)
        )
        {
            errorMessage =
                "Regional elevation sample position contains a non-finite " +
                "value.";
            return false;
        }

        if (
            !source.TryValidateOutputData(
                out string sourceError
            )
        )
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;
            return false;
        }

        switch (source.InterpolationMode)
        {
            case TerrainNodeElevationInterpolationMode.InverseDistanceWeighted:
                return TryEvaluateInverseDistanceWeighted(
                    source,
                    worldPositionXZ,
                    out height,
                    out errorMessage);

            case TerrainNodeElevationInterpolationMode.TriangulatedLinear:
                if (!TerrainNodeElevationInterpolationModeUtility
                    .SupportsCpuEvaluation(source.InterpolationMode))
                {
                    errorMessage =
                        TerrainNodeElevationInterpolationModeUtility
                            .GetCpuNotImplementedMessage(
                                source.InterpolationMode);
                    return false;
                }

                if (!triangulatedLinearTopologyCache.TryGetOrBuild(
                    source,
                    out TerrainNodeElevationTopology topology,
                    out string topologyError))
                {
                    errorMessage =
                        "Triangulated Linear topology could not be built. " +
                        topologyError;
                    return false;
                }

                return TerrainNodeElevationLinearInterpolationUtility
                    .TryEvaluateHeight(
                        source,
                        topology,
                        worldPositionXZ,
                        out height,
                        out errorMessage);

            case TerrainNodeElevationInterpolationMode.TriangulatedSmooth:
                if (!TerrainNodeElevationInterpolationModeUtility
                    .SupportsCpuEvaluation(source.InterpolationMode))
                {
                    errorMessage =
                        TerrainNodeElevationInterpolationModeUtility
                            .GetCpuNotImplementedMessage(
                                source.InterpolationMode);
                    return false;
                }

                if (!triangulatedSmoothTopologyCache.TryGetOrBuild(
                    source,
                    out TerrainNodeElevationTopology smoothTopology,
                    out string smoothTopologyError))
                {
                    errorMessage =
                        "Triangulated Smooth topology could not be built. " +
                        smoothTopologyError;
                    return false;
                }

                if (!triangulatedSmoothGradientCache.TryGetOrBuild(
                    source,
                    smoothTopology,
                    out TerrainNodeElevationGradientData smoothGradients,
                    out string smoothGradientError))
                {
                    errorMessage =
                        "Triangulated Smooth gradients could not be built. " +
                        smoothGradientError;
                    return false;
                }

                if (!triangulatedSmoothPatchCache.TryGetOrBuild(
                    source,
                    smoothTopology,
                    smoothGradients,
                    out TerrainNodeElevationSmoothPatchData smoothPatches,
                    out string smoothPatchError))
                {
                    errorMessage =
                        "Triangulated Smooth patches could not be built. " +
                        smoothPatchError;
                    return false;
                }

                return TerrainNodeElevationSmoothInterpolationUtility
                    .TryEvaluateHeight(
                        source,
                        smoothTopology,
                        smoothGradients,
                        smoothPatches,
                        worldPositionXZ,
                        out height,
                        out errorMessage);

            default:
                errorMessage =
                    "Terrain node elevation source contains an unsupported " +
                    "interpolation mode.";
                return false;
        }
    }

    /*
     * Existing global inverse-distance weighting with fixed power 2.
     *
     * This method is the pre-I3 algorithm moved behind explicit mode dispatch;
     * its numerical behavior is intentionally unchanged.
     */
    private static bool TryEvaluateInverseDistanceWeighted(
        TerrainNodeElevationSource source,
        Vector2 worldPositionXZ,
        out float height,
        out string errorMessage
    )
    {
        height = 0f;
        errorMessage = "";

        IReadOnlyList<TerrainElevationNode> nodes =
            source.Nodes;

        if (
            nodes == null ||
            nodes.Count == 0
        )
        {
            errorMessage =
                "TerrainNodeElevationSource contains no elevation nodes and " +
                "does not define an evaluable regional surface.";
            return false;
        }

        if (nodes.Count == 1)
        {
            float singleHeight =
                nodes[0].Elevation;

            if (!IsFinite(singleHeight))
            {
                errorMessage =
                    "Single-node regional elevation produced a non-finite " +
                    "height.";
                return false;
            }

            height = singleHeight;
            return true;
        }

        double exactHeightSum = 0.0;
        int exactMatchCount = 0;
        double weightedHeightSum = 0.0;
        double weightSum = 0.0;

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node =
                nodes[index];

            Vector2 nodePosition =
                node.PositionXZ;

            float nodeElevation =
                node.Elevation;

            double deltaX =
                (double)worldPositionXZ.x -
                nodePosition.x;

            double deltaZ =
                (double)worldPositionXZ.y -
                nodePosition.y;

            double distanceSquared =
                deltaX * deltaX +
                deltaZ * deltaZ;

            if (!IsFinite(distanceSquared))
            {
                errorMessage =
                    $"Regional elevation distance calculation failed for " +
                    $"node index {index}.";
                return false;
            }

            if (
                distanceSquared <=
                ExactNodeDistanceSquared
            )
            {
                exactHeightSum +=
                    nodeElevation;
                exactMatchCount++;
                continue;
            }

            double weight =
                1.0 /
                distanceSquared;

            if (
                !IsFinite(weight) ||
                weight <= 0.0
            )
            {
                errorMessage =
                    $"Regional elevation weight calculation failed for " +
                    $"node index {index}.";
                return false;
            }

            weightedHeightSum +=
                (double)nodeElevation *
                weight;

            weightSum +=
                weight;

            if (
                !IsFinite(weightedHeightSum) ||
                !IsFinite(weightSum)
            )
            {
                errorMessage =
                    "Regional elevation weighted accumulation became " +
                    "non-finite.";
                return false;
            }
        }

        double evaluatedHeight;

        if (exactMatchCount > 0)
        {
            evaluatedHeight =
                exactHeightSum /
                exactMatchCount;
        }
        else
        {
            if (
                weightSum <= 0.0 ||
                !IsFinite(weightSum)
            )
            {
                errorMessage =
                    "Regional elevation interpolation produced an invalid " +
                    "weight sum.";
                return false;
            }

            evaluatedHeight =
                weightedHeightSum /
                weightSum;
        }

        if (
            !IsFinite(evaluatedHeight) ||
            evaluatedHeight > float.MaxValue ||
            evaluatedHeight < -float.MaxValue
        )
        {
            errorMessage =
                "Regional elevation interpolation produced a non-finite or " +
                "out-of-range height.";
            return false;
        }

        float evaluatedHeightFloat =
            (float)evaluatedHeight;

        if (!IsFinite(evaluatedHeightFloat))
        {
            errorMessage =
                "Regional elevation interpolation could not be represented " +
                "as a finite float height.";
            return false;
        }

        height = evaluatedHeightFloat;
        return true;
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
