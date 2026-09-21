using UnityEngine;

/*
 * Immutable description of one bounded GPU height source used by Terrain
 * Analysis. SourceWindow describes the physical height slices. Analysis
 * output may intentionally cover a smaller window so source-only guard tiles
 * can provide numerically valid neighbour samples without becoming public
 * analysis slices.
 */
public readonly struct TerrainAnalysisGpuSource
{
    public RenderTexture HeightCache { get; }
    public TerrainHeightCacheWindow SourceWindow { get; }
    public Vector2Int WorldTileGridSize { get; }
    public int SamplesPerSide { get; }
    public float SampleSpacing { get; }
    public Vector2 WorldSizeXZ { get; }
    public string SourceSignature { get; }
    public int SourceResourceIdentity { get; }
    public long ResidencyGeneration { get; }
    public long CompositeGeneration { get; }

    public TerrainAnalysisGpuSource(
        RenderTexture heightCache,
        TerrainHeightCacheWindow sourceWindow,
        Vector2Int worldTileGridSize,
        int samplesPerSide,
        float sampleSpacing,
        Vector2 worldSizeXZ,
        string sourceSignature,
        int sourceResourceIdentity,
        long residencyGeneration = 0,
        long compositeGeneration = 0
    )
    {
        HeightCache = heightCache;
        SourceWindow = sourceWindow;
        WorldTileGridSize = worldTileGridSize;
        SamplesPerSide = samplesPerSide;
        SampleSpacing = sampleSpacing;
        WorldSizeXZ = worldSizeXZ;
        SourceSignature = sourceSignature ?? "";
        SourceResourceIdentity = sourceResourceIdentity;
        ResidencyGeneration = residencyGeneration;
        CompositeGeneration = compositeGeneration;
    }

    public int SourceSliceCount =>
        SourceWindow.TileCount;

    public bool IsValid =>
        HeightCache != null
        &&
        HeightCache.IsCreated()
        &&
        SourceWindow.IsValid
        &&
        WorldTileGridSize.x > 0
        &&
        WorldTileGridSize.y > 0
        &&
        SamplesPerSide > 1
        &&
        SampleSpacing > 0f
        &&
        WorldSizeXZ.x > 0f
        &&
        WorldSizeXZ.y > 0f
        &&
        SourceResourceIdentity != 0
        &&
        HeightCache.width == SamplesPerSide
        &&
        HeightCache.height == SamplesPerSide
        &&
        HeightCache.volumeDepth == SourceSliceCount;
}
