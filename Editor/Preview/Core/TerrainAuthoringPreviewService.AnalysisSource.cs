using System;
using System.Collections.Generic;
using UnityEngine;

internal enum TerrainAuthoringAnalysisSourceKind
{
    Unavailable,
    BorrowedNative,
    OwnedNative
}

internal enum TerrainAuthoringAnalysisWaitingReason
{
    None, NoFocus, NoDemand, PreviewSuspended, InvalidContext,
    NativeCoverageMissing, NativeContentPending, NativeContentFailed,
    NativePublicationPending, NativePreparationPending, NativePreparationFailed
}

internal readonly struct TerrainAuthoringNativeAnalysisWindowState
{
    internal readonly bool Coverage;
    internal readonly bool ContentCurrent;
    internal readonly int PendingCount;
    internal readonly int FailedCount;
    internal readonly int PublicationCount;
    internal bool Ready => Coverage && ContentCurrent && PublicationCount == 0;

    internal TerrainAuthoringNativeAnalysisWindowState(bool coverage, bool current,
        int pending, int failed, int publication)
    {
        Coverage = coverage; ContentCurrent = current;
        PendingCount = pending; FailedCount = failed; PublicationCount = publication;
    }
}

internal readonly struct TerrainAuthoringAnalysisSourceSnapshot
{
    internal readonly TerrainAuthoringAnalysisSourceKind Kind;
    internal readonly TerrainHeightCacheWindow OutputWindow;
    internal readonly TerrainHeightCacheWindow RequiredSourceWindow;
    internal readonly TerrainHeightCacheWindow PhysicalWindow;
    internal readonly int GuardTileCount;
    internal readonly int SamplesPerSide;
    internal readonly float SampleSpacing;
    internal readonly bool Ready;
    internal readonly bool Failed;
    internal readonly long AuthoringGeneration;
    internal readonly long OwnershipGeneration;
    internal readonly long ResidencyGeneration;
    internal readonly long CompositeGeneration;
    internal readonly string Message;
    internal readonly TerrainAuthoringAnalysisWaitingReason WaitingReason;
    internal readonly int PendingRequiredTileCount;
    internal readonly int FailedRequiredTileCount;
    internal readonly int PendingPublicationTileCount;
    internal readonly long TargetAuthoringGeneration;

    internal TerrainAuthoringAnalysisSourceSnapshot(TerrainAuthoringAnalysisSourceKind kind,
        TerrainHeightCacheWindow output, TerrainHeightCacheWindow required, TerrainHeightCacheWindow physical,
        int guard, int samples, float spacing, bool ready, bool failed, long authoring, long owner,
        long residency, long composite, string message,
        TerrainAuthoringAnalysisWaitingReason waitingReason = TerrainAuthoringAnalysisWaitingReason.None,
        int pending = 0, int failedTiles = 0, int publication = 0, long target = 0)
    {
        Kind = kind;
        OutputWindow = output;
        RequiredSourceWindow = required;
        PhysicalWindow = physical;
        GuardTileCount = guard;
        SamplesPerSide = samples;
        SampleSpacing = spacing;
        Ready = ready;
        Failed = failed;
        AuthoringGeneration = authoring;
        OwnershipGeneration = owner;
        ResidencyGeneration = residency;
        CompositeGeneration = composite;
        Message = message;
        WaitingReason = waitingReason; PendingRequiredTileCount = pending;
        FailedRequiredTileCount = failedTiles; PendingPublicationTileCount = publication;
        TargetAuthoringGeneration = target;
    }
}

/*
 * Preview-owned native Height demand for interactive analysis. Physical Height
 * storage and bounded derived output have independent layouts. The selected
 * source is borrowed by consumers; only the optional analysis state is owned
 * here. Height preparation uses the shared preview transaction worker.
 */
public static partial class TerrainAuthoringPreviewService
{
    internal static event Action TerrainAnalysisSourceChanged;
    internal static event Action<IReadOnlyList<Vector2Int>> TerrainAnalysisSourceTilesUpdated;
    internal static event Action TerrainAnalysisSourceStateChanged;

    private static long analysisResidencyGeneration;
    private static long analysisCompositeGeneration;
    private static long analysisIntentGeneration;
    private static long analysisAcceptedRequestGeneration;
    private static TerrainAuthoringPreviewCacheSetTransition lastFailedAnalysisCacheSetRequest;
    private static long analysisOwnershipGeneration;
    private static long analysisOwnedOwnershipGeneration;
    private static bool hasAnalysisSourceIntent;
    private static bool hasAnalysisSourceDemand;
    private static WorldSettings analysisSettings;
    private static Vector3 analysisFocus;
    private static TerrainHeightCacheWindow analysisOutputWindow;
    private static TerrainHeightCacheWindow analysisRequiredSourceWindow;
    private static int analysisGuardTileCount;
    private static TerrainAuthoringPreviewLodState analysisOwnedState;
    private static TerrainAuthoringPreviewCache analysisSelectedCache;
    private static TerrainAuthoringAnalysisSourceKind analysisSelectedKind;
    private static int analysisSelectedResourceIdentity;
    private static long analysisSelectedOwnershipGeneration;
    private static TerrainHeightCacheWindow analysisSelectedPhysicalWindow;
    private static TerrainHeightCacheWindow analysisSelectedOutputWindow;
    private static string analysisSelectedCommittedSignature = "";
    private static string analysisSourceError = "";
    private static string lastAnalysisSourceState = "";

    internal static long AnalysisResidencyGeneration => analysisResidencyGeneration;
    internal static long AnalysisCompositeGeneration => analysisCompositeGeneration;
    internal static bool HasTerrainAnalysisSourceIntent => hasAnalysisSourceIntent;

    // This query never creates demand, changes focus, or starts Height work.
    internal static bool TryGetTerrainAnalysisGpuSource(out TerrainAnalysisGpuSource source)
    {
        return TryGetTerrainAnalysisGpuSource(out source, out _);
    }

    internal static bool TryGetTerrainAnalysisGpuSource(out TerrainAnalysisGpuSource source,
        out TerrainHeightCacheWindow outputWindow)
    { return TryGetSharedAnalysisSource(out source, out outputWindow); }

    private static bool TryGetLegacyTerrainAnalysisSource(out TerrainAnalysisGpuSource source,
        out TerrainHeightCacheWindow outputWindow)
    {
        source = default;
        outputWindow = default;
        var settings = analysisSettings;
        var data = LoadAuthoringData();
        if (!Enabled || !hasAnalysisSourceIntent || settings == null || data == null
            || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            || analysisOwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
            return false;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        if (analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative)
        {
            if (!TryResolveNativeDisplayState(settings, committed, out var native)
                || !ReferenceEquals(native.ActiveCache, analysisSelectedCache)
                || !CaptureNativeAnalysisWindow(native, settings, committed).Ready) return false;
        }
        else if (analysisSelectedKind != TerrainAuthoringAnalysisSourceKind.OwnedNative
            || analysisOwnedOwnershipGeneration != analysisOwnershipGeneration
            || !ReferenceEquals(analysisSelectedCache, analysisOwnedState?.ActiveCache)
            || !IsNativeAnalysisCacheEligible(analysisSelectedCache, settings,
                analysisOwnedState.ActiveAuthoringGeneration, authoringGeneration, committed,
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data),
                analysisRequiredSourceWindow, analysisOutputWindow)) return false;

        if (analysisSelectedOwnershipGeneration != analysisOwnershipGeneration
            || analysisSelectedResourceIdentity != analysisSelectedCache.HeightCache.GetInstanceID()
            || analysisSelectedPhysicalWindow != new TerrainHeightCacheWindow(
                analysisSelectedCache.CacheOriginTile, analysisSelectedCache.CacheSize)
            || analysisSelectedOutputWindow != analysisOutputWindow) return false;
        var cache = analysisSelectedCache;
        string signature = committed + "|native:" + cache.SamplesPerSide + ":" + cache.SampleSpacing
            + "|cache:" + analysisSelectedResourceIdentity + "|source:" + analysisSelectedPhysicalWindow
            + "|output:" + analysisOutputWindow + "|residency:" + analysisResidencyGeneration
            + "|composite:" + analysisCompositeGeneration;
        source = new TerrainAnalysisGpuSource(cache.HeightCache, analysisSelectedPhysicalWindow,
            new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight),
            cache.SamplesPerSide, cache.SampleSpacing, cache.WorldSizeXZ, signature,
            analysisSelectedResourceIdentity, analysisResidencyGeneration, analysisCompositeGeneration);
        outputWindow = analysisOutputWindow;
        return source.IsValid;
    }

    internal static bool TryRequestTerrainAnalysisGpuSource(out TerrainAnalysisGpuSource source,
        out TerrainHeightCacheWindow outputWindow, out string error)
    {
        source = default;
        outputWindow = default;
        error = "";
        if (!CanRunEditorPreviewWork)
        {
            error = "Native terrain analysis is waiting for a stable enabled editor preview.";
            return false;
        }
        var settings = LoadWorldSettings();
        var data = LoadAuthoringData();
        if (settings == null || data == null)
        {
            error = "Native terrain analysis requires WorldSettings and TerrainAuthoringData.";
            return false;
        }
        if (!hasAnalysisSourceIntent || analysisSettings != settings)
            RecordTerrainAnalysisFocus(settings,
                TerrainClipmapLayoutUtility.CalculateWorldCenterPosition(settings, 0f));
        if (!hasAnalysisSourceIntent)
        {
            error = analysisSourceError;
            return false;
        }
        hasAnalysisSourceDemand = true;
        EvaluateTerrainAnalysisSource(settings, data);
        if (TryGetTerrainAnalysisGpuSource(out source, out outputWindow)) return true;
        // Actual admission/allocation occurs only from the shared worker callback.
        PublishTerrainAnalysisSourceState();
        error = GetTerrainAnalysisSourceSnapshot().Message;
        return false;
    }

    internal static void RecordTerrainAnalysisFocus(WorldSettings settings, Vector3 focus)
    {
        if (!CanRunEditorPreviewWork) return;
        if (!TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(settings, focus,
            out var output, out var required, out int guard, out string error))
        {
            analysisSourceError = error;
            hasAnalysisSourceIntent = false;
            CancelTerrainAnalysisPreparation("Native analysis focus became invalid.");
            SelectTerrainAnalysisCache(null, TerrainAuthoringAnalysisSourceKind.Unavailable);
            PublishTerrainAnalysisSourceState();
            return;
        }
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        if (hasAnalysisSourceIntent && analysisSettings == settings && analysisOutputWindow == output
            && analysisRequiredSourceWindow == required && analysisOwnershipGeneration == owner) return;
        CancelTerrainAnalysisPreparation("The native analysis focus changed.");
        analysisSettings = settings;
        analysisFocus = focus;
        analysisOutputWindow = output;
        analysisRequiredSourceWindow = required;
        analysisGuardTileCount = guard;
        analysisOwnershipGeneration = owner;
        hasAnalysisSourceIntent = true;
        analysisIntentGeneration = NextAnalysisGeneration(analysisIntentGeneration);
        lastFailedAnalysisCacheSetRequest = null;
        analysisSourceError = "";
        EvaluateTerrainAnalysisSource(settings, LoadAuthoringData());
    }

    internal static bool IsNativeAnalysisCacheStructurallyEligible(TerrainAuthoringPreviewCache cache,
        WorldSettings settings, string committed, TerrainHeightCacheWindow required,
        TerrainHeightCacheWindow output)
    {
        if (cache == null || settings == null || !required.IsValid || !output.IsValid
            || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, 1)
            || !cache.IsReady || cache.HeightCache == null || !cache.HeightCache.IsCreated()
            || cache.SampleStride != 1 || string.IsNullOrEmpty(committed)
            || cache.SourceCommittedHeightfieldSignature != committed
            || cache.SamplesPerSide != TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1)
            || !Mathf.Approximately(cache.SampleSpacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1))
            || cache.WorldSizeXZ != TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings)) return false;
        var texture = cache.HeightCache;
        var physical = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
        var grid = new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        return physical.Contains(required) && required.Contains(output)
            && texture.format == RenderTextureFormat.RFloat
            && texture.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray
            && texture.width == cache.SamplesPerSide && texture.height == cache.SamplesPerSide
            && texture.volumeDepth == physical.TileCount
            && TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(physical, grid,
                TerrainAnalysisWindowUtility.CalculateRequiredInteractiveGuardTileCount(settings),
                out var safe, out _) && safe.Contains(output);
    }

    internal static bool IsNativeAnalysisCacheEligible(TerrainAuthoringPreviewCache cache,
        WorldSettings settings, long cacheGeneration, long currentGeneration,
        string committed, string overall, TerrainHeightCacheWindow required,
        TerrainHeightCacheWindow output)
    {
        return IsNativeAnalysisCacheStructurallyEligible(cache, settings, committed, required, output)
            && cache.IsCompleteForActivation && cacheGeneration == currentGeneration
            && !string.IsNullOrEmpty(overall) && cache.SourceOverallAuthoringSignature == overall;
    }

    internal static TerrainAuthoringNativeAnalysisWindowState EvaluateNativeAnalysisWindow(
        TerrainAuthoringPreviewLodState state, WorldSettings settings, string committed, long generation,
        TerrainHeightCacheWindow required, TerrainHeightCacheWindow output,
        ISet<Vector2Int> unprojected, TerrainRegionalElevationInvalidationScope regional,
        ISet<Vector2Int> publication)
    {
        if (state == null || state.Level != 0 || state.SampleStride != 1 || !state.HasUsableActiveAllocation
            || state.ActiveCache.SampleStride != state.SampleStride
            || state.ActiveCache.SamplesPerSide != state.SamplesPerSide
            || !Mathf.Approximately(state.ActiveCache.SampleSpacing, state.SampleSpacing)
            || !IsNativeAnalysisCacheStructurallyEligible(state.ActiveCache, settings, committed, required, output))
            return default;
        int pending = 0, failed = 0, publishing = 0;
        for (int z = required.OriginTile.y; z < required.MaximumExclusive.y; z++)
            for (int x = required.OriginTile.x; x < required.MaximumExclusive.x; x++)
            {
                var tile = new Vector2Int(x, z);
                if (!IsResidentTileContentCurrent(state, committed, generation, tile, unprojected,
                    TerrainRegionalElevationResidencyPolicy.ScopeAffectsTile(settings, regional, tile))) pending++;
                if (state.DirtyFailures.ContainsKey(tile)) failed++;
                if (publication != null && publication.Contains(tile)) publishing++;
            }
        return new TerrainAuthoringNativeAnalysisWindowState(true, pending == 0, pending, failed, publishing);
    }

    private static TerrainAuthoringNativeAnalysisWindowState CaptureNativeAnalysisWindow(
        TerrainAuthoringPreviewLodState state, WorldSettings settings, string committed)
    {
        return EvaluateNativeAnalysisWindow(state, settings, committed, authoringGeneration,
            analysisRequiredSourceWindow, analysisOutputWindow, dirtyCompositeTiles,
            hasPendingRegionalElevationInvalidation ? pendingRegionalElevationInvalidation
                : TerrainRegionalElevationInvalidationScope.None, pendingNativePublication);
    }

    private static bool TryResolveNativeDisplayState(WorldSettings settings, string committed,
        out TerrainAuthoringPreviewLodState state)
    {
        state = null;
        if (committedRebuildRequested || activeHeightStates == null || activeHeightStates.Length == 0
            || activeDisplayIntent == null || activeDisplayIntent.Root == null
            || activeDisplayIntent.Root != boundClipmapRoot || !activeDisplayIntent.ConfigurationMatches(settings)
            || activeDisplayIntent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
            return false;
        var native = activeHeightStates[0];
        if (native == null || native.Level != 0 || native.SampleStride != 1 || !native.HasUsableActiveAllocation
            || native.ActiveCache.SourceCommittedHeightfieldSignature != committed) return false;
        state = native; return true;
    }

    private static void EvaluateTerrainAnalysisSource(WorldSettings settings, TerrainAuthoringData data,
        bool notifyState = true)
    { RequestGeographicDemandRefresh(); if (notifyState) PublishTerrainAnalysisSourceState(); }

    private static void EvaluateLegacyAnalysisSource(WorldSettings settings, TerrainAuthoringData data,
        bool notifyState = true)
    {
        if (!hasAnalysisSourceIntent || settings == null || data == null) return;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        var local = TryResolveNativeDisplayState(settings, committed, out var native)
            ? CaptureNativeAnalysisWindow(native, settings, committed) : default;
        if (local.Ready)
            SelectTerrainAnalysisCache(native.ActiveCache, TerrainAuthoringAnalysisSourceKind.BorrowedNative);
        else if (analysisOwnedOwnershipGeneration == analysisOwnershipGeneration
            && IsNativeAnalysisCacheEligible(analysisOwnedState?.ActiveCache, settings,
                analysisOwnedState?.ActiveAuthoringGeneration ?? -1L, authoringGeneration,
                committed, overall, analysisRequiredSourceWindow, analysisOutputWindow))
            SelectTerrainAnalysisCache(analysisOwnedState.ActiveCache, TerrainAuthoringAnalysisSourceKind.OwnedNative);
        else
        {
            bool selectedOwned = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.OwnedNative
                && ReferenceEquals(analysisSelectedCache, analysisOwnedState?.ActiveCache)
                && analysisOwnedOwnershipGeneration == analysisOwnershipGeneration
                && analysisOwnedState != null && !analysisOwnedState.WriteFailed;
            bool selectedNative = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
                && local.Coverage && ReferenceEquals(analysisSelectedCache, native?.ActiveCache);
            bool retain = (selectedOwned || selectedNative)
                && analysisSelectedOwnershipGeneration == analysisOwnershipGeneration
                && analysisSelectedOutputWindow == analysisOutputWindow
                && IsNativeAnalysisCacheStructurallyEligible(analysisSelectedCache, settings, committed,
                    analysisRequiredSourceWindow, analysisOutputWindow);
            if (!retain) SelectTerrainAnalysisCache(null, TerrainAuthoringAnalysisSourceKind.Unavailable);
        }
        if (notifyState) PublishTerrainAnalysisSourceState();
    }

    private static void SelectTerrainAnalysisCache(TerrainAuthoringPreviewCache cache,
        TerrainAuthoringAnalysisSourceKind kind)
    {
        int identity = cache != null ? cache.HeightCache.GetInstanceID() : 0;
        var physical = cache != null ? new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize) : default;
        var output = cache != null ? analysisOutputWindow : default;
        string committed = cache != null ? cache.SourceCommittedHeightfieldSignature : "";
        long owner = cache != null ? analysisOwnershipGeneration : 0L;
        if (identity == analysisSelectedResourceIdentity && physical == analysisSelectedPhysicalWindow
            && owner == analysisSelectedOwnershipGeneration
            && output == analysisSelectedOutputWindow && kind == analysisSelectedKind
            && committed == analysisSelectedCommittedSignature) return;
        bool contentPublished = identity != 0 && identity != analysisSelectedResourceIdentity;
        analysisSelectedCache = cache;
        analysisSelectedKind = kind;
        analysisSelectedResourceIdentity = identity;
        analysisSelectedOwnershipGeneration = owner;
        analysisSelectedPhysicalWindow = physical;
        analysisSelectedOutputWindow = output;
        analysisSelectedCommittedSignature = committed;
        analysisResidencyGeneration = NextAnalysisGeneration(analysisResidencyGeneration);
        if (contentPublished) analysisCompositeGeneration = NextAnalysisGeneration(analysisCompositeGeneration);
        DispatchPreviewObservers(TerrainAnalysisSourceChanged, "Analysis source");
    }

    internal static bool TryAcknowledgeOwnedNativeAuthoring(TerrainAuthoringPreviewLodState state,
        WorldSettings settings, string committed, string overall, long previous, long current,
        bool contentOnly, bool ownershipCurrent, ISet<Vector2Int> incoming,
        TerrainRegionalElevationInvalidationScope regional)
    {
        var cache = state?.ActiveCache;
        if (!contentOnly || !ownershipCurrent || state == null || !state.CacheReady
            || state.ActiveAuthoringGeneration != previous || !state.HasUsableActiveAllocation
            || state.PendingDirtyTiles.Count != 0 || state.PendingRegionalTiles.Count != 0
            || state.DirtyFailures.Count != 0 || cache.SampleStride != 1
            || cache.SourceCommittedHeightfieldSignature != committed || string.IsNullOrEmpty(overall)) return false;
        var physical = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
        for (int z = physical.OriginTile.y; z < physical.MaximumExclusive.y; z++)
            for (int x = physical.OriginTile.x; x < physical.MaximumExclusive.x; x++)
            {
                var tile = new Vector2Int(x, z);
                if (incoming != null && incoming.Contains(tile)
                    || TerrainRegionalElevationResidencyPolicy.ScopeAffectsTile(settings, regional, tile)) return false;
            }
        cache.MarkOverallAuthoringSignature(overall);
        state.ActiveAuthoringGeneration = state.DirtyTargetGeneration = current;
        return true;
    }

    private static void InvalidateTerrainAnalysisAuthoring(bool contentOnly, long previousGeneration)
    {
        var data = LoadAuthoringData();
        if (analysisOwnedState != null)
        {
            string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(analysisSettings);
            string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(analysisSettings, data);
            bool ownedValid = data != null && hasAnalysisSourceIntent && analysisSettings != null
                && analysisOwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
                && analysisOwnedOwnershipGeneration == analysisOwnershipGeneration
                && IsNativeAnalysisCacheStructurallyEligible(analysisOwnedState.ActiveCache, analysisSettings,
                    committed, analysisRequiredSourceWindow, analysisOutputWindow);
            if (!TryAcknowledgeOwnedNativeAuthoring(analysisOwnedState, analysisSettings, committed, overall,
                previousGeneration, authoringGeneration, contentOnly, ownedValid, dirtyCompositeTiles,
                hasPendingRegionalElevationInvalidation ? pendingRegionalElevationInvalidation
                    : TerrainRegionalElevationInvalidationScope.None)) analysisOwnedState.CacheReady = false;
        }
        analysisSourceError = "";
        lastFailedAnalysisCacheSetRequest = null;
        TryRunAnalysisFollowUp(() =>
        {
            if (hasAnalysisSourceIntent) EvaluateTerrainAnalysisSource(analysisSettings, data);
            PublishTerrainAnalysisSourceState();
        });
    }

    internal static void CollectNativePublicationTiles(TerrainAuthoringPreviewLodState native,
        WorldSettings settings, string committed, long generation, bool hasIntent,
        TerrainHeightCacheWindow required, bool windowCurrent, ISet<Vector2Int> markers,
        ISet<Vector2Int> unprojected, TerrainRegionalElevationInvalidationScope regional,
        List<Vector2Int> consume, List<Vector2Int> changed)
    {
        foreach (var tile in markers)
        {
            if (native?.ActiveCache == null || native.ActiveCache.GetSliceIndex(tile.x, tile.y) < 0)
            { consume.Add(tile); continue; }
            if (!IsResidentTileContentCurrent(native, committed, generation, tile, unprojected,
                TerrainRegionalElevationResidencyPolicy.ScopeAffectsTile(settings, regional, tile))) continue;
            if (!hasIntent || !required.Contains(tile)) consume.Add(tile);
            else if (windowCurrent) { consume.Add(tile); changed.Add(tile); }
        }
        SortWorldTilesRowMajor(consume); SortWorldTilesRowMajor(changed);
    }

    private static void PublishNativeTerrainAnalysisCompositeUpdate()
    {
        if (pendingNativePublication.Count == 0) return;
        var settings = LoadWorldSettings();
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        long target = authoringGeneration, owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        long intent = analysisIntentGeneration;
        var required = analysisRequiredSourceWindow; var output = analysisOutputWindow;
        var display = activeDisplayIntent;
        var native = activeHeightStates != null && activeHeightStates.Length > 0 ? activeHeightStates[0] : null;
        var cache = native?.ActiveCache; var texture = cache?.HeightCache;
        bool nativeContext = TryResolveNativeDisplayState(settings, committed, out var resolved)
            && ReferenceEquals(resolved, native);
        var local = nativeContext ? CaptureNativeAnalysisWindow(native, settings, committed) : default;
        bool currentFocus = hasAnalysisSourceIntent && analysisSettings == settings
            && analysisOwnershipGeneration == owner;
        // Resolve fallible authoring inputs before consuming any publication intent.
        var data = currentFocus ? LoadAuthoringData() : null;
        if (currentFocus && data == null) return;
        var consume = new List<Vector2Int>(); var changed = new List<Vector2Int>();
        CollectNativePublicationTiles(native, settings, committed, target, currentFocus,
            required, local.Coverage && local.ContentCurrent,
            pendingNativePublication, dirtyCompositeTiles,
            hasPendingRegionalElevationInvalidation ? pendingRegionalElevationInvalidation
                : TerrainRegionalElevationInvalidationScope.None, consume, changed);
        if (consume.Count == 0) return;
        bool wasBorrowed = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
            && ReferenceEquals(analysisSelectedCache, cache);
        long previousComposite = analysisCompositeGeneration, previousResidency = analysisResidencyGeneration;
        var previousSelected = analysisSelectedCache; var previousKind = analysisSelectedKind;
        // Publish identity and consume old intent before any observer can query or queue new work.
        if (changed.Count > 0 && wasBorrowed) analysisCompositeGeneration = NextAnalysisGeneration(analysisCompositeGeneration);
        long publishedComposite = analysisCompositeGeneration;
        foreach (var tile in consume) pendingNativePublication.Remove(tile);
        if (!currentFocus) return;
        try { EvaluateTerrainAnalysisSource(settings, data, false); }
        catch
        {
            // Internal preparation failed before observers: preserve the bounded retry's work.
            // If a callback already changed identity, retain its newer intent untouched.
            if (NativeAnalysisPublicationContextIsCurrent(target, owner, intent, display, native, cache,
                    texture, required, output)
                && ReferenceEquals(previousSelected, analysisSelectedCache) && previousKind == analysisSelectedKind
                && previousResidency == analysisResidencyGeneration && publishedComposite == analysisCompositeGeneration)
            {
                analysisCompositeGeneration = previousComposite;
                foreach (var tile in consume) pendingNativePublication.Add(tile);
            }
            throw;
        }
        if (!NativeAnalysisPublicationContextIsCurrent(target, owner, intent, display, native, cache,
            texture, required, output)) return;
        // A source replacement already invalidates derived layers through SourceChanged.
        if (changed.Count > 0 && wasBorrowed && analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
            && ReferenceEquals(analysisSelectedCache, cache))
        {
            var observers = TerrainAnalysisSourceTilesUpdated;
            if (observers != null) foreach (Action<IReadOnlyList<Vector2Int>> observer in observers.GetInvocationList())
            {
                if (!NativeAnalysisPublicationContextIsCurrent(target, owner, intent, display, native, cache,
                    texture, required, output)) return;
                DispatchPreviewObservers(observer, (IReadOnlyList<Vector2Int>)changed, "Analysis tiles");
            }
        }
        if (NativeAnalysisPublicationContextIsCurrent(target, owner, intent, display, native, cache,
            texture, required, output))
            PublishTerrainAnalysisSourceState(true);
    }

    private static bool NativeAnalysisPublicationContextIsCurrent(long target, long owner, long intent,
        TerrainAuthoringPreviewDisplayIntent display, TerrainAuthoringPreviewLodState native,
        TerrainAuthoringPreviewCache cache, RenderTexture texture, TerrainHeightCacheWindow required,
        TerrainHeightCacheWindow output)
    {
        return target == authoringGeneration && owner == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && intent == analysisIntentGeneration && required == analysisRequiredSourceWindow && output == analysisOutputWindow
            && ReferenceEquals(display, activeDisplayIntent) && display != null && display.Root == boundClipmapRoot
            && display.OwnershipGeneration == owner && ReferenceEquals(cache, native?.ActiveCache)
            && texture != null && texture.IsCreated() && ReferenceEquals(texture, cache?.HeightCache)
            && activeHeightStates != null && activeHeightStates.Length > 0 && ReferenceEquals(native, activeHeightStates[0]);
    }

    private static TerrainAuthoringPreviewResidencyPlan CreateTerrainAnalysisPlan(WorldSettings settings)
    {
        return new TerrainAuthoringPreviewResidencyPlan
        {
            Generation = (int)(analysisIntentGeneration % int.MaxValue), LevelCount = 1,
            CoverageCenter = analysisFocus,
            Levels = new[] { new TerrainAuthoringPreviewLodResidencyPlan
            {
                Level = 0, SampleStride = 1, Anchor = analysisFocus,
                SamplesPerSide = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1),
                SampleSpacing = TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1),
                RequiredWindow = analysisRequiredSourceWindow, DesiredWindow = analysisRequiredSourceWindow
            }}
        };
    }

    private static bool TerrainAnalysisHeightIsRequired => hasAnalysisSourceDemand && hasAnalysisSourceIntent
        && !TryGetTerrainAnalysisGpuSource(out _, out _);

    private static bool HasLiveTerrainAnalysisDemand => hasAnalysisSourceDemand && hasAnalysisSourceIntent
        && analysisSettings != null
        && analysisOwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
        && TerrainAuthoringVisualizationController.RequiresLiveTerrainAnalysisDuringInteractiveEdit;

    private static bool IsNativeAnalysisPreparationSuppressed(string committed, string overall)
    {
        var failed = lastFailedAnalysisCacheSetRequest;
        return failed != null && failed.MatchesContent(committed, overall, authoringGeneration,
            TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration, false)
            && failed.AcceptedPlan.Generation == (int)(analysisIntentGeneration % int.MaxValue)
            && failed.Entries.Length == 1 && failed.Entries[0].Target == analysisRequiredSourceWindow;
    }

    internal static bool RequiresOwnedNativeAnalysisPreparation(bool heightRequired, bool residentNativeCoverage) =>
        heightRequired && !residentNativeCoverage;

    private static bool HasResidentNativeAnalysisCoverage(string committed) =>
        hasAnalysisSourceIntent && TryResolveNativeDisplayState(analysisSettings, committed, out var native)
            && CaptureNativeAnalysisWindow(native, analysisSettings, committed).Coverage;

    private static bool IsTerrainAnalysisPreparationRunnable(string committed, string overall)
    {
        return RequiresOwnedNativeAnalysisPreparation(TerrainAnalysisHeightIsRequired,
                HasResidentNativeAnalysisCoverage(committed)) && !IsNativeAnalysisPreparationSuppressed(committed, overall)
            && (!analysisFollowUpPending || lastAnalysisFollowUpAttempt != FollowUpBoundary);
    }

    private static void AdmitPendingTerrainAnalysisSource(WorldSettings settings, TerrainAuthoringData data)
    {
        if (!hasAnalysisSourceDemand || !hasAnalysisSourceIntent || settings == null || data == null) return;
        RecordTerrainAnalysisFocus(settings, analysisFocus);
        EvaluateTerrainAnalysisSource(settings, data);
        if (!TerrainAnalysisHeightIsRequired)
        {
            CancelTerrainAnalysisPreparation("Current native Height already satisfies terrain analysis.");
            if (analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative && analysisOwnedState != null)
            {
                heightCompositor.ReleaseTextureBindings();
                analysisOwnedState.Dispose();
                analysisOwnedState = null;
            }
            return;
        }
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        if (HasResidentNativeAnalysisCoverage(committed))
        {
            CancelTerrainAnalysisPreparation("Resident native Height is updating the analysis dependency window.");
            PublishTerrainAnalysisSourceState();
            return;
        }
        if (LegacyTransitionInProgress || hasPendingStreamingStart
            || TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(
                TerrainAuthoringPreviewCachePublication.NativeAnalysis, HasActiveInteractiveTerrainAuthoringEdit,
                HasLiveTerrainAnalysisDemand)) return;
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            analysisSourceError = "Native terrain analysis is waiting for valid committed Height and current authoring identity.";
            PublishTerrainAnalysisSourceState();
            return;
        }
        if (!RequiresOwnedNativeAnalysisPreparation(TerrainAnalysisHeightIsRequired,
            HasResidentNativeAnalysisCoverage(committed)) || IsNativeAnalysisPreparationSuppressed(committed, overall)) return;
        var plan = CreateTerrainAnalysisPlan(settings);
        var request = CreateCacheSetRequest(settings, plan, new[] { analysisRequiredSourceWindow }, new[] { false },
            TerrainAuthoringPreviewCachePublication.NativeAnalysis, committed, overall, false);
        if (QueueCacheSetRequest(request, out string error))
        {
            analysisAcceptedRequestGeneration = request.RequestGeneration;
            analysisSourceError = "";
        }
        else analysisSourceError = error;
        PublishTerrainAnalysisSourceState();
    }

    internal static bool IsTerrainAnalysisTransactionCurrent(TerrainAuthoringPreviewCacheSetTransition t,
        TerrainAuthoringPreviewResidencyPlan intent, long requestGeneration)
    {
        return t != null && t.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis
            && intent != null && intent.IsStructurallyValid && t.Entries.Length == 1
            && t.Entries[0].Plan.SampleStride == 1 && t.RequestGeneration == requestGeneration
            && t.AcceptedPlan.Generation == intent.Generation
            && TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(t.AcceptedPlan, intent)
            && t.Entries[0].Target.Contains(intent.Levels[0].RequiredWindow);
    }

    private static void PublishTerrainAnalysisHeight(TerrainAuthoringPreviewCacheSetTransition t)
    {
        var previous = analysisOwnedState;
        var states = t.TransferPreparedStates();
        retiringAnalysisState = previous; analysisOwnedState = states[0];
        analysisOwnedOwnershipGeneration = t.OwnershipGeneration; analysisSourceError = "";
        CaptureTransitionMemoryEstimate();
        try
        {
            TryRunAnalysisFollowUp(() =>
            {
                EvaluateTerrainAnalysisSource(analysisSettings, LoadAuthoringData(), false);
                PublishTerrainAnalysisSourceState(true);
            });
        }
        finally
        {
            heightCompositor.ReleaseTextureBindings(); previous?.Dispose(); retiringAnalysisState = null; CompleteTransitionMemoryTracking();
        }
    }

    private static void CancelTerrainAnalysisPreparation(string reason)
    {
        CancelSharedNativeCandidate();
        if ((LegacyTransitionInProgress && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
            || (hasPendingStreamingStart && pendingCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis))
            CancelCurrentStreamingTransition(reason, false);
    }

    private static void DetachTerrainAnalysisBorrowing(TerrainAuthoringPreviewCache releasing)
    {
        if (releasing == null || !ReferenceEquals(releasing, analysisSelectedCache)) return;
        TryRunAnalysisFollowUp(() =>
        {
            SelectTerrainAnalysisCache(null, TerrainAuthoringAnalysisSourceKind.Unavailable);
            PublishTerrainAnalysisSourceState();
        });
    }

    private static void ReleaseTerrainAnalysisSource(bool notifyObservers)
    {
        DetachSharedAnalysisSource(notifyObservers); RetireSharedNativeOwners();
        var owned = analysisOwnedState;
        bool hadSource = analysisSelectedResourceIdentity != 0;
        analysisOwnedState = null;
        analysisSelectedCache = null;
        analysisSelectedKind = TerrainAuthoringAnalysisSourceKind.Unavailable;
        analysisSelectedResourceIdentity = 0;
        analysisSelectedOwnershipGeneration = 0;
        analysisSelectedPhysicalWindow = analysisSelectedOutputWindow = default;
        analysisSelectedCommittedSignature = "";
        hasAnalysisSourceIntent = hasAnalysisSourceDemand = false;
        analysisSettings = null;
        analysisOutputWindow = analysisRequiredSourceWindow = default;
        analysisGuardTileCount = 0;
        analysisAcceptedRequestGeneration = 0;
        lastFailedAnalysisCacheSetRequest = null;
        analysisOwnershipGeneration = analysisOwnedOwnershipGeneration = 0;
        analysisSourceError = "";
        if (hadSource) analysisResidencyGeneration = NextAnalysisGeneration(analysisResidencyGeneration);
        if (notifyObservers && hadSource) DispatchPreviewObservers(TerrainAnalysisSourceChanged, "Analysis source");
        try { if (notifyObservers) TryRunAnalysisFollowUp(() => PublishTerrainAnalysisSourceState(true)); }
        finally { heightCompositor.ReleaseTextureBindings(); owned?.Dispose(); }
    }

    internal static TerrainAuthoringAnalysisSourceSnapshot GetTerrainAnalysisSourceSnapshot()
    {
        bool displayConfigurationCurrent = activeDisplayIntent != null
            && activeDisplayIntent.OwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && activeDisplayIntent.Root != null && activeDisplayIntent.Root == boundClipmapRoot
            && activeDisplayIntent.ConfigurationMatches(analysisSettings);
        return CaptureAnalysisSourceMetadata(displayConfigurationCurrent);
    }

    internal static TerrainAuthoringAnalysisSourceSnapshot CaptureAnalysisSourceMetadata(bool displayConfigurationCurrent,
        string committed = null)
    { return CaptureSharedAnalysisMetadata(); }

    private static TerrainAuthoringAnalysisSourceSnapshot CaptureLegacyAnalysisMetadata(bool displayConfigurationCurrent,
        string committed = null)
    {
        // Metadata only: no authoring signature construction, allocation or activation audit.
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        bool borrowed = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative;
        var native = activeHeightStates != null && activeHeightStates.Length > 0 ? activeHeightStates[0] : null;
        var state = borrowed ? native : analysisOwnedState;
        var cache = state?.ActiveCache;
        committed = committed ?? (!string.IsNullOrEmpty(analysisSelectedCommittedSignature)
            ? analysisSelectedCommittedSignature : native?.ActiveCache?.SourceCommittedHeightfieldSignature ?? "");
        bool nativeSettingsValid = TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(analysisSettings, 1);
        int samples = nativeSettingsValid ? TerrainHeightResolutionUtility.GetSamplesPerSide(analysisSettings, 1) : 0;
        float spacing = nativeSettingsValid ? TerrainHeightResolutionUtility.GetSampleSpacing(analysisSettings, 1) : 0f;
        bool context = Enabled && hasAnalysisSourceIntent && analysisSettings != null
            && analysisOwnershipGeneration == owner && !Application.isPlaying
            && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
        var local = context && displayConfigurationCurrent && !committedRebuildRequested
            ? CaptureNativeAnalysisWindow(native, analysisSettings, committed) : default;
        bool ownedCurrent = context && analysisOwnedOwnershipGeneration == owner
            && StateCurrentForDiagnostics(analysisOwnedState,
                NativeCacheGeometryCurrentForDiagnostics(analysisOwnedState?.ActiveCache, samples, spacing))
            && IsNativeAnalysisCacheStructurallyEligible(analysisOwnedState?.ActiveCache, analysisSettings,
                committed, analysisRequiredSourceWindow, analysisOutputWindow);
        bool identity = cache != null && cache.HeightCache != null && cache.HeightCache.IsCreated()
            && ReferenceEquals(cache, analysisSelectedCache) && analysisSelectedOwnershipGeneration == owner
            && cache.HeightCache.GetInstanceID() == analysisSelectedResourceIdentity
            && analysisSelectedOutputWindow == analysisOutputWindow
            && analysisSelectedPhysicalWindow == new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
        bool ready = context && identity && (borrowed ? local.Ready :
            analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.OwnedNative && ownedCurrent);
        bool preparationFailed = lastFailedAnalysisCacheSetRequest != null
            && lastFailedAnalysisCacheSetRequest.AuthoringGeneration == authoringGeneration
            && lastFailedAnalysisCacheSetRequest.OwnershipGeneration == owner
            && lastFailedAnalysisCacheSetRequest.AcceptedPlan.Generation == (int)(analysisIntentGeneration % int.MaxValue);
        var reason = ready ? TerrainAuthoringAnalysisWaitingReason.None
            : !hasAnalysisSourceIntent ? TerrainAuthoringAnalysisWaitingReason.NoFocus
            : !CanRunEditorPreviewWork ? TerrainAuthoringAnalysisWaitingReason.PreviewSuspended
            : !context ? TerrainAuthoringAnalysisWaitingReason.InvalidContext
            : !hasAnalysisSourceDemand ? TerrainAuthoringAnalysisWaitingReason.NoDemand
            : local.FailedCount > 0 ? TerrainAuthoringAnalysisWaitingReason.NativeContentFailed
            : local.Coverage && !local.ContentCurrent ? TerrainAuthoringAnalysisWaitingReason.NativeContentPending
            : local.Coverage && local.PublicationCount > 0 ? TerrainAuthoringAnalysisWaitingReason.NativePublicationPending
            : preparationFailed ? TerrainAuthoringAnalysisWaitingReason.NativePreparationFailed
            : HasNativeAnalysisPreparation ? TerrainAuthoringAnalysisWaitingReason.NativePreparationPending
            : TerrainAuthoringAnalysisWaitingReason.NativeCoverageMissing;
        string message = AnalysisWaitingMessage(reason, local);
        if (preparationFailed && reason == TerrainAuthoringAnalysisWaitingReason.NativePreparationFailed
            && !string.IsNullOrEmpty(analysisSourceError)) message += " " + analysisSourceError;
        return new TerrainAuthoringAnalysisSourceSnapshot(analysisSelectedKind, analysisOutputWindow,
            analysisRequiredSourceWindow, analysisSelectedPhysicalWindow, analysisGuardTileCount, samples, spacing,
            ready, reason == TerrainAuthoringAnalysisWaitingReason.NativeContentFailed
                || reason == TerrainAuthoringAnalysisWaitingReason.NativePreparationFailed
                || !hasAnalysisSourceIntent && !string.IsNullOrEmpty(analysisSourceError),
            state?.ActiveAuthoringGeneration ?? 0L, analysisOwnershipGeneration,
            analysisResidencyGeneration, analysisCompositeGeneration, message, reason,
            local.PendingCount, local.FailedCount, local.PublicationCount, authoringGeneration);
    }

    private static bool HasNativeAnalysisPreparation =>
        LegacyTransitionInProgress && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis
        || hasPendingStreamingStart && pendingCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis;

    private static string AnalysisWaitingMessage(TerrainAuthoringAnalysisWaitingReason reason,
        TerrainAuthoringNativeAnalysisWindowState local)
    {
        switch (reason)
        {
            case TerrainAuthoringAnalysisWaitingReason.None: return "Current native Height is available for bounded terrain analysis.";
            case TerrainAuthoringAnalysisWaitingReason.NoFocus: return string.IsNullOrEmpty(analysisSourceError)
                ? "No native terrain analysis focus is available." : analysisSourceError;
            case TerrainAuthoringAnalysisWaitingReason.NoDemand: return "Native terrain analysis has not requested Height.";
            case TerrainAuthoringAnalysisWaitingReason.PreviewSuspended: return "Native terrain analysis is paused with the editor preview.";
            case TerrainAuthoringAnalysisWaitingReason.InvalidContext: return "Native terrain analysis is waiting for current settings and Scene View ownership.";
            case TerrainAuthoringAnalysisWaitingReason.NativeContentFailed: return $"Native analysis dependency has {local.FailedCount} failed Height update(s). Safe last-good terrain remains active; retry the affected update.";
            case TerrainAuthoringAnalysisWaitingReason.NativeContentPending: return $"Waiting for {local.PendingCount} current native Height dependency tile(s) in {analysisRequiredSourceWindow}.";
            case TerrainAuthoringAnalysisWaitingReason.NativePublicationPending: return "Native Height is prepared; waiting for its analysis source publication.";
            case TerrainAuthoringAnalysisWaitingReason.NativePreparationFailed: return "The required owned native Height preparation failed.";
            case TerrainAuthoringAnalysisWaitingReason.NativePreparationPending: return "Preparing owned native Height for analysis dependencies " + analysisRequiredSourceWindow + ".";
            default: return "Waiting for native Height coverage of analysis dependencies " + analysisRequiredSourceWindow + ".";
        }
    }

    private static void PublishTerrainAnalysisSourceState(bool forceBoundary = false)
    {
        var state = GetTerrainAnalysisSourceSnapshot();
        string snapshot = state.Kind + "|" + state.Ready + "|" + state.ResidencyGeneration + "|"
            + state.CompositeGeneration + "|" + state.Message;
        if (!forceBoundary && snapshot == lastAnalysisSourceState) return;
        lastAnalysisSourceState = snapshot;
        DispatchPreviewObservers(TerrainAnalysisSourceStateChanged, "Analysis state");
    }

    private static long NextAnalysisGeneration(long current)
    {
        return current < long.MaxValue ? current + 1 : long.MaxValue;
    }


    private static bool NativeCacheGeometryCurrentForDiagnostics(TerrainAuthoringPreviewCache cache, int samples, float spacing) =>
        samples > 1 && analysisSettings != null && cache != null && cache.SampleStride == 1
        && cache.SamplesPerSide == samples && Mathf.Approximately(cache.SampleSpacing, spacing)
        && cache.WorldSizeXZ == TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(analysisSettings);

    // Known exact-native consumer intent only. No stamp-wide native window is
    // inferred: contextual display authoring can run at its selected coarse stride.
    internal static bool TryCaptureSharedNativeWorkingRequirement(WorldSettings settings,
        long ownership, out TerrainAuthoringPreviewNativeWorkingDemand requirement)
    {
        requirement = default;
        if (!Enabled || !hasAnalysisSourceIntent || !hasAnalysisSourceDemand || analysisSettings != settings
            || ownership != analysisOwnershipGeneration
            || ownership != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || !analysisRequiredSourceWindow.IsValid) return false;
        requirement = new TerrainAuthoringPreviewNativeWorkingDemand(analysisRequiredSourceWindow,
            TerrainAuthoringPreviewNativeWorkingReason.Analysis);
        return true;
    }

    // Explicit selection seam only; the normal analysis source/observers stay on
    // their existing path. The future owner publishes once after consumer detachment,
    // then projects ordinary source identity/generations through the same observers.
    internal static bool TryGetExplicitSharedNativeSource(TerrainAuthoringPreviewNativeAnalysisAdapter adapter,
        out TerrainAnalysisGpuSource source, out TerrainHeightCacheWindow output, out string error)
    {
        source = default; output = default; error = "No shared-native analysis adapter was explicitly selected.";
        return adapter != null && adapter.TryGetCurrentSource(out source, out output, out error);
    }
}


