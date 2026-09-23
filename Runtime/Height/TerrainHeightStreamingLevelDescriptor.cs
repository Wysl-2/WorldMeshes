using System;
using UnityEngine;

[Serializable]
public struct TerrainHeightStreamingLevelDescriptor
{
    [SerializeField]
    private int sampleStride;

    [SerializeField]
    private float sampleSpacing;

    [SerializeField]
    private int samplesPerSide;

    [SerializeField]
    private float tileWorldSize;

    [SerializeField]
    private int tileGridWidth;

    [SerializeField]
    private int tileGridHeight;

    [SerializeField]
    private TextureFormat textureFormat;

    public int SampleStride =>
        sampleStride;

    public float SampleSpacing =>
        sampleSpacing;

    public int SamplesPerSide =>
        samplesPerSide;

    public float TileWorldSize =>
        tileWorldSize;

    public int TileGridWidth =>
        tileGridWidth;

    public int TileGridHeight =>
        tileGridHeight;

    public TextureFormat TextureFormat =>
        textureFormat;

    public bool IsDerived =>
        sampleStride > 1;

    public bool IsStructurallyValid
    {
        get
        {
            return
                sampleStride >= 1
                &&
                IsPowerOfTwo(
                    sampleStride
                )
                &&
                IsFinite(
                    sampleSpacing
                )
                &&
                sampleSpacing > 0f
                &&
                samplesPerSide >= 2
                &&
                IsFinite(
                    tileWorldSize
                )
                &&
                tileWorldSize > 0f
                &&
                tileGridWidth > 0
                &&
                tileGridHeight > 0
                &&
                textureFormat ==
                    TextureFormat.RFloat;
        }
    }

    public static TerrainHeightStreamingLevelDescriptor Create(
        int sampleStride,
        float sampleSpacing,
        int samplesPerSide,
        float tileWorldSize,
        int tileGridWidth,
        int tileGridHeight,
        TextureFormat textureFormat
    )
    {
        return
            new TerrainHeightStreamingLevelDescriptor
            {
                sampleStride =
                    sampleStride,

                sampleSpacing =
                    sampleSpacing,

                samplesPerSide =
                    samplesPerSide,

                tileWorldSize =
                    tileWorldSize,

                tileGridWidth =
                    tileGridWidth,

                tileGridHeight =
                    tileGridHeight,

                textureFormat =
                    textureFormat
            };
    }

    private static bool IsPowerOfTwo(
        int value
    )
    {
        return
            value > 0
            &&
            (
                value &
                (value - 1)
            ) == 0;
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
}
