using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainAuthoringStagedTransitionValidationUtility
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

    public static void ValidateStagedTransitions()
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
                "Staged window transition validation is running."
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
            ValidatePureClassification();
            ValidateOneTileShift();
            ValidateNoSourceClassification();
            ValidateLiveStagingFoundation();
            ValidateCacheSetStaging();
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

    private static void ValidatePureClassification()
    {
        TerrainHeightCacheWindow identical =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    5,
                    7
                ),
                new Vector2Int(
                    4,
                    3
                )
            );

        if (
            !TerrainAuthoringPreviewCacheTransition.TryCreate(
                true,
                identical,
                identical,
                "committed",
                "overall",
                out TerrainAuthoringPreviewCacheTransition same,
                out string sameError
            )
        )
        {
            AddFail(
                "Pure window classification",
                sameError
            );

            return;
        }

        bool sameValid =
            same.RetainedTiles.Count ==
                identical.TileCount
            &&
            same.EnteringTiles.Count ==
                0
            &&
            same.LeavingTiles.Count ==
                0;

        TerrainHeightCacheWindow disjointTarget =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    20,
                    20
                ),
                new Vector2Int(
                    3,
                    2
                )
            );

        TerrainAuthoringPreviewCacheTransition.TryCreate(
            true,
            identical,
            disjointTarget,
            "committed",
            "overall",
            out TerrainAuthoringPreviewCacheTransition disjoint,
            out _
        );

        bool disjointValid =
            disjoint != null
            &&
            disjoint.RetainedTiles.Count ==
                0
            &&
            disjoint.EnteringTiles.Count ==
                disjointTarget.TileCount
            &&
            disjoint.LeavingTiles.Count ==
                identical.TileCount;

        bool deterministic =
            IsRowMajor(
                same.RetainedTiles
            )
            &&
            IsRowMajor(
                disjoint.EnteringTiles
            )
            &&
            IsRowMajor(
                disjoint.LeavingTiles
            );

        if (
            sameValid
            &&
            disjointValid
            &&
            deterministic
        )
        {
            AddPass(
                "Pure window classification",
                "Identical/disjoint transition sets are complete, exclusive, and row-major."
            );
        }
        else
        {
            AddFail(
                "Pure window classification",
                "One or more retained/entering/leaving invariants failed."
            );
        }
    }

    private static void ValidateOneTileShift()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    10,
                    10
                ),
                new Vector2Int(
                    10,
                    10
                )
            );

        TerrainHeightCacheWindow target =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    11,
                    10
                ),
                new Vector2Int(
                    10,
                    10
                )
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
                "One-tile staged shift classification",
                error
            );

            return;
        }

        bool countsValid =
            transition.RetainedTiles.Count ==
                90
            &&
            transition.EnteringTiles.Count ==
                10
            &&
            transition.LeavingTiles.Count ==
                10;

        bool enteringValid =
            AllTilesHaveX(
                transition.EnteringTiles,
                20
            );

        bool leavingValid =
            AllTilesHaveX(
                transition.LeavingTiles,
                10
            );

        if (
            countsValid
            &&
            enteringValid
            &&
            leavingValid
        )
        {
            AddPass(
                "One-tile staged shift classification",
                "Retained=90, Entering=10, Leaving=10."
            );
        }
        else
        {
            AddFail(
                "One-tile staged shift classification",
                $"Retained={transition.RetainedTiles.Count}, " +
                $"Entering={transition.EnteringTiles.Count}, " +
                $"Leaving={transition.LeavingTiles.Count}."
            );
        }
    }

    private static void ValidateNoSourceClassification()
    {
        TerrainHeightCacheWindow target =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    3,
                    4
                ),
                new Vector2Int(
                    5,
                    6
                )
            );

        if (
            !TerrainAuthoringPreviewCacheTransition.TryCreate(
                false,
                default,
                target,
                "committed",
                "overall",
                out TerrainAuthoringPreviewCacheTransition transition,
                out string error
            )
        )
        {
            AddFail(
                "Initial staging classification",
                error
            );

            return;
        }

        if (
            transition.RetainedTiles.Count ==
                0
            &&
            transition.LeavingTiles.Count ==
                0
            &&
            transition.EnteringTiles.Count ==
                target.TileCount
        )
        {
            AddPass(
                "Initial staging classification",
                $"All {target.TileCount} target tile(s) are entering."
            );
        }
        else
        {
            AddFail(
                "Initial staging classification",
                "A no-source transition did not classify every target tile as entering."
            );
        }
    }

    private static void ValidateLiveStagingFoundation()
    {
        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths
                    .WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths
                    .TerrainAuthoringDataAssetPath
            );

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            AddBlocked(
                "Live staged-transition prerequisites",
                "WorldSettings or TerrainAuthoringData could not be loaded."
            );

            return;
        }

        if (
            !TerrainAuthoringPreviewService
                .TryGetActiveDisplayLodCacheForValidation(0, 
                    out TerrainAuthoringPreviewCache activeCache
                )
            ||
            activeCache == null
            ||
            !activeCache.IsCompleteForActivation
        )
        {
            AddBlocked(
                "Live staged-transition prerequisites",
                "Enable Height Preview and allow the active resident cache to become current."
            );

            return;
        }

        if (activeCache.SampleStride != 1)
        {
            AddBlocked("Selected LOD0 native retained-reuse fixture", "LOD0 is coarse. Native-only comparisons require stride one; the bounded complete-set fixture still validates coarse representations.");
            return;
        }
        var activeWindow = new TerrainHeightCacheWindow(activeCache.CacheOriginTile, activeCache.CacheSize);
        string liveIdentityBefore = TerrainAuthoringPreviewService.CaptureDisplayIdentityForValidation();

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
            string.IsNullOrEmpty(
                committedSignature
            )
            ||
            string.IsNullOrEmpty(
                overallSignature
            )
        )
        {
            AddBlocked(
                "Live staged-transition prerequisites",
                "Current authoring signatures could not be calculated."
            );

            return;
        }

        int activeTextureBefore =
            activeCache.HeightCache.GetInstanceID();

        int authoringRevisionBefore =
            authoringData.authoringRevision;

        string committedBefore =
            committedSignature;

        string overallBefore =
            overallSignature;

        AddPass(
            "Live staged-transition prerequisites",
            $"Active={activeWindow}, TextureID={activeTextureBefore}."
        );

        Vector2Int retainedTile =
            activeWindow.OriginTile
            +
            new Vector2Int(
                activeWindow.Width > 1
                    ? 1
                    : 0,
                activeWindow.Height > 1
                    ? 1
                    : 0
            );

        TerrainHeightCacheWindow singleTileWindow =
            new TerrainHeightCacheWindow(
                retainedTile,
                Vector2Int.one
            );

        ValidateSliceRemapping(
            activeWindow,
            retainedTile
        );

        ValidateReadinessAndEnteringComposition(
            worldSettings,
            authoringData,
            singleTileWindow,
            retainedTile,
            overallSignature
        );

        ValidateRetainedGpuReuse(
            worldSettings,
            authoringData,
            activeCache,
            singleTileWindow,
            retainedTile,
            committedSignature,
            overallSignature
        );

        ValidateReuseEligibility(
            activeCache,
            worldSettings,
            authoringData,
            singleTileWindow,
            committedSignature,
            overallSignature
        );

        bool activeIdentityUnchanged = TerrainAuthoringPreviewService.CaptureDisplayIdentityForValidation() == liveIdentityBefore;

        if (activeIdentityUnchanged)
        {
            AddPass(
                "All live display allocations unaffected by isolated staging",
                $"Active TextureID remained {activeTextureBefore}, window remained {activeWindow}."
            );
        }
        else
        {
            AddFail(
                "All live display allocations unaffected by isolated staging",
                "Temporary staging validation changed the live active cache identity or window."
            );
        }

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
            authoringData.authoringRevision ==
                authoringRevisionBefore
            &&
            committedAfter ==
                committedBefore
            &&
            overallAfter ==
                overallBefore
        )
        {
            AddPass(
                "Persistent authoring state unchanged",
                "Staged transition validation did not mutate persistent authoring identity."
            );
        }
        else
        {
            AddFail(
                "Persistent authoring state unchanged",
                "Authoring revision or signatures changed during staged-transition validation."
            );
        }
    }

    private static void ValidateSliceRemapping(
        TerrainHeightCacheWindow activeWindow,
        Vector2Int worldTile
    )
    {
        if (
            activeWindow.Width <= 1
            &&
            activeWindow.Height <= 1
        )
        {
            AddBlocked(
                "World tile to staging-local slice remapping",
                "The active cache has only one slice."
            );

            return;
        }

        Vector2Int shiftedOrigin =
            activeWindow.OriginTile;

        Vector2Int destinationSize =
            activeWindow.Size;

        if (activeWindow.Width > 1)
        {
            shiftedOrigin.x =
                worldTile.x;

            destinationSize.x =
                activeWindow.Width - 1;
        }
        else if (activeWindow.Height > 1)
        {
            shiftedOrigin.y =
                worldTile.y;

            destinationSize.y =
                activeWindow.Height - 1;
        }

        TerrainHeightCacheWindow destination =
            new TerrainHeightCacheWindow(
                shiftedOrigin,
                destinationSize
            );

        int sourceLocalX =
            worldTile.x -
            activeWindow.OriginTile.x;

        int sourceLocalZ =
            worldTile.y -
            activeWindow.OriginTile.y;

        int sourceSlice =
            sourceLocalX
            +
            sourceLocalZ *
            activeWindow.Width;

        int destinationLocalX =
            worldTile.x -
            destination.OriginTile.x;

        int destinationLocalZ =
            worldTile.y -
            destination.OriginTile.y;

        int destinationSlice =
            destinationLocalX
            +
            destinationLocalZ *
            destination.Width;

        if (
            sourceSlice >= 0
            &&
            destinationSlice >= 0
            &&
            (
                activeWindow.TileCount <= 1
                ||
                sourceSlice !=
                    destinationSlice
            )
        )
        {
            AddPass(
                "World tile to staging-local slice remapping",
                $"World tile {worldTile} maps source slice {sourceSlice} and destination slice {destinationSlice}."
            );
        }
        else
        {
            AddFail(
                "World tile to staging-local slice remapping",
                "A retained world tile did not resolve valid source/destination local slices."
            );
        }
    }

    private static void ValidateReadinessAndEnteringComposition(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainHeightCacheWindow window,
        Vector2Int tile,
        string overallSignature
    )
    {
        TerrainAuthoringPreviewCache staging =
            new TerrainAuthoringPreviewCache();

        TerrainHeightCompositor compositor =
            new TerrainHeightCompositor();

        try
        {
            if (
                !staging.TryInitializeStagingWindow(
                    worldSettings,
                    authoringData,
                    window,
                    out string initializationError
                )
            )
            {
                AddBlocked(
                    "Staging slice readiness initialization",
                    initializationError
                );

                return;
            }

            if (
                staging.GetSliceReadiness(
                    tile
                )
                ==
                TerrainAuthoringPreviewSliceReadiness
                    .Uninitialized
            )
            {
                AddPass(
                    "Staging slice readiness initialization",
                    "New staging slice is Uninitialized."
                );
            }
            else
            {
                AddFail(
                    "Staging slice readiness initialization",
                    "New staging slice did not start Uninitialized."
                );
            }

            if (
                staging.TryValidateCompleteForActivation(
                    out _
                )
            )
            {
                AddFail(
                    "Incomplete staging activation gate",
                    "An uninitialized staging slice incorrectly passed activation validation."
                );
            }
            else
            {
                AddPass(
                    "Incomplete staging activation gate",
                    "Uninitialized staging correctly rejected activation."
                );
            }

            if (
                !staging.TryLoadCommittedBaseTile(
                    tile,
                    out string loadError
                )
            )
            {
                AddFail(
                    "Committed staging materialization",
                    loadError
                );

                return;
            }

            if (
                staging.GetSliceReadiness(
                    tile
                )
                ==
                TerrainAuthoringPreviewSliceReadiness
                    .CommittedBaseReady
            )
            {
                AddPass(
                    "Committed staging materialization",
                    "Committed source loaded and readiness advanced to CommittedBaseReady."
                );
            }
            else
            {
                AddFail(
                    "Committed staging materialization",
                    "Committed source did not produce CommittedBaseReady."
                );

                return;
            }

            if (
                staging.TryValidateCompleteForActivation(
                    out _
                )
            )
            {
                AddFail(
                    "Committed-only staging rejection",
                    "Committed-base-only staging incorrectly passed activation validation."
                );
            }
            else
            {
                AddPass(
                    "Committed-only staging rejection",
                    "CommittedBaseReady staging correctly rejected activation."
                );
            }

            if (
                !staging.TryGetCommittedRange(
                    tile,
                    out float baseMinimum,
                    out float baseMaximum,
                    out string rangeError
                )
            )
            {
                AddFail(
                    "Entering staging composition",
                    rangeError
                );

                return;
            }

            int slice =
                staging.GetSliceIndex(
                    tile.x,
                    tile.y
                );

            compositor.BeginTransactionDiagnostics();

            if (
                !compositor.TryComposeTile(
                    staging.HeightCache,
                    tile,
                    slice,
                    staging.SamplesPerSide,
                    staging.SampleSpacing,
                    worldSettings.HeightTileWorldSize,
                    staging.WorldSizeXZ,
                    authoringData,
                    baseMinimum,
                    baseMaximum,
                    out float finalMinimum,
                    out float finalMaximum,
                    out string compositorError
                )
            )
            {
                AddFail(
                    "Entering staging composition",
                    compositorError
                );

                return;
            }

            if (
                !staging.TryCommitFinalCompositeTile(
                    tile,
                    finalMinimum,
                    finalMaximum,
                    out string finalRangeError
                )
            )
            {
                AddFail(
                    "Entering staging composition",
                    finalRangeError
                );

                return;
            }

            if (
                !staging.TryFinalizeStagingForActivation(
                    overallSignature,
                    out string finalizeError
                )
            )
            {
                AddFail(
                    "Entering staging composition",
                    finalizeError
                );

                return;
            }

            if (
                staging.IsCompleteForActivation
                &&
                staging.GetSliceReadiness(
                    tile
                )
                ==
                TerrainAuthoringPreviewSliceReadiness
                    .FinalCompositeReady
            )
            {
                AddPass(
                    "Entering staging composition",
                    $"Tile {tile} reached FinalCompositeReady with range {finalMinimum:R} -> {finalMaximum:R}."
                );
            }
            else
            {
                AddFail(
                    "Entering staging composition",
                    "Fully composed staging did not pass activation validation."
                );
            }

            long expectedMemory =
                staging.ApproximateGpuMemoryBytes;

            if (expectedMemory > 0L)
            {
                AddPass(
                    "Staging GPU memory accounting",
                    $"One-slice staging allocation reports {expectedMemory:N0} byte(s)."
                );
            }
            else
            {
                AddFail(
                    "Staging GPU memory accounting",
                    "Staging GPU memory estimate was not positive."
                );
            }
        }
        finally
        {
            compositor.Dispose();

            RenderTexture stagedTexture =
                staging.HeightCache;

            staging.Dispose();

            if (
                staging.HeightCache == null
            )
            {
                AddPass(
                    "Temporary staging resource cleanup",
                    stagedTexture != null
                        ? "Temporary staging RenderTexture was released and cache reference cleared."
                        : "Temporary staging cache reference remained clear."
                );
            }
            else
            {
                AddFail(
                    "Temporary staging resource cleanup",
                    "Temporary staging cache retained a GPU texture after Dispose()."
                );
            }
        }
    }

    private static void ValidateRetainedGpuReuse(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainAuthoringPreviewCache activeCache,
        TerrainHeightCacheWindow window,
        Vector2Int tile,
        string committedSignature,
        string overallSignature
    )
    {
        TerrainAuthoringPreviewCache staging =
            new TerrainAuthoringPreviewCache();

        try
        {
            if (
                !staging.TryInitializeStagingWindow(
                    worldSettings,
                    authoringData,
                    window,
                    out string initializationError
                )
            )
            {
                AddBlocked(
                    "Retained final-composite GPU reuse",
                    initializationError
                );

                return;
            }

            bool reuseEligible =
                TerrainAuthoringPreviewService
                    .IsRetainedReuseGloballyEligible(
                        activeCache,
                        staging,
                        committedSignature,
                        overallSignature,
                        false
                    );

            if (!reuseEligible)
            {
                AddBlocked(
                    "Retained final-composite GPU reuse",
                    "The live active cache is not eligible for final retained reuse."
                );

                return;
            }

            if (
                !staging.TryCopyFinalCompositeTileFrom(
                    activeCache,
                    tile,
                    out string copyError
                )
            )
            {
                AddFail(
                    "Retained final-composite GPU reuse",
                    copyError
                );

                return;
            }

            if (
                !staging.TryFinalizeStagingForActivation(
                    overallSignature,
                    out string finalizeError
                )
            )
            {
                AddFail(
                    "Retained final-composite GPU reuse",
                    finalizeError
                );

                return;
            }

            bool rangesMatch =
                activeCache.TryGetCompositeSliceRange(
                    tile.x,
                    tile.y,
                    out float activeMinimum,
                    out float activeMaximum
                )
                &&
                staging.TryGetCompositeSliceRange(
                    tile.x,
                    tile.y,
                    out float stagingMinimum,
                    out float stagingMaximum
                )
                &&
                Mathf.Approximately(
                    activeMinimum,
                    stagingMinimum
                )
                &&
                Mathf.Approximately(
                    activeMaximum,
                    stagingMaximum
                );

            if (
                staging.IsCompleteForActivation
                &&
                staging.IsSliceFinalCompositeReady(
                    tile
                )
                &&
                rangesMatch
            )
            {
                AddPass(
                    "Retained final-composite GPU reuse",
                    $"Tile {tile} copied active final GPU contents and metadata without committed source materialization."
                );
            }
            else
            {
                AddFail(
                    "Retained final-composite GPU reuse",
                    "Retained destination readiness/range did not match the active final tile."
                );
            }
        }
        finally
        {
            staging.Dispose();
        }
    }

    private static void ValidateReuseEligibility(
        TerrainAuthoringPreviewCache activeCache,
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainHeightCacheWindow window,
        string committedSignature,
        string overallSignature
    )
    {
        TerrainAuthoringPreviewCache staging =
            new TerrainAuthoringPreviewCache();

        try
        {
            if (
                !staging.TryInitializeStagingWindow(
                    worldSettings,
                    authoringData,
                    window,
                    out string error
                )
            )
            {
                AddBlocked(
                    "Retained reuse eligibility policy",
                    error
                );

                return;
            }

            bool normalAllowed =
                TerrainAuthoringPreviewService
                    .IsRetainedReuseGloballyEligible(
                        activeCache,
                        staging,
                        committedSignature,
                        overallSignature,
                        false
                    );

            bool forceRebuildRejected =
                !TerrainAuthoringPreviewService
                    .IsRetainedReuseGloballyEligible(
                        activeCache,
                        staging,
                        committedSignature,
                        overallSignature,
                        true
                    );

            bool overallMismatchRejected =
                !TerrainAuthoringPreviewService
                    .IsRetainedReuseGloballyEligible(
                        activeCache,
                        staging,
                        committedSignature,
                        overallSignature + "-validation-mismatch",
                        false
                    );

            if (
                normalAllowed
                &&
                forceRebuildRejected
                &&
                overallMismatchRejected
            )
            {
                AddPass(
                    "Retained reuse eligibility policy",
                    "Current residency may reuse; explicit committed rebuild and overall-signature mismatch both disable reuse."
                );
            }
            else
            {
                AddFail(
                    "Retained reuse eligibility policy",
                    $"Normal={normalAllowed}, ForceRejected={forceRebuildRejected}, OverallMismatchRejected={overallMismatchRejected}."
                );
            }
        }
        finally
        {
            staging.Dispose();
        }
    }

    private static bool IsRowMajor(
        IReadOnlyList<Vector2Int> tiles
    )
    {
        for (
            int index = 1;
            index < tiles.Count;
            index++
        )
        {
            Vector2Int previous =
                tiles[
                    index - 1
                ];

            Vector2Int current =
                tiles[
                    index
                ];

            if (
                current.y <
                    previous.y
                ||
                (
                    current.y ==
                        previous.y
                    &&
                    current.x <=
                        previous.x
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllTilesHaveX(
        IReadOnlyList<Vector2Int> tiles,
        int x
    )
    {
        for (
            int index = 0;
            index < tiles.Count;
            index++
        )
        {
            if (
                tiles[
                    index
                ].x !=
                x
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
                Passed = true
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
                Passed = false
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
                Blocked = true
            }
        );
    }

    private static void FinishValidation()
    {
        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            "WorldMeshes Staged Window Transition Validation"
        );

        builder.AppendLine(
            "===================================================================="
        );

        builder.AppendLine();

        foreach (
            ValidationResult result
            in results
        )
        {
            string prefix;

            if (result.Blocked)
            {
                blocked++;

                prefix =
                    "BLOCKED";
            }
            else if (result.Passed)
            {
                passed++;

                prefix =
                    "PASS";
            }
            else
            {
                failed++;

                prefix =
                    "FAIL";
            }

            builder.AppendLine(
                $"{prefix} - {result.Name}"
            );

            if (
                !string.IsNullOrEmpty(
                    result.Detail
                )
            )
            {
                builder.AppendLine(
                    $"       {result.Detail}"
                );
            }

            builder.AppendLine();
        }

        builder.AppendLine(
            "----------------------------------------------"
        );

        builder.AppendLine(
            $"{passed} passed"
        );

        builder.AppendLine(
            $"{failed} failed"
        );

        builder.AppendLine(
            $"{blocked} blocked"
        );

        builder.AppendLine();

        lastRunSummary =
            TerrainValidationRunSummary.CreateCompleted(
                passed,
                failed,
                blocked,
                $"{passed} passed, {failed} failed, {blocked} blocked."
            );

        builder.AppendLine(
            failed > 0
                ? "Staged window transitions: FAILED"
                : blocked > 0
                    ? "Staged window transitions: BLOCKED"
                    : "Staged window transitions: PASSED"
        );

        if (failed > 0)
        {
            Debug.LogError(
                builder.ToString()
            );
        }
        else if (blocked > 0)
        {
            Debug.LogWarning(
                builder.ToString()
            );
        }
        else
        {
            Debug.Log(
                builder.ToString()
            );
        }
    }
    // Local ownership only: no live intent, binding, service cache, or assets are changed.
    internal static bool RunCacheSetWorkerFixture(bool verifyBudgets, out string detail, out bool blocked)
    {
        detail = "";
        blocked = false;
        var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldMeshesPaths.WorldSettingsAssetPath);
        var data = AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(WorldMeshesPaths.TerrainAuthoringDataAssetPath);
        if (settings == null || data == null || !SystemInfo.supportsComputeShaders
            || !SystemInfo.supports2DArrayTextures || settings.HeightTileIntervalsPerSide < 4
            || settings.HeightTileGridWidth < 4 || settings.HeightTileGridHeight < 1
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat))
        {
            blocked = true;
            detail = "A committed world at least four tiles wide and compute/RFloat support are required.";
            return false;
        }
        if (!TerrainAuthoringStateUtility.TryValidateCommittedHeightfield(settings, data,
            TerrainAuthoringHeightfieldValidationMode.Operational, out _, out _, out detail))
        {
            blocked = true;
            return false;
        }
        if (Shader.Find("Custom/ClipmapTerrain") == null)
        { blocked = true; detail = "The clipmap terrain shader is required for semantic binding validation."; return false; }
        TerrainAuthoringPreviewCacheSetTransition initial = null;
        TerrainAuthoringPreviewCacheSetTransition replacement = null;
        TerrainAuthoringPreviewCacheSetTransition failed = null;
        TerrainAuthoringPreviewLodState[] published = null;
        TerrainAuthoringPreviewLodState[] next = null;
        var compositor = new TerrainHeightCompositor();
        string liveIdentityBefore = TerrainAuthoringPreviewService.CaptureDisplayIdentityForValidation();
        try
        {
            string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
            string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
            int finestStride = 1;
            while (settings.HeightTileIntervalsPerSide / finestStride > 8
                && TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, finestStride * 2))
                finestStride *= 2;
            var firstWindow = new TerrainHeightCacheWindow(UnityEngine.Vector2Int.zero,
                new UnityEngine.Vector2Int(3, 1));
            var plan = new TerrainAuthoringPreviewResidencyPlan
            {
                LevelCount = 3, Generation = 1,
                Levels = new TerrainAuthoringPreviewLodResidencyPlan[3]
            };
            for (int i = 0; i < 3; i++)
            {
                int stride = finestStride << i;
                if (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride))
                {
                    blocked = true;
                    detail = "Three nested Height representations are not supported by this topology.";
                    return false;
                }
                plan.Levels[i] = new TerrainAuthoringPreviewLodResidencyPlan
                {
                    Level = i, SampleStride = stride,
                    SamplesPerSide = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, stride),
                    SampleSpacing = TerrainHeightResolutionUtility.GetSampleSpacing(settings, stride),
                    RequiredWindow = firstWindow, DesiredWindow = new TerrainHeightCacheWindow(
                        UnityEngine.Vector2Int.zero, new UnityEngine.Vector2Int(4, 1))
                };
            }
            var targets = new[] { firstWindow, firstWindow, firstWindow };
            var sources = new TerrainAuthoringPreviewCache[3];
            var generations = new long[3];
            initial = new TerrainAuthoringPreviewCacheSetTransition(plan, targets, new bool[3],
                sources, generations, TerrainAuthoringPreviewCachePublication.DisplayHeightSet,
                committed, overall, 7, 10, 3, false, settings.HeightTileWorldSize);
            // Mutating caller-owned plans must not change the frozen work specification.
            plan.Levels[0].Anchor = new UnityEngine.Vector3(999, 0, 999);
            RequireSetFixture(initial.AcceptedPlan.Levels[0].Anchor != plan.Levels[0].Anchor,
                "The request did not deep-copy its level plans.");
            bool rejectedPartial = false;
            try { initial.TransferPreparedStates(); }
            catch (InvalidOperationException) { rejectedPartial = true; }
            RequireSetFixture(rejectedPartial && initial.Entries[0].Destination != null,
                "A partial set transferred ownership.");
            var queuedSnapshot = TerrainAuthoringPreviewService.CaptureWorkerMetadata(initial);
            RequireSetFixture(queuedSnapshot.Purpose == TerrainAuthoringPreviewCachePublication.DisplayHeightSet
                && queuedSnapshot.SourceGroupCount == 3 && queuedSnapshot.RepresentationCount == 9
                && queuedSnapshot.MaterializedCount == 0 && !TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(
                    0, null, initial.AcceptedPlan.Levels[0], initial.Entries[0], null, true).Staging.Present,
                "Queued display work merged geographic groups with representation pages or invented allocation.");
            int actualLoads = 0;
            TerrainAuthoringPreviewService.NativeHeightSourceLoader loader =
                (WorldSettings world, UnityEngine.Vector2Int tile, out Texture2D source, out string error) =>
                {
                    actualLoads++;
                    return TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile(
                        world, tile, out source, out error);
                };
            bool observedResume = false;
            int calls = DriveSetFixture(initial, settings, data, compositor, loader,
                verifyBudgets ? 1 : TerrainAuthoringPreviewService.DefaultMaterializationsPerUpdate,
                ref observedResume);
            RequireSetFixture(initial.Complete && initial.SourceLoads == 3 && actualLoads == 3
                && initial.MaterializedSlices == 9 && initial.ComposedSlices == 9,
                "Shared native sources were loaded more than once per geographic tile or pages were omitted.");
            RequireSetFixture(!verifyBudgets || observedResume,
                "The fixture did not resume a retained source reference across worker updates.");
            RequireSetFixture(initial.CurrentSource == null
                && initial.CompletedWorkUnits == initial.TotalWorkUnits - 1,
                "Source ownership or operation accounting did not finish before publication.");
            var useful = initial.AcceptedPlan.CreateSnapshot();
            useful.Generation++;
            initial.AcceptIntent(useful, 11);
            RequireSetFixture(initial.RequestGeneration == 11
                && initial.CompletedWorkUnits == initial.TotalWorkUnits - 1
                && TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(initial, useful),
                "Useful adoption reset progress or compared required-only work against desired size.");
            var obsolete = useful.CreateSnapshot();
            obsolete.Levels[2].RequiredWindow = new TerrainHeightCacheWindow(
                new UnityEngine.Vector2Int(3, 0), UnityEngine.Vector2Int.one);
            RequireSetFixture(!TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(initial, obsolete)
                && !initial.MatchesContent(committed, overall, 8, 3, false)
                && !initial.MatchesContent(committed, overall, 7, 4, false),
                "Missing latest coverage or stale content/owner was accepted.");
            var completedSnapshot = TerrainAuthoringPreviewService.CaptureWorkerMetadata(initial);
            RequireSetFixture(completedSnapshot.LoadedGroupCount == 3 && completedSnapshot.MaterializedCount == 9
                && completedSnapshot.ComposedCount == 9, "Completed worker snapshot used incorrect denominators.");
            published = initial.TransferPreparedStates();
            initial.Dispose();
            foreach (var state in published)
                RequireSetFixture(state.CacheReady && state.ActiveCache.IsCompleteForActivation,
                    "Transferred caches were disposed with their transaction.");

            ValidateDirtyResidentCoverage(published, initial.AcceptedPlan, settings, committed, overall);

            // Local fine publication can be current while an unresolved coarse row is pending.
            published[0].ActiveAuthoringGeneration = TerrainAuthoringPreviewService.AuthoringGeneration;
            published[1].ActiveAuthoringGeneration = TerrainAuthoringPreviewService.AuthoringGeneration;
            published[1].PendingDirtyTiles.Add(firstWindow.OriginTile);
            var fineRow = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(0, published[0], initial.AcceptedPlan.Levels[0], null, null, true);
            var coarseRow = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(1, published[1], initial.AcceptedPlan.Levels[1], null, null, true);
            RequireSetFixture(fineRow.Active.Current && !coarseRow.Active.Current && coarseRow.PendingDirtyCount == 1,
                "A pending coarse obligation changed fine-row diagnostic currency.");
            published[0].ActiveAuthoringGeneration = published[1].ActiveAuthoringGeneration = 7;
            published[1].PendingDirtyTiles.Clear();
            ValidateSemanticHeightBinding(settings, initial.AcceptedPlan, published);
            RequireSetFixture(!TerrainAuthoringPreviewService.IsNativeAnalysisCacheEligible(published[2].ActiveCache,
                settings, 7, 7, committed, overall, firstWindow, firstWindow),
                "A complete coarse representation passed native analysis eligibility.");

            var movedWindow = new TerrainHeightCacheWindow(new UnityEngine.Vector2Int(1, 0),
                new UnityEngine.Vector2Int(3, 1));
            var moved = useful.CreateSnapshot();
            for (int i = 0; i < 3; i++)
            {
                moved.Levels[i].RequiredWindow = movedWindow;
                moved.Levels[i].DesiredWindow = movedWindow;
                sources[i] = published[i].ActiveCache;
                generations[i] = 7;
            }
            replacement = new TerrainAuthoringPreviewCacheSetTransition(moved,
                new[] { movedWindow, movedWindow, movedWindow }, new bool[3], sources, generations,
                TerrainAuthoringPreviewCachePublication.DisplayHeightSet, committed, overall,
                7, 12, 3, false, settings.HeightTileWorldSize);
            RequireSetFixture(!TerrainAuthoringPreviewService.IsRetainedReuseGloballyEligible(
                sources[0], sources[1], committed, overall, false), "Cross-stride final reuse was accepted.");
            calls += DriveSetFixture(replacement, settings, data, compositor, loader,
                TerrainAuthoringPreviewService.DefaultMaterializationsPerUpdate, ref observedResume);
            RequireSetFixture(replacement.RetainedCopies == 6 && replacement.SourceLoads == 1
                && replacement.MaterializedSlices == 3 && replacement.ComposedSlices == 3,
                "Shifted retained pages did not reuse their world-tile overlap.");
            var retainedTile = new UnityEngine.Vector2Int(1, 0);
            for (int i = 0; i < 3; i++)
            {
                var dest = replacement.Entries[i].Destination.StagingCache;
                bool ranges = sources[i].TryGetCompositeSliceRange(1, 0, out float a, out float b)
                    && dest.TryGetCompositeSliceRange(1, 0, out float c, out float d)
                    && a == c && b == d;
                RequireSetFixture(sources[i].GetSliceIndex(1, 0) == 1
                    && dest.GetSliceIndex(1, 0) == 0 && dest.IsSliceFinalCompositeReady(retainedTile)
                    && ranges, "Retained copy lost ranges/readiness after local slice remapping.");
            }
            next = replacement.TransferPreparedStates();
            replacement.Dispose();
            // Deliberately fail guard expansion after keeping a complete required set.
            var expanded = moved.CreateSnapshot();
            var guard = new TerrainHeightCacheWindow(UnityEngine.Vector2Int.zero,
                new UnityEngine.Vector2Int(4, 1));
            for (int i = 0; i < 3; i++)
            {
                expanded.Levels[i].DesiredWindow = guard;
                sources[i] = next[i].ActiveCache;
            }
            failed = new TerrainAuthoringPreviewCacheSetTransition(expanded,
                new[] { guard, guard, guard }, new[] { true, true, true }, sources, generations,
                TerrainAuthoringPreviewCachePublication.DisplayHeightSet, committed, overall,
                7, 13, 3, false, settings.HeightTileWorldSize);
            RequireSetFixture(TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(failed, expanded),
                "A guard candidate lost required coverage.");
            var becomesMandatory = expanded.CreateSnapshot();
            foreach (var level in becomesMandatory.Levels) level.RequiredWindow = guard;
            RequireSetFixture(TerrainAuthoringPreviewStreamingPolicy.IsCacheSetUseful(failed, becomesMandatory),
                "Guard expansion did not reevaluate coverage after navigation made it mandatory.");
            TerrainAuthoringPreviewService.NativeHeightSourceLoader failLoader =
                (WorldSettings world, UnityEngine.Vector2Int tile, out Texture2D source, out string error) =>
                { source = null; error = "Intentional missing source in transient validation."; return false; };
            bool failedAsExpected = false;
            // Nine retained destinations across three LODs exercise the global eight-copy limit.
            for (int i = 0; i < 32; i++)
            {
                bool advanced = TerrainAuthoringPreviewService.AdvanceHeightCacheSet(failed, settings, data,
                    compositor, System.Diagnostics.Stopwatch.StartNew(), 4.0, 8, failLoader, out _);
                RequireSetFixture(failed.LastUpdateCopies <= TerrainAuthoringPreviewService.DefaultRetainedCopiesPerUpdate,
                    "Retained-copy limits were applied per LOD instead of across the set.");
                if (!advanced) { failedAsExpected = true; break; }
            }
            RequireSetFixture(failedAsExpected && !failed.Complete, "A failed candidate claimed complete readiness.");
            RequireSetFixture(failed.FailedLevel >= 0 && failed.HasFailedTile && failed.FailedWindow == guard,
                "Source failure lost representation/tile attribution.");
            var failureSnapshot = TerrainAuthoringPreviewService.CaptureWorkerMetadata(failed);
            failed.Dispose();
            RequireSetFixture(failureSnapshot.Phase == TerrainAuthoringPreviewTransitionState.Failed
                && failureSnapshot.Purpose == TerrainAuthoringPreviewCachePublication.DisplayHeightSet,
                "Failure metadata was not copied before disposal.");
            RequireSetFixture(TerrainAuthoringPreviewService.CaptureDisplayIdentityForValidation() == liveIdentityBefore,
                "The isolated fixture changed a live display texture or physical window.");
            foreach (var state in next)
                RequireSetFixture(state.ActiveCache.IsCompleteForActivation,
                    "Failed expansion disposed the previous complete required cache.");
            detail = $"Three local representations; {calls} worker updates; grouped loads, global caps, " +
                "required-first readiness, atomic transfer, retained remapping, stale intent and failure cleanup passed.";
            return true;
        }
        catch (Exception exception)
        {
            detail = exception.Message;
            return false;
        }
        finally
        {
            initial?.Dispose();
            replacement?.Dispose();
            failed?.Dispose();
            if (published != null) foreach (var state in published) state.Dispose();
            if (next != null) foreach (var state in next) state.Dispose();
            compositor.Dispose();
            if (TerrainAuthoringPreviewService.CaptureDisplayIdentityForValidation() != liveIdentityBefore)
                Debug.LogError("Isolated Height set validation changed live display identities during cleanup.");
        }
    }

    private static int DriveSetFixture(TerrainAuthoringPreviewCacheSetTransition transaction,
        WorldSettings settings, TerrainAuthoringData data, TerrainHeightCompositor compositor,
        TerrainAuthoringPreviewService.NativeHeightSourceLoader loader, int materializationLimit,
        ref bool observedResume)
    {
        int completed = 0;
        for (int update = 0; update < 256; update++)
        {
            var held = transaction.CurrentSource;
            int oldLoads = transaction.SourceLoads;
            int oldGroup = transaction.GroupCursor;
            if (!TerrainAuthoringPreviewService.AdvanceHeightCacheSet(transaction, settings, data,
                compositor, System.Diagnostics.Stopwatch.StartNew(), 4.0, materializationLimit,
                loader, out string error)) throw new InvalidOperationException(error);
            RequireSetFixture(transaction.LastUpdateAllocations <= 1
                && transaction.LastUpdateCopies <= TerrainAuthoringPreviewService.DefaultRetainedCopiesPerUpdate
                && transaction.LastUpdateLoads <= TerrainAuthoringPreviewService.DefaultCommittedLoadsPerUpdate
                && transaction.LastUpdateMaterializations <= materializationLimit
                && transaction.LastUpdateCompositions <= TerrainAuthoringPreviewService.DefaultCompositionsPerUpdate,
                "A global per-update operation cap was exceeded.");
            RequireSetFixture(transaction.CompletedWorkUnits >= completed
                && transaction.CompletedWorkUnits <= transaction.TotalWorkUnits,
                "Operation progress regressed or exceeded planned work.");
            var snapshot = TerrainAuthoringPreviewService.CaptureWorkerMetadata(transaction);
            RequireSetFixture(snapshot.LoadedGroupCount == transaction.SourceLoads
                && snapshot.ComposedCount == transaction.ComposedSlices
                && snapshot.LastAllocations <= 1 && snapshot.LastLoads <= 1
                && snapshot.LastMaterializations <= materializationLimit
                && snapshot.LastCompositions <= TerrainAuthoringPreviewService.DefaultCompositionsPerUpdate,
                "Partial preparation snapshot lost whole-callback counts.");
            if (transaction.Entries[0].Destination.StagingCache != null && !transaction.Entries[0].Finalized)
                RequireSetFixture(!TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(0, null,
                    transaction.AcceptedPlan.Levels[0], transaction.Entries[0], null, true).Staging.Complete,
                    "Partial staging claimed complete publication.");
            completed = transaction.CompletedWorkUnits;
            if (held != null && transaction.GroupCursor == oldGroup)
            {
                RequireSetFixture(transaction.SourceLoads == oldLoads
                    && ReferenceEquals(held, transaction.CurrentSource), "A suspended source group reloaded its texture.");
                observedResume = true;
            }
            if (transaction.State == TerrainAuthoringPreviewTransitionState.ReadyToActivate)
            {
                RequireSetFixture(transaction.Complete, "One finalized LOD published an incomplete set.");
                return update + 1;
            }
            if (transaction.ComposedSlices > 0)
                RequireSetFixture(transaction.Entries[0].Transition.FullyComposedTileCount > 0,
                    "Coarse composition preceded available fine work.");
        }
        throw new InvalidOperationException("The bounded Height fixture did not finish.");
    }

    private static void RequireSetFixture(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static void ValidateCacheSetStaging()
    {
        bool passed = RunCacheSetWorkerFixture(false, out string detail, out bool blocked);
        if (blocked) AddBlocked("Complete Height cache set staging", detail);
        else if (passed) AddPass("Complete Height cache set staging", detail);
        else AddFail("Complete Height cache set staging", detail);
    }


    private static void ValidateDirtyResidentCoverage(TerrainAuthoringPreviewLodState[] states,
        TerrainAuthoringPreviewResidencyPlan plan, WorldSettings settings, string committed, string overall)
    {
        var state = states[0];
        var tile = plan.Levels[0].RequiredWindow.OriginTile;
        var geometrySettings = UnityEngine.Object.Instantiate(settings);
        TerrainAuthoringPreviewCacheSetTransition stale = null;
        try
        {
            // The isolated worker uses small representations independent of the
            // user's configured streaming cap. Only this temporary copy changes.
            geometrySettings.heightStreamingMaximumStride = Math.Max(settings.heightStreamingMaximumStride, states[2].SampleStride);
            state.CacheReady = false;
            state.PendingDirtyTiles.Add(tile);
            state.DirtyTargetGeneration = 8;
            var view = new TerrainAuthoringPreviewHeightCacheView(state);
            RequireSetFixture(TerrainAuthoringPreviewService.StateHasResidentCoverage(state, plan.Levels[0], geometrySettings, committed)
                && !view.IsCurrent && state.ActiveCache.IsSliceFinalCompositeReady(tile),
                "Queued content invalidated a published physical representation or claimed current content.");
            RequireSetFixture(!TerrainAuthoringPreviewService.StateHasResidentCoverage(state, plan.Levels[0], geometrySettings, committed + "-changed"),
                "A mismatched committed source passed physical coverage.");
            var incompatible = plan.CreateSnapshot().Levels[0];
            incompatible.SamplesPerSide++;
            RequireSetFixture(!TerrainAuthoringPreviewService.StateHasResidentCoverage(state, incompatible, geometrySettings, committed),
                "Incompatible representation geometry passed physical coverage.");
            state.WriteFailed = true;
            RequireSetFixture(!TerrainAuthoringPreviewService.StateHasResidentCoverage(state, plan.Levels[0], geometrySettings, committed),
                "An unsafe active write was treated as usable residency.");
            state.WriteFailed = false;
            var sources = new[] { states[0].ActiveCache, states[1].ActiveCache, states[2].ActiveCache };
            var targets = new[] { plan.Levels[0].RequiredWindow, plan.Levels[1].RequiredWindow, plan.Levels[2].RequiredWindow };
            stale = new TerrainAuthoringPreviewCacheSetTransition(plan, targets, new bool[3], sources, new long[] { 7, 7, 7 },
                TerrainAuthoringPreviewCachePublication.DisplayHeightSet, committed, overall, 8, 1, 3, false);
            foreach (var entry in stale.Entries)
                RequireSetFixture(entry.Transition.ReusableRetainedTiles.Count == 0,
                    "An older authoring generation was copied as final-current replacement content.");
            RequireSetFixture(!TerrainAuthoringPreviewService.IsRetainedReuseGloballyEligible(state.ActiveCache, state.ActiveCache,
                committed, overall + "-changed", false), "A stale overall signature passed retained content reuse.");
        }
        finally
        {
            stale?.Dispose();
            state.CacheReady = true;
            state.WriteFailed = false;
            state.PendingDirtyTiles.Remove(tile);
            state.DirtyTargetGeneration = 0;
            UnityEngine.Object.DestroyImmediate(geometrySettings);
        }
    }

    private static void ValidateSemanticHeightBinding(WorldSettings settings,
        TerrainAuthoringPreviewResidencyPlan plan, TerrainAuthoringPreviewLodState[] states)
    {
        Shader shader = Shader.Find("Custom/ClipmapTerrain");
        if (shader == null) throw new InvalidOperationException("The generated terrain shader is unavailable.");
        Material material = null; var objects = new List<GameObject>();
        var bindings = new List<TerrainClipmapRendererBinding>();
        var views = new TerrainAuthoringPreviewHeightCacheView[states.Length];
        try
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            for (int level = 0; level < states.Length; level++)
            {
                views[level] = new TerrainAuthoringPreviewHeightCacheView(states[level]);
                var obj = new GameObject("Transient Height role validation") { hideFlags = HideFlags.HideAndDontSave };
                objects.Add(obj); var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                TerrainClipmapRendererRole role;
                if (level == 0) role = TerrainClipmapRendererRole.CreateCenter();
                else TerrainClipmapRendererRole.TryCreateRing(level, out role);
                bindings.Add(new TerrainClipmapRendererBinding(renderer, role));
                if (level > 0)
                {
                    obj = new GameObject("Transient Height stitch validation") { hideFlags = HideFlags.HideAndDontSave };
                    objects.Add(obj); renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    TerrainClipmapRendererRole.TryCreateStitch(level - 1, level, out role);
                    bindings.Add(new TerrainClipmapRendererBinding(renderer, role));
                }
            }
            int protectedId = Shader.PropertyToID("_ClipmapTransitionOffset");
            foreach (var binding in bindings)
            {
                var block = new MaterialPropertyBlock(); block.SetVector(protectedId, new Vector4(11, 0, 13, 0));
                binding.Renderer.SetPropertyBlock(block);
            }
            var malformed = plan.CreateSnapshot();
            malformed.Levels[states.Length - 1].RequiredWindow = new TerrainHeightCacheWindow(new Vector2Int(999, 999), Vector2Int.one);
            RequireSetFixture(!TerrainAuthoringPreviewHeightBindingUtility.TryPreflight(settings, malformed, bindings, views, out _),
                "A missing coarse coverage page passed binding preflight.");
            foreach (var binding in bindings)
            {
                var block = new MaterialPropertyBlock(); binding.Renderer.GetPropertyBlock(block);
                RequireSetFixture(block.GetTexture(Shader.PropertyToID("_HeightCache")) == null
                    && block.GetVector(protectedId) == new Vector4(11, 0, 13, 0), "Failed binding preflight changed an earlier renderer.");
            }
            RequireSetFixture(TerrainAuthoringPreviewHeightBindingUtility.TryPreflight(settings, plan, bindings, views, out string error), error);
            TerrainAuthoringPreviewHeightBindingUtility.Bind(bindings, views);
            foreach (var binding in bindings)
            {
                var block = new MaterialPropertyBlock(); binding.Renderer.GetPropertyBlock(block);
                int owner = binding.Role.HeightOwnerLevel;
                float coarse = binding.Role.Kind == TerrainClipmapRendererKind.Stitch
                    ? states[binding.Role.CoarseLevel].SampleSpacing : states[owner].SampleSpacing;
                RequireSetFixture(block.GetTexture(Shader.PropertyToID("_HeightCache")) == states[owner].ActiveCache.HeightCache
                    && block.GetFloat(Shader.PropertyToID("_HeightNormalSampleSpacingCoarse")) == coarse
                    && block.GetVector(protectedId) == new Vector4(11, 0, 13, 0), "Semantic Height binding lost ownership, normal spacing or placement state.");
            }
            states[0].CacheReady = false;
            RequireSetFixture(!views[0].IsCurrent, "A borrowed Height view captured stale readiness.");
            states[0].CacheReady = true;
            TerrainAuthoringPreviewHeightBindingUtility.Disable(bindings);
            foreach (var binding in bindings)
            {
                var block = new MaterialPropertyBlock(); binding.Renderer.GetPropertyBlock(block);
                RequireSetFixture(block.GetTexture(Shader.PropertyToID("_HeightCache")) == null
                    && block.GetFloat(Shader.PropertyToID("_HeightCacheReady")) == 0
                    && block.GetVector(protectedId) == new Vector4(11, 0, 13, 0), "Height disable retained a texture or changed unrelated state.");
            }
        }
        finally
        {
            TerrainAuthoringPreviewHeightBindingUtility.Disable(bindings);
            foreach (var obj in objects) UnityEngine.Object.DestroyImmediate(obj);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

}


