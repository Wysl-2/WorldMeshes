using UnityEngine;

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
     * Legacy field retained while older terrain-generation
     * utilities and validators still exist in the project.
     */
    public int generatorVersion =
        1;

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

    /*
     * Hash of the committed source tile assets used by the
     * successful compilation. This is primarily diagnostic
     * and provides a precise record of compiled source data.
     */
    public string sourceAuthoringContentHash =
        "";

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
    // LEGACY PROCEDURAL SETTINGS
    // =====================================================

    /*
     * These remain temporarily because the existing
     * TerrainHeightmapValidator still compares them against
     * WorldSettings. They are no longer the source of the
     * runtime terrain once TerrainRuntimeHeightCompiler is used.
     */

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
                    lod0Resolution
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
