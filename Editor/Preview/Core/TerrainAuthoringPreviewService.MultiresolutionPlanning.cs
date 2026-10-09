using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class TerrainAuthoringPreviewService
{
    private static TerrainAuthoringPreviewResidencyPlan latestMultiresolutionResidencyPlan;
    private static TerrainAuthoringPreviewDisplayIntent latestDisplayIntent;
    private static bool displayPreparationDeferredForInteractiveEdit;
    private static TerrainAuthoringPreviewDisplayIntent activeDisplayIntent;
    private static int nextMultiresolutionResidencyGeneration = 1;
    private static long nextPlacementGeneration;
    private static string lastMultiresolutionPlanningError = "";
    private static TerrainAuthoringPreviewLodState[] activeHeightStates;
    private static IReadOnlyList<TerrainAuthoringPreviewHeightCacheView> activeHeightView;
    private static TerrainAuthoringPreviewLodState[] retiringHeightStates;
    private static readonly List<TerrainClipmapRendererBinding> boundHeightRenderers = new List<TerrainClipmapRendererBinding>();
    private static bool displayCommitInProgress;
    private static float aggregateMinimumHeight;
    private static float aggregateMaximumHeight;
    private static string publishedHeightCoverageStamp = "";

    internal static int LatestMultiresolutionResidencyGeneration => latestMultiresolutionResidencyPlan?.Generation ?? 0;
    internal static string LastMultiresolutionPlanningError => lastMultiresolutionPlanningError;

    internal static bool TryGetLatestMultiresolutionResidencyPlan(out TerrainAuthoringPreviewResidencyPlan plan)
    {
        plan = latestMultiresolutionResidencyPlan?.CreateSnapshot();
        return plan != null && plan.IsStructurallyValid;
    }

    // SceneGUI records snapshots only. Resource work and binding occur later.
    internal static bool RecordMultiresolutionResidencyIntent(WorldSettings settings,
        TerrainClipmapLayout layout, TerrainAuthoringPreviewFocus focus, out string error)
    {
        error = "";
        if (!CanRunEditorPreviewWork || displayCommitInProgress) return true;
        if (!TerrainAuthoringPreviewLodResidencyUtility.TryBuildPlan(settings, layout,
            nextMultiresolutionResidencyGeneration, out var plan, out error, false)
            || !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out error) || root == null)
        { lastMultiresolutionPlanningError = error; return false; }
        bool samePlan = TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(latestMultiresolutionResidencyPlan, plan);
        if (samePlan) plan.Generation = latestMultiresolutionResidencyPlan.Generation;
        else nextMultiresolutionResidencyGeneration = nextMultiresolutionResidencyGeneration == int.MaxValue ? 1 : nextMultiresolutionResidencyGeneration + 1;
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        bool samePlacement = latestDisplayIntent != null && latestDisplayIntent.Root == root
            && latestDisplayIntent.ConfigurationMatches(settings) && latestDisplayIntent.OwnershipGeneration == owner
            && TerrainAuthoringPreviewDisplayIntent.PlacementMatches(latestDisplayIntent.Layout, layout);
        if (samePlan && samePlacement)
        {
            geographicFocus = focus; hasGeographicFocus = true;
            RequestGeographicDemandRefresh();
            return true;
        }
        latestMultiresolutionResidencyPlan = plan.CreateSnapshot();
        latestDisplayIntent = new TerrainAuthoringPreviewDisplayIntent(settings, root, plan, layout,
            CalculateNextAuthoringGeneration(nextPlacementGeneration), owner);
        nextPlacementGeneration = latestDisplayIntent.PlacementGeneration;
        geographicFocus = focus; hasGeographicFocus = true;
        RequestGeographicDemandRefresh();
        lastMultiresolutionPlanningError = "";
        ScheduleRefresh();
        return true;
    }

    internal static void ClearMultiresolutionResidencyIntent()
    {
        ClearGeographicDemandIntent();
        latestDisplayIntent = null; latestMultiresolutionResidencyPlan = null; lastMultiresolutionPlanningError = "";
        displayPreparationDeferredForInteractiveEdit = false; ClearInteractiveDirtyHint();
    }

    public static bool HasDrawableHeightPreview
    {
        get
        {
            var settings = LoadWorldSettings();
            return PublishedDisplayIsDrawable(settings,
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings));
        }
    }

    private static bool PublishedDisplayIsDrawable(WorldSettings settings, string committed)
    { return SharedDisplayIsDrawable(settings, committed); }

    private static bool LegacyPublishedDisplayIsDrawable(WorldSettings settings, string committed)
    {
        if (!Enabled || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            || activeDisplayIntent == null || !activeDisplayIntent.ConfigurationMatches(settings)
            || boundClipmapRoot == null || activeDisplayIntent.Root != boundClipmapRoot
            || activeHeightStates == null || activeHeightStates.Length != activeDisplayIntent.Plan.LevelCount
            || boundHeightRenderers.Count != activeHeightStates.Length * 2 - 1) return false;
        foreach (var binding in boundHeightRenderers) if (!binding.IsValid) return false;
        for (int i = 0; i < activeHeightStates.Length; i++)
            if (!StateHasResidentCoverage(activeHeightStates[i], activeDisplayIntent.Plan.Levels[i], settings, committed))
                return false;
        return true;
    }

    // Active ownership follows complete publication. Dirty queues change content
    // targets, not allocation geometry; unsafe writes invalidate that ownership.
    internal static bool StateHasResidentCoverage(TerrainAuthoringPreviewLodState state,
        TerrainAuthoringPreviewLodResidencyPlan plan, WorldSettings settings, string committed)
    {
        if (state == null || plan == null || !plan.IsStructurallyValid || settings == null
            || string.IsNullOrEmpty(committed) || !state.HasUsableActiveAllocation) return false;
        var cache = state.ActiveCache;
        var texture = cache.HeightCache;
        var window = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
        return state.Level == plan.Level && state.SampleStride == plan.SampleStride
            && state.SamplesPerSide == plan.SamplesPerSide && Mathf.Approximately(state.SampleSpacing, plan.SampleSpacing)
            && cache.SampleStride == state.SampleStride && cache.SamplesPerSide == state.SamplesPerSide
            && Mathf.Approximately(cache.SampleSpacing, state.SampleSpacing)
            && TerrainHeightStreamingPyramidPolicy.IsHeightRepresentationStrideSupported(settings, state.SampleStride)
            && TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, state.SampleStride)
            && state.SamplesPerSide == TerrainHeightResolutionUtility.GetSamplesPerSide(settings, state.SampleStride)
            && Mathf.Approximately(state.SampleSpacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, state.SampleStride))
            && cache.SourceCommittedHeightfieldSignature == committed
            && cache.WorldSizeXZ == TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings)
            && TerrainAuthoringPreviewResidencyPolicy.IsWindowInsideWorldGrid(window,
                new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight))
            && window.Contains(plan.RequiredWindow) && texture.format == RenderTextureFormat.RFloat
            && texture.dimension == TextureDimension.Tex2DArray && texture.width == state.SamplesPerSide
            && texture.height == state.SamplesPerSide && texture.volumeDepth == cache.SliceCount;
    }

    private static bool ActiveHeightContentIsCurrent(WorldSettings settings, TerrainAuthoringData data)
    { return SharedRequiredContentIsCurrent(settings); }

    private static bool LegacyActiveHeightContentIsCurrent(WorldSettings settings, TerrainAuthoringData data)
    {
        if (committedRebuildRequested || activeHeightStates == null || settings == null || data == null) return false;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        foreach (var state in activeHeightStates) if (!StateContentIsCurrent(state, committed, overall)) return false;
        return true;
    }

    private static bool StateContentIsCurrent(TerrainAuthoringPreviewLodState state, string committed, string overall)
    {
        return state != null && state.CacheReady && state.HasUsableActiveAllocation && state.PendingDirtyTiles.Count == 0
            && state.PendingRegionalTiles.Count == 0 && state.ActiveAuthoringGeneration == authoringGeneration
            && !string.IsNullOrEmpty(committed) && !string.IsNullOrEmpty(overall)
            && state.ActiveCache.SourceCommittedHeightfieldSignature == committed
            && state.ActiveCache.SourceOverallAuthoringSignature == overall;
    }

    internal static bool CanActiveHeightCacheSetCover(TerrainAuthoringPreviewResidencyPlan plan)
    {
        if (plan == null || !plan.IsStructurallyValid || activeHeightStates == null || activeHeightStates.Length != plan.LevelCount) return false;
        var settings = LoadWorldSettings();
        return HasActiveBaseCoverage(plan, settings,
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings));
    }

    private static bool HasActiveBaseCoverage(TerrainAuthoringPreviewResidencyPlan plan,
        WorldSettings settings, string committed)
    {
        if (plan == null || !plan.IsStructurallyValid || activeHeightStates == null
            || activeHeightStates.Length != plan.LevelCount) return false;
        for (int i = 0; i < plan.LevelCount; i++)
        {
            if (!StateHasResidentCoverage(activeHeightStates[i], plan.Levels[i], settings, committed)) return false;
        }
        return true;
    }

    internal static bool IsDisplayLayoutPublished(TerrainClipmapLayout layout)
    {
        return activeDisplayIntent != null && activeDisplayIntent.Root == boundClipmapRoot
            && activeDisplayIntent.OwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && TerrainAuthoringPreviewDisplayIntent.PlacementMatches(activeDisplayIntent.Layout, layout) && HasDrawableHeightPreview;
    }

    private static bool LatestDisplayCoverageIsCurrent(WorldSettings settings, string committed)
    { return latestDisplayIntent != null && latestDisplayIntent.Root == boundClipmapRoot
            && sharedPublishedDemand != null && latestGeographicDemand != null && sharedPublishedDemand.IsEquivalentTo(latestGeographicDemand)
            && latestDisplayIntent.ConfigurationMatches(settings)
            && latestDisplayIntent.OwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && PublishedDisplayIsDrawable(settings, committed); }

    internal static bool RequestPreparedHeightCacheSet(WorldSettings settings, TerrainAuthoringData data,
        TerrainAuthoringPreviewResidencyPlan plan, bool rebuildCommitted, out string error)
    { return RequestSharedHeightPreview(settings, data, out error); }

    private static bool RequestLegacyHeightCacheSetForValidation(WorldSettings settings, TerrainAuthoringData data,
        TerrainAuthoringPreviewResidencyPlan plan, bool rebuildCommitted, out string error)
    {
        error = ""; var intent = latestDisplayIntent;
        if (!Enabled || !CanRunEditorPreviewWork || settings == null || data == null || intent == null
            || !intent.ConfigurationMatches(settings) || intent.Root == null || plan == null || !plan.IsStructurallyValid
            || !TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(plan, intent.Plan))
        { error = "A stable paired display intent and current authoring data are required."; return false; }
        var snapshot = intent.Plan; var grid = new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        foreach (var p in snapshot.Levels)
            if (!TerrainHeightStreamingPyramidPolicy.IsHeightRepresentationStrideSupported(settings, p.SampleStride)
                || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, p.SampleStride)
                || p.SamplesPerSide != TerrainHeightResolutionUtility.GetSamplesPerSide(settings, p.SampleStride)
                || !Mathf.Approximately(p.SampleSpacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, p.SampleStride))
                || !TerrainAuthoringPreviewResidencyPolicy.IsWindowInsideWorldGrid(p.RequiredWindow, grid)
                || !TerrainAuthoringPreviewResidencyPolicy.IsWindowInsideWorldGrid(p.DesiredWindow, grid))
            { error = $"The Height representation or window at LOD {p.Level} is invalid."; return false; }
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        { error = "Current authoring signatures are unavailable."; return false; }
        bool covered = !rebuildCommitted && HasActiveBaseCoverage(snapshot, settings, committed);
        if (covered && (!IsDisplayLayoutPublished(intent.Layout) || clipmapRebindRequested
            || activeDisplayIntent.PlacementGeneration != intent.PlacementGeneration))
            if (!TryCommitDisplayHeight(null, intent, activeHeightStates, out error)) return false;
        // Placement can reuse valid allocations while their content converges.
        // Optional expansion waits; a genuinely missing destination does not.
        if (covered && HasPendingActiveDirtyWork)
        {
            if (HasActiveInteractiveTerrainAuthoringEdit) displayPreparationDeferredForInteractiveEdit = true;
            return true;
        }
        bool current = covered && ActiveHeightContentIsCurrent(settings, data);
        var targets = new TerrainHeightCacheWindow[snapshot.LevelCount]; var expansions = new bool[snapshot.LevelCount];
        bool needsWork = !current; bool critical = !current;
        for (int i = 0; i < snapshot.LevelCount; i++)
        {
            var p = snapshot.Levels[i]; targets[i] = p.RequiredWindow;
            if (!current) continue;
            var c = activeHeightStates[i].ActiveCache; var active = new TerrainHeightCacheWindow(c.CacheOriginTile, c.CacheSize);
            if (TerrainAuthoringPreviewResidencyPolicy.EvaluateSizeHealth(true, active, p.DesiredWindow)
                == TerrainAuthoringPreviewResidencySizeHealth.Oversized) { needsWork = critical = true; continue; }
            targets[i] = active; expansions[i] = active != p.RequiredWindow;
            if (TerrainAuthoringPreviewStreamingPolicy.TryCalculateHeightSetPrefetchTarget(active, p, grid, out var expanded))
            { targets[i] = expanded; expansions[i] = true; needsWork = true; }
        }
        if (!needsWork) { displayPreparationDeferredForInteractiveEdit = false; return true; }
        bool mandatory = rebuildCommitted || !covered || !PublishedDisplayIsDrawable(settings, committed);
        if (TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(
            TerrainAuthoringPreviewCachePublication.DisplayHeightSet, HasActiveInteractiveTerrainAuthoringEdit,
            HasLiveTerrainAnalysisDemand, mandatory))
        { displayPreparationDeferredForInteractiveEdit = true; return true; }
        var occupied = LegacyTransitionInProgress ? currentCacheSetTransition : pendingCacheSetTransition;
        if (!mandatory && (occupied?.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis
            || HasLiveTerrainAnalysisDemand && IsTerrainAnalysisPreparationRunnable(committed, overall))) return true;
        displayPreparationDeferredForInteractiveEdit = false;
        var request = CreateCacheSetRequest(settings, snapshot, targets, expansions,
            TerrainAuthoringPreviewCachePublication.DisplayHeightSet, committed, overall, rebuildCommitted);
        request.DisplayCritical = critical; request.AcceptDisplayIntent(intent, request.RequestGeneration);
        return QueueCacheSetRequest(request, out error);
    }

    internal static bool TryGetActiveHeightCacheSet(out IReadOnlyList<TerrainAuthoringPreviewHeightCacheView> caches)
    {
        caches = activeHeightView; return caches != null && HasDrawableHeightPreview;
    }

    private static IReadOnlyList<TerrainAuthoringPreviewHeightCacheView> CreateHeightView(TerrainAuthoringPreviewLodState[] states)
    {
        var view = new TerrainAuthoringPreviewHeightCacheView[states.Length];
        for (int i = 0; i < states.Length; i++) view[i] = new TerrainAuthoringPreviewHeightCacheView(states[i]);
        return Array.AsReadOnly(view);
    }

    private static void RefreshAggregateHeightRange(bool preservePrevious)
    {
        if (activeHeightStates == null) return;
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        foreach (var s in activeHeightStates)
            if (s.ActiveCache != null) { low = Mathf.Min(low, s.ActiveCache.MinimumHeight); high = Mathf.Max(high, s.ActiveCache.MaximumHeight); }
        aggregateMinimumHeight = preservePrevious ? Mathf.Min(aggregateMinimumHeight, low) : low;
        aggregateMaximumHeight = preservePrevious ? Mathf.Max(aggregateMaximumHeight, high) : high;
    }

    private static void ReleaseActiveHeightCacheSet()
    {
        ReleaseBinding(); ReleaseActiveDirtySource(); heightCompositor.ReleaseTextureBindings();
        ClearInteractiveDirtyHint(); displayPreparationDeferredForInteractiveEdit = false;
        var states = activeHeightStates; activeHeightStates = null; activeHeightView = null; activeDisplayIntent = null;
        if (states != null) foreach (var s in states) s.Dispose();
        if (retiringHeightStates != null) foreach (var s in retiringHeightStates) s.Dispose();
        retiringHeightStates = null; aggregateMinimumHeight = aggregateMaximumHeight = 0;
        pendingCompositePublication.Clear(); pendingNativePublication.Clear(); diagnosticPendingGeographicDirty.Clear();
    }
    private static TerrainAuthoringPreviewGeographicDemandPlan latestGeographicDemand;
    private static TerrainAuthoringPreviewFocus geographicFocus;
    private static bool hasGeographicFocus;
    private static long geographicDemandGeneration;
    private static string geographicDemandError = "";
    internal static string LastGeographicDemandError => geographicDemandError;

    internal static bool TryGetLatestGeographicalHeightDemand(out TerrainAuthoringPreviewGeographicDemandPlan snapshot)
    {
        RequestGeographicDemandRefresh();
        snapshot = latestGeographicDemand?.CreateSnapshot();
        return snapshot != null;
    }

    internal static void ClearGeographicDemandIntent()
    {
        latestGeographicDemand = null; hasGeographicFocus = false; geographicFocus = default; geographicDemandError = "";
    }

    // Intent construction records demand without admitting GPU or legacy cache work.
    internal static void RequestGeographicDemandRefresh()
    {
        var intent = latestDisplayIntent;
        if (!CanRunEditorPreviewWork || !hasGeographicFocus || intent == null || intent.Root == null || intent.Settings == null
            || intent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || geographicFocus.OwnershipGeneration != intent.OwnershipGeneration
            || geographicFocus.WorldIdentity != intent.Settings.GetInstanceID()
            || !intent.ConfigurationMatches(LoadWorldSettings())
            || !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out _) || root != intent.Root)
        { ClearGeographicDemandIntent(); return; }
        var settings = intent.Settings;
        var policy = TerrainAuthoringPreviewQualityPolicy.GetSnapshot(settings);
        if (!TerrainAuthoringPreviewLodResidencyUtility.TryCalculateEditableWindow(settings, policy,
            geographicFocus.PositionXZ, out var focusTile, out var window, out string error))
        { latestGeographicDemand = null; geographicDemandError = error; return; }
        // Only explicitly active native analysis is known to require exact working data.
        // Modifier and elevation composition already work at the destination representation.
        bool analysis = TryCaptureSharedNativeWorkingRequirement(settings, intent.OwnershipGeneration, out var requirement);
        long working = analysis ? analysisIntentGeneration : 0;
        var previous = latestGeographicDemand;
        if (previous != null && previous.ConfigurationMatches(settings) && previous.PolicyGeneration == policy.Generation
            && previous.PlacementGeneration == intent.PlacementGeneration
            && previous.OwnershipGeneration == geographicFocus.OwnershipGeneration
            && previous.FocusKind == geographicFocus.Kind && previous.FocusTile == focusTile
            && previous.EditableWindow == window && previous.NativeWorkingGeneration == working) return;
        TerrainAuthoringPreviewNativeWorkingDemand[] native = analysis
            ? new[] { requirement } : null;
        long next = geographicDemandGeneration + 1;
        if (!TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings, intent.Layout, policy,
            geographicFocus, native, intent.PlacementGeneration, working, next, out var planned, out error))
        { latestGeographicDemand = null; geographicDemandError = error; return; }
        latestGeographicDemand = planned; geographicDemandGeneration = next; geographicDemandError = "";
    }

}



