public static class TerrainHeightBlendModeUtility
{
    public static bool IsSupported(
        TerrainHeightBlendMode blendMode
    )
    {
        return
            blendMode == TerrainHeightBlendMode.Additive
            ||
            blendMode == TerrainHeightBlendMode.Max
            ||
            blendMode == TerrainHeightBlendMode.Min
            ||
            blendMode == TerrainHeightBlendMode.Replace;
    }

    public static TerrainHeightBlendMode Sanitize(
        TerrainHeightBlendMode blendMode
    )
    {
        return
            IsSupported(
                blendMode
            )
                ? blendMode
                : TerrainHeightBlendMode.Additive;
    }
}
