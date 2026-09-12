using System.Collections.Generic;
using UnityEngine;

/*
 * Pure CPU evaluator for node-based regional elevation.
 *
 * Package 3 defines the mathematical field only. It does not mutate authoring
 * data and is not connected to terrain composition, preview, or runtime
 * streaming yet.
 */
public static class TerrainNodeElevationEvaluator
{
    /*
     * Samples within one millimetre of a node position are treated as exact
     * matches. This avoids singular IDW weights while remaining small relative
     * to normal terrain-authoring scales.
     */
    public const float ExactNodeDistance =
        0.001f;

    public const float ExactNodeDistanceSquared =
        ExactNodeDistance *
        ExactNodeDistance;

    /*
     * Evaluates the global regional-elevation field using inverse-distance
     * weighting with fixed power 2.
     *
     * Zero nodes are structurally valid source data but do not define a
     * mathematical surface, so evaluation fails instead of inventing a 0m
     * fallback. Stable IDs are intentionally not inspected.
     */
    public static bool TryEvaluateHeight(
        TerrainNodeElevationSource source,
        Vector2 worldPositionXZ,
        out float height,
        out string errorMessage
    )
    {
        height =
            0f;

        errorMessage =
            "";

        if (source == null)
        {
            errorMessage =
                "TerrainNodeElevationSource is null.";

            return false;
        }

        if (
            !IsFinite(
                worldPositionXZ.x
            )
            ||
            !IsFinite(
                worldPositionXZ.y
            )
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

        if (
            !TerrainNodeElevationInterpolationModeUtility.IsImplemented(
                source.InterpolationMode
            )
        )
        {
            errorMessage =
                TerrainNodeElevationInterpolationModeUtility
                    .GetNotImplementedMessage(
                        source.InterpolationMode
                    );

            return false;
        }

        IReadOnlyList<TerrainElevationNode> nodes =
            source.Nodes;

        if (
            nodes == null
            ||
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

            if (!IsFinite(
                singleHeight
            ))
            {
                errorMessage =
                    "Single-node regional elevation produced a non-finite " +
                    "height.";

                return false;
            }

            height =
                singleHeight;

            return true;
        }

        double exactHeightSum =
            0.0;

        int exactMatchCount =
            0;

        double weightedHeightSum =
            0.0;

        double weightSum =
            0.0;

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
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
                deltaX *
                    deltaX
                +
                deltaZ *
                    deltaZ;

            if (!IsFinite(
                distanceSquared
            ))
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
                !IsFinite(
                    weight
                )
                ||
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
                !IsFinite(
                    weightedHeightSum
                )
                ||
                !IsFinite(
                    weightSum
                )
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
                weightSum <= 0.0
                ||
                !IsFinite(
                    weightSum
                )
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
            !IsFinite(
                evaluatedHeight
            )
            ||
            evaluatedHeight > float.MaxValue
            ||
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

        if (!IsFinite(
            evaluatedHeightFloat
        ))
        {
            errorMessage =
                "Regional elevation interpolation could not be represented " +
                "as a finite float height.";

            return false;
        }

        height =
            evaluatedHeightFloat;

        return true;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

    private static bool IsFinite(
        double value
    )
    {
        return
            !double.IsNaN(
                value
            )
            &&
            !double.IsInfinity(
                value
            );
    }
}
