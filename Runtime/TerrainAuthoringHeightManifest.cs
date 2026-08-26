using UnityEngine;

public class TerrainAuthoringHeightManifest :
    ScriptableObject
{
    // =====================================================
    // VERSION
    // =====================================================

    public const int CurrentVersion =
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
     * TerrainAuthoringData.authoringRevision. Future non-destructive
     * modifier edits will advance the overall authoring revision
     * without rewriting the committed base tiles.
     */
    public int committedHeightRevision =
        0;

    public string committedContentHash =
        "";

    // =====================================================
    // WORLD LAYOUT
    // =====================================================

    public int gridWidth;

    public int gridHeight;

    public float chunkSize;

    public int lod0Resolution;

    // =====================================================
    // HEIGHT TILE LAYOUT
    // =====================================================

    public int heightTileChunkSpan;

    public int heightTileGridWidth;

    public int heightTileGridHeight;

    public float heightTileWorldSize;

    public int heightTileSamplesPerSide;
}
