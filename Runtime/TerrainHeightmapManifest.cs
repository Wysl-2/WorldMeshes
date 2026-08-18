using UnityEngine;

public class TerrainHeightmapManifest :
    ScriptableObject
{
    // =====================================================
    // ADDRESSABLES
    // =====================================================

    /*
     * Runtime Addressables prefix used for generated
     * heightmap tiles.
     *
     * Example:
     *
     * TerrainHeight/HeightTile_3_5
     */
    public const string HeightTileAddressPrefix =
        "TerrainHeight/HeightTile";

    // =====================================================
    // GENERATION STATE
    // =====================================================

    public bool isComplete =
        false;

    public int generatorVersion =
        1;

    // =====================================================
    // WORLD
    // =====================================================

    public int gridWidth;

    public int gridHeight;

    // =====================================================
    // MESH LAYOUT
    // =====================================================

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

    // =====================================================
    // NOISE SETTINGS
    // =====================================================

    public int heightSeed;

    public float heightNoiseScale;

    public float heightBaseHeight;

    public float heightAmplitude;

    public int heightOctaves;

    public float heightPersistence;

    public float heightLacunarity;

    // =====================================================
    // DERIVED RUNTIME VALUES
    // =====================================================

    /*
     * Number of actual height intervals in one tile.
     *
     * Example:
     *
     * 257 samples
     * =
     * 256 intervals
     *
     * Neighboring tiles share their boundary sample.
     */
    public int HeightTileIntervalsPerSide
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    heightTileSamplesPerSide - 1
                );
        }
    }

    /*
     * World-space distance between adjacent samples in
     * the generated heightfield.
     *
     * Example:
     *
     * Chunk Size = 128
     * LOD0 Resolution = 128
     *
     * Sample Spacing = 1 metre.
     */
    public float HeightSampleSpacing
    {
        get
        {
            return
                Mathf.Max(
                    0.01f,
                    chunkSize
                )
                /
                Mathf.Max(
                    1,
                    lod0Resolution
                );
        }
    }

    /*
     * Exact playable/renderable world dimensions implied
     * by the chunk grid.
     *
     * Height tiles may extend beyond these values when
     * the final tile is only partially used.
     */
    public float WorldSizeX
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridWidth
                )
                *
                Mathf.Max(
                    0.01f,
                    chunkSize
                );
        }
    }

    public float WorldSizeZ
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridHeight
                )
                *
                Mathf.Max(
                    0.01f,
                    chunkSize
                );
        }
    }

    public Vector2 WorldSizeXZ
    {
        get
        {
            return
                new Vector2(
                    WorldSizeX,
                    WorldSizeZ
                );
        }
    }

    /*
     * Maximum valid global height-sample coordinate for
     * the actual world.
     *
     * Because the sample grid includes both ends:
     *
     * 128 metres at 1 metre spacing
     *
     * samples 0 ... 128
     */
    public int WorldMaxSampleX
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridWidth
                )
                *
                Mathf.Max(
                    1,
                    lod0Resolution
                );
        }
    }

    public int WorldMaxSampleZ
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridHeight
                )
                *
                Mathf.Max(
                    1,
                    lod0Resolution
                );
        }
    }

    // =====================================================
    // TILE VALIDATION
    // =====================================================

    public bool IsTileCoordinateValid(
        int tileX,
        int tileZ
    )
    {
        return
            tileX >= 0
            &&
            tileZ >= 0
            &&
            tileX <
                Mathf.Max(
                    1,
                    heightTileGridWidth
                )
            &&
            tileZ <
                Mathf.Max(
                    1,
                    heightTileGridHeight
                );
    }

    // =====================================================
    // ADDRESSABLE TILE ADDRESS
    // =====================================================

    public string GetHeightTileAddress(
        int tileX,
        int tileZ
    )
    {
        return
            $"{HeightTileAddressPrefix}_" +
            $"{tileX}_{tileZ}";
    }

    // =====================================================
    // WORLD POSITION -> TILE
    // =====================================================

    /*
     * Converts a world-space X/Z point into the height
     * tile containing that point.
     *
     * This method does not clamp positions outside the
     * world. It returns false instead.
     */
    public bool TryGetHeightTileCoordinate(
        Vector2 worldPositionXZ,
        out Vector2Int tileCoordinate
    )
    {
        tileCoordinate =
            default;

        if (
            worldPositionXZ.x < 0f
            ||
            worldPositionXZ.y < 0f
            ||
            worldPositionXZ.x > WorldSizeX
            ||
            worldPositionXZ.y > WorldSizeZ
        )
        {
            return false;
        }

        int tileX =
            Mathf.FloorToInt(
                worldPositionXZ.x /
                Mathf.Max(
                    0.01f,
                    heightTileWorldSize
                )
            );

        int tileZ =
            Mathf.FloorToInt(
                worldPositionXZ.y /
                Mathf.Max(
                    0.01f,
                    heightTileWorldSize
                )
            );

        /*
         * A point exactly on the maximum world edge can
         * otherwise calculate one tile beyond the grid.
         */

        tileX =
            Mathf.Clamp(
                tileX,
                0,
                Mathf.Max(
                    0,
                    heightTileGridWidth - 1
                )
            );

        tileZ =
            Mathf.Clamp(
                tileZ,
                0,
                Mathf.Max(
                    0,
                    heightTileGridHeight - 1
                )
            );

        tileCoordinate =
            new Vector2Int(
                tileX,
                tileZ
            );

        return true;
    }
}