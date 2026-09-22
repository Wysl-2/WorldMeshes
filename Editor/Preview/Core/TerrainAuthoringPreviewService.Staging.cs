using System;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    public static event Action<
        TerrainHeightCacheWindow,
        string
    > HeightCacheTransitionFailed;

    private static TerrainAuthoringPreviewCacheTransition
        currentTransition;

    private static bool hasTransitionFailure;

    private static TerrainHeightCacheWindow
        lastFailedTransitionWindow;

    private static string lastTransitionFailureMessage =
        "";

    private static string lastFailedCommittedSignature =
        "";

    private static string lastFailedOverallSignature =
        "";

    public static bool TransitionInProgress
    {
        get
        {
            return
                currentTransition != null
                &&
                currentTransition.State !=
                    TerrainAuthoringPreviewTransitionState
                        .Activated
                &&
                currentTransition.State !=
                    TerrainAuthoringPreviewTransitionState
                        .Failed
                &&
                currentTransition.State !=
                    TerrainAuthoringPreviewTransitionState
                        .Cancelled;
        }
    }

    public static bool HasTransitionDiagnostics
    {
        get
        {
            return
                currentTransition != null
                ||
                hasTransitionFailure;
        }
    }

    public static string TransitionStateLabel
    {
        get
        {
            if (currentTransition == null)
            {
                return
                    hasTransitionFailure
                        ? "Failed"
                        : "Idle";
            }

            switch (currentTransition.State)
            {
                case TerrainAuthoringPreviewTransitionState.CopyingRetained:
                    return "Copying Retained";

                case TerrainAuthoringPreviewTransitionState.LoadingSourceTiles:
                    return "Loading Source Tiles";

                case TerrainAuthoringPreviewTransitionState.ComposingSourceTiles:
                    return "Composing Source Tiles";

                case TerrainAuthoringPreviewTransitionState.ReadyToActivate:
                    return "Ready To Activate";

                default:
                    return
                        currentTransition
                            .State
                            .ToString();
            }
        }
    }

    public static bool HasTransitionFailure =>
        hasTransitionFailure;

    public static string LastTransitionFailureMessage =>
        lastTransitionFailureMessage;

    public static TerrainHeightCacheWindow LastFailedTransitionWindow =>
        lastFailedTransitionWindow;

    public static int LastTransitionRetainedTileCount =>
        currentTransition != null
            ? currentTransition.RetainedTiles.Count
            : 0;

    public static int LastTransitionEnteringTileCount =>
        currentTransition != null
            ? currentTransition.EnteringTiles.Count
            : 0;

    public static int LastTransitionLeavingTileCount =>
        currentTransition != null
            ? currentTransition.LeavingTiles.Count
            : 0;

    public static int LastTransitionReusableRetainedTileCount =>
        currentTransition != null
            ? currentTransition.ReusableRetainedTiles.Count
            : 0;

    public static int LastTransitionRetainedGpuCopyCount =>
        currentTransition != null
            ? currentTransition.RetainedGpuCopyCount
            : 0;

    public static int LastTransitionCommittedSourceLoadCount =>
        currentTransition != null
            ? currentTransition.CommittedSourceLoadCount
            : 0;

    public static int LastTransitionComposedTileCount =>
        currentTransition != null
            ? currentTransition.FullyComposedTileCount
            : 0;

    public static long ApproximateStagingGpuMemoryBytes
    {
        get
        {
            return
                stagingCache != null
                    ? stagingCache.ApproximateGpuMemoryBytes
                    : 0L;
        }
    }

    public static long ApproximateTotalResidentGpuMemoryBytes =>
        ApproximateGpuMemoryBytes
        +
        ApproximateStagingGpuMemoryBytes;

    public static bool TryGetStagingResidentWindow(
        out TerrainHeightCacheWindow window
    )
    {
        window =
            default;

        if (
            stagingCache == null
            ||
            !stagingCache.IsReady
        )
        {
            return false;
        }

        window =
            new TerrainHeightCacheWindow(
                stagingCache.CacheOriginTile,
                stagingCache.CacheSize
            );

        return
            window.IsValid;
    }

    internal static bool TryGetActiveCacheForValidation(
        out TerrainAuthoringPreviewCache cache
    )
    {
        cache =
            activeCache;

        return
            cache != null
            &&
            cache.IsReady;
    }

    private static bool TryExecuteSynchronousStagedTransition(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainHeightCacheWindow targetWindow,
        string currentCommittedSignature,
        string currentOverallSignature,
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        ReleaseStagingCacheOnly();

        TerrainHeightCacheWindow sourceWindow =
            default;

        bool hasSourceWindow =
            activeCache != null
            &&
            activeCache.IsReady
            &&
            TryGetActiveResidentWindow(
                out sourceWindow
            );

        if (
            !TerrainAuthoringPreviewCacheTransition
                .TryCreate(
                    hasSourceWindow,
                    sourceWindow,
                    targetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    out TerrainAuthoringPreviewCacheTransition transition,
                    out errorMessage
                )
        )
        {
            return
                FailStagedTransition(
                    transition,
                    targetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        currentTransition =
            transition;

        if (
            activeCache != null
            &&
            activeCache.HeightCache != null
        )
        {
            transition.SourceTextureInstanceId =
                activeCache
                    .HeightCache
                    .GetInstanceID();
        }

        stagingCache =
            new TerrainAuthoringPreviewCache();

        if (
            !stagingCache.TryInitializeStagingWindow(
                worldSettings,
                authoringData,
                targetWindow,
                out errorMessage
            )
        )
        {
            return
                FailStagedTransition(
                    transition,
                    targetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        transition.DestinationTextureInstanceId =
            stagingCache.HeightCache != null
                ? stagingCache
                    .HeightCache
                    .GetInstanceID()
                : 0;

        bool retainedReuseAllowed =
            IsRetainedReuseGloballyEligible(
                activeCache,
                stagingCache,
                currentCommittedSignature,
                currentOverallSignature,
                committedRebuildRequested
            );

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .CopyingRetained
        );

        foreach (
            Vector2Int retainedTile
            in transition.RetainedTiles
        )
        {
            if (
                retainedReuseAllowed
                &&
                activeCache.IsSliceFinalCompositeReady(
                    retainedTile
                )
            )
            {
                if (
                    !stagingCache.TryCopyFinalCompositeTileFrom(
                        activeCache,
                        retainedTile,
                        out errorMessage
                    )
                )
                {
                    return
                        FailStagedTransition(
                            transition,
                            targetWindow,
                            currentCommittedSignature,
                            currentOverallSignature,
                            errorMessage
                        );
                }

                transition.AddReusableRetainedTile(
                    retainedTile
                );

                transition.RetainedGpuCopyCount++;

                continue;
            }

            transition.AddSourceMaterializationTile(
                retainedTile
            );
        }

        foreach (
            Vector2Int enteringTile
            in transition.EnteringTiles
        )
        {
            transition.AddSourceMaterializationTile(
                enteringTile
            );
        }

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .LoadingSourceTiles
        );

        heightCompositor
            .BeginTransactionDiagnostics();

        foreach (
            Vector2Int materializationTile
            in transition.SourceMaterializationTiles
        )
        {
            if (
                !stagingCache.TryLoadCommittedBaseTile(
                    materializationTile,
                    out errorMessage
                )
            )
            {
                return
                    FailStagedTransition(
                        transition,
                        targetWindow,
                        currentCommittedSignature,
                        currentOverallSignature,
                        errorMessage
                    );
            }

            transition.CommittedSourceLoadCount++;

            if (
                !stagingCache.TryGetCommittedRange(
                    materializationTile,
                    out float baseMinimum,
                    out float baseMaximum,
                    out errorMessage
                )
            )
            {
                return
                    FailStagedTransition(
                        transition,
                        targetWindow,
                        currentCommittedSignature,
                        currentOverallSignature,
                        errorMessage
                    );
            }

            int stagingSlice =
                stagingCache.GetSliceIndex(
                    materializationTile.x,
                    materializationTile.y
                );

            if (stagingSlice < 0)
            {
                errorMessage =
                    $"Staging tile ({materializationTile.x}, " +
                    $"{materializationTile.y}) could not resolve a local slice.";

                return
                    FailStagedTransition(
                        transition,
                        targetWindow,
                        currentCommittedSignature,
                        currentOverallSignature,
                        errorMessage
                    );
            }

            if (
                !heightCompositor.TryComposeTile(
                    stagingCache.HeightCache,
                    materializationTile,
                    stagingSlice,
                    stagingCache.SamplesPerSide,
                    stagingCache.SampleSpacing,
                    worldSettings.HeightTileWorldSize,
                    stagingCache.WorldSizeXZ,
                    authoringData,
                    baseMinimum,
                    baseMaximum,
                    out float compositeMinimum,
                    out float compositeMaximum,
                    out errorMessage
                )
            )
            {
                return
                    FailStagedTransition(
                        transition,
                        targetWindow,
                        currentCommittedSignature,
                        currentOverallSignature,
                        errorMessage
                    );
            }

            if (
                !stagingCache.TryCommitFinalCompositeTile(
                    materializationTile,
                    compositeMinimum,
                    compositeMaximum,
                    out errorMessage
                )
            )
            {
                return
                    FailStagedTransition(
                        transition,
                        targetWindow,
                        currentCommittedSignature,
                        currentOverallSignature,
                        errorMessage
                    );
            }

            transition.FullyComposedTileCount++;
        }

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .Finalizing
        );

        string latestCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string latestOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            latestCommittedSignature !=
                currentCommittedSignature
            ||
            latestOverallSignature !=
                currentOverallSignature
        )
        {
            errorMessage =
                "Authoring state changed while the staging cache was being prepared.";

            return
                FailStagedTransition(
                    transition,
                    targetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        if (
            !stagingCache.TryFinalizeStagingForActivation(
                currentOverallSignature,
                out errorMessage
            )
        )
        {
            return
                FailStagedTransition(
                    transition,
                    targetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .ReadyToActivate
        );

        if (
            !TryActivateStagingCache(
                clipmapRoot,
                transition,
                currentCommittedSignature,
                currentOverallSignature,
                out errorMessage
            )
        )
        {
            return false;
        }

        return true;
    }

    internal static bool IsRetainedReuseGloballyEligible(
        TerrainAuthoringPreviewCache sourceCache,
        TerrainAuthoringPreviewCache destinationCache,
        string currentCommittedSignature,
        string currentOverallSignature,
        bool rebuildRequested
    )
    {
        return
            sourceCache != null
            &&
            destinationCache != null
            &&
            sourceCache.IsCompleteForActivation
            &&
            !rebuildRequested
            &&
            sourceCache
                .SourceCommittedHeightfieldSignature
            ==
            currentCommittedSignature
            &&
            sourceCache
                .SourceOverallAuthoringSignature
            ==
            currentOverallSignature
            &&
            sourceCache.SamplesPerSide ==
                destinationCache.SamplesPerSide
            &&
            Mathf.Approximately(
                sourceCache.SampleSpacing,
                destinationCache.SampleSpacing
            )
            &&
            sourceCache.WorldSizeXZ ==
                destinationCache.WorldSizeXZ;
    }

    private static bool TryActivateStagingCache(
        Transform clipmapRoot,
        TerrainAuthoringPreviewCacheTransition transition,
        string currentCommittedSignature,
        string currentOverallSignature,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            stagingCache == null
            ||
            transition == null
            ||
            transition.State !=
                TerrainAuthoringPreviewTransitionState
                    .ReadyToActivate
        )
        {
            errorMessage =
                "The staging cache is not ready for activation.";

            return
                FailStagedTransition(
                    transition,
                    transition != null
                        ? transition.TargetWindow
                        : default,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        if (
            !stagingCache.TryValidateCompleteForActivation(
                out errorMessage
            )
            ||
            stagingCache.CacheOriginTile !=
                transition.TargetWindow.OriginTile
            ||
            stagingCache.CacheSize !=
                transition.TargetWindow.Size
            ||
            stagingCache
                .SourceCommittedHeightfieldSignature
            !=
            transition.TargetCommittedHeightfieldSignature
            ||
            stagingCache
                .SourceOverallAuthoringSignature
            !=
            transition.TargetOverallAuthoringSignature
        )
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage =
                    "The completed staging cache does not match its transition target metadata.";
            }

            return
                FailStagedTransition(
                    transition,
                    transition.TargetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        TerrainAuthoringPreviewCache previousActive =
            activeCache;

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .Activating
        );

        if (
            !TryBindSpecificPreviewCache(
                stagingCache,
                clipmapRoot,
                out errorMessage
            )
        )
        {
            if (
                previousActive != null
                &&
                previousActive.IsReady
            )
            {
                TryBindSpecificPreviewCache(
                    previousActive,
                    clipmapRoot,
                    out _
                );
            }
            else
            {
                TerrainHeightCacheBindingUtility
                    .Disable(
                        clipmapRoot
                    );
            }

            return
                FailStagedTransition(
                    transition,
                    transition.TargetWindow,
                    currentCommittedSignature,
                    currentOverallSignature,
                    errorMessage
                );
        }

        activeCache =
            stagingCache;

        stagingCache =
            null;

        transition.SetState(
            TerrainAuthoringPreviewTransitionState
                .Activated
        );

        if (
            hasRequestedResidencyWindow
            &&
            requestedResidencyWindow ==
                transition.TargetWindow
        )
        {
            ClearRequestedResidency();
        }

        committedRebuildRequested =
            false;

        clipmapRebindRequested =
            false;

        dirtyCompositeTiles.Clear();

        overallSignatureAcknowledgementRequested =
            false;

        fullCommittedBuildCount++;

        ClearTransitionFailureSuppression();

        SetStatus(
            TerrainAuthoringPreviewStatus.Ready,
            "The staged resident height cache was activated."
        );

        NotifyPreviewStateChanged();

        /*
         * Active ownership and binding are already coherent before this
         * event. SceneViewController can therefore apply its latest desired
         * layout immediately against the new active coverage.
         */
        NotifyHeightCacheCoverageIfChanged();

        if (
            previousActive != null
            &&
            previousActive !=
                activeCache
        )
        {
            previousActive.Dispose();
        }

        return true;
    }

    private static bool TryBindSpecificPreviewCache(
        TerrainAuthoringPreviewCache cache,
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            cache == null
            ||
            !cache.IsReady
            ||
            clipmapRoot == null
        )
        {
            errorMessage =
                "The requested preview cache cannot be bound because the cache or clipmap is unavailable.";

            return false;
        }

        TerrainClipmapBoundsController boundsController =
            clipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController == null)
        {
            errorMessage =
                "TerrainClipmapBoundsController is missing from WorldRoot/Clipmap.";

            return false;
        }

        using var bindingProfilerScope =
            WorldMeshesProfiler.PreviewBindCache.Auto();

        if (
            !TerrainHeightCacheBindingUtility
                .TryBind(
                    clipmapRoot,
                    cache.HeightCache,
                    cache.CacheOriginTile,
                    cache.CacheSize,
                    cache.SamplesPerSide,
                    cache.SampleSpacing,
                    cache.WorldSizeXZ,
                    out _,
                    out string bindingError
                )
        )
        {
            errorMessage =
                "The staged preview height cache could not be bound to the clipmap.\n\n" +
                bindingError;

            return false;
        }

        if (
            !boundsController.ApplyBoundsForRange(
                cache.MinimumHeight,
                cache.MaximumHeight
            )
        )
        {
            errorMessage =
                "The staged preview cache was bound, but its displacement bounds could not be applied.";

            return false;
        }

        boundClipmapRoot =
            clipmapRoot;

        diagnosticBindingApplyCount++;

        return true;
    }

    private static bool FailStagedTransition(
        TerrainAuthoringPreviewCacheTransition transition,
        TerrainHeightCacheWindow targetWindow,
        string targetCommittedSignature,
        string targetOverallSignature,
        string failureMessage
    )
    {
        string message =
            string.IsNullOrEmpty(
                failureMessage
            )
                ? "The staged height-cache transition failed."
                : failureMessage;

        if (transition != null)
        {
            transition.MarkFailed(
                message
            );

            currentTransition =
                transition;
        }

        ReleaseStagingCacheOnly();

        hasTransitionFailure =
            true;

        lastFailedTransitionWindow =
            targetWindow;

        lastTransitionFailureMessage =
            message;

        lastFailedCommittedSignature =
            targetCommittedSignature ?? "";

        lastFailedOverallSignature =
            targetOverallSignature ?? "";

        bool activeStillCurrent =
            activeCache != null
            &&
            activeCache.IsCompleteForActivation
            &&
            activeCache
                .SourceCommittedHeightfieldSignature
            ==
            targetCommittedSignature
            &&
            activeCache
                .SourceOverallAuthoringSignature
            ==
            targetOverallSignature;

        if (activeStillCurrent)
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Ready,
                "The requested staged height-cache transition failed. " +
                "The previous resident terrain preview remains active.\n\n" +
                message
            );
        }
        else
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Error,
                "The requested staged height-cache transition failed. " +
                "The previous GPU cache was preserved, but it does not " +
                "represent the current authoritative authoring state.\n\n" +
                message
            );
        }

        HeightCacheTransitionFailed?.Invoke(
            targetWindow,
            message
        );

        RepaintEditorViews();

        return false;
    }

    private static bool IsTransitionFailureSuppressed(
        TerrainHeightCacheWindow targetWindow,
        out string failureMessage
    )
    {
        failureMessage =
            "";

        if (!hasTransitionFailure)
        {
            return false;
        }

        if (
            lastFailedTransitionWindow !=
                targetWindow
        )
        {
            ClearTransitionFailureSuppression();

            return false;
        }

        WorldSettings worldSettings =
            LoadWorldSettings();

        TerrainAuthoringData authoringData =
            LoadAuthoringData();

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            ClearTransitionFailureSuppression();

            return false;
        }

        string committedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            committedSignature !=
                lastFailedCommittedSignature
            ||
            overallSignature !=
                lastFailedOverallSignature
        )
        {
            ClearTransitionFailureSuppression();

            return false;
        }

        failureMessage =
            lastTransitionFailureMessage;

        return true;
    }

    private static void ClearTransitionFailureSuppression()
    {
        hasTransitionFailure =
            false;

        lastFailedTransitionWindow =
            default;

        lastTransitionFailureMessage =
            "";

        lastFailedCommittedSignature =
            "";

        lastFailedOverallSignature =
            "";
    }

    private static void ReleaseStagingCacheOnly()
    {
        if (stagingCache == null)
        {
            return;
        }

        stagingCache.Dispose();

        stagingCache =
            null;
    }

    private static void ReleaseAllPreviewCaches(
        bool notifyObservers = true
    )
    {
        ReleaseStagingCacheOnly();

        if (activeCache != null)
        {
            activeCache.Dispose();

            activeCache =
                null;
        }

        currentTransition =
            null;

        ResetStreamingStateForResourceRelease(
            notifyObservers
        );

        ClearRegionalElevationResidencyForResourceRelease();

        ClearDesiredResidency();
        ClearTransitionFailureSuppression();

        if (notifyObservers)
        {
            NotifyHeightCacheCoverageIfChanged();

            NotifyPreviewStateChanged();
        }
    }
}
