using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Shared multi-analysis CPU access for authoring/generation systems.
 *
 * This utility intentionally batches by analysis field:
 *
 *     RequestTiles(Slope, many tile coordinates)
 *     RequestTiles(Curvature, many tile coordinates)
 *     RequestTiles(Roughness, many tile coordinates)
 *     ...
 *
 * It never performs GPU readback per sampled placement point.
 */
public static class TerrainAnalysisTileSetReadbackService
{
    public static void RequestTileSets(
        IReadOnlyList<TerrainAnalysisKey> keys,
        IReadOnlyList<Vector2Int> tileCoordinates,
        Action<
            IReadOnlyList<TerrainAnalysisTileSet>,
            string
        > callback
    )
    {
        if (
            keys == null
            ||
            keys.Count == 0
        )
        {
            callback?.Invoke(
                Array.Empty<TerrainAnalysisTileSet>(),
                "No terrain analysis keys were requested."
            );

            return;
        }

        if (
            tileCoordinates == null
            ||
            tileCoordinates.Count == 0
        )
        {
            callback?.Invoke(
                Array.Empty<TerrainAnalysisTileSet>(),
                ""
            );

            return;
        }

        List<TerrainAnalysisKey> uniqueKeys =
            new List<TerrainAnalysisKey>();

        HashSet<TerrainAnalysisKey> seenKeys =
            new HashSet<TerrainAnalysisKey>();

        for (
            int keyIndex = 0;
            keyIndex < keys.Count;
            keyIndex++
        )
        {
            TerrainAnalysisKey key =
                keys[keyIndex];

            if (
                !seenKeys.Add(
                    key
                )
            )
            {
                continue;
            }

            if (
                !TerrainAnalysisRegistry
                    .TryValidateKey(
                        key,
                        out _,
                        out string validationError
                    )
            )
            {
                callback?.Invoke(
                    Array.Empty<TerrainAnalysisTileSet>(),
                    validationError
                );

                return;
            }

            uniqueKeys.Add(
                key
            );
        }

        List<Vector2Int> uniqueCoordinates =
            new List<Vector2Int>();

        HashSet<Vector2Int> seenCoordinates =
            new HashSet<Vector2Int>();

        for (
            int tileIndex = 0;
            tileIndex < tileCoordinates.Count;
            tileIndex++
        )
        {
            Vector2Int coordinate =
                tileCoordinates[tileIndex];

            if (
                seenCoordinates.Add(
                    coordinate
                )
            )
            {
                uniqueCoordinates.Add(
                    coordinate
                );
            }
        }

        if (
            uniqueKeys.Count == 0
            ||
            uniqueCoordinates.Count == 0
        )
        {
            callback?.Invoke(
                Array.Empty<TerrainAnalysisTileSet>(),
                ""
            );

            return;
        }

        MultiFieldRequest request =
            new MultiFieldRequest(
                uniqueKeys.Count,
                uniqueCoordinates,
                callback
            );

        foreach (
            TerrainAnalysisKey key
            in uniqueKeys
        )
        {
            TerrainAnalysisReadbackService
                .RequestTiles(
                    key,
                    uniqueCoordinates,
                    (
                        IReadOnlyList<TerrainAnalysisTileData> data,
                        string error
                    ) =>
                    {
                        request.CompleteField(
                            key,
                            data,
                            error
                        );
                    }
                );
        }
    }

    public static void RequestTileSet(
        IReadOnlyList<TerrainAnalysisKey> keys,
        Vector2Int tileCoordinate,
        Action<
            TerrainAnalysisTileSet,
            string
        > callback
    )
    {
        RequestTileSets(
            keys,
            new[]
            {
                tileCoordinate
            },
            (
                IReadOnlyList<TerrainAnalysisTileSet> sets,
                string error
            ) =>
            {
                TerrainAnalysisTileSet set =
                    sets != null
                    &&
                    sets.Count > 0
                        ? sets[0]
                        : null;

                callback?.Invoke(
                    set,
                    error
                );
            }
        );
    }

    private sealed class MultiFieldRequest
    {
        private int remainingFields;

        private readonly List<Vector2Int>
            requestedCoordinates;

        private readonly Dictionary<
            Vector2Int,
            TerrainAnalysisTileSet
        > setsByCoordinate =
            new Dictionary<
                Vector2Int,
                TerrainAnalysisTileSet
            >();

        private readonly Action<
            IReadOnlyList<TerrainAnalysisTileSet>,
            string
        > callback;

        private string firstError;

        public MultiFieldRequest(
            int fieldCount,
            List<Vector2Int> requestedCoordinates,
            Action<
                IReadOnlyList<TerrainAnalysisTileSet>,
                string
            > callback
        )
        {
            remainingFields =
                Mathf.Max(
                    0,
                    fieldCount
                );

            this.requestedCoordinates =
                requestedCoordinates;

            this.callback =
                callback;

            foreach (
                Vector2Int coordinate
                in requestedCoordinates
            )
            {
                setsByCoordinate.Add(
                    coordinate,
                    new TerrainAnalysisTileSet(
                        coordinate
                    )
                );
            }
        }

        public void CompleteField(
            TerrainAnalysisKey key,
            IReadOnlyList<TerrainAnalysisTileData> data,
            string error
        )
        {
            if (
                string.IsNullOrEmpty(
                    firstError
                )
                &&
                !string.IsNullOrEmpty(
                    error
                )
            )
            {
                firstError =
                    error;
            }

            if (data != null)
            {
                for (
                    int index = 0;
                    index < data.Count;
                    index++
                )
                {
                    TerrainAnalysisTileData tile =
                        data[index];

                    if (
                        tile == null
                        ||
                        !tile.Key.Equals(
                            key
                        )
                        ||
                        !setsByCoordinate.TryGetValue(
                            tile.TileCoordinate,
                            out TerrainAnalysisTileSet set
                        )
                    )
                    {
                        continue;
                    }

                    set.Add(
                        tile
                    );
                }
            }

            remainingFields--;

            if (remainingFields > 0)
            {
                return;
            }

            List<TerrainAnalysisTileSet> result =
                new List<TerrainAnalysisTileSet>(
                    requestedCoordinates.Count
                );

            foreach (
                Vector2Int coordinate
                in requestedCoordinates
            )
            {
                result.Add(
                    setsByCoordinate[
                        coordinate
                    ]
                );
            }

            callback?.Invoke(
                result,
                firstError ?? ""
            );
        }
    }
}
