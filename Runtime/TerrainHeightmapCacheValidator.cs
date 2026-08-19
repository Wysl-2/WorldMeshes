using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainHeightmapStreamer))]
public class TerrainHeightmapCacheValidator :
    MonoBehaviour
{
    // =====================================================
    // VALIDATION SETTINGS
    // =====================================================

    [Header("Validation")]

    /*
     * Both the source height tiles and GPU cache use
     * TextureFormat.RFloat, so an exact copy should normally
     * produce a difference of zero.
     */
    [SerializeField]
    [Min(0f)]
    private float cacheValidationTolerance =
        0f;

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private TerrainHeightmapStreamer streamer;

    private Coroutine validationRoutine;

    private bool cacheInspectionAcquired;

    private bool hasValidationResult;

    private bool lastValidationPassed;

    private Texture2DArray lastValidatedCache;

    private Vector2Int lastValidatedOrigin;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public bool IsValidating
    {
        get
        {
            return validationRoutine != null;
        }
    }

    public bool HasValidatedCurrentCache
    {
        get
        {
            EnsureStreamerReference();

            return
                hasValidationResult
                &&
                streamer != null
                &&
                streamer.HeightCache ==
                    lastValidatedCache
                &&
                streamer.CacheOriginTile ==
                    lastValidatedOrigin;
        }
    }

    public bool LastValidationPassed
    {
        get
        {
            return
                HasValidatedCurrentCache
                &&
                lastValidationPassed;
        }
    }

    // =====================================================
    // ENABLE
    // =====================================================

    private void OnEnable()
    {
        EnsureStreamerReference();
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (validationRoutine != null)
        {
            StopCoroutine(
                validationRoutine
            );

            validationRoutine =
                null;
        }

        ReleaseCacheInspection();
    }

    // =====================================================
    // BEGIN VALIDATION
    // =====================================================

    [ContextMenu("Validate Current Height Cache")]
    public void BeginValidation()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "Height-cache validation can only run " +
                "in Play Mode.",
                this
            );

            return;
        }

        if (validationRoutine != null)
        {
            Debug.LogWarning(
                "Height-cache validation is already running.",
                this
            );

            return;
        }

        EnsureStreamerReference();

        if (streamer == null)
        {
            Debug.LogError(
                "TerrainHeightmapCacheValidator requires " +
                "TerrainHeightmapStreamer on the same " +
                "GameObject.",
                this
            );

            return;
        }

        if (
            !streamer.TryBeginCacheInspection(
                out string reason
            )
        )
        {
            Debug.LogWarning(
                "Cannot validate the current height cache.\n\n" +
                reason,
                this
            );

            return;
        }

        cacheInspectionAcquired =
            true;

        Texture2DArray cache =
            streamer.HeightCache;

        Vector2Int cacheOrigin =
            streamer.CacheOriginTile;

        int cacheWidth =
            streamer.CacheWidth;

        int cacheHeight =
            streamer.CacheHeight;

        hasValidationResult =
            false;

        lastValidationPassed =
            false;

        lastValidatedCache =
            null;

        validationRoutine =
            StartCoroutine(
                ValidateHeightCacheRoutine(
                    cache,
                    cacheOrigin,
                    cacheWidth,
                    cacheHeight
                )
            );
    }

    // =====================================================
    // VALIDATE HEIGHT CACHE
    // =====================================================

    private IEnumerator ValidateHeightCacheRoutine(
        Texture2DArray cache,
        Vector2Int cacheOrigin,
        int cacheWidth,
        int cacheHeight
    )
    {
        if (
            cache == null
            ||
            cacheWidth <= 0
            ||
            cacheHeight <= 0
        )
        {
            FailValidation(
                "The captured height cache state is invalid."
            );

            yield break;
        }

        int samplesPerSide =
            cache.width;

        int samplesPerTile =
            samplesPerSide *
            samplesPerSide;

        int expectedTileCount =
            cacheWidth *
            cacheHeight;

        if (
            samplesPerSide <= 1
            ||
            cache.height != samplesPerSide
            ||
            cache.depth != expectedTileCount
        )
        {
            FailValidation(
                "The captured Texture2DArray dimensions do not " +
                "match the streamer's cache layout."
            );

            yield break;
        }

        // =====================================================
        // GPU READBACK
        // =====================================================

        AsyncGPUReadbackRequest readback;

        try
        {
            readback =
                AsyncGPUReadback.Request(
                    cache,
                    0,
                    TextureFormat.RFloat,
                    null
                );
        }
        catch (
            System.Exception exception
        )
        {
            FailValidation(
                "Could not begin GPU readback.\n\n" +
                exception.Message
            );

            yield break;
        }

        while (!readback.done)
        {
            yield return null;
        }

        if (readback.hasError)
        {
            FailValidation(
                "GPU readback of the terrain height cache failed."
            );

            yield break;
        }

        // =====================================================
        // VALIDATION STATISTICS
        // =====================================================

        int tilesValidated =
            0;

        int slicesValidated =
            0;

        int missingTiles =
            0;

        int sliceMappingMismatches =
            0;

        int invalidSourceTextures =
            0;

        long samplesCompared =
            0L;

        long sampleMismatches =
            0L;

        float maximumHeightDifference =
            0f;

        string firstSampleMismatch =
            null;

        string firstSliceMismatch =
            null;

        // =====================================================
        // VALIDATE EVERY TILE / SLICE
        // =====================================================

        for (
            int localZ = 0;
            localZ < cacheHeight;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < cacheWidth;
                localX++
            )
            {
                Vector2Int tileCoordinate =
                    new Vector2Int(
                        cacheOrigin.x +
                        localX,

                        cacheOrigin.y +
                        localZ
                    );

                int expectedSlice =
                    localX
                    +
                    localZ *
                    cacheWidth;

                // -----------------------------------------
                // Loaded source tile
                // -----------------------------------------

                if (
                    !streamer.TryGetLoadedTileForInspection(
                        tileCoordinate,
                        out Texture2D sourceTexture,
                        out int recordedSlice
                    )
                    ||
                    sourceTexture == null
                )
                {
                    missingTiles++;

                    continue;
                }

                tilesValidated++;

                // -----------------------------------------
                // Recorded slice mapping
                // -----------------------------------------

                if (
                    recordedSlice !=
                    expectedSlice
                )
                {
                    sliceMappingMismatches++;

                    if (firstSliceMismatch == null)
                    {
                        firstSliceMismatch =
                            $"Tile ({tileCoordinate.x}, " +
                            $"{tileCoordinate.y})\n" +
                            $"Expected Slice: {expectedSlice}\n" +
                            $"Recorded Slice: {recordedSlice}";
                    }
                }

                // -----------------------------------------
                // Source texture
                // -----------------------------------------

                if (
                    sourceTexture.width !=
                        samplesPerSide
                    ||
                    sourceTexture.height !=
                        samplesPerSide
                    ||
                    sourceTexture.format !=
                        TextureFormat.RFloat
                    ||
                    !sourceTexture.isReadable
                )
                {
                    invalidSourceTextures++;

                    continue;
                }

                NativeArray<float> sourceData;

                try
                {
                    sourceData =
                        sourceTexture
                            .GetPixelData<float>(
                                0
                            );
                }
                catch (
                    System.Exception exception
                )
                {
                    invalidSourceTextures++;

                    Debug.LogError(
                        "Could not read source heightmap tile.\n\n" +
                        $"Tile: ({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n\n" +
                        exception.Message,
                        this
                    );

                    continue;
                }

                NativeArray<float> cacheData;

                try
                {
                    cacheData =
                        readback
                            .GetData<float>(
                                expectedSlice
                            );
                }
                catch (
                    System.Exception exception
                )
                {
                    FailValidation(
                        "Could not access a Texture2DArray " +
                        "readback layer.\n\n" +
                        $"Tile: ({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n" +
                        $"Slice: {expectedSlice}\n\n" +
                        exception.Message
                    );

                    yield break;
                }

                if (
                    sourceData.Length !=
                        samplesPerTile
                    ||
                    cacheData.Length !=
                        samplesPerTile
                )
                {
                    invalidSourceTextures++;

                    Debug.LogError(
                        "Height-cache validation encountered " +
                        "an unexpected sample count.\n\n" +
                        $"Tile: ({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n" +
                        $"Expected: {samplesPerTile:N0}\n" +
                        $"Source: {sourceData.Length:N0}\n" +
                        $"Cache: {cacheData.Length:N0}",
                        this
                    );

                    continue;
                }

                slicesValidated++;

                // =============================================
                // COMPARE EVERY FLOAT
                // =============================================

                for (
                    int sampleIndex = 0;
                    sampleIndex < samplesPerTile;
                    sampleIndex++
                )
                {
                    float sourceHeight =
                        sourceData[
                            sampleIndex
                        ];

                    float cacheHeightValue =
                        cacheData[
                            sampleIndex
                        ];

                    samplesCompared++;

                    bool invalidValue =
                        float.IsNaN(
                            sourceHeight
                        )
                        ||
                        float.IsInfinity(
                            sourceHeight
                        )
                        ||
                        float.IsNaN(
                            cacheHeightValue
                        )
                        ||
                        float.IsInfinity(
                            cacheHeightValue
                        );

                    float difference =
                        invalidValue
                            ? float.PositiveInfinity
                            : Mathf.Abs(
                                sourceHeight -
                                cacheHeightValue
                            );

                    if (
                        !float.IsInfinity(
                            difference
                        )
                    )
                    {
                        maximumHeightDifference =
                            Mathf.Max(
                                maximumHeightDifference,
                                difference
                            );
                    }

                    if (
                        invalidValue
                        ||
                        difference >
                            cacheValidationTolerance
                    )
                    {
                        sampleMismatches++;

                        if (firstSampleMismatch == null)
                        {
                            int sampleX =
                                sampleIndex %
                                samplesPerSide;

                            int sampleZ =
                                sampleIndex /
                                samplesPerSide;

                            firstSampleMismatch =
                                $"Tile: ({tileCoordinate.x}, " +
                                $"{tileCoordinate.y})\n" +
                                $"Slice: {expectedSlice}\n" +
                                $"Sample: ({sampleX}, {sampleZ})\n" +
                                $"Source Height: {sourceHeight:R}\n" +
                                $"Cache Height: {cacheHeightValue:R}\n" +
                                $"Difference: {difference:R}";
                        }
                    }
                }
            }
        }

        // =====================================================
        // VALIDATE CACHE TILE BOUNDARIES
        // =====================================================

        long boundarySamplesCompared =
            0L;

        long boundaryMismatches =
            0L;

        float maximumBoundaryDifference =
            0f;

        string firstBoundaryMismatch =
            null;

        ValidateCacheBoundaries(
            readback,
            cacheOrigin,
            cacheWidth,
            cacheHeight,
            samplesPerSide,

            ref boundarySamplesCompared,
            ref boundaryMismatches,
            ref maximumBoundaryDifference,
            ref firstBoundaryMismatch
        );

        // =====================================================
        // RESULT
        // =====================================================

        bool passed =
            tilesValidated ==
                expectedTileCount
            &&
            slicesValidated ==
                expectedTileCount
            &&
            missingTiles == 0
            &&
            invalidSourceTextures == 0
            &&
            sliceMappingMismatches == 0
            &&
            sampleMismatches == 0
            &&
            boundaryMismatches == 0;

        hasValidationResult =
            true;

        lastValidationPassed =
            passed;

        lastValidatedCache =
            cache;

        lastValidatedOrigin =
            cacheOrigin;

        if (passed)
        {
            Debug.Log(
                "Terrain height cache validation passed.\n\n" +
                $"Cache Origin Tile: " +
                $"({cacheOrigin.x}, {cacheOrigin.y})\n" +
                $"Cache Tile Grid: " +
                $"{cacheWidth} x {cacheHeight}\n\n" +
                $"Tiles Validated: {tilesValidated:N0}\n" +
                $"Slices Validated: {slicesValidated:N0}\n\n" +
                $"Samples Compared: {samplesCompared:N0}\n" +
                $"Sample Mismatches: {sampleMismatches:N0}\n\n" +
                $"Slice Mapping Mismatches: " +
                $"{sliceMappingMismatches:N0}\n\n" +
                $"Boundary Samples Compared: " +
                $"{boundarySamplesCompared:N0}\n" +
                $"Boundary Mismatches: " +
                $"{boundaryMismatches:N0}\n\n" +
                $"Maximum Height Difference: " +
                $"{maximumHeightDifference:R}\n" +
                $"Maximum Boundary Difference: " +
                $"{maximumBoundaryDifference:R}",
                this
            );
        }
        else
        {
            string details =
                "";

            if (firstSliceMismatch != null)
            {
                details +=
                    "\n\nFirst Slice Mapping Mismatch:\n" +
                    firstSliceMismatch;
            }

            if (firstSampleMismatch != null)
            {
                details +=
                    "\n\nFirst Height Mismatch:\n" +
                    firstSampleMismatch;
            }

            if (firstBoundaryMismatch != null)
            {
                details +=
                    "\n\nFirst Boundary Mismatch:\n" +
                    firstBoundaryMismatch;
            }

            Debug.LogError(
                "Terrain height cache validation FAILED.\n\n" +
                $"Expected Tiles: {expectedTileCount:N0}\n" +
                $"Tiles Validated: {tilesValidated:N0}\n" +
                $"Slices Validated: {slicesValidated:N0}\n" +
                $"Missing Tiles: {missingTiles:N0}\n" +
                $"Invalid Source Textures: " +
                $"{invalidSourceTextures:N0}\n\n" +
                $"Samples Compared: {samplesCompared:N0}\n" +
                $"Sample Mismatches: {sampleMismatches:N0}\n\n" +
                $"Slice Mapping Mismatches: " +
                $"{sliceMappingMismatches:N0}\n\n" +
                $"Boundary Samples Compared: " +
                $"{boundarySamplesCompared:N0}\n" +
                $"Boundary Mismatches: " +
                $"{boundaryMismatches:N0}\n\n" +
                $"Maximum Height Difference: " +
                $"{maximumHeightDifference:R}\n" +
                $"Maximum Boundary Difference: " +
                $"{maximumBoundaryDifference:R}" +
                details,
                this
            );
        }

        validationRoutine =
            null;

        ReleaseCacheInspection();
    }

    // =====================================================
    // VALIDATE CACHE BOUNDARIES
    // =====================================================

    private void ValidateCacheBoundaries(
        AsyncGPUReadbackRequest readback,
        Vector2Int cacheOrigin,
        int cacheWidth,
        int cacheHeight,
        int samplesPerSide,

        ref long samplesCompared,
        ref long mismatches,
        ref float maximumDifference,
        ref string firstMismatch
    )
    {
        int maximumSample =
            samplesPerSide - 1;

        // =====================================================
        // X-AXIS NEIGHBOURS
        // =====================================================

        for (
            int localZ = 0;
            localZ < cacheHeight;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < cacheWidth - 1;
                localX++
            )
            {
                int leftSlice =
                    localX
                    +
                    localZ *
                    cacheWidth;

                int rightSlice =
                    localX + 1
                    +
                    localZ *
                    cacheWidth;

                NativeArray<float> leftData =
                    readback
                        .GetData<float>(
                            leftSlice
                        );

                NativeArray<float> rightData =
                    readback
                        .GetData<float>(
                            rightSlice
                        );

                for (
                    int sampleZ = 0;
                    sampleZ < samplesPerSide;
                    sampleZ++
                )
                {
                    int leftIndex =
                        maximumSample
                        +
                        sampleZ *
                        samplesPerSide;

                    int rightIndex =
                        sampleZ *
                        samplesPerSide;

                    float leftHeight =
                        leftData[
                            leftIndex
                        ];

                    float rightHeight =
                        rightData[
                            rightIndex
                        ];

                    float difference =
                        Mathf.Abs(
                            leftHeight -
                            rightHeight
                        );

                    samplesCompared++;

                    maximumDifference =
                        Mathf.Max(
                            maximumDifference,
                            difference
                        );

                    if (
                        difference >
                        cacheValidationTolerance
                    )
                    {
                        mismatches++;

                        if (firstMismatch == null)
                        {
                            Vector2Int leftTile =
                                new Vector2Int(
                                    cacheOrigin.x +
                                    localX,

                                    cacheOrigin.y +
                                    localZ
                                );

                            Vector2Int rightTile =
                                leftTile +
                                Vector2Int.right;

                            firstMismatch =
                                "X Boundary\n" +
                                $"Left Tile: ({leftTile.x}, " +
                                $"{leftTile.y})\n" +
                                $"Right Tile: ({rightTile.x}, " +
                                $"{rightTile.y})\n" +
                                $"Sample Z: {sampleZ}\n" +
                                $"Left Height: {leftHeight:R}\n" +
                                $"Right Height: {rightHeight:R}\n" +
                                $"Difference: {difference:R}";
                        }
                    }
                }
            }
        }

        // =====================================================
        // Z-AXIS NEIGHBOURS
        // =====================================================

        for (
            int localZ = 0;
            localZ < cacheHeight - 1;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < cacheWidth;
                localX++
            )
            {
                int lowerSlice =
                    localX
                    +
                    localZ *
                    cacheWidth;

                int upperSlice =
                    localX
                    +
                    (localZ + 1) *
                    cacheWidth;

                NativeArray<float> lowerData =
                    readback
                        .GetData<float>(
                            lowerSlice
                        );

                NativeArray<float> upperData =
                    readback
                        .GetData<float>(
                            upperSlice
                        );

                for (
                    int sampleX = 0;
                    sampleX < samplesPerSide;
                    sampleX++
                )
                {
                    int lowerIndex =
                        sampleX
                        +
                        maximumSample *
                        samplesPerSide;

                    int upperIndex =
                        sampleX;

                    float lowerHeight =
                        lowerData[
                            lowerIndex
                        ];

                    float upperHeight =
                        upperData[
                            upperIndex
                        ];

                    float difference =
                        Mathf.Abs(
                            lowerHeight -
                            upperHeight
                        );

                    samplesCompared++;

                    maximumDifference =
                        Mathf.Max(
                            maximumDifference,
                            difference
                        );

                    if (
                        difference >
                        cacheValidationTolerance
                    )
                    {
                        mismatches++;

                        if (firstMismatch == null)
                        {
                            Vector2Int lowerTile =
                                new Vector2Int(
                                    cacheOrigin.x +
                                    localX,

                                    cacheOrigin.y +
                                    localZ
                                );

                            Vector2Int upperTile =
                                lowerTile +
                                Vector2Int.up;

                            firstMismatch =
                                "Z Boundary\n" +
                                $"Lower Tile: ({lowerTile.x}, " +
                                $"{lowerTile.y})\n" +
                                $"Upper Tile: ({upperTile.x}, " +
                                $"{upperTile.y})\n" +
                                $"Sample X: {sampleX}\n" +
                                $"Lower Height: {lowerHeight:R}\n" +
                                $"Upper Height: {upperHeight:R}\n" +
                                $"Difference: {difference:R}";
                        }
                    }
                }
            }
        }
    }

    // =====================================================
    // FAIL VALIDATION
    // =====================================================

    private void FailValidation(
        string reason
    )
    {
        hasValidationResult =
            false;

        lastValidationPassed =
            false;

        lastValidatedCache =
            null;

        validationRoutine =
            null;

        ReleaseCacheInspection();

        Debug.LogError(
            "Terrain height cache validation FAILED.\n\n" +
            reason,
            this
        );
    }

    // =====================================================
    // RELEASE CACHE INSPECTION
    // =====================================================

    private void ReleaseCacheInspection()
    {
        if (
            !cacheInspectionAcquired
            ||
            streamer == null
        )
        {
            cacheInspectionAcquired =
                false;

            return;
        }

        streamer.EndCacheInspection();

        cacheInspectionAcquired =
            false;
    }

    // =====================================================
    // STREAMER REFERENCE
    // =====================================================

    private void EnsureStreamerReference()
    {
        if (streamer != null)
        {
            return;
        }

        streamer =
            GetComponent<TerrainHeightmapStreamer>();
    }
}
