using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    private static TerrainAuthoringPreviewResidencyPlan latestMultiresolutionResidencyPlan;
    private static TerrainAuthoringPreviewDisplayIntent latestDisplayIntent;
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
        TerrainClipmapLayout layout, out string error)
    {
        error = "";
        if (!CanRunEditorPreviewWork || displayCommitInProgress) return true;
        if (!TerrainAuthoringPreviewLodResidencyUtility.TryBuildPlan(settings, layout,
            nextMultiresolutionResidencyGeneration, out var plan, out error)
            || !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out error) || root == null)
        { lastMultiresolutionPlanningError = error; return false; }
        bool samePlan = TerrainAuthoringPreviewStreamingPolicy.AreMultiresolutionResidencyPlansEquivalent(latestMultiresolutionResidencyPlan, plan);
        if (samePlan) plan.Generation = latestMultiresolutionResidencyPlan.Generation;
        else nextMultiresolutionResidencyGeneration = nextMultiresolutionResidencyGeneration == int.MaxValue ? 1 : nextMultiresolutionResidencyGeneration + 1;
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        bool samePlacement = latestDisplayIntent != null && latestDisplayIntent.Root == root
            && latestDisplayIntent.ConfigurationMatches(settings) && latestDisplayIntent.OwnershipGeneration == owner
            && TerrainAuthoringPreviewDisplayIntent.PlacementMatches(latestDisplayIntent.Layout, layout);
        if (samePlan && samePlacement) return true;
        latestMultiresolutionResidencyPlan = plan.CreateSnapshot();
        latestDisplayIntent = new TerrainAuthoringPreviewDisplayIntent(settings, root, plan, layout,
            CalculateNextAuthoringGeneration(nextPlacementGeneration), owner);
        nextPlacementGeneration = latestDisplayIntent.PlacementGeneration;
        lastMultiresolutionPlanningError = "";
        ScheduleRefresh();
        return true;
    }

    internal static void ClearMultiresolutionResidencyIntent()
    {
        latestDisplayIntent = null; latestMultiresolutionResidencyPlan = null; lastMultiresolutionPlanningError = "";
    }

    public static bool HasDrawableHeightPreview
    {
        get
        {
            if (!Enabled || activeDisplayIntent == null || boundClipmapRoot == null
                || activeDisplayIntent.Root != boundClipmapRoot || activeHeightStates == null
                || activeHeightStates.Length != activeDisplayIntent.Plan.LevelCount) return false;
            if (boundHeightRenderers.Count != activeHeightStates.Length * 2 - 1) return false;
            foreach (var binding in boundHeightRenderers) if (!binding.IsValid) return false;
            foreach (var state in activeHeightStates)
                if (state.WriteFailed || state.ActiveCache == null || !state.ActiveCache.IsCompleteForActivation) return false;
            return true;
        }
    }

    private static bool ActiveHeightContentIsCurrent(WorldSettings settings, TerrainAuthoringData data)
    {
        if (activeHeightStates == null || settings == null || data == null) return false;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        foreach (var state in activeHeightStates) if (!StateContentIsCurrent(state, committed, overall)) return false;
        return true;
    }

    private static bool StateContentIsCurrent(TerrainAuthoringPreviewLodState state, string committed, string overall)
    {
        return state != null && state.CacheReady && !state.WriteFailed && state.PendingDirtyTiles.Count == 0
            && state.ActiveAuthoringGeneration == authoringGeneration && state.ActiveCache != null
            && state.ActiveCache.IsCompleteForActivation && state.ActiveCache.SourceCommittedHeightfieldSignature == committed
            && state.ActiveCache.SourceOverallAuthoringSignature == overall;
    }

    internal static bool CanActiveHeightCacheSetCover(TerrainAuthoringPreviewResidencyPlan plan)
    {
        if (plan == null || !plan.IsStructurallyValid || activeHeightStates == null || activeHeightStates.Length != plan.LevelCount) return false;
        var settings = LoadWorldSettings(); var data = LoadAuthoringData();
        if (!ActiveHeightContentIsCurrent(settings, data)) return false;
        for (int i = 0; i < plan.LevelCount; i++)
        {
            var s = activeHeightStates[i]; var p = plan.Levels[i]; var c = s.ActiveCache;
            if (s.Level != i || s.SampleStride != p.SampleStride || s.SamplesPerSide != p.SamplesPerSide
                || !Mathf.Approximately(s.SampleSpacing, p.SampleSpacing)
                || c.WorldSizeXZ != TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings)
                || !new TerrainHeightCacheWindow(c.CacheOriginTile, c.CacheSize).Contains(p.RequiredWindow)) return false;
        }
        return true;
    }

    internal static bool IsDisplayLayoutPublished(TerrainClipmapLayout layout)
    {
        return activeDisplayIntent != null && activeDisplayIntent.Root == boundClipmapRoot
            && activeDisplayIntent.OwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && TerrainAuthoringPreviewDisplayIntent.PlacementMatches(activeDisplayIntent.Layout, layout) && HasDrawableHeightPreview;
    }

    private static bool HasActiveBaseCoverage(TerrainAuthoringPreviewResidencyPlan plan, string committed)
    {
        if (activeHeightStates == null || activeHeightStates.Length != plan.LevelCount) return false;
        for (int i = 0; i < plan.LevelCount; i++)
        {
            var s = activeHeightStates[i]; var c = s.ActiveCache; var p = plan.Levels[i];
            if (c == null || s.WriteFailed || !c.IsCompleteForActivation || c.SourceCommittedHeightfieldSignature != committed
                || s.SampleStride != p.SampleStride || s.SamplesPerSide != p.SamplesPerSide || !Mathf.Approximately(s.SampleSpacing, p.SampleSpacing)
                || !new TerrainHeightCacheWindow(c.CacheOriginTile, c.CacheSize).Contains(p.RequiredWindow)) return false;
        }
        return true;
    }

    internal static bool RequestPreparedHeightCacheSet(WorldSettings settings, TerrainAuthoringData data,
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
        bool covered = !rebuildCommitted && HasActiveBaseCoverage(snapshot, committed);
        // A retained valid base with known dirty obligations is updated in place.
        if (covered && HasPendingActiveDirtyWork) return true;
        bool current = covered && CanActiveHeightCacheSetCover(snapshot);
        if (current && (!IsDisplayLayoutPublished(intent.Layout) || clipmapRebindRequested
            || activeDisplayIntent.PlacementGeneration != intent.PlacementGeneration))
            if (!TryCommitDisplayHeight(null, intent, activeHeightStates, out error)) return false;
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
        if (!needsWork) return true;
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
        ReleaseBinding(); ReleaseActiveDirtySource();
        var states = activeHeightStates; activeHeightStates = null; activeHeightView = null; activeDisplayIntent = null;
        if (states != null) foreach (var s in states) s.Dispose();
        if (retiringHeightStates != null) foreach (var s in retiringHeightStates) s.Dispose();
        retiringHeightStates = null; aggregateMinimumHeight = aggregateMaximumHeight = 0;
        pendingCompositePublication.Clear(); pendingNativePublication.Clear(); diagnosticPendingGeographicDirty.Clear();
    }
}
