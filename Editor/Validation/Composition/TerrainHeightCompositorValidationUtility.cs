using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Stage 13A validation for the GPU compositor foundation.
 *
 * The real preview path remains identity-only. A separate transient
 * two-slice RFloat array proves that compute dispatch genuinely writes
 * the selected slice while leaving another slice untouched.
 *
 * Startup is deferred outside the WorldMeshes IMGUI event.
 */
public static class TerrainHeightCompositorValidationUtility
{
    private const int MaximumAsyncWaitCycles =
        30;

    private const int GpuValidationTextureSize =
        8;

    private const float GpuValidationSlice0Value =
        10f;

    private const float GpuValidationSlice1Value =
        20f;

    private const float GpuValidationDelta =
        5f;

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

    private static readonly List<Vector2Int>
        dirtyBatchTiles =
            new List<Vector2Int>();

    private static WorldSettings worldSettings;

    private static TerrainAuthoringData authoringData;

    private static bool validationRunning;

    private static bool validationScheduled;

    private static bool persistentBaselineCaptured;

    private static int authoringRevisionBefore;

    private static string committedSignatureBefore =
        "";

    private static string overallSignatureBefore =
        "";

    private static int dirtyTextureIdBefore;

    private static long dirtyFullBuildCountBefore;

    private static long dirtyBindingCountBefore;

    private static long dirtyIncrementalUpdatesBefore;

    private static long dirtyCompositorDispatchesBefore;

    private static RenderTexture validationTexture;

    private static Texture2D validationSeed0;

    private static Texture2D validationSeed1;

    private static TerrainHeightCompositor validationCompositor;

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

    public static bool IsScheduled =>
        validationScheduled;

    public static void RequestValidation()
    {
        if (
            validationRunning
            ||
            validationScheduled
        )
        {
            return;
        }

        validationScheduled =
            true;

        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall +=
            RunScheduledValidation;
    }

    public static void ValidateGpuCompositorFoundation()
    {
        RequestValidation();
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

        dirtyBatchTiles.Clear();

        worldSettings =
            null;

        authoringData =
            null;

        try
        {
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
                "Height Preview is Ready, the committed authoring " +
                "heightfield is current, compute shaders and async " +
                "GPU readback are supported, and no dirty tiles are " +
                "already pending."
            );

            CapturePersistentBaseline();

            RunCompositorPreparationValidation();

            RunSliceAddressingValidation();

            RunAbsoluteWorldAddressingValidation();

            StartTransientGpuWriteValidation();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );

            FinishValidation();
        }
    }

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

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support compute " +
                "shaders.";

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

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            errorMessage =
                "Async GPU readback is not supported. Stage 13A can " +
                "dispatch compute work, but validation needs readback " +
                "to prove selected-slice writes.";

            return false;
        }

        if (!TerrainAuthoringPreviewService.Enabled)
        {
            errorMessage =
                "Height Preview is disabled.";

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
                "The Height Preview cache is not Ready.";

            return false;
        }

        if (
            TerrainAuthoringPreviewService
                .PendingDirtyTileCount !=
            0
        )
        {
            errorMessage =
                "The preview already has pending dirty tiles.";

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
                "The committed authoring heightfield is not Current.";

            return false;
        }

        return true;
    }

    private static void RunCompositorPreparationValidation()
    {
        bool prepared =
            TerrainAuthoringPreviewService
                .TryPrepareHeightCompositor(
                    out string prepareError
                );

        AddResult(
            "Composition compute shader and kernels",
            prepared
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            prepared
                ? "Loaded TerrainHeightComposition.compute and cached " +
                    "IdentityComposite + ValidationAddConstant kernels."
                : prepareError
        );

        bool randomWrite =
            TerrainAuthoringPreviewService
                .DiagnosticCacheRandomWriteEnabled;

        AddResult(
            "Preview cache random-write support",
            randomWrite
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            randomWrite
                ? "The existing preview RFloat texture array was " +
                    "created with enableRandomWrite."
                : "The existing preview cache is not random-write " +
                    "enabled."
        );
    }

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

        int validMappings =
            0;

        bool allMappingsCorrect =
            expectedSliceCount > 0;

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

                bool mapped =
                    TerrainAuthoringPreviewService
                        .TryGetSliceIndex(
                            tileX,
                            tileZ,
                            out int actualSlice
                        );

                Vector2Int reversedTile =
                    Vector2Int.zero;

                bool reversed =
                    mapped
                    &&
                    TerrainAuthoringPreviewService
                        .TryGetTileCoordinate(
                            actualSlice,
                            out reversedTile
                        );

                if (
                    !mapped
                    ||
                    !reversed
                    ||
                    actualSlice !=
                    expectedSlice
                    ||
                    reversedTile.x !=
                    tileX
                    ||
                    reversedTile.y !=
                    tileZ
                )
                {
                    allMappingsCorrect =
                        false;

                    continue;
                }

                validMappings++;
            }
        }

        AddResult(
            "Every preview tile maps to its expected cache slice",
            allMappingsCorrect
            &&
            validMappings ==
                expectedSliceCount
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Validated {validMappings} / {expectedSliceCount} " +
            "tile-to-slice mappings and reverse lookups."
        );

        bool rejectsOutside =
            !TerrainAuthoringPreviewService
                .TryGetSliceIndex(
                    origin.x - 1,
                    origin.y,
                    out _
                )
            &&
            !TerrainAuthoringPreviewService
                .TryGetSliceIndex(
                    origin.x,
                    origin.y - 1,
                    out _
                )
            &&
            !TerrainAuthoringPreviewService
                .TryGetSliceIndex(
                    origin.x + width,
                    origin.y,
                    out _
                )
            &&
            !TerrainAuthoringPreviewService
                .TryGetSliceIndex(
                    origin.x,
                    origin.y + height,
                    out _
                );

        AddResult(
            "Out-of-cache slice addressing is rejected",
            rejectsOutside
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            rejectsOutside
                ? "Coordinates immediately outside all four cache " +
                    "edges do not resolve to slices."
                : "One or more out-of-cache coordinates resolved to a " +
                    "valid slice."
        );
    }

    private static void RunAbsoluteWorldAddressingValidation()
    {
        int samplesPerSide =
            TerrainAuthoringPreviewService
                .SamplesPerSide;

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

        float tileWorldSize =
            worldSettings.HeightTileWorldSize;

        float expectedTileWorldSize =
            (samplesPerSide - 1) *
            sampleSpacing;

        bool spanMatches =
            Approximately(
                expectedTileWorldSize,
                tileWorldSize
            );

        AddResult(
            "Height-tile sample span matches tile world size",
            spanMatches
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Expected span {expectedTileWorldSize:R}; " +
            $"tile size {tileWorldSize:R}."
        );

        int sampleMid =
            Mathf.Clamp(
                samplesPerSide /
                    2,
                0,
                samplesPerSide -
                    1
            );

        if (
            TerrainAuthoringPreviewService.CacheWidth >=
            2
        )
        {
            Vector2Int origin =
                TerrainAuthoringPreviewService
                    .CacheOriginTile;

            bool leftOk =
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        origin,
                        samplesPerSide - 1,
                        sampleMid,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out Vector2 leftWorld,
                        out string leftError
                    );

            bool rightOk =
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        new Vector2Int(
                            origin.x + 1,
                            origin.y
                        ),
                        0,
                        sampleMid,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out Vector2 rightWorld,
                        out string rightError
                    );

            bool matches =
                leftOk
                &&
                rightOk
                &&
                Approximately(
                    leftWorld.x,
                    rightWorld.x
                )
                &&
                Approximately(
                    leftWorld.y,
                    rightWorld.y
                );

            AddResult(
                "Shared X-border samples use identical world XZ",
                matches
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                matches
                    ? $"Both samples resolved to " +
                        $"({leftWorld.x:R}, {leftWorld.y:R})."
                    : leftError + "\n" + rightError
            );
        }
        else
        {
            AddResult(
                "Shared X-border samples use identical world XZ",
                ValidationOutcome.Blocked,
                "The cache is only one tile wide."
            );
        }

        if (
            TerrainAuthoringPreviewService.CacheHeight >=
            2
        )
        {
            Vector2Int origin =
                TerrainAuthoringPreviewService
                    .CacheOriginTile;

            bool bottomOk =
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        origin,
                        sampleMid,
                        samplesPerSide - 1,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out Vector2 bottomWorld,
                        out string bottomError
                    );

            bool topOk =
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        new Vector2Int(
                            origin.x,
                            origin.y + 1
                        ),
                        sampleMid,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out Vector2 topWorld,
                        out string topError
                    );

            bool matches =
                bottomOk
                &&
                topOk
                &&
                Approximately(
                    bottomWorld.x,
                    topWorld.x
                )
                &&
                Approximately(
                    bottomWorld.y,
                    topWorld.y
                );

            AddResult(
                "Shared Z-border samples use identical world XZ",
                matches
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                matches
                    ? $"Both samples resolved to " +
                        $"({bottomWorld.x:R}, {bottomWorld.y:R})."
                    : bottomError + "\n" + topError
            );
        }
        else
        {
            AddResult(
                "Shared Z-border samples use identical world XZ",
                ValidationOutcome.Blocked,
                "The cache is only one tile tall."
            );
        }

        if (
            TerrainAuthoringPreviewService.CacheWidth >=
            2
            &&
            TerrainAuthoringPreviewService.CacheHeight >=
            2
        )
        {
            Vector2Int origin =
                TerrainAuthoringPreviewService
                    .CacheOriginTile;

            Vector2[] cornerWorld =
                new Vector2[4];

            bool ok =
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        origin,
                        samplesPerSide - 1,
                        samplesPerSide - 1,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out cornerWorld[0],
                        out _
                    )
                &&
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        new Vector2Int(
                            origin.x + 1,
                            origin.y
                        ),
                        0,
                        samplesPerSide - 1,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out cornerWorld[1],
                        out _
                    )
                &&
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        new Vector2Int(
                            origin.x,
                            origin.y + 1
                        ),
                        samplesPerSide - 1,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out cornerWorld[2],
                        out _
                    )
                &&
                TerrainHeightCompositor
                    .TryCalculateSampleWorldXZ(
                        new Vector2Int(
                            origin.x + 1,
                            origin.y + 1
                        ),
                        0,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        out cornerWorld[3],
                        out _
                    );

            for (
                int index = 1;
                index < cornerWorld.Length;
                index++
            )
            {
                ok &=
                    Approximately(
                        cornerWorld[0].x,
                        cornerWorld[index].x
                    )
                    &&
                    Approximately(
                        cornerWorld[0].y,
                        cornerWorld[index].y
                    );
            }

            AddResult(
                "Four-tile corner uses one identical world XZ sample",
                ok
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                ok
                    ? $"All four samples resolved to " +
                        $"({cornerWorld[0].x:R}, {cornerWorld[0].y:R})."
                    : "The duplicated corner samples did not match."
            );
        }
        else
        {
            AddResult(
                "Four-tile corner uses one identical world XZ sample",
                ValidationOutcome.Blocked,
                "A 2 x 2 cache region is required."
            );
        }
    }

    private static void StartTransientGpuWriteValidation()
    {
        CleanupGpuValidationResources();

        validationCompositor =
            new TerrainHeightCompositor();

        validationTexture =
            new RenderTexture(
                GpuValidationTextureSize,
                GpuValidationTextureSize,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );

        validationTexture.name =
            "WorldMeshes Stage 13A Validation Height Array";

        validationTexture.dimension =
            TextureDimension.Tex2DArray;

        validationTexture.volumeDepth =
            2;

        validationTexture.enableRandomWrite =
            true;

        validationTexture.useMipMap =
            false;

        validationTexture.autoGenerateMips =
            false;

        validationTexture.wrapMode =
            TextureWrapMode.Clamp;

        validationTexture.filterMode =
            FilterMode.Point;

        validationTexture.hideFlags =
            HideFlags.HideAndDontSave;

        validationTexture.Create();

        if (
            !validationTexture.IsCreated()
        )
        {
            AddResult(
                "Transient GPU validation texture",
                ValidationOutcome.Fail,
                "Could not create the temporary two-slice RFloat " +
                "texture array."
            );

            CleanupGpuValidationResources();

            StartDirtyPipelineValidation();

            return;
        }

        validationSeed0 =
            CreateConstantRFloatTexture(
                GpuValidationTextureSize,
                GpuValidationSlice0Value,
                "Stage13A Seed Slice 0"
            );

        validationSeed1 =
            CreateConstantRFloatTexture(
                GpuValidationTextureSize,
                GpuValidationSlice1Value,
                "Stage13A Seed Slice 1"
            );

        Graphics.CopyTexture(
            validationSeed0,
            0,
            0,
            validationTexture,
            0,
            0
        );

        Graphics.CopyTexture(
            validationSeed1,
            0,
            0,
            validationTexture,
            1,
            0
        );

        string previewSignatureBeforeFailureProbe =
            TerrainAuthoringPreviewService
                .SourceOverallAuthoringSignature;

        bool invalidRejected =
            !validationCompositor
                .TryComposeTile(
                    validationTexture,
                    Vector2Int.zero,
                    -1,
                    GpuValidationTextureSize,
                    1f,
                    GpuValidationTextureSize -
                        1,
                    new Vector2(
                        GpuValidationTextureSize -
                            1,
                        GpuValidationTextureSize -
                            1
                    ),
                    out string invalidError
                );

        string previewSignatureAfterFailureProbe =
            TerrainAuthoringPreviewService
                .SourceOverallAuthoringSignature;

        AddResult(
            "Invalid compositor target is rejected safely",
            invalidRejected
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            invalidRejected
                ? "Invalid slice rejected: " +
                    invalidError
                : "The compositor accepted an invalid target slice."
        );

        bool signatureStable =
            previewSignatureBeforeFailureProbe ==
            previewSignatureAfterFailureProbe;

        AddResult(
            "Compositor failure does not acknowledge preview state",
            signatureStable
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            signatureStable
                ? "The failed transient dispatch did not touch the " +
                    "real preview's overall source signature."
                : "The real preview source signature changed."
        );

        if (
            !validationCompositor
                .TryValidationAddConstant(
                    validationTexture,
                    1,
                    GpuValidationTextureSize,
                    GpuValidationDelta,
                    out string validationDispatchError
                )
        )
        {
            AddResult(
                "Selected-slice GPU write",
                ValidationOutcome.Fail,
                validationDispatchError
            );

            CleanupGpuValidationResources();

            StartDirtyPipelineValidation();

            return;
        }

        AsyncGPUReadback.Request(
            validationTexture,
            0,
            OnGpuValidationReadback
        );
    }

    private static void OnGpuValidationReadback(
    AsyncGPUReadbackRequest request
)
{
    bool continueValidation =
        validationRunning;

    try
    {
        if (!validationRunning)
        {
            return;
        }

        if (request.hasError)
        {
            AddResult(
                "Selected-slice GPU write",
                ValidationOutcome.Fail,
                "Async GPU readback reported an error."
            );

            return;
        }

        int samplesPerSlice =
            GpuValidationTextureSize *
            GpuValidationTextureSize;

        if (
            request.layerCount <
            2
        )
        {
            AddResult(
                "Selected-slice GPU write",
                ValidationOutcome.Fail,
                "The GPU readback request did not expose both expected " +
                $"texture-array layers. Layer count: {request.layerCount}."
            );

            return;
        }

        var slice0Data =
            request.GetData<float>(
                0
            );

        var slice1Data =
            request.GetData<float>(
                1
            );

        if (
            slice0Data.Length <
                samplesPerSlice
            ||
            slice1Data.Length <
                samplesPerSlice
        )
        {
            AddResult(
                "Selected-slice GPU write",
                ValidationOutcome.Fail,
                $"Expected {samplesPerSlice} float samples per layer. " +
                $"Actual layer 0: {slice0Data.Length}, " +
                $"layer 1: {slice1Data.Length}."
            );

            return;
        }

        bool slice0Correct =
            true;

        bool slice1Correct =
            true;

        float expectedSlice1 =
            GpuValidationSlice1Value +
            GpuValidationDelta;

        for (
            int index = 0;
            index < samplesPerSlice;
            index++
        )
        {
            slice0Correct &=
                Approximately(
                    slice0Data[index],
                    GpuValidationSlice0Value
                );

            slice1Correct &=
                Approximately(
                    slice1Data[index],
                    expectedSlice1
                );
        }

        bool passed =
            slice0Correct
            &&
            slice1Correct;

        AddResult(
            "Selected-slice GPU write",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "ValidationAddConstant changed only slice 1 " +
                    $"from {GpuValidationSlice1Value:R} to " +
                    $"{expectedSlice1:R}; slice 0 stayed " +
                    $"{GpuValidationSlice0Value:R}."
                : "The GPU write changed the wrong slice or " +
                    "produced unexpected values."
        );
    }
    catch (Exception exception)
    {
        AddResult(
            "Selected-slice GPU write",
            ValidationOutcome.Fail,
            exception.ToString()
        );
    }
    finally
    {
        CleanupGpuValidationResources();

        if (
            continueValidation
            &&
            validationRunning
        )
        {
            StartDirtyPipelineValidation();
        }
    }
}

    private static Texture2D CreateConstantRFloatTexture(
        int size,
        float value,
        string textureName
    )
    {
        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RFloat,
                false,
                true
            );

        texture.name =
            textureName;

        texture.hideFlags =
            HideFlags.HideAndDontSave;

        float[] pixels =
            new float[
                size *
                size
            ];

        for (
            int index = 0;
            index < pixels.Length;
            index++
        )
        {
            pixels[index] =
                value;
        }

        texture.SetPixelData(
            pixels,
            0
        );

        texture.Apply(
            false,
            false
        );

        return texture;
    }

    private static void StartDirtyPipelineValidation()
    {
        if (!validationRunning)
        {
            return;
        }

        if (
            !TryChooseDirtyBatchTiles(
                dirtyBatchTiles
            )
        )
        {
            AddResult(
                "Real dirty-pipeline compositor dispatch",
                ValidationOutcome.Blocked,
                "At least three valid preview slices are required."
            );

            FinishValidation();

            return;
        }

        dirtyTextureIdBefore =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        dirtyFullBuildCountBefore =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount;

        dirtyBindingCountBefore =
            TerrainAuthoringPreviewService
                .DiagnosticBindingApplyCount;

        dirtyIncrementalUpdatesBefore =
            TerrainAuthoringPreviewService
                .TotalIncrementalSliceUpdates;

        dirtyCompositorDispatchesBefore =
            TerrainAuthoringPreviewService
                .TotalCompositeDispatchTileCount;

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

        Vector2Int cacheOrigin =
            TerrainAuthoringPreviewService
                .CacheOriginTile;

        TerrainAuthoringPreviewService
            .NotifyCompositeTileChanged(
                new Vector2Int(
                    cacheOrigin.x - 1,
                    cacheOrigin.y - 1
                )
            );

        int queuedCount =
            TerrainAuthoringPreviewService
                .PendingDirtyTileCount;

        AddResult(
            "Dirty notification batching before refresh",
            queuedCount ==
                dirtyBatchTiles.Count +
                1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            queuedCount ==
                dirtyBatchTiles.Count +
                1
                ? "Repeated valid notifications collapsed to three " +
                    "valid tiles plus one deliberate conservative " +
                    "out-of-cache coordinate."
                : $"Expected 4 queued coordinates; got {queuedCount}."
        );

        QueueDelayCall(
            () =>
                WaitForDirtyPipelineCompletion(
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
            TerrainAuthoringPreviewService
                .CacheWidth;

        int height =
            TerrainAuthoringPreviewService
                .CacheHeight;

        Vector2Int origin =
            TerrainAuthoringPreviewService
                .CacheOriginTile;

        if (
            width *
            height <
            3
        )
        {
            return false;
        }

        for (
            int z = 0;
            z < height
            &&
            output.Count < 3;
            z++
        )
        {
            for (
                int x = 0;
                x < width
                &&
                output.Count < 3;
                x++
            )
            {
                output.Add(
                    new Vector2Int(
                        origin.x +
                            x,
                        origin.y +
                            z
                    )
                );
            }
        }

        return
            output.Count ==
            3;
    }

    private static void WaitForDirtyPipelineCompletion(
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
                "Real dirty-pipeline compositor dispatch",
                ValidationOutcome.Blocked,
                "Play Mode began during validation."
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
                AddDirtyPipelineTimeout();
                FinishValidation();
                return;
            }

            QueueDelayCall(
                () =>
                    WaitForDirtyPipelineCompletion(
                        waitCycle + 1
                    )
            );

            return;
        }

        long compositorDelta =
            TerrainAuthoringPreviewService
                .TotalCompositeDispatchTileCount
            -
            dirtyCompositorDispatchesBefore;

        bool processingEvidence =
            compositorDelta >=
                dirtyBatchTiles.Count
            ||
            TerrainAuthoringPreviewService.Status ==
                TerrainAuthoringPreviewStatus.Error;

        if (
            TerrainAuthoringPreviewService
                .PendingDirtyTileCount ==
                0
            &&
            processingEvidence
        )
        {
            VerifyDirtyPipelineResults();

            FinishValidation();

            return;
        }

        if (
            waitCycle >=
            MaximumAsyncWaitCycles
        )
        {
            AddDirtyPipelineTimeout();
            FinishValidation();
            return;
        }

        QueueDelayCall(
            () =>
                WaitForDirtyPipelineCompletion(
                    waitCycle + 1
                )
        );
    }

    private static void VerifyDirtyPipelineResults()
    {
        int expectedValidTiles =
            dirtyBatchTiles.Count;

        int lastDispatchCount =
            TerrainAuthoringPreviewService
                .LastCompositeDispatchTileCount;

        long dispatchDelta =
            TerrainAuthoringPreviewService
                .TotalCompositeDispatchTileCount
            -
            dirtyCompositorDispatchesBefore;

        bool dispatchCorrect =
            lastDispatchCount ==
                expectedValidTiles
            &&
            dispatchDelta ==
                expectedValidTiles;

        AddResult(
            "Real dirty-pipeline compositor dispatch",
            dispatchCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            dispatchCorrect
                ? $"Exactly {expectedValidTiles} valid dirty slices " +
                    "were dispatched; the out-of-cache coordinate " +
                    "was ignored."
                : $"Expected {expectedValidTiles}; last=" +
                    $"{lastDispatchCount}, delta={dispatchDelta}."
        );

        long incrementalDelta =
            TerrainAuthoringPreviewService
                .TotalIncrementalSliceUpdates
            -
            dirtyIncrementalUpdatesBefore;

        bool resetCorrect =
            TerrainAuthoringPreviewService
                .LastIncrementalSliceCount ==
                expectedValidTiles
            &&
            incrementalDelta ==
                expectedValidTiles;

        AddResult(
            "Committed reset count matches compositor dispatch count",
            resetCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Last reset=" +
            $"{TerrainAuthoringPreviewService.LastIncrementalSliceCount}; " +
            $"delta={incrementalDelta}."
        );

        int textureIdAfter =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        AddResult(
            "Cache Texture ID remains stable",
            textureIdAfter ==
                dirtyTextureIdBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Before/after: {dirtyTextureIdBefore} / {textureIdAfter}."
        );

        long fullBuildAfter =
            TerrainAuthoringPreviewService
                .FullCommittedBuildCount;

        AddResult(
            "Dirty GPU composition avoids full cache rebuild",
            fullBuildAfter ==
                dirtyFullBuildCountBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Before/after: {dirtyFullBuildCountBefore} / " +
            $"{fullBuildAfter}."
        );

        long bindingAfter =
            TerrainAuthoringPreviewService
                .DiagnosticBindingApplyCount;

        AddResult(
            "Dirty GPU composition avoids renderer rebind",
            bindingAfter ==
                dirtyBindingCountBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Before/after: {dirtyBindingCountBefore} / " +
            $"{bindingAfter}."
        );

        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        AddResult(
            "Committed heightfield signature remains unchanged",
            committedAfter ==
                committedSignatureBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            committedAfter ==
                committedSignatureBefore
                ? "Identity GPU composition did not change committed " +
                    "heightfield identity."
                : "Committed heightfield signature changed."
        );

        string overallCurrent =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        bool readyAndCurrent =
            TerrainAuthoringPreviewService.Status ==
                TerrainAuthoringPreviewStatus.Ready
            &&
            TerrainAuthoringPreviewService.CacheReady
            &&
            TerrainAuthoringPreviewService
                .SourceOverallAuthoringSignature
            ==
            overallCurrent;

        AddResult(
            "Preview is Ready and signature-current after composition",
            readyAndCurrent
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            readyAndCurrent
                ? "Overall preview state was acknowledged only after " +
                    "the reset + GPU composition transaction completed."
                : "Preview status/signature is not current."
        );
    }

    private static void AddDirtyPipelineTimeout()
    {
        AddResult(
            "Real dirty-pipeline compositor dispatch",
            ValidationOutcome.Fail,
            "Scheduled composition did not complete within " +
            $"{MaximumAsyncWaitCycles} delay-call cycles.\n" +
            $"Status: {TerrainAuthoringPreviewService.StatusLabel}\n" +
            $"Pending: " +
            $"{TerrainAuthoringPreviewService.PendingDirtyTileCount}\n" +
            $"Last Dispatch: " +
            $"{TerrainAuthoringPreviewService.LastCompositeDispatchTileCount}"
        );
    }

    private static void CapturePersistentBaseline()
    {
        persistentBaselineCaptured =
            worldSettings != null
            &&
            authoringData != null;

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

    private static void AddPersistentStateSafetyResult()
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
                ? "Validation did not mutate TerrainAuthoringData, " +
                    "authoringRevision, or authoring signatures."
                : "Persistent authoring state changed."
        );
    }

    private static void CleanupGpuValidationResources()
    {
        if (validationTexture != null)
        {
            validationTexture.Release();

            UnityEngine.Object
                .DestroyImmediate(
                    validationTexture
                );

            validationTexture =
                null;
        }

        if (validationSeed0 != null)
        {
            UnityEngine.Object
                .DestroyImmediate(
                    validationSeed0
                );

            validationSeed0 =
                null;
        }

        if (validationSeed1 != null)
        {
            UnityEngine.Object
                .DestroyImmediate(
                    validationSeed1
                );

            validationSeed1 =
                null;
        }

        validationCompositor =
            null;
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
        if (!validationRunning)
        {
            return;
        }

        EditorApplication.delayCall -=
            RunScheduledValidation;

        validationScheduled =
            false;

        CleanupGpuValidationResources();

        AddPersistentStateSafetyResult();

        validationRunning =
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
            "WorldMeshes Stage 13A GPU Compositor Foundation Validation"
        );

        builder.AppendLine(
            "=========================================================="
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

        if (
            failCount == 0
            &&
            blockedCount == 0
        )
        {
            builder.AppendLine(
                "Stage 13A GPU compositor foundation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failCount > 0)
        {
            builder.AppendLine(
                "Stage 13A GPU compositor foundation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Stage 13A GPU compositor foundation: BLOCKED"
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }

        results.Clear();

        dirtyBatchTiles.Clear();

        worldSettings =
            null;

        authoringData =
            null;

        persistentBaselineCaptured =
            false;

        committedSignatureBefore =
            "";

        overallSignatureBefore =
            "";
    }

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
