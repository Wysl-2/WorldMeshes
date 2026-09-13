using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainHeightModifierSnapshot
{
    public string StableId { get; private set; }
    public int Index { get; private set; }
    public bool Enabled { get; private set; }
    public Bounds AffectedWorldBounds { get; private set; }
    public string ContentSignature { get; private set; }

    private TerrainHeightModifierSnapshot()
    {
    }

    internal static bool TryCreate(
        TerrainHeightModifier modifier,
        int index,
        out TerrainHeightModifierSnapshot snapshot
    )
    {
        snapshot = null;

        if (
            modifier == null
            ||
            string.IsNullOrEmpty(modifier.StableId)
        )
        {
            return false;
        }

        string contentSignature =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    modifier
                );

        if (string.IsNullOrEmpty(contentSignature))
        {
            return false;
        }

        snapshot =
            new TerrainHeightModifierSnapshot
            {
                StableId = modifier.StableId,
                Index = index,
                Enabled = modifier.Enabled,
                AffectedWorldBounds =
                    modifier.GetAffectedWorldBounds(),
                ContentSignature = contentSignature
            };

        return true;
    }

    internal static bool StackEquals(
        IReadOnlyList<TerrainHeightModifierSnapshot> a,
        IReadOnlyList<TerrainHeightModifierSnapshot> b
    )
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (
            a == null
            ||
            b == null
            ||
            a.Count != b.Count
        )
        {
            return false;
        }

        for (int index = 0; index < a.Count; index++)
        {
            TerrainHeightModifierSnapshot left = a[index];
            TerrainHeightModifierSnapshot right = b[index];

            if (
                left == null
                ||
                right == null
                ||
                left.StableId != right.StableId
                ||
                left.Index != right.Index
                ||
                left.ContentSignature !=
                    right.ContentSignature
            )
            {
                return false;
            }
        }

        return true;
    }
}
