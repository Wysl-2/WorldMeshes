using UnityEngine;

[CreateAssetMenu(
    fileName = "WorldSettings",
    menuName = "Scriptable Objects/WorldSettings"
)]
public class WorldSettings : ScriptableObject
{
    // =====================================================
    // WORLD SIZE
    // =====================================================

    [Header("World Size")]

    [Min(1)]
    public int gridWidth =
        10;

    [Min(1)]
    public int gridHeight =
        10;

    // =====================================================
    // HEIGHTFIELD SPATIAL LAYOUT
    // =====================================================

    [Header("Heightfield Spatial Layout")]

    [Min(0.01f)]
    public float chunkSize =
        128f;

    /*
     * Historical serialized name retained so existing
     * WorldSettings assets migrate without data loss.
     *
     * This no longer describes the resolution of a generated
     * LOD0 preview mesh. It describes the number of native
     * heightfield intervals per terrain chunk.
     *
     * Native height sample spacing:
     *
     *     chunkSize / lod0Resolution
     */
    [Min(1)]
    public int lod0Resolution =
        128;

    // =====================================================
    // HEIGHT GENERATION / INITIALIZATION
    // =====================================================

    [Header("Height Generation")]

    /*
     * Number of terrain chunks covered by one heightmap tile
     * along each axis.
     */
    [Min(1)]
    public int heightTileChunkSpan =
        4;

    public int heightSeed =
        12345;

    [Min(0.0001f)]
    public float heightNoiseScale =
        500f;

    public float heightBaseHeight =
        0f;

    [Min(0f)]
    public float heightAmplitude =
        100f;

    [Range(1, 12)]
    public int heightOctaves =
        5;

    [Range(0f, 1f)]
    public float heightPersistence =
        0.5f;

    [Min(1f)]
    public float heightLacunarity =
        2f;

    // =====================================================
    // DERIVED HEIGHTMAP VALUES
    // =====================================================

    public float HeightTileWorldSize
    {
        get
        {
            int span =
                Mathf.Max(
                    1,
                    heightTileChunkSpan
                );

            return
                Mathf.Max(
                    0.01f,
                    chunkSize
                )
                *
                span;
        }
    }

    public int HeightTileSamplesPerSide
    {
        get
        {
            int span =
                Mathf.Max(
                    1,
                    heightTileChunkSpan
                );

            int resolution =
                Mathf.Max(
                    1,
                    lod0Resolution
                );

            return
                span *
                resolution +
                1;
        }
    }

    public int HeightTileGridWidth
    {
        get
        {
            return
                Mathf.CeilToInt(
                    (float)
                    Mathf.Max(
                        1,
                        gridWidth
                    )
                    /
                    Mathf.Max(
                        1,
                        heightTileChunkSpan
                    )
                );
        }
    }

    public int HeightTileGridHeight
    {
        get
        {
            return
                Mathf.CeilToInt(
                    (float)
                    Mathf.Max(
                        1,
                        gridHeight
                    )
                    /
                    Mathf.Max(
                        1,
                        heightTileChunkSpan
                    )
                );
        }
    }

    public int HeightTileCount
    {
        get
        {
            return
                HeightTileGridWidth *
                HeightTileGridHeight;
        }
    }

    // =====================================================
    // COLLISION
    // =====================================================

    [Header("Collision")]

    [Min(1)]
    public int collisionResolution =
        64;

    // =====================================================
    // CLIPMAP
    // =====================================================

    [Header("Clipmap")]

    [Min(8)]
    public int clipmapCenterResolution =
        64;

    [Range(1, 10)]
    public int clipmapLevelCount =
        6;

    [Min(1)]
    public int clipmapBaseSampleStep =
        1;

    // =====================================================
    // DERIVED CLIPMAP VALUES
    // =====================================================

    public float ClipmapBaseSpacing
    {
        get
        {
            float safeChunkSize =
                Mathf.Max(
                    0.01f,
                    chunkSize
                );

            int safeHeightfieldResolution =
                Mathf.Max(
                    1,
                    lod0Resolution
                );

            int safeSampleStep =
                Mathf.Max(
                    1,
                    clipmapBaseSampleStep
                );

            float heightSampleSpacing =
                safeChunkSize /
                safeHeightfieldResolution;

            return
                heightSampleSpacing *
                safeSampleStep;
        }
    }

    // =====================================================
    // GENERATED RUNTIME HEIGHTMAP STATE
    // =====================================================

    /*
     * Signature of the authored terrain state used by the
     * most recent successful complete runtime compilation.
     */
    [HideInInspector]
    public string lastGeneratedHeightSignature =
        "";

    /*
     * Incremented after every successful complete runtime
     * heightmap compilation.
     */
    [HideInInspector]
    public int heightmapGenerationRevision =
        0;

    // =====================================================
    // COLLISION MESH GENERATION STATE
    // =====================================================

    [HideInInspector]
    public string lastGeneratedCollisionSignature =
        "";

    [HideInInspector]
    public int collisionMeshGenerationRevision =
        0;

    /*
     * Runtime heightmap revision used to generate the current
     * collision meshes.
     */
    [HideInInspector]
    public int collisionSourceHeightmapGenerationRevision =
        -1;
}
