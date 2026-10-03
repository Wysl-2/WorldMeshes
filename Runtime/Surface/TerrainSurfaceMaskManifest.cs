using System.Collections.Generic;
using UnityEngine;

/*
 * Runtime metadata for the baked terrain surface-mask tiles.
 *
 * The authoritative native dataset stores one normalized R8 channel:
 *
 *     R = final Scree suitability
 *
 * The optional streaming pyramid is a rendering derivative of that native
 * dataset. Native completeness and streaming completeness are deliberately
 * independent so derived data can be rebuilt without redefining the
 * authoritative Surface result.
 */
public sealed class TerrainSurfaceMaskManifest :
    ScriptableObject
{
    public const int CurrentCompilerVersion =
        1;

    public const int CurrentChannelLayoutVersion =
        1;

    public const int CurrentStreamingPyramidCompilerVersion =
        1;

    public const string SurfaceTileAddressPrefix =
        "TerrainSurface/SurfaceTile";

    // =====================================================
    // GENERATED OUTPUT STATE
    // =====================================================

    public bool isComplete =
        false;

    public int compilerVersion =
        0;

    public int channelLayoutVersion =
        0;

    public int surfaceMaskGenerationRevision =
        0;

    // =====================================================
    // STREAMING SURFACE PYRAMID STATE
    // =====================================================

    public bool streamingPyramidIsComplete =
        false;

    public int streamingPyramidCompilerVersion =
        0;

    public int streamingPyramidPolicyVersion =
        0;

    public int streamingSourceSurfaceMaskGenerationRevision =
        -1;

    public string streamingSourceSurfaceGenerationSignature =
        "";

    public int streamingGenerationRevision =
        0;

    public string streamingGenerationSignature =
        "";

    [SerializeField]
    private List<TerrainSurfaceStreamingLevelDescriptor>
        streamingLevels =
            new List<TerrainSurfaceStreamingLevelDescriptor>();

    public int StreamingLevelCount =>
        streamingLevels != null
            ? streamingLevels.Count
            : 0;

    public bool TryGetStreamingLevelDescriptor(
        int sampleStride,
        out TerrainSurfaceStreamingLevelDescriptor descriptor
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
            TerrainSurfaceStreamingLevelDescriptor candidate =
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

    public bool TryGetSurfaceRepresentationDescriptor(
        int sampleStride,
        out TerrainSurfaceStreamingLevelDescriptor descriptor
    )
    {
        descriptor =
            default;

        if (sampleStride == 1)
        {
            descriptor =
                TerrainSurfaceStreamingLevelDescriptor
                    .Create(
                        1,
                        sampleSpacing,
                        samplesPerSide,
                        tileWorldSize,
                        tileGridWidth,
                        tileGridHeight,
                        TextureFormat.R8
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
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> descriptors
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
            TerrainSurfaceStreamingLevelDescriptor descriptor =
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
                new List<TerrainSurfaceStreamingLevelDescriptor>(
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
        TerrainSurfaceStreamingLevelDescriptor descriptor,
        int previousStride
    )
    {
        int nativeIntervals =
            samplesPerSide - 1;

        if (
            !descriptor.IsStructurallyValid
            ||
            !descriptor.IsDerived
            ||
            descriptor.SampleStride <=
                previousStride
            ||
            nativeIntervals <= 0
            ||
            nativeIntervals %
                descriptor.SampleStride !=
                0
        )
        {
            return false;
        }

        int expectedSamplesPerSide =
            nativeIntervals /
                descriptor.SampleStride +
            1;

        float expectedSpacing =
            sampleSpacing *
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
                tileWorldSize
            )
            &&
            descriptor.TileGridWidth ==
                tileGridWidth
            &&
            descriptor.TileGridHeight ==
                tileGridHeight
            &&
            descriptor.TextureFormat ==
                TextureFormat.R8;
    }

    // =====================================================
    // SOURCE HEIGHTMAP STATE
    // =====================================================

    public int sourceHeightmapGenerationRevision =
        0;

    public string sourceHeightmapSignature =
        "";

    public string sourceAuthoringSignature =
        "";

    public string sourceAuthoringContentHash =
        "";

    // =====================================================
    // SURFACE SETTINGS / GENERATED SIGNATURES
    // =====================================================

    public string surfaceSettingsSignature =
        "";

    public string surfaceGenerationSignature =
        "";

    // =====================================================
    // TILE LAYOUT
    // =====================================================

    public int tileGridWidth =
        0;

    public int tileGridHeight =
        0;

    public float tileWorldSize =
        0f;

    public int samplesPerSide =
        0;

    public float sampleSpacing =
        0f;

    public Vector2 worldSizeXZ =
        Vector2.zero;

    public int TileCount =>
        Mathf.Max(
            0,
            tileGridWidth
        )
        *
        Mathf.Max(
            0,
            tileGridHeight
        );

    public bool IsTileCoordinateValid(
        int tileX,
        int tileZ
    )
    {
        return
            tileX >= 0
            &&
            tileZ >= 0
            &&
            tileX < tileGridWidth
            &&
            tileZ < tileGridHeight;
    }

    public string GetSurfaceTileAddress(
        int tileX,
        int tileZ
    )
    {
        return
            $"{SurfaceTileAddressPrefix}_" +
            $"{tileX}_{tileZ}";
    }

    public bool MatchesHeightLayout(
        TerrainHeightmapManifest heightManifest
    )
    {
        if (heightManifest == null)
        {
            return false;
        }

        return
            tileGridWidth ==
                heightManifest.heightTileGridWidth
            &&
            tileGridHeight ==
                heightManifest.heightTileGridHeight
            &&
            samplesPerSide ==
                heightManifest.heightTileSamplesPerSide
            &&
            Mathf.Approximately(
                tileWorldSize,
                heightManifest.heightTileWorldSize
            )
            &&
            Mathf.Approximately(
                sampleSpacing,
                heightManifest.HeightSampleSpacing
            )
            &&
            Mathf.Approximately(
                worldSizeXZ.x,
                heightManifest.WorldSizeX
            )
            &&
            Mathf.Approximately(
                worldSizeXZ.y,
                heightManifest.WorldSizeZ
            );
    }
}
