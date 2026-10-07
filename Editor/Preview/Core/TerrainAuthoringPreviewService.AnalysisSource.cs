using System;
using System.Collections.Generic;
using UnityEngine;

internal enum TerrainAuthoringAnalysisSourceKind
{
    Unavailable,
    BorrowedNative,
    OwnedNative
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

    internal TerrainAuthoringAnalysisSourceSnapshot(TerrainAuthoringAnalysisSourceKind kind,
        TerrainHeightCacheWindow output, TerrainHeightCacheWindow required, TerrainHeightCacheWindow physical,
        int guard, int samples, float spacing, bool ready, bool failed, long authoring, long owner,
        long residency, long composite, string message)
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
    {
        source = default;
        outputWindow = default;
        var settings = analysisSettings;
        var data = LoadAuthoringData();
        if (!Enabled || !hasAnalysisSourceIntent || settings == null || data == null
            || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            || analysisOwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
            return false;
        long generation = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
            && ReferenceEquals(analysisSelectedCache, Lod0DisplayCache) ? CurrentNativeDisplayAuthoringGeneration
            : analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.OwnedNative
                && ReferenceEquals(analysisSelectedCache, analysisOwnedState?.ActiveCache)
                && analysisOwnedOwnershipGeneration == analysisOwnershipGeneration
                    ? analysisOwnedState.ActiveAuthoringGeneration : -1L;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
            && (!TryGetCurrentNativeDisplayHeight(settings, data, out var native)
                || !ReferenceEquals(native, analysisSelectedCache))) return false;
        if (!IsNativeAnalysisCacheEligible(analysisSelectedCache, settings, generation, authoringGeneration,
            committed, overall, analysisRequiredSourceWindow, analysisOutputWindow)) return false;
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

    internal static bool IsNativeAnalysisCacheEligible(TerrainAuthoringPreviewCache cache,
        WorldSettings settings, long cacheGeneration, long currentGeneration,
        string committed, string overall, TerrainHeightCacheWindow required,
        TerrainHeightCacheWindow output)
    {
        if (cache == null || settings == null || !cache.IsCompleteForActivation
            || cache.HeightCache == null || !cache.HeightCache.IsCreated() || cache.SampleStride != 1
            || cache.SamplesPerSide != TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1)
            || !Mathf.Approximately(cache.SampleSpacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1))
            || cache.WorldSizeXZ != TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings)
            || cacheGeneration != currentGeneration || string.IsNullOrEmpty(committed)
            || string.IsNullOrEmpty(overall) || cache.SourceCommittedHeightfieldSignature != committed
            || cache.SourceOverallAuthoringSignature != overall) return false;
        var physical = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
        var grid = new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        return physical.Contains(required) && required.Contains(output)
            && cache.HeightCache.width == cache.SamplesPerSide
            && cache.HeightCache.height == cache.SamplesPerSide
            && cache.HeightCache.volumeDepth == physical.TileCount
            && TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(physical, grid,
                TerrainAnalysisWindowUtility.CalculateRequiredInteractiveGuardTileCount(settings),
                out var safe, out _) && safe.Contains(output);
    }

    private static void EvaluateTerrainAnalysisSource(WorldSettings settings, TerrainAuthoringData data,
        bool notifyState = true)
    {
        if (!hasAnalysisSourceIntent || settings == null || data == null) return;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (TryGetCurrentNativeDisplayHeight(settings, data, out var nativeDisplay)
            && IsNativeAnalysisCacheEligible(nativeDisplay, settings, CurrentNativeDisplayAuthoringGeneration,
                authoringGeneration, committed, overall, analysisRequiredSourceWindow, analysisOutputWindow))
        {
            SelectTerrainAnalysisCache(nativeDisplay, TerrainAuthoringAnalysisSourceKind.BorrowedNative);
        }
        else if (analysisOwnedOwnershipGeneration == analysisOwnershipGeneration
            && IsNativeAnalysisCacheEligible(analysisOwnedState?.ActiveCache, settings,
                analysisOwnedState?.ActiveAuthoringGeneration ?? -1L, authoringGeneration,
                committed, overall, analysisRequiredSourceWindow, analysisOutputWindow))
        {
            SelectTerrainAnalysisCache(analysisOwnedState.ActiveCache, TerrainAuthoringAnalysisSourceKind.OwnedNative);
        }
        else if (analysisSelectedCache == null || analysisSelectedCache.HeightCache == null
            || !analysisSelectedCache.HeightCache.IsCreated()
            || analysisSelectedCommittedSignature != committed
            || analysisSelectedOwnershipGeneration != analysisOwnershipGeneration
            || !analysisSelectedPhysicalWindow.Contains(analysisRequiredSourceWindow)
            || analysisSelectedOutputWindow != analysisOutputWindow)
        {
            SelectTerrainAnalysisCache(null, TerrainAuthoringAnalysisSourceKind.Unavailable);
        }
        // Otherwise keep the same structural identity while content is pending.
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

    private static void InvalidateTerrainAnalysisAuthoring()
    {
        // This runs before display dirty notifications can be consumed. Owned
        // Height is rebuilt conservatively even for edits outside display coverage.
        analysisSourceError = "";
        lastFailedAnalysisCacheSetRequest = null;
        TryRunAnalysisFollowUp(() =>
        {
            if (hasAnalysisSourceIntent) EvaluateTerrainAnalysisSource(analysisSettings, LoadAuthoringData());
            PublishTerrainAnalysisSourceState();
        });
    }

    private static void PublishNativeTerrainAnalysisCompositeUpdate(IReadOnlyList<Vector2Int> tiles)
    {
        List<Vector2Int> changed = null;
        long target = authoringGeneration;
        if (hasAnalysisSourceIntent)
        {
            EvaluateTerrainAnalysisSource(analysisSettings, LoadAuthoringData(), false);
            if (target != authoringGeneration) return;
            if (analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
                && TryGetTerrainAnalysisGpuSource(out _, out _) && tiles != null)
            {
                var unique = new HashSet<Vector2Int>();
                foreach (var tile in tiles) if (analysisRequiredSourceWindow.Contains(tile)) unique.Add(tile);
                if (unique.Count > 0)
                {
                    changed = new List<Vector2Int>(unique); SortWorldTilesRowMajor(changed);
                }
            }
        }
        if (changed != null) analysisCompositeGeneration = NextAnalysisGeneration(analysisCompositeGeneration);
        // Consume internal intent before external callbacks; never clear their newly queued work.
        foreach (var tile in tiles) pendingNativePublication.Remove(tile);
        if (changed != null) DispatchPreviewObservers(TerrainAnalysisSourceTilesUpdated,
            (IReadOnlyList<Vector2Int>)changed, "Analysis tiles");
        PublishTerrainAnalysisSourceState(true);
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
        if (TransitionInProgress || hasPendingStreamingStart) return;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            analysisSourceError = "Native terrain analysis is waiting for valid committed Height and current authoring identity.";
            PublishTerrainAnalysisSourceState();
            return;
        }
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
        if ((TransitionInProgress && currentCacheSetTransition.Publication == TerrainAuthoringPreviewCachePublication.NativeAnalysis)
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
        var owned = analysisOwnedState;
        bool hadSource = analysisSelectedResourceIdentity != 0 || hasAnalysisSourceIntent || hasAnalysisSourceDemand;
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
        bool displayConfigurationCurrent = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative
            && activeDisplayIntent != null && activeDisplayIntent.OwnershipGeneration == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && activeDisplayIntent.Root == boundClipmapRoot && activeDisplayIntent.ConfigurationMatches(activeDisplayIntent.Settings);
        return CaptureAnalysisSourceMetadata(displayConfigurationCurrent);
    }

    internal static TerrainAuthoringAnalysisSourceSnapshot CaptureAnalysisSourceMetadata(bool displayConfigurationCurrent)
    {
        // Observational state only. The GPU source query retains full eligibility
        // checks; this projection does not load assets or rebuild signatures.
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        bool borrowed = analysisSelectedKind == TerrainAuthoringAnalysisSourceKind.BorrowedNative;
        var state = borrowed ? activeHeightStates != null && activeHeightStates.Length > 0
            ? activeHeightStates[0] : null : analysisOwnedState;
        var cache = state?.ActiveCache;
        bool nativeSettingsValid = TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(analysisSettings, 1);
        int nativeSamples = nativeSettingsValid ? TerrainHeightResolutionUtility.GetSamplesPerSide(analysisSettings, 1) : 0;
        float nativeSpacing = nativeSettingsValid ? TerrainHeightResolutionUtility.GetSampleSpacing(analysisSettings, 1) : 0f;
        bool nativeGeometryCurrent = NativeCacheGeometryCurrentForDiagnostics(cache, nativeSamples, nativeSpacing);
        bool configurationCurrent = nativeGeometryCurrent && (borrowed ? displayConfigurationCurrent
            : analysisOwnedOwnershipGeneration == owner);
        bool ready = Enabled && hasAnalysisSourceIntent && analysisOwnershipGeneration == owner
            && !Application.isPlaying && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            && StateCurrentForDiagnostics(state, configurationCurrent) && cache.SampleStride == 1
            && ReferenceEquals(cache, analysisSelectedCache) && analysisSelectedOwnershipGeneration == owner
            && cache.HeightCache.GetInstanceID() == analysisSelectedResourceIdentity
            && analysisSelectedOutputWindow == analysisOutputWindow
            && analysisSelectedPhysicalWindow == new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize)
            && analysisSelectedPhysicalWindow.Contains(analysisRequiredSourceWindow)
            && analysisRequiredSourceWindow.Contains(analysisOutputWindow);
        string message = ready ? "Current native Height is available for bounded terrain analysis."
            : !string.IsNullOrEmpty(analysisSourceError) ? analysisSourceError
            : !hasAnalysisSourceIntent ? "No native terrain analysis focus is available."
            : !hasAnalysisSourceDemand ? "Native terrain analysis has not requested Height."
            : !CanRunEditorPreviewWork ? "Native terrain analysis is suspended with the editor preview."
            : "Waiting for current native Height for analysis output " + analysisOutputWindow
                + "; dependency source " + analysisRequiredSourceWindow + ".";
        return new TerrainAuthoringAnalysisSourceSnapshot(analysisSelectedKind, analysisOutputWindow,
            analysisRequiredSourceWindow, analysisSelectedPhysicalWindow, analysisGuardTileCount,
            nativeSamples, nativeSpacing,
            ready, lastFailedAnalysisCacheSetRequest != null
                || (!hasAnalysisSourceIntent && !string.IsNullOrEmpty(analysisSourceError)),
            state?.ActiveAuthoringGeneration ?? 0L, analysisOwnershipGeneration,
            analysisResidencyGeneration, analysisCompositeGeneration, message);
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
    private static long CurrentNativeDisplayAuthoringGeneration => activeHeightStates != null
        && activeHeightStates.Length > 0 && activeHeightStates[0].SampleStride == 1
            ? activeHeightStates[0].ActiveAuthoringGeneration : -1L;

    private static bool TryGetCurrentNativeDisplayHeight(WorldSettings settings, TerrainAuthoringData data,
        out TerrainAuthoringPreviewCache cache)
    {
        cache = null;
        if (activeHeightStates == null || activeHeightStates.Length == 0 || activeDisplayIntent == null
            || activeDisplayIntent.Root == null || activeDisplayIntent.Root != boundClipmapRoot
            || !activeDisplayIntent.ConfigurationMatches(settings) || data == null
            || activeDisplayIntent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration) return false;
        var state = activeHeightStates[0];
        if (state.SampleStride != 1 || !StateContentIsCurrent(state,
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings),
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data))) return false;
        cache = state.ActiveCache; return true;
    }

    private static bool NativeCacheGeometryCurrentForDiagnostics(TerrainAuthoringPreviewCache cache, int samples, float spacing) =>
        samples > 1 && analysisSettings != null && cache != null && cache.SampleStride == 1
        && cache.SamplesPerSide == samples && Mathf.Approximately(cache.SampleSpacing, spacing)
        && cache.WorldSizeXZ == TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(analysisSettings);

}

