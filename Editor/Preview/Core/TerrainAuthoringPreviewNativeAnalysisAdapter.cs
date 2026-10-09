using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit subordinate of the analysis owner. Progress builds an unpublished,
// contiguous native array; publication is a separate consumer-detachment boundary.
// No callback schedules work or emits service observer notifications.
internal sealed class TerrainAuthoringPreviewNativeAnalysisAdapter : IDisposable
{
    private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
    private readonly TerrainAuthoringPreviewSharedHeightCache cache;
    private readonly TerrainHeightCompositor compositor;
    private readonly TerrainAuthoringPreviewHeightMaterializer materializer;
    private readonly long budget;
    private readonly int maximumTiles;
    private readonly List<Frame> retiring = new List<Frame>();
    private Frame active, candidate;
    private TerrainAuthoringPreviewCommittedSourceContext failedSource;
    private TerrainHeightCacheWindow failedOutput, failedPhysical;
    private TerrainHeightCacheWindow requestedOutput, requestedPhysical;
    private bool disposed;
    private long publicationGeneration;
    internal long AllocatedBytes { get; private set; }
    internal long PeakAllocatedBytes { get; private set; }
    internal int BorrowedNativeCount { get; private set; }
    internal int NativeCompositionCount { get; private set; }
    internal int BlockedAdmissionCount { get; private set; }
    internal string Error { get; private set; } = "";
    internal bool HasLastGood => active != null && active.Texture != null;
    internal bool CandidateReady => candidate != null && candidate.Ready && Current(candidate);
    internal bool ReleaseComplete { get { CollectRelease(); return disposed && retiring.Count == 0; } }

    private TerrainAuthoringPreviewNativeAnalysisAdapter(TerrainAuthoringPreviewSharedHeightCache cache,
        TerrainHeightCompositor compositor, TerrainAuthoringPreviewHeightMaterializer materializer,
        long budget, int maximumTiles)
    { this.cache = cache; this.compositor = compositor; this.materializer = materializer; this.budget = budget; this.maximumTiles = maximumTiles; }

    internal static bool TryCreate(TerrainAuthoringPreviewSharedHeightCache cache,
        TerrainHeightCompositor compositor, TerrainAuthoringPreviewHeightMaterializer materializer,
        long nativeBudgetBytes, out TerrainAuthoringPreviewNativeAnalysisAdapter adapter, out string error,
        int maximumTiles = 256)
    {
        adapter = null; error = "";
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out error)
            || !TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(2, 1, out error)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat))
        { if (string.IsNullOrEmpty(error)) error = "Committed Height RFloat textures are unsupported."; return false; }
        if (cache == null || cache.IsDisposed || compositor == null || materializer == null
            || nativeBudgetBytes <= 0 || nativeBudgetBytes > cache.GpuBudgetBytes || maximumTiles < 1 || maximumTiles > 4096)
        { error = "Native analysis requires a live cache, explicit GPU budget and bounded physical window."; return false; }
        cache.RequireOwnerThread();
        if (!materializer.TryPrepare(out error) || !compositor.TryPrepare(out error)) return false;
        adapter = new TerrainAuthoringPreviewNativeAnalysisAdapter(cache, compositor, materializer, nativeBudgetBytes, maximumTiles); return true;
    }

    internal bool TryBeginBuild(TerrainAuthoringPreviewCommittedSourceContext source,
        TerrainHeightCacheWindow output, TerrainHeightCacheWindow physical, out string error, bool retry = false)
    {
        RequireThread(); CollectRelease(); error = "";
        if (disposed || !TerrainAuthoringPreviewSharedHeightComposer.TargetCurrent(cache, source))
        { error = "Native analysis source/ownership is stale or disposed."; return false; }
        if (!retry && failedSource != null && failedSource.Settings == source.Settings
            && failedSource.AuthoringGeneration == source.AuthoringGeneration && failedSource.CommittedSignature == source.CommittedSignature
            && failedSource.Demand.Generation == source.Demand.Generation && failedSource.Demand.IsEquivalentTo(source.Demand)
            && failedOutput == output && failedPhysical == physical)
        { error = Error; return false; }
        var settings = source.Settings;
        var grid = new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        int samples = settings.HeightTileSamplesPerSide;
        float spacing = TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1);
        int guard = TerrainAnalysisWindowUtility.CalculateRequiredInteractiveGuardTileCount(samples, spacing);
        if (!TerrainAnalysisWindowUtility.TryExpandOutputWindow(output, grid, guard, out var required, out error)
            || !TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(physical, grid, guard, out var safe, out error)
            || !Contains(physical, required) || !Contains(safe, output))
        { if (string.IsNullOrEmpty(error)) error = "The complete native dependency guard is missing."; return false; }
        if (physical.TileCount > maximumTiles || physical.TileCount > SystemInfo.maxTextureArraySlices || samples > SystemInfo.maxTextureSize)
        { BlockedAdmissionCount++; error = "The bounded native window exceeds its tile or device capacity."; return false; }
        for (int slice = 0; slice < physical.TileCount; slice++)
        {
            var tile = physical.OriginTile + new Vector2Int(slice % physical.Size.x, slice / physical.Size.x);
            if (!source.Demand.TryGetTile(tile, out var row) || !row.NativeWorkingRequired)
            { error = "Every physical analysis tile must have an explicit exact-native working requirement."; return false; }
        }
        long bytes;
        try { bytes = checked((long)samples * samples * physical.TileCount * sizeof(float)); }
        catch (OverflowException) { error = "Native analysis byte arithmetic overflowed."; return false; }
        // Do not cancel a good in-flight candidate merely because a repeated request arrived.
        if (candidate != null && ReferenceEquals(candidate.Source, source) && candidate.Output == output && candidate.Physical == physical)
        { error = Error; return string.IsNullOrEmpty(Error); }
        if (candidate != null) CancelCandidate();
        CollectRelease();
        foreach (var frame in retiring)
            if (!frame.InputsReleased)
            { BlockedAdmissionCount++; error = "Retired native authoring commands still borrow the compositor inputs."; return false; }
        if (retiring.Count >= 2 || bytes > budget - AllocatedBytes)
        { BlockedAdmissionCount++; error = "Native analysis active/candidate/retired capacity is still charged to its budget."; return false; }
        if (!cache.TryReserveAnalysisBytes(source, bytes, out var charge, out error))
        { BlockedAdmissionCount++; return false; }
        TerrainAuthoringPreviewHeightPageMap map = null;
        RenderTexture texture = null;
        try
        {
            if (!cache.TryCreateRenderMapSnapshot(out map, out error)) { charge.Dispose(); return false; }
            texture = new RenderTexture(samples, samples, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = "Native terrain analysis candidate", hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Tex2DArray, volumeDepth = physical.TileCount,
                enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1, useMipMap = false, autoGenerateMips = false, anisoLevel = 0
            };
            if (!texture.Create() || !texture.IsCreated()) throw new InvalidOperationException("Native analysis texture allocation failed.");
            candidate = new Frame(this, source, output, physical, texture, map, charge, bytes);
            AllocatedBytes += bytes; PeakAllocatedBytes = Math.Max(PeakAllocatedBytes, AllocatedBytes);
            requestedOutput = output; requestedPhysical = physical; failedSource = null; Error = ""; return true;
        }
        catch (Exception exception)
        {
            map?.Dispose(); if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            charge.Dispose(); error = exception.Message; return false;
        }
    }

    // One copy or one tile-stage per call. No synchronous readback in normal progress.
    internal bool TryProgress(out bool ready, out string error)
    {
        RequireThread(); CollectRelease(); ready = false; error = Error;
        if (disposed || candidate == null) { if (string.IsNullOrEmpty(error)) error = "No native analysis candidate exists."; return false; }
        try
        {
            if (!Current(candidate)) throw new InvalidOperationException("Native analysis source/demand was superseded during construction.");
            candidate.Progress(); ready = CandidateReady; error = ""; return true;
        }
        catch (Exception exception)
        { Error = error = exception.Message; failedSource = candidate.Source; failedOutput = candidate.Output; failedPhysical = candidate.Physical; CancelCandidate(); return false; }
    }

    // Blocking boundary ONLY for explicitly invoked isolated validation. Normal
    // progress polls fences; this method never publishes or makes partial data ready.
    internal bool WaitForBuildGpuBoundary(out string error)
    {
        RequireThread(); error = "";
        if (disposed || candidate == null) { error = "No native analysis build can be waited."; return false; }
        try
        {
            var boundary = AsyncGPUReadback.Request(candidate.Texture, 0, 0, 1, 0, 1, 0, 1);
            boundary.WaitForCompletion();
            if (boundary.hasError) { error = "The isolated native build GPU boundary failed."; return false; }
            return true;
        }
        catch (Exception exception) { error = exception.Message; return false; }
    }

    // Caller MUST detach/stop submitting consumers of the previous source before
    // this switch. A final fence covers already submitted reads on the graphics queue.
    // Emit one ordinary residency/content observer change AFTER this succeeds.
    internal bool TryPublishCandidate(out TerrainAnalysisGpuSource source, out string error)
    {
        RequireThread(); CollectRelease(); source = default; error = "";
        if (!CandidateReady || disposed || retiring.Count >= 2)
        { error = "The full current native window is not publishable or retired capacity is still bounded."; return false; }
        if (active != null) Retire(active);
        active = candidate; candidate = null; active.Publication = checked(++publicationGeneration);
        return TryGetCurrentSource(out source, out _, out error);
    }

    internal bool TryGetCurrentSource(out TerrainAnalysisGpuSource source, out TerrainHeightCacheWindow output, out string error)
    {
        RequireThread(); source = default; output = default; error = "The complete current native analysis window is not ready.";
        if (disposed || active == null || !active.Ready || !Current(active)
            || active.Output != requestedOutput || active.Physical != requestedPhysical) return false;
        var world = active.Source.Settings;
        string signature = active.Source.CommittedSignature + ":" + active.Source.AuthoringGeneration
            + ":" + cache.ResourceGeneration + ":" + active.Source.Demand.Generation + ":" + active.Source.Demand.OwnershipGeneration
            + ":" + active.Physical + ":" + active.Output + ":" + active.Publication;
        source = new TerrainAnalysisGpuSource(active.Texture, active.Physical,
            new Vector2Int(world.HeightTileGridWidth, world.HeightTileGridHeight), world.HeightTileSamplesPerSide,
            TerrainHeightResolutionUtility.GetSampleSpacing(world, 1), TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(world),
            signature, active.Texture.GetInstanceID(), active.Publication, active.Source.AuthoringGeneration);
        if (!source.IsValid) { source = default; return false; }
        output = active.Output; error = ""; return true;
    }
    internal void CancelCandidate()
    { RequireThread(); if (candidate == null) return; Retire(candidate); candidate = null; }
    private bool Current(Frame frame) => frame.Texture != null && frame.Texture.IsCreated()
        && TerrainAuthoringPreviewSharedHeightComposer.TargetCurrent(cache, frame.Source);
    private static bool Contains(TerrainHeightCacheWindow outer, TerrainHeightCacheWindow inner) => outer.IsValid && inner.IsValid
        && outer.Contains(inner.OriginTile) && outer.Contains(inner.MaximumExclusive - Vector2Int.one);
    private void Retire(Frame frame) { frame.Dispose(); if (!frame.ReleaseComplete) retiring.Add(frame); }
    private void CollectRelease()
    { RequireThread(); for (int i = retiring.Count - 1; i >= 0; i--) if (retiring[i].ReleaseComplete) retiring.RemoveAt(i); }
    private void RequireThread()
    { if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Native analysis ownership requires its creating Unity main thread."); }
    public void Dispose()
    {
        RequireThread(); if (!disposed)
        {
            // The analysis owner detaches consumers BEFORE disposal, including reload/Play Mode.
            disposed = true; CancelCandidate(); if (active != null) { Retire(active); active = null; }
        }
        CollectRelease();
    }
    internal bool WaitForRelease(out string error)
    {
        RequireThread(); error = ""; if (!disposed) { error = "Dispose native analysis before its explicit release wait."; return false; }
        foreach (var frame in retiring) if (!frame.WaitForRelease(out error)) return false;
        CollectRelease(); return ReleaseComplete;
    }

    private sealed class Frame : IDisposable
    {
        private readonly TerrainAuthoringPreviewNativeAnalysisAdapter owner;
        internal readonly TerrainAuthoringPreviewCommittedSourceContext Source;
        internal readonly TerrainHeightCacheWindow Output, Physical;
        internal RenderTexture Texture { get; private set; }
        private TerrainAuthoringPreviewHeightPageMap map;
        private IDisposable charge;
        private readonly long bytes;
        private int nextSlice;
        private bool copyPending, disposed, fenceCaptured, releaseIssued, releaseDone;
        private GraphicsFence fence;
        private AsyncGPUReadbackRequest release;
        private TerrainAuthoringPreviewSharedHeightComposer.TileWork work;
        internal long Publication;
        internal bool InputsReleased => work == null || work.ReleaseComplete;
        internal bool Ready => nextSlice == Physical.TileCount && !copyPending && work == null;
        internal Frame(TerrainAuthoringPreviewNativeAnalysisAdapter owner, TerrainAuthoringPreviewCommittedSourceContext source,
            TerrainHeightCacheWindow output, TerrainHeightCacheWindow physical, RenderTexture texture,
            TerrainAuthoringPreviewHeightPageMap map, IDisposable charge, long bytes)
        { this.owner = owner; Source = source; Output = output; Physical = physical; Texture = texture; this.map = map; this.charge = charge; this.bytes = bytes; }
        internal void Progress()
        {
            if (Ready) return;
            if (copyPending)
            {
                if (!fenceCaptured || !fence.passed) return;
                copyPending = false; nextSlice++; CompleteWindow(); return;
            }
            if (work != null)
            {
                work.Step(); if (!work.IsTerminal) return;
                if (!work.Result.Succeeded) throw new InvalidOperationException(work.Error);
                owner.NativeCompositionCount++; work.Dispose(); work = null; nextSlice++; CompleteWindow(); return;
            }
            var tile = Physical.OriginTile + new Vector2Int(nextSlice % Physical.Size.x, nextSlice / Physical.Size.x);
            int samples = Source.Settings.HeightTileSamplesPerSide;
            float spacing = TerrainHeightResolutionUtility.GetSampleSpacing(Source.Settings, 1);
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0
                && map != null && map.TargetsAreCurrent && map.TryGetEntry(tile, out var entry) && entry.IsCurrent
                && entry.RequestedStride == 1 && entry.Page.Handle.Stride == 1 && entry.Page.SamplesPerSide == samples
                && entry.Page.SampleSpacing == spacing && entry.Page.CommittedSignature == Source.CommittedSignature
                && entry.Page.AuthoringGeneration == Source.AuthoringGeneration)
            {
                if (!map.TryGetPoolTexture(entry.PoolIndex, out var pool, out string error)) throw new InvalidOperationException(error);
                if (pool.format != RenderTextureFormat.RFloat || pool.dimension != TextureDimension.Tex2DArray
                    || pool.width != samples || pool.height != samples || entry.Slice < 0 || entry.Slice >= pool.volumeDepth)
                    throw new InvalidOperationException("The retained shared-native source allocation is incompatible.");
                // Already composed native pixels are copied exactly once, never authored again.
                fenceCaptured = false;
                try { Graphics.CopyTexture(pool, entry.Slice, 0, Texture, nextSlice, 0); }
                finally { CaptureFence(); }
                copyPending = true; owner.BorrowedNativeCount++; return;
            }
            // Coarse, missing and native-only tiles all start from committed NATIVE BASE.
            work = new TerrainAuthoringPreviewSharedHeightComposer.TileWork(owner.cache, Source, owner.compositor,
                owner.materializer, tile, 1, default, Texture, nextSlice);
            work.Step();
            if (work.IsTerminal && !work.Result.Succeeded) throw new InvalidOperationException(work.Error);
        }
        private void CompleteWindow() { if (Ready) { map?.Dispose(); map = null; } }
        private void CaptureFence()
        { fenceCaptured = false; fence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations); fenceCaptured = true; }
        public void Dispose()
        {
            if (!disposed) { disposed = true; work?.Dispose(); map?.Dispose(); map = null; fenceCaptured = false; }
            BeginRelease();
        }
        private void BeginRelease()
        {
            if (!disposed || Texture == null) return;
            try
            {
                if (!fenceCaptured) CaptureFence();
                if (!releaseIssued)
                {
                    releaseIssued = true;
                    try { release = AsyncGPUReadback.Request(Texture, 0, 0, 1, 0, 1, 0, 1, _ => { releaseDone = true; try { PollRelease(); } catch { fenceCaptured = false; } }); }
                    catch { releaseIssued = false; throw; }
                }
                PollRelease();
            }
            catch { /* Retain capacity/resources until the owner retries a safe graphics boundary. */ }
        }
        private void PollRelease()
        {
            owner.RequireThread();
            if (!disposed || Texture == null || !fenceCaptured || !fence.passed || !releaseIssued
                || !releaseDone && !release.done || work != null && !work.ReleaseComplete) return;
            Texture.Release(); UnityEngine.Object.DestroyImmediate(Texture); Texture = null;
            charge.Dispose(); charge = null; owner.AllocatedBytes -= bytes; work = null;
        }
        internal bool ReleaseComplete { get { BeginRelease(); return disposed && Texture == null; } }
        internal bool WaitForRelease(out string error)
        {
            error = ""; work?.WaitForRelease(out error); BeginRelease();
            if (releaseIssued && !releaseDone && !release.done) release.WaitForCompletion();
            if (!ReleaseComplete) { error = "Native analysis GPU reads or authoring inputs are still pending."; return false; }
            return true;
        }
    }
}
