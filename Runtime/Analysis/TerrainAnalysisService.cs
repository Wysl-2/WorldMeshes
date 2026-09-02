/*
 * Central public entry point for terrain analysis.
 *
 * Stage 1 only establishes the request/cache ownership contract. Requesting
 * a layer registers it and returns its stable TerrainAnalysisLayer object;
 * no slope or curvature data is generated yet.
 *
 * Later stages will extend this service with GPU generation, dirty-tile
 * invalidation, sampling, and readback without requiring consumers to own
 * those implementation details.
 *
 * This service contains no scree-, rock-, vegetation-, or biome-specific
 * logic. Those systems consume terrain analysis rather than becoming part
 * of it.
 */
public static class TerrainAnalysisService
{
    private static readonly TerrainAnalysisCache Cache =
        new TerrainAnalysisCache();

    public static int ActiveLayerCount
    {
        get
        {
            return
                Cache.Count;
        }
    }

    public static TerrainAnalysisLayer RequestLayer(
        TerrainAnalysisKey key
    )
    {
        return
            Cache.GetOrCreateLayer(
                key
            );
    }

    public static bool TryGetLayer(
        TerrainAnalysisKey key,
        out TerrainAnalysisLayer layer
    )
    {
        return
            Cache.TryGetLayer(
                key,
                out layer
            );
    }

    public static bool ReleaseLayer(
        TerrainAnalysisKey key
    )
    {
        return
            Cache.RemoveLayer(
                key
            );
    }

    public static void InvalidateAll()
    {
        Cache.InvalidateAll();
    }

    public static void Clear()
    {
        Cache.Clear();
    }
}
