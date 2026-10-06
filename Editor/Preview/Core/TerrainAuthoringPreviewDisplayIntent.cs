using UnityEngine;

// Paired snapshots prevent a reusable residency plan from losing a newer placement.
internal sealed class TerrainAuthoringPreviewDisplayIntent
{
    internal TerrainAuthoringPreviewResidencyPlan Plan { get; }
    internal TerrainClipmapLayout Layout { get; }
    internal long PlacementGeneration { get; }
    internal long OwnershipGeneration { get; }
    internal Transform Root { get; }
    internal WorldSettings Settings { get; }
    private readonly string configurationSignature;
    internal bool ConfigurationMatches(WorldSettings settings) => settings == Settings
        && configurationSignature == CaptureConfiguration(settings);

    private static string CaptureConfiguration(WorldSettings settings)
    {
        if (settings == null) return "";
        var stamp = new System.Text.StringBuilder();
        stamp.Append(TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings).x).Append(':')
            .Append(TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings).y).Append(':')
            .Append(settings.HeightTileWorldSize).Append(':').Append(settings.HeightTileIntervalsPerSide)
            .Append(':').Append(TerrainHeightResolutionUtility.GetNativeSampleSpacing(settings))
            .Append(':').Append(settings.clipmapBaseSampleStep).Append(':').Append(settings.clipmapLevelCount);
        for (int i = 0; i < TerrainClipmapLayoutUtility.GetLevelCount(settings); i++)
        {
            stamp.Append('|').Append(TerrainClipmapTopologyUtility.GetLODOuterResolution(settings, i))
                .Append(':').Append(TerrainClipmapLayoutUtility.GetLODSpacing(settings, i));
            if (TerrainHeightResolutionUtility.TryGetRequiredStrideForClipmapLevel(settings, i, out int stride, out _))
                stamp.Append(':').Append(TerrainHeightStreamingPyramidPolicy.IsHeightRepresentationStrideSupported(settings, stride));
        }
        return stamp.ToString();
    }

    internal TerrainAuthoringPreviewDisplayIntent(WorldSettings settings, Transform root,
        TerrainAuthoringPreviewResidencyPlan plan, TerrainClipmapLayout layout,
        long placementGeneration, long ownershipGeneration)
    {
        Settings = settings; configurationSignature = CaptureConfiguration(settings);
        Root = root;
        Plan = plan.CreateSnapshot();
        Layout = layout.CreateSnapshot();
        PlacementGeneration = placementGeneration;
        OwnershipGeneration = ownershipGeneration;
    }

    internal static bool PlacementMatches(TerrainClipmapLayout a, TerrainClipmapLayout b)
    {
        if (a == null || b == null || !a.IsValid || !b.IsValid
            || a.LevelCount != b.LevelCount || a.MinimumXZ != b.MinimumXZ
            || a.MaximumXZ != b.MaximumXZ || a.CoverageCenter != b.CoverageCenter
            || a.Diameter != b.Diameter) return false;
        for (int i = 0; i < a.LevelCount; i++)
            if (a.GetAnchor(i) != b.GetAnchor(i) || a.GetSpacing(i) != b.GetSpacing(i)) return false;
        return true;
    }
}
