using System;
using System.Collections.Generic;
using UnityEngine;

internal readonly struct TerrainAuthoringPreviewGeographicAuthoringTarget
{
    internal readonly Vector2Int Tile;
    internal readonly int DisplayStride;
    internal readonly TerrainAuthoringPreviewDemandPriority Priority;
    internal readonly bool DisplayRequired, NativeRequired, Changed;
    internal readonly long AuthoringGeneration;
    internal readonly string CommittedSignature;
    internal TerrainAuthoringPreviewGeographicAuthoringTarget(TerrainAuthoringPreviewGeographicTileDemand row,
        bool changed, long generation, string committed)
    {
        Tile = row.Tile; DisplayStride = row.HasDisplay ? row.SelectedDisplayStride : 0;
        Priority = row.Priority; DisplayRequired = row.DisplayRequired;
        NativeRequired = row.NativeWorkingRequired; Changed = changed;
        AuthoringGeneration = generation; CommittedSignature = committed;
    }
}

// Immutable intent/evidence, never a second residency or dirty queue. Complete local
// inputs must cover the ENTIRE transition, including previous/new footprints and Undo.
// Null/incomplete input is conservative; an empty complete input is a real no-pixel change.
internal sealed class TerrainAuthoringPreviewGeographicAuthoringProjection
{
    private readonly HashSet<Vector2Int> affected;
    internal WorldSettings Settings { get; }
    internal TerrainAuthoringPreviewGeographicDemandPlan Demand { get; }
    internal long OwnerId { get; }
    internal long ResourceGeneration { get; }
    internal long PreviousGeneration { get; }
    internal long TargetGeneration { get; }
    internal string PreviousCommittedSignature { get; }
    internal string CommittedSignature { get; }
    internal bool CanProveUntouched { get; }
    internal int NonresidentLogicalChangeCount { get; }
    internal IReadOnlyList<TerrainAuthoringPreviewGeographicAuthoringTarget> Targets { get; }
    internal int DisplayJobCount { get; }
    internal int NativeChangedCount { get; }
    internal bool Affects(Vector2Int tile) => !CanProveUntouched || affected.Contains(tile);

    private TerrainAuthoringPreviewGeographicAuthoringProjection(WorldSettings settings,
        TerrainAuthoringPreviewGeographicDemandPlan demand, TerrainAuthoringPreviewSharedHeightCache cache,
        long previous, long target, string previousCommitted, string committed, bool complete,
        HashSet<Vector2Int> affected, int nonresident,
        TerrainAuthoringPreviewGeographicAuthoringTarget[] targets)
    {
        Settings = settings; Demand = demand; OwnerId = cache.OwnerId; ResourceGeneration = cache.ResourceGeneration;
        PreviousGeneration = previous; TargetGeneration = target;
        PreviousCommittedSignature = previousCommitted; CommittedSignature = committed;
        CanProveUntouched = complete; this.affected = new HashSet<Vector2Int>(affected);
        NonresidentLogicalChangeCount = nonresident;
        Targets = Array.AsReadOnly(targets);
        foreach (var row in targets) if (row.Changed)
        { if (row.DisplayStride != 0) DisplayJobCount++; if (row.NativeRequired) NativeChangedCount++; }
    }

    internal const int MaximumIncomingTiles = 131072;

    // Both collections are required for a complete local transition. Established tool
    // notifications may pass their already-unioned authoritative set as previousTiles
    // with an empty newTiles collection. Never mark a late/drained service snapshot complete.
    internal static bool TryProject(WorldSettings settings, TerrainAuthoringPreviewGeographicDemandPlan demand,
        TerrainAuthoringPreviewSharedHeightCache cache, long previousGeneration, long targetGeneration,
        string committedSignature, IReadOnlyCollection<Vector2Int> previousTiles, IReadOnlyCollection<Vector2Int> newTiles,
        TerrainRegionalElevationInvalidationScope regionalScope,
        out TerrainAuthoringPreviewGeographicAuthoringProjection projection, out string error)
    {
        projection = null; error = "";
        if (settings == null || demand == null || !demand.TryValidate(settings, out error) || cache == null
            || !cache.TryCaptureAuthoringTarget(settings, demand, out string previousCommitted, out long accepted, out error)
            || previousGeneration != accepted || targetGeneration <= previousGeneration || string.IsNullOrEmpty(committedSignature))
        { if (string.IsNullOrEmpty(error)) error = "Geographical authoring requires the exact accepted previous target and a newer complete transition."; return false; }
        if ((long)(previousTiles?.Count ?? 0) + (newTiles?.Count ?? 0) > MaximumIncomingTiles)
        { error = "The incoming geographical dirty scope exceeds its bounded snapshot limit; use conservative demanded scope."; return false; }
        bool complete = previousTiles != null && newTiles != null && previousCommitted == committedSignature
            && regionalScope.Kind != TerrainRegionalElevationInvalidationKind.WholeWorld;
        if (regionalScope.Kind != TerrainRegionalElevationInvalidationKind.None
            && regionalScope.Kind != TerrainRegionalElevationInvalidationKind.WorldBounds
            && regionalScope.Kind != TerrainRegionalElevationInvalidationKind.WholeWorld) complete = false;
        if (regionalScope.Kind == TerrainRegionalElevationInvalidationKind.WorldBounds
            && (!Finite(regionalScope.WorldBounds.min.x) || !Finite(regionalScope.WorldBounds.min.z)
                || !Finite(regionalScope.WorldBounds.max.x) || !Finite(regionalScope.WorldBounds.max.z))) complete = false;
        var affected = new HashSet<Vector2Int>();
        if (!AddTiles(settings, previousTiles, affected, out error) || !AddTiles(settings, newTiles, affected, out error)) return false;
        int nonresident = 0;
        foreach (var tile in affected) if (!demand.TryGetTile(tile, out _)) nonresident++;
        var targets = new List<TerrainAuthoringPreviewGeographicAuthoringTarget>(demand.Tiles.Count);
        foreach (var row in demand.Tiles)
        {
            // Regional policy intersects logical influence with requested geography;
            // it never enumerates a potentially whole-world affected rectangle.
            bool changed = !complete || affected.Contains(row.Tile)
                || TerrainRegionalElevationResidencyPolicy.ScopeAffectsTile(settings, regionalScope, row.Tile);
            if (changed) affected.Add(row.Tile);
            targets.Add(new TerrainAuthoringPreviewGeographicAuthoringTarget(row, changed, targetGeneration, committedSignature));
        }
        targets.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority)
            : a.Tile.y != b.Tile.y ? a.Tile.y.CompareTo(b.Tile.y) : a.Tile.x.CompareTo(b.Tile.x));
        projection = new TerrainAuthoringPreviewGeographicAuthoringProjection(settings, demand, cache,
            previousGeneration, targetGeneration, previousCommitted, committedSignature, complete, affected,
            nonresident, targets.ToArray());
        return true;
    }
    private static bool AddTiles(WorldSettings settings, IReadOnlyCollection<Vector2Int> input,
        HashSet<Vector2Int> output, out string error)
    {
        error = ""; if (input == null) return true;
        foreach (var tile in input)
        {
            if (tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth || tile.y >= settings.HeightTileGridHeight)
            { error = "An authoritative dirty coordinate is outside the geographical world grid: " + tile + "."; return false; }
            output.Add(tile);
        }
        return true;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
