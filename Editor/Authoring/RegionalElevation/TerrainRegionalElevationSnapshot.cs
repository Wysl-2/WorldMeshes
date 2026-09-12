using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Transient editor snapshot of regional-elevation authoring state.
 *
 * This type is intentionally not serialized. It exists only so the Package 5
 * mutation service and Undo/Redo change tracker can compare acknowledged
 * authoring state without modifying persistent data.
 */
internal sealed class TerrainRegionalElevationSnapshot
{
    internal sealed class NodeSnapshot
    {
        public string StableId;
        public Vector2 PositionXZ;
        public float Elevation;
    }

    public string SourceType { get; private set; }

    public TerrainNodeElevationInterpolationMode InterpolationMode
    {
        get;
        private set;
    }

    public IReadOnlyList<NodeSnapshot> Nodes =>
        nodes;

    public int NodeCount =>
        nodes.Count;

    private readonly List<NodeSnapshot> nodes =
        new List<NodeSnapshot>();

    private TerrainRegionalElevationSnapshot()
    {
    }

    public static bool TryCapture(
        TerrainAuthoringData authoringData,
        out TerrainRegionalElevationSnapshot snapshot,
        out string errorMessage
    )
    {
        snapshot =
            null;

        errorMessage =
            "";

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        TerrainRegionalElevationSnapshot result =
            new TerrainRegionalElevationSnapshot();

        TerrainRegionalElevationSource source =
            authoringData.RegionalElevationSource;

        if (source == null)
        {
            result.SourceType =
                "<none>";

            snapshot =
                result;

            return true;
        }

        result.SourceType =
            source.GetType().AssemblyQualifiedName ??
            source.GetType().FullName ??
            source.GetType().Name;

        if (!(source is TerrainNodeElevationSource nodeSource))
        {
            snapshot =
                result;

            return true;
        }

        result.InterpolationMode =
            nodeSource.InterpolationMode;

        if (!nodeSource.TryValidateOutputData(
            out errorMessage
        ))
        {
            return false;
        }

        IReadOnlyList<TerrainElevationNode> sourceNodes =
            nodeSource.Nodes;

        for (
            int index = 0;
            index < sourceNodes.Count;
            index++
        )
        {
            TerrainElevationNode node =
                sourceNodes[index];

            if (node == null)
            {
                errorMessage =
                    $"Regional elevation node index {index} is null.";

                return false;
            }

            result.nodes.Add(
                new NodeSnapshot
                {
                    StableId =
                        node.StableId ?? "",

                    PositionXZ =
                        node.PositionXZ,

                    Elevation =
                        node.Elevation
                }
            );
        }

        snapshot =
            result;

        return true;
    }

    public bool StateEquals(
        TerrainRegionalElevationSnapshot other
    )
    {
        if (!OutputEquals(other))
        {
            return false;
        }

        if (other == null)
        {
            return false;
        }

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            if (
                !string.Equals(
                    nodes[index].StableId,
                    other.nodes[index].StableId,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    public bool OutputEquals(
        TerrainRegionalElevationSnapshot other
    )
    {
        if (other == null)
        {
            return false;
        }

        if (
            !string.Equals(
                SourceType,
                other.SourceType,
                StringComparison.Ordinal
            )
            ||
            InterpolationMode !=
                other.InterpolationMode
            ||
            nodes.Count !=
                other.nodes.Count
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            NodeSnapshot a =
                nodes[index];

            NodeSnapshot b =
                other.nodes[index];

            if (
                a.PositionXZ !=
                    b.PositionXZ
                ||
                a.Elevation !=
                    b.Elevation
            )
            {
                return false;
            }
        }

        return true;
    }
}
