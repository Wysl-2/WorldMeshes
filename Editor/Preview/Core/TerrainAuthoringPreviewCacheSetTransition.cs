using System;
using System.Collections.Generic;
using UnityEngine;

internal enum TerrainAuthoringPreviewCachePublication
{
    NativePreview,
    PreparedHeightSet
}

// Read-only borrowed result. Only the preview service owns/disposes its cache.
internal sealed class TerrainAuthoringPreviewPreparedHeightCache
{
    public int Level { get; }
    public int SampleStride { get; }
    public RenderTexture HeightCache { get; }
    public TerrainHeightCacheWindow ResidentWindow { get; }
    public int SamplesPerSide { get; }
    public float SampleSpacing { get; }
    internal TerrainAuthoringPreviewPreparedHeightCache(TerrainAuthoringPreviewLodState state)
    {
        Level = state.Level;
        SampleStride = state.SampleStride;
        HeightCache = state.ActiveCache.HeightCache;
        ResidentWindow = new TerrainHeightCacheWindow(
            state.ActiveCache.CacheOriginTile, state.ActiveCache.CacheSize);
        SamplesPerSide = state.SamplesPerSide;
        SampleSpacing = state.SampleSpacing;
    }
}

// Resource owner and resumable work data; scheduling remains in the service.
internal sealed class TerrainAuthoringPreviewCacheSetTransition : IDisposable
{
    internal sealed class Entry
    {
        internal TerrainAuthoringPreviewLodResidencyPlan Plan;
        internal TerrainHeightCacheWindow Target;
        internal bool IsExpansion;
        internal TerrainAuthoringPreviewCache Source;
        internal long SourceAuthoringGeneration;
        internal TerrainAuthoringPreviewLodState Destination;
        internal TerrainAuthoringPreviewCacheTransition Transition;
        internal readonly Queue<Vector2Int> Composition = new Queue<Vector2Int>();
        internal bool Prepared;
        internal bool Finalized;
    }

    internal sealed class SourceGroup
    {
        internal Vector2Int Tile;
        internal readonly List<Entry> Destinations = new List<Entry>();
        internal int Cursor;
        internal float Distance;
    }

    internal readonly Entry[] Entries;
    internal readonly List<SourceGroup> SourceGroups = new List<SourceGroup>();
    internal readonly TerrainAuthoringPreviewCachePublication Publication;
    internal readonly string CommittedSignature;
    internal readonly string OverallSignature;
    internal readonly long AuthoringGeneration;
    internal readonly long OwnershipGeneration;
    internal readonly bool RebuildRequested;
    internal TerrainAuthoringPreviewResidencyPlan AcceptedPlan { get; private set; }
    internal long RequestGeneration { get; private set; }
    internal TerrainAuthoringPreviewTransitionState State = TerrainAuthoringPreviewTransitionState.Preparing;
    internal int PreparationCursor;
    internal int GroupCursor;
    internal Texture2D CurrentSource;
    internal readonly TerrainAuthoringPreviewHeightMaterializer Materializer =
        new TerrainAuthoringPreviewHeightMaterializer();
    internal int SourceLoads;
    internal int MaterializedSlices;
    internal int ComposedSlices;
    internal int RetainedCopies;
    internal int TotalWorkUnits;
    internal int CompletedWorkUnits;
    internal string Error = "";
    internal int LastUpdateAllocations;
    internal int LastUpdateLoads;
    internal int LastUpdateMaterializations;
    internal int LastUpdateCopies;
    internal int LastUpdateCompositions;
    internal bool InProgress => State != TerrainAuthoringPreviewTransitionState.Activated
        && State != TerrainAuthoringPreviewTransitionState.Cancelled
        && State != TerrainAuthoringPreviewTransitionState.Failed;
    internal bool Complete => Array.TrueForAll(Entries, e => e.Finalized
        && e.Destination != null && e.Destination.StagingCache != null
        && e.Destination.StagingCache.IsCompleteForActivation);

    internal TerrainAuthoringPreviewCacheSetTransition(
        TerrainAuthoringPreviewResidencyPlan plan, TerrainHeightCacheWindow[] targets,
        bool[] expansions, TerrainAuthoringPreviewCache[] sources, long[] sourceGenerations,
        TerrainAuthoringPreviewCachePublication publication, string committed, string overall,
        long authoringGeneration, long requestGeneration, long ownershipGeneration, bool rebuild, float tileWorldSize = 1f, Vector2? worldSizeXZ = null)
    {
        AcceptedPlan = plan.CreateSnapshot();
        Publication = publication;
        CommittedSignature = committed;
        OverallSignature = overall;
        AuthoringGeneration = authoringGeneration;
        RequestGeneration = requestGeneration;
        OwnershipGeneration = ownershipGeneration;
        RebuildRequested = rebuild;
        Entries = new Entry[plan.LevelCount];
        var groups = new Dictionary<Vector2Int, SourceGroup>();
        for (int i = 0; i < Entries.Length; i++)
        {
            var p = AcceptedPlan.Levels[i];
            var source = sources[i];
            bool hasSource = source != null && source.IsReady;
            if (!TerrainAuthoringPreviewCacheTransition.TryCreate(hasSource,
                hasSource ? new TerrainHeightCacheWindow(source.CacheOriginTile, source.CacheSize) : default,
                targets[i], committed, overall, out var child, out string error))
            {
                throw new ArgumentException(error);
            }
            child.RequestGeneration = requestGeneration;
            child.TargetAuthoringGeneration = authoringGeneration;
            child.CommittedRebuildRequested = rebuild;
            child.SourceTextureInstanceId = hasSource ? source.HeightCache.GetInstanceID() : 0;
            var entry = new Entry
            {
                Plan = p, Target = targets[i], IsExpansion = expansions[i], Source = source,
                SourceAuthoringGeneration = sourceGenerations[i], Transition = child,
                Destination = new TerrainAuthoringPreviewLodState(i, p.SampleStride, p.SamplesPerSide, p.SampleSpacing)
                {
                    RequestedRequiredWindow = p.RequiredWindow,
                    RequestedDesiredWindow = p.DesiredWindow,
                    RequestGeneration = requestGeneration, StagingGeneration = requestGeneration,
                    StagingAuthoringGeneration = authoringGeneration,
                    TransitionState = TerrainAuthoringPreviewLodTransitionState.WaitingForRequiredPages
                }
            };
            Entries[i] = entry;
            bool reusable = hasSource && !rebuild && sourceGenerations[i] == authoringGeneration
                && source.IsCompleteForActivation && source.SampleStride == p.SampleStride
                && source.SamplesPerSide == p.SamplesPerSide
                && Mathf.Approximately(source.SampleSpacing, p.SampleSpacing)
                && source.SourceCommittedHeightfieldSignature == committed
                && source.SourceOverallAuthoringSignature == overall
                && (!worldSizeXZ.HasValue || source.WorldSizeXZ == worldSizeXZ.Value);
            foreach (var tile in child.RetainedTiles)
            {
                if (reusable && source.IsSliceFinalCompositeReady(tile))
                    child.AddReusableRetainedTile(tile);
                else child.AddSourceMaterializationTile(tile);
            }
            foreach (var tile in child.EnteringTiles) child.AddSourceMaterializationTile(tile);
            foreach (var tile in child.SourceMaterializationTiles)
            {
                if (!groups.TryGetValue(tile, out var group))
                {
                    group = new SourceGroup { Tile = tile };
                    groups.Add(tile, group);
                }
                group.Destinations.Add(entry);
            }
            TotalWorkUnits += 2 + child.ReusableRetainedTiles.Count
                + child.SourceMaterializationTiles.Count * 2;
        }
        SourceGroups.AddRange(groups.Values);
        foreach (var group in SourceGroups)
        {
            group.Destinations.Sort((a, b) => a.Plan.Level.CompareTo(b.Plan.Level));
            var first = group.Destinations[0];
            var delta = new Vector2((group.Tile.x + 0.5f) * tileWorldSize, (group.Tile.y + 0.5f) * tileWorldSize) -
                new Vector2(first.Plan.Anchor.x, first.Plan.Anchor.z);
            group.Distance = delta.sqrMagnitude;
        }
        SourceGroups.Sort((a, b) =>
        {
            int order = a.Destinations[0].Plan.Level.CompareTo(b.Destinations[0].Plan.Level);
            if (order == 0) order = a.Distance.CompareTo(b.Distance);
            if (order == 0) order = a.Tile.y.CompareTo(b.Tile.y);
            return order == 0 ? a.Tile.x.CompareTo(b.Tile.x) : order;
        });
        TotalWorkUnits += SourceGroups.Count + 1; // geographic loads and one publication
    }

    internal void AcceptIntent(TerrainAuthoringPreviewResidencyPlan plan, long generation)
    {
        AcceptedPlan = plan.CreateSnapshot();
        RequestGeneration = generation;
        foreach (var e in Entries)
        {
            var p = AcceptedPlan.Levels[e.Plan.Level];
            e.Destination.RequestedRequiredWindow = p.RequiredWindow;
            e.Destination.RequestedDesiredWindow = p.DesiredWindow;
            e.Destination.RequestGeneration = generation;
            e.Transition.RequestGeneration = generation;
        }
    }

    internal bool MatchesContent(string committed, string overall, long authoring, long owner, bool rebuild)
    {
        return CommittedSignature == committed && OverallSignature == overall
            && AuthoringGeneration == authoring && OwnershipGeneration == owner
            && RebuildRequested == rebuild;
    }

    internal bool BorrowedSourcesAreValid()
    {
        foreach (var e in Entries)
        {
            if (!e.Transition.HasSourceWindow) continue;
            if (e.Source == null || !e.Source.IsReady || e.Source.HeightCache == null
                || e.Source.HeightCache.GetInstanceID() != e.Transition.SourceTextureInstanceId
                || e.Source.CacheOriginTile != e.Transition.SourceWindow.OriginTile
                || e.Source.CacheSize != e.Transition.SourceWindow.Size) return false;
            if (e.Transition.ReusableRetainedTiles.Count > 0
                && (!e.Source.IsCompleteForActivation || e.Source.SampleStride != e.Plan.SampleStride
                    || e.Source.SourceCommittedHeightfieldSignature != CommittedSignature
                    || e.Source.SourceOverallAuthoringSignature != OverallSignature)) return false;
        }
        return true;
    }

    internal TerrainAuthoringPreviewLodState[] TransferPreparedStates()
    {
        if (!Complete || State != TerrainAuthoringPreviewTransitionState.ReadyToActivate)
            throw new InvalidOperationException("The complete Height cache set is not ready for publication.");
        var result = new TerrainAuthoringPreviewLodState[Entries.Length];
        // Validate the whole set before moving any ownership.
        foreach (var e in Entries)
            if (e.Destination.ActiveCache != null)
                throw new InvalidOperationException("A destination already owns an active cache.");
        for (int i = 0; i < Entries.Length; i++)
        {
            result[i] = Entries[i].Destination;
            result[i].PromoteStagingCache();
            Entries[i].Destination = null;
        }
        State = TerrainAuthoringPreviewTransitionState.Activated;
        CompletedWorkUnits++;
        return result;
    }

    internal void ReleaseCurrentSource()
    {
        Materializer.ReleaseTextureBindings();
        CurrentSource = null;
    }

    public void Dispose()
    {
        ReleaseCurrentSource();
        foreach (var e in Entries)
        {
            e.Destination?.Dispose();
            e.Destination = null;
            e.Source = null; // borrowed, never disposed
            e.Composition.Clear();
        }
    }
}
