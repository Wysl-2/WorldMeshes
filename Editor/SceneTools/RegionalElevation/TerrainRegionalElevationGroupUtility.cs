using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainRegionalElevationSelectionSummary
{
    public int Count { get; internal set; }
    public string PrimaryStableId { get; internal set; }
    public bool HasCommonElevation { get; internal set; }
    public float CommonElevation { get; internal set; }
    public float MinimumElevation { get; internal set; }
    public float MaximumElevation { get; internal set; }
    public Vector2 AveragePositionXZ { get; internal set; }
    public float AverageElevation { get; internal set; }
}

public static class TerrainRegionalElevationGroupUtility
{
    public static Rect NormalizeGuiRect(Vector2 a, Vector2 b)
    {
        float xMin = Mathf.Min(a.x, b.x);
        float xMax = Mathf.Max(a.x, b.x);
        float yMin = Mathf.Min(a.y, b.y);
        float yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    public static bool ContainsGuiPoint(Rect rect, Vector2 point)
    {
        return rect.Contains(point);
    }

    public static Vector2 CalculateAveragePositionXZ(
        IReadOnlyList<TerrainElevationNode> nodes)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return Vector2.zero;
        }

        double x = 0.0;
        double z = 0.0;
        int count = 0;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            Vector2 position = node.PositionXZ;
            x += position.x;
            z += position.y;
            count++;
        }

        return count > 0
            ? new Vector2((float)(x / count), (float)(z / count))
            : Vector2.zero;
    }

    public static float CalculateAverageElevation(
        IReadOnlyList<TerrainElevationNode> nodes)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return 0f;
        }

        double total = 0.0;
        int count = 0;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null)
            {
                continue;
            }

            total += node.Elevation;
            count++;
        }

        return count > 0 ? (float)(total / count) : 0f;
    }

    /*
     * Clamp one translation for the complete current group. Every node receives
     * the same delta, so relative spacing can never collapse at a world edge.
     */
    public static Vector2 ClampCommonDeltaToWorld(
        WorldSettings worldSettings,
        IReadOnlyList<TerrainElevationNode> currentNodes,
        Vector2 requestedDelta)
    {
        if (worldSettings == null || currentNodes == null || currentNodes.Count == 0)
        {
            return requestedDelta;
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        int count = 0;

        for (int index = 0; index < currentNodes.Count; index++)
        {
            TerrainElevationNode node = currentNodes[index];
            if (node == null)
            {
                continue;
            }

            Vector2 position = node.PositionXZ;
            minX = Mathf.Min(minX, position.x);
            maxX = Mathf.Max(maxX, position.x);
            minZ = Mathf.Min(minZ, position.y);
            maxZ = Mathf.Max(maxZ, position.y);
            count++;
        }

        if (count == 0)
        {
            return requestedDelta;
        }

        Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);

        float minimumDeltaX = -minX;
        float maximumDeltaX = worldSize.x - maxX;
        float minimumDeltaZ = -minZ;
        float maximumDeltaZ = worldSize.y - maxZ;

        float clampedX = ClampTranslationAxis(requestedDelta.x, minimumDeltaX, maximumDeltaX);
        float clampedZ = ClampTranslationAxis(requestedDelta.y, minimumDeltaZ, maximumDeltaZ);
        return new Vector2(clampedX, clampedZ);
    }

    public static bool TryBuildSelectionSummary(
        TerrainAuthoringData authoringData,
        out TerrainRegionalElevationSelectionSummary summary,
        out string errorMessage)
    {
        summary = new TerrainRegionalElevationSelectionSummary
        {
            PrimaryStableId = ""
        };
        errorMessage = "";

        if (authoringData == null)
        {
            errorMessage = "TerrainAuthoringData is null.";
            return false;
        }

        if (!(authoringData.RegionalElevationSource is TerrainNodeElevationSource source))
        {
            errorMessage = "The active regional elevation source is not a node source.";
            return false;
        }

        TerrainRegionalElevationSelectionState.ValidateSelection(authoringData);
        HashSet<string> selected = new HashSet<string>(System.StringComparer.Ordinal);
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(authoringData, selected);

        summary.PrimaryStableId =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(authoringData);

        if (selected.Count == 0)
        {
            return true;
        }

        double x = 0.0;
        double z = 0.0;
        double elevationTotal = 0.0;
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        float firstElevation = 0f;
        bool first = true;
        bool common = true;
        int count = 0;

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node == null || !selected.Contains(node.StableId))
            {
                continue;
            }

            Vector2 position = node.PositionXZ;
            float elevation = node.Elevation;
            x += position.x;
            z += position.y;
            elevationTotal += elevation;
            minimum = Mathf.Min(minimum, elevation);
            maximum = Mathf.Max(maximum, elevation);

            if (first)
            {
                firstElevation = elevation;
                first = false;
            }
            else if (elevation != firstElevation)
            {
                common = false;
            }

            count++;
        }

        summary.Count = count;
        if (count > 0)
        {
            summary.AveragePositionXZ = new Vector2((float)(x / count), (float)(z / count));
            summary.AverageElevation = (float)(elevationTotal / count);
            summary.MinimumElevation = minimum;
            summary.MaximumElevation = maximum;
            summary.HasCommonElevation = common;
            summary.CommonElevation = common ? firstElevation : 0f;
        }

        return true;
    }

    private static float ClampTranslationAxis(
        float requested,
        float minimum,
        float maximum)
    {
        if (minimum <= maximum)
        {
            return Mathf.Clamp(requested, minimum, maximum);
        }

        /*
         * A pre-existing group can already span beyond the logical world because
         * Package 5 deliberately permits finite out-of-world node values. When
         * no single translation can place the entire group in-bounds, allow only
         * motion that reduces the larger violation rather than distorting nodes.
         */
        if (requested < 0f && maximum < 0f)
        {
            return Mathf.Max(requested, maximum);
        }

        if (requested > 0f && minimum > 0f)
        {
            return Mathf.Min(requested, minimum);
        }

        return 0f;
    }
}
