using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainStampFalloffValidationUtility
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
        public Texture2D ConstantTexture;
        public Texture2D AsymmetricTexture;
        public Texture2D CommittedTile;
        public int SamplesPerSide;
        public int CenterIndex;
        public float SampleSpacing;
        public float TileWorldSize;
        public Vector2 WorldSizeXZ;

        public void Dispose()
        {
            if (AuthoringData != null)
            {
                TerrainAuthoringModifierChangeTracker
                    .Forget(AuthoringData);
            }

            Destroy(CommittedTile);
            Destroy(ConstantTexture);
            Destroy(AsymmetricTexture);
            Destroy(StampAsset);
            Destroy(AuthoringData);
        }

        private static void Destroy(
            UnityEngine.Object value
        )
        {
            if (value != null)
            {
                UnityEngine.Object
                    .DestroyImmediate(value);
            }
        }
    }

    private const float MathTolerance =
        0.0001f;

    private const float GpuTolerance =
        0.003f;

    private const float RatioTolerance =
        0.008f;

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

        validationScheduled = true;

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

        validationScheduled = false;
        validationRunning = true;
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

        ValidateUtility();
        ValidateRectangleCompatibility();
        ValidateModifierContract();
        ValidateSignature();
        ValidateDiscreteMutations(
            worldSettings
        );
        ValidateDuplicate(
            worldSettings
        );

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
                "GPU/runtime falloff validation",
                false,
                "Required RFloat/compute/2D-array/AsyncGPUReadback support is unavailable."
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
                    "Transient falloff test context",
                    false,
                    contextError
                );

                return;
            }

            ValidateGpuCases(context);
            ValidateSourceCoordinateIndependence(
                context
            );
            ValidateRotatedEllipse(context);
            ValidateRuntimeParity(
                worldSettings,
                context
            );
        }
        finally
        {
            context?.Dispose();
        }
    }

    private static void ValidateUtility()
    {
        bool sanitization =
            TerrainStampFalloffUtility
                .SanitizeShape(
                    (TerrainStampFalloffShape)999
                )
            ==
            TerrainStampFalloffShape.Rectangle
            &&
            TerrainStampFalloffUtility
                .SanitizeProfile(
                    (TerrainStampFalloffProfile)999
                )
            ==
            TerrainStampFalloffProfile.Smooth;

        float smooth =
            TerrainStampFalloffUtility
                .EvaluateProfile(
                    0.5f,
                    TerrainStampFalloffProfile.Smooth
                );

        float linear =
            TerrainStampFalloffUtility
                .EvaluateProfile(
                    0.25f,
                    TerrainStampFalloffProfile.Linear
                );

        float sharp =
            TerrainStampFalloffUtility
                .EvaluateProfile(
                    0.25f,
                    TerrainStampFalloffProfile.Sharp
                );

        float hardEllipseCorner =
            TerrainStampFalloffUtility
                .Evaluate(
                    Vector2.one,
                    TerrainStampFalloffShape.Ellipse,
                    TerrainStampFalloffProfile.Smooth,
                    0f
                );

        float hardEllipseBoundary =
            TerrainStampFalloffUtility
                .Evaluate(
                    new Vector2(
                        1f,
                        0.5f
                    ),
                    TerrainStampFalloffShape.Ellipse,
                    TerrainStampFalloffProfile.Smooth,
                    0f
                );

        bool passed =
            sanitization
            &&
            Approx(smooth, 0.5f)
            &&
            Approx(linear, 0.25f)
            &&
            Approx(sharp, 0.4375f)
            &&
            sharp > linear
            &&
            Approx(
                hardEllipseCorner,
                0f
            )
            &&
            Approx(
                hardEllipseBoundary,
                1f
            );

        Add(
            "Falloff utility contract",
            passed,
            passed
                ? "Enum sanitization, Smooth/Linear/Sharp, and hard Ellipse membership match the contract."
                : "Falloff utility reference behavior is incorrect."
        );
    }

    private static void ValidateRectangleCompatibility()
    {
        float[] falloffs =
        {
            0f,
            0.1f,
            0.25f,
            0.5f,
            1f
        };

        bool passed = true;
        float maximumDifference = 0f;

        const int samples =
            33;

        for (
            int f = 0;
            f < falloffs.Length;
            f++
        )
        {
            for (
                int y = 0;
                y < samples;
                y++
            )
            {
                for (
                    int x = 0;
                    x < samples;
                    x++
                )
                {
                    Vector2 uv =
                        new Vector2(
                            x /
                            (
                                (float)(
                                    samples - 1
                                )
                            ),
                            y /
                            (
                                (float)(
                                    samples - 1
                                )
                            )
                        );

                    float legacy =
                        EvaluateLegacyRectangleSmooth(
                            uv,
                            falloffs[f]
                        );

                    float current =
                        TerrainStampFalloffUtility
                            .Evaluate(
                                uv,
                                TerrainStampFalloffShape.Rectangle,
                                TerrainStampFalloffProfile.Smooth,
                                falloffs[f]
                            );

                    float difference =
                        Mathf.Abs(
                            legacy - current
                        );

                    maximumDifference =
                        Mathf.Max(
                            maximumDifference,
                            difference
                        );

                    if (
                        difference >
                            MathTolerance
                    )
                    {
                        passed = false;
                    }
                }
            }
        }

        Add(
            "Rectangle + Smooth compatibility",
            passed,
            passed
                ? "The new Rectangle + Smooth CPU contract exactly matches the established nearest-edge smoothstep response."
                : "Rectangle compatibility failed. Maximum difference: " +
                    maximumDifference
        );
    }

    private static float EvaluateLegacyRectangleSmooth(
        Vector2 uv,
        float falloff
    )
    {
        if (
            uv.x < 0f
            ||
            uv.x > 1f
            ||
            uv.y < 0f
            ||
            uv.y > 1f
        )
        {
            return 0f;
        }

        float safeFalloff =
            Mathf.Clamp01(falloff);

        if (safeFalloff <= 0f)
        {
            return 1f;
        }

        float nearestEdge =
            Mathf.Min(
                Mathf.Min(
                    uv.x,
                    1f - uv.x
                ),
                Mathf.Min(
                    uv.y,
                    1f - uv.y
                )
            );

        float edgeDistance =
            Mathf.Clamp01(
                nearestEdge * 2f
            );

        float t =
            Mathf.Clamp01(
                edgeDistance /
                safeFalloff
            );

        return
            t *
            t *
            (
                3f -
                2f * t
            );
    }

    private static void ValidateModifierContract()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        bool defaults =
            stamp.FalloffShape ==
                TerrainStampFalloffShape.Rectangle
            &&
            stamp.FalloffProfile ==
                TerrainStampFalloffProfile.Smooth
            &&
            Approx(
                stamp.Falloff,
                0.25f
            );

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

        Bounds before =
            stamp.GetAffectedWorldBounds();

        stamp.SetFalloffShapeInternal(
            TerrainStampFalloffShape.Ellipse
        );

        stamp.SetFalloffProfileInternal(
            TerrainStampFalloffProfile.Sharp
        );

        Bounds after =
            stamp.GetAffectedWorldBounds();

        stamp.SetFalloffShapeInternal(
            (TerrainStampFalloffShape)999
        );

        stamp.SetFalloffProfileInternal(
            (TerrainStampFalloffProfile)999
        );

        bool sanitized =
            stamp.FalloffShape ==
                TerrainStampFalloffShape.Rectangle
            &&
            stamp.FalloffProfile ==
                TerrainStampFalloffProfile.Smooth;

        bool passed =
            defaults
            &&
            sanitized
            &&
            BoundsApprox(
                before,
                after
            );

        Add(
            "Modifier defaults / Bounds",
            passed,
            passed
                ? "Defaults are Rectangle/Smooth with existing Falloff=0.25, invalid enums sanitize safely, and Bounds remain unchanged."
                : "Falloff defaults, sanitization, or spatial Bounds are incorrect."
        );
    }

    private static void ValidateSignature()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        string baseline =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFalloffShapeInternal(
            TerrainStampFalloffShape.Ellipse
        );

        string ellipse =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFalloffShapeInternal(
            TerrainStampFalloffShape.Rectangle
        );

        string restoredShape =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFalloffProfileInternal(
            TerrainStampFalloffProfile.Linear
        );

        string linear =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetFalloffProfileInternal(
            TerrainStampFalloffProfile.Smooth
        );

        string restoredProfile =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool passed =
            !string.IsNullOrEmpty(
                baseline
            )
            &&
            baseline != ellipse
            &&
            baseline ==
                restoredShape
            &&
            baseline != linear
            &&
            baseline ==
                restoredProfile;

        Add(
            "Falloff deterministic signature",
            passed,
            passed
                ? "Shape/Profile affect V6 output identity and restoring Rectangle/Smooth restores the signature."
                : "Falloff signature behavior is incorrect."
        );
    }

    private static void ValidateDiscreteMutations(
        WorldSettings worldSettings
    )
    {
        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        try
        {
            TerrainStampModifier stamp =
                new TerrainStampModifier();

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            int before =
                data.authoringRevision;

            bool changedShape =
                TerrainAuthoringModifierService
                    .SetStampFalloffShape(
                        data,
                        worldSettings,
                        stamp.StableId,
                        TerrainStampFalloffShape.Ellipse,
                        out string shapeError
                    );

            bool changedProfile =
                TerrainAuthoringModifierService
                    .SetStampFalloffProfile(
                        data,
                        worldSettings,
                        stamp.StableId,
                        TerrainStampFalloffProfile.Sharp,
                        out string profileError
                    );

            int after =
                data.authoringRevision;

            bool noChange =
                TerrainAuthoringModifierService
                    .SetStampFalloffProfile(
                        data,
                        worldSettings,
                        stamp.StableId,
                        TerrainStampFalloffProfile.Sharp,
                        out string noChangeError
                    );

            bool passed =
                changedShape
                &&
                changedProfile
                &&
                noChange
                &&
                after ==
                    before + 2
                &&
                data.authoringRevision ==
                    after;

            Add(
                "Discrete falloff mutations",
                passed,
                passed
                    ? "Shape/Profile service edits advance one revision per real change and preserve equivalent requests as no-ops."
                    : "Discrete falloff mutation failed. " +
                        shapeError + " " +
                        profileError + " " +
                        noChangeError
            );
        }
        finally
        {
            Undo.ClearUndo(data);

            TerrainAuthoringModifierChangeTracker
                .Forget(data);

            UnityEngine.Object
                .DestroyImmediate(data);
        }
    }

    private static void ValidateDuplicate(
        WorldSettings worldSettings
    )
    {
        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        try
        {
            TerrainStampModifier source =
                new TerrainStampModifier();

            source.SetFalloffInternal(
                0.35f
            );

            source.SetFalloffShapeInternal(
                TerrainStampFalloffShape.Ellipse
            );

            source.SetFalloffProfileInternal(
                TerrainStampFalloffProfile.Sharp
            );

            data.AddHeightModifierInternal(
                source
            );

            data.RepairModifierStableIds();

            bool duplicated =
                TerrainAuthoringModifierService
                    .DuplicateModifier(
                        data,
                        worldSettings,
                        source.StableId,
                        out string duplicateStableId,
                        out string duplicateError
                    );

            TerrainStampModifier duplicate =
                null;

            if (duplicated)
            {
                IReadOnlyList<TerrainHeightModifier>
                    modifiers =
                        data.HeightModifiers;

                for (
                    int i = 0;
                    i < modifiers.Count;
                    i++
                )
                {
                    if (
                        modifiers[i] is
                            TerrainStampModifier candidate
                        &&
                        candidate.StableId ==
                            duplicateStableId
                    )
                    {
                        duplicate = candidate;
                        break;
                    }
                }
            }

            bool passed =
                duplicated
                &&
                duplicate != null
                &&
                Approx(
                    duplicate.Falloff,
                    source.Falloff
                )
                &&
                duplicate.FalloffShape ==
                    source.FalloffShape
                &&
                duplicate.FalloffProfile ==
                    source.FalloffProfile;

            Add(
                "Duplicate preserves falloff",
                passed,
                passed
                    ? "DuplicateModifier preserves Falloff amount, Shape, and Profile."
                    : "DuplicateModifier did not preserve falloff state. " +
                        (duplicateError ?? "")
            );
        }
        finally
        {
            Undo.ClearUndo(data);

            TerrainAuthoringModifierChangeTracker
                .Forget(data);

            UnityEngine.Object
                .DestroyImmediate(data);
        }
    }

    private static void ValidateGpuCases(
        TestContext context
    )
    {
        context.StampAsset
            .SetHeightTextureInternal(
                context.ConstantTexture
            );

        ConfigureSourceIdentity(
            context.Stamp
        );

        context.Stamp
            .SetRotationDegreesInternal(
                0f
            );

        bool passed = true;
        string failures = "";

        TerrainStampFalloffShape[] shapes =
        {
            TerrainStampFalloffShape.Rectangle,
            TerrainStampFalloffShape.Rectangle,
            TerrainStampFalloffShape.Rectangle,
            TerrainStampFalloffShape.Ellipse,
            TerrainStampFalloffShape.Ellipse,
            TerrainStampFalloffShape.Ellipse,
            TerrainStampFalloffShape.Ellipse
        };

        TerrainStampFalloffProfile[] profiles =
        {
            TerrainStampFalloffProfile.Smooth,
            TerrainStampFalloffProfile.Linear,
            TerrainStampFalloffProfile.Sharp,
            TerrainStampFalloffProfile.Smooth,
            TerrainStampFalloffProfile.Smooth,
            TerrainStampFalloffProfile.Linear,
            TerrainStampFalloffProfile.Sharp
        };

        float[] falloffs =
        {
            0f,
            0.35f,
            0.35f,
            0f,
            1f,
            0.35f,
            0.35f
        };

        for (
            int i = 0;
            i < shapes.Length;
            i++
        )
        {
            passed &=
                ValidateGpuMaskCase(
                    context,
                    shapes[i],
                    profiles[i],
                    falloffs[i],
                    ref failures
                );
        }

        Add(
            "GPU falloff masks",
            passed,
            passed
                ? "Rectangle/Ellipse, hard boundaries, Falloff=1, and Smooth/Linear/Sharp match the CPU reference and stay in [0,1]."
                : failures
        );
    }

    private static bool ValidateGpuMaskCase(
        TestContext context,
        TerrainStampFalloffShape shape,
        TerrainStampFalloffProfile profile,
        float falloff,
        ref string failures
    )
    {
        context.Stamp
            .SetFalloffShapeInternal(
                shape
            );

        context.Stamp
            .SetFalloffProfileInternal(
                profile
            );

        context.Stamp
            .SetFalloffInternal(
                falloff
            );

        if (
            !TryComposeDirect(
                context,
                out float[] actual,
                out string error
            )
        )
        {
            failures +=
                shape + "/" +
                profile +
                " failed: " +
                error +
                " ";

            return false;
        }

        float maximumDifference = 0f;
        bool rangeValid = true;

        for (
            int y = 0;
            y < context.SamplesPerSide;
            y++
        )
        {
            for (
                int x = 0;
                x < context.SamplesPerSide;
                x++
            )
            {
                int index =
                    y *
                    context.SamplesPerSide +
                    x;

                Vector2 uv =
                    GetSampleStampUV(
                        context,
                        x,
                        y
                    );

                float expected =
                    TerrainStampFalloffUtility
                        .Evaluate(
                            uv,
                            shape,
                            profile,
                            falloff
                        );

                maximumDifference =
                    Mathf.Max(
                        maximumDifference,
                        Mathf.Abs(
                            actual[index] -
                            expected
                        )
                    );

                if (
                    actual[index] <
                        -GpuTolerance
                    ||
                    actual[index] >
                        1f +
                        GpuTolerance
                )
                {
                    rangeValid = false;
                }
            }
        }

        bool passed =
            maximumDifference <=
                GpuTolerance
            &&
            rangeValid;

        if (!passed)
        {
            failures +=
                shape + "/" +
                profile +
                " maxDiff=" +
                maximumDifference +
                " rangeValid=" +
                rangeValid +
                ". ";
        }

        return passed;
    }

    private static void ValidateSourceCoordinateIndependence(
        TestContext context
    )
    {
        context.StampAsset
            .SetHeightTextureInternal(
                context.AsymmetricTexture
            );

        context.Stamp
            .SetRotationDegreesInternal(
                0f
            );

        context.Stamp
            .SetFalloffShapeInternal(
                TerrainStampFalloffShape.Ellipse
            );

        context.Stamp
            .SetFalloffProfileInternal(
                TerrainStampFalloffProfile.Sharp
            );

        context.Stamp
            .SetSourceRemapInternal(
                0.1f,
                0.95f,
                1.3f
            );

        bool passed = true;
        string failures = "";

        passed &=
            ValidateExtractedMask(
                context,
                false,
                false,
                ref failures
            );

        passed &=
            ValidateExtractedMask(
                context,
                true,
                false,
                ref failures
            );

        passed &=
            ValidateExtractedMask(
                context,
                false,
                true,
                ref failures
            );

        Add(
            "Flip / Source Remapping independence",
            passed,
            passed
                ? "Falloff extracted from asymmetric source output remains a stampUV mask under Flip X, Flip Z, and non-default Source Remapping."
                : failures
        );
    }

    private static bool ValidateExtractedMask(
        TestContext context,
        bool flipX,
        bool flipZ,
        ref string failures
    )
    {
        context.Stamp
            .SetFlipXInternal(flipX);

        context.Stamp
            .SetFlipZInternal(flipZ);

        context.Stamp
            .SetFalloffInternal(0f);

        if (
            !TryComposeDirect(
                context,
                out float[] hard,
                out string hardError
            )
        )
        {
            failures +=
                "Hard mask failed: " +
                hardError +
                " ";

            return false;
        }

        const float falloff =
            0.35f;

        context.Stamp
            .SetFalloffInternal(falloff);

        if (
            !TryComposeDirect(
                context,
                out float[] faded,
                out string fadedError
            )
        )
        {
            failures +=
                "Faded mask failed: " +
                fadedError +
                " ";

            return false;
        }

        float maximumDifference = 0f;
        int compared = 0;

        for (
            int y = 0;
            y < context.SamplesPerSide;
            y++
        )
        {
            for (
                int x = 0;
                x < context.SamplesPerSide;
                x++
            )
            {
                int index =
                    y *
                    context.SamplesPerSide +
                    x;

                if (hard[index] <= 0.05f)
                {
                    continue;
                }

                float expected =
                    TerrainStampFalloffUtility
                        .Evaluate(
                            GetSampleStampUV(
                                context,
                                x,
                                y
                            ),
                            context.Stamp.FalloffShape,
                            context.Stamp.FalloffProfile,
                            falloff
                        );

                float extracted =
                    faded[index] /
                    hard[index];

                maximumDifference =
                    Mathf.Max(
                        maximumDifference,
                        Mathf.Abs(
                            extracted -
                            expected
                        )
                    );

                compared++;
            }
        }

        bool passed =
            compared > 0
            &&
            maximumDifference <=
                RatioTolerance;

        if (!passed)
        {
            failures +=
                "FlipX=" +
                flipX +
                " FlipZ=" +
                flipZ +
                " maxDiff=" +
                maximumDifference +
                ". ";
        }

        return passed;
    }

    private static void ValidateRotatedEllipse(
        TestContext context
    )
    {
        context.StampAsset
            .SetHeightTextureInternal(
                context.ConstantTexture
            );

        ConfigureSourceIdentity(
            context.Stamp
        );

        context.Stamp
            .SetRotationDegreesInternal(
                37f
            );

        string failures = "";

        bool passed =
            ValidateGpuMaskCase(
                context,
                TerrainStampFalloffShape.Ellipse,
                TerrainStampFalloffProfile.Linear,
                0.4f,
                ref failures
            );

        Add(
            "Rotated non-square Ellipse",
            passed,
            passed
                ? "The GPU Ellipse follows the existing stamp-local transform at Rotation=37 degrees."
                : failures
        );
    }

    private static void ValidateRuntimeParity(
        WorldSettings worldSettings,
        TestContext context
    )
    {
        context.StampAsset
            .SetHeightTextureInternal(
                context.AsymmetricTexture
            );

        context.Stamp
            .SetRotationDegreesInternal(37f);

        context.Stamp
            .SetFlipXInternal(true);

        context.Stamp
            .SetFlipZInternal(false);

        context.Stamp
            .SetSourceRemapInternal(
                0.1f,
                0.95f,
                1.3f
            );

        context.Stamp
            .SetSmoothingRadiusInternal(0f);

        context.Stamp
            .SetSmoothingStrengthInternal(0f);

        context.Stamp
            .SetFalloffShapeInternal(
                TerrainStampFalloffShape.Ellipse
            );

        context.Stamp
            .SetFalloffProfileInternal(
                TerrainStampFalloffProfile.Sharp
            );

        context.Stamp
            .SetFalloffInternal(0.4f);

        if (
            !TryComposeDirect(
                context,
                out float[] direct,
                out string directError
            )
        )
        {
            Add(
                "Runtime/shared compositor parity",
                false,
                "Direct composition failed: " +
                directError
            );

            return;
        }

        TerrainRuntimeHeightCompositionContext
            runtime =
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
                    "Runtime/shared compositor parity",
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
                    "Runtime/shared compositor parity",
                    false,
                    "The rotated Ellipse stamp was not classified as affecting tile (0,0)."
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
                    "Runtime/shared compositor parity",
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
                "Runtime/shared compositor parity",
                passed,
                passed
                    ? "Direct and runtime composition match for Ellipse + Sharp + Rotation + Flip + Source Remapping."
                    : "Runtime output differs from the shared direct compositor."
            );
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private static void ConfigureSourceIdentity(
        TerrainStampModifier stamp
    )
    {
        stamp.SetFlipXInternal(false);
        stamp.SetFlipZInternal(false);

        stamp.SetSourceRemapInternal(
            0f,
            1f,
            1f
        );

        stamp.SetSmoothingRadiusInternal(0f);
        stamp.SetSmoothingStrengthInternal(0f);
    }

    private static Vector2 GetSampleStampUV(
        TestContext context,
        int x,
        int y
    )
    {
        Vector2 worldXZ =
            new Vector2(
                x *
                    context.SampleSpacing,
                y *
                    context.SampleSpacing
            );

        Vector2 local =
            TerrainStampTransformUtility
                .WorldToLocalXZ(
                    worldXZ,
                    context.Stamp.PositionXZ,
                    context.Stamp.RotationDegrees
                );

        Vector2 size =
            context.Stamp.SizeXZ;

        return new Vector2(
            local.x / size.x + 0.5f,
            local.y / size.y + 0.5f
        );
    }

    private static bool TryCreateContext(
        WorldSettings worldSettings,
        out TestContext context,
        out string errorMessage
    )
    {
        context = null;
        errorMessage = "";

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
        )
        {
            errorMessage =
                "Current height-tile layout is invalid for validation.";

            return false;
        }

        TestContext created =
            new TestContext();

        try
        {
            created.SamplesPerSide = samples;
            created.CenterIndex =
                (samples - 1) / 2;
            created.SampleSpacing = spacing;
            created.TileWorldSize = tileSize;
            created.WorldSizeXZ = worldSize;

            created.ConstantTexture =
                CreateConstantTexture();

            created.AsymmetricTexture =
                CreateAsymmetricTexture();

            created.StampAsset =
                ScriptableObject
                    .CreateInstance<TerrainHeightStampAsset>();

            created.StampAsset
                .SetHeightTextureInternal(
                    created.ConstantTexture
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
                        created.CenterIndex *
                            spacing,
                        created.CenterIndex *
                            spacing
                    )
                );

            int halfX =
                Mathf.Max(
                    4,
                    Mathf.Min(
                        18,
                        created.CenterIndex / 2
                    )
                );

            int halfZ =
                Mathf.Max(
                    3,
                    Mathf.FloorToInt(
                        halfX * 0.65f
                    )
                );

            created.Stamp
                .SetSizeXZInternal(
                    new Vector2(
                        halfX *
                            2f *
                            spacing,
                        halfZ *
                            2f *
                            spacing
                    )
                );

            created.Stamp
                .SetHeightDeltaInternal(1f);

            created.Stamp
                .SetBlendModeInternal(
                    TerrainHeightBlendMode.Additive
                );

            created.Stamp
                .SetEnabledInternal(true);

            ConfigureSourceIdentity(
                created.Stamp
            );

            created.Stamp
                .SetFalloffInternal(0.25f);

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
                CreateZeroTile(samples);

            context = created;
            return true;
        }
        catch (Exception exception)
        {
            created.Dispose();

            errorMessage =
                exception.Message;

            return false;
        }
    }

    private static Texture2D CreateConstantTexture()
    {
        return CreateStampTexture(
            false
        );
    }

    private static Texture2D CreateAsymmetricTexture()
    {
        return CreateStampTexture(
            true
        );
    }

    private static Texture2D CreateStampTexture(
        bool asymmetric
    )
    {
        const int size = 16;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RFloat,
                true,
                true
            );

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
                        size - 1
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
                            size - 1
                        )
                    );

                values[
                    z * size + x
                ] =
                    asymmetric
                        ? Mathf.Clamp01(
                            0.45f +
                            0.30f * u +
                            0.15f * v +
                            0.05f * u * v
                        )
                        : 1f;
            }
        }

        texture.SetPixelData(
            values,
            0
        );

        texture.Apply(
            true,
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
        output = null;
        errorMessage = "";

        RenderTexture target = null;

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

            target.volumeDepth = 1;
            target.enableRandomWrite = true;
            target.useMipMap = false;
            target.autoGenerateMips = false;
            target.wrapMode =
                TextureWrapMode.Clamp;
            target.filterMode =
                FilterMode.Point;

            if (!target.Create())
            {
                errorMessage =
                    "Could not create transient RFloat target.";

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
                    out _,
                    out _,
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
                int i = 0;
                i < data.Length;
                i++
            )
            {
                output[i] =
                    data[i];
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
                    .DestroyImmediate(target);
            }
        }
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
            a.Length != b.Length
        )
        {
            return false;
        }

        for (
            int i = 0;
            i < a.Length;
            i++
        )
        {
            if (
                Mathf.Abs(
                    a[i] - b[i]
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
            Approx(a.center.x, b.center.x)
            &&
            Approx(a.center.y, b.center.y)
            &&
            Approx(a.center.z, b.center.z)
            &&
            Approx(a.size.x, b.size.x)
            &&
            Approx(a.size.y, b.size.y)
            &&
            Approx(a.size.z, b.size.z);
    }

    private static bool Approx(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(a - b)
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
                Name = name,
                Passed = passed,
                Details = details ?? ""
            }
        );
    }

    private static void Finish()
    {
        validationRunning = false;
        lastPassedCount = 0;
        lastFailedCount = 0;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        for (
            int i = 0;
            i < results.Count;
            i++
        )
        {
            Result result =
                results[i];

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
                builder.Append("\n");
                builder.Append(result.Details);
            }

            if (
                i < results.Count - 1
            )
            {
                builder.Append("\n\n");
            }
        }

        lastSummary =
            builder.Length > 0
                ? builder.ToString()
                : "No validation results were produced.";

        if (lastFailedCount > 0)
        {
            Debug.LogError(
                "Stamp falloff validation failed.\n\n" +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp falloff validation passed.\n\n" +
                lastSummary
            );
        }

        SceneView.RepaintAll();
    }
}
