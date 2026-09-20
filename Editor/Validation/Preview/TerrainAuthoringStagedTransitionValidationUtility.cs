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

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

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
                .TryGetActiveCacheForValidation(
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

        if (
            !TerrainAuthoringPreviewService
                .TryGetActiveResidentWindow(
                    out TerrainHeightCacheWindow activeWindow
                )
        )
        {
            AddBlocked(
                "Live staged-transition prerequisites",
                "The active resident window is unavailable."
            );

            return;
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
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

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

        bool activeIdentityUnchanged =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId
            ==
            activeTextureBefore
            &&
            TerrainAuthoringPreviewService
                .TryGetActiveResidentWindow(
                    out TerrainHeightCacheWindow activeAfter
                )
            &&
            activeAfter ==
                activeWindow;

        if (activeIdentityUnchanged)
        {
            AddPass(
                "Active cache unaffected by isolated staging",
                $"Active TextureID remained {activeTextureBefore}, window remained {activeWindow}."
            );
        }
        else
        {
            AddFail(
                "Active cache unaffected by isolated staging",
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
                "Package 03 validation did not mutate persistent authoring identity."
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
            "WorldMeshes Edit-Mode Height Cache Streaming - Package 03 Validation"
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

        builder.AppendLine(
            failed == 0
                ? "Package 03 staged window transitions: PASSED"
                : "Package 03 staged window transitions: FAILED"
        );

        if (failed == 0)
        {
            Debug.Log(
                builder.ToString()
            );
        }
        else
        {
            Debug.LogError(
                builder.ToString()
            );
        }
    }
}
