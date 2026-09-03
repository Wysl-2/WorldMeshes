using UnityEngine;

/*
 * Authoritative C# addressing for Terrain Analysis Texture2DArray data.
 *
 * This utility owns:
 * - world XZ -> absolute terrain tile
 * - absolute tile -> cache-local tile / array slice
 * - world XZ -> continuous/native sample coordinates
 *
 * Consumers should use this rather than reproducing tile/slice arithmetic.
 */
public static class TerrainAnalysisAddressingUtility
{
    public static float GetTileWorldSize(
        TerrainAnalysisLayer layer
    )
    {
        if (
            layer == null ||
            layer.SamplesPerSide <= 1 ||
            layer.SampleSpacing <= 0f
        )
        {
            return 0f;
        }

        return
            (layer.SamplesPerSide - 1) *
            layer.SampleSpacing;
    }

    public static bool TryGetTileCoordinate(
        TerrainAnalysisLayer layer,
        Vector2 worldXZ,
        out Vector2Int tileCoordinate
    )
    {
        tileCoordinate =
            Vector2Int.zero;

        if (!ValidateLayerLayout(layer))
        {
            return false;
        }

        if (!IsInsideWorld(layer, worldXZ))
        {
            return false;
        }

        float tileWorldSize =
            GetTileWorldSize(layer);

        Vector2 clampedWorldXZ =
            new Vector2(
                Mathf.Clamp(
                    worldXZ.x,
                    0f,
                    layer.WorldSizeXZ.x
                ),
                Mathf.Clamp(
                    worldXZ.y,
                    0f,
                    layer.WorldSizeXZ.y
                )
            );

        int totalTileCountX =
            CalculateLogicalTileCount(
                layer.WorldSizeXZ.x,
                tileWorldSize
            );

        int totalTileCountZ =
            CalculateLogicalTileCount(
                layer.WorldSizeXZ.y,
                tileWorldSize
            );

        int tileX =
            Mathf.FloorToInt(
                clampedWorldXZ.x /
                tileWorldSize
            );

        int tileZ =
            Mathf.FloorToInt(
                clampedWorldXZ.y /
                tileWorldSize
            );

        tileX =
            Mathf.Clamp(
                tileX,
                0,
                totalTileCountX - 1
            );

        tileZ =
            Mathf.Clamp(
                tileZ,
                0,
                totalTileCountZ - 1
            );

        tileCoordinate =
            new Vector2Int(
                tileX,
                tileZ
            );

        return
            TryGetSliceIndex(
                layer,
                tileCoordinate,
                out _
            );
    }

    public static bool TryGetSliceIndex(
        TerrainAnalysisLayer layer,
        Vector2Int tileCoordinate,
        out int sliceIndex
    )
    {
        sliceIndex =
            -1;

        if (!ValidateLayerLayout(layer))
        {
            return false;
        }

        Vector2Int localTile =
            tileCoordinate -
            layer.CacheOriginTile;

        if (
            localTile.x < 0 ||
            localTile.y < 0 ||
            localTile.x >= layer.CacheSize.x ||
            localTile.y >= layer.CacheSize.y
        )
        {
            return false;
        }

        sliceIndex =
            localTile.x +
            localTile.y *
            layer.CacheSize.x;

        return true;
    }

    public static bool TryGetTile(
        TerrainAnalysisLayer layer,
        Vector2Int tileCoordinate,
        out TerrainAnalysisTile tile
    )
    {
        tile =
            default;

        if (
            layer == null ||
            !layer.IsReady ||
            layer.Texture == null ||
            !layer.Texture.IsCreated()
        )
        {
            return false;
        }

        if (
            !TryGetSliceIndex(
                layer,
                tileCoordinate,
                out int sliceIndex
            )
        )
        {
            return false;
        }

        Vector2Int localTile =
            tileCoordinate -
            layer.CacheOriginTile;

        tile =
            new TerrainAnalysisTile(
                layer,
                tileCoordinate,
                localTile,
                sliceIndex
            );

        return true;
    }

    public static bool TryGetAddress(
        TerrainAnalysisLayer layer,
        Vector2 worldXZ,
        out TerrainAnalysisAddress address
    )
    {
        address =
            default;

        if (
            !TryGetTileCoordinate(
                layer,
                worldXZ,
                out Vector2Int tileCoordinate
            )
        )
        {
            return false;
        }

        if (
            !TryGetSliceIndex(
                layer,
                tileCoordinate,
                out int sliceIndex
            )
        )
        {
            return false;
        }

        float tileWorldSize =
            GetTileWorldSize(layer);

        Vector2 tileWorldOrigin =
            new Vector2(
                tileCoordinate.x *
                    tileWorldSize,
                tileCoordinate.y *
                    tileWorldSize
            );

        Vector2 clampedWorldXZ =
            new Vector2(
                Mathf.Clamp(
                    worldXZ.x,
                    0f,
                    layer.WorldSizeXZ.x
                ),
                Mathf.Clamp(
                    worldXZ.y,
                    0f,
                    layer.WorldSizeXZ.y
                )
            );

        Vector2 continuousSample =
            (
                clampedWorldXZ -
                tileWorldOrigin
            )
            /
            layer.SampleSpacing;

        float maximumSample =
            layer.SamplesPerSide - 1;

        continuousSample.x =
            Mathf.Clamp(
                continuousSample.x,
                0f,
                maximumSample
            );

        continuousSample.y =
            Mathf.Clamp(
                continuousSample.y,
                0f,
                maximumSample
            );

        Vector2Int nearestSample =
            new Vector2Int(
                Mathf.Clamp(
                    Mathf.RoundToInt(
                        continuousSample.x
                    ),
                    0,
                    layer.SamplesPerSide - 1
                ),
                Mathf.Clamp(
                    Mathf.RoundToInt(
                        continuousSample.y
                    ),
                    0,
                    layer.SamplesPerSide - 1
                )
            );

        address =
            new TerrainAnalysisAddress(
                clampedWorldXZ,
                tileCoordinate,
                tileCoordinate -
                    layer.CacheOriginTile,
                sliceIndex,
                continuousSample,
                nearestSample
            );

        return true;
    }

    private static bool ValidateLayerLayout(
        TerrainAnalysisLayer layer
    )
    {
        return
            layer != null &&
            layer.CacheSize.x > 0 &&
            layer.CacheSize.y > 0 &&
            layer.SamplesPerSide > 1 &&
            layer.SampleSpacing > 0f &&
            layer.WorldSizeXZ.x > 0f &&
            layer.WorldSizeXZ.y > 0f;
    }

    private static bool IsInsideWorld(
        TerrainAnalysisLayer layer,
        Vector2 worldXZ
    )
    {
        const float Tolerance =
            0.0001f;

        return
            worldXZ.x >= -Tolerance &&
            worldXZ.y >= -Tolerance &&
            worldXZ.x <=
                layer.WorldSizeXZ.x +
                Tolerance &&
            worldXZ.y <=
                layer.WorldSizeXZ.y +
                Tolerance;
    }

    private static int CalculateLogicalTileCount(
        float worldSize,
        float tileWorldSize
    )
    {
        return
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Max(
                        0f,
                        worldSize
                    )
                    /
                    Mathf.Max(
                        0.000001f,
                        tileWorldSize
                    )
                )
            );
    }
}
