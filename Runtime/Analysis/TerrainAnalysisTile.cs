using UnityEngine;

/*
 * Read-only view of one tile/slice in a ready TerrainAnalysisLayer.
 *
 * The tile snapshots the layer revision and texture instance used when it was
 * resolved. IsCurrent can be used before a later operation to ensure the view
 * still refers to the current analysis data.
 */
public readonly struct TerrainAnalysisTile
{
    public readonly TerrainAnalysisLayer Layer;
    public readonly Vector2Int TileCoordinate;
    public readonly Vector2Int CacheLocalTile;
    public readonly int SliceIndex;
    public readonly int Revision;
    public readonly int TextureInstanceId;

    public TerrainAnalysisTile(
        TerrainAnalysisLayer layer,
        Vector2Int tileCoordinate,
        Vector2Int cacheLocalTile,
        int sliceIndex
    )
    {
        Layer = layer;
        TileCoordinate = tileCoordinate;
        CacheLocalTile = cacheLocalTile;
        SliceIndex = sliceIndex;

        Revision =
            layer != null
                ? layer.Revision
                : 0;

        TextureInstanceId =
            layer != null &&
            layer.Texture != null
                ? layer.Texture.GetInstanceID()
                : 0;
    }

    public TerrainAnalysisKey Key =>
        Layer != null
            ? Layer.Key
            : default;

    public int SamplesPerSide =>
        Layer != null
            ? Layer.SamplesPerSide
            : 0;

    public float SampleSpacing =>
        Layer != null
            ? Layer.SampleSpacing
            : 0f;

    public float WorldSize
    {
        get
        {
            if (
                Layer == null ||
                Layer.SamplesPerSide <= 1 ||
                Layer.SampleSpacing <= 0f
            )
            {
                return 0f;
            }

            return
                (Layer.SamplesPerSide - 1) *
                Layer.SampleSpacing;
        }
    }

    public Vector2 WorldOriginXZ =>
        new Vector2(
            TileCoordinate.x * WorldSize,
            TileCoordinate.y * WorldSize
        );

    public bool IsCurrent
    {
        get
        {
            return
                Layer != null &&
                Layer.IsReady &&
                Layer.Texture != null &&
                Layer.Texture.IsCreated() &&
                Layer.Revision == Revision &&
                Layer.Texture.GetInstanceID() ==
                    TextureInstanceId;
        }
    }
}
