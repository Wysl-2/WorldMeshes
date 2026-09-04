using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainStampSourceRemapValidationUtility
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

            Destroy(
                CommittedTile
            );

            Destroy(
                StampTexture
            );

            Destroy(
                StampAsset
            );

            Destroy(
                AuthoringData
            );
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

    private const float MathTolerance =
        0.0001f;

    private const float GpuTolerance =
        0.002f;

    private const float SmoothingTolerance =
        0.004f;

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

        ValidateUtilityContract();
        ValidateModifierDefaultsAndBounds();
        ValidateSignature();
        ValidateDiscreteMutations(
            worldSettings
        );
        ValidateInteractiveMutation(
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
                "GPU/runtime source-remap validation",
                false,
                "RFloat, compute shaders, 2D texture arrays, or AsyncGPUReadback are unavailable."
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
                    "Transient source-remap test context",
                    false,
                    contextError
                );

                return;
            }

            ValidateGpuRemapCases(
                context
            );

            ValidatePostSmoothingOrdering(
                context
            );

            ValidateTransformAndFlipInteraction(
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
    }

    private static void ValidateUtilityContract()
    {
        TerrainStampSourceRemapUtility
            .SanitizeStoredValues(
                0f,
                0f,
                0f,
                out float legacyMin,
                out float legacyMax,
                out float legacyGamma
            );

        bool legacyIdentity =
            Approx(
                legacyMin,
                TerrainStampSourceRemapUtility
                    .IdentityInputMin
            )
            &&
            Approx(
                legacyMax,
                TerrainStampSourceRemapUtility
                    .IdentityInputMax
            )
            &&
            Approx(
                legacyGamma,
                TerrainStampSourceRemapUtility
                    .IdentityGamma
            );

        TerrainStampSourceRemapUtility
            .SanitizeStoredValues(
                0f,
                1f,
                0f,
                out _,
                out _,
                out float missingGamma
            );

        bool missingGammaIdentity =
            Approx(
                missingGamma,
                TerrainStampSourceRemapUtility
                    .IdentityGamma
            );

        float low =
            TerrainStampSourceRemapUtility
                .Evaluate(
                    0f,
                    0.25f,
                    0.75f,
                    1f
                );

        float middle =
            TerrainStampSourceRemapUtility
                .Evaluate(
                    0.5f,
                    0.25f,
                    0.75f,
                    1f
                );

        float high =
            TerrainStampSourceRemapUtility
                .Evaluate(
                    1f,
                    0.25f,
                    0.75f,
                    1f
                );

        float gammaAboveOne =
            TerrainStampSourceRemapUtility
                .Evaluate(
                    0.5f,
                    0f,
                    1f,
                    2f
                );

        float gammaBelowOne =
            TerrainStampSourceRemapUtility
                .Evaluate(
                    0.25f,
                    0f,
                    1f,
                    0.5f
                );

        bool responsePassed =
            Approx(
                low,
                0f
            )
            &&
            Approx(
                middle,
                0.5f
            )
            &&
            Approx(
                high,
                1f
            )
            &&
            Approx(
                gammaAboveOne,
                0.25f
            )
            &&
            Approx(
                gammaBelowOne,
                0.5f
            );

        TerrainStampSourceRemapUtility
            .SanitizeRequestedValues(
                1f,
                0f,
                float.NaN,
                out float requestedMin,
                out float requestedMax,
                out float requestedGamma
            );

        bool requestedCanonical =
            requestedMin >= 0f
            &&
            requestedMax <= 1f
            &&
            requestedMax -
                requestedMin
            >=
            TerrainStampSourceRemapUtility
                .MinimumInputRange -
            MathTolerance
            &&
            requestedGamma >=
                TerrainStampSourceRemapUtility
                    .MinimumGamma;

        bool passed =
            legacyIdentity
            &&
            missingGammaIdentity
            &&
            responsePassed
            &&
            requestedCanonical;

        Add(
            "Source-remap utility contract",
            passed,
            passed
                ? "Legacy invalid storage resolves to identity; Min/Max/Gamma math and requested-state canonicalization match the source-response contract."
                : "Source-remap utility identity, response, or canonicalization behavior is incorrect."
        );
    }

    private static void ValidateModifierDefaultsAndBounds()
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
            Approx(
                stamp.SourceInputMin,
                0f
            )
            &&
            Approx(
                stamp.SourceInputMax,
                1f
            )
            &&
            Approx(
                stamp.SourceGamma,
                1f
            );

        stamp.SetSourceRemapInternal(
            0.2f,
            0.8f,
            1.5f
        );

        Bounds remappedBounds =
            stamp.GetAffectedWorldBounds();

        bool boundsPassed =
            BoundsApprox(
                baselineBounds,
                remappedBounds
            );

        Add(
            "Modifier source-remap defaults and Bounds",
            defaultsPassed
            &&
            boundsPassed,
            defaultsPassed
            &&
            boundsPassed
                ? "New stamps expose identity 0/1/1 and source response leaves the rotated spatial Bounds unchanged."
                : "Default source response or remap-independent Bounds are incorrect."
        );
    }

    private static void ValidateSignature()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        stamp.SetRotationDegreesInternal(
            37f
        );

        stamp.SetFlipXInternal(
            true
        );

        string baseline =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetSourceInputMinInternal(
            0.2f
        );

        string inputMin =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetSourceRemapInternal(
            0f,
            1f,
            1f
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetSourceInputMaxInternal(
            0.8f
        );

        string inputMax =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetSourceRemapInternal(
            0f,
            1f,
            1f
        );

        stamp.SetSourceGammaInternal(
            1.5f
        );

        string gamma =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool passed =
            !string.IsNullOrEmpty(
                baseline
            )
            &&
            baseline != inputMin
            &&
            baseline == restored
            &&
            baseline != inputMax
            &&
            baseline != gamma
            &&
            inputMin != inputMax
            &&
            inputMin != gamma;

        Add(
            "Source-remap deterministic signature",
            passed,
            passed
                ? "Input Min, Input Max, and Gamma affect output identity; restoring 0/1/1 restores the original V5 content signature."
                : "Source-remap deterministic signature behavior is incorrect."
        );
    }

    private static void ValidateDiscreteMutations(
        WorldSettings worldSettings
    )
    {
        TerrainAuthoringData authoringData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        string minError = "";
        string maxError = "";
        string gammaError = "";
        string noChangeError = "";

        try
        {
            TerrainStampModifier stamp =
                new TerrainStampModifier();

            authoringData
                .AddHeightModifierInternal(
                    stamp
                );

            authoringData
                .RepairModifierStableIds();

            int revisionBefore =
                authoringData.authoringRevision;

            bool minChanged =
                TerrainAuthoringModifierService
                    .SetStampSourceInputMin(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        0.25f,
                        out minError
                    );

            bool maxChanged =
                TerrainAuthoringModifierService
                    .SetStampSourceInputMax(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        0.75f,
                        out maxError
                    );

            bool gammaChanged =
                TerrainAuthoringModifierService
                    .SetStampSourceGamma(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        1.5f,
                        out gammaError
                    );

            int revisionAfterChanges =
                authoringData.authoringRevision;

            bool noChange =
                TerrainAuthoringModifierService
                    .SetStampSourceGamma(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        1.5f,
                        out noChangeError
                    );

            bool passed =
                minChanged
                &&
                maxChanged
                &&
                gammaChanged
                &&
                noChange
                &&
                Approx(
                    stamp.SourceInputMin,
                    0.25f
                )
                &&
                Approx(
                    stamp.SourceInputMax,
                    0.75f
                )
                &&
                Approx(
                    stamp.SourceGamma,
                    1.5f
                )
                &&
                revisionAfterChanges ==
                    revisionBefore +
                    3
                &&
                authoringData.authoringRevision ==
                    revisionAfterChanges;

            Add(
                "Discrete source-remap mutations",
                passed,
                passed
                    ? "Production setters canonicalize values, produce one revision per real mutation, and keep an equivalent Gamma request as a no-op."
                    : "Discrete source-remap mutation behavior failed. " +
                        minError + " " +
                        maxError + " " +
                        gammaError + " " +
                        noChangeError
            );
        }
        catch (Exception exception)
        {
            Add(
                "Discrete source-remap mutations",
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

    private static void ValidateInteractiveMutation(
        WorldSettings worldSettings
    )
    {
        TerrainAuthoringData authoringData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        string stableId = "";
        string beginError = "";
        string updateError = "";
        string cancelError = "";
        string beginCommitError = "";
        string updateCommitError = "";
        string commitError = "";

        try
        {
            TerrainStampModifier stamp =
                new TerrainStampModifier();

            authoringData
                .AddHeightModifierInternal(
                    stamp
                );

            authoringData
                .RepairModifierStableIds();

            stableId =
                stamp.StableId;

            int revisionBefore =
                authoringData.authoringRevision;

            bool began =
                TerrainAuthoringModifierService
                    .BeginInteractiveModifierEdit(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        "Validate Terrain Stamp Source Remap",
                        out beginError
                    );

            bool updated =
                began
                &&
                TerrainAuthoringModifierService
                    .UpdateInteractiveStampSourceRemap(
                        0.2f,
                        0.8f,
                        1.4f,
                        out updateError
                    );

            bool changedLive =
                updated
                &&
                Approx(
                    stamp.SourceInputMin,
                    0.2f
                )
                &&
                Approx(
                    stamp.SourceInputMax,
                    0.8f
                )
                &&
                Approx(
                    stamp.SourceGamma,
                    1.4f
                )
                &&
                authoringData.authoringRevision ==
                    revisionBefore;

            bool cancelled =
                TerrainAuthoringModifierService
                    .CancelInteractiveEdit(
                        out cancelError
                    );

            bool restored =
                cancelled
                &&
                Approx(
                    stamp.SourceInputMin,
                    0f
                )
                &&
                Approx(
                    stamp.SourceInputMax,
                    1f
                )
                &&
                Approx(
                    stamp.SourceGamma,
                    1f
                )
                &&
                authoringData.authoringRevision ==
                    revisionBefore;

            bool beganCommit =
                TerrainAuthoringModifierService
                    .BeginInteractiveModifierEdit(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        "Validate Terrain Stamp Source Remap",
                        out beginCommitError
                    );

            bool updatedCommit =
                beganCommit
                &&
                TerrainAuthoringModifierService
                    .UpdateInteractiveStampSourceRemap(
                        0.15f,
                        0.85f,
                        1.3f,
                        out updateCommitError
                    );

            bool committed =
                updatedCommit
                &&
                TerrainAuthoringModifierService
                    .CommitInteractiveEdit(
                        out commitError
                    );

            bool committedOnce =
                committed
                &&
                authoringData.authoringRevision ==
                    revisionBefore +
                    1
                &&
                Approx(
                    stamp.SourceInputMin,
                    0.15f
                )
                &&
                Approx(
                    stamp.SourceInputMax,
                    0.85f
                )
                &&
                Approx(
                    stamp.SourceGamma,
                    1.3f
                );

            bool passed =
                changedLive
                &&
                restored
                &&
                committedOnce;

            Add(
                "Grouped interactive source-remap mutation",
                passed,
                passed
                    ? "Live grouped updates use the existing transaction, cancel restores MouseDown state, and commit advances authoringRevision exactly once."
                    : "Interactive source-remap transaction behavior failed. " +
                        beginError + " " +
                        updateError + " " +
                        cancelError + " " +
                        beginCommitError + " " +
                        updateCommitError + " " +
                        commitError
            );
        }
        catch (Exception exception)
        {
            if (
                TerrainAuthoringModifierService
                    .HasActiveInteractiveEdit
            )
            {
                TerrainAuthoringModifierService
                    .CancelInteractiveEdit(
                        out _
                    );
            }

            Add(
                "Grouped interactive source-remap mutation",
                false,
                exception.Message
            );
        }
        finally
        {
            if (
                TerrainAuthoringModifierService
                    .HasActiveInteractiveEdit
                &&
                TerrainAuthoringModifierService
                    .ActiveInteractiveStableId ==
                    stableId
            )
            {
                TerrainAuthoringModifierService
                    .CancelInteractiveEdit(
                        out _
                    );
            }

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

            source.SetSourceRemapInternal(
                0.2f,
                0.82f,
                1.6f
            );

            authoringData
                .AddHeightModifierInternal(
                    source
                );

            authoringData
                .RepairModifierStableIds();

            bool duplicated =
                TerrainAuthoringModifierService
                    .DuplicateModifier(
                        authoringData,
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
                    source.StableId
                &&
                Approx(
                    duplicate.SourceInputMin,
                    source.SourceInputMin
                )
                &&
                Approx(
                    duplicate.SourceInputMax,
                    source.SourceInputMax
                )
                &&
                Approx(
                    duplicate.SourceGamma,
                    source.SourceGamma
                )
                &&
                duplicate.FlipX ==
                    source.FlipX
                &&
                Approx(
                    duplicate.RotationDegrees,
                    source.RotationDegrees
                );

            Add(
                "Production duplicate preserves source remapping",
                passed,
                passed
                    ? "DuplicateModifier preserves Input Min, Input Max, Gamma, Flip, and Rotation while assigning a new StableId."
                    : "DuplicateModifier did not preserve source-remap state. " +
                        (duplicateError ?? "")
            );
        }
        catch (Exception exception)
        {
            Add(
                "Production duplicate preserves source remapping",
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

    private static void ValidateGpuRemapCases(
        TestContext context
    )
    {
        ConfigureStamp(
            context,
            0f,
            false,
            false,
            0f,
            0f,
            0f,
            1f,
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
                "GPU source-remap cases",
                false,
                "Identity baseline composition failed: " +
                baselineError
            );

            return;
        }

        bool identityRange =
            IsOutputRangeValid(
                baseline
            );

        bool allCasesPassed =
            true;

        string failure =
            "";

        allCasesPassed &=
            TryValidateGpuCase(
                context,
                baseline,
                0.25f,
                1f,
                1f,
                GpuTolerance,
                "Input Min",
                ref failure
            );

        allCasesPassed &=
            TryValidateGpuCase(
                context,
                baseline,
                0f,
                0.75f,
                1f,
                GpuTolerance,
                "Input Max",
                ref failure
            );

        allCasesPassed &=
            TryValidateGpuCase(
                context,
                baseline,
                0f,
                1f,
                2f,
                GpuTolerance,
                "Gamma > 1",
                ref failure
            );

        allCasesPassed &=
            TryValidateGpuCase(
                context,
                baseline,
                0f,
                1f,
                0.5f,
                GpuTolerance,
                "Gamma < 1",
                ref failure
            );

        allCasesPassed &=
            TryValidateGpuCase(
                context,
                baseline,
                0.2f,
                0.8f,
                1.5f,
                GpuTolerance,
                "Combined Min/Max/Gamma",
                ref failure
            );

        ConfigureStamp(
            context,
            0f,
            false,
            false,
            0f,
            0f,
            0f,
            1f,
            1f
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
                "GPU source-remap cases",
                false,
                "Restored identity composition failed: " +
                restoredError
            );

            return;
        }

        bool identityRestored =
            ArraysEqual(
                baseline,
                restored,
                GpuTolerance
            );

        context.Stamp.SetSourceRemapInternal(
            0.2f,
            0.8f,
            1.5f
        );

        bool visibleEffect =
            false;

        if (
            TryComposeDirect(
                context,
                out float[] visiblyRemapped,
                out _
            )
        )
        {
            visibleEffect =
                MaxDifference(
                    baseline,
                    visiblyRemapped
                )
                >
                0.03f;
        }

        bool passed =
            identityRange
            &&
            allCasesPassed
            &&
            identityRestored
            &&
            visibleEffect;

        Add(
            "GPU source-remap cases",
            passed,
            passed
                ? "Identity, Min, Max, Gamma above/below 1, combined response, output range, and identity restoration match the CPU reference."
                : "One or more GPU source-remap cases failed. " +
                    failure
        );
    }

    private static bool TryValidateGpuCase(
        TestContext context,
        float[] baseline,
        float inputMin,
        float inputMax,
        float gamma,
        float tolerance,
        string label,
        ref string failure
    )
    {
        context.Stamp.SetSourceRemapInternal(
            inputMin,
            inputMax,
            gamma
        );

        if (
            !TryComposeDirect(
                context,
                out float[] actual,
                out string error
            )
        )
        {
            failure +=
                label +
                " composition failed: " +
                error +
                " ";

            return false;
        }

        bool mapped =
            CompareAgainstRemappedBaseline(
                baseline,
                actual,
                inputMin,
                inputMax,
                gamma,
                tolerance
            );

        bool range =
            IsOutputRangeValid(
                actual
            );

        if (
            !mapped
            ||
            !range
        )
        {
            failure +=
                label +
                " did not match expected remap/range. ";
        }

        return
            mapped
            &&
            range;
    }

    private static void ValidatePostSmoothingOrdering(
        TestContext context
    )
    {
        ConfigureStamp(
            context,
            0f,
            false,
            false,
            context.SampleSpacing *
                2.5f,
            1f,
            0f,
            1f,
            1f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] smoothedIdentity,
                out string baselineError
            )
        )
        {
            Add(
                "Remap-after-smoothing ordering",
                false,
                "Smoothed identity composition failed: " +
                baselineError
            );

            return;
        }

        context.Stamp.SetSourceRemapInternal(
            0.15f,
            0.85f,
            1.4f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] remapped,
                out string remapError
            )
        )
        {
            Add(
                "Remap-after-smoothing ordering",
                false,
                "Smoothed remap composition failed: " +
                remapError
            );

            return;
        }

        bool passed =
            CompareAgainstRemappedBaseline(
                smoothedIdentity,
                remapped,
                0.15f,
                0.85f,
                1.4f,
                SmoothingTolerance
            );

        Add(
            "Remap-after-smoothing ordering",
            passed,
            passed
                ? "GPU output equals remap(smoothed identity output), confirming source response is applied after the established smoothing stage."
                : "GPU output does not match remap(smoothed source), indicating source-response ordering changed."
        );
    }

    private static void ValidateTransformAndFlipInteraction(
        TestContext context
    )
    {
        ConfigureStamp(
            context,
            37f,
            true,
            false,
            0f,
            0f,
            0f,
            1f,
            1f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] transformedIdentity,
                out string baselineError
            )
        )
        {
            Add(
                "Rotation / Flip / remap composition",
                false,
                "Transformed identity composition failed: " +
                baselineError
            );

            return;
        }

        context.Stamp.SetSourceRemapInternal(
            0.18f,
            0.82f,
            1.35f
        );

        if (
            !TryComposeDirect(
                context,
                out float[] transformedRemap,
                out string remapError
            )
        )
        {
            Add(
                "Rotation / Flip / remap composition",
                false,
                "Transformed remap composition failed: " +
                remapError
            );

            return;
        }

        bool passed =
            CompareAgainstRemappedBaseline(
                transformedIdentity,
                transformedRemap,
                0.18f,
                0.82f,
                1.35f,
                GpuTolerance
            );

        Add(
            "Rotation / Flip / remap composition",
            passed,
            passed
                ? "At Rotation=37 and FlipX=true, remapping affects only final source values and leaves established coordinate semantics intact."
                : "Source remapping did not remain independent of Rotation/Flip coordinate processing."
        );
    }

    private static void ValidateRuntimeParity(
        WorldSettings worldSettings,
        TestContext context
    )
    {
        ConfigureStamp(
            context,
            37f,
            true,
            false,
            context.SampleSpacing *
                1.5f,
            0.65f,
            0.15f,
            0.85f,
            1.35f
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
                "Runtime/shared compositor source-remap parity",
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
                    "Runtime/shared compositor source-remap parity",
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
                    "Runtime/shared compositor source-remap parity",
                    false,
                    "The rotated/flipped/remapped stamp was not classified as affecting tile (0, 0)."
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
                    "Runtime/shared compositor source-remap parity",
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
                "Runtime/shared compositor source-remap parity",
                passed,
                passed
                    ? "Runtime context and direct shared compositor match for Rotation=37, FlipX=true, nonzero smoothing, and non-default remapping."
                    : "Runtime and direct shared-compositor remap output differ beyond tolerance."
            );
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private static void ConfigureStamp(
        TestContext context,
        float rotationDegrees,
        bool flipX,
        bool flipZ,
        float smoothingRadius,
        float smoothingStrength,
        float inputMin,
        float inputMax,
        float gamma
    )
    {
        context.Stamp
            .SetRotationDegreesInternal(
                rotationDegrees
            );

        context.Stamp
            .SetFlipXInternal(
                flipX
            );

        context.Stamp
            .SetFlipZInternal(
                flipZ
            );

        context.Stamp
            .SetSmoothingRadiusInternal(
                smoothingRadius
            );

        context.Stamp
            .SetSmoothingStrengthInternal(
                smoothingStrength
            );

        context.Stamp
            .SetSourceRemapInternal(
                inputMin,
                inputMax,
                gamma
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
                CreateRampTexture();

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
                .SetHeightDeltaInternal(
                    1f
                );

            created.Stamp
                .SetFalloffInternal(
                    0f
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

            ConfigureStamp(
                created,
                0f,
                false,
                false,
                0f,
                0f,
                0f,
                1f,
                1f
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
                "Could not create transient source-remap resources.\n\n" +
                exception.Message;

            return false;
        }
    }

    private static Texture2D CreateRampTexture()
    {
        const int size =
            16;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RFloat,
                true,
                true
            );

        texture.name =
            "WorldMeshes Source Remap Validation Stamp";

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
                    Mathf.Clamp01(
                        0.05f
                        +
                        0.50f *
                            u
                        +
                        0.30f *
                            v
                        +
                        0.10f *
                            u *
                            v
                    );
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

        texture.name =
            "WorldMeshes Source Remap Validation Base";

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

    private static bool CompareAgainstRemappedBaseline(
        float[] baseline,
        float[] actual,
        float inputMin,
        float inputMax,
        float gamma,
        float tolerance
    )
    {
        if (
            baseline == null
            ||
            actual == null
            ||
            baseline.Length !=
                actual.Length
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < baseline.Length;
            index++
        )
        {
            float expected =
                TerrainStampSourceRemapUtility
                    .Evaluate(
                        baseline[index],
                        inputMin,
                        inputMax,
                        gamma
                    );

            if (
                Mathf.Abs(
                    actual[index] -
                    expected
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

    private static bool IsOutputRangeValid(
        float[] values
    )
    {
        if (values == null)
        {
            return false;
        }

        for (
            int index = 0;
            index < values.Length;
            index++
        )
        {
            if (
                float.IsNaN(
                    values[index]
                )
                ||
                float.IsInfinity(
                    values[index]
                )
                ||
                values[index] <
                    -GpuTolerance
                ||
                values[index] >
                    1f +
                    GpuTolerance
            )
            {
                return false;
            }
        }

        return true;
    }

    private static float MaxDifference(
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
            return 0f;
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
                "Stamp source-remap validation failed.\n\n" +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp source-remap validation passed.\n\n" +
                lastSummary
            );
        }

        SceneView.RepaintAll();
    }
}
