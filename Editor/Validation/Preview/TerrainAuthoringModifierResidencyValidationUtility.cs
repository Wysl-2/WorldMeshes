using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainAuthoringModifierResidencyValidationUtility
{
    private sealed class ValidationResult
    {
        public string Name;
        public string Detail;
        public bool Passed;
        public bool Blocked;
    }

    private static readonly List<ValidationResult> results =
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

    public static void ValidateModifierResidency()
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
                "Modifier residency validation is running."
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
            ValidateDirtyPartition();
            ValidateLodDirtyFanout();
            ValidateNonresidentDirtyPolicy();
            ValidateResidentPublicationPolicy();
            ValidateGenerationMonotonicity();
            ValidateTransitionGenerationCapture();
            ValidateStaleGenerationRejection();
            ValidateCancellationSemantics();
            ValidateInteractiveStreamingDeferral();
            ValidateNonresidentAcknowledgementPolicy();
            ValidateReadinessPolicy();
            ValidateTileSpecificReadiness();
            ValidateCommittedInvalidationStrength();
            ValidateReadinessQuerySideEffects();
            ValidateResidencySizeRecovery();
            ValidateLiveInformation();
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

    private static void ValidateDirtyPartition()
    {
        TerrainHeightCacheWindow active =
            Window(
                10,
                10,
                4,
                4
            );

        List<Vector2Int> global =
            new List<Vector2Int>
            {
                new Vector2Int(10, 10),
                new Vector2Int(11, 10),
                new Vector2Int(12, 11),
                new Vector2Int(13, 13),
                new Vector2Int(9, 10),
                new Vector2Int(14, 10),
                new Vector2Int(10, 9),
                new Vector2Int(10, 14),
                new Vector2Int(20, 20),
                new Vector2Int(2, 3)
            };

        List<Vector2Int> resident =
            new List<Vector2Int>();

        List<Vector2Int> nonresident =
            new List<Vector2Int>();

        TerrainAuthoringPreviewService
            .PartitionDirtyTilesForActiveWindow(
                global,
                true,
                active,
                resident,
                nonresident
            );

        bool ordered =
            IsRowMajor(
                resident
            )
            &&
            IsRowMajor(
                nonresident
            );

        if (
            resident.Count == 4
            &&
            nonresident.Count == 6
            &&
            ordered
        )
        {
            AddPass(
                "Global dirty / resident intersection",
                "Global=10, Resident=4, Nonresident=6 with deterministic world-tile ordering."
            );
        }
        else
        {
            AddFail(
                "Global dirty / resident intersection",
                $"Unexpected partition: resident={resident.Count}, nonresident={nonresident.Count}, ordered={ordered}."
            );
        }
    }

    private static void ValidateNonresidentDirtyPolicy()
    {
        List<Vector2Int> resident =
            new List<Vector2Int>();

        List<Vector2Int> nonresident =
            new List<Vector2Int>();

        TerrainAuthoringPreviewService
            .PartitionDirtyTilesForActiveWindow(
                new[]
                {
                    new Vector2Int(20, 20),
                    new Vector2Int(21, 20),
                    new Vector2Int(22, 20),
                    new Vector2Int(23, 20)
                },
                true,
                Window(0, 0, 4, 4),
                resident,
                nonresident
            );

        if (
            resident.Count == 0
            &&
            nonresident.Count == 4
        )
        {
            AddPass(
                "Nonresident modifier dirt remains logical",
                "Four nonresident dirty world tiles produce zero immediate resident GPU work candidates."
            );
        }
        else
        {
            AddFail(
                "Nonresident modifier dirt remains logical",
                "Nonresident dirt was incorrectly classified as active resident work."
            );
        }
    }

    private static void ValidateResidentPublicationPolicy()
    {
        List<Vector2Int> resident =
            new List<Vector2Int>();

        List<Vector2Int> nonresident =
            new List<Vector2Int>();

        List<Vector2Int> global =
            new List<Vector2Int>();

        for (int index = 0; index < 12; index++)
        {
            global.Add(
                index < 5
                    ? new Vector2Int(index, 0)
                    : new Vector2Int(index + 20, 0)
            );
        }

        TerrainAuthoringPreviewService
            .PartitionDirtyTilesForActiveWindow(
                global,
                true,
                Window(0, 0, 5, 2),
                resident,
                nonresident
            );

        if (
            resident.Count == 5
            &&
            nonresident.Count == 7
        )
        {
            AddPass(
                "Resident-only composite publication policy",
                "A 12-tile logical dirty set yields exactly five resident update/publication candidates."
            );
        }
        else
        {
            AddFail(
                "Resident-only composite publication policy",
                $"Expected 5 resident / 7 nonresident, received {resident.Count} / {nonresident.Count}."
            );
        }
    }

    private static void ValidateGenerationMonotonicity()
    {
        long first =
            TerrainAuthoringPreviewService
                .CalculateNextAuthoringGeneration(40L);

        long second =
            TerrainAuthoringPreviewService
                .CalculateNextAuthoringGeneration(first);

        if (
            first == 41L
            &&
            second == 42L
        )
        {
            AddPass(
                "Preview authoring generation monotonicity",
                "Pure generation advancement produces 40 -> 41 -> 42 independently of residency requests."
            );
        }
        else
        {
            AddFail(
                "Preview authoring generation monotonicity",
                $"Unexpected generation sequence: {first} -> {second}."
            );
        }
    }

    private static void ValidateTransitionGenerationCapture()
    {
        TerrainAuthoringPreviewCacheTransition.TryCreate(
            false,
            default,
            Window(2, 3, 4, 4),
            "committed",
            "overall",
            out TerrainAuthoringPreviewCacheTransition transition,
            out string error
        );

        if (transition == null)
        {
            AddFail(
                "Staging authoring-generation capture",
                error
            );
            return;
        }

        transition.TargetAuthoringGeneration =
            77L;

        if (transition.TargetAuthoringGeneration == 77L)
        {
            AddPass(
                "Staging authoring-generation capture",
                "A staging transition captures its editor-session authoring generation independently from request generation."
            );
        }
        else
        {
            AddFail(
                "Staging authoring-generation capture",
                "The transition did not retain its captured authoring generation."
            );
        }
    }

    private static void ValidateStaleGenerationRejection()
    {
        TerrainAuthoringPreviewCacheTransition.TryCreate(
            false,
            default,
            Window(1, 1, 4, 4),
            "committed",
            "overall",
            out TerrainAuthoringPreviewCacheTransition transition,
            out _
        );

        transition.TargetAuthoringGeneration =
            10L;

        bool current =
            TerrainAuthoringPreviewService
                .IsTransitionAuthoringStateCurrent(
                    transition,
                    11L,
                    "committed",
                    "overall"
                );

        if (!current)
        {
            AddPass(
                "Stale staging generation rejection",
                "A transition built at generation 10 is rejected against current generation 11."
            );
        }
        else
        {
            AddFail(
                "Stale staging generation rejection",
                "A stale transition generation was incorrectly accepted."
            );
        }
    }

    private static void ValidateCancellationSemantics()
    {
        TerrainAuthoringPreviewCacheTransition.TryCreate(
            false,
            default,
            Window(0, 0, 4, 4),
            "committed",
            "overall",
            out TerrainAuthoringPreviewCacheTransition transition,
            out _
        );

        transition.MarkCancelled(
            "Authoring generation changed."
        );

        if (
            transition.State ==
                TerrainAuthoringPreviewTransitionState.Cancelled
            &&
            string.IsNullOrEmpty(
                transition.FailureMessage
            )
        )
        {
            AddPass(
                "Authoring invalidation cancellation is not failure",
                "Stale authoring work reaches Cancelled without populating transition failure state."
            );
        }
        else
        {
            AddFail(
                "Authoring invalidation cancellation is not failure",
                "Authoring cancellation leaked into failure semantics."
            );
        }
    }

    private static void ValidateInteractiveStreamingDeferral()
    {
        bool deferred =
            TerrainAuthoringPreviewService
                .ShouldDeferStreamingRestart(
                    true
                );

        bool resumes =
            !TerrainAuthoringPreviewService
                .ShouldDeferStreamingRestart(
                    false
                );

        if (deferred && resumes)
        {
            AddPass(
                "Interactive streaming restart deferral",
                "Remote replacement staging is deferred during a live modifier gesture and becomes eligible after the gesture ends."
            );
        }
        else
        {
            AddFail(
                "Interactive streaming restart deferral",
                "Interactive-edit deferral policy did not distinguish active and completed gestures."
            );
        }
    }

    private static void ValidateNonresidentAcknowledgementPolicy()
    {
        bool mayAcknowledge =
            TerrainAuthoringPreviewService
                .CanAcknowledgeActiveAuthoringState(
                    true,
                    0
                );

        bool staleCommittedCannot =
            !TerrainAuthoringPreviewService
                .CanAcknowledgeActiveAuthoringState(
                    false,
                    0
                );

        if (mayAcknowledge && staleCommittedCannot)
        {
            AddPass(
                "Nonresident-only authoring acknowledgement",
                "A modifier-only change with zero resident dirty tiles can acknowledge active authoring state only when the committed base remains current."
            );
        }
        else
        {
            AddFail(
                "Nonresident-only authoring acknowledgement",
                "Authoring acknowledgement policy did not preserve committed-base authority."
            );
        }
    }

    private static void ValidateReadinessPolicy()
    {
        bool ready =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    false
                ) ==
            TerrainAuthoringPreviewReadiness.Ready;

        bool loading =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    true,
                    true,
                    true,
                    true,
                    false,
                    false,
                    false
                ) ==
            TerrainAuthoringPreviewReadiness.Loading;

        bool outside =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    true,
                    false,
                    true,
                    true,
                    true,
                    true,
                    false
                ) ==
            TerrainAuthoringPreviewReadiness.OutsideWorld;

        bool unavailable =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    false,
                    true,
                    false,
                    false,
                    false,
                    false,
                    false
                ) ==
            TerrainAuthoringPreviewReadiness.PreviewUnavailable;

        if (ready && loading && outside && unavailable)
        {
            AddPass(
                "Preview readiness classification",
                "Ready, Loading, OutsideWorld, and PreviewUnavailable remain distinct query states."
            );
        }
        else
        {
            AddFail(
                "Preview readiness classification",
                "One or more readiness states were classified incorrectly."
            );
        }
    }

    private static void ValidateTileSpecificReadiness()
    {
        TerrainAuthoringPreviewReadiness dirty =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true
                );

        TerrainAuthoringPreviewReadiness clean =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    false
                );

        if (
            dirty == TerrainAuthoringPreviewReadiness.Loading
            &&
            clean == TerrainAuthoringPreviewReadiness.Ready
        )
        {
            AddPass(
                "Tile-specific resident dirty readiness",
                "A pending resident dirty tile reports Loading while an unaffected resident tile remains Ready."
            );
        }
        else
        {
            AddFail(
                "Tile-specific resident dirty readiness",
                "Pending dirt incorrectly changed unrelated tile readiness."
            );
        }
    }

    private static void ValidateCommittedInvalidationStrength()
    {
        TerrainAuthoringPreviewReadiness readiness =
            TerrainAuthoringPreviewReadinessPolicy
                .EvaluateTile(
                    true,
                    true,
                    true,
                    false,
                    true,
                    true,
                    false
                );

        if (
            readiness ==
                TerrainAuthoringPreviewReadiness.Loading
        )
        {
            AddPass(
                "Committed-base invalidation strength",
                "A resident final slice backed by a stale committed signature is not reported as authoritative Ready terrain."
            );
        }
        else
        {
            AddFail(
                "Committed-base invalidation strength",
                $"Expected Loading for stale committed base, received {readiness}."
            );
        }
    }

    private static void ValidateReadinessQuerySideEffects()
    {
        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        if (worldSettings == null)
        {
            AddBlocked(
                "Readiness query side-effect safety",
                "WorldSettings is unavailable."
            );
            return;
        }

        long authoringBefore =
            TerrainAuthoringPreviewService.AuthoringGeneration;

        long streamingBefore =
            TerrainAuthoringPreviewService.StreamingRequestGeneration;

        TerrainAuthoringPreviewService
            .GetWorldTileReadiness(
                new Vector2Int(
                    -1,
                    0
                )
            );

        long authoringAfter =
            TerrainAuthoringPreviewService.AuthoringGeneration;

        long streamingAfter =
            TerrainAuthoringPreviewService.StreamingRequestGeneration;

        if (
            authoringBefore == authoringAfter
            &&
            streamingBefore == streamingAfter
        )
        {
            AddPass(
                "Readiness query side-effect safety",
                "A readiness query changed neither authoring nor streaming request generation."
            );
        }
        else
        {
            AddFail(
                "Readiness query side-effect safety",
                "Readiness query mutated authoring or streaming intent."
            );
        }
    }

    private static void ValidateResidencySizeRecovery()
    {
        TerrainHeightCacheWindow active =
            Window(
                0,
                0,
                24,
                24
            );

        TerrainHeightCacheWindow desired =
            Window(
                6,
                6,
                12,
                12
            );

        TerrainAuthoringPreviewResidencySizeHealth health =
            TerrainAuthoringPreviewResidencyPolicy
                .EvaluateSizeHealth(
                    true,
                    active,
                    desired
                );

        if (
            health ==
                TerrainAuthoringPreviewResidencySizeHealth.Oversized
        )
        {
            AddPass(
                "Residency size recovery regression",
                "24x24 active versus 12x12 desired remains Oversized; modifier residency does not reintroduce whole-world residency."
            );
        }
        else
        {
            AddFail(
                "Residency size recovery regression",
                $"Unexpected size health: {health}."
            );
        }
    }

    private static void ValidateLiveInformation()
    {
        var snapshot = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        int jobs = 0;
        var detail = new System.Text.StringBuilder();
        foreach (var row in snapshot.DisplayLods)
        {
            jobs += row.PendingDirtyCount;
            detail.Append($"LOD {row.Level}: current={row.Active.Current}, dirty jobs={row.PendingDirtyCount}, physical={row.Active.Window}; ");
        }
        detail.Append($"Unique geographic dirty tiles={snapshot.PendingGeographicDirtyCount}; display representation jobs={jobs}; authoring={snapshot.AuthoringGeneration}. ");
        detail.Append($"Modifier logical pending={TerrainAuthoringPreviewService.PendingGlobalDirtyTileCount}.");
        bool consistent = jobs >= snapshot.PendingGeographicDirtyCount;
        foreach (var row in snapshot.DisplayLods) consistent &= !row.Active.Current || row.PendingDirtyCount == 0 && !row.WriteFailed;
        if (snapshot.DisplayLods.Count == 0) AddBlocked("Live modifier diagnostics", detail.ToString());
        else if (consistent) AddPass("Live modifier diagnostic consistency", detail.ToString() + " Metadata only; no live GPU work was tested.");
        else AddFail("Live modifier diagnostic consistency", detail.ToString());
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

        int revisionBefore =
            authoringData.authoringRevision;

        int modifierCountBefore =
            authoringData.HeightModifierCount;

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

        int revisionAfter =
            authoringData.authoringRevision;

        int modifierCountAfter =
            authoringData.HeightModifierCount;

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

        if (
            revisionBefore == revisionAfter
            &&
            modifierCountBefore == modifierCountAfter
            &&
            committedBefore == committedAfter
            &&
            overallBefore == overallAfter
        )
        {
            AddPass(
                "Persistent authoring state unchanged",
                "Modifier residency validation did not mutate persistent terrain authoring identity or modifier count."
            );
        }
        else
        {
            AddFail(
                "Persistent authoring state unchanged",
                "Persistent terrain authoring state changed during validation."
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

    private static bool IsRowMajor(
        IReadOnlyList<Vector2Int> tiles
    )
    {
        if (tiles == null)
        {
            return false;
        }

        for (int index = 1; index < tiles.Count; index++)
        {
            Vector2Int previous =
                tiles[index - 1];

            Vector2Int current =
                tiles[index];

            if (
                current.y < previous.y
                ||
                (
                    current.y == previous.y
                    &&
                    current.x < previous.x
                )
            )
            {
                return false;
            }
        }

        return true;
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
            "WorldMeshes Modifier Residency Validation"
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
            string state;

            if (result.Blocked)
            {
                state = "BLOCKED";
                blocked++;
            }
            else if (result.Passed)
            {
                state = "PASS";
                passed++;
            }
            else
            {
                state = "FAIL";
                failed++;
            }

            report.AppendLine(
                $"{state} - {result.Name}"
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
                ? "Modifier residency validation: FAILED"
                : blocked > 0
                    ? "Modifier residency validation: BLOCKED"
                    : "Modifier residency validation: PASSED"
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
    private static void ValidateLodDirtyFanout()
    {
        var states = new[] { new TerrainAuthoringPreviewLodState(0, 1, 9, 1),
            new TerrainAuthoringPreviewLodState(1, 2, 5, 2), new TerrainAuthoringPreviewLodState(2, 4, 3, 4) };
        try
        {
            var windows = new[] { Window(5, 5, 1, 1), Window(4, 4, 3, 3), Window(3, 3, 5, 5) };
            var all = new HashSet<Vector2Int>();
            var tiles = new[] { new Vector2Int(5, 5), new Vector2Int(5, 5), new Vector2Int(3, 3), new Vector2Int(99, 99) };
            for (int i = 0; i < states.Length; i++) TerrainAuthoringPreviewService.QueueDirtyTilesForLod(states[i], windows[i], tiles, all);
            states[0].SuccessfulDirtyTiles.Add(new Vector2Int(5, 5));
            TerrainAuthoringPreviewService.QueueDirtyTilesForLod(states[0], windows[0], tiles, all);
            bool valid = states[0].PendingDirtyTiles.Count == 1 && states[1].PendingDirtyTiles.Count == 1
                && states[2].PendingDirtyTiles.Count == 2 && all.Count == 2 && states[0].SuccessfulDirtyTiles.Count == 0;
            // Resolve the fine obligation; repeated coarse invalidation must preserve all unresolved guard work.
            states[0].PendingDirtyTiles.Clear();
            var unresolved = new HashSet<Vector2Int>(states[1].PendingDirtyTiles);
            TerrainAuthoringPreviewService.QueueDirtyTilesForLod(states[1], windows[1],
                new[] { new Vector2Int(5, 5), windows[1].OriginTile }, all);
            valid &= states[0].PendingDirtyTiles.Count == 0 && states[1].PendingDirtyTiles.IsSupersetOf(unresolved)
                && states[1].PendingDirtyTiles.Contains(windows[1].OriginTile);
            var fine = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(0, states[0], null, null, null, true);
            var coarse = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(1, states[1], null, null, null, true);
            valid &= fine.PendingDirtyCount == 0 && coarse.PendingDirtyCount > 0;
            if (valid) AddPass("Dirty fan-out retains every affected display LOD", "Duplicates coalesce, coarse-only tiles remain queued and later edits invalidate prior success.");
            else AddFail("Dirty fan-out retains every affected display LOD", "A resident obligation was lost or assigned outside a physical window.");
        }
        finally { foreach (var state in states) state.Dispose(); }
    }

}
