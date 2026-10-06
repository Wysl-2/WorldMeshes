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
            source.Apply(false, true);

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
                null, materializer, firstTile, out _
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

