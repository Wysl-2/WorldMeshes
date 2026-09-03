using UnityEngine;

/*
 * Resolved world-space address inside one Terrain Analysis layer.
 *
 * TileCoordinate is absolute terrain-tile space.
 * CacheLocalTile is relative to the layer's Texture2DArray cache origin.
 * SliceIndex addresses the Texture2DArray slice.
 * ContinuousSample is measured in native analysis samples inside the tile.
 */
public readonly struct TerrainAnalysisAddress
{
    public readonly Vector2 WorldXZ;
    public readonly Vector2Int TileCoordinate;
    public readonly Vector2Int CacheLocalTile;
    public readonly int SliceIndex;
    public readonly Vector2 ContinuousSample;
    public readonly Vector2Int NearestSample;

    public TerrainAnalysisAddress(
        Vector2 worldXZ,
        Vector2Int tileCoordinate,
        Vector2Int cacheLocalTile,
        int sliceIndex,
        Vector2 continuousSample,
        Vector2Int nearestSample
    )
    {
        WorldXZ = worldXZ;
        TileCoordinate = tileCoordinate;
        CacheLocalTile = cacheLocalTile;
        SliceIndex = sliceIndex;
        ContinuousSample = continuousSample;
        NearestSample = nearestSample;
    }
}
