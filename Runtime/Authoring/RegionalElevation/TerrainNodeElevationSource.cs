using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

/*
 * Persistent regional-elevation source backed by an ordered collection of
 * freely placeable elevation nodes.
 *
 * Package 1 defines source storage/signature behavior only. Node placement,
 * interpolation, terrain composition, and production editor mutation are not
 * implemented here.
 */
[Serializable]
public sealed class TerrainNodeElevationSource :
    TerrainRegionalElevationSource
{
    [SerializeField]
    private List<TerrainElevationNode> nodes =
        new List<TerrainElevationNode>();

    [NonSerialized]
    private ReadOnlyCollection<TerrainElevationNode>
        readOnlyNodes;

    public IReadOnlyList<TerrainElevationNode> Nodes
    {
        get
        {
            EnsureNodeList();

            if (readOnlyNodes == null)
            {
                readOnlyNodes =
                    nodes.AsReadOnly();
            }

            return
                readOnlyNodes;
        }
    }

    public int NodeCount
    {
        get
        {
            return
                nodes != null
                    ? nodes.Count
                    : 0;
        }
    }

    /*
     * Detects malformed persistent node identity without changing source data.
     */
    internal bool TryValidateNodeStableIds(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        EnsureNodeList();

        HashSet<string> usedIds =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            TerrainElevationNode node =
                nodes[index];

            if (node == null)
            {
                errorMessage =
                    $"Elevation node index {index} is null.";

                return false;
            }

            if (!node.HasValidStableId())
            {
                errorMessage =
                    $"Elevation node index {index} does not have a valid " +
                    "persistent stable ID.";

                return false;
            }

            if (!usedIds.Add(
                node.StableId
            ))
            {
                errorMessage =
                    $"Elevation node index {index} duplicates stable ID " +
                    $"{node.StableId}.";

                return false;
            }
        }

        return true;
    }

    /*
     * Repairs only persistent editor identity.
     *
     * The first valid occurrence keeps its ID. Missing/malformed IDs and later
     * duplicates receive new IDs. Position, elevation, and ordering are not
     * changed.
     */
    internal int RepairNodeStableIds()
    {
        EnsureNodeList();

        HashSet<string> usedIds =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        int repairedCount =
            0;

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            TerrainElevationNode node =
                nodes[index];

            if (node == null)
            {
                continue;
            }

            if (node.EnsureUniqueStableId(
                usedIds
            ))
            {
                repairedCount++;
            }
        }

        return
            repairedCount;
    }

    // =====================================================
    // INTERNAL NODE STORAGE ACCESS
    // =====================================================

    internal void AddNodeInternal(
        TerrainElevationNode node
    )
    {
        if (node == null)
        {
            return;
        }

        EnsureNodeList();

        nodes.Add(
            node
        );
    }

    internal void InsertNodeInternal(
        int index,
        TerrainElevationNode node
    )
    {
        if (node == null)
        {
            return;
        }

        EnsureNodeList();

        int safeIndex =
            Mathf.Clamp(
                index,
                0,
                nodes.Count
            );

        nodes.Insert(
            safeIndex,
            node
        );
    }

    internal bool RemoveNodeAtInternal(
        int index
    )
    {
        EnsureNodeList();

        if (
            index < 0
            ||
            index >= nodes.Count
        )
        {
            return false;
        }

        nodes.RemoveAt(
            index
        );

        return true;
    }

    internal void ClearNodesInternal()
    {
        EnsureNodeList();

        nodes.Clear();
    }

    // =====================================================
    // OUTPUT VALIDATION / SIGNATURE
    // =====================================================

    internal override bool TryValidateOutputData(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        EnsureNodeList();

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            TerrainElevationNode node =
                nodes[index];

            if (node == null)
            {
                errorMessage =
                    $"Elevation node index {index} is null.";

                return false;
            }

            if (!node.TryValidateOutputData(
                out string nodeError
            ))
            {
                errorMessage =
                    $"Elevation node index {index} is invalid. " +
                    nodeError;

                return false;
            }
        }

        return true;
    }

    protected override string GetSignatureTypeId()
    {
        return
            "TerrainNodeElevationSourceV2";
    }

    protected override void AppendTypeSpecificSignatureData(
        StringBuilder builder
    )
    {
        EnsureNodeList();

        AppendInt(
            builder,
            nodes.Count
        );

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            TerrainElevationNode node =
                nodes[index];

            AppendInt(
                builder,
                index
            );

            AppendVector2(
                builder,
                node.PositionXZ
            );

            AppendFloat(
                builder,
                node.Elevation
            );
        }
    }

    private void EnsureNodeList()
    {
        if (nodes != null)
        {
            return;
        }

        nodes =
            new List<TerrainElevationNode>();

        readOnlyNodes =
            null;
    }
}
