using UnityEngine;

public readonly struct TerrainAnalysisGenerationResult
{
    public readonly RenderTexture Texture;
    public readonly Vector2Int CacheOriginTile;
    public readonly Vector2Int CacheSize;
    public readonly int SamplesPerSide;
    public readonly float SampleSpacing;
    public readonly Vector2 WorldSizeXZ;
    public readonly string SourceSignature;
    public readonly int SourceHeightCacheInstanceId;

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
    {
        Texture = texture;
        CacheOriginTile = cacheOriginTile;
        CacheSize = cacheSize;
        SamplesPerSide = samplesPerSide;
        SampleSpacing = sampleSpacing;
        WorldSizeXZ = worldSizeXZ;
        SourceSignature = sourceSignature ?? "";
        SourceHeightCacheInstanceId =
            sourceHeightCacheInstanceId;
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
