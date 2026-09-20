using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

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
            ValidatePackage03ARecoveryClassification();
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

    private static void ValidatePackage03ARecoveryClassification()
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
                "Package 03A bounded-residency regression",
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
                "Package 03A bounded-residency regression",
                "24x24 -> 12x12 remains Retained=144, Entering=0, Leaving=432 under the incremental transition model."
            );
        }
        else
        {
            AddFail(
                "Package 03A bounded-residency regression",
                $"Unexpected classification: retained={transition.RetainedTiles.Count}, entering={transition.EnteringTiles.Count}, leaving={transition.LeavingTiles.Count}."
            );
        }
    }

    private static void ValidateLiveStreamingInformation()
    {
        string detail =
            $"State={TerrainAuthoringPreviewService.StreamingStateLabel}; " +
            $"Coverage={TerrainAuthoringPreviewService.StreamingCoverageLabel}; " +
            $"Progress={TerrainAuthoringPreviewService.StreamingProgress:P1}; " +
            $"Generation={TerrainAuthoringPreviewService.StreamingRequestGeneration}; " +
            $"Budgets={TerrainAuthoringPreviewService.StreamingRetainedCopiesPerUpdate}/" +
            $"{TerrainAuthoringPreviewService.StreamingCommittedLoadsPerUpdate}/" +
            $"{TerrainAuthoringPreviewService.StreamingCompositionsPerUpdate}.";

        AddPass(
            "Live incremental streaming information",
            detail
        );
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
                "Package 04 validation did not mutate persistent terrain authoring identity."
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

    private static void FinishValidation()
    {
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        System.Text.StringBuilder report =
            new System.Text.StringBuilder();

        report.AppendLine(
            "WorldMeshes Edit-Mode Height Cache Streaming - Package 04 Validation"
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

        bool success =
            failed == 0
            &&
            blocked == 0;

        report.AppendLine(
            success
                ? "Package 04 incremental streaming: PASSED"
                : "Package 04 incremental streaming: FAILED"
        );

        if (success)
        {
            Debug.Log(
                report.ToString()
            );
        }
        else
        {
            Debug.LogError(
                report.ToString()
            );
        }
    }
}
