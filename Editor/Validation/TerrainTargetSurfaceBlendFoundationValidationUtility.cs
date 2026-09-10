using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainTargetSurfaceBlendFoundationValidationUtility
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
        "/TargetSurfaceBlendFoundation";

    private static readonly string ValidationDataPath =
        ValidationRoot
        +
        "/TerrainAuthoringData.asset";

    private static readonly string StampAssetPath =
        ValidationRoot
        +
        "/TargetSurfaceStamp.asset";

    private static readonly string StampTexturePath =
        ValidationRoot
        +
        "/TargetSurfaceStampTexture.asset";

    private const float MathTolerance =
        0.0001f;

    private const float GpuTolerance =
        0.000001f;

    private static bool validationScheduled;
    private static bool validationRunning;

    private static int lastPassedCount;
    private static int lastFailedCount;

    private static string lastSummary =
        "Not run.";

    private static WorldSettings worldSettings;
    private static TerrainAuthoringData tempData;
    private static string modifierId;

    private static float undoExpectedBaseHeight;
    private static float undoExpectedHeightRange;
    private static float redoExpectedBaseHeight;
    private static float redoExpectedHeightRange;

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

        ValidateTargetHeightMath();
        ValidateSanitizationAndBounds();
        ValidateSignatureContract();

        CleanupValidationAssets();
        EnsureFolder(
            ValidationRoot
        );

        tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.authoringRevision =
            realData.authoringRevision;

        AssetDatabase.CreateAsset(
            tempData,
            ValidationDataPath
        );

        Texture2D stampTexture =
            CreateStampTexture();

        AssetDatabase.CreateAsset(
            stampTexture,
            StampTexturePath
        );

        TerrainHeightStampAsset stampAsset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        stampAsset.SetHeightTextureInternal(
            stampTexture
        );

        AssetDatabase.CreateAsset(
            stampAsset,
            StampAssetPath
        );

        AssetDatabase.SaveAssets();

        float tileSize =
            worldSettings.HeightTileWorldSize;

        Vector2 position =
            new Vector2(
                tileSize * 0.5f,
                tileSize * 0.5f
            );

        Vector2 size =
            new Vector2(
                tileSize * 0.4f,
                tileSize * 0.4f
            );

        bool added =
            TerrainAuthoringModifierService
                .AddStampModifier(
                    tempData,
                    worldSettings,
                    stampAsset,
                    position,
                    size,
                    25f,
                    0.25f,
                    out modifierId,
                    out string addError
                );

        TerrainStampModifier stamp =
            FindStampModifier(
                tempData,
                modifierId
            );

        if (
            !added
            ||
            stamp == null
        )
        {
            Add(
                "Transient target-surface modifier",
                false,
                addError
            );

            return false;
        }

        Add(
            "Existing Additive defaults preserved",
            stamp.BlendMode ==
                TerrainHeightBlendMode.Additive
            &&
            Approximately(
                stamp.HeightDelta,
                25f
            )
            &&
            Approximately(
                stamp.TargetBaseHeight,
                0f
            )
            &&
            Approximately(
                stamp.TargetHeightRange,
                10f
            ),
            $"Blend={stamp.BlendMode}, HeightDelta={stamp.HeightDelta}, Base={stamp.TargetBaseHeight}, Range={stamp.TargetHeightRange}"
        );

        Bounds boundsBefore =
            stamp.GetAffectedWorldBounds();

        int revisionBeforeBase =
            tempData.authoringRevision;

        string signatureBeforeBase =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool changedBase =
            TerrainAuthoringModifierService
                .SetStampTargetBaseHeight(
                    tempData,
                    worldSettings,
                    modifierId,
                    200f,
                    out string baseError
                );

        string signatureAfterBase =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool baseMutationPassed =
            changedBase
            &&
            Approximately(
                stamp.TargetBaseHeight,
                200f
            )
            &&
            tempData.authoringRevision ==
                revisionBeforeBase + 1
            &&
            signatureAfterBase !=
                signatureBeforeBase;

        Add(
            "TargetBaseHeight service mutation",
            baseMutationPassed,
            baseMutationPassed
                ? "The central mutation service changed TargetBaseHeight, incremented authoringRevision once, and changed the modifier signature."
                : baseError
        );

        ValidateDirtyRegion(
            boundsBefore
        );

        int revisionBeforeRange =
            tempData.authoringRevision;

        string signatureBeforeRange =
            signatureAfterBase;

        bool changedRange =
            TerrainAuthoringModifierService
                .SetStampTargetHeightRange(
                    tempData,
                    worldSettings,
                    modifierId,
                    800f,
                    out string rangeError
                );

        string signatureAfterRange =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool rangeMutationPassed =
            changedRange
            &&
            Approximately(
                stamp.TargetHeightRange,
                800f
            )
            &&
            tempData.authoringRevision ==
                revisionBeforeRange + 1
            &&
            signatureAfterRange !=
                signatureBeforeRange;

        Add(
            "TargetHeightRange service mutation",
            rangeMutationPassed,
            rangeMutationPassed
                ? "The central mutation service changed TargetHeightRange, incremented authoringRevision once, and changed the modifier signature."
                : rangeError
        );

        Bounds boundsAfter =
            stamp.GetAffectedWorldBounds();

        Add(
            "Target parameters preserve affected bounds",
            BoundsApproximately(
                boundsBefore,
                boundsAfter
            ),
            BoundsApproximately(
                boundsBefore,
                boundsAfter
            )
                ? "Target-surface parameters do not alter the modifier XZ footprint."
                : $"Bounds changed from {boundsBefore} to {boundsAfter}."
        );

        bool duplicated =
            TerrainAuthoringModifierService
                .DuplicateModifier(
                    tempData,
                    worldSettings,
                    modifierId,
                    out string duplicateId,
                    out string duplicateError
                );

        TerrainStampModifier duplicate =
            FindStampModifier(
                tempData,
                duplicateId
            );

        bool duplicatePassed =
            duplicated
            &&
            duplicate != null
            &&
            duplicate.StableId !=
                stamp.StableId
            &&
            Approximately(
                duplicate.TargetBaseHeight,
                stamp.TargetBaseHeight
            )
            &&
            Approximately(
                duplicate.TargetHeightRange,
                stamp.TargetHeightRange
            );

        Add(
            "Duplicate preserves target-surface state",
            duplicatePassed,
            duplicatePassed
                ? "DuplicateModifier copied TargetBaseHeight/TargetHeightRange and generated a fresh StableId."
                : duplicateError
        );

        ValidateSerialization(
            duplicateId
        );

        stamp =
            FindStampModifier(
                tempData,
                modifierId
            );

        if (stamp == null)
        {
            Add(
                "Post-serialization modifier lookup",
                false,
                "The original target-surface modifier could not be reloaded."
            );

            return false;
        }

        TerrainHeightStampAsset reloadedStampAsset =
            AssetDatabase.LoadAssetAtPath<TerrainHeightStampAsset>(
                StampAssetPath
            );

        ValidateAdditiveCompatibility(
            reloadedStampAsset
        );

        undoExpectedBaseHeight =
            stamp.TargetBaseHeight;

        undoExpectedHeightRange =
            stamp.TargetHeightRange;

        undoExpectedRevision =
            tempData.authoringRevision;

        undoExpectedSignature =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool finalMutation =
            TerrainAuthoringModifierService
                .SetStampTargetBaseHeight(
                    tempData,
                    worldSettings,
                    modifierId,
                    350f,
                    out string finalMutationError
                );

        stamp =
            FindStampModifier(
                tempData,
                modifierId
            );

        if (
            !finalMutation
            ||
            stamp == null
        )
        {
            Add(
                "Undo/Redo setup mutation",
                false,
                finalMutationError
            );

            return false;
        }

        redoExpectedBaseHeight =
            stamp.TargetBaseHeight;

        redoExpectedHeightRange =
            stamp.TargetHeightRange;

        redoExpectedRevision =
            tempData.authoringRevision;

        redoExpectedSignature =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        Add(
            "Undo/Redo setup mutation",
            Approximately(
                redoExpectedBaseHeight,
                350f
            )
            &&
            redoExpectedRevision ==
                undoExpectedRevision + 1
            &&
            redoExpectedSignature !=
                undoExpectedSignature,
            "A final target-height mutation was recorded as one service-owned Undo step."
        );

        return true;
    }

    private static void ValidateTargetHeightMath()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        stamp.SetTargetBaseHeightInternal(
            200f
        );

        stamp.SetTargetHeightRangeInternal(
            800f
        );

        bool positivePassed =
            Approximately(
                stamp.EvaluateTargetHeight(0f),
                200f
            )
            &&
            Approximately(
                stamp.EvaluateTargetHeight(0.5f),
                600f
            )
            &&
            Approximately(
                stamp.EvaluateTargetHeight(1f),
                1000f
            );

        Add(
            "Target-height positive range",
            positivePassed,
            positivePassed
                ? "Base=200, Range=800 maps source 0/0.5/1 to 200/600/1000."
                : "Positive target-height evaluation did not match the foundation equation."
        );

        stamp.SetTargetBaseHeightInternal(
            1000f
        );

        stamp.SetTargetHeightRangeInternal(
            -800f
        );

        bool negativePassed =
            Approximately(
                stamp.EvaluateTargetHeight(0f),
                1000f
            )
            &&
            Approximately(
                stamp.EvaluateTargetHeight(0.5f),
                600f
            )
            &&
            Approximately(
                stamp.EvaluateTargetHeight(1f),
                200f
            );

        Add(
            "Target-height negative range",
            negativePassed,
            negativePassed
                ? "Base=1000, Range=-800 maps source 0/0.5/1 to 1000/600/200."
                : "Negative TargetHeightRange is not preserved correctly."
        );

        bool clampPassed =
            Approximately(
                stamp.EvaluateTargetHeight(-5f),
                1000f
            )
            &&
            Approximately(
                stamp.EvaluateTargetHeight(5f),
                200f
            )
            &&
            Approximately(
                stamp.EvaluateTargetHeight(float.NaN),
                1000f
            );

        Add(
            "Target-height source clamping",
            clampPassed,
            clampPassed
                ? "Target-height evaluation clamps finite source values to [0,1] and canonicalizes NaN input."
                : "Target-height source clamping/canonicalization is incorrect."
        );
    }

    private static void ValidateSanitizationAndBounds()
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
            27f
        );

        Bounds before =
            stamp.GetAffectedWorldBounds();

        stamp.SetTargetBaseHeightInternal(
            float.NaN
        );

        stamp.SetTargetHeightRangeInternal(
            float.PositiveInfinity
        );

        Bounds after =
            stamp.GetAffectedWorldBounds();

        bool passed =
            Approximately(
                stamp.TargetBaseHeight,
                0f
            )
            &&
            Approximately(
                stamp.TargetHeightRange,
                0f
            )
            &&
            BoundsApproximately(
                before,
                after
            );

        Add(
            "Target parameter sanitization / Bounds",
            passed,
            passed
                ? "Non-finite target parameters canonicalize to finite zero without changing the XZ footprint."
                : "Target parameter sanitization or footprint preservation is incorrect."
        );
    }

    private static void ValidateSignatureContract()
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        string baseline =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetTargetBaseHeightInternal(
            123f
        );

        string changedBase =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetTargetBaseHeightInternal(
            0f
        );

        string restoredBase =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetTargetHeightRangeInternal(
            -456f
        );

        string changedRange =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        stamp.SetTargetHeightRangeInternal(
            10f
        );

        string restoredRange =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        bool passed =
            !string.IsNullOrEmpty(
                baseline
            )
            &&
            changedBase !=
                baseline
            &&
            restoredBase ==
                baseline
            &&
            changedRange !=
                baseline
            &&
            restoredRange ==
                baseline;

        Add(
            "Target-surface deterministic signature",
            passed,
            passed
                ? "TargetBaseHeight and TargetHeightRange participate deterministically in the V7 modifier content signature."
                : "Target-surface signature behavior is not deterministic."
        );
    }

    private static void ValidateDirtyRegion(
        Bounds expectedBounds
    )
    {
        HashSet<Vector2Int> expected =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                expectedBounds,
                expected,
                1
            );

        HashSet<Vector2Int> actual =
            new HashSet<Vector2Int>(
                TerrainAuthoringModifierService
                    .LastMutationDiagnostics
                    .DirtyTiles
            );

        bool passed =
            actual.SetEquals(
                expected
            );

        Add(
            "Target parameter dirty region",
            passed,
            passed
                ? "The target parameter mutation dirtied the existing modifier footprint through the normal change tracker."
                : $"Expected {expected.Count} dirty tiles; actual {actual.Count}."
        );
    }

    private static void ValidateSerialization(
        string duplicateId
    )
    {
        EditorUtility.SetDirty(
            tempData
        );

        AssetDatabase.SaveAssets();

        TerrainAuthoringModifierChangeTracker
            .Forget(
                tempData
            );

        AssetDatabase.ForceReserializeAssets(
            new[]
            {
                ValidationDataPath
            }
        );

        AssetDatabase.ImportAsset(
            ValidationDataPath,
            ImportAssetOptions.ForceUpdate
        );

        tempData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                ValidationDataPath
            );

        TerrainStampModifier original =
            FindStampModifier(
                tempData,
                modifierId
            );

        TerrainStampModifier duplicate =
            FindStampModifier(
                tempData,
                duplicateId
            );

        bool passed =
            original != null
            &&
            duplicate != null
            &&
            Approximately(
                original.TargetBaseHeight,
                200f
            )
            &&
            Approximately(
                original.TargetHeightRange,
                800f
            )
            &&
            Approximately(
                duplicate.TargetBaseHeight,
                200f
            )
            &&
            Approximately(
                duplicate.TargetHeightRange,
                800f
            );

        Add(
            "Target-surface serialization persistence",
            passed,
            passed
                ? "Original and duplicated target-surface values survived save, force-reserialize, and reload."
                : "Target-surface values did not survive serialization/reload."
        );
    }

    private static void ValidateAdditiveCompatibility(
        TerrainHeightStampAsset stampAsset
    )
    {
        bool gpuSupported =
            stampAsset != null
            &&
            stampAsset.HeightTexture != null
            &&
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
                "Additive target-field output compatibility",
                false,
                "Required RFloat/compute/2D-array/CopyTexture/AsyncGPUReadback support is unavailable."
            );

            return;
        }

        const int samplesPerSide =
            17;

        const float sampleSpacing =
            1f;

        const float tileWorldSize =
            16f;

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        Texture2D seed = null;
        RenderTexture target = null;

        try
        {
            TerrainStampModifier stamp =
                new TerrainStampModifier();

            stamp.SetStampAssetInternal(
                stampAsset
            );

            stamp.SetPositionXZInternal(
                new Vector2(
                    8f,
                    8f
                )
            );

            stamp.SetSizeXZInternal(
                new Vector2(
                    12f,
                    12f
                )
            );

            stamp.SetHeightDeltaInternal(
                25f
            );

            stamp.SetFalloffInternal(
                0.25f
            );

            stamp.SetSmoothingRadiusInternal(
                0f
            );

            stamp.SetBlendModeInternal(
                TerrainHeightBlendMode.Additive
            );

            stamp.SetEnabledInternal(
                true
            );

            data.AddHeightModifierInternal(
                stamp
            );

            data.RepairModifierStableIds();

            float[] seedValues =
                new float[
                    samplesPerSide *
                    samplesPerSide
                ];

            for (
                int y = 0;
                y < samplesPerSide;
                y++
            )
            {
                for (
                    int x = 0;
                    x < samplesPerSide;
                    x++
                )
                {
                    seedValues[
                        y * samplesPerSide + x
                    ] =
                        100f
                        +
                        x * 0.1f
                        +
                        y * 0.2f;
                }
            }

            seed =
                new Texture2D(
                    samplesPerSide,
                    samplesPerSide,
                    TextureFormat.RFloat,
                    false,
                    true
                );

            seed.SetPixelData(
                seedValues,
                0
            );

            seed.Apply(
                false,
                false
            );

            target =
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

            if (!target.Create())
            {
                Add(
                    "Additive target-field output compatibility",
                    false,
                    "Could not create the temporary RFloat composition target."
                );

                return;
            }

            if (
                !TryComposeAndRead(
                    target,
                    seed,
                    data,
                    samplesPerSide,
                    sampleSpacing,
                    tileWorldSize,
                    out float[] before,
                    out string beforeError
                )
            )
            {
                Add(
                    "Additive target-field output compatibility",
                    false,
                    beforeError
                );

                return;
            }

            stamp.SetTargetBaseHeightInternal(
                765f
            );

            stamp.SetTargetHeightRangeInternal(
                -432f
            );

            if (
                !TryComposeAndRead(
                    target,
                    seed,
                    data,
                    samplesPerSide,
                    sampleSpacing,
                    tileWorldSize,
                    out float[] after,
                    out string afterError
                )
            )
            {
                Add(
                    "Additive target-field output compatibility",
                    false,
                    afterError
                );

                return;
            }

            float maximumDifference =
                0f;

            float maximumContribution =
                0f;

            bool sameOutput =
                before.Length ==
                    after.Length
                &&
                before.Length ==
                    seedValues.Length;

            if (sameOutput)
            {
                for (
                    int index = 0;
                    index < before.Length;
                    index++
                )
                {
                    maximumDifference =
                        Mathf.Max(
                            maximumDifference,
                            Mathf.Abs(
                                before[index] -
                                after[index]
                            )
                        );

                    maximumContribution =
                        Mathf.Max(
                            maximumContribution,
                            Mathf.Abs(
                                before[index] -
                                seedValues[index]
                            )
                        );
                }

                sameOutput =
                    maximumDifference <=
                        GpuTolerance;
            }

            bool passed =
                sameOutput
                &&
                maximumContribution >
                    0.01f;

            Add(
                "Additive target-field output compatibility",
                passed,
                passed
                    ? $"Changing only TargetBaseHeight/TargetHeightRange produced identical Additive GPU output (max difference {maximumDifference}) while the stamp made a non-zero contribution."
                    : $"Max output difference={maximumDifference}, max additive contribution={maximumContribution}."
            );
        }
        finally
        {
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(
                    target
                );
            }

            if (seed != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    seed
                );
            }

            if (data != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    data
                );
            }
        }
    }

    private static bool TryComposeAndRead(
        RenderTexture target,
        Texture2D seed,
        TerrainAuthoringData data,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        out float[] values,
        out string errorMessage
    )
    {
        values =
            null;

        errorMessage =
            "";

        try
        {
            Graphics.CopyTexture(
                seed,
                0,
                0,
                target,
                0,
                0
            );
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not seed the temporary additive composition target.\n\n" +
                exception.Message;

            return false;
        }

        TerrainHeightCompositor compositor =
            new TerrainHeightCompositor();

        if (
            !compositor.TryComposeTile(
                target,
                Vector2Int.zero,
                0,
                samplesPerSide,
                sampleSpacing,
                tileWorldSize,
                new Vector2(
                    tileWorldSize,
                    tileWorldSize
                ),
                data,
                out errorMessage
            )
        )
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
                "AsyncGPUReadback failed for the temporary additive composition target.";

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

    private static Texture2D CreateStampTexture()
    {
        const int size =
            8;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RFloat,
                false,
                true
            );

        texture.name =
            "TargetSurfaceStampTexture";

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
                        x + y
                    ) /
                    (
                        2f *
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
                    tempData,
                    modifierId
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
                Approximately(
                    stamp.TargetBaseHeight,
                    undoExpectedBaseHeight
                )
                &&
                Approximately(
                    stamp.TargetHeightRange,
                    undoExpectedHeightRange
                )
                &&
                tempData.authoringRevision ==
                    undoExpectedRevision
                &&
                signature ==
                    undoExpectedSignature;

            Add(
                "Undo restores target-surface state",
                passed,
                passed
                    ? "Undo restored target values, authoringRevision, and deterministic modifier signature."
                    : "Undo did not restore the expected target-surface state."
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
                    tempData,
                    modifierId
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
                Approximately(
                    stamp.TargetBaseHeight,
                    redoExpectedBaseHeight
                )
                &&
                Approximately(
                    stamp.TargetHeightRange,
                    redoExpectedHeightRange
                )
                &&
                tempData.authoringRevision ==
                    redoExpectedRevision
                &&
                signature ==
                    redoExpectedSignature;

            Add(
                "Redo restores target-surface state",
                passed,
                passed
                    ? "Redo restored target values, authoringRevision, and deterministic modifier signature."
                    : "Redo did not restore the expected target-surface state."
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
        if (tempData != null)
        {
            TerrainAuthoringModifierChangeTracker
                .Forget(
                    tempData
                );

            Undo.ClearUndo(
                tempData
            );
        }

        tempData =
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
                "WorldMeshes Target-Surface Blend Foundation validation\n\n" +
                builder
            );
        }
        else
        {
            Debug.Log(
                "WorldMeshes Target-Surface Blend Foundation validation\n\n" +
                builder
            );
        }

        validationScheduled =
            false;

        validationRunning =
            false;

        worldSettings =
            null;

        modifierId =
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

    private static bool BoundsApproximately(
        Bounds a,
        Bounds b
    )
    {
        return
            Vector3.Distance(
                a.center,
                b.center
            ) <=
            MathTolerance
            &&
            Vector3.Distance(
                a.size,
                b.size
            ) <=
            MathTolerance;
    }
}
