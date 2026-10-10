using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Deterministic validation for bounded interactive/offline Terrain Analysis.
 * Tests here deliberately avoid moving Scene View residency or
 * mutating authoring state; live source information is reported separately.
 */
public static class TerrainAuthoringAnalysisDecouplingValidationUtility
{
    private enum ValidationOutcome
    {
        Pass,
        Fail,
        Blocked
    }

    private sealed class ValidationResult
    {
        public string Name;
        public ValidationOutcome Outcome;
        public string Details;
    }

    private static readonly List<ValidationResult> results =
        new List<ValidationResult>();

    private static TerrainValidationRunSummary lastRunSummary =
        TerrainValidationRunSummary.CreateNotRun();

    public static TerrainValidationRunSummary LastRunSummary =>
        lastRunSummary;

    [MenuItem(
        "Tools/WorldMeshes/Validation/Analysis Window Safety"
    )]
    public static void ValidateAnalysisDecoupling()
    {
        lastRunSummary =
            TerrainValidationRunSummary.CreateRunning(
                "Analysis window safety validation is running."
            );

        results.Clear();

        try
        {
            ValidateNonZeroOriginSafeWindow();
            ValidateArtificialBoundaryInset();
            ValidateWorldMinimumBoundaryPreservation();
            ValidateWorldMaximumBoundaryPreservation();
            ValidateFourTileDependencyGuard();
            ValidateCurrentScaleOneTileGuard();
            ValidateBoundedExpansion();
            ValidateSparseBatchPlanning();
            ValidateContiguousBatchMaximum();
            ValidateNativeFocusWindows();
            ValidateAnalysisWorkPolicy();
            ValidateOwnedNativeHeight();
            ValidateNativeWindowLocality();
            ValidateLiveAnalysisSource();
        }
        catch (Exception exception)
        {
            Add(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }

        WriteReport();
    }

    private static void ValidateNonZeroOriginSafeWindow()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(10, 12),
                new Vector2Int(7, 7)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(64, 64),
                1,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == new Vector2Int(11, 13)
            &&
            output.Size == new Vector2Int(5, 5);

        Add(
            "Non-zero resident origin maps to a safe local output window",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success
                ? "Source=" + source + ", Output=" + output
                : error
        );
    }

    private static void ValidateArtificialBoundaryInset()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(20, 20),
                new Vector2Int(9, 9)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(128, 128),
                2,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == new Vector2Int(22, 22)
            &&
            output.Size == new Vector2Int(5, 5);

        Add(
            "Artificial resident boundaries are excluded from analysis output",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success ? output.ToString() : error
        );
    }

    private static void ValidateWorldMinimumBoundaryPreservation()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                Vector2Int.zero,
                new Vector2Int(7, 7)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(64, 64),
                1,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == Vector2Int.zero
            &&
            output.Size == new Vector2Int(6, 6);

        Add(
            "Logical minimum world edges are preserved",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success ? output.ToString() : error
        );
    }

    private static void ValidateWorldMaximumBoundaryPreservation()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(57, 57),
                new Vector2Int(7, 7)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(64, 64),
                1,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == new Vector2Int(58, 58)
            &&
            output.Size == new Vector2Int(6, 6)
            &&
            output.MaximumExclusive == new Vector2Int(64, 64);

        Add(
            "Logical maximum world edges are preserved",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success ? output.ToString() : error
        );
    }

    private static void ValidateFourTileDependencyGuard()
    {
        int guard =
            TerrainAnalysisWindowUtility.CalculateRequiredGuardTileCount(
                65,
                1f,
                256f
            );

        Add(
            "256 m dependency on 64 m height tiles requires four guard tiles",
            guard == 4
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Guard tiles=" + guard
        );
    }

    private static void ValidateCurrentScaleOneTileGuard()
    {
        int guard =
            TerrainAnalysisWindowUtility.CalculateRequiredGuardTileCount(
                1025,
                1f,
                256f
            );

        Add(
            "256 m dependency on 1024 m height tiles remains one guard tile",
            guard == 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Guard tiles=" + guard
        );
    }

    private static void ValidateBoundedExpansion()
    {
        TerrainHeightCacheWindow output =
            new TerrainHeightCacheWindow(
                new Vector2Int(100, 50),
                new Vector2Int(8, 1)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryExpandOutputWindow(
                output,
                new Vector2Int(256, 256),
                2,
                out TerrainHeightCacheWindow source,
                out string error
            );

        bool bounded =
            success
            &&
            source.Size == new Vector2Int(12, 5)
            &&
            source.TileCount == 60;

        Add(
            "Offline source expansion scales with batch plus guard",
            bounded ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success
                ? "Output=" + output + ", Source=" + source
                : error
        );
    }

    private static void ValidateSparseBatchPlanning()
    {
        List<Vector2Int> sparse =
            new List<Vector2Int>
            {
                new Vector2Int(2, 2),
                new Vector2Int(100, 50)
            };

        List<Vector2Int> firstRun =
            TerrainAnalysisRuntimeHeightBatchService.CollectContiguousRun(
                sparse,
                0,
                8
            );

        Add(
            "Sparse surface tiles are split into independent bounded batches",
            firstRun.Count == 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "First run tile count=" + firstRun.Count
        );
    }

    private static void ValidateContiguousBatchMaximum()
    {
        List<Vector2Int> row =
            new List<Vector2Int>();

        for (int x = 0; x < 20; x++)
        {
            row.Add(new Vector2Int(x, 3));
        }

        List<Vector2Int> run =
            TerrainAnalysisRuntimeHeightBatchService.CollectContiguousRun(
                row,
                0,
                20
            );

        Add(
            "Whole-world output batches remain capped",
            run.Count ==
                TerrainAnalysisRuntimeHeightBatchService.MaximumOutputTilesPerBatch
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Planned tile count=" + run.Count
        );
    }

    private static void ValidateNativeFocusWindows()
    {
        var grid = new Vector2Int(64, 64);
        bool centred = TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(grid, 65, 1f,
            new Vector3(20 * 64 + 3, 0, 12 * 64 + 8), out var output, out var source, out int guard, out string error);
        bool same = TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(grid, 65, 1f,
            new Vector3(20 * 64 + 60, 0, 12 * 64 + 60), out var repeated, out var repeatedSource, out _, out _);
        bool large = TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(new Vector2Int(1024, 1024),
            65, 1f, new Vector3(20 * 64 + 3, 0, 12 * 64 + 8), out var largeOutput, out var largeSource, out _, out _);
        Add("Native focus stays bounded independently of outer world/display coverage",
            centred && same && large && guard == 4 && output.OriginTile == new Vector2Int(19, 11)
                && output.Size == new Vector2Int(3, 3) && source.Size == new Vector2Int(11, 11)
                && output == repeated && source == repeatedSource && output == largeOutput && source == largeSource
                ? ValidationOutcome.Pass : ValidationOutcome.Fail, error);
        bool corner = TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(grid, 1025, 1f,
            new Vector3(64 * 1024, 0, 64 * 1024), out var edgeOutput, out var edgeSource, out int edgeGuard, out error);
        bool single = TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(Vector2Int.one, 65, 1f,
            Vector3.zero, out var oneOutput, out var oneSource, out _, out _);
        bool safe = TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(edgeSource, grid, edgeGuard,
            out var safeOutput, out _);
        bool invalid = !TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(grid, 65, 1f,
            new Vector3(float.NaN, 0, 0), out _, out _, out _, out _);
        Add("Native world-edge output is clipped without artificial inward padding",
            corner && edgeGuard == 1 && edgeOutput.OriginTile == new Vector2Int(62, 62)
                && edgeOutput.Size == new Vector2Int(2, 2) && edgeSource.Size == new Vector2Int(3, 3)
                && safe && safeOutput.Contains(edgeOutput) && single && oneOutput.TileCount == 1
                && oneSource == oneOutput && invalid ? ValidationOutcome.Pass : ValidationOutcome.Fail, error);
    }

    private static void ValidateAnalysisWorkPolicy()
    {
        var display = TerrainAuthoringPreviewCachePublication.DisplayHeightSet;
        var analysis = TerrainAuthoringPreviewCachePublication.NativeAnalysis;
        
        bool correct = !TerrainAuthoringPreviewStreamingPolicy.ShouldDeferForNativeAnalysis(display, true, true, analysis)
            && TerrainAuthoringPreviewStreamingPolicy.ShouldDeferForNativeAnalysis(display, false, true, analysis)
            && TerrainAuthoringPreviewStreamingPolicy.ShouldDeferForNativeAnalysis(analysis, false, true, display)
            && !TerrainAuthoringPreviewStreamingPolicy.ShouldDeferForNativeAnalysis(analysis, false, true, null)
            && !TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(analysis, true, true)
            && TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(analysis, true, false)
            && TerrainAuthoringPreviewStreamingPolicy.ShouldDeferHeightRequestRestart(display, true, true);
        correct &= !TerrainAuthoringPreviewService.RequiresOwnedNativeAnalysisPreparation(true, true)
            && TerrainAuthoringPreviewService.RequiresOwnedNativeAnalysisPreparation(true, false)
            && !TerrainAuthoringPreviewService.RequiresOwnedNativeAnalysisPreparation(false, false);
        Add("Native analysis uses shared priority and interactive restart policy",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            "Mandatory display recovery wins; current analysis cannot be repeatedly cancelled by optional work.");
    }

    private static void ValidateOwnedNativeHeight()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldMeshesPaths.WorldSettingsAssetPath);
        var data = AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(WorldMeshesPaths.TerrainAuthoringDataAssetPath);
        if (settings == null || data == null || !SystemInfo.supportsComputeShaders
            || !SystemInfo.supports2DArrayTextures || !SystemInfo.supportsAsyncGPUReadback
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat))
        {
            Add("Owned native analysis Height production", ValidationOutcome.Blocked,
                "Committed Height plus compute/RFloat/array/readback support are required.");
            return;
        }
        if (!TerrainAuthoringStateUtility.TryValidateCommittedHeightfield(settings, data,
            TerrainAuthoringHeightfieldValidationMode.Operational, out _, out _, out string error)
            || !TerrainAnalysisWindowUtility.TryCalculateNativeInteractiveWindows(settings, Vector3.zero,
                out var output, out var required, out _, out error))
        {
            Add("Owned native analysis Height production", ValidationOutcome.Blocked, error);
            return;
        }
        int samples = TerrainHeightResolutionUtility.GetSamplesPerSide(settings, 1);
        long bytes = (long)samples * samples * 4 * (2L * required.TileCount + 2L);
        if (required.TileCount > 64 || bytes > 192L * 1024 * 1024)
        {
            Add("Owned native analysis Height production", ValidationOutcome.Blocked,
                "The explicit GPU fixture is capped at 64 source tiles and 192 MiB; use a smaller committed test world.");
            return;
        }
        TerrainAuthoringPreviewCacheSetTransition transaction = null;
        TerrainAuthoringPreviewCacheSetTransition stale = null;
        TerrainAuthoringPreviewLodState[] owned = null;
        var reference = new TerrainAuthoringPreviewCache();
        var compositor = new TerrainHeightCompositor();
        try
        {
            string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
            string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
            var plan = new TerrainAuthoringPreviewResidencyPlan
            {
                Generation = 1, LevelCount = 1,
                Levels = new[] { new TerrainAuthoringPreviewLodResidencyPlan
                {
                    Level = 0, SampleStride = 1, SamplesPerSide = samples,
                    SampleSpacing = TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1),
                    RequiredWindow = required, DesiredWindow = required
                }}
            };
            transaction = new TerrainAuthoringPreviewCacheSetTransition(plan, new[] { required }, new[] { false },
                new TerrainAuthoringPreviewCache[1], new long[1], TerrainAuthoringPreviewCachePublication.NativeAnalysis,
                committed, overall, 17, 23, 5, false, settings.HeightTileWorldSize,
                TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings));
            bool partialRejected = false;
            try { transaction.TransferPreparedStates(); }
            catch (InvalidOperationException) { partialRejected = true; }
            RequireNativeFixture(partialRejected, "Partial native analysis Height transferred ownership.");
            int calls = 0;
            while (transaction.State != TerrainAuthoringPreviewTransitionState.ReadyToActivate && calls < 512)
            {
                RequireNativeFixture(TerrainAuthoringPreviewService.AdvanceHeightCacheSet(transaction, settings, data,
                    compositor, System.Diagnostics.Stopwatch.StartNew(), 4.0,
                    TerrainAuthoringPreviewService.DefaultMaterializationsPerUpdate,
                    TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile, out error), error);
                RequireNativeFixture(transaction.LastUpdateAllocations <= 1 && transaction.LastUpdateLoads <= 1
                    && transaction.LastUpdateCompositions <= 1 && transaction.LastUpdateCopies <= 8
                    && transaction.LastUpdateMaterializations <= 8, "Shared per-update work caps were exceeded.");
                calls++;
            }
            RequireNativeFixture(transaction.Complete, "Bounded native analysis Height did not finish.");
            RequireNativeFixture(TerrainAuthoringPreviewService.IsTerrainAnalysisTransactionCurrent(transaction, plan, 23),
                "A current native analysis destination was rejected.");
            var changed = plan.CreateSnapshot();
            changed.Generation++;
            RequireNativeFixture(!TerrainAuthoringPreviewService.IsTerrainAnalysisTransactionCurrent(transaction, changed, 23)
                && !transaction.MatchesContent(committed, overall, 18, 5, false)
                && !transaction.MatchesContent(committed, overall, 17, 6, false),
                "Superseded output, authoring, or ownership remained eligible.");
            owned = transaction.TransferPreparedStates();
            transaction.Dispose();
            var cache = owned[0].ActiveCache;
            RequireNativeFixture(TerrainAuthoringPreviewService.IsNativeAnalysisCacheEligible(cache, settings,
                17, 17, committed, overall, required, output), "A complete native analysis source was rejected.");
            RequireNativeFixture(!TerrainAuthoringPreviewService.IsNativeAnalysisCacheEligible(cache, settings,
                16, 17, committed, overall, required, output)
                && !TerrainAuthoringPreviewService.IsNativeAnalysisCacheEligible(cache, settings,
                    17, 17, committed, overall + " changed", required, output), "Stale content was accepted.");
            var physical = new TerrainHeightCacheWindow(cache.CacheOriginTile, cache.CacheSize);
            var descriptor = new TerrainAnalysisGpuSource(cache.HeightCache, physical,
                new Vector2Int(settings.HeightTileGridWidth, settings.HeightTileGridHeight), samples,
                cache.SampleSpacing, cache.WorldSizeXZ, overall, cache.HeightCache.GetInstanceID(), 3, 4);
            RequireNativeFixture(descriptor.IsValid && descriptor.SourceSliceCount == required.TileCount
                && physical.Contains(output) && output.TileCount <= 9, "Borrowed physical slice mapping changed.");

            // Independent native copy/composition of one committed tile checks
            // worker values without changing any live preview/authoring state.
            var tile = output.OriginTile;
            RequireNativeFixture(reference.TryBuild(settings, data,
                new TerrainHeightCacheWindow(tile, Vector2Int.one), out error), error);
            RequireNativeFixture(reference.TryGetCommittedRange(tile, out float low, out float high, out error), error);
            RequireNativeFixture(compositor.TryComposeTile(reference.HeightCache, tile, 0, samples, cache.SampleSpacing,
                settings.HeightTileWorldSize, cache.WorldSizeXZ, data, low, high,
                out float finalLow, out float finalHigh, out error), error);
            RequireNativeFixture(cache.TryGetCompositeSliceRange(tile.x, tile.y, out float actualLow, out float actualHigh)
                && Mathf.Approximately(finalLow, actualLow) && Mathf.Approximately(finalHigh, actualHigh),
                "Native analysis Height ranges differ from committed plus authoring production.");
            var expected = AsyncGPUReadback.Request(reference.HeightCache, 0, 0, samples, 0, samples, 0, 1, TextureFormat.RFloat, null);
            var actual = AsyncGPUReadback.Request(cache.HeightCache, 0, 0, samples, 0, samples,
                cache.GetSliceIndex(tile.x, tile.y), 1, TextureFormat.RFloat, null);
            expected.WaitForCompletion();
            actual.WaitForCompletion();
            RequireNativeFixture(!expected.hasError && !actual.hasError, "Native analysis Height readback failed.");
            var expectedValues = expected.GetData<float>();
            var actualValues = actual.GetData<float>();
            RequireNativeFixture(expectedValues.Length == samples * samples && actualValues.Length == expectedValues.Length,
                "Native analysis sample count changed.");
            int[] probes = { 0, samples - 1, (samples * samples) / 2, samples * samples - samples, samples * samples - 1 };
            foreach (int index in probes)
                RequireNativeFixture(!float.IsNaN(actualValues[index]) && !float.IsInfinity(actualValues[index])
                    && Mathf.Approximately(actualValues[index], expectedValues[index]),
                    "Native analysis differs from current authoring at sample " + index + ".");

            // Allocated stale candidates must dispose their texture and leave
            // the already-transferred source alive.
            stale = new TerrainAuthoringPreviewCacheSetTransition(plan, new[] { required }, new[] { false },
                new TerrainAuthoringPreviewCache[1], new long[1], TerrainAuthoringPreviewCachePublication.NativeAnalysis,
                committed, overall, 17, 24, 5, false, settings.HeightTileWorldSize);
            RequireNativeFixture(TerrainAuthoringPreviewService.AdvanceHeightCacheSet(stale, settings, data, compositor,
                System.Diagnostics.Stopwatch.StartNew(), 0.0, 1,
                TerrainAuthoringPreviewHeightSourceUtility.TryLoadCommittedNativeTile, out error), error);
            var candidate = stale.Entries[0].Destination.StagingCache.HeightCache;
            RequireNativeFixture(!stale.MatchesContent(committed, overall, 18, 5, false), "Stale authoring passed publication.");
            stale.Dispose();
            RequireNativeFixture((candidate == null || !candidate.IsCreated()) && cache.HeightCache.IsCreated(),
                "Candidate disposal damaged the transferred source or leaked a texture.");
            Add("Owned native analysis Height production", ValidationOutcome.Pass,
                required.TileCount + " native source tiles; " + calls + " capped worker updates; current/stale identity, "
                + "atomic ownership, native values/ranges and candidate release checked.");
        }
        catch (Exception exception)
        {
            Add("Owned native analysis Height production", ValidationOutcome.Fail, exception.Message);
        }
        finally
        {
            transaction?.Dispose();
            stale?.Dispose();
            if (owned != null) foreach (var state in owned) state.Dispose();
            reference.Dispose();
            compositor.Dispose();
        }
    }

    private static void RequireNativeFixture(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static void ValidateNativeWindowLocality()
    {
        const string name = "Native dependency locality, publication and owned acknowledgement";
        var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldMeshesPaths.WorldSettingsAssetPath);
        var data = AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(WorldMeshesPaths.TerrainAuthoringDataAssetPath);
        if (settings == null || data == null || !SystemInfo.supports2DArrayTextures
            || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat)
            || (SystemInfo.copyTextureSupport & (CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT))
                != (CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT))
        { Add(name, ValidationOutcome.Blocked, "Committed native Height and RFloat/array copy support are required."); return; }
        if (!TerrainAuthoringStateUtility.TryValidateCommittedHeightfield(settings, data,
            TerrainAuthoringHeightfieldValidationMode.Operational, out _, out _, out string error))
        { Add(name, ValidationOutcome.Blocked, error); return; }
        int samples = settings.HeightTileSamplesPerSide;
        int guard = TerrainAnalysisWindowUtility.CalculateRequiredInteractiveGuardTileCount(settings);
        int width = guard + 2;
        long bytes = (long)samples * samples * sizeof(float) * (2L * width * width + 1);
        if (width > settings.HeightTileGridWidth || width > settings.HeightTileGridHeight
            || width * (long)width > 64 || bytes > 128L * 1024 * 1024)
        { Add(name, ValidationOutcome.Blocked, "The isolated native fixture needs an extra tile outside its guard and is capped at 64 tiles / 128 MiB."); return; }
        var physical = new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(width, width));
        var required = new TerrainHeightCacheWindow(Vector2Int.zero, new Vector2Int(guard + 1, guard + 1));
        var output = new TerrainHeightCacheWindow(Vector2Int.zero, Vector2Int.one);
        var native = new TerrainAuthoringPreviewLodState(0, 1, samples,
            TerrainHeightResolutionUtility.GetSampleSpacing(settings, 1));
        TerrainAuthoringPreviewLodState incomplete = null;
        Texture2D seed = null;
        WorldSettings changedSettings = null;
        try
        {
            native.StagingCache = new TerrainAuthoringPreviewCache();
            RequireNativeFixture(native.StagingCache.TryInitializeStagingWindow(settings, data, physical, 1, out error), error);
            seed = new Texture2D(samples, samples, TextureFormat.RFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var values = new float[samples * samples];
            for (int i = 0; i < values.Length; i++) values[i] = 5f;
            seed.SetPixelData(values, 0); seed.Apply(false, false);
            for (int i = 0; i < physical.TileCount; i++)
            {
                Graphics.CopyTexture(seed, 0, 0, native.StagingCache.HeightCache, i, 0);
                RequireNativeFixture(native.StagingCache.TryGetTileCoordinate(i, out var tile)
                    && native.StagingCache.TryCommitFinalCompositeTile(tile, 5f, 5f, out error), error);
            }
            string committed = native.StagingCache.SourceCommittedHeightfieldSignature;
            string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(settings, data);
            RequireNativeFixture(native.StagingCache.TryFinalizeStagingForActivation(overall, out error), error);
            native.RequestedRequiredWindow = physical; native.StagingAuthoringGeneration = 17;
            native.PromoteStagingCache(); native.DirtyTargetGeneration = 18;
            var inside = Vector2Int.zero; var outside = new Vector2Int(width - 1, width - 1);
            var incoming = new HashSet<Vector2Int>(); var markers = new HashSet<Vector2Int>();
            Func<TerrainRegionalElevationInvalidationScope, TerrainAuthoringNativeAnalysisWindowState> evaluate = scope =>
                TerrainAuthoringPreviewService.EvaluateNativeAnalysisWindow(native, settings, committed, 18,
                    required, output, incoming, scope, markers);
            native.RecordDirtyFailure(outside, 18, "Isolated distant native failure", true, 1);
            var local = evaluate(TerrainRegionalElevationInvalidationScope.None);
            RequireNativeFixture(local.Ready && local.PendingCount == 0 && local.FailedCount == 0 && !native.CacheReady
                && !TerrainAuthoringPreviewService.IsNativeAnalysisCacheEligible(native.ActiveCache, settings,
                    17, 18, committed, overall, required, output), "Whole-cache staleness blocked a current native dependency.");
            incoming.Add(outside);
            RequireNativeFixture(evaluate(TerrainRegionalElevationInvalidationScope.None).Ready, "Distant unprojected scope blocked native analysis.");
            incoming.Add(inside);
            RequireNativeFixture(!evaluate(TerrainRegionalElevationInvalidationScope.None).Ready, "Inside unprojected scope was ignored.");
            incoming.Clear();
            native.DirtyFailures.Remove(outside);
            native.PendingDirtyTiles.Add(inside);
            var selected = TerrainAuthoringPreviewService.ChooseDirtyDestination(new[] { native }, true, outside, false,
                null, true, required, out var selectedTile);
            RequireNativeFixture(ReferenceEquals(selected, native) && selectedTile == inside,
                "Held distant native work beat the live dependency tie-break.");
            var latest = new HashSet<Vector2Int> { outside };
            selected = TerrainAuthoringPreviewService.ChooseDirtyDestination(new[] { native }, true, inside, true,
                latest.Contains, true, required, out selectedTile);
            RequireNativeFixture(selectedTile == outside, "Analysis demand displaced the latest visible edit.");
            native.ActiveRequiredWindow = new TerrainHeightCacheWindow(outside, Vector2Int.one);
            selected = TerrainAuthoringPreviewService.ChooseDirtyDestination(new[] { native }, true, outside, false,
                null, true, required, out selectedTile);
            RequireNativeFixture(selectedTile == inside, "A live analysis dependency in display guard storage was not promoted.");
            selected = TerrainAuthoringPreviewService.ChooseDirtyDestination(new[] { native }, true, outside, false,
                null, false, required, out selectedTile);
            RequireNativeFixture(selectedTile == outside, "Non-demand focus elevated native guard work.");
            native.RecordDirtyFailure(inside, 18, "Suppressed dependency", true, 2);
            selected = TerrainAuthoringPreviewService.ChooseDirtyDestination(new[] { native }, true, inside, false,
                null, true, required, out selectedTile);
            RequireNativeFixture(selectedTile == outside, "Failed native demand blocked unrelated runnable dirty work.");
            native.DirtyFailures.Remove(inside); native.PendingDirtyTiles.Remove(inside);
            native.ActiveRequiredWindow = physical;
            float size = settings.HeightTileWorldSize;
            var near = TerrainRegionalElevationInvalidationScope.FromWorldBounds(new Bounds(new Vector3(size * 0.5f, 0, size * 0.5f), Vector3.one));
            var far = TerrainRegionalElevationInvalidationScope.FromWorldBounds(new Bounds(new Vector3((outside.x + 0.5f) * size, 0, (outside.y + 0.5f) * size), Vector3.one));
            RequireNativeFixture(!evaluate(near).Ready && evaluate(far).Ready
                && !evaluate(TerrainRegionalElevationInvalidationScope.WholeWorld).Ready, "Regional dependency locality was incorrect.");
            native.PendingRegionalTiles.Add(inside);
            RequireNativeFixture(!evaluate(TerrainRegionalElevationInvalidationScope.None).Ready, "Projected regional work was ignored.");
            native.PendingRegionalTiles.Clear(); native.RecordDirtyFailure(inside, 18, "Isolated dependency failure", true, 2);
            RequireNativeFixture(evaluate(TerrainRegionalElevationInvalidationScope.None).FailedCount == 1
                && !evaluate(TerrainRegionalElevationInvalidationScope.None).Ready, "Required dirty failure was accepted as current.");
            native.DirtyFailures.Remove(inside); native.PendingDirtyTiles.Remove(inside);
            markers.Add(inside);
            local = evaluate(TerrainRegionalElevationInvalidationScope.None);
            RequireNativeFixture(local.ContentCurrent && !local.Ready && local.PublicationCount == 1,
                "Committed native bytes could export an old analysis composite identity.");
            native.DirtyFailures.Remove(outside); native.PendingDirtyTiles.Remove(outside); markers.Add(outside);
            var consume = new List<Vector2Int>(); var changed = new List<Vector2Int>();
            TerrainAuthoringPreviewService.CollectNativePublicationTiles(native, settings, committed, 18, true,
                required, false, markers, incoming, TerrainRegionalElevationInvalidationScope.None, consume, changed);
            RequireNativeFixture(consume.Count == 1 && consume[0] == outside && changed.Count == 0,
                "Outside publication waited on the window, or an incomplete window published.");
            markers.Remove(outside); consume.Clear();
            TerrainAuthoringPreviewService.CollectNativePublicationTiles(native, settings, committed, 18, true,
                required, true, markers, incoming, TerrainRegionalElevationInvalidationScope.None, consume, changed);
            RequireNativeFixture(changed.Count == 1 && changed[0] == inside && consume.Count == 1,
                "Native tile completion depended on unrelated coarse obligations.");
            foreach (var tile in consume) markers.Remove(tile);
            markers.Add(inside); native.PendingDirtyTiles.Add(inside);
            RequireNativeFixture(markers.Contains(inside) && !evaluate(TerrainRegionalElevationInvalidationScope.None).Ready,
                "Reentrant authoring was cleared by old publication intent.");
            markers.Clear(); native.PendingDirtyTiles.Clear();
            RequireNativeFixture(!TerrainAuthoringPreviewService.EvaluateNativeAnalysisWindow(native, settings, committed + " changed", 18,
                required, output, incoming, TerrainRegionalElevationInvalidationScope.None, markers).Coverage,
                "An incompatible committed base was accepted.");
            native.DirtyTargetGeneration = 19;
            RequireNativeFixture(!evaluate(TerrainRegionalElevationInvalidationScope.None).Ready, "A mismatched accepted content target was accepted.");
            native.DirtyTargetGeneration = 18;
            native.WriteFailed = true;
            RequireNativeFixture(!evaluate(TerrainRegionalElevationInvalidationScope.None).Coverage, "Unsafe native storage was borrowed.");
            native.WriteFailed = false;
            changedSettings = UnityEngine.Object.Instantiate(settings);
            changedSettings.heightfieldResolutionPerChunk *= 2;
            RequireNativeFixture(!TerrainAuthoringPreviewService.EvaluateNativeAnalysisWindow(native, changedSettings, committed, 18,
                required, output, incoming, TerrainRegionalElevationInvalidationScope.None, markers).Coverage,
                "Foreign native sample geometry was accepted.");
            incomplete = new TerrainAuthoringPreviewLodState(0, 1, samples, native.SampleSpacing)
                { ActiveCache = new TerrainAuthoringPreviewCache(), DirtyTargetGeneration = 18 };
            RequireNativeFixture(incomplete.ActiveCache.TryInitializeStagingWindow(settings, data, physical, 1, out error), error);
            RequireNativeFixture(!TerrainAuthoringPreviewService.EvaluateNativeAnalysisWindow(incomplete, settings, committed, 18,
                required, output, incoming, TerrainRegionalElevationInvalidationScope.None, markers).Ready,
                "Never-composed native slices were accepted.");
            native.CacheReady = true; native.ActiveAuthoringGeneration = 17;
            incoming.Add(new Vector2Int(width, width));
            RequireNativeFixture(TerrainAuthoringPreviewService.TryAcknowledgeOwnedNativeAuthoring(native, settings,
                committed, overall + " metadata", 17, 18, true, true, incoming, TerrainRegionalElevationInvalidationScope.None)
                && native.ActiveAuthoringGeneration == 18, "Proven distant owned authoring could not acknowledge metadata.");
            incoming.Clear(); incoming.Add(outside);
            RequireNativeFixture(!TerrainAuthoringPreviewService.TryAcknowledgeOwnedNativeAuthoring(native, settings,
                committed, overall, 18, 19, true, true, incoming, TerrainRegionalElevationInvalidationScope.None),
                "Owned acknowledgement checked only the current required window, not physical guard storage.");
            native.CacheReady = false; incoming.Clear();
            RequireNativeFixture(!TerrainAuthoringPreviewService.TryAcknowledgeOwnedNativeAuthoring(native, settings,
                committed, overall, 18, 19, true, true, incoming, TerrainRegionalElevationInvalidationScope.None),
                "A distant edit rescued previously stale owned Height.");
            native.CacheReady = true;
            RequireNativeFixture(!TerrainAuthoringPreviewService.TryAcknowledgeOwnedNativeAuthoring(native, settings,
                committed, overall, 18, 19, false, true, incoming, TerrainRegionalElevationInvalidationScope.None),
                "Full invalidation used metadata-only acknowledgement.");
            native.ActiveCache.HeightCache.Release();
            RequireNativeFixture(!evaluate(TerrainRegionalElevationInvalidationScope.None).Coverage, "Released native texture was accepted.");
            Add(name, ValidationOutcome.Pass, "Required native scope, failure/publication gating, strict owned identity, guard storage and reentrant intent remain independent from distant work.");
        }
        catch (Exception exception) { Add(name, ValidationOutcome.Fail, exception.Message); }
        finally
        {
            native.Dispose(); incomplete?.Dispose();
            if (seed != null) UnityEngine.Object.DestroyImmediate(seed);
            if (changedSettings != null) UnityEngine.Object.DestroyImmediate(changedSettings);
        }
    }

    private static void ValidateLiveAnalysisSource()
    {
        var snapshot = TerrainAuthoringPreviewService.GetTerrainAnalysisSourceSnapshot();
        if (!TerrainAuthoringPreviewService.TryGetTerrainAnalysisGpuSource(out var source, out var output))
        {
            Add("Live native analysis source", ValidationOutcome.Blocked, snapshot.Message);
            return;
        }
        var diagnostics = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        var ownership = diagnostics.Ownership;
        if (diagnostics.SharedHeight.Present)
        {
            bool bounded = diagnostics.LastDirtyAllocations <= 2 && diagnostics.LastDirtyCopies <= 1
                && diagnostics.LastDirtyLoads <= TerrainAuthoringPreviewService.StreamingCommittedLoadsPerUpdate
                && diagnostics.LastDirtyMaterializations <= TerrainAuthoringPreviewService.StreamingMaterializationsPerUpdate
                && diagnostics.LastDirtyCompositions <= TerrainAuthoringPreviewService.StreamingCompositionsPerUpdate;
            Add("Shared display/native callback quotas", bounded ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                "Native and display share the measured callback budget with independent bounded source, copy and GPU submission opportunities.");
        }
        bool nativeClassified = snapshot.Kind != TerrainAuthoringAnalysisSourceKind.Unavailable
            && snapshot.SamplesPerSide == source.SamplesPerSide && Mathf.Approximately(snapshot.SampleSpacing, source.SampleSpacing)
            && (!diagnostics.Worker.Present || diagnostics.Worker.Purpose != TerrainAuthoringPreviewCachePublication.NativeAnalysis
                || diagnostics.DisplayLods.Count == 0 || !diagnostics.DisplayLods[0].Staging.Present);
        if (snapshot.Kind == TerrainAuthoringAnalysisSourceKind.BorrowedNative)
        {
            nativeClassified &= diagnostics.DisplayLods.Count > 0 && diagnostics.DisplayLods[0].Active.Representation.Stride == 1
                && diagnostics.DisplayLods[0].Active.Window == snapshot.PhysicalWindow
                && ownership.TotalBytes == TerrainAuthoringPreviewService.ApproximateTotalResidentGpuMemoryBytes;
            // A retained owned native cache is still charged once; borrowing adds no new allocation category.
        }
        bool safe = nativeClassified && source.SourceWindow.Contains(snapshot.RequiredSourceWindow)
            && snapshot.RequiredSourceWindow.Contains(output) && output.TileCount <= 9
            && TerrainAnalysisWindowUtility.TryCalculateInteractiveOutputWindow(source, out var safeOutput, out _)
            && safeOutput.Contains(output);
        Add("Live native analysis source", safe ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            "Kind=" + snapshot.Kind + ", Source=" + source.SourceWindow + ", Required=" + snapshot.RequiredSourceWindow
            + ", Analysis=" + output + ", Guard=" + snapshot.GuardTileCount
            + ", Authoring=" + snapshot.AuthoringGeneration + ", Owner=" + snapshot.OwnershipGeneration
            + ", Residency=" + source.ResidencyGeneration + ", Composite=" + source.CompositeGeneration);
    }

    private static void Add(
        string name,
        ValidationOutcome outcome,
        string details
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Outcome = outcome,
                Details = details ?? ""
            }
        );
    }

    private static void WriteReport()
    {
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Analysis Window Safety Validation"
        );

        builder.AppendLine();

        foreach (ValidationResult result in results)
        {
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    passed++;
                    break;

                case ValidationOutcome.Fail:
                    failed++;
                    break;

                default:
                    blocked++;
                    break;
            }

            builder.Append("[");
            builder.Append(result.Outcome.ToString().ToUpperInvariant());
            builder.Append("] ");
            builder.AppendLine(result.Name);

            if (!string.IsNullOrEmpty(result.Details))
            {
                builder.AppendLine("    " + result.Details);
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Summary: " +
            passed +
            " passed, " +
            failed +
            " failed, " +
            blocked +
            " blocked."
        );

        lastRunSummary =
            TerrainValidationRunSummary.CreateCompleted(
                passed,
                failed,
                blocked,
                $"{passed} passed, {failed} failed, {blocked} blocked."
            );

        if (failed > 0)
        {
            Debug.LogError(builder.ToString());
        }
        else if (blocked > 0)
        {
            Debug.LogWarning(builder.ToString());
        }
        else
        {
            Debug.Log(builder.ToString());
        }
    }
}
