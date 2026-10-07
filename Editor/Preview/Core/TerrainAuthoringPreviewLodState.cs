using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal readonly struct TerrainAuthoringPreviewDirtyFailure
{
    internal readonly long AttemptedGeneration;
    internal readonly string Message;
    internal readonly bool LastGoodAvailable;

    internal TerrainAuthoringPreviewDirtyFailure(long generation, string message, bool lastGoodAvailable)
    {
        AttemptedGeneration = generation;
        Message = message ?? "";
        LastGoodAvailable = lastGoodAvailable;
    }
}

internal enum TerrainAuthoringPreviewLodTransitionState
{
    Idle,
    WaitingForRequiredPages,
    PopulatingStaging,
    CommitPending
}

/*
 * Editor-owned state for one clipmap Height LOD.
 *
 * The state owns representation identity, cache references, residency
 * metadata, and transition generations. TerrainAuthoringPreviewService remains
 * responsible for planning, scheduling, Scene View intent, and activation.
 */
internal sealed class TerrainAuthoringPreviewLodState :
    IDisposable
{
    public int Level { get; }

    public int SampleStride { get; }

    public int SamplesPerSide { get; }

    public float SampleSpacing { get; }

    public TerrainHeightCacheWindow ActiveRequiredWindow;

    public TerrainHeightCacheWindow RequestedRequiredWindow;

    public TerrainHeightCacheWindow RequestedDesiredWindow;

    public TerrainAuthoringPreviewCache ActiveCache;

    public TerrainAuthoringPreviewCache StagingCache;

    // Whole-representation content acknowledgement, independent of residency.
    public bool CacheReady;
    internal readonly HashSet<Vector2Int> PendingDirtyTiles = new HashSet<Vector2Int>();
    internal readonly HashSet<Vector2Int> SuccessfulDirtyTiles = new HashSet<Vector2Int>();
    internal readonly HashSet<Vector2Int> PendingRegionalTiles = new HashSet<Vector2Int>();
    internal RenderTexture DirtyScratch { get; private set; }
    internal readonly Dictionary<Vector2Int, TerrainAuthoringPreviewDirtyFailure> DirtyFailures =
        new Dictionary<Vector2Int, TerrainAuthoringPreviewDirtyFailure>();
    internal long DirtyScratchBytes => DirtyScratch != null && DirtyScratch.IsCreated()
        ? 2L * sizeof(float) * SamplesPerSide * SamplesPerSide : 0L;

    // Only a destroyed live allocation or an unsuccessful restoration is unsafe.
    internal bool WriteFailed;
    // Accepted content target; global scope may still await per-LOD projection.
    internal long DirtyTargetGeneration;

    internal bool HasUsableActiveAllocation =>
        !WriteFailed && ActiveCache != null && ActiveCache.IsReady;

    internal bool HasPendingContent(Vector2Int tile) =>
        PendingDirtyTiles.Contains(tile) || PendingRegionalTiles.Contains(tile);

    public TerrainAuthoringPreviewLodTransitionState TransitionState =
        TerrainAuthoringPreviewLodTransitionState.Idle;

    public long RequestGeneration;

    public long StagingGeneration;

    public long ActiveAuthoringGeneration;

    public long StagingAuthoringGeneration;

    internal TerrainAuthoringPreviewCache DetachStagingCache()
    {
        var cache = StagingCache;
        StagingCache = null;
        return cache;
    }

    internal void PromoteStagingCache()
    {
        if (ActiveCache != null || StagingCache == null
            || !StagingCache.IsCompleteForActivation)
        {
            throw new InvalidOperationException("The LOD staging cache cannot transfer ownership.");
        }
        ActiveCache = DetachStagingCache();
        ActiveRequiredWindow = RequestedRequiredWindow;
        ActiveAuthoringGeneration = StagingAuthoringGeneration;
        CacheReady = true;
        TransitionState = TerrainAuthoringPreviewLodTransitionState.Idle;
    }

    public TerrainAuthoringPreviewLodState(
        int level,
        int sampleStride,
        int samplesPerSide,
        float sampleSpacing
    )
    {
        if (
            level < 0
            ||
            level >=
                TerrainClipmapTopologyUtility
                    .MaximumLevelCount
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(level)
            );
        }

        if (
            sampleStride < 1
            ||
            !TerrainHeightResolutionUtility
                .IsPowerOfTwo(
                    sampleStride
                )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride)
            );
        }

        if (samplesPerSide < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplesPerSide)
            );
        }

        if (
            !IsFinite(
                sampleSpacing
            )
            ||
            sampleSpacing <= 0f
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleSpacing)
            );
        }

        Level =
            level;

        SampleStride =
            sampleStride;

        SamplesPerSide =
            samplesPerSide;

        SampleSpacing =
            sampleSpacing;
    }

    internal bool IsDirtyTileRunnable(Vector2Int tile) =>
        HasUsableActiveAllocation && PendingDirtyTiles.Contains(tile) && !DirtyFailures.ContainsKey(tile);

    internal void RecordDirtyFailure(Vector2Int tile, long generation, string message, bool lastGoodAvailable)
    {
        PendingDirtyTiles.Add(tile);
        SuccessfulDirtyTiles.Remove(tile);
        CacheReady = false;
        DirtyFailures[tile] = new TerrainAuthoringPreviewDirtyFailure(generation, message, lastGoodAvailable);
    }

    internal bool TryEnsureDirtyScratch(out bool allocated, out string error)
    {
        allocated = false;
        error = "";
        if (DirtyScratch != null && DirtyScratch.IsCreated() && DirtyScratch.width == SamplesPerSide
            && DirtyScratch.height == SamplesPerSide && DirtyScratch.volumeDepth == 2
            && DirtyScratch.dimension == TextureDimension.Tex2DArray && DirtyScratch.format == RenderTextureFormat.RFloat
            && DirtyScratch.antiAliasing == 1 && !DirtyScratch.useMipMap && DirtyScratch.enableRandomWrite)
            return true;

        ReleaseDirtyScratch();
        if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports2DArrayTextures
            || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat)
            || SamplesPerSide > SystemInfo.maxTextureSize || SystemInfo.maxTextureArraySlices < 2)
        {
            error = "The graphics device cannot allocate the dirty Height scratch array.";
            return false;
        }

        RenderTexture candidate = null;
        try
        {
            candidate = new RenderTexture(SamplesPerSide, SamplesPerSide, 0, RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear)
            {
                name = "Terrain Height dirty scratch", dimension = TextureDimension.Tex2DArray, volumeDepth = 2,
                enableRandomWrite = true, antiAliasing = 1, useMipMap = false, autoGenerateMips = false,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            if (!candidate.Create() || !candidate.IsCreated())
                throw new InvalidOperationException("The dirty Height scratch array could not be created.");
            DirtyScratch = candidate;
            allocated = true;
            return true;
        }
        catch (Exception exception)
        {
            if (candidate != null) { candidate.Release(); UnityEngine.Object.DestroyImmediate(candidate); }
            error = exception.Message;
            return false;
        }
    }

    private void ReleaseDirtyScratch()
    {
        var scratch = DirtyScratch;
        DirtyScratch = null;
        if (scratch != null) { scratch.Release(); UnityEngine.Object.DestroyImmediate(scratch); }
    }

    public void Dispose()
    {
        TerrainAuthoringPreviewCache active =
            ActiveCache;

        TerrainAuthoringPreviewCache staging =
            StagingCache;

        ActiveCache =
            null;

        StagingCache =
            null;

        if (active != null)
        {
            active.Dispose();
        }

        if (
            staging != null
            &&
            !ReferenceEquals(
                staging,
                active
            )
        )
        {
            staging.Dispose();
        }

        ActiveRequiredWindow =
            default;

        RequestedRequiredWindow =
            default;

        RequestedDesiredWindow =
            default;

        CacheReady =
            false;

        TransitionState =
            TerrainAuthoringPreviewLodTransitionState.Idle;

        ReleaseDirtyScratch();
        DirtyFailures.Clear();
        PendingDirtyTiles.Clear();
        SuccessfulDirtyTiles.Clear();
        PendingRegionalTiles.Clear();
        WriteFailed = false;
        DirtyTargetGeneration = 0;
        ActiveAuthoringGeneration = 0;
        StagingAuthoringGeneration = 0;

        RequestGeneration =
            0;

        StagingGeneration =
            0;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }
}


