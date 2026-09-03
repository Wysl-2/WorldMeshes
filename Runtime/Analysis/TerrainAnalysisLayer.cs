using UnityEngine;

public sealed class TerrainAnalysisLayer
{
    public TerrainAnalysisKey Key { get; }
    public RenderTexture Texture { get; private set; }
    public bool IsReady { get; private set; }
    public int Revision { get; private set; }

    public Vector2Int CacheOriginTile { get; private set; }
    public Vector2Int CacheSize { get; private set; }
    public int SamplesPerSide { get; private set; }
    public float SampleSpacing { get; private set; }
    public Vector2 WorldSizeXZ { get; private set; }

    public string SourceSignature { get; private set; }
    public string ErrorMessage { get; private set; }

    public int SliceCount
    {
        get
        {
            return
                Mathf.Max(0, CacheSize.x) *
                Mathf.Max(0, CacheSize.y);
        }
    }

    internal TerrainAnalysisLayer(
        TerrainAnalysisKey key
    )
    {
        Key = key;
        SourceSignature = "";
        ErrorMessage = "";
    }

    internal void SetResult(
        TerrainAnalysisGenerationResult result,
        int revision
    )
    {
        RenderTexture previousTexture = Texture;

        Texture = result.Texture;
        CacheOriginTile = result.CacheOriginTile;
        CacheSize = result.CacheSize;
        SamplesPerSide = result.SamplesPerSide;
        SampleSpacing = result.SampleSpacing;
        WorldSizeXZ = result.WorldSizeXZ;
        SourceSignature = result.SourceSignature;
        Revision = Mathf.Max(0, revision);
        IsReady = result.IsValid;
        ErrorMessage = "";

        if (
            previousTexture != null &&
            previousTexture != Texture
        )
        {
            DestroyRenderTexture(
                previousTexture
            );
        }
    }

    internal void SetGenerationError(
        string errorMessage
    )
    {
        IsReady = false;
        ErrorMessage = errorMessage ?? "";
    }

    internal void Invalidate()
    {
        IsReady = false;
        ErrorMessage = "";
    }

    internal void Reset()
    {
        DestroyRenderTexture(
            Texture
        );

        Texture = null;
        IsReady = false;
        Revision = 0;
        CacheOriginTile = Vector2Int.zero;
        CacheSize = Vector2Int.zero;
        SamplesPerSide = 0;
        SampleSpacing = 0f;
        WorldSizeXZ = Vector2.zero;
        SourceSignature = "";
        ErrorMessage = "";
    }

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

        if (Application.isPlaying)
        {
            Object.Destroy(texture);
        }
        else
        {
            Object.DestroyImmediate(texture);
        }
    }
}
