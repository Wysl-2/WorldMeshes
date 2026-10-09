using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Validation for the window-capable edit-mode height preview cache.
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

    private static TerrainValidationRunSummary lastRunSummary =
        TerrainValidationRunSummary.CreateNotRun();

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

    public static TerrainValidationRunSummary LastRunSummary =>
        lastRunSummary;

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

        lastRunSummary =
            TerrainValidationRunSummary.CreateRunning(
                "Height cache window validation is running."
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
            RunWindowValueValidation();
            RunWindowFittingValidation();
            RunClipmapStrideValidation();
            RunExactHeightMaterializationValidation();
            RunDerivedCommittedSourceValidation();

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

            RunLastGoodDirtyPublicationValidation(worldSettings, authoringData);
            RunHeightRepresentationValidation(
                worldSettings
            );

            RunCoarseStagingAllocationValidation(
                worldSettings,
                authoringData,
                manifest
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

    // Isolated promoted caches share this fixture with the existing preview validators.
    internal static bool TryCreateDirtyFixtureState(int level, int strideMultiplier,
        out TerrainAuthoringPreviewLodState state, out WorldSettings settings, out TerrainAuthoringData data,
        out string error, out bool blocked)
    {
        state = null;
        blocked = !TryValidateCachePrerequisites(out settings, out data, out _, out error);
        if (blocked) return false;
        if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports2DArrayTextures
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat)
            || (SystemInfo.copyTextureSupport & (CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT))
                != (CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT)
            || settings.HeightTileGridWidth * (long)settings.HeightTileGridHeight < 2)
        { blocked = true; error = "Two committed tiles and array/compute/RFloat copy support are required."; return false; }
        int stride = 1;
        while (TerrainHeightResolutionUtility.GetSamplesPerSide(settings, stride) > 17) stride *= 2;
        stride *= strideMultiplier;
        if (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride))
        { blocked = true; error = "The committed geometry cannot provide this small representation."; return false; }
        int samples = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, stride);
        var window = new TerrainHeightCacheWindow(Vector2Int.zero,
            settings.HeightTileGridWidth >= 2 ? new Vector2Int(2, 1) : new Vector2Int(1, 2));
        state = new TerrainAuthoringPreviewLodState(level, stride, samples,
            TerrainHeightResolutionUtility.GetSampleSpacing(settings, stride));
        state.StagingCache = new TerrainAuthoringPreviewCache();
        Texture2D seed = null;
        try
        {
            if (!state.StagingCache.TryInitializeStagingWindow(settings, data, window, stride, out error))
                throw new InvalidOperationException(error);
            seed = new Texture2D(samples, samples, TextureFormat.RFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var values = new float[samples * samples];
            for (int slice = 0; slice < 2; slice++)
            {
                float sentinel = slice == 0 ? 5f : 10f;
                for (int index = 0; index < values.Length; index++) values[index] = sentinel;
                seed.SetPixelData(values, 0); seed.Apply(false, false);
                Graphics.CopyTexture(seed, 0, 0, state.StagingCache.HeightCache, slice, 0);
                if (!state.StagingCache.TryGetTileCoordinate(slice, out var tile)
                    || !state.StagingCache.TryCommitFinalCompositeTile(tile, sentinel, sentinel, out error))
                    throw new InvalidOperationException(error);
            }
            if (!state.StagingCache.TryFinalizeStagingForActivation(
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data), out error))
                throw new InvalidOperationException(error);
            state.RequestedRequiredWindow = window;
            state.PromoteStagingCache();
            return true;
        }
        catch (Exception exception)
        { state.Dispose(); state = null; error = exception.Message; return false; }
        finally { if (seed != null) UnityEngine.Object.DestroyImmediate(seed); }
    }

    private static void RunLastGoodDirtyPublicationValidation(WorldSettings settings, TerrainAuthoringData data)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        { AddResult("Last-good dirty Height publication", ValidationOutcome.Blocked, "Async GPU readback is required for the explicit isolated fixture."); return; }
        if (!TryCreateDirtyFixtureState(0, 1, out var state, out _, out _, out string error, out bool blocked))
        { AddResult("Last-good dirty Height publication", blocked ? ValidationOutcome.Blocked : ValidationOutcome.Fail, error); return; }
        Texture2D seed = null;
        var materializer = new TerrainAuthoringPreviewHeightMaterializer();
        var compositor = new TerrainHeightCompositor();
        try
        {
            var cache = state.ActiveCache;
            var tile = cache.CacheOriginTile;
            cache.TryGetTileCoordinate(1, out var neighbor);
            cache.TryGetCommittedRange(tile, out float baseLow, out float baseHigh, out _);
            if (!state.TryEnsureDirtyScratch(out _, out error)) throw new InvalidOperationException(error);
            seed = new Texture2D(state.SamplesPerSide, state.SamplesPerSide, TextureFormat.RFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            var samples = new float[state.SamplesPerSide * state.SamplesPerSide];
            for (int index = 0; index < samples.Length; index++) samples[index] = 70f;
            seed.SetPixelData(samples, 0); seed.Apply(false, true);
            Graphics.CopyTexture(seed, 0, 0, state.DirtyScratch, 0, 0);
            bool rejected = !cache.TryCommitCompositeSliceFromScratch(state.DirtyScratch, tile,
                float.NaN, 70f, out bool safe, out int copies, out _) && safe && copies == 0;
            AssertDirtyFixture(cache, tile, neighbor, 5f, 10f, baseLow, baseHigh);
            rejected &= !cache.TryCommitCompositeSliceFromScratch(cache.HeightCache, tile,
                70f, 70f, out safe, out copies, out _) && safe && copies == 0;
            AssertDirtyFixture(cache, tile, neighbor, 5f, 10f, baseLow, baseHigh);
            rejected &= !materializer.TryMaterialize((Texture2D)null, state.DirtyScratch, 0,
                settings.HeightTileSamplesPerSide, state.SamplesPerSide, state.SampleStride, out _);
            if (!compositor.TryPrepare(out error)) throw new InvalidOperationException(error);
            rejected &= !compositor.TryComposeTile(state.DirtyScratch, tile, -1, state.SamplesPerSide,
                state.SampleSpacing, settings.HeightTileWorldSize, cache.WorldSizeXZ, data,
                5f, 5f, out _, out _, out _);
            materializer.ReleaseTextureBindings(); compositor.ReleaseTextureBindings();
            AssertDirtyFixture(cache, tile, neighbor, 5f, 10f, baseLow, baseHigh);
            AddResult("Dirty preparation and preflight preserve live Height", rejected ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Rejected invalid range, live-as-scratch, materialization, and composition inputs; checked all live pixels, neighbor, readiness and ranges.");

            bool recovered = !cache.TryCommitCompositeSliceFromScratch(state.DirtyScratch, tile, 70f, 70f,
                (source, sourceSlice, destination, destinationSlice) =>
                {
                    Graphics.CopyTexture(source, sourceSlice, 0, destination, destinationSlice, 0);
                    if (source == state.DirtyScratch && sourceSlice == 0)
                        throw new InvalidOperationException("Isolated publication failure after the copy.");
                }, out safe, out copies, out _) && safe && copies == 3;
            AssertDirtyFixture(cache, tile, neighbor, 5f, 10f, baseLow, baseHigh);
            AddResult("Dirty publication restores after a submitted live write", recovered ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Injected the exception after the real candidate copy; checked restored pixels and unchanged neighboring/committed metadata.");

            bool success = cache.TryCommitCompositeSliceFromScratch(state.DirtyScratch, tile, 70f, 70f,
                out safe, out copies, out error) && safe && copies == 2;
            if (!success) throw new InvalidOperationException(error);
            AssertDirtyFixture(cache, tile, neighbor, 70f, 10f, baseLow, baseHigh);
            AddResult("Dirty publication commits pixels and final metadata together", ValidationOutcome.Pass,
                "The target became 70, its neighbor stayed 10, and global range became 10..70 without altering committed range.");

            bool unsafeRejected = !cache.TryCommitCompositeSliceFromScratch(state.DirtyScratch, tile, 70f, 70f,
                (source, sourceSlice, destination, destinationSlice) =>
                {
                    if (source == state.DirtyScratch && sourceSlice == 1)
                        throw new InvalidOperationException("Isolated recovery failure.");
                    Graphics.CopyTexture(source, sourceSlice, 0, destination, destinationSlice, 0);
                    if (source == state.DirtyScratch && sourceSlice == 0)
                        throw new InvalidOperationException("Isolated publication failure.");
                }, out safe, out copies, out _) && !safe && copies == 3;
            AddResult("Unrestorable live publication is reported unsafe", unsafeRejected ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "The isolated cache is disposed; no user preview or global fault switch was used.");
        }
        catch (Exception exception)
        { AddResult("Last-good dirty Height fixture", ValidationOutcome.Fail, exception.ToString()); }
        finally
        {
            materializer.ReleaseTextureBindings(); compositor.Dispose(); state.Dispose();
            if (seed != null) UnityEngine.Object.DestroyImmediate(seed);
        }
    }

    private static void AssertDirtyFixture(TerrainAuthoringPreviewCache cache, Vector2Int tile, Vector2Int neighbor,
        float expected, float expectedNeighbor, float committedLow, float committedHigh)
    {
        var readback = AsyncGPUReadback.Request(cache.HeightCache, 0);
        readback.WaitForCompletion();
        if (readback.hasError || readback.layerCount != 2) throw new InvalidOperationException("Dirty fixture readback failed.");
        for (int slice = 0; slice < 2; slice++)
        {
            var pixels = readback.GetData<float>(slice);
            if (pixels.Length != cache.SamplesPerSide * cache.SamplesPerSide) throw new InvalidOperationException("Dirty fixture sample count changed.");
            foreach (float pixel in pixels)
                if (pixel != (slice == 0 ? expected : expectedNeighbor)) throw new InvalidOperationException("Live or neighboring pixels changed unexpectedly.");
        }
        if (!cache.TryGetCompositeSliceRange(tile.x, tile.y, out float low, out float high) || low != expected || high != expected
            || !cache.TryGetCompositeSliceRange(neighbor.x, neighbor.y, out low, out high) || low != expectedNeighbor || high != expectedNeighbor
            || !cache.IsSliceFinalCompositeReady(tile) || !cache.IsSliceFinalCompositeReady(neighbor)
            || cache.MinimumHeight != Mathf.Min(expected, expectedNeighbor) || cache.MaximumHeight != Mathf.Max(expected, expectedNeighbor)
            || !cache.TryGetCommittedRange(tile, out low, out high, out _) || low != committedLow || high != committedHigh)
            throw new InvalidOperationException("Dirty fixture range/readiness metadata changed unexpectedly.");
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


    private static void RunClipmapStrideValidation()
    {
        bool valid =
            true;

        StringBuilder details =
            new StringBuilder();

        int levelCount =
            Mathf.Min(
                7,
                TerrainClipmapTopologyUtility
                    .MaximumLevelCount
            );

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            int expectedStride =
                1 <<
                level;

            bool resolved =
                TerrainHeightResolutionUtility
                    .TryGetRequiredStrideForClipmapLevel(
                        1,
                        level,
                        out int actualStride,
                        out string errorMessage
                    );

            if (
                !resolved
                ||
                actualStride !=
                    expectedStride
            )
            {
                valid =
                    false;

                details.AppendLine(
                    resolved
                        ? $"LOD{level}: expected {expectedStride}, " +
                            $"actual {actualStride}."
                        : $"LOD{level}: {errorMessage}"
                );
            }
        }

        AddResult(
            "Clipmap Height stride progression",
            valid
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            valid
                ? $"Validated LOD0 through LOD{levelCount - 1} from " +
                    "base sample step 1."
                : details.ToString()
        );
    }

    private static void RunHeightRepresentationValidation(
        WorldSettings worldSettings
    )
    {
        int intervals =
            worldSettings
                .HeightTileIntervalsPerSide;

        float nativeSpacing =
            TerrainHeightResolutionUtility
                .GetNativeSampleSpacing(
                    worldSettings
                );

        float tileWorldSize =
            worldSettings
                .HeightTileWorldSize;

        bool valid =
            true;

        int testedCount =
            0;

        StringBuilder details =
            new StringBuilder();

        int stride =
            1;

        while (
            stride > 0
            &&
            stride <= intervals
        )
        {
            if (
                TerrainHeightResolutionUtility
                    .IsRepresentationStrideCompatible(
                        worldSettings,
                        stride
                    )
            )
            {
                int samplesPerSide =
                    TerrainHeightResolutionUtility
                        .GetSamplesPerSide(
                            worldSettings,
                            stride
                        );

                float sampleSpacing =
                    TerrainHeightResolutionUtility
                        .GetSampleSpacing(
                            worldSettings,
                            stride
                        );

                int expectedSamples =
                    intervals /
                    stride +
                    1;

                float expectedSpacing =
                    nativeSpacing *
                    stride;

                float representedTileWorldSize =
                    (
                        samplesPerSide -
                        1
                    )
                    *
                    sampleSpacing;

                bool representationValid =
                    samplesPerSide ==
                        expectedSamples
                    &&
                    Approximately(
                        sampleSpacing,
                        expectedSpacing
                    )
                    &&
                    Approximately(
                        representedTileWorldSize,
                        tileWorldSize
                    );

                if (!representationValid)
                {
                    valid =
                        false;

                    details.AppendLine(
                        $"Stride {stride}: samples={samplesPerSide} " +
                        $"(expected {expectedSamples}), spacing=" +
                        $"{sampleSpacing:R} (expected " +
                        $"{expectedSpacing:R}), footprint=" +
                        $"{representedTileWorldSize:R} (expected " +
                        $"{tileWorldSize:R})."
                    );
                }

                testedCount++;
            }

            if (stride > int.MaxValue / 2)
            {
                break;
            }

            stride *=
                2;
        }

        AddResult(
            "Height representation resolution",
            valid
                &&
                testedCount > 0
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
            valid
                && testedCount > 0
                    ? $"Validated {testedCount} compatible power-of-two " +
                        "Height representations with invariant tile " +
                        "footprint."
                    : details.ToString()
        );

        bool runtimeDelegationValid =
            TerrainHeightStreamingPyramidPolicy
                .GetSamplesPerSide(
                    worldSettings,
                    1
                )
            ==
            TerrainHeightResolutionUtility
                .GetSamplesPerSide(
                    worldSettings,
                    1
                )
            &&
            Approximately(
                TerrainHeightStreamingPyramidPolicy
                    .GetSampleSpacing(
                        worldSettings,
                        1
                    ),
                TerrainHeightResolutionUtility
                    .GetSampleSpacing(
                        worldSettings,
                        1
                    )
            );

        int derivedStride =
            TerrainHeightStreamingPyramidPolicy
                .GetMaximumSupportedDerivedStride(
                    worldSettings
                );

        if (derivedStride >= 2)
        {
            runtimeDelegationValid =
                runtimeDelegationValid
                &&
                TerrainHeightStreamingPyramidPolicy
                    .GetSamplesPerSide(
                        worldSettings,
                        derivedStride
                    )
                ==
                TerrainHeightResolutionUtility
                    .GetSamplesPerSide(
                        worldSettings,
                        derivedStride
                    )
                &&
                Approximately(
                    TerrainHeightStreamingPyramidPolicy
                        .GetSampleSpacing(
                            worldSettings,
                            derivedStride
                        ),
                    TerrainHeightResolutionUtility
                        .GetSampleSpacing(
                            worldSettings,
                            derivedStride
                        )
                );
        }

        AddResult(
            "Runtime Height policy representation delegation",
            runtimeDelegationValid
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            runtimeDelegationValid
                ? "Runtime streaming policy and shared Height resolution " +
                    "mathematics agree for native and configured derived " +
                    "representations."
                : "Runtime streaming policy and shared Height resolution " +
                    "mathematics disagree."
        );
    }

    private static void RunCoarseStagingAllocationValidation(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainAuthoringHeightManifest manifest
    )
    {
        int intervals =
            worldSettings
                .HeightTileIntervalsPerSide;

        int sampleStride =
            2;

        while (
            sampleStride > 0
            &&
            sampleStride <= intervals
            &&
            !TerrainHeightResolutionUtility
                .IsRepresentationStrideCompatible(
                    worldSettings,
                    sampleStride
                )
        )
        {
            if (sampleStride > int.MaxValue / 2)
            {
                sampleStride =
                    0;

                break;
            }

            sampleStride *=
                2;
        }

        if (
            sampleStride < 2
            ||
            sampleStride > intervals
        )
        {
            AddResult(
                "Coarse staging cache allocation",
                ValidationOutcome.Blocked,
                "The current Height tile topology has no compatible " +
                "derived power-of-two representation."
            );

            return;
        }

        TerrainHeightCacheWindow testWindow =
            new TerrainHeightCacheWindow(
                Vector2Int.zero,
                new Vector2Int(
                    Mathf.Min(
                        2,
                        manifest
                            .heightTileGridWidth
                    ),
                    Mathf.Min(
                        2,
                        manifest
                            .heightTileGridHeight
                    )
                )
            );

        if (!testWindow.IsValid)
        {
            AddResult(
                "Coarse staging cache allocation",
                ValidationOutcome.Blocked,
                "The committed Height tile grid is empty."
            );

            return;
        }

        TerrainAuthoringPreviewCache validationCache =
            new TerrainAuthoringPreviewCache();

        try
        {
            if (
                !validationCache
                    .TryInitializeStagingWindow(
                        worldSettings,
                        authoringData,
                        testWindow,
                        sampleStride,
                        out string stagingError
                    )
            )
            {
                AddResult(
                    "Coarse staging cache allocation",
                    ValidationOutcome.Fail,
                    stagingError
                );

                return;
            }

            int expectedSamples =
                TerrainHeightResolutionUtility
                    .GetSamplesPerSide(
                        worldSettings,
                        sampleStride
                    );

            float expectedSpacing =
                TerrainHeightResolutionUtility
                    .GetSampleSpacing(
                        worldSettings,
                        sampleStride
                    );

            long expectedMemoryBytes =
                (long)expectedSamples
                *
                expectedSamples
                *
                testWindow.TileCount
                *
                sizeof(float);

            bool coverageAvailable =
                validationCache
                    .TryGetWorldCoverage(
                        out Vector2 minimumXZ,
                        out Vector2 maximumXZ
                    );

            float representedTileWorldSize =
                (
                    validationCache
                        .SamplesPerSide -
                    1
                )
                *
                validationCache
                    .SampleSpacing;

            bool allocationValid =
                validationCache.SampleStride ==
                    sampleStride
                &&
                validationCache.SamplesPerSide ==
                    expectedSamples
                &&
                Approximately(
                    validationCache.SampleSpacing,
                    expectedSpacing
                )
                &&
                validationCache.CacheOriginTile ==
                    testWindow.OriginTile
                &&
                validationCache.CacheSize ==
                    testWindow.Size
                &&
                validationCache.HeightCache !=
                    null
                &&
                validationCache.HeightCache.width ==
                    expectedSamples
                &&
                validationCache.HeightCache.height ==
                    expectedSamples
                &&
                validationCache.ApproximateGpuMemoryBytes ==
                    expectedMemoryBytes
                &&
                coverageAvailable
                &&
                minimumXZ ==
                    Vector2.zero
                &&
                Approximately(
                    representedTileWorldSize,
                    worldSettings
                        .HeightTileWorldSize
                )
                &&
                !validationCache
                    .IsCompleteForActivation;

            AddResult(
                "Coarse staging cache allocation",
                allocationValid
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Stride={validationCache.SampleStride}, " +
                $"Samples={validationCache.SamplesPerSide}, " +
                $"Spacing={validationCache.SampleSpacing:R}, " +
                $"Window={validationCache.CacheSize}, " +
                $"Coverage={minimumXZ} -> {maximumXZ}, " +
                $"Bytes={validationCache.ApproximateGpuMemoryBytes}."
            );

            RunCoarseStagingMaterializationValidation(
                worldSettings,
                manifest,
                validationCache,
                testWindow
            );
        }
        finally
        {
            validationCache.Dispose();
        }
    }


    private static void RunDerivedCommittedSourceValidation()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WorldMeshesHeightSource-" + Guid.NewGuid().ToString("N"));
        Texture2D native = null;
        TerrainAuthoringPreviewHeightSourceLease owned = null;
        try
        {
            const int nativeSize = 17;
            const int stride = 4;
            const int size = 5;
            var samples = new float[nativeSize * nativeSize];
            for (int z = 0; z < nativeSize; z++)
                for (int x = 0; x < nativeSize; x++) samples[x + z * nativeSize] = x + z * 100f;
            native = new Texture2D(nativeSize, nativeSize, TextureFormat.RFloat, false, true);
            native.hideFlags = HideFlags.HideAndDontSave;
            native.SetPixelData(samples, 0);
            native.Apply(false, false);
            if (!TerrainAuthoringPreviewDerivedHeightCache.TryExtract(native, nativeSize, stride, out byte[] bytes, out string error))
                throw new InvalidOperationException(error);
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    if (BitConverter.ToSingle(bytes, (x + z * size) * sizeof(float)) != samples[x * stride + z * stride * nativeSize])
                        throw new InvalidOperationException("Derived samples differ from the exact native lattice, including the last edges.");

            // The adjacent native tile begins at the previous tile's last
            // interval. Its derived first edge must remain exactly shared.
            var neighbour = new float[nativeSize * nativeSize];
            for (int z = 0; z < nativeSize; z++)
                for (int x = 0; x < nativeSize; x++) neighbour[x + z * nativeSize] = x + nativeSize - 1 + z * 100f;
            native.SetPixelData(neighbour, 0);
            native.Apply(false, false);
            if (!TerrainAuthoringPreviewDerivedHeightCache.TryExtract(native, nativeSize, stride, out var neighbourBytes, out error))
                throw new InvalidOperationException(error);
            for (int z = 0; z < size; z++)
                if (BitConverter.ToSingle(bytes, (size - 1 + z * size) * sizeof(float))
                    != BitConverter.ToSingle(neighbourBytes, z * size * sizeof(float)))
                    throw new InvalidOperationException("Adjacent derived tiles disagree at their shared native edge.");
            native.SetPixelData(samples, 0);
            native.Apply(false, false);

            var identity = new TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity(
                Hash128.Compute("Validation world topology").ToString(), Hash128.Compute("Validation source").ToString(),
                new Vector2Int(2, 3), nativeSize, stride);
            if (TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, identity, out _, out _) != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Missing)
                throw new InvalidOperationException("A missing entry was not reported as missing.");
            if (!TerrainAuthoringPreviewDerivedHeightCache.TryWrite(root, identity, bytes, () => true, out error)
                || TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, identity, out var read, out error)
                    != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Hit)
                throw new InvalidOperationException("A written entry could not be read: " + error);
            for (int i = 0; i < bytes.Length; i++)
                if (read[i] != bytes[i]) throw new InvalidOperationException("The persistent payload changed.");

            var stale = new TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity(identity.Namespace,
                Hash128.Compute("Changed tile source").ToString(), identity.Tile, nativeSize, stride);
            if (TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, stale, out _, out _) != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Rejected)
                throw new InvalidOperationException("A stale tile-specific source identity was accepted.");
            if (TerrainAuthoringPreviewDerivedHeightCache.TryWrite(root, identity, bytes, () => false, out _)
                || TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, identity, out _, out _) != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Hit)
                throw new InvalidOperationException("A superseded write replaced the previously valid entry.");
            if (System.IO.Directory.GetFiles(root, "*.tmp", System.IO.SearchOption.AllDirectories).Length != 0)
                throw new InvalidOperationException("An interrupted candidate retained a temporary file.");

            string path = TerrainAuthoringPreviewDerivedHeightCache.GetEntryPath(root, identity);
            byte[] validFile = System.IO.File.ReadAllBytes(path);
            System.IO.File.WriteAllBytes(path, new byte[7]);
            if (TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, identity, out _, out _) != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Rejected)
                throw new InvalidOperationException("A truncated entry was accepted.");
            byte[] corrupt = (byte[])validFile.Clone();
            corrupt[corrupt.Length - 1] ^= 1;
            System.IO.File.WriteAllBytes(path, corrupt);
            if (TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, identity, out _, out _) != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Rejected)
                throw new InvalidOperationException("Same-length payload corruption was accepted.");
            corrupt = (byte[])validFile.Clone();
            corrupt[4] ^= 1; // unsupported schema
            System.IO.File.WriteAllBytes(path, corrupt);
            if (TerrainAuthoringPreviewDerivedHeightCache.TryRead(root, identity, out _, out _) != TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Rejected)
                throw new InvalidOperationException("An incompatible format was accepted.");
            if (!TerrainAuthoringPreviewDerivedHeightCache.TryWrite(root, identity, bytes, () => true, out error))
                throw new InvalidOperationException("A corrupt entry could not be regenerated: " + error);

            string unavailableRoot = System.IO.Path.Combine(root, "unavailable");
            System.IO.File.WriteAllText(unavailableRoot, "This is a file, not a directory.");
            if (TerrainAuthoringPreviewDerivedHeightCache.TryWrite(unavailableRoot, identity, bytes, () => true, out _))
                throw new InvalidOperationException("An unavailable cache directory was reported as writable.");

            if (!TerrainAuthoringPreviewDerivedHeightCache.TryCreateTexture(bytes, size, out var derived, out error))
                throw new InvalidOperationException(error);
            owned = new TerrainAuthoringPreviewHeightSourceLease(derived, stride, nativeSize, true, identity);
            if (!owned.CanMaterializeAt(stride) || !owned.CanMaterializeAt(8) || owned.CanMaterializeAt(2))
                throw new InvalidOperationException("A lease accepted incompatible source/destination strides.");
            owned.Dispose();
            owned.Dispose();
            if (owned.Texture != null || derived != null) throw new InvalidOperationException("An owned derived source survived disposal.");
            using (var borrowed = new TerrainAuthoringPreviewHeightSourceLease(native, 1, nativeSize, false)) { }
            if (native == null) throw new InvalidOperationException("Disposing a borrowed lease destroyed the native asset.");
            AddResult("Persistent committed Height sources", ValidationOutcome.Pass,
                "Exact lattice/edges, warm file read, stale identity, truncated/corrupt/obsolete entries, atomic rejection, unavailable directory and lease disposal.");
        }
        catch (Exception exception)
        {
            AddResult("Persistent committed Height sources", ValidationOutcome.Fail, exception.Message);
        }
        finally
        {
            owned?.Dispose();
            if (native != null) UnityEngine.Object.DestroyImmediate(native);
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    private static void RunExactHeightMaterializationValidation()
    {
        if (
            !SystemInfo.supportsComputeShaders
            || !SystemInfo.supportsAsyncGPUReadback
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            AddResult(
                "Exact Height representation lattice",
                ValidationOutcome.Blocked,
                "Compute shaders, RFloat random writes, and async GPU readback are required."
            );
            return;
        }

        const int nativeSize = 17;
        const float untouchedValue = -12345f;
        Texture2D source = null;
        RenderTexture destination = null;
        Texture2D seed = null;

        try
        {
            float[] nativeSamples = new float[nativeSize * nativeSize];
            for (int z = 0; z < nativeSize; z++)
            {
                for (int x = 0; x < nativeSize; x++)
                {
                    nativeSamples[x + z * nativeSize] = x + z * 100f;
                }
            }
            nativeSamples[1 + nativeSize] = -10000f;

            source = new Texture2D(
                nativeSize, nativeSize, TextureFormat.RFloat, false, true
            );
            source.name = "Height Representation Validation Native Source";
            source.hideFlags = HideFlags.HideAndDontSave;
            source.SetPixelData(nativeSamples, 0);
            source.Apply(false, false);

            TerrainAuthoringPreviewHeightMaterializer materializer =
                new TerrainAuthoringPreviewHeightMaterializer();
            float[] strideTwoSamples = null;

            foreach (int stride in new[] { 1, 2, 4, 8, 16 })
            {
                int size = (nativeSize - 1) / stride + 1;
                destination = new RenderTexture(
                    size, size, 0, RenderTextureFormat.RFloat,
                    RenderTextureReadWrite.Linear
                );
                destination.name = "Height Representation Validation Array";
                destination.dimension = TextureDimension.Tex2DArray;
                destination.volumeDepth = 2;
                destination.enableRandomWrite = true;
                destination.useMipMap = false;
                destination.autoGenerateMips = false;
                destination.filterMode = FilterMode.Point;
                destination.hideFlags = HideFlags.HideAndDontSave;
                destination.Create();

                seed = new Texture2D(size, size, TextureFormat.RFloat, false, true);
                seed.hideFlags = HideFlags.HideAndDontSave;
                float[] seedSamples = new float[size * size];
                for (int index = 0; index < seedSamples.Length; index++)
                {
                    seedSamples[index] = untouchedValue;
                }
                seed.SetPixelData(seedSamples, 0);
                seed.Apply(false, true);
                Graphics.CopyTexture(seed, 0, 0, destination, 0, 0);
                Graphics.CopyTexture(seed, 0, 0, destination, 1, 0);

                bool invalidSliceRejected = !materializer.TryMaterialize(
                    source, destination, -1, nativeSize, size, stride, out _
                );
                bool invalidStrideRejected = !materializer.TryMaterialize(
                    source, destination, 1, nativeSize, size, 3, out _
                );
                bool invalidDimensionsRejected = !materializer.TryMaterialize(
                    source, destination, 1, nativeSize, size + 1, stride, out _
                );
                if (
                    !invalidSliceRejected
                    || !invalidStrideRejected
                    || !invalidDimensionsRejected
                )
                {
                    throw new InvalidOperationException(
                        "Height materialization accepted an invalid slice, stride, or dimension."
                    );
                }

                if (
                    !materializer.TryMaterialize(
                        source, destination, 1, nativeSize, size, stride,
                        out string materializationError
                    )
                )
                {
                    throw new InvalidOperationException(materializationError);
                }

                // This small readback runs only from explicit deferred validation.
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(destination, 0);
                request.WaitForCompletion();
                if (request.hasError || request.layerCount != 2)
                {
                    throw new InvalidOperationException(
                        "Height representation validation readback failed."
                    );
                }

                var untouched = request.GetData<float>(0);
                var materialized = request.GetData<float>(1);
                bool passed =
                    untouched.Length == size * size
                    && materialized.Length == size * size;
                if (passed)
                {
                    for (int z = 0; z < size; z++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            int index = x + z * size;
                            passed &= untouched[index] == untouchedValue;
                            passed &= materialized[index] ==
                                nativeSamples[x * stride + z * stride * nativeSize];
                        }
                    }
                }
                AddResult(
                    $"Exact Height lattice at stride {stride}",
                    passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                    $"Checked all {size * size} exact samples, both tile edges, and untouched slice 0."
                );

                if (stride == 2 && passed)
                {
                    strideTwoSamples = materialized.ToArray();
                }
                else if (stride == 4)
                {
                    bool sharedPassed = passed && strideTwoSamples != null;
                    if (sharedPassed)
                    {
                        for (int z = 0; z < size; z++)
                        {
                            for (int x = 0; x < size; x++)
                            {
                                sharedPassed &= materialized[x + z * size] ==
                                    strideTwoSamples[x * 2 + z * 2 * 9];
                            }
                        }
                    }
                    AddResult(
                        "Cross-resolution shared Height lattice",
                        sharedPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                        "Stride-4 samples must match every second stride-2 sample."
                    );
                }


                if (stride >= 2)
                {
                    if (!TerrainAuthoringPreviewDerivedHeightCache.TryExtract(source, nativeSize, 2,
                        out byte[] derivedBytes, out string sourceError)
                        || !TerrainAuthoringPreviewDerivedHeightCache.TryCreateTexture(derivedBytes, 9, out var derived, out sourceError))
                        throw new InvalidOperationException(sourceError);
                    using (var lease = new TerrainAuthoringPreviewHeightSourceLease(derived, 2, nativeSize, true))
                    {
                        if (materializer.TryMaterialize(lease, destination, 1, nativeSize, size, 1, out _)
                            || !materializer.TryMaterialize(lease, destination, 1, nativeSize, size, stride, out sourceError))
                            throw new InvalidOperationException("Source-relative materialization failed: " + sourceError);
                        var derivedReadback = AsyncGPUReadback.Request(destination, 0);
                        derivedReadback.WaitForCompletion();
                        if (derivedReadback.hasError || derivedReadback.layerCount != 2)
                            throw new InvalidOperationException("Derived-source GPU readback failed.");
                        var derivedSamples = derivedReadback.GetData<float>(1);
                        var untouchedSamples = derivedReadback.GetData<float>(0);
                        for (int z = 0; z < size; z++)
                            for (int x = 0; x < size; x++)
                            {
                                int index = x + z * size;
                                if (derivedSamples[index] != nativeSamples[x * stride + z * stride * nativeSize]
                                    || untouchedSamples[index] != untouchedValue)
                                    throw new InvalidOperationException("Source-relative extraction changed the lattice or another slice.");
                            }
                    }
                    AddResult($"Derived source stride 2 to destination stride {stride}", ValidationOutcome.Pass,
                        "Verified GPU source-relative extraction, same-stride copy, last edges and no coarse-to-fine upsampling.");
                }
                destination.Release();
                UnityEngine.Object.DestroyImmediate(destination);
                destination = null;
                UnityEngine.Object.DestroyImmediate(seed);
                seed = null;
            }
        }
        catch (Exception exception)
        {
            AddResult(
                "Exact Height representation lattice",
                ValidationOutcome.Fail,
                exception.Message
            );
        }
        finally
        {
            if (destination != null)
            {
                destination.Release();
                UnityEngine.Object.DestroyImmediate(destination);
            }
            if (seed != null)
            {
                UnityEngine.Object.DestroyImmediate(seed);
            }
            if (source != null)
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }

    private static void RunCoarseStagingMaterializationValidation(
        WorldSettings worldSettings,
        TerrainAuthoringHeightManifest manifest,
        TerrainAuthoringPreviewCache validationCache,
        TerrainHeightCacheWindow testWindow
    )
    {
        if (
            !SystemInfo.supportsComputeShaders
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            AddResult(
                "Coarse staging materialization and composition",
                ValidationOutcome.Blocked,
                "Compute shaders and RFloat random writes are required."
            );
            return;
        }

        TerrainAuthoringPreviewHeightMaterializer materializer =
            new TerrainAuthoringPreviewHeightMaterializer();
        TerrainAuthoringData identityAuthoring = null;
        TerrainHeightCompositor compositor = new TerrainHeightCompositor();

        try
        {
            Vector2Int firstTile = testWindow.OriginTile;
            bool rejected = !validationCache.TryMaterializeCommittedBaseTile(
                (Texture2D)null, materializer, firstTile, out _
            );
            bool failureSafe = rejected
                && validationCache.GetSliceReadiness(firstTile) ==
                    TerrainAuthoringPreviewSliceReadiness.Uninitialized
                && !validationCache.TryGetCompositeSliceRange(
                    firstTile.x, firstTile.y, out _, out _
                )
                && !validationCache.IsCompleteForActivation;
            AddResult(
                "Failed materialization preserves uninitialized slice",
                failureSafe ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "A missing source must not acknowledge readiness or composite range."
            );

            identityAuthoring = ScriptableObject.CreateInstance<TerrainAuthoringData>();
            identityAuthoring.hideFlags = HideFlags.HideAndDontSave;

            for (int localZ = 0; localZ < testWindow.Height; localZ++)
            {
                for (int localX = 0; localX < testWindow.Width; localX++)
                {
                    Vector2Int tile = testWindow.OriginTile + new Vector2Int(localX, localZ);
                    if (
                        !TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile(
                            worldSettings, tile, out Texture2D source, out string loadError
                        )
                    )
                    {
                        throw new InvalidOperationException(loadError);
                    }
                    if (
                        !validationCache.TryMaterializeCommittedBaseTile(
                            source, materializer, tile, out string materializationError
                        )
                    )
                    {
                        throw new InvalidOperationException(materializationError);
                    }
                    if (
                        !manifest.TryGetTileHeightRange(tile.x, tile.y, out float nativeMin, out float nativeMax)
                        || !validationCache.TryGetCommittedRange(tile, out float baseMin, out float baseMax, out _)
                        || !validationCache.TryGetCompositeSliceRange(tile.x, tile.y, out float sliceMin, out float sliceMax)
                        || baseMin != nativeMin || baseMax != nativeMax
                        || sliceMin != nativeMin || sliceMax != nativeMax
                        || validationCache.GetSliceReadiness(tile) !=
                            TerrainAuthoringPreviewSliceReadiness.CommittedBaseReady
                        || validationCache.IsSliceFinalCompositeReady(tile)
                        || validationCache.IsCompleteForActivation
                    )
                    {
                        throw new InvalidOperationException(
                            "Coarse committed-base readiness or authoritative native ranges are invalid."
                        );
                    }

                    int slice = validationCache.GetSliceIndex(tile.x, tile.y);
                    if (tile == firstTile)
                    {
                        bool compositionRejected = !compositor.TryComposeTile(
                            validationCache.HeightCache, tile, slice,
                            validationCache.SamplesPerSide, validationCache.SampleSpacing,
                            worldSettings.HeightTileWorldSize * 2f, validationCache.WorldSizeXZ,
                            identityAuthoring, baseMin, baseMax, out _, out _, out _
                        );
                        bool readinessPreserved = compositionRejected
                            && validationCache.GetSliceReadiness(tile) ==
                                TerrainAuthoringPreviewSliceReadiness.CommittedBaseReady;
                        AddResult(
                            "Failed composition preserves committed-base readiness",
                            readinessPreserved ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                            "An invalid tile span must fail before final-composite acknowledgment."
                        );
                    }
                    if (
                        !compositor.TryComposeTile(
                            validationCache.HeightCache, tile, slice,
                            validationCache.SamplesPerSide, validationCache.SampleSpacing,
                            worldSettings.HeightTileWorldSize, validationCache.WorldSizeXZ,
                            identityAuthoring, baseMin, baseMax,
                            out float finalMin, out float finalMax, out string compositionError
                        )
                    )
                    {
                        throw new InvalidOperationException(compositionError);
                    }
                    if (
                        finalMin != nativeMin || finalMax != nativeMax
                        || !validationCache.TryCommitFinalCompositeTile(tile, finalMin, finalMax, out _)
                        || !validationCache.IsSliceFinalCompositeReady(tile)
                    )
                    {
                        throw new InvalidOperationException(
                            "Coarse identity composition did not preserve ranges or final readiness."
                        );
                    }
                }
            }

            if (!validationCache.TryFinalizeStagingForActivation(
                "Transient identity composition validation", out string finalizeError
            ))
            {
                throw new InvalidOperationException(finalizeError);
            }
            AddResult(
                "Coarse staging materialization and composition",
                validationCache.IsCompleteForActivation ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Native sources materialized into coarse slices with authoritative ranges; " +
                "the production range-aware compositor accepted coarse count/spacing and finalization."
            );
        }
        catch (Exception exception)
        {
            AddResult(
                "Coarse staging materialization and composition",
                ValidationOutcome.Fail,
                exception.Message
            );
        }
        finally
        {
            compositor.Dispose();
            if (identityAuthoring != null)
            {
                UnityEngine.Object.DestroyImmediate(identityAuthoring);
            }
        }
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
                validationCache.SampleStride ==
                    1
                &&
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
            "WorldMeshes Height Cache Window Validation"
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

        lastRunSummary =
            TerrainValidationRunSummary.CreateCompleted(
                passCount,
                failCount,
                blockedCount,
                $"{passCount} passed, {failCount} failed, " +
                $"{blockedCount} blocked."
            );

        if (failCount > 0)
        {
            builder.AppendLine(
                "Height Cache Window validation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else if (blockedCount > 0)
        {
            builder.AppendLine(
                "Height Cache Window validation: BLOCKED"
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Height Cache Window validation: PASSED"
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



