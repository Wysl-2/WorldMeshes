using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

public enum TerrainAuthoringPreviewStreamingState
{
    Idle,
    Preparing,
    CopyingRetained,
    Loading,
    Composing,
    Finalizing,
    Activating,
    Failed
}

public static partial class TerrainAuthoringPreviewService
{
    public static event Action StreamingStateChanged;

    internal const int DefaultRetainedCopiesPerUpdate =
        8;

    internal const int DefaultCommittedLoadsPerUpdate =
        1;

    internal const int DefaultCompositionsPerUpdate =
        1;

    internal const double DefaultSoftWorkBudgetMilliseconds =
        4.0;

    private static TerrainAuthoringPreviewStreamingState
        streamingState =
            TerrainAuthoringPreviewStreamingState.Idle;

    private static string streamingStatusMessage =
        "Streaming is idle.";

    private static bool hasLatestRequiredResidencyWindow;

    private static TerrainHeightCacheWindow
        latestRequiredResidencyWindow;

    private static long streamingRequestGeneration;

    private static bool hasPendingStreamingStart;

    private static TerrainHeightCacheWindow
        pendingStreamingTarget;

    private static string pendingStreamingCommittedSignature =
        "";

    private static string pendingStreamingOverallSignature =
        "";

    private static bool pendingStreamingCommittedRebuild;

    private static long pendingStreamingGeneration;

    /*
     * Package 05 authoring-content generation captured when the queued
     * transition request was created. This is independent from the Package 04
     * residency request generation.
     */
    private static long pendingStreamingAuthoringGeneration;

    private static float streamingProgress;

    private static string lastStreamingFailureMessage =
        "";

    private static string lastStreamingCancellationReason =
        "";

    private static string lastPublishedStreamingSnapshot =
        "";

    public static TerrainAuthoringPreviewStreamingState StreamingState =>
        streamingState;

    public static string StreamingStateLabel
    {
        get
        {
            switch (streamingState)
            {
                case TerrainAuthoringPreviewStreamingState.CopyingRetained:
                    return "Copying Retained";

                case TerrainAuthoringPreviewStreamingState.Loading:
                    return "Loading";

                case TerrainAuthoringPreviewStreamingState.Composing:
                    return "Composing";

                case TerrainAuthoringPreviewStreamingState.Finalizing:
                    return "Finalizing";

                case TerrainAuthoringPreviewStreamingState.Activating:
                    return "Activating";

                case TerrainAuthoringPreviewStreamingState.Preparing:
                    return "Preparing";

                case TerrainAuthoringPreviewStreamingState.Failed:
                    return "Failed";

                default:
                    return "Idle";
            }
        }
    }

    public static string StreamingStatusMessage =>
        streamingStatusMessage;

    public static bool IsStreaming =>
        streamingState !=
            TerrainAuthoringPreviewStreamingState.Idle
        &&
        streamingState !=
            TerrainAuthoringPreviewStreamingState.Failed;

    public static bool IsWaitingForStreamingCoverage
    {
        get
        {
            if (
                !Enabled
                ||
                !hasLatestRequiredResidencyWindow
            )
            {
                return false;
            }

            return
                !ActiveCacheContains(
                    latestRequiredResidencyWindow
                );
        }
    }

    public static float StreamingProgress =>
        Mathf.Clamp01(
            streamingProgress
        );

    public static long StreamingRequestGeneration =>
        streamingRequestGeneration;

    public static int StreamingRetainedTileCount =>
        currentTransition != null
            ? currentTransition.ReusableRetainedTiles.Count
            : 0;

    public static int StreamingRetainedCopiedCount =>
        currentTransition != null
            ? currentTransition.RetainedGpuCopyCount
            : 0;

    public static int StreamingSourceTileCount =>
        currentTransition != null
            ? currentTransition.SourceMaterializationTiles.Count
            : 0;

    public static int StreamingSourceLoadedCount =>
        currentTransition != null
            ? currentTransition.CommittedSourceLoadCount
            : 0;

    public static int StreamingSourceComposedCount =>
        currentTransition != null
            ? currentTransition.FullyComposedTileCount
            : 0;

    public static int StreamingRetainedCopiesPerUpdate =>
        DefaultRetainedCopiesPerUpdate;

    public static int StreamingCommittedLoadsPerUpdate =>
        DefaultCommittedLoadsPerUpdate;

    public static int StreamingCompositionsPerUpdate =>
        DefaultCompositionsPerUpdate;

    public static double StreamingSoftWorkBudgetMilliseconds =>
        DefaultSoftWorkBudgetMilliseconds;

    public static string StreamingCoverageLabel
    {
        get
        {
            if (!TryGetActiveResidentWindow(out _))
            {
                return "No Active Cache";
            }

            return
                IsWaitingForStreamingCoverage
                    ? "Waiting For Destination"
                    : "Active Safe";
        }
    }

    public static bool TryGetLatestRequiredResidentWindow(
        out TerrainHeightCacheWindow window
    )
    {
        window =
            hasLatestRequiredResidencyWindow
                ? latestRequiredResidencyWindow
                : default;

        return
            hasLatestRequiredResidencyWindow
            &&
            window.IsValid;
    }

    internal static bool RecordStreamingResidencyIntent(
        TerrainHeightCacheWindow requiredWindow,
        TerrainHeightCacheWindow desiredWindow
    )
    {
        bool changed =
            !TerrainAuthoringPreviewStreamingPolicy
                .IsSameResidencyIntent(
                    hasLatestRequiredResidencyWindow,
                    latestRequiredResidencyWindow,
                    hasDesiredResidencyWindow,
                    desiredResidencyWindow,
                    requiredWindow,
                    desiredWindow
                );

        hasLatestRequiredResidencyWindow =
            true;

        latestRequiredResidencyWindow =
            requiredWindow;

        hasDesiredResidencyWindow =
            true;

        desiredResidencyWindow =
            desiredWindow;

        if (changed)
        {
            streamingRequestGeneration++;

            if (
                streamingState ==
                    TerrainAuthoringPreviewStreamingState.Failed
            )
            {
                lastStreamingFailureMessage =
                    "";

                streamingState =
                    TerrainAuthoringPreviewStreamingState.Idle;
            }
        }

        PublishStreamingStateIfChanged();

        return changed;
    }

    internal static void NotifyStreamingIntentNoLongerRequiresTarget()
    {
        /*
         * A prefetch may be queued by ExecuteRefresh and become unnecessary
         * before the next EditorApplication.update. Drop that pending work
         * immediately, but never discard an explicit committed rebuild.
         */
        if (
            hasPendingStreamingStart
            &&
            !pendingStreamingCommittedRebuild
        )
        {
            ClearPendingStreamingStart();

            streamingProgress =
                0f;

            if (
                currentTransition == null
                ||
                !TransitionInProgress
            )
            {
                streamingState =
                    TerrainAuthoringPreviewStreamingState.Idle;

                streamingStatusMessage =
                    "Queued prefetch was cancelled because active residency is comfortably sufficient.";
            }
        }

        PublishStreamingStateIfChanged();
    }

    private static bool RequestIncrementalStagedTransition(
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

        if (
            worldSettings == null
            ||
            authoringData == null
            ||
            clipmapRoot == null
            ||
            !targetWindow.IsValid
            ||
            string.IsNullOrEmpty(
                currentCommittedSignature
            )
            ||
            string.IsNullOrEmpty(
                currentOverallSignature
            )
        )
        {
            errorMessage =
                "The incremental staging request is missing required state.";

            return false;
        }

        if (
            currentTransition != null
            &&
            TransitionInProgress
        )
        {
            bool sameTransaction =
                currentTransition.TargetWindow ==
                    targetWindow
                &&
                currentTransition
                    .TargetCommittedHeightfieldSignature
                ==
                currentCommittedSignature
                &&
                currentTransition
                    .TargetOverallAuthoringSignature
                ==
                currentOverallSignature
                &&
                currentTransition.CommittedRebuildRequested ==
                    committedRebuildRequested
                &&
                currentTransition.TargetAuthoringGeneration ==
                    authoringGeneration;

            if (sameTransaction)
            {
                PublishStreamingStateIfChanged();

                return true;
            }

            if (
                IsCurrentStreamingTransitionUsefulForLatestIntent()
                &&
                currentTransition
                    .TargetCommittedHeightfieldSignature
                ==
                currentCommittedSignature
                &&
                currentTransition
                    .TargetOverallAuthoringSignature
                ==
                currentOverallSignature
                &&
                currentTransition.CommittedRebuildRequested ==
                    committedRebuildRequested
                &&
                currentTransition.TargetAuthoringGeneration ==
                    authoringGeneration
            )
            {
                return true;
            }

            CancelCurrentStreamingTransition(
                "A newer residency request superseded the current staging target.",
                false
            );
        }

        bool duplicatePending =
            hasPendingStreamingStart
            &&
            pendingStreamingTarget ==
                targetWindow
            &&
            pendingStreamingCommittedSignature ==
                currentCommittedSignature
            &&
            pendingStreamingOverallSignature ==
                currentOverallSignature
            &&
            pendingStreamingCommittedRebuild ==
                committedRebuildRequested
            &&
            pendingStreamingAuthoringGeneration ==
                authoringGeneration;

        if (duplicatePending)
        {
            return true;
        }

        hasPendingStreamingStart =
            true;

        pendingStreamingTarget =
            targetWindow;

        pendingStreamingCommittedSignature =
            currentCommittedSignature;

        pendingStreamingOverallSignature =
            currentOverallSignature;

        pendingStreamingCommittedRebuild =
            committedRebuildRequested;

        pendingStreamingGeneration =
            streamingRequestGeneration;

        pendingStreamingAuthoringGeneration =
            authoringGeneration;

        streamingProgress =
            0f;

        lastStreamingFailureMessage =
            "";

        lastStreamingCancellationReason =
            "";

        if (
            activeCache != null
            &&
            activeCache.IsReady
        )
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Ready,
                "The active resident preview remains ready while the replacement cache streams incrementally."
            );
        }
        else
        {
            SetStatus(
                TerrainAuthoringPreviewStatus.Preparing,
                "The initial resident height cache is streaming incrementally."
            );
        }

        SetStreamingState(
            TerrainAuthoringPreviewStreamingState.Preparing,
            $"Queued incremental staging for {targetWindow}."
        );

        return true;
    }

    private static void OnStreamingEditorUpdate()
    {
        if (
            !Enabled
            ||
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
            ||
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            return;
        }

        if (
            currentTransition != null
            &&
            TransitionInProgress
        )
        {
            if (
                !ValidateCurrentStreamingTransaction(
                    out string staleReason
                )
            )
            {
                CancelCurrentStreamingTransition(
                    staleReason,
                    false
                );

                ScheduleRefresh();

                return;
            }

            if (!IsCurrentStreamingTransitionUsefulForLatestIntent())
            {
                CancelCurrentStreamingTransition(
                    "The current staging target is no longer useful for the latest residency intent.",
                    false
                );

                ScheduleRefresh();

                return;
            }

            AdvanceCurrentStreamingTransaction();

            return;
        }

        if (hasPendingStreamingStart)
        {
            if (
                ShouldDeferStreamingRestart(
                    TerrainAuthoringModifierService
                        .HasActiveInteractiveEdit
                )
            )
            {
                SetStreamingState(
                    TerrainAuthoringPreviewStreamingState.Preparing,
                    "Streaming restart is deferred while an interactive terrain modifier edit is active."
                );

                return;
            }

            BeginPendingStreamingTransition();

            return;
        }

        if (
            streamingState !=
                TerrainAuthoringPreviewStreamingState.Failed
        )
        {
            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Idle,
                "Streaming is idle."
            );
        }
    }

    private static void BeginPendingStreamingTransition()
    {
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
            FailStreamingTransition(
                null,
                pendingStreamingTarget,
                pendingStreamingCommittedSignature,
                pendingStreamingOverallSignature,
                "The incremental staging worker could not load current authoring state."
            );

            return;
        }

        string currentCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string currentOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            currentCommittedSignature !=
                pendingStreamingCommittedSignature
            ||
            currentOverallSignature !=
                pendingStreamingOverallSignature
            ||
            pendingStreamingAuthoringGeneration !=
                authoringGeneration
        )
        {
            ClearPendingStreamingStart();

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Idle,
                "The queued staging request became stale before preparation began."
            );

            ScheduleRefresh();

            return;
        }

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
                    pendingStreamingTarget,
                    currentCommittedSignature,
                    currentOverallSignature,
                    out TerrainAuthoringPreviewCacheTransition transition,
                    out string transitionError
                )
        )
        {
            FailStreamingTransition(
                transition,
                pendingStreamingTarget,
                currentCommittedSignature,
                currentOverallSignature,
                transitionError
            );

            return;
        }

        transition.RequestGeneration =
            pendingStreamingGeneration;

        transition.TargetAuthoringGeneration =
            pendingStreamingAuthoringGeneration;

        transition.CommittedRebuildRequested =
            pendingStreamingCommittedRebuild;

        transition.SetState(
            TerrainAuthoringPreviewTransitionState.Preparing
        );

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
                transition.TargetWindow,
                out string stagingError
            )
        )
        {
            FailStreamingTransition(
                transition,
                transition.TargetWindow,
                currentCommittedSignature,
                currentOverallSignature,
                stagingError
            );

            return;
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
                transition.CommittedRebuildRequested
            );

        foreach (
            Vector2Int retainedTile
            in transition.RetainedTiles
        )
        {
            if (
                retainedReuseAllowed
                &&
                activeCache != null
                &&
                activeCache.IsSliceFinalCompositeReady(
                    retainedTile
                )
            )
            {
                transition.AddReusableRetainedTile(
                    retainedTile
                );
            }
            else
            {
                transition.AddSourceMaterializationTile(
                    retainedTile
                );
            }
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

        transition.ResetStreamingExecutionState();

        transition.TotalWorkUnits =
            1
            +
            transition.ReusableRetainedTiles.Count
            +
            transition.SourceMaterializationTiles.Count
            +
            transition.SourceMaterializationTiles.Count
            +
            1
            +
            1;

        transition.CompletedWorkUnits =
            1;

        heightCompositor
            .BeginTransactionDiagnostics();

        ClearPendingStreamingStart();

        if (
            transition.ReusableRetainedTiles.Count >
                0
        )
        {
            transition.SetState(
                TerrainAuthoringPreviewTransitionState.CopyingRetained
            );

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.CopyingRetained,
                "Copying reusable retained final-composite slices."
            );
        }
        else if (
            transition.SourceMaterializationTiles.Count >
                0
        )
        {
            transition.SetState(
                TerrainAuthoringPreviewTransitionState.LoadingSourceTiles
            );

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Loading,
                "Loading authoritative committed source tiles."
            );
        }
        else
        {
            transition.SetState(
                TerrainAuthoringPreviewTransitionState.Finalizing
            );

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Finalizing,
                "Finalizing the completed staging cache."
            );
        }

        UpdateStreamingProgress();
    }

    private static void AdvanceCurrentStreamingTransaction()
    {
        if (
            currentTransition == null
            ||
            stagingCache == null
        )
        {
            return;
        }

        switch (currentTransition.State)
        {
            case TerrainAuthoringPreviewTransitionState.CopyingRetained:
                AdvanceRetainedCopies();
                return;

            case TerrainAuthoringPreviewTransitionState.LoadingSourceTiles:
                AdvanceCommittedLoads();
                return;

            case TerrainAuthoringPreviewTransitionState.ComposingSourceTiles:
                AdvanceCompositions();
                return;

            case TerrainAuthoringPreviewTransitionState.Finalizing:
                FinalizeCurrentStreamingTransition();
                return;

            case TerrainAuthoringPreviewTransitionState.ReadyToActivate:
                ActivateCurrentStreamingTransition();
                return;
        }
    }

    private static void AdvanceRetainedCopies()
    {
        int count =
            currentTransition
                .ReusableRetainedTiles
                .Count;

        int endIndex =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    currentTransition.RetainedCopyCursor,
                    count,
                    DefaultRetainedCopiesPerUpdate
                );

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        int processed =
            0;

        while (
            currentTransition.RetainedCopyCursor <
                endIndex
        )
        {
            Vector2Int tile =
                currentTransition
                    .ReusableRetainedTiles[
                        currentTransition.RetainedCopyCursor
                    ];

            if (activeCache == null)
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    "The retained-copy source cache became unavailable."
                );

                return;
            }

            if (
                !stagingCache.TryCopyFinalCompositeTileFrom(
                    activeCache,
                    tile,
                    out string copyError
                )
            )
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    copyError
                );

                return;
            }

            currentTransition.RetainedCopyCursor++;
            currentTransition.RetainedGpuCopyCount++;
            currentTransition.CompletedWorkUnits++;
            processed++;

            if (
                processed > 0
                &&
                stopwatch.Elapsed.TotalMilliseconds >=
                    DefaultSoftWorkBudgetMilliseconds
            )
            {
                break;
            }
        }

        if (
            currentTransition.RetainedCopyCursor >=
                count
        )
        {
            if (
                currentTransition.SourceMaterializationTiles.Count >
                    0
            )
            {
                currentTransition.SetState(
                    TerrainAuthoringPreviewTransitionState.LoadingSourceTiles
                );

                SetStreamingState(
                    TerrainAuthoringPreviewStreamingState.Loading,
                    "Loading authoritative committed source tiles."
                );
            }
            else
            {
                currentTransition.SetState(
                    TerrainAuthoringPreviewTransitionState.Finalizing
                );

                SetStreamingState(
                    TerrainAuthoringPreviewStreamingState.Finalizing,
                    "Finalizing the completed staging cache."
                );
            }
        }

        UpdateStreamingProgress();
    }

    private static void AdvanceCommittedLoads()
    {
        int count =
            currentTransition
                .SourceMaterializationTiles
                .Count;

        int endIndex =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    currentTransition.CommittedLoadCursor,
                    count,
                    DefaultCommittedLoadsPerUpdate
                );

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        int processed =
            0;

        while (
            currentTransition.CommittedLoadCursor <
                endIndex
        )
        {
            Vector2Int tile =
                currentTransition
                    .SourceMaterializationTiles[
                        currentTransition.CommittedLoadCursor
                    ];

            if (
                !stagingCache.TryLoadCommittedBaseTile(
                    tile,
                    out string loadError
                )
            )
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    loadError
                );

                return;
            }

            currentTransition.CommittedLoadCursor++;
            currentTransition.CommittedSourceLoadCount++;
            currentTransition.CompletedWorkUnits++;
            processed++;

            if (
                processed > 0
                &&
                stopwatch.Elapsed.TotalMilliseconds >=
                    DefaultSoftWorkBudgetMilliseconds
            )
            {
                break;
            }
        }

        if (
            currentTransition.CommittedLoadCursor >=
                count
        )
        {
            currentTransition.SetState(
                TerrainAuthoringPreviewTransitionState.ComposingSourceTiles
            );

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Composing,
                "Composing loaded source tiles into final staging slices."
            );
        }

        UpdateStreamingProgress();
    }

    private static void AdvanceCompositions()
    {
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
            FailStreamingTransition(
                currentTransition,
                currentTransition.TargetWindow,
                currentTransition.TargetCommittedHeightfieldSignature,
                currentTransition.TargetOverallAuthoringSignature,
                "The streaming compositor could not load current authoring state."
            );

            return;
        }

        int count =
            currentTransition
                .SourceMaterializationTiles
                .Count;

        int endIndex =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    currentTransition.CompositionCursor,
                    count,
                    DefaultCompositionsPerUpdate
                );

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        int processed =
            0;

        while (
            currentTransition.CompositionCursor <
                endIndex
        )
        {
            Vector2Int tile =
                currentTransition
                    .SourceMaterializationTiles[
                        currentTransition.CompositionCursor
                    ];

            if (
                !stagingCache.TryGetCommittedRange(
                    tile,
                    out float baseMinimum,
                    out float baseMaximum,
                    out string rangeError
                )
            )
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    rangeError
                );

                return;
            }

            int stagingSlice =
                stagingCache.GetSliceIndex(
                    tile.x,
                    tile.y
                );

            if (stagingSlice < 0)
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    $"Staging tile ({tile.x}, {tile.y}) could not resolve a local slice."
                );

                return;
            }

            if (
                !heightCompositor.TryComposeTile(
                    stagingCache.HeightCache,
                    tile,
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
                    out string compositorError
                )
            )
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    compositorError
                );

                return;
            }

            if (
                !stagingCache.TryCommitFinalCompositeTile(
                    tile,
                    compositeMinimum,
                    compositeMaximum,
                    out string commitError
                )
            )
            {
                FailStreamingTransition(
                    currentTransition,
                    currentTransition.TargetWindow,
                    currentTransition.TargetCommittedHeightfieldSignature,
                    currentTransition.TargetOverallAuthoringSignature,
                    commitError
                );

                return;
            }

            currentTransition.CompositionCursor++;
            currentTransition.FullyComposedTileCount++;
            currentTransition.CompletedWorkUnits++;
            processed++;

            if (
                processed > 0
                &&
                stopwatch.Elapsed.TotalMilliseconds >=
                    DefaultSoftWorkBudgetMilliseconds
            )
            {
                break;
            }
        }

        if (
            currentTransition.CompositionCursor >=
                count
        )
        {
            currentTransition.SetState(
                TerrainAuthoringPreviewTransitionState.Finalizing
            );

            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Finalizing,
                "Finalizing the completed staging cache."
            );
        }

        UpdateStreamingProgress();
    }

    private static void FinalizeCurrentStreamingTransition()
    {
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
            FailStreamingTransition(
                currentTransition,
                currentTransition.TargetWindow,
                currentTransition.TargetCommittedHeightfieldSignature,
                currentTransition.TargetOverallAuthoringSignature,
                "The staging transaction could not load current state for finalization."
            );

            return;
        }

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
                currentTransition.TargetCommittedHeightfieldSignature
            ||
            latestOverallSignature !=
                currentTransition.TargetOverallAuthoringSignature
            ||
            currentTransition.TargetAuthoringGeneration !=
                authoringGeneration
        )
        {
            CancelCurrentStreamingTransition(
                "Authoring state changed before staging finalization.",
                false
            );

            ScheduleRefresh();

            return;
        }

        if (
            !stagingCache.TryFinalizeStagingForActivation(
                latestOverallSignature,
                out string finalizationError
            )
        )
        {
            FailStreamingTransition(
                currentTransition,
                currentTransition.TargetWindow,
                latestCommittedSignature,
                latestOverallSignature,
                finalizationError
            );

            return;
        }

        currentTransition.CompletedWorkUnits++;

        currentTransition.SetState(
            TerrainAuthoringPreviewTransitionState.ReadyToActivate
        );

        SetStreamingState(
            TerrainAuthoringPreviewStreamingState.Finalizing,
            "Staging is complete and ready for atomic activation."
        );

        UpdateStreamingProgress();
    }

    private static void ActivateCurrentStreamingTransition()
    {
        if (
            currentTransition == null
            ||
            currentTransition.TargetAuthoringGeneration !=
                authoringGeneration
        )
        {
            CancelCurrentStreamingTransition(
                "Authoring generation changed before staging activation.",
                false
            );

            ScheduleRefresh();

            return;
        }

        if (
            !TerrainWorldSceneUtility.TryFindActiveClipmapRoot(
                out Transform clipmapRoot,
                out string sceneLookupError
            )
            ||
            clipmapRoot == null
        )
        {
            FailStreamingTransition(
                currentTransition,
                currentTransition.TargetWindow,
                currentTransition.TargetCommittedHeightfieldSignature,
                currentTransition.TargetOverallAuthoringSignature,
                string.IsNullOrEmpty(
                    sceneLookupError
                )
                    ? "WorldRoot/Clipmap is unavailable for staging activation."
                    : sceneLookupError
            );

            return;
        }

        SetStreamingState(
            TerrainAuthoringPreviewStreamingState.Activating,
            "Atomically activating the completed staging cache."
        );

        TerrainAuthoringPreviewCacheTransition transition =
            currentTransition;

        if (
            !TryActivateStagingCache(
                clipmapRoot,
                transition,
                transition.TargetCommittedHeightfieldSignature,
                transition.TargetOverallAuthoringSignature,
                out string activationError
            )
        )
        {
            streamingState =
                TerrainAuthoringPreviewStreamingState.Failed;

            lastStreamingFailureMessage =
                activationError;

            streamingStatusMessage =
                string.IsNullOrEmpty(
                    activationError
                )
                    ? "Incremental staging activation failed."
                    : activationError;

            PublishStreamingStateIfChanged();

            return;
        }

        MarkActiveCacheAuthoringGeneration(
            transition.TargetAuthoringGeneration
        );

        transition.CompletedWorkUnits++;

        streamingProgress =
            1f;

        lastStreamingFailureMessage =
            "";

        SetStreamingState(
            TerrainAuthoringPreviewStreamingState.Idle,
            "The latest streamed resident cache was activated."
        );

        ScheduleRefresh();
    }

    private static bool ValidateCurrentStreamingTransaction(
        out string staleReason
    )
    {
        staleReason =
            "";

        if (currentTransition == null)
        {
            staleReason =
                "No current streaming transaction exists.";

            return false;
        }

        if (
            currentTransition.TargetAuthoringGeneration !=
                authoringGeneration
        )
        {
            staleReason =
                "Authoring generation changed while staging was in progress.";

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
            staleReason =
                "Current world or authoring data became unavailable.";

            return false;
        }

        Vector2Int worldGridSize =
            new Vector2Int(
                worldSettings.HeightTileGridWidth,
                worldSettings.HeightTileGridHeight
            );

        if (
            !TerrainAuthoringPreviewResidencyPolicy
                .IsWindowInsideWorldGrid(
                    currentTransition.TargetWindow,
                    worldGridSize
                )
        )
        {
            staleReason =
                "The current staging target is no longer inside the logical world grid.";

            return false;
        }

        string currentCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string currentOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            currentCommittedSignature !=
                currentTransition.TargetCommittedHeightfieldSignature
            ||
            currentOverallSignature !=
                currentTransition.TargetOverallAuthoringSignature
        )
        {
            staleReason =
                "Authoring signatures changed while staging was in progress.";

            return false;
        }

        if (currentTransition.HasSourceWindow)
        {
            if (
                activeCache == null
                ||
                !activeCache.IsReady
                ||
                activeCache.CacheOriginTile !=
                    currentTransition.SourceWindow.OriginTile
                ||
                activeCache.CacheSize !=
                    currentTransition.SourceWindow.Size
                ||
                activeCache.HeightCache == null
                ||
                activeCache.HeightCache.GetInstanceID() !=
                    currentTransition.SourceTextureInstanceId
            )
            {
                staleReason =
                    "The active source cache changed while staging was in progress.";

                return false;
            }
        }

        return true;
    }

    private static bool IsCurrentStreamingTransitionUsefulForLatestIntent()
    {
        if (currentTransition == null)
        {
            return false;
        }

        if (
            !hasLatestRequiredResidencyWindow
            ||
            !hasDesiredResidencyWindow
        )
        {
            return true;
        }

        if (
            !TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    currentTransition.TargetWindow,
                    latestRequiredResidencyWindow,
                    desiredResidencyWindow
                )
        )
        {
            return false;
        }

        /*
         * An explicit/committed rebuild must keep rebuilding once its target
         * still serves the newest residency intent. It may still be cancelled
         * above when rapid navigation makes that target obsolete.
         */
        if (currentTransition.CommittedRebuildRequested)
        {
            return true;
        }

        WorldSettings worldSettings =
            LoadWorldSettings();

        if (worldSettings == null)
        {
            return true;
        }

        if (
            TryGetActiveResidentWindow(
                out TerrainHeightCacheWindow activeWindow
            )
            &&
            TerrainAuthoringPreviewStreamingPolicy
                .IsActiveComfortablySufficient(
                    activeWindow,
                    latestRequiredResidencyWindow,
                    desiredResidencyWindow,
                    new Vector2Int(
                        worldSettings.HeightTileGridWidth,
                        worldSettings.HeightTileGridHeight
                    )
                )
        )
        {
            return false;
        }

        return true;
    }

    private static void CancelCurrentStreamingTransition(
        string reason,
        bool clearIntent
    )
    {
        string cancellationReason =
            string.IsNullOrEmpty(
                reason
            )
                ? "The incremental staging transaction was cancelled."
                : reason;

        if (currentTransition != null)
        {
            currentTransition.MarkCancelled(
                cancellationReason
            );
        }

        lastStreamingCancellationReason =
            cancellationReason;

        ReleaseStagingCacheOnly();

        currentTransition =
            null;

        ClearPendingStreamingStart();

        if (clearIntent)
        {
            hasLatestRequiredResidencyWindow =
                false;

            latestRequiredResidencyWindow =
                default;
        }

        streamingProgress =
            0f;

        if (
            streamingState !=
                TerrainAuthoringPreviewStreamingState.Failed
        )
        {
            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Idle,
                cancellationReason
            );
        }
        else
        {
            PublishStreamingStateIfChanged();
        }
    }

    private static bool FailStreamingTransition(
        TerrainAuthoringPreviewCacheTransition transition,
        TerrainHeightCacheWindow targetWindow,
        string committedSignature,
        string overallSignature,
        string failureMessage
    )
    {
        ClearPendingStreamingStart();

        bool result =
            FailStagedTransition(
                transition,
                targetWindow,
                committedSignature,
                overallSignature,
                failureMessage
            );

        lastStreamingFailureMessage =
            string.IsNullOrEmpty(
                failureMessage
            )
                ? "Incremental staging failed."
                : failureMessage;

        streamingState =
            TerrainAuthoringPreviewStreamingState.Failed;

        streamingStatusMessage =
            lastStreamingFailureMessage;

        PublishStreamingStateIfChanged();

        return result;
    }

    private static void ClearPendingStreamingStart()
    {
        hasPendingStreamingStart =
            false;

        pendingStreamingTarget =
            default;

        pendingStreamingCommittedSignature =
            "";

        pendingStreamingOverallSignature =
            "";

        pendingStreamingCommittedRebuild =
            false;

        pendingStreamingGeneration =
            0L;

        pendingStreamingAuthoringGeneration =
            0L;
    }

    private static void ResetStreamingStateForResourceRelease()
    {
        if (currentTransition != null)
        {
            currentTransition.MarkCancelled(
                "Preview resources were released."
            );
        }

        ClearPendingStreamingStart();

        hasLatestRequiredResidencyWindow =
            false;

        latestRequiredResidencyWindow =
            default;

        streamingState =
            TerrainAuthoringPreviewStreamingState.Idle;

        streamingStatusMessage =
            "Streaming is idle.";

        streamingProgress =
            0f;

        lastStreamingFailureMessage =
            "";

        lastStreamingCancellationReason =
            "";

        lastPublishedStreamingSnapshot =
            "";

        PublishStreamingStateIfChanged();
    }

    private static void SetStreamingState(
        TerrainAuthoringPreviewStreamingState newState,
        string message
    )
    {
        streamingState =
            newState;

        streamingStatusMessage =
            string.IsNullOrEmpty(
                message
            )
                ? ""
                : message;

        PublishStreamingStateIfChanged();
    }

    private static void UpdateStreamingProgress()
    {
        if (
            currentTransition == null
            ||
            currentTransition.TotalWorkUnits <= 0
        )
        {
            streamingProgress =
                0f;
        }
        else
        {
            streamingProgress =
                Mathf.Clamp01(
                    (float)currentTransition.CompletedWorkUnits /
                    currentTransition.TotalWorkUnits
                );
        }

        PublishStreamingStateIfChanged();
    }

    private static void PublishStreamingStateIfChanged()
    {
        string target =
            currentTransition != null
                ? currentTransition.TargetWindow.ToString()
                : hasPendingStreamingStart
                    ? pendingStreamingTarget.ToString()
                    : "(none)";

        string snapshot =
            streamingState.ToString()
            + "|"
            + target
            + "|"
            + StreamingRetainedCopiedCount
            + "/"
            + StreamingRetainedTileCount
            + "|"
            + StreamingSourceLoadedCount
            + "/"
            + StreamingSourceTileCount
            + "|"
            + StreamingSourceComposedCount
            + "/"
            + StreamingSourceTileCount
            + "|"
            + IsWaitingForStreamingCoverage
            + "|"
            + streamingRequestGeneration
            + "|"
            + lastStreamingFailureMessage;

        if (
            snapshot ==
                lastPublishedStreamingSnapshot
        )
        {
            return;
        }

        lastPublishedStreamingSnapshot =
            snapshot;

        StreamingStateChanged?.Invoke();

        RepaintEditorViews();
    }
}
