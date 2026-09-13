using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainMaxMinBlendValidationUtility
{
    private sealed class Result
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    private static readonly List<Result>
        results =
            new List<Result>();

    private static readonly string ValidationRoot =
        WorldMeshesPaths.GeneratedValidation
        +
        "/MaxMinBlendModes";

    private static readonly string ValidationDataPath =
        ValidationRoot
        +
        "/TerrainAuthoringData.asset";

    private const float MathTolerance =
        0.0001f;

    private const float GpuTolerance =
        0.003f;

    private static bool validationScheduled;
    private static bool validationRunning;

    private static int lastPassedCount;
    private static int lastFailedCount;

    private static string lastSummary =
        "Not run.";

    private static WorldSettings worldSettings;
    private static TerrainAuthoringData mutationData;
    private static string mutationModifierId;

    private static TerrainHeightBlendMode undoExpectedBlendMode;
    private static TerrainHeightBlendMode redoExpectedBlendMode;
    private static int undoExpectedRevision;
    private static int redoExpectedRevision;
    private static string undoExpectedSignature;
    private static string redoExpectedSignature;

    public static bool IsScheduled =>
        validationScheduled;

    public static bool IsRunning =>
        validationRunning;

    public static int LastPassedCount =>
        lastPassedCount;

    public static int LastFailedCount =>
        lastFailedCount;

    public static string LastSummary =>
        lastSummary;

    public static void RequestValidation()
    {
        if (
            validationScheduled
            ||
            validationRunning
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

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -=
            RunScheduledValidation;

        if (
            !validationScheduled
            ||
            validationRunning
        )
        {
            return;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            EditorApplication.delayCall +=
                RunScheduledValidation;

            return;
        }

        validationScheduled =
            false;

        validationRunning =
            true;

        results.Clear();

        try
        {
            if (!RunSynchronousValidation())
            {
                Finish();
                return;
            }

            Undo.PerformUndo();
            ScheduleUndoVerification();
        }
        catch (Exception exception)
        {
            Add(
                "Unexpected validation exception",
                false,
                exception.ToString()
            );

            Finish();
        }
    }

    private static bool RunSynchronousValidation()
    {
        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "Run while the editor is idle in Edit Mode."
            );

            return false;
        }

        if (
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "Finish or cancel the active terrain modifier interaction first."
            );

            return false;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData realData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (
            worldSettings == null
            ||
            realData == null
            ||
            worldSettings.HeightTileWorldSize <= 0f
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "WorldSettings or TerrainAuthoringData could not be loaded with a valid height-tile layout."
            );

            return false;
        }

        Add(
            "Validation prerequisites",
            true,
            "Current WorldSettings and TerrainAuthoringData are available."
        );

        ValidateEnumAndCompilerContract();

        bool gpuSupported =
            SystemInfo.supportsComputeShaders
            &&
            SystemInfo.supports2DArrayTextures
            &&
            SystemInfo.supportsAsyncGPUReadback
            &&
            SystemInfo.copyTextureSupport !=
                CopyTextureSupport.None
            &&
            SystemInfo.SupportsTextureFormat(
                TextureFormat.RFloat
            )
            &&
            SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            );

        if (!gpuSupported)
        {
            Add(
                "GPU validation prerequisites",
                false,
                "Required RFloat/compute/2D-array/CopyTexture/AsyncGPUReadback support is unavailable."
            );
        }
        else
        {
            Add(
                "GPU validation prerequisites",
                true,
                "Required GPU composition/readback capabilities are available."
            );

            ValidateCoreSemantics();
            ValidateFalloffAndNegativeRange();
            ValidateSourceRemapAndOrientation();
            ValidateOrderingAndReorder();
            ValidateCrossTileSeam();
            ValidateRangeMetadata();
            ValidateUnsupportedBlendMode();
            ValidateRuntimeParity();
        }

        return
            PrepareMutationUndoRedoValidation(
                realData.authoringRevision
            );
    }

    private static void ValidateEnumAndCompilerContract()
    {
        bool passed =
            (int)TerrainHeightBlendMode.Additive == 0
            &&
            (int)TerrainHeightBlendMode.Max == 1
            &&
            (int)TerrainHeightBlendMode.Min == 2;

        Add(
            "Blend-mode enum contract",
            passed,
            passed
                ? "Additive=0, Max=1, and Min=2 preserve explicit serialization values."
                : "TerrainHeightBlendMode numeric values do not match the Package 2 contract."
        );

        Add(
            "Runtime height compiler version",
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion == 10,
            $"RuntimeHeightCompilerVersion={TerrainGenerationStateUtility.RuntimeHeightCompilerVersion}; expected 10 after Package 3 Replace support."
        );

        TerrainStampModifier defaultStamp =
            new TerrainStampModifier();

        Add(
            "Existing Additive default preserved",
            defaultStamp.BlendMode ==
                TerrainHeightBlendMode.Additive,
            $"Default blend mode={defaultStamp.BlendMode}."
        );
    }

    private static void ValidateCoreSemantics()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateSingleCenterCase(
                "Additive positive regression",
                asset,
                TerrainHeightBlendMode.Additive,
                50f,
                20f,
                0f,
                10f,
                70f
            );

            ValidateSingleCenterCase(
                "Additive negative regression",
                asset,
                TerrainHeightBlendMode.Additive,
                50f,
                -15f,
                0f,
                10f,
                35f
            );

            ValidateSingleCenterCase(
                "Max raises toward target",
                asset,
                TerrainHeightBlendMode.Max,
                50f,
                0f,
                100f,
                0f,
                100f
            );

            ValidateSingleCenterCase(
                "Max never lowers terrain",
                asset,
                TerrainHeightBlendMode.Max,
                120f,
                0f,
                100f,
                0f,
                120f
            );

            ValidateSingleCenterCase(
                "Min lowers toward target",
                asset,
                TerrainHeightBlendMode.Min,
                100f,
                0f,
                40f,
                0f,
                40f
            );

            ValidateSingleCenterCase(
                "Min never raises terrain",
                asset,
                TerrainHeightBlendMode.Min,
                20f,
                0f,
                40f,
                0f,
                20f
            );
        }
        finally
        {
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateSingleCenterCase(
        string name,
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float seedHeight,
        float heightDelta,
        float targetBaseHeight,
        float targetHeightRange,
        float expectedCenter
    )
    {
        const int samplesPerSide = 17;
        const float sampleSpacing = 1f;
        const float tileWorldSize = 16f;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    blendMode,
                    new Vector2(8f, 8f),
                    new Vector2(12f, 12f),
                    heightDelta,
                    targetBaseHeight,
                    targetHeightRange,
                    0f
                );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    seedHeight,
                    "MaxMinSeed"
                );

            if (
                !TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    sampleSpacing,
                    tileWorldSize,
                    new Vector2(
                        tileWorldSize,
                        tileWorldSize
                    ),
                    false,
                    seedHeight,
                    seedHeight,
                    out float[] values,
                    out _,
                    out _,
                    out string errorMessage
                )
            )
            {
                Add(
                    name,
                    false,
                    errorMessage
                );

                return;
            }

            float center =
                values[
                    8 * samplesPerSide + 8
                ];

            bool passed =
                ApproximatelyGpu(
                    center,
                    expectedCenter
                );

            Add(
                name,
                passed,
                $"Center={center:R}, expected={expectedCenter:R}."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateFalloffAndNegativeRange()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinFalloffWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateFalloffCase(
                asset,
                TerrainHeightBlendMode.Max,
                0f,
                100f,
                0f,
                50f,
                "Max partial footprint falloff"
            );

            ValidateFalloffCase(
                asset,
                TerrainHeightBlendMode.Min,
                100f,
                0f,
                0f,
                50f,
                "Min partial footprint falloff"
            );

            ValidateSingleCenterCase(
                "Max negative TargetHeightRange",
                asset,
                TerrainHeightBlendMode.Max,
                0f,
                0f,
                200f,
                -150f,
                50f
            );

            ValidateSingleCenterCase(
                "Min negative TargetHeightRange",
                asset,
                TerrainHeightBlendMode.Min,
                100f,
                0f,
                200f,
                -150f,
                50f
            );
        }
        finally
        {
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateFalloffCase(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float seedHeight,
        float targetBaseHeight,
        float targetHeightRange,
        float expectedQuarterHeight,
        string name
    )
    {
        const int samplesPerSide = 17;
        const float sampleSpacing = 1f;
        const float tileWorldSize = 16f;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    blendMode,
                    new Vector2(8f, 8f),
                    new Vector2(16f, 16f),
                    0f,
                    targetBaseHeight,
                    targetHeightRange,
                    1f
                );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    seedHeight,
                    "MaxMinFalloffSeed"
                );

            if (
                !TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    sampleSpacing,
                    tileWorldSize,
                    new Vector2(16f, 16f),
                    false,
                    seedHeight,
                    seedHeight,
                    out float[] values,
                    out _,
                    out _,
                    out string errorMessage
                )
            )
            {
                Add(name, false, errorMessage);
                return;
            }

            float quarter =
                values[
                    8 * samplesPerSide + 4
                ];

            float edge =
                values[
                    8 * samplesPerSide
                ];

            bool passed =
                ApproximatelyGpu(
                    quarter,
                    expectedQuarterHeight
                )
                &&
                ApproximatelyGpu(
                    edge,
                    seedHeight
                );

            Add(
                name,
                passed,
                $"Quarter={quarter:R}, expected={expectedQuarterHeight:R}; edge={edge:R}, expected unchanged {seedHeight:R}."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateSourceRemapAndOrientation()
    {
        Texture2D quarterTexture =
            CreateConstantTexture(
                8,
                0.25f,
                "MaxMinQuarterSource"
            );

        TerrainHeightStampAsset quarterAsset =
            CreateStampAsset(
                quarterTexture
            );

        Texture2D gradientTexture =
            CreateGradientTexture(
                9,
                "MaxMinGradientSource"
            );

        TerrainHeightStampAsset gradientAsset =
            CreateStampAsset(
                gradientTexture
            );

        try
        {
            ValidateSourceRemapCase(
                quarterAsset,
                TerrainHeightBlendMode.Max,
                0f,
                50f,
                "Max source remap"
            );

            ValidateSourceRemapCase(
                quarterAsset,
                TerrainHeightBlendMode.Min,
                100f,
                50f,
                "Min source remap"
            );

            ValidateOrientationCase(
                gradientAsset,
                TerrainHeightBlendMode.Max,
                0f,
                "Max source orientation / rotation"
            );

            ValidateOrientationCase(
                gradientAsset,
                TerrainHeightBlendMode.Min,
                100f,
                "Min source orientation / rotation"
            );
        }
        finally
        {
            Destroy(quarterAsset);
            Destroy(quarterTexture);
            Destroy(gradientAsset);
            Destroy(gradientTexture);
        }
    }

    private static void ValidateSourceRemapCase(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float seedHeight,
        float expectedCenter,
        string name
    )
    {
        const int samplesPerSide = 17;
        const float tileWorldSize = 16f;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    blendMode,
                    new Vector2(8f, 8f),
                    new Vector2(12f, 12f),
                    0f,
                    0f,
                    100f,
                    0f
                );

            stamp.SetSourceRemapInternal(
                0f,
                0.5f,
                1f
            );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    seedHeight,
                    "MaxMinRemapSeed"
                );

            if (
                !TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    tileWorldSize,
                    new Vector2(16f, 16f),
                    false,
                    seedHeight,
                    seedHeight,
                    out float[] values,
                    out _,
                    out _,
                    out string errorMessage
                )
            )
            {
                Add(name, false, errorMessage);
                return;
            }

            float center =
                values[
                    8 * samplesPerSide + 8
                ];

            bool passed =
                ApproximatelyGpu(
                    center,
                    expectedCenter
                );

            Add(
                name,
                passed,
                $"Source 0.25 remapped through [0,0.5] produced center {center:R}; expected {expectedCenter:R}."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateOrientationCase(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float seedHeight,
        string name
    )
    {
        const int samplesPerSide = 17;
        const float tileWorldSize = 16f;

        float[] baseline = null;
        float[] flipX = null;
        float[] flipZ = null;
        float[] rotated = null;

        string errorMessage = "";

        if (
            !TryComposeOrientationVariant(
                asset,
                blendMode,
                seedHeight,
                false,
                false,
                0f,
                out baseline,
                out errorMessage
            )
            ||
            !TryComposeOrientationVariant(
                asset,
                blendMode,
                seedHeight,
                true,
                false,
                0f,
                out flipX,
                out errorMessage
            )
            ||
            !TryComposeOrientationVariant(
                asset,
                blendMode,
                seedHeight,
                false,
                true,
                0f,
                out flipZ,
                out errorMessage
            )
            ||
            !TryComposeOrientationVariant(
                asset,
                blendMode,
                seedHeight,
                false,
                false,
                180f,
                out rotated,
                out errorMessage
            )
        )
        {
            Add(name, false, errorMessage);
            return;
        }

        int a =
            6 * samplesPerSide + 4;

        int mirrorX =
            6 * samplesPerSide + 12;

        int mirrorZ =
            10 * samplesPerSide + 4;

        int opposite =
            10 * samplesPerSide + 12;

        bool sourceVaries =
            Mathf.Abs(
                baseline[a] -
                baseline[opposite]
            ) > 5f;

        bool passed =
            sourceVaries
            &&
            ApproximatelyGpu(
                baseline[a],
                flipX[mirrorX]
            )
            &&
            ApproximatelyGpu(
                baseline[a],
                flipZ[mirrorZ]
            )
            &&
            ApproximatelyGpu(
                baseline[a],
                rotated[opposite]
            );

        Add(
            name,
            passed,
            passed
                ? "Flip X, Flip Z, and 180-degree rotation reuse the existing source-coordinate pipeline."
                : $"baseline={baseline[a]:R}, flipX={flipX[mirrorX]:R}, flipZ={flipZ[mirrorZ]:R}, rotated={rotated[opposite]:R}."
        );
    }

    private static bool TryComposeOrientationVariant(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float seedHeight,
        bool flipX,
        bool flipZ,
        float rotationDegrees,
        out float[] values,
        out string errorMessage
    )
    {
        const int samplesPerSide = 17;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    blendMode,
                    new Vector2(8f, 8f),
                    new Vector2(16f, 16f),
                    0f,
                    0f,
                    100f,
                    0f
                );

            stamp.SetFlipXInternal(
                flipX
            );

            stamp.SetFlipZInternal(
                flipZ
            );

            stamp.SetRotationDegreesInternal(
                rotationDegrees
            );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    seedHeight,
                    "MaxMinOrientationSeed"
                );

            return
                TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    seedHeight,
                    seedHeight,
                    out values,
                    out _,
                    out _,
                    out errorMessage
                );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateOrderingAndReorder()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinOrderWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Additive,
                TerrainHeightBlendMode.Max,
                100f,
                "Additive -> Max"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Max,
                TerrainHeightBlendMode.Additive,
                120f,
                "Max -> Additive"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Additive,
                TerrainHeightBlendMode.Min,
                40f,
                "Additive -> Min"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Min,
                TerrainHeightBlendMode.Additive,
                60f,
                "Min -> Additive"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Max,
                TerrainHeightBlendMode.Min,
                40f,
                "Max -> Min"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Min,
                TerrainHeightBlendMode.Max,
                100f,
                "Min -> Max"
            );

            ValidateServiceReorder(
                asset
            );
        }
        finally
        {
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateOrderCase(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode firstMode,
        TerrainHeightBlendMode secondMode,
        float expectedCenter,
        string name
    )
    {
        const int samplesPerSide = 17;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            data.AddHeightModifierInternal(
                CreateOrderingStamp(
                    asset,
                    firstMode
                )
            );

            data.AddHeightModifierInternal(
                CreateOrderingStamp(
                    asset,
                    secondMode
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "MaxMinOrderSeed"
                );

            if (
                !TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    50f,
                    50f,
                    out float[] values,
                    out _,
                    out _,
                    out string errorMessage
                )
            )
            {
                Add(name, false, errorMessage);
                return;
            }

            float center =
                values[
                    8 * samplesPerSide + 8
                ];

            Add(
                name,
                ApproximatelyGpu(
                    center,
                    expectedCenter
                ),
                $"Center={center:R}, expected={expectedCenter:R}."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static TerrainStampModifier CreateOrderingStamp(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode
    )
    {
        float heightDelta =
            blendMode == TerrainHeightBlendMode.Additive
                ? 20f
                : 0f;

        float targetBaseHeight =
            blendMode == TerrainHeightBlendMode.Max
                ? 100f
                : 40f;

        return
            CreateStamp(
                asset,
                blendMode,
                new Vector2(8f, 8f),
                new Vector2(12f, 12f),
                heightDelta,
                targetBaseHeight,
                0f,
                0f
            );
    }

    private static void ValidateServiceReorder(
        TerrainHeightStampAsset asset
    )
    {
        const int samplesPerSide = 17;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier additive =
                CreateOrderingStamp(
                    asset,
                    TerrainHeightBlendMode.Additive
                );

            TerrainStampModifier maximum =
                CreateOrderingStamp(
                    asset,
                    TerrainHeightBlendMode.Max
                );

            data.AddHeightModifierInternal(
                additive
            );

            data.AddHeightModifierInternal(
                maximum
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "MaxMinReorderSeed"
                );

            if (
                !TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    50f,
                    50f,
                    out float[] before,
                    out _,
                    out _,
                    out string beforeError
                )
            )
            {
                Add(
                    "Service reorder preserves stack semantics",
                    false,
                    beforeError
                );

                return;
            }

            bool reordered =
                TerrainAuthoringModifierService
                    .ReorderModifier(
                        data,
                        worldSettings,
                        maximum.StableId,
                        0,
                        out string reorderError
                    );

            if (!reordered)
            {
                Add(
                    "Service reorder preserves stack semantics",
                    false,
                    reorderError
                );

                return;
            }

            if (
                !TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    50f,
                    50f,
                    out float[] after,
                    out _,
                    out _,
                    out string afterError
                )
            )
            {
                Add(
                    "Service reorder preserves stack semantics",
                    false,
                    afterError
                );

                return;
            }

            float beforeCenter =
                before[8 * samplesPerSide + 8];

            float afterCenter =
                after[8 * samplesPerSide + 8];

            bool passed =
                ApproximatelyGpu(
                    beforeCenter,
                    100f
                )
                &&
                ApproximatelyGpu(
                    afterCenter,
                    120f
                );

            Add(
                "Service reorder preserves stack semantics",
                passed,
                $"Additive->Max={beforeCenter:R}; Max->Additive after reorder={afterCenter:R}."
            );
        }
        finally
        {
            Undo.ClearUndo(
                data
            );

            TerrainAuthoringModifierChangeTracker
                .Forget(
                    data
                );

            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateCrossTileSeam()
    {
        const int samplesPerSide = 17;
        const float tileWorldSize = 16f;

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinCrossTileWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            data.AddHeightModifierInternal(
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Max,
                    new Vector2(16f, 8f),
                    new Vector2(16f, 12f),
                    0f,
                    100f,
                    0f,
                    0f
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    0f,
                    "MaxMinCrossTileSeed"
                );

            bool leftOk =
                TryComposeAndRead(
                    data,
                    seed,
                    new Vector2Int(0, 0),
                    samplesPerSide,
                    1f,
                    tileWorldSize,
                    new Vector2(32f, 16f),
                    false,
                    0f,
                    0f,
                    out float[] left,
                    out _,
                    out _,
                    out string leftError
                );

            bool rightOk =
                TryComposeAndRead(
                    data,
                    seed,
                    new Vector2Int(1, 0),
                    samplesPerSide,
                    1f,
                    tileWorldSize,
                    new Vector2(32f, 16f),
                    false,
                    0f,
                    0f,
                    out float[] right,
                    out _,
                    out _,
                    out string rightError
                );

            if (
                !leftOk
                ||
                !rightOk
            )
            {
                Add(
                    "Max cross-tile duplicated border",
                    false,
                    !leftOk
                        ? leftError
                        : rightError
                );

                return;
            }

            float leftBorder =
                left[
                    8 * samplesPerSide + 16
                ];

            float rightBorder =
                right[
                    8 * samplesPerSide
                ];

            bool passed =
                ApproximatelyGpu(
                    leftBorder,
                    rightBorder
                )
                &&
                ApproximatelyGpu(
                    leftBorder,
                    100f
                );

            Add(
                "Max cross-tile duplicated border",
                passed,
                $"Left border={leftBorder:R}; right border={rightBorder:R}."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateRangeMetadata()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinRangeWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateStamp(
                        asset,
                        TerrainHeightBlendMode.Additive,
                        new Vector2(8f, 8f),
                        new Vector2(12f, 12f),
                        20f,
                        0f,
                        0f,
                        0f
                    )
                },
                40f,
                60f,
                40f,
                80f,
                "Additive conservative absolute range"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateStamp(
                        asset,
                        TerrainHeightBlendMode.Max,
                        new Vector2(8f, 8f),
                        new Vector2(12f, 12f),
                        0f,
                        100f,
                        0f,
                        0f
                    )
                },
                40f,
                60f,
                40f,
                100f,
                "Max conservative absolute range"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateStamp(
                        asset,
                        TerrainHeightBlendMode.Min,
                        new Vector2(8f, 8f),
                        new Vector2(12f, 12f),
                        0f,
                        20f,
                        0f,
                        0f
                    )
                },
                40f,
                60f,
                20f,
                60f,
                "Min conservative absolute range"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateOrderingStamp(
                        asset,
                        TerrainHeightBlendMode.Additive
                    ),
                    CreateOrderingStamp(
                        asset,
                        TerrainHeightBlendMode.Max
                    ),
                    CreateOrderingStamp(
                        asset,
                        TerrainHeightBlendMode.Min
                    )
                },
                40f,
                60f,
                40f,
                100f,
                "Mixed-mode conservative absolute range"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateStamp(
                        asset,
                        TerrainHeightBlendMode.Min,
                        new Vector2(8f, 8f),
                        new Vector2(12f, 12f),
                        0f,
                        200f,
                        -180f,
                        0f
                    )
                },
                40f,
                60f,
                20f,
                60f,
                "Negative target-range metadata"
            );
        }
        finally
        {
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateRangeCase(
        TerrainHeightStampAsset asset,
        TerrainStampModifier[] stamps,
        float baseMinimum,
        float baseMaximum,
        float expectedMinimum,
        float expectedMaximum,
        string name
    )
    {
        const int samplesPerSide = 17;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            for (
                int index = 0;
                index < stamps.Length;
                index++
            )
            {
                data.AddHeightModifierInternal(
                    stamps[index]
                );
            }

            data.RepairModifierStableIds();

            seed =
                CreateRampSeedTexture(
                    samplesPerSide,
                    baseMinimum,
                    baseMaximum,
                    "MaxMinRangeSeed"
                );

            bool trackedOk =
                TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    true,
                    baseMinimum,
                    baseMaximum,
                    out float[] trackedValues,
                    out float compositeMinimum,
                    out float compositeMaximum,
                    out string trackedError
                );

            bool plainOk =
                TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    baseMinimum,
                    baseMaximum,
                    out float[] plainValues,
                    out _,
                    out _,
                    out string plainError
                );

            if (
                !trackedOk
                ||
                !plainOk
            )
            {
                Add(
                    name,
                    false,
                    !trackedOk
                        ? trackedError
                        : plainError
                );

                return;
            }

            GetRange(
                trackedValues,
                out float actualMinimum,
                out float actualMaximum
            );

            float maximumDifference =
                CalculateMaximumDifference(
                    trackedValues,
                    plainValues
                );

            bool containsActual =
                compositeMinimum <=
                    actualMinimum + GpuTolerance
                &&
                compositeMaximum >=
                    actualMaximum - GpuTolerance;

            bool expectedConservative =
                Approximately(
                    compositeMinimum,
                    expectedMinimum
                )
                &&
                Approximately(
                    compositeMaximum,
                    expectedMaximum
                );

            bool passed =
                containsActual
                &&
                expectedConservative
                &&
                maximumDifference <=
                    GpuTolerance;

            Add(
                name,
                passed,
                $"Returned=[{compositeMinimum:R}, {compositeMaximum:R}], actual=[{actualMinimum:R}, {actualMaximum:R}], preview/no-range max diff={maximumDifference:R}."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateUnsupportedBlendMode()
    {
        const int samplesPerSide = 17;

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinUnsupportedWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            data.AddHeightModifierInternal(
                CreateStamp(
                    asset,
                    (TerrainHeightBlendMode)999,
                    new Vector2(8f, 8f),
                    new Vector2(12f, 12f),
                    0f,
                    100f,
                    0f,
                    0f
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    0f,
                    "MaxMinUnsupportedSeed"
                );

            bool succeeded =
                TryComposeAndRead(
                    data,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    0f,
                    0f,
                    out _,
                    out _,
                    out _,
                    out string errorMessage
                );

            bool passed =
                !succeeded
                &&
                !string.IsNullOrEmpty(
                    errorMessage
                )
                &&
                errorMessage.Contains(
                    "Unsupported terrain height blend mode"
                );

            Add(
                "Unsupported blend modes fail explicitly",
                passed,
                succeeded
                    ? "The compositor unexpectedly accepted blend mode 999."
                    : errorMessage
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateRuntimeParity()
    {
        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        float tileWorldSize =
            worldSettings.HeightTileWorldSize;

        float sampleSpacing =
            worldSettings.chunkSize
            /
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            samplesPerSide <= 1
            ||
            tileWorldSize <= 0f
            ||
            sampleSpacing <= 0f
            ||
            worldSizeXZ.x < tileWorldSize
            ||
            worldSizeXZ.y < tileWorldSize
        )
        {
            Add(
                "Preview/runtime shared compositor parity",
                false,
                "Current world layout is not valid for a first-tile runtime parity test."
            );

            return;
        }

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "MaxMinRuntimeWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D committed = null;

        try
        {
            Vector2 center =
                new Vector2(
                    tileWorldSize * 0.5f,
                    tileWorldSize * 0.5f
                );

            Vector2 size =
                new Vector2(
                    tileWorldSize * 0.6f,
                    tileWorldSize * 0.6f
                );

            data.AddHeightModifierInternal(
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Max,
                    center,
                    size,
                    0f,
                    100f,
                    0f,
                    0.2f
                )
            );

            data.AddHeightModifierInternal(
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Additive,
                    center,
                    size,
                    10f,
                    0f,
                    0f,
                    0.2f
                )
            );

            data.AddHeightModifierInternal(
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Min,
                    center,
                    size,
                    0f,
                    80f,
                    0f,
                    0.2f
                )
            );

            data.RepairModifierStableIds();

            committed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "MaxMinRuntimeCommitted"
                );

            if (
                !TryComposeAndRead(
                    data,
                    committed,
                    Vector2Int.zero,
                    samplesPerSide,
                    sampleSpacing,
                    tileWorldSize,
                    worldSizeXZ,
                    false,
                    50f,
                    50f,
                    out float[] direct,
                    out _,
                    out _,
                    out string directError
                )
            )
            {
                Add(
                    "Preview/runtime shared compositor parity",
                    false,
                    directError
                );

                return;
            }

            float[] runtime =
                new float[
                    samplesPerSide *
                    samplesPerSide
                ];

            using (
                TerrainRuntimeHeightCompositionContext context =
                    new TerrainRuntimeHeightCompositionContext()
            )
            {
                if (
                    !context.TryPrepare(
                        worldSettings,
                        data,
                        out string prepareError
                    )
                )
                {
                    Add(
                        "Preview/runtime shared compositor parity",
                        false,
                        prepareError
                    );

                    return;
                }

                if (
                    !context.RequiresComposition(
                        Vector2Int.zero
                    )
                )
                {
                    Add(
                        "Preview/runtime shared compositor parity",
                        false,
                        "The runtime composition context did not classify tile (0, 0) as modifier-affected."
                    );

                    return;
                }

                if (
                    !context.TryComposeCommittedTile(
                        committed,
                        Vector2Int.zero,
                        runtime,
                        out string runtimeError
                    )
                )
                {
                    Add(
                        "Preview/runtime shared compositor parity",
                        false,
                        runtimeError
                    );

                    return;
                }
            }

            float maximumDifference =
                CalculateMaximumDifference(
                    direct,
                    runtime
                );

            Add(
                "Preview/runtime shared compositor parity",
                maximumDifference <=
                    GpuTolerance,
                $"Direct compositor vs runtime composition-context maximum difference={maximumDifference:R}."
            );
        }
        finally
        {
            Destroy(committed);
            Destroy(data);
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static bool PrepareMutationUndoRedoValidation(
        int baseRevision
    )
    {
        CleanupValidationAssets();
        EnsureFolder(
            ValidationRoot
        );

        mutationData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        mutationData.authoringRevision =
            baseRevision;

        AssetDatabase.CreateAsset(
            mutationData,
            ValidationDataPath
        );

        float tileSize =
            worldSettings.HeightTileWorldSize;

        bool added =
            TerrainAuthoringModifierService
                .AddStampModifier(
                    mutationData,
                    worldSettings,
                    null,
                    new Vector2(
                        tileSize * 0.5f,
                        tileSize * 0.5f
                    ),
                    new Vector2(
                        tileSize * 0.25f,
                        tileSize * 0.25f
                    ),
                    10f,
                    0.25f,
                    out mutationModifierId,
                    out string addError
                );

        TerrainStampModifier stamp =
            FindStampModifier(
                mutationData,
                mutationModifierId
            );

        if (
            !added
            ||
            stamp == null
        )
        {
            Add(
                "Blend-mode mutation setup",
                false,
                addError
            );

            return false;
        }

        Undo.ClearUndo(
            mutationData
        );

        int revisionBefore =
            mutationData.authoringRevision;

        string signatureBefore =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        Bounds bounds =
            stamp.GetAffectedWorldBounds();

        bool changed =
            TerrainAuthoringModifierService
                .SetModifierBlendMode(
                    mutationData,
                    worldSettings,
                    mutationModifierId,
                    TerrainHeightBlendMode.Max,
                    out string blendError
                );

        string signatureAfter =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        HashSet<Vector2Int> expectedDirty =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                bounds,
                expectedDirty,
                1
            );

        HashSet<Vector2Int> actualDirty =
            new HashSet<Vector2Int>(
                TerrainAuthoringModifierService
                    .LastMutationDiagnostics
                    .DirtyTiles
            );

        bool mutationPassed =
            changed
            &&
            stamp.BlendMode ==
                TerrainHeightBlendMode.Max
            &&
            mutationData.authoringRevision ==
                revisionBefore + 1
            &&
            signatureAfter !=
                signatureBefore
            &&
            actualDirty.SetEquals(
                expectedDirty
            );

        Add(
            "Blend-mode service mutation",
            mutationPassed,
            mutationPassed
                ? "SetModifierBlendMode changed mode, revision, signature, and expected dirty tiles through the central mutation path."
                : blendError
        );

        bool disabled =
            TerrainAuthoringModifierService
                .SetModifierEnabled(
                    mutationData,
                    worldSettings,
                    mutationModifierId,
                    false,
                    out string disableError
                );

        int disableDirtyCount =
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
                .DirtyTileCount;

        bool enabled =
            TerrainAuthoringModifierService
                .SetModifierEnabled(
                    mutationData,
                    worldSettings,
                    mutationModifierId,
                    true,
                    out string enableError
                );

        int enableDirtyCount =
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
                .DirtyTileCount;

        bool enableDisablePassed =
            disabled
            &&
            enabled
            &&
            disableDirtyCount > 0
            &&
            enableDirtyCount > 0;

        Add(
            "Max enable / disable invalidation",
            enableDisablePassed,
            enableDisablePassed
                ? "Disable and enable both dirtied the modifier footprint through the established change tracker."
                : string.IsNullOrEmpty(disableError)
                    ? enableError
                    : disableError
        );

        TerrainAuthoringModifierService
            .SetModifierBlendMode(
                mutationData,
                worldSettings,
                mutationModifierId,
                TerrainHeightBlendMode.Additive,
                out _
            );

        Undo.ClearUndo(
            mutationData
        );

        stamp =
            FindStampModifier(
                mutationData,
                mutationModifierId
            );

        undoExpectedBlendMode =
            stamp.BlendMode;

        undoExpectedRevision =
            mutationData.authoringRevision;

        undoExpectedSignature =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool finalMutation =
            TerrainAuthoringModifierService
                .SetModifierBlendMode(
                    mutationData,
                    worldSettings,
                    mutationModifierId,
                    TerrainHeightBlendMode.Min,
                    out string finalError
                );

        stamp =
            FindStampModifier(
                mutationData,
                mutationModifierId
            );

        if (
            !finalMutation
            ||
            stamp == null
        )
        {
            Add(
                "Undo/Redo blend-mode setup",
                false,
                finalError
            );

            return false;
        }

        redoExpectedBlendMode =
            stamp.BlendMode;

        redoExpectedRevision =
            mutationData.authoringRevision;

        redoExpectedSignature =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        Add(
            "Undo/Redo blend-mode setup",
            undoExpectedBlendMode ==
                TerrainHeightBlendMode.Additive
            &&
            redoExpectedBlendMode ==
                TerrainHeightBlendMode.Min
            &&
            redoExpectedRevision ==
                undoExpectedRevision + 1
            &&
            redoExpectedSignature !=
                undoExpectedSignature,
            "A final Additive -> Min service mutation was recorded as one Undo step."
        );

        return true;
    }

    private static void ScheduleUndoVerification()
    {
        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        EditorApplication.delayCall +=
            VerifyUndoAndRequestRedo;
    }

    private static void VerifyUndoAndRequestRedo()
    {
        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        try
        {
            TerrainStampModifier stamp =
                FindStampModifier(
                    mutationData,
                    mutationModifierId
                );

            string signature =
                stamp != null
                    ? TerrainAuthoringStateUtility
                        .GetModifierContentSignature(
                            stamp
                        )
                    : "";

            bool passed =
                stamp != null
                &&
                stamp.BlendMode ==
                    undoExpectedBlendMode
                &&
                mutationData.authoringRevision ==
                    undoExpectedRevision
                &&
                signature ==
                    undoExpectedSignature;

            Add(
                "Undo restores blend mode",
                passed,
                passed
                    ? "Undo restored Additive mode, revision, and deterministic modifier signature."
                    : "Undo did not restore the expected blend-mode state."
            );

            Undo.PerformRedo();
            ScheduleRedoVerification();
        }
        catch (Exception exception)
        {
            Add(
                "Undo verification exception",
                false,
                exception.ToString()
            );

            Finish();
        }
    }

    private static void ScheduleRedoVerification()
    {
        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        EditorApplication.delayCall +=
            VerifyRedoAndFinish;
    }

    private static void VerifyRedoAndFinish()
    {
        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        try
        {
            TerrainStampModifier stamp =
                FindStampModifier(
                    mutationData,
                    mutationModifierId
                );

            string signature =
                stamp != null
                    ? TerrainAuthoringStateUtility
                        .GetModifierContentSignature(
                            stamp
                        )
                    : "";

            bool passed =
                stamp != null
                &&
                stamp.BlendMode ==
                    redoExpectedBlendMode
                &&
                mutationData.authoringRevision ==
                    redoExpectedRevision
                &&
                signature ==
                    redoExpectedSignature;

            Add(
                "Redo restores blend mode",
                passed,
                passed
                    ? "Redo restored Min mode, revision, and deterministic modifier signature."
                    : "Redo did not restore the expected blend-mode state."
            );
        }
        catch (Exception exception)
        {
            Add(
                "Redo verification exception",
                false,
                exception.ToString()
            );
        }
        finally
        {
            Finish();
        }
    }

    private static TerrainStampModifier CreateStamp(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        Vector2 position,
        Vector2 size,
        float heightDelta,
        float targetBaseHeight,
        float targetHeightRange,
        float falloff
    )
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        stamp.SetStampAssetInternal(
            asset
        );

        stamp.SetPositionXZInternal(
            position
        );

        stamp.SetSizeXZInternal(
            size
        );

        stamp.SetRotationDegreesInternal(
            0f
        );

        stamp.SetFlipXInternal(
            false
        );

        stamp.SetFlipZInternal(
            false
        );

        stamp.SetSourceRemapInternal(
            TerrainStampSourceRemapUtility.IdentityInputMin,
            TerrainStampSourceRemapUtility.IdentityInputMax,
            TerrainStampSourceRemapUtility.IdentityGamma
        );

        stamp.SetHeightDeltaInternal(
            heightDelta
        );

        stamp.SetTargetBaseHeightInternal(
            targetBaseHeight
        );

        stamp.SetTargetHeightRangeInternal(
            targetHeightRange
        );

        stamp.SetFalloffInternal(
            falloff
        );

        stamp.SetFalloffShapeInternal(
            TerrainStampFalloffShape.Rectangle
        );

        stamp.SetFalloffProfileInternal(
            TerrainStampFalloffProfile.Smooth
        );

        stamp.SetSmoothingRadiusInternal(
            0f
        );

        stamp.SetSmoothingStrengthInternal(
            1f
        );

        stamp.SetBlendModeInternal(
            blendMode
        );

        stamp.SetEnabledInternal(
            true
        );

        return stamp;
    }

    private static TerrainHeightStampAsset CreateStampAsset(
        Texture2D texture
    )
    {
        TerrainHeightStampAsset asset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        asset.SetHeightTextureInternal(
            texture
        );

        return asset;
    }

    private static Texture2D CreateConstantTexture(
        int size,
        float value,
        string name
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
            name;

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        texture.anisoLevel =
            0;

        float[] values =
            new float[
                size * size
            ];

        for (
            int index = 0;
            index < values.Length;
            index++
        )
        {
            values[index] =
                value;
        }

        texture.SetPixelData(
            values,
            0
        );

        texture.Apply(
            false,
            false
        );

        return texture;
    }

    private static Texture2D CreateGradientTexture(
        int size,
        string name
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
            name;

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        texture.anisoLevel =
            0;

        float[] values =
            new float[
                size * size
            ];

        for (
            int y = 0;
            y < size;
            y++
        )
        {
            for (
                int x = 0;
                x < size;
                x++
            )
            {
                values[
                    y * size + x
                ] =
                    (
                        x +
                        2f * y
                    )
                    /
                    (
                        3f *
                        (
                            size - 1
                        )
                    );
            }
        }

        texture.SetPixelData(
            values,
            0
        );

        texture.Apply(
            false,
            false
        );

        return texture;
    }

    private static Texture2D CreateRampSeedTexture(
        int size,
        float minimum,
        float maximum,
        string name
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
            name;

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Point;

        float[] values =
            new float[
                size * size
            ];

        for (
            int y = 0;
            y < size;
            y++
        )
        {
            for (
                int x = 0;
                x < size;
                x++
            )
            {
                float t =
                    (
                        x +
                        y
                    )
                    /
                    (
                        2f *
                        (
                            size - 1
                        )
                    );

                values[
                    y * size + x
                ] =
                    Mathf.Lerp(
                        minimum,
                        maximum,
                        t
                    );
            }
        }

        texture.SetPixelData(
            values,
            0
        );

        texture.Apply(
            false,
            false
        );

        return texture;
    }

    private static bool TryComposeAndRead(
        TerrainAuthoringData data,
        Texture2D seed,
        Vector2Int tileCoordinate,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ,
        bool trackRange,
        float baseMinimumHeight,
        float baseMaximumHeight,
        out float[] values,
        out float compositeMinimumHeight,
        out float compositeMaximumHeight,
        out string errorMessage
    )
    {
        values =
            null;

        compositeMinimumHeight =
            baseMinimumHeight;

        compositeMaximumHeight =
            baseMaximumHeight;

        errorMessage =
            "";

        RenderTexture target =
            new RenderTexture(
                samplesPerSide,
                samplesPerSide,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );

        target.dimension =
            TextureDimension.Tex2DArray;

        target.volumeDepth =
            1;

        target.enableRandomWrite =
            true;

        target.useMipMap =
            false;

        target.autoGenerateMips =
            false;

        target.wrapMode =
            TextureWrapMode.Clamp;

        target.filterMode =
            FilterMode.Point;

        try
        {
            if (!target.Create())
            {
                errorMessage =
                    "Could not create the temporary RFloat composition target.";

                return false;
            }

            Graphics.CopyTexture(
                seed,
                0,
                0,
                target,
                0,
                0
            );

            TerrainHeightCompositor compositor =
                new TerrainHeightCompositor();

            bool composed;

            if (trackRange)
            {
                composed =
                    compositor.TryComposeTile(
                        target,
                        tileCoordinate,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        worldSizeXZ,
                        data,
                        baseMinimumHeight,
                        baseMaximumHeight,
                        out compositeMinimumHeight,
                        out compositeMaximumHeight,
                        out errorMessage
                    );
            }
            else
            {
                composed =
                    compositor.TryComposeTile(
                        target,
                        tileCoordinate,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        worldSizeXZ,
                        data,
                        out errorMessage
                    );
            }

            if (!composed)
            {
                return false;
            }

            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    target,
                    0
                );

            request.WaitForCompletion();

            if (request.hasError)
            {
                errorMessage =
                    "AsyncGPUReadback failed for the temporary composition target.";

                return false;
            }

            var readback =
                request.GetData<float>();

            values =
                new float[
                    readback.Length
                ];

            readback.CopyTo(
                values
            );

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                exception.ToString();

            return false;
        }
        finally
        {
            target.Release();
            Destroy(target);
        }
    }

    private static TerrainStampModifier FindStampModifier(
        TerrainAuthoringData data,
        string stableId
    )
    {
        if (
            data == null
            ||
            string.IsNullOrEmpty(
                stableId
            )
        )
        {
            return null;
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            data.HeightModifiers;

        for (
            int index = 0;
            index < modifiers.Count;
            index++
        )
        {
            if (
                modifiers[index] is TerrainStampModifier stamp
                &&
                stamp.StableId ==
                    stableId
            )
            {
                return stamp;
            }
        }

        return null;
    }

    private static void GetRange(
        float[] values,
        out float minimum,
        out float maximum
    )
    {
        minimum =
            float.PositiveInfinity;

        maximum =
            float.NegativeInfinity;

        if (values == null)
        {
            return;
        }

        for (
            int index = 0;
            index < values.Length;
            index++
        )
        {
            minimum =
                Mathf.Min(
                    minimum,
                    values[index]
                );

            maximum =
                Mathf.Max(
                    maximum,
                    values[index]
                );
        }
    }

    private static float CalculateMaximumDifference(
        float[] a,
        float[] b
    )
    {
        if (
            a == null
            ||
            b == null
            ||
            a.Length !=
                b.Length
        )
        {
            return
                float.PositiveInfinity;
        }

        float maximumDifference =
            0f;

        for (
            int index = 0;
            index < a.Length;
            index++
        )
        {
            maximumDifference =
                Mathf.Max(
                    maximumDifference,
                    Mathf.Abs(
                        a[index] -
                        b[index]
                    )
                );
        }

        return maximumDifference;
    }

    private static void EnsureFolder(
        string folderPath
    )
    {
        if (
            string.IsNullOrEmpty(
                folderPath
            )
            ||
            AssetDatabase.IsValidFolder(
                folderPath
            )
        )
        {
            return;
        }

        string[] parts =
            folderPath.Split('/');

        string current =
            parts[0];

        for (
            int index = 1;
            index < parts.Length;
            index++
        )
        {
            string next =
                current
                +
                "/"
                +
                parts[index];

            if (
                !AssetDatabase.IsValidFolder(
                    next
                )
            )
            {
                AssetDatabase.CreateFolder(
                    current,
                    parts[index]
                );
            }

            current =
                next;
        }
    }

    private static void CleanupValidationAssets()
    {
        if (mutationData != null)
        {
            TerrainAuthoringModifierChangeTracker
                .Forget(
                    mutationData
                );

            Undo.ClearUndo(
                mutationData
            );
        }

        mutationData =
            null;

        if (
            AssetDatabase.IsValidFolder(
                ValidationRoot
            )
        )
        {
            AssetDatabase.DeleteAsset(
                ValidationRoot
            );
        }
    }

    private static void Finish()
    {
        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        CleanupValidationAssets();

        lastPassedCount =
            0;

        lastFailedCount =
            0;

        StringBuilder builder =
            new StringBuilder();

        for (
            int index = 0;
            index < results.Count;
            index++
        )
        {
            Result result =
                results[index];

            if (result.Passed)
            {
                lastPassedCount++;
            }
            else
            {
                lastFailedCount++;
            }

            builder.Append(
                result.Passed
                    ? "PASS: "
                    : "FAIL: "
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
                builder.AppendLine(
                    "  " +
                    result.Details
                );
            }
        }

        lastSummary =
            $"{lastPassedCount} passed, {lastFailedCount} failed.";

        builder.AppendLine();
        builder.AppendLine(
            lastSummary
        );

        if (lastFailedCount > 0)
        {
            Debug.LogError(
                "WorldMeshes Max / Min Blend validation\n\n" +
                builder
            );
        }
        else
        {
            Debug.Log(
                "WorldMeshes Max / Min Blend validation\n\n" +
                builder
            );
        }

        validationScheduled =
            false;

        validationRunning =
            false;

        worldSettings =
            null;

        mutationModifierId =
            "";

        undoExpectedSignature =
            "";

        redoExpectedSignature =
            "";
    }

    private static void Add(
        string name,
        bool passed,
        string details
    )
    {
        results.Add(
            new Result
            {
                Name = name,
                Passed = passed,
                Details = details ?? ""
            }
        );
    }

    private static bool Approximately(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a - b
            ) <=
            MathTolerance;
    }

    private static bool ApproximatelyGpu(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a - b
            ) <=
            GpuTolerance;
    }

    private static void Destroy(
        UnityEngine.Object value
    )
    {
        if (value != null)
        {
            UnityEngine.Object
                .DestroyImmediate(
                    value
                );
        }
    }
}
