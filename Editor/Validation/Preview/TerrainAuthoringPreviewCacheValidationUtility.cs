using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Package 01 validation for the window-capable edit-mode height preview cache.
 *
 * Pure window tests always run. When current committed authoring data and the
 * required GPU features are available, a temporary non-zero-origin preview
 * cache is also built and validated without binding it to the live clipmap.
 */
public static class TerrainAuthoringPreviewCacheValidationUtility
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

    public static void ValidateWindowCacheFoundation()
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
                "WorldMeshes preview cache window validation is " +
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
            RunWindowValueValidation();
            RunWindowFittingValidation();

            if (
                !TryValidateCachePrerequisites(
                    out WorldSettings worldSettings,
                    out TerrainAuthoringData authoringData,
                    out TerrainAuthoringHeightManifest manifest,
                    out string prerequisiteError
                )
            )
            {
                AddResult(
                    "Temporary partial-cache prerequisites",
                    ValidationOutcome.Blocked,
                    prerequisiteError
                );

                FinishValidation();

                return;
            }

            AddResult(
                "Temporary partial-cache prerequisites",
                ValidationOutcome.Pass,
                "Current committed authoring data and required GPU " +
                "features are available."
            );

            RunPartialCacheValidation(
                worldSettings,
                authoringData,
                manifest
            );
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

    private static void RunWindowValueValidation()
    {
        TerrainHeightCacheWindow window =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    3,
                    2
                ),
                new Vector2Int(
                    4,
                    3
                )
            );

        bool propertiesCorrect =
            window.IsValid
            &&
            window.Width ==
                4
            &&
            window.Height ==
                3
            &&
            window.TileCount ==
                12
            &&
            window.MaximumExclusive ==
                new Vector2Int(
                    7,
                    5
                );

        AddResult(
            "Window basic properties",
            propertiesCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Window: {window}."
        );

        bool tileContainmentCorrect =
            window.Contains(
                new Vector2Int(
                    3,
                    2
                )
            )
            &&
            window.Contains(
                new Vector2Int(
                    6,
                    4
                )
            )
            &&
            !window.Contains(
                new Vector2Int(
                    2,
                    2
                )
            )
            &&
            !window.Contains(
                new Vector2Int(
                    7,
                    2
                )
            )
            &&
            !window.Contains(
                new Vector2Int(
                    3,
                    5
                )
            );

        AddResult(
            "Window tile containment",
            tileContainmentCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Inclusive origin and maximum-exclusive bounds were tested."
        );

        TerrainHeightCacheWindow contained =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    4,
                    3
                ),
                new Vector2Int(
                    2,
                    1
                )
            );

        TerrainHeightCacheWindow overlapping =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    6,
                    4
                ),
                new Vector2Int(
                    3,
                    2
                )
            );

        TerrainHeightCacheWindow separate =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    7,
                    5
                ),
                new Vector2Int(
                    2,
                    2
                )
            );

        bool relationChecksCorrect =
            window.Contains(
                contained
            )
            &&
            window.Overlaps(
                overlapping
            )
            &&
            !window.Overlaps(
                separate
            );

        AddResult(
            "Window containment and overlap",
            relationChecksCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Contained, overlapping, and edge-touching non-overlap " +
            "cases were tested."
        );

        bool equalityCorrect =
            window ==
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        3,
                        2
                    ),
                    new Vector2Int(
                        4,
                        3
                    )
                )
            &&
            window !=
                contained;

        AddResult(
            "Window equality",
            equalityCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Value equality uses origin and size."
        );

        bool intersectionCorrect =
            window.TryGetIntersection(
                overlapping,
                out TerrainHeightCacheWindow intersection
            )
            &&
            intersection ==
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        6,
                        4
                    ),
                    new Vector2Int(
                        1,
                        1
                    )
                );

        AddResult(
            "Window intersection",
            intersectionCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            intersectionCorrect
                ? $"Intersection: {intersection}."
                : "Expected a one-tile intersection at (6, 4)."
        );
    }

    private static void RunWindowFittingValidation()
    {
        bool edgeFitSucceeded =
            TerrainHeightCacheWindow
                .TryFitToWorld(
                    new Vector2Int(
                        98,
                        78
                    ),
                    new Vector2Int(
                        7,
                        7
                    ),
                    new Vector2Int(
                        100,
                        80
                    ),
                    out TerrainHeightCacheWindow edgeWindow
                );

        bool edgeFitCorrect =
            edgeFitSucceeded
            &&
            edgeWindow ==
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        93,
                        73
                    ),
                    new Vector2Int(
                        7,
                        7
                    )
                );

        AddResult(
            "Fixed-size world-edge fitting",
            edgeFitCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            edgeFitSucceeded
                ? $"Fitted window: {edgeWindow}."
                : "TryFitToWorld unexpectedly rejected valid inputs."
        );

        bool smallWorldFitSucceeded =
            TerrainHeightCacheWindow
                .TryFitToWorld(
                    new Vector2Int(
                        5,
                        5
                    ),
                    new Vector2Int(
                        7,
                        7
                    ),
                    new Vector2Int(
                        4,
                        3
                    ),
                    out TerrainHeightCacheWindow smallWorldWindow
                );

        bool smallWorldFitCorrect =
            smallWorldFitSucceeded
            &&
            smallWorldWindow.OriginTile ==
                Vector2Int.zero
            &&
            smallWorldWindow.Size ==
                new Vector2Int(
                    4,
                    3
                );

        AddResult(
            "World-smaller-than-window fitting",
            smallWorldFitCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            smallWorldFitSucceeded
                ? $"Fitted window: {smallWorldWindow}."
                : "TryFitToWorld unexpectedly rejected valid inputs."
        );

        bool negativeOriginFitSucceeded =
            TerrainHeightCacheWindow
                .TryFitToWorld(
                    new Vector2Int(
                        -5,
                        -9
                    ),
                    new Vector2Int(
                        7,
                        5
                    ),
                    new Vector2Int(
                        20,
                        20
                    ),
                    out TerrainHeightCacheWindow negativeOriginWindow
                );

        bool negativeOriginFitCorrect =
            negativeOriginFitSucceeded
            &&
            negativeOriginWindow.OriginTile ==
                Vector2Int.zero
            &&
            negativeOriginWindow.Size ==
                new Vector2Int(
                    7,
                    5
                );

        AddResult(
            "Negative desired origin fitting",
            negativeOriginFitCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            negativeOriginFitSucceeded
                ? $"Fitted window: {negativeOriginWindow}."
                : "TryFitToWorld unexpectedly rejected valid inputs."
        );

        bool invalidRejected =
            !TerrainHeightCacheWindow
                .TryFitToWorld(
                    Vector2Int.zero,
                    Vector2Int.zero,
                    new Vector2Int(
                        10,
                        10
                    ),
                    out _
                )
            &&
            !TerrainHeightCacheWindow
                .TryFitToWorld(
                    Vector2Int.zero,
                    new Vector2Int(
                        5,
                        5
                    ),
                    Vector2Int.zero,
                    out _
                );

        AddResult(
            "Invalid fitting inputs rejected",
            invalidRejected
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Zero requested/world dimensions must fail deterministically."
        );
    }

    private static bool TryValidateCachePrerequisites(
        out WorldSettings worldSettings,
        out TerrainAuthoringData authoringData,
        out TerrainAuthoringHeightManifest manifest,
        out string errorMessage
    )
    {
        worldSettings =
            null;

        authoringData =
            null;

        manifest =
            null;

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
                "Validation cannot run in or while entering Play Mode.";

            return false;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            errorMessage =
                "Unity is compiling or updating assets.";

            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support 2D " +
                "texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The current graphics device does not support RFloat " +
                "RenderTextures.";

            return false;
        }

        if (
            SystemInfo.copyTextureSupport ==
            CopyTextureSupport.None
        )
        {
            errorMessage =
                "The current graphics device does not support " +
                "Graphics.CopyTexture.";

            return false;
        }

        worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        authoringData =
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
            errorMessage =
                "WorldSettings or TerrainAuthoringData could not be " +
                "loaded.";

            return false;
        }

        if (
            !TerrainAuthoringStateUtility
                .TryValidateCommittedHeightfield(
                    worldSettings,
                    authoringData,
                    TerrainAuthoringHeightfieldValidationMode
                        .Operational,
                    out manifest,
                    out _,
                    out string validationError
                )
        )
        {
            errorMessage =
                "The committed authoring heightfield is not ready for " +
                "window validation.\n\n" +
                validationError;

            return false;
        }

        if (
            manifest == null
            ||
            manifest.heightTileGridWidth <= 0
            ||
            manifest.heightTileGridHeight <= 0
        )
        {
            errorMessage =
                "The committed manifest contains an invalid height-tile " +
                "grid.";

            return false;
        }

        return true;
    }

    private static void RunPartialCacheValidation(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainAuthoringHeightManifest manifest
    )
    {
        if (
            !TryChoosePartialWindow(
                manifest,
                out TerrainHeightCacheWindow testWindow
            )
        )
        {
            AddResult(
                "Non-zero-origin partial cache",
                ValidationOutcome.Blocked,
                "The current committed world contains only one height " +
                "tile, so a non-zero-origin cache window cannot be " +
                "constructed."
            );

            return;
        }

        int authoringRevisionBefore =
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

        TerrainAuthoringPreviewCache validationCache =
            new TerrainAuthoringPreviewCache();

        try
        {
            if (
                !validationCache.TryBuild(
                    worldSettings,
                    authoringData,
                    testWindow,
                    out string buildError
                )
            )
            {
                AddResult(
                    "Non-zero-origin partial cache build",
                    ValidationOutcome.Fail,
                    buildError
                );

                return;
            }

            AddResult(
                "Non-zero-origin partial cache build",
                ValidationOutcome.Pass,
                $"Built {testWindow}."
            );

            bool layoutCorrect =
                validationCache.CacheOriginTile ==
                    testWindow.OriginTile
                &&
                validationCache.CacheSize ==
                    testWindow.Size
                &&
                validationCache.SliceCount ==
                    testWindow.TileCount;

            AddResult(
                "Partial cache layout state",
                layoutCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Origin={validationCache.CacheOriginTile}, " +
                $"Size={validationCache.CacheSize}, " +
                $"Slices={validationCache.SliceCount}."
            );

            RunPartialAddressingValidation(
                validationCache,
                testWindow
            );

            RunPartialRangeValidation(
                validationCache,
                manifest,
                testWindow
            );

            RunPartialCoverageValidation(
                validationCache,
                testWindow
            );

            long expectedMemoryBytes =
                (long)validationCache.SamplesPerSide
                *
                validationCache.SamplesPerSide
                *
                validationCache.SliceCount
                *
                sizeof(float);

            AddResult(
                "Resident-window GPU memory estimate",
                validationCache.ApproximateGpuMemoryBytes ==
                    expectedMemoryBytes
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Expected/actual bytes: {expectedMemoryBytes} / " +
                $"{validationCache.ApproximateGpuMemoryBytes}."
            );
        }
        finally
        {
            validationCache.Dispose();

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

            bool persistentStateUnchanged =
                authoringData.authoringRevision ==
                    authoringRevisionBefore
                &&
                committedSignatureAfter ==
                    committedSignatureBefore
                &&
                overallSignatureAfter ==
                    overallSignatureBefore;

            AddResult(
                "Persistent authoring state unchanged",
                persistentStateUnchanged
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                persistentStateUnchanged
                    ? "Temporary cache validation did not change persistent " +
                        "authoring identity."
                    : "Persistent authoring state changed during validation."
            );
        }
    }

    private static bool TryChoosePartialWindow(
        TerrainAuthoringHeightManifest manifest,
        out TerrainHeightCacheWindow window
    )
    {
        window =
            default;

        int gridWidth =
            manifest.heightTileGridWidth;

        int gridHeight =
            manifest.heightTileGridHeight;

        if (
            gridWidth <= 0
            ||
            gridHeight <= 0
            ||
            (
                gridWidth == 1
                &&
                gridHeight == 1
            )
        )
        {
            return false;
        }

        Vector2Int origin =
            new Vector2Int(
                gridWidth > 1
                    ? 1
                    : 0,
                gridHeight > 1
                    ? 1
                    : 0
            );

        Vector2Int size =
            new Vector2Int(
                Mathf.Min(
                    4,
                    gridWidth -
                        origin.x
                ),
                Mathf.Min(
                    3,
                    gridHeight -
                        origin.y
                )
            );

        window =
            new TerrainHeightCacheWindow(
                origin,
                size
            );

        return
            window.IsValid
            &&
            origin !=
                Vector2Int.zero;
    }

    private static void RunPartialAddressingValidation(
        TerrainAuthoringPreviewCache cache,
        TerrainHeightCacheWindow window
    )
    {
        bool mappingsCorrect =
            true;

        for (
            int localZ = 0;
            localZ < window.Height;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < window.Width;
                localX++
            )
            {
                int expectedSlice =
                    localX
                    +
                    localZ *
                    window.Width;

                Vector2Int worldTile =
                    new Vector2Int(
                        window.OriginTile.x +
                            localX,
                        window.OriginTile.y +
                            localZ
                    );

                int actualSlice =
                    cache.GetSliceIndex(
                        worldTile.x,
                        worldTile.y
                    );

                if (
                    actualSlice !=
                        expectedSlice
                    ||
                    !cache.TryGetTileCoordinate(
                        expectedSlice,
                        out Vector2Int roundTripTile
                    )
                    ||
                    roundTripTile !=
                        worldTile
                )
                {
                    mappingsCorrect =
                        false;

                    break;
                }
            }

            if (!mappingsCorrect)
            {
                break;
            }
        }

        Vector2Int maximumExclusive =
            window.MaximumExclusive;

        bool outsideRejected =
            cache.GetSliceIndex(
                maximumExclusive.x,
                window.OriginTile.y
            ) < 0
            &&
            cache.GetSliceIndex(
                window.OriginTile.x,
                maximumExclusive.y
            ) < 0;

        if (window.OriginTile.x > 0)
        {
            outsideRejected =
                outsideRejected
                &&
                cache.GetSliceIndex(
                    window.OriginTile.x -
                        1,
                    window.OriginTile.y
                ) < 0;
        }

        if (window.OriginTile.y > 0)
        {
            outsideRejected =
                outsideRejected
                &&
                cache.GetSliceIndex(
                    window.OriginTile.x,
                    window.OriginTile.y -
                        1
                ) < 0;
        }

        AddResult(
            "World/local slice addressing round trip",
            mappingsCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            mappingsCorrect
                ? "Every resident world tile mapped to its row-major local " +
                    "slice and round-tripped correctly."
                : "At least one resident tile did not map/round-trip " +
                    "correctly."
        );

        AddResult(
            "Nonresident tile addressing",
            outsideRejected
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Coordinates immediately outside the cache window must return -1."
        );
    }

    private static void RunPartialRangeValidation(
        TerrainAuthoringPreviewCache cache,
        TerrainAuthoringHeightManifest manifest,
        TerrainHeightCacheWindow window
    )
    {
        bool hasExpectedRange =
            false;

        float expectedMinimum =
            0f;

        float expectedMaximum =
            0f;

        bool metadataValid =
            true;

        for (
            int localZ = 0;
            localZ < window.Height;
            localZ++
        )
        {
            int worldTileZ =
                window.OriginTile.y +
                localZ;

            for (
                int localX = 0;
                localX < window.Width;
                localX++
            )
            {
                int worldTileX =
                    window.OriginTile.x +
                    localX;

                if (
                    !manifest.TryGetTileHeightRange(
                        worldTileX,
                        worldTileZ,
                        out float tileMinimum,
                        out float tileMaximum
                    )
                )
                {
                    metadataValid =
                        false;

                    break;
                }

                if (!hasExpectedRange)
                {
                    expectedMinimum =
                        tileMinimum;

                    expectedMaximum =
                        tileMaximum;

                    hasExpectedRange =
                        true;
                }
                else
                {
                    expectedMinimum =
                        Mathf.Min(
                            expectedMinimum,
                            tileMinimum
                        );

                    expectedMaximum =
                        Mathf.Max(
                            expectedMaximum,
                            tileMaximum
                        );
                }
            }

            if (!metadataValid)
            {
                break;
            }
        }

        bool rangeCorrect =
            metadataValid
            &&
            hasExpectedRange
            &&
            Approximately(
                cache.MinimumHeight,
                expectedMinimum
            )
            &&
            Approximately(
                cache.MaximumHeight,
                expectedMaximum
            );

        AddResult(
            "Resident cache height range",
            rangeCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            rangeCorrect
                ? $"Expected/actual: {expectedMinimum:R} -> " +
                    $"{expectedMaximum:R}."
                : $"Expected {expectedMinimum:R} -> {expectedMaximum:R}; " +
                    $"actual {cache.MinimumHeight:R} -> " +
                    $"{cache.MaximumHeight:R}."
        );
    }

    private static void RunPartialCoverageValidation(
        TerrainAuthoringPreviewCache cache,
        TerrainHeightCacheWindow window
    )
    {
        bool coverageAvailable =
            cache.TryGetWorldCoverage(
                out Vector2 minimumXZ,
                out Vector2 maximumXZ
            );

        float tileWorldSize =
            cache.SampleSpacing
            *
            Mathf.Max(
                1,
                cache.SamplesPerSide -
                    1
            );

        Vector2 expectedMinimum =
            new Vector2(
                window.OriginTile.x *
                    tileWorldSize,
                window.OriginTile.y *
                    tileWorldSize
            );

        Vector2 expectedMaximum =
            new Vector2(
                Mathf.Min(
                    cache.WorldSizeXZ.x,
                    window.MaximumExclusive.x *
                        tileWorldSize
                ),
                Mathf.Min(
                    cache.WorldSizeXZ.y,
                    window.MaximumExclusive.y *
                        tileWorldSize
                )
            );

        bool coverageCorrect =
            coverageAvailable
            &&
            Approximately(
                minimumXZ.x,
                expectedMinimum.x
            )
            &&
            Approximately(
                minimumXZ.y,
                expectedMinimum.y
            )
            &&
            Approximately(
                maximumXZ.x,
                expectedMaximum.x
            )
            &&
            Approximately(
                maximumXZ.y,
                expectedMaximum.y
            );

        AddResult(
            "Partial cache world coverage",
            coverageCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            coverageAvailable
                ? $"Expected {expectedMinimum} -> {expectedMaximum}; " +
                    $"actual {minimumXZ} -> {maximumXZ}."
                : "TryGetWorldCoverage returned false."
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

        validationScheduled =
            false;

        int passCount =
            0;

        int failCount =
            0;

        int blockedCount =
            0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Edit-Mode Height Cache Streaming - Package 01 Validation"
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
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    passCount++;
                    break;

                case ValidationOutcome.Fail:
                    failCount++;
                    break;

                default:
                    blockedCount++;
                    break;
            }

            builder.Append(
                result.Outcome ==
                    ValidationOutcome.Pass
                    ? "PASS"
                    :
                    result.Outcome ==
                        ValidationOutcome.Fail
                        ? "FAIL"
                        : "BLOCKED"
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
                    result.Details
                        .Replace(
                            "\r\n",
                            "\n"
                        )
                        .Split(
                            '\n'
                        );

                foreach (
                    string line
                    in detailLines
                )
                {
                    builder.Append(
                        "       "
                    );

                    builder.AppendLine(
                        line
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

        if (failCount > 0)
        {
            builder.AppendLine(
                "Package 01 window cache foundation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else if (blockedCount > 0)
        {
            builder.AppendLine(
                "Package 01 window cache foundation: BLOCKED"
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Package 01 window cache foundation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }

        results.Clear();
    }

    private static bool Approximately(
        float a,
        float b
    )
    {
        float tolerance =
            Mathf.Max(
                0.00001f,
                Mathf.Max(
                    Mathf.Abs(a),
                    Mathf.Abs(b)
                )
                *
                0.00001f
            );

        return
            Mathf.Abs(
                a -
                b
            )
            <=
            tolerance;
    }
}
