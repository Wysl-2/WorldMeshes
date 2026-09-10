public static class TerrainHeightBlendModeAuthoringUtility
{
    public const string BlendModeTooltip =
        "Controls how this stamp combines with terrain that already exists.";

    public const string HeightDeltaTooltip =
        "Signed vertical offset used by Additive mode. Positive values raise terrain and negative values lower terrain.";

    public const string TargetBaseHeightTooltip =
        "Absolute terrain elevation represented when the evaluated source height is 0.";

    public const string TargetHeightRangeTooltip =
        "Signed elevation range applied across normalized source height. May be positive, zero, or negative.";

    public const string FalloffTooltip =
        "Controls how the stamp's influence fades toward its footprint edge.";

    public const string SmoothingTooltip =
        "Controls filtering of the source height data before blend-mode evaluation.";

    public static bool IsSupported(
        TerrainHeightBlendMode blendMode
    )
    {
        return
            TerrainHeightBlendModeUtility
                .IsSupported(
                    blendMode
                );
    }

    public static bool UsesHeightDelta(
        TerrainHeightBlendMode blendMode
    )
    {
        return
            blendMode ==
                TerrainHeightBlendMode.Additive;
    }

    public static bool UsesTargetSurface(
        TerrainHeightBlendMode blendMode
    )
    {
        return
            blendMode == TerrainHeightBlendMode.Max
            ||
            blendMode == TerrainHeightBlendMode.Min
            ||
            blendMode == TerrainHeightBlendMode.Replace;
    }

    public static string GetDisplayName(
        TerrainHeightBlendMode blendMode
    )
    {
        switch (blendMode)
        {
            case TerrainHeightBlendMode.Additive:
                return "Additive";

            case TerrainHeightBlendMode.Max:
                return "Max";

            case TerrainHeightBlendMode.Min:
                return "Min";

            case TerrainHeightBlendMode.Replace:
                return "Replace";

            default:
                return
                    "Unsupported (" +
                    ((int)blendMode).ToString() +
                    ")";
        }
    }

    public static string GetDescription(
        TerrainHeightBlendMode blendMode
    )
    {
        switch (blendMode)
        {
            case TerrainHeightBlendMode.Additive:
                return
                    "Adds a signed height offset scaled by the evaluated source height and footprint influence.";

            case TerrainHeightBlendMode.Max:
                return
                    "Raises terrain toward the target surface, but never lowers it.";

            case TerrainHeightBlendMode.Min:
                return
                    "Lowers terrain toward the target surface, but never raises it.";

            case TerrainHeightBlendMode.Replace:
                return
                    "Blends terrain toward the target surface using the stamp footprint influence.";

            default:
                return
                    "This terrain height blend mode is not supported by the current authoring UI.";
        }
    }
}
