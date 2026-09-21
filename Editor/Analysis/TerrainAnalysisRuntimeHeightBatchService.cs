using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Package 07 bounded whole-world Terrain Analysis path.
 *
 * Runtime surface generation consumes the generated runtime height dataset,
 * not the transient Scene View preview. One request loads only a small
 * contiguous output run plus the dependency halo required by its analysis
 * keys. Temporary GPU resources are destroyed when all readbacks complete.
 */
public static class TerrainAnalysisRuntimeHeightBatchService
{
    public const int MaximumOutputTilesPerBatch = 8;

    public static int LastOutputTileCount { get; private set; }
    public static int LastSourceTileCount { get; private set; }
    public static int LastGuardTileCount { get; private set; }

    public static bool TryValidateSource(
        TerrainHeightmapManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (manifest == null)
        {
            errorMessage =
                "The runtime heightmap manifest is unavailable.";

            return false;
        }

        if (
            !manifest.isComplete
            ||
            manifest.heightTileGridWidth <= 0
            ||
            manifest.heightTileGridHeight <= 0
            ||
            manifest.heightTileSamplesPerSide <= 1
            ||
            manifest.HeightSampleSpacing <= 0f
            ||
            manifest.WorldSizeX <= 0f
            ||
            manifest.WorldSizeZ <= 0f
            ||
            string.IsNullOrEmpty(
                manifest.sourceAuthoringSignature
            )
        )
        {
            errorMessage =
                "The generated runtime height dataset is incomplete or has an invalid analysis layout.";

            return false;
        }

        return true;
    }

    /*
     * Returns a same-row, consecutive run starting at startIndex. This policy
     * prevents sparse incremental surface requests from being merged into an
     * enormous temporary source rectangle.
     */
    internal static List<Vector2Int> CollectContiguousRun(
        IReadOnlyList<Vector2Int> sortedCoordinates,
        int startIndex,
        int maximumCount
    )
    {
        List<Vector2Int> result =
            new List<Vector2Int>();

        if (
            sortedCoordinates == null
            ||
            startIndex < 0
            ||
            startIndex >= sortedCoordinates.Count
            ||
            maximumCount <= 0
        )
        {
            return result;
        }

        int safeMaximum =
            Mathf.Min(
                MaximumOutputTilesPerBatch,
                maximumCount
            );

        Vector2Int first =
            sortedCoordinates[startIndex];

        result.Add(first);

        Vector2Int previous = first;

        for (
            int index = startIndex + 1;
            index < sortedCoordinates.Count &&
                result.Count < safeMaximum;
            index++
        )
        {
            Vector2Int candidate =
                sortedCoordinates[index];

            if (
                candidate.y != first.y
                ||
                candidate.x != previous.x + 1
            )
            {
                break;
            }

            result.Add(candidate);
            previous = candidate;
        }

        return result;
    }

    public static void RequestBatch(
        TerrainHeightmapManifest manifest,
        IReadOnlyList<Vector2Int> outputTiles,
        IReadOnlyList<TerrainAnalysisKey> keys,
        Action<
            TerrainAnalysisRuntimeHeightBatchResult,
            string
        > callback
    )
    {
        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            callback?.Invoke(
                null,
                "Runtime-height Terrain Analysis is unavailable while entering or running Play Mode."
            );

            return;
        }

        if (!TryValidateSource(manifest, out string sourceError))
        {
            callback?.Invoke(null, sourceError);
            return;
        }

        if (
            !TryBuildOutputWindow(
                manifest,
                outputTiles,
                out TerrainHeightCacheWindow outputWindow,
                out string outputError
            )
        )
        {
            callback?.Invoke(null, outputError);
            return;
        }

        if (
            !TryCollectKeys(
                keys,
                manifest.HeightSampleSpacing,
                out List<TerrainAnalysisKey> uniqueKeys,
                out float maximumDependencyRadius,
                out string keyError
            )
        )
        {
            callback?.Invoke(null, keyError);
            return;
        }

        int guardTileCount =
            TerrainAnalysisWindowUtility
                .CalculateRequiredGuardTileCount(
                    manifest.heightTileSamplesPerSide,
                    manifest.HeightSampleSpacing,
                    maximumDependencyRadius
                );

        Vector2Int worldGridSize =
            new Vector2Int(
                manifest.heightTileGridWidth,
                manifest.heightTileGridHeight
            );

        if (
            !TerrainAnalysisWindowUtility
                .TryExpandOutputWindow(
                    outputWindow,
                    worldGridSize,
                    guardTileCount,
                    out TerrainHeightCacheWindow sourceWindow,
                    out string windowError
                )
        )
        {
            callback?.Invoke(null, windowError);
            return;
        }

        LastOutputTileCount = outputWindow.TileCount;
        LastSourceTileCount = sourceWindow.TileCount;
        LastGuardTileCount = guardTileCount;

        RenderTexture sourceTexture =
            null;

        List<TerrainAnalysisLayer> layers =
            new List<TerrainAnalysisLayer>();

        try
        {
            if (
                !TryCreateRuntimeHeightSource(
                    manifest,
                    sourceWindow,
                    out sourceTexture,
                    out string loadError
                )
            )
            {
                ReleaseSource(sourceTexture);
                callback?.Invoke(null, loadError);
                return;
            }

            string sourceSignature =
                BuildRuntimeSourceSignature(
                    manifest,
                    sourceWindow
                );

            TerrainAnalysisGpuSource source =
                new TerrainAnalysisGpuSource(
                    sourceTexture,
                    sourceWindow,
                    worldGridSize,
                    manifest.heightTileSamplesPerSide,
                    manifest.HeightSampleSpacing,
                    manifest.WorldSizeXZ,
                    sourceSignature,
                    sourceTexture.GetInstanceID(),
                    0L,
                    0L
                );

            for (
                int index = 0;
                index < uniqueKeys.Count;
                index++
            )
            {
                TerrainAnalysisKey key =
                    uniqueKeys[index];

                if (
                    !TerrainAnalysisGpuGenerator
                        .TryGenerateFromSource(
                            key,
                            source,
                            outputWindow,
                            out TerrainAnalysisGenerationResult generation,
                            out string generationError
                        )
                )
                {
                    ReleaseLayers(layers);
                    ReleaseSource(sourceTexture);
                    callback?.Invoke(null, generationError);
                    return;
                }

                TerrainAnalysisLayer layer =
                    new TerrainAnalysisLayer(key);

                layer.SetResult(
                    generation,
                    index + 1
                );

                if (!layer.IsReady)
                {
                    layer.Reset();
                    ReleaseLayers(layers);
                    ReleaseSource(sourceTexture);
                    callback?.Invoke(
                        null,
                        "A bounded runtime-height Terrain Analysis layer could not be prepared."
                    );

                    return;
                }

                layers.Add(layer);
            }

            BatchRequest request =
                new BatchRequest(
                    outputWindow,
                    sourceTexture,
                    layers,
                    callback
                );

            request.Begin();
        }
        catch (Exception exception)
        {
            ReleaseLayers(layers);
            ReleaseSource(sourceTexture);

            callback?.Invoke(
                null,
                "Bounded runtime-height Terrain Analysis failed.\n\n" +
                exception.Message
            );
        }
    }

    private static bool TryBuildOutputWindow(
        TerrainHeightmapManifest manifest,
        IReadOnlyList<Vector2Int> outputTiles,
        out TerrainHeightCacheWindow outputWindow,
        out string errorMessage
    )
    {
        outputWindow = default;
        errorMessage = "";

        if (
            outputTiles == null
            ||
            outputTiles.Count == 0
            ||
            outputTiles.Count > MaximumOutputTilesPerBatch
        )
        {
            errorMessage =
                "A runtime-height Terrain Analysis batch must contain between 1 and " +
                MaximumOutputTilesPerBatch +
                " output tiles.";

            return false;
        }

        Vector2Int first = outputTiles[0];
        Vector2Int previous = first;

        if (!CoordinateIsValid(manifest, first))
        {
            errorMessage =
                "Runtime-height Terrain Analysis received an invalid output tile: " +
                first;

            return false;
        }

        for (
            int index = 1;
            index < outputTiles.Count;
            index++
        )
        {
            Vector2Int coordinate =
                outputTiles[index];

            if (
                !CoordinateIsValid(manifest, coordinate)
                ||
                coordinate.y != first.y
                ||
                coordinate.x != previous.x + 1
            )
            {
                errorMessage =
                    "Runtime-height Terrain Analysis batches must be consecutive tiles on one row.";

                return false;
            }

            previous = coordinate;
        }

        outputWindow =
            new TerrainHeightCacheWindow(
                first,
                new Vector2Int(
                    outputTiles.Count,
                    1
                )
            );

        return outputWindow.IsValid;
    }

    private static bool TryCollectKeys(
        IReadOnlyList<TerrainAnalysisKey> keys,
        float sampleSpacing,
        out List<TerrainAnalysisKey> uniqueKeys,
        out float maximumDependencyRadius,
        out string errorMessage
    )
    {
        uniqueKeys =
            new List<TerrainAnalysisKey>();

        maximumDependencyRadius = 0f;
        errorMessage = "";

        if (keys == null || keys.Count == 0)
        {
            errorMessage =
                "No Terrain Analysis fields were requested for the runtime-height batch.";

            return false;
        }

        HashSet<TerrainAnalysisKey> seen =
            new HashSet<TerrainAnalysisKey>();

        for (
            int index = 0;
            index < keys.Count;
            index++
        )
        {
            TerrainAnalysisKey key = keys[index];

            if (!seen.Add(key))
            {
                continue;
            }

            if (
                !TerrainAnalysisRegistry
                    .TryValidateKey(
                        key,
                        out _,
                        out errorMessage
                    )
                ||
                !TerrainAnalysisDependencyUtility
                    .TryGetDependencyRadiusMeters(
                        key,
                        sampleSpacing,
                        out float radius
                    )
            )
            {
                if (string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage =
                        "Could not determine the dependency radius for Terrain Analysis " +
                        key +
                        ".";
                }

                return false;
            }

            maximumDependencyRadius =
                Mathf.Max(
                    maximumDependencyRadius,
                    radius
                );

            uniqueKeys.Add(key);
        }

        return uniqueKeys.Count > 0;
    }

    private static bool TryCreateRuntimeHeightSource(
        TerrainHeightmapManifest manifest,
        TerrainHeightCacheWindow sourceWindow,
        out RenderTexture sourceTexture,
        out string errorMessage
    )
    {
        sourceTexture = null;
        errorMessage = "";

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support 2D texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The current graphics device does not support RFloat render textures required by generated runtime heights.";

            return false;
        }

        RenderTextureDescriptor descriptor =
            new RenderTextureDescriptor(
                manifest.heightTileSamplesPerSide,
                manifest.heightTileSamplesPerSide,
                RenderTextureFormat.RFloat,
                0
            );

        descriptor.dimension = TextureDimension.Tex2DArray;
        descriptor.volumeDepth = sourceWindow.TileCount;
        descriptor.msaaSamples = 1;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.enableRandomWrite = false;
        descriptor.sRGB = false;

        sourceTexture = new RenderTexture(descriptor);
        sourceTexture.name =
            "WorldMeshes Runtime Height Analysis Batch";
        sourceTexture.filterMode = FilterMode.Point;
        sourceTexture.wrapMode = TextureWrapMode.Clamp;
        sourceTexture.hideFlags = HideFlags.HideAndDontSave;

        if (!sourceTexture.Create())
        {
            errorMessage =
                "Could not create the bounded runtime-height GPU source.";

            ReleaseSource(sourceTexture);
            sourceTexture = null;
            return false;
        }

        for (
            int localZ = 0;
            localZ < sourceWindow.Height;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < sourceWindow.Width;
                localX++
            )
            {
                Vector2Int coordinate =
                    sourceWindow.OriginTile +
                    new Vector2Int(
                        localX,
                        localZ
                    );

                string path =
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTilePath(
                            coordinate.x,
                            coordinate.y
                        );

                Texture2D sourceTile =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            path
                        );

                if (
                    sourceTile == null
                    ||
                    sourceTile.width != manifest.heightTileSamplesPerSide
                    ||
                    sourceTile.height != manifest.heightTileSamplesPerSide
                    ||
                    sourceTile.format != TextureFormat.RFloat
                )
                {
                    errorMessage =
                        "The generated runtime height tile is missing or incompatible:\n" +
                        path;

                    ReleaseSource(sourceTexture);
                    sourceTexture = null;
                    return false;
                }

                int slice =
                    localX +
                    localZ *
                    sourceWindow.Width;

                try
                {
                    Graphics.CopyTexture(
                        sourceTile,
                        0,
                        0,
                        sourceTexture,
                        slice,
                        0
                    );
                }
                catch (Exception exception)
                {
                    errorMessage =
                        "Could not copy a generated runtime height tile into the bounded analysis source:\n" +
                        path +
                        "\n\n" +
                        exception.Message;

                    ReleaseSource(sourceTexture);
                    sourceTexture = null;
                    return false;
                }
            }
        }

        return true;
    }

    private static string BuildRuntimeSourceSignature(
        TerrainHeightmapManifest manifest,
        TerrainHeightCacheWindow sourceWindow
    )
    {
        return
            (manifest.sourceAuthoringSignature ?? "") +
            "|hash:" +
            (manifest.sourceAuthoringContentHash ?? "") +
            "|compiler:" +
            manifest.compilerVersion +
            "|origin:" +
            sourceWindow.OriginTile.x +
            "," +
            sourceWindow.OriginTile.y +
            "|size:" +
            sourceWindow.Size.x +
            "," +
            sourceWindow.Size.y;
    }

    private static bool CoordinateIsValid(
        TerrainHeightmapManifest manifest,
        Vector2Int coordinate
    )
    {
        return
            coordinate.x >= 0
            &&
            coordinate.y >= 0
            &&
            coordinate.x < manifest.heightTileGridWidth
            &&
            coordinate.y < manifest.heightTileGridHeight;
    }

    private static void ReleaseLayers(
        List<TerrainAnalysisLayer> layers
    )
    {
        if (layers == null)
        {
            return;
        }

        for (
            int index = 0;
            index < layers.Count;
            index++
        )
        {
            layers[index]?.Reset();
        }

        layers.Clear();
    }

    private static void ReleaseSource(
        RenderTexture texture
    )
    {
        if (texture == null)
        {
            return;
        }

        if (texture.IsCreated())
        {
            texture.Release();
        }

        UnityEngine.Object.DestroyImmediate(texture);
    }

    private sealed class BatchRequest
    {
        private readonly TerrainHeightCacheWindow outputWindow;
        private readonly RenderTexture sourceTexture;
        private readonly List<TerrainAnalysisLayer> layers;
        private readonly Action<
            TerrainAnalysisRuntimeHeightBatchResult,
            string
        > callback;

        private readonly Dictionary<
            TerrainAnalysisKey,
            IReadOnlyList<TerrainAnalysisTileData>
        > dataByKey =
            new Dictionary<
                TerrainAnalysisKey,
                IReadOnlyList<TerrainAnalysisTileData>
            >();

        private int remaining;
        private string firstError;
        private bool completed;

        public BatchRequest(
            TerrainHeightCacheWindow outputWindow,
            RenderTexture sourceTexture,
            List<TerrainAnalysisLayer> layers,
            Action<
                TerrainAnalysisRuntimeHeightBatchResult,
                string
            > callback
        )
        {
            this.outputWindow = outputWindow;
            this.sourceTexture = sourceTexture;
            this.layers = layers;
            this.callback = callback;
            remaining = layers != null ? layers.Count : 0;
        }

        public void Begin()
        {
            if (
                layers == null
                ||
                layers.Count == 0
            )
            {
                Complete(
                    null,
                    "No bounded Terrain Analysis layers were generated."
                );

                return;
            }

            for (
                int index = 0;
                index < layers.Count;
                index++
            )
            {
                TerrainAnalysisLayer layer = layers[index];

                try
                {
                    AsyncGPUReadback.Request(
                        layer.Texture,
                        0,
                        0,
                        layer.SamplesPerSide,
                        0,
                        layer.SamplesPerSide,
                        0,
                        layer.SliceCount,
                        TextureFormat.RHalf,
                        request =>
                        {
                            CompleteLayerReadback(
                                layer,
                                request
                            );
                        }
                    );
                }
                catch (Exception exception)
                {
                    CaptureError(
                        "Could not queue bounded Terrain Analysis GPU readback.\n\n" +
                        exception.Message
                    );

                    remaining--;
                }
            }

            TryComplete();
        }

        private void CompleteLayerReadback(
            TerrainAnalysisLayer layer,
            AsyncGPUReadbackRequest request
        )
        {
            if (completed)
            {
                return;
            }

            if (request.hasError)
            {
                CaptureError(
                    "Bounded Terrain Analysis GPU readback failed for " +
                    layer.Key +
                    "."
                );
            }
            else if (
                request.layerCount !=
                    layer.SliceCount
            )
            {
                CaptureError(
                    "Bounded Terrain Analysis readback returned an unexpected layer count for " +
                    layer.Key +
                    "."
                );
            }
            else
            {
                List<TerrainAnalysisTileData> data =
                    new List<TerrainAnalysisTileData>(
                        layer.SliceCount
                    );

                int valuesPerTile =
                    layer.SamplesPerSide *
                    layer.SamplesPerSide;

                for (
                    int slice = 0;
                    slice < layer.SliceCount;
                    slice++
                )
                {
                    var raw =
                        request.GetData<ushort>(
                            slice
                        );

                    if (raw.Length != valuesPerTile)
                    {
                        CaptureError(
                            "Bounded Terrain Analysis readback returned an unexpected sample count for " +
                            layer.Key +
                            "."
                        );

                        break;
                    }

                    float[] values =
                        new float[valuesPerTile];

                    for (
                        int valueIndex = 0;
                        valueIndex < valuesPerTile;
                        valueIndex++
                    )
                    {
                        values[valueIndex] =
                            HalfToSingle(
                                raw[valueIndex]
                            );
                    }

                    int localX =
                        slice % outputWindow.Width;

                    int localZ =
                        slice / outputWindow.Width;

                    Vector2Int tileCoordinate =
                        outputWindow.OriginTile +
                        new Vector2Int(
                            localX,
                            localZ
                        );

                    TerrainAnalysisTile tile =
                        new TerrainAnalysisTile(
                            layer,
                            tileCoordinate,
                            new Vector2Int(
                                localX,
                                localZ
                            ),
                            slice
                        );

                    data.Add(
                        new TerrainAnalysisTileData(
                            tile,
                            values
                        )
                    );
                }

                if (string.IsNullOrEmpty(firstError))
                {
                    dataByKey[layer.Key] = data;
                }
            }

            remaining--;
            TryComplete();
        }

        private void CaptureError(
            string error
        )
        {
            if (
                string.IsNullOrEmpty(firstError)
                &&
                !string.IsNullOrEmpty(error)
            )
            {
                firstError = error;
            }
        }

        private void TryComplete()
        {
            if (
                completed
                ||
                remaining > 0
            )
            {
                return;
            }

            TerrainAnalysisRuntimeHeightBatchResult result =
                string.IsNullOrEmpty(firstError)
                    ? new TerrainAnalysisRuntimeHeightBatchResult(
                        outputWindow,
                        dataByKey
                    )
                    : null;

            Complete(
                result,
                firstError ?? ""
            );
        }

        private void Complete(
            TerrainAnalysisRuntimeHeightBatchResult result,
            string error
        )
        {
            if (completed)
            {
                return;
            }

            completed = true;

            ReleaseLayers(layers);
            ReleaseSource(sourceTexture);

            try
            {
                callback?.Invoke(
                    result,
                    error ?? ""
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    /*
     * Convert IEEE-754 binary16 values returned by RHalf readback without a
     * dependency on System.Half or Unity.Mathematics.
     */
    private static float HalfToSingle(
        ushort half
    )
    {
        uint sign =
            (uint)(half & 0x8000) << 16;

        uint exponent =
            (uint)(half >> 10) & 0x1F;

        uint mantissa =
            (uint)(half & 0x03FF);

        uint bits;

        if (exponent == 0)
        {
            if (mantissa == 0)
            {
                bits = sign;
            }
            else
            {
                int shift = -1;

                do
                {
                    shift++;
                    mantissa <<= 1;
                }
                while ((mantissa & 0x0400) == 0);

                mantissa &= 0x03FF;

                uint floatExponent =
                    (uint)(
                        127 -
                        15 -
                        shift
                    );

                bits =
                    sign |
                    (floatExponent << 23) |
                    (mantissa << 13);
            }
        }
        else if (exponent == 31)
        {
            bits =
                sign |
                0x7F800000u |
                (mantissa << 13);
        }
        else
        {
            uint floatExponent =
                exponent +
                (127 - 15);

            bits =
                sign |
                (floatExponent << 23) |
                (mantissa << 13);
        }

        FloatUIntUnion converter =
            new FloatUIntUnion
            {
                UIntValue = bits
            };

        return converter.FloatValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatUIntUnion
    {
        [FieldOffset(0)]
        public float FloatValue;

        [FieldOffset(0)]
        public uint UIntValue;
    }
}

public sealed class TerrainAnalysisRuntimeHeightBatchResult
{
    private readonly Dictionary<
        TerrainAnalysisKey,
        IReadOnlyList<TerrainAnalysisTileData>
    > dataByKey;

    public TerrainHeightCacheWindow OutputWindow { get; }

    internal TerrainAnalysisRuntimeHeightBatchResult(
        TerrainHeightCacheWindow outputWindow,
        Dictionary<
            TerrainAnalysisKey,
            IReadOnlyList<TerrainAnalysisTileData>
        > dataByKey
    )
    {
        OutputWindow = outputWindow;

        this.dataByKey =
            dataByKey != null
                ? new Dictionary<
                    TerrainAnalysisKey,
                    IReadOnlyList<TerrainAnalysisTileData>
                >(dataByKey)
                : new Dictionary<
                    TerrainAnalysisKey,
                    IReadOnlyList<TerrainAnalysisTileData>
                >();
    }

    public bool TryGetTiles(
        TerrainAnalysisKey key,
        out IReadOnlyList<TerrainAnalysisTileData> data
    )
    {
        return dataByKey.TryGetValue(key, out data);
    }
}
