using UnityEngine;

public class TerrainAuthoringHeightManifest :
    ScriptableObject
{
    // =====================================================
    // VERSION
    // =====================================================

    public const int CurrentVersion =
        2;

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
     *
     * This metadata replaces the old dependency on deformed
     * LOD0 preview meshes for determining clipmap displacement
     * bounds.
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
     * Historical serialized name retained for compatibility.
     * This value now means heightfield intervals per terrain
     * chunk, not a generated LOD0 preview-mesh asset.
     */
    public int lod0Resolution;

    // =====================================================
    // HEIGHT TILE LAYOUT
    // =====================================================

    public int heightTileChunkSpan;

    public int heightTileGridWidth;

    public int heightTileGridHeight;

    public float heightTileWorldSize;

    public int heightTileSamplesPerSide;

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
                !float.IsNaN(
                    minimumCommittedHeight
                )
                &&
                !float.IsInfinity(
                    minimumCommittedHeight
                )
                &&
                !float.IsNaN(
                    maximumCommittedHeight
                )
                &&
                !float.IsInfinity(
                    maximumCommittedHeight
                )
                &&
                maximumCommittedHeight >=
                    minimumCommittedHeight;
        }
    }
}
