using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal enum TerrainAuthoringPreviewHeightSlotState { Free, Reserved, Active, Retiring }

// One stable physical array per stride. Capacity is fixed at construction; an exhausted
// pool blocks admission rather than invalidating handles, growing unbounded bindings or evicting readers.
internal sealed class TerrainAuthoringPreviewHeightPagePool : IDisposable
{
    private sealed class Slot
    {
        internal long PageGeneration;
        internal TerrainAuthoringPreviewHeightSlotState State;
        internal int Readers;
        internal bool Writing, WasWritten, FencePending, CompletionFailed;
        internal GraphicsFence Fence;
    }
    private readonly Slot[] slots;
    private readonly Stack<int> free;
    private readonly Action<long> released;
    private bool disposed, gpuUsed, disposalRequestIssued, disposalFenceCaptured, disposalReadbackCompleted;
    private GraphicsFence disposalFence;
    private AsyncGPUReadbackRequest disposalReadback;
    internal RenderTexture Texture { get; private set; }
    internal int Stride { get; }
    internal int PoolIndex { get; }
    internal int SamplesPerSide { get; }
    internal float SampleSpacing { get; }
    internal int Capacity => slots.Length;
    internal long AllocationGeneration { get; }
    internal long AllocatedBytes { get; private set; }
    internal bool ReleaseComplete => Texture == null;

    private TerrainAuthoringPreviewHeightPagePool(int stride, int index, int samples, float spacing,
        int capacity, long allocation, long bytes, RenderTexture texture, Action<long> released)
    {
        Stride = stride; PoolIndex = index; SamplesPerSide = samples; SampleSpacing = spacing;
        AllocationGeneration = allocation; AllocatedBytes = bytes; Texture = texture; this.released = released;
        slots = new Slot[capacity]; free = new Stack<int>(capacity);
        for (int i = capacity - 1; i >= 0; i--) { slots[i] = new Slot(); free.Push(i); }
    }

    internal static bool TryEstimateBytes(int samples, int capacity, out long bytes)
    {
        bytes = 0;
        if (samples < 2 || capacity < 1) return false;
        try { bytes = checked((long)samples * samples * capacity * sizeof(float)); return true; }
        catch (OverflowException) { return false; }
    }

    internal static bool TryValidateDevice(int samples, int capacity, out string error)
    {
        error = "";
        if (!SystemInfo.supports2DArrayTextures || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
            || !SystemInfo.supportsComputeShaders || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat)
            || !SystemInfo.supportsGraphicsFence || !SystemInfo.supportsAsyncGPUReadback)
        { error = "Shared Height pages require RFloat arrays, compute/random write, graphics fences and asynchronous GPU readback for safe release."; return false; }
        if (samples < 2 || samples > SystemInfo.maxTextureSize || capacity < 1
            || SystemInfo.maxTextureArraySlices < 1 || capacity > SystemInfo.maxTextureArraySlices)
        { error = "Shared Height pool dimensions/capacity exceed the graphics device limits."; return false; }
        return true;
    }

    internal static bool TryCreate(int stride, int index, int samples, float spacing, int capacity,
        long allocation, Action<long> released, out TerrainAuthoringPreviewHeightPagePool pool, out string error)
    {
        pool = null;
        if (!TryValidateDevice(samples, capacity, out error) || !TryEstimateBytes(samples, capacity, out long bytes))
        { if (string.IsNullOrEmpty(error)) error = "Shared Height allocation byte arithmetic overflowed."; return false; }
        RenderTexture texture = null;
        try
        {
            texture = new RenderTexture(samples, samples, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = "Terrain Authoring Shared Height Pages", dimension = TextureDimension.Tex2DArray,
                volumeDepth = capacity, enableRandomWrite = true, useMipMap = false, autoGenerateMips = false,
                antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0, hideFlags = HideFlags.HideAndDontSave
            };
            if (!texture.Create() || !texture.IsCreated()) throw new InvalidOperationException("Shared Height array creation failed.");
            pool = new TerrainAuthoringPreviewHeightPagePool(stride, index, samples, spacing, capacity, allocation, bytes, texture, released);
            return true;
        }
        catch (Exception exception)
        {
            DestroyTexture(texture); error = "Shared Height allocation failed: " + exception.Message; return false;
        }
    }

    internal bool TryReserve(long pageGeneration, out int slice, out string error)
    {
        slice = -1; error = ""; CollectRetiring();
        if (disposed || Texture == null || !Texture.IsCreated()) { error = "Shared Height pool is unavailable."; return false; }
        if (free.Count == 0) { error = "Shared Height pool capacity is exhausted; active/read-held pages were retained."; return false; }
        slice = free.Pop(); var slot = slots[slice]; slot.PageGeneration = pageGeneration;
        slot.State = TerrainAuthoringPreviewHeightSlotState.Reserved;
        return true;
    }

    internal bool Matches(TerrainAuthoringPreviewHeightPageHandle handle, out int slice)
    {
        slice = handle.Slice;
        return !disposed && MatchesAllocation(handle, slice);
    }
    private bool MatchesAllocation(TerrainAuthoringPreviewHeightPageHandle handle, int slice)
    {
        return handle.AllocationGeneration == AllocationGeneration && handle.PoolIndex == PoolIndex
            && handle.Stride == Stride && slice >= 0 && slice < Capacity && slots[slice].PageGeneration == handle.PageGeneration
            && slots[slice].State != TerrainAuthoringPreviewHeightSlotState.Free && Texture != null && Texture.IsCreated();
    }

    internal bool TryBeginWrite(TerrainAuthoringPreviewHeightPageHandle handle, out string error)
    {
        error = "";
        if (!Matches(handle, out int i) || slots[i].State != TerrainAuthoringPreviewHeightSlotState.Reserved
            || slots[i].Writing || !GpuComplete(slots[i]) || slots[i].CompletionFailed)
        { error = "The Height candidate has a stale token or an outstanding/failed GPU write."; return false; }
        slots[i].Writing = true; gpuUsed = true; return true;
    }

    internal void EndWrite(TerrainAuthoringPreviewHeightPageHandle handle)
    {
        int i = handle.Slice;
        if (!MatchesAllocation(handle, i) || !slots[i].Writing) return;
        var slot = slots[i]; slot.Writing = false; slot.WasWritten = true;
        CaptureFence(slot); CollectRetiring();
    }

    private static void CaptureFence(Slot slot)
    {
        slot.Fence = CreateCompletionFence(out bool failed); slot.FencePending = true;
        slot.CompletionFailed |= failed;
    }
    private static GraphicsFence CreateCompletionFence(out bool failed)
    {
        failed = false;
        try { return Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations); }
        catch { failed = true; return default; }
    }
    private static bool GpuComplete(Slot slot)
    {
        if (slot.Writing || slot.CompletionFailed) return false;
        if (!slot.FencePending) return true;
        try { if (slot.Fence.passed) slot.FencePending = false; }
        catch { slot.CompletionFailed = true; }
        return !slot.FencePending;
    }

    internal bool TryPollWrite(TerrainAuthoringPreviewHeightPageHandle handle, out bool complete, out string error)
    {
        complete = false; error = "";
        if (!Matches(handle, out int i) || slots[i].State != TerrainAuthoringPreviewHeightSlotState.Reserved
            || !slots[i].WasWritten)
        { error = "The Height candidate does not have a submitted reserved write."; return false; }
        complete = GpuComplete(slots[i]);
        if (slots[i].CompletionFailed) { error = "Height candidate GPU completion failed."; return false; }
        return true;
    }

    internal bool CanPublish(TerrainAuthoringPreviewHeightPageHandle handle, out string error)
    {
        error = "";
        if (!Matches(handle, out int i) || slots[i].State != TerrainAuthoringPreviewHeightSlotState.Reserved)
        { error = "The Height candidate no longer owns its reserved slice."; return false; }
        var slot = slots[i];
        if (!slot.WasWritten || slot.CompletionFailed) { error = "Height candidate GPU output is missing or failed."; return false; }
        if (!GpuComplete(slot)) { error = "Height candidate GPU work has not completed."; return false; }
        return true;
    }
    internal void Publish(TerrainAuthoringPreviewHeightPageHandle handle) { slots[handle.Slice].State = TerrainAuthoringPreviewHeightSlotState.Active; }
    internal void Retire(TerrainAuthoringPreviewHeightPageHandle handle)
    {
        if (Matches(handle, out int i)) { slots[i].State = TerrainAuthoringPreviewHeightSlotState.Retiring; CollectRetiring(); }
    }
    internal void AddReader(TerrainAuthoringPreviewHeightPageHandle handle) { slots[handle.Slice].Readers++; }
    internal GraphicsFence SealReads(out bool failed)
    {
        gpuUsed = true; return CreateCompletionFence(out failed);
    }
    internal void RemoveReader(TerrainAuthoringPreviewHeightPageHandle handle, bool gpuAccessed, GraphicsFence fence, bool failed)
    {
        if (!Matches(handle, out int i)) return;
        var slot = slots[i];
        if (gpuAccessed) { slot.Fence = fence; slot.FencePending = true; slot.CompletionFailed |= failed; }
        if (slot.Readers > 0) slot.Readers--;
    }

    internal void CollectRetiring()
    {
        if (disposed) { BeginDisposal(); return; }
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.State != TerrainAuthoringPreviewHeightSlotState.Retiring || slot.Readers != 0 || !GpuComplete(slot)) continue;
            slots[i] = new Slot(); free.Push(i);
        }
    }

    internal TerrainAuthoringPreviewHeightPoolSnapshot Capture()
    {
        int active = 0, reserved = 0, retiring = 0;
        foreach (var slot in slots)
            switch (slot.State)
            {
                case TerrainAuthoringPreviewHeightSlotState.Active: active++; break;
                case TerrainAuthoringPreviewHeightSlotState.Reserved: reserved++; break;
                case TerrainAuthoringPreviewHeightSlotState.Retiring: retiring++; break;
            }
        return new TerrainAuthoringPreviewHeightPoolSnapshot(PoolIndex, Stride, SamplesPerSide, SampleSpacing,
            AllocationGeneration, Capacity, active, reserved, retiring, AllocatedBytes);
    }

    // No recurring callback or CPU wait. On teardown, one tiny readback wakes cleanup after
    // the graphics queue reaches the final fence. The allocation stays accounted until released.
    public void Dispose()
    {
        disposed = true;
        foreach (var slot in slots)
            if (slot.State != TerrainAuthoringPreviewHeightSlotState.Free) slot.State = TerrainAuthoringPreviewHeightSlotState.Retiring;
        BeginDisposal();
    }
    private void BeginDisposal()
    {
        if (Texture == null) return;
        foreach (var slot in slots) if (slot.Writing) return;
        if (!gpuUsed) { FinishDisposal(); return; }
        try
        {
            if (!disposalFenceCaptured)
            { disposalFence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations); disposalFenceCaptured = true; }
            if (TryFinishDisposal()) return;
            if (disposalRequestIssued) return;
            disposalRequestIssued = true;
            try
            {
                disposalReadback = AsyncGPUReadback.Request(Texture, 0, 0, 1, 0, 1, 0, 1, _ =>
                { disposalReadbackCompleted = true; TryFinishDisposal(); });
            }
            catch { disposalRequestIssued = false; throw; }
        }
        catch { /* Keep ownership on failure; explicit collection/release reports the incomplete boundary. */ }
    }

    private bool TryFinishDisposal()
    {
        if (Texture == null) return true;
        if (!disposed || !disposalFenceCaptured) return false;
        try { if (!disposalFence.passed) return false; }
        catch { return false; }
        // An issued readback still owns the texture until its completion, even if the fence passed first.
        if (disposalRequestIssued && !disposalReadbackCompleted && !disposalReadback.done) return false;
        FinishDisposal(); return true;
    }

    private void FinishDisposal()
    {
        var texture = Texture; if (texture == null) return;
        Texture = null; DestroyTexture(texture); long bytes = AllocatedBytes; AllocatedBytes = 0;
        released?.Invoke(bytes);
    }

    // Explicit isolated validation/shutdown boundary only. Normal admission, publication and
    // retirement poll fences and never wait on the CPU or call WaitOnAsyncGraphicsFence.
    internal bool WaitForRelease()
    {
        if (!disposed) return false;
        if (Texture == null) return true;
        BeginDisposal();
        if (Texture == null) return true;
        if (disposalRequestIssued && !disposalReadbackCompleted && !disposalReadback.done)
            disposalReadback.WaitForCompletion();
        return TryFinishDisposal();
    }

    private static void DestroyTexture(RenderTexture texture)
    {
        if (texture == null) return;
        if (texture.IsCreated()) texture.Release();
        UnityEngine.Object.DestroyImmediate(texture);
    }
}
