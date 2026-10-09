using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Validation for Scene View driven edit-mode height residency.
 *
 * Pure synthetic tests prove sample-safe tile addressing, guard fitting,
 * world-edge behavior, and bounded scaling without moving the user's Scene
 * View. Live checks inspect the current PreviewService residency only when the
 * required authoring/preview prerequisites are available.
 */
public static class TerrainAuthoringSceneViewResidencyValidationUtility
{
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

    public static void ValidateSceneViewResidency()
    {
        RequestValidation();
    }

    public static void RequestValidation()
    {
        if (
            validationRunning
            ||
            validationScheduled
        )
        {
            Debug.LogWarning(
                "WorldMeshes Scene View residency validation is " +
                "already running."
            );

            return;
        }

        validationScheduled =
            true;

        lastRunSummary =
            TerrainValidationRunSummary.CreateRunning(
                "Scene View residency validation is running."
            );

        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall +=
            RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -=
            RunScheduledValidation;

        if (!validationScheduled)
        {
            return;
        }

        validationScheduled =
            false;

        if (validationRunning)
        {
            return;
        }

        validationRunning =
            true;

        results.Clear();

        try
        {
            RunShaderAddressingValidation();
            RunSamplePaddingValidation();
            RunGuardAndWorldEdgeValidation();
            RunSmallWorldValidation();
            RunBoundedScalingValidation();
            RunMultiresolutionResidencyPlanValidation();
            RunGeographicDemandValidation();
            RunMultiresolutionBoundedScalingValidation();
            RunDiagnosticsProjectionValidation();
            RunOperationalFeedbackValidation();
            RunDirtyScratchOwnershipValidation();
            RunLivePreviewValidation();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }

        FinishValidation();
    }

    // =====================================================
    // PURE ADDRESSING
    // =====================================================

    private static void RunShaderAddressingValidation()
    {
        WorldSettings settings =
            CreateSyntheticSettings(
                256,
                256
            );

        try
        {
            float sampleSpacing =
                TerrainAuthoringPreviewResidencyUtility
                    .CalculateHeightSampleSpacing(
                        settings
                    );

            int samplesPerSide =
                settings.HeightTileSamplesPerSide;

            int tileCount =
                settings.HeightTileGridWidth;

            float worldSize =
                TerrainClipmapLayoutUtility
                    .CalculateWorldSizeXZ(
                        settings
                    )
                    .x;

            float tileWorldSize =
                settings.HeightTileWorldSize;

            float firstTileTransition =
                tileWorldSize -
                sampleSpacing *
                0.5f;

            int atWorldMinimum =
                TerrainAuthoringPreviewResidencyUtility
                    .WorldPositionToTileCoordinate(
                        0f,
                        worldSize,
                        sampleSpacing,
                        samplesPerSide,
                        tileCount
                    );

            int beforeTransition =
                TerrainAuthoringPreviewResidencyUtility
                    .WorldPositionToTileCoordinate(
                        firstTileTransition -
                            sampleSpacing *
                            0.01f,
                        worldSize,
                        sampleSpacing,
                        samplesPerSide,
                        tileCount
                    );

            int atTransition =
                TerrainAuthoringPreviewResidencyUtility
                    .WorldPositionToTileCoordinate(
                        firstTileTransition,
                        worldSize,
                        sampleSpacing,
                        samplesPerSide,
                        tileCount
                    );

            int atGeometricBoundary =
                TerrainAuthoringPreviewResidencyUtility
                    .WorldPositionToTileCoordinate(
                        tileWorldSize,
                        worldSize,
                        sampleSpacing,
                        samplesPerSide,
                        tileCount
                    );

            int atWorldMaximum =
                TerrainAuthoringPreviewResidencyUtility
                    .WorldPositionToTileCoordinate(
                        worldSize,
                        worldSize,
                        sampleSpacing,
                        samplesPerSide,
                        tileCount
                    );

            bool passed =
                Mathf.Approximately(
                    sampleSpacing,
                    1f
                )
                &&
                atWorldMinimum ==
                    0
                &&
                beforeTransition ==
                    0
                &&
                atTransition ==
                    1
                &&
                atGeometricBoundary ==
                    1
                &&
                atWorldMaximum ==
                    tileCount -
                    1;

            AddResult(
                "Shader-compatible world-to-tile addressing",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Spacing={sampleSpacing:R}, transition={firstTileTransition:R}, " +
                $"mapped: min={atWorldMinimum}, before={beforeTransition}, " +
                $"transition={atTransition}, boundary={atGeometricBoundary}, " +
                $"worldMax={atWorldMaximum}."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                settings
            );
        }
    }

    // =====================================================
    // SAMPLE PADDING
    // =====================================================

    private static void RunSamplePaddingValidation()
    {
        WorldSettings settings =
            CreateSyntheticSettings(
                256,
                256
            );

        try
        {
            float tileWorldSize =
                settings.HeightTileWorldSize;

            Vector2 minimumXZ =
                new Vector2(
                    tileWorldSize,
                    tileWorldSize
                );

            Vector2 maximumXZ =
                new Vector2(
                    tileWorldSize *
                        2f,
                    tileWorldSize *
                        2f
                );

            bool zeroSucceeded =
                TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateRequiredWindow(
                        settings,
                        minimumXZ,
                        maximumXZ,
                        0,
                        out TerrainHeightCacheWindow zeroPadding,
                        out string zeroError
                    );

            bool oneSucceeded =
                TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateRequiredWindow(
                        settings,
                        minimumXZ,
                        maximumXZ,
                        1,
                        out TerrainHeightCacheWindow onePadding,
                        out string oneError
                    );

            bool passed =
                zeroSucceeded
                &&
                oneSucceeded
                &&
                zeroPadding.OriginTile ==
                    new Vector2Int(
                        1,
                        1
                    )
                &&
                zeroPadding.Size ==
                    new Vector2Int(
                        2,
                        2
                    )
                &&
                onePadding.OriginTile ==
                    Vector2Int.zero
                &&
                onePadding.Contains(
                    zeroPadding
                );

            AddResult(
                "One-native-sample safety padding",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? $"Padding 0={zeroPadding}; padding 1={onePadding}."
                    : "Padding calculation failed. " +
                      $"Padding0Error={zeroError}; Padding1Error={oneError}."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                settings
            );
        }
    }

    // =====================================================
    // GUARD + EDGE FITTING
    // =====================================================

    private static void RunGuardAndWorldEdgeValidation()
    {
        WorldSettings settings =
            CreateSyntheticSettings(
                256,
                256
            );

        try
        {
            float tileWorldSize =
                settings.HeightTileWorldSize;

            Vector2 interiorMinimum =
                new Vector2(
                    10f *
                        tileWorldSize,
                    20f *
                        tileWorldSize
                );

            Vector2 interiorMaximum =
                new Vector2(
                    18f *
                        tileWorldSize -
                        1f,
                    29f *
                        tileWorldSize -
                        1f
                );

            bool interiorSucceeded =
                TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateResidentWindow(
                        settings,
                        interiorMinimum,
                        interiorMaximum,
                        0,
                        1,
                        out TerrainHeightCacheWindow interiorWindow,
                        out string interiorError
                    );

            bool interiorCorrect =
                interiorSucceeded
                &&
                interiorWindow ==
                    new TerrainHeightCacheWindow(
                        new Vector2Int(
                            9,
                            19
                        ),
                        new Vector2Int(
                            10,
                            11
                        )
                    );

            AddResult(
                "One-tile residency guard expansion",
                interiorCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                interiorSucceeded
                    ? $"Resident window: {interiorWindow}."
                    : interiorError
            );

            int worldWidth =
                settings.HeightTileGridWidth;

            int worldHeight =
                settings.HeightTileGridHeight;

            bool leftSucceeded =
                TryCalculateWindowForInclusiveTiles(
                    settings,
                    0,
                    20,
                    7,
                    28,
                    out TerrainHeightCacheWindow leftWindow,
                    out string leftError
                );

            bool rightSucceeded =
                TryCalculateWindowForInclusiveTiles(
                    settings,
                    worldWidth -
                        8,
                    20,
                    worldWidth -
                        1,
                    28,
                    out TerrainHeightCacheWindow rightWindow,
                    out string rightError
                );

            bool bottomSucceeded =
                TryCalculateWindowForInclusiveTiles(
                    settings,
                    10,
                    0,
                    17,
                    8,
                    out TerrainHeightCacheWindow bottomWindow,
                    out string bottomError
                );

            bool topSucceeded =
                TryCalculateWindowForInclusiveTiles(
                    settings,
                    10,
                    worldHeight -
                        9,
                    17,
                    worldHeight -
                        1,
                    out TerrainHeightCacheWindow topWindow,
                    out string topError
                );

            bool cornerSucceeded =
                TryCalculateWindowForInclusiveTiles(
                    settings,
                    worldWidth -
                        8,
                    worldHeight -
                        9,
                    worldWidth -
                        1,
                    worldHeight -
                        1,
                    out TerrainHeightCacheWindow cornerWindow,
                    out string cornerError
                );

            bool edgesCorrect =
                leftSucceeded
                &&
                rightSucceeded
                &&
                bottomSucceeded
                &&
                topSucceeded
                &&
                cornerSucceeded
                &&
                leftWindow.OriginTile.x ==
                    0
                &&
                rightWindow.MaximumExclusive.x ==
                    worldWidth
                &&
                bottomWindow.OriginTile.y ==
                    0
                &&
                topWindow.MaximumExclusive.y ==
                    worldHeight
                &&
                cornerWindow.MaximumExclusive ==
                    new Vector2Int(
                        worldWidth,
                        worldHeight
                    );

            AddResult(
                "Resident window edge/corner fitting",
                edgesCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                edgesCorrect
                    ? $"Left={leftWindow}; Right={rightWindow}; " +
                      $"Bottom={bottomWindow}; Top={topWindow}; " +
                      $"Corner={cornerWindow}."
                    : $"Left={leftError}; Right={rightError}; " +
                      $"Bottom={bottomError}; Top={topError}; " +
                      $"Corner={cornerError}."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                settings
            );
        }
    }

    // =====================================================
    // SMALL WORLD
    // =====================================================

    private static void RunSmallWorldValidation()
    {
        WorldSettings settings =
            CreateSyntheticSettings(
                8,
                8
            );

        try
        {
            bool succeeded =
                TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateCanonicalResidentWindow(
                        settings,
                        out TerrainHeightCacheWindow residentWindow,
                        out string errorMessage
                    );

            Vector2Int expectedSize =
                new Vector2Int(
                    settings.HeightTileGridWidth,
                    settings.HeightTileGridHeight
                );

            bool passed =
                succeeded
                &&
                residentWindow.OriginTile ==
                    Vector2Int.zero
                &&
                residentWindow.Size ==
                    expectedSize;

            AddResult(
                "Small-world residency fallback",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                succeeded
                    ? $"Resident={residentWindow}, worldGrid={expectedSize}."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                settings
            );
        }
    }

    // =====================================================
    // BOUNDED SCALING
    // =====================================================

    private static void RunBoundedScalingValidation()
    {
        WorldSettings small =
            CreateSyntheticSettings(
                128,
                128
            );

        WorldSettings large =
            CreateSyntheticSettings(
                4096,
                4096
            );

        try
        {
            bool smallSucceeded =
                TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateCanonicalResidentWindow(
                        small,
                        out TerrainHeightCacheWindow smallWindow,
                        out string smallError
                    );

            bool largeSucceeded =
                TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateCanonicalResidentWindow(
                        large,
                        out TerrainHeightCacheWindow largeWindow,
                        out string largeError
                    );

            int smallWorldTiles =
                small.HeightTileCount;

            int largeWorldTiles =
                large.HeightTileCount;

            bool passed =
                smallSucceeded
                &&
                largeSucceeded
                &&
                smallWindow.Size ==
                    largeWindow.Size
                &&
                largeWorldTiles >
                    smallWorldTiles *
                    100
                &&
                largeWindow.TileCount <
                    largeWorldTiles;

            AddResult(
                "Large-world bounded resident dimensions",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? $"Small world tiles={smallWorldTiles:N0}, resident={smallWindow.Size}; " +
                      $"large world tiles={largeWorldTiles:N0}, resident={largeWindow.Size}."
                    : $"Small={smallError}; Large={largeError}; " +
                      $"smallWindow={smallWindow}; largeWindow={largeWindow}."
            );

            if (smallSucceeded && largeSucceeded)
            {
                long smallTransitionBytes =
                    EstimateTransitionMemoryBytes(
                        small,
                        smallWindow
                    );

                long largeTransitionBytes =
                    EstimateTransitionMemoryBytes(
                        large,
                        largeWindow
                    );

                bool memoryBounded =
                    smallWindow.Size == largeWindow.Size
                    && smallTransitionBytes > 0L
                    && smallTransitionBytes == largeTransitionBytes;

                AddResult(
                    "Large-world transition memory remains bounded",
                    memoryBounded
                        ? ValidationOutcome.Pass
                        : ValidationOutcome.Fail,
                    $"Active + staging bytes: small={smallTransitionBytes:N0}; " +
                    $"large={largeTransitionBytes:N0}. Logical world growth must not increase local cache payload."
                );
            }
            else
            {
                AddResult(
                    "Large-world transition memory remains bounded",
                    ValidationOutcome.Fail,
                    $"Canonical residency calculation failed. Small={smallError}; Large={largeError}."
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                small
            );

            UnityEngine.Object.DestroyImmediate(
                large
            );
        }
    }

    // =====================================================
    // MULTIRESOLUTION RESIDENCY PLAN
    // =====================================================

    private static void RunGeographicDemandValidation()
    {
        var settings = CreateSyntheticSettings(256, 256);
        try
        {
            var layout = new TerrainClipmapLayout();
            Vector3 center = TerrainClipmapLayoutUtility.CalculateWorldCenterPosition(settings, 0f);
            if (!TerrainClipmapLayoutUtility.TryCalculateLayout(settings, center, 0f, layout, out string error))
                throw new InvalidOperationException(error);
            var policy = new TerrainAuthoringPreviewQualitySnapshot(1, 128);
            var focus = new TerrainAuthoringPreviewFocus(settings, center, TerrainAuthoringPreviewFocusKind.Following, 1);
            var working = new[] { new TerrainAuthoringPreviewNativeWorkingDemand(
                new TerrainHeightCacheWindow(Vector2Int.zero, Vector2Int.one), TerrainAuthoringPreviewNativeWorkingReason.Analysis) };
            if (!TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings, layout, policy,
                focus, working, 1, 1, 1, out var plan, out error)) throw new InvalidOperationException(error);
            bool mapping = TerrainAuthoringPreviewLodResidencyUtility.TryCalculateEditableWindow(settings, policy,
                new Vector2(-1, -1), out var negative, out _, out _)
                && negative == Vector2Int.zero
                && TerrainAuthoringPreviewLodResidencyUtility.TryCalculateEditableWindow(settings, policy,
                    new Vector2(settings.HeightTileWorldSize, settings.HeightTileWorldSize), out var edge, out _, out _)
                && edge == Vector2Int.one
                && TerrainAuthoringPreviewLodResidencyUtility.TryCalculateEditableWindow(settings, policy,
                    TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings), out var maximum, out var clipped, out _)
                && maximum == new Vector2Int(settings.HeightTileGridWidth - 1, settings.HeightTileGridHeight - 1)
                && clipped.Size == Vector2Int.one;
            AddResult("Geographical focus uses canonical tile boundaries", mapping ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Negative, interior shared edge and exact world maximum map to bounded canonical tiles.");
            bool independent = plan.TryGetTile(Vector2Int.zero, out var nativeOnly) && nativeOnly.NativeWorkingRequired
                && !nativeOnly.HasDisplay && nativeOnly.SelectedDisplayStride == 0
                && plan.TryGetTile(plan.FocusTile, out var editable) && editable.SelectedDisplayStride == editable.FinestGeometryStride;
            foreach (var row in plan.Tiles)
                if (row.HasDisplay && !row.IsEditable) independent &= row.SelectedDisplayStride == Mathf.Max(row.FinestGeometryStride, 128);
            AddResult("One geographical display stride with independent native demand", independent && plan.TryValidate(settings, out _)
                ? ValidationOutcome.Pass : ValidationOutcome.Fail, "Context can exceed the runtime pyramid cap; native-only demand has no fake display page.");
            var allEditable = new TerrainAuthoringPreviewQualitySnapshot(4095, 128);
            bool geometry = TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings, layout, allEditable,
                focus, null, 1, 0, 1, out var all, out _)
                && all.TryGetTile(plan.FocusTile + new Vector2Int(1, 0), out var coarse)
                && coarse.FinestGeometryStride == 2 && coarse.SelectedDisplayStride == 2;
            if (all != null)
                foreach (var row in all.Tiles) geometry &= row.SelectedDisplayStride == row.FinestGeometryStride;
            AddResult("Editable geography follows contributing center, rings and stitches", geometry ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "A nearby coarse-only tile keeps stride 2; an all-visible editable window does not force native stride.");
            var subTile = new TerrainAuthoringPreviewFocus(settings, center + new Vector3(1, 0, 1), TerrainAuthoringPreviewFocusKind.Following, 1);
            var nextTile = new TerrainAuthoringPreviewFocus(settings, center + new Vector3(settings.HeightTileWorldSize, 0, 0),
                TerrainAuthoringPreviewFocusKind.Following, 1);
            bool stable = TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings, layout, policy,
                subTile, working, 1, 1, 2, out var same, out _) && plan.IsEquivalentTo(same)
                && TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings, layout, policy,
                    nextTile, working, 1, 1, 2, out var changed, out _) && !plan.IsEquivalentTo(changed);
            var unpinned = new TerrainAuthoringPreviewQualitySnapshot(1, 128, TerrainAuthoringPreviewEditFocusMode.PinnedFocus);
            stable &= TerrainAuthoringPreviewLodResidencyUtility.TryCalculateEditableWindow(settings, unpinned,
                new Vector2(center.x, center.z), out var fallback, out _, out _) && fallback == plan.FocusTile;
            AddResult("Geographical equivalence tracks focus independently of layout", stable ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Sub-tile drift is equivalent; tile-crossing focus changes demand with the same layout; absent pins use current focus.");
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }

    private static void RunMultiresolutionResidencyPlanValidation()
    {
        WorldSettings settings =
            CreateSyntheticSettings(
                256,
                256
            );

        try
        {
            TerrainClipmapLayout layout =
                new TerrainClipmapLayout();

            Vector3 center =
                TerrainClipmapLayoutUtility
                    .CalculateWorldCenterPosition(
                        settings,
                        0f
                    );

            bool layoutSucceeded =
                TerrainClipmapLayoutUtility
                    .TryCalculateLayout(
                        settings,
                        center,
                        0f,
                        layout,
                        out string layoutError
                    );

            if (!layoutSucceeded)
            {
                AddResult(
                    "Per-LOD Height residency plan",
                    ValidationOutcome.Fail,
                    layoutError
                );

                return;
            }

            bool planSucceeded =
                TerrainAuthoringPreviewLodResidencyUtility
                    .TryBuildPlan(
                        settings,
                        layout,
                        17,
                        out TerrainAuthoringPreviewResidencyPlan plan,
                        out string planError
                    );

            if (!planSucceeded)
            {
                AddResult(
                    "Per-LOD Height residency plan",
                    ValidationOutcome.Fail,
                    planError
                );

                return;
            }

            bool levelsValid =
                plan.IsStructurallyValid
                &&
                plan.LevelCount ==
                    layout.LevelCount;

            Vector2Int worldGridSize =
                new Vector2Int(
                    settings.HeightTileGridWidth,
                    settings.HeightTileGridHeight
                );

            StringBuilder details =
                new StringBuilder();

            for (
                int level = 0;
                level < plan.LevelCount;
                level++
            )
            {
                TerrainAuthoringPreviewLodResidencyPlan levelPlan =
                    plan.Levels[level];

                bool strideResolved =
                    TerrainHeightResolutionUtility
                        .TryGetRequiredStrideForClipmapLevel(
                            settings,
                            level,
                            out int expectedStride,
                            out _
                        );

                int expectedSamples =
                    strideResolved
                        ? TerrainHeightResolutionUtility
                            .GetSamplesPerSide(
                                settings,
                                expectedStride
                            )
                        : 0;

                float expectedSpacing =
                    strideResolved
                        ? TerrainHeightResolutionUtility
                            .GetSampleSpacing(
                                settings,
                                expectedStride
                            )
                        : 0f;

                float coarseSpacing =
                    expectedSpacing;

                if (
                    strideResolved
                    &&
                    level < plan.LevelCount - 1
                    &&
                    TerrainHeightResolutionUtility
                        .TryGetRequiredStrideForClipmapLevel(
                            settings,
                            level + 1,
                            out int coarseStride,
                            out _
                        )
                )
                {
                    coarseSpacing =
                        TerrainHeightResolutionUtility
                            .GetSampleSpacing(
                                settings,
                                coarseStride
                            );
                }

                Vector2 expectedMinimum =
                    Vector2.zero;

                Vector2 expectedMaximum =
                    Vector2.zero;

                bool coverageResolved =
                    strideResolved
                    &&
                    TerrainHeightClipmapCoverageUtility
                        .TryCalculateRequiredWorldBounds(
                            settings,
                            layout,
                            level,
                            expectedSpacing,
                            coarseSpacing,
                            out expectedMinimum,
                            out expectedMaximum,
                            out _
                        );

                TerrainHeightCacheWindow expectedRequired =
                    default;

                bool expectedWindowResolved =
                    coverageResolved
                    &&
                    TerrainAuthoringPreviewResidencyUtility
                        .TryCalculateRequiredWindowForRepresentation(
                            settings,
                            expectedMinimum,
                            expectedMaximum,
                            expectedStride,
                            out expectedRequired,
                            out _
                        );

                bool windowInsideWorld =
                    levelPlan.RequiredWindow.OriginTile.x >=
                        0
                    &&
                    levelPlan.RequiredWindow.OriginTile.y >=
                        0
                    &&
                    levelPlan.RequiredWindow.MaximumExclusive.x <=
                        worldGridSize.x
                    &&
                    levelPlan.RequiredWindow.MaximumExclusive.y <=
                        worldGridSize.y
                    &&
                    levelPlan.DesiredWindow.OriginTile.x >=
                        0
                    &&
                    levelPlan.DesiredWindow.OriginTile.y >=
                        0
                    &&
                    levelPlan.DesiredWindow.MaximumExclusive.x <=
                        worldGridSize.x
                    &&
                    levelPlan.DesiredWindow.MaximumExclusive.y <=
                        worldGridSize.y;

                bool levelValid =
                    strideResolved
                    &&
                    coverageResolved
                    &&
                    expectedWindowResolved
                    &&
                    levelPlan.SampleStride ==
                        expectedStride
                    &&
                    levelPlan.SamplesPerSide ==
                        expectedSamples
                    &&
                    Mathf.Approximately(
                        levelPlan.SampleSpacing,
                        expectedSpacing
                    )
                    &&
                    levelPlan.RequiredWindow ==
                        expectedRequired
                    &&
                    levelPlan.DesiredWindow.Contains(
                        levelPlan.RequiredWindow
                    )
                    &&
                    windowInsideWorld;

                levelsValid &=
                    levelValid;

                details.AppendLine(
                    $"LOD{level}: stride={levelPlan.SampleStride}, " +
                    $"required={levelPlan.RequiredWindow}, " +
                    $"desired={levelPlan.DesiredWindow}."
                );
            }

            AddResult(
                "Per-LOD Height residency plan",
                levelsValid
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                details.ToString()
            );

            var snapshot = layout.CreateSnapshot();
            var capturedAnchor = snapshot.GetAnchor(0);
            var paired = new TerrainAuthoringPreviewDisplayIntent(settings, null, plan, layout, 5, 3);
            bool copied = TerrainClipmapLayoutUtility.TryCalculateLayout(settings,
                center + new Vector3(layout.GetSpacing(0) * 4, 0, 0), 0, layout, out _)
                && snapshot.GetAnchor(0) == capturedAnchor && paired.Layout.GetAnchor(0) == capturedAnchor
                && !TerrainAuthoringPreviewDisplayIntent.PlacementMatches(snapshot, layout);
            // Restore the calculation used by the remaining residency checks.
            copied &= TerrainClipmapLayoutUtility.TryCalculateLayout(settings, center, 0, layout, out _);
            AddResult("Paired layout snapshot preserves all independent anchors",
                copied ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Recalculating the caller's reusable layout cannot mutate a recorded placement.");

            bool repeatedSucceeded =
                TerrainAuthoringPreviewLodResidencyUtility
                    .TryBuildPlan(
                        settings,
                        layout,
                        99,
                        out TerrainAuthoringPreviewResidencyPlan repeated,
                        out string repeatedError
                    );

            bool equalityValid =
                repeatedSucceeded
                &&
                TerrainAuthoringPreviewStreamingPolicy
                    .AreMultiresolutionResidencyPlansEquivalent(
                        plan,
                        repeated
                    );

            AddResult(
                "Multiresolution residency plan equality",
                equalityValid
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                equalityValid
                    ? "Equivalent layouts produce equivalent residency intent independent of plan generation."
                    : repeatedError
            );

            TerrainClipmapLayout movedLayout =
                new TerrainClipmapLayout();

            Vector3 movedTarget =
                center +
                new Vector3(
                    settings.HeightTileWorldSize *
                        4f,
                    0f,
                    settings.HeightTileWorldSize *
                        3f
                );

            movedTarget =
                TerrainClipmapLayoutUtility
                    .ClampTargetXZToWorld(
                        settings,
                        movedTarget
                    );

            bool movedLayoutSucceeded =
                TerrainClipmapLayoutUtility
                    .TryCalculateLayout(
                        settings,
                        movedTarget,
                        0f,
                        movedLayout,
                        out string movedLayoutError
                    );

            string movedPlanError =
                "";

            TerrainAuthoringPreviewResidencyPlan movedPlan =
                null;

            bool movedPlanSucceeded =
                movedLayoutSucceeded
                &&
                TerrainAuthoringPreviewLodResidencyUtility
                    .TryBuildPlan(
                        settings,
                        movedLayout,
                        100,
                        out movedPlan,
                        out movedPlanError
                    );

            bool movementChangesIntent =
                movedPlanSucceeded
                &&
                !TerrainAuthoringPreviewStreamingPolicy
                    .AreMultiresolutionResidencyPlansEquivalent(
                        plan,
                        movedPlan
                    );

            AddResult(
                "Multiresolution residency movement intent",
                movementChangesIntent
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                movementChangesIntent
                    ? "A multi-tile Scene View movement changes the per-LOD residency intent."
                    : movedLayoutSucceeded
                        ? movedPlanError
                        : movedLayoutError
            );

            Vector2 worldSize =
                TerrainClipmapLayoutUtility
                    .CalculateWorldSizeXZ(
                        settings
                    );

            bool edgesValid =
                ValidateMultiresolutionPlanAtTarget(
                    settings,
                    Vector3.zero
                )
                &&
                ValidateMultiresolutionPlanAtTarget(
                    settings,
                    new Vector3(
                        worldSize.x,
                        0f,
                        worldSize.y
                    )
                );

            AddResult(
                "Multiresolution residency world-edge fitting",
                edgesValid
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                edgesValid
                    ? "Per-LOD required and desired windows remain valid at opposite world corners."
                    : "A per-LOD residency plan failed world-edge fitting."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                settings
            );
        }
    }

    private static void RunMultiresolutionBoundedScalingValidation()
    {
        WorldSettings small =
            CreateSyntheticSettings(
                128,
                128
            );

        WorldSettings large =
            CreateSyntheticSettings(
                4096,
                4096
            );

        try
        {
            bool smallSucceeded =
                TryBuildCenteredMultiresolutionPlan(
                    small,
                    out TerrainAuthoringPreviewResidencyPlan smallPlan,
                    out string smallError
                );

            bool largeSucceeded =
                TryBuildCenteredMultiresolutionPlan(
                    large,
                    out TerrainAuthoringPreviewResidencyPlan largePlan,
                    out string largeError
                );

            bool bounded =
                smallSucceeded
                &&
                largeSucceeded
                &&
                smallPlan.LevelCount ==
                    largePlan.LevelCount;

            if (bounded)
            {
                for (
                    int level = 0;
                    level < smallPlan.LevelCount;
                    level++
                )
                {
                    bounded &=
                        smallPlan.Levels[level]
                            .RequiredWindow
                            .Size
                        ==
                        largePlan.Levels[level]
                            .RequiredWindow
                            .Size
                        &&
                        smallPlan.Levels[level]
                            .DesiredWindow
                            .Size
                        ==
                        largePlan.Levels[level]
                            .DesiredWindow
                            .Size;
                }
            }

            AddResult(
                "Multiresolution residency remains locally bounded",
                bounded
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                bounded
                    ? $"Compared {smallPlan.LevelCount} LOD windows across " +
                        $"{small.HeightTileCount:N0} and " +
                        $"{large.HeightTileCount:N0} tile worlds."
                    : $"Small={smallError}; Large={largeError}."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                small
            );

            UnityEngine.Object.DestroyImmediate(
                large
            );
        }
    }

    private static bool ValidateMultiresolutionPlanAtTarget(
        WorldSettings settings,
        Vector3 target
    )
    {
        TerrainClipmapLayout layout =
            new TerrainClipmapLayout();

        target =
            TerrainClipmapLayoutUtility
                .ClampTargetXZToWorld(
                    settings,
                    target
                );

        if (
            !TerrainClipmapLayoutUtility
                .TryCalculateLayout(
                    settings,
                    target,
                    0f,
                    layout,
                    out _
                )
            ||
            !TerrainAuthoringPreviewLodResidencyUtility
                .TryBuildPlan(
                    settings,
                    layout,
                    1,
                    out TerrainAuthoringPreviewResidencyPlan plan,
                    out _
                )
        )
        {
            return false;
        }

        Vector2Int worldGridSize =
            new Vector2Int(
                settings.HeightTileGridWidth,
                settings.HeightTileGridHeight
            );

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            TerrainAuthoringPreviewLodResidencyPlan levelPlan =
                plan.Levels[level];

            if (
                !levelPlan.RequiredWindow.IsValid
                ||
                !levelPlan.DesiredWindow.IsValid
                ||
                !levelPlan.DesiredWindow.Contains(
                    levelPlan.RequiredWindow
                )
                ||
                levelPlan.RequiredWindow.OriginTile.x <
                    0
                ||
                levelPlan.RequiredWindow.OriginTile.y <
                    0
                ||
                levelPlan.RequiredWindow.MaximumExclusive.x >
                    worldGridSize.x
                ||
                levelPlan.RequiredWindow.MaximumExclusive.y >
                    worldGridSize.y
                ||
                levelPlan.DesiredWindow.OriginTile.x <
                    0
                ||
                levelPlan.DesiredWindow.OriginTile.y <
                    0
                ||
                levelPlan.DesiredWindow.MaximumExclusive.x >
                    worldGridSize.x
                ||
                levelPlan.DesiredWindow.MaximumExclusive.y >
                    worldGridSize.y
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildCenteredMultiresolutionPlan(
        WorldSettings settings,
        out TerrainAuthoringPreviewResidencyPlan plan,
        out string errorMessage
    )
    {
        plan =
            null;

        errorMessage =
            "";

        TerrainClipmapLayout layout =
            new TerrainClipmapLayout();

        Vector3 center =
            TerrainClipmapLayoutUtility
                .CalculateWorldCenterPosition(
                    settings,
                    0f
                );

        if (
            !TerrainClipmapLayoutUtility
                .TryCalculateLayout(
                    settings,
                    center,
                    0f,
                    layout,
                    out errorMessage
                )
        )
        {
            return false;
        }

        return
            TerrainAuthoringPreviewLodResidencyUtility
                .TryBuildPlan(
                    settings,
                    layout,
                    1,
                    out plan,
                    out errorMessage
                );
    }

    // =====================================================
    // LIVE PREVIEW
    // =====================================================

    private static void RunLivePreviewValidation()
    {
        var snapshot = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        var ownership = snapshot.Ownership;
        bool ownersValid = ownership.OwnedCacheCount == ownership.DisplayActiveCount + ownership.AnalysisActiveCount
            + ownership.DisplayStagingCount + ownership.AnalysisStagingCount + ownership.RetiringCount
            && ownership.AllocatedArrayCount <= ownership.OwnedCacheCount
            && ownership.OwnedCacheCount <= snapshot.CacheLiveCount
            && snapshot.CacheCreateCount - snapshot.CacheDisposeCount == snapshot.CacheLiveCount
            && ownership.TotalBytes == TerrainAuthoringPreviewService.ApproximateTotalResidentGpuMemoryBytes;
        AddResult("Distinct preview resource ownership", ownersValid ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            $"Service cache objects={ownership.OwnedCacheCount}, arrays={ownership.AllocatedArrayCount}; editor-domain live={snapshot.CacheLiveCount}; Height payload={ownership.TotalBytes} bytes. Stale/retained owners remain counted.");
        long displayBytes = 0, stagingBytes = 0, scratchBytes = 0;
        int allocated = 0;
        bool rowsValid = true;
        foreach (var row in snapshot.DisplayLods)
        {
            displayBytes += row.Active.GpuBytes;
            scratchBytes += row.DirtyScratchBytes;
            rowsValid &= row.FailedDirtyCount <= row.PendingDirtyCount
                && (row.FailedDirtyCount == 0 || row.DirtyFailure.Present);
            stagingBytes += row.Staging.GpuBytes;
            if (!row.Active.HasTexture) continue;
            allocated++;
            bool borrowed = TerrainAuthoringPreviewService.TryGetActiveDisplayLodCacheForValidation(row.Level, out var cache);
            bool valid = row.Active.Representation.IsValid && row.Active.PageCount == row.Active.Window.TileCount
                && row.Active.Window.Contains(row.PublishedRequiredWindow)
                && row.Active.GpuBytes == (long)row.Active.Representation.SamplesPerSide * row.Active.Representation.SamplesPerSide * row.Active.PageCount * sizeof(float);
            if (!row.WriteFailed)
            {
                valid &= borrowed && cache.HeightCache.volumeDepth == row.Active.PageCount
                    && cache.SamplesPerSide == row.Active.Representation.SamplesPerSide
                    && cache.SampleStride == row.Active.Representation.Stride
                    && Mathf.Approximately(cache.SampleSpacing, row.Active.Representation.SampleSpacing);
                if (borrowed)
                {
                    // Bounded selected probes; no large live per-page readback or scan.
                    valid &= TerrainAuthoringPreviewService.TryGetDisplayLodSliceIndex(row.Level, row.Active.Window.OriginTile.x, row.Active.Window.OriginTile.y, out int first)
                        && first == 0 && cache.TryGetTileCoordinate(row.Active.PageCount - 1, out var last)
                        && last == (row.Active.Window.MaximumExclusive - Vector2Int.one);
                }
            }
            if (snapshot.ReadyForLatestIntent)
                valid &= row.HasLatestPlan && row.Active.Complete && !row.WriteFailed && row.Active.Window.Contains(row.RequiredWindow)
                    && row.Active.Representation.Stride == row.PlannedRepresentation.Stride
                    && row.Active.Representation.SamplesPerSide == row.PlannedRepresentation.SamplesPerSide
                    && Mathf.Approximately(row.Active.Representation.SampleSpacing, row.PlannedRepresentation.SampleSpacing);
            rowsValid &= valid;
            AddResult($"Published display LOD {row.Level} geometry and required coverage", valid ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                $"Stride={row.Active.Representation.Stride}, physical={row.Active.Window}, published required={row.PublishedRequiredWindow}, latest required={row.RequiredWindow}; current={row.Active.Current}, dirty={row.PendingDirtyCount}.");
        }
        bool totalsValid = displayBytes == ownership.DisplayActiveBytes && stagingBytes == ownership.DisplayStagingBytes
            && scratchBytes <= ownership.DirtyScratchBytes
            && ownership.TotalBytes == ownership.ActiveBytes + ownership.StagingBytes
                + ownership.RetiringDisplayBytes + ownership.RetiringAnalysisBytes + ownership.DirtyScratchBytes
            && snapshot.ReadyForLatestIntent == (snapshot.Drawable && snapshot.LatestCoverageCurrent && snapshot.PlacementCurrent)
            && snapshot.WaitingForCoverage == (snapshot.Enabled && TerrainAuthoringPreviewService.LatestMultiresolutionResidencyGeneration != 0
                && !snapshot.ReadyForLatestIntent)
            && snapshot.WaitingForCoverage == TerrainAuthoringPreviewService.IsWaitingForStreamingCoverage;
        foreach (var row in snapshot.DisplayLods)
            totalsValid &= !row.Active.Current || row.PendingDirtyCount == 0 && !row.WriteFailed;
        AddResult("Display rows reconcile ownership and paired intent", rowsValid && totalsValid ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            $"{allocated} allocated display rows; active={displayBytes}, staging={stagingBytes} bytes; latest intent ready={snapshot.ReadyForLatestIntent}.");
        if (allocated == 0) AddResult("Live display prerequisites", ValidationOutcome.Blocked, "No published Height arrays are available. Synthetic checks are separate.");
        var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldMeshesPaths.WorldSettingsAssetPath);
        var data = AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(WorldMeshesPaths.TerrainAuthoringDataAssetPath);
        if (settings != null && data != null)
            RunPersistentStateSafetyValidation(settings, data, data.authoringRevision,
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings),
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data));
        else AddResult("Live authoring prerequisites", ValidationOutcome.Blocked, "WorldSettings or TerrainAuthoringData is unavailable.");
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static long EstimateCacheMemoryBytes(
        WorldSettings settings,
        TerrainHeightCacheWindow window
    )
    {
        long samplesPerSide =
            settings.HeightTileSamplesPerSide;

        return checked(
            samplesPerSide
            * samplesPerSide
            * window.Width
            * window.Height
            * sizeof(float)
        );
    }

    private static long EstimateTransitionMemoryBytes(
        WorldSettings settings,
        TerrainHeightCacheWindow window
    )
    {
        long cacheBytes =
            EstimateCacheMemoryBytes(
                settings,
                window
            );

        return checked(cacheBytes + cacheBytes);
    }

    private static WorldSettings CreateSyntheticSettings(
        int gridWidth,
        int gridHeight
    )
    {
        WorldSettings settings =
            ScriptableObject
                .CreateInstance<WorldSettings>();

        settings.gridWidth =
            Mathf.Max(
                1,
                gridWidth
            );

        settings.gridHeight =
            Mathf.Max(
                1,
                gridHeight
            );

        settings.chunkSize =
            128f;

        settings.heightfieldResolutionPerChunk =
            128;

        settings.heightTileChunkSpan =
            4;

        settings.clipmapCenterResolution =
            512;

        settings.clipmapLevelCount =
            5;

        settings.clipmapBaseSampleStep =
            1;

        settings.heightStreamingMaximumStride =
            64;

        settings.clipmapLODOuterResolutions =
            new int[]
            {
                512,
                512,
                512,
                512
            };

        return settings;
    }

    private static bool TryCalculateWindowForInclusiveTiles(
        WorldSettings settings,
        int minimumTileX,
        int minimumTileZ,
        int maximumTileX,
        int maximumTileZ,
        out TerrainHeightCacheWindow residentWindow,
        out string errorMessage
    )
    {
        float tileWorldSize =
            settings.HeightTileWorldSize;

        Vector2 minimumXZ =
            new Vector2(
                minimumTileX *
                    tileWorldSize,
                minimumTileZ *
                    tileWorldSize
            );

        Vector2 maximumXZ =
            new Vector2(
                Mathf.Min(
                    TerrainClipmapLayoutUtility
                        .CalculateWorldSizeXZ(
                            settings
                        )
                        .x,
                    (
                        maximumTileX +
                        1
                    )
                    *
                    tileWorldSize
                    -
                    1f
                ),
                Mathf.Min(
                    TerrainClipmapLayoutUtility
                        .CalculateWorldSizeXZ(
                            settings
                        )
                        .y,
                    (
                        maximumTileZ +
                        1
                    )
                    *
                    tileWorldSize
                    -
                    1f
                )
            );

        return
            TerrainAuthoringPreviewResidencyUtility
                .TryCalculateResidentWindow(
                    settings,
                    minimumXZ,
                    maximumXZ,
                    0,
                    1,
                    out residentWindow,
                    out errorMessage
                );
    }



    private static void RunPersistentStateSafetyValidation(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        int revisionBefore,
        string committedSignatureBefore,
        string overallSignatureBefore
    )
    {
        int revisionAfter =
            authoringData.authoringRevision;

        string committedSignatureAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallSignatureAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        bool unchanged =
            revisionBefore ==
                revisionAfter
            &&
            committedSignatureBefore ==
                committedSignatureAfter
            &&
            overallSignatureBefore ==
                overallSignatureAfter;

        AddResult(
            "Persistent authoring state unchanged",
            unchanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Residency validation must not mutate persistent terrain authoring identity."
        );
    }

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
        validationRunning =
            false;

        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        StringBuilder report =
            new StringBuilder();

        report.AppendLine(
            "WorldMeshes Scene View Residency Validation"
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
            string label;

            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    label =
                        "PASS";

                    passed++;

                    break;

                case ValidationOutcome.Fail:
                    label =
                        "FAIL";

                    failed++;

                    break;

                default:
                    label =
                        "BLOCKED";

                    blocked++;

                    break;
            }

            report.AppendLine(
                $"{label} - {result.Name}"
            );

            if (
                !string.IsNullOrEmpty(
                    result.Details
                )
            )
            {
                report.AppendLine(
                    $"       {result.Details}"
                );
            }

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
                ? "Scene View residency validation: FAILED"
                : blocked > 0
                    ? "Scene View residency validation: BLOCKED"
                    : "Scene View residency validation: PASSED"
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
    private static void RunDirtyScratchOwnershipValidation()
    {
        var state = new TerrainAuthoringPreviewLodState(0, 1, 9, 1);
        long cacheLive = TerrainAuthoringPreviewCache.DiagnosticLiveCount;
        RenderTexture scratch = null;
        try
        {
            if (!state.TryEnsureDirtyScratch(out bool allocated, out string error))
            { AddResult("Dirty scratch ownership", ValidationOutcome.Blocked, error); return; }
            scratch = state.DirtyScratch;
            var active = TerrainAuthoringPreviewService.CaptureHeightOwnership(new[] { state }, null, null, null, new[] { state }, null);
            var retiring = TerrainAuthoringPreviewService.CaptureHeightOwnership(null, null, null, null, new[] { state }, null);
            bool valid = allocated && state.TryEnsureDirtyScratch(out allocated, out _) && !allocated
                && active.DirtyScratchArrayCount == 1 && active.DirtyScratchBytes == 2L * sizeof(float) * 9 * 9
                && active.TotalBytes == active.DirtyScratchBytes && active.OwnedCacheCount == 0 && active.AllocatedArrayCount == 0
                && active.DisplayActiveBytes == 0 && active.StagingBytes == 0
                && retiring.DirtyScratchBytes == active.DirtyScratchBytes && retiring.DirtyScratchArrayCount == 1
                && TerrainAuthoringPreviewCache.DiagnosticLiveCount == cacheLive;
            state.Dispose(); state.Dispose();
            valid &= state.DirtyScratch == null && scratch == null && state.DirtyScratchBytes == 0
                && TerrainAuthoringPreviewService.CaptureHeightOwnership(new[] { state }, null, null, null, null, null).TotalBytes == 0;
            AddResult("Dirty scratch ownership", valid ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Scratch reuses one two-slice array, counts active/retiring identity once, changes no cache/page counters, and releases idempotently.");
        }
        finally { state.Dispose(); }
    }

    private static void RunOperationalFeedbackValidation()
    {
        var loading = TerrainAuthoringPreviewFeedbackKind.ResidencyLoading;
        var updating = TerrainAuthoringPreviewFeedbackKind.Updating;
        var failure = TerrainAuthoringPreviewFeedbackKind.DirtyFailure;
        bool valid = TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, true, true, false, false, true, false, true) == updating
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, true, true, false, false, true, true, true) == failure
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, true, true, true, false, true, true, true) == loading
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, true, true, true, true, true, true, true)
                == TerrainAuthoringPreviewFeedbackKind.ResidencyFailure
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, true, false, false, false, true, false, true)
                == TerrainAuthoringPreviewFeedbackKind.Paused
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(false, true, true, true, false, true, false, true)
                == TerrainAuthoringPreviewFeedbackKind.None
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, false, true, true, false, true, false, true)
                == TerrainAuthoringPreviewFeedbackKind.None
            && TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(true, true, true, false, false, true, false, false)
                == TerrainAuthoringPreviewFeedbackKind.None
            && !TerrainAuthoringPreviewFeedbackPolicy.HasDisplayProgress(default)
            && !TerrainAuthoringPreviewFeedbackPolicy.ShouldShowLoading(true, 0.1, 0.2)
            && TerrainAuthoringPreviewFeedbackPolicy.ShouldShowLoading(true, 0.3, 0.2)
            && !TerrainAuthoringPreviewFeedbackPolicy.ShouldShowFailure(true, false);
        AddResult("Operational feedback distinguishes residency and authoring", valid ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            "Resident updates/failures have no residency progress; required display failure, suspension and controlling ownership retain precedence.");
    }

    private static void RunDiagnosticsProjectionValidation()
    {
        var required = new TerrainHeightCacheWindow(new Vector2Int(5, 6), Vector2Int.one);
        var plan = new TerrainAuthoringPreviewLodResidencyPlan { Level = 0, SampleStride = 2,
            SamplesPerSide = 5, SampleSpacing = 2, RequiredWindow = required, DesiredWindow = required };
        var fullPlan = new TerrainAuthoringPreviewResidencyPlan { LevelCount = 1, Levels = new[] { plan } };
        var display = new TerrainAuthoringPreviewLodState(0, 1, 9, 1) { ActiveCache = new TerrainAuthoringPreviewCache() };
        var analysis = new TerrainAuthoringPreviewLodState(0, 1, 9, 1) { ActiveCache = new TerrainAuthoringPreviewCache() };
        TerrainAuthoringPreviewCacheSetTransition running = null, queued = null;
        long liveBefore = TerrainAuthoringPreviewCache.DiagnosticLiveCount - 2;
        try
        {
            running = new TerrainAuthoringPreviewCacheSetTransition(fullPlan, new[] { required }, new bool[1],
                new[] { display.ActiveCache }, new long[1], TerrainAuthoringPreviewCachePublication.DisplayHeightSet,
                "committed", "overall", 1, 2, 3, false);
            queued = new TerrainAuthoringPreviewCacheSetTransition(fullPlan, new[] { required }, new bool[1],
                new TerrainAuthoringPreviewCache[1], new long[1], TerrainAuthoringPreviewCachePublication.NativeAnalysis,
                "committed", "overall", 1, 4, 3, false);
            running.Entries[0].Destination.StagingCache = new TerrainAuthoringPreviewCache();
            var row = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(0, display, plan,
                running.Entries[0], null, false);
            var missing = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(0, null, plan,
                null, running.Entries[0], false);
            display.RecordDirtyFailure(required.OriginTile, 4, "Isolated copied failure", true, 1);
            display.RecordDirtyFailure(required.OriginTile + Vector2Int.one, 4, "Later copied failure", true, 2);
            var failedRow = TerrainAuthoringPreviewService.CaptureDisplayLodMetadata(0, display, plan, null, null, false);
            var heldSource = new TerrainAuthoringPreviewDirtySourceSnapshot(true, required.OriginTile,
                "committed", 123, 9, 456, 9L * 9 * sizeof(float));
            var rows = new[] { row, missing, failedRow };
            var snapshot = new TerrainAuthoringPreviewDiagnosticsSnapshot(
                enabled: true,
                cacheReady: default,
                drawable: true,
                latestCoverageCurrent: true,
                placementCurrent: true,
                readyForLatestIntent: default,
                previewStatus: default,
                previewStatusMessage: default,
                streamingState: default,
                streamingStatusMessage: default,
                streamingProgress: default,
                isStreaming: default,
                waitingForCoverage: default,
                authoringGeneration: default,
                streamingRequestGeneration: default,
                worker: default,
                queuedWorker: default,
                analysis: default,
                analysisActive: default,
                analysisStaging: default,
                ownership: default,
                displayFailure: default,
                analysisFailure: default,
                failureAffectsRequiredCoverage: default,
                peakTransitionGpuMemoryBytes: default,
                cacheCreateCount: default,
                cacheDisposeCount: default,
                cacheLiveCount: default,
                editorLifecycleStable: default,
                previewWorkAllowed: default,
                lifecycleResumePending: default,
                suspensionReasons: default,
                hasControllingSceneView: default,
                controllingSceneViewInstanceId: default,
                sceneViewOwnershipGeneration: default,
                followSceneView: default,
                followSource: default,
                freezePreview: default,
                pendingGeographicDirtyCount: default,
                lastDirtyLoads: default,
                lastDirtyMaterializations: default,
                lastDirtyCompositions: default,
                cancellationReason: default,
                displayLods: rows,
                heldDirtySource: heldSource,
                latestPlacementGeneration: 7);
            rows[0] = default;
            rows[2] = default;
            heldSource = default;
            plan.SampleStride = 4;
            display.DirtyFailures.Clear();
            display.PendingDirtyTiles.Clear();
            bool failureCopied = failedRow.FailedDirtyCount == 2 && failedRow.DirtyFailure.Present
                && failedRow.PendingDirtyCount == 2 && failedRow.DirtyFailure.AttemptSequence == 2
                && failedRow.DirtyFailure.Level == 0
                && failedRow.DirtyFailure.Tile == required.OriginTile + Vector2Int.one && failedRow.DirtyFailure.AttemptedGeneration == 4
                && failedRow.DirtyFailure.LastGoodAvailable && failedRow.DirtyScratchBytes == 0;
            AddResult("Dirty diagnostics are copied values", failureCopied ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Clearing authoritative suppression after capture leaves its representative tile/generation/message unchanged.");
            var laterAttempt = new TerrainAuthoringPreviewDirtyFailureSnapshot(required.OriginTile,
                new TerrainAuthoringPreviewDirtyFailure(3, "Later attempt at an older target", true, 3), 2);
            bool aggregate = !snapshot.CacheReady && snapshot.Drawable && snapshot.LatestCoverageCurrent && snapshot.PlacementCurrent
                && snapshot.PendingRepresentationCount == 2 && snapshot.FailedRepresentationCount == 2
                && snapshot.AuthoringConvergencePending && snapshot.MostRecentDirtyFailure.AttemptSequence == 2
                && snapshot.LatestPlacementGeneration == 7 && snapshot.HeldDirtySource.Present
                && snapshot.HeldDirtySource.Tile == required.OriginTile && snapshot.HeldDirtySource.TextureId == 456
                && snapshot.HeldDirtySource.ApproximatePayloadBytes == 9L * 9 * sizeof(float)
                && TerrainAuthoringPreviewService.IsNewerDirtyFailure(laterAttempt, snapshot.MostRecentDirtyFailure);
            AddResult("Convergence aggregates preserve residency and borrowed metadata", aggregate ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Failures are counted within pending representations; later attempts outrank older targets; held-source values own no resource.");
            bool copied = snapshot.DisplayLods.Count == 3 && snapshot.DisplayLods[0].PlannedRepresentation.Stride == 2
                && snapshot.DisplayLods[0].Active.Present && !snapshot.DisplayLods[0].Active.HasTexture
                && snapshot.DisplayLods[0].Staging.Present && !snapshot.DisplayLods[0].Staging.HasTexture
                && !snapshot.DisplayLods[1].Active.Present && snapshot.DisplayLods[1].HasLatestPlan
                && snapshot.DisplayLods[1].QueuedRepresentation.Stride == 2
                && snapshot.DisplayLods[0].PendingDirtyCount == 0
                && snapshot.DisplayLods[0].SizeHealth == TerrainAuthoringPreviewResidencySizeHealth.Unavailable;
            var borrowed = TerrainAuthoringPreviewService.CaptureHeightOwnership(new[] { display }, null,
                running, queued, new[] { display }, null);
            var owned = TerrainAuthoringPreviewService.CaptureHeightOwnership(new[] { display }, analysis,
                running, queued, new[] { display }, analysis);
            bool classification = borrowed.OwnedCacheCount == 2 && owned.OwnedCacheCount == 3
                && borrowed.DisplayActiveCount == 1 && borrowed.DisplayStagingCount == 1
                && borrowed.AnalysisActiveCount == 0 && borrowed.AnalysisStagingCount == 0
                && owned.AnalysisActiveCount == 1 && owned.RetiringCount == 0
                && owned.AllocatedArrayCount == 0 && owned.TotalBytes == 0
                && TerrainAuthoringPreviewFeedbackPolicy.HasDisplayProgress(TerrainAuthoringPreviewService.CaptureWorkerMetadata(running))
                && !TerrainAuthoringPreviewFeedbackPolicy.HasDisplayProgress(TerrainAuthoringPreviewService.CaptureWorkerMetadata(queued));
            AddResult("Copied diagnostics preserve queued and changing representations", copied ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Caller rows, source plans and dirty queues were mutated after capture; old native allocation and new coarse intent remain distinct.");
            AddResult("Borrowed sources and pending metadata add no ownership", classification ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Borrowed transaction sources and duplicate retiring references count once; unallocated objects have zero Height payload.");
        }
        finally
        {
            running?.Dispose(); queued?.Dispose(); display.Dispose(); analysis.Dispose();
        }
        AddResult("Snapshot fixture releases cache objects", TerrainAuthoringPreviewCache.DiagnosticLiveCount == liveBefore ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            "The isolated snapshot fixture restored the editor-domain live count.");
    }

}



