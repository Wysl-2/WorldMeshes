using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Stage 10 integration validation for the Stage 7 incremental preview
 * cache/invalidation architecture.
 *
 * This utility deliberately validates the real public preview
 * notification path rather than reaching into TerrainAuthoringPreviewCache.
 *
 * The only transient cache mutation performed is asking the preview
 * service to recompose selected tiles. Until the modifier compositor
 * exists, Stage 7 recomposition copies the already-committed tile data
 * back into the same GPU texture-array slices, so persistent authoring
 * data and visible terrain content are unchanged.
 */
public static class TerrainAuthoringPreviewValidationUtility
{
    // =====================================================
    // MENU
    // =====================================================

    private const int MaximumAsyncWaitCycles =
        30;

    // =====================================================
    // RESULT MODEL
    // =====================================================

    private enum ValidationOutcome
    {
        Pass,
        Fail,
        Blocked
    }

    private sealed class ValidationResult
    {
        public readonly string Name;

        public readonly ValidationOutcome Outcome;

        public readonly string Details;

        public ValidationResult(
            string name,
            ValidationOutcome outcome,
            string details
        )
        {
            Name =
                name;

            Outcome =
                outcome;

            Details =
                string.IsNullOrEmpty(
                    details
                )
                    ? ""
                    : details;
        }
    }

    // =====================================================
    // ACTIVE VALIDATION STATE
    // =====================================================

    private static readonly List<ValidationResult>
        results =
            new List<ValidationResult>();

    private static WorldSettings worldSettings;

    private static TerrainAuthoringData authoringData;

    private static bool validationRunning;

    private static bool persistentBaselineCaptured;

    private static int authoringRevisionBefore;

    private static string committedSignatureBefore =
        "";

    private static string overallSignatureBefore =
        "";

    // -----------------------------------------------------
    // Dirty-batch baseline
    // -----------------------------------------------------

    private static readonly List<Vector2Int>
        dirtyBatchTiles =
            new List<Vector2Int>();

    private static int dirtyTextureIdBefore;

    private static long dirtyFullBuildCountBefore;

    private static long dirtyTotalIncrementalUpdatesBefore;

    private static float dirtyGlobalMinimumBefore;

    private static float dirtyGlobalMaximumBefore;

    private static float dirtySliceMinimumBefore;

    private static float dirtySliceMaximumBefore;

    private static bool dirtySliceRangeBeforeValid;

    // -----------------------------------------------------
    // Hierarchy-rebind baseline
    // -----------------------------------------------------

    private static int hierarchyTextureIdBefore;

    private static long hierarchyFullBuildCountBefore;

    private static long hierarchyBindingCountBefore;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public static bool IsRunning
    {
        get
        {
            return
                validationRunning;
        }
    }

    // =====================================================
    // ENTRY POINT
    // =====================================================

    public static void ValidatePreviewResponsiveness()
    {
        if (validationRunning)
        {
            Debug.LogWarning(
                "WorldMeshes preview responsiveness validation is " +
                "already running."
            );

            return;
        }

        validationRunning =
            true;

        results.Clear();

        dirtyBatchTiles.Clear();

        worldSettings =
            null;

        authoringData =
            null;

        if (
            !TryValidatePrerequisites(
                out string prerequisiteError
            )
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                prerequisiteError
            );

            FinishValidation();

            return;
        }

        AddResult(
            "Validation prerequisites",
            ValidationOutcome.Pass,
            "Height Preview is enabled, the cache is Ready, the " +
            "committed authoring heightfield is current, and no " +
            "dirty composite tiles are already pending."
        );

        CapturePersistentAuthoringBaseline();

        RunSliceAddressingValidation();

        RunDirtyRegionValidation();

        RunSignatureRegressionValidation();

        StartDirtyBatchValidation();
    }

    // =====================================================
    // PREREQUISITES
    // =====================================================

    private static bool TryValidatePrerequisites(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Validation cannot run while entering or using " +
                "Play Mode.";

            return false;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            errorMessage =
                "Unity is currently compiling or updating assets. " +
                "Wait for the Editor to become idle and run the " +
                "validation again.";

            return false;
        }

        if (!TerrainAuthoringPreviewService.Enabled)
        {
            errorMessage =
                "Height Preview is disabled. Enable Height Preview " +
                "before running Stage 10 validation.";

            return false;
        }

        if (
            !TerrainAuthoringPreviewService.CacheReady
            ||
            TerrainAuthoringPreviewService.Status !=
                TerrainAuthoringPreviewStatus.Ready
        )
        {
            errorMessage =
                "The Height Preview cache is not Ready. Initialize " +
                "the committed authoring heightfield, run Setup / Repair " +
                "World Hierarchy if needed, and wait for Height Preview " +
                "status to become Ready.";

            return false;
        }

        if (
            TerrainAuthoringPreviewService
                .PendingDirtyTileCount !=
            0
        )
        {
            errorMessage =
                "The preview already has pending dirty composite " +
                "tiles. Wait for the scheduled preview refresh to " +
                "finish, then run validation again.";

            return false;
        }

        worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings could not be loaded from:\n" +
                WorldMeshesPaths.WorldSettingsAssetPath;

            return false;
        }

        authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData could not be loaded from:\n" +
                WorldMeshesPaths.TerrainAuthoringDataAssetPath;

            return false;
        }

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        worldSettings,
                        authoringData
                    );

        if (
            authoringStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            errorMessage =
                "The committed authoring heightfield is not Current. " +
                "Initialize/reinitialize it before Stage 10 " +
                "validation.";

            return false;
        }

        if (
            TerrainAuthoringPreviewService.CacheWidth <= 0
            ||
            TerrainAuthoringPreviewService.CacheHeight <= 0
            ||
            TerrainAuthoringPreviewService.CacheSliceCount <= 0
        )
        {
            errorMessage =
                "The preview reports an invalid cache layout.";

            return false;
        }

        return true;
    }

    // =====================================================
    // SLICE ADDRESSING
    // =====================================================

    private static void RunSliceAddressingValidation()
    {
        int width =
            TerrainAuthoringPreviewService.CacheWidth;

        int height =
            TerrainAuthoringPreviewService.CacheHeight;

        Vector2Int origin =
            TerrainAuthoringPreviewService.CacheOriginTile;

        int expectedSliceCount =
            width *
            height;

        int mappingFailures =
            0;

        int roundTripFailures =
            0;

        string firstMappingFailure =
            "";

        string firstRoundTripFailure =
            "";

        for (
            int localZ = 0;
            localZ < height;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < width;
                localX++
            )
            {
                int tileX =
                    origin.x +
                    localX;

                int tileZ =
                    origin.y +
                    localZ;

                int expectedSlice =
                    localX +
                    localZ *
                    width;

                bool found =
                    TerrainAuthoringPreviewService
                        .TryGetSliceIndex(
                            tileX,
                            tileZ,
                            out int actualSlice
                        );

                if (
                    !found
                    ||
                    actualSlice !=
                        expectedSlice
                )
                {
                    mappingFailures++;

                    if (
                        string.IsNullOrEmpty(
                            firstMappingFailure
                        )
                    )
                    {
                        firstMappingFailure =
                            $"Tile ({tileX}, {tileZ}) expected " +
                            $"slice {expectedSlice}, actual " +
                            $"{actualSlice}, found={found}.";
                    }

                    continue;
                }

                bool reverseFound =
                    TerrainAuthoringPreviewService
                        .TryGetTileCoordinate(
                            actualSlice,
                            out Vector2Int roundTripTile
                        );

                if (
                    !reverseFound
                    ||
                    roundTripTile.x !=
                        tileX
                    ||
                    roundTripTile.y !=
                        tileZ
                )
                {
                    roundTripFailures++;

                    if (
                        string.IsNullOrEmpty(
                            firstRoundTripFailure
                        )
                    )
                    {
                        firstRoundTripFailure =
                            $"Slice {actualSlice} expected tile " +
                            $"({tileX}, {tileZ}), actual " +
                            $"({roundTripTile.x}, " +
                            $"{roundTripTile.y}), " +
                            $"found={reverseFound}.";
                    }
                }
            }
        }

        AddResult(
            "Valid tile -> slice addressing",
            mappingFailures == 0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            mappingFailures == 0
                ? $"Validated all {expectedSliceCount:N0} cache " +
                    "tile coordinates."
                : $"{mappingFailures:N0} mapping failure(s). " +
                    firstMappingFailure
        );

        AddResult(
            "Slice -> tile round trip",
            roundTripFailures == 0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            roundTripFailures == 0
                ? $"All {expectedSliceCount:N0} valid mappings " +
                    "round-tripped to their original tile."
                : $"{roundTripFailures:N0} round-trip " +
                    "failure(s). " +
                    firstRoundTripFailure
        );

        ValidateInvalidSliceCoordinates(
            origin,
            width,
            height,
            expectedSliceCount
        );
    }

    private static void ValidateInvalidSliceCoordinates(
        Vector2Int origin,
        int width,
        int height,
        int sliceCount
    )
    {
        Vector2Int[] invalidTiles =
        {
            new Vector2Int(
                origin.x - 1,
                origin.y
            ),

            new Vector2Int(
                origin.x,
                origin.y - 1
            ),

            new Vector2Int(
                origin.x + width,
                origin.y
            ),

            new Vector2Int(
                origin.x,
                origin.y + height
            )
        };

        int failures =
            0;

        string firstFailure =
            "";

        foreach (
            Vector2Int tile
            in invalidTiles
        )
        {
            bool found =
                TerrainAuthoringPreviewService
                    .TryGetSliceIndex(
                        tile.x,
                        tile.y,
                        out int slice
                    );

            if (
                found
                ||
                slice !=
                    -1
            )
            {
                failures++;

                if (
                    string.IsNullOrEmpty(
                        firstFailure
                    )
                )
                {
                    firstFailure =
                        $"Invalid tile ({tile.x}, {tile.y}) " +
                        $"returned found={found}, slice={slice}.";
                }
            }
        }

        bool negativeSliceFound =
            TerrainAuthoringPreviewService
                .TryGetTileCoordinate(
                    -1,
                    out _
                );

        bool pastEndSliceFound =
            TerrainAuthoringPreviewService
                .TryGetTileCoordinate(
                    sliceCount,
                    out _
                );

        if (negativeSliceFound)
        {
            failures++;

            if (
                string.IsNullOrEmpty(
                    firstFailure
                )
            )
            {
                firstFailure =
                    "Slice -1 unexpectedly resolved to a tile.";
            }
        }

        if (pastEndSliceFound)
        {
            failures++;

            if (
                string.IsNullOrEmpty(
                    firstFailure
                )
            )
            {
                firstFailure =
                    $"Slice {sliceCount} unexpectedly resolved " +
                    "to a tile.";
            }
        }

        AddResult(
            "Invalid slice/tile addressing",
            failures == 0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            failures == 0
                ? "Out-of-cache tile coordinates and slice indices " +
                    "were rejected."
                : $"{failures:N0} invalid-address failure(s). " +
                    firstFailure
        );
    }

    // =====================================================
    // DIRTY REGION MAPPING
    // =====================================================

    private static void RunDirtyRegionValidation()
    {
        int tileGridWidth =
            Mathf.Max(
                1,
                worldSettings.HeightTileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                worldSettings.HeightTileGridHeight
            );

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        float sampleSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        float safeMargin =
            Mathf.Min(
                tileWorldSize *
                    0.2f,
                Mathf.Max(
                    sampleSpacing *
                        4f,
                    0.01f
                )
            );

        // -------------------------------------------------
        // Completely inside tile (0,0)
        // -------------------------------------------------

        Rect insideFirstTile =
            Rect.MinMaxRect(
                safeMargin,
                safeMargin,
                Mathf.Max(
                    safeMargin,
                    tileWorldSize -
                        safeMargin
                ),
                Mathf.Max(
                    safeMargin,
                    tileWorldSize -
                        safeMargin
                )
            );

        ValidateDirtyRegionSet(
            "Dirty bounds inside one tile",
            insideFirstTile,
            0,
            CreateExpectedSet(
                new Vector2Int(
                    0,
                    0
                )
            )
        );

        // -------------------------------------------------
        // Completely inside another tile
        // -------------------------------------------------

        if (tileGridWidth >= 2)
        {
            Rect insideSecondTile =
                Rect.MinMaxRect(
                    tileWorldSize +
                        safeMargin,
                    safeMargin,
                    (tileWorldSize * 2f) -
                        safeMargin,
                    tileWorldSize -
                        safeMargin
                );

            ValidateDirtyRegionSet(
                "Dirty bounds inside another tile",
                insideSecondTile,
                0,
                CreateExpectedSet(
                    new Vector2Int(
                        1,
                        0
                    )
                )
            );
        }
        else if (tileGridHeight >= 2)
        {
            Rect insideSecondTile =
                Rect.MinMaxRect(
                    safeMargin,
                    tileWorldSize +
                        safeMargin,
                    tileWorldSize -
                        safeMargin,
                    (tileWorldSize * 2f) -
                        safeMargin
                );

            ValidateDirtyRegionSet(
                "Dirty bounds inside another tile",
                insideSecondTile,
                0,
                CreateExpectedSet(
                    new Vector2Int(
                        0,
                        1
                    )
                )
            );
        }
        else
        {
            AddResult(
                "Dirty bounds inside another tile",
                ValidationOutcome.Blocked,
                "The current authoring heightfield contains only " +
                "one height tile."
            );
        }

        float boundaryHalfWidth =
            Mathf.Max(
                sampleSpacing *
                    0.25f,
                0.0001f
            );

        float interiorZ =
            Mathf.Min(
                tileWorldSize *
                    0.5f,
                Mathf.Max(
                    safeMargin,
                    0.01f
                )
            );

        // -------------------------------------------------
        // Cross X boundary
        // -------------------------------------------------

        if (tileGridWidth >= 2)
        {
            float boundaryX =
                tileWorldSize;

            Rect crossX =
                Rect.MinMaxRect(
                    boundaryX -
                        boundaryHalfWidth,
                    interiorZ -
                        boundaryHalfWidth,
                    boundaryX +
                        boundaryHalfWidth,
                    interiorZ +
                        boundaryHalfWidth
                );

            ValidateDirtyRegionSet(
                "Dirty bounds crossing X tile boundary",
                crossX,
                0,
                CreateExpectedSet(
                    new Vector2Int(
                        0,
                        0
                    ),
                    new Vector2Int(
                        1,
                        0
                    )
                )
            );
        }
        else
        {
            AddResult(
                "Dirty bounds crossing X tile boundary",
                ValidationOutcome.Blocked,
                "The current height-tile grid is only one tile wide."
            );
        }

        // -------------------------------------------------
        // Cross Z boundary
        // -------------------------------------------------

        if (tileGridHeight >= 2)
        {
            float boundaryZ =
                tileWorldSize;

            Rect crossZ =
                Rect.MinMaxRect(
                    interiorZ -
                        boundaryHalfWidth,
                    boundaryZ -
                        boundaryHalfWidth,
                    interiorZ +
                        boundaryHalfWidth,
                    boundaryZ +
                        boundaryHalfWidth
                );

            ValidateDirtyRegionSet(
                "Dirty bounds crossing Z tile boundary",
                crossZ,
                0,
                CreateExpectedSet(
                    new Vector2Int(
                        0,
                        0
                    ),
                    new Vector2Int(
                        0,
                        1
                    )
                )
            );
        }
        else
        {
            AddResult(
                "Dirty bounds crossing Z tile boundary",
                ValidationOutcome.Blocked,
                "The current height-tile grid is only one tile tall."
            );
        }

        // -------------------------------------------------
        // Cross X + Z corner
        // -------------------------------------------------

        if (
            tileGridWidth >= 2
            &&
            tileGridHeight >= 2
        )
        {
            float boundary =
                tileWorldSize;

            Rect crossCorner =
                Rect.MinMaxRect(
                    boundary -
                        boundaryHalfWidth,
                    boundary -
                        boundaryHalfWidth,
                    boundary +
                        boundaryHalfWidth,
                    boundary +
                        boundaryHalfWidth
                );

            ValidateDirtyRegionSet(
                "Dirty bounds crossing X+Z tile corner",
                crossCorner,
                0,
                CreateExpectedSet(
                    new Vector2Int(
                        0,
                        0
                    ),
                    new Vector2Int(
                        1,
                        0
                    ),
                    new Vector2Int(
                        0,
                        1
                    ),
                    new Vector2Int(
                        1,
                        1
                    )
                )
            );
        }
        else
        {
            AddResult(
                "Dirty bounds crossing X+Z tile corner",
                ValidationOutcome.Blocked,
                "The current height-tile grid does not contain a " +
                "2 x 2 tile corner."
            );
        }

        // -------------------------------------------------
        // Sample padding should conservatively cross boundary
        // -------------------------------------------------

        if (
            tileGridWidth >= 2
            &&
            sampleSpacing <
                tileWorldSize *
                0.45f
        )
        {
            float boundaryX =
                tileWorldSize;

            float unpaddedMaximumX =
                boundaryX -
                (sampleSpacing *
                    0.5f);

            float narrowHalfWidth =
                Mathf.Max(
                    sampleSpacing *
                        0.05f,
                    0.00001f
                );

            Rect nearBoundary =
                Rect.MinMaxRect(
                    unpaddedMaximumX -
                        narrowHalfWidth,
                    interiorZ -
                        narrowHalfWidth,
                    unpaddedMaximumX,
                    interiorZ +
                        narrowHalfWidth
                );

            HashSet<Vector2Int> unpadded =
                CollectDirtyTiles(
                    nearBoundary,
                    0
                );

            HashSet<Vector2Int> padded =
                CollectDirtyTiles(
                    nearBoundary,
                    1
                );

            bool unpaddedCorrect =
                SetsMatch(
                    unpadded,
                    CreateExpectedSet(
                        new Vector2Int(
                            0,
                            0
                        )
                    )
                );

            bool paddedCorrect =
                SetsMatch(
                    padded,
                    CreateExpectedSet(
                        new Vector2Int(
                            0,
                            0
                        ),
                        new Vector2Int(
                            1,
                            0
                        )
                    )
                );

            AddResult(
                "Dirty-region sample padding",
                unpaddedCorrect
                &&
                paddedCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                unpaddedCorrect
                &&
                paddedCorrect
                    ? "Without padding the region remained in tile " +
                        "(0,0); one native-sample padding correctly " +
                        "expanded the dirty set across the X boundary."
                    : "Expected unpadded {(0,0)} and padded " +
                        "{(0,0),(1,0)}.\n" +
                        $"Unpadded: {FormatTileSet(unpadded)}\n" +
                        $"Padded: {FormatTileSet(padded)}"
            );
        }
        else
        {
            AddResult(
                "Dirty-region sample padding",
                ValidationOutcome.Blocked,
                tileGridWidth < 2
                    ? "The current height-tile grid is only one tile wide."
                    : "Native height sample spacing is too large " +
                        "relative to tile size to isolate a one-axis " +
                        "sample-padding boundary test."
            );
        }

        // -------------------------------------------------
        // World-edge clamping
        // -------------------------------------------------

        float edgeEpsilon =
            Mathf.Min(
                tileWorldSize *
                    0.1f,
                Mathf.Max(
                    sampleSpacing,
                    0.0001f
                )
            );

        Rect partiallyOutsideMinimum =
            Rect.MinMaxRect(
                -edgeEpsilon,
                -edgeEpsilon,
                edgeEpsilon,
                edgeEpsilon
            );

        HashSet<Vector2Int> minimumEdgeTiles =
            CollectDirtyTiles(
                partiallyOutsideMinimum,
                0
            );

        bool minimumEdgeCorrect =
            SetsMatch(
                minimumEdgeTiles,
                CreateExpectedSet(
                    new Vector2Int(
                        0,
                        0
                    )
                )
            );

        Rect completelyOutsideMinimum =
            Rect.MinMaxRect(
                -tileWorldSize,
                -tileWorldSize,
                -edgeEpsilon,
                -edgeEpsilon
            );

        HashSet<Vector2Int> outsideMinimumTiles =
            CollectDirtyTiles(
                completelyOutsideMinimum,
                0
            );

        bool outsideMinimumCorrect =
            outsideMinimumTiles.Count ==
            0;

        int lastTileX =
            tileGridWidth -
            1;

        int lastTileZ =
            tileGridHeight -
            1;

        Rect partiallyOutsideMaximum =
            Rect.MinMaxRect(
                worldSize.x -
                    edgeEpsilon,
                worldSize.y -
                    edgeEpsilon,
                worldSize.x +
                    edgeEpsilon,
                worldSize.y +
                    edgeEpsilon
            );

        HashSet<Vector2Int> maximumEdgeTiles =
            CollectDirtyTiles(
                partiallyOutsideMaximum,
                0
            );

        bool maximumEdgeCorrect =
            SetsMatch(
                maximumEdgeTiles,
                CreateExpectedSet(
                    new Vector2Int(
                        lastTileX,
                        lastTileZ
                    )
                )
            );

        AddResult(
            "Dirty-region world-edge clamping",
            minimumEdgeCorrect
            &&
            outsideMinimumCorrect
            &&
            maximumEdgeCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            minimumEdgeCorrect
            &&
            outsideMinimumCorrect
            &&
            maximumEdgeCorrect
                ? "Partially outside bounds clamped to the logical " +
                    "world and completely outside bounds produced no " +
                    "dirty tiles."
                : "World-edge mapping did not match expectations.\n" +
                    $"Minimum edge: " +
                    $"{FormatTileSet(minimumEdgeTiles)}\n" +
                    $"Outside minimum: " +
                    $"{FormatTileSet(outsideMinimumTiles)}\n" +
                    $"Maximum edge: " +
                    $"{FormatTileSet(maximumEdgeTiles)}"
        );
    }

    private static void ValidateDirtyRegionSet(
        string testName,
        Rect worldXZRect,
        int samplePadding,
        HashSet<Vector2Int> expected
    )
    {
        HashSet<Vector2Int> actual =
            CollectDirtyTiles(
                worldXZRect,
                samplePadding
            );

        bool matches =
            SetsMatch(
                actual,
                expected
            );

        AddResult(
            testName,
            matches
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            matches
                ? $"Affected tiles: {FormatTileSet(actual)}"
                : $"Expected: {FormatTileSet(expected)}\n" +
                    $"Actual: {FormatTileSet(actual)}"
        );
    }

    private static HashSet<Vector2Int> CollectDirtyTiles(
        Rect worldXZRect,
        int samplePadding
    )
    {
        HashSet<Vector2Int> tiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingWorldRect(
                worldSettings,
                worldXZRect,
                tiles,
                samplePadding
            );

        return tiles;
    }

    // =====================================================
    // SIGNATURE REGRESSION
    // =====================================================

    private static void RunSignatureRegressionValidation()
    {
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

        string compatibilitySignature =
            TerrainAuthoringStateUtility
                .GetCurrentAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        bool signaturesValid =
            !string.IsNullOrEmpty(
                committedSignature
            )
            &&
            !string.IsNullOrEmpty(
                overallSignature
            );

        AddResult(
            "Committed/overall signature availability",
            signaturesValid
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            signaturesValid
                ? "Both committed-heightfield and overall-authoring " +
                    "signatures are available."
                : "One or both authoring signatures are empty."
        );

        bool previewSignaturesCurrent =
            TerrainAuthoringPreviewService
                .SourceCommittedHeightfieldSignature
            ==
            committedSignature
            &&
            TerrainAuthoringPreviewService
                .SourceOverallAuthoringSignature
            ==
            overallSignature;

        AddResult(
            "Preview source signatures",
            previewSignaturesCurrent
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            previewSignaturesCurrent
                ? "Ready preview cache source signatures match the " +
                    "current committed and overall authoring " +
                    "signatures."
                : "The Ready preview cache source signatures do not " +
                    "match current authoring state.\n" +
                    "Committed current/cache: " +
                    $"{ShortSignature(committedSignature)} / " +
                    $"{ShortSignature(TerrainAuthoringPreviewService.SourceCommittedHeightfieldSignature)}\n" +
                    "Overall current/cache: " +
                    $"{ShortSignature(overallSignature)} / " +
                    $"{ShortSignature(TerrainAuthoringPreviewService.SourceOverallAuthoringSignature)}"
        );

        bool compatibilityCorrect =
            !string.IsNullOrEmpty(
                overallSignature
            )
            &&
            compatibilitySignature ==
                overallSignature;

        AddResult(
            "Legacy signature compatibility alias",
            compatibilityCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            compatibilityCorrect
                ? "GetCurrentAuthoringSignature(...) matches " +
                    "GetOverallAuthoringSignature(...)."
                : "The legacy signature API no longer matches the " +
                    "overall authoring signature."
        );

        TerrainGenerationStateUtility.GenerationStatus
            runtimeStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    );

        if (
            runtimeStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            bool generatedSignatureMatches =
                !string.IsNullOrEmpty(
                    overallSignature
                )
                &&
                worldSettings
                    .lastGeneratedHeightSignature
                ==
                overallSignature;

            AddResult(
                "Runtime/generated-state signature behavior",
                generatedSignatureMatches
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                generatedSignatureMatches
                    ? "Runtime heightmaps are Current and " +
                        "WorldSettings.lastGeneratedHeightSignature " +
                        "matches the current overall authoring " +
                        "signature."
                    : "Runtime heightmaps report Current, but the " +
                        "stored generated height signature does not " +
                        "match the current overall authoring signature."
            );
        }
        else
        {
            AddResult(
                "Runtime/generated-state signature behavior",
                ValidationOutcome.Pass,
                $"Runtime heightmap status is {runtimeStatus}. " +
                "A stale/not-generated runtime output is valid for " +
                "this test; generation-state code did not falsely " +
                "report it as Current."
            );
        }
    }

    // =====================================================
    // DIRTY BATCH / INCREMENTAL CACHE VALIDATION
    // =====================================================

    private static void StartDirtyBatchValidation()
    {
        if (
            !TryChooseDirtyBatchTiles(
                dirtyBatchTiles
            )
        )
        {
            AddResult(
                "Dirty tile batching",
                ValidationOutcome.Blocked,
                "At least three valid preview cache slices are " +
                "required for the deduplication test."
            );

            AddResult(
                "Incremental cache invariants",
                ValidationOutcome.Blocked,
                "Dirty batching could not be exercised."
            );

            AddResult(
                "Per-slice/global range metadata",
                ValidationOutcome.Blocked,
                "Dirty batching could not be exercised."
            );

            StartHierarchyRebindValidation();

            return;
        }

        dirtyTextureIdBefore =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        dirtyFullBuildCountBefore =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount;

        dirtyTotalIncrementalUpdatesBefore =
            TerrainAuthoringPreviewService
                .TotalIncrementalSliceUpdates;

        dirtyGlobalMinimumBefore =
            TerrainAuthoringPreviewService
                .MinimumPreviewHeight;

        dirtyGlobalMaximumBefore =
            TerrainAuthoringPreviewService
                .MaximumPreviewHeight;

        Vector2Int rangeTile =
            dirtyBatchTiles[
                0
            ];

        dirtySliceRangeBeforeValid =
            TerrainAuthoringPreviewService
                .TryGetCompositeSliceRange(
                    rangeTile.x,
                    rangeTile.y,
                    out dirtySliceMinimumBefore,
                    out dirtySliceMaximumBefore
                );

        /*
         * Repeated notifications intentionally exercise the real
         * service-side HashSet batching path.
         */
        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[0]
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[0]
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[1]
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[1]
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[2]
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[2]
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                dirtyBatchTiles[2]
            );

        int queuedCount =
            TerrainAuthoringPreviewService
                .PendingDirtyTileCount;

        AddResult(
            "Dirty tile deduplication - queued set",
            queuedCount ==
                dirtyBatchTiles.Count
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            queuedCount ==
                dirtyBatchTiles.Count
                ? "Seven repeated notifications collapsed to three " +
                    "unique pending dirty tiles before refresh."
                : $"Expected 3 unique pending dirty tiles after " +
                    $"seven notifications; actual {queuedCount}."
        );

        QueueDelayCall(
            () =>
                WaitForDirtyBatchCompletion(
                    0
                )
        );
    }

    private static bool TryChooseDirtyBatchTiles(
        List<Vector2Int> output
    )
    {
        output.Clear();

        int width =
            TerrainAuthoringPreviewService.CacheWidth;

        int height =
            TerrainAuthoringPreviewService.CacheHeight;

        Vector2Int origin =
            TerrainAuthoringPreviewService.CacheOriginTile;

        if (
            width *
            height <
            3
        )
        {
            return false;
        }

        for (
            int localZ = 0;
            localZ < height
            &&
            output.Count < 3;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < width
                &&
                output.Count < 3;
                localX++
            )
            {
                output.Add(
                    new Vector2Int(
                        origin.x +
                            localX,
                        origin.y +
                            localZ
                    )
                );
            }
        }

        return
            output.Count ==
            3;
    }

    private static void WaitForDirtyBatchCompletion(
        int waitCycle
    )
    {
        if (!validationRunning)
        {
            return;
        }

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            AddResult(
                "Dirty tile batching - processed update",
                ValidationOutcome.Blocked,
                "Play Mode began while validation was running."
            );

            AddResult(
                "Incremental cache invariants",
                ValidationOutcome.Blocked,
                "Play Mode began while validation was running."
            );

            AddResult(
                "Per-slice/global range metadata",
                ValidationOutcome.Blocked,
                "Play Mode began while validation was running."
            );

            FinishValidation();

            return;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            if (
                waitCycle >=
                MaximumAsyncWaitCycles
            )
            {
                AddDirtyBatchTimeoutResults();

                StartHierarchyRebindValidation();

                return;
            }

            QueueDelayCall(
                () =>
                    WaitForDirtyBatchCompletion(
                        waitCycle +
                        1
                    )
            );

            return;
        }

        int pendingCount =
            TerrainAuthoringPreviewService
                .PendingDirtyTileCount;

        long totalUpdates =
            TerrainAuthoringPreviewService
                .TotalIncrementalSliceUpdates;

        long expectedTotalUpdates =
            dirtyTotalIncrementalUpdatesBefore +
            dirtyBatchTiles.Count;

        bool cacheIdentityChanged =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId
            !=
            dirtyTextureIdBefore;

        bool fullBuildCountChanged =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount
            !=
            dirtyFullBuildCountBefore;

        bool processingEvidence =
            totalUpdates >=
                expectedTotalUpdates
            ||
            cacheIdentityChanged
            ||
            fullBuildCountChanged
            ||
            TerrainAuthoringPreviewService.Status ==
                TerrainAuthoringPreviewStatus.Error;

        if (
            pendingCount == 0
            &&
            processingEvidence
        )
        {
            VerifyDirtyBatchResults();

            StartHierarchyRebindValidation();

            return;
        }

        if (
            waitCycle >=
            MaximumAsyncWaitCycles
        )
        {
            AddDirtyBatchTimeoutResults();

            StartHierarchyRebindValidation();

            return;
        }

        QueueDelayCall(
            () =>
                WaitForDirtyBatchCompletion(
                    waitCycle +
                    1
                )
        );
    }

    private static void VerifyDirtyBatchResults()
    {
        int expectedUniqueCount =
            dirtyBatchTiles.Count;

        long incrementalDelta =
            TerrainAuthoringPreviewService
                .TotalIncrementalSliceUpdates
            -
            dirtyTotalIncrementalUpdatesBefore;

        int lastIncrementalCount =
            TerrainAuthoringPreviewService
                .LastIncrementalSliceCount;

        bool batchingCorrect =
            lastIncrementalCount ==
                expectedUniqueCount
            &&
            incrementalDelta ==
                expectedUniqueCount;

        AddResult(
            "Dirty tile deduplication - processed update",
            batchingCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            batchingCorrect
                ? $"Seven repeated notifications produced exactly " +
                    $"{expectedUniqueCount} unique incremental slice " +
                    "updates."
                : $"Expected last/delta incremental count " +
                    $"{expectedUniqueCount}. Actual last=" +
                    $"{lastIncrementalCount}, delta=" +
                    $"{incrementalDelta}."
        );

        int textureIdAfter =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        long fullBuildCountAfter =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount;

        bool textureStable =
            textureIdAfter ==
            dirtyTextureIdBefore;

        bool buildCountStable =
            fullBuildCountAfter ==
            dirtyFullBuildCountBefore;

        AddResult(
            "Incremental cache Texture ID stability",
            textureStable
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            textureStable
                ? $"Cache Texture ID remained " +
                    $"{textureIdAfter}."
                : $"Cache Texture ID changed from " +
                    $"{dirtyTextureIdBefore} to " +
                    $"{textureIdAfter} during a dirty-slice update."
        );

        AddResult(
            "Incremental update avoids full rebuild",
            buildCountStable
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            buildCountStable
                ? $"Full Cache Builds remained " +
                    $"{fullBuildCountAfter}."
                : $"Full Cache Builds changed from " +
                    $"{dirtyFullBuildCountBefore} to " +
                    $"{fullBuildCountAfter} during an incremental " +
                    "update."
        );

        VerifyRangeMetadataAfterDirtyUpdate();
    }

    private static void VerifyRangeMetadataAfterDirtyUpdate()
    {
        Vector2Int rangeTile =
            dirtyBatchTiles[
                0
            ];

        bool rangeAfterValid =
            TerrainAuthoringPreviewService
                .TryGetCompositeSliceRange(
                    rangeTile.x,
                    rangeTile.y,
                    out float sliceMinimumAfter,
                    out float sliceMaximumAfter
                );

        float globalMinimumAfter =
            TerrainAuthoringPreviewService
                .MinimumPreviewHeight;

        float globalMaximumAfter =
            TerrainAuthoringPreviewService
                .MaximumPreviewHeight;

        bool beforeFinite =
            dirtySliceRangeBeforeValid
            &&
            IsFinite(
                dirtySliceMinimumBefore
            )
            &&
            IsFinite(
                dirtySliceMaximumBefore
            )
            &&
            dirtySliceMaximumBefore >=
                dirtySliceMinimumBefore;

        bool afterFinite =
            rangeAfterValid
            &&
            IsFinite(
                sliceMinimumAfter
            )
            &&
            IsFinite(
                sliceMaximumAfter
            )
            &&
            sliceMaximumAfter >=
                sliceMinimumAfter;

        bool sliceRangeStable =
            beforeFinite
            &&
            afterFinite
            &&
            Approximately(
                dirtySliceMinimumBefore,
                sliceMinimumAfter
            )
            &&
            Approximately(
                dirtySliceMaximumBefore,
                sliceMaximumAfter
            );

        AddResult(
            "Per-slice height-range metadata",
            sliceRangeStable
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            sliceRangeStable
                ? $"Tile ({rangeTile.x}, {rangeTile.y}) range " +
                    $"remained {sliceMinimumAfter:R} -> " +
                    $"{sliceMaximumAfter:R} after copying the same " +
                    "committed tile back into its composite slice."
                : "Per-slice range metadata was missing, invalid, " +
                    "or changed after an identity recomposition.\n" +
                    $"Before valid={dirtySliceRangeBeforeValid}, " +
                    $"range={dirtySliceMinimumBefore:R} -> " +
                    $"{dirtySliceMaximumBefore:R}\n" +
                    $"After valid={rangeAfterValid}, " +
                    $"range={sliceMinimumAfter:R} -> " +
                    $"{sliceMaximumAfter:R}"
        );

        bool globalBeforeValid =
            IsFinite(
                dirtyGlobalMinimumBefore
            )
            &&
            IsFinite(
                dirtyGlobalMaximumBefore
            )
            &&
            dirtyGlobalMaximumBefore >=
                dirtyGlobalMinimumBefore;

        bool globalAfterValid =
            IsFinite(
                globalMinimumAfter
            )
            &&
            IsFinite(
                globalMaximumAfter
            )
            &&
            globalMaximumAfter >=
                globalMinimumAfter;

        bool globalRangeStable =
            globalBeforeValid
            &&
            globalAfterValid
            &&
            Approximately(
                dirtyGlobalMinimumBefore,
                globalMinimumAfter
            )
            &&
            Approximately(
                dirtyGlobalMaximumBefore,
                globalMaximumAfter
            );

        AddResult(
            "Global preview height-range metadata",
            globalRangeStable
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            globalRangeStable
                ? $"Global range remained " +
                    $"{globalMinimumAfter:R} -> " +
                    $"{globalMaximumAfter:R}."
                : "Global preview height range was invalid or " +
                    "changed after identity recomposition.\n" +
                    $"Before: {dirtyGlobalMinimumBefore:R} -> " +
                    $"{dirtyGlobalMaximumBefore:R}\n" +
                    $"After: {globalMinimumAfter:R} -> " +
                    $"{globalMaximumAfter:R}"
        );
    }

    private static void AddDirtyBatchTimeoutResults()
    {
        string details =
            "The scheduled incremental preview update did not " +
            $"complete within {MaximumAsyncWaitCycles} Editor " +
            "delay-call cycles.\n" +
            $"Status: {TerrainAuthoringPreviewService.StatusLabel}\n" +
            $"Pending Dirty Tiles: " +
            $"{TerrainAuthoringPreviewService.PendingDirtyTileCount}\n" +
            $"Last Incremental Slice Count: " +
            $"{TerrainAuthoringPreviewService.LastIncrementalSliceCount}";

        AddResult(
            "Dirty tile deduplication - processed update",
            ValidationOutcome.Fail,
            details
        );

        AddResult(
            "Incremental cache invariants",
            ValidationOutcome.Fail,
            details
        );

        AddResult(
            "Per-slice/global range metadata",
            ValidationOutcome.Fail,
            details
        );
    }

    // =====================================================
    // HIERARCHY-ONLY REBIND VALIDATION
    // =====================================================

    private static void StartHierarchyRebindValidation()
    {
        if (!validationRunning)
        {
            return;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out Transform clipmapRoot,
                    out string lookupError
                )
        )
        {
            AddResult(
                "Hierarchy-only preview rebind",
                ValidationOutcome.Blocked,
                lookupError
            );

            FinishValidation();

            return;
        }

        if (clipmapRoot == null)
        {
            AddResult(
                "Hierarchy-only preview rebind",
                ValidationOutcome.Blocked,
                "WorldRoot/Clipmap is not present in the active scene."
            );

            FinishValidation();

            return;
        }

        hierarchyTextureIdBefore =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        hierarchyFullBuildCountBefore =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount;

        hierarchyBindingCountBefore =
            TerrainAuthoringPreviewService
                .DiagnosticBindingApplyCount;

        TerrainAuthoringPreviewService
            .NotifyClipmapHierarchyChanged();

        QueueDelayCall(
            () =>
                WaitForHierarchyRebindCompletion(
                    0
                )
        );
    }

    private static void WaitForHierarchyRebindCompletion(
        int waitCycle
    )
    {
        if (!validationRunning)
        {
            return;
        }

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            AddResult(
                "Hierarchy-only preview rebind",
                ValidationOutcome.Blocked,
                "Play Mode began while validation was running."
            );

            FinishValidation();

            return;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            if (
                waitCycle >=
                MaximumAsyncWaitCycles
            )
            {
                AddHierarchyTimeoutResult();

                FinishValidation();

                return;
            }

            QueueDelayCall(
                () =>
                    WaitForHierarchyRebindCompletion(
                        waitCycle +
                        1
                    )
            );

            return;
        }

        bool bindingApplied =
            TerrainAuthoringPreviewService
                .DiagnosticBindingApplyCount
            >
            hierarchyBindingCountBefore;

        if (bindingApplied)
        {
            VerifyHierarchyRebindResults();

            FinishValidation();

            return;
        }

        if (
            TerrainAuthoringPreviewService.Status ==
            TerrainAuthoringPreviewStatus.Error
        )
        {
            AddResult(
                "Hierarchy-only preview rebind",
                ValidationOutcome.Fail,
                "Preview entered Error status while processing the " +
                "hierarchy-only rebind.\n" +
                TerrainAuthoringPreviewService.StatusMessage
            );

            FinishValidation();

            return;
        }

        if (
            waitCycle >=
            MaximumAsyncWaitCycles
        )
        {
            AddHierarchyTimeoutResult();

            FinishValidation();

            return;
        }

        QueueDelayCall(
            () =>
                WaitForHierarchyRebindCompletion(
                    waitCycle +
                    1
                )
        );
    }

    private static void VerifyHierarchyRebindResults()
    {
        int textureIdAfter =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        long fullBuildCountAfter =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount;

        bool textureStable =
            textureIdAfter ==
            hierarchyTextureIdBefore;

        bool buildCountStable =
            fullBuildCountAfter ==
            hierarchyFullBuildCountBefore;

        bool previewReady =
            TerrainAuthoringPreviewService.Status ==
                TerrainAuthoringPreviewStatus.Ready
            &&
            TerrainAuthoringPreviewService.CacheReady;

        bool passed =
            textureStable
            &&
            buildCountStable
            &&
            previewReady;

        AddResult(
            "Hierarchy-only preview rebind",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "NotifyClipmapHierarchyChanged() completed a real " +
                    "preview binding transaction while preserving the " +
                    $"same Cache Texture ID " +
                    $"({textureIdAfter}) and Full Cache Builds count " +
                    $"({fullBuildCountAfter})."
                : "Hierarchy-only rebind violated one or more " +
                    "invariants.\n" +
                    $"Texture ID before/after: " +
                    $"{hierarchyTextureIdBefore} / " +
                    $"{textureIdAfter}\n" +
                    $"Full builds before/after: " +
                    $"{hierarchyFullBuildCountBefore} / " +
                    $"{fullBuildCountAfter}\n" +
                    $"Preview Ready: {previewReady}"
        );
    }

    private static void AddHierarchyTimeoutResult()
    {
        AddResult(
            "Hierarchy-only preview rebind",
            ValidationOutcome.Fail,
            "NotifyClipmapHierarchyChanged() did not produce a " +
            "successful cache-binding transaction within " +
            $"{MaximumAsyncWaitCycles} Editor delay-call cycles."
        );
    }

    // =====================================================
    // PERSISTENT AUTHORING STATE SAFETY
    // =====================================================

    private static void CapturePersistentAuthoringBaseline()
    {
        persistentBaselineCaptured =
            authoringData != null
            &&
            worldSettings != null;

        if (!persistentBaselineCaptured)
        {
            return;
        }

        authoringRevisionBefore =
            authoringData.authoringRevision;

        committedSignatureBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        overallSignatureBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );
    }

    private static void AddPersistentAuthoringSafetyResult()
    {
        if (!persistentBaselineCaptured)
        {
            return;
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

        bool unchanged =
            authoringData != null
            &&
            authoringData.authoringRevision ==
                authoringRevisionBefore
            &&
            committedAfter ==
                committedSignatureBefore
            &&
            overallAfter ==
                overallSignatureBefore;

        AddResult(
            "Persistent authoring data unchanged",
            unchanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            unchanged
                ? "Validation did not change authoringRevision or " +
                    "the committed/overall authoring signatures."
                : "Persistent authoring state changed while the " +
                    "validation was running.\n" +
                    $"authoringRevision before/after: " +
                    $"{authoringRevisionBefore} / " +
                    $"{(authoringData != null ? authoringData.authoringRevision : -1)}\n" +
                    $"Committed before/after: " +
                    $"{ShortSignature(committedSignatureBefore)} / " +
                    $"{ShortSignature(committedAfter)}\n" +
                    $"Overall before/after: " +
                    $"{ShortSignature(overallSignatureBefore)} / " +
                    $"{ShortSignature(overallAfter)}"
        );
    }

    // =====================================================
    // RESULTS
    // =====================================================

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details
    )
    {
        results.Add(
            new ValidationResult(
                name,
                outcome,
                details
            )
        );
    }

    private static void FinishValidation()
    {
        if (!validationRunning)
        {
            return;
        }

        validationRunning =
            false;

        AddPersistentAuthoringSafetyResult();

        int passCount =
            0;

        int failCount =
            0;

        int blockedCount =
            0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Preview Responsiveness Validation"
        );

        builder.AppendLine(
            "=============================================="
        );

        builder.AppendLine();

        foreach (
            ValidationResult result
            in results
        )
        {
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                {
                    passCount++;

                    break;
                }

                case ValidationOutcome.Fail:
                {
                    failCount++;

                    break;
                }

                default:
                {
                    blockedCount++;

                    break;
                }
            }

            builder.Append(
                OutcomeLabel(
                    result.Outcome
                )
            );

            builder.Append(
                " - "
            );

            builder.AppendLine(
                result.Name
            );

            if (
                !string.IsNullOrEmpty(
                    result.Details
                )
            )
            {
                string[] detailLines =
                    result.Details.Replace(
                        "\r\n",
                        "\n"
                    )
                    .Split(
                        '\n'
                    );

                foreach (
                    string detailLine
                    in detailLines
                )
                {
                    builder.Append(
                        "       "
                    );

                    builder.AppendLine(
                        detailLine
                    );
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine(
            "----------------------------------------------"
        );

        builder.AppendLine(
            $"{passCount} passed"
        );

        builder.AppendLine(
            $"{failCount} failed"
        );

        builder.AppendLine(
            $"{blockedCount} blocked"
        );

        builder.AppendLine();

        if (
            failCount == 0
            &&
            blockedCount == 0
        )
        {
            builder.AppendLine(
                "Stage 7 integration validation: PASSED"
            );

            builder.AppendLine(
                "The incremental preview cache/invalidation " +
                "foundation is ready for the modifier data layer."
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failCount > 0)
        {
            builder.AppendLine(
                "Stage 7 integration validation: FAILED"
            );

            builder.AppendLine(
                "Resolve the failed Stage 10 invariants before " +
                "building modifier composition on top of the " +
                "preview cache."
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Stage 7 integration validation: BLOCKED"
            );

            builder.AppendLine(
                "No validation failure was detected, but one or " +
                "more checks could not run in the current editor/" +
                "world configuration."
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }

        worldSettings =
            null;

        authoringData =
            null;

        dirtyBatchTiles.Clear();

        persistentBaselineCaptured =
            false;

        committedSignatureBefore =
            "";

        overallSignatureBefore =
            "";
    }

    // =====================================================
    // COLLECTION HELPERS
    // =====================================================

    private static HashSet<Vector2Int> CreateExpectedSet(
        params Vector2Int[] coordinates
    )
    {
        HashSet<Vector2Int> result =
            new HashSet<Vector2Int>();

        if (coordinates == null)
        {
            return result;
        }

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            result.Add(
                coordinate
            );
        }

        return result;
    }

    private static bool SetsMatch(
        HashSet<Vector2Int> actual,
        HashSet<Vector2Int> expected
    )
    {
        if (
            actual == null
            ||
            expected == null
            ||
            actual.Count !=
                expected.Count
        )
        {
            return false;
        }

        return
            actual.SetEquals(
                expected
            );
    }

    private static string FormatTileSet(
        HashSet<Vector2Int> tiles
    )
    {
        if (
            tiles == null
            ||
            tiles.Count == 0
        )
        {
            return
                "{}";
        }

        List<Vector2Int> sorted =
            new List<Vector2Int>(
                tiles
            );

        sorted.Sort(
            (a, b) =>
            {
                int zComparison =
                    a.y.CompareTo(
                        b.y
                    );

                if (zComparison != 0)
                {
                    return
                        zComparison;
                }

                return
                    a.x.CompareTo(
                        b.x
                    );
            }
        );

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "{"
        );

        for (
            int index = 0;
            index < sorted.Count;
            index++
        )
        {
            if (index > 0)
            {
                builder.Append(
                    ", "
                );
            }

            builder.Append(
                "("
            );

            builder.Append(
                sorted[index].x
            );

            builder.Append(
                ","
            );

            builder.Append(
                sorted[index].y
            );

            builder.Append(
                ")"
            );
        }

        builder.Append(
            "}"
        );

        return
            builder.ToString();
    }

    // =====================================================
    // ASYNC HELPER
    // =====================================================

    private static void QueueDelayCall(
        Action action
    )
    {
        if (action == null)
        {
            return;
        }

        EditorApplication.delayCall +=
            () =>
            {
                if (!validationRunning)
                {
                    return;
                }

                action();
            };
    }

    private static string ShortSignature(
        string signature
    )
    {
        if (
            string.IsNullOrEmpty(
                signature
            )
        )
        {
            return
                "(none)";
        }

        if (
            signature.Length <=
            12
        )
        {
            return
                signature;
        }

        return
            signature.Substring(
                0,
                12
            )
            +
            "...";
    }

    // =====================================================
    // FLOAT HELPERS
    // =====================================================

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

    private static bool Approximately(
        float a,
        float b
    )
    {
        float tolerance =
            Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(
                        a
                    ),
                    Mathf.Abs(
                        b
                    )
                )
                *
                0.000001f
            );

        return
            Mathf.Abs(
                a -
                b
            )
            <=
            tolerance;
    }

    private static string OutcomeLabel(
        ValidationOutcome outcome
    )
    {
        switch (outcome)
        {
            case ValidationOutcome.Pass:
                return
                    "PASS";

            case ValidationOutcome.Fail:
                return
                    "FAIL";

            default:
                return
                    "BLOCKED";
        }
    }
}
