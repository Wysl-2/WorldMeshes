using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
internal enum TerrainAuthoringPreviewDisplayRequirement
{
    None = 0, Geometry = 1, SamplingDependency = 2, OptionalPrefetch = 4
}

[Flags]
internal enum TerrainAuthoringPreviewNativeWorkingReason
{
    None = 0, Analysis = 1, InteractiveAuthoring = 2
}

internal enum TerrainAuthoringPreviewDemandPriority
{
    NativeWorking, EditableDisplay, ContextualDisplay, SamplingDependency, OptionalPrefetch
}

internal readonly struct TerrainAuthoringPreviewNativeWorkingDemand
{
    internal TerrainHeightCacheWindow Window { get; }
    internal TerrainAuthoringPreviewNativeWorkingReason Reason { get; }
    internal TerrainAuthoringPreviewNativeWorkingDemand(TerrainHeightCacheWindow window,
        TerrainAuthoringPreviewNativeWorkingReason reason) { Window = window; Reason = reason; }
}

internal readonly struct TerrainAuthoringPreviewGeographicTileDemand : IEquatable<TerrainAuthoringPreviewGeographicTileDemand>
{
    internal Vector2Int Tile { get; }
    internal int FinestGeometryStride { get; }
    internal int SelectedDisplayStride { get; }
    internal bool IsEditable { get; }
    internal TerrainAuthoringPreviewDisplayRequirement DisplayRequirement { get; }
    internal TerrainAuthoringPreviewNativeWorkingReason NativeWorkingReason { get; }
    internal bool HasDisplay => DisplayRequirement != TerrainAuthoringPreviewDisplayRequirement.None;
    internal bool NativeWorkingRequired => NativeWorkingReason != TerrainAuthoringPreviewNativeWorkingReason.None;
    internal bool DisplayRequired => (DisplayRequirement & (TerrainAuthoringPreviewDisplayRequirement.Geometry
        | TerrainAuthoringPreviewDisplayRequirement.SamplingDependency)) != 0;
    internal TerrainAuthoringPreviewDemandPriority Priority => NativeWorkingRequired
        ? TerrainAuthoringPreviewDemandPriority.NativeWorking
        : !DisplayRequired ? TerrainAuthoringPreviewDemandPriority.OptionalPrefetch
        : (DisplayRequirement & TerrainAuthoringPreviewDisplayRequirement.Geometry) == 0
            ? TerrainAuthoringPreviewDemandPriority.SamplingDependency
            : IsEditable ? TerrainAuthoringPreviewDemandPriority.EditableDisplay : TerrainAuthoringPreviewDemandPriority.ContextualDisplay;

    internal TerrainAuthoringPreviewGeographicTileDemand(Vector2Int tile, int geometryStride, int displayStride,
        bool editable, TerrainAuthoringPreviewDisplayRequirement display, TerrainAuthoringPreviewNativeWorkingReason native)
    {
        Tile = tile; FinestGeometryStride = geometryStride; SelectedDisplayStride = displayStride;
        IsEditable = editable; DisplayRequirement = display; NativeWorkingReason = native;
    }

    public bool Equals(TerrainAuthoringPreviewGeographicTileDemand other) => Tile == other.Tile
        && FinestGeometryStride == other.FinestGeometryStride && SelectedDisplayStride == other.SelectedDisplayStride
        && IsEditable == other.IsEditable && DisplayRequirement == other.DisplayRequirement && NativeWorkingReason == other.NativeWorkingReason;
    public override bool Equals(object obj) => obj is TerrainAuthoringPreviewGeographicTileDemand other && Equals(other);
    public override int GetHashCode() => Tile.GetHashCode();
}

// Bounded immutable intent. Native-only rows have no display stride or fabricated page.
internal sealed class TerrainAuthoringPreviewGeographicDemandPlan
{
    private readonly TerrainAuthoringPreviewGeographicTileDemand[] tiles;
    private readonly Dictionary<Vector2Int, int> index;
    private readonly int[] outerResolutions;
    private readonly int gridWidth, gridHeight, tileSpan, resolution, baseStep;
    private readonly float chunkSize;
    internal int WorldIdentity { get; }
    internal long Generation { get; }
    internal long PolicyGeneration { get; }
    internal long PlacementGeneration { get; }
    internal long OwnershipGeneration { get; }
    internal long NativeWorkingGeneration { get; }
    internal TerrainAuthoringPreviewFocusKind FocusKind { get; }
    internal Vector2Int FocusTile { get; }
    internal TerrainHeightCacheWindow EditableWindow { get; }
    internal IReadOnlyList<TerrainAuthoringPreviewGeographicTileDemand> Tiles { get; }
    internal int RequiredDisplayCount { get; }
    internal int OptionalDisplayCount { get; }
    internal int EditableDisplayCount { get; }
    internal int ContextualDisplayCount { get; }
    internal int NativeWorkingCount { get; }
    internal string SelectedStrideDistribution { get; }
    internal string Description => "Planned geographical Height demand; not yet used by the active renderer.";

    internal TerrainAuthoringPreviewGeographicDemandPlan(WorldSettings settings,
        TerrainAuthoringPreviewQualitySnapshot policy, TerrainAuthoringPreviewFocus focus,
        Vector2Int focusTile, TerrainHeightCacheWindow window, long placement, long working, long generation,
        TerrainAuthoringPreviewGeographicTileDemand[] records)
    {
        WorldIdentity = settings.GetInstanceID(); PolicyGeneration = policy.Generation;
        OwnershipGeneration = focus.OwnershipGeneration; FocusKind = focus.Kind; FocusTile = focusTile;
        EditableWindow = window; PlacementGeneration = placement; NativeWorkingGeneration = working; Generation = generation;
        gridWidth = settings.gridWidth; gridHeight = settings.gridHeight; tileSpan = settings.heightTileChunkSpan;
        resolution = settings.heightfieldResolutionPerChunk; chunkSize = settings.chunkSize; baseStep = settings.clipmapBaseSampleStep;
        outerResolutions = new int[settings.clipmapLevelCount];
        for (int level = 0; level < outerResolutions.Length; level++)
            outerResolutions[level] = TerrainClipmapTopologyUtility.GetLODOuterResolution(settings, level);
        tiles = (TerrainAuthoringPreviewGeographicTileDemand[])records.Clone();
        Tiles = Array.AsReadOnly(tiles); index = new Dictionary<Vector2Int, int>(tiles.Length);
        var strides = new SortedDictionary<int, int>();
        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i]; index.Add(tile.Tile, i);
            if (tile.DisplayRequired) RequiredDisplayCount++;
            else if (tile.HasDisplay) OptionalDisplayCount++;
            if (tile.HasDisplay)
            {
                if (tile.IsEditable) EditableDisplayCount++; else ContextualDisplayCount++;
                strides.TryGetValue(tile.SelectedDisplayStride, out int count);
                strides[tile.SelectedDisplayStride] = count + 1;
            }
            if (tile.NativeWorkingRequired) NativeWorkingCount++;
        }
        var distribution = new System.Text.StringBuilder();
        foreach (var pair in strides)
        {
            if (distribution.Length > 0) distribution.Append("; ");
            distribution.Append(pair.Key).Append(": ").Append(pair.Value);
        }
        SelectedStrideDistribution = distribution.ToString();
    }

    internal bool ConfigurationMatches(WorldSettings settings) => settings != null
        && settings.GetInstanceID() == WorldIdentity && settings.gridWidth == gridWidth && settings.gridHeight == gridHeight
        && settings.heightTileChunkSpan == tileSpan && settings.heightfieldResolutionPerChunk == resolution
        && settings.chunkSize == chunkSize && settings.clipmapBaseSampleStep == baseStep
        && settings.clipmapLevelCount == outerResolutions.Length && OuterResolutionsMatch(settings);

    private bool OuterResolutionsMatch(WorldSettings settings)
    {
        for (int level = 0; level < outerResolutions.Length; level++)
            if (outerResolutions[level] != TerrainClipmapTopologyUtility.GetLODOuterResolution(settings, level)) return false;
        return true;
    }

    internal bool TryGetTile(Vector2Int tile, out TerrainAuthoringPreviewGeographicTileDemand demand)
    {
        if (index.TryGetValue(tile, out int i)) { demand = tiles[i]; return true; }
        demand = default; return false;
    }

    // The arrays and lookup are private, and every returned row is a readonly value.
    internal TerrainAuthoringPreviewGeographicDemandPlan CreateSnapshot() => this;

    internal bool IsEquivalentTo(TerrainAuthoringPreviewGeographicDemandPlan other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other == null || WorldIdentity != other.WorldIdentity || PolicyGeneration != other.PolicyGeneration
            || PlacementGeneration != other.PlacementGeneration || OwnershipGeneration != other.OwnershipGeneration
            || NativeWorkingGeneration != other.NativeWorkingGeneration || FocusKind != other.FocusKind
            || FocusTile != other.FocusTile || EditableWindow != other.EditableWindow || tiles.Length != other.tiles.Length
            || gridWidth != other.gridWidth || gridHeight != other.gridHeight || tileSpan != other.tileSpan
            || resolution != other.resolution || chunkSize != other.chunkSize || baseStep != other.baseStep
            || outerResolutions.Length != other.outerResolutions.Length) return false;
        for (int level = 0; level < outerResolutions.Length; level++)
            if (outerResolutions[level] != other.outerResolutions[level]) return false;
        for (int i = 0; i < tiles.Length; i++) if (!tiles[i].Equals(other.tiles[i])) return false;
        return true;
    }

    internal bool TryValidate(WorldSettings settings, out string error)
    {
        error = "";
        if (!ConfigurationMatches(settings) || !EditableWindow.IsValid
            || EditableWindow.OriginTile.x < 0 || EditableWindow.OriginTile.y < 0
            || (long)EditableWindow.OriginTile.x + EditableWindow.Width > settings.HeightTileGridWidth
            || (long)EditableWindow.OriginTile.y + EditableWindow.Height > settings.HeightTileGridHeight
            || !EditableWindow.Contains(FocusTile))
        { error = "Geographical Height demand belongs to a different world topology."; return false; }
        for (int i = 0; i < tiles.Length; i++)
        {
            var row = tiles[i];
            bool ordered = i == 0 || row.Tile.y > tiles[i - 1].Tile.y
                || row.Tile.y == tiles[i - 1].Tile.y && row.Tile.x > tiles[i - 1].Tile.x;
            if (!ordered || row.Tile.x < 0 || row.Tile.y < 0 || row.Tile.x >= settings.HeightTileGridWidth
                || row.Tile.y >= settings.HeightTileGridHeight || !row.HasDisplay && !row.NativeWorkingRequired
                || row.IsEditable != EditableWindow.Contains(row.Tile)
                || row.HasDisplay && (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, row.FinestGeometryStride)
                    || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, row.SelectedDisplayStride)
                    || row.SelectedDisplayStride < row.FinestGeometryStride)
                || !row.HasDisplay && (row.FinestGeometryStride != 0 || row.SelectedDisplayStride != 0))
            { error = "Invalid geographical Height requirement at " + row.Tile + "."; return false; }
        }
        return true;
    }
}
