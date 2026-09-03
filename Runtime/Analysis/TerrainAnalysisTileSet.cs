using System.Collections.Generic;
using UnityEngine;

/*
 * CPU-side collection of several analysis fields for one absolute terrain
 * tile. Placement/generation systems can sample many candidate points from
 * this object after one asynchronous whole-tile readback per requested field.
 */
public sealed class TerrainAnalysisTileSet
{
    private readonly Dictionary<
        TerrainAnalysisKey,
        TerrainAnalysisTileData
    > tiles =
        new Dictionary<
            TerrainAnalysisKey,
            TerrainAnalysisTileData
        >();

    public Vector2Int TileCoordinate
    {
        get;
    }

    public int Count =>
        tiles.Count;

    public TerrainAnalysisTileSet(
        Vector2Int tileCoordinate
    )
    {
        TileCoordinate =
            tileCoordinate;
    }

    internal void Add(
        TerrainAnalysisTileData tile
    )
    {
        if (
            tile == null
            ||
            tile.TileCoordinate !=
                TileCoordinate
        )
        {
            return;
        }

        tiles[
            tile.Key
        ] =
            tile;
    }

    public bool TryGetTile(
        TerrainAnalysisKey key,
        out TerrainAnalysisTileData tile
    )
    {
        return
            tiles.TryGetValue(
                key,
                out tile
            );
    }

    public bool TrySampleBilinear(
        TerrainAnalysisKey key,
        Vector2 worldXZ,
        out float value
    )
    {
        value =
            0f;

        return
            TryGetTile(
                key,
                out TerrainAnalysisTileData tile
            )
            &&
            tile != null
            &&
            tile.TrySampleBilinear(
                worldXZ,
                out value
            );
    }
}
