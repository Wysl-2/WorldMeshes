using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    private static long authoringGeneration;

    private static long activeCacheAuthoringGeneration;

    private static string lastAuthoringGenerationReason =
        "";

    private static int lastGlobalDirtyTileCount;

    private static int lastResidentDirtyTileCount;

    private static int lastNonresidentDirtyTileCount;

    private static int lastPublishedCompositeTileCount;

    private static readonly List<Vector2Int>
        modifierResidencyGlobalDirtyScratch =
            new List<Vector2Int>();

    private static readonly List<Vector2Int>
        modifierResidencyResidentDirtyScratch =
            new List<Vector2Int>();

    private static readonly List<Vector2Int>
        modifierResidencyNonresidentDirtyScratch =
            new List<Vector2Int>();

    public static long AuthoringGeneration =>
        authoringGeneration;

    public static long ActiveCacheAuthoringGeneration =>
        activeCache != null
        &&
        activeCache.IsReady
            ? activeCacheAuthoringGeneration
            : 0L;

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

    public static bool StreamingRestartDeferredForInteractiveEdit =>
        HasActiveInteractiveTerrainAuthoringEdit
        &&
        (
            hasPendingStreamingStart
            ||
            (
                currentTransition != null
                &&
                TransitionInProgress
            )
        );

    private static void RegisterPreviewAuthoringInvalidation(
        string reason
    )
    {
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

        bool hasStreamingWork =
            hasPendingStreamingStart
            ||
            (
                currentTransition != null
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
             * Generation is part of Package 05 streaming diagnostics. Force a
             * publication even when the Package 04 phase/counters are otherwise
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

    internal static void MarkActiveCacheAuthoringGeneration(
        long generation
    )
    {
        activeCacheAuthoringGeneration =
            generation;
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

    private static bool TryProcessResidentModifierAuthoring(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        string currentCommittedSignature,
        string currentOverallSignature,
        out int updatedResidentSliceCount,
        out bool compositeRangeChanged,
        out string errorMessage
    )
    {
        updatedResidentSliceCount =
            0;

        compositeRangeChanged =
            false;

        errorMessage =
            "";

        if (
            worldSettings == null
            ||
            authoringData == null
            ||
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            return true;
        }

        if (
            previewCache.SourceCommittedHeightfieldSignature !=
                currentCommittedSignature
        )
        {
            return true;
        }

        modifierResidencyGlobalDirtyScratch.Clear();

        foreach (
            Vector2Int tile
            in dirtyCompositeTiles
        )
        {
            modifierResidencyGlobalDirtyScratch.Add(
                tile
            );
        }

        SortWorldTilesRowMajor(
            modifierResidencyGlobalDirtyScratch
        );

        bool hasActiveWindow =
            TryGetActiveResidentWindow(
                out TerrainHeightCacheWindow activeWindow
            );

        PartitionDirtyTilesForActiveWindow(
            modifierResidencyGlobalDirtyScratch,
            hasActiveWindow,
            activeWindow,
            modifierResidencyResidentDirtyScratch,
            modifierResidencyNonresidentDirtyScratch
        );

        lastGlobalDirtyTileCount =
            modifierResidencyGlobalDirtyScratch.Count;

        lastResidentDirtyTileCount =
            modifierResidencyResidentDirtyScratch.Count;

        lastNonresidentDirtyTileCount =
            modifierResidencyNonresidentDirtyScratch.Count;

        lastPublishedCompositeTileCount =
            0;

        bool hadPendingRegionalInvalidation =
            hasPendingRegionalElevationInvalidation;

        if (
            !TryCollectPendingRegionalResidentTiles(
                worldSettings,
                hasActiveWindow,
                activeWindow,
                regionalResidencyResidentDirtyScratch,
                out errorMessage
            )
        )
        {
            return false;
        }

        BuildResidentAuthoringDirtyUnion(
            modifierResidencyResidentDirtyScratch,
            regionalResidencyResidentDirtyScratch,
            regionalResidencyCompositeUnionScratch
        );

        if (
            regionalResidencyCompositeUnionScratch.Count == 0
        )
        {
            if (
                CanAcknowledgeActiveAuthoringState(
                    true,
                    0
                )
            )
            {
                dirtyCompositeTiles.Clear();

                ConsumePendingRegionalElevationInvalidation(
                    0
                );

                previewCache
                    .MarkOverallAuthoringSignature(
                        currentOverallSignature
                    );

                activeCacheAuthoringGeneration =
                    authoringGeneration;

                overallSignatureAcknowledgementRequested =
                    false;

                if (
                    lastGlobalDirtyTileCount > 0
                    ||
                    hadPendingRegionalInvalidation
                    ||
                    !string.IsNullOrEmpty(
                        lastAuthoringGenerationReason
                    )
                )
                {
                    NotifyPreviewStateChanged();
                }
            }

            return true;
        }

        float globalMinimumBefore =
            previewCache.MinimumHeight;

        float globalMaximumBefore =
            previewCache.MaximumHeight;

        heightCompositor
            .BeginTransactionDiagnostics();

        if (
            !previewCache
                .ResetCompositeTilesForRecomposition(
                    regionalResidencyCompositeUnionScratch,
                    out updatedResidentSliceCount,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (
            updatedResidentSliceCount !=
                regionalResidencyCompositeUnionScratch.Count
        )
        {
            errorMessage =
                "The resident modifier/regional dirty union and committed-base " +
                "reset produced different slice counts.";

            return false;
        }

        List<TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate>
            finalCompositeRanges =
                new List<TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate>(
                    updatedResidentSliceCount
                );

        for (
            int index = 0;
            index < regionalResidencyCompositeUnionScratch.Count;
            index++
        )
        {
            Vector2Int dirtyTile =
                regionalResidencyCompositeUnionScratch[index];

            int sliceIndex =
                previewCache.GetSliceIndex(
                    dirtyTile.x,
                    dirtyTile.y
                );

            if (sliceIndex < 0)
            {
                errorMessage =
                    $"Resident dirty tile ({dirtyTile.x}, {dirtyTile.y}) " +
                    "lost active residency during recomposition.";

                return false;
            }

            if (
                !previewCache.TryGetCompositeSliceRange(
                    dirtyTile.x,
                    dirtyTile.y,
                    out float baseMinimumHeight,
                    out float baseMaximumHeight
                )
            )
            {
                errorMessage =
                    "The active cache could not provide committed/base " +
                    $"range metadata for resident dirty tile " +
                    $"({dirtyTile.x}, {dirtyTile.y}).";

                return false;
            }

            if (
                !heightCompositor.TryComposeTile(
                    previewCache.HeightCache,
                    dirtyTile,
                    sliceIndex,
                    previewCache.SamplesPerSide,
                    previewCache.SampleSpacing,
                    worldSettings.HeightTileWorldSize,
                    previewCache.WorldSizeXZ,
                    authoringData,
                    baseMinimumHeight,
                    baseMaximumHeight,
                    out float compositeMinimumHeight,
                    out float compositeMaximumHeight,
                    out errorMessage
                )
            )
            {
                return false;
            }

            finalCompositeRanges.Add(
                new TerrainAuthoringPreviewCache.CompositeSliceRangeUpdate(
                    dirtyTile,
                    compositeMinimumHeight,
                    compositeMaximumHeight
                )
            );
        }

        if (
            heightCompositor.LastDispatchTileCount !=
                updatedResidentSliceCount
        )
        {
            errorMessage =
                "The resident authoring reset/composition transaction " +
                "produced different valid-slice counts.";

            return false;
        }

        if (
            !previewCache.ApplyCompositeSliceRangeBatch(
                finalCompositeRanges,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        compositeRangeChanged =
            !Mathf.Approximately(
                globalMinimumBefore,
                previewCache.MinimumHeight
            )
            ||
            !Mathf.Approximately(
                globalMaximumBefore,
                previewCache.MaximumHeight
            );

        dirtyCompositeTiles.Clear();

        ConsumePendingRegionalElevationInvalidation(
            regionalResidencyResidentDirtyScratch.Count
        );

        previewCache
            .MarkOverallAuthoringSignature(
                currentOverallSignature
            );

        activeCacheAuthoringGeneration =
            authoringGeneration;

        overallSignatureAcknowledgementRequested =
            false;

        List<Vector2Int> publishedTiles =
            new List<Vector2Int>(
                regionalResidencyCompositeUnionScratch
            );

        lastPublishedCompositeTileCount =
            publishedTiles.Count;

        if (publishedTiles.Count > 0)
        {
            CompositeTilesUpdated?.Invoke(
                publishedTiles
            );
        }

        NotifyPreviewStateChanged();

        return true;
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
            !Enabled
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
            ||
            Status ==
                TerrainAuthoringPreviewStatus.Error
        )
        {
            return false;
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

    private static TerrainAuthoringPreviewReadiness EvaluateWorldTileReadiness(
        WorldSettings worldSettings,
        Vector2Int worldTile,
        bool previewAvailable,
        string currentCommittedSignature
    )
    {
        bool insideWorld =
            worldSettings != null
            &&
            worldTile.x >= 0
            &&
            worldTile.y >= 0
            &&
            worldTile.x <
                worldSettings.HeightTileGridWidth
            &&
            worldTile.y <
                worldSettings.HeightTileGridHeight;

        if (!insideWorld)
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        bool hasActiveCache =
            activeCache != null
            &&
            activeCache.IsReady;

        bool committedSourceCurrent =
            hasActiveCache
            &&
            activeCache.SourceCommittedHeightfieldSignature ==
                currentCommittedSignature;

        bool resident =
            hasActiveCache
            &&
            activeCache.GetSliceIndex(
                worldTile.x,
                worldTile.y
            ) >= 0;

        bool finalCompositeReady =
            resident
            &&
            activeCache.IsSliceFinalCompositeReady(
                worldTile
            );

        bool pendingResidentDirty =
            resident
            &&
            (
                dirtyCompositeTiles.Contains(
                    worldTile
                )
                ||
                IsWorldTilePendingRegionalElevationRecomposition(
                    worldSettings,
                    worldTile
                )
            );

        return
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    previewAvailable,
                    true,
                    hasActiveCache,
                    committedSourceCurrent,
                    resident,
                    finalCompositeReady,
                    pendingResidentDirty
                );
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
}
