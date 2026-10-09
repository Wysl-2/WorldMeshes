using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

internal readonly struct TerrainAuthoringPreviewSharedHeightSnapshot
{
    internal readonly bool Present;
    internal readonly int Demanded, Required, Current, Stale, Missing, Backlog, Failed;
    internal readonly long PoolBytes, LookupBytes, NativeBytes, RetiringBytes;
    internal readonly string Strides;
    internal readonly TerrainAuthoringPreviewDirtyFailureSnapshot Failure;
    internal TerrainAuthoringPreviewSharedHeightSnapshot(bool present, int demanded, int required, int current, int stale,
        int missing, int backlog, int failed, long pools, long lookups, long native, long retiring, string strides,
        TerrainAuthoringPreviewDirtyFailureSnapshot failure)
    { Present = present; Demanded = demanded; Required = required; Current = current; Stale = stale; Missing = missing;
      Backlog = backlog; Failed = failed; PoolBytes = pools; LookupBytes = lookups; NativeBytes = native; RetiringBytes = retiring;
      Strides = strides; Failure = failure; }
}

// One live edit-mode display owner. Retained cache-set code receives no normal
// streaming callback; only explicit isolated validation uses that representation.
public static partial class TerrainAuthoringPreviewService
{
    private static TerrainAuthoringPreviewSharedHeightCache sharedHeight;
    private static TerrainAuthoringPreviewGeographicDemandPlan sharedDemand, sharedPublishedDemand;
    private static TerrainAuthoringPreviewQualitySnapshot sharedQuality;
    private static TerrainAuthoringPreviewCommittedSourceContext sharedSource;
    private static SharedWorkOwner sharedDisplayWork, sharedNativeWork, sharedPublishedNative;
    private static TerrainAuthoringPreviewSharedHeightBindingData sharedBinding;
    private static long sharedResourceGeneration, sharedAcceptedGeneration, sharedPublishedEpoch, sharedCompositionCount;
    private static string sharedCommitted = "", sharedOverall = "", sharedError = "", sharedFailedTarget = "";
    private static Vector2Int? sharedRunningTile;
    private static TerrainAuthoringPreviewSharedCompositionState sharedRunningState;
    private static int sharedTargetJobs, sharedTargetLoads, sharedTargetRetained;
    private static bool sharedAnalysisAttached, sharedAnalysisFirst, sharedScopeUnknown, sharedNotificationCaptured, sharedSaveRestored;
    private static readonly HashSet<Vector2Int> sharedAffectedTiles = new HashSet<Vector2Int>();
    private static TerrainRegionalElevationInvalidationScope sharedRegionalScope;
    private static readonly List<Vector2Int> sharedQueue = new List<Vector2Int>();
    private static readonly HashSet<Vector2Int> sharedPhysicalCompletions = new HashSet<Vector2Int>();
    private static readonly HashSet<Vector2Int> sharedNativeChanged = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, string> sharedTileFailures = new Dictionary<Vector2Int, string>();
    private static readonly List<SharedWorkOwner> sharedRetiringWork = new List<SharedWorkOwner>();
    private static readonly List<TerrainAuthoringPreviewSharedHeightCache> sharedRetiringCaches = new List<TerrainAuthoringPreviewSharedHeightCache>();
    private static readonly List<TerrainAuthoringPreviewSharedHeightBindingData> sharedRetiringBindings = new List<TerrainAuthoringPreviewSharedHeightBindingData>();
    private static readonly List<SharedMaterialRetirement> sharedRetiringMaterials = new List<SharedMaterialRetirement>();
    private static readonly Dictionary<MeshRenderer, SharedRendererState> sharedRendererOriginals = new Dictionary<MeshRenderer, SharedRendererState>();
    private static readonly Dictionary<Material, Material> sharedMaterials = new Dictionary<Material, Material>();
    private static readonly MaterialPropertyBlock sharedInspectionBlock = new MaterialPropertyBlock();

    private sealed class SharedWorkOwner
    {
        internal readonly TerrainHeightCompositor Compositor = new TerrainHeightCompositor();
        internal readonly TerrainAuthoringPreviewHeightMaterializer Materializer = new TerrainAuthoringPreviewHeightMaterializer();
        internal TerrainAuthoringPreviewSharedHeightComposer Display;
        internal TerrainAuthoringPreviewNativeAnalysisAdapter Native;
        internal TerrainAuthoringPreviewCommittedSourceContext Source;
        internal bool Complete => (Display == null || Display.ReleaseComplete) && (Native == null || Native.ReleaseComplete);
        internal void Retire() { Display?.Dispose(); Native?.Dispose(); }
        internal void Destroy() { Compositor.Dispose(); Materializer.ReleaseTextureBindings(); }
    }
    private sealed class SharedRendererState
    {
        internal readonly Material Material;
        internal readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        internal readonly Bounds Bounds;
        internal readonly bool Enabled;
        internal SharedRendererState(MeshRenderer renderer)
        { Material = renderer.sharedMaterial; renderer.GetPropertyBlock(Block); Bounds = renderer.localBounds; Enabled = renderer.enabled; }
        internal void Restore(MeshRenderer renderer)
        { renderer.sharedMaterial = Material; renderer.SetPropertyBlock(Block); renderer.localBounds = Bounds; renderer.enabled = Enabled; }
    }
    private sealed class SharedMaterialRetirement
    {
        internal readonly List<Material> Materials;
        internal readonly TerrainAuthoringPreviewSharedHeightBindingData Boundary;
        internal SharedMaterialRetirement(List<Material> materials, TerrainAuthoringPreviewSharedHeightBindingData boundary)
        { Materials = materials; Boundary = boundary; }
    }
    private static void RetireSharedWork(ref SharedWorkOwner work)
    { if (work == null) return; work.Retire(); sharedRetiringWork.Add(work); work = null; }
    private static void CollectSharedRetirement()
    {
        for (int i = sharedRetiringWork.Count - 1; i >= 0; i--)
            if (sharedRetiringWork[i].Complete) { sharedRetiringWork[i].Destroy(); sharedRetiringWork.RemoveAt(i); }
        for (int i = sharedRetiringBindings.Count - 1; i >= 0; i--)
        { sharedRetiringBindings[i].Dispose(); if (sharedRetiringBindings[i].ReleaseComplete) sharedRetiringBindings.RemoveAt(i); }
        for (int i = sharedRetiringMaterials.Count - 1; i >= 0; i--)
        {
            var row = sharedRetiringMaterials[i];
            if (row.Boundary != null && !row.Boundary.ReleaseComplete) continue;
            foreach (var material in row.Materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            sharedRetiringMaterials.RemoveAt(i);
        }
        for (int i = sharedRetiringCaches.Count - 1; i >= 0; i--)
        { sharedRetiringCaches[i].CollectRetiredPages(); if (sharedRetiringCaches[i].ReleaseComplete) sharedRetiringCaches.RemoveAt(i); }
        sharedHeight?.CollectRetiredPages();
        if (CanRunEditorPreviewWork && sharedBinding != null && sharedBinding.IsAlive && sharedRetiringBindings.Count == 0
            && (aggregateMinimumHeight != sharedBinding.MinimumHeight || aggregateMaximumHeight != sharedBinding.MaximumHeight))
        {
            var controller = boundClipmapRoot != null ? boundClipmapRoot.GetComponent<TerrainClipmapBoundsController>() : null;
            if (controller != null && controller.ApplyBoundsForRange(sharedBinding.MinimumHeight, sharedBinding.MaximumHeight))
            { aggregateMinimumHeight = sharedBinding.MinimumHeight; aggregateMaximumHeight = sharedBinding.MaximumHeight; }
        }
    }
    // Explicit terminal shutdown only. Normal updates poll GPU retirement.
    private static void DrainSharedRetirementForShutdown()
    {
        foreach (var work in sharedRetiringWork)
        {
            if (work.Display != null && !work.Display.WaitForRelease(out string error)) UnityEngine.Debug.LogError(error);
            if (work.Native != null && !work.Native.WaitForRelease(out string nativeError)) UnityEngine.Debug.LogError(nativeError);
        }
        foreach (var binding in sharedRetiringBindings) if (!binding.WaitForRelease(out string error)) UnityEngine.Debug.LogError(error);
        foreach (var cache in sharedRetiringCaches) if (!cache.WaitForRelease(out string error)) UnityEngine.Debug.LogError(error);
        CollectSharedRetirement();
    }
    private static bool SharedFiniteBounds(Bounds bounds) =>
        !float.IsNaN(bounds.min.x) && !float.IsInfinity(bounds.min.x) && !float.IsNaN(bounds.min.z) && !float.IsInfinity(bounds.min.z)
        && !float.IsNaN(bounds.max.x) && !float.IsInfinity(bounds.max.x) && !float.IsNaN(bounds.max.z) && !float.IsInfinity(bounds.max.z);
    private static void BeginSharedAuthoringScope(bool complete)
    { sharedNotificationCaptured = true; sharedScopeUnknown |= !complete; }
    private static void CaptureSharedAuthoringTile(Vector2Int tile)
    {
        var settings = LoadWorldSettings();
        if (settings == null || tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth || tile.y >= settings.HeightTileGridHeight
            || sharedAffectedTiles.Count >= TerrainAuthoringPreviewGeographicAuthoringProjection.MaximumIncomingTiles)
        { sharedScopeUnknown = true; return; }
        sharedAffectedTiles.Add(tile);
    }
    private static void CaptureSharedRegionalScope(TerrainRegionalElevationInvalidationScope scope)
    { sharedRegionalScope = TerrainRegionalElevationResidencyPolicy.Merge(sharedRegionalScope, scope); }
    private static void RegisterSharedAuthoringInvalidation(bool contentOnly)
    {
        sharedScopeUnknown |= !contentOnly || !sharedNotificationCaptured; sharedNotificationCaptured = false;
        sharedFailedTarget = sharedError = ""; sharedTileFailures.Clear();
        // Keep the union until complete required publication, including superseded work.
        dirtyCompositeTiles.Clear(); overallSignatureAcknowledgementRequested = false;
        PublishTerrainAnalysisSourceState(); NotifyPreviewStateChanged();
    }
    private static void ClearSharedAuthoringScope()
    { sharedAffectedTiles.Clear(); sharedScopeUnknown = false; sharedRegionalScope = TerrainRegionalElevationInvalidationScope.None; sharedNotificationCaptured = false; }
    private static void RetrySharedHeightFailures()
    {
        sharedFailedTarget = sharedError = analysisSourceError = ""; sharedTileFailures.Clear();
        RetireSharedWork(ref sharedDisplayWork); RetireSharedWork(ref sharedNativeWork);
        sharedRunningTile = null; sharedQueue.Clear(); sharedSource = null;
    }
    private static bool RequestSharedHeightPreview(WorldSettings settings, TerrainAuthoringData data, out string error)
    {
        RequestGeographicDemandRefresh(); error = geographicDemandError;
        if (latestGeographicDemand == null || latestDisplayIntent == null || settings == null || data == null)
        { if (string.IsNullOrEmpty(error)) error = "Waiting for paired shared Height layout and geographical demand."; return false; }
        error = sharedError; return string.IsNullOrEmpty(error);
    }
    private static string SharedTargetStamp(TerrainAuthoringPreviewGeographicDemandPlan demand, string committed) =>
        demand.Generation + ":" + demand.OwnershipGeneration + ":" + demand.PolicyGeneration + ":" + authoringGeneration + ":" + committed;
    private static bool SharedTargetStillCurrent(TerrainAuthoringPreviewDisplayIntent intent, TerrainAuthoringPreviewGeographicDemandPlan demand) =>
        CanRunEditorPreviewWork && sharedSource != null && sharedSource.IsCurrent && ReferenceEquals(intent, latestDisplayIntent)
        && ReferenceEquals(demand, latestGeographicDemand) && sharedAcceptedGeneration == authoringGeneration && sharedHeight != null && !sharedHeight.IsDisposed;

    private static void AdvanceSharedHeightPreview()
    {
        var settings = LoadWorldSettings(); var data = LoadAuthoringData();
        if (settings == null || data == null || displayCommitInProgress) return;
        RequestGeographicDemandRefresh(); var target = latestGeographicDemand; var intent = latestDisplayIntent;
        if (target == null || intent == null) return;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (sharedSource != null && sharedAcceptedGeneration == authoringGeneration && sharedOverall != overall)
        { BeginSharedAuthoringScope(false); RegisterPreviewAuthoringInvalidation("Authoring identity changed without a complete tile notification."); return; }
        string stamp = SharedTargetStamp(target, committed);
        if (stamp == sharedFailedTarget) return;
        if (sharedHeight != null && (sharedDemand == null || !sharedDemand.ConfigurationMatches(settings)
            || sharedDemand.OwnershipGeneration != target.OwnershipGeneration || sharedQuality.Generation != target.PolicyGeneration))
        { ReleaseSharedHeightPreview(true); CollectSharedRetirement(); }
        // Serial incompatible-owner replacement prevents hidden old/new budget overlap.
        if (sharedRetiringCaches.Count != 0 || sharedRetiringWork.Count >= 4) return;
        if (sharedSource == null || sharedDemand == null || sharedDemand.Generation != target.Generation
            || sharedAcceptedGeneration != authoringGeneration || sharedCommitted != committed)
            if (!TryAcceptSharedTarget(settings, data, target, committed, overall, out string error)) { FailSharedTarget(stamp, error); return; }
        LastDirtyUpdateAllocations = LastDirtyUpdateCopies = LastDirtyUpdateLoads = LastDirtyUpdateMaterializations = LastDirtyUpdateCompositions = 0;
        var watch = Stopwatch.StartNew(); sharedAnalysisFirst = !sharedAnalysisFirst;
        if (sharedAnalysisFirst) AdvanceSharedNative();
        if (!SharedTargetStillCurrent(intent, target)) return;
        AdvanceSharedDisplay(watch);
        if (!SharedTargetStillCurrent(intent, target)) return;
        if (!sharedAnalysisFirst) AdvanceSharedNative();
        if (!SharedTargetStillCurrent(intent, target)) return;
        if (sharedTileFailures.Count == 0 && sharedRetiringBindings.Count < 2 && SharedAllRequiredPagesCurrent()
            && (sharedBinding == null || sharedPublishedEpoch != sharedHeight.MappingEpoch || !ReferenceEquals(activeDisplayIntent, intent)
                || clipmapRebindRequested || !SharedDisplayIsDrawable(settings, committed)))
        {
            if (!TryPublishSharedDisplay(intent, out string error)) { FailSharedTarget(stamp, error); return; }
            // Publication observers may disable preview or supersede this target.
            if (!SharedTargetStillCurrent(intent, target)) return;
        }
        int current = 0; foreach (var row in sharedDemand.Tiles)
            if (row.HasDisplay && sharedHeight.TryGetPublishedPage(row.Tile, out _, out bool ready) && ready) current++;
        streamingProgress = sharedDemand.RequiredDisplayCount == 0 ? 0 : Mathf.Clamp01((float)current / sharedDemand.RequiredDisplayCount);
        streamingState = sharedTileFailures.Count > 0 ? TerrainAuthoringPreviewStreamingState.Failed
            : sharedQueue.Count > 0 || sharedRunningTile.HasValue ? TerrainAuthoringPreviewStreamingState.Composing : TerrainAuthoringPreviewStreamingState.Idle;
        streamingStatusMessage = SharedHeightStatusText();
        SetStatus(sharedTileFailures.Count > 0 ? TerrainAuthoringPreviewStatus.Error
            : SharedDisplayIsDrawable(settings, committed) ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Preparing, streamingStatusMessage);
        PublishStreamingStateIfChanged(); PublishTerrainAnalysisSourceState(); RepaintEditorViews();
    }
    private static bool TryAcceptSharedTarget(WorldSettings settings, TerrainAuthoringData data, TerrainAuthoringPreviewGeographicDemandPlan target,
        string committed, string overall, out string error)
    {
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out error)
            || !TerrainAuthoringPreviewSharedHeightComposer.TryCreateCommittedSourceContext(settings, data, target, authoringGeneration,
                () => authoringGeneration, () => TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration, out var source, out error)) return false;
        RetireSharedWork(ref sharedDisplayWork); RetireSharedWork(ref sharedNativeWork);
        sharedRunningTile = null; sharedQueue.Clear(); sharedTileFailures.Clear(); sharedError = "";
        TerrainAuthoringPreviewGeographicAuthoringProjection acceptedProjection = null;
        if (sharedHeight == null)
        {
            sharedQuality = TerrainAuthoringPreviewQualityPolicy.GetSnapshot(settings);
            if (!TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, sharedQuality, target, ++sharedResourceGeneration,
                committed, authoringGeneration, out sharedHeight, out error, CalculateSharedPoolCapacities(settings, target, sharedQuality))) return false;
            fullCommittedBuildCount++;
            foreach (var row in target.Tiles) if (row.NativeWorkingRequired) sharedNativeChanged.Add(row.Tile);
        }
        else
        {
            // Exact predecessor projection precedes movement demand acceptance.
            if (sharedAcceptedGeneration != authoringGeneration)
            {
                var empty = Array.Empty<Vector2Int>();
                if (!TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, sharedDemand, sharedHeight,
                    sharedAcceptedGeneration, authoringGeneration, committed, sharedScopeUnknown ? null : sharedAffectedTiles,
                    sharedScopeUnknown ? null : empty, sharedRegionalScope, out var projection, out error)
                    || !sharedHeight.TryAcceptAuthoringProjection(projection, out var proof, out error)) return false;
                acceptedProjection = projection;
                lastGlobalDirtyTileCount = sharedAffectedTiles.Count; lastResidentDirtyTileCount = projection.DisplayJobCount;
                lastNonresidentDirtyTileCount = projection.NonresidentLogicalChangeCount;
                foreach (var row in projection.Targets) if (row.NativeRequired && row.Changed) sharedNativeChanged.Add(row.Tile);
                if (projection.CanProveUntouched) foreach (var row in sharedDemand.Tiles)
                    if (row.HasDisplay && !projection.Affects(row.Tile)) sharedHeight.TryAcknowledgeUnchangedPage(row.Tile, proof, out _);
            }
            if (!sharedHeight.TryAcceptDemand(target, committed, authoringGeneration, out error)) return false;
        }
        if (sharedPublishedNative?.Native != null && sharedPublishedNative.Native.TryAcceptCurrentTarget(source,
            acceptedProjection, analysisOutputWindow, analysisRequiredSourceWindow, out _)) sharedPublishedNative.Source = source;
        analysisSourceError = ""; sharedSource = source; sharedDemand = target; sharedAcceptedGeneration = authoringGeneration;
        sharedCommitted = committed; sharedOverall = overall;
        sharedPhysicalCompletions.RemoveWhere(tile => !target.TryGetTile(tile, out var row) || !row.HasDisplay);
        sharedDisplayWork = new SharedWorkOwner { Source = source };
        if (!TerrainAuthoringPreviewSharedHeightComposer.TryCreate(sharedHeight, source, sharedDisplayWork.Compositor,
            sharedDisplayWork.Materializer, out sharedDisplayWork.Display, out error, 1)) return false;
        foreach (var row in target.Tiles)
            if (row.HasDisplay && (!sharedHeight.TryGetPublishedPage(row.Tile, out _, out bool ready) || !ready)) sharedQueue.Add(row.Tile);
        sharedQueue.Sort((a, b) => CompareSharedPriority(target, a, b));
        sharedTargetJobs = sharedQueue.Count; sharedTargetLoads = 0;
        sharedTargetRetained = target.RequiredDisplayCount + target.OptionalDisplayCount - sharedTargetJobs;
        streamingRequestGeneration++; sharedFailedTarget = ""; return true;
    }
    private static Dictionary<int, int> CalculateSharedPoolCapacities(WorldSettings settings,
        TerrainAuthoringPreviewGeographicDemandPlan demand, TerrainAuthoringPreviewQualitySnapshot quality)
    {
        float half = TerrainClipmapTopologyUtility.GetLODHalfExtent(settings, settings.clipmapLevelCount - 1);
        long side = (long)Math.Ceiling(2 * half / settings.HeightTileWorldSize) + 10;
        long displayBound = Math.Min(side, settings.HeightTileGridWidth) * Math.Min(side, settings.HeightTileGridHeight);
        long editableBound = Math.Min(quality.EditableWindowSizeTiles, settings.HeightTileGridWidth)
            * (long)Math.Min(quality.EditableWindowSizeTiles, settings.HeightTileGridHeight);
        var result = new Dictionary<int, int>();
        for (int stride = 1; TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride); stride *= 2)
        {
            long count = displayBound;
            if (stride < quality.ContextualMinimumStride)
            {
                long geometryBound = 0;
                for (int level = 0; level < settings.clipmapLevelCount; level++)
                {
                    if (!TerrainHeightResolutionUtility.TryGetRequiredStrideForClipmapLevel(settings, level, out int geometryStride, out _) || geometryStride != stride) continue;
                    float spacing = TerrainClipmapLayoutUtility.GetLODSpacing(settings, level);
                    float extent = TerrainClipmapTopologyUtility.GetLODHalfExtent(settings, level);
                    long geometrySide = (long)Math.Ceiling((2 * extent + 10 * spacing) / settings.HeightTileWorldSize) + 2;
                    geometryBound = Math.Min(geometrySide, settings.HeightTileGridWidth) * Math.Min(geometrySide, settings.HeightTileGridHeight);
                }
                count = Math.Min(editableBound, geometryBound);
            }
            int requested = 0; foreach (var row in demand.Tiles) if (row.HasDisplay && row.SelectedDisplayStride == stride) requested++;
            result[stride] = (int)Math.Min(SystemInfo.maxTextureArraySlices, Math.Max(2L, Math.Max(count, requested) * 2));
            if (stride > int.MaxValue / 2) break;
        }
        return result;
    }
    private static int CompareSharedPriority(TerrainAuthoringPreviewGeographicDemandPlan demand, Vector2Int a, Vector2Int b)
    {
        demand.TryGetTile(a, out var ra); demand.TryGetTile(b, out var rb);
        if (ra.DisplayRequired != rb.DisplayRequired) return ra.DisplayRequired ? -1 : 1;
        return ra.Priority != rb.Priority ? ra.Priority.CompareTo(rb.Priority) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
    }
    private static void AdvanceSharedDisplay(Stopwatch watch)
    {
        if (sharedDisplayWork?.Display == null) return;
        if (!sharedRunningTile.HasValue && sharedQueue.Count > 0)
        {
            var tile = sharedQueue[0]; sharedQueue.RemoveAt(0);
            if (!sharedDisplayWork.Display.TryBeginTile(tile, out _, out string error))
            {
                bool retiring = sharedDisplayWork.Display.HasRetiringWork;
                foreach (var pool in sharedHeight.CapturePoolInventory()) retiring |= pool.RetiringCount > 0;
                if (retiring) { sharedQueue.Insert(0, tile); return; }
                if (sharedHeight.TryGetPublishedPage(tile, out var unbound, out bool current) && !current
                    && (sharedBinding == null || !sharedBinding.Map.TryGetEntry(tile, out var bound) || !bound.Page.Handle.Equals(unbound.Handle)))
                    foreach (var pool in sharedHeight.CapturePoolInventory())
                        if (pool.Stride == unbound.Handle.Stride && pool.FreeCount == 0 && sharedHeight.ReleaseTile(tile, out _))
                        { sharedQueue.Insert(0, tile); return; }
                sharedTileFailures[tile] = error; sharedError = error; return;
            }
            sharedRunningTile = tile; sharedRunningState = TerrainAuthoringPreviewSharedCompositionState.Prepared; LastDirtyUpdateAllocations++;
        }
        if (!sharedRunningTile.HasValue) return;
        var running = sharedRunningTile.Value;
        for (int step = 0; step < 4; step++)
        {
            if (!sharedDisplayWork.Display.TryStep(running, out var result, out string error))
            { sharedTileFailures[running] = error; sharedError = error; break; }
            if (sharedRunningState == TerrainAuthoringPreviewSharedCompositionState.Prepared && result.State == TerrainAuthoringPreviewSharedCompositionState.WaitingForBase)
            { sharedTargetLoads++; LastDirtyUpdateLoads++; LastDirtyUpdateMaterializations++; }
            if (sharedRunningState == TerrainAuthoringPreviewSharedCompositionState.WaitingForBase && result.State == TerrainAuthoringPreviewSharedCompositionState.WaitingForComposite) LastDirtyUpdateCompositions++;
            sharedRunningState = result.State;
            if (result.State == TerrainAuthoringPreviewSharedCompositionState.Published)
            { sharedPhysicalCompletions.Add(running); sharedCompositionCount++; sharedDisplayWork.Display.RetireResult(running); sharedRunningTile = null; return; }
            if (result.State == TerrainAuthoringPreviewSharedCompositionState.Failed || result.State == TerrainAuthoringPreviewSharedCompositionState.Cancelled)
            { sharedTileFailures[running] = result.Error; sharedError = result.Error; break; }
            if (watch.Elapsed.TotalMilliseconds >= DefaultSoftWorkBudgetMilliseconds) return;
        }
        if (sharedTileFailures.ContainsKey(running)) { sharedDisplayWork.Display.RetireResult(running); sharedRunningTile = null; }
    }
    private static bool SharedAllRequiredPagesCurrent()
    {
        if (sharedHeight == null || sharedDemand == null) return false;
        foreach (var row in sharedDemand.Tiles)
            if (row.DisplayRequired && (!sharedHeight.TryGetPublishedPage(row.Tile, out _, out bool current) || !current)) return false;
        return true;
    }
    private static bool SharedRequiredContentIsCurrent(WorldSettings settings)
    {
        if (!SharedDisplayIsDrawable(settings, TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings))
            || committedRebuildRequested || sharedTileFailures.Count > 0 || sharedAcceptedGeneration != authoringGeneration
            || sharedDemand == null || sharedPublishedDemand == null || !sharedDemand.IsEquivalentTo(sharedPublishedDemand)) return false;
        foreach (var row in sharedPublishedDemand.Tiles)
            if (row.DisplayRequired && (!sharedHeight.TryGetPublishedPage(row.Tile, out var page, out bool current) || !current
                || !sharedBinding.Map.TryGetEntry(row.Tile, out var entry) || !entry.Page.Handle.Equals(page.Handle))) return false;
        return true;
    }
    private static bool SharedDisplayIsDrawable(WorldSettings settings, string committed)
    {
        if (!Enabled || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || settings == null
            || activeDisplayIntent == null || !activeDisplayIntent.ConfigurationMatches(settings)
            || activeDisplayIntent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || boundClipmapRoot == null || boundClipmapRoot != activeDisplayIntent.Root
            || !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out _) || root != boundClipmapRoot
            || sharedBinding == null || !sharedBinding.IsAlive || sharedPublishedDemand == null || sharedBinding.Map.CommittedTarget != committed
            || boundHeightRenderers.Count != settings.clipmapLevelCount * 2 - 1) return false;
        foreach (var binding in boundHeightRenderers)
        {
            if (!binding.IsValid || !binding.Renderer.enabled || !sharedRendererOriginals.TryGetValue(binding.Renderer, out var original)
                || !sharedMaterials.TryGetValue(original.Material, out var material) || binding.Renderer.sharedMaterial != material) return false;
            binding.Renderer.GetPropertyBlock(sharedInspectionBlock);
            if (sharedInspectionBlock.GetFloat(TerrainAuthoringPreviewSharedHeightBindingData.EnabledId) != 1
                || sharedInspectionBlock.GetTexture(TerrainAuthoringPreviewSharedHeightBindingData.MapId) != sharedBinding.Lookup) return false;
        }
        return sharedBinding.RequiredMissingCount == 0 && sharedBinding.DrawableCount > 0;
    }
    private static bool TryPublishSharedDisplay(TerrainAuthoringPreviewDisplayIntent intent, out string error)
    {
        error = ""; var demand = sharedDemand; var cache = sharedHeight;
        if (!SharedTargetStillCurrent(intent, demand) || !SharedAllRequiredPagesCurrent() || sharedRetiringBindings.Count >= 2)
        { error = "Shared display publication is waiting for current coverage or lookup retirement."; return false; }
        var renderers = new List<TerrainClipmapRendererBinding>();
        if (!TerrainAuthoringSceneViewController.TryPreflightDisplayPlacement(intent, renderers, out error)) return false;
        if (!cache.TryCreateRenderMapSnapshot(out var map, out error)) return false;
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(intent.Settings, map, out var candidate, out error)) { map.Dispose(); return false; }
        var originalsAdded = new List<MeshRenderer>(); var materialsAdded = new List<Material>();
        var selected = new Dictionary<MeshRenderer, Material>(); var previous = sharedBinding;
        TerrainAuthoringSceneViewController.DisplayPlacementSnapshot placement = null;
        displayCommitInProgress = true;
        try
        {
            foreach (var binding in renderers)
            {
                var renderer = binding.Renderer;
                if (!sharedRendererOriginals.TryGetValue(renderer, out var original))
                { original = new SharedRendererState(renderer); sharedRendererOriginals.Add(renderer, original); originalsAdded.Add(renderer); }
                if (original.Material == null) throw new InvalidOperationException("The shared terrain renderer has no original material.");
                if (!sharedMaterials.TryGetValue(original.Material, out var material))
                {
                    if (!TerrainAuthoringPreviewSharedHeightBindingData.TryCreateMaterial(original.Material, out material, out error)) throw new InvalidOperationException(error);
                    sharedMaterials.Add(original.Material, material); materialsAdded.Add(original.Material);
                }
                selected.Add(renderer, material);
            }
            float low = candidate.MinimumHeight, high = candidate.MaximumHeight;
            if (previous != null && previous.IsAlive) { low = Mathf.Min(low, aggregateMinimumHeight); high = Mathf.Max(high, aggregateMaximumHeight); }
            var controller = intent.Root.GetComponent<TerrainClipmapBoundsController>();
            if (!TryApplySharedRendererTransaction(intent.Settings, sharedQuality, intent.Layout, demand, candidate, renderers, boundHeightRenderers, selected,
                () => { placement = TerrainAuthoringSceneViewController.BeginDisplayPlacement(); return TerrainAuthoringSceneViewController.TryApplyDisplayPlacement(intent, out _); },
                () => TerrainAuthoringSceneViewController.RestoreDisplayPlacement(placement),
                () => controller != null && controller.ApplyBoundsForRange(low, high),
                () =>
                {
                    var verify = new List<TerrainClipmapRendererBinding>();
                    if (!SharedTargetStillCurrent(intent, demand) || !TerrainAuthoringSceneViewController.TryPreflightDisplayPlacement(intent, verify, out _) || verify.Count != renderers.Count) return false;
                    for (int i = 0; i < verify.Count; i++) if (verify[i].Renderer != renderers[i].Renderer || !verify[i].Role.Equals(renderers[i].Role)) return false;
                    foreach (var old in boundHeightRenderers)
                        if (!renderers.Exists(next => next.Renderer == old.Renderer) && old.Renderer != null && sharedRendererOriginals.TryGetValue(old.Renderer, out var original)) original.Restore(old.Renderer);
                    if (!SharedTargetStillCurrent(intent, demand)) return false;
                    foreach (var binding in renderers) binding.Renderer.enabled = sharedRendererOriginals[binding.Renderer].Enabled;
                    TerrainAuthoringSceneViewController.CommitDisplayPlacement(intent); return true;
                }, out error)) throw new InvalidOperationException(error);
            // No fallible renderer writes follow this ownership transfer.
            sharedBinding = candidate; sharedPublishedDemand = demand; sharedPublishedEpoch = cache.MappingEpoch; sharedSaveRestored = false;
            activeDisplayIntent = intent; boundClipmapRoot = intent.Root; boundHeightRenderers.Clear(); boundHeightRenderers.AddRange(renderers);
            aggregateMinimumHeight = low; aggregateMaximumHeight = high; clipmapRebindRequested = committedRebuildRequested = false;
            diagnosticBindingApplyCount++; ClearBoundsFollowUp(); ClearPreviewFollowUps();
        }
        catch (Exception exception)
        {
            error = "The shared Height display could not be published: " + exception.Message;
            foreach (var renderer in originalsAdded) sharedRendererOriginals.Remove(renderer);
            var materials = new List<Material>(); foreach (var original in materialsAdded) { materials.Add(sharedMaterials[original]); sharedMaterials.Remove(original); }
            if (materials.Count > 0) sharedRetiringMaterials.Add(new SharedMaterialRetirement(materials, candidate));
            candidate.Dispose(); sharedRetiringBindings.Add(candidate); return false;
        }
        finally { TerrainAuthoringSceneViewController.EndDisplayPlacement(); displayCommitInProgress = false; }
        if (previous != null) { previous.Dispose(); sharedRetiringBindings.Add(previous); }
        var unusedRenderers = new List<MeshRenderer>(); foreach (var pair in sharedRendererOriginals) if (!renderers.Exists(row => row.Renderer == pair.Key)) unusedRenderers.Add(pair.Key);
        foreach (var renderer in unusedRenderers) sharedRendererOriginals.Remove(renderer);
        var unused = new List<Material>(); var retired = new List<Material>();
        foreach (var pair in sharedMaterials)
        { bool used = false; foreach (var original in sharedRendererOriginals.Values) used |= original.Material == pair.Key;
          if (!used) { unused.Add(pair.Key); retired.Add(pair.Value); } }
        foreach (var material in unused) sharedMaterials.Remove(material);
        if (retired.Count > 0) sharedRetiringMaterials.Add(new SharedMaterialRetirement(retired, previous));
        ClearSharedAuthoringScope(); ClearPendingRegionalElevationInvalidationForCommittedChange();
        var completed = new List<Vector2Int>(); foreach (var tile in sharedPhysicalCompletions)
            if (cache.TryGetPublishedPage(tile, out _, out bool current) && current) completed.Add(tile);
        completed.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)); sharedPhysicalCompletions.Clear(); lastPublishedCompositeTileCount = completed.Count;
        NotifyHeightCacheCoverageIfChanged();
        if (!SharedTargetStillCurrent(intent, demand)) return true;
        var observers = CompositeTilesUpdated;
        if (completed.Count > 0 && observers != null) foreach (Action<IReadOnlyList<Vector2Int>> observer in observers.GetInvocationList())
        { if (!SharedTargetStillCurrent(intent, demand)) break; DispatchPreviewObservers(observer, (IReadOnlyList<Vector2Int>)completed.AsReadOnly(), "Composite tiles"); }
        if (SharedTargetStillCurrent(intent, demand)) NotifyPreviewStateChanged(); return true;
    }
    // The live owner and focused isolated validator use this same rollback boundary.
    internal static bool TryApplySharedRendererTransaction(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot quality,
        TerrainClipmapLayout layout, TerrainAuthoringPreviewGeographicDemandPlan demand, TerrainAuthoringPreviewSharedHeightBindingData data,
        IReadOnlyList<TerrainClipmapRendererBinding> renderers, IReadOnlyList<TerrainClipmapRendererBinding> previousRenderers,
        IReadOnlyDictionary<MeshRenderer, Material> materials, Func<bool> applyPlacement, Func<bool> restorePlacement,
        Func<bool> applyBounds, Func<bool> validateAndCommitPlacement, out string error)
    {
        error = ""; var rollback = new Dictionary<MeshRenderer, SharedRendererState>();
        try
        {
            if (renderers == null || previousRenderers == null || materials == null || applyPlacement == null || restorePlacement == null || applyBounds == null || validateAndCommitPlacement == null)
                throw new InvalidOperationException("A complete shared renderer transaction is required.");
            foreach (var binding in previousRenderers) if (binding.Renderer != null && !rollback.ContainsKey(binding.Renderer)) rollback.Add(binding.Renderer, new SharedRendererState(binding.Renderer));
            foreach (var binding in renderers)
            {
                if (binding.Renderer == null) throw new InvalidOperationException("A shared renderer was destroyed before publication.");
                if (!rollback.ContainsKey(binding.Renderer)) rollback.Add(binding.Renderer, new SharedRendererState(binding.Renderer));
                if (!materials.TryGetValue(binding.Renderer, out var material) || material == null) throw new InvalidOperationException("A shared renderer has no prepared transient material.");
                binding.Renderer.sharedMaterial = material;
            }
            if (!TerrainAuthoringPreviewHeightBindingUtility.TryPreflightShared(settings, quality, layout, demand, data, renderers, false, out error)) throw new InvalidOperationException(error);
            if (!applyPlacement()) throw new InvalidOperationException("The canonical shared display placement failed.");
            if (!TerrainAuthoringPreviewHeightBindingUtility.TryBindShared(settings, quality, layout, demand, data, renderers, false, out error)) throw new InvalidOperationException(error);
            if (!applyBounds()) throw new InvalidOperationException("Shared Height displacement bounds failed.");
            if (!validateAndCommitPlacement()) throw new InvalidOperationException("Shared display ownership or renderer roles changed during publication.");
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message; bool restored;
            try { restored = restorePlacement == null || restorePlacement(); } catch { restored = false; }
            foreach (var pair in rollback) try { if (pair.Key == null) restored = false; else pair.Value.Restore(pair.Key); } catch { restored = false; }
            if (!restored)
            { TerrainAuthoringPreviewHeightBindingUtility.DisableShared(renderers); TerrainAuthoringPreviewHeightBindingUtility.DisableShared(previousRenderers);
              foreach (var pair in rollback) if (pair.Key != null) pair.Key.enabled = false;
              error += " Renderer rollback failed; unsafe terrain draws were disabled."; }
            return false;
        }
    }
    private static void RestoreSharedRendererReferences(bool retainOriginals = false)
    {
        foreach (var pair in sharedRendererOriginals) if (pair.Key != null)
            try { pair.Value.Restore(pair.Key); } catch (Exception exception) { pair.Key.enabled = false; UnityEngine.Debug.LogError("Shared terrain renderer restoration failed: " + exception.Message); }
        if (!retainOriginals) sharedRendererOriginals.Clear();
        if (sharedMaterials.Count > 0) { sharedRetiringMaterials.Add(new SharedMaterialRetirement(new List<Material>(sharedMaterials.Values), sharedBinding)); sharedMaterials.Clear(); }
    }
    internal static void RestoreSharedMaterialsForSceneSave()
    { if (sharedBinding == null) return; sharedSaveRestored = true; RestoreSharedRendererReferences(true); clipmapRebindRequested = true; }
    internal static void ResumeSharedDisplayAfterSceneSave()
    {
        if (!sharedSaveRestored) return; sharedSaveRestored = false;
        foreach (var binding in boundHeightRenderers) if (binding.Renderer != null) binding.Renderer.enabled = false;
        clipmapRebindRequested = true; ScheduleRefresh();
    }
    private static void ReleaseSharedHeightPreview(bool notifyObservers)
    {
        var controller = boundClipmapRoot != null ? boundClipmapRoot.GetComponent<TerrainClipmapBoundsController>() : null;
        if (controller != null) controller.RestoreConfiguredBounds(); RestoreSharedRendererReferences();
        DetachSharedAnalysisSource(notifyObservers); RetireSharedNativeOwners(); RetireSharedWork(ref sharedDisplayWork);
        if (sharedBinding != null) { sharedBinding.Dispose(); sharedRetiringBindings.Add(sharedBinding); sharedBinding = null; }
        if (sharedHeight != null) { sharedHeight.Dispose(); sharedRetiringCaches.Add(sharedHeight); sharedHeight = null; }
        sharedSource = null; sharedDemand = sharedPublishedDemand = null; sharedQuality = null; sharedSaveRestored = false;
        sharedQueue.Clear(); sharedRunningTile = null; sharedPhysicalCompletions.Clear(); sharedNativeChanged.Clear(); sharedTileFailures.Clear();
        sharedError = sharedFailedTarget = sharedCommitted = sharedOverall = ""; ClearSharedAuthoringScope();
        activeDisplayIntent = null; boundHeightRenderers.Clear(); boundClipmapRoot = null; CollectSharedRetirement();
    }
    private static void FailSharedTarget(string stamp, string error)
    {
        sharedFailedTarget = stamp; sharedError = error; lastStreamingFailureMessage = error; streamingState = TerrainAuthoringPreviewStreamingState.Failed;
        SetStatus(TerrainAuthoringPreviewStatus.Error, "Shared Height preview: " + error); PublishStreamingStateIfChanged(); NotifyPreviewStateChanged(); RepaintEditorViews();
    }
    private static bool TryGetSharedDrawablePage(Vector2Int tile, out TerrainAuthoringPreviewHeightPageDescriptor page, out bool current)
    {
        page = default; current = false; var settings = LoadWorldSettings();
        if (!SharedDisplayIsDrawable(settings, TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings)) || !sharedBinding.Map.TryGetEntry(tile, out var entry) || !entry.IsValid) return false;
        page = entry.Page;
        current = sharedAcceptedGeneration == authoringGeneration && sharedHeight.TryGetPublishedPage(tile, out var live, out bool ready)
            && ready && live.Handle.Equals(page.Handle) && !sharedTileFailures.ContainsKey(tile); return true;
    }
    private static TerrainAuthoringPreviewReadiness EvaluateSharedTileReadiness(WorldSettings settings, Vector2Int tile, bool available)
    {
        bool inside = settings != null && tile.x >= 0 && tile.y >= 0 && tile.x < settings.HeightTileGridWidth && tile.y < settings.HeightTileGridHeight;
        bool drawable = TryGetSharedDrawablePage(tile, out _, out bool current);
        return TerrainAuthoringPreviewReadinessPolicy.EvaluateTile(available, inside, drawable, drawable, drawable, drawable, !current);
    }
    private static TerrainAuthoringPreviewInteractionReadiness EvaluateSharedInteractionReadiness(WorldSettings settings, Vector2Int tile, bool available)
    {
        bool inside = settings != null && tile.x >= 0 && tile.y >= 0 && tile.x < settings.HeightTileGridWidth && tile.y < settings.HeightTileGridHeight;
        bool drawable = TryGetSharedDrawablePage(tile, out _, out bool current);
        return TerrainAuthoringPreviewReadinessPolicy.EvaluateInteractionTile(available, inside, drawable, drawable, drawable, drawable, !current);
    }
    // Retained inspection never invents a contiguous per-LOD array. Dispose the map.
    internal static bool TryCaptureSharedDisplayForValidation(out TerrainAuthoringPreviewHeightPageMap map, out string error)
    { map = null; error = "Shared display storage is unavailable."; return sharedHeight != null && sharedHeight.TryCreateRenderMapSnapshot(out map, out error); }
    private static void CancelSharedNativeCandidate() { RetireSharedWork(ref sharedNativeWork); }
    private static void RetireSharedNativeOwners() { RetireSharedWork(ref sharedNativeWork); RetireSharedWork(ref sharedPublishedNative); }
    private static bool TryGetSharedAnalysisSource(out TerrainAnalysisGpuSource source, out TerrainHeightCacheWindow output)
    {
        source = default; output = default;
        if (!CanRunEditorPreviewWork || !sharedAnalysisAttached || !hasAnalysisSourceDemand || !hasAnalysisSourceIntent || sharedPublishedNative?.Native == null
            || analysisSettings != LoadWorldSettings() || latestDisplayIntent?.Root == null || !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out _)
            || root != latestDisplayIntent.Root || analysisOwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || !sharedPublishedNative.Native.TryGetCurrentSource(out var native, out output, out _) || output != analysisOutputWindow || !native.SourceWindow.Contains(analysisRequiredSourceWindow)) return false;
        source = new TerrainAnalysisGpuSource(native.HeightCache, native.SourceWindow, native.WorldTileGridSize, native.SamplesPerSide, native.SampleSpacing, native.WorldSizeXZ,
            native.SourceSignature + "|publication:" + analysisResidencyGeneration + ":" + analysisCompositeGeneration,
            native.SourceResourceIdentity, analysisResidencyGeneration, analysisCompositeGeneration); return source.IsValid;
    }
    private static void DetachSharedAnalysisSource(bool notify)
    {
        bool hadSource = sharedAnalysisAttached; sharedAnalysisAttached = false; if (!hadSource) return;
        analysisResidencyGeneration = NextAnalysisGeneration(analysisResidencyGeneration);
        if (notify) DispatchPreviewObservers(TerrainAnalysisSourceChanged, "Analysis source");
    }
    private static void AdvanceSharedNative()
    {
        // Finish mandatory display pages first; native work can then borrow them
        // and cannot consume capacity needed by first-time display admission.
        if (!SharedAllRequiredPagesCurrent()) return;
        if (!TryCaptureSharedNativeWorkingRequirement(sharedSource.Settings, sharedDemand.OwnershipGeneration, out _) || TryGetSharedAnalysisSource(out _, out _)) return;
        if (sharedNativeWork == null)
        {
            if (sharedRetiringWork.Count >= 4 || !string.IsNullOrEmpty(analysisSourceError)) return;
            long bytes = (long)sharedSource.Settings.HeightTileSamplesPerSide * sharedSource.Settings.HeightTileSamplesPerSide * analysisRequiredSourceWindow.TileCount * sizeof(float);
            if (bytes > sharedHeight.GpuBudgetBytes - sharedHeight.AllocatedBytes - sharedHeight.LookupAllocatedBytes - sharedHeight.AnalysisAllocatedBytes)
            {
                if (sharedPublishedNative != null)
                {
                    var captured = sharedSource; DetachSharedAnalysisSource(true);
                    if (!CanRunEditorPreviewWork || !ReferenceEquals(captured, sharedSource)) return;
                    RetireSharedWork(ref sharedPublishedNative); return;
                }
                if (sharedHeight.AnalysisAllocatedBytes > 0) return;
            }
            sharedNativeWork = new SharedWorkOwner { Source = sharedSource };
            if (!TerrainAuthoringPreviewNativeAnalysisAdapter.TryCreate(sharedHeight, sharedNativeWork.Compositor, sharedNativeWork.Materializer, sharedHeight.GpuBudgetBytes, out sharedNativeWork.Native, out analysisSourceError)
                || !sharedNativeWork.Native.TryBeginBuild(sharedSource, analysisOutputWindow, analysisRequiredSourceWindow, out analysisSourceError))
            { RetireSharedWork(ref sharedNativeWork); PublishTerrainAnalysisSourceState(); return; }
        }
        if (!sharedNativeWork.Native.TryProgress(out bool ready, out analysisSourceError)) { RetireSharedWork(ref sharedNativeWork); PublishTerrainAnalysisSourceState(); return; }
        if (!ready) return;
        var work = sharedNativeWork; var source = sharedSource; long owner = analysisOwnershipGeneration, intent = analysisIntentGeneration;
        // Synchronous consumer detachment precedes old-array retirement. Reentrant
        // observers can supersede focus or disable preview before ownership transfer.
        DetachSharedAnalysisSource(true);
        if (!CanRunEditorPreviewWork || !ReferenceEquals(work, sharedNativeWork) || !ReferenceEquals(source, sharedSource)
            || !source.IsCurrent || owner != analysisOwnershipGeneration || intent != analysisIntentGeneration) return;
        if (!work.Native.TryPublishCandidate(out _, out analysisSourceError)) return;
        RetireSharedWork(ref sharedPublishedNative); sharedPublishedNative = work; sharedNativeWork = null; sharedAnalysisAttached = true;
        analysisResidencyGeneration = NextAnalysisGeneration(analysisResidencyGeneration); analysisCompositeGeneration = NextAnalysisGeneration(analysisCompositeGeneration); analysisSourceError = "";
        var changed = new List<Vector2Int>(); foreach (var tile in sharedNativeChanged) if (sharedDemand.TryGetTile(tile, out var row) && row.NativeWorkingRequired) changed.Add(tile);
        changed.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)); sharedNativeChanged.Clear();
        DispatchPreviewObservers(TerrainAnalysisSourceChanged, "Analysis source");
        if (!TryGetSharedAnalysisSource(out _, out _) || !ReferenceEquals(work, sharedPublishedNative)) return;
        var observers = TerrainAnalysisSourceTilesUpdated;
        if (changed.Count > 0 && observers != null) foreach (Action<IReadOnlyList<Vector2Int>> observer in observers.GetInvocationList())
        { if (!ReferenceEquals(work, sharedPublishedNative) || !TryGetSharedAnalysisSource(out _, out _)) return;
          DispatchPreviewObservers(observer, (IReadOnlyList<Vector2Int>)changed.AsReadOnly(), "Analysis tiles"); }
        if (ReferenceEquals(work, sharedPublishedNative)) PublishTerrainAnalysisSourceState(true);
    }
    private static TerrainAuthoringAnalysisSourceSnapshot CaptureSharedAnalysisMetadata()
    {
        bool ready = TryGetSharedAnalysisSource(out var source, out _); bool failed = !string.IsNullOrEmpty(analysisSourceError);
        var reason = ready ? TerrainAuthoringAnalysisWaitingReason.None : !hasAnalysisSourceIntent ? TerrainAuthoringAnalysisWaitingReason.NoFocus
            : !CanRunEditorPreviewWork ? TerrainAuthoringAnalysisWaitingReason.PreviewSuspended : !hasAnalysisSourceDemand ? TerrainAuthoringAnalysisWaitingReason.NoDemand
            : failed ? TerrainAuthoringAnalysisWaitingReason.NativePreparationFailed : TerrainAuthoringAnalysisWaitingReason.NativePreparationPending;
        string message = ready ? "Current native Height is available for bounded terrain analysis." : failed ? "Native analysis source: " + analysisSourceError : AnalysisWaitingMessage(reason, default);
        return new TerrainAuthoringAnalysisSourceSnapshot(ready ? TerrainAuthoringAnalysisSourceKind.OwnedNative : TerrainAuthoringAnalysisSourceKind.Unavailable,
            analysisOutputWindow, analysisRequiredSourceWindow, ready ? source.SourceWindow : default, analysisGuardTileCount,
            analysisSettings != null ? analysisSettings.HeightTileSamplesPerSide : 0, analysisSettings != null ? TerrainHeightResolutionUtility.GetSampleSpacing(analysisSettings, 1) : 0,
            ready, failed, ready ? sharedPublishedNative.Source.AuthoringGeneration : 0, analysisOwnershipGeneration, analysisResidencyGeneration, analysisCompositeGeneration,
            message, reason, ready ? 0 : analysisRequiredSourceWindow.TileCount, failed ? 1 : 0, 0, authoringGeneration);
    }
    private static string SharedHeightStatusText() => !string.IsNullOrEmpty(sharedError) ? "Shared Height update failed: " + sharedError
        : "Shared Height: " + (sharedQueue.Count + (sharedRunningTile.HasValue ? 1 : 0)) + " display tile(s) pending; " + (sharedDemand?.RequiredDisplayCount ?? 0)
            + " required; native analysis " + (sharedAnalysisAttached ? "published" : sharedNativeWork != null ? "preparing" : "idle") + ".";
    private static TerrainAuthoringPreviewDiagnosticsSnapshot CaptureSharedServiceDiagnostics(WorldSettings settings)
    {
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        bool drawable = SharedDisplayIsDrawable(settings, committed), coverage = LatestDisplayCoverageIsCurrent(settings, committed);
        bool placement = drawable && latestDisplayIntent != null && IsDisplayLayoutPublished(latestDisplayIntent.Layout);
        bool waiting = Enabled && latestDisplayIntent != null && (!coverage || !placement), ready = SharedRequiredContentIsCurrent(settings);
        int demanded = 0, requiredCount = 0, current = 0, stale = 0, missing = 0; var counts = new SortedDictionary<int, int>();
        if (sharedDemand != null) foreach (var row in sharedDemand.Tiles)
        {
            if (!row.HasDisplay) continue; demanded++; if (row.DisplayRequired) requiredCount++;
            counts.TryGetValue(row.SelectedDisplayStride, out int count); counts[row.SelectedDisplayStride] = count + 1;
            if (!sharedHeight.TryGetPublishedPage(row.Tile, out _, out bool tileCurrent)) missing++;
            else if (tileCurrent && sharedAcceptedGeneration == authoringGeneration) current++; else stale++;
        }
        var strides = new System.Text.StringBuilder(); foreach (var pair in counts) { if (strides.Length > 0) strides.Append(", "); strides.Append(pair.Key).Append(":").Append(pair.Value); }
        int backlog = sharedQueue.Count + (sharedRunningTile.HasValue ? 1 : 0) + sharedTileFailures.Count;
        var failure = default(TerrainAuthoringPreviewDirtyFailureSnapshot); var failedTiles = new List<Vector2Int>(sharedTileFailures.Keys);
        failedTiles.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        if (failedTiles.Count > 0) { var tile = failedTiles[0]; failure = new TerrainAuthoringPreviewDirtyFailureSnapshot(tile,
            new TerrainAuthoringPreviewDirtyFailure(authoringGeneration, sharedTileFailures[tile], TryGetSharedDrawablePage(tile, out _, out _))); }
        long retiring = 0; foreach (var cache in sharedRetiringCaches) retiring += cache.AllocatedBytes + cache.LookupAllocatedBytes + cache.AnalysisAllocatedBytes;
        var shared = new TerrainAuthoringPreviewSharedHeightSnapshot(sharedHeight != null, demanded, requiredCount, current, stale, missing, backlog, sharedTileFailures.Count,
            sharedHeight?.AllocatedBytes ?? 0, sharedHeight?.LookupAllocatedBytes ?? 0, sharedHeight?.AnalysisAllocatedBytes ?? 0, retiring, strides.ToString(), failure);
        var displayFailure = new TerrainAuthoringPreviewFailureSnapshot(!string.IsNullOrEmpty(sharedFailedTarget), TerrainAuthoringPreviewCachePublication.DisplayHeightSet, -1, false, default, default,
            streamingRequestGeneration, latestDisplayIntent?.PlacementGeneration ?? 0, sharedError);
        var worker = new TerrainAuthoringPreviewWorkerSnapshot(backlog > 0, TerrainAuthoringPreviewCachePublication.DisplayHeightSet, TerrainAuthoringPreviewTransitionState.ComposingSourceTiles,
            streamingRequestGeneration, authoringGeneration, owner, sharedTargetJobs == 0 ? 1 : (float)(sharedDisplayWork?.Display?.DisplayCompositionCount ?? 0) / sharedTargetJobs,
            sharedTargetJobs, sharedTargetLoads, sharedTargetJobs, sharedTargetLoads, sharedDisplayWork?.Display?.DisplayCompositionCount ?? 0, sharedTargetRetained, 0,
            LastDirtyUpdateAllocations, LastDirtyUpdateLoads, 0, LastDirtyUpdateMaterializations, LastDirtyUpdateCompositions, sharedTargetLoads);
        return new TerrainAuthoringPreviewDiagnosticsSnapshot(Enabled, ready, drawable, coverage, placement, coverage && placement, status, statusMessage,
            streamingState, streamingStatusMessage, worker.Progress, IsStreaming, waiting, authoringGeneration, streamingRequestGeneration, worker, default,
            CaptureSharedAnalysisMetadata(), default, default, CaptureHeightOwnership(), displayFailure, default, displayFailure.Present && waiting, sharedHeight?.PeakCombinedAllocatedBytes ?? 0,
            TerrainAuthoringPreviewCache.DiagnosticCreateCount, TerrainAuthoringPreviewCache.DiagnosticDisposeCount, TerrainAuthoringPreviewCache.DiagnosticLiveCount,
            IsEditorLifecycleStable, CanRunEditorPreviewWork, lifecycleResumePending, suspensionReasons, TerrainAuthoringSceneViewController.HasControllingSceneView,
            TerrainAuthoringSceneViewController.ControllingSceneViewInstanceId, owner, TerrainAuthoringSceneViewController.FollowSceneView, TerrainAuthoringSceneViewController.FollowSource,
            TerrainAuthoringSceneViewController.FreezePreview, backlog, LastDirtyUpdateLoads, LastDirtyUpdateMaterializations, LastDirtyUpdateCompositions, lastStreamingCancellationReason,
            Array.Empty<TerrainAuthoringPreviewLodDiagnosticsSnapshot>(), LastDirtyUpdateAllocations, 0, boundsFollowUpPending, analysisFollowUpPending, boundsFollowUpError, analysisFollowUpError,
            latestFollowUpError, sharedAcceptedGeneration != authoringGeneration, sharedAffectedTiles.Count, default, latestDisplayIntent?.PlacementGeneration ?? 0,
            TerrainAuthoringPreviewHeightSourceUtility.CaptureDiagnostics(), shared);
    }
}
