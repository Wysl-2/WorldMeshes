using System;
using System.Collections.Generic;
using UnityEngine;

internal enum TerrainAuthoringPreviewHeightPageReadiness { Uninitialized, CommittedBase, FinalComposite }

// Tokens identify the owner, immutable physical allocation and a particular use of its slice.
internal readonly struct TerrainAuthoringPreviewHeightPageHandle : IEquatable<TerrainAuthoringPreviewHeightPageHandle>
{
    internal long OwnerId { get; }
    internal long ResourceGeneration { get; }
    internal long AllocationGeneration { get; }
    internal long PageGeneration { get; }
    internal long RequestToken { get; }
    internal long DemandGeneration { get; }
    internal Vector2Int Tile { get; }
    internal int Stride { get; }
    private readonly int poolIndex, slice;
    internal bool IsValid => OwnerId > 0 && ResourceGeneration > 0 && AllocationGeneration > 0 && PageGeneration > 0;
    internal int PoolIndex => IsValid ? poolIndex : -1;
    internal int Slice => IsValid ? slice : -1;

    internal TerrainAuthoringPreviewHeightPageHandle(long owner, long resource, long allocation, long page,
        long request, long demand, Vector2Int tile, int stride, int pool, int slice)
    {
        OwnerId = owner; ResourceGeneration = resource; AllocationGeneration = allocation; PageGeneration = page;
        RequestToken = request; DemandGeneration = demand; Tile = tile; Stride = stride; poolIndex = pool; this.slice = slice;
    }
    public bool Equals(TerrainAuthoringPreviewHeightPageHandle other) => OwnerId == other.OwnerId
        && ResourceGeneration == other.ResourceGeneration && AllocationGeneration == other.AllocationGeneration
        && PageGeneration == other.PageGeneration && RequestToken == other.RequestToken && DemandGeneration == other.DemandGeneration
        && Tile == other.Tile && Stride == other.Stride && poolIndex == other.poolIndex && slice == other.slice;
    public override bool Equals(object obj) => obj is TerrainAuthoringPreviewHeightPageHandle other && Equals(other);
    public override int GetHashCode() => PageGeneration.GetHashCode();
}

internal readonly struct TerrainAuthoringPreviewHeightPageDescriptor
{
    internal TerrainAuthoringPreviewHeightPageHandle Handle { get; }
    internal int SourceStride { get; }
    internal int SamplesPerSide { get; }
    internal float SampleSpacing { get; }
    internal string CommittedSignature { get; }
    internal long AuthoringGeneration { get; }
    internal float MinimumHeight { get; }
    internal float MaximumHeight { get; }
    internal TerrainAuthoringPreviewHeightPageReadiness Readiness => IsDrawable
        ? TerrainAuthoringPreviewHeightPageReadiness.FinalComposite : TerrainAuthoringPreviewHeightPageReadiness.Uninitialized;
    internal bool IsDrawable => Handle.IsValid;
    internal TerrainAuthoringPreviewHeightPageDescriptor(TerrainAuthoringPreviewHeightPageHandle handle, int sourceStride,
        int samples, float spacing, string committed, long authoring, float minimum, float maximum)
    {
        Handle = handle; SourceStride = sourceStride; SamplesPerSide = samples; SampleSpacing = spacing;
        CommittedSignature = committed; AuthoringGeneration = authoring; MinimumHeight = minimum; MaximumHeight = maximum;
    }
}

internal readonly struct TerrainAuthoringPreviewHeightPageMapEntry
{
    internal Vector2Int Tile { get; }
    internal int RequestedStride { get; }
    internal TerrainAuthoringPreviewHeightPageDescriptor Page { get; }
    internal bool IsValid => Page.IsDrawable;
    internal bool IsCurrent { get; }
    internal int PoolIndex => IsValid ? Page.Handle.PoolIndex : -1;
    internal int Slice => IsValid ? Page.Handle.Slice : -1;
    internal TerrainAuthoringPreviewHeightPageMapEntry(Vector2Int tile, int requestedStride,
        TerrainAuthoringPreviewHeightPageDescriptor page, bool current)
    { Tile = tile; RequestedStride = requestedStride; Page = page; IsCurrent = page.IsDrawable && current; }
}

internal readonly struct TerrainAuthoringPreviewHeightPoolSnapshot
{
    internal int PoolIndex { get; }
    internal int Stride { get; }
    internal int SamplesPerSide { get; }
    internal float SampleSpacing { get; }
    internal long AllocationGeneration { get; }
    internal int Capacity { get; }
    internal int ActiveCount { get; }
    internal int ReservedCount { get; }
    internal int RetiringCount { get; }
    internal int FreeCount => Capacity - ActiveCount - ReservedCount - RetiringCount;
    internal long AllocatedBytes { get; }
    internal bool IsAllocated => AllocationGeneration > 0 && AllocatedBytes > 0;
    internal TerrainAuthoringPreviewHeightPoolSnapshot(int index, int stride, int samples, float spacing, long allocation,
        int capacity, int active, int reserved, int retiring, long bytes)
    {
        PoolIndex = index; Stride = stride; SamplesPerSide = samples; SampleSpacing = spacing;
        AllocationGeneration = allocation; Capacity = capacity; ActiveCount = active; ReservedCount = reserved;
        RetiringCount = retiring; AllocatedBytes = bytes;
    }
}

// Sorted sparse lookup: binary search by (Z, X). No whole-world texture or dense map.
// Currentness describes the captured target, not a later authoring/demand revision.
internal sealed class TerrainAuthoringPreviewHeightPageMap : IDisposable
{
    private TerrainAuthoringPreviewSharedHeightCache owner;
    private readonly TerrainAuthoringPreviewHeightPageMapEntry[] entries;
    internal long OwnerId { get; }
    internal long ResourceGeneration { get; }
    internal long OwnershipGeneration { get; }
    internal long MappingEpoch { get; }
    internal long DemandGeneration { get; }
    internal IReadOnlyList<TerrainAuthoringPreviewHeightPageMapEntry> Entries { get; }
    internal IReadOnlyList<TerrainAuthoringPreviewHeightPoolSnapshot> Pools { get; }
    internal bool IsAlive => owner != null && !owner.IsDisposed;
    private readonly HashSet<int> gpuUsedPools = new HashSet<int>();

    internal TerrainAuthoringPreviewHeightPageMap(TerrainAuthoringPreviewSharedHeightCache owner,
        TerrainAuthoringPreviewHeightPageMapEntry[] entries, TerrainAuthoringPreviewHeightPoolSnapshot[] pools,
        long epoch, long demand, long ownership)
    {
        this.owner = owner; OwnerId = owner.OwnerId; ResourceGeneration = owner.ResourceGeneration;
        MappingEpoch = epoch; DemandGeneration = demand; OwnershipGeneration = ownership;
        this.entries = (TerrainAuthoringPreviewHeightPageMapEntry[])entries.Clone();
        Entries = Array.AsReadOnly(this.entries); Pools = Array.AsReadOnly((TerrainAuthoringPreviewHeightPoolSnapshot[])pools.Clone());
    }

    internal bool TryGetEntry(Vector2Int tile, out TerrainAuthoringPreviewHeightPageMapEntry entry)
    {
        entry = default;
        if (!IsAlive) return false;
        int low = 0, high = entries.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            Vector2Int candidate = entries[mid].Tile;
            int order = candidate.y != tile.y ? candidate.y.CompareTo(tile.y) : candidate.x.CompareTo(tile.x);
            if (order == 0) { entry = entries[mid]; return true; }
            if (order < 0) low = mid + 1; else high = mid - 1;
        }
        return false;
    }

    // Borrow only while this map is alive. Submit all GPU reads on Unity's graphics queue
    // before Dispose; retirement fences cover those submitted reads. Async compute must be joined first.
    internal bool TryGetPoolTexture(int poolIndex, out RenderTexture texture, out string error)
    {
        texture = null; error = "The Height page map has been retired.";
        if (!IsAlive || !owner.TryResolveMapPool(this, poolIndex, out texture, out error)) return false;
        gpuUsedPools.Add(poolIndex); return true;
    }

    public void Dispose()
    {
        var previous = owner;
        if (previous == null) return;
        previous.RetireMap(this, gpuUsedPools);
        owner = null; gpuUsedPools.Clear();
    }
}

// A writer borrows an unpublished slice, never a drawable page. Dispose seals submitted
// CopyTexture/Dispatch commands with a fence; it does not mark base/composite metadata ready.
internal sealed class TerrainAuthoringPreviewHeightPageWrite : IDisposable
{
    private TerrainAuthoringPreviewSharedHeightCache owner;
    private readonly TerrainAuthoringPreviewHeightPagePool pool;
    internal TerrainAuthoringPreviewHeightPageHandle Handle { get; }
    internal int Slice => Handle.Slice;
    internal RenderTexture Array
    {
        get
        {
            if (owner == null || !owner.IsWritableHandle(Handle)) throw new InvalidOperationException("The Height page writer is no longer current.");
            return pool.Texture;
        }
    }
    internal TerrainAuthoringPreviewHeightPageWrite(TerrainAuthoringPreviewSharedHeightCache owner,
        TerrainAuthoringPreviewHeightPagePool pool, TerrainAuthoringPreviewHeightPageHandle handle)
    { this.owner = owner; this.pool = pool; Handle = handle; }
    public void Dispose()
    {
        if (owner == null) return;
        owner.RequireOwnerThread();
        pool.EndWrite(Handle); owner = null;
    }
}
