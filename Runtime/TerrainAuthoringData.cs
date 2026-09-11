using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum TerrainHeightSourceMode
{
    Flat,
    Procedural,
    Imported
}

[CreateAssetMenu(
    fileName = "TerrainAuthoringData",
    menuName = "WorldMeshes/Terrain Authoring Data"
)]
public class TerrainAuthoringData :
    ScriptableObject,
    ISerializationCallbackReceiver
{
    [Header("Heightfield Initialization")]

    public TerrainHeightSourceMode sourceMode =
        TerrainHeightSourceMode.Procedural;

    public float flatHeight =
        0f;

    public Texture2D importedHeightmap;

    [Header("Regional Elevation")]

    /*
     * Optional broad regional-elevation authoring source.
     *
     * Existing TerrainAuthoringData assets intentionally deserialize this as
     * null. Later packages will provide explicit setup/mutation workflows;
     * Package 1 never creates a source automatically.
     */
    [SerializeReference]
    private TerrainRegionalElevationSource regionalElevationSource;

    [Header("Height Modifiers")]

    /*
     * Ordered modifier ownership.
     *
     * SerializeReference allows this one list to persist polymorphic
     * TerrainHeightModifier subclasses while preserving list order.
     */
    [SerializeReference]
    private List<TerrainHeightModifier> heightModifiers =
        new List<TerrainHeightModifier>();

    [NonSerialized]
    private ReadOnlyCollection<TerrainHeightModifier>
        readOnlyHeightModifiers;

    [Header("State")]

    [HideInInspector]
    public int authoringRevision =
        0;

    public TerrainRegionalElevationSource RegionalElevationSource
    {
        get
        {
            return
                regionalElevationSource;
        }
    }

    public bool HasRegionalElevationSource
    {
        get
        {
            return
                regionalElevationSource !=
                null;
        }
    }

    public IReadOnlyList<TerrainHeightModifier> HeightModifiers
    {
        get
        {
            EnsureModifierList();

            if (
                readOnlyHeightModifiers == null
            )
            {
                readOnlyHeightModifiers =
                    heightModifiers.AsReadOnly();
            }

            return
                readOnlyHeightModifiers;
        }
    }

    public int HeightModifierCount
    {
        get
        {
            return
                heightModifiers != null
                    ? heightModifiers.Count
                    : 0;
        }
    }

    /*
     * Detects malformed identity state without changing the asset.
     */
    internal bool TryValidateModifierStableIds(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        EnsureModifierList();

        HashSet<string> usedIds =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        for (
            int index = 0;
            index < heightModifiers.Count;
            index++
        )
        {
            TerrainHeightModifier modifier =
                heightModifiers[
                    index
                ];

            if (modifier == null)
            {
                errorMessage =
                    $"Modifier index {index} is null.";

                return false;
            }

            if (!modifier.HasValidStableId())
            {
                errorMessage =
                    $"Modifier index {index} does not have a valid " +
                    "persistent stable ID.";

                return false;
            }

            if (
                !usedIds.Add(
                    modifier.StableId
                )
            )
            {
                errorMessage =
                    $"Modifier index {index} duplicates stable ID " +
                    $"{modifier.StableId}.";

                return false;
            }
        }

        return true;
    }

    /*
     * Repairs missing/malformed/duplicate stable IDs.
     *
     * The first valid occurrence of an ID keeps it. Later duplicates
     * receive new IDs. This method does not change authoringRevision;
     * Stage 12's mutation service will own revision transactions.
     */
    internal int RepairModifierStableIds()
    {
        EnsureModifierList();

        HashSet<string> usedIds =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        int repairedCount =
            0;

        for (
            int index = 0;
            index < heightModifiers.Count;
            index++
        )
        {
            TerrainHeightModifier modifier =
                heightModifiers[
                    index
                ];

            if (modifier == null)
            {
                continue;
            }

            if (
                modifier.EnsureUniqueStableId(
                    usedIds
                )
            )
            {
                repairedCount++;
            }
        }

        return
            repairedCount;
    }

    // =====================================================
    // INTERNAL REGIONAL ELEVATION STORAGE ACCESS
    // =====================================================

    /*
     * These are intentionally internal. A later package will introduce the
     * production regional-elevation mutation service and transaction boundary.
     */

    internal void SetRegionalElevationSourceInternal(
        TerrainRegionalElevationSource source
    )
    {
        regionalElevationSource =
            source;
    }

    internal void ClearRegionalElevationSourceInternal()
    {
        regionalElevationSource =
            null;
    }

    // =====================================================
    // INTERNAL MODIFIER STORAGE ACCESS
    // =====================================================

    /*
     * These are intentionally internal rather than public.
     *
     * Stage 12's TerrainAuthoringModifierService will become the sole
     * production mutation entry point. Stage 11 validation also uses
     * these methods against temporary test assets.
     */

    internal void AddHeightModifierInternal(
        TerrainHeightModifier modifier
    )
    {
        if (modifier == null)
        {
            return;
        }

        EnsureModifierList();

        heightModifiers.Add(
            modifier
        );
    }

    internal void InsertHeightModifierInternal(
        int index,
        TerrainHeightModifier modifier
    )
    {
        if (modifier == null)
        {
            return;
        }

        EnsureModifierList();

        int safeIndex =
            Mathf.Clamp(
                index,
                0,
                heightModifiers.Count
            );

        heightModifiers.Insert(
            safeIndex,
            modifier
        );
    }

    internal bool RemoveHeightModifierAtInternal(
        int index
    )
    {
        EnsureModifierList();

        if (
            index < 0
            ||
            index >=
                heightModifiers.Count
        )
        {
            return false;
        }

        heightModifiers.RemoveAt(
            index
        );

        return true;
    }

    internal bool MoveHeightModifierInternal(
        int oldIndex,
        int newIndex
    )
    {
        EnsureModifierList();

        if (
            oldIndex < 0
            ||
            oldIndex >=
                heightModifiers.Count
            ||
            newIndex < 0
            ||
            newIndex >=
                heightModifiers.Count
            ||
            oldIndex ==
                newIndex
        )
        {
            return false;
        }

        TerrainHeightModifier modifier =
            heightModifiers[
                oldIndex
            ];

        heightModifiers.RemoveAt(
            oldIndex
        );

        heightModifiers.Insert(
            newIndex,
            modifier
        );

        return true;
    }

    internal void ClearHeightModifiersInternal()
    {
        EnsureModifierList();

        heightModifiers.Clear();
    }

    // =====================================================
    // UNITY SERIALIZATION
    // =====================================================

    private void OnEnable()
    {
        EnsureModifierList();
    }

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
        /*
         * Unity may reconstruct the backing list during managed
         * reference deserialization. Rebuild the read-only wrapper on
         * next access.
         */
        readOnlyHeightModifiers =
            null;
    }

    private void EnsureModifierList()
    {
        if (heightModifiers != null)
        {
            return;
        }

        heightModifiers =
            new List<TerrainHeightModifier>();

        readOnlyHeightModifiers =
            null;
    }
}
