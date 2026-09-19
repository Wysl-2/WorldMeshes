using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package 02 validation for Scene View driven edit-mode height residency.
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

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

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
    // LIVE PREVIEW
    // =====================================================

    private static void RunLivePreviewValidation()
    {
        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            AddResult(
                "Live PreviewService residency prerequisites",
                ValidationOutcome.Blocked,
                "WorldSettings or TerrainAuthoringData is unavailable."
            );

            return;
        }

        int revisionBefore =
            authoringData.authoringRevision;

        string committedSignatureBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallSignatureBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            !TerrainAuthoringPreviewService.CacheReady
            ||
            !TerrainAuthoringPreviewService
                .TryGetActiveResidentWindow(
                    out TerrainHeightCacheWindow activeWindow
                )
            ||
            !TerrainAuthoringPreviewService
                .TryGetHeightCacheWorldCoverage(
                    out Vector2 coverageMinimum,
                    out Vector2 coverageMaximum
                )
        )
        {
            AddResult(
                "Live PreviewService residency prerequisites",
                ValidationOutcome.Blocked,
                "Enable Height Preview and wait for a ready resident cache, then rerun validation."
            );

            RunPersistentStateSafetyValidation(
                worldSettings,
                authoringData,
                revisionBefore,
                committedSignatureBefore,
                overallSignatureBefore
            );

            return;
        }

        AddResult(
            "Live PreviewService residency prerequisites",
            ValidationOutcome.Pass,
            $"Active={activeWindow}; coverage={coverageMinimum} -> {coverageMaximum}."
        );

        bool layoutStateCorrect =
            activeWindow.OriginTile ==
                TerrainAuthoringPreviewService
                    .CacheOriginTile
            &&
            activeWindow.Size ==
                new Vector2Int(
                    TerrainAuthoringPreviewService
                        .CacheWidth,
                    TerrainAuthoringPreviewService
                        .CacheHeight
                )
            &&
            TerrainAuthoringPreviewService
                .CacheSliceCount ==
                activeWindow.TileCount;

        AddResult(
            "Active resident window matches cache layout",
            layoutStateCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Active={activeWindow}, slices={TerrainAuthoringPreviewService.CacheSliceCount:N0}."
        );

        long expectedMemory =
            (long)TerrainAuthoringPreviewService
                .SamplesPerSide
            *
            TerrainAuthoringPreviewService
                .SamplesPerSide
            *
            activeWindow.TileCount
            *
            sizeof(float);

        bool memoryCorrect =
            TerrainAuthoringPreviewService
                .ApproximateGpuMemoryBytes ==
            expectedMemory;

        AddResult(
            "Resident GPU memory uses local slice count",
            memoryCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Expected/actual bytes: {expectedMemory} / " +
            $"{TerrainAuthoringPreviewService.ApproximateGpuMemoryBytes}."
        );

        float tileWorldSize =
            worldSettings.HeightTileWorldSize;

        float sampleSpacing =
            TerrainAuthoringPreviewResidencyUtility
                .CalculateHeightSampleSpacing(
                    worldSettings
                );

        if (
            activeWindow.Width >= 3
            &&
            activeWindow.Height >= 3
        )
        {
            Vector2 insideMinimum =
                new Vector2(
                    (
                        activeWindow.OriginTile.x +
                        1
                    )
                    *
                    tileWorldSize
                    +
                    sampleSpacing *
                    2f,
                    (
                        activeWindow.OriginTile.y +
                        1
                    )
                    *
                    tileWorldSize
                    +
                    sampleSpacing *
                    2f
                );

            Vector2 insideMaximum =
                new Vector2(
                    (
                        activeWindow.OriginTile.x +
                        2
                    )
                    *
                    tileWorldSize
                    -
                    sampleSpacing *
                    2f,
                    (
                        activeWindow.OriginTile.y +
                        2
                    )
                    *
                    tileWorldSize
                    -
                    sampleSpacing *
                    2f
                );

            bool insideCovered =
                TerrainAuthoringPreviewService
                    .CanActiveCacheCoverWorldBounds(
                        insideMinimum,
                        insideMaximum
                    );

            AddResult(
                "Sample-safe active containment query",
                insideCovered
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Interior bounds: {insideMinimum} -> {insideMaximum}."
            );
        }
        else
        {
            AddResult(
                "Sample-safe active containment query",
                ValidationOutcome.Blocked,
                "The current resident cache is too small for a stable interior containment probe."
            );
        }

        if (
            TryCreateOutsideActiveProbe(
                worldSettings,
                activeWindow,
                out Vector2 outsideMinimum,
                out Vector2 outsideMaximum
            )
        )
        {
            bool outsideCovered =
                TerrainAuthoringPreviewService
                    .CanActiveCacheCoverWorldBounds(
                        outsideMinimum,
                        outsideMaximum
                    );

            AddResult(
                "Nonresident bounds rejected by active cache",
                !outsideCovered
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Outside bounds: {outsideMinimum} -> {outsideMaximum}."
            );
        }
        else
        {
            AddResult(
                "Nonresident bounds rejected by active cache",
                ValidationOutcome.Blocked,
                "The current resident cache covers the complete logical world."
            );
        }

        int fullWorldTileCount =
            worldSettings.HeightTileCount;

        if (
            activeWindow.TileCount <
            fullWorldTileCount
        )
        {
            AddResult(
                "Current cache is locally bounded",
                ValidationOutcome.Pass,
                $"Resident={activeWindow.TileCount:N0} slices; full world={fullWorldTileCount:N0} tiles."
            );
        }
        else
        {
            AddResult(
                "Current cache is locally bounded",
                ValidationOutcome.Blocked,
                "The current world/clipmap configuration legitimately requires the complete height-tile grid. Synthetic scaling remains the architectural test."
            );
        }

        RunPersistentStateSafetyValidation(
            worldSettings,
            authoringData,
            revisionBefore,
            committedSignatureBefore,
            overallSignatureBefore
        );
    }

    // =====================================================
    // HELPERS
    // =====================================================

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

    private static bool TryCreateOutsideActiveProbe(
        WorldSettings settings,
        TerrainHeightCacheWindow activeWindow,
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        Vector2Int worldGridSize =
            new Vector2Int(
                settings.HeightTileGridWidth,
                settings.HeightTileGridHeight
            );

        Vector2Int outsideTile;

        if (
            activeWindow.MaximumExclusive.x <
                worldGridSize.x
        )
        {
            outsideTile =
                new Vector2Int(
                    activeWindow.MaximumExclusive.x,
                    activeWindow.OriginTile.y
                );
        }
        else if (
            activeWindow.OriginTile.x > 0
        )
        {
            outsideTile =
                new Vector2Int(
                    activeWindow.OriginTile.x -
                        1,
                    activeWindow.OriginTile.y
                );
        }
        else if (
            activeWindow.MaximumExclusive.y <
                worldGridSize.y
        )
        {
            outsideTile =
                new Vector2Int(
                    activeWindow.OriginTile.x,
                    activeWindow.MaximumExclusive.y
                );
        }
        else if (
            activeWindow.OriginTile.y > 0
        )
        {
            outsideTile =
                new Vector2Int(
                    activeWindow.OriginTile.x,
                    activeWindow.OriginTile.y -
                        1
                );
        }
        else
        {
            return false;
        }

        float tileWorldSize =
            settings.HeightTileWorldSize;

        Vector2 center =
            new Vector2(
                (
                    outsideTile.x +
                    0.5f
                )
                *
                tileWorldSize,
                (
                    outsideTile.y +
                    0.5f
                )
                *
                tileWorldSize
            );

        float extent =
            Mathf.Max(
                1f,
                TerrainAuthoringPreviewResidencyUtility
                    .CalculateHeightSampleSpacing(
                        settings
                    )
                *
                2f
            );

        minimumXZ =
            center -
            Vector2.one *
            extent;

        maximumXZ =
            center +
            Vector2.one *
            extent;

        return true;
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
            "WorldMeshes Edit-Mode Height Cache Streaming - Package 02 Validation"
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

        report.AppendLine(
            failed == 0
                ? "Package 02 Scene View residency: PASSED"
                : "Package 02 Scene View residency: FAILED"
        );

        if (failed == 0)
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
