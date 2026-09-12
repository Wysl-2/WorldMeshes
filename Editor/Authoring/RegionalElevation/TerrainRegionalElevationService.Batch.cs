using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainRegionalElevationService
{
    public static bool MoveNodesXZ(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        IEnumerable<string> stableIds,
        Vector2 deltaXZ,
        out string errorMessage)
    {
        errorMessage = "";

        if (!ValidateFinite(deltaXZ, 0f, out errorMessage))
        {
            return false;
        }

        List<TerrainElevationNode> nodes = new List<TerrainElevationNode>();
        if (!TryResolveUniqueNodes(authoringData, stableIds, nodes, out errorMessage))
        {
            return false;
        }

        if (deltaXZ == Vector2.zero)
        {
            SetNoChangeDiagnostics("Move Regional Elevation Nodes", authoringData, worldSettings);
            return true;
        }

        for (int index = 0; index < nodes.Count; index++)
        {
            Vector2 result = nodes[index].PositionXZ + deltaXZ;
            if (!ValidateFinite(result, nodes[index].Elevation, out errorMessage))
            {
                errorMessage = "Regional elevation group movement would produce a non-finite node position.";
                return false;
            }
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Move Regional Elevation Nodes",
            () =>
            {
                for (int index = 0; index < nodes.Count; index++)
                {
                    nodes[index].SetPositionXZInternal(nodes[index].PositionXZ + deltaXZ);
                }

                return true;
            },
            out errorMessage);
    }

    public static bool SetNodesElevation(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        IEnumerable<string> stableIds,
        float elevation,
        out string errorMessage)
    {
        errorMessage = "";
        if (!IsFinite(elevation))
        {
            errorMessage = "Regional elevation batch height must be finite.";
            return false;
        }

        List<TerrainElevationNode> nodes = new List<TerrainElevationNode>();
        if (!TryResolveUniqueNodes(authoringData, stableIds, nodes, out errorMessage))
        {
            return false;
        }

        bool anyChanged = false;
        for (int index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].Elevation != elevation)
            {
                anyChanged = true;
                break;
            }
        }

        if (!anyChanged)
        {
            SetNoChangeDiagnostics("Set Regional Elevation Nodes Height", authoringData, worldSettings);
            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Set Regional Elevation Nodes Height",
            () =>
            {
                for (int index = 0; index < nodes.Count; index++)
                {
                    nodes[index].SetElevationInternal(elevation);
                }

                return true;
            },
            out errorMessage);
    }

    public static bool OffsetNodesElevation(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        IEnumerable<string> stableIds,
        float deltaElevation,
        out string errorMessage)
    {
        errorMessage = "";
        if (!IsFinite(deltaElevation))
        {
            errorMessage = "Regional elevation batch offset must be finite.";
            return false;
        }

        List<TerrainElevationNode> nodes = new List<TerrainElevationNode>();
        if (!TryResolveUniqueNodes(authoringData, stableIds, nodes, out errorMessage))
        {
            return false;
        }

        if (deltaElevation == 0f)
        {
            SetNoChangeDiagnostics("Offset Regional Elevation Nodes Height", authoringData, worldSettings);
            return true;
        }

        float[] results = new float[nodes.Count];
        for (int index = 0; index < nodes.Count; index++)
        {
            float result = nodes[index].Elevation + deltaElevation;
            if (!IsFinite(result))
            {
                errorMessage = "Regional elevation batch offset would produce a non-finite node height.";
                return false;
            }

            results[index] = result;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            deltaElevation >= 0f
                ? "Raise Regional Elevation Nodes"
                : "Lower Regional Elevation Nodes",
            () =>
            {
                for (int index = 0; index < nodes.Count; index++)
                {
                    nodes[index].SetElevationInternal(results[index]);
                }

                return true;
            },
            out errorMessage);
    }

    private static bool TryResolveUniqueNodes(
        TerrainAuthoringData authoringData,
        IEnumerable<string> stableIds,
        List<TerrainElevationNode> output,
        out string errorMessage)
    {
        errorMessage = "";
        if (output == null)
        {
            errorMessage = "Regional elevation batch output collection is null.";
            return false;
        }

        output.Clear();

        if (!TryGetValidatedNodeSource(
            authoringData,
            out TerrainNodeElevationSource source,
            out errorMessage))
        {
            return false;
        }

        if (stableIds == null)
        {
            errorMessage = "Regional elevation batch selection is null.";
            return false;
        }

        HashSet<string> requested = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string stableId in stableIds)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                errorMessage = "Regional elevation batch selection contains an empty StableId.";
                return false;
            }

            if (!requested.Add(stableId))
            {
                errorMessage = "Regional elevation batch selection contains duplicate StableIds.";
                return false;
            }
        }

        if (requested.Count <= 0)
        {
            errorMessage = "Regional elevation batch selection is empty.";
            return false;
        }

        IReadOnlyList<TerrainElevationNode> sourceNodes = source.Nodes;
        Dictionary<string, TerrainElevationNode> byId =
            new Dictionary<string, TerrainElevationNode>(System.StringComparer.Ordinal);

        for (int index = 0; index < sourceNodes.Count; index++)
        {
            TerrainElevationNode node = sourceNodes[index];
            if (node != null)
            {
                byId[node.StableId] = node;
            }
        }

        foreach (string stableId in requested)
        {
            if (!byId.TryGetValue(stableId, out TerrainElevationNode node))
            {
                errorMessage = $"Regional elevation node {stableId} was not found.";
                output.Clear();
                return false;
            }
        }

        /* Preserve source order for deterministic group operations and diagnostics. */
        for (int index = 0; index < sourceNodes.Count; index++)
        {
            TerrainElevationNode node = sourceNodes[index];
            if (node != null && requested.Contains(node.StableId))
            {
                output.Add(node);
            }
        }

        return output.Count == requested.Count;
    }
}
