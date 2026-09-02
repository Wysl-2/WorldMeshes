using System.Collections.Generic;

/*
 * Owns all currently registered terrain-analysis layers.
 *
 * This cache is intentionally internal. Consumers request analysis through
 * TerrainAnalysisService rather than creating or managing cache entries
 * directly.
 */
internal sealed class TerrainAnalysisCache
{
    private readonly Dictionary<
        TerrainAnalysisKey,
        TerrainAnalysisLayer
    > layers =
        new Dictionary<
            TerrainAnalysisKey,
            TerrainAnalysisLayer
        >();

    public int Count
    {
        get
        {
            return
                layers.Count;
        }
    }

    public TerrainAnalysisLayer GetOrCreateLayer(
        TerrainAnalysisKey key
    )
    {
        if (
            layers.TryGetValue(
                key,
                out TerrainAnalysisLayer layer
            )
        )
        {
            return
                layer;
        }

        layer =
            new TerrainAnalysisLayer(
                key
            );

        layers.Add(
            key,
            layer
        );

        return
            layer;
    }

    public bool TryGetLayer(
        TerrainAnalysisKey key,
        out TerrainAnalysisLayer layer
    )
    {
        return
            layers.TryGetValue(
                key,
                out layer
            );
    }

    public bool RemoveLayer(
        TerrainAnalysisKey key
    )
    {
        if (
            !layers.TryGetValue(
                key,
                out TerrainAnalysisLayer layer
            )
        )
        {
            return
                false;
        }

        layer.Reset();

        return
            layers.Remove(
                key
            );
    }

    public void InvalidateAll()
    {
        foreach (
            TerrainAnalysisLayer layer
            in layers.Values
        )
        {
            layer.Invalidate();
        }
    }

    public void Clear()
    {
        foreach (
            TerrainAnalysisLayer layer
            in layers.Values
        )
        {
            layer.Reset();
        }

        layers.Clear();
    }
}
