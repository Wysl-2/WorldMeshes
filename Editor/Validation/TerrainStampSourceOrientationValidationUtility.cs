using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainStampSourceOrientationValidationUtility
{
    private sealed class Result
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    private sealed class TestContext :
        IDisposable
    {
        public TerrainAuthoringData AuthoringData;
        public TerrainStampModifier Stamp;
        public TerrainHeightStampAsset StampAsset;
        public Texture2D StampTexture;
        public Texture2D CommittedTile;

        public int SamplesPerSide;
        public int CenterIndex;
        public int HalfStampSamples;

        public float SampleSpacing;
        public float TileWorldSize;

        public Vector2 WorldSizeXZ;

        public void Dispose()
        {
            if (AuthoringData != null)
            {
                TerrainAuthoringModifierChangeTracker
                    .Forget(
                        AuthoringData
                    );
            }

            Destroy(CommittedTile);
            Destroy(StampTexture);
            Destroy(StampAsset);
            Destroy(AuthoringData);
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

    private const float MathTolerance =
        0.0001f;

    private const float GpuTolerance =
        0.001f;

    private const float SmoothingTolerance =
        0.003f;

    private static readonly List<Result>
        results =
            new List<Result>();

    private static bool validationScheduled;
    private static bool validationRunning;

    private static int lastPassedCount;
    private static int lastFailedCount;

    private static string lastSummary =
        "Not run.";

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

        validationScheduled =
            false;

        validationRunning =
            true;

        results.Clear();

        try
        {
            RunValidation();
        }
        catch (Exception exception)
        {
            Add(
                "Unexpected validation exception",
                false,
                exception.ToString()
            );
        }
        finally
        {
            Finish();
        }
    }

    private static void RunValidation()
    {
        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "Run while the editor is idle in Edit Mode."
            );

            return;
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

            return;
        }

        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (worldSettings == null)
        {
            Add(
                "Validation prerequisites",
                false,
                "WorldSettings could not be loaded."
            );

            return;
        }

        ValidateDefaultsAndBounds();
        ValidateSignature();

        bool gpuSupported =
            SystemInfo.supportsComputeShaders
            &&
            SystemInfo.supports2DArrayTextures
            &&
            SystemInfo.supportsAsyncGPUReadback
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
                "GPU/runtime source-orientation validation",
                false,
                "RFloat, compute shaders, 2D texture arrays, or AsyncGPUReadback are unavailable."
            );

            ValidateDuplicate(
                worldSettings
            );

            return;
        }

        TestContext context = null;

        try
        {
            if (
                !TryCreateContext(
                    worldSettings,
                    out context,
                    out string contextError
                )
            )
            {
                Add(
                    "Transient source-orientation test context",
                    false,
                    contextError
                );

                ValidateDuplicate(
                    worldSettings
                );

                return;
            }

            ValidateGpuMirrors(
                context
            );

            ValidateRotatedLocalFlip(
                context
            );

            ValidateSmoothing(
                context
            );

            ValidateRuntimeParity(
                worldSettings,
                context
            );
        }
        finally
        {
            context?.Dispose();
        }

        ValidateDuplicate(
            worldSettings
        );
    }

    private static void ValidateDefaultsAndBounds()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        stamp.SetPositionXZInternal(
            new Vector2(
                120f,
                240f
            )
        );

        stamp.SetSizeXZInternal(
            new Vector2(
                80f,
                40f
            )
        );

        stamp.SetRotationDegreesInternal(
            37f
        );

        Bounds baselineBounds =
            stamp.GetAffectedWorldBounds();

        bool defaultsPassed =
            !stamp.FlipX
            &&
            !stamp.FlipZ;

        stamp.SetFlipXInternal(
            true
        );

        stamp.SetFlipZInternal(
            true
        );

        Bounds flippedBounds =
            stamp.GetAffectedWorldBounds();

        bool boundsPassed =
            BoundsApprox(
                baselineBounds,
                flippedBounds
            );

        Add(
            "Default source orientation",
            defaultsPassed,
            defaultsPassed
                ? "New stamps default to FlipX=false and FlipZ=false."
                : "One or both source-orientation flags did not default to false."
        );

        Add(
            "Flip-independent spatial bounds",
            boundsPassed,
            boundsPassed
                ? "Flip state leaves rotated GetAffectedWorldBounds() unchanged."
                : "Flip state incorrectly changed the spatial stamp Bounds."
        );
    }

    private static void ValidateSignature()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        stamp.SetSizeXZInternal(
            new Vector2(
                40f,
                20f
            )
        );

        stamp.SetRotationDegreesInternal(
            37f
        );

        string baseline =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFlipXInternal(
            true
        );

        string flipX =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFlipXInternal(
            false
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFlipZInternal(
            true
        );

        string flipZ =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFlipXInternal(
            true
        );

        string both =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool passed =
            !string.IsNullOrEmpty(
                baseline
            )
            &&
            baseline != flipX
            &&
            baseline == restored
            &&
            baseline != flipZ
            &&
            both != flipX
            &&
            both != flipZ;

        Add(
            "Source-orientation deterministic signature",
            passed,
            passed
                ? "FlipX/FlipZ affect output identity and false/false restores the original signature."
                : "Flip source-orientation signature behavior is incorrect."
        );
    }

    private static void ValidateGpuMirrors(
        TestContext context
    )
    {
        ResetSamplingState(
            context,
            0f,
            0f,
            0f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] baseline,
                out string baselineError
            )
        )
        {
            Add(
                "GPU Flip X / Flip Z mapping",
                false,
                "Baseline composition failed: " +
                baselineError
            );

            return;
        }

        context.Stamp.SetFlipXInternal(
            true
        );

        if (
            !TryComposeDirect(
                context,
                out float[] flipX,
                out string flipXError
            )
        )
        {
            Add(
                "GPU Flip X / Flip Z mapping",
                false,
                "Flip X composition failed: " +
                flipXError
            );

            return;
        }

        context.Stamp.SetFlipXInternal(
            false
        );

        context.Stamp.SetFlipZInternal(
            true
        );

        if (
            !TryComposeDirect(
                context,
                out float[] flipZ,
                out string flipZError
            )
        )
        {
            Add(
                "GPU Flip X / Flip Z mapping",
                false,
                "Flip Z composition failed: " +
                flipZError
            );

            return;
        }

        context.Stamp.SetFlipXInternal(
            true
        );

        if (
            !TryComposeDirect(
                context,
                out float[] both,
                out string bothError
            )
        )
        {
            Add(
                "GPU Flip X / Flip Z mapping",
                false,
                "Combined Flip X/Z composition failed: " +
                bothError
            );

            return;
        }

        context.Stamp.SetFlipXInternal(
            false
        );

        context.Stamp.SetFlipZInternal(
            false
        );

        if (
            !TryComposeDirect(
                context,
                out float[] restored,
                out string restoredError
            )
        )
        {
            Add(
                "GPU Flip X / Flip Z mapping",
                false,
                "Restored false/false composition failed: " +
                restoredError
            );

            return;
        }

        bool flipXMapped =
            CompareMirroredRegion(
                context,
                flipX,
                baseline,
                true,
                false,
                GpuTolerance
            );

        bool flipZMapped =
            CompareMirroredRegion(
                context,
                flipZ,
                baseline,
                false,
                true,
                GpuTolerance
            );

        bool bothMapped =
            CompareMirroredRegion(
                context,
                both,
                baseline,
                true,
                true,
                GpuTolerance
            );

        bool restoredMapped =
            ArraysEqual(
                baseline,
                restored,
                GpuTolerance
            );

        float visibleDifference =
            MaxDifferenceInStamp(
                context,
                baseline,
                flipX
            );

        bool passed =
            flipXMapped
            &&
            flipZMapped
            &&
            bothMapped
            &&
            restoredMapped
            &&
            visibleDifference >
                0.03f;

        Add(
            "GPU Flip X / Flip Z mapping",
            passed,
            passed
                ? "Asymmetric source mirrors correctly on local X, local Z, both axes, and returns exactly to false/false output."
                : "One or more source mirror mappings failed or the asymmetric source did not visibly change."
        );
    }

    private static void ValidateRotatedLocalFlip(
        TestContext context
    )
    {
        ResetSamplingState(
            context,
            90f,
            0f,
            0f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] baseline,
                out string baselineError
            )
        )
        {
            Add(
                "Rotated local-axis Flip X",
                false,
                "90-degree baseline composition failed: " +
                baselineError
            );

            return;
        }

        context.Stamp.SetFlipXInternal(
            true
        );

        if (
            !TryComposeDirect(
                context,
                out float[] flipped,
                out string flippedError
            )
        )
        {
            Add(
                "Rotated local-axis Flip X",
                false,
                "90-degree Flip X composition failed: " +
                flippedError
            );

            return;
        }

        int step =
            Mathf.Max(
                1,
                context.HalfStampSamples /
                    8
            );

        bool mapped =
            true;

        float wrongWorldXDifference =
            0f;

        for (
            int dz =
                -context.HalfStampSamples + 1;
            dz <=
                context.HalfStampSamples - 1;
            dz += step
        )
        {
            for (
                int dx =
                    -context.HalfStampSamples + 1;
                dx <=
                    context.HalfStampSamples - 1;
                dx += step
            )
            {
                int x =
                    context.CenterIndex +
                    dx;

                int z =
                    context.CenterIndex +
                    dz;

                int expectedX =
                    context.CenterIndex +
                    dx;

                int expectedZ =
                    context.CenterIndex -
                    dz;

                int wrongX =
                    context.CenterIndex -
                    dx;

                int wrongZ =
                    context.CenterIndex +
                    dz;

                if (
                    !InRange(
                        x,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        z,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        expectedX,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        expectedZ,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        wrongX,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        wrongZ,
                        context.SamplesPerSide
                    )
                )
                {
                    continue;
                }

                float actual =
                    flipped[
                        z *
                        context.SamplesPerSide +
                        x
                    ];

                float expected =
                    baseline[
                        expectedZ *
                        context.SamplesPerSide +
                        expectedX
                    ];

                if (
                    Mathf.Abs(
                        actual -
                        expected
                    )
                    >
                    GpuTolerance
                )
                {
                    mapped =
                        false;

                    break;
                }

                float wrongWorldX =
                    baseline[
                        wrongZ *
                        context.SamplesPerSide +
                        wrongX
                    ];

                wrongWorldXDifference =
                    Mathf.Max(
                        wrongWorldXDifference,
                        Mathf.Abs(
                            actual -
                            wrongWorldX
                        )
                    );
            }

            if (!mapped)
            {
                break;
            }
        }

        bool passed =
            mapped
            &&
            wrongWorldXDifference >
                0.03f;

        Add(
            "Rotated local-axis Flip X",
            passed,
            passed
                ? "At +90 degrees, Flip X mirrors along the rotated local-X axis (world Z), not world X."
                : "Flip X did not follow the stamp-local axis after rotation."
        );
    }

    private static void ValidateSmoothing(
        TestContext context
    )
    {
        ResetSamplingState(
            context,
            0f,
            context.SampleSpacing *
                2.5f,
            1f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] baseline,
                out string baselineError
            )
        )
        {
            Add(
                "Flipped source smoothing",
                false,
                "Smoothed baseline composition failed: " +
                baselineError
            );

            return;
        }

        context.Stamp.SetFlipXInternal(
            true
        );

        if (
            !TryComposeDirect(
                context,
                out float[] flipped,
                out string flippedError
            )
        )
        {
            Add(
                "Flipped source smoothing",
                false,
                "Smoothed Flip X composition failed: " +
                flippedError
            );

            return;
        }

        bool passed =
            CompareMirroredRegion(
                context,
                flipped,
                baseline,
                true,
                false,
                SmoothingTolerance
            );

        Add(
            "Flipped source smoothing",
            passed,
            passed
                ? "The established Gaussian/adaptive sampling path mirrors with the source and keeps its existing semantics."
                : "Smoothed source output did not mirror within tolerance."
        );
    }

    private static void ValidateRuntimeParity(
        WorldSettings worldSettings,
        TestContext context
    )
    {
        ResetSamplingState(
            context,
            37f,
            context.SampleSpacing *
                1.5f,
            0.65f
        );

        context.Stamp.SetFlipXInternal(
            true
        );

        context.Stamp.SetFlipZInternal(
            false
        );

        if (
            !TryComposeDirect(
                context,
                out float[] direct,
                out string directError
            )
        )
        {
            Add(
                "Runtime/shared compositor flip parity",
                false,
                "Direct composition failed: " +
                directError
            );

            return;
        }

        TerrainRuntimeHeightCompositionContext runtime =
            new TerrainRuntimeHeightCompositionContext();

        try
        {
            if (
                !runtime.TryPrepare(
                    worldSettings,
                    context.AuthoringData,
                    out string prepareError
                )
            )
            {
                Add(
                    "Runtime/shared compositor flip parity",
                    false,
                    "Runtime preparation failed: " +
                    prepareError
                );

                return;
            }

            if (
                !runtime.RequiresComposition(
                    Vector2Int.zero
                )
            )
            {
                Add(
                    "Runtime/shared compositor flip parity",
                    false,
                    "The rotated/flipped stamp was not classified as affecting tile (0, 0)."
                );

                return;
            }

            float[] runtimeOutput =
                new float[
                    context.SamplesPerSide *
                    context.SamplesPerSide
                ];

            if (
                !runtime.TryComposeCommittedTile(
                    context.CommittedTile,
                    Vector2Int.zero,
                    runtimeOutput,
                    out string composeError
                )
            )
            {
                Add(
                    "Runtime/shared compositor flip parity",
                    false,
                    "Runtime composition failed: " +
                    composeError
                );

                return;
            }

            bool passed =
                ArraysEqual(
                    direct,
                    runtimeOutput,
                    GpuTolerance
                );

            Add(
                "Runtime/shared compositor flip parity",
                passed,
                passed
                    ? "Runtime context and direct shared compositor match for Rotation=37, FlipX=true, and nonzero smoothing."
                    : "Runtime and direct shared-compositor output differ beyond tolerance."
            );
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private static void ValidateDuplicate(
        WorldSettings worldSettings
    )
    {
        TerrainAuthoringData authoringData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        try
        {
            TerrainStampModifier source =
                new TerrainStampModifier();

            source.SetRotationDegreesInternal(
                37f
            );

            source.SetFlipXInternal(
                true
            );

            source.SetFlipZInternal(
                false
            );

            authoringData
                .AddHeightModifierInternal(
                    source
                );

            authoringData
                .RepairModifierStableIds();

            string sourceStableId =
                source.StableId;

            bool duplicated =
                TerrainAuthoringModifierService
                    .DuplicateModifier(
                        authoringData,
                        worldSettings,
                        sourceStableId,
                        out string duplicateStableId,
                        out string duplicateError
                    );

            TerrainStampModifier duplicate =
                null;

            if (duplicated)
            {
                IReadOnlyList<TerrainHeightModifier>
                    modifiers =
                        authoringData.HeightModifiers;

                for (
                    int index = 0;
                    index < modifiers.Count;
                    index++
                )
                {
                    if (
                        modifiers[index] is
                            TerrainStampModifier candidate
                        &&
                        candidate.StableId ==
                            duplicateStableId
                    )
                    {
                        duplicate =
                            candidate;

                        break;
                    }
                }
            }

            bool passed =
                duplicated
                &&
                duplicate != null
                &&
                duplicate.StableId !=
                    sourceStableId
                &&
                duplicate.FlipX ==
                    source.FlipX
                &&
                duplicate.FlipZ ==
                    source.FlipZ
                &&
                Mathf.Approximately(
                    duplicate.RotationDegrees,
                    source.RotationDegrees
                );

            Add(
                "Production duplicate preserves source orientation",
                passed,
                passed
                    ? "DuplicateModifier preserves FlipX, FlipZ, and RotationDegrees while assigning a new StableId."
                    : "DuplicateModifier did not preserve source orientation. " +
                        (duplicateError ?? "")
            );
        }
        catch (Exception exception)
        {
            Add(
                "Production duplicate preserves source orientation",
                false,
                exception.Message
            );
        }
        finally
        {
            Undo.ClearUndo(
                authoringData
            );

            TerrainAuthoringModifierChangeTracker
                .Forget(
                    authoringData
                );

            UnityEngine.Object
                .DestroyImmediate(
                    authoringData
                );
        }
    }

    private static void ResetSamplingState(
        TestContext context,
        float rotationDegrees,
        float smoothingRadius,
        float smoothingStrength
    )
    {
        context.Stamp
            .SetRotationDegreesInternal(
                rotationDegrees
            );

        context.Stamp
            .SetFlipXInternal(
                false
            );

        context.Stamp
            .SetFlipZInternal(
                false
            );

        context.Stamp
            .SetSmoothingRadiusInternal(
                smoothingRadius
            );

        context.Stamp
            .SetSmoothingStrengthInternal(
                smoothingStrength
            );
    }

    private static bool TryCreateContext(
        WorldSettings worldSettings,
        out TestContext context,
        out string errorMessage
    )
    {
        context =
            null;

        errorMessage =
            "";

        int samples =
            worldSettings
                .HeightTileSamplesPerSide;

        float spacing =
            worldSettings.chunkSize
            /
            Mathf.Max(
                1,
                worldSettings
                    .heightfieldResolutionPerChunk
            );

        float tileSize =
            worldSettings
                .HeightTileWorldSize;

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            samples < 17
            ||
            spacing <= 0f
            ||
            tileSize <= 0f
            ||
            worldSize.x <
                tileSize
            ||
            worldSize.y <
                tileSize
        )
        {
            errorMessage =
                "Current world/height-tile layout is invalid for transient validation.";

            return false;
        }

        int centerIndex =
            (
                samples -
                1
            )
            /
            2;

        int maximumRotatedHalfSamples =
            Mathf.Max(
                3,
                Mathf.FloorToInt(
                    (
                        centerIndex -
                        2
                    )
                    /
                    1.5f
                )
            );

        int halfSamples =
            Mathf.Max(
                3,
                Mathf.Min(
                    24,
                    maximumRotatedHalfSamples
                )
            );

        float stampSize =
            halfSamples *
            2f *
            spacing;

        TestContext created =
            new TestContext();

        try
        {
            created.SamplesPerSide =
                samples;

            created.CenterIndex =
                centerIndex;

            created.HalfStampSamples =
                halfSamples;

            created.SampleSpacing =
                spacing;

            created.TileWorldSize =
                tileSize;

            created.WorldSizeXZ =
                worldSize;

            created.StampTexture =
                CreateAsymmetricTexture();

            created.StampAsset =
                ScriptableObject
                    .CreateInstance<TerrainHeightStampAsset>();

            created.StampAsset
                .SetHeightTextureInternal(
                    created.StampTexture
                );

            created.Stamp =
                new TerrainStampModifier();

            created.Stamp
                .SetStampAssetInternal(
                    created.StampAsset
                );

            created.Stamp
                .SetPositionXZInternal(
                    new Vector2(
                        centerIndex *
                            spacing,
                        centerIndex *
                            spacing
                    )
                );

            created.Stamp
                .SetSizeXZInternal(
                    new Vector2(
                        stampSize,
                        stampSize
                    )
                );

            created.Stamp
                .SetRotationDegreesInternal(
                    0f
                );

            created.Stamp
                .SetFlipXInternal(
                    false
                );

            created.Stamp
                .SetFlipZInternal(
                    false
                );

            created.Stamp
                .SetHeightDeltaInternal(
                    1f
                );

            created.Stamp
                .SetFalloffInternal(
                    0f
                );

            created.Stamp
                .SetSmoothingRadiusInternal(
                    0f
                );

            created.Stamp
                .SetSmoothingStrengthInternal(
                    1f
                );

            created.Stamp
                .SetBlendModeInternal(
                    TerrainHeightBlendMode
                        .Additive
                );

            created.Stamp
                .SetEnabledInternal(
                    true
                );

            created.AuthoringData =
                ScriptableObject
                    .CreateInstance<TerrainAuthoringData>();

            created.AuthoringData
                .AddHeightModifierInternal(
                    created.Stamp
                );

            created.AuthoringData
                .RepairModifierStableIds();

            created.CommittedTile =
                CreateZeroTile(
                    samples
                );

            context =
                created;

            return true;
        }
        catch (Exception exception)
        {
            created.Dispose();

            errorMessage =
                "Could not create transient source-orientation resources.\n\n" +
                exception.Message;

            return false;
        }
    }

    private static Texture2D CreateAsymmetricTexture()
    {
        const int size =
            7;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RFloat,
                false,
                true
            );

        texture.name =
            "WorldMeshes Source Orientation Validation Stamp";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        float[] values =
            new float[
                size *
                size
            ];

        for (
            int z = 0;
            z < size;
            z++
        )
        {
            float v =
                z /
                (
                    (float)(
                        size -
                        1
                    )
                );

            for (
                int x = 0;
                x < size;
                x++
            )
            {
                float u =
                    x /
                    (
                        (float)(
                            size -
                            1
                        )
                    );

                values[
                    z *
                    size +
                    x
                ] =
                    0.05f
                    +
                    0.50f *
                        u
                    +
                    0.25f *
                        v
                    +
                    0.15f *
                        u *
                        v;
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

    private static Texture2D CreateZeroTile(
        int samples
    )
    {
        Texture2D texture =
            new Texture2D(
                samples,
                samples,
                TextureFormat.RFloat,
                false,
                true
            );

        texture.name =
            "WorldMeshes Source Orientation Validation Base";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Point;

        texture.SetPixelData(
            new float[
                samples *
                samples
            ],
            0
        );

        texture.Apply(
            false,
            false
        );

        return texture;
    }

    private static bool TryComposeDirect(
        TestContext context,
        out float[] output,
        out string errorMessage
    )
    {
        output =
            null;

        errorMessage =
            "";

        RenderTexture target =
            null;

        try
        {
            target =
                new RenderTexture(
                    context.SamplesPerSide,
                    context.SamplesPerSide,
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

            if (!target.Create())
            {
                errorMessage =
                    "Could not create transient RFloat composition target.";

                return false;
            }

            Graphics.CopyTexture(
                context.CommittedTile,
                0,
                0,
                target,
                0,
                0
            );

            TerrainHeightCompositor compositor =
                new TerrainHeightCompositor();

            if (
                !compositor.TryComposeTile(
                    target,
                    Vector2Int.zero,
                    0,
                    context.SamplesPerSide,
                    context.SampleSpacing,
                    context.TileWorldSize,
                    context.WorldSizeXZ,
                    context.AuthoringData,
                    out errorMessage
                )
            )
            {
                return false;
            }

            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    target,
                    0,
                    0,
                    context.SamplesPerSide,
                    0,
                    context.SamplesPerSide,
                    0,
                    1,
                    TextureFormat.RFloat,
                    null
                );

            request.WaitForCompletion();

            if (request.hasError)
            {
                errorMessage =
                    "Async GPU readback failed.";

                return false;
            }

            var data =
                request.GetData<float>();

            output =
                new float[
                    data.Length
                ];

            for (
                int index = 0;
                index < data.Length;
                index++
            )
            {
                output[index] =
                    data[index];
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                exception.Message;

            return false;
        }
        finally
        {
            if (target != null)
            {
                if (target.IsCreated())
                {
                    target.Release();
                }

                UnityEngine.Object
                    .DestroyImmediate(
                        target
                    );
            }
        }
    }

    private static bool CompareMirroredRegion(
        TestContext context,
        float[] actual,
        float[] baseline,
        bool mirrorX,
        bool mirrorZ,
        float tolerance
    )
    {
        if (
            actual == null
            ||
            baseline == null
            ||
            actual.Length !=
                baseline.Length
        )
        {
            return false;
        }

        int step =
            Mathf.Max(
                1,
                context.HalfStampSamples /
                    10
            );

        for (
            int dz =
                -context.HalfStampSamples + 1;
            dz <=
                context.HalfStampSamples - 1;
            dz += step
        )
        {
            for (
                int dx =
                    -context.HalfStampSamples + 1;
                dx <=
                    context.HalfStampSamples - 1;
                dx += step
            )
            {
                int x =
                    context.CenterIndex +
                    dx;

                int z =
                    context.CenterIndex +
                    dz;

                int expectedX =
                    context.CenterIndex +
                    (
                        mirrorX
                            ? -dx
                            : dx
                    );

                int expectedZ =
                    context.CenterIndex +
                    (
                        mirrorZ
                            ? -dz
                            : dz
                    );

                if (
                    !InRange(
                        x,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        z,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        expectedX,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        expectedZ,
                        context.SamplesPerSide
                    )
                )
                {
                    continue;
                }

                float actualValue =
                    actual[
                        z *
                        context.SamplesPerSide +
                        x
                    ];

                float expectedValue =
                    baseline[
                        expectedZ *
                        context.SamplesPerSide +
                        expectedX
                    ];

                if (
                    Mathf.Abs(
                        actualValue -
                        expectedValue
                    )
                    >
                    tolerance
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static float MaxDifferenceInStamp(
        TestContext context,
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
            return 0f;
        }

        float maximum =
            0f;

        int step =
            Mathf.Max(
                1,
                context.HalfStampSamples /
                    10
            );

        for (
            int dz =
                -context.HalfStampSamples + 1;
            dz <=
                context.HalfStampSamples - 1;
            dz += step
        )
        {
            for (
                int dx =
                    -context.HalfStampSamples + 1;
                dx <=
                    context.HalfStampSamples - 1;
                dx += step
            )
            {
                int x =
                    context.CenterIndex +
                    dx;

                int z =
                    context.CenterIndex +
                    dz;

                if (
                    !InRange(
                        x,
                        context.SamplesPerSide
                    )
                    ||
                    !InRange(
                        z,
                        context.SamplesPerSide
                    )
                )
                {
                    continue;
                }

                int index =
                    z *
                    context.SamplesPerSide +
                    x;

                maximum =
                    Mathf.Max(
                        maximum,
                        Mathf.Abs(
                            a[index] -
                            b[index]
                        )
                    );
            }
        }

        return maximum;
    }

    private static bool ArraysEqual(
        float[] a,
        float[] b,
        float tolerance
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
            return false;
        }

        for (
            int index = 0;
            index < a.Length;
            index++
        )
        {
            if (
                Mathf.Abs(
                    a[index] -
                    b[index]
                )
                >
                tolerance
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool BoundsApprox(
        Bounds a,
        Bounds b
    )
    {
        return
            Approx(
                a.center.x,
                b.center.x
            )
            &&
            Approx(
                a.center.y,
                b.center.y
            )
            &&
            Approx(
                a.center.z,
                b.center.z
            )
            &&
            Approx(
                a.size.x,
                b.size.x
            )
            &&
            Approx(
                a.size.y,
                b.size.y
            )
            &&
            Approx(
                a.size.z,
                b.size.z
            );
    }

    private static bool InRange(
        int value,
        int length
    )
    {
        return
            value >= 0
            &&
            value < length;
    }

    private static bool Approx(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a -
                b
            )
            <=
            MathTolerance;
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
                Name =
                    name,

                Passed =
                    passed,

                Details =
                    details ??
                    ""
            }
        );
    }

    private static void Finish()
    {
        validationRunning =
            false;

        lastPassedCount =
            0;

        lastFailedCount =
            0;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

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

            builder.Append(
                result.Name
            );

            if (
                !string.IsNullOrEmpty(
                    result.Details
                )
            )
            {
                builder.Append(
                    "\n"
                );

                builder.Append(
                    result.Details
                );
            }

            if (
                index <
                results.Count -
                    1
            )
            {
                builder.Append(
                    "\n\n"
                );
            }
        }

        lastSummary =
            builder.Length > 0
                ? builder.ToString()
                : "No validation results were produced.";

        if (lastFailedCount > 0)
        {
            Debug.LogError(
                "Stamp source-orientation validation failed.\n\n" +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp source-orientation validation passed.\n\n" +
                lastSummary
            );
        }

        SceneView.RepaintAll();
    }
}
