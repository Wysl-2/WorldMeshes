using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainHeightmapStreamer))]
public class TerrainHeightmapCacheValidator :
    MonoBehaviour
{
    [Header("Validation")]

    [SerializeField]
    [Min(0f)]
    private float cacheValidationTolerance = 0f;

    private TerrainHeightmapStreamer streamer;
    private Coroutine validationRoutine;
    private bool cacheInspectionAcquired;

    private TerrainRuntimeValidationStatus cacheValidationStatus =
        TerrainRuntimeValidationStatus.NotRun;

    private TerrainRuntimeValidationStatus crossResolutionValidationStatus =
        TerrainRuntimeValidationStatus.NotRun;

    private string cacheValidationSummary =
        "Height LOD caches have not been validated.";

    private string crossResolutionValidationSummary =
        "Cross-resolution Height consistency has not been validated.";

    public TerrainRuntimeValidationStatus CacheValidationStatus =>
        cacheValidationStatus;

    public TerrainRuntimeValidationStatus CrossResolutionValidationStatus =>
        crossResolutionValidationStatus;

    public string CacheValidationSummary =>
        cacheValidationSummary;

    public string CrossResolutionValidationSummary =>
        crossResolutionValidationSummary;

    public bool IsValidating =>
        validationRoutine != null;

    public bool HasValidatedCurrentCache =>
        cacheValidationStatus == TerrainRuntimeValidationStatus.Passed
        || cacheValidationStatus == TerrainRuntimeValidationStatus.Failed
        || cacheValidationStatus == TerrainRuntimeValidationStatus.Inconclusive;

    public bool LastValidationPassed =>
        cacheValidationStatus == TerrainRuntimeValidationStatus.Passed;

    private void OnEnable()
    {
        EnsureStreamerReference();
    }

    private void OnDisable()
    {
        if (validationRoutine != null)
        {
            StopCoroutine(validationRoutine);
            validationRoutine = null;
        }

        ReleaseCacheInspection();
    }

    [ContextMenu("Validate All Height LOD Caches")]
    public void BeginValidation()
    {
        if (!CanBeginValidation())
        {
            return;
        }

        cacheValidationStatus =
            TerrainRuntimeValidationStatus.Running;

        cacheValidationSummary =
            "Validating active multiresolution Height caches...";

        validationRoutine =
            StartCoroutine(
                ValidateAllHeightCachesRoutine()
            );
    }

    [ContextMenu("Validate Cross-Resolution Height Consistency")]
    public void BeginCrossResolutionValidation()
    {
        if (!CanBeginValidation())
        {
            return;
        }

        crossResolutionValidationStatus =
            TerrainRuntimeValidationStatus.Running;

        crossResolutionValidationSummary =
            "Validating shared-lattice and authoritative Height consistency...";

        validationRoutine =
            StartCoroutine(
                ValidateCrossResolutionRoutine()
            );
    }

    private bool CanBeginValidation()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "Multiresolution Height validation can only run in Play Mode.",
                this
            );

            return false;
        }

        if (validationRoutine != null)
        {
            Debug.LogWarning(
                "A Height runtime validation is already running.",
                this
            );

            return false;
        }

        EnsureStreamerReference();

        if (streamer == null)
        {
            Debug.LogError(
                "TerrainHeightmapCacheValidator requires TerrainHeightmapStreamer on the same GameObject.",
                this
            );

            return false;
        }

        return true;
    }

    private IEnumerator ValidateAllHeightCachesRoutine()
    {
        if (!AcquireCacheInspection(out string inspectionError))
        {
            FailCacheValidation(inspectionError);
            yield break;
        }

        if (
            !streamer.TryGetRuntimeHeightConfigurationForInspection(
                out _,
                out TerrainHeightmapManifest heightManifest
            )
        )
        {
            ReleaseCacheInspection();
            FailCacheValidation(
                "Runtime Height manifest is unavailable for cache validation."
            );
            yield break;
        }

        int levelsValidated = 0;
        int pagesValidated = 0;
        long samplesCompared = 0L;
        long sampleMismatches = 0L;
        long boundarySamplesCompared = 0L;
        long boundaryMismatches = 0L;
        int missingRequiredPages = 0;
        int missingOptionalPrefetchPages = 0;
        float maximumDifference = 0f;
        string firstFailure = null;

        try
        {
            List<Vector2Int> activePages =
                new List<Vector2Int>();

            for (
                int level = 0;
                level < streamer.HeightLodRuntimeStateCount;
                level++
            )
            {
                if (
                    !streamer.TryGetHeightLodInspectionSnapshot(
                        level,
                        out TerrainHeightLodInspectionSnapshot snapshot
                    )
                )
                {
                    firstFailure =
                        $"Could not capture Height LOD{level} inspection state.";

                    break;
                }

                if (
                    !ValidateLodStructure(
                        snapshot,
                        out string structureError
                    )
                )
                {
                    firstFailure = structureError;
                    break;
                }

                streamer.GetHeightLodActiveValidPagesForInspection(
                    level,
                    activePages
                );

                HashSet<Vector2Int> activeSet =
                    new HashSet<Vector2Int>(activePages);

                if (snapshot.ActiveRequiredPages.IsValid)
                {
                    for (
                        int z = snapshot.ActiveRequiredPages.Minimum.y;
                        z <= snapshot.ActiveRequiredPages.Maximum.y;
                        z++
                    )
                    {
                        for (
                            int x = snapshot.ActiveRequiredPages.Minimum.x;
                            x <= snapshot.ActiveRequiredPages.Maximum.x;
                            x++
                        )
                        {
                            Vector2Int coordinate =
                                new Vector2Int(x, z);

                            if (!activeSet.Contains(coordinate))
                            {
                                missingRequiredPages++;

                                if (firstFailure == null)
                                {
                                    firstFailure =
                                        $"LOD{level} mandatory page ({x}, {z}) is not in ActiveValidPages.";
                                }
                            }
                        }
                    }
                }

                if (snapshot.RequestedPrefetchPages.IsValid)
                {
                    for (
                        int z = snapshot.RequestedPrefetchPages.Minimum.y;
                        z <= snapshot.RequestedPrefetchPages.Maximum.y;
                        z++
                    )
                    {
                        for (
                            int x = snapshot.RequestedPrefetchPages.Minimum.x;
                            x <= snapshot.RequestedPrefetchPages.Maximum.x;
                            x++
                        )
                        {
                            Vector2Int coordinate =
                                new Vector2Int(x, z);

                            if (
                                !snapshot.RequestedRequiredPages.Contains(coordinate)
                                && !activeSet.Contains(coordinate)
                            )
                            {
                                missingOptionalPrefetchPages++;
                            }
                        }
                    }
                }

                for (
                    int pageIndex = 0;
                    pageIndex < activePages.Count;
                    pageIndex++
                )
                {
                    Vector2Int coordinate =
                        activePages[pageIndex];

                    if (
                        !streamer.TryGetHeightLodCacheSliceForInspection(
                            level,
                            coordinate,
                            out int slice,
                            out bool activeValid,
                            out _
                        )
                        || !activeValid
                    )
                    {
                        if (firstFailure == null)
                        {
                            firstFailure =
                                $"LOD{level} active page ({coordinate.x}, {coordinate.y}) has no usable cache slice.";
                        }

                        continue;
                    }

                    float[] sourceData = null;
                    float[] cacheData = null;
                    string sourceError = null;
                    string cacheError = null;

                    yield return ReadExpectedSourcePage(
                        heightManifest,
                        snapshot.SampleStride,
                        coordinate,
                        (data, error) =>
                        {
                            sourceData = data;
                            sourceError = error;
                        }
                    );

                    if (sourceError != null)
                    {
                        firstFailure = sourceError;
                        break;
                    }

                    yield return ReadTexturePage(
                        snapshot.ActiveCache,
                        slice,
                        true,
                        (data, error) =>
                        {
                            cacheData = data;
                            cacheError = error;
                        }
                    );

                    if (cacheError != null)
                    {
                        firstFailure = cacheError;
                        break;
                    }

                    if (
                        sourceData == null
                        || cacheData == null
                        || sourceData.Length != cacheData.Length
                    )
                    {
                        firstFailure =
                            $"LOD{level} page ({coordinate.x}, {coordinate.y}) returned incompatible source/cache sample counts.";

                        break;
                    }

                    pagesValidated++;

                    for (
                        int sampleIndex = 0;
                        sampleIndex < sourceData.Length;
                        sampleIndex++
                    )
                    {
                        float expected = sourceData[sampleIndex];
                        float actual = cacheData[sampleIndex];

                        samplesCompared++;

                        float difference =
                            FiniteDifference(expected, actual);

                        if (!float.IsInfinity(difference))
                        {
                            maximumDifference =
                                Mathf.Max(
                                    maximumDifference,
                                    difference
                                );
                        }

                        if (
                            float.IsInfinity(difference)
                            || difference > cacheValidationTolerance
                        )
                        {
                            sampleMismatches++;

                            if (firstFailure == null)
                            {
                                firstFailure =
                                    $"LOD{level} cache mismatch at page ({coordinate.x}, {coordinate.y}), sample {sampleIndex}. Expected {expected:R}, actual {actual:R}.";
                            }
                        }
                    }

                    Vector2Int right =
                        new Vector2Int(
                            coordinate.x + 1,
                            coordinate.y
                        );

                    if (activeSet.Contains(right))
                    {
                        yield return ValidateCacheBoundary(
                            level,
                            snapshot,
                            coordinate,
                            right,
                            true,
                            result =>
                            {
                                boundarySamplesCompared += result.Comparisons;
                                boundaryMismatches += result.Mismatches;
                                maximumDifference =
                                    Mathf.Max(
                                        maximumDifference,
                                        result.MaximumDifference
                                    );

                                if (
                                    firstFailure == null
                                    && result.FirstError != null
                                )
                                {
                                    firstFailure = result.FirstError;
                                }
                            }
                        );
                    }

                    Vector2Int up =
                        new Vector2Int(
                            coordinate.x,
                            coordinate.y + 1
                        );

                    if (activeSet.Contains(up))
                    {
                        yield return ValidateCacheBoundary(
                            level,
                            snapshot,
                            coordinate,
                            up,
                            false,
                            result =>
                            {
                                boundarySamplesCompared += result.Comparisons;
                                boundaryMismatches += result.Mismatches;
                                maximumDifference =
                                    Mathf.Max(
                                        maximumDifference,
                                        result.MaximumDifference
                                    );

                                if (
                                    firstFailure == null
                                    && result.FirstError != null
                                )
                                {
                                    firstFailure = result.FirstError;
                                }
                            }
                        );
                    }
                }

                if (firstFailure != null)
                {
                    break;
                }

                levelsValidated++;
            }
        }
        finally
        {
            ReleaseCacheInspection();
        }

        bool passed =
            firstFailure == null
            && missingRequiredPages == 0
            && sampleMismatches == 0
            && boundaryMismatches == 0
            && levelsValidated == streamer.HeightLodRuntimeStateCount;

        cacheValidationStatus =
            passed
                ? TerrainRuntimeValidationStatus.Passed
                : TerrainRuntimeValidationStatus.Failed;

        cacheValidationSummary =
            $"LOD Levels: {levelsValidated}/{streamer.HeightLodRuntimeStateCount}\n" +
            $"Pages: {pagesValidated:N0}\n" +
            $"Samples Compared: {samplesCompared:N0}\n" +
            $"Sample Mismatches: {sampleMismatches:N0}\n" +
            $"Boundary Samples: {boundarySamplesCompared:N0}\n" +
            $"Boundary Mismatches: {boundaryMismatches:N0}\n" +
            $"Missing Required Pages: {missingRequiredPages:N0}\n" +
            $"Missing Optional Prefetch Pages: {missingOptionalPrefetchPages:N0}\n" +
            $"Maximum Difference: {maximumDifference:R}" +
            (firstFailure != null ? "\n\n" + firstFailure : "");

        validationRoutine = null;

        if (passed)
        {
            Debug.Log(
                "Multiresolution Height-cache validation passed.\n\n" +
                cacheValidationSummary,
                this
            );
        }
        else
        {
            Debug.LogError(
                "Multiresolution Height-cache validation FAILED.\n\n" +
                cacheValidationSummary,
                this
            );
        }
    }

    private IEnumerator ValidateCrossResolutionRoutine()
    {
        if (!AcquireCacheInspection(out string inspectionError))
        {
            FailCrossResolutionValidation(inspectionError);
            yield break;
        }

        int pairsTested = 0;
        int pagesTested = 0;
        long sharedSamplesCompared = 0L;
        long authoritativeSamplesCompared = 0L;
        long mismatchCount = 0L;
        float maximumDifference = 0f;
        string firstFailure = null;

        try
        {
            if (
                !streamer.TryGetRuntimeHeightConfigurationForInspection(
                    out _,
                    out TerrainHeightmapManifest manifest
                )
            )
            {
                firstFailure =
                    "Runtime Height configuration is unavailable.";
            }

            List<Vector2Int> finePages =
                new List<Vector2Int>();

            List<Vector2Int> coarsePages =
                new List<Vector2Int>();

            for (
                int fineLevel = 0;
                firstFailure == null
                && fineLevel < streamer.HeightLodRuntimeStateCount - 1;
                fineLevel++
            )
            {
                int coarseLevel = fineLevel + 1;

                if (
                    !streamer.TryGetHeightLodInspectionSnapshot(
                        fineLevel,
                        out TerrainHeightLodInspectionSnapshot fineSnapshot
                    )
                    ||
                    !streamer.TryGetHeightLodInspectionSnapshot(
                        coarseLevel,
                        out TerrainHeightLodInspectionSnapshot coarseSnapshot
                    )
                )
                {
                    firstFailure =
                        $"Could not capture adjacent LOD{fineLevel}/LOD{coarseLevel} state.";

                    break;
                }

                if (
                    coarseSnapshot.SampleStride % fineSnapshot.SampleStride != 0
                )
                {
                    firstFailure =
                        $"LOD{coarseLevel} stride {coarseSnapshot.SampleStride} is not an integer multiple of LOD{fineLevel} stride {fineSnapshot.SampleStride}.";

                    break;
                }

                int ratio =
                    coarseSnapshot.SampleStride /
                    fineSnapshot.SampleStride;

                streamer.GetHeightLodActiveValidPagesForInspection(
                    fineLevel,
                    finePages
                );

                streamer.GetHeightLodActiveValidPagesForInspection(
                    coarseLevel,
                    coarsePages
                );

                HashSet<Vector2Int> fineSet =
                    new HashSet<Vector2Int>(finePages);

                bool pairHadOverlap = false;

                for (
                    int pageIndex = 0;
                    pageIndex < coarsePages.Count;
                    pageIndex++
                )
                {
                    Vector2Int coordinate =
                        coarsePages[pageIndex];

                    if (!fineSet.Contains(coordinate))
                    {
                        continue;
                    }

                    pairHadOverlap = true;

                    if (
                        !streamer.TryGetHeightLodCacheSliceForInspection(
                            fineLevel,
                            coordinate,
                            out int fineSlice,
                            out bool fineValid,
                            out _
                        )
                        ||
                        !streamer.TryGetHeightLodCacheSliceForInspection(
                            coarseLevel,
                            coordinate,
                            out int coarseSlice,
                            out bool coarseValid,
                            out _
                        )
                        || !fineValid
                        || !coarseValid
                    )
                    {
                        firstFailure =
                            $"Could not inspect overlapping page ({coordinate.x}, {coordinate.y}) for LOD{fineLevel}/LOD{coarseLevel}.";

                        break;
                    }

                    float[] fineData = null;
                    float[] coarseData = null;
                    string readError = null;

                    yield return ReadTexturePage(
                        fineSnapshot.ActiveCache,
                        fineSlice,
                        true,
                        (data, error) =>
                        {
                            fineData = data;
                            readError = error;
                        }
                    );

                    if (readError != null)
                    {
                        firstFailure = readError;
                        break;
                    }

                    yield return ReadTexturePage(
                        coarseSnapshot.ActiveCache,
                        coarseSlice,
                        true,
                        (data, error) =>
                        {
                            coarseData = data;
                            readError = error;
                        }
                    );

                    if (readError != null)
                    {
                        firstFailure = readError;
                        break;
                    }

                    int fineSamples =
                        fineSnapshot.Descriptor.SamplesPerSide;

                    int coarseSamples =
                        coarseSnapshot.Descriptor.SamplesPerSide;

                    for (int z = 0; z < coarseSamples; z++)
                    {
                        for (int x = 0; x < coarseSamples; x++)
                        {
                            int fineIndex =
                                x * ratio +
                                z * ratio * fineSamples;

                            int coarseIndex =
                                x + z * coarseSamples;

                            float expected = fineData[fineIndex];
                            float actual = coarseData[coarseIndex];

                            sharedSamplesCompared++;

                            float difference =
                                FiniteDifference(expected, actual);

                            if (!float.IsInfinity(difference))
                            {
                                maximumDifference =
                                    Mathf.Max(
                                        maximumDifference,
                                        difference
                                    );
                            }

                            if (
                                float.IsInfinity(difference)
                                || difference > cacheValidationTolerance
                            )
                            {
                                mismatchCount++;

                                if (firstFailure == null)
                                {
                                    firstFailure =
                                        $"Shared-lattice mismatch for page ({coordinate.x}, {coordinate.y}) LOD{fineLevel}->LOD{coarseLevel}, coarse sample ({x}, {z}).";
                                }
                            }
                        }
                    }

                    AsyncOperationHandle<Texture2D> nativeHandle =
                        default;

                    bool nativeHandleValid = false;

                    try
                    {
                        string nativeAddress =
                            manifest.GetHeightRepresentationAddress(
                                1,
                                coordinate.x,
                                coordinate.y
                            );

                        nativeHandle =
                            Addressables.LoadAssetAsync<Texture2D>(
                                nativeAddress
                            );

                        nativeHandleValid = true;

                        if (!nativeHandle.IsDone)
                        {
                            yield return nativeHandle;
                        }

                        if (
                            nativeHandle.Status != AsyncOperationStatus.Succeeded
                            || nativeHandle.Result == null
                        )
                        {
                            firstFailure =
                                $"Could not load authoritative stride-1 page ({coordinate.x}, {coordinate.y}) for runtime validation.";

                            break;
                        }

                        float[] nativeData = null;

                        yield return ReadTexturePage(
                            nativeHandle.Result,
                            0,
                            false,
                            (data, error) =>
                            {
                                nativeData = data;
                                readError = error;
                            }
                        );

                        if (readError != null)
                        {
                            firstFailure = readError;
                            break;
                        }

                        int nativeSamples =
                            manifest.heightTileSamplesPerSide;

                        int stride =
                            coarseSnapshot.SampleStride;

                        for (int z = 0; z < coarseSamples; z++)
                        {
                            for (int x = 0; x < coarseSamples; x++)
                            {
                                int nativeIndex =
                                    x * stride +
                                    z * stride * nativeSamples;

                                int coarseIndex =
                                    x + z * coarseSamples;

                                float expected = nativeData[nativeIndex];
                                float actual = coarseData[coarseIndex];

                                authoritativeSamplesCompared++;

                                float difference =
                                    FiniteDifference(expected, actual);

                                if (!float.IsInfinity(difference))
                                {
                                    maximumDifference =
                                        Mathf.Max(
                                            maximumDifference,
                                            difference
                                        );
                                }

                                if (
                                    float.IsInfinity(difference)
                                    || difference > cacheValidationTolerance
                                )
                                {
                                    mismatchCount++;

                                    if (firstFailure == null)
                                    {
                                        firstFailure =
                                            $"Authoritative stride-1 mismatch for page ({coordinate.x}, {coordinate.y}), runtime stride {stride}, sample ({x}, {z}).";
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (
                            nativeHandleValid
                            && nativeHandle.IsValid()
                        )
                        {
                            Addressables.Release(nativeHandle);
                        }
                    }

                    pagesTested++;

                    if (firstFailure != null)
                    {
                        break;
                    }
                }

                if (firstFailure != null)
                {
                    break;
                }

                if (pairHadOverlap)
                {
                    pairsTested++;
                }
            }
        }
        finally
        {
            ReleaseCacheInspection();
        }

        if (
            firstFailure == null
            && pairsTested == 0
        )
        {
            crossResolutionValidationStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            crossResolutionValidationSummary =
                "No adjacent active Height LOD pair had an overlapping valid geographic page to compare.";
        }
        else
        {
            bool passed =
                firstFailure == null
                && mismatchCount == 0;

            crossResolutionValidationStatus =
                passed
                    ? TerrainRuntimeValidationStatus.Passed
                    : TerrainRuntimeValidationStatus.Failed;

            crossResolutionValidationSummary =
                $"Adjacent LOD Pairs: {pairsTested:N0}\n" +
                $"Overlapping Pages: {pagesTested:N0}\n" +
                $"Shared-Lattice Samples: {sharedSamplesCompared:N0}\n" +
                $"Authoritative Samples: {authoritativeSamplesCompared:N0}\n" +
                $"Mismatches: {mismatchCount:N0}\n" +
                $"Maximum Difference: {maximumDifference:R}" +
                (firstFailure != null ? "\n\n" + firstFailure : "");
        }

        validationRoutine = null;

        if (
            crossResolutionValidationStatus ==
            TerrainRuntimeValidationStatus.Passed
        )
        {
            Debug.Log(
                "Cross-resolution runtime Height validation passed.\n\n" +
                crossResolutionValidationSummary,
                this
            );
        }
        else if (
            crossResolutionValidationStatus ==
            TerrainRuntimeValidationStatus.Failed
        )
        {
            Debug.LogError(
                "Cross-resolution runtime Height validation FAILED.\n\n" +
                crossResolutionValidationSummary,
                this
            );
        }
        else
        {
            Debug.LogWarning(
                "Cross-resolution runtime Height validation was inconclusive.\n\n" +
                crossResolutionValidationSummary,
                this
            );
        }
    }

    private bool ValidateLodStructure(
        TerrainHeightLodInspectionSnapshot snapshot,
        out string error
    )
    {
        error = null;

        if (
            !snapshot.CacheReady
            || snapshot.ActiveCache == null
        )
        {
            error =
                $"LOD{snapshot.Level} has no ready active cache.";

            return false;
        }

        int expectedDepth =
            snapshot.CacheWidth *
            snapshot.CacheHeight;

        if (
            snapshot.ActiveCache.width !=
                snapshot.Descriptor.SamplesPerSide
            ||
            snapshot.ActiveCache.height !=
                snapshot.Descriptor.SamplesPerSide
            ||
            snapshot.ActiveCache.depth != expectedDepth
        )
        {
            error =
                $"LOD{snapshot.Level} Texture2DArray dimensions do not match its descriptor/cache geometry.";

            return false;
        }

        TerrainHeightPageRect cachePages =
            new TerrainHeightPageRect(
                snapshot.ActiveCacheOrigin,
                new Vector2Int(
                    snapshot.ActiveCacheOrigin.x + snapshot.CacheWidth - 1,
                    snapshot.ActiveCacheOrigin.y + snapshot.CacheHeight - 1
                )
            );

        if (
            snapshot.ActiveRequiredPages.IsValid
            && !cachePages.Contains(
                snapshot.ActiveRequiredPages
            )
        )
        {
            error =
                $"LOD{snapshot.Level} active cache rectangle does not contain its required page rectangle.";

            return false;
        }

        return true;
    }

    private IEnumerator ValidateCacheBoundary(
        int level,
        TerrainHeightLodInspectionSnapshot snapshot,
        Vector2Int firstCoordinate,
        Vector2Int secondCoordinate,
        bool horizontal,
        Action<BoundaryResult> completed
    )
    {
        BoundaryResult result = default;

        if (
            !streamer.TryGetHeightLodCacheSliceForInspection(
                level,
                firstCoordinate,
                out int firstSlice,
                out bool firstValid,
                out _
            )
            ||
            !streamer.TryGetHeightLodCacheSliceForInspection(
                level,
                secondCoordinate,
                out int secondSlice,
                out bool secondValid,
                out _
            )
            || !firstValid
            || !secondValid
        )
        {
            completed(result);
            yield break;
        }

        float[] firstData = null;
        float[] secondData = null;
        string error = null;

        yield return ReadTexturePage(
            snapshot.ActiveCache,
            firstSlice,
            true,
            (data, readError) =>
            {
                firstData = data;
                error = readError;
            }
        );

        if (error != null)
        {
            result.FirstError = error;
            completed(result);
            yield break;
        }

        yield return ReadTexturePage(
            snapshot.ActiveCache,
            secondSlice,
            true,
            (data, readError) =>
            {
                secondData = data;
                error = readError;
            }
        );

        if (error != null)
        {
            result.FirstError = error;
            completed(result);
            yield break;
        }

        int samples =
            snapshot.Descriptor.SamplesPerSide;

        for (int i = 0; i < samples; i++)
        {
            int firstIndex;
            int secondIndex;

            if (horizontal)
            {
                firstIndex =
                    (samples - 1) + i * samples;

                secondIndex =
                    i * samples;
            }
            else
            {
                firstIndex =
                    i + (samples - 1) * samples;

                secondIndex = i;
            }

            float difference =
                FiniteDifference(
                    firstData[firstIndex],
                    secondData[secondIndex]
                );

            result.Comparisons++;

            if (!float.IsInfinity(difference))
            {
                result.MaximumDifference =
                    Mathf.Max(
                        result.MaximumDifference,
                        difference
                    );
            }

            if (
                float.IsInfinity(difference)
                || difference > cacheValidationTolerance
            )
            {
                result.Mismatches++;

                if (result.FirstError == null)
                {
                    result.FirstError =
                        $"LOD{level} cache page boundary mismatch between ({firstCoordinate.x}, {firstCoordinate.y}) and ({secondCoordinate.x}, {secondCoordinate.y}).";
                }
            }
        }

        completed(result);
    }

    private IEnumerator ReadExpectedSourcePage(
        TerrainHeightmapManifest manifest,
        int sampleStride,
        Vector2Int coordinate,
        Action<float[], string> completed
    )
    {
        if (manifest == null)
        {
            completed(null, "Runtime Height manifest is unavailable.");
            yield break;
        }

        string address =
            manifest.GetHeightRepresentationAddress(
                sampleStride,
                coordinate.x,
                coordinate.y
            );

        AsyncOperationHandle<Texture2D> handle =
            default;

        bool handleValid = false;

        try
        {
            handle =
                Addressables.LoadAssetAsync<Texture2D>(
                    address
                );

            handleValid = true;

            if (!handle.IsDone)
            {
                yield return handle;
            }

            if (
                handle.Status != AsyncOperationStatus.Succeeded
                || handle.Result == null
            )
            {
                completed(
                    null,
                    $"Could not load Height validation source stride {sampleStride} page ({coordinate.x}, {coordinate.y})."
                );

                yield break;
            }

            float[] data = null;
            string error = null;

            yield return ReadTexturePage(
                handle.Result,
                0,
                false,
                (readData, readError) =>
                {
                    data = readData;
                    error = readError;
                }
            );

            completed(data, error);
        }
        finally
        {
            if (
                handleValid
                && handle.IsValid()
            )
            {
                Addressables.Release(handle);
            }
        }
    }

    private IEnumerator ReadTexturePage(
        Texture texture,
        int slice,
        bool isArray,
        Action<float[], string> completed
    )
    {
        if (texture == null)
        {
            completed(null, "Cannot GPU-read a null Height texture.");
            yield break;
        }

        AsyncGPUReadbackRequest request;

        try
        {
            request =
                AsyncGPUReadback.Request(
                    texture,
                    0,
                    0,
                    texture.width,
                    0,
                    texture.height,
                    isArray ? slice : 0,
                    1,
                    TextureFormat.RFloat,
                    null
                );
        }
        catch (Exception exception)
        {
            completed(
                null,
                "Could not begin bounded Height GPU readback.\n\n" +
                exception.Message
            );

            yield break;
        }

        while (!request.done)
        {
            yield return null;
        }

        if (request.hasError)
        {
            completed(
                null,
                "Bounded Height GPU readback failed."
            );

            yield break;
        }

        NativeArray<float> data;

        try
        {
            data = request.GetData<float>();
        }
        catch (Exception exception)
        {
            completed(
                null,
                "Could not access Height GPU readback data.\n\n" +
                exception.Message
            );

            yield break;
        }

        float[] copy =
            new float[data.Length];

        data.CopyTo(copy);

        completed(copy, null);
    }

    private bool AcquireCacheInspection(
        out string error
    )
    {
        error = null;

        EnsureStreamerReference();

        if (
            streamer == null
            || !streamer.TryBeginMultiresolutionCacheInspection(
                out error
            )
        )
        {
            return false;
        }

        cacheInspectionAcquired = true;
        return true;
    }

    private void ReleaseCacheInspection()
    {
        if (!cacheInspectionAcquired)
        {
            return;
        }

        EnsureStreamerReference();

        if (streamer != null)
        {
            streamer.EndCacheInspection();
        }

        cacheInspectionAcquired = false;
    }

    private void EnsureStreamerReference()
    {
        if (streamer == null)
        {
            streamer =
                GetComponent<TerrainHeightmapStreamer>();
        }
    }

    private void FailCacheValidation(
        string reason
    )
    {
        ReleaseCacheInspection();

        cacheValidationStatus =
            TerrainRuntimeValidationStatus.Failed;

        cacheValidationSummary =
            string.IsNullOrEmpty(reason)
                ? "Height LOD cache validation could not start."
                : reason;

        validationRoutine = null;

        Debug.LogError(
            "Multiresolution Height-cache validation FAILED.\n\n" +
            cacheValidationSummary,
            this
        );
    }

    private void FailCrossResolutionValidation(
        string reason
    )
    {
        ReleaseCacheInspection();

        crossResolutionValidationStatus =
            TerrainRuntimeValidationStatus.Failed;

        crossResolutionValidationSummary =
            string.IsNullOrEmpty(reason)
                ? "Cross-resolution validation could not start."
                : reason;

        validationRoutine = null;

        Debug.LogError(
            "Cross-resolution runtime Height validation FAILED.\n\n" +
            crossResolutionValidationSummary,
            this
        );
    }

    private static float FiniteDifference(
        float first,
        float second
    )
    {
        if (
            float.IsNaN(first)
            || float.IsInfinity(first)
            || float.IsNaN(second)
            || float.IsInfinity(second)
        )
        {
            return float.PositiveInfinity;
        }

        return Mathf.Abs(first - second);
    }

    private struct BoundaryResult
    {
        public long Comparisons;
        public long Mismatches;
        public float MaximumDifference;
        public string FirstError;
    }
}
