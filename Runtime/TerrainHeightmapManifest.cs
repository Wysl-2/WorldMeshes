using UnityEngine;
using UnityEngine.Serialization;

public class TerrainHeightmapManifest :
    ScriptableObject
{
    // =====================================================
    // ADDRESSABLES
    // =====================================================

    public const string HeightTileAddressPrefix =
        "TerrainHeight/HeightTile";

    // =====================================================
    // GENERATED OUTPUT STATE
    // =====================================================

    public bool isComplete =
        false;

    /*
     * Version of the authoring -> runtime compilation logic.
     */
    public int compilerVersion =
        0;

    // =====================================================
    // AUTHORING SOURCE
    // =====================================================

    public int sourceAuthoringRevision =
        0;

    public string sourceAuthoringSignature =
        "";

    public string sourceAuthoringContentHash =
        "";

    // =====================================================
    // COMPILED HEIGHT RANGE
    // =====================================================

    /*
     * Exact finite sample range written to the compiled
     * runtime heightmap tiles.
     */
    public float minimumTerrainHeight =
        0f;

    public float maximumTerrainHeight =
        0f;

    // =====================================================
    // WORLD
    // =====================================================

    public int gridWidth;

    public int gridHeight;

    // =====================================================
    // HEIGHTFIELD LAYOUT
    // =====================================================

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
    // DERIVED RUNTIME VALUES
    // =====================================================

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
                    heightfieldResolutionPerChunk
                );
        }
    }

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
                    heightfieldResolutionPerChunk
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
                    heightfieldResolutionPerChunk
                );
        }
    }

    public bool HasValidHeightRange
    {
        get
        {
            return
                isComplete
                &&
                !float.IsNaN(
                    minimumTerrainHeight
                )
                &&
                !float.IsInfinity(
                    minimumTerrainHeight
                )
                &&
                !float.IsNaN(
                    maximumTerrainHeight
                )
                &&
                !float.IsInfinity(
                    maximumTerrainHeight
                )
                &&
                maximumTerrainHeight >=
                    minimumTerrainHeight;
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
