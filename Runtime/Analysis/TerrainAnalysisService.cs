using System.Collections.Generic;
using UnityEngine;

public static class TerrainAnalysisService
{
    private static readonly TerrainAnalysisCache Cache =
        new TerrainAnalysisCache();

    private static ITerrainAnalysisGenerator generator;
    private static int generationRevision;

    public static int ActiveLayerCount => Cache.Count;
    public static bool GeneratorAvailable => generator != null;

    public static TerrainAnalysisLayer RequestLayer(
        TerrainAnalysisKey key
    )
    {
        TerrainAnalysisLayer layer =
            Cache.GetOrCreateLayer(key);

        if (
            layer.IsReady ||
            generator == null
        )
        {
            return layer;
        }

        if (
            generator.TryGenerate(
                key,
                out TerrainAnalysisGenerationResult result,
                out string errorMessage
            ) &&
            result.IsValid
        )
        {
            layer.SetResult(
                result,
                NextGenerationRevision()
            );

            return layer;
        }

        layer.SetGenerationError(
            string.IsNullOrEmpty(errorMessage)
                ? "Terrain analysis generation failed."
                : errorMessage
        );

        return layer;
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
     * Each ready requested analysis layer expands that source set by its own
     * dependency radius and regenerates only the affected analysis slices.
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
            /*
             * We know the source changed but cannot update cached analysis.
             * Keep correctness by forcing full generation if a backend is
             * registered and the layer is requested later.
             */
            InvalidateAll();
            return;
        }

        List<TerrainAnalysisLayer> activeLayers =
            new List<TerrainAnalysisLayer>(
                Cache.Count
            );

        Cache.CopyLayers(
            activeLayers
        );

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
             * The next RequestLayer() will perform a complete regeneration.
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
    }

    public static void Clear()
    {
        Cache.Clear();
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
