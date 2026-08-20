using UnityEngine;

public class TerrainCollisionManifest :
    ScriptableObject
{
    // =====================================================
    // RUNTIME ADDRESSING
    // =====================================================

    public const string CollisionMeshAddressPrefix =
        "TerrainCollision/Chunk";

    public const string CollisionRegionLabelPrefix =
        "TerrainCollisionRegion";

    // =====================================================
    // PREPARATION STATE
    // =====================================================

    public bool isComplete =
        false;

    public int manifestVersion =
        1;

    /*
     * Version of the collision mesh generator that produced
     * the assets represented by this manifest.
     */
    public int collisionGeneratorVersion =
        1;

    // =====================================================
    // WORLD LAYOUT
    // =====================================================

    public int gridWidth;

    public int gridHeight;

    public float chunkSize;

    public int lod0Resolution;

    public int collisionResolution;

    // =====================================================
    // SOURCE GENERATION STATE
    // =====================================================

    /*
     * Collision-mesh generation revision that was current when
     * these Addressables entries were prepared.
     */
    public int collisionMeshGenerationRevision;

    /*
     * Heightmap revision used by the generated collision meshes.
     */
    public int collisionSourceHeightmapGenerationRevision;

    // =====================================================
    // ADDRESSABLE PACKAGING
    // =====================================================

    /*
     * Number of collision chunks grouped along each axis into
     * one Addressables packaging region.
     *
     * Each collision mesh still has its own runtime address.
     */
    [Min(1)]
    public int regionChunkSpan =
        8;

    // =====================================================
    // DERIVED VALUES
    // =====================================================

    public int CollisionMeshCount
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
                    gridHeight
                );
        }
    }

    public int RegionGridWidth
    {
        get
        {
            return
                Mathf.CeilToInt(
                    (float)Mathf.Max(
                        1,
                        gridWidth
                    )
                    /
                    Mathf.Max(
                        1,
                        regionChunkSpan
                    )
                );
        }
    }

    public int RegionGridHeight
    {
        get
        {
            return
                Mathf.CeilToInt(
                    (float)Mathf.Max(
                        1,
                        gridHeight
                    )
                    /
                    Mathf.Max(
                        1,
                        regionChunkSpan
                    )
                );
        }
    }

    public int RegionCount
    {
        get
        {
            return
                RegionGridWidth
                *
                RegionGridHeight;
        }
    }

    // =====================================================
    // CHUNK VALIDATION
    // =====================================================

    public bool IsChunkCoordinateValid(
        int chunkX,
        int chunkZ
    )
    {
        return
            chunkX >= 0
            &&
            chunkZ >= 0
            &&
            chunkX <
                Mathf.Max(
                    1,
                    gridWidth
                )
            &&
            chunkZ <
                Mathf.Max(
                    1,
                    gridHeight
                );
    }

    // =====================================================
    // COLLISION MESH ADDRESS
    // =====================================================

    public string GetCollisionMeshAddress(
        int chunkX,
        int chunkZ
    )
    {
        return
            $"{CollisionMeshAddressPrefix}_" +
            $"{chunkX}_{chunkZ}";
    }

    // =====================================================
    // REGION COORDINATE
    // =====================================================

    public Vector2Int GetCollisionRegionCoordinate(
        int chunkX,
        int chunkZ
    )
    {
        int safeSpan =
            Mathf.Max(
                1,
                regionChunkSpan
            );

        return
            new Vector2Int(
                chunkX /
                    safeSpan,

                chunkZ /
                    safeSpan
            );
    }

    // =====================================================
    // REGION LABEL
    // =====================================================

    public string GetCollisionRegionLabel(
        int chunkX,
        int chunkZ
    )
    {
        Vector2Int region =
            GetCollisionRegionCoordinate(
                chunkX,
                chunkZ
            );

        return
            $"{CollisionRegionLabelPrefix}_" +
            $"{region.x}_{region.y}";
    }
}
