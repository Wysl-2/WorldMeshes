using UnityEngine;

public readonly struct TerrainAnalysisGenerationResult
{
    public readonly RenderTexture Texture;

    /*
     * Valid public analysis output layout. This may be smaller than the source
     * height window because source-only guard tiles are intentionally excluded.
     */
    public readonly Vector2Int CacheOriginTile;
    public readonly Vector2Int CacheSize;

    /*
     * Physical source height-cache layout used while generating this result.
     */
    public readonly Vector2Int SourceCacheOriginTile;
    public readonly Vector2Int SourceCacheSize;

    public readonly int SamplesPerSide;
    public readonly float SampleSpacing;
    public readonly Vector2 WorldSizeXZ;
    public readonly string SourceSignature;
    public readonly int SourceHeightCacheInstanceId;
    public readonly long SourceResidencyGeneration;

    public TerrainAnalysisGenerationResult(
        RenderTexture texture,
        Vector2Int cacheOriginTile,
        Vector2Int cacheSize,
        int samplesPerSide,
        float sampleSpacing,
        Vector2 worldSizeXZ,
        string sourceSignature,
        int sourceHeightCacheInstanceId
    )
        : this(
            texture,
            cacheOriginTile,
            cacheSize,
            cacheOriginTile,
            cacheSize,
            samplesPerSide,
            sampleSpacing,
            worldSizeXZ,
            sourceSignature,
            sourceHeightCacheInstanceId,
            0
        )
    {
    }

    public TerrainAnalysisGenerationResult(
        RenderTexture texture,
        Vector2Int cacheOriginTile,
        Vector2Int cacheSize,
        Vector2Int sourceCacheOriginTile,
        Vector2Int sourceCacheSize,
        int samplesPerSide,
        float sampleSpacing,
        Vector2 worldSizeXZ,
        string sourceSignature,
        int sourceHeightCacheInstanceId,
        long sourceResidencyGeneration
    )
    {
        Texture = texture;
        CacheOriginTile = cacheOriginTile;
        CacheSize = cacheSize;
        SourceCacheOriginTile = sourceCacheOriginTile;
        SourceCacheSize = sourceCacheSize;
        SamplesPerSide = samplesPerSide;
        SampleSpacing = sampleSpacing;
        WorldSizeXZ = worldSizeXZ;
        SourceSignature = sourceSignature ?? "";
        SourceHeightCacheInstanceId =
            sourceHeightCacheInstanceId;
        SourceResidencyGeneration =
            sourceResidencyGeneration;
    }

    public int SliceCount
    {
        get
        {
            return
                Mathf.Max(0, CacheSize.x) *
                Mathf.Max(0, CacheSize.y);
        }
    }

    public bool IsValid
    {
        get
        {
            return
                Texture != null &&
                Texture.IsCreated() &&
                CacheSize.x > 0 &&
                CacheSize.y > 0 &&
                SourceCacheSize.x > 0 &&
                SourceCacheSize.y > 0 &&
                SamplesPerSide > 1 &&
                SampleSpacing > 0f &&
                WorldSizeXZ.x > 0f &&
                WorldSizeXZ.y > 0f &&
                SourceHeightCacheInstanceId != 0 &&
                Texture.width == SamplesPerSide &&
                Texture.height == SamplesPerSide &&
                Texture.volumeDepth == SliceCount;
        }
    }
}
