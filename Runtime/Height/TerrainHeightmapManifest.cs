using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct TerrainHeightTileRange
{
    [SerializeField]
    private bool valid;

    [SerializeField]
    private float minimumHeight;

    [SerializeField]
    private float maximumHeight;

    public bool IsValid
    {
        get
        {
            return
                valid
                &&
                IsFinite(minimumHeight)
                &&
                IsFinite(maximumHeight)
                &&
                maximumHeight >= minimumHeight;
        }
    }

    public float MinimumHeight =>
        minimumHeight;

    public float MaximumHeight =>
        maximumHeight;

    public static TerrainHeightTileRange Create(
        float minimumHeight,
        float maximumHeight
    )
    {
        TerrainHeightTileRange range =
            default;

        if (
            IsFinite(minimumHeight)
            &&
            IsFinite(maximumHeight)
            &&
            maximumHeight >= minimumHeight
        )
        {
            range.valid =
                true;

            range.minimumHeight =
                minimumHeight;

            range.maximumHeight =
                maximumHeight;
        }

        return range;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }
}

public class TerrainHeightmapManifest :
    ScriptableObject
{
    // =====================================================
    // ADDRESSABLES
    // =====================================================

    public const string HeightTileAddressPrefix =
        "TerrainHeight/HeightTile";

    /*
     * Logical identity root for the future multiresolution
     * representation Addressables layout.
     *
     * Existing authoritative tile addresses remain unchanged
     * until the Addressables migration package activates this
     * representation-aware contract.
     */
    public const string HeightRepresentationAddressRoot =
        "TerrainHeight";

    // =====================================================
    // GENERATED OUTPUT STATE
    // =====================================================

    /*
     * Structural completeness means that a complete runtime height tile
     * dataset exists for this manifest layout. It does not imply that the
     * dataset matches the latest authoring signature. Source currentness is
     * tracked separately by the source fields and WorldSettings generation
     * state.
     */
    public bool isComplete =
        false;

    /*
     * Version of the authoring -> runtime compilation logic.
     */
    public int compilerVersion =
        0;

    // =====================================================
    // STREAMING HEIGHT PYRAMID STATE
    // =====================================================

    /*
     * Streaming completeness is deliberately independent from
     * authoritative height completeness. A missing/outdated
     * derived representation must never redefine isComplete.
     */
    public bool streamingPyramidIsComplete =
        false;

    public int streamingPyramidCompilerVersion =
        0;

    public int streamingSourceHeightmapGenerationRevision =
        -1;

    public int streamingGenerationRevision =
        0;

    public string streamingGenerationSignature =
        "";

    [SerializeField]
    private List<TerrainHeightStreamingLevelDescriptor>
        streamingLevels =
            new List<TerrainHeightStreamingLevelDescriptor>();

    public int StreamingLevelCount =>
        streamingLevels != null
            ? streamingLevels.Count
            : 0;

    public bool TryGetStreamingLevelDescriptor(
        int sampleStride,
        out TerrainHeightStreamingLevelDescriptor descriptor
    )
    {
        descriptor =
            default;

        if (
            sampleStride <= 1
            ||
            streamingLevels == null
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < streamingLevels.Count;
            index++
        )
        {
            TerrainHeightStreamingLevelDescriptor candidate =
                streamingLevels[index];

            if (
                candidate.SampleStride ==
                    sampleStride
            )
            {
                descriptor =
                    candidate;

                return
                    descriptor
                        .IsStructurallyValid;
            }
        }

        return false;
    }

    public bool HasStreamingStride(
        int sampleStride
    )
    {
        return
            TryGetStreamingLevelDescriptor(
                sampleStride,
                out _
            );
    }

    public bool TryGetHeightRepresentationDescriptor(
        int sampleStride,
        out TerrainHeightStreamingLevelDescriptor descriptor
    )
    {
        descriptor =
            default;

        if (sampleStride == 1)
        {
            descriptor =
                TerrainHeightStreamingLevelDescriptor
                    .Create(
                        1,
                        HeightSampleSpacing,
                        heightTileSamplesPerSide,
                        heightTileWorldSize,
                        heightTileGridWidth,
                        heightTileGridHeight,
                        TextureFormat.RFloat
                    );

            return
                descriptor
                    .IsStructurallyValid;
        }

        return
            TryGetStreamingLevelDescriptor(
                sampleStride,
                out descriptor
            );
    }

    public bool TrySetStreamingLevelDescriptors(
        IReadOnlyList<TerrainHeightStreamingLevelDescriptor> descriptors
    )
    {
        if (descriptors == null)
        {
            return false;
        }

        int previousStride =
            1;

        for (
            int index = 0;
            index < descriptors.Count;
            index++
        )
        {
            TerrainHeightStreamingLevelDescriptor descriptor =
                descriptors[index];

            if (
                !IsStreamingDescriptorCompatible(
                    descriptor,
                    previousStride
                )
            )
            {
                return false;
            }

            previousStride =
                descriptor.SampleStride;
        }

        if (streamingLevels == null)
        {
            streamingLevels =
                new List<TerrainHeightStreamingLevelDescriptor>(
                    descriptors.Count
                );
        }
        else
        {
            streamingLevels.Clear();

            if (
                streamingLevels.Capacity <
                    descriptors.Count
            )
            {
                streamingLevels.Capacity =
                    descriptors.Count;
            }
        }

        for (
            int index = 0;
            index < descriptors.Count;
            index++
        )
        {
            streamingLevels.Add(
                descriptors[index]
            );
        }

        return true;
    }

    public void ClearStreamingLevelDescriptors()
    {
        if (streamingLevels != null)
        {
            streamingLevels.Clear();
        }
    }

    private bool IsStreamingDescriptorCompatible(
        TerrainHeightStreamingLevelDescriptor descriptor,
        int previousStride
    )
    {
        if (
            !descriptor.IsStructurallyValid
            ||
            !descriptor.IsDerived
            ||
            descriptor.SampleStride <=
                previousStride
            ||
            HeightTileIntervalsPerSide %
                descriptor.SampleStride !=
                0
        )
        {
            return false;
        }

        int expectedSamplesPerSide =
            HeightTileIntervalsPerSide /
                descriptor.SampleStride +
            1;

        float expectedSpacing =
            HeightSampleSpacing *
            descriptor.SampleStride;

        return
            descriptor.SamplesPerSide ==
                expectedSamplesPerSide
            &&
            Mathf.Approximately(
                descriptor.SampleSpacing,
                expectedSpacing
            )
            &&
            Mathf.Approximately(
                descriptor.TileWorldSize,
                heightTileWorldSize
            )
            &&
            descriptor.TileGridWidth ==
                heightTileGridWidth
            &&
            descriptor.TileGridHeight ==
                heightTileGridHeight
            &&
            descriptor.TextureFormat ==
                TextureFormat.RFloat;
    }

    // =====================================================
    // AUTHORING SOURCE
    // =====================================================

    public int sourceAuthoringRevision =
        0;

    public string sourceAuthoringSignature =
        "";

    public string sourceAuthoringContentHash =
        "";

    // =====================================================
    // COMPILED HEIGHT RANGE
    // =====================================================

    /*
     * Exact finite sample range represented by the physical runtime height
     * tiles currently on disk. During a partial incremental update these
     * values intentionally describe the mixed physical dataset (some updated
     * tiles, some previous tiles) even while source currentness remains stale.
     */
    public float minimumTerrainHeight =
        0f;

    public float maximumTerrainHeight =
        0f;

    /*
     * Flattened row-major per-tile range metadata:
     *
     *     index = tileX + tileZ * heightTileGridWidth
     *
     * This metadata is the authoritative lightweight source for incremental
     * global min/max reduction. Unchanged Texture2D assets never need to be
     * reread merely to recalculate world bounds.
     */
    [SerializeField]
    private List<TerrainHeightTileRange>
        tileHeightRanges =
            new List<TerrainHeightTileRange>();

    public int TileHeightRangeCount =>
        tileHeightRanges != null
            ? tileHeightRanges.Count
            : 0;

    public int ExpectedTileHeightRangeCount
    {
        get
        {
            if (
                heightTileGridWidth <= 0
                ||
                heightTileGridHeight <= 0
            )
            {
                return 0;
            }

            return
                heightTileGridWidth *
                heightTileGridHeight;
        }
    }

    public int ValidTileHeightRangeCount
    {
        get
        {
            if (tileHeightRanges == null)
            {
                return 0;
            }

            int count =
                0;

            for (
                int index = 0;
                index < tileHeightRanges.Count;
                index++
            )
            {
                if (tileHeightRanges[index].IsValid)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool HasCompleteTileHeightRanges
    {
        get
        {
            int expectedCount =
                ExpectedTileHeightRangeCount;

            if (
                expectedCount <= 0
                ||
                tileHeightRanges == null
                ||
                tileHeightRanges.Count != expectedCount
            )
            {
                return false;
            }

            for (
                int index = 0;
                index < tileHeightRanges.Count;
                index++
            )
            {
                if (!tileHeightRanges[index].IsValid)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void InitializeTileHeightRanges(
        int tileGridWidth,
        int tileGridHeight
    )
    {
        int safeWidth =
            Mathf.Max(
                0,
                tileGridWidth
            );

        int safeHeight =
            Mathf.Max(
                0,
                tileGridHeight
            );

        int count =
            safeWidth *
            safeHeight;

        if (tileHeightRanges == null)
        {
            tileHeightRanges =
                new List<TerrainHeightTileRange>(
                    count
                );
        }
        else
        {
            tileHeightRanges.Clear();

            if (tileHeightRanges.Capacity < count)
            {
                tileHeightRanges.Capacity =
                    count;
            }
        }

        for (
            int index = 0;
            index < count;
            index++
        )
        {
            tileHeightRanges.Add(
                default
            );
        }
    }

    public void ClearTileHeightRanges()
    {
        if (tileHeightRanges != null)
        {
            tileHeightRanges.Clear();
        }
    }

    public bool SetTileHeightRange(
        int tileX,
        int tileZ,
        float minimumHeight,
        float maximumHeight
    )
    {
        if (
            !IsTileCoordinateValid(
                tileX,
                tileZ
            )
        )
        {
            return false;
        }

        TerrainHeightTileRange range =
            TerrainHeightTileRange.Create(
                minimumHeight,
                maximumHeight
            );

        if (!range.IsValid)
        {
            return false;
        }

        int expectedCount =
            ExpectedTileHeightRangeCount;

        if (
            tileHeightRanges == null
            ||
            tileHeightRanges.Count != expectedCount
        )
        {
            return false;
        }

        int index =
            tileX +
            tileZ *
            heightTileGridWidth;

        tileHeightRanges[index] =
            range;

        return true;
    }

    public bool TryGetTileHeightRange(
        int tileX,
        int tileZ,
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            0f;

        maximumHeight =
            0f;

        if (
            !IsTileCoordinateValid(
                tileX,
                tileZ
            )
        )
        {
            return false;
        }

        int expectedCount =
            ExpectedTileHeightRangeCount;

        if (
            tileHeightRanges == null
            ||
            tileHeightRanges.Count != expectedCount
        )
        {
            return false;
        }

        int index =
            tileX +
            tileZ *
            heightTileGridWidth;

        TerrainHeightTileRange range =
            tileHeightRanges[index];

        if (!range.IsValid)
        {
            return false;
        }

        minimumHeight =
            range.MinimumHeight;

        maximumHeight =
            range.MaximumHeight;

        return true;
    }

    public bool TryCalculateGlobalHeightRange(
        out float minimumHeight,
        out float maximumHeight
    )
    {
        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        if (!HasCompleteTileHeightRanges)
        {
            return false;
        }

        for (
            int index = 0;
            index < tileHeightRanges.Count;
            index++
        )
        {
            TerrainHeightTileRange range =
                tileHeightRanges[index];

            minimumHeight =
                Mathf.Min(
                    minimumHeight,
                    range.MinimumHeight
                );

            maximumHeight =
                Mathf.Max(
                    maximumHeight,
                    range.MaximumHeight
                );
        }

        return
            IsFinite(minimumHeight)
            &&
            IsFinite(maximumHeight)
            &&
            maximumHeight >= minimumHeight;
    }

    public bool TryRecalculateGlobalHeightRange(
        out float minimumHeight,
        out float maximumHeight
    )
    {
        if (
            !TryCalculateGlobalHeightRange(
                out minimumHeight,
                out maximumHeight
            )
        )
        {
            return false;
        }

        minimumTerrainHeight =
            minimumHeight;

        maximumTerrainHeight =
            maximumHeight;

        return true;
    }

    // =====================================================
    // WORLD
    // =====================================================

    public int gridWidth;

    public int gridHeight;

    // =====================================================
    // HEIGHTFIELD LAYOUT
    // =====================================================

    public float chunkSize;

    /*
     * Native heightfield intervals per terrain chunk.
     */
    [FormerlySerializedAs("lod0Resolution")]
    public int heightfieldResolutionPerChunk;

    // =====================================================
    // HEIGHT TILE LAYOUT
    // =====================================================

    public int heightTileChunkSpan;

    public int heightTileGridWidth;

    public int heightTileGridHeight;

    public float heightTileWorldSize;

    public int heightTileSamplesPerSide;

    // =====================================================
    // DERIVED RUNTIME VALUES
    // =====================================================

    public int HeightTileIntervalsPerSide
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    heightTileSamplesPerSide - 1
                );
        }
    }

    public float HeightSampleSpacing
    {
        get
        {
            return
                Mathf.Max(
                    0.01f,
                    chunkSize
                )
                /
                Mathf.Max(
                    1,
                    heightfieldResolutionPerChunk
                );
        }
    }

    public float WorldSizeX
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridWidth
                )
                *
                Mathf.Max(
                    0.01f,
                    chunkSize
                );
        }
    }

    public float WorldSizeZ
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridHeight
                )
                *
                Mathf.Max(
                    0.01f,
                    chunkSize
                );
        }
    }

    public Vector2 WorldSizeXZ
    {
        get
        {
            return
                new Vector2(
                    WorldSizeX,
                    WorldSizeZ
                );
        }
    }

    public int WorldMaxSampleX
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridWidth
                )
                *
                Mathf.Max(
                    1,
                    heightfieldResolutionPerChunk
                );
        }
    }

    public int WorldMaxSampleZ
    {
        get
        {
            return
                Mathf.Max(
                    1,
                    gridHeight
                )
                *
                Mathf.Max(
                    1,
                    heightfieldResolutionPerChunk
                );
        }
    }

    public bool HasValidHeightRange
    {
        get
        {
            return
                isComplete
                &&
                IsFinite(
                    minimumTerrainHeight
                )
                &&
                IsFinite(
                    maximumTerrainHeight
                )
                &&
                maximumTerrainHeight >=
                    minimumTerrainHeight;
        }
    }

    // =====================================================
    // TILE VALIDATION
    // =====================================================

    public bool IsTileCoordinateValid(
        int tileX,
        int tileZ
    )
    {
        return
            heightTileGridWidth > 0
            &&
            heightTileGridHeight > 0
            &&
            tileX >= 0
            &&
            tileZ >= 0
            &&
            tileX <
                heightTileGridWidth
            &&
            tileZ <
                heightTileGridHeight;
    }

    // =====================================================
    // ADDRESSABLE TILE ADDRESS
    // =====================================================

    public string GetHeightTileAddress(
        int tileX,
        int tileZ
    )
    {
        return
            $"{HeightTileAddressPrefix}_" +
            $"{tileX}_{tileZ}";
    }

    /*
     * Representation-aware logical identity reserved for the
     * multiresolution Addressables migration. The existing
     * GetHeightTileAddress contract remains unchanged.
     */
    public string GetHeightRepresentationAddress(
        int sampleStride,
        int tileX,
        int tileZ
    )
    {
        if (
            sampleStride < 1
            ||
            !TerrainHeightStreamingPyramidPolicy
                .IsPowerOfTwo(
                    sampleStride
                )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "A height representation stride must be a positive power of two."
            );
        }

        return
            $"{HeightRepresentationAddressRoot}/" +
            $"Stride{sampleStride}/" +
            $"HeightTile_{tileX}_{tileZ}";
    }

    // =====================================================
    // WORLD POSITION -> TILE
    // =====================================================

    public bool TryGetHeightTileCoordinate(
        Vector2 worldPositionXZ,
        out Vector2Int tileCoordinate
    )
    {
        tileCoordinate =
            default;

        if (
            worldPositionXZ.x < 0f
            ||
            worldPositionXZ.y < 0f
            ||
            worldPositionXZ.x > WorldSizeX
            ||
            worldPositionXZ.y > WorldSizeZ
        )
        {
            return false;
        }

        int tileX =
            Mathf.FloorToInt(
                worldPositionXZ.x /
                Mathf.Max(
                    0.01f,
                    heightTileWorldSize
                )
            );

        int tileZ =
            Mathf.FloorToInt(
                worldPositionXZ.y /
                Mathf.Max(
                    0.01f,
                    heightTileWorldSize
                )
            );

        tileX =
            Mathf.Clamp(
                tileX,
                0,
                Mathf.Max(
                    0,
                    heightTileGridWidth - 1
                )
            );

        tileZ =
            Mathf.Clamp(
                tileZ,
                0,
                Mathf.Max(
                    0,
                    heightTileGridHeight - 1
                )
            );

        tileCoordinate =
            new Vector2Int(
                tileX,
                tileZ
            );

        return true;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }
}
