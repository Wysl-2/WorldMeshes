/*
 * Transient multiresolution planning state for edit-mode Height preview.
 *
 * Ordinary planning does not allocate caches or start streaming. It records the
 * latest exact per-LOD residency intent so later streaming stages can consume
 * a stable, generation-aware plan.
 */
public static partial class TerrainAuthoringPreviewService
{
    private static TerrainAuthoringPreviewResidencyPlan
        latestMultiresolutionResidencyPlan;

    private static int
        nextMultiresolutionResidencyGeneration =
            1;

    private static string
        lastMultiresolutionPlanningError =
            "";

    internal static int
        LatestMultiresolutionResidencyGeneration =>
            latestMultiresolutionResidencyPlan != null
                ? latestMultiresolutionResidencyPlan.Generation
                : 0;

    internal static string
        LastMultiresolutionPlanningError =>
            lastMultiresolutionPlanningError;

    internal static bool TryGetLatestMultiresolutionResidencyPlan(
        out TerrainAuthoringPreviewResidencyPlan plan
    )
    {
        plan =
            latestMultiresolutionResidencyPlan;

        return
            plan != null
            &&
            plan.IsStructurallyValid;
    }

    internal static bool RecordMultiresolutionResidencyIntent(
        WorldSettings worldSettings,
        TerrainClipmapLayout layout,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int candidateGeneration =
            nextMultiresolutionResidencyGeneration;

        if (
            !TerrainAuthoringPreviewLodResidencyUtility
                .TryBuildPlan(
                    worldSettings,
                    layout,
                    candidateGeneration,
                    out TerrainAuthoringPreviewResidencyPlan candidate,
                    out errorMessage
                )
        )
        {
            lastMultiresolutionPlanningError =
                errorMessage;

            return false;
        }

        bool equivalent =
            TerrainAuthoringPreviewStreamingPolicy
                .AreMultiresolutionResidencyPlansEquivalent(
                    latestMultiresolutionResidencyPlan,
                    candidate
                );

        if (
            equivalent
            &&
            latestMultiresolutionResidencyPlan != null
        )
        {
            candidate.Generation =
                latestMultiresolutionResidencyPlan.Generation;
        }
        else
        {
            if (
                nextMultiresolutionResidencyGeneration ==
                    int.MaxValue
            )
            {
                nextMultiresolutionResidencyGeneration =
                    1;
            }
            else
            {
                nextMultiresolutionResidencyGeneration++;
            }
        }

        latestMultiresolutionResidencyPlan =
            candidate;

        lastMultiresolutionPlanningError =
            "";

        return true;
    }

    internal static void ClearMultiresolutionResidencyIntent()
    {
        latestMultiresolutionResidencyPlan =
            null;

        lastMultiresolutionPlanningError =
            "";
    }
    private static TerrainAuthoringPreviewLodState[] preparedHeightStates;
    private static System.Collections.Generic.IReadOnlyList<TerrainAuthoringPreviewPreparedHeightCache>
        preparedHeightView;
    private static TerrainAuthoringPreviewResidencyPlan acceptedPreparedHeightIntent;
    private static TerrainAuthoringPreviewResidencyPlan publishedPreparedHeightIntent;
    private static long acceptedPreparedHeightRequestGeneration;
    private static long preparedHeightAuthoringGeneration;
    private static long preparedHeightOwnershipGeneration;
    private static string preparedHeightCommittedSignature = "";
    private static string preparedHeightOverallSignature = "";

    internal static bool RequestPreparedHeightCacheSet(WorldSettings settings,
        TerrainAuthoringData data, TerrainAuthoringPreviewResidencyPlan plan,
        bool rebuildCommitted, out string error)
    {
        error = "";
        if (!Enabled || !CanRunEditorPreviewWork || settings == null || data == null
            || plan == null || !plan.IsStructurallyValid
            || plan.LevelCount > TerrainClipmapTopologyUtility.MaximumLevelCount)
        {
            error = "A valid complete plan and stable enabled editor preview are required.";
            return false;
        }
        var snapshot = plan.CreateSnapshot();
        var grid = new UnityEngine.Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        foreach (var p in snapshot.Levels)
        {
            if (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, p.SampleStride)
                || p.SamplesPerSide != TerrainHeightResolutionUtility.GetSamplesPerSide(settings, p.SampleStride)
                || !UnityEngine.Mathf.Approximately(p.SampleSpacing,
                    TerrainHeightResolutionUtility.GetSampleSpacing(settings, p.SampleStride))
                || !TerrainAuthoringPreviewResidencyPolicy.IsWindowInsideWorldGrid(p.RequiredWindow, grid)
                || !TerrainAuthoringPreviewResidencyPolicy.IsWindowInsideWorldGrid(p.DesiredWindow, grid))
            {
                error = $"The Height residency representation or window at LOD {p.Level} is invalid.";
                return false;
            }
        }
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            error = "Current authoring signatures are unavailable.";
            return false;
        }
        bool requiredSetSafe = !rebuildCommitted && PreparedHeightContentIsCurrent(settings, data)
            && preparedHeightStates.Length == snapshot.LevelCount;
        if (requiredSetSafe)
            for (int i = 0; i < snapshot.LevelCount; i++)
            {
                var state = preparedHeightStates[i];
                var p = snapshot.Levels[i];
                var cache = state.ActiveCache;
                if (state.SampleStride != p.SampleStride || state.SamplesPerSide != p.SamplesPerSide
                    || !UnityEngine.Mathf.Approximately(state.SampleSpacing, p.SampleSpacing)
                    || !cache.IsCompleteForActivation
                    || !new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize).Contains(p.RequiredWindow))
                { requiredSetSafe = false; break; }
            }
        var targets = new TerrainHeightCacheWindow[snapshot.LevelCount];
        var expansions = new bool[snapshot.LevelCount];
        bool needsWork = !requiredSetSafe;
        for (int i = 0; i < snapshot.LevelCount; i++)
        {
            var p = snapshot.Levels[i];
            targets[i] = p.RequiredWindow;
            if (requiredSetSafe)
            {
                var cache = preparedHeightStates[i].ActiveCache;
                var active = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
                targets[i] = active;
                bool prefetch = TerrainAuthoringPreviewStreamingPolicy.TryCalculateHeightSetPrefetchTarget(
                    active, p, grid, out var expanded);
                if (prefetch) { targets[i] = expanded; needsWork = true; }
                // Kept guard-sized entries use desired-size health even when another LOD grows.
                expansions[i] = targets[i] != p.RequiredWindow;
            }
        }
        if (!needsWork && !TransitionInProgress && !hasPendingStreamingStart)
        {
            acceptedPreparedHeightIntent = snapshot;
            publishedPreparedHeightIntent = snapshot.CreateSnapshot();
            return true;
        }
        var request = CreateCacheSetRequest(settings, snapshot, targets, expansions,
            TerrainAuthoringPreviewCachePublication.PreparedHeightSet, committed, overall, rebuildCommitted);
        if (!QueueCacheSetRequest(request, out error)) return false;
        acceptedPreparedHeightRequestGeneration = request.RequestGeneration;
        acceptedPreparedHeightIntent = snapshot;
        return true;
    }

    private static bool PreparedHeightContentIsCurrent(WorldSettings settings, TerrainAuthoringData data)
    {
        return preparedHeightStates != null && settings != null && data != null
            && preparedHeightAuthoringGeneration == authoringGeneration
            && preparedHeightOwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && preparedHeightCommittedSignature == TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings)
            && preparedHeightOverallSignature == TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
    }

    internal static bool TryGetPreparedHeightCacheSet(
        out System.Collections.Generic.IReadOnlyList<TerrainAuthoringPreviewPreparedHeightCache> caches)
    {
        caches = null;
        if (!PreparedHeightContentIsCurrent(LoadWorldSettings(), LoadAuthoringData())
            || acceptedPreparedHeightIntent == null
            || preparedHeightStates.Length != acceptedPreparedHeightIntent.LevelCount) return false;
        for (int i = 0; i < preparedHeightStates.Length; i++)
        {
            var state = preparedHeightStates[i];
            var p = acceptedPreparedHeightIntent.Levels[i];
            var cache = state.ActiveCache;
            if (!state.CacheReady || cache == null || !cache.IsCompleteForActivation
                || state.SampleStride != p.SampleStride || state.SamplesPerSide != p.SamplesPerSide
                || !UnityEngine.Mathf.Approximately(state.SampleSpacing, p.SampleSpacing)
                || !new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize).Contains(p.RequiredWindow))
                return false;
        }
        caches = preparedHeightView;
        return caches != null;
    }

    private static void PublishPreparedHeightCacheSet(TerrainAuthoringPreviewCacheSetTransition transaction)
    {
        // Build the read-only view before publishing; no callbacks can observe a partial array.
        var states = transaction.TransferPreparedStates();
        var view = new TerrainAuthoringPreviewPreparedHeightCache[states.Length];
        for (int i = 0; i < states.Length; i++) view[i] = new TerrainAuthoringPreviewPreparedHeightCache(states[i]);
        var previous = preparedHeightStates;
        preparedHeightStates = states;
        preparedHeightView = System.Array.AsReadOnly(view);
        preparedHeightAuthoringGeneration = transaction.AuthoringGeneration;
        preparedHeightOwnershipGeneration = transaction.OwnershipGeneration;
        preparedHeightCommittedSignature = transaction.CommittedSignature;
        preparedHeightOverallSignature = transaction.OverallSignature;
        publishedPreparedHeightIntent = transaction.AcceptedPlan.CreateSnapshot();
        CompleteTransitionMemoryTracking();
        ClearTransitionFailureSuppression();
        if (previous != null) foreach (var state in previous) state.Dispose();
    }

    private static void InvalidatePreparedHeightCacheSet()
    {
        if (preparedHeightStates != null)
            foreach (var state in preparedHeightStates) state.CacheReady = false;
        publishedPreparedHeightIntent = null;
    }

    private static void ReleasePreparedHeightCacheSet()
    {
        var states = preparedHeightStates;
        preparedHeightStates = null;
        preparedHeightView = null;
        acceptedPreparedHeightIntent = publishedPreparedHeightIntent = null;
        preparedHeightCommittedSignature = preparedHeightOverallSignature = "";
        if (states != null) foreach (var state in states) state.Dispose();
    }


}

