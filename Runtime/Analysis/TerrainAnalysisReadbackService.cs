using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Production asynchronous CPU access for Terrain Analysis.
 *
 * Important rule:
 * GPU readback happens per requested tile/range, never per sampled point.
 * CPU tools should request the tiles they need once, then perform all point
 * sampling against TerrainAnalysisTileData.
 */
public static class TerrainAnalysisReadbackService
{
    private readonly struct ReadbackCacheKey :
        IEquatable<ReadbackCacheKey>
    {
        public readonly TerrainAnalysisKey AnalysisKey;
        public readonly Vector2Int TileCoordinate;
        public readonly int Revision;
        public readonly int TextureInstanceId;

        public ReadbackCacheKey(
            TerrainAnalysisTile tile
        )
        {
            AnalysisKey = tile.Key;
            TileCoordinate = tile.TileCoordinate;
            Revision = tile.Revision;
            TextureInstanceId =
                tile.TextureInstanceId;
        }

        public bool Equals(
            ReadbackCacheKey other
        )
        {
            return
                AnalysisKey.Equals(
                    other.AnalysisKey
                ) &&
                TileCoordinate ==
                    other.TileCoordinate &&
                Revision ==
                    other.Revision &&
                TextureInstanceId ==
                    other.TextureInstanceId;
        }

        public override bool Equals(
            object obj
        )
        {
            return
                obj is ReadbackCacheKey other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash =
                    AnalysisKey.GetHashCode();

                hash =
                    hash * 397 ^
                    TileCoordinate.GetHashCode();

                hash =
                    hash * 397 ^
                    Revision;

                hash =
                    hash * 397 ^
                    TextureInstanceId;

                return hash;
            }
        }
    }

    private sealed class BatchRequest
    {
        private readonly Action<
            IReadOnlyList<TerrainAnalysisTileData>,
            string
        > callback;

        private readonly List<
            TerrainAnalysisTileData
        > results;

        private int remaining;
        private string firstError;

        public BatchRequest(
            int count,
            Action<
                IReadOnlyList<TerrainAnalysisTileData>,
                string
            > callback
        )
        {
            remaining =
                Mathf.Max(
                    0,
                    count
                );

            this.callback =
                callback;

            results =
                new List<
                    TerrainAnalysisTileData
                >(
                    count
                );
        }

        public void CompleteOne(
            TerrainAnalysisTileData data,
            string error
        )
        {
            if (data != null)
            {
                results.Add(
                    data
                );
            }

            if (
                string.IsNullOrEmpty(
                    firstError
                ) &&
                !string.IsNullOrEmpty(
                    error
                )
            )
            {
                firstError =
                    error;
            }

            remaining--;

            if (remaining > 0)
            {
                return;
            }

            callback?.Invoke(
                results,
                firstError ?? ""
            );
        }
    }

    private static readonly Dictionary<
        ReadbackCacheKey,
        TerrainAnalysisTileData
    > Cache =
        new Dictionary<
            ReadbackCacheKey,
            TerrainAnalysisTileData
        >();

    private static readonly Dictionary<
        ReadbackCacheKey,
        List<
            Action<
                TerrainAnalysisTileData,
                string
            >
        >
    > InFlight =
        new Dictionary<
            ReadbackCacheKey,
            List<
                Action<
                    TerrainAnalysisTileData,
                    string
                >
            >
        >();

    public static int CachedTileCount =>
        Cache.Count;

    public static int InFlightTileCount =>
        InFlight.Count;

    public static void RequestTile(
        TerrainAnalysisKey key,
        Vector2Int tileCoordinate,
        Action<
            TerrainAnalysisTileData,
            string
        > callback
    )
    {
        TerrainAnalysisLayer layer =
            TerrainAnalysisService
                .RequestLayer(
                    key
                );

        if (
            layer == null ||
            !layer.IsReady
        )
        {
            callback?.Invoke(
                null,
                layer != null
                    ? layer.ErrorMessage
                    : "Terrain analysis layer is unavailable."
            );

            return;
        }

        if (
            !TerrainAnalysisAddressingUtility
                .TryGetTile(
                    layer,
                    tileCoordinate,
                    out TerrainAnalysisTile tile
                )
        )
        {
            callback?.Invoke(
                null,
                "The requested terrain analysis tile is outside the active analysis cache."
            );

            return;
        }

        RequestResolvedTile(
            tile,
            callback
        );
    }

    public static void RequestTiles(
        TerrainAnalysisKey key,
        IReadOnlyList<Vector2Int> tileCoordinates,
        Action<
            IReadOnlyList<TerrainAnalysisTileData>,
            string
        > callback
    )
    {
        if (
            tileCoordinates == null ||
            tileCoordinates.Count == 0
        )
        {
            callback?.Invoke(
                Array.Empty<
                    TerrainAnalysisTileData
                >(),
                ""
            );

            return;
        }

        TerrainAnalysisLayer layer =
            TerrainAnalysisService
                .RequestLayer(
                    key
                );

        if (
            layer == null ||
            !layer.IsReady
        )
        {
            callback?.Invoke(
                Array.Empty<
                    TerrainAnalysisTileData
                >(),
                layer != null
                    ? layer.ErrorMessage
                    : "Terrain analysis layer is unavailable."
            );

            return;
        }

        HashSet<Vector2Int> uniqueCoordinates =
            new HashSet<Vector2Int>();

        List<TerrainAnalysisTile> resolvedTiles =
            new List<TerrainAnalysisTile>();

        for (
            int index = 0;
            index < tileCoordinates.Count;
            index++
        )
        {
            Vector2Int coordinate =
                tileCoordinates[index];

            if (
                !uniqueCoordinates.Add(
                    coordinate
                )
            )
            {
                continue;
            }

            if (
                !TerrainAnalysisAddressingUtility
                    .TryGetTile(
                        layer,
                        coordinate,
                        out TerrainAnalysisTile tile
                    )
            )
            {
                callback?.Invoke(
                    Array.Empty<
                        TerrainAnalysisTileData
                    >(),
                    "One or more requested terrain analysis tiles are outside the active analysis cache."
                );

                return;
            }

            resolvedTiles.Add(
                tile
            );
        }

        BatchRequest batch =
            new BatchRequest(
                resolvedTiles.Count,
                callback
            );

        List<TerrainAnalysisTile> newGpuRequests =
            new List<TerrainAnalysisTile>();

        for (
            int index = 0;
            index < resolvedTiles.Count;
            index++
        )
        {
            TerrainAnalysisTile tile =
                resolvedTiles[index];

            bool needsGpuRequest =
                PrepareResolvedTileRequest(
                    tile,
                    batch.CompleteOne
                );

            if (needsGpuRequest)
            {
                newGpuRequests.Add(
                    tile
                );
            }
        }

        if (newGpuRequests.Count > 0)
        {
            IssueGroupedReadbacks(
                newGpuRequests
            );
        }
    }

    public static bool TryGetCachedTile(
        TerrainAnalysisKey key,
        Vector2Int tileCoordinate,
        out TerrainAnalysisTileData data
    )
    {
        data =
            null;

        if (
            !TerrainAnalysisService.TryGetLayer(
                key,
                out TerrainAnalysisLayer layer
            ) ||
            layer == null ||
            !layer.IsReady ||
            !TerrainAnalysisAddressingUtility
                .TryGetTile(
                    layer,
                    tileCoordinate,
                    out TerrainAnalysisTile tile
                )
        )
        {
            return false;
        }

        PruneOlderCachedRevisions(
            key,
            layer.Revision,
            tile.TextureInstanceId
        );

        return
            Cache.TryGetValue(
                new ReadbackCacheKey(
                    tile
                ),
                out data
            );
    }

    public static void Clear()
    {
        Cache.Clear();

        /*
         * AsyncGPUReadback cannot be cancelled here. Existing requests may
         * still complete, but their callbacks remain associated with their
         * original revision/texture snapshot.
         */
    }

    private static void RequestResolvedTile(
        TerrainAnalysisTile tile,
        Action<
            TerrainAnalysisTileData,
            string
        > callback
    )
    {
        bool needsGpuRequest =
            PrepareResolvedTileRequest(
                tile,
                callback
            );

        if (!needsGpuRequest)
        {
            return;
        }

        IssueGroupedReadbacks(
            new List<TerrainAnalysisTile>
            {
                tile
            }
        );
    }

    private static bool PrepareResolvedTileRequest(
        TerrainAnalysisTile tile,
        Action<
            TerrainAnalysisTileData,
            string
        > callback
    )
    {
        ReadbackCacheKey cacheKey =
            new ReadbackCacheKey(
                tile
            );

        PruneOlderCachedRevisions(
            tile.Key,
            tile.Revision,
            tile.TextureInstanceId
        );

        if (
            Cache.TryGetValue(
                cacheKey,
                out TerrainAnalysisTileData cached
            )
        )
        {
            callback?.Invoke(
                cached,
                ""
            );

            return false;
        }

        if (
            InFlight.TryGetValue(
                cacheKey,
                out List<
                    Action<
                        TerrainAnalysisTileData,
                        string
                    >
                > waiters
            )
        )
        {
            waiters.Add(
                callback
            );

            return false;
        }

        InFlight.Add(
            cacheKey,
            new List<
                Action<
                    TerrainAnalysisTileData,
                    string
                >
            >
            {
                callback
            }
        );

        return true;
    }

    private static void IssueGroupedReadbacks(
        List<TerrainAnalysisTile> tiles
    )
    {
        if (
            tiles == null ||
            tiles.Count == 0
        )
        {
            return;
        }

        tiles.Sort(
            (
                left,
                right
            ) =>
                left.SliceIndex.CompareTo(
                    right.SliceIndex
                )
        );

        int index =
            0;

        while (index < tiles.Count)
        {
            int firstIndex =
                index;

            int lastIndex =
                index;

            while (
                lastIndex + 1 <
                    tiles.Count &&
                tiles[
                    lastIndex + 1
                ].SliceIndex ==
                tiles[
                    lastIndex
                ].SliceIndex +
                1
            )
            {
                lastIndex++;
            }

            List<TerrainAnalysisTile> run =
                tiles.GetRange(
                    firstIndex,
                    lastIndex -
                    firstIndex +
                    1
                );

            IssueReadbackRun(
                run
            );

            index =
                lastIndex + 1;
        }
    }

    private static void IssueReadbackRun(
        List<TerrainAnalysisTile> run
    )
    {
        if (
            run == null ||
            run.Count == 0
        )
        {
            return;
        }

        TerrainAnalysisTile first =
            run[0];

        if (
            first.Layer == null ||
            first.Layer.Texture == null ||
            !first.Layer.Texture.IsCreated()
        )
        {
            CompleteRunWithError(
                run,
                "The terrain analysis texture is unavailable for GPU readback."
            );

            return;
        }

        int samples =
            first.SamplesPerSide;

        int firstSlice =
            first.SliceIndex;

        int sliceCount =
            run.Count;

        try
        {
            AsyncGPUReadback.Request(
                first.Layer.Texture,
                0,
                0,
                samples,
                0,
                samples,
                firstSlice,
                sliceCount,
                TextureFormat.RHalf,
                request =>
                {
                    CompleteReadbackRun(
                        run,
                        request
                    );
                }
            );
        }
        catch (Exception exception)
        {
            CompleteRunWithError(
                run,
                "Terrain analysis GPU readback could not be queued.\n\n" +
                exception.Message
            );
        }
    }

    private static void CompleteReadbackRun(
    List<TerrainAnalysisTile> run,
    AsyncGPUReadbackRequest request
)
{
    if (request.hasError)
    {
        CompleteRunWithError(
            run,
            "Terrain analysis GPU readback failed."
        );

        return;
    }

    if (
        run == null ||
        run.Count == 0
    )
    {
        return;
    }

    int samples =
        run[0].SamplesPerSide;

    int valuesPerTile =
        samples *
        samples;

    /*
     * A Texture2DArray readback exposes each requested array
     * slice as a separate readback layer.
     *
     * GetData<T>() without a layer does NOT concatenate every
     * requested Texture2DArray slice.
     */
    if (
        request.layerCount !=
        run.Count
    )
    {
        CompleteRunWithError(
            run,
            "Terrain analysis GPU readback returned an " +
            "unexpected layer count.\n\n" +
            $"Expected: {run.Count}\n" +
            $"Actual: {request.layerCount}"
        );

        return;
    }

    for (
        int tileIndex = 0;
        tileIndex < run.Count;
        tileIndex++
    )
    {
        TerrainAnalysisTile tile =
            run[tileIndex];

        /*
         * tileIndex is relative to this readback request.
         *
         * It is NOT the absolute Texture2DArray slice index.
         */
        var raw =
            request.GetData<ushort>(
                tileIndex
            );

        if (
            raw.Length !=
            valuesPerTile
        )
        {
            CompleteRunWithError(
                run,
                "Terrain analysis GPU readback returned an " +
                "unexpected sample count for one layer.\n\n" +
                $"Tile: {tile.TileCoordinate}\n" +
                $"Readback Layer: {tileIndex}\n" +
                $"Expected: {valuesPerTile}\n" +
                $"Actual: {raw.Length}"
            );

            return;
        }

        float[] values =
            new float[
                valuesPerTile
            ];

        for (
            int valueIndex = 0;
            valueIndex < valuesPerTile;
            valueIndex++
        )
        {
            values[valueIndex] =
                HalfToSingle(
                    raw[
                        valueIndex
                    ]
                );
        }

        TerrainAnalysisTileData data =
            new TerrainAnalysisTileData(
                tile,
                values
            );

        ReadbackCacheKey key =
            new ReadbackCacheKey(
                tile
            );

        Cache[key] =
            data;

        CompleteInFlight(
            key,
            data,
            ""
        );
    }
}

    private static void CompleteRunWithError(
        List<TerrainAnalysisTile> run,
        string error
    )
    {
        if (run == null)
        {
            return;
        }

        for (
            int index = 0;
            index < run.Count;
            index++
        )
        {
            TerrainAnalysisTile tile =
                run[index];

            CompleteInFlight(
                new ReadbackCacheKey(
                    tile
                ),
                null,
                error
            );
        }
    }

    private static void CompleteInFlight(
        ReadbackCacheKey key,
        TerrainAnalysisTileData data,
        string error
    )
    {
        if (
            !InFlight.TryGetValue(
                key,
                out List<
                    Action<
                        TerrainAnalysisTileData,
                        string
                    >
                > waiters
            )
        )
        {
            return;
        }

        InFlight.Remove(
            key
        );

        for (
            int index = 0;
            index < waiters.Count;
            index++
        )
        {
            waiters[index]?.Invoke(
                data,
                error ?? ""
            );
        }
    }

    private static void PruneOlderCachedRevisions(
        TerrainAnalysisKey key,
        int currentRevision,
        int currentTextureInstanceId
    )
    {
        if (Cache.Count == 0)
        {
            return;
        }

        List<ReadbackCacheKey> stale =
            null;

        foreach (
            KeyValuePair<
                ReadbackCacheKey,
                TerrainAnalysisTileData
            > pair
            in Cache
        )
        {
            ReadbackCacheKey cacheKey =
                pair.Key;

            if (
                !cacheKey.AnalysisKey.Equals(
                    key
                )
            )
            {
                continue;
            }

            if (
                cacheKey.Revision ==
                    currentRevision &&
                cacheKey.TextureInstanceId ==
                    currentTextureInstanceId
            )
            {
                continue;
            }

            if (stale == null)
            {
                stale =
                    new List<
                        ReadbackCacheKey
                    >();
            }

            stale.Add(
                cacheKey
            );
        }

        if (stale == null)
        {
            return;
        }

        for (
            int index = 0;
            index < stale.Count;
            index++
        )
        {
            Cache.Remove(
                stale[index]
            );
        }
    }

    /*
     * Convert one IEEE-754 binary16 value into a float without requiring
     * Unity.Mathematics or System.Half.
     */
    private static float HalfToSingle(
        ushort half
    )
    {
        uint sign =
            (uint)(
                half &
                0x8000
            )
            <<
            16;

        uint exponent =
            (uint)(
                half >>
                10
            )
            &
            0x1F;

        uint mantissa =
            (uint)(
                half &
                0x03FF
            );

        uint bits;

        if (exponent == 0)
        {
            if (mantissa == 0)
            {
                bits =
                    sign;
            }
            else
            {
                int shift =
                    -1;

                do
                {
                    shift++;
                    mantissa <<=
                        1;
                }
                while (
                    (
                        mantissa &
                        0x0400
                    )
                    ==
                    0
                );

                mantissa &=
                    0x03FF;

                uint floatExponent =
                    (uint)(
                        127 -
                        15 -
                        shift
                    );

                bits =
                    sign |
                    (
                        floatExponent <<
                        23
                    ) |
                    (
                        mantissa <<
                        13
                    );
            }
        }
        else if (exponent == 31)
        {
            bits =
                sign |
                0x7F800000u |
                (
                    mantissa <<
                    13
                );
        }
        else
        {
            uint floatExponent =
                exponent +
                (
                    127 -
                    15
                );

            bits =
                sign |
                (
                    floatExponent <<
                    23
                ) |
                (
                    mantissa <<
                    13
                );
        }

        FloatUIntUnion converter =
            new FloatUIntUnion
            {
                UIntValue =
                    bits
            };

        return
            converter.FloatValue;
    }

    [StructLayout(
        LayoutKind.Explicit
    )]
    private struct FloatUIntUnion
    {
        [FieldOffset(0)]
        public float FloatValue;

        [FieldOffset(0)]
        public uint UIntValue;
    }
}
