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
    public int gridWidth = 10;

    [Min(1)]
    public int gridHeight = 10;

    // =====================================================
    // CHUNKS
    // =====================================================

    [Header("Chunks")]

    [Min(0.01f)]
    public float chunkSize = 128f;

    [Min(1)]
    public int lod0Resolution = 128;

    // =====================================================
    // HEIGHT GENERATION
    // =====================================================

    [Header("Height Generation")]

    /*
     * Number of terrain mesh chunks covered by one
     * heightmap tile along each axis.
     *
     * Example:
     *
     * Chunk Size = 128
     * Height Tile Chunk Span = 4
     *
     * Height Tile World Size:
     *
     * 128 * 4 = 512
     *
     * Therefore each height tile covers:
     *
     * 4 x 4 = 16 terrain chunks.
     */
    [Min(1)]
    public int heightTileChunkSpan = 4;

    /*
     * Seed used by the procedural noise generator.
     */
    public int heightSeed = 12345;

    /*
     * Controls the world-space scale of the noise.
     *
     * Larger values produce broader terrain features.
     * Smaller values produce more rapidly changing terrain.
     */
    [Min(0.0001f)]
    public float heightNoiseScale = 500f;

    /*
     * Vertical world-space offset applied to the
     * generated terrain.
     */
    public float heightBaseHeight = 0f;

    /*
     * Maximum vertical influence of the generated noise.
     */
    [Min(0f)]
    public float heightAmplitude = 100f;

    /*
     * Number of noise layers used by the fBM generator.
     */
    [Range(1, 12)]
    public int heightOctaves = 5;

    /*
     * Controls how much amplitude remains for each
     * successive octave.
     *
     * Typical value:
     *
     * 0.5
     */
    [Range(0f, 1f)]
    public float heightPersistence = 0.5f;

    /*
     * Controls how much the frequency increases for
     * each successive octave.
     *
     * Typical value:
     *
     * 2.0
     */
    [Min(1f)]
    public float heightLacunarity = 2f;

    // =====================================================
    // DERIVED HEIGHTMAP VALUES
    // =====================================================

    /*
     * Physical world-space width/length of one
     * heightmap tile.
     */
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
                * span;
        }
    }

    /*
     * Number of height samples required along one
     * side of a heightmap tile.
     *
     * Example:
     *
     * 4 chunks
     * 128 quads per chunk
     *
     * 4 * 128 = 512 intervals
     *
     * therefore:
     *
     * 513 samples.
     */
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
                span * resolution + 1;
        }
    }

    /*
     * Number of heightmap tiles required along X.
     *
     * Ceil is used so worlds that are not evenly
     * divisible by heightTileChunkSpan can still have
     * a partially used final tile.
     */
    public int HeightTileGridWidth
    {
        get
        {
            return Mathf.CeilToInt(
                (float)Mathf.Max(
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

    /*
     * Number of heightmap tiles required along Z.
     */
    public int HeightTileGridHeight
    {
        get
        {
            return Mathf.CeilToInt(
                (float)Mathf.Max(
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

    /*
     * Number of collision quads generated along one side
     * of a terrain chunk.
     *
     * This should evenly divide lod0Resolution so collision
     * vertices can sample the existing heightfield exactly.
     *
     * Example:
     *
     * LOD0 Resolution = 128
     * Collision Resolution = 64
     *
     * Height sample step = 2
     */
    [Min(1)]
    public int collisionResolution = 64;

    // =====================================================
    // CLIPMAP
    // =====================================================

    [Header("Clipmap")]

    /*
     * Number of quads along one side of the central,
     * highest-detail clipmap mesh.
     *
     * The current clipmap topology generator requires this
     * value to be evenly divisible by 4 so 2:1 transitions
     * can be generated between adjacent LOD levels.
     *
     * Example:
     *
     * 64 x 64 quads
     */
    [Min(8)]
    public int clipmapCenterResolution = 64;

    /*
     * Total number of clipmap LOD levels.
     *
     * Example:
     *
     * 6 levels:
     *
     * LOD0
     * LOD1
     * LOD2
     * LOD3
     * LOD4
     * LOD5
     */
    [Range(1, 10)]
    public int clipmapLevelCount = 6;

    /*
     * Number of source heightfield samples skipped between
     * vertices of the finest clipmap level.
     *
     * 1 = every height sample
     * 2 = every second height sample
     * 4 = every fourth height sample
     *
     * Each successive clipmap level then doubles the
     * resulting world-space vertex spacing.
     */
    [Min(1)]
    public int clipmapBaseSampleStep = 1;

    // =====================================================
    // DERIVED CLIPMAP VALUES
    // =====================================================

    /*
     * World-space spacing between neighboring vertices
     * in the finest clipmap level.
     *
     * The native heightfield sample spacing is:
     *
     * chunkSize / lod0Resolution
     *
     * The clipmap sample step multiplies that spacing.
     *
     * Example:
     *
     * Chunk Size = 128
     * LOD0 Resolution = 128
     * Base Sample Step = 1
     *
     * Height sample spacing:
     *
     * 128 / 128 = 1
     *
     * Clipmap base spacing:
     *
     * 1 * 1 = 1 world unit.
     */
    public float ClipmapBaseSpacing
    {
        get
        {
            float safeChunkSize =
                Mathf.Max(
                    0.01f,
                    chunkSize
                );

            int safeLOD0Resolution =
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
                safeLOD0Resolution;

            return
                heightSampleSpacing *
                safeSampleStep;
        }
    }

    // =====================================================
    // INTERNAL GENERATION STATE
    // =====================================================

    /*
     * These fields describe generated terrain state.
     *
     * Authoring settings above describe what we WANT.
     *
     * These fields describe what has actually been
     * generated successfully.
     *
     * They must only be updated by the corresponding
     * successful generation/synchronization operations.
     */

    // =====================================================
    // CHUNK MESH SYNCHRONIZATION STATE
    // =====================================================

    [HideInInspector]
    public int lastSyncedGridWidth = -1;

    [HideInInspector]
    public int lastSyncedGridHeight = -1;

    [HideInInspector]
    public float lastSyncedChunkSize = -1f;

    [HideInInspector]
    public int lastSyncedLOD0Resolution = -1;

    [HideInInspector]
    public string lastSyncedBaseMeshHash = "";

    /*
     * Incremented whenever a successful chunk synchronization
     * changes the required generated chunk mesh set.
     *
     * Examples:
     *
     * - chunk created
     * - chunk rebuilt
     * - obsolete chunk removed
     * - synchronized generation configuration changed
     *
     * Calling Sync Chunk Meshes when nothing changes does
     * NOT increment this revision.
     */
    [HideInInspector]
    public int chunkMeshGenerationRevision = 0;

    // =====================================================
    // HEIGHTMAP GENERATION STATE
    // =====================================================

    /*
     * Signature of the WorldSettings values used for the
     * most recent SUCCESSFUL complete heightmap generation.
     *
     * If the current settings produce a different signature,
     * the existing heightmap set is considered out of date.
     */
    [HideInInspector]
    public string lastGeneratedHeightSignature = "";

    /*
     * Incremented after every successful complete heightmap
     * generation.
     *
     * Regenerating the same height settings still increments
     * this revision. This is intentional: the generated
     * height data may have changed because the generator
     * itself changed.
     */
    [HideInInspector]
    public int heightmapGenerationRevision = 0;

    // =====================================================
    // HEIGHT APPLICATION STATE
    // =====================================================

    /*
     * These record exactly which chunk-mesh revision and
     * heightmap revision were combined during the most recent
     * SUCCESSFUL height application.
     *
     * If either source revision later changes, the chunk
     * heights automatically become out of date.
     */

    [HideInInspector]
    public int appliedChunkMeshGenerationRevision = -1;

    [HideInInspector]
    public int appliedHeightmapGenerationRevision = -1;

    // =====================================================
    // COLLISION MESH GENERATION STATE
    // =====================================================

    /*
     * Signature of the settings used to generate the
     * current collision mesh set.
     */
    [HideInInspector]
    public string lastGeneratedCollisionSignature = "";

    /*
     * Incremented after each successful complete collision
     * mesh generation.
     */
    [HideInInspector]
    public int collisionMeshGenerationRevision = 0;

    /*
     * Heightmap generation revision from which the current
     * collision meshes were produced.
     *
     * If the heightmaps are regenerated, this value no
     * longer matches heightmapGenerationRevision and the
     * collision meshes become out of date.
     */
    [HideInInspector]
    public int collisionSourceHeightmapGenerationRevision = -1;
}