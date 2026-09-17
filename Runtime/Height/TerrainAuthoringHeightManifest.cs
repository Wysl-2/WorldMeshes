using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class TerrainAuthoringHeightManifest :
    ScriptableObject
{
    // =====================================================
    // VERSION
    // =====================================================

    public const int CurrentVersion =
        2;

    public const int CurrentTileHeightRangeMetadataVersion =
        1;

    public int manifestVersion =
        CurrentVersion;

    // =====================================================
    // COMMITTED AUTHORING STATE
    // =====================================================

    public bool isComplete =
        false;

    /*
     * Revision of the COMMITTED base heightfield only.
     *
     * This is intentionally independent from
     * TerrainAuthoringData.authoringRevision. Future
     * non-destructive modifier edits can advance the overall
     * authoring revision without rewriting the committed base
     * tiles.
     */
    public int committedHeightRevision =
        0;

    public string committedContentHash =
        "";

    /*
     * Exact finite sample range of the committed authoring
     * height tiles.
     */
    public float minimumCommittedHeight =
        0f;

    public float maximumCommittedHeight =
        0f;

    // =====================================================
    // WORLD LAYOUT
    // =====================================================

    public int gridWidth;

    public int gridHeight;

    public float chunkSize;

    /*
     * Native heightfield intervals per terrain chunk.
     */
    [FormerlySerializedAs("lod0Resolution")]
    public int heightfieldResolutionPerChunk;

    // =====================================================
    // HEIGHT TILE LAYOUT
    // =====================================================

    public int heightTileChunkSpan;

    public int heightTileGridWidth;

    public int heightTileGridHeight;

    public float heightTileWorldSize;

    public int heightTileSamplesPerSide;

    // =====================================================
    // PER-TILE HEIGHT RANGE METADATA
    // =====================================================

    /*
     * Independent schema version for derived per-tile range
     * metadata.
     *
     * Version 0 means the manifest predates authoritative
     * per-tile range metadata. This intentionally does not
     * participate in committed terrain identity/signatures.
     */
    [SerializeField]
    private int tileHeightRangeMetadataVersion =
        0;

    /*
     * Flattened row-major per-tile range metadata:
     *
     *     index = tileX + tileZ * heightTileGridWidth
     *
     * Complete/current manifests contain exactly one valid
     * range for every committed authoring height tile.
     */
    [SerializeField]
    private List<TerrainHeightTileRange>
        tileHeightRanges =
            new List<TerrainHeightTileRange>();

    public int TileHeightRangeMetadataVersion =>
        tileHeightRangeMetadataVersion;

    public int TileHeightRangeCount =>
        tileHeightRanges != null
            ? tileHeightRanges.Count
            : 0;

    public int ExpectedTileHeightRangeCount
    {
        get
        {
            if (
                heightTileGridWidth <= 0
                ||
                heightTileGridHeight <= 0
            )
            {
                return 0;
            }

            return
                heightTileGridWidth *
                heightTileGridHeight;
        }
    }

    public int ValidTileHeightRangeCount
    {
        get
        {
            if (tileHeightRanges == null)
            {
                return 0;
            }

            int count =
                0;

            for (
                int index = 0;
                index < tileHeightRanges.Count;
                index++
            )
            {
                if (tileHeightRanges[index].IsValid)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool HasCompleteTileHeightRanges
    {
        get
        {
            int expectedCount =
                ExpectedTileHeightRangeCount;

            if (
                tileHeightRangeMetadataVersion !=
                    CurrentTileHeightRangeMetadataVersion
                ||
                expectedCount <= 0
                ||
                tileHeightRanges == null
                ||
                tileHeightRanges.Count != expectedCount
            )
            {
                return false;
            }

            for (
                int index = 0;
                index < tileHeightRanges.Count;
                index++
            )
            {
                if (!tileHeightRanges[index].IsValid)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void InitializeTileHeightRanges()
    {
        int count =
            ExpectedTileHeightRangeCount;

        if (tileHeightRanges == null)
        {
            tileHeightRanges =
                new List<TerrainHeightTileRange>(
                    count
                );
        }
        else
        {
            tileHeightRanges.Clear();

            if (tileHeightRanges.Capacity < count)
            {
                tileHeightRanges.Capacity =
                    count;
            }
        }

        for (
            int index = 0;
            index < count;
            index++
        )
        {
            tileHeightRanges.Add(
                default
            );
        }

        tileHeightRangeMetadataVersion =
            CurrentTileHeightRangeMetadataVersion;
    }

    public void ClearTileHeightRanges()
    {
        if (tileHeightRanges != null)
        {
            tileHeightRanges.Clear();
        }

        tileHeightRangeMetadataVersion =
            0;
    }

    public bool SetTileHeightRange(
        int tileX,
        int tileZ,
        float minimumHeight,
        float maximumHeight
    )
    {
        if (
            tileHeightRangeMetadataVersion !=
                CurrentTileHeightRangeMetadataVersion
            ||
            !IsTileCoordinateValid(
                tileX,
                tileZ
            )
        )
        {
            return false;
        }

        TerrainHeightTileRange range =
            TerrainHeightTileRange.Create(
                minimumHeight,
                maximumHeight
            );

        if (!range.IsValid)
        {
            return false;
        }

        int expectedCount =
            ExpectedTileHeightRangeCount;

        if (
            tileHeightRanges == null
            ||
            tileHeightRanges.Count != expectedCount
        )
        {
            return false;
        }

        int index =
            tileX +
            tileZ *
            heightTileGridWidth;

        tileHeightRanges[index] =
            range;

        return true;
    }

    public bool TryGetTileHeightRange(
        int tileX,
        int tileZ,
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            0f;

        maximumHeight =
            0f;

        if (
            tileHeightRangeMetadataVersion !=
                CurrentTileHeightRangeMetadataVersion
            ||
            !IsTileCoordinateValid(
                tileX,
                tileZ
            )
        )
        {
            return false;
        }

        int expectedCount =
            ExpectedTileHeightRangeCount;

        if (
            tileHeightRanges == null
            ||
            tileHeightRanges.Count != expectedCount
        )
        {
            return false;
        }

        int index =
            tileX +
            tileZ *
            heightTileGridWidth;

        TerrainHeightTileRange range =
            tileHeightRanges[index];

        if (!range.IsValid)
        {
            return false;
        }

        minimumHeight =
            range.MinimumHeight;

        maximumHeight =
            range.MaximumHeight;

        return true;
    }

    public bool TryReplaceTileHeightRanges(
        IReadOnlyList<TerrainHeightTileRange> ranges
    )
    {
        int expectedCount =
            ExpectedTileHeightRangeCount;

        if (
            expectedCount <= 0
            ||
            ranges == null
            ||
            ranges.Count != expectedCount
        )
        {
            return false;
        }

        List<TerrainHeightTileRange> replacement =
            new List<TerrainHeightTileRange>(
                expectedCount
            );

        for (
            int index = 0;
            index < expectedCount;
            index++
        )
        {
            TerrainHeightTileRange range =
                ranges[index];

            if (!range.IsValid)
            {
                return false;
            }

            replacement.Add(
                range
            );
        }

        tileHeightRanges =
            replacement;

        tileHeightRangeMetadataVersion =
            CurrentTileHeightRangeMetadataVersion;

        return true;
    }

    public bool TryCalculateGlobalHeightRange(
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        if (!HasCompleteTileHeightRanges)
        {
            return false;
        }

        for (
            int index = 0;
            index < tileHeightRanges.Count;
            index++
        )
        {
            TerrainHeightTileRange range =
                tileHeightRanges[index];

            minimumHeight =
                Mathf.Min(
                    minimumHeight,
                    range.MinimumHeight
                );

            maximumHeight =
                Mathf.Max(
                    maximumHeight,
                    range.MaximumHeight
                );
        }

        return
            IsFinite(minimumHeight)
            &&
            IsFinite(maximumHeight)
            &&
            maximumHeight >= minimumHeight;
    }

    public bool IsTileCoordinateValid(
        int tileX,
        int tileZ
    )
    {
        return
            heightTileGridWidth > 0
            &&
            heightTileGridHeight > 0
            &&
            tileX >= 0
            &&
            tileZ >= 0
            &&
            tileX <
                heightTileGridWidth
            &&
            tileZ <
                heightTileGridHeight;
    }

    // =====================================================
    // HEIGHT RANGE
    // =====================================================

    public bool HasValidCommittedHeightRange
    {
        get
        {
            return
                isComplete
                &&
                IsFinite(
                    minimumCommittedHeight
                )
                &&
                IsFinite(
                    maximumCommittedHeight
                )
                &&
                maximumCommittedHeight >=
                    minimumCommittedHeight;
        }
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }
}
