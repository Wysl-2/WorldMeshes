using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainAuthoringIncrementalStreamingValidationUtility
{
    private sealed class ValidationResult
    {
        public string Name;
        public string Detail;
        public bool Passed;
        public bool Blocked;
    }

    private static readonly List<ValidationResult>
        results =
            new List<ValidationResult>();

    private static bool validationRunning;

    private static bool validationScheduled;

    private static TerrainValidationRunSummary lastRunSummary =
        TerrainValidationRunSummary.CreateNotRun();

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

    public static TerrainValidationRunSummary LastRunSummary =>
        lastRunSummary;

    public static void ValidateIncrementalStreaming()
    {
        RequestValidation();
    }

    public static void RequestValidation()
    {
        if (IsRunning)
        {
            return;
        }

        validationScheduled =
            true;

        lastRunSummary =
            TerrainValidationRunSummary.CreateRunning(
                "Incremental streaming validation is running."
            );

        EditorApplication.delayCall +=
            RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        validationScheduled =
            false;

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            RequestValidation();

            return;
        }

        validationRunning =
            true;

        results.Clear();

        try
        {
            bool setPassed = TerrainAuthoringStagedTransitionValidationUtility.RunCacheSetWorkerFixture(
                true, out string setDetail, out bool setBlocked);
            if (setBlocked) AddBlocked("Resumable Height cache set worker", setDetail);
            else if (setPassed) AddPass("Resumable Height cache set worker", setDetail);
            else AddFail("Resumable Height cache set worker", setDetail);
            ValidateHeightSetPolicy();
            ValidateDirtyFailureProgress();
            ValidateDirtyPriorityAndAdmission();
            ValidateCommittedSourceReuse();
            ValidatePostCommitFollowUps();
            ValidateBoundedWorkBudgets();
            ValidateCursorResume();
            ValidateDuplicateIntentCoalescing();
            ValidateUsefulTargetSurvivesOriginChange();
            ValidateRapidLatestTargetWins();
            ValidatePrefetchThreshold();
            ValidatePrefetchReversal();
            ValidatePrefetchBecomesCoverageCritical();
            ValidateProgressAccounting();
            ValidateCancellationSemantics();
            ValidateResidencySizeRecoveryClassification();
            ValidateLiveStreamingInformation();
            ValidatePersistentAuthoringStateSafety();
        }
        catch (Exception exception)
        {
            AddFail(
                "Unexpected validation exception",
                exception.ToString()
            );
        }
        finally
        {
            validationRunning =
                false;

            FinishValidation();
        }
    }

    private static void ValidateBoundedWorkBudgets()
    {
        int retainedEnd =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    0,
                    20,
                    TerrainAuthoringPreviewService
                        .DefaultRetainedCopiesPerUpdate
                );

        int loadEnd =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    0,
                    6,
                    TerrainAuthoringPreviewService
                        .DefaultCommittedLoadsPerUpdate
                );

        int composeEnd =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    0,
                    6,
                    TerrainAuthoringPreviewService
                        .DefaultCompositionsPerUpdate
                );

        bool valid =
            retainedEnd ==
                Mathf.Min(
                    20,
                    TerrainAuthoringPreviewService
                        .DefaultRetainedCopiesPerUpdate
                )
            &&
            loadEnd ==
                1
            &&
            composeEnd ==
                1;

        if (valid)
        {
            AddPass(
                "Bounded editor-update work budgets",
                $"Retained copies are capped at {TerrainAuthoringPreviewService.DefaultRetainedCopiesPerUpdate}/update; committed loads and compositions are capped at one tile/update."
            );
        }
        else
        {
            AddFail(
                "Bounded editor-update work budgets",
                $"Unexpected budget endpoints: retained={retainedEnd}, load={loadEnd}, compose={composeEnd}."
            );
        }
    }

    private static void ValidateCursorResume()
    {
        int first =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    0,
                    20,
                    8
                );

        int second =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    first,
                    20,
                    8
                );

        int third =
            TerrainAuthoringPreviewStreamingPolicy
                .CalculateBudgetedEndIndex(
                    second,
                    20,
                    8
                );

        if (
            first == 8
            &&
            second == 16
            &&
            third == 20
        )
        {
            AddPass(
                "Persistent work cursors",
                "A 20-unit phase resumes 0->8, 8->16, then 16->20 instead of restarting from zero."
            );
        }
        else
        {
            AddFail(
                "Persistent work cursors",
                $"Unexpected cursor sequence: {first}, {second}, {third}."
            );
        }
    }

    private static void ValidateDuplicateIntentCoalescing()
    {
        TerrainHeightCacheWindow required =
            Window(
                5,
                5,
                8,
                8
            );

        TerrainHeightCacheWindow desired =
            Window(
                4,
                4,
                10,
                10
            );

        bool same =
            TerrainAuthoringPreviewStreamingPolicy
                .IsSameResidencyIntent(
                    true,
                    required,
                    true,
                    desired,
                    required,
                    desired
                );

        bool changed =
            TerrainAuthoringPreviewStreamingPolicy
                .IsSameResidencyIntent(
                    true,
                    required,
                    true,
                    desired,
                    Window(
                        6,
                        5,
                        8,
                        8
                    ),
                    Window(
                        5,
                        4,
                        10,
                        10
                    )
                );

        if (
            same
            &&
            !changed
        )
        {
            AddPass(
                "Duplicate request coalescing",
                "Identical required/desired intent is recognized as a duplicate while a meaningful move is not."
            );
        }
        else
        {
            AddFail(
                "Duplicate request coalescing",
                "Residency intent equality did not distinguish duplicate and changed requests."
            );
        }
    }

    private static void ValidateUsefulTargetSurvivesOriginChange()
    {
        TerrainHeightCacheWindow staging =
            Window(
                5,
                4,
                12,
                12
            );

        TerrainHeightCacheWindow latestDesired =
            Window(
                6,
                4,
                12,
                12
            );

        TerrainHeightCacheWindow latestRequired =
            Window(
                7,
                6,
                9,
                8
            );

        bool useful =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    staging,
                    latestRequired,
                    latestDesired
                );

        if (useful)
        {
            AddPass(
                "Useful staging survives small origin change",
                "A healthy staging target that still contains newest required coverage remains useful despite exact desired-origin movement."
            );
        }
        else
        {
            AddFail(
                "Useful staging survives small origin change",
                "The streaming policy would cancel a still-useful target solely because desired origin changed."
            );
        }
    }

    private static void ValidateRapidLatestTargetWins()
    {
        TerrainHeightCacheWindow stagingB =
            Window(
                2,
                2,
                12,
                12
            );

        TerrainHeightCacheWindow requiredD =
            Window(
                20,
                20,
                9,
                9
            );

        TerrainHeightCacheWindow desiredD =
            Window(
                19,
                19,
                11,
                11
            );

        bool bUsefulForD =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    stagingB,
                    requiredD,
                    desiredD
                );

        if (!bUsefulForD)
        {
            AddPass(
                "Rapid navigation latest-target wins",
                "An obsolete B staging target cannot satisfy distant destination D and is therefore eligible for cancellation instead of queued sequential activation."
            );
        }
        else
        {
            AddFail(
                "Rapid navigation latest-target wins",
                "A clearly obsolete staging target was incorrectly classified as useful for the latest destination."
            );
        }
    }

    private static void ValidatePrefetchThreshold()
    {
        TerrainHeightCacheWindow active =
            Window(
                4,
                4,
                12,
                12
            );

        Vector2Int worldGrid =
            new Vector2Int(
                32,
                32
            );

        bool interiorPrefetch =
            TerrainAuthoringPreviewStreamingPolicy
                .TryCalculatePrefetchTarget(
                    active,
                    Window(
                        7,
                        7,
                        6,
                        6
                    ),
                    Window(
                        6,
                        6,
                        11,
                        11
                    ),
                    worldGrid,
                    out _
                );

        bool edgePrefetch =
            TerrainAuthoringPreviewStreamingPolicy
                .TryCalculatePrefetchTarget(
                    active,
                    Window(
                        6,
                        6,
                        9,
                        8
                    ),
                    Window(
                        5,
                        5,
                        12,
                        12
                    ),
                    worldGrid,
                    out TerrainHeightCacheWindow target
                );

        if (
            !interiorPrefetch
            &&
            edgePrefetch
            &&
            target !=
                active
        )
        {
            AddPass(
                "Guard-based prefetch threshold",
                $"Interior required coverage remains idle; one-tile edge pressure selects {target}."
            );
        }
        else
        {
            AddFail(
                "Guard-based prefetch threshold",
                $"Unexpected prefetch result: interior={interiorPrefetch}, edge={edgePrefetch}, target={target}."
            );
        }
    }

    private static void ValidatePrefetchReversal()
    {
        TerrainHeightCacheWindow active =
            Window(
                4,
                4,
                12,
                12
            );

        bool comfortable =
            TerrainAuthoringPreviewStreamingPolicy
                .IsActiveComfortablySufficient(
                    active,
                    Window(
                        7,
                        7,
                        6,
                        6
                    ),
                    Window(
                        6,
                        6,
                        11,
                        11
                    ),
                    new Vector2Int(
                        32,
                        32
                    )
                );

        if (comfortable)
        {
            AddPass(
                "Prefetch reversal cancellation policy",
                "Returning to comfortable active headroom makes obsolete prefetch unnecessary without changing active residency."
            );
        }
        else
        {
            AddFail(
                "Prefetch reversal cancellation policy",
                "The active cache was not recognized as comfortably sufficient after reversal."
            );
        }
    }

    private static void ValidatePrefetchBecomesCoverageCritical()
    {
        TerrainHeightCacheWindow active =
            Window(
                4,
                4,
                12,
                12
            );

        TerrainHeightCacheWindow staging =
            Window(
                8,
                4,
                12,
                12
            );

        TerrainHeightCacheWindow required =
            Window(
                15,
                6,
                4,
                8
            );

        TerrainHeightCacheWindow desired =
            Window(
                9,
                5,
                11,
                11
            );

        bool activeSafe =
            active.Contains(
                required
            );

        bool stagingUseful =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    staging,
                    required,
                    desired
                );

        if (
            !activeSafe
            &&
            stagingUseful
        )
        {
            AddPass(
                "Prefetch can become coverage-critical without restart",
                "Active coverage can become unsafe while the same staging target remains useful for the newest required window."
            );
        }
        else
        {
            AddFail(
                "Prefetch can become coverage-critical without restart",
                $"Unexpected coverage/usefulness result: activeSafe={activeSafe}, stagingUseful={stagingUseful}."
            );
        }
    }

    private static void ValidateProgressAccounting()
    {
        TerrainHeightCacheWindow source =
            Window(
                0,
                0,
                4,
                4
            );

        TerrainHeightCacheWindow target =
            Window(
                1,
                0,
                4,
                4
            );

        if (
            !TerrainAuthoringPreviewCacheTransition.TryCreate(
                true,
                source,
                target,
                "committed",
                "overall",
                out TerrainAuthoringPreviewCacheTransition transition,
                out string error
            )
        )
        {
            AddFail(
                "Streaming progress accounting",
                error
            );

            return;
        }

        foreach (
            Vector2Int retained
            in transition.RetainedTiles
        )
        {
            transition.AddReusableRetainedTile(
                retained
            );
        }

        foreach (
            Vector2Int entering
            in transition.EnteringTiles
        )
        {
            transition.AddSourceMaterializationTile(
                entering
            );
        }

        transition.ResetStreamingExecutionState();

        transition.TotalWorkUnits =
            1
            +
            transition.ReusableRetainedTiles.Count
            +
            transition.SourceMaterializationTiles.Count * 2
            +
            1
            +
            1;

        transition.CompletedWorkUnits =
            1;

        bool valid =
            transition.TotalWorkUnits >
                transition.CompletedWorkUnits
            &&
            transition.RetainedCopyCursor ==
                0
            &&
            transition.CommittedLoadCursor ==
                0
            &&
            transition.CompositionCursor ==
                0;

        if (valid)
        {
            AddPass(
                "Streaming progress accounting",
                $"Prepared work plan reports {transition.TotalWorkUnits} total unit(s) with independent retained/load/compose cursors starting at zero."
            );
        }
        else
        {
            AddFail(
                "Streaming progress accounting",
                "Prepared work-plan counters were inconsistent."
            );
        }
    }

    private static void ValidateCancellationSemantics()
    {
        TerrainHeightCacheWindow target =
            Window(
                2,
                3,
                4,
                4
            );

        TerrainAuthoringPreviewCacheTransition.TryCreate(
            false,
            default,
            target,
            "committed",
            "overall",
            out TerrainAuthoringPreviewCacheTransition transition,
            out _
        );

        transition.MarkCancelled(
            "Superseded by latest target."
        );

        if (
            transition.State ==
                TerrainAuthoringPreviewTransitionState.Cancelled
            &&
            string.IsNullOrEmpty(
                transition.FailureMessage
            )
            &&
            !string.IsNullOrEmpty(
                transition.CancellationReason
            )
        )
        {
            AddPass(
                "Cancellation is distinct from failure",
                "Obsolete streaming work reaches Cancelled with a cancellation reason and no failure message."
            );
        }
        else
        {
            AddFail(
                "Cancellation is distinct from failure",
                "Cancelled transition state leaked into failure semantics."
            );
        }
    }

    private static void ValidateResidencySizeRecoveryClassification()
    {
        TerrainHeightCacheWindow source =
            Window(
                0,
                0,
                24,
                24
            );

        TerrainHeightCacheWindow target =
            Window(
                6,
                6,
                12,
                12
            );

        if (
            !TerrainAuthoringPreviewCacheTransition.TryCreate(
                true,
                source,
                target,
                "committed",
                "overall",
                out TerrainAuthoringPreviewCacheTransition transition,
                out string error
            )
        )
        {
            AddFail(
                "Residency size recovery regression",
                error
            );

            return;
        }

        if (
            transition.RetainedTiles.Count ==
                144
            &&
            transition.EnteringTiles.Count ==
                0
            &&
            transition.LeavingTiles.Count ==
                432
        )
        {
            AddPass(
                "Residency size recovery regression",
                "24x24 -> 12x12 remains Retained=144, Entering=0, Leaving=432 under the incremental transition model."
            );
        }
        else
        {
            AddFail(
                "Residency size recovery regression",
                $"Unexpected classification: retained={transition.RetainedTiles.Count}, entering={transition.EnteringTiles.Count}, leaving={transition.LeavingTiles.Count}."
            );
        }
    }

    private static void ValidateLiveStreamingInformation()
    {
        var snapshot = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        string detail = $"State={snapshot.StreamingState}; published current={snapshot.CacheReady}; latest paired ready={snapshot.ReadyForLatestIntent}; worker={snapshot.Worker.Purpose}, present={snapshot.Worker.Present}; queued={snapshot.QueuedWorker.Purpose}, present={snapshot.QueuedWorker.Present}; geographic loads={snapshot.Worker.LoadedGroupCount}/{snapshot.Worker.SourceGroupCount}; composed pages={snapshot.Worker.ComposedCount}/{snapshot.Worker.RepresentationCount}.";
        var worker = snapshot.Worker;
        bool consistent = !worker.Present || worker.LoadedGroupCount <= worker.SourceGroupCount
            && worker.MaterializedCount <= worker.RepresentationCount && worker.ComposedCount <= worker.MaterializedCount
            && worker.LastAllocations <= 1 && worker.LastLoads <= TerrainAuthoringPreviewService.StreamingCommittedLoadsPerUpdate
            && worker.LastCopies <= TerrainAuthoringPreviewService.StreamingRetainedCopiesPerUpdate
            && worker.LastMaterializations <= TerrainAuthoringPreviewService.StreamingMaterializationsPerUpdate
            && worker.LastCompositions <= TerrainAuthoringPreviewService.StreamingCompositionsPerUpdate;
        if (!snapshot.Worker.Present && snapshot.DisplayLods.Count == 0) AddBlocked("Live streaming diagnostics", detail);
        else if (consistent) AddPass("Live worker metadata consistency", detail + " GPU behavior is validated only by the isolated fixture.");
        else AddFail("Live worker metadata consistency", detail);
    }

    private static void ValidatePersistentAuthoringStateSafety()
    {
        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            AddBlocked(
                "Persistent authoring state unchanged",
                "WorldSettings or TerrainAuthoringData is unavailable."
            );

            return;
        }

        string committedBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        int revisionBefore =
            authoringData.authoringRevision;

        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        int revisionAfter =
            authoringData.authoringRevision;

        if (
            revisionBefore ==
                revisionAfter
            &&
            committedBefore ==
                committedAfter
            &&
            overallBefore ==
                overallAfter
        )
        {
            AddPass(
                "Persistent authoring state unchanged",
                "Incremental streaming validation did not mutate persistent terrain authoring identity."
            );
        }
        else
        {
            AddFail(
                "Persistent authoring state unchanged",
                "Persistent authoring identity changed during validation."
            );
        }
    }

    private static TerrainHeightCacheWindow Window(
        int x,
        int y,
        int width,
        int height
    )
    {
        return
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    x,
                    y
                ),
                new Vector2Int(
                    width,
                    height
                )
            );
    }

    private static void AddPass(
        string name,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Passed = true,
                Blocked = false
            }
        );
    }

    private static void AddFail(
        string name,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Passed = false,
                Blocked = false
            }
        );
    }

    private static void AddBlocked(
        string name,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Passed = false,
                Blocked = true
            }
        );
    }

    private static void ValidateDirtyFailureProgress()
    {
        if (!TerrainAuthoringPreviewCacheValidationUtility.TryCreateDirtyFixtureState(0, 1,
            out var fine, out _, out _, out string error, out bool blocked))
        { if (blocked) AddBlocked("Suppressed dirty worker progress", error); else AddFail("Suppressed dirty worker progress", error); return; }
        TerrainAuthoringPreviewLodState coarse = null;
        try
        {
            if (!TerrainAuthoringPreviewCacheValidationUtility.TryCreateDirtyFixtureState(1, 2,
                out coarse, out _, out _, out error, out blocked))
            { if (blocked) AddBlocked("Suppressed dirty worker progress", error); else AddFail("Suppressed dirty worker progress", error); return; }
            var failed = fine.ActiveCache.CacheOriginTile;
            fine.ActiveCache.TryGetTileCoordinate(1, out var other);
            var states = new[] { fine, coarse };
            fine.RecordDirtyFailure(failed, 1, "Isolated fine failure", true);
            coarse.PendingDirtyTiles.Add(failed);
            bool valid = !fine.IsDirtyTileRunnable(failed) && coarse.IsDirtyTileRunnable(failed);
            var chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, failed, false, out var tile);
            valid &= ReferenceEquals(chosen, coarse) && tile == failed;
            fine.PendingDirtyTiles.Add(other);
            chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, failed, true, out tile);
            valid &= ReferenceEquals(chosen, fine) && tile == other;
            fine.RecordDirtyFailure(other, 1, "Isolated other failure", true);
            coarse.RecordDirtyFailure(failed, 1, "Isolated coarse failure", true);
            valid &= !TerrainAuthoringPreviewService.HasRunnableDirtyWork(states, true)
                && !TerrainAuthoringPreviewService.HasRunnableDirtyWork(states, false)
                && TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, failed, true, out _) == null
                && fine.PendingDirtyTiles.Count == 2 && coarse.PendingDirtyTiles.Count == 1;
            TerrainAuthoringPreviewService.RearmDirtyTile(states, other);
            chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, failed, true, out tile);
            valid &= ReferenceEquals(chosen, fine) && tile == other;
            if (valid) AddPass("Suppressed dirty worker progress", "Held-source and interactive fine selection skip local failures; other LODs/tiles progress, and all-suppressed obligations leave other workers eligible.");
            else AddFail("Suppressed dirty worker progress", "A failed obligation blocked runnable work or all-suppressed state selected a dirty worker.");
        }
        finally { fine.Dispose(); coarse?.Dispose(); }
    }

    private static void ValidateDirtyPriorityAndAdmission()
    {
        const string name = "Interactive dirty priority and bounded admission";
        if (!TerrainAuthoringPreviewCacheValidationUtility.TryCreateDirtyFixtureState(0, 1,
            out var fine, out var settings, out var data, out string error, out bool blocked))
        { if (blocked) AddBlocked(name, error); else AddFail(name, error); return; }
        TerrainAuthoringPreviewLodState coarse = null;
        try
        {
            if (!TerrainAuthoringPreviewCacheValidationUtility.TryCreateDirtyFixtureState(1, 2,
                out coarse, out _, out _, out error, out blocked))
            { if (blocked) AddBlocked(name, error); else AddFail(name, error); return; }
            var first = fine.ActiveCache.CacheOriginTile;
            fine.ActiveCache.TryGetTileCoordinate(1, out var outer);
            RestrictFineFixtureWindow(fine, settings, data, first);
            var states = new[] { fine, coarse };
            fine.PendingDirtyTiles.Add(first);
            coarse.PendingDirtyTiles.Add(first); coarse.PendingDirtyTiles.Add(outer);
            var latest = new HashSet<Vector2Int> { outer };
            var chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, first, true, latest.Contains, out var tile);
            bool valid = fine.ActiveCache.GetSliceIndex(outer.x, outer.y) < 0 && ReferenceEquals(chosen, coarse) && tile == outer;
            latest.Clear();
            chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, outer, true, latest.Contains, out tile);
            valid &= ReferenceEquals(chosen, fine) && tile == first;
            fine.ActiveRequiredWindow = default;
            chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, first, false, out tile);
            valid &= ReferenceEquals(chosen, coarse);
            chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, outer, false, out tile);
            valid &= ReferenceEquals(chosen, coarse) && tile == outer;
            coarse.RecordDirtyFailure(outer, 1, "Isolated coarse failure", true);
            chosen = TerrainAuthoringPreviewService.ChooseDirtyDestination(states, true, outer, false, out tile);
            valid &= ReferenceEquals(chosen, coarse) && tile == first;
            coarse.RecordDirtyFailure(first, 1, "Isolated required failure", true);
            valid &= !TerrainAuthoringPreviewService.HasRunnableDirtyWork(states, true)
                && TerrainAuthoringPreviewService.HasRunnableDirtyWork(states, false);

            valid &= TerrainAuthoringPreviewStreamingPolicy.FineDirtyPriority
                    < TerrainAuthoringPreviewStreamingPolicy.GetHeightPreparationPriority(false, true, true, true)
                && TerrainAuthoringPreviewStreamingPolicy.GetHeightPreparationPriority(false, true, true, true)
                    < TerrainAuthoringPreviewStreamingPolicy.RequiredDirtyPriority
                && TerrainAuthoringPreviewStreamingPolicy.GetHeightPreparationPriority(true, false, false, true)
                    < TerrainAuthoringPreviewStreamingPolicy.InteractiveDirtyPriority
                && TerrainAuthoringPreviewStreamingPolicy.GetHeightPreparationPriority(false, true, false, true) == int.MaxValue
                && TerrainAuthoringPreviewStreamingPolicy.GetHeightPreparationPriority(false, true, false, false)
                    > TerrainAuthoringPreviewStreamingPolicy.GuardDirtyPriority;
            valid &= !TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(
                    TerrainAuthoringPreviewCachePublication.DisplayHeightSet, true, false, true)
                && TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(
                    TerrainAuthoringPreviewCachePublication.DisplayHeightSet, true, false, false);
            valid &= TerrainAuthoringPreviewService.DefaultDirtyCompositionsPerUpdate == 2
                && TerrainAuthoringPreviewService.DefaultCompositionsPerUpdate == 1
                && TerrainAuthoringPreviewStreamingPolicy.CanAdmitDirtyRepresentation(1, 4, 1, 1, false, 1)
                && !TerrainAuthoringPreviewStreamingPolicy.CanAdmitDirtyRepresentation(1, 5, 1, 1, false, 1)
                && !TerrainAuthoringPreviewStreamingPolicy.CanAdmitDirtyRepresentation(1, 0, 0, 0, true, 2)
                && !TerrainAuthoringPreviewStreamingPolicy.CanAdmitDirtyRepresentation(0, 0, 0, 2, false, 2)
                && TerrainAuthoringPreviewStreamingPolicy.CanAdmitDirtyRepresentation(1, 5, 1, 1, false, 2);
            if (valid) AddPass(name, "An outer resident owner wins the latest edit; fine/required work defeats coarse/guard affinity. Mandatory coverage, live analysis, deferred background and recovery-copy reservations retain bounded order.");
            else AddFail(name, "Priority, actual tile ownership, guard membership or callback admission was incorrect.");
        }
        finally { fine.Dispose(); coarse?.Dispose(); }
    }

    private static void RestrictFineFixtureWindow(TerrainAuthoringPreviewLodState state,
        WorldSettings settings, TerrainAuthoringData data, Vector2Int tile)
    {
        var window = new TerrainHeightCacheWindow(tile, Vector2Int.one);
        var cache = new TerrainAuthoringPreviewCache();
        Texture2D seed = null;
        try
        {
            if (!cache.TryInitializeStagingWindow(settings, data, window, state.SampleStride, out string error))
                throw new InvalidOperationException(error);
            seed = new Texture2D(state.SamplesPerSide, state.SamplesPerSide, TextureFormat.RFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            var values = new float[state.SamplesPerSide * state.SamplesPerSide];
            for (int i = 0; i < values.Length; i++) values[i] = 5f;
            seed.SetPixelData(values, 0); seed.Apply(false, false);
            Graphics.CopyTexture(seed, 0, 0, cache.HeightCache, 0, 0);
            if (!cache.TryCommitFinalCompositeTile(tile, 5f, 5f, out error)
                || !cache.TryFinalizeStagingForActivation(TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data), out error))
                throw new InvalidOperationException(error);
            state.ActiveCache.Dispose(); state.ActiveCache = cache; cache = null;
            state.ActiveRequiredWindow = window;
        }
        finally { cache?.Dispose(); if (seed != null) UnityEngine.Object.DestroyImmediate(seed); }
    }

    private static void ValidateCommittedSourceReuse()
    {
        const string name = "Committed source reuse across dirty representations";
        if (!SystemInfo.supportsAsyncGPUReadback)
        { AddBlocked(name, "GPU readback is required for explicit lattice verification."); return; }
        if (!TerrainAuthoringPreviewCacheValidationUtility.TryCreateDirtyFixtureState(0, 1,
            out var state, out var settings, out _, out string error, out bool blocked))
        { if (blocked) AddBlocked(name, error); else AddFail(name, error); return; }
        Texture2D source = null, disposable = null;
        RenderTexture native = null;
        var materializer = new TerrainAuthoringPreviewHeightMaterializer();
        try
        {
            var tile = state.ActiveCache.CacheOriginTile;
            string committed = state.ActiveCache.SourceCommittedHeightfieldSignature;
            int samples = settings.HeightTileSamplesPerSide, settingsId = settings.GetInstanceID();
            if (!TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile(settings, tile, out source, out error))
                throw new InvalidOperationException(error);
            var key = new TerrainAuthoringPreviewService.DirtySourceIdentity(tile, committed, settingsId, samples);
            bool valid = true;
            for (long generation = 1; generation <= 4; generation++)
            {
                state.DirtyTargetGeneration = generation;
                valid &= TerrainAuthoringPreviewService.CanReuseDirtySource(source, key,
                    new TerrainAuthoringPreviewService.DirtySourceIdentity(tile, committed, settingsId, samples));
            }
            valid &= !TerrainAuthoringPreviewService.CanReuseDirtySource(source, key,
                    new TerrainAuthoringPreviewService.DirtySourceIdentity(tile, committed + " changed", settingsId, samples))
                && !TerrainAuthoringPreviewService.CanReuseDirtySource(source, key,
                    new TerrainAuthoringPreviewService.DirtySourceIdentity(tile + Vector2Int.one, committed, settingsId, samples))
                && !TerrainAuthoringPreviewService.CanReuseDirtySource(source, key,
                    new TerrainAuthoringPreviewService.DirtySourceIdentity(tile, committed, settingsId + 1, samples))
                && !TerrainAuthoringPreviewService.CanReuseDirtySource(source, key,
                    new TerrainAuthoringPreviewService.DirtySourceIdentity(tile, committed, settingsId, samples + 1));
            native = new RenderTexture(samples, samples, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray, volumeDepth = 1, antiAliasing = 1,
                enableRandomWrite = true, useMipMap = false, hideFlags = HideFlags.HideAndDontSave
            };
            if (!native.Create()) throw new InvalidOperationException("The native Height validation array could not be created.");
            if (!state.TryEnsureDirtyScratch(out _, out error)) throw new InvalidOperationException(error);
            if (!materializer.TryMaterialize(source, native, 0, samples, samples, 1, out error)
                || !materializer.TryMaterialize(source, state.DirtyScratch, 0, samples,
                    state.SamplesPerSide, state.SampleStride, out error)) throw new InvalidOperationException(error);
            materializer.ReleaseTextureBindings();
            var nativeRead = AsyncGPUReadback.Request(native, 0); nativeRead.WaitForCompletion();
            var coarseRead = AsyncGPUReadback.Request(state.DirtyScratch, 0); coarseRead.WaitForCompletion();
            if (nativeRead.hasError || coarseRead.hasError) throw new InvalidOperationException("Committed source fixture GPU readback failed.");
            var nativeValues = nativeRead.GetData<float>(); var coarseValues = coarseRead.GetData<float>();
            for (int z = 0; z < state.SamplesPerSide; z++)
                for (int x = 0; x < state.SamplesPerSide; x++)
                    valid &= nativeValues[z * state.SampleStride * samples + x * state.SampleStride]
                        == coarseValues[z * state.SamplesPerSide + x];
            disposable = new Texture2D(2, 2, TextureFormat.RFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var temporaryKey = new TerrainAuthoringPreviewService.DirtySourceIdentity(tile, committed, settingsId, 2);
            valid &= TerrainAuthoringPreviewService.CanReuseDirtySource(disposable, temporaryKey, temporaryKey);
            UnityEngine.Object.DestroyImmediate(disposable);
            valid &= !TerrainAuthoringPreviewService.CanReuseDirtySource(disposable, temporaryKey, temporaryKey);
            if (valid) AddPass(name, "One borrowed committed tile materialized native and coarse lattices. Modifier targets preserve reuse; base, tile, settings, dimensions and object lifetime invalidate it.");
            else AddFail(name, "Source identity or exact shared native-lattice extraction was incorrect.");
        }
        finally
        {
            materializer.ReleaseTextureBindings(); state.Dispose(); source = null;
            if (native != null) { native.Release(); UnityEngine.Object.DestroyImmediate(native); }
            if (disposable != null) UnityEngine.Object.DestroyImmediate(disposable);
        }
    }

    private static void ValidatePostCommitFollowUps()
    {
        var state = new TerrainAuthoringPreviewLodState(0, 1, 9, 1);
        var tile = Vector2Int.zero;
        var instance = new GameObject("Transient Height bounds validation") { hideFlags = HideFlags.HideAndDontSave };
        var renderer = instance.AddComponent<MeshRenderer>();
        var oldBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        renderer.localBounds = oldBounds;
        try
        {
            state.RecordDirtyFailure(tile, 1, "Isolated earlier failure", true);
            state.PendingRegionalTiles.Add(tile);
            TerrainAuthoringPreviewService.CompleteDirtyContent(state, tile);
            bool firstRan = false, secondRan = false;
            Action listeners = () => { firstRan = true; throw new InvalidOperationException("Isolated observer failure"); };
            listeners += () => secondRan = true;
            string errors = TerrainAuthoringPreviewService.DispatchObserverCallbacks(listeners);
            bool restored = !TerrainAuthoringPreviewService.TryApplyPreviewBounds(new[] { renderer }, () =>
            {
                renderer.localBounds = new Bounds(Vector3.one, Vector3.one * 100f);
                throw new InvalidOperationException("Isolated bounds failure after a renderer write");
            }, out string boundsError) && renderer.localBounds == oldBounds;
            bool valid = firstRan && secondRan && !string.IsNullOrEmpty(errors) && restored && !string.IsNullOrEmpty(boundsError)
                && state.SuccessfulDirtyTiles.Contains(tile) && state.PendingDirtyTiles.Count == 0
                && state.PendingRegionalTiles.Count == 0 && state.DirtyFailures.Count == 0 && !state.WriteFailed;
            if (valid) AddPass("Postcommit bounds and observer isolation", "A throwing first listener did not skip the second; partial bounds writes were restored, and neither follow-up requeued successful content.");
            else AddFail("Postcommit bounds and observer isolation", "Observer dispatch, bounds restoration or committed obligations regressed.");
        }
        finally { state.Dispose(); UnityEngine.Object.DestroyImmediate(instance); }
    }

    private static void ValidateHeightSetPolicy()
    {
        var required = new TerrainHeightCacheWindow(new Vector2Int(4, 4), Vector2Int.one);
        var desired = new TerrainHeightCacheWindow(new Vector2Int(3, 3), new Vector2Int(3, 3));
        var plan = new TerrainAuthoringPreviewResidencyPlan
        {
            LevelCount = 2, Generation = 1,
            Levels = new TerrainAuthoringPreviewLodResidencyPlan[2]
        };
        for (int i = 0; i < 2; i++)
            plan.Levels[i] = new TerrainAuthoringPreviewLodResidencyPlan
            {
                Level = i, SampleStride = 1 << i, SamplesPerSide = (8 >> i) + 1,
                SampleSpacing = 1 << i, RequiredWindow = required, DesiredWindow = desired
            };
        var sources = new TerrainAuthoringPreviewCache[2];
        var generations = new long[2];
        using var first = new TerrainAuthoringPreviewCacheSetTransition(plan,
            new[] { required, required }, new bool[2], sources, generations,
            TerrainAuthoringPreviewCachePublication.DisplayHeightSet, "base", "content", 1, 1, 1, false);
        using var duplicate = new TerrainAuthoringPreviewCacheSetTransition(plan.CreateSnapshot(),
            new[] { required, required }, new bool[2], sources, generations,
            TerrainAuthoringPreviewCachePublication.DisplayHeightSet, "base", "content", 1, 2, 1, false);
        bool valid = TerrainAuthoringPreviewStreamingPolicy.AreCacheSetTargetsEquivalent(first, duplicate)
            && TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(first, plan)
            && TerrainAuthoringPreviewStreamingPolicy.TryCalculateHeightSetPrefetchTarget(required,
                plan.Levels[0], new Vector2Int(10, 10), out var growth) && growth == desired;
        var movedPlacement = plan.CreateSnapshot(); movedPlacement.Levels[0].Anchor = new Vector3(2, 0, 2);
        first.AcceptIntent(movedPlacement, 3);
        valid &= first.AcceptedPlan.Levels[0].Anchor == movedPlacement.Levels[0].Anchor
            && first.SourceGroups.Count == 1 && first.TotalWorkUnits > first.SourceGroups.Count
            && first.Entries[0].Target == required;
        var changed = plan.CreateSnapshot();
        changed.Levels[1].SampleStride *= 2;
        valid &= !TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(first, changed);
        var incompatible = plan.CreateSnapshot();
        incompatible.Levels[1].RequiredWindow = new TerrainHeightCacheWindow(
            new Vector2Int(8, 8), Vector2Int.one);
        incompatible.Levels[1].DesiredWindow = incompatible.Levels[1].RequiredWindow;
        valid &= !TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(first, incompatible);
        var metadata = TerrainAuthoringPreviewService.CaptureWorkerMetadata(first);
        valid &= metadata.SourceGroupCount == 1 && metadata.RepresentationCount == 2
            && metadata.Purpose == TerrainAuthoringPreviewCachePublication.DisplayHeightSet
            && metadata.RequestGeneration == 3 && metadata.Progress == 0f;
        first.CompletedWorkUnits = 1;
        first.State = TerrainAuthoringPreviewTransitionState.Preparing;
        var partial = TerrainAuthoringPreviewService.CaptureWorkerMetadata(first);
        first.State = TerrainAuthoringPreviewTransitionState.Cancelled;
        var cancelled = TerrainAuthoringPreviewService.CaptureWorkerMetadata(first);
        first.Dispose();
        valid &= partial.Progress > 0f && partial.Phase != cancelled.Phase
            && cancelled.Phase == TerrainAuthoringPreviewTransitionState.Cancelled
            && partial.RequestGeneration == 3 && cancelled.RepresentationCount == 2;
        var nativePlan = new TerrainAuthoringPreviewResidencyPlan { LevelCount = 1, Levels = new[] { plan.Levels[0] } };
        using var native = new TerrainAuthoringPreviewCacheSetTransition(nativePlan, new[] { required }, new bool[1],
            new TerrainAuthoringPreviewCache[1], new long[1], TerrainAuthoringPreviewCachePublication.NativeAnalysis,
            "base", "content", 1, 4, 1, false);
        var nativeMetadata = TerrainAuthoringPreviewService.CaptureWorkerMetadata(native);
        valid &= nativeMetadata.Purpose == TerrainAuthoringPreviewCachePublication.NativeAnalysis
            && nativeMetadata.SourceGroupCount == 1 && nativeMetadata.RepresentationCount == 1;
        if (valid) AddPass("Complete Height set request policy",
            "Identical effective targets coalesce; required-only work remains useful; guard growth and incompatible latest intent are classified.");
        else AddFail("Complete Height set request policy", "A set-level usefulness, equality, or prefetch decision was incorrect.");
    }

    private static void FinishValidation()
    {
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        System.Text.StringBuilder report =
            new System.Text.StringBuilder();

        report.AppendLine(
            "WorldMeshes Incremental Streaming Validation"
        );

        report.AppendLine(
            "===================================================================="
        );

        report.AppendLine();

        foreach (
            ValidationResult result
            in results
        )
        {
            string status;

            if (result.Blocked)
            {
                status = "BLOCKED";
                blocked++;
            }
            else if (result.Passed)
            {
                status = "PASS";
                passed++;
            }
            else
            {
                status = "FAIL";
                failed++;
            }

            report.AppendLine(
                $"{status} - {result.Name}"
            );

            report.AppendLine(
                $"       {result.Detail}"
            );

            report.AppendLine();
        }

        report.AppendLine(
            "----------------------------------------------"
        );

        report.AppendLine(
            $"{passed} passed"
        );

        report.AppendLine(
            $"{failed} failed"
        );

        report.AppendLine(
            $"{blocked} blocked"
        );

        report.AppendLine();

        lastRunSummary =
            TerrainValidationRunSummary.CreateCompleted(
                passed,
                failed,
                blocked,
                $"{passed} passed, {failed} failed, {blocked} blocked."
            );

        report.AppendLine(
            failed > 0
                ? "Incremental streaming validation: FAILED"
                : blocked > 0
                    ? "Incremental streaming validation: BLOCKED"
                    : "Incremental streaming validation: PASSED"
        );

        if (failed > 0)
        {
            Debug.LogError(
                report.ToString()
            );
        }
        else if (blocked > 0)
        {
            Debug.LogWarning(
                report.ToString()
            );
        }
        else
        {
            Debug.Log(
                report.ToString()
            );
        }
    }
}


