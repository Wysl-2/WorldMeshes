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
            RunSharedHeightPageValidation();
            RunSharedHeightSamplingValidation();
            RunGeographicAuthoringScopeValidation();
            RunSharedGeographicAuthoringValidation();
            RunSharedPreviewActivationValidation();

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


    private static TerrainAuthoringPreviewGeographicDemandPlan CreateSharedHeightValidationDemand(
        WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot policy, int stride, long generation)
    {
        var tile = new Vector2Int(1, 1);
        return new TerrainAuthoringPreviewGeographicDemandPlan(settings, policy,
            new TerrainAuthoringPreviewFocus(settings, Vector3.zero, TerrainAuthoringPreviewFocusKind.Canonical, 1),
            tile, new TerrainHeightCacheWindow(tile, Vector2Int.one), 1, 1, generation,
            new[]
            {
                new TerrainAuthoringPreviewGeographicTileDemand(tile, 1, stride, true,
                    TerrainAuthoringPreviewDisplayRequirement.Geometry, TerrainAuthoringPreviewNativeWorkingReason.None),
                new TerrainAuthoringPreviewGeographicTileDemand(new Vector2Int(2, 1), 4, 4, false,
                    TerrainAuthoringPreviewDisplayRequirement.SamplingDependency, TerrainAuthoringPreviewNativeWorkingReason.None),
                new TerrainAuthoringPreviewGeographicTileDemand(new Vector2Int(3, 2), 0, 0, false,
                    TerrainAuthoringPreviewDisplayRequirement.None, TerrainAuthoringPreviewNativeWorkingReason.Analysis)
            });
    }

    private static void RequireSharedHeightValidation(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void PrepareSharedHeightValidationPage(TerrainAuthoringPreviewSharedHeightCache cache,
        TerrainAuthoringPreviewHeightPageHandle handle, long authoringGeneration, float value)
    {
        Texture2D source = null;
        RenderTexture array = null;
        try
        {
            RequireSharedHeightValidation(cache.TryGetWritableCandidate(handle, out var writer, out string error), error);
            using (writer)
            {
                array = writer.Array;
                source = new Texture2D(array.width, array.height, TextureFormat.RFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                float[] data = new float[array.width * array.height];
                for (int i = 0; i < data.Length; i++) data[i] = value;
                source.SetPixelData(data, 0); source.Apply(false, false);
                Graphics.CopyTexture(source, 0, 0, array, writer.Slice, 0);
            }
            // This explicit isolated validation boundary also keeps the source alive through CopyTexture.
            var readback = AsyncGPUReadback.Request(array, 0); readback.WaitForCompletion();
            RequireSharedHeightValidation(!readback.hasError, "Shared Height fixture readback failed.");
            var pixels = readback.GetData<float>(handle.Slice);
            RequireSharedHeightValidation(pixels.Length == array.width * array.height, "Shared Height fixture dimensions changed.");
            for (int i = 0; i < pixels.Length; i++)
                RequireSharedHeightValidation(pixels[i] == value, "Shared Height fixture slice pixels changed.");
            RequireSharedHeightValidation(cache.TryMarkCommittedBase(handle, "Shared Height fixture", 1, out error), error);
            RequireSharedHeightValidation(!cache.TryMarkFinalComposite(handle, authoringGeneration + 1, value, value, out _)
                && !cache.TryMarkFinalComposite(handle, authoringGeneration, float.NaN, value, out _)
                && !cache.TryMarkFinalComposite(handle, authoringGeneration, value + 1, value, out _),
                "Shared Height accepted stale authoring or invalid height bounds.");
            RequireSharedHeightValidation(cache.TryMarkFinalComposite(handle, authoringGeneration, value, value, out error), error);
        }
        finally
        {
            if (source != null)
            {
                if (array != null && array.IsCreated())
                { var releaseBoundary = AsyncGPUReadback.Request(array, 0); releaseBoundary.WaitForCompletion(); }
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }

    private static void RunSharedHeightPageValidation()
    {
        if (!TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(17, 2, out string featureError)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat)
            || (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
        {
            AddResult("Shared Height page residency", ValidationOutcome.Blocked,
                string.IsNullOrEmpty(featureError) ? "The isolated fixture requires RFloat source textures and cross-type CopyTexture." : featureError);
            return;
        }
        WorldSettings settings = null;
        TerrainAuthoringPreviewSharedHeightCache cache = null;
        TerrainAuthoringPreviewHeightPageMap oldMap = null, newMap = null;
        try
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>(); settings.hideFlags = HideFlags.HideAndDontSave;
            settings.gridWidth = 7; settings.gridHeight = 5; settings.chunkSize = 16;
            settings.heightTileChunkSpan = 2; settings.heightfieldResolutionPerChunk = 8;
            var policy = new TerrainAuthoringPreviewQualitySnapshot(1, 4, gpuBudgetMiB: 1);
            var demand = CreateSharedHeightValidationDemand(settings, policy, 1, 1);
            RequireSharedHeightValidation(demand.TryValidate(settings, out string error), error);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, policy, demand,
                1, "Shared Height fixture", 1, out cache, out error), error);
            RequireSharedHeightValidation(cache.AllocatedBytes == 0, "Shared Height allocated before admission.");
            RequireSharedHeightValidation(!TerrainAuthoringPreviewHeightPagePool.TryEstimateBytes(int.MaxValue, int.MaxValue, out _)
                && !TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(17, int.MaxValue, out _),
                "Shared Height accepted overflowing byte arithmetic or impossible array depth.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, policy, demand,
                2, "Shared Height fixture", 1, out var overBudget, out error,
                new Dictionary<int, int> { { 1, int.MaxValue } }), error);
            using (overBudget)
                RequireSharedHeightValidation(!overBudget.TryReservePage(new Vector2Int(1, 1), 1, 1, 1, out _, out _)
                    && overBudget.AllocatedBytes == 0 && overBudget.PeakAllocatedBytes == 0,
                    "Shared Height allocated capacity beyond its budget.");
            var tile = new Vector2Int(1, 1);
            RequireSharedHeightValidation(!cache.TryReservePage(new Vector2Int(3, 2), 1, 1, 1, out _, out _)
                && !cache.TryReservePage(tile, 3, 1, 1, out _, out _), "Shared Height admitted native-only/invalid-stride display.");
            RequireSharedHeightValidation(cache.TryReservePage(tile, 1, 1, 1, out var a, out error), error);
            RequireSharedHeightValidation(!cache.TryCommitPage(a, out _), "Shared Height published uninitialized output.");
            var foreign = new TerrainAuthoringPreviewHeightPageHandle(a.OwnerId, a.ResourceGeneration + 1,
                a.AllocationGeneration, a.PageGeneration, a.RequestToken, a.DemandGeneration, a.Tile, a.Stride, a.PoolIndex, a.Slice);
            RequireSharedHeightValidation(!cache.TryGetWritableCandidate(foreign, out _, out _)
                && !cache.ReleaseCandidate(foreign, out _) && !cache.TryCommitPage(foreign, out _),
                "Shared Height accepted an incompatible resource generation.");
            PrepareSharedHeightValidationPage(cache, a, 1, 12.5f);
            RequireSharedHeightValidation(cache.TryCommitPage(a, out error), error);
            RequireSharedHeightValidation(!cache.TryCommitPage(a, out _) && !cache.TryGetWritableCandidate(a, out _, out _),
                "Shared Height accepted duplicate publication or wrote into a displayed page.");
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out oldMap, out error), error);
            RequireSharedHeightValidation(oldMap.Entries.Count == 2 && !oldMap.TryGetEntry(new Vector2Int(3, 2), out _)
                && oldMap.TryGetEntry(new Vector2Int(2, 1), out var missing) && !missing.IsValid && missing.Slice == -1 && missing.PoolIndex == -1,
                "Shared Height sparse map fabricated a missing/native page.");
            RequireSharedHeightValidation(oldMap.TryGetPoolTexture(a.PoolIndex, out var array, out error), error);
            RequireSharedHeightValidation(array.format == RenderTextureFormat.RFloat && array.dimension == TextureDimension.Tex2DArray
                && array.width == 17 && array.volumeDepth == 2 && array.enableRandomWrite && !array.useMipMap
                && cache.AllocatedBytes == 17L * 17 * 2 * sizeof(float), "Shared Height array format/capacity accounting changed.");
            long epoch = oldMap.MappingEpoch;
            demand = CreateSharedHeightValidationDemand(settings, policy, 4, 2);
            RequireSharedHeightValidation(cache.TryAcceptDemand(demand, "Shared Height fixture", 2, out error), error);
            RequireSharedHeightValidation(cache.TryReservePage(tile, 4, 2, 2, out var b, out error), error);
            RequireSharedHeightValidation(cache.ReleaseCandidate(b, out error), error);
            RequireSharedHeightValidation(cache.TryGetPublishedPage(tile, out var lastGood, out bool current)
                && lastGood.Handle.Equals(a) && !current, "Cancelled replacement lost last-good Height.");
            RequireSharedHeightValidation(cache.TryReservePage(tile, 4, 3, 2, out b, out error), error);
            PrepareSharedHeightValidationPage(cache, b, 2, -3.25f);
            RequireSharedHeightValidation(cache.TryCommitPage(b, out error), error);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out newMap, out error), error);
            RequireSharedHeightValidation(newMap.MappingEpoch > epoch && newMap.TryGetEntry(tile, out var changed)
                && changed.IsCurrent && changed.Page.Handle.Equals(b) && changed.Page.SamplesPerSide == 5
                && oldMap.TryGetEntry(tile, out var retained) && retained.Page.Handle.Equals(a) && retained.IsCurrent
                && cache.CapturePoolInventory()[a.PoolIndex].RetiringCount == 1,
                "Shared Height replacement mutated/recycled a retained map.");
            demand = CreateSharedHeightValidationDemand(settings, policy, 1, 3);
            RequireSharedHeightValidation(cache.TryAcceptDemand(demand, "Shared Height fixture", 3, out error), error);
            RequireSharedHeightValidation(cache.TryReservePage(tile, 1, 4, 3, out var c, out error) && c.Slice != a.Slice, error);
            RequireSharedHeightValidation(!cache.TryReservePage(tile, 1, 5, 3, out _, out _)
                && cache.ReleaseCandidate(c, out error), "Shared Height exhausted-pool admission replaced a valid pending page.");
            RequireSharedHeightValidation(cache.TryReservePage(tile, 1, 5, 3, out var d, out error) && d.Slice != a.Slice, error);
            RequireSharedHeightValidation(!cache.TryGetWritableCandidate(c, out _, out _) && !cache.TryCommitPage(a, out _),
                "Shared Height accepted stale/recycled tokens.");
            RequireSharedHeightValidation(cache.ReleaseCandidate(d, out error), error);
            oldMap.Dispose(); oldMap = null;
            var boundary = AsyncGPUReadback.Request(array, 0); boundary.WaitForCompletion(); cache.CollectRetiredPages();
            RequireSharedHeightValidation(cache.CapturePoolInventory()[a.PoolIndex].RetiringCount == 0,
                "Shared Height retained a slice after its reader/GPU boundary completed.");
            RequireSharedHeightValidation(cache.PeakAllocatedBytes == (17L * 17 * 2 + 5L * 5 * 2) * sizeof(float)
                && cache.FailedAdmissions >= 3, "Shared Height peak/admission accounting changed.");
            newMap.Dispose(); newMap = null; cache.Dispose(); cache.Dispose();
            RequireSharedHeightValidation(cache.WaitForRelease(out error) && cache.ReleaseComplete && cache.AllocatedBytes == 0, error);
            AddResult("Shared Height page residency", ValidationOutcome.Pass,
                "Isolated partial-world fixture checked lazy arrays, exact pixels, invalid/native map rows, stride replacement, retained readers, stale tokens, fixed capacity and release. Live preview was not bound.");
        }
        catch (Exception exception)
        { AddResult("Shared Height page residency", ValidationOutcome.Fail, exception.ToString()); }
        finally
        {
            oldMap?.Dispose(); newMap?.Dispose();
            if (cache != null)
            {
                cache.Dispose();
                if (!cache.WaitForRelease(out string error)) AddResult("Shared Height fixture release", ValidationOutcome.Fail, error);
            }
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    private static int SharedProbeStride(Vector2Int tile) => tile.y < 2 ? tile.x < 2 ? 1 : 4 : tile.x < 2 ? 2 : 8;
    private static float SharedProbeBias(int stride) => stride == 1 ? 0 : stride == 2 ? -10 : stride == 4 ? 20 : 35;
    private static float SharedProbeHeight(float x, float z, float bias) => x * x * 0.01f + z * z * 0.02f + bias;
    private static float SharedProbeBilinear(Vector2 point, float spacing, float bias)
    {
        float x = Mathf.FloorToInt(point.x / spacing) * spacing, z = Mathf.FloorToInt(point.y / spacing) * spacing;
        float tx = (point.x - x) / spacing, tz = (point.y - z) / spacing;
        float a = SharedProbeHeight(x, z, bias), b = SharedProbeHeight(x + spacing, z, bias);
        float c = SharedProbeHeight(x, z + spacing, bias), d = SharedProbeHeight(x + spacing, z + spacing, bias);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }
    private static TerrainAuthoringPreviewGeographicDemandPlan CreateSharedSamplingDemand(WorldSettings settings,
        TerrainAuthoringPreviewQualitySnapshot quality, long generation, bool nativeReplacement = false, bool sparse = false, int uniformStride = 0)
    {
        var rows = new List<TerrainAuthoringPreviewGeographicTileDemand>();
        var editable = new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(3, 3));
        for (int z = 0; z < 4; z++) for (int x = 0; x < 4; x++)
        {
            var tile = new Vector2Int(x, z);
            if (sparse && (x < 1 || x > 2 || z < 1 || z > 2)) continue;
            int stride = uniformStride > 0 ? uniformStride : nativeReplacement && x == 2 && z == 1 ? 1 : SharedProbeStride(tile);
            rows.Add(new TerrainAuthoringPreviewGeographicTileDemand(tile, 1, stride, editable.Contains(tile),
                TerrainAuthoringPreviewDisplayRequirement.Geometry, TerrainAuthoringPreviewNativeWorkingReason.None));
        }
        if (sparse) rows.Add(new TerrainAuthoringPreviewGeographicTileDemand(new Vector2Int(3, 3), 0, 0, false,
            TerrainAuthoringPreviewDisplayRequirement.None, TerrainAuthoringPreviewNativeWorkingReason.Analysis));
        return new TerrainAuthoringPreviewGeographicDemandPlan(settings, quality,
            new TerrainAuthoringPreviewFocus(settings, Vector3.zero, TerrainAuthoringPreviewFocusKind.Canonical, 1),
            new Vector2Int(1, 1), editable, 1, 1, generation, rows.ToArray());
    }
    private static void FillSharedSamplingPage(WorldSettings settings, TerrainAuthoringPreviewSharedHeightCache cache,
        TerrainAuthoringPreviewHeightPageHandle handle, long authoring, float bias)
    {
        Texture2D source = null; RenderTexture array = null;
        try
        {
            RequireSharedHeightValidation(cache.TryGetWritableCandidate(handle, out var writer, out string error), error);
            float minimum = float.MaxValue, maximum = float.MinValue;
            using (writer)
            {
                array = writer.Array; int samples = array.width;
                float spacing = TerrainHeightResolutionUtility.GetSampleSpacing(settings, handle.Stride);
                float[] pixels = new float[samples * samples];
                for (int z = 0; z < samples; z++) for (int x = 0; x < samples; x++)
                {
                    float value = SharedProbeHeight(handle.Tile.x * settings.HeightTileWorldSize + x * spacing,
                        handle.Tile.y * settings.HeightTileWorldSize + z * spacing, bias);
                    pixels[x + z * samples] = value; minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
                }
                source = new Texture2D(samples, samples, TextureFormat.RFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
                source.SetPixelData(pixels, 0); source.Apply(false, false); Graphics.CopyTexture(source, 0, 0, array, handle.Slice, 0);
            }
            var boundary = AsyncGPUReadback.Request(array, 0); boundary.WaitForCompletion();
            RequireSharedHeightValidation(!boundary.hasError, "Shared sampling page upload readback failed.");
            RequireSharedHeightValidation(cache.TryMarkCommittedBase(handle, "Shared sampling fixture", 1, out string metadataError)
                && cache.TryMarkFinalComposite(handle, authoring, minimum, maximum, out metadataError)
                && cache.TryCommitPage(handle, out metadataError), metadataError);
        }
        finally
        {
            if (source != null)
            {
                if (array != null && array.IsCreated()) { var boundary = AsyncGPUReadback.Request(array, 0); boundary.WaitForCompletion(); }
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
    private static Color[] DrawSharedSamplingProbes(Material material, MaterialPropertyBlock block, Vector2[] points, float weight = 1)
    {
        Mesh mesh = null; RenderTexture target = null; CommandBuffer commands = null;
        try
        {
            int count = points.Length; var vertices = new Vector3[count * 4]; var uv = new Vector2[count * 4];
            var transition = new Vector2[count * 4]; var triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float left = -1 + 2f * i / count, right = -1 + 2f * (i + 1) / count; int v = i * 4;
                vertices[v] = new Vector3(left, -1, 0); vertices[v + 1] = new Vector3(right, -1, 0);
                vertices[v + 2] = new Vector3(right, 1, 0); vertices[v + 3] = new Vector3(left, 1, 0);
                for (int n = 0; n < 4; n++) { uv[v + n] = points[i]; transition[v + n] = new Vector2(weight, 0); }
                int t = i * 6; triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, vertices = vertices, uv = uv, uv2 = transition, triangles = triangles };
            target = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, useMipMap = false, antiAliasing = 1 };
            RequireSharedHeightValidation(target.Create() && target.IsCreated(), "Shared sampling probe render target creation failed.");
            commands = new CommandBuffer { name = "Terrain shared Height sampling probes" };
            commands.SetRenderTarget(target); commands.SetViewport(new Rect(0, 0, count, 1));
            commands.ClearRenderTarget(false, true, new Color(-999, -999, -999, -999));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, block); Graphics.ExecuteCommandBuffer(commands);
            var request = AsyncGPUReadback.Request(target, 0); request.WaitForCompletion();
            RequireSharedHeightValidation(!request.hasError, "Shared sampling shader readback failed.");
            return request.GetData<Color>().ToArray();
        }
        finally
        {
            commands?.Release();
            if (target != null) { if (target.IsCreated()) target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
    private static void ValidateHighestSharedHeightPool(Material probeMaterial)
    {
        WorldSettings settings = null; TerrainAuthoringPreviewSharedHeightCache cache = null;
        TerrainAuthoringPreviewHeightPageMap map = null; TerrainAuthoringPreviewSharedHeightBindingData data = null;
        try
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>(); settings.hideFlags = HideFlags.HideAndDontSave;
            settings.gridWidth = settings.gridHeight = 7; settings.chunkSize = 16;
            settings.heightTileChunkSpan = 2; settings.heightfieldResolutionPerChunk = 512;
            settings.clipmapLevelCount = 3; settings.clipmapCenterResolution = 8; settings.clipmapBaseSampleStep = 1;
            settings.clipmapLODOuterResolutions = new[] { 8, 8 };
            var quality = new TerrainAuthoringPreviewQualitySnapshot(3, 4, gpuBudgetMiB: 1);
            var demand = CreateSharedSamplingDemand(settings, quality, 1, uniformStride: 1024);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, quality, demand, 1,
                "Shared sampling fixture", 1, out cache, out string error), error);
            long token = 0;
            foreach (var row in demand.Tiles)
            {
                RequireSharedHeightValidation(cache.TryReservePage(row.Tile, row.SelectedDisplayStride, ++token,
                    demand.Generation, out var page, out error), error);
                FillSharedSamplingPage(settings, cache, page, 1, 0);
            }
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error);
            RequireSharedHeightValidation(map.Pools.Count == 11
                && TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out data, out error), error);
            var block = new MaterialPropertyBlock(); data.Apply(block);
            block.SetVector(Shader.PropertyToID("_WorldSizeXZ"), new Vector4(112, 112, 0, 0));
            block.SetFloat(Shader.PropertyToID("_WorldBoundsReady"), 1);
            block.SetFloat(Shader.PropertyToID("_HeightNormalSampleSpacingFine"), 1);
            block.SetFloat(Shader.PropertyToID("_HeightNormalSampleSpacingCoarse"), 1);
            var pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { new Vector2(16.5f, 16.5f) });
            RequireSharedHeightValidation(pixels[0].g > 0.99f
                && Mathf.Abs(pixels[0].r - SharedProbeBilinear(new Vector2(16.5f, 16.5f), 32, 0)) < 0.002f,
                "The highest supported pool slot/stride did not sample correctly on this graphics backend.");
            data.Dispose(); RequireSharedHeightValidation(data.WaitForRelease(out error), error);
            cache.Dispose(); RequireSharedHeightValidation(cache.WaitForRelease(out error), error);
            data = null; map = null; cache = null;
            // A world requiring a twelfth slot must reject upload explicitly, even
            // when none of its physical arrays has yet been allocated.
            settings.heightfieldResolutionPerChunk = 1024;
            demand = CreateSharedSamplingDemand(settings, quality, 1);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, quality, demand, 1,
                "Shared sampling fixture", 1, out cache, out error), error);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error);
            RequireSharedHeightValidation(map.Pools.Count == 12
                && !TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out _, out _)
                && cache.LookupAllocatedBytes == 0, "Unsupported twelfth pool was silently clamped or uploaded.");
        }
        finally
        {
            string releaseError = null;
            if (data != null) { data.Dispose(); if (!data.WaitForRelease(out string error)) releaseError = error; }
            map?.Dispose();
            if (cache != null) { cache.Dispose(); if (!cache.WaitForRelease(out string error)) releaseError = error; }
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
            if (releaseError != null) throw new InvalidOperationException(releaseError);
        }
    }
    private static void RunSharedHeightSamplingValidation()
    {
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out string featureError)
            || !TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(17, 8, out featureError)
            || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
            || (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
        { AddResult("Shared Height shader sampling", ValidationOutcome.Blocked, string.IsNullOrEmpty(featureError) ? "The probe requires float render targets and cross-type CopyTexture." : featureError); return; }
        WorldSettings settings = null; TerrainAuthoringPreviewSharedHeightCache cache = null;
        Material sourceMaterial = null, sharedMaterial = null, probeSource = null, probeMaterial = null;
        var payloads = new List<TerrainAuthoringPreviewSharedHeightBindingData>();
        var objects = new List<GameObject>(); var bindings = new List<TerrainClipmapRendererBinding>();
        var unclaimedMaps = new List<TerrainAuthoringPreviewHeightPageMap>();
        try
        {
            var terrainShader = Shader.Find("Custom/ClipmapTerrain"); var probeShader = Shader.Find("Hidden/WorldMeshes/TerrainSharedHeightSamplingValidation");
            RequireSharedHeightValidation(terrainShader != null && probeShader != null, "Shared terrain/probe shader assets are missing.");
            sourceMaterial = new Material(terrainShader) { hideFlags = HideFlags.HideAndDontSave };
            probeSource = new Material(probeShader) { hideFlags = HideFlags.HideAndDontSave };
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreateMaterial(sourceMaterial, out sharedMaterial, out string error), error);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreateMaterial(probeSource, out probeMaterial, out error), error);
            RequireSharedHeightValidation(!sourceMaterial.IsKeywordEnabled(TerrainAuthoringPreviewSharedHeightBindingData.ShaderKeyword), "Shared variant changed the source material.");
            settings = ScriptableObject.CreateInstance<WorldSettings>(); settings.hideFlags = HideFlags.HideAndDontSave;
            settings.gridWidth = settings.gridHeight = 7; settings.chunkSize = 16;
            settings.heightTileChunkSpan = 2; settings.heightfieldResolutionPerChunk = 8;
            settings.clipmapLevelCount = 3; settings.clipmapCenterResolution = 8; settings.clipmapBaseSampleStep = 1;
            settings.clipmapLODOuterResolutions = new[] { 8, 8 };
            var quality = new TerrainAuthoringPreviewQualitySnapshot(3, 4, gpuBudgetMiB: 1);
            var demand = CreateSharedSamplingDemand(settings, quality, 1);
            var layout = new TerrainClipmapLayout();
            RequireSharedHeightValidation(TerrainClipmapLayoutUtility.TryCalculateLayout(settings, new Vector3(56, 0, 56), 0, layout, out error), error);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, quality, demand, 1,
                "Shared sampling fixture", 1, out cache, out error), error);
            long token = 0;
            foreach (var row in demand.Tiles)
            {
                RequireSharedHeightValidation(cache.TryReservePage(row.Tile, row.SelectedDisplayStride, ++token, demand.Generation, out var page, out error), error);
                FillSharedSamplingPage(settings, cache, page, 1, SharedProbeBias(row.SelectedDisplayStride));
            }
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("Shared Height fixture") { hideFlags = HideFlags.HideAndDontSave }; objects.Add(go);
                var renderer = go.AddComponent<MeshRenderer>(); renderer.enabled = false; renderer.sharedMaterial = sharedMaterial;
                TerrainClipmapRendererRole role;
                if (i == 0) role = TerrainClipmapRendererRole.CreateCenter();
                else if (i < 3) TerrainClipmapRendererRole.TryCreateRing(i, out role);
                else TerrainClipmapRendererRole.TryCreateStitch(i - 3, i - 2, out role);
                bindings.Add(new TerrainClipmapRendererBinding(renderer, role));
                var marker = new MaterialPropertyBlock(); marker.SetFloat(Shader.PropertyToID("_AuthoringVisualizationMode"), 73); renderer.SetPropertyBlock(marker);
            }
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out var map, out error), error); unclaimedMaps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var data, out error), error); payloads.Add(data);
            RequireSharedHeightValidation(cache.LookupAllocatedBytes == 4 * 4 * 16 && data.DrawableCount == 16 && data.CurrentCount == 16,
                "Shared lookup byte/currentness accounting changed.");
            RequireSharedHeightValidation(!TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out _, out _), "One map was transferred to two binding owners.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewHeightBindingUtility.TryBindShared(settings, quality, layout, demand, data, bindings, false, out error), error);
            var block = new MaterialPropertyBlock(); bindings[0].Renderer.GetPropertyBlock(block);
            RequireSharedHeightValidation(block.GetFloat(Shader.PropertyToID("_AuthoringVisualizationMode")) == 73, "Shared binding overwrote visualization state.");
            var interior = new[] { new Vector2(16, 16), new Vector2(16.5f, 16.5f), new Vector2(80.5f, 16.5f), new Vector2(112, 112), new Vector2(120, 120) };
            var pixels = DrawSharedSamplingProbes(probeMaterial, block, interior);
            var expected = new[] { SharedProbeHeight(16, 16, 0), SharedProbeBilinear(interior[1], 2, 0), SharedProbeBilinear(interior[2], 8, 20), SharedProbeHeight(112, 112, 35), SharedProbeHeight(112, 112, 35) };
            for (int i = 0; i < pixels.Length; i++) RequireSharedHeightValidation(pixels[i].g > 0.99f && Mathf.Abs(pixels[i].r - expected[i]) < 0.002f,
                "Shared exact/bilinear/world-edge sample failed at probe " + i + ".");
            var equalEdges = new[] { new Vector2(32, 1), new Vector2(1, 32), new Vector2(32, 2), new Vector2(2, 32) };
            pixels = DrawSharedSamplingProbes(probeMaterial, block, equalEdges);
            for (int i = 0; i < equalEdges.Length; i++)
                RequireSharedHeightValidation(pixels[i].g > 0.99f
                    && Mathf.Abs(pixels[i].r - SharedProbeBilinear(equalEdges[i], 2, 0)) < 0.002f,
                    "Equal-resolution compatible edge/lattice samples were deformed near a corner.");
            const float epsilon = 0.002f;
            var seams = new[] { new Vector2(64 - epsilon, 48), new Vector2(64, 48), new Vector2(64 + epsilon, 48),
                new Vector2(48, 64 - epsilon), new Vector2(48, 64), new Vector2(48, 64 + epsilon),
                new Vector2(64 - epsilon, 64 - epsilon), new Vector2(64 + epsilon, 64 - epsilon),
                new Vector2(64 - epsilon, 64 + epsilon), new Vector2(64 + epsilon, 64 + epsilon), new Vector2(64, 64),
                new Vector2(32 - epsilon, 16), new Vector2(32 + epsilon, 16) };
            pixels = DrawSharedSamplingProbes(probeMaterial, block, seams);
            foreach (var pixel in pixels) RequireSharedHeightValidation(pixel.g > 0.99f, "A resident shared seam was classified invalid.");
            foreach (var pair in new[] { new Vector2Int(0, 2), new Vector2Int(3, 5), new Vector2Int(6, 10), new Vector2Int(7, 10), new Vector2Int(8, 10), new Vector2Int(9, 10), new Vector2Int(11, 12) })
                RequireSharedHeightValidation(Mathf.Abs(pixels[pair.x].r - pixels[pair.y].r) < 0.04f
                    && Mathf.Abs(pixels[pair.x].b - pixels[pair.y].b) < 0.004f && Mathf.Abs(pixels[pair.x].a - pixels[pair.y].a) < 0.004f,
                    "Shared edge/corner height or normal continuity failed.");
            foreach (var binding in bindings)
            {
                binding.Renderer.GetPropertyBlock(block);
                pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { interior[2] });
                RequireSharedHeightValidation(Mathf.Abs(pixels[0].r - expected[2]) < 0.002f, "Different mesh roles sampled different published pages.");
            }
            block.SetVector(Shader.PropertyToID("_ClipmapTransitionOffset"), new Vector4(2, 0, -2, 0));
            foreach (float weight in new[] { 0f, 0.5f, 1f })
            {
                pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { interior[1] }, weight);
                RequireSharedHeightValidation(Mathf.Abs(pixels[0].r - SharedProbeBilinear(interior[1] + new Vector2(2, -2) * weight, 2, 0)) < 0.002f,
                    "Shared stitch transition did not sample displaced world coordinates.");
                var crossing = new Vector2(63, 48);
                var shifted = DrawSharedSamplingProbes(probeMaterial, block, new[] { crossing }, weight);
                block.SetVector(Shader.PropertyToID("_ClipmapTransitionOffset"), Vector4.zero);
                var reference = DrawSharedSamplingProbes(probeMaterial, block, new[] { crossing + new Vector2(2, -2) * weight }, weight);
                RequireSharedHeightValidation(shifted[0].g > 0.99f && reference[0].g > 0.99f
                    && Mathf.Abs(shifted[0].r - reference[0].r) < 0.002f,
                    "A stitch crossing the mixed tile boundary sampled before its world-space offset.");
                block.SetVector(Shader.PropertyToID("_ClipmapTransitionOffset"), new Vector4(2, 0, -2, 0));
            }
            block.SetVector(Shader.PropertyToID("_ClipmapTransitionOffset"), Vector4.zero);
            block.SetFloat(Shader.PropertyToID("_SharedHeightProbeClipWorld"), 1);
            pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { interior[3], interior[4] });
            RequireSharedHeightValidation(pixels[0].g > 0.99f && pixels[1].g == -999, "Shared exact logical-world clipping changed.");
            block.SetFloat(Shader.PropertyToID("_SharedHeightProbeClipWorld"), 0);
            demand = CreateSharedSamplingDemand(settings, quality, 2, true);
            RequireSharedHeightValidation(cache.TryAcceptDemand(demand, "Shared sampling fixture", 2, out error), error);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error); unclaimedMaps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var lastGood, out error), error); payloads.Add(lastGood);
            RequireSharedHeightValidation(!TerrainAuthoringPreviewHeightBindingUtility.TryPreflightShared(settings, quality, layout, demand, lastGood, bindings, false, out _)
                && TerrainAuthoringPreviewHeightBindingUtility.TryBindShared(settings, quality, layout, demand, lastGood, bindings, true, out error), error);
            bindings[0].Renderer.GetPropertyBlock(block); pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { new Vector2(80.5f, 40.5f) });
            RequireSharedHeightValidation(Mathf.Abs(pixels[0].r - SharedProbeBilinear(new Vector2(80.5f, 40.5f), 8, 20)) < 0.002f,
                "Last-good shared sampling used requested rather than published stride.");
            var changedTile = new Vector2Int(2, 1); RequireSharedHeightValidation(cache.ReleaseTile(changedTile, out error), error);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error); unclaimedMaps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var missing, out error), error); payloads.Add(missing);
            RequireSharedHeightValidation(missing.RequiredMissingCount == 1
                && !TerrainAuthoringPreviewHeightBindingUtility.TryPreflightShared(settings, quality, layout, demand, missing, bindings, true, out _),
                "Shared preflight declared missing required coverage ready.");
            missing.Apply(block); pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { new Vector2(80.5f, 40.5f) });
            RequireSharedHeightValidation(pixels[0].g == 0 && pixels[0].r == 999, "A missing shared page fabricated valid flat terrain.");
            lastGood.Apply(block); pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { new Vector2(80.5f, 40.5f) });
            RequireSharedHeightValidation(pixels[0].g > 0.99f, "A retained old map lost its retired physical page before rendering.");
            RequireSharedHeightValidation(cache.TryReservePage(changedTile, 1, ++token, demand.Generation, out var replacement, out error), error);
            FillSharedSamplingPage(settings, cache, replacement, 2, 100);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error); unclaimedMaps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var updated, out error), error); payloads.Add(updated);
            RequireSharedHeightValidation(updated.Map.TryGetEntry(changedTile, out var replacedEntry) && replacedEntry.IsCurrent
                && replacedEntry.Page.Handle.Stride == 1, "Replacement did not publish its new stride/current content.");
            updated.Apply(block); pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { new Vector2(80.5f, 40.5f) });
            RequireSharedHeightValidation(Mathf.Abs(pixels[0].r - SharedProbeBilinear(new Vector2(80.5f, 40.5f), 2, 100)) < 0.002f,
                "New mapping did not sample its newly published page.");
            lastGood.Apply(block); pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { new Vector2(80.5f, 40.5f) });
            RequireSharedHeightValidation(Mathf.Abs(pixels[0].r - SharedProbeBilinear(new Vector2(80.5f, 40.5f), 8, 20)) < 0.002f,
                "Replacement reused a page still referenced by an old GPU map.");
            // Leave an explicit sparse invalid cell after checking both epoch views.
            RequireSharedHeightValidation(cache.ReleaseTile(changedTile, out error), error);
            demand = CreateSharedSamplingDemand(settings, quality, 3, true, true);
            RequireSharedHeightValidation(cache.TryAcceptDemand(demand, "Shared sampling fixture", 2, out error), error);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error); unclaimedMaps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var sparse, out error), error); payloads.Add(sparse);
            RequireSharedHeightValidation(sparse.Window.OriginTile == new Vector2Int(1, 1) && sparse.Window.Size == new Vector2Int(2, 2) && map.Entries.Count == 4,
                "Shared local lookup included native-only rows or lost non-zero origin.");
            var lookupRead = AsyncGPUReadback.Request(sparse.Lookup, 0); lookupRead.WaitForCompletion();
            RequireSharedHeightValidation(!lookupRead.hasError && lookupRead.GetData<Color>()[1].r == 0
                && lookupRead.GetData<Color>()[1].g == -1 && lookupRead.GetData<Color>()[1].b == -1, "Shared GPU sparse invalid cell encoding changed.");
            // The disabled shared branch must execute the exact legacy lattice sampler.
            block.SetFloat(TerrainAuthoringPreviewSharedHeightBindingData.EnabledId, 0);
            RequireSharedHeightValidation(data.Map.TryGetPoolTexture(0, out var nativePool, out error), error);
            block.SetTexture(Shader.PropertyToID("_HeightCache"), nativePool);
            block.SetVector(Shader.PropertyToID("_HeightCacheOriginTile"), new Vector4(0, 0, 0, 0));
            block.SetVector(Shader.PropertyToID("_HeightCacheSize"), new Vector4(1, 1, 0, 0));
            block.SetFloat(Shader.PropertyToID("_HeightTileSamplesPerSide"), 17); block.SetFloat(Shader.PropertyToID("_HeightSampleSpacing"), 2);
            block.SetFloat(Shader.PropertyToID("_HeightCacheReady"), 1);
            pixels = DrawSharedSamplingProbes(probeMaterial, block, new[] { interior[1] });
            RequireSharedHeightValidation(pixels[0].g > 0.99f && Mathf.Abs(pixels[0].r - SharedProbeHeight(16, 16, 0)) < 0.002f,
                "Disabled shared mode changed legacy nearest-lattice sampling.");
            TerrainAuthoringPreviewHeightBindingUtility.DisableShared(bindings);
            foreach (var payload in payloads) { payload.Dispose(); RequireSharedHeightValidation(payload.WaitForRelease(out error), error); }
            RequireSharedHeightValidation(cache.LookupAllocatedBytes == 0, "Shared lookup budget leases leaked after GPU retirement.");
            ValidateHighestSharedHeightPool(probeMaterial);
            AddResult("Shared Height shader sampling", ValidationOutcome.Pass,
                "Controlled draws checked published-stride bilinear sampling, mixed/equal edges and corners, normals, all renderer roles/stitch weights, sparse invalid cells, last-good readers, legacy branch and world clipping. No live renderer was changed.");
        }
        catch (Exception exception) { AddResult("Shared Height shader sampling", ValidationOutcome.Fail, exception.ToString()); }
        finally
        {
            TerrainAuthoringPreviewHeightBindingUtility.DisableShared(bindings);
            foreach (var payload in payloads) { payload.Dispose(); if (!payload.WaitForRelease(out string error)) AddResult("Shared sampling lookup release", ValidationOutcome.Fail, error); }
            foreach (var map in unclaimedMaps) map.Dispose();
            if (cache != null) { cache.Dispose(); if (!cache.WaitForRelease(out string error)) AddResult("Shared sampling cache release", ValidationOutcome.Fail, error); }
            foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
            if (sourceMaterial != null) UnityEngine.Object.DestroyImmediate(sourceMaterial);
            if (sharedMaterial != null) UnityEngine.Object.DestroyImmediate(sharedMaterial);
            if (probeSource != null) UnityEngine.Object.DestroyImmediate(probeSource);
            if (probeMaterial != null) UnityEngine.Object.DestroyImmediate(probeMaterial);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    // Transient committed-height provider for isolated integration. The production
    // context uses the validated manifest and source utility; fixtures never generate
    // disk entries or mutate committed/authoring assets.
    private sealed class GeographicHeightFixtureSource : TerrainAuthoringPreviewCommittedSourceContext
    {
        public WorldSettings Settings { get; }
        public TerrainAuthoringData Data { get; }
        public TerrainAuthoringPreviewGeographicDemandPlan Demand { get; }
        public string CommittedSignature => "Transient committed Height";
        public long AuthoringGeneration { get; }
        internal bool Current = true, MissingNative;
        public bool IsCurrent => Current && Settings != null && Data != null && Demand.ConfigurationMatches(Settings);
        internal GeographicHeightFixtureSource(WorldSettings settings, TerrainAuthoringData data,
            TerrainAuthoringPreviewGeographicDemandPlan demand, long generation)
        { Settings = settings; Data = data; Demand = demand; AuthoringGeneration = generation; }
        public bool TryAcquire(Vector2Int tile, int stride, out TerrainAuthoringPreviewHeightSourceLease lease, out string error)
        {
            lease = null; error = "The transient native Height dependency is deliberately missing.";
            if (!IsCurrent || MissingNative && stride == 1) return false;
            int samples = TerrainHeightResolutionUtility.GetSamplesPerSide(Settings, stride);
            var texture = new Texture2D(samples, samples, TextureFormat.RFloat, false, true)
            { name = "Transient committed Height", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var values = new float[samples * samples];
            for (int z = 0; z < samples; z++) for (int x = 0; x < samples; x++)
                values[x + z * samples] = tile.x * 3f + tile.y * 9f + x * stride * 0.1f + z * stride * 0.2f;
            texture.SetPixelData(values, 0); texture.Apply(false, false);
            lease = new TerrainAuthoringPreviewHeightSourceLease(texture, stride, Settings.HeightTileSamplesPerSide,
                true, committedSignature: CommittedSignature); error = ""; return true;
        }
        public bool LeaseIsCurrent(TerrainAuthoringPreviewHeightSourceLease lease) => IsCurrent && lease != null
            && lease.Texture != null && lease.CommittedSignature == CommittedSignature;
        public bool TryGetRange(Vector2Int tile, out float low, out float high, out string error)
        { low = tile.x * 3f + tile.y * 9f; high = low + (Settings.HeightTileSamplesPerSide - 1) * 0.3f; error = ""; return IsCurrent; }
    }

    private static WorldSettings CreateGeographicAuthoringFixtureWorld()
    {
        var settings = ScriptableObject.CreateInstance<WorldSettings>(); settings.hideFlags = HideFlags.HideAndDontSave;
        settings.gridWidth = settings.gridHeight = 6; settings.chunkSize = 256;
        settings.heightTileChunkSpan = 2; settings.heightfieldResolutionPerChunk = 8;
        return settings;
    }
    private static TerrainAuthoringPreviewGeographicDemandPlan CreateGeographicAuthoringFixtureDemand(
        WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot policy)
    {
        var rows = new List<TerrainAuthoringPreviewGeographicTileDemand>();
        for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
        {
            bool display = z == 0 && x < 2;
            rows.Add(new TerrainAuthoringPreviewGeographicTileDemand(new Vector2Int(x, z), display ? 1 : 0,
                display ? x == 0 ? 1 : 4 : 0, x == 0 && z == 0,
                display ? TerrainAuthoringPreviewDisplayRequirement.Geometry : TerrainAuthoringPreviewDisplayRequirement.None,
                TerrainAuthoringPreviewNativeWorkingReason.Analysis));
        }
        return new TerrainAuthoringPreviewGeographicDemandPlan(settings, policy,
            new TerrainAuthoringPreviewFocus(settings, Vector3.zero, TerrainAuthoringPreviewFocusKind.Canonical, 1),
            Vector2Int.zero, new TerrainHeightCacheWindow(Vector2Int.zero, Vector2Int.one), 1, 1, 1, rows.ToArray());
    }
    private static void RunGeographicAuthoringScopeValidation()
    {
        WorldSettings settings = null; TerrainAuthoringPreviewSharedHeightCache cache = null;
        try
        {
            settings = CreateGeographicAuthoringFixtureWorld();
            var policy = new TerrainAuthoringPreviewQualitySnapshot(1, 4, gpuBudgetMiB: 1);
            var plan = CreateGeographicAuthoringFixtureDemand(settings, policy);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, policy, plan,
                1, "Transient committed Height", 1, out cache, out string error), error);
            var previous = new[] { Vector2Int.zero, new Vector2Int(2, 2) }; var next = new[] { new Vector2Int(1, 0) };
            RequireSharedHeightValidation(TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, plan, cache,
                1, 7, "Transient committed Height", previous, next, TerrainRegionalElevationInvalidationScope.None,
                out var local, out error), error);
            previous[0] = new Vector2Int(1, 1);
            RequireSharedHeightValidation(local.CanProveUntouched && local.DisplayJobCount == 2 && local.NativeChangedCount == 3
                && local.Affects(Vector2Int.zero) && !local.Affects(new Vector2Int(1, 1)), "Old/new footprint union is not immutable or geographical.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, plan, cache,
                1, 7, "Transient committed Height", null, next, TerrainRegionalElevationInvalidationScope.None,
                out var unknown, out error) && !unknown.CanProveUntouched && unknown.Targets.Count == 9 && unknown.DisplayJobCount == 2,
                "Incomplete scope must invalidate only requested geography: " + error);
            RequireSharedHeightValidation(TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, plan, cache,
                1, 7, "Transient committed Height", Array.Empty<Vector2Int>(), Array.Empty<Vector2Int>(),
                TerrainRegionalElevationInvalidationScope.WholeWorld, out var global, out error)
                && !global.CanProveUntouched && global.NativeChangedCount == 9 && cache.AllocatedBytes == 0,
                "Global scope must be conservative without allocating/enumerating the world: " + error);
            var bounds = new Bounds(new Vector3(800, 0, 20), Vector3.one);
            RequireSharedHeightValidation(TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, plan, cache,
                1, 7, "Transient committed Height", Array.Empty<Vector2Int>(), Array.Empty<Vector2Int>(),
                TerrainRegionalElevationInvalidationScope.FromWorldBounds(bounds), out var regional, out error)
                && regional.CanProveUntouched && regional.Affects(new Vector2Int(1, 0)) && !regional.Affects(Vector2Int.zero),
                "Regional scope must use the established bounded policy: " + error);
            RequireSharedHeightValidation(!TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, plan, cache,
                1, 7, "Transient committed Height", new[] { new Vector2Int(-1, 0) }, next,
                TerrainRegionalElevationInvalidationScope.None, out _, out _), "Invalid authoritative coordinates were accepted.");
            AddResult("Geographical authoring scope", ValidationOutcome.Pass, "Immutable old/new, unknown/global and bounded regional intent; no GPU allocation.");
        }
        catch (Exception exception) { AddResult("Geographical authoring scope", ValidationOutcome.Fail, exception.Message); }
        finally { cache?.Dispose(); if (settings != null) UnityEngine.Object.DestroyImmediate(settings); }
    }

    private static void SetGeographicFixtureField(object value, string name, object fieldValue)
    {
        var field = value.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("The transient authoring fixture field is unavailable: " + name);
        field.SetValue(value, fieldValue);
    }
    private static void DriveGeographicFixtureTile(TerrainAuthoringPreviewSharedHeightComposer worker, Vector2Int tile)
    {
        RequireSharedHeightValidation(worker.TryBeginTile(tile, out var job, out string error), error);
        for (int step = 0; step < 8 && !job.IsTerminal; step++)
        {
            RequireSharedHeightValidation(worker.TryStep(tile, out _, out error), error);
            if (job.Destination != null) { var boundary = AsyncGPUReadback.Request(job.Destination, 0); boundary.WaitForCompletion(); }
        }
        RequireSharedHeightValidation(job.Result.Succeeded, job.Error);
    }
    private static RenderTexture CreateGeographicFixtureArray(int samples, int slices)
    {
        var texture = new RenderTexture(samples, samples, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        { hideFlags = HideFlags.HideAndDontSave, dimension = TextureDimension.Tex2DArray, volumeDepth = slices,
          antiAliasing = 1, enableRandomWrite = true, useMipMap = false, autoGenerateMips = false,
          filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!texture.Create()) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidOperationException("Transient reference array allocation failed."); }
        return texture;
    }
    private static void CompareGeographicFixtureReference(GeographicHeightFixtureSource source,
        TerrainHeightCompositor compositor, TerrainAuthoringPreviewHeightMaterializer materializer,
        Vector2Int tile, int stride, RenderTexture actual, int actualSlice, float minimum, float maximum)
    {
        RenderTexture reference = null; TerrainAuthoringPreviewHeightSourceLease lease = null;
        try
        {
            int samples = TerrainHeightResolutionUtility.GetSamplesPerSide(source.Settings, stride);
            reference = CreateGeographicFixtureArray(samples, 1);
            RequireSharedHeightValidation(source.TryAcquire(tile, stride, out lease, out string error), error);
            RequireSharedHeightValidation(materializer.TryMaterialize(lease, reference, 0,
                source.Settings.HeightTileSamplesPerSide, samples, stride, out error), error);
            RequireSharedHeightValidation(source.TryGetRange(tile, out float baseLow, out float baseHigh, out error), error);
            RequireSharedHeightValidation(compositor.TryComposeTile(reference, tile, 0, samples,
                TerrainHeightResolutionUtility.GetSampleSpacing(source.Settings, stride), source.Settings.HeightTileWorldSize,
                TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(source.Settings), source.Data, baseLow, baseHigh,
                out float low, out float high, out error), error);
            materializer.ReleaseTextureBindings(); compositor.ReleaseTextureBindings();
            var expected = AsyncGPUReadback.Request(reference, 0); expected.WaitForCompletion();
            var observed = AsyncGPUReadback.Request(actual, 0); observed.WaitForCompletion();
            RequireSharedHeightValidation(!expected.hasError && !observed.hasError, "Explicit authoring comparison readback failed.");
            var a = expected.GetData<float>(0); var b = observed.GetData<float>(actualSlice);
            RequireSharedHeightValidation(a.Length == samples * samples && b.Length == a.Length
                && Approximately(low, minimum) && Approximately(high, maximum), "Equivalent-stride ranges or dimensions differ.");
            for (int i = 0; i < a.Length; i++) RequireSharedHeightValidation(Approximately(a[i], b[i]), "Equivalent-stride authoring pixel mismatch at " + i + ".");
        }
        finally
        {
            materializer.ReleaseTextureBindings(); compositor.ReleaseTextureBindings();
            // Even an unsuccessful dispatch can have queued commands referencing inputs.
            if (reference != null && reference.IsCreated()) { var boundary = AsyncGPUReadback.Request(reference, 0); boundary.WaitForCompletion(); }
            lease?.Dispose(); if (reference != null) { reference.Release(); UnityEngine.Object.DestroyImmediate(reference); }
        }
    }
    private static void RunSharedGeographicAuthoringValidation()
    {
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out string featureError)
            || !TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(17, 9, out featureError)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat)
            || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
        { AddResult("Shared geographical authoring and native adapter", ValidationOutcome.Blocked,
            string.IsNullOrEmpty(featureError) ? "This fixture requires RFloat source textures and native array slice copies." : featureError); return; }
        WorldSettings settings = null; TerrainAuthoringData data = null; TerrainHeightStampAsset stampAsset = null;
        Texture2D stampTexture = null; TerrainAuthoringPreviewSharedHeightCache cache = null;
        TerrainAuthoringPreviewSharedHeightComposer worker = null, newerWorker = null;
        TerrainAuthoringPreviewNativeAnalysisAdapter adapter = null;
        TerrainAuthoringPreviewHeightPageMap map = null;
        var compositor = new TerrainHeightCompositor(); var referenceCompositor = new TerrainHeightCompositor();
        var materializer = new TerrainAuthoringPreviewHeightMaterializer();
        try
        {
            if (!compositor.TryPrepare(out string error) || !referenceCompositor.TryPrepare(out error) || !materializer.TryPrepare(out error))
            { AddResult("Shared geographical authoring and native adapter", ValidationOutcome.Blocked, error); return; }
            settings = CreateGeographicAuthoringFixtureWorld();
            data = ScriptableObject.CreateInstance<TerrainAuthoringData>(); data.hideFlags = HideFlags.HideAndDontSave;
            stampAsset = ScriptableObject.CreateInstance<TerrainHeightStampAsset>(); stampAsset.hideFlags = HideFlags.HideAndDontSave;
            stampTexture = new Texture2D(4, 4, TextureFormat.RFloat, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var stampValues = new float[16]; for (int i = 0; i < stampValues.Length; i++) stampValues[i] = i / 15f;
            stampTexture.SetPixelData(stampValues, 0); stampTexture.Apply(false, false);
            SetGeographicFixtureField(stampAsset, "heightTexture", stampTexture);
            var stamp = new TerrainStampModifier();
            SetGeographicFixtureField(stamp, "stampAsset", stampAsset);
            SetGeographicFixtureField(stamp, "positionXZ", new Vector2(750, 750));
            SetGeographicFixtureField(stamp, "sizeXZ", new Vector2(2000, 2000));
            SetGeographicFixtureField(stamp, "falloff", 0f);
            SetGeographicFixtureField(stamp, "targetBaseHeight", 20f);
            SetGeographicFixtureField(stamp, "targetHeightRange", 15f);
            var blendField = typeof(TerrainHeightModifier).GetField("blendMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (blendField == null) { AddResult("Shared geographical authoring and native adapter", ValidationOutcome.Blocked, "Transient nonlinear stamp fixture data is unavailable."); return; }
            blendField.SetValue(stamp, Enum.Parse(blendField.FieldType, "Max"));
            var modifierField = typeof(TerrainAuthoringData).GetField("heightModifiers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (modifierField == null) { AddResult("Shared geographical authoring and native adapter", ValidationOutcome.Blocked, "Transient modifier list fixture data is unavailable."); return; }
            ((System.Collections.IList)modifierField.GetValue(data)).Add(stamp);
            var policy = new TerrainAuthoringPreviewQualitySnapshot(1, 4, gpuBudgetMiB: 1);
            var demand = CreateGeographicAuthoringFixtureDemand(settings, policy);
            var source = new GeographicHeightFixtureSource(settings, data, demand, 1);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, policy, demand,
                1, source.CommittedSignature, 1, out cache, out error), error);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightComposer.TryCreate(cache, source,
                compositor, materializer, out worker, out error), error);
            RequireSharedHeightValidation(worker.TryBeginTile(Vector2Int.zero, out var first, out error)
                && worker.TryBeginTile(Vector2Int.zero, out var duplicate, out error) && ReferenceEquals(first, duplicate),
                "Overlapping display requests were not coalesced: " + error);
            DriveGeographicFixtureTile(worker, Vector2Int.zero); DriveGeographicFixtureTile(worker, new Vector2Int(1, 0));
            RequireSharedHeightValidation(worker.DisplayCompositionCount == 2
                && !worker.TryBeginTile(new Vector2Int(2, 0), out _, out _), "Native-only work fabricated a display page.");
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error);
            foreach (var row in map.Entries)
            {
                RequireSharedHeightValidation(row.IsCurrent && map.TryGetPoolTexture(row.PoolIndex, out _, out error), error);
                RequireSharedHeightValidation(map.TryGetPoolTexture(row.PoolIndex, out var pool, out error), error);
                CompareGeographicFixtureReference(source, referenceCompositor, materializer, row.Tile,
                    row.Page.Handle.Stride, pool, row.Slice, row.Page.MinimumHeight, row.Page.MaximumHeight);
            }
            RequireSharedHeightValidation(TerrainAuthoringPreviewNativeAnalysisAdapter.TryCreate(cache, compositor, materializer,
                100000, out adapter, out error), error);
            var output = new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(2, 2));
            var physical = new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(3, 3));
            RequireSharedHeightValidation(adapter.TryBeginBuild(source, output, physical, out error)
                && !adapter.TryGetCurrentSource(out _, out _, out _) && !adapter.TryPublishCandidate(out _, out _),
                "Partly filled native analysis was published: " + error);
            DriveGeographicFixtureAdapter(adapter);
            RequireSharedHeightValidation(adapter.TryPublishCandidate(out var analysis, out error) && analysis.IsValid
                && analysis.SamplesPerSide == settings.HeightTileSamplesPerSide && analysis.SourceWindow == physical
                && analysis.ResidencyGeneration == 1 && analysis.CompositeGeneration == 1
                && adapter.BorrowedNativeCount == 1 && adapter.NativeCompositionCount == 8,
                "Native page copy/coarse fallback/ordinary source contract failed: " + error);
            for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
            {
                var tile = new Vector2Int(x, z);
                RequireSharedHeightValidation(source.TryGetRange(tile, out float baseLow, out float baseHigh, out error), error);
                // Read range from the same equivalent native composition below; bounds for
                // native analysis are not exposed as a fabricated display page descriptor.
                var reference = CreateGeographicFixtureArray(settings.HeightTileSamplesPerSide, 1);
                TerrainAuthoringPreviewHeightSourceLease lease = null;
                try
                {
                    RequireSharedHeightValidation(source.TryAcquire(tile, 1, out lease, out error), error);
                    RequireSharedHeightValidation(materializer.TryMaterialize(lease, reference, 0, settings.HeightTileSamplesPerSide,
                        settings.HeightTileSamplesPerSide, 1, out error), error);
                    RequireSharedHeightValidation(referenceCompositor.TryComposeTile(reference, tile, 0, settings.HeightTileSamplesPerSide,
                        TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1), settings.HeightTileWorldSize,
                        TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings), data, baseLow, baseHigh,
                        out float low, out float high, out error), error);
                    var boundary = AsyncGPUReadback.Request(reference, 0); boundary.WaitForCompletion();
                    CompareGeographicFixtureReference(source, referenceCompositor, materializer, tile, 1,
                        analysis.HeightCache, x + z * 3, low, high);
                }
                finally
                {
                    var boundary = AsyncGPUReadback.Request(reference, 0); boundary.WaitForCompletion();
                    lease?.Dispose(); reference.Release(); UnityEngine.Object.DestroyImmediate(reference);
                }
            }
            RequireSharedHeightValidation(cache.AnalysisAllocatedBytes == 17L * 17 * 9 * sizeof(float)
                && cache.PeakCombinedAllocatedBytes >= cache.AllocatedBytes + cache.AnalysisAllocatedBytes,
                "Native adapter active allocation was not charged separately alongside shared pools.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, demand, cache,
                1, 7, source.CommittedSignature, new[] { new Vector2Int(1, 0) }, new[] { new Vector2Int(1, 0) },
                TerrainRegionalElevationInvalidationScope.None, out var transition, out error), error);
            RequireSharedHeightValidation(cache.TryAcceptAuthoringProjection(transition, out var proof, out error), error);
            long epoch = cache.MappingEpoch;
            RequireSharedHeightValidation(map.TryGetEntry(Vector2Int.zero, out var oldEntry)
                && cache.TryAcknowledgeUnchangedPage(Vector2Int.zero, proof, out error)
                && !cache.TryAcknowledgeUnchangedPage(new Vector2Int(1, 0), proof, out _)
                && cache.TryGetPublishedPage(Vector2Int.zero, out var acknowledged, out bool current) && current
                && acknowledged.Handle.Equals(oldEntry.Page.Handle) && acknowledged.MinimumHeight == oldEntry.Page.MinimumHeight
                && acknowledged.MaximumHeight == oldEntry.Page.MaximumHeight && cache.MappingEpoch == epoch + 1
                && oldEntry.Page.AuthoringGeneration == 1 && !map.TargetsAreCurrent,
                "Untouched metadata acknowledgement mutated pixels/identity/range or an old map: " + error);
            source.Current = false;
            RequireSharedHeightValidation(adapter.HasLastGood && !adapter.TryGetCurrentSource(out _, out _, out _),
                "Stale native analysis remained current.");
            var newer = new GeographicHeightFixtureSource(settings, data, demand, 7);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightComposer.TryCreate(cache, newer, compositor, materializer,
                out newerWorker, out error), error);
            RequireSharedHeightValidation(newerWorker.TryBeginTile(new Vector2Int(1, 0), out _, out error), error);
            RequireSharedHeightValidation(newerWorker.TryStep(new Vector2Int(1, 0), out _, out error), error);
            newer.Current = false;
            RequireSharedHeightValidation(newerWorker.TryStep(new Vector2Int(1, 0), out var cancelled, out error)
                && cancelled.State == TerrainAuthoringPreviewSharedCompositionState.Cancelled
                && cache.TryGetPublishedPage(new Vector2Int(1, 0), out var lastGood, out current) && !current
                && lastGood.AuthoringGeneration == 1, "Superseded candidate lost last-good display: " + error);
            newer.Current = true; newer.MissingNative = true;
            RequireSharedHeightValidation(adapter.TryBeginBuild(newer, output, physical, out error), error);
            RequireSharedHeightValidation(adapter.TryProgress(out _, out error), error); // acknowledged native page can be copied
            var read = AsyncGPUReadback.Request(analysis.HeightCache, 0); read.WaitForCompletion();
            RequireSharedHeightValidation(adapter.TryProgress(out _, out error), error);
            RequireSharedHeightValidation(!adapter.TryProgress(out _, out _) && adapter.HasLastGood
                && !adapter.TryGetCurrentSource(out _, out _, out _), "Missing exact native dependency was published as current.");
            AddResult("Shared geographical authoring and native adapter", ValidationOutcome.Pass,
                "Real materializer/compositor with transient nonlinear Max stamp at native and stride 4; equivalent-stride pixels/ranges, guarded row-major native copy/fallback, safe acknowledgement and superseded/missing-source last-good handling.");
        }
        catch (Exception exception) { AddResult("Shared geographical authoring and native adapter", ValidationOutcome.Fail, exception.Message); }
        finally
        {
            // No live consumers were attached. Explicit fixture shutdown may block.
            bool inputsReleased = true;
            adapter?.Dispose(); if (adapter != null && !adapter.WaitForRelease(out string adapterError))
            { inputsReleased = false; AddResult("Native adapter release", ValidationOutcome.Fail, adapterError); }
            worker?.Dispose(); newerWorker?.Dispose();
            if (worker != null && !worker.WaitForRelease(out string workError)) { inputsReleased = false; AddResult("Shared authoring release", ValidationOutcome.Fail, workError); }
            if (newerWorker != null && !newerWorker.WaitForRelease(out string newerError)) { inputsReleased = false; AddResult("Superseded authoring release", ValidationOutcome.Fail, newerError); }
            map?.Dispose(); cache?.Dispose();
            if (cache != null && !cache.WaitForRelease(out string cacheError)) AddResult("Shared authoring storage release", ValidationOutcome.Fail, cacheError);
            // On a failed graphics boundary, pending work retains its compositor and
            // borrowed assets. Never force-destroy their inputs to make cleanup pass.
            if (inputsReleased)
            {
                materializer.ReleaseTextureBindings(); compositor.Dispose(); referenceCompositor.Dispose();
                if (stampTexture != null) UnityEngine.Object.DestroyImmediate(stampTexture);
                if (stampAsset != null) UnityEngine.Object.DestroyImmediate(stampAsset);
                if (data != null) UnityEngine.Object.DestroyImmediate(data);
                if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
    private static void DriveGeographicFixtureAdapter(TerrainAuthoringPreviewNativeAnalysisAdapter adapter)
    {
        // A bounded isolated readback may flush the graphics queue. It is never
        // called by normal Editor progress or installed as a recurring callback.
        for (int step = 0; step < 80 && !adapter.CandidateReady; step++)
        {
            RequireSharedHeightValidation(adapter.TryProgress(out _, out string error), error);
            RequireSharedHeightValidation(adapter.WaitForBuildGpuBoundary(out error), error);
        }
        RequireSharedHeightValidation(adapter.CandidateReady, "Bounded native analysis fixture did not finish its GPU stages.");
    }


    // Isolated activation uses the same fallible renderer transaction as the live
    // service. It never installs a second scheduler or changes user terrain assets.
    private static void RunSharedPreviewActivationValidation()
    {
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out string featureError)
            || !TerrainAuthoringPreviewHeightPagePool.TryValidateDevice(17, 18, out featureError)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat))
        { AddResult("Shared preview activation", ValidationOutcome.Blocked, featureError); return; }
        WorldSettings settings = null; TerrainAuthoringData authoring = null;
        Material original = null, variant = null;
        TerrainAuthoringPreviewSharedHeightCache cache = null;
        TerrainAuthoringPreviewSharedHeightComposer worker = null, changedWorker = null;
        TerrainAuthoringPreviewNativeAnalysisAdapter adapter = null;
        TerrainHeightCompositor compositor = null, changedCompositor = null, nativeCompositor = null;
        var payloads = new List<TerrainAuthoringPreviewSharedHeightBindingData>();
        var maps = new List<TerrainAuthoringPreviewHeightPageMap>();
        var objects = new List<GameObject>(); var renderers = new List<TerrainClipmapRendererBinding>();
        try
        {
            settings = CreateGeographicAuthoringFixtureWorld(); settings.gridWidth = settings.gridHeight = 7;
            settings.chunkSize = 16; settings.clipmapLevelCount = 3; settings.clipmapCenterResolution = 8;
            settings.clipmapBaseSampleStep = 1; settings.clipmapLODOuterResolutions = new[] { 8, 8 };
            authoring = ScriptableObject.CreateInstance<TerrainAuthoringData>(); authoring.hideFlags = HideFlags.HideAndDontSave;
            original = new Material(Shader.Find("Custom/ClipmapTerrain")) { hideFlags = HideFlags.HideAndDontSave };
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreateMaterial(original, out variant, out string error), error);
            var quality = new TerrainAuthoringPreviewQualitySnapshot(3, 4, gpuBudgetMiB: 8);
            var layout = new TerrainClipmapLayout(); var position = new Vector3(56, 0, 56);
            RequireSharedHeightValidation(TerrainClipmapLayoutUtility.TryCalculateLayout(settings, position, 0, layout, out error), error);
            var focus = new TerrainAuthoringPreviewFocus(settings, position, TerrainAuthoringPreviewFocusKind.Canonical, 1);
            RequireSharedHeightValidation(TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings,
                layout, quality, focus, new[] { new TerrainAuthoringPreviewNativeWorkingDemand(
                    new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(4, 4)), TerrainAuthoringPreviewNativeWorkingReason.Analysis) },
                1, 1, 1, out var demand, out error), error);
            var source = new GeographicHeightFixtureSource(settings, authoring, demand, 1);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, quality, demand,
                1, source.CommittedSignature, 1, out cache, out error), error);
            var selected = new Dictionary<MeshRenderer, Material>();
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("Shared activation fixture") { hideFlags = HideFlags.HideAndDontSave }; objects.Add(go);
                var renderer = go.AddComponent<MeshRenderer>(); renderer.enabled = false; renderer.sharedMaterial = original;
                TerrainClipmapRendererRole role;
                if (i == 0) role = TerrainClipmapRendererRole.CreateCenter();
                else if (i < 3) TerrainClipmapRendererRole.TryCreateRing(i, out role);
                else TerrainClipmapRendererRole.TryCreateStitch(i - 3, i - 2, out role);
                renderers.Add(new TerrainClipmapRendererBinding(renderer, role)); selected.Add(renderer, variant);
                var marker = new MaterialPropertyBlock(); marker.SetFloat(Shader.PropertyToID("_AuthoringVisualizationMode"), 73); renderer.SetPropertyBlock(marker);
            }
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out var map, out error), error); maps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var empty, out error), error); payloads.Add(empty);
            RequireSharedHeightValidation(empty.RequiredMissingCount > 0
                && !TerrainAuthoringPreviewService.TryApplySharedRendererTransaction(settings, quality, layout, demand, empty,
                    renderers, renderers, selected, () => true, () => true, () => true, () => true, out _),
                "Missing mandatory pages activated a display.");
            compositor = new TerrainHeightCompositor(); var materializer = new TerrainAuthoringPreviewHeightMaterializer();
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightComposer.TryCreate(cache, source,
                compositor, materializer, out worker, out error), error);
            foreach (var row in demand.Tiles) if (row.HasDisplay)
            { DriveGeographicFixtureTile(worker, row.Tile); worker.RetireResult(row.Tile); }
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error); maps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var complete, out error), error); payloads.Add(complete);
            RequireSharedHeightValidation(!TerrainAuthoringPreviewService.TryApplySharedRendererTransaction(settings, quality, layout, demand, complete,
                renderers, renderers, selected, () => true, () => true, () => true, () => false, out _),
                "Late ownership failure committed a candidate.");
            var block = new MaterialPropertyBlock();
            foreach (var binding in renderers)
            {
                binding.Renderer.GetPropertyBlock(block);
                RequireSharedHeightValidation(binding.Renderer.sharedMaterial == original && !binding.Renderer.enabled
                    && block.GetFloat(Shader.PropertyToID("_AuthoringVisualizationMode")) == 73
                    && block.GetTexture(TerrainAuthoringPreviewSharedHeightBindingData.MapId) == null,
                    "Failed activation did not restore material, MPB and renderer state.");
            }
            RequireSharedHeightValidation(TerrainAuthoringPreviewService.TryApplySharedRendererTransaction(settings, quality, layout, demand, complete,
                renderers, renderers, selected, () => true, () => true, () => true, () => true, out error), error);
            Vector2Int changed = default; bool picked = false; int displayCount = 0;
            foreach (var row in demand.Tiles) if (row.HasDisplay) { displayCount++; if (!picked) { changed = row.Tile; picked = true; } }
            RequireSharedHeightValidation(worker.DisplayCompositionCount == displayCount, "Display composed more than one selected representation per tile.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewGeographicAuthoringProjection.TryProject(settings, demand, cache, 1, 2,
                source.CommittedSignature, new[] { changed }, Array.Empty<Vector2Int>(), TerrainRegionalElevationInvalidationScope.None,
                out var projection, out error), error);
            RequireSharedHeightValidation(projection.DisplayJobCount == 1, "One edit projected multiple display jobs.");
            RequireSharedHeightValidation(cache.TryAcceptAuthoringProjection(projection, out var proof, out error), error);
            foreach (var row in demand.Tiles) if (row.HasDisplay && row.Tile != changed)
                RequireSharedHeightValidation(cache.TryAcknowledgeUnchangedPage(row.Tile, proof, out error), error);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error); maps.Add(map);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out var stale, out error), error); payloads.Add(stale);
            RequireSharedHeightValidation(!TerrainAuthoringPreviewService.TryApplySharedRendererTransaction(settings, quality, layout, demand, stale,
                renderers, renderers, selected, () => true, () => true, () => true, () => true, out _), "Stale required page passed activation.");
            renderers[0].Renderer.GetPropertyBlock(block);
            RequireSharedHeightValidation(block.GetTexture(TerrainAuthoringPreviewSharedHeightBindingData.MapId) == complete.Lookup,
                "Required-page failure removed the last-good binding.");
            var changedSource = new GeographicHeightFixtureSource(settings, authoring, demand, 2);
            changedCompositor = new TerrainHeightCompositor();
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightComposer.TryCreate(cache, changedSource,
                changedCompositor, new TerrainAuthoringPreviewHeightMaterializer(), out changedWorker, out error), error);
            DriveGeographicFixtureTile(changedWorker, changed);
            RequireSharedHeightValidation(changedWorker.DisplayCompositionCount == 1, "One-tile edit composed duplicate display representations.");
            nativeCompositor = new TerrainHeightCompositor();
            RequireSharedHeightValidation(TerrainAuthoringPreviewNativeAnalysisAdapter.TryCreate(cache, nativeCompositor,
                new TerrainAuthoringPreviewHeightMaterializer(), 1024 * 1024, out adapter, out error), error);
            var physical = new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(4, 4));
            var output = new TerrainHeightCacheWindow(Vector2Int.one, Vector2Int.one);
            RequireSharedHeightValidation(adapter.TryBeginBuild(changedSource, output, physical, out error), error);
            DriveGeographicFixtureAdapter(adapter);
            RequireSharedHeightValidation(adapter.TryPublishCandidate(out var native, out error) && native.IsValid
                && native.SamplesPerSide == settings.HeightTileSamplesPerSide && native.SourceWindow == physical,
                "Shared activation changed the ordinary complete exact-native source contract: " + error);
            RequireSharedHeightValidation(!original.IsKeywordEnabled(TerrainAuthoringPreviewSharedHeightBindingData.ShaderKeyword),
                "Shared activation modified the source material keyword.");
            AddResult("Shared preview activation", ValidationOutcome.Pass,
                "Required halo, one selected display composition, dirty proof, rollback/last-good, transient materials and independent whole-window native source passed.");
        }
        catch (Exception exception) { AddResult("Shared preview activation", ValidationOutcome.Fail, exception.Message); }
        finally
        {
            foreach (var binding in renderers) if (binding.Renderer != null)
            { binding.Renderer.sharedMaterial = original; binding.Renderer.SetPropertyBlock(new MaterialPropertyBlock()); }
            if (adapter != null) { adapter.Dispose(); if (!adapter.WaitForRelease(out string error)) AddResult("Activation native release", ValidationOutcome.Fail, error); }
            if (worker != null) { worker.Dispose(); if (!worker.WaitForRelease(out string error)) AddResult("Activation worker release", ValidationOutcome.Fail, error); }
            if (changedWorker != null) { changedWorker.Dispose(); if (!changedWorker.WaitForRelease(out string error)) AddResult("Activation dirty release", ValidationOutcome.Fail, error); }
            foreach (var payload in payloads) { payload.Dispose(); if (!payload.WaitForRelease(out string error)) AddResult("Activation lookup release", ValidationOutcome.Fail, error); }
            foreach (var map in maps) map.Dispose();
            if (cache != null) { cache.Dispose(); if (!cache.WaitForRelease(out string error)) AddResult("Activation page release", ValidationOutcome.Fail, error); }
            compositor?.Dispose(); changedCompositor?.Dispose(); nativeCompositor?.Dispose();
            foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
            if (variant != null) UnityEngine.Object.DestroyImmediate(variant);
            if (original != null) UnityEngine.Object.DestroyImmediate(original);
            if (authoring != null) UnityEngine.Object.DestroyImmediate(authoring);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
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
    // Explicit isolated GPU fixture. No live preview owner or persistent source is changed.
    internal static bool RunIncrementalSharedHeightFixture(out string detail, out bool blocked)
    {
        detail = ""; blocked = false;
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out detail)) { blocked = true; return false; }
        WorldSettings settings = null; TerrainAuthoringData data = null;
        TerrainAuthoringPreviewSharedHeightCache cache = null;
        TerrainAuthoringPreviewSharedHeightComposer worker = null;
        TerrainHeightCompositor compositor = null;
        TerrainAuthoringPreviewHeightPageMap retained = null;
        try
        {
            settings = CreateGeographicAuthoringFixtureWorld(); data = ScriptableObject.CreateInstance<TerrainAuthoringData>();
            data.hideFlags = HideFlags.HideAndDontSave;
            var quality = new TerrainAuthoringPreviewQualitySnapshot(1, 4, gpuBudgetMiB: 8);
            var demand = CreateGeographicAuthoringFixtureDemand(settings, quality);
            var source = new GeographicHeightFixtureSource(settings, data, demand, 1);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, quality, demand,
                1, source.CommittedSignature, 1, out cache, out string error, new Dictionary<int, int> { { 4, 4 } }), error);
            compositor = new TerrainHeightCompositor();
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightComposer.TryCreate(cache, source, compositor,
                new TerrainAuthoringPreviewHeightMaterializer(), out worker, out error, 1), error);
            RequireSharedHeightValidation(worker.TryBeginTile(Vector2Int.zero, out var job, out error, requestedStride: 4), error);
            RequireSharedHeightValidation(worker.TryStep(Vector2Int.zero, out _, out error), error);
            var revised = new TerrainAuthoringPreviewGeographicDemandPlan(settings, quality,
                new TerrainAuthoringPreviewFocus(settings, Vector3.zero, TerrainAuthoringPreviewFocusKind.Canonical, 1),
                Vector2Int.zero, demand.EditableWindow, 2, 1, 2, new List<TerrainAuthoringPreviewGeographicTileDemand>(demand.Tiles).ToArray());
            RequireSharedHeightValidation(cache.TryAcceptDemand(revised, source.CommittedSignature, 1, out error), error);
            var next = new GeographicHeightFixtureSource(settings, data, revised, 1);
            RequireSharedHeightValidation(worker.TryRetarget(next), "A compatible GPU-submitted job was restarted on a harmless demand revision.");
            DriveGeographicFixtureTile(worker, Vector2Int.zero);
            RequireSharedHeightValidation(cache.TryGetPublishedPage(Vector2Int.zero, out var coarse, out bool final)
                && !final && coarse.Handle.Stride == 4 && coarse.Handle.DemandGeneration == 1,
                "Adoption changed physical identity or declared provisional Height final.");
            RequireSharedHeightValidation(!cache.TryReservePage(Vector2Int.zero, 8, cache.NextRequestToken,
                revised.Generation, out _, out _, true), "An arbitrary coarse stride bypassed the contextual policy.");
            worker.RetireResult(Vector2Int.zero);
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out retained, out error), error);
            DriveGeographicFixtureTile(worker, Vector2Int.zero); worker.RetireResult(Vector2Int.zero);
            RequireSharedHeightValidation(cache.TryGetPublishedPage(Vector2Int.zero, out var fine, out final)
                && final && fine.Handle.Stride == 1 && !fine.Handle.Equals(coarse.Handle)
                && retained.TryGetEntry(Vector2Int.zero, out var old) && old.IsValid && old.Page.Handle.Equals(coarse.Handle),
                "Final replacement reclaimed the retained coarse reader or did not converge.");
            int releases = 0; var texture = new Texture2D(2, 2, TextureFormat.RFloat, false, true);
            try
            {
                var lease = new TerrainAuthoringPreviewHeightSourceLease(texture, 1, 2, false, release: () => releases++);
                lease.Dispose(); lease.Dispose();
                RequireSharedHeightValidation(releases == 1, "Shared source release ran twice.");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            detail = "Demand adoption preserved submitted physical identity; provisional quality converged with retained readers; arbitrary coarse admission rejected; source release is idempotent.";
            return true;
        }
        catch (Exception exception) { detail = exception.Message; return false; }
        finally
        {
            retained?.Dispose(); worker?.Dispose();
            bool released = worker == null || worker.WaitForRelease(out _);
            if (released) compositor?.Dispose();
            cache?.Dispose(); if (cache != null) cache.WaitForRelease(out _);
            if (released && data != null) UnityEngine.Object.DestroyImmediate(data);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
        }
    }
    internal static bool RunSafeSharedVisibilityFixture(out string detail, out bool blocked)
    {
        detail = ""; blocked = false;
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out detail)) { blocked = true; return false; }
        WorldSettings settings = null; TerrainAuthoringData data = null;
        TerrainAuthoringPreviewSharedHeightCache cache = null;
        TerrainAuthoringPreviewSharedHeightComposer worker = null;
        TerrainAuthoringPreviewNativeAnalysisAdapter adapter = null;
        TerrainAuthoringPreviewSharedHeightBindingData payload = null;
        TerrainAuthoringPreviewHeightPageMap map = null;
        TerrainHeightCompositor compositor = null, nativeCompositor = null;
        Material original = null, variant = null; GameObject go = null; Mesh mesh = null;
        try
        {
            settings = CreateGeographicAuthoringFixtureWorld(); settings.gridWidth = settings.gridHeight = 14;
            settings.clipmapLevelCount = 1; settings.clipmapCenterResolution = 8; settings.clipmapBaseSampleStep = 1;
            data = ScriptableObject.CreateInstance<TerrainAuthoringData>(); data.hideFlags = HideFlags.HideAndDontSave;
            var quality = new TerrainAuthoringPreviewQualitySnapshot(3, 4, gpuBudgetMiB: 8);
            var focusTile = new Vector2Int(3, 3); var editable = new TerrainHeightCacheWindow(new Vector2Int(2, 2), new Vector2Int(3, 3));
            var rows = new List<TerrainAuthoringPreviewGeographicTileDemand>();
            for (int z = 0; z < 7; z++) for (int x = 0; x < 7; x++)
            {
                var tile = new Vector2Int(x, z); bool fine = editable.Contains(tile);
                rows.Add(new TerrainAuthoringPreviewGeographicTileDemand(tile, 1, fine ? 1 : 4, fine,
                    TerrainAuthoringPreviewDisplayRequirement.Geometry, TerrainAuthoringPreviewNativeWorkingReason.Analysis));
            }
            var position = new Vector3(1792, 0, 1792); var layout = new TerrainClipmapLayout();
            RequireSharedHeightValidation(TerrainClipmapLayoutUtility.TryCalculateLayout(settings, position, 0, layout, out string error), error);
            var demand = new TerrainAuthoringPreviewGeographicDemandPlan(settings, quality,
                new TerrainAuthoringPreviewFocus(settings, position, TerrainAuthoringPreviewFocusKind.Canonical, 1), focusTile, editable, 1, 1, 1, rows.ToArray());
            var source = new GeographicHeightFixtureSource(settings, data, demand, 1);
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightCache.TryCreate(settings, quality, demand, 1,
                source.CommittedSignature, 1, out cache, out error), error);
            nativeCompositor = new TerrainHeightCompositor();
            RequireSharedHeightValidation(TerrainAuthoringPreviewNativeAnalysisAdapter.TryCreate(cache, nativeCompositor,
                new TerrainAuthoringPreviewHeightMaterializer(), cache.GpuBudgetBytes, out adapter, out error), error);
            RequireSharedHeightValidation(adapter.TryBeginBuild(source, new TerrainHeightCacheWindow(focusTile, Vector2Int.one), editable, out error), error);
            DriveGeographicFixtureAdapter(adapter);
            RequireSharedHeightValidation(adapter.TryPublishCandidate(out var native, out error) && native.IsValid
                && native.SamplesPerSide == settings.HeightTileSamplesPerSide && cache.AllocatedBytes == 0,
                "Native preparation was blocked by missing display or substituted coarse precision: " + error);
            compositor = new TerrainHeightCompositor();
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightComposer.TryCreate(cache, source, compositor,
                new TerrainAuthoringPreviewHeightMaterializer(), out worker, out error, 1), error);
            for (int z = 1; z <= 5; z++) for (int x = 1; x <= 5; x++)
            {
                var tile = new Vector2Int(x, z);
                RequireSharedHeightValidation(worker.TryBeginTile(tile, out _, out error, requestedStride: 4), error);
                DriveGeographicFixtureTile(worker, tile); worker.RetireResult(tile);
            }
            go = new GameObject("Safe shared visibility fixture") { hideFlags = HideFlags.HideAndDontSave };
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, vertices = new[] { Vector3.zero, new Vector3(32, 0, 0), new Vector3(0, 0, 32) }, triangles = new[] { 0, 1, 2 } };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.transform.position = position;
            original = new Material(Shader.Find("Custom/ClipmapTerrain")) { hideFlags = HideFlags.HideAndDontSave };
            renderer.sharedMaterial = original;
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreateMaterial(original, out variant, out error), error);
            var bindings = new[] { new TerrainClipmapRendererBinding(renderer, TerrainClipmapRendererRole.CreateCenter()) };
            RequireSharedHeightValidation(cache.TryCreateRenderMapSnapshot(out map, out error), error);
            RequireSharedHeightValidation(TerrainAuthoringPreviewHeightBindingUtility.TryBuildSafeVisibility(settings, layout, demand,
                map, bindings, out var visible, out int margin, out error) && margin == 2 && visible.SetEquals(new[] { focusTile }),
                "Triangle/halo erosion did not isolate the safe region: " + error);
            RequireSharedHeightValidation(!TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out _, out _,
                new[] { new Vector2Int(1, 1) }, margin), "Unsafe visibility cell was accepted.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewSharedHeightBindingData.TryCreate(settings, map, out payload, out error, visible, margin), error);
            RequireSharedHeightValidation(payload.IsPartial && payload.VisibleCount == 1 && payload.RequiredMissingCount > 0
                && map.TryGetEntry(new Vector2Int(2, 2), out var halo) && halo.IsValid && !payload.IsVisible(halo.Tile),
                "Partial visibility discarded valid physical halo or claimed full coverage.");
            RequireSharedHeightValidation(TerrainAuthoringPreviewService.TryApplySharedRendererTransaction(settings, quality, layout, demand,
                payload, bindings, Array.Empty<TerrainClipmapRendererBinding>(), new Dictionary<MeshRenderer, Material> { { renderer, variant } },
                () => true, () => true, () => true, () => true, out error), error);
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            var mask = block.GetTexture(TerrainAuthoringPreviewSharedHeightBindingData.VisibilityId) as Texture2D;
            RequireSharedHeightValidation(mask != null && mask.width == payload.Window.Width && mask.height == payload.Window.Height
                && block.GetFloat(TerrainAuthoringPreviewSharedHeightBindingData.PartialId) == 1, "Mask and lookup addressing differ.");
            var read = AsyncGPUReadback.Request(mask, 0); read.WaitForCompletion();
            var bytes = read.GetData<byte>(); int channels = mask.format == TextureFormat.R8 ? 1 : 4;
            RequireSharedHeightValidation(!read.hasError && bytes[24 * channels] == 255 && bytes[16 * channels] == 0,
                "Safe mask upload differs from its proof.");
            detail = "Independent exact native source completed before any display page; safe partial transaction retained physical halo, rejected an unsafe cell, uploaded the bounded visibility mask and kept final quality pending.";
            return true;
        }
        catch (Exception exception) { detail = exception.Message; return false; }
        finally
        {
            if (go != null) TerrainAuthoringPreviewHeightBindingUtility.DisableShared(new[] { new TerrainClipmapRendererBinding(go.GetComponent<MeshRenderer>(), TerrainClipmapRendererRole.CreateCenter()) });
            payload?.Dispose(); if (payload != null) payload.WaitForRelease(out _); map?.Dispose();
            worker?.Dispose(); adapter?.Dispose();
            bool displayReleased = worker == null || worker.WaitForRelease(out _), nativeReleased = adapter == null || adapter.WaitForRelease(out _);
            if (displayReleased) compositor?.Dispose(); if (nativeReleased) nativeCompositor?.Dispose();
            cache?.Dispose(); if (cache != null) cache.WaitForRelease(out _);
            if (go != null) UnityEngine.Object.DestroyImmediate(go); if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (variant != null) UnityEngine.Object.DestroyImmediate(variant); if (original != null) UnityEngine.Object.DestroyImmediate(original);
            if (displayReleased && nativeReleased && data != null) UnityEngine.Object.DestroyImmediate(data);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
        }
    }

}




