using UnityEngine;

/*
 * Represents one cached terrain-analysis result.
 *
 * Stage 1 establishes ownership and identity only. No analysis textures are
 * generated yet. Stage 2 will populate Texture and mark layers ready after
 * GPU analysis generation.
 *
 * Consumers may inspect a layer, but only Terrain Analysis infrastructure
 * should mutate its cached state.
 */
public sealed class TerrainAnalysisLayer
{
    public TerrainAnalysisKey Key
    {
        get;
    }

    public RenderTexture Texture
    {
        get;
        private set;
    }

    public bool IsReady
    {
        get;
        private set;
    }

    public int Revision
    {
        get;
        private set;
    }

    internal TerrainAnalysisLayer(
        TerrainAnalysisKey key
    )
    {
        Key =
            key;

        Texture =
            null;

        IsReady =
            false;

        Revision =
            0;
    }

    internal void SetResult(
        RenderTexture texture,
        int revision
    )
    {
        Texture =
            texture;

        Revision =
            Mathf.Max(
                0,
                revision
            );

        IsReady =
            texture !=
            null;
    }

    internal void Invalidate()
    {
        IsReady =
            false;
    }

    internal void Reset()
    {
        Texture =
            null;

        IsReady =
            false;

        Revision =
            0;
    }
}
