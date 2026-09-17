using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Full-world edit-mode height preview cache.
 *
 * The committed base range metadata is immutable for the lifetime of one
 * committed-cache build. Composite range metadata may change repeatedly while
 * terrain modifiers are edited.
 *
 * Keeping those concerns separate lets ordinary dirty-slice resets restore
 * authoritative committed min/max values without rescanning every RFloat
 * sample in the source Texture2D.
 */
public sealed class TerrainAuthoringPreviewCache :
    IDisposable
{
    // =====================================================
    // GPU CACHE
    // =====================================================

    private RenderTexture heightCache;

    // =====================================================
    // SOURCE SIGNATURES
    // =====================================================

    private string sourceCommittedHeightfieldSignature =
        "";

    private string sourceOverallAuthoringSignature =
        "";

    private string sourceContentHash =
        "";

    // =====================================================
    // LAYOUT STATE
    // =====================================================

    private Vector2Int cacheOriginTile =
        Vector2Int.zero;

    private int cacheWidth;

    private int cacheHeight;

    private int samplesPerSide;

    private float sampleSpacing;

    private Vector2 worldSizeXZ;

    // =====================================================
    // COMMITTED RANGE STATE
    // =====================================================

    /*
     * These arrays describe the immutable committed/base heightfield used to
     * build this cache. They are populated only by TryBuild() and remain
     * unchanged until the committed cache is rebuilt or disposed.
     */
    private float[] committedSliceMinimumHeights;

    private float[] committedSliceMaximumHeights;

    // =====================================================
    // COMPOSITE RANGE STATE
    // =====================================================

    private float[] sliceMinimumHeights;

    private float[] sliceMaximumHeights;

    private bool[] sliceRangeValid;

    private float minimumHeight;

    private float maximumHeight;

    // =====================================================
    // INCREMENTAL UPDATE DIAGNOSTICS
    // =====================================================

    private int lastIncrementalSliceCount;

    private long totalIncrementalSliceUpdates;

    // =====================================================
    // RANGE UPDATE MODEL
    // =====================================================

    internal readonly struct CompositeSliceRangeUpdate
    {
        public readonly Vector2Int TileCoordinate;
        public readonly float MinimumHeight;
        public readonly float MaximumHeight;

        public CompositeSliceRangeUpdate(
            Vector2Int tileCoordinate,
            float minimumHeight,
            float maximumHeight
        )
        {
            TileCoordinate =
                tileCoordinate;

            MinimumHeight =
                minimumHeight;

            MaximumHeight =
                maximumHeight;
        }
    }

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public RenderTexture HeightCache
    {
        get
        {
            return heightCache;
        }
    }

    public string SourceCommittedHeightfieldSignature
    {
        get
        {
            return sourceCommittedHeightfieldSignature;
        }
    }

    public string SourceOverallAuthoringSignature
    {
        get
        {
            return sourceOverallAuthoringSignature;
        }
    }

    public string SourceAuthoringSignature
    {
        get
        {
            return sourceOverallAuthoringSignature;
        }
    }

    public string SourceContentHash
    {
        get
        {
            return sourceContentHash;
        }
    }

    public Vector2Int CacheOriginTile
    {
        get
        {
            return cacheOriginTile;
        }
    }

    public int CacheWidth
    {
        get
        {
            return cacheWidth;
        }
    }

    public int CacheHeight
    {
        get
        {
            return cacheHeight;
        }
    }

    public Vector2Int CacheSize
    {
        get
        {
            return
                new Vector2Int(
                    cacheWidth,
                    cacheHeight
                );
        }
    }

    public int SliceCount
    {
        get
        {
            return
                Mathf.Max(
                    0,
                    cacheWidth
                )
                *
                Mathf.Max(
                    0,
                    cacheHeight
                );
        }
    }

    public int SamplesPerSide
    {
        get
        {
            return samplesPerSide;
        }
    }

    public float SampleSpacing
    {
        get
        {
            return sampleSpacing;
        }
    }

    public Vector2 WorldSizeXZ
    {
        get
        {
            return worldSizeXZ;
        }
    }

    public float MinimumHeight
    {
        get
        {
            return minimumHeight;
        }
    }

    public float MaximumHeight
    {
        get
        {
            return maximumHeight;
        }
    }

    public int LastIncrementalSliceCount
    {
        get
        {
            return lastIncrementalSliceCount;
        }
    }

    public long TotalIncrementalSliceUpdates
    {
        get
        {
            return totalIncrementalSliceUpdates;
        }
    }

    public bool IsReady
    {
        get
        {
            return
                heightCache != null
                &&
                heightCache.IsCreated()
                &&
                cacheWidth > 0
                &&
                cacheHeight > 0
                &&
                samplesPerSide > 1
                &&
                !string.IsNullOrEmpty(
                    sourceCommittedHeightfieldSignature
                )
                &&
                committedSliceMinimumHeights != null
                &&
                committedSliceMaximumHeights != null
                &&
                sliceMinimumHeights != null
                &&
                sliceMaximumHeights != null
                &&
                sliceRangeValid != null
                &&
                committedSliceMinimumHeights.Length ==
                    SliceCount
                &&
                committedSliceMaximumHeights.Length ==
                    SliceCount
                &&
                sliceMinimumHeights.Length ==
                    SliceCount
                &&
                sliceMaximumHeights.Length ==
                    SliceCount
                &&
                sliceRangeValid.Length ==
                    SliceCount;
        }
    }

    public long ApproximateGpuMemoryBytes
    {
        get
        {
            if (
                samplesPerSide <= 0
                ||
                SliceCount <= 0
            )
            {
                return 0L;
            }

            return
                (long)samplesPerSide
                *
                samplesPerSide
                *
                SliceCount
                *
                sizeof(float);
        }
    }

    // =====================================================
    // FULL COMMITTED CACHE BUILD
    // =====================================================

    public bool TryBuild(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        TerrainAuthoringHeightManifest manifest;
        string currentContentHash;
        string validationError;
        bool validationSucceeded;

        using (WorldMeshesProfiler.PreviewValidateCommitted.Auto())
        {
            validationSucceeded =
                TerrainAuthoringStateUtility
                    .TryValidateCommittedHeightfield(
                        worldSettings,
                        authoringData,
                        out manifest,
                        out currentContentHash,
                        out validationError
                    );
        }

        if (!validationSucceeded)
        {
            errorMessage =
                "The editor terrain preview could not validate " +
                "the committed authoring heightfield.\n\n" +
                validationError;

            return false;
        }

        string committedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        if (
            string.IsNullOrEmpty(
                committedSignature
            )
        )
        {
            errorMessage =
                "The committed heightfield signature could not be " +
                "calculated.";

            return false;
        }

        string overallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                overallSignature
            )
        )
        {
            errorMessage =
                "The overall authoring signature could not be " +
                "calculated.";

            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support " +
                "2D texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The current graphics device does not support " +
                "RFloat render textures.";

            return false;
        }

        if (
            SystemInfo.copyTextureSupport ==
            CopyTextureSupport.None
        )
        {
            errorMessage =
                "The current graphics device does not support " +
                "Graphics.CopyTexture.";

            return false;
        }

        int newCacheWidth =
            manifest.heightTileGridWidth;

        int newCacheHeight =
            manifest.heightTileGridHeight;

        int newSamplesPerSide =
            manifest.heightTileSamplesPerSide;

        int newSliceCount =
            newCacheWidth *
            newCacheHeight;

        if (
            newCacheWidth <= 0
            ||
            newCacheHeight <= 0
            ||
            newSamplesPerSide <= 1
            ||
            newSliceCount <= 0
        )
        {
            errorMessage =
                "The committed authoring manifest contains an " +
                "invalid height-tile layout.";

            return false;
        }

        if (
            newSamplesPerSide >
            SystemInfo.maxTextureSize
        )
        {
            errorMessage =
                "The authoring height tile size exceeds the " +
                "maximum texture size supported by the current " +
                "graphics device.\n\n" +
                $"Required: {newSamplesPerSide}\n" +
                $"Maximum: {SystemInfo.maxTextureSize}";

            return false;
        }

        if (
            SystemInfo.maxTextureArraySlices > 0
            &&
            newSliceCount >
                SystemInfo.maxTextureArraySlices
        )
        {
            errorMessage =
                "The full-world editor preview cache requires " +
                "more texture-array slices than the current " +
                "graphics device supports.\n\n" +
                $"Required: {newSliceCount}\n" +
                $"Maximum: {SystemInfo.maxTextureArraySlices}\n\n" +
                "A future windowed editor preview cache can " +
                "remove this whole-world limitation.";

            return false;
        }

        RenderTexture candidateCache =
            CreateHeightCache(
                newSamplesPerSide,
                newSliceCount
            );

        if (
            candidateCache == null
            ||
            !candidateCache.IsCreated()
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The editor preview GPU height cache could not " +
                "be created.";

            return false;
        }

        /*
         * These ranges are calculated once from the authoritative committed
         * RFloat textures. The same values seed the initial composite state
         * and become the immutable reset metadata for this cache generation.
         */
        float[] candidateCommittedMinimums =
            new float[
                newSliceCount
            ];

        float[] candidateCommittedMaximums =
            new float[
                newSliceCount
            ];

        bool[] candidateRangeValid =
            new bool[
                newSliceCount
            ];

        try
        {
            for (
                int tileZ = 0;
                tileZ < newCacheHeight;
                tileZ++
            )
            {
                for (
                    int tileX = 0;
                    tileX < newCacheWidth;
                    tileX++
                )
                {
                    if (
                        !TryLoadCommittedTile(
                            tileX,
                            tileZ,
                            newSamplesPerSide,
                            out Texture2D sourceTexture,
                            out float tileMinimumHeight,
                            out float tileMaximumHeight,
                            out string tileError
                        )
                    )
                    {
                        throw
                            new InvalidOperationException(
                                tileError
                            );
                    }

                    int slice =
                        tileX
                        +
                        tileZ *
                        newCacheWidth;

                    using (WorldMeshesProfiler.PreviewCopyTiles.Auto())
                    {
                        Graphics.CopyTexture(
                            sourceTexture,
                            0,
                            0,
                            candidateCache,
                            slice,
                            0
                        );
                    }

                    candidateCommittedMinimums[
                        slice
                    ] =
                        tileMinimumHeight;

                    candidateCommittedMaximums[
                        slice
                    ] =
                        tileMaximumHeight;

                    candidateRangeValid[
                        slice
                    ] =
                        true;
                }
            }
        }
        catch (
            Exception exception
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The committed authoring height tiles could not " +
                "be copied into the editor preview cache.\n\n" +
                exception.Message;

            return false;
        }

        if (
            !TryCalculateGlobalRange(
                candidateCommittedMinimums,
                candidateCommittedMaximums,
                candidateRangeValid,
                out float candidateMinimumHeight,
                out float candidateMaximumHeight
            )
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The editor preview cache was populated, but valid " +
                "per-slice height-range metadata could not be built.";

            return false;
        }

        if (
            !FloatMatches(
                candidateMinimumHeight,
                manifest.minimumCommittedHeight
            )
            ||
            !FloatMatches(
                candidateMaximumHeight,
                manifest.maximumCommittedHeight
            )
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The preview cache slice ranges do not match the " +
                "validated committed manifest range.\n\n" +
                $"Manifest: {manifest.minimumCommittedHeight:R} -> " +
                $"{manifest.maximumCommittedHeight:R}\n" +
                $"Cache: {candidateMinimumHeight:R} -> " +
                $"{candidateMaximumHeight:R}";

            return false;
        }

        RenderTexture previousCache =
            heightCache;

        heightCache =
            candidateCache;

        cacheOriginTile =
            Vector2Int.zero;

        cacheWidth =
            newCacheWidth;

        cacheHeight =
            newCacheHeight;

        samplesPerSide =
            newSamplesPerSide;

        sampleSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        committedSliceMinimumHeights =
            candidateCommittedMinimums;

        committedSliceMaximumHeights =
            candidateCommittedMaximums;

        /*
         * Composite ranges are mutable and must never alias the immutable
         * committed arrays.
         */
        sliceMinimumHeights =
            (float[])candidateCommittedMinimums.Clone();

        sliceMaximumHeights =
            (float[])candidateCommittedMaximums.Clone();

        sliceRangeValid =
            candidateRangeValid;

        minimumHeight =
            candidateMinimumHeight;

        maximumHeight =
            candidateMaximumHeight;

        sourceCommittedHeightfieldSignature =
            committedSignature;

        sourceOverallAuthoringSignature =
            "";

        sourceContentHash =
            currentContentHash;

        lastIncrementalSliceCount =
            0;

        totalIncrementalSliceUpdates =
            0L;

        DestroyRenderTexture(
            previousCache
        );

        return true;
    }

    // =====================================================
    // SLICE ADDRESSING
    // =====================================================

    public int GetSliceIndex(
        int tileX,
        int tileZ
    )
    {
        int localTileX =
            tileX -
            cacheOriginTile.x;

        int localTileZ =
            tileZ -
            cacheOriginTile.y;

        if (
            localTileX < 0
            ||
            localTileZ < 0
            ||
            localTileX >= cacheWidth
            ||
            localTileZ >= cacheHeight
        )
        {
            return -1;
        }

        return
            localTileX
            +
            localTileZ *
            cacheWidth;
    }

    public bool TryGetTileCoordinate(
        int sliceIndex,
        out Vector2Int tileCoordinate
    )
    {
        tileCoordinate =
            Vector2Int.zero;

        if (
            sliceIndex < 0
            ||
            sliceIndex >=
                SliceCount
            ||
            cacheWidth <= 0
        )
        {
            return false;
        }

        int localTileX =
            sliceIndex %
            cacheWidth;

        int localTileZ =
            sliceIndex /
            cacheWidth;

        tileCoordinate =
            new Vector2Int(
                cacheOriginTile.x +
                    localTileX,
                cacheOriginTile.y +
                    localTileZ
            );

        return true;
    }

    // =====================================================
    // COMMITTED TILE -> EXISTING SLICE
    // =====================================================

    public bool CopyCommittedTileToSlice(
        int tileX,
        int tileZ,
        out string errorMessage
    )
    {
        return
            CopyCommittedTileToSlice(
                tileX,
                tileZ,
                out _,
                out errorMessage
            );
    }

    public bool CopyCommittedTileToSlice(
        int tileX,
        int tileZ,
        out bool heightRangeChanged,
        out string errorMessage
    )
    {
        heightRangeChanged =
            false;

        errorMessage =
            "";

        if (!IsReady)
        {
            errorMessage =
                "The preview cache is not ready.";

            return false;
        }

        float previousMinimum =
            minimumHeight;

        float previousMaximum =
            maximumHeight;

        if (
            !CopyCommittedTileToSliceInternal(
                tileX,
                tileZ,
                false,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !RecalculateGlobalHeightRange(
                out errorMessage
            )
        )
        {
            return false;
        }

        heightRangeChanged =
            !FloatMatches(
                previousMinimum,
                minimumHeight
            )
            ||
            !FloatMatches(
                previousMaximum,
                maximumHeight
            );

        return true;
    }

    // =====================================================
    // COMPOSITE SLICE UPDATE API
    // =====================================================

    public bool UpdateCompositeSlice(
        int tileX,
        int tileZ,
        out string errorMessage
    )
    {
        return
            UpdateCompositeSlice(
                tileX,
                tileZ,
                out _,
                out errorMessage
            );
    }

    public bool UpdateCompositeSlice(
        int tileX,
        int tileZ,
        out bool heightRangeChanged,
        out string errorMessage
    )
    {
        bool success =
            CopyCommittedTileToSlice(
                tileX,
                tileZ,
                out heightRangeChanged,
                out errorMessage
            );

        if (success)
        {
            lastIncrementalSliceCount =
                1;

            totalIncrementalSliceUpdates++;
        }

        return success;
    }

    public bool UpdateCompositeTiles(
        IEnumerable<Vector2Int> tileCoordinates,
        out int updatedSliceCount,
        out bool heightRangeChanged,
        out string errorMessage
    )
    {
        return
            ResetCompositeTilesInternal(
                tileCoordinates,
                true,
                out updatedSliceCount,
                out heightRangeChanged,
                out errorMessage
            );
    }

    /*
     * PreviewService uses this during a complete dirty recomposition
     * transaction. Individual slices are restored to committed/base contents
     * and range metadata, but the global range is deliberately left untouched
     * until all final composite ranges can be committed as one batch.
     */
    internal bool ResetCompositeTilesForRecomposition(
        IEnumerable<Vector2Int> tileCoordinates,
        out int updatedSliceCount,
        out string errorMessage
    )
    {
        return
            ResetCompositeTilesInternal(
                tileCoordinates,
                false,
                out updatedSliceCount,
                out _,
                out errorMessage
            );
    }

    private bool ResetCompositeTilesInternal(
        IEnumerable<Vector2Int> tileCoordinates,
        bool recalculateGlobalRange,
        out int updatedSliceCount,
        out bool heightRangeChanged,
        out string errorMessage
    )
    {
        updatedSliceCount =
            0;

        heightRangeChanged =
            false;

        errorMessage =
            "";

        if (!IsReady)
        {
            errorMessage =
                "The preview cache is not ready.";

            return false;
        }

        if (tileCoordinates == null)
        {
            errorMessage =
                "The composite tile collection is null.";

            return false;
        }

        HashSet<int> uniqueSlices =
            new HashSet<int>();

        List<Vector2Int> uniqueTiles =
            new List<Vector2Int>();

        foreach (
            Vector2Int coordinate
            in tileCoordinates
        )
        {
            int slice =
                GetSliceIndex(
                    coordinate.x,
                    coordinate.y
                );

            if (slice < 0)
            {
                continue;
            }

            if (
                uniqueSlices.Add(
                    slice
                )
            )
            {
                uniqueTiles.Add(
                    coordinate
                );
            }
        }

        if (uniqueTiles.Count <= 0)
        {
            lastIncrementalSliceCount =
                0;

            return true;
        }

        float previousMinimum =
            minimumHeight;

        float previousMaximum =
            maximumHeight;

        foreach (
            Vector2Int coordinate
            in uniqueTiles
        )
        {
            if (
                !CopyCommittedTileToSliceInternal(
                    coordinate.x,
                    coordinate.y,
                    false,
                    out errorMessage
                )
            )
            {
                return false;
            }

            updatedSliceCount++;
        }

        if (recalculateGlobalRange)
        {
            if (
                !RecalculateGlobalHeightRange(
                    out errorMessage
                )
            )
            {
                return false;
            }

            heightRangeChanged =
                !FloatMatches(
                    previousMinimum,
                    minimumHeight
                )
                ||
                !FloatMatches(
                    previousMaximum,
                    maximumHeight
                );
        }

        lastIncrementalSliceCount =
            updatedSliceCount;

        totalIncrementalSliceUpdates +=
            updatedSliceCount;

        return true;
    }

    /*
     * Compatibility API for callers updating one final composite range.
     * Internally it uses the same validate-then-commit batch path.
     */
    public bool SetCompositeSliceRange(
        int tileX,
        int tileZ,
        float sliceMinimumHeight,
        float sliceMaximumHeight,
        out bool globalRangeChanged,
        out string errorMessage
    )
    {
        List<CompositeSliceRangeUpdate> update =
            new List<CompositeSliceRangeUpdate>(
                1
            )
            {
                new CompositeSliceRangeUpdate(
                    new Vector2Int(
                        tileX,
                        tileZ
                    ),
                    sliceMinimumHeight,
                    sliceMaximumHeight
                )
            };

        return
            ApplyCompositeSliceRangeBatch(
                update,
                out globalRangeChanged,
                out errorMessage
            );
    }

    /*
     * Validate every final range before mutating metadata, then apply the
     * complete range batch and calculate the full-world range exactly once.
     */
    internal bool ApplyCompositeSliceRangeBatch(
        IReadOnlyList<CompositeSliceRangeUpdate> updates,
        out bool globalRangeChanged,
        out string errorMessage
    )
    {
        globalRangeChanged =
            false;

        errorMessage =
            "";

        if (!IsReady)
        {
            errorMessage =
                "The preview cache is not ready.";

            return false;
        }

        if (updates == null)
        {
            errorMessage =
                "The composite slice-range update collection is null.";

            return false;
        }

        if (updates.Count <= 0)
        {
            return true;
        }

        int[] slices =
            new int[
                updates.Count
            ];

        HashSet<int> uniqueSlices =
            new HashSet<int>();

        for (
            int index = 0;
            index < updates.Count;
            index++
        )
        {
            CompositeSliceRangeUpdate update =
                updates[
                    index
                ];

            int slice =
                GetSliceIndex(
                    update.TileCoordinate.x,
                    update.TileCoordinate.y
                );

            if (slice < 0)
            {
                errorMessage =
                    $"Tile ({update.TileCoordinate.x}, " +
                    $"{update.TileCoordinate.y}) is outside the preview cache.";

                return false;
            }

            if (!uniqueSlices.Add(slice))
            {
                errorMessage =
                    $"Composite slice-range batch contains duplicate tile " +
                    $"({update.TileCoordinate.x}, {update.TileCoordinate.y}).";

                return false;
            }

            if (
                !IsFinite(
                    update.MinimumHeight
                )
                ||
                !IsFinite(
                    update.MaximumHeight
                )
                ||
                update.MaximumHeight <
                    update.MinimumHeight
            )
            {
                errorMessage =
                    "Composite slice height range is invalid for tile " +
                    $"({update.TileCoordinate.x}, " +
                    $"{update.TileCoordinate.y}).";

                return false;
            }

            slices[
                index
            ] =
                slice;
        }

        float previousMinimum =
            minimumHeight;

        float previousMaximum =
            maximumHeight;

        for (
            int index = 0;
            index < updates.Count;
            index++
        )
        {
            CompositeSliceRangeUpdate update =
                updates[
                    index
                ];

            int slice =
                slices[
                    index
                ];

            sliceMinimumHeights[
                slice
            ] =
                update.MinimumHeight;

            sliceMaximumHeights[
                slice
            ] =
                update.MaximumHeight;

            sliceRangeValid[
                slice
            ] =
                true;
        }

        if (
            !RecalculateGlobalHeightRange(
                out errorMessage
            )
        )
        {
            return false;
        }

        globalRangeChanged =
            !FloatMatches(
                previousMinimum,
                minimumHeight
            )
            ||
            !FloatMatches(
                previousMaximum,
                maximumHeight
            );

        return true;
    }

    public bool TryGetCompositeSliceRange(
        int tileX,
        int tileZ,
        out float sliceMinimumHeight,
        out float sliceMaximumHeight
    )
    {
        sliceMinimumHeight =
            0f;

        sliceMaximumHeight =
            0f;

        int slice =
            GetSliceIndex(
                tileX,
                tileZ
            );

        if (
            slice < 0
            ||
            sliceRangeValid == null
            ||
            slice >=
                sliceRangeValid.Length
            ||
            !sliceRangeValid[
                slice
            ]
        )
        {
            return false;
        }

        sliceMinimumHeight =
            sliceMinimumHeights[
                slice
            ];

        sliceMaximumHeight =
            sliceMaximumHeights[
                slice
            ];

        return true;
    }

    public void MarkOverallAuthoringSignature(
        string overallAuthoringSignature
    )
    {
        if (
            string.IsNullOrEmpty(
                overallAuthoringSignature
            )
        )
        {
            return;
        }

        sourceOverallAuthoringSignature =
            overallAuthoringSignature;
    }

    // =====================================================
    // INTERNAL COMMITTED TILE COPY
    // =====================================================

    private bool CopyCommittedTileToSliceInternal(
        int tileX,
        int tileZ,
        bool recalculateGlobalRange,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int slice =
            GetSliceIndex(
                tileX,
                tileZ
            );

        if (slice < 0)
        {
            errorMessage =
                $"Tile ({tileX}, {tileZ}) is outside the preview cache.";

            return false;
        }

        if (
            !TryLoadCommittedTileTexture(
                tileX,
                tileZ,
                samplesPerSide,
                out Texture2D sourceTexture,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TryGetCommittedSliceRange(
                slice,
                out float tileMinimumHeight,
                out float tileMaximumHeight,
                out errorMessage
            )
        )
        {
            return false;
        }

        try
        {
            using (WorldMeshesProfiler.PreviewCopyTiles.Auto())
            {
                Graphics.CopyTexture(
                    sourceTexture,
                    0,
                    0,
                    heightCache,
                    slice,
                    0
                );
            }
        }
        catch (
            Exception exception
        )
        {
            errorMessage =
                $"Committed tile ({tileX}, {tileZ}) could not be " +
                "copied into the existing preview cache slice.\n\n" +
                exception.Message;

            return false;
        }

        sliceMinimumHeights[
            slice
        ] =
            tileMinimumHeight;

        sliceMaximumHeights[
            slice
        ] =
            tileMaximumHeight;

        sliceRangeValid[
            slice
        ] =
            true;

        if (
            recalculateGlobalRange
            &&
            !RecalculateGlobalHeightRange(
                out errorMessage
            )
        )
        {
            return false;
        }

        return true;
    }

    private bool TryGetCommittedSliceRange(
        int slice,
        out float minimum,
        out float maximum,
        out string errorMessage
    )
    {
        minimum =
            0f;

        maximum =
            0f;

        errorMessage =
            "";

        if (
            committedSliceMinimumHeights == null
            ||
            committedSliceMaximumHeights == null
            ||
            slice < 0
            ||
            slice >=
                committedSliceMinimumHeights.Length
            ||
            slice >=
                committedSliceMaximumHeights.Length
        )
        {
            errorMessage =
                "Committed preview range metadata is unavailable for " +
                $"cache slice {slice}.";

            return false;
        }

        minimum =
            committedSliceMinimumHeights[
                slice
            ];

        maximum =
            committedSliceMaximumHeights[
                slice
            ];

        if (
            !IsFinite(
                minimum
            )
            ||
            !IsFinite(
                maximum
            )
            ||
            maximum <
                minimum
        )
        {
            errorMessage =
                "Committed preview range metadata is invalid for " +
                $"cache slice {slice}.";

            return false;
        }

        return true;
    }

    // =====================================================
    // COMMITTED TILE LOAD / RANGE
    // =====================================================

    private static bool TryLoadCommittedTile(
        int tileX,
        int tileZ,
        int expectedSamplesPerSide,
        out Texture2D texture,
        out float tileMinimumHeight,
        out float tileMaximumHeight,
        out string errorMessage
    )
    {
        tileMinimumHeight =
            float.PositiveInfinity;

        tileMaximumHeight =
            float.NegativeInfinity;

        if (
            !TryLoadCommittedTileTexture(
                tileX,
                tileZ,
                expectedSamplesPerSide,
                out texture,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            TryReadCommittedTileRange(
                tileX,
                tileZ,
                texture,
                expectedSamplesPerSide,
                out tileMinimumHeight,
                out tileMaximumHeight,
                out errorMessage
            );
    }

    private static bool TryLoadCommittedTileTexture(
        int tileX,
        int tileZ,
        int expectedSamplesPerSide,
        out Texture2D texture,
        out string errorMessage
    )
    {
        texture =
            null;

        errorMessage =
            "";

        string sourcePath =
            TerrainAuthoringStateUtility
                .GetAuthoringHeightTilePath(
                    tileX,
                    tileZ
                );

        using (WorldMeshesProfiler.PreviewLoadTiles.Auto())
        {
            texture =
                AssetDatabase
                    .LoadAssetAtPath<Texture2D>(
                        sourcePath
                    );
        }

        if (texture == null)
        {
            errorMessage =
                $"Committed authoring tile ({tileX}, {tileZ}) " +
                "could not be loaded.\n\n" +
                sourcePath;

            return false;
        }

        if (
            texture.width !=
                expectedSamplesPerSide
            ||
            texture.height !=
                expectedSamplesPerSide
        )
        {
            errorMessage =
                $"Committed authoring tile ({tileX}, {tileZ}) has " +
                "unexpected dimensions.\n\n" +
                $"Expected: {expectedSamplesPerSide} x " +
                $"{expectedSamplesPerSide}\n" +
                $"Actual: {texture.width} x {texture.height}\n\n" +
                sourcePath;

            return false;
        }

        if (
            texture.format !=
            TextureFormat.RFloat
        )
        {
            errorMessage =
                $"Committed authoring tile ({tileX}, {tileZ}) does " +
                "not use TextureFormat.RFloat.\n\n" +
                $"Actual: {texture.format}\n\n" +
                sourcePath;

            return false;
        }

        return true;
    }

    private static bool TryReadCommittedTileRange(
        int tileX,
        int tileZ,
        Texture2D texture,
        int expectedSamplesPerSide,
        out float tileMinimumHeight,
        out float tileMaximumHeight,
        out string errorMessage
    )
    {
        tileMinimumHeight =
            float.PositiveInfinity;

        tileMaximumHeight =
            float.NegativeInfinity;

        errorMessage =
            "";

        using var rangeProfilerScope =
            WorldMeshesProfiler.PreviewReadTileRanges.Auto();

        string sourcePath =
            TerrainAuthoringStateUtility
                .GetAuthoringHeightTilePath(
                    tileX,
                    tileZ
                );

        NativeArray<float> data;

        try
        {
            data =
                texture.GetPixelData<float>(
                    0
                );
        }
        catch (
            Exception exception
        )
        {
            errorMessage =
                $"Committed authoring tile ({tileX}, {tileZ}) could " +
                "not be read for range metadata.\n\n" +
                exception.Message +
                "\n\n" +
                sourcePath;

            return false;
        }

        int expectedSampleCount =
            expectedSamplesPerSide *
            expectedSamplesPerSide;

        if (
            data.Length !=
            expectedSampleCount
        )
        {
            errorMessage =
                $"Committed authoring tile ({tileX}, {tileZ}) has " +
                "an unexpected sample count.\n\n" +
                $"Expected: {expectedSampleCount:N0}\n" +
                $"Actual: {data.Length:N0}\n\n" +
                sourcePath;

            return false;
        }

        for (
            int index = 0;
            index < data.Length;
            index++
        )
        {
            float height =
                data[
                    index
                ];

            if (!IsFinite(height))
            {
                errorMessage =
                    $"Committed authoring tile ({tileX}, {tileZ}) " +
                    "contains a non-finite height sample.\n\n" +
                    $"Sample: {index}\n" +
                    $"Value: {height}\n\n" +
                    sourcePath;

                return false;
            }

            tileMinimumHeight =
                Mathf.Min(
                    tileMinimumHeight,
                    height
                );

            tileMaximumHeight =
                Mathf.Max(
                    tileMaximumHeight,
                    height
                );
        }

        if (
            !IsFinite(
                tileMinimumHeight
            )
            ||
            !IsFinite(
                tileMaximumHeight
            )
            ||
            tileMaximumHeight <
                tileMinimumHeight
        )
        {
            errorMessage =
                $"Committed authoring tile ({tileX}, {tileZ}) did " +
                "not produce a valid height range.";

            return false;
        }

        return true;
    }

    // =====================================================
    // GLOBAL RANGE
    // =====================================================

    private bool RecalculateGlobalHeightRange(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryCalculateGlobalRange(
                sliceMinimumHeights,
                sliceMaximumHeights,
                sliceRangeValid,
                out float newMinimumHeight,
                out float newMaximumHeight
            )
        )
        {
            errorMessage =
                "The preview cache does not contain valid range " +
                "metadata for every composite slice.";

            return false;
        }

        minimumHeight =
            newMinimumHeight;

        maximumHeight =
            newMaximumHeight;

        return true;
    }

    private static bool TryCalculateGlobalRange(
        float[] minimums,
        float[] maximums,
        bool[] rangeValid,
        out float globalMinimum,
        out float globalMaximum
    )
    {
        globalMinimum =
            float.PositiveInfinity;

        globalMaximum =
            float.NegativeInfinity;

        if (
            minimums == null
            ||
            maximums == null
            ||
            rangeValid == null
            ||
            minimums.Length <= 0
            ||
            minimums.Length !=
                maximums.Length
            ||
            minimums.Length !=
                rangeValid.Length
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < minimums.Length;
            index++
        )
        {
            if (!rangeValid[index])
            {
                return false;
            }

            float sliceMinimum =
                minimums[
                    index
                ];

            float sliceMaximum =
                maximums[
                    index
                ];

            if (
                !IsFinite(
                    sliceMinimum
                )
                ||
                !IsFinite(
                    sliceMaximum
                )
                ||
                sliceMaximum <
                    sliceMinimum
            )
            {
                return false;
            }

            globalMinimum =
                Mathf.Min(
                    globalMinimum,
                    sliceMinimum
                );

            globalMaximum =
                Mathf.Max(
                    globalMaximum,
                    sliceMaximum
                );
        }

        return
            IsFinite(
                globalMinimum
            )
            &&
            IsFinite(
                globalMaximum
            )
            &&
            globalMaximum >=
                globalMinimum;
    }

    // =====================================================
    // CREATE GPU CACHE
    // =====================================================

    private static RenderTexture CreateHeightCache(
        int cacheSamplesPerSide,
        int sliceCount
    )
    {
        RenderTexture cache =
            new RenderTexture(
                cacheSamplesPerSide,
                cacheSamplesPerSide,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );

        cache.name =
            "Terrain Authoring Composite Preview Height Cache";

        cache.dimension =
            TextureDimension.Tex2DArray;

        cache.volumeDepth =
            sliceCount;

        cache.enableRandomWrite =
            SystemInfo.supportsComputeShaders;

        cache.useMipMap =
            false;

        cache.autoGenerateMips =
            false;

        cache.wrapMode =
            TextureWrapMode.Clamp;

        cache.filterMode =
            FilterMode.Point;

        cache.anisoLevel =
            0;

        cache.hideFlags =
            HideFlags.HideAndDontSave;

        cache.Create();

        return cache;
    }

    // =====================================================
    // DISPOSE
    // =====================================================

    public void Dispose()
    {
        DestroyRenderTexture(
            heightCache
        );

        heightCache =
            null;

        sourceCommittedHeightfieldSignature =
            "";

        sourceOverallAuthoringSignature =
            "";

        sourceContentHash =
            "";

        cacheOriginTile =
            Vector2Int.zero;

        cacheWidth =
            0;

        cacheHeight =
            0;

        samplesPerSide =
            0;

        sampleSpacing =
            0f;

        worldSizeXZ =
            Vector2.zero;

        committedSliceMinimumHeights =
            null;

        committedSliceMaximumHeights =
            null;

        sliceMinimumHeights =
            null;

        sliceMaximumHeights =
            null;

        sliceRangeValid =
            null;

        minimumHeight =
            0f;

        maximumHeight =
            0f;

        lastIncrementalSliceCount =
            0;

        totalIncrementalSliceUpdates =
            0L;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static void DestroyRenderTexture(
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

        UnityEngine.Object.DestroyImmediate(
            texture
        );
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

    private static bool FloatMatches(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a -
                b
            )
            <=
            0.0001f;
    }
}
