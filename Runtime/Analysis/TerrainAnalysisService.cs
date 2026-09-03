using System.Collections.Generic;
using UnityEngine;

public static class TerrainAnalysisService
{
    private static readonly TerrainAnalysisCache Cache =
        new TerrainAnalysisCache();

    /*
     * Owner-scoped transient layers are intended for interactive consumers
     * such as authoring visualization sliders.
     *
     * Each owner receives at most one layer. Requesting a different key for
     * the same owner releases the previous GPU texture before generating the
     * replacement, preventing arbitrary scale changes from accumulating many
     * large analysis Texture2DArrays in the persistent cache.
     *
     * Persistent RequestLayer(...) behavior is unchanged.
     */
    private static readonly Dictionary<
        string,
        TerrainAnalysisLayer
    > TransientLayers =
        new Dictionary<
            string,
            TerrainAnalysisLayer
        >();

    private static ITerrainAnalysisGenerator generator;
    private static int generationRevision;

    public static int ActiveLayerCount =>
        Cache.Count +
        TransientLayers.Count;

    public static bool GeneratorAvailable =>
        generator != null;

    public static TerrainAnalysisLayer RequestLayer(
        TerrainAnalysisKey key
    )
    {
        TerrainAnalysisLayer layer =
            Cache.GetOrCreateLayer(key);

        EnsureLayerGenerated(
            layer
        );

        return layer;
    }

    // =====================================================
    // OWNER-SCOPED TRANSIENT ANALYSIS
    // =====================================================

    public static TerrainAnalysisLayer RequestTransientLayer(
        string ownerId,
        TerrainAnalysisKey key
    )
    {
        if (string.IsNullOrEmpty(ownerId))
        {
            return null;
        }

        if (
            TransientLayers.TryGetValue(
                ownerId,
                out TerrainAnalysisLayer layer
            )
        )
        {
            if (
                layer != null &&
                layer.Key.Equals(key)
            )
            {
                EnsureLayerGenerated(
                    layer
                );

                return layer;
            }

            if (layer != null)
            {
                layer.Reset();
            }

            TransientLayers.Remove(
                ownerId
            );
        }

        layer =
            new TerrainAnalysisLayer(
                key
            );

        TransientLayers.Add(
            ownerId,
            layer
        );

        EnsureLayerGenerated(
            layer
        );

        return layer;
    }

    public static bool TryGetTransientLayer(
        string ownerId,
        out TerrainAnalysisLayer layer
    )
    {
        layer =
            null;

        if (string.IsNullOrEmpty(ownerId))
        {
            return false;
        }

        return
            TransientLayers.TryGetValue(
                ownerId,
                out layer
            );
    }

    public static bool ReleaseTransientLayer(
        string ownerId
    )
    {
        if (
            string.IsNullOrEmpty(ownerId) ||
            !TransientLayers.TryGetValue(
                ownerId,
                out TerrainAnalysisLayer layer
            )
        )
        {
            return false;
        }

        if (layer != null)
        {
            layer.Reset();
        }

        return
            TransientLayers.Remove(
                ownerId
            );
    }

    // =====================================================
    // ANALYSIS TILE / ADDRESS ACCESS
    // =====================================================

    /*
     * RequestTile may lazily generate the requested layer first.
     */
    public static bool RequestTile(
        TerrainAnalysisKey key,
        Vector2Int tileCoordinate,
        out TerrainAnalysisTile tile
    )
    {
        TerrainAnalysisLayer layer =
            RequestLayer(
                key
            );

        return
            TerrainAnalysisAddressingUtility
                .TryGetTile(
                    layer,
                    tileCoordinate,
                    out tile
                );
    }

    /*
     * TryGetTile never triggers generation. It only accesses an existing
     * ready layer.
     */
    public static bool TryGetTile(
        TerrainAnalysisKey key,
        Vector2Int tileCoordinate,
        out TerrainAnalysisTile tile
    )
    {
        tile =
            default;

        if (
            !Cache.TryGetLayer(
                key,
                out TerrainAnalysisLayer layer
            )
            ||
            layer == null
            ||
            !layer.IsReady
        )
        {
            return false;
        }

        return
            TerrainAnalysisAddressingUtility
                .TryGetTile(
                    layer,
                    tileCoordinate,
                    out tile
                );
    }

    /*
     * RequestAddress may lazily generate the requested analysis layer.
     */
    public static bool RequestAddress(
        TerrainAnalysisKey key,
        Vector2 worldXZ,
        out TerrainAnalysisAddress address
    )
    {
        TerrainAnalysisLayer layer =
            RequestLayer(
                key
            );

        return
            TerrainAnalysisAddressingUtility
                .TryGetAddress(
                    layer,
                    worldXZ,
                    out address
                );
    }

    /*
     * TryGetAddress never triggers generation.
     */
    public static bool TryGetAddress(
        TerrainAnalysisKey key,
        Vector2 worldXZ,
        out TerrainAnalysisAddress address
    )
    {
        address =
            default;

        if (
            !Cache.TryGetLayer(
                key,
                out TerrainAnalysisLayer layer
            )
            ||
            layer == null
            ||
            !layer.IsReady
        )
        {
            return false;
        }

        return
            TerrainAnalysisAddressingUtility
                .TryGetAddress(
                    layer,
                    worldXZ,
                    out address
                );
    }

    /*
     * Called after the authoring preview has successfully recomposited a set
     * of source height tiles.
     *
     * Both persistent and transient ready analysis layers expand that source
     * set by their dependency radius and regenerate only affected slices.
     */
    public static void NotifySourceTilesChanged(
        IReadOnlyList<Vector2Int> changedSourceTiles
    )
    {
        if (
            changedSourceTiles == null ||
            changedSourceTiles.Count == 0
        )
        {
            return;
        }

        if (generator == null)
        {
            InvalidateAll();
            return;
        }

        List<TerrainAnalysisLayer> activeLayers =
            new List<TerrainAnalysisLayer>(
                Cache.Count +
                TransientLayers.Count
            );

        Cache.CopyLayers(
            activeLayers
        );

        foreach (
            TerrainAnalysisLayer transientLayer
            in TransientLayers.Values
        )
        {
            activeLayers.Add(
                transientLayer
            );
        }

        HashSet<Vector2Int> affectedTileSet =
            new HashSet<Vector2Int>();

        List<Vector2Int> affectedTiles =
            new List<Vector2Int>();

        for (
            int layerIndex = 0;
            layerIndex < activeLayers.Count;
            layerIndex++
        )
        {
            TerrainAnalysisLayer layer =
                activeLayers[
                    layerIndex
                ];

            if (
                layer == null ||
                !layer.IsReady
            )
            {
                continue;
            }

            if (
                !TerrainAnalysisDependencyUtility
                    .TryGetDependencyRadiusMeters(
                        layer.Key,
                        layer.SampleSpacing,
                        out float dependencyRadiusMeters
                    )
            )
            {
                layer.SetGenerationError(
                    "No dependency radius is defined for terrain analysis " +
                    layer.Key +
                    "."
                );

                continue;
            }

            affectedTileSet.Clear();

            TerrainAnalysisInvalidationUtility
                .CollectAffectedTiles(
                    layer,
                    changedSourceTiles,
                    dependencyRadiusMeters,
                    affectedTileSet
                );

            if (affectedTileSet.Count == 0)
            {
                continue;
            }

            affectedTiles.Clear();

            foreach (
                Vector2Int tile
                in affectedTileSet
            )
            {
                affectedTiles.Add(
                    tile
                );
            }

            if (
                generator.TryUpdateTiles(
                    layer,
                    affectedTiles,
                    out string sourceSignature,
                    out string errorMessage
                )
            )
            {
                layer.MarkIncrementalUpdate(
                    sourceSignature,
                    NextGenerationRevision()
                );

                continue;
            }

            /*
             * Leave the old GPU texture allocated, but mark the layer stale.
             * Its next persistent/transient request falls back to complete
             * generation.
             */
            layer.SetGenerationError(
                string.IsNullOrEmpty(errorMessage)
                    ? "Incremental terrain analysis update failed."
                    : errorMessage
            );
        }
    }

    public static bool TryGetLayer(
        TerrainAnalysisKey key,
        out TerrainAnalysisLayer layer
    )
    {
        return Cache.TryGetLayer(
            key,
            out layer
        );
    }

    public static bool ReleaseLayer(
        TerrainAnalysisKey key
    )
    {
        return Cache.RemoveLayer(key);
    }

    public static void InvalidateAll()
    {
        Cache.InvalidateAll();

        foreach (
            TerrainAnalysisLayer layer
            in TransientLayers.Values
        )
        {
            if (layer != null)
            {
                layer.Invalidate();
            }
        }
    }

    public static void Clear()
    {
        Cache.Clear();

        foreach (
            TerrainAnalysisLayer layer
            in TransientLayers.Values
        )
        {
            if (layer != null)
            {
                layer.Reset();
            }
        }

        TransientLayers.Clear();
    }

    public static void RegisterGenerator(
        ITerrainAnalysisGenerator newGenerator
    )
    {
        if (
            ReferenceEquals(
                generator,
                newGenerator
            )
        )
        {
            return;
        }

        generator = newGenerator;
        InvalidateAll();
    }

    public static void UnregisterGenerator(
        ITerrainAnalysisGenerator existingGenerator
    )
    {
        if (
            !ReferenceEquals(
                generator,
                existingGenerator
            )
        )
        {
            return;
        }

        generator = null;
        InvalidateAll();
    }

    private static void EnsureLayerGenerated(
        TerrainAnalysisLayer layer
    )
    {
        if (
            layer == null ||
            layer.IsReady ||
            generator == null
        )
        {
            return;
        }

        if (
            generator.TryGenerate(
                layer.Key,
                out TerrainAnalysisGenerationResult result,
                out string errorMessage
            )
            &&
            result.IsValid
        )
        {
            layer.SetResult(
                result,
                NextGenerationRevision()
            );

            return;
        }

        layer.SetGenerationError(
            string.IsNullOrEmpty(errorMessage)
                ? "Terrain analysis generation failed."
                : errorMessage
        );
    }

    private static int NextGenerationRevision()
    {
        generationRevision++;

        if (generationRevision <= 0)
        {
            generationRevision = 1;
        }

        return generationRevision;
    }
}
