using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    private static long authoringGeneration;



    private static string lastAuthoringGenerationReason =
        "";

    private static int lastGlobalDirtyTileCount;

    private static int lastResidentDirtyTileCount;

    private static int lastNonresidentDirtyTileCount;

    private static int lastPublishedCompositeTileCount;

    public static long AuthoringGeneration =>
        authoringGeneration;



    public static string LastAuthoringGenerationReason =>
        lastAuthoringGenerationReason;

    public static int PendingGlobalDirtyTileCount =>
        dirtyCompositeTiles.Count;

    public static int LastGlobalDirtyTileCount =>
        lastGlobalDirtyTileCount;

    public static int LastResidentDirtyTileCount =>
        lastResidentDirtyTileCount;

    public static int LastNonresidentDirtyTileCount =>
        lastNonresidentDirtyTileCount;

    public static int LastPublishedCompositeTileCount =>
        lastPublishedCompositeTileCount;

    public static bool InteractiveModifierEditActive =>
        TerrainAuthoringModifierService
            .HasActiveInteractiveEdit;

    public static bool StreamingRestartDeferredForInteractiveEdit
    {
        get
        {
            if (!HasActiveInteractiveTerrainAuthoringEdit) return false;
            if (displayPreparationDeferredForInteractiveEdit) return true;
            var work = TransitionInProgress ? currentCacheSetTransition : pendingCacheSetTransition;
            var settings = LoadWorldSettings();
            return work != null && TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(work.Publication,
                true, HasLiveTerrainAnalysisDemand, IsMandatoryDisplayWork(work, settings,
                    TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings)));
        }
    }

    private static void RegisterPreviewAuthoringInvalidation(
        string reason,
        bool contentOnly = true
    )
    {
        long previousGeneration = authoringGeneration;
        authoringGeneration =
            CalculateNextAuthoringGeneration(
                authoringGeneration
            );

        lastAuthoringGenerationReason =
            string.IsNullOrEmpty(
                reason
            )
                ? "Preview-affecting authoring state changed."
                : reason;

        ClearTransitionFailureSuppression();
        InvalidateActiveHeightContent(contentOnly);
        if (contentOnly && HasActiveInteractiveTerrainAuthoringEdit) interactiveDirtyHintGeneration = authoringGeneration;
        InvalidateTerrainAnalysisAuthoring(contentOnly, previousGeneration);

        bool hasStreamingWork =
            hasPendingStreamingStart
            ||
            (
                currentCacheSetTransition != null
                &&
                TransitionInProgress
            );

        if (hasStreamingWork)
        {
            CancelCurrentStreamingTransition(
                "Preview authoring changed while staging was in progress. " +
                "The stale destination was cancelled and the latest " +
                "residency intent was preserved.",
                false
            );
        }

        if (
            streamingState ==
                TerrainAuthoringPreviewStreamingState.Failed
        )
        {
            lastStreamingFailureMessage =
                "";

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Idle,
                "Authoring changed; the previous streaming failure is no longer current."
            );
        }
        else
        {
            /*
             * Generation is part of authoring-content streaming diagnostics. Force a
             * publication even when the streaming phase/counters are otherwise
             * unchanged.
             */
            lastPublishedStreamingSnapshot =
                "";

            PublishStreamingStateIfChanged();
        }
    }

    internal static long CalculateNextAuthoringGeneration(
        long currentGeneration
    )
    {
        return
            currentGeneration < long.MaxValue
                ? currentGeneration + 1L
                : long.MaxValue;
    }



    internal static bool IsTransitionAuthoringStateCurrent(
        TerrainAuthoringPreviewCacheTransition transition,
        long currentGeneration,
        string committedSignature,
        string overallSignature
    )
    {
        return
            transition != null
            &&
            transition.TargetAuthoringGeneration ==
                currentGeneration
            &&
            transition.TargetCommittedHeightfieldSignature ==
                (committedSignature ?? "")
            &&
            transition.TargetOverallAuthoringSignature ==
                (overallSignature ?? "");
    }

    internal static bool ShouldDeferStreamingRestart(
        bool hasActiveInteractiveEdit
    )
    {
        return
            hasActiveInteractiveEdit;
    }

    internal static bool CanAcknowledgeActiveAuthoringState(
        bool committedSourceCurrent,
        int residentDirtyTileCount
    )
    {
        return
            committedSourceCurrent
            &&
            residentDirtyTileCount >= 0;
    }

    internal static void PartitionDirtyTilesForActiveWindow(
        IEnumerable<Vector2Int> globalDirtyTiles,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        List<Vector2Int> residentTiles,
        List<Vector2Int> nonresidentTiles
    )
    {
        if (residentTiles == null || nonresidentTiles == null)
        {
            return;
        }

        residentTiles.Clear();
        nonresidentTiles.Clear();

        if (globalDirtyTiles == null)
        {
            return;
        }

        HashSet<Vector2Int> unique =
            new HashSet<Vector2Int>();

        foreach (
            Vector2Int tile
            in globalDirtyTiles
        )
        {
            unique.Add(
                tile
            );
        }

        List<Vector2Int> ordered =
            new List<Vector2Int>(
                unique
            );

        SortWorldTilesRowMajor(
            ordered
        );

        for (
            int index = 0;
            index < ordered.Count;
            index++
        )
        {
            Vector2Int tile =
                ordered[index];

            if (
                hasActiveWindow
                &&
                activeWindow.Contains(
                    tile
                )
            )
            {
                residentTiles.Add(
                    tile
                );
            }
            else
            {
                nonresidentTiles.Add(
                    tile
                );
            }
        }
    }

    private static void SortWorldTilesRowMajor(
        List<Vector2Int> tiles
    )
    {
        if (tiles == null)
        {
            return;
        }

        tiles.Sort(
            (a, b) =>
            {
                int zCompare =
                    a.y.CompareTo(
                        b.y
                    );

                if (zCompare != 0)
                {
                    return zCompare;
                }

                return
                    a.x.CompareTo(
                        b.x
                    );
            }
        );
    }



    public static TerrainAuthoringPreviewReadiness GetWorldTileReadiness(
        Vector2Int worldTile
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        bool previewAvailable =
            TryResolveReadinessContext(
                worldSettings,
                out string currentCommittedSignature
            );

        return
            EvaluateWorldTileReadiness(
                worldSettings,
                worldTile,
                previewAvailable,
                currentCommittedSignature
            );
    }

    private static bool TryResolveReadinessContext(
        WorldSettings worldSettings,
        out string currentCommittedSignature
    )
    {
        currentCommittedSignature =
            "";

        if (
            worldSettings == null
            ||
            !CanRunEditorPreviewWork
            ||
            Application.isPlaying
            ||
            UnityEditor.EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            LoadAuthoringData() == null
            ||
            Status ==
                TerrainAuthoringPreviewStatus.Disabled
            ||
            Status ==
                TerrainAuthoringPreviewStatus.PlayMode
            ||
            Status ==
                TerrainAuthoringPreviewStatus.AuthoringUnavailable
            ||
            Status ==
                TerrainAuthoringPreviewStatus.ClipmapUnavailable

        )
        {
            return false;
        }

        if (activeDisplayIntent != null)
        {
            if (!activeDisplayIntent.ConfigurationMatches(worldSettings)
                || activeDisplayIntent.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
                || activeDisplayIntent.Root == null || activeDisplayIntent.Root != boundClipmapRoot
                || !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(out Transform root, out _) || root != boundClipmapRoot
                || boundHeightRenderers.Count != activeDisplayIntent.Plan.LevelCount * 2 - 1) return false;
            foreach (var binding in boundHeightRenderers) if (!binding.IsValid) return false;
        }

        currentCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        return
            !string.IsNullOrEmpty(
                currentCommittedSignature
            );
    }

    private static TerrainAuthoringPreviewReadiness EvaluateWorldTileReadiness(WorldSettings settings,
        Vector2Int tile, bool previewAvailable, string committed)
    {
        if (settings == null || tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth || tile.y >= settings.HeightTileGridHeight)
            return TerrainAuthoringPreviewReadiness.OutsideWorld;
        var state = FindFinestResidentDisplayState(tile);
        var cache = state?.ActiveCache;
        bool usable = IsStateTileDrawable(state, settings, committed, tile);
        bool pending = !IsStateTileContentCurrent(state, settings, committed, tile);
        return TerrainAuthoringPreviewReadinessPolicy.EvaluateTile(previewAvailable, true, cache != null,
            cache != null && cache.SourceCommittedHeightfieldSignature == committed, cache != null,
            usable, pending);
    }

    private static bool IsStateTileDrawable(TerrainAuthoringPreviewLodState state,
        WorldSettings settings, string committed, Vector2Int tile)
    {
        if (committedRebuildRequested || activeDisplayIntent == null || state == null
            || state.Level < 0 || state.Level >= activeDisplayIntent.Plan.LevelCount
            || !PublishedDisplayIsDrawable(settings, committed)
            || !StateHasResidentCoverage(state, activeDisplayIntent.Plan.Levels[state.Level], settings, committed)) return false;
        var cache = state.ActiveCache;
        return cache.IsSliceFinalCompositeReady(tile)
            && cache.TryGetCompositeSliceRange(tile.x, tile.y, out float low, out float high)
            && !float.IsNaN(low) && !float.IsNaN(high) && !float.IsInfinity(low) && !float.IsInfinity(high) && low <= high;
    }

    private static bool IsStateTileContentCurrent(TerrainAuthoringPreviewLodState state,
        WorldSettings settings, string committed, Vector2Int tile)
    {
        return IsStateTileDrawable(state, settings, committed, tile)
            && IsResidentTileContentCurrent(state, committed, authoringGeneration, tile, dirtyCompositeTiles,
                IsWorldTilePendingRegionalElevationRecomposition(settings, tile));
    }

    internal static bool IsResidentTileContentCurrent(TerrainAuthoringPreviewLodState state, string committed,
        long generation, Vector2Int tile, ISet<Vector2Int> unprojectedTiles, bool pendingRegionalScope)
    {
        return IsDirtyLiveSliceUsable(state, tile) && state.ActiveCache.SourceCommittedHeightfieldSignature == committed
            && (state.CacheReady && state.ActiveAuthoringGeneration == generation
                || state.DirtyTargetGeneration > 0 && state.DirtyTargetGeneration == generation)
            && !state.DirtyFailures.ContainsKey(tile)
            && !HasTileContentObligation(state, tile, unprojectedTiles, pendingRegionalScope);
    }

    internal static bool IsGeographicDirtyTileCurrent(TerrainAuthoringPreviewLodState[] states, string committed,
        long generation, Vector2Int tile, ISet<Vector2Int> unprojectedTiles, bool pendingRegionalScope, out bool hasOwner)
    {
        hasOwner = false;
        if (states == null) return false;
        bool current = true;
        foreach (var state in states)
        {
            if (state?.ActiveCache == null || state.ActiveCache.GetSliceIndex(tile.x, tile.y) < 0) continue;
            hasOwner = true;
            current &= IsResidentTileContentCurrent(state, committed, generation, tile, unprojectedTiles, pendingRegionalScope);
        }
        return hasOwner && current;
    }

    internal static bool HasTileContentObligation(TerrainAuthoringPreviewLodState state,
        Vector2Int tile, ISet<Vector2Int> unprojectedTiles, bool pendingRegionalScope)
    {
        return state != null && state.HasPendingContent(tile)
            || unprojectedTiles != null && unprojectedTiles.Contains(tile) || pendingRegionalScope;
    }

    internal static bool IsDisplayLodWindowContentCurrent(int level, TerrainHeightCacheWindow window)
    {
        var settings = LoadWorldSettings();
        if (!window.IsValid || !TryResolveReadinessContext(settings, out string committed)
            || activeHeightStates == null || level < 0 || level >= activeHeightStates.Length) return false;
        var state = activeHeightStates[level];
        var cache = state.ActiveCache;
        if (cache == null || !new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize).Contains(window)) return false;
        var maximum = window.MaximumExclusive;
        for (int z = window.OriginTile.y; z < maximum.y; z++)
            for (int x = window.OriginTile.x; x < maximum.x; x++)
                if (!IsStateTileContentCurrent(state, settings, committed, new Vector2Int(x, z))) return false;
        return true;
    }

    public static TerrainAuthoringPreviewInteractionReadiness GetWorldTileInteractionReadiness(Vector2Int tile)
    {
        var settings = LoadWorldSettings();
        if (settings == null) return TerrainAuthoringPreviewInteractionReadiness.PreviewUnavailable;
        bool available = TryResolveReadinessContext(settings, out string committed);
        return EvaluateWorldTileInteractionReadiness(settings, tile, available, committed);
    }

    private static TerrainAuthoringPreviewInteractionReadiness EvaluateWorldTileInteractionReadiness(
        WorldSettings settings, Vector2Int tile, bool available, string committed)
    {
        bool inside = tile.x >= 0 && tile.y >= 0 && tile.x < settings.HeightTileGridWidth && tile.y < settings.HeightTileGridHeight;
        var state = inside ? FindFinestResidentDisplayState(tile) : null;
        var cache = state?.ActiveCache;
        return TerrainAuthoringPreviewReadinessPolicy.EvaluateInteractionTile(available, inside,
            cache != null, cache != null && cache.SourceCommittedHeightfieldSignature == committed, cache != null,
            IsStateTileDrawable(state, settings, committed, tile), !IsStateTileContentCurrent(state, settings, committed, tile));
    }

    public static TerrainAuthoringPreviewInteractionReadiness GetWorldBoundsInteractionReadiness(
        Bounds bounds, int samplePadding = 1)
    {
        var settings = LoadWorldSettings();
        if (settings == null) return TerrainAuthoringPreviewInteractionReadiness.PreviewUnavailable;
        var world = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        if (bounds.max.x < 0 || bounds.max.z < 0 || bounds.min.x > world.x || bounds.min.z > world.y)
            return TerrainAuthoringPreviewInteractionReadiness.OutsideWorld;
        var tiles = new List<Vector2Int>();
        TerrainAuthoringPreviewDirtyRegionUtility.CollectTilesOverlappingBounds(settings, bounds, tiles, samplePadding);
        if (tiles.Count == 0) return TerrainAuthoringPreviewInteractionReadiness.OutsideWorld;
        bool available = TryResolveReadinessContext(settings, out string committed);
        if (!available) return TerrainAuthoringPreviewInteractionReadiness.PreviewUnavailable;
        bool updating = false;
        foreach (var tile in tiles)
        {
            var readiness = EvaluateWorldTileInteractionReadiness(settings, tile, available, committed);
            if (!TerrainAuthoringPreviewReadinessPolicy.IsInteractionAllowed(readiness)) return readiness;
            updating |= readiness == TerrainAuthoringPreviewInteractionReadiness.Updating;
        }
        return updating ? TerrainAuthoringPreviewInteractionReadiness.Updating : TerrainAuthoringPreviewInteractionReadiness.Ready;
    }

    public static TerrainAuthoringPreviewReadiness GetWorldTileReadiness(
        int tileX,
        int tileZ
    )
    {
        return
            GetWorldTileReadiness(
                new Vector2Int(
                    tileX,
                    tileZ
                )
            );
    }

    public static TerrainAuthoringPreviewReadiness GetWorldPositionReadiness(
        Vector2 worldXZ
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            worldXZ.x < 0f
            ||
            worldXZ.y < 0f
            ||
            worldXZ.x > worldSize.x
            ||
            worldXZ.y > worldSize.y
        )
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        int tileX =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    worldXZ.x /
                    tileWorldSize
                ),
                0,
                Mathf.Max(
                    0,
                    worldSettings.HeightTileGridWidth - 1
                )
            );

        int tileZ =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    worldXZ.y /
                    tileWorldSize
                ),
                0,
                Mathf.Max(
                    0,
                    worldSettings.HeightTileGridHeight - 1
                )
            );

        return
            GetWorldTileReadiness(
                tileX,
                tileZ
            );
    }

    public static TerrainAuthoringPreviewReadiness GetWorldPositionReadiness(
        Vector3 worldPosition
    )
    {
        return
            GetWorldPositionReadiness(
                new Vector2(
                    worldPosition.x,
                    worldPosition.z
                )
            );
    }

    public static TerrainAuthoringPreviewReadiness GetWorldBoundsReadiness(
        Bounds bounds,
        int samplePadding = 1
    )
    {
        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            bounds.max.x < 0f
            ||
            bounds.max.z < 0f
            ||
            bounds.min.x > worldSize.x
            ||
            bounds.min.z > worldSize.y
        )
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        List<Vector2Int> tiles =
            new List<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                bounds,
                tiles,
                samplePadding
            );

        if (tiles.Count <= 0)
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        SortWorldTilesRowMajor(
            tiles
        );

        bool previewAvailable =
            TryResolveReadinessContext(
                worldSettings,
                out string currentCommittedSignature
            );

        for (
            int index = 0;
            index < tiles.Count;
            index++
        )
        {
            TerrainAuthoringPreviewReadiness readiness =
                EvaluateWorldTileReadiness(
                    worldSettings,
                    tiles[index],
                    previewAvailable,
                    currentCommittedSignature
                );

            if (
                readiness ==
                    TerrainAuthoringPreviewReadiness.PreviewUnavailable
            )
            {
                return readiness;
            }

            if (
                readiness !=
                    TerrainAuthoringPreviewReadiness.Ready
            )
            {
                return
                    TerrainAuthoringPreviewReadiness.Loading;
            }
        }

        return
            TerrainAuthoringPreviewReadiness.Ready;
    }
    private static readonly HashSet<Vector2Int> pendingCompositePublication = new HashSet<Vector2Int>();
    private static readonly HashSet<Vector2Int> pendingNativePublication = new HashSet<Vector2Int>();
    private static long dirtyFailureAttemptSequence;
    private static TerrainAuthoringPreviewHeightSourceLease activeDirtySourceLease;
    private static Texture2D activeDirtySource => activeDirtySourceLease?.Texture;
    private static Vector2Int activeDirtySourceTile;
    private static DirtySourceIdentity activeDirtySourceIdentity;
    private static readonly HashSet<Vector2Int> latestInteractiveDirtyTiles = new HashSet<Vector2Int>();
    private static TerrainRegionalElevationInvalidationScope latestInteractiveRegionalScope;
    private static long interactiveDirtyHintGeneration;
    private static long interactiveDirtyHintOwnership = -1;
    private static int interactiveDirtyHintSettingsId;
    private static WorldSettings interactiveDirtyHintSettings;
    private static Transform interactiveDirtyHintRoot;
    private static readonly Func<Vector2Int, bool> latestInteractiveScopeContains = LatestInteractionAffectsTile;

    internal readonly struct DirtySourceIdentity
    {
        internal readonly Vector2Int Tile;
        internal readonly string CommittedSignature;
        internal readonly int SettingsId;
        internal readonly int NativeSamples;

        internal DirtySourceIdentity(Vector2Int tile, string committed, int settingsId, int nativeSamples)
        {
            Tile = tile; CommittedSignature = committed;
            SettingsId = settingsId; NativeSamples = nativeSamples;
        }
    }

    internal static bool CanReuseDirtySource(Texture2D source, DirtySourceIdentity held, DirtySourceIdentity target)
    {
        return source != null && held.Tile == target.Tile && held.SettingsId == target.SettingsId
            && held.NativeSamples == target.NativeSamples && !string.IsNullOrEmpty(target.CommittedSignature)
            && string.Equals(held.CommittedSignature, target.CommittedSignature, StringComparison.Ordinal)
            && TerrainAuthoringPreviewHeightSourceUtility.TryValidateNativeSource(source, target.NativeSamples, out _);
    }
    private static readonly TerrainAuthoringPreviewHeightMaterializer activeDirtyMaterializer = new TerrainAuthoringPreviewHeightMaterializer();
    internal static int LastDirtyUpdateAllocations { get; private set; }
    internal static int LastDirtyUpdateCopies { get; private set; }
    internal static int LastDirtyUpdateLoads { get; private set; }
    internal static int LastDirtyUpdateMaterializations { get; private set; }
    internal static int LastDirtyUpdateCompositions { get; private set; }
    private static bool HasPendingActiveDirtyWork
    {
        get
        {
            if (activeHeightStates != null) foreach (var s in activeHeightStates)
                if (s.PendingDirtyTiles.Count > 0) return true;
            return false;
        }
    }

    internal static bool HasRunnableDirtyWork(TerrainAuthoringPreviewLodState[] states, bool requiredOnly)
    {
        if (states != null) foreach (var state in states)
            foreach (var tile in state.PendingDirtyTiles)
                if (state.IsDirtyTileRunnable(tile) && (!requiredOnly || state.ActiveRequiredWindow.Contains(tile)))
                    return true;
        return false;
    }

    internal static void RearmDirtyTile(TerrainAuthoringPreviewLodState[] states, Vector2Int tile)
    {
        if (states != null) foreach (var state in states) state.DirtyFailures.Remove(tile);
    }

    internal static void RearmRegionalDirtyFailures(TerrainAuthoringPreviewLodState[] states,
        WorldSettings settings, TerrainRegionalElevationInvalidationScope scope)
    {
        if (states == null || scope.Kind == TerrainRegionalElevationInvalidationKind.None) return;
        foreach (var state in states)
        {
            var relevant = new List<Vector2Int>();
            foreach (var pair in state.DirtyFailures)
                if (TerrainRegionalElevationResidencyPolicy.ScopeAffectsTile(settings, scope, pair.Key)) relevant.Add(pair.Key);
            foreach (var tile in relevant) state.DirtyFailures.Remove(tile);
        }
    }

    internal static void RearmAllDirtyFailures(TerrainAuthoringPreviewLodState[] states)
    {
        if (states != null) foreach (var state in states) state.DirtyFailures.Clear();
    }

    private static TerrainAuthoringPreviewLodState FindFinestResidentDisplayState(Vector2Int tile)
    {
        if (activeHeightStates != null) foreach (var s in activeHeightStates)
            if (s.ActiveCache != null && s.ActiveCache.IsReady && s.ActiveCache.GetSliceIndex(tile.x, tile.y) >= 0) return s;
        return null;
    }

    private static void ReleaseActiveDirtySource()
    {
        activeDirtyMaterializer.ReleaseTextureBindings();
        activeDirtySourceLease?.Dispose(); activeDirtySourceLease = null;
        activeDirtySourceIdentity = default;
    }

    private static void ClearInteractiveDirtyHint()
    {
        latestInteractiveDirtyTiles.Clear(); latestInteractiveRegionalScope = TerrainRegionalElevationInvalidationScope.None;
        interactiveDirtyHintGeneration = 0; interactiveDirtyHintOwnership = -1;
        interactiveDirtyHintSettingsId = 0; interactiveDirtyHintSettings = null; interactiveDirtyHintRoot = null;
    }

    private static void PrepareInteractiveDirtyHint(bool replaceScope)
    {
        if (!HasActiveInteractiveTerrainAuthoringEdit) { ClearInteractiveDirtyHint(); return; }
        var settings = LoadWorldSettings();
        if (settings == null) { ClearInteractiveDirtyHint(); return; }
        long owner = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        if (interactiveDirtyHintRoot != boundClipmapRoot || interactiveDirtyHintOwnership != owner
            || interactiveDirtyHintSettingsId != settings.GetInstanceID()) ClearInteractiveDirtyHint();
        if (replaceScope)
        {
            latestInteractiveDirtyTiles.Clear();
            latestInteractiveRegionalScope = TerrainRegionalElevationInvalidationScope.None;
        }
        interactiveDirtyHintRoot = boundClipmapRoot; interactiveDirtyHintOwnership = owner;
        interactiveDirtyHintSettingsId = settings.GetInstanceID();
        interactiveDirtyHintSettings = settings;
    }

    private static bool LatestInteractionAffectsTile(Vector2Int tile)
    {
        var settings = interactiveDirtyHintSettings;
        return HasActiveInteractiveTerrainAuthoringEdit && settings != null && interactiveDirtyHintGeneration > 0
            && interactiveDirtyHintGeneration <= authoringGeneration && interactiveDirtyHintRoot == boundClipmapRoot
            && interactiveDirtyHintOwnership == TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            && interactiveDirtyHintSettingsId == settings.GetInstanceID()
            && (latestInteractiveDirtyTiles.Contains(tile)
                || TerrainRegionalElevationResidencyPolicy.ScopeAffectsTile(settings, latestInteractiveRegionalScope, tile));
    }

    private static bool CanReuseActiveDirtySource(WorldSettings settings, DirtySourceIdentity target)
    {
        var held = activeDirtySourceIdentity;
        return activeDirtySourceLease != null && activeDirtySource != null
            && held.Tile == target.Tile && held.SettingsId == target.SettingsId && held.NativeSamples == target.NativeSamples
            && !string.IsNullOrEmpty(target.CommittedSignature) && held.CommittedSignature == target.CommittedSignature
            && (activeDirtySourceLease.HasIdentity
                ? TerrainAuthoringPreviewHeightSourceUtility.IsCurrent(settings, activeDirtySourceLease)
                : TerrainAuthoringPreviewHeightSourceUtility.TryValidateNativeSource(activeDirtySource, target.NativeSamples, out _));
    }

    private static bool DirtyGroupNeedsNativeSource(Vector2Int tile)
    {
        if (activeHeightStates != null) foreach (var state in activeHeightStates)
            if (state.SampleStride == 1 && state.PendingDirtyTiles.Contains(tile)) return true;
        return false;
    }

    private static bool ShouldRetainActiveDirtySource(WorldSettings settings, string committed)
    {
        if (settings == null || !CanReuseActiveDirtySource(settings,
            new DirtySourceIdentity(activeDirtySourceTile, committed, settings.GetInstanceID(), settings.HeightTileSamplesPerSide))
            || !PublishedDisplayIsDrawable(settings, committed) || activeDisplayIntent.OwnershipGeneration
                != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration) return false;
        bool pending = false, runnable = false, owner = false;
        foreach (var state in activeHeightStates)
        {
            owner |= IsStateTileDrawable(state, settings, committed, activeDirtySourceTile);
            pending |= state.PendingDirtyTiles.Contains(activeDirtySourceTile);
            runnable |= state.IsDirtyTileRunnable(activeDirtySourceTile);
        }
        if (HasActiveInteractiveTerrainAuthoringEdit && !LatestInteractionAffectsTile(activeDirtySourceTile)) return false;
        return owner && (runnable || !pending && LatestInteractionAffectsTile(activeDirtySourceTile));
    }

    private static void MaintainDirtySchedulingIntent(WorldSettings settings, string committed)
    {
        if (HasActiveInteractiveTerrainAuthoringEdit && (settings == null || interactiveDirtyHintRoot == null
            || interactiveDirtyHintRoot != boundClipmapRoot
            || interactiveDirtyHintOwnership != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || interactiveDirtyHintSettingsId != settings.GetInstanceID())) ClearInteractiveDirtyHint();
        if (!HasActiveInteractiveTerrainAuthoringEdit)
        {
            ClearInteractiveDirtyHint();
            if (displayPreparationDeferredForInteractiveEdit)
            { displayPreparationDeferredForInteractiveEdit = false; ScheduleRefresh(); }
        }
        if (!ReferenceEquals(activeDirtySource, null) && !ShouldRetainActiveDirtySource(settings, committed)) ReleaseActiveDirtySource();
    }

    private static void InvalidateActiveHeightContent(bool contentOnly)
    {
        if (!contentOnly)
        { ReleaseActiveDirtySource(); ClearInteractiveDirtyHint(); displayPreparationDeferredForInteractiveEdit = false; }
        if (activeHeightStates != null) foreach (var s in activeHeightStates)
        {
            s.CacheReady = false;
            if (!contentOnly) s.DirtyFailures.Clear();
            s.DirtyTargetGeneration = contentOnly ? authoringGeneration : 0L;
        }
    }

    // Every obligation is assigned before the incoming global/regional scope is
    // consumed. Window clipping keeps regional invalidation bounded by residency.
    private static bool TryProjectPendingDisplayAuthoring(WorldSettings settings, string committed,
        string overall, out string error)
    {
        error = "";
        if (activeHeightStates == null) return true;
        if (dirtyCompositeTiles.Count == 0 && !hasPendingRegionalElevationInvalidation
            && !overallSignatureAcknowledgementRequested) return true;
        var modifierResident = new HashSet<Vector2Int>(); var regionalResident = new HashSet<Vector2Int>();
        foreach (var s in activeHeightStates)
        {
            var cache = s.ActiveCache;
            if (cache == null || !cache.IsReady || cache.SourceCommittedHeightfieldSignature != committed) continue;
            var window = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
            QueueDirtyTilesForLod(s, window, dirtyCompositeTiles, modifierResident);
            diagnosticPendingGeographicDirty.UnionWith(modifierResident);
            if (hasPendingRegionalElevationInvalidation)
            {
                var resident = new List<Vector2Int>();
                if (!TerrainRegionalElevationResidencyPolicy.TryCollectResidentTiles(settings,
                    pendingRegionalElevationInvalidation, true, window, resident, out error)) return false;
                QueueDirtyTilesForLod(s, window, resident, regionalResident);
                diagnosticPendingGeographicDirty.UnionWith(regionalResident);
                foreach (var tile in resident) s.PendingRegionalTiles.Add(tile);
            }
            s.DirtyTargetGeneration = authoringGeneration;
            if (s.PendingDirtyTiles.Count > 0) s.CacheReady = false;
        }
        lastGlobalDirtyTileCount = dirtyCompositeTiles.Count;
        lastResidentDirtyTileCount = modifierResident.Count;
        lastNonresidentDirtyTileCount = Math.Max(0, lastGlobalDirtyTileCount - lastResidentDirtyTileCount);
        lastPublishedCompositeTileCount = 0;
        if (hasPendingRegionalElevationInvalidation)
        {
            lastRegionalResidentAffectedTileCount = regionalResident.Count;
            lastRegionalNonresidentAffectedTileCount = Math.Max(0L, lastRegionalLogicalAffectedTileCount - regionalResident.Count);
            ConsumePendingRegionalElevationInvalidation(0);
        }
        dirtyCompositeTiles.Clear(); overallSignatureAcknowledgementRequested = false;
        AcknowledgeCompletedDisplayAuthoring(committed, overall);
        return true;
    }

    private static void AcknowledgeCompletedDisplayAuthoring(string committed, string overall)
    {
        if (activeHeightStates == null) return;
        foreach (var s in activeHeightStates)
        {
            var c = s.ActiveCache;
            if (!committedRebuildRequested && s.HasUsableActiveAllocation && c.SourceCommittedHeightfieldSignature == committed
                && s.DirtyTargetGeneration == authoringGeneration
                && s.PendingDirtyTiles.Count == 0 && s.PendingRegionalTiles.Count == 0 && s.DirtyFailures.Count == 0)
            {
                c.MarkOverallAuthoringSignature(overall); s.ActiveAuthoringGeneration = authoringGeneration;
                s.DirtyTargetGeneration = authoringGeneration; s.CacheReady = true;
            }
        }
    }

    private static TerrainAuthoringPreviewLodState ChooseActiveDirtyDestination(out Vector2Int tile)
    {
        return ChooseDirtyDestination(activeHeightStates, activeDirtySource != null, activeDirtySourceTile,
            HasActiveInteractiveTerrainAuthoringEdit, latestInteractiveScopeContains,
            HasLiveTerrainAnalysisDemand, analysisRequiredSourceWindow, out tile);
    }

    internal static TerrainAuthoringPreviewLodState ChooseDirtyDestination(TerrainAuthoringPreviewLodState[] states,
        bool hasHeldSource, Vector2Int heldTile, bool interactive, out Vector2Int tile)
    {
        return ChooseDirtyDestination(states, hasHeldSource, heldTile, interactive, null, out tile);
    }

    internal static bool IsDirtyLiveSliceUsable(TerrainAuthoringPreviewLodState state, Vector2Int tile)
    {
        if (state == null || !state.HasUsableActiveAllocation) return false;
        var cache = state.ActiveCache;
        return cache.SampleStride == state.SampleStride && cache.SamplesPerSide == state.SamplesPerSide
            && Mathf.Approximately(cache.SampleSpacing, state.SampleSpacing)
            && cache.HeightCache != null && cache.HeightCache.IsCreated() && cache.IsSliceFinalCompositeReady(tile)
            && cache.TryGetCompositeSliceRange(tile.x, tile.y, out float low, out float high)
            && !float.IsNaN(low) && !float.IsInfinity(low) && !float.IsNaN(high) && !float.IsInfinity(high) && low <= high;
    }

    internal static bool IsLiveNativeAnalysisTile(TerrainAuthoringPreviewLodState state,
        Vector2Int tile, bool liveAnalysis, TerrainHeightCacheWindow analysisRequired) =>
        liveAnalysis && state != null && state.Level == 0 && state.SampleStride == 1
            && analysisRequired.IsValid && analysisRequired.Contains(tile);

    internal static int GetDirtyDestinationPriority(TerrainAuthoringPreviewLodState[] states,
        TerrainAuthoringPreviewLodState state, Vector2Int tile, bool interactive, Func<Vector2Int, bool> latestScope) =>
        GetDirtyDestinationPriority(states, state, tile, interactive, latestScope, false, default);

    internal static int GetDirtyDestinationPriority(TerrainAuthoringPreviewLodState[] states,
        TerrainAuthoringPreviewLodState state, Vector2Int tile, bool interactive, Func<Vector2Int, bool> latestScope,
        bool liveAnalysis, TerrainHeightCacheWindow analysisRequired)
    {
        if (state == null || !state.IsDirtyTileRunnable(tile) || !IsDirtyLiveSliceUsable(state, tile)) return int.MaxValue;
        int finestStride = int.MaxValue;
        TerrainAuthoringPreviewLodState finestOwner = null;
        bool finestRequired = false;
        bool latest = interactive && latestScope != null && latestScope(tile);
        foreach (var owner in states)
        {
            finestStride = Math.Min(finestStride, owner.SampleStride);
            if (!latest || !IsDirtyLiveSliceUsable(owner, tile)
                || owner.DirtyFailures.ContainsKey(tile)) continue;
            bool requiredOwner = owner.ActiveRequiredWindow.Contains(tile);
            if (finestOwner == null || requiredOwner && !finestRequired
                || requiredOwner == finestRequired && (owner.SampleStride < finestOwner.SampleStride
                    || owner.SampleStride == finestOwner.SampleStride && owner.Level < finestOwner.Level))
            { finestOwner = owner; finestRequired = requiredOwner; }
        }
        if (latest && ReferenceEquals(finestOwner, state))
            return TerrainAuthoringPreviewStreamingPolicy.InteractiveDirtyPriority;
        if (IsLiveNativeAnalysisTile(state, tile, liveAnalysis, analysisRequired))
            return TerrainAuthoringPreviewStreamingPolicy.FineDirtyPriority;
        if (!state.ActiveRequiredWindow.Contains(tile)) return TerrainAuthoringPreviewStreamingPolicy.GuardDirtyPriority;
        return state.SampleStride == 1 || state.SampleStride == finestStride
            ? TerrainAuthoringPreviewStreamingPolicy.FineDirtyPriority : TerrainAuthoringPreviewStreamingPolicy.RequiredDirtyPriority;
    }

    internal static TerrainAuthoringPreviewLodState ChooseDirtyDestination(TerrainAuthoringPreviewLodState[] states,
        bool hasHeldSource, Vector2Int heldTile, bool interactive, Func<Vector2Int, bool> latestScope, out Vector2Int tile) =>
        ChooseDirtyDestination(states, hasHeldSource, heldTile, interactive, latestScope, false, default, out tile);

    internal static TerrainAuthoringPreviewLodState ChooseDirtyDestination(TerrainAuthoringPreviewLodState[] states,
        bool hasHeldSource, Vector2Int heldTile, bool interactive, Func<Vector2Int, bool> latestScope,
        bool liveAnalysis, TerrainHeightCacheWindow analysisRequired, out Vector2Int tile)
    {
        tile = default;
        if (states == null || states.Length == 0) return null;
        TerrainAuthoringPreviewLodState chosen = null;
        int chosenPriority = int.MaxValue;
        foreach (var state in states)
            foreach (var candidate in state.PendingDirtyTiles)
            {
                int priority = GetDirtyDestinationPriority(states, state, candidate, interactive, latestScope, liveAnalysis, analysisRequired);
                if (priority == int.MaxValue) continue;
                bool candidateAnalysis = IsLiveNativeAnalysisTile(state, candidate, liveAnalysis, analysisRequired);
                bool chosenAnalysis = IsLiveNativeAnalysisTile(chosen, tile, liveAnalysis, analysisRequired);
                bool sameRepresentation = chosen != null && state.SampleStride == chosen.SampleStride && state.Level == chosen.Level;
                bool candidateHeld = hasHeldSource && candidate == heldTile, chosenHeld = hasHeldSource && tile == heldTile;
                if (chosen == null || priority < chosenPriority
                    || priority == chosenPriority && (candidateAnalysis && !chosenAnalysis
                        || candidateAnalysis == chosenAnalysis && (state.SampleStride < chosen.SampleStride
                            || state.SampleStride == chosen.SampleStride && state.Level < chosen.Level
                            || sameRepresentation && (candidateHeld && !chosenHeld
                                || candidateHeld == chosenHeld && (candidate.y < tile.y || candidate.y == tile.y && candidate.x < tile.x)))))
                { chosen = state; tile = candidate; chosenPriority = priority; }
            }
        return chosen;
    }

    internal static void CompleteDirtyContent(TerrainAuthoringPreviewLodState state, Vector2Int tile)
    {
        state.PendingDirtyTiles.Remove(tile);
        state.PendingRegionalTiles.Remove(tile);
        state.DirtyFailures.Remove(tile);
        state.SuccessfulDirtyTiles.Add(tile);
    }

    private static bool DirtyTargetStillCurrent(TerrainAuthoringPreviewLodState state,
        TerrainAuthoringPreviewCache cache, RenderTexture texture, TerrainAuthoringPreviewDisplayIntent display,
        long generation, string committed, WorldSettings settings, TerrainAuthoringData data, string overall)
    {
        if (!CanRunEditorPreviewWork || displayCommitInProgress || committedRebuildRequested || activeHeightStates == null
            || state.Level >= activeHeightStates.Length || !ReferenceEquals(activeHeightStates[state.Level], state)
            || !ReferenceEquals(state.ActiveCache, cache) || cache.HeightCache != texture || texture == null || !texture.IsCreated()
            || !ReferenceEquals(display, activeDisplayIntent) || display == null || display.Root != boundClipmapRoot
            || !display.ConfigurationMatches(settings)
            || display.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || authoringGeneration != generation || state.DirtyTargetGeneration != generation || !state.HasUsableActiveAllocation
            || state.SampleStride != cache.SampleStride || state.SamplesPerSide != cache.SamplesPerSide
            || !Mathf.Approximately(state.SampleSpacing, cache.SampleSpacing)) return false;
        return cache.SourceCommittedHeightfieldSignature == committed
            && TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings) == committed
            && TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data) == overall;
    }

    private static void AdvanceActiveDisplayDirty(WorldSettings settings, TerrainAuthoringData data,
        System.Diagnostics.Stopwatch watch)
    {
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
        var first = ChooseActiveDirtyDestination(out Vector2Int groupTile);
        if (first == null || !PublishedDisplayIsDrawable(settings, committed)) return;
        var identity = new DirtySourceIdentity(groupTile, committed, settings.GetInstanceID(), settings.HeightTileSamplesPerSide);
        if (!CanReuseActiveDirtySource(settings, identity)) ReleaseActiveDirtySource();
        long batchGeneration = authoringGeneration;
        while (LastDirtyUpdateCompositions < DefaultDirtyCompositionsPerUpdate)
        {
            var state = ChooseActiveDirtyDestination(out Vector2Int tile);
            if (state == null || tile != groupTile || !ShouldRunActiveDirtyWork(settings, committed, overall, state, tile)) return;
            var cache = state.ActiveCache; var texture = cache.HeightCache; var display = activeDisplayIntent;
            long target = authoringGeneration;
            if (target != batchGeneration || !DirtyTargetStillCurrent(state, cache, texture, display,
                target, committed, settings, data, overall)) return;
            var scratch = state.DirtyScratch;
            bool needsScratch = scratch == null || !scratch.IsCreated() || scratch.width != state.SamplesPerSide
                || scratch.height != state.SamplesPerSide || scratch.volumeDepth != 2
                || scratch.dimension != UnityEngine.Rendering.TextureDimension.Tex2DArray
                || scratch.format != RenderTextureFormat.RFloat || scratch.antiAliasing != 1
                || scratch.useMipMap || !scratch.enableRandomWrite;
            if (!TerrainAuthoringPreviewStreamingPolicy.CanAdmitDirtyRepresentation(LastDirtyUpdateAllocations,
                LastDirtyUpdateCopies, LastDirtyUpdateMaterializations, LastDirtyUpdateCompositions, needsScratch, state.SampleStride)) return;
            bool contentCommitted = false, liveSliceSafe = true;
            string error = "";
            try
            {
                int sourceStride = DirtyGroupNeedsNativeSource(tile) ? 1 : state.SampleStride;
                if (activeDirtySourceLease != null && !activeDirtySourceLease.CanMaterializeAt(sourceStride))
                    ReleaseActiveDirtySource();
                if (activeDirtySource == null)
                {
                    if (LastDirtyUpdateLoads >= DefaultCommittedLoadsPerUpdate) return;
                    LastDirtyUpdateLoads++;
                    Texture2D native = null;
                    if (!TerrainAuthoringPreviewHeightSourceUtility.TryAcquireCommittedSource(settings, tile, sourceStride,
                        ref native, false, out activeDirtySourceLease, out error))
                        throw new InvalidOperationException(error);
                    activeDirtySourceTile = tile; activeDirtySourceIdentity = identity;
                    if (watch.Elapsed.TotalMilliseconds >= DefaultSoftWorkBudgetMilliseconds) return;
                }
                if (!CanReuseActiveDirtySource(settings, identity))
                    throw new InvalidOperationException("The held committed Height source became incompatible.");
                // Count allocation attempts as work, including unsuccessful attempts.
                if (needsScratch) LastDirtyUpdateAllocations++;
                if (!state.TryEnsureDirtyScratch(out bool allocated, out error)) throw new InvalidOperationException(error);
                if (allocated) CaptureTransitionMemoryEstimate();
                if (allocated && watch.Elapsed.TotalMilliseconds >= DefaultSoftWorkBudgetMilliseconds) return;
                if (!cache.TryGetCommittedRange(tile, out float low, out float high, out error)) throw new InvalidOperationException(error);
                LastDirtyUpdateMaterializations++;
                if (state.SampleStride == 1) LastDirtyUpdateCopies++;
                if (!activeDirtyMaterializer.TryMaterialize(activeDirtySourceLease, state.DirtyScratch, 0,
                    settings.HeightTileSamplesPerSide, state.SamplesPerSide, state.SampleStride, out error))
                    throw new InvalidOperationException(error);
                LastDirtyUpdateCompositions++;
                if (!heightCompositor.TryComposeTile(state.DirtyScratch, tile, 0, state.SamplesPerSide,
                    state.SampleSpacing, settings.HeightTileWorldSize, cache.WorldSizeXZ, data,
                    low, high, out float finalLow, out float finalHigh, out error)) throw new InvalidOperationException(error);
                if (!DirtyTargetStillCurrent(state, cache, texture, display, target, committed, settings, data, overall)
                    || !CanReuseActiveDirtySource(settings, identity)) return;
                bool success = cache.TryCommitCompositeSliceFromScratch(state.DirtyScratch, tile, finalLow, finalHigh,
                    out liveSliceSafe, out int copies, out error);
                LastDirtyUpdateCopies += copies;
                if (!success) throw new InvalidOperationException(error);
                contentCommitted = true;
                bool regional = state.PendingRegionalTiles.Contains(tile);
                CompleteDirtyContent(state, tile);
                dirtyContentBoundary++;
                if (regional) lastRegionalPublishedCompositeTileCount++;
                pendingCompositePublication.Add(tile);
                if (state.Level == 0 && state.SampleStride == 1) pendingNativePublication.Add(tile);
                bool groupPending = false;
                foreach (var owner in activeHeightStates) if (owner.PendingDirtyTiles.Contains(tile)) groupPending = true;
                if (!groupPending) diagnosticPendingGeographicDirty.Remove(tile);
                AcknowledgeCompletedDisplayAuthoring(committed, overall);
            }
            catch (Exception exception)
            {
                ReleaseActiveDirtySource();
                if (contentCommitted)
                {
                    CompleteDirtyContent(state, tile);
                    ReportFollowUpFailure("Height publication", exception.Message);
                }
                else if (DirtyTargetStillCurrent(state, cache, texture, display, target, committed, settings, data, overall)
                    || ((!liveSliceSafe || texture == null || !texture.IsCreated()) && activeHeightStates != null
                        && state.Level < activeHeightStates.Length && ReferenceEquals(activeHeightStates[state.Level], state)
                        && ReferenceEquals(state.ActiveCache, cache)))
                {
                    liveSliceSafe &= texture != null && texture.IsCreated();
                    dirtyFailureAttemptSequence = NextAnalysisGeneration(dirtyFailureAttemptSequence);
                    state.RecordDirtyFailure(tile, target, exception.Message, liveSliceSafe, dirtyFailureAttemptSequence);
                    diagnosticPendingGeographicDirty.Add(tile);
                    if (!liveSliceSafe)
                    {
                        state.WriteFailed = true;
                        TerrainAuthoringPreviewHeightBindingUtility.Disable(boundHeightRenderers, state.Level);
                        ClearDisplayTransitionFailureSuppression(); clipmapRebindRequested = true; ScheduleRefresh();
                    }
                    SetStatus(liveSliceSafe ? TerrainAuthoringPreviewStatus.Ready : TerrainAuthoringPreviewStatus.Error,
                        $"Height update failed at LOD {state.Level}, tile {tile}. "
                        + (liveSliceSafe ? "Last-good terrain is retained. " : "The unsafe representation requires replacement. ") + exception.Message);
                    Debug.LogWarning($"Resident Height update, LOD {state.Level}, tile {tile}: {exception.Message}");
                    activeDirtyMaterializer.ReleaseTextureBindings(); heightCompositor.ReleaseTextureBindings();
                    NotifyPreviewStateChanged();
                }
                if (!contentCommitted) return;
            }
            finally
            {
                activeDirtyMaterializer.ReleaseTextureBindings();
                heightCompositor.ReleaseTextureBindings();
            }
            QueueBoundsFollowUp();
            TryAdvancePreviewFollowUps(settings, data, committed, overall);
            ScheduleRefresh(); NotifyPreviewStateChanged(); RepaintEditorViews();
            // Observers may invalidate authoring, replace ownership, or dispose live storage.
            if (!DirtyTargetStillCurrent(state, cache, texture, display, target, committed, settings, data, overall)
                || activeDirtySource == null) return;
            if (!ShouldRetainActiveDirtySource(settings, committed)) { ReleaseActiveDirtySource(); return; }
            if (watch.Elapsed.TotalMilliseconds >= DefaultSoftWorkBudgetMilliseconds) return;
        }
    }

    private static void PublishCompletedDisplayDirtyTiles(string committed, string overall)
    {
        if (activeHeightStates == null) return;
        long target = authoringGeneration;
        long ownership = TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration;
        var states = activeHeightStates; var display = activeDisplayIntent;
        if (pendingNativePublication.Count > 0)
            TryRunAnalysisFollowUp(PublishNativeTerrainAnalysisCompositeUpdate);
        var settings = LoadWorldSettings();
        if (!CanRunEditorPreviewWork || target != authoringGeneration || !ReferenceEquals(states, activeHeightStates)
            || !ReferenceEquals(display, activeDisplayIntent) || settings == null
            || display == null || display.OwnershipGeneration != ownership
            || ownership != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration
            || !PublishedDisplayIsDrawable(settings, committed)) return;
        var completed = new List<Vector2Int>();
        var dropped = new List<Vector2Int>();
        foreach (var tile in pendingCompositePublication)
        {
            bool current = IsGeographicDirtyTileCurrent(states, committed, target, tile, dirtyCompositeTiles,
                IsWorldTilePendingRegionalElevationRecomposition(settings, tile), out bool hasOwner);
            if (current) completed.Add(tile);
            else if (!hasOwner) dropped.Add(tile);
        }
        foreach (var tile in dropped) pendingCompositePublication.Remove(tile);
        if (completed.Count == 0) return;
        SortWorldTilesRowMajor(completed);
        foreach (var tile in completed) pendingCompositePublication.Remove(tile);
        lastPublishedCompositeTileCount = completed.Count;
        DispatchPreviewObservers(CompositeTilesUpdated, (IReadOnlyList<Vector2Int>)completed, "Composite tiles");
    }

    internal static void QueueDirtyTilesForLod(TerrainAuthoringPreviewLodState state,
        TerrainHeightCacheWindow physicalWindow, IEnumerable<Vector2Int> tiles, ISet<Vector2Int> resident)
    {
        foreach (var tile in tiles)
            if (physicalWindow.Contains(tile))
            {
                state.PendingDirtyTiles.Add(tile); state.SuccessfulDirtyTiles.Remove(tile); resident?.Add(tile);
            }
    }

    // Observability only; maintained alongside existing bounded dirty projection.
    private static readonly HashSet<Vector2Int> diagnosticPendingGeographicDirty = new HashSet<Vector2Int>();
    // Read-only late snapshot. Legacy work may already have drained part of this
    // transition, so it deliberately supplies incomplete evidence: no acknowledgements.
    // A controlled caller with authoritative old/new footprints uses TryProject directly.
    internal static bool TryCaptureGeographicAuthoringScope(WorldSettings settings,
        TerrainAuthoringPreviewGeographicDemandPlan demand, TerrainAuthoringPreviewSharedHeightCache cache,
        long previousGeneration, string committed,
        out TerrainAuthoringPreviewGeographicAuthoringProjection projection, out string error)
    {
        projection = null; error = "";
        if (settings == null || demand == null || demand.OwnershipGeneration != TerrainAuthoringSceneViewController.SceneViewOwnershipGeneration)
        { error = "Geographical authoring snapshot ownership is stale."; return false; }
        var incoming = new List<Vector2Int>(dirtyCompositeTiles);
        return TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, demand, cache,
            previousGeneration, authoringGeneration, committed, incoming, null,
            hasPendingRegionalElevationInvalidation ? pendingRegionalElevationInvalidation
                : TerrainRegionalElevationInvalidationScope.None, out projection, out error);
    }
}





