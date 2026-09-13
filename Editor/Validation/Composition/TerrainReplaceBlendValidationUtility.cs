using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainReplaceBlendValidationUtility
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
        "/ReplaceBlendMode";

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

            ValidateExistingModeRegression();
            ValidateCoreReplaceSemantics();
            ValidateSourceTargetSeparation();
            ValidateFalloffAndFootprintShapes();
            ValidateSourcePipeline();
            ValidateOrdering();
            ValidateEnableDisableOutput();
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
        bool enumPassed =
            (int)TerrainHeightBlendMode.Additive == 0
            &&
            (int)TerrainHeightBlendMode.Max == 1
            &&
            (int)TerrainHeightBlendMode.Min == 2
            &&
            (int)TerrainHeightBlendMode.Replace == 3;

        Add(
            "Blend-mode enum contract",
            enumPassed,
            enumPassed
                ? "Additive=0, Max=1, Min=2, and Replace=3 preserve explicit serialization values."
                : "TerrainHeightBlendMode numeric values do not match the Package 3 contract."
        );

        Add(
            "Runtime height compiler version",
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion == 10,
            $"RuntimeHeightCompilerVersion={TerrainGenerationStateUtility.RuntimeHeightCompilerVersion}; expected 10 for Replace composition."
        );

        TerrainStampModifier defaultStamp =
            new TerrainStampModifier();

        Add(
            "Existing Additive creation default preserved",
            defaultStamp.BlendMode ==
                TerrainHeightBlendMode.Additive,
            $"Default blend mode={defaultStamp.BlendMode}."
        );
    }

    private static void ValidateExistingModeRegression()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceRegressionWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateExistingModeCenterCase(
                "Additive regression",
                asset,
                TerrainHeightBlendMode.Additive,
                50f,
                20f,
                0f,
                70f
            );

            ValidateExistingModeCenterCase(
                "Max regression",
                asset,
                TerrainHeightBlendMode.Max,
                50f,
                0f,
                100f,
                100f
            );

            ValidateExistingModeCenterCase(
                "Min regression",
                asset,
                TerrainHeightBlendMode.Min,
                50f,
                0f,
                40f,
                40f
            );
        }
        finally
        {
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateExistingModeCenterCase(
        string name,
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float seedHeight,
        float heightDelta,
        float targetBaseHeight,
        float expectedCenter
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
                CreateStamp(
                    asset,
                    blendMode,
                    new Vector2(8f, 8f),
                    new Vector2(12f, 12f),
                    heightDelta,
                    targetBaseHeight,
                    0f,
                    0f
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    seedHeight,
                    "ReplaceRegressionSeed"
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

    private static void ValidateCoreReplaceSemantics()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceWhite"
            );

        TerrainHeightStampAsset whiteAsset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateSingleCenterCase(
                "Replace raises terrain",
                whiteAsset,
                40f,
                100f,
                0f,
                0f,
                100f
            );

            ValidateSingleCenterCase(
                "Replace lowers terrain",
                whiteAsset,
                160f,
                100f,
                0f,
                0f,
                100f
            );

            ValidateSingleCenterCase(
                "Replace flat zero target range",
                whiteAsset,
                35f,
                150f,
                0f,
                0f,
                150f
            );

            ValidateSingleCenterCase(
                "Replace positive target range",
                whiteAsset,
                20f,
                100f,
                200f,
                0f,
                300f
            );

            ValidateSingleCenterCase(
                "Replace negative target range",
                whiteAsset,
                20f,
                300f,
                -200f,
                0f,
                100f
            );
        }
        finally
        {
            Destroy(whiteAsset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateSourceTargetSeparation()
    {
        Texture2D blackTexture =
            CreateConstantTexture(
                8,
                0f,
                "ReplaceBlack"
            );

        TerrainHeightStampAsset blackAsset =
            CreateStampAsset(
                blackTexture
            );

        Texture2D quarterTexture =
            CreateConstantTexture(
                8,
                0.25f,
                "ReplaceQuarter"
            );

        TerrainHeightStampAsset quarterAsset =
            CreateStampAsset(
                quarterTexture
            );

        try
        {
            ValidateSingleCenterCase(
                "Black source still replaces to target base",
                blackAsset,
                240f,
                70f,
                500f,
                0f,
                70f
            );

            ValidateRemapCase(
                quarterAsset
            );
        }
        finally
        {
            Destroy(blackAsset);
            Destroy(blackTexture);
            Destroy(quarterAsset);
            Destroy(quarterTexture);
        }
    }

    private static void ValidateRemapCase(
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
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
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
                    0f,
                    "ReplaceRemapSeed"
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
                    0f,
                    0f,
                    out float[] values,
                    out _,
                    out _,
                    out string errorMessage
                )
            )
            {
                Add(
                    "Replace source remap",
                    false,
                    errorMessage
                );

                return;
            }

            float center =
                values[
                    8 * samplesPerSide + 8
                ];

            Add(
                "Replace source remap",
                ApproximatelyGpu(
                    center,
                    50f
                ),
                $"Source 0.25 remapped through [0,0.5] produced center={center:R}; expected 50."
            );

            stamp.SetSourceRemapInternal(
                0f,
                0.5f,
                2f
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
                    0f,
                    0f,
                    out values,
                    out _,
                    out _,
                    out errorMessage
                )
            )
            {
                Add(
                    "Replace source gamma",
                    false,
                    errorMessage
                );

                return;
            }

            center =
                values[
                    8 * samplesPerSide + 8
                ];

            Add(
                "Replace source gamma",
                ApproximatelyGpu(
                    center,
                    25f
                ),
                $"Remapped source 0.5 with gamma 2 produced center={center:R}; expected 25."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateSingleCenterCase(
        string name,
        TerrainHeightStampAsset asset,
        float seedHeight,
        float targetBaseHeight,
        float targetHeightRange,
        float falloff,
        float expectedCenter
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
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    new Vector2(8f, 8f),
                    new Vector2(12f, 12f),
                    0f,
                    targetBaseHeight,
                    targetHeightRange,
                    falloff
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    seedHeight,
                    "ReplaceCenterSeed"
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

    private static void ValidateFalloffAndFootprintShapes()
    {
        const int samplesPerSide = 17;

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceFalloffWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        TerrainAuthoringData rectangleData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        TerrainAuthoringData ellipseData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier rectangle =
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    new Vector2(8f, 8f),
                    new Vector2(16f, 16f),
                    0f,
                    100f,
                    0f,
                    1f
                );

            rectangle.SetFalloffShapeInternal(
                TerrainStampFalloffShape.Rectangle
            );

            rectangle.SetFalloffProfileInternal(
                TerrainStampFalloffProfile.Smooth
            );

            rectangleData.AddHeightModifierInternal(
                rectangle
            );

            rectangleData.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    0f,
                    "ReplaceFalloffSeed"
                );

            if (
                TryComposeAndRead(
                    rectangleData,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    0f,
                    0f,
                    out float[] rectangleValues,
                    out _,
                    out _,
                    out string rectangleError
                )
            )
            {
                float center =
                    rectangleValues[
                        8 * samplesPerSide + 8
                    ];

                float quarter =
                    rectangleValues[
                        8 * samplesPerSide + 4
                    ];

                float edge =
                    rectangleValues[
                        8 * samplesPerSide
                    ];

                bool passed =
                    ApproximatelyGpu(
                        center,
                        100f
                    )
                    &&
                    ApproximatelyGpu(
                        quarter,
                        50f
                    )
                    &&
                    ApproximatelyGpu(
                        edge,
                        0f
                    );

                Add(
                    "Replace footprint falloff influence",
                    passed,
                    $"Center={center:R}, quarter={quarter:R}, edge={edge:R}; expected 100/50/0."
                );
            }
            else
            {
                Add(
                    "Replace footprint falloff influence",
                    false,
                    rectangleError
                );
            }

            TerrainStampModifier ellipse =
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    new Vector2(8f, 8f),
                    new Vector2(16f, 16f),
                    0f,
                    100f,
                    0f,
                    0f
                );

            ellipse.SetFalloffShapeInternal(
                TerrainStampFalloffShape.Ellipse
            );

            ellipseData.AddHeightModifierInternal(
                ellipse
            );

            ellipseData.RepairModifierStableIds();

            if (
                TryComposeAndRead(
                    ellipseData,
                    seed,
                    Vector2Int.zero,
                    samplesPerSide,
                    1f,
                    16f,
                    new Vector2(16f, 16f),
                    false,
                    0f,
                    0f,
                    out float[] ellipseValues,
                    out _,
                    out _,
                    out string ellipseError
                )
            )
            {
                float center =
                    ellipseValues[
                        8 * samplesPerSide + 8
                    ];

                float corner =
                    ellipseValues[0];

                Add(
                    "Replace ellipse footprint",
                    ApproximatelyGpu(
                        center,
                        100f
                    )
                    &&
                    ApproximatelyGpu(
                        corner,
                        0f
                    ),
                    $"Center={center:R}, corner={corner:R}; expected 100/0."
                );
            }
            else
            {
                Add(
                    "Replace ellipse footprint",
                    false,
                    ellipseError
                );
            }
        }
        finally
        {
            Destroy(seed);
            Destroy(rectangleData);
            Destroy(ellipseData);
            Destroy(asset);
            Destroy(whiteTexture);
        }
    }

    private static void ValidateSourcePipeline()
    {
        Texture2D gradientTexture =
            CreateGradientTexture(
                9,
                "ReplaceGradient"
            );

        TerrainHeightStampAsset gradientAsset =
            CreateStampAsset(
                gradientTexture
            );

        Texture2D impulseTexture =
            CreateImpulseTexture(
                9,
                "ReplaceImpulse"
            );

        TerrainHeightStampAsset impulseAsset =
            CreateStampAsset(
                impulseTexture
            );

        try
        {
            ValidateOrientationCase(
                gradientAsset
            );

            ValidateSmoothingCase(
                impulseAsset
            );
        }
        finally
        {
            Destroy(gradientAsset);
            Destroy(gradientTexture);
            Destroy(impulseAsset);
            Destroy(impulseTexture);
        }
    }

    private static void ValidateOrientationCase(
        TerrainHeightStampAsset asset
    )
    {
        const int samplesPerSide = 17;

        float[] baseline = null;
        float[] flipX = null;
        float[] flipZ = null;
        float[] rotated = null;

        string errorMessage = "";

        if (
            !TryComposeOrientationVariant(
                asset,
                false,
                false,
                0f,
                out baseline,
                out errorMessage
            )
            ||
            !TryComposeOrientationVariant(
                asset,
                true,
                false,
                0f,
                out flipX,
                out errorMessage
            )
            ||
            !TryComposeOrientationVariant(
                asset,
                false,
                true,
                0f,
                out flipZ,
                out errorMessage
            )
            ||
            !TryComposeOrientationVariant(
                asset,
                false,
                false,
                180f,
                out rotated,
                out errorMessage
            )
        )
        {
            Add(
                "Replace source orientation / rotation",
                false,
                errorMessage
            );

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
            "Replace source orientation / rotation",
            passed,
            passed
                ? "Flip X, Flip Z, and 180-degree rotation reuse the existing shared source-coordinate pipeline."
                : $"baseline={baseline[a]:R}, flipX={flipX[mirrorX]:R}, flipZ={flipZ[mirrorZ]:R}, rotated={rotated[opposite]:R}."
        );
    }

    private static bool TryComposeOrientationVariant(
        TerrainHeightStampAsset asset,
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
                    TerrainHeightBlendMode.Replace,
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
                    0f,
                    "ReplaceOrientationSeed"
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
                    0f,
                    0f,
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

    private static void ValidateSmoothingCase(
        TerrainHeightStampAsset asset
    )
    {
        const int samplesPerSide = 17;

        float rawCenter;
        float smoothCenter;

        if (
            !TryComposeSmoothingVariant(
                asset,
                0f,
                out rawCenter,
                out string rawError
            )
        )
        {
            Add(
                "Replace smoothing pipeline",
                false,
                rawError
            );

            return;
        }

        if (
            !TryComposeSmoothingVariant(
                asset,
                2f,
                out smoothCenter,
                out string smoothError
            )
        )
        {
            Add(
                "Replace smoothing pipeline",
                false,
                smoothError
            );

            return;
        }

        bool passed =
            rawCenter > 90f
            &&
            smoothCenter > 0f
            &&
            smoothCenter <
                rawCenter - 0.01f;

        Add(
            "Replace smoothing pipeline",
            passed,
            $"Raw center={rawCenter:R}; smoothed center={smoothCenter:R}. Replace consumes the existing filtered source result."
        );
    }

    private static bool TryComposeSmoothingVariant(
        TerrainHeightStampAsset asset,
        float smoothingRadius,
        out float center,
        out string errorMessage
    )
    {
        const int samplesPerSide = 17;

        center =
            0f;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;

        try
        {
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    new Vector2(8f, 8f),
                    new Vector2(16f, 16f),
                    0f,
                    0f,
                    100f,
                    0f
                );

            stamp.SetSmoothingRadiusInternal(
                smoothingRadius
            );

            stamp.SetSmoothingStrengthInternal(
                1f
            );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    0f,
                    "ReplaceSmoothingSeed"
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
                    0f,
                    0f,
                    out float[] values,
                    out _,
                    out _,
                    out errorMessage
                )
            )
            {
                return false;
            }

            center =
                values[
                    8 * samplesPerSide + 8
                ];

            return true;
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static void ValidateOrdering()
    {
        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceOrderWhite"
            );

        TerrainHeightStampAsset asset =
            CreateStampAsset(
                whiteTexture
            );

        try
        {
            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Replace,
                100f,
                TerrainHeightBlendMode.Additive,
                0f,
                120f,
                "Replace -> Additive"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Additive,
                0f,
                TerrainHeightBlendMode.Replace,
                100f,
                100f,
                "Additive -> Replace"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Replace,
                100f,
                TerrainHeightBlendMode.Max,
                0f,
                120f,
                "Replace -> Max"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Max,
                0f,
                TerrainHeightBlendMode.Replace,
                100f,
                100f,
                "Max -> Replace"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Replace,
                100f,
                TerrainHeightBlendMode.Min,
                0f,
                40f,
                "Replace -> Min"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Min,
                0f,
                TerrainHeightBlendMode.Replace,
                100f,
                100f,
                "Min -> Replace"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Replace,
                100f,
                TerrainHeightBlendMode.Replace,
                30f,
                30f,
                "Replace -> Replace"
            );

            ValidateOrderCase(
                asset,
                TerrainHeightBlendMode.Replace,
                30f,
                TerrainHeightBlendMode.Replace,
                100f,
                100f,
                "Replace overlap reverse order"
            );

            ValidateMixedOrderCase(
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
        float firstReplaceTarget,
        TerrainHeightBlendMode secondMode,
        float secondReplaceTarget,
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
                CreateOrderStamp(
                    asset,
                    firstMode,
                    firstReplaceTarget
                )
            );

            data.AddHeightModifierInternal(
                CreateOrderStamp(
                    asset,
                    secondMode,
                    secondReplaceTarget
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "ReplaceOrderSeed"
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

    private static void ValidateMixedOrderCase(
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
            data.AddHeightModifierInternal(
                CreateOrderStamp(
                    asset,
                    TerrainHeightBlendMode.Additive,
                    0f
                )
            );

            data.AddHeightModifierInternal(
                CreateOrderStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    100f
                )
            );

            data.AddHeightModifierInternal(
                CreateOrderStamp(
                    asset,
                    TerrainHeightBlendMode.Max,
                    0f
                )
            );

            data.AddHeightModifierInternal(
                CreateOrderStamp(
                    asset,
                    TerrainHeightBlendMode.Min,
                    0f
                )
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "ReplaceMixedOrderSeed"
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
                Add(
                    "Mixed Additive/Replace/Max/Min ordering",
                    false,
                    errorMessage
                );

                return;
            }

            float center =
                values[
                    8 * samplesPerSide + 8
                ];

            Add(
                "Mixed Additive/Replace/Max/Min ordering",
                ApproximatelyGpu(
                    center,
                    40f
                ),
                $"Center={center:R}, expected 40 from Additive -> Replace -> Max -> Min."
            );
        }
        finally
        {
            Destroy(seed);
            Destroy(data);
        }
    }

    private static TerrainStampModifier CreateOrderStamp(
        TerrainHeightStampAsset asset,
        TerrainHeightBlendMode blendMode,
        float replaceTarget
    )
    {
        float heightDelta =
            blendMode == TerrainHeightBlendMode.Additive
                ? 20f
                : 0f;

        float targetBaseHeight =
            blendMode == TerrainHeightBlendMode.Max
                ? 120f
                :
                blendMode == TerrainHeightBlendMode.Min
                    ? 40f
                    :
                    blendMode == TerrainHeightBlendMode.Replace
                        ? replaceTarget
                        : 0f;

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

    private static void ValidateEnableDisableOutput()
    {
        const int samplesPerSide = 17;

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceEnableWhite"
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
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    new Vector2(8f, 8f),
                    new Vector2(12f, 12f),
                    0f,
                    100f,
                    0f,
                    0f
                );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "ReplaceEnableSeed"
                );

            float enabledCenter =
                ComposeCenterOrNaN(
                    data,
                    seed
                );

            stamp.SetEnabledInternal(
                false
            );

            float disabledCenter =
                ComposeCenterOrNaN(
                    data,
                    seed
                );

            stamp.SetEnabledInternal(
                true
            );

            float reenabledCenter =
                ComposeCenterOrNaN(
                    data,
                    seed
                );

            bool passed =
                ApproximatelyGpu(
                    enabledCenter,
                    100f
                )
                &&
                ApproximatelyGpu(
                    disabledCenter,
                    50f
                )
                &&
                ApproximatelyGpu(
                    reenabledCenter,
                    100f
                );

            Add(
                "Replace enable / disable output",
                passed,
                $"Enabled={enabledCenter:R}, disabled={disabledCenter:R}, re-enabled={reenabledCenter:R}; expected 100/50/100."
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

    private static float ComposeCenterOrNaN(
        TerrainAuthoringData data,
        Texture2D seed
    )
    {
        const int samplesPerSide = 17;

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
                out _
            )
        )
        {
            return float.NaN;
        }

        return
            values[
                8 * samplesPerSide + 8
            ];
    }

    private static void ValidateCrossTileSeam()
    {
        const int samplesPerSide = 17;
        const float tileWorldSize = 16f;

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceCrossTileWhite"
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
            TerrainStampModifier stamp =
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Replace,
                    new Vector2(16f, 8f),
                    new Vector2(18f, 12f),
                    0f,
                    100f,
                    0f,
                    0f
                );

            stamp.SetRotationDegreesInternal(
                35f
            );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            seed =
                CreateConstantTexture(
                    samplesPerSide,
                    0f,
                    "ReplaceCrossTileSeed"
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
                    "Rotated Replace cross-tile duplicated border",
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
                "Rotated Replace cross-tile duplicated border",
                passed,
                $"Left border={leftBorder:R}; right border={rightBorder:R}; expected equal 100."
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
                "ReplaceRangeWhite"
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
                        TerrainHeightBlendMode.Replace,
                        new Vector2(8f, 8f),
                        new Vector2(12f, 12f),
                        0f,
                        100f,
                        200f,
                        0f
                    )
                },
                40f,
                60f,
                40f,
                300f,
                "Replace conservative range raises maximum"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateStamp(
                        asset,
                        TerrainHeightBlendMode.Replace,
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
                "Replace conservative range lowers minimum"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateStamp(
                        asset,
                        TerrainHeightBlendMode.Replace,
                        new Vector2(8f, 8f),
                        new Vector2(12f, 12f),
                        0f,
                        300f,
                        -200f,
                        0.5f
                    )
                },
                40f,
                60f,
                40f,
                300f,
                "Replace negative target range metadata"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateOrderStamp(
                        asset,
                        TerrainHeightBlendMode.Replace,
                        100f
                    ),
                    CreateOrderStamp(
                        asset,
                        TerrainHeightBlendMode.Additive,
                        0f
                    )
                },
                40f,
                60f,
                40f,
                120f,
                "Replace -> Additive conservative range"
            );

            ValidateRangeCase(
                asset,
                new[]
                {
                    CreateOrderStamp(
                        asset,
                        TerrainHeightBlendMode.Additive,
                        0f
                    ),
                    CreateOrderStamp(
                        asset,
                        TerrainHeightBlendMode.Replace,
                        100f
                    )
                },
                40f,
                60f,
                40f,
                100f,
                "Additive -> Replace conservative range"
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
                    "ReplaceRangeSeed"
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
                $"Returned=[{compositeMinimum:R}, {compositeMaximum:R}], actual=[{actualMinimum:R}, {actualMaximum:R}], range/no-range max diff={maximumDifference:R}."
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
                "ReplaceUnsupportedWhite"
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
                    "ReplaceUnsupportedSeed"
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
                "Unsupported blend modes still fail explicitly",
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
                "Replace preview/runtime shared compositor parity",
                false,
                "Current world layout is not valid for a first-tile runtime parity test."
            );

            return;
        }

        Texture2D whiteTexture =
            CreateConstantTexture(
                8,
                1f,
                "ReplaceRuntimeWhite"
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
                    TerrainHeightBlendMode.Replace,
                    center,
                    size,
                    0f,
                    90f,
                    0f,
                    0.2f
                )
            );

            data.AddHeightModifierInternal(
                CreateStamp(
                    asset,
                    TerrainHeightBlendMode.Max,
                    center,
                    size,
                    0f,
                    110f,
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
                    100f,
                    0f,
                    0.2f
                )
            );

            data.RepairModifierStableIds();

            committed =
                CreateConstantTexture(
                    samplesPerSide,
                    50f,
                    "ReplaceRuntimeCommitted"
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
                    true,
                    50f,
                    50f,
                    out float[] previewPath,
                    out _,
                    out _,
                    out string previewError
                )
            )
            {
                Add(
                    "Replace preview/runtime shared compositor parity",
                    false,
                    previewError
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
                        "Replace preview/runtime shared compositor parity",
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
                        "Replace preview/runtime shared compositor parity",
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
                        "Replace preview/runtime shared compositor parity",
                        false,
                        runtimeError
                    );

                    return;
                }
            }

            float maximumDifference =
                CalculateMaximumDifference(
                    previewPath,
                    runtime
                );

            Add(
                "Replace preview/runtime shared compositor parity",
                maximumDifference <=
                    GpuTolerance,
                $"Range-aware preview compositor vs runtime composition-context maximum difference={maximumDifference:R}."
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
                "Replace mutation setup",
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
                    TerrainHeightBlendMode.Replace,
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
                TerrainHeightBlendMode.Replace
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
            "Replace blend-mode service mutation",
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
            "Replace enable / disable invalidation",
            enableDisablePassed,
            enableDisablePassed
                ? "Disable and enable both dirtied the Replace footprint through the established change tracker."
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

        if (stamp == null)
        {
            Add(
                "Undo/Redo Replace setup",
                false,
                "The mutation test modifier could not be found after resetting to Additive."
            );

            return false;
        }

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
                    TerrainHeightBlendMode.Replace,
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
                "Undo/Redo Replace setup",
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
            "Undo/Redo Replace setup",
            undoExpectedBlendMode ==
                TerrainHeightBlendMode.Additive
            &&
            redoExpectedBlendMode ==
                TerrainHeightBlendMode.Replace
            &&
            redoExpectedRevision ==
                undoExpectedRevision + 1
            &&
            redoExpectedSignature !=
                undoExpectedSignature,
            "A final Additive -> Replace service mutation was recorded as one Undo step."
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
                "Undo restores pre-Replace mode",
                passed,
                passed
                    ? "Undo restored Additive mode, revision, and deterministic modifier signature."
                    : "Undo did not restore the expected pre-Replace state."
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
                "Redo restores Replace mode",
                passed,
                passed
                    ? "Redo restored Replace mode, revision, and deterministic modifier signature."
                    : "Redo did not restore the expected Replace state."
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
            CreateNumericTexture(
                size,
                name,
                FilterMode.Bilinear
            );

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
            CreateNumericTexture(
                size,
                name,
                FilterMode.Bilinear
            );

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

    private static Texture2D CreateImpulseTexture(
        int size,
        string name
    )
    {
        Texture2D texture =
            CreateNumericTexture(
                size,
                name,
                FilterMode.Bilinear
            );

        float[] values =
            new float[
                size * size
            ];

        int center =
            size / 2;

        values[
            center * size + center
        ] =
            1f;

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
            CreateNumericTexture(
                size,
                name,
                FilterMode.Point
            );

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

    private static Texture2D CreateNumericTexture(
        int size,
        string name,
        FilterMode filterMode
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
            filterMode;

        texture.anisoLevel =
            0;

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
                    "AsyncGPUReadback failed for the temporary Replace composition target.";

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
            a.Length != b.Length
        )
        {
            return
                float.PositiveInfinity;
        }

        float maximum =
            0f;

        for (
            int index = 0;
            index < a.Length;
            index++
        )
        {
            maximum =
                Mathf.Max(
                    maximum,
                    Mathf.Abs(
                        a[index] -
                        b[index]
                    )
                );
        }

        return maximum;
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
                "WorldMeshes Replace Blend Mode validation\n\n" +
                builder
            );
        }
        else
        {
            Debug.Log(
                "WorldMeshes Replace Blend Mode validation\n\n" +
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
            !float.IsNaN(a)
            &&
            !float.IsInfinity(a)
            &&
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
            UnityEngine.Object.DestroyImmediate(
                value
            );
        }
    }
}
