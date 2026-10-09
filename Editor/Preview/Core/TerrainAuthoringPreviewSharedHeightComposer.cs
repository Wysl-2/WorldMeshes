using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Immutable target context. Production acquisition uses derived committed BASE
// sources; an explicit fixture may inject transient sources without touching assets.
internal interface TerrainAuthoringPreviewCommittedSourceContext
{
    WorldSettings Settings { get; }
    TerrainAuthoringData Data { get; }
    TerrainAuthoringPreviewGeographicDemandPlan Demand { get; }
    string CommittedSignature { get; }
    long AuthoringGeneration { get; }
    bool IsCurrent { get; }
    bool TryAcquire(Vector2Int tile, int stride, out TerrainAuthoringPreviewHeightSourceLease lease, out string error);
    bool LeaseIsCurrent(TerrainAuthoringPreviewHeightSourceLease lease);
    bool TryGetRange(Vector2Int tile, out float minimum, out float maximum, out string error);
}

internal enum TerrainAuthoringPreviewSharedCompositionState
{
    Prepared, WaitingForBase, WaitingForComposite, Published, NativeComplete, Failed, Cancelled
}

internal readonly struct TerrainAuthoringPreviewSharedCompositionResult
{
    internal readonly Vector2Int Tile;
    internal readonly int Stride;
    internal readonly long AuthoringGeneration;
    internal readonly string CommittedSignature;
    internal readonly float Minimum, Maximum;
    internal readonly bool SourceWasNative;
    internal readonly TerrainAuthoringPreviewSharedCompositionState State;
    internal readonly string Error;
    internal bool Succeeded => State == TerrainAuthoringPreviewSharedCompositionState.Published
        || State == TerrainAuthoringPreviewSharedCompositionState.NativeComplete;
    internal TerrainAuthoringPreviewSharedCompositionResult(Vector2Int tile, int stride, long generation,
        string committed, float minimum, float maximum, bool native,
        TerrainAuthoringPreviewSharedCompositionState state, string error)
    {
        Tile = tile; Stride = stride; AuthoringGeneration = generation; CommittedSignature = committed;
        Minimum = minimum; Maximum = maximum; SourceWasNative = native; State = state; Error = error ?? "";
    }
}

// Explicit caller-owned bounded work, not an Editor update subscription. Page identity
// stays solely in SharedHeightCache; this dictionary holds jobs/failures for one target.
// The caller dedicates compositor/materializer resources to this captured target;
// they remain alive and unmodified through ReleaseComplete. A new target needs fresh
// compositor resources while older authoring commands are still pending.
internal sealed class TerrainAuthoringPreviewSharedHeightComposer : IDisposable
{
    private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
    private readonly TerrainAuthoringPreviewSharedHeightCache cache;
    private readonly TerrainAuthoringPreviewCommittedSourceContext source;
    private readonly TerrainHeightCompositor compositor;
    private readonly TerrainAuthoringPreviewHeightMaterializer materializer;
    private readonly int maximumJobs;
    private readonly Dictionary<Vector2Int, TileWork> jobs = new Dictionary<Vector2Int, TileWork>();
    private readonly List<TileWork> retiring = new List<TileWork>();
    private bool disposed;
    internal int DisplayCompositionCount { get; private set; }
    internal int BlockedAdmissionCount { get; private set; }
    internal bool ReleaseComplete { get { CollectRelease(); return disposed && retiring.Count == 0; } }

    private TerrainAuthoringPreviewSharedHeightComposer(TerrainAuthoringPreviewSharedHeightCache cache,
        TerrainAuthoringPreviewCommittedSourceContext source, TerrainHeightCompositor compositor,
        TerrainAuthoringPreviewHeightMaterializer materializer, int maximumJobs)
    { this.cache = cache; this.source = source; this.compositor = compositor; this.materializer = materializer; this.maximumJobs = maximumJobs; }

    internal static bool TryCreate(TerrainAuthoringPreviewSharedHeightCache cache,
        TerrainAuthoringPreviewCommittedSourceContext source, TerrainHeightCompositor compositor,
        TerrainAuthoringPreviewHeightMaterializer materializer, out TerrainAuthoringPreviewSharedHeightComposer worker,
        out string error, int maximumJobs = 8)
    {
        worker = null; error = "";
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out error)
            || !TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(2, 1, out error)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat))
        { if (string.IsNullOrEmpty(error)) error = "Committed Height RFloat textures are unsupported."; return false; }
        if (!TargetCurrent(cache, source) || compositor == null || materializer == null || maximumJobs < 1 || maximumJobs > 64
            || cache.CapturePoolInventory().Count > TerrainAuthoringPreviewSharedHeightBindingData.MaximumPoolCount)
        { error = "Shared display composition requires a current target, prepared owners and a supported bounded pool/job inventory."; return false; }
        if (!materializer.TryPrepare(out error) || !compositor.TryPrepare(out error)) return false;
        worker = new TerrainAuthoringPreviewSharedHeightComposer(cache, source, compositor, materializer, maximumJobs); return true;
    }
    internal static bool TargetCurrent(TerrainAuthoringPreviewSharedHeightCache cache, TerrainAuthoringPreviewCommittedSourceContext source) =>
        cache != null && source != null && source.Settings != null && source.Data != null && source.Demand != null
        && source.IsCurrent && cache.AuthoringTargetMatches(source.Settings, source.Demand, source.CommittedSignature, source.AuthoringGeneration);

    internal bool TryBeginTile(Vector2Int tile, out TileWork work, out string error, bool retry = false)
    {
        RequireThread(); CollectRelease(); work = null; error = "";
        if (disposed || !TargetCurrent(cache, source) || !source.Demand.TryGetTile(tile, out var row) || !row.HasDisplay)
        { error = "The shared display request is stale, outside display demand, or native-only."; return false; }
        if (jobs.TryGetValue(tile, out var existing))
        {
            if (!retry) { work = existing; return true; } // Same-target failure is retained until explicit retry.
            existing.Dispose(); if (!existing.ReleaseComplete) retiring.Add(existing); jobs.Remove(tile);
        }
        if (jobs.Count + retiring.Count >= maximumJobs)
        { BlockedAdmissionCount++; error = "The bounded shared composer is waiting for job/result retirement."; return false; }
        if (!cache.TryReservePage(tile, row.SelectedDisplayStride, cache.NextRequestToken, source.Demand.Generation,
            out var handle, out error)) { BlockedAdmissionCount++; return false; }
        work = new TileWork(cache, source, compositor, materializer, tile, row.SelectedDisplayStride, handle, null, 0);
        jobs.Add(tile, work); return true;
    }
    internal bool TryStep(Vector2Int tile, out TerrainAuthoringPreviewSharedCompositionResult result, out string error)
    {
        RequireThread(); CollectRelease(); result = default; error = "";
        if (disposed || !jobs.TryGetValue(tile, out var work)) { error = "No shared composition job exists for this tile."; return false; }
        bool before = work.Result.Succeeded; work.Step(); result = work.Result;
        if (!before && result.State == TerrainAuthoringPreviewSharedCompositionState.Published) DisplayCompositionCount++;
        return true;
    }
    internal void RetireResult(Vector2Int tile)
    {
        RequireThread(); if (!jobs.TryGetValue(tile, out var work)) return;
        jobs.Remove(tile); work.Dispose(); if (!work.ReleaseComplete) retiring.Add(work);
    }
    private void CollectRelease()
    {
        RequireThread(); for (int i = retiring.Count - 1; i >= 0; i--)
            if (retiring[i].ReleaseComplete) retiring.RemoveAt(i);
    }
    private void RequireThread()
    { if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Shared authoring work requires its creating Unity main thread."); }
    public void Dispose()
    {
        RequireThread(); if (!disposed)
        { disposed = true; foreach (var work in jobs.Values) { work.Dispose(); if (!work.ReleaseComplete) retiring.Add(work); } jobs.Clear(); }
        CollectRelease();
    }
    internal bool WaitForRelease(out string error)
    {
        RequireThread(); error = ""; if (!disposed) { error = "Dispose shared authoring work before its explicit release wait."; return false; }
        foreach (var work in retiring) if (!work.WaitForRelease(out error)) return false;
        CollectRelease(); return retiring.Count == 0;
    }

    // Validate the manifest/ranges once at the explicit target boundary, not per tile.
    // The caller supplies its live generation/ownership probes; no service subscription.
    internal static bool TryCreateCommittedSourceContext(WorldSettings settings, TerrainAuthoringData data,
        TerrainAuthoringPreviewGeographicDemandPlan demand, long targetGeneration,
        Func<long> currentGeneration, Func<long> currentOwnership,
        out TerrainAuthoringPreviewCommittedSourceContext source, out string error)
    {
        source = null; error = "";
        if (settings == null || data == null || demand == null || !demand.TryValidate(settings, out error)
            || currentGeneration == null || currentOwnership == null || targetGeneration < 0
            || currentGeneration() != targetGeneration || currentOwnership() != demand.OwnershipGeneration)
        { if (string.IsNullOrEmpty(error)) error = "Committed source context requires current authoring/ownership probes and geographical demand."; return false; }
        if (!TerrainAuthoringStateUtility.TryValidateCommittedHeightfield(settings, data,
            TerrainAuthoringHeightfieldValidationMode.Operational, out var manifest, out _, out error)) return false;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        if (string.IsNullOrEmpty(committed)) { error = "The committed Height identity is unavailable."; return false; }
        var candidate = new CommittedSource(settings, data, demand, manifest, committed, targetGeneration,
            currentGeneration, currentOwnership);
        if (!candidate.IsCurrent) { error = "Authoring/source ownership changed during committed-source validation."; return false; }
        source = candidate; return true;
    }
    private sealed class CommittedSource : TerrainAuthoringPreviewCommittedSourceContext
    {
        public WorldSettings Settings { get; }
        public TerrainAuthoringData Data { get; }
        public TerrainAuthoringPreviewGeographicDemandPlan Demand { get; }
        public string CommittedSignature { get; }
        public long AuthoringGeneration { get; }
        private readonly TerrainAuthoringHeightManifest manifest;
        private readonly string overallSignature;
        private readonly Func<long> generation, ownership;
        internal CommittedSource(WorldSettings settings, TerrainAuthoringData data, TerrainAuthoringPreviewGeographicDemandPlan demand,
            TerrainAuthoringHeightManifest manifest, string committed, long authoring, Func<long> generation, Func<long> ownership)
        {
            Settings = settings; Data = data; Demand = demand; this.manifest = manifest;
            CommittedSignature = committed; AuthoringGeneration = authoring; this.generation = generation; this.ownership = ownership;
            overallSignature = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        }
        public bool IsCurrent => Settings != null && Data != null && manifest != null && Demand.ConfigurationMatches(Settings)
            && !Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode
            && generation() == AuthoringGeneration && ownership() == Demand.OwnershipGeneration
            && TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(Settings) == CommittedSignature
            && TerrainAuthoringStateUtility.GetOverallAuthoringSignature(Settings, Data) == overallSignature;
        public bool TryAcquire(Vector2Int tile, int stride, out TerrainAuthoringPreviewHeightSourceLease lease, out string error)
        {
            lease = null; error = "The committed authoring target is stale."; if (!IsCurrent) return false;
            Texture2D native = null;
            // Modifier/native-analysis work reads derived BASE entries but never generates/writes cache files.
            return TerrainAuthoringPreviewHeightSourceUtility.TryAcquireCommittedSource(Settings, tile, stride,
                ref native, false, out lease, out error);
        }
        // Optional source-identity I/O failure can still serve the legacy native loader,
        // but cannot prove this new transaction current. Retain last-good rather than
        // publish pixels whose exact committed identity cannot be revalidated.
        public bool LeaseIsCurrent(TerrainAuthoringPreviewHeightSourceLease lease) => IsCurrent && lease != null
            && lease.HasIdentity && lease.CommittedSignature == CommittedSignature
            && TerrainAuthoringPreviewHeightSourceUtility.IsCurrent(Settings, lease);
        public bool TryGetRange(Vector2Int tile, out float minimum, out float maximum, out string error)
        {
            minimum = maximum = 0; error = "The current native committed tile range is unavailable.";
            if (!IsCurrent || !manifest.TryGetTileHeightRange(tile.x, tile.y, out minimum, out maximum)
                || !Finite(minimum) || !Finite(maximum) || maximum < minimum) return false;
            error = ""; return true;
        }
    }
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    // The same staged source/materializer/compositor operation serves an cache-owned
    // pending display slice or an analysis-owned unpublished native slice. Neither
    // operation owns the destination array or the caller's compositor resources.
    internal sealed class TileWork : IDisposable
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly TerrainAuthoringPreviewSharedHeightCache cache;
        private readonly TerrainAuthoringPreviewCommittedSourceContext source;
        private readonly TerrainHeightCompositor compositor;
        private readonly TerrainAuthoringPreviewHeightMaterializer materializer;
        private readonly TerrainAuthoringPreviewHeightPageHandle handle;
        private readonly bool display;
        private readonly int slice, samples;
        private readonly float spacing;
        private readonly List<object> retainedAuthoringInputs = new List<object>();
        private TerrainAuthoringPreviewHeightSourceLease lease;
        private RenderTexture destination;
        private GraphicsFence fence;
        private bool fenceCaptured, disposed, releaseReadbackIssued, releaseReadbackComplete;
        private AsyncGPUReadbackRequest releaseReadback;
        private int sourceStride;
        private float minimum, maximum;
        internal Vector2Int Tile { get; }
        internal int Stride { get; }
        internal long AuthoringGeneration { get; }
        internal string CommittedSignature { get; }
        internal TerrainAuthoringPreviewSharedCompositionState State { get; private set; }
        internal string Error { get; private set; } = "";
        internal RenderTexture Destination => destination;
        internal TerrainAuthoringPreviewSharedCompositionResult Result => new TerrainAuthoringPreviewSharedCompositionResult(
            Tile, Stride, AuthoringGeneration, CommittedSignature, minimum, maximum, sourceStride == 1, State, Error);
        internal bool IsTerminal => State == TerrainAuthoringPreviewSharedCompositionState.Published
            || State == TerrainAuthoringPreviewSharedCompositionState.NativeComplete || State == TerrainAuthoringPreviewSharedCompositionState.Failed
            || State == TerrainAuthoringPreviewSharedCompositionState.Cancelled;
        internal bool ReleaseComplete { get { if (disposed) BeginReleaseInputs(); return disposed && lease == null && retainedAuthoringInputs.Count == 0; } }

        internal TileWork(TerrainAuthoringPreviewSharedHeightCache cache, TerrainAuthoringPreviewCommittedSourceContext source,
            TerrainHeightCompositor compositor, TerrainAuthoringPreviewHeightMaterializer materializer,
            Vector2Int tile, int stride, TerrainAuthoringPreviewHeightPageHandle handle, RenderTexture nativeDestination, int nativeSlice)
        {
            this.cache = cache; this.source = source; this.compositor = compositor; this.materializer = materializer;
            this.handle = handle; display = handle.IsValid; Tile = tile; Stride = stride;
            AuthoringGeneration = source.AuthoringGeneration; CommittedSignature = source.CommittedSignature;
            samples = TerrainHeightResolutionUtility.GetSamplesPerSide(source.Settings, stride);
            spacing = TerrainHeightResolutionUtility.GetSampleSpacing(source.Settings, stride);
            slice = display ? handle.Slice : nativeSlice; destination = nativeDestination;
            retainedAuthoringInputs.Add(source.Data);
            if (source.Data.RegionalElevationSource != null) retainedAuthoringInputs.Add(source.Data.RegionalElevationSource);
            foreach (var modifier in source.Data.HeightModifiers)
                if (modifier is TerrainStampModifier stamp && stamp.StampAsset != null)
                { retainedAuthoringInputs.Add(stamp.StampAsset); if (stamp.StampAsset.HeightTexture != null) retainedAuthoringInputs.Add(stamp.StampAsset.HeightTexture); }
        }
        internal void Step()
        {
            RequireThread(); if (disposed || IsTerminal) return;
            try
            {
                if (!Current()) { Fail("The shared authoring source/demand/generation was superseded.", true); return; }
                if (State == TerrainAuthoringPreviewSharedCompositionState.Prepared)
                {
                    if (!source.TryGetRange(Tile, out minimum, out maximum, out string error)
                        || !Finite(minimum) || !Finite(maximum) || maximum < minimum)
                        throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Invalid native committed range." : error);
                    if (!source.TryAcquire(Tile, Stride, out lease, out error) || lease == null
                        || !source.LeaseIsCurrent(lease) || !lease.CanMaterializeAt(Stride)
                        || lease.NativeSamplesPerSide != source.Settings.HeightTileSamplesPerSide)
                        throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Invalid/stale committed source lease." : error);
                    sourceStride = lease.SourceStride;
                    Submit(false); State = TerrainAuthoringPreviewSharedCompositionState.WaitingForBase; return;
                }
                if (!FencePassed()) return;
                if (display)
                {
                    if (!cache.TryPollCandidate(handle, out bool complete, out string error)) throw new InvalidOperationException(error);
                    if (!complete) return;
                }
                if (!source.LeaseIsCurrent(lease)) { Fail("The committed Height source changed while GPU work was pending.", true); return; }
                if (State == TerrainAuthoringPreviewSharedCompositionState.WaitingForBase)
                {
                    if (display && !cache.TryMarkCommittedBase(handle, CommittedSignature, sourceStride, out string error))
                        throw new InvalidOperationException(error);
                    Submit(true); State = TerrainAuthoringPreviewSharedCompositionState.WaitingForComposite; return;
                }
                if (!Current()) { Fail("The completed Height output is no longer the current authoring target.", true); return; }
                if (display && (!cache.TryMarkFinalComposite(handle, AuthoringGeneration, minimum, maximum, out string finalError)
                    || !cache.TryCommitPage(handle, out finalError))) throw new InvalidOperationException(finalError);
                State = display ? TerrainAuthoringPreviewSharedCompositionState.Published : TerrainAuthoringPreviewSharedCompositionState.NativeComplete;
                ReleaseInputs();
            }
            catch (Exception exception) { Fail(exception.Message, false); }
        }
        private bool Current() => TargetCurrent(cache, source) && source.AuthoringGeneration == AuthoringGeneration
            && source.CommittedSignature == CommittedSignature && source.Demand.TryGetTile(Tile, out var row)
            && (display ? row.HasDisplay && row.SelectedDisplayStride == Stride && cache.IsWritableHandle(handle) : row.NativeWorkingRequired);
        private void Submit(bool compose)
        {
            TerrainAuthoringPreviewHeightPageWrite writer = null;
            try
            {
                if (display)
                {
                    if (!cache.TryGetWritableCandidate(handle, out writer, out string error, compose)) throw new InvalidOperationException(error);
                    destination = writer.Array;
                }
                if (destination == null || !destination.IsCreated() || destination.dimension != TextureDimension.Tex2DArray
                    || destination.format != RenderTextureFormat.RFloat || destination.width != samples || destination.height != samples
                    || slice < 0 || slice >= destination.volumeDepth) throw new InvalidOperationException("The unpublished Height destination is incompatible.");
                // Capture a boundary even on dispatch failure: some GPU commands may already have been submitted.
                fenceCaptured = false;
                if (compose)
                {
                    if (!compositor.TryComposeTile(destination, Tile, slice, samples, spacing, source.Settings.HeightTileWorldSize,
                        TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(source.Settings), source.Data, minimum, maximum,
                        out float low, out float high, out string error)) throw new InvalidOperationException(error);
                    if (!Finite(low) || !Finite(high) || high < low) throw new InvalidOperationException("The compositor returned invalid conservative bounds.");
                    minimum = low; maximum = high;
                }
                else if (!materializer.TryMaterialize(lease, destination, slice, source.Settings.HeightTileSamplesPerSide,
                    samples, Stride, out string error)) throw new InvalidOperationException(error);
            }
            finally
            {
                materializer.ReleaseTextureBindings(); compositor.ReleaseTextureBindings();
                writer?.Dispose();
                if (destination != null) { fenceCaptured = false; fence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations); fenceCaptured = true; }
            }
        }
        private bool FencePassed()
        { if (!fenceCaptured) throw new InvalidOperationException("The Height work has no trustworthy graphics completion boundary."); return fence.passed; }
        private void Fail(string error, bool cancelled)
        {
            Error = error ?? "Shared composition failed.";
            State = cancelled ? TerrainAuthoringPreviewSharedCompositionState.Cancelled : TerrainAuthoringPreviewSharedCompositionState.Failed;
            if (display && handle.IsValid) cache.ReleaseCandidate(handle, out _);
            // Seal cancellation with a fresh boundary, including a failed write fence.
            fenceCaptured = false; BeginReleaseInputs();
        }
        private void ReleaseInputs() { lease?.Dispose(); lease = null; retainedAuthoringInputs.Clear(); }
        private void TryReleaseInputs()
        {
            RequireThread();
            if (lease == null && retainedAuthoringInputs.Count == 0) return;
            if (!disposed && State != TerrainAuthoringPreviewSharedCompositionState.Failed && State != TerrainAuthoringPreviewSharedCompositionState.Cancelled) return;
            try
            {
                if (destination != null && (!fenceCaptured || !fence.passed)) return;
                if (releaseReadbackIssued && !releaseReadbackComplete && !releaseReadback.done) return;
                ReleaseInputs();
            }
            catch { fenceCaptured = false; /* Retain inputs until an explicit safe release retry. */ }
        }
        private void BeginReleaseInputs()
        {
            TryReleaseInputs(); if (lease == null && retainedAuthoringInputs.Count == 0 || destination == null) return;
            try
            {
                if (!fenceCaptured) { fence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations); fenceCaptured = true; }
                if (releaseReadbackIssued) { TryReleaseInputs(); return; }
                releaseReadbackIssued = true;
                try { releaseReadback = AsyncGPUReadback.Request(destination, 0, 0, 1, 0, 1, 0, 1, _ => { releaseReadbackComplete = true; TryReleaseInputs(); }); }
                catch { releaseReadbackIssued = false; throw; }
            }
            catch { /* Explicit shutdown can retry; unsafe input destruction is forbidden. */ }
        }
        private void RequireThread()
        { if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Height tile work must run on its creating Unity main thread."); }
        public void Dispose()
        {
            RequireThread(); if (!disposed)
            { if (!IsTerminal) Fail("Height tile work was cancelled by its owner.", true); disposed = true; }
            BeginReleaseInputs();
        }
        internal bool WaitForRelease(out string error)
        {
            RequireThread(); error = ""; if (!disposed) { error = "Dispose Height tile work before waiting for release."; return false; }
            BeginReleaseInputs();
            if (releaseReadbackIssued && !releaseReadbackComplete && !releaseReadback.done) releaseReadback.WaitForCompletion();
            TryReleaseInputs(); if (!ReleaseComplete) error = "Height source/authoring inputs are still retained by pending GPU work.";
            return ReleaseComplete;
        }
    }
}
