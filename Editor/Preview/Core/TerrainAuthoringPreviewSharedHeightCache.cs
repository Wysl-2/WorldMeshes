using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit subordinate storage. No service subscriptions, source acquisition or live renderer ownership.
internal sealed class TerrainAuthoringPreviewSharedHeightCache : IDisposable
{
    private sealed class Page
    {
        internal TerrainAuthoringPreviewHeightPageHandle Handle;
        internal TerrainAuthoringPreviewHeightPageReadiness Readiness;
        internal string Committed;
        internal int SourceStride;
        internal long AcceptedDemand;
        internal bool Provisional;
        internal long Authoring;
        internal float Minimum, Maximum;
    }
    private sealed class TileState { internal Page Active, Pending; }
    private static long nextOwner;
    private const int MaximumSnapshots = 8;
    private readonly int threadId;
    private readonly WorldSettings settings;
    private readonly TerrainAuthoringPreviewGeographicDemandPlan topology;
    private readonly TerrainAuthoringPreviewQualitySnapshot policy;
    private readonly long ownership;
    private readonly int[] capacities;
    private readonly int[] samples;
    private readonly float[] spacings;
    private readonly TerrainAuthoringPreviewHeightPagePool[] pools;
    private readonly Dictionary<Vector2Int, TileState> tiles = new Dictionary<Vector2Int, TileState>();
    private readonly HashSet<TerrainAuthoringPreviewHeightPageMap> maps = new HashSet<TerrainAuthoringPreviewHeightPageMap>();
    private TerrainAuthoringPreviewGeographicDemandPlan demand;
    private string committedTarget;
    private long authoringTarget, nextAllocation, nextPage, lastRequest, authoringProofEpoch;
    internal long OwnerId { get; }
    internal long ResourceGeneration { get; }
    internal long MappingEpoch { get; private set; } = 1;
    internal long AllocatedBytes { get; private set; }
    internal long PeakAllocatedBytes { get; private set; }
    internal long LookupAllocatedBytes { get; private set; }
    internal long AnalysisAllocatedBytes { get; private set; }
    internal bool LastAdmissionBlocked { get; private set; }
    internal long UnallocatedPoolBytes(int stride)
    {
        RequireOwnerThread(); int index = PoolIndex(stride);
        if (index < 0 || index >= pools.Length || pools[index] != null) return 0;
        return TerrainAuthoringPreviewHeightPagePool.TryEstimateBytes(samples[index], capacities[index], out long bytes) ? bytes : long.MaxValue;
    }
    internal long PeakCombinedAllocatedBytes { get; private set; }
    internal long GpuBudgetBytes { get; }
    internal long AdmissionAttempts { get; private set; }
    internal long FailedAdmissions { get; private set; }
    internal bool IsDisposed { get; private set; }
    internal bool ReleaseComplete => IsDisposed && AllocatedBytes == 0 && LookupAllocatedBytes == 0 && AnalysisAllocatedBytes == 0;

    private TerrainAuthoringPreviewSharedHeightCache(WorldSettings settings,
        TerrainAuthoringPreviewQualitySnapshot policy, TerrainAuthoringPreviewGeographicDemandPlan plan,
        long resource, string committed, long authoring, int[] capacities)
    {
        threadId = Thread.CurrentThread.ManagedThreadId; this.settings = settings; this.policy = policy;
        topology = plan; demand = plan; ownership = plan.OwnershipGeneration; this.capacities = capacities;
        pools = new TerrainAuthoringPreviewHeightPagePool[capacities.Length];
        samples = new int[pools.Length]; spacings = new float[pools.Length];
        for (int i = 0; i < pools.Length; i++)
        { samples[i] = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1 << i);
          spacings[i] = TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1 << i); }
        OwnerId = Interlocked.Increment(ref nextOwner); ResourceGeneration = resource;
        committedTarget = committed; authoringTarget = authoring;
        GpuBudgetBytes = checked((long)policy.GpuMemoryBudgetMiB * 1024 * 1024);
    }

    // Binding index = log2(native-relative stride). One fixed array per index, never an array chain.
    // Default capacity permits active + replacement for each initially requested page of that stride.
    // Explicit capacities are owner construction limits, not new user preferences. Allocation remains lazy.
    internal static bool TryCreate(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot policy,
        TerrainAuthoringPreviewGeographicDemandPlan plan, long resourceGeneration, string committedSignature,
        long authoringGeneration, out TerrainAuthoringPreviewSharedHeightCache cache, out string error,
        IReadOnlyDictionary<int, int> capacityByStride = null)
    {
        cache = null; error = "";
        if (!TerrainAuthoringPreviewQualityPolicy.TryValidate(settings, policy, out var normalized, out error)
            || !normalized.HasSameValues(policy))
        { if (string.IsNullOrEmpty(error)) error = "Shared Height requires a validated quality snapshot."; return false; }
        if (plan == null || !plan.TryValidate(settings, out error))
        { if (string.IsNullOrEmpty(error)) error = "Shared Height requires geographical display demand."; return false; }
        if (resourceGeneration <= 0 || plan.Generation <= 0 || plan.OwnershipGeneration <= 0
            || plan.PolicyGeneration != policy.Generation || string.IsNullOrEmpty(committedSignature) || authoringGeneration < 0)
        { error = "Shared Height ownership, policy, demand and content generations are invalid."; return false; }
        var sizes = new List<int>();
        for (int stride = 1; TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride);)
        {
            int count = 0;
            foreach (var row in plan.Tiles) if (row.HasDisplay && row.SelectedDisplayStride == stride) count++;
            long capacity = Math.Max(2L, (long)count * 2);
            if (capacityByStride != null && capacityByStride.TryGetValue(stride, out int supplied)) capacity = supplied;
            if (capacity < 1 || capacity > int.MaxValue)
            { error = "Shared Height pool capacity must be a positive bounded integer."; return false; }
            sizes.Add((int)capacity);
            if (stride > int.MaxValue / 2) break;
            stride *= 2;
        }
        if (capacityByStride != null)
            foreach (var pair in capacityByStride)
                if (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, pair.Key))
                { error = "A Shared Height capacity was supplied for an unsupported stride."; return false; }
        cache = new TerrainAuthoringPreviewSharedHeightCache(settings, policy, plan, resourceGeneration,
            committedSignature, authoringGeneration, sizes.ToArray());
        return true;
    }

    private bool CheckAlive(out string error)
    {
        error = "";
        if (Thread.CurrentThread.ManagedThreadId != threadId)
        { error = "Shared Height GPU ownership must be accessed on its creating Unity main thread."; return false; }
        if (IsDisposed) { error = "Shared Height storage has been disposed."; return false; }
        if (!topology.ConfigurationMatches(settings)) { error = "Shared Height world topology has changed."; return false; }
        return true;
    }
    internal void RequireOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != threadId)
            throw new InvalidOperationException("Shared Height lifetime operations require the owning Unity main thread.");
    }
    private static int PoolIndex(int stride)
    {
        if (!TerrainHeightResolutionUtility.IsPowerOfTwo(stride)) return -1;
        int index = 0; while (stride > 1) { stride /= 2; index++; } return index;
    }
    private bool Owns(TerrainAuthoringPreviewHeightPageHandle handle) => handle.IsValid
        && handle.OwnerId == OwnerId && handle.ResourceGeneration == ResourceGeneration
        && handle.PoolIndex >= 0 && handle.PoolIndex < pools.Length;
    private bool TryPending(TerrainAuthoringPreviewHeightPageHandle handle, out Page page, out string error)
    {
        page = null;
        if (!CheckAlive(out error)) return false;
        if (!Owns(handle) || !tiles.TryGetValue(handle.Tile, out var state) || state.Pending == null
            || !state.Pending.Handle.Equals(handle) || state.Pending.AcceptedDemand != demand.Generation
            || !demand.TryGetTile(handle.Tile, out var row) || !row.HasDisplay || !CanPrepare(row, handle.Stride, state.Pending.Provisional)
            || pools[handle.PoolIndex] == null || !pools[handle.PoolIndex].Matches(handle, out _))
        { error = "The Height candidate is stale or belongs to another owner/demand."; return false; }
        page = state.Pending; return true;
    }

    internal bool CanPrepare(TerrainAuthoringPreviewGeographicTileDemand row, int stride, bool provisional) => row.HasDisplay
        && TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride)
        && (stride == row.SelectedDisplayStride || provisional && stride == Math.Max(row.SelectedDisplayStride, policy.ContextualMinimumStride));
    internal bool PendingTargetMatches(TerrainAuthoringPreviewHeightPageHandle handle) => TryPending(handle, out _, out _);

    // Demand adoption changes only the logical accepted revision, never physical handle identity.
    // Pages leaving display demand are retired, so the canonical dictionary stays coverage-bounded.
    internal bool TryAcceptDemand(TerrainAuthoringPreviewGeographicDemandPlan plan, string committedSignature,
        long authoringGeneration, out string error)
    {
        if (!CheckAlive(out error)) return false;
        if (plan == null || !plan.TryValidate(settings, out error))
        { if (string.IsNullOrEmpty(error)) error = "Shared Height demand is missing."; return false; }
        if (plan.OwnershipGeneration != ownership || plan.PolicyGeneration != policy.Generation
            || plan.Generation < demand.Generation || plan.Generation == demand.Generation && !plan.IsEquivalentTo(demand)
            || authoringGeneration < authoringTarget || string.IsNullOrEmpty(committedSignature))
        { error = "Shared Height demand/content revision is stale or has incompatible ownership/policy."; return false; }
        if (ReferenceEquals(plan, demand) && committedSignature == committedTarget && authoringGeneration == authoringTarget) return true;
        foreach (var pair in tiles)
        {
            var pending = pair.Value.Pending;
            if (pending != null)
            {
                if (committedSignature == committedTarget && authoringGeneration == authoringTarget
                    && plan.TryGetTile(pair.Key, out var next) && CanPrepare(next, pending.Handle.Stride, pending.Provisional))
                    pending.AcceptedDemand = plan.Generation;
                else { Retire(pending); pair.Value.Pending = null; }
            }
            if (!plan.TryGetTile(pair.Key, out var row) || !row.HasDisplay)
            { if (pair.Value.Active != null) Retire(pair.Value.Active); pair.Value.Active = null; }
        }
        RemoveEmptyTiles(); demand = plan; committedTarget = committedSignature; authoringTarget = authoringGeneration;
        MappingEpoch++; authoringProofEpoch++; CollectRetiredPages(); return true;
    }

    internal bool TryCaptureAuthoringTarget(WorldSettings world, TerrainAuthoringPreviewGeographicDemandPlan plan,
        out string committed, out long authoring, out string error)
    {
        committed = ""; authoring = 0;
        if (!CheckAlive(out error)) return false;
        if (!topology.ConfigurationMatches(world) || plan == null || plan.Generation != demand.Generation
            || !plan.IsEquivalentTo(demand))
        { error = "The shared authoring world/demand target is stale or belongs to another owner."; return false; }
        committed = committedTarget; authoring = authoringTarget; return true;
    }
    internal bool AuthoringTargetMatches(WorldSettings world, TerrainAuthoringPreviewGeographicDemandPlan plan,
        string committed, long authoring) => TryCaptureAuthoringTarget(world, plan, out string c, out long a, out _)
        && c == committed && a == authoring;

    // Receipt issued only when this owner accepts a validated complete transition.
    // Affected-set exclusion is checked here; callers cannot acknowledge with a boolean.
    internal sealed class UnchangedPageProof
    {
        private readonly TerrainAuthoringPreviewSharedHeightCache owner;
        private readonly long epoch;
        internal readonly TerrainAuthoringPreviewGeographicAuthoringProjection Projection;
        internal UnchangedPageProof(TerrainAuthoringPreviewSharedHeightCache owner, long epoch,
            TerrainAuthoringPreviewGeographicAuthoringProjection projection)
        { this.owner = owner; this.epoch = epoch; Projection = projection; }
        internal bool Matches(TerrainAuthoringPreviewSharedHeightCache candidate, long acceptedEpoch) =>
            ReferenceEquals(owner, candidate) && epoch == acceptedEpoch;
    }
    internal bool TryAcceptAuthoringProjection(TerrainAuthoringPreviewGeographicAuthoringProjection projection,
        out UnchangedPageProof proof, out string error)
    {
        proof = null;
        if (!CheckAlive(out error)) return false;
        if (projection == null || projection.OwnerId != OwnerId || projection.ResourceGeneration != ResourceGeneration
            || !AuthoringTargetMatches(projection.Settings, projection.Demand, projection.PreviousCommittedSignature,
                projection.PreviousGeneration) || projection.TargetGeneration <= authoringTarget)
        { error = "The geographical authoring projection does not cover this owner's exact current transition."; return false; }
        if (!TryAcceptDemand(projection.Demand, projection.CommittedSignature, projection.TargetGeneration, out error)) return false;
        proof = new UnchangedPageProof(this, authoringProofEpoch, projection); return true;
    }
    internal bool TryAcknowledgeUnchangedPage(Vector2Int tile, UnchangedPageProof proof, out string error)
    {
        if (!CheckAlive(out error)) return false;
        var projection = proof?.Projection;
        if (projection == null || !proof.Matches(this, authoringProofEpoch) || !projection.CanProveUntouched
            || projection.Affects(tile) || !AuthoringTargetMatches(projection.Settings, projection.Demand,
                projection.CommittedSignature, projection.TargetGeneration)
            || !demand.TryGetTile(tile, out var row) || !row.HasDisplay
            || !tiles.TryGetValue(tile, out var state) || state.Active == null || state.Pending != null)
        { error = "Untouched-page acknowledgement lacks a complete current affected-scope receipt or has unresolved tile work."; return false; }
        var page = state.Active;
        if (page.Readiness != TerrainAuthoringPreviewHeightPageReadiness.FinalComposite
            || page.Committed != committedTarget || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, page.Handle.Stride)
            || !pools[page.Handle.PoolIndex].Matches(page.Handle, out _)
            || page.Authoring != projection.PreviousGeneration && page.Authoring != projection.TargetGeneration)
        { error = "The published page is not a current unchanged predecessor with matching source/stride/allocation."; return false; }
        if (page.Authoring == projection.TargetGeneration) return true;
        page.Authoring = projection.TargetGeneration; MappingEpoch++; return true;
    }
    internal bool TryPollCandidate(TerrainAuthoringPreviewHeightPageHandle handle, out bool complete, out string error)
    {
        complete = false;
        return TryPending(handle, out _, out error) && pools[handle.PoolIndex].TryPollWrite(handle, out complete, out error);
    }
    // The caller owns request sequencing; this narrow query avoids two explicit
    // coordinators accidentally issuing the same token within one resource lifetime.
    internal long NextRequestToken { get { RequireOwnerThread(); return checked(lastRequest + 1); } }

    internal bool TryReservePage(Vector2Int tile, int stride, long requestToken, long demandGeneration,
        out TerrainAuthoringPreviewHeightPageHandle handle, out string error, bool provisional = false)
    {
        handle = default;
        LastAdmissionBlocked = false;
        if (!CheckAlive(out error)) return false;
        AdmissionAttempts++;
        if (!TryReserve(tile, stride, requestToken, demandGeneration, out handle, out error, provisional))
        { FailedAdmissions++; return false; }
        return true;
    }
    private bool TryReserve(Vector2Int tile, int stride, long requestToken, long demandGeneration,
        out TerrainAuthoringPreviewHeightPageHandle handle, out string error, bool provisional = false)
    {
        handle = default;
        if (!CheckAlive(out error)) return false;
        if (requestToken <= lastRequest || requestToken <= 0 || demandGeneration != demand.Generation
            || !demand.TryGetTile(tile, out var row) || !row.HasDisplay || !CanPrepare(row, stride, provisional))
        { error = "Height admission requires current display demand, selected stride and an increasing request token."; return false; }
        int index = PoolIndex(stride);
        if (index < 0 || index >= pools.Length || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride))
        { error = "Shared Height display stride is unsupported."; return false; }
        CollectRetiredPages();
        if (pools[index] == null)
        {
            int samples = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, stride);
            if (!TerrainAuthoringPreviewHeightPagePool.TryEstimateBytes(samples, capacities[index], out long bytes)
                || bytes > GpuBudgetBytes - AllocatedBytes - LookupAllocatedBytes - AnalysisAllocatedBytes)
            { LastAdmissionBlocked = true; error = "Shared Height allocated capacity would exceed its GPU budget or byte arithmetic limits."; return false; }
            if (!TerrainAuthoringPreviewHeightPagePool.TryCreate(stride, index, samples,
                TerrainHeightResolutionUtility.GetSampleSpacing(settings, stride), capacities[index], ++nextAllocation,
                ReleasedBytes, out var pool, out error)) return false;
            pools[index] = pool; AllocatedBytes += bytes; PeakAllocatedBytes = Math.Max(PeakAllocatedBytes, AllocatedBytes);
            PeakCombinedAllocatedBytes = Math.Max(PeakCombinedAllocatedBytes, AllocatedBytes + LookupAllocatedBytes + AnalysisAllocatedBytes);
        }
        var physical = pools[index];
        long pageGeneration = ++nextPage;
        if (!physical.TryReserve(pageGeneration, out int slice, out error))
        { LastAdmissionBlocked = physical.Capture().FreeCount == 0; return false; }
        handle = new TerrainAuthoringPreviewHeightPageHandle(OwnerId, ResourceGeneration, physical.AllocationGeneration,
            pageGeneration, requestToken, demand.Generation, tile, stride, index, slice);
        if (!tiles.TryGetValue(tile, out var state)) { state = new TileState(); tiles.Add(tile, state); }
        if (state.Pending != null) Retire(state.Pending);
        state.Pending = new Page { Handle = handle, AcceptedDemand = demand.Generation, Provisional = provisional }; lastRequest = requestToken;
        return true;
    }
    private void ReleasedBytes(long bytes) { AllocatedBytes -= bytes; }
    private void Retire(Page page) { pools[page.Handle.PoolIndex].Retire(page.Handle); }
    private void RemoveEmptyTiles()
    {
        var empty = new List<Vector2Int>();
        foreach (var pair in tiles) if (pair.Value.Active == null && pair.Value.Pending == null) empty.Add(pair.Key);
        foreach (var tile in empty) tiles.Remove(tile);
    }

    // Submit commands on the graphics queue, then dispose the writer before declaring output ready.
    // preserveCommittedBase is only for composition over an already declared base, never a new base upload.
    internal bool TryGetWritableCandidate(TerrainAuthoringPreviewHeightPageHandle handle,
        out TerrainAuthoringPreviewHeightPageWrite writer, out string error, bool preserveCommittedBase = false)
    {
        writer = null;
        if (!TryPending(handle, out var page, out error)) return false;
        if (preserveCommittedBase && page.Readiness == TerrainAuthoringPreviewHeightPageReadiness.Uninitialized)
        { error = "Height composition requires a declared committed base."; return false; }
        var pool = pools[handle.PoolIndex];
        if (!pool.TryBeginWrite(handle, out error)) return false;
        page.Readiness = preserveCommittedBase ? TerrainAuthoringPreviewHeightPageReadiness.CommittedBase
            : TerrainAuthoringPreviewHeightPageReadiness.Uninitialized;
        if (!preserveCommittedBase) { page.Committed = null; page.SourceStride = 0; }
        writer = new TerrainAuthoringPreviewHeightPageWrite(this, pool, handle); return true;
    }
    internal bool IsWritableHandle(TerrainAuthoringPreviewHeightPageHandle handle) => TryPending(handle, out _, out _);

    internal bool TryMarkCommittedBase(TerrainAuthoringPreviewHeightPageHandle handle, string committedSignature,
        int sourceStride, out string error)
    {
        if (!TryPending(handle, out var page, out error)) return false;
        if (committedSignature != committedTarget || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, sourceStride)
            || sourceStride > handle.Stride || !pools[handle.PoolIndex].CanPublish(handle, out error))
        { if (string.IsNullOrEmpty(error)) error = "Height committed source identity/stride is incompatible with the target."; return false; }
        page.Committed = committedSignature; page.SourceStride = sourceStride;
        page.Readiness = TerrainAuthoringPreviewHeightPageReadiness.CommittedBase; return true;
    }
    internal bool TryMarkFinalComposite(TerrainAuthoringPreviewHeightPageHandle handle, long authoringGeneration,
        float minimumHeight, float maximumHeight, out string error)
    {
        if (!TryPending(handle, out var page, out error)) return false;
        if (page.Readiness == TerrainAuthoringPreviewHeightPageReadiness.Uninitialized || page.Committed != committedTarget
            || authoringGeneration != authoringTarget || !Finite(minimumHeight) || !Finite(maximumHeight) || maximumHeight < minimumHeight)
        { error = "Height final output requires current committed/authoring metadata and finite ordered bounds."; return false; }
        if (!pools[handle.PoolIndex].CanPublish(handle, out error)) return false;
        page.Authoring = authoringGeneration; page.Minimum = minimumHeight; page.Maximum = maximumHeight;
        page.Readiness = TerrainAuthoringPreviewHeightPageReadiness.FinalComposite; return true;
    }
    internal bool TryCommitPage(TerrainAuthoringPreviewHeightPageHandle handle, out string error)
    {
        if (!TryPending(handle, out var page, out error)) return false;
        if (page.Readiness != TerrainAuthoringPreviewHeightPageReadiness.FinalComposite
            || page.Committed != committedTarget || page.Authoring != authoringTarget)
        { error = "Height candidate output is not final/current."; return false; }
        if (!pools[handle.PoolIndex].CanPublish(handle, out error)) return false;
        var state = tiles[handle.Tile]; var previous = state.Active;
        pools[handle.PoolIndex].Publish(handle); state.Active = page; state.Pending = null; MappingEpoch++;
        if (previous != null) Retire(previous);
        return true;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private TerrainAuthoringPreviewHeightPageDescriptor Describe(Page page)
    {
        var pool = pools[page.Handle.PoolIndex];
        return new TerrainAuthoringPreviewHeightPageDescriptor(page.Handle, page.SourceStride, pool.SamplesPerSide,
            pool.SampleSpacing, page.Committed, page.Authoring, page.Minimum, page.Maximum);
    }
    internal bool TryGetPublishedPage(Vector2Int tile, out TerrainAuthoringPreviewHeightPageDescriptor descriptor, out bool current)
    {
        descriptor = default; current = false;
        if (!CheckAlive(out _) || !tiles.TryGetValue(tile, out var state) || state.Active == null
            || !pools[state.Active.Handle.PoolIndex].Matches(state.Active.Handle, out _)) return false;
        var page = state.Active; descriptor = Describe(page);
        current = page.Committed == committedTarget && page.Authoring == authoringTarget
            && demand.TryGetTile(tile, out var row) && row.HasDisplay && row.SelectedDisplayStride == page.Handle.Stride;
        return true;
    }
    internal bool ReleaseCandidate(TerrainAuthoringPreviewHeightPageHandle handle, out string error)
    {
        if (!TryPending(handle, out var page, out error)) return false;
        Retire(page); tiles[handle.Tile].Pending = null; RemoveEmptyTiles(); return true;
    }
    internal bool ReleaseTile(Vector2Int tile, out string error)
    {
        if (!CheckAlive(out error)) return false;
        if (!tiles.TryGetValue(tile, out var state)) return true;
        if (state.Pending != null) Retire(state.Pending);
        if (state.Active != null) Retire(state.Active);
        tiles.Remove(tile); MappingEpoch++; return true;
    }

    internal IReadOnlyList<TerrainAuthoringPreviewHeightPoolSnapshot> CapturePoolInventory()
    {
        RequireOwnerThread();
        var inventory = new TerrainAuthoringPreviewHeightPoolSnapshot[pools.Length];
        for (int index = 0; index < pools.Length; index++)
        {
            int stride = 1 << index;
            inventory[index] = pools[index] != null ? pools[index].Capture()
                : new TerrainAuthoringPreviewHeightPoolSnapshot(index, stride,
                    samples[index], spacings[index], 0, capacities[index], 0, 0, 0, 0);
        }
        return Array.AsReadOnly(inventory);
    }
    internal bool TryCreateRenderMapSnapshot(out TerrainAuthoringPreviewHeightPageMap map, out string error)
    {
        map = null;
        if (!CheckAlive(out error)) return false;
        if (maps.Count >= MaximumSnapshots) { error = "Shared Height render-map snapshot limit reached; retire an old map first."; return false; }
        var entries = new List<TerrainAuthoringPreviewHeightPageMapEntry>(demand.RequiredDisplayCount + demand.OptionalDisplayCount);
        foreach (var row in demand.Tiles)
        {
            if (!row.HasDisplay) continue;
            TryGetPublishedPage(row.Tile, out var page, out bool current);
            entries.Add(new TerrainAuthoringPreviewHeightPageMapEntry(row.Tile, row.SelectedDisplayStride, page, current));
        }
        var inventory = CapturePoolInventory(); var poolSnapshots = new TerrainAuthoringPreviewHeightPoolSnapshot[inventory.Count];
        for (int i = 0; i < inventory.Count; i++) poolSnapshots[i] = inventory[i];
        map = new TerrainAuthoringPreviewHeightPageMap(this, entries.ToArray(), poolSnapshots, MappingEpoch, demand, ownership, committedTarget, authoringTarget);
        foreach (var entry in entries) if (entry.IsValid) pools[entry.PoolIndex].AddReader(entry.Page.Handle);
        maps.Add(map); return true;
    }
    internal bool MapConfigurationMatches(TerrainAuthoringPreviewHeightPageMap map, WorldSettings world) =>
        CheckAlive(out _) && maps.Contains(map) && topology.ConfigurationMatches(world);
    internal bool MapPolicyMatches(TerrainAuthoringPreviewHeightPageMap map, TerrainAuthoringPreviewQualitySnapshot quality) =>
        CheckAlive(out _) && maps.Contains(map) && policy.HasSameValues(quality) && policy.Generation == quality.Generation;
    internal bool MapTargetsAreCurrent(TerrainAuthoringPreviewHeightPageMap map) =>
        CheckAlive(out _) && maps.Contains(map) && demand.Generation == map.DemandGeneration && demand.IsEquivalentTo(map.Demand)
        && committedTarget == map.CommittedTarget && authoringTarget == map.AuthoringTarget;

    // The upload owns/destroys its lookup. This lease only charges its allocation for the
    // complete GPU lifetime, alongside page capacity, without a second residency registry.
    internal bool TryReserveLookupBytes(TerrainAuthoringPreviewHeightPageMap map, long bytes,
        out IDisposable allocation, out string error)
    {
        allocation = null;
        if (!CheckAlive(out error)) return false;
        if (!maps.Contains(map) || bytes <= 0 || bytes > GpuBudgetBytes - AllocatedBytes - LookupAllocatedBytes - AnalysisAllocatedBytes)
        { error = "Shared Height lookup capacity exceeds the remaining GPU budget or belongs to a retired map."; return false; }
        LookupAllocatedBytes += bytes;
        PeakCombinedAllocatedBytes = Math.Max(PeakCombinedAllocatedBytes, AllocatedBytes + LookupAllocatedBytes + AnalysisAllocatedBytes);
        allocation = new LookupAllocation(this, bytes); return true;
    }
    // Analysis owns a distinct contiguous native array, including candidate and retired
    // arrays. Borrowed shared pools are already charged once in AllocatedBytes.
    internal bool TryReserveAnalysisBytes(TerrainAuthoringPreviewCommittedSourceContext source, long bytes,
        out IDisposable allocation, out string error)
    {
        allocation = null;
        if (!CheckAlive(out error)) return false;
        if (!TerrainAuthoringPreviewSharedHeightComposer.TargetCurrent(this, source) || bytes <= 0
            || bytes > GpuBudgetBytes - AllocatedBytes - LookupAllocatedBytes - AnalysisAllocatedBytes)
        { error = "Native analysis capacity exceeds the remaining shared GPU budget or has a stale target."; return false; }
        AnalysisAllocatedBytes += bytes;
        PeakCombinedAllocatedBytes = Math.Max(PeakCombinedAllocatedBytes, AllocatedBytes + LookupAllocatedBytes + AnalysisAllocatedBytes);
        allocation = new AnalysisAllocation(this, bytes); return true;
    }
    private sealed class AnalysisAllocation : IDisposable
    {
        private TerrainAuthoringPreviewSharedHeightCache owner;
        private readonly long bytes;
        internal AnalysisAllocation(TerrainAuthoringPreviewSharedHeightCache owner, long bytes) { this.owner = owner; this.bytes = bytes; }
        public void Dispose()
        {
            if (owner == null) return;
            owner.RequireOwnerThread(); owner.AnalysisAllocatedBytes -= bytes; owner = null;
        }
    }
    private sealed class LookupAllocation : IDisposable
    {
        private TerrainAuthoringPreviewSharedHeightCache owner;
        private readonly long bytes;
        internal LookupAllocation(TerrainAuthoringPreviewSharedHeightCache owner, long bytes) { this.owner = owner; this.bytes = bytes; }
        public void Dispose()
        {
            if (owner == null) return;
            owner.RequireOwnerThread(); owner.LookupAllocatedBytes -= bytes; owner = null;
        }
    }
    internal bool TryResolveMapPool(TerrainAuthoringPreviewHeightPageMap map, int index, out RenderTexture texture, out string error)
    {
        texture = null;
        if (!CheckAlive(out error)) return false;
        if (!maps.Contains(map) || map.OwnerId != OwnerId || map.ResourceGeneration != ResourceGeneration
            || index < 0 || index >= pools.Length || pools[index] == null
            || !map.Pools[index].IsAllocated || map.Pools[index].AllocationGeneration != pools[index].AllocationGeneration
            || pools[index].Texture == null || !pools[index].Texture.IsCreated())
        { error = "The Height map cannot borrow this physical pool allocation."; return false; }
        bool referenced = false;
        foreach (var entry in map.Entries) if (entry.IsValid && entry.PoolIndex == index) { referenced = true; break; }
        if (!referenced) { error = "The Height map contains no drawable references to this pool."; return false; }
        texture = pools[index].Texture; return true;
    }
    internal void RetireMap(TerrainAuthoringPreviewHeightPageMap map, HashSet<int> gpuUsedPools)
    {
        RequireOwnerThread();
        if (!maps.Remove(map)) return;
        // A single completion fence covers all submitted reads of each bound array.
        var fences = new GraphicsFence[pools.Length]; var failed = new bool[pools.Length];
        foreach (int index in gpuUsedPools) fences[index] = pools[index].SealReads(out failed[index]);
        foreach (var entry in map.Entries)
            if (entry.IsValid) pools[entry.PoolIndex].RemoveReader(entry.Page.Handle,
                gpuUsedPools.Contains(entry.PoolIndex), fences[entry.PoolIndex], failed[entry.PoolIndex]);
        CollectRetiredPages();
    }
    internal void CollectRetiredPages()
    {
        RequireOwnerThread(); foreach (var pool in pools) pool?.CollectRetiring();
    }
    public void Dispose()
    {
        RequireOwnerThread();
        if (IsDisposed) { CollectRetiredPages(); return; }
        // Close maps first so submitted GPU reads receive retirement fences before owner invalidation.
        foreach (var map in new List<TerrainAuthoringPreviewHeightPageMap>(maps)) map.Dispose();
        IsDisposed = true; tiles.Clear(); demand = null; MappingEpoch++;
        foreach (var pool in pools) pool?.Dispose();
    }
    // Only an explicit isolated validation/shutdown boundary may block on GPU release.
    internal bool WaitForRelease(out string error)
    {
        RequireOwnerThread(); error = "";
        if (!IsDisposed) { error = "Dispose Shared Height storage before waiting for release."; return false; }
        bool complete = true; foreach (var pool in pools) if (pool != null && !pool.WaitForRelease()) complete = false;
        if (!complete) error = "Shared Height GPU release is still pending; close writers and collect after the graphics boundary completes.";
        if (LookupAllocatedBytes != 0 || AnalysisAllocatedBytes != 0) error = "Retire owned shared Height binding lookups and native analysis arrays before completing cache release.";
        return complete && AllocatedBytes == 0 && LookupAllocatedBytes == 0 && AnalysisAllocatedBytes == 0;
    }
}
