using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainBlendModeAuthoringUXDefaultsValidationUtility
{
    private sealed class Result
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    private sealed class StampSnapshot
    {
        public TerrainHeightStampAsset StampAsset;
        public Vector2 PositionXZ;
        public Vector2 SizeXZ;
        public float RotationDegrees;
        public bool FlipX;
        public bool FlipZ;
        public float SourceInputMin;
        public float SourceInputMax;
        public float SourceGamma;
        public float HeightDelta;
        public float TargetBaseHeight;
        public float TargetHeightRange;
        public TerrainStampFalloffShape FalloffShape;
        public TerrainStampFalloffProfile FalloffProfile;
        public float Falloff;
        public float SmoothingRadius;
        public float SmoothingStrength;
        public TerrainHeightBlendMode BlendMode;
        public bool Enabled;

        public static StampSnapshot Capture(
            TerrainStampModifier stamp
        )
        {
            if (stamp == null)
            {
                return null;
            }

            return
                new StampSnapshot
                {
                    StampAsset = stamp.StampAsset,
                    PositionXZ = stamp.PositionXZ,
                    SizeXZ = stamp.SizeXZ,
                    RotationDegrees = stamp.RotationDegrees,
                    FlipX = stamp.FlipX,
                    FlipZ = stamp.FlipZ,
                    SourceInputMin = stamp.SourceInputMin,
                    SourceInputMax = stamp.SourceInputMax,
                    SourceGamma = stamp.SourceGamma,
                    HeightDelta = stamp.HeightDelta,
                    TargetBaseHeight = stamp.TargetBaseHeight,
                    TargetHeightRange = stamp.TargetHeightRange,
                    FalloffShape = stamp.FalloffShape,
                    FalloffProfile = stamp.FalloffProfile,
                    Falloff = stamp.Falloff,
                    SmoothingRadius = stamp.SmoothingRadius,
                    SmoothingStrength = stamp.SmoothingStrength,
                    BlendMode = stamp.BlendMode,
                    Enabled = stamp.Enabled
                };
        }

        public bool Matches(
            TerrainStampModifier stamp,
            bool compareAsset
        )
        {
            return
                stamp != null
                &&
                (!compareAsset || stamp.StampAsset == StampAsset)
                &&
                Approximately(PositionXZ, stamp.PositionXZ)
                &&
                Approximately(SizeXZ, stamp.SizeXZ)
                &&
                Mathf.Approximately(
                    RotationDegrees,
                    stamp.RotationDegrees
                )
                &&
                FlipX == stamp.FlipX
                &&
                FlipZ == stamp.FlipZ
                &&
                Mathf.Approximately(
                    SourceInputMin,
                    stamp.SourceInputMin
                )
                &&
                Mathf.Approximately(
                    SourceInputMax,
                    stamp.SourceInputMax
                )
                &&
                Mathf.Approximately(
                    SourceGamma,
                    stamp.SourceGamma
                )
                &&
                Mathf.Approximately(
                    HeightDelta,
                    stamp.HeightDelta
                )
                &&
                Mathf.Approximately(
                    TargetBaseHeight,
                    stamp.TargetBaseHeight
                )
                &&
                Mathf.Approximately(
                    TargetHeightRange,
                    stamp.TargetHeightRange
                )
                &&
                FalloffShape == stamp.FalloffShape
                &&
                FalloffProfile == stamp.FalloffProfile
                &&
                Mathf.Approximately(
                    Falloff,
                    stamp.Falloff
                )
                &&
                Mathf.Approximately(
                    SmoothingRadius,
                    stamp.SmoothingRadius
                )
                &&
                Mathf.Approximately(
                    SmoothingStrength,
                    stamp.SmoothingStrength
                )
                &&
                BlendMode == stamp.BlendMode
                &&
                Enabled == stamp.Enabled;
        }
    }

    private static readonly List<Result>
        results =
            new List<Result>();

    private static readonly string ValidationRoot =
        WorldMeshesPaths.GeneratedValidation
        +
        "/BlendModeAuthoringUXDefaults";

    private static readonly string ValidationDataPath =
        ValidationRoot
        +
        "/TerrainAuthoringData.asset";

    private static readonly string LegacyAssetPath =
        ValidationRoot
        +
        "/LegacyV1.asset";

    private static readonly string AdditiveAssetPath =
        ValidationRoot
        +
        "/AdditiveDefaults.asset";

    private static readonly string MaxAssetPath =
        ValidationRoot
        +
        "/MaxDefaults.asset";

    private static readonly string MinAssetPath =
        ValidationRoot
        +
        "/MinDefaults.asset";

    private static readonly string ReplaceAssetPath =
        ValidationRoot
        +
        "/ReplaceDefaults.asset";

    private static bool validationScheduled;
    private static bool validationRunning;

    private static int lastPassedCount;
    private static int lastFailedCount;

    private static string lastSummary =
        "Not run.";

    private static WorldSettings worldSettings;
    private static TerrainAuthoringData mutationData;
    private static string undoModifierId;

    private static TerrainHeightBlendMode undoExpectedBlendMode;
    private static TerrainHeightBlendMode redoExpectedBlendMode;
    private static int undoExpectedRevision;
    private static int redoExpectedRevision;
    private static string undoExpectedSignature;
    private static string redoExpectedSignature;
    private static float expectedInactiveHeightDelta;
    private static float expectedInactiveTargetBaseHeight;
    private static float expectedInactiveTargetHeightRange;

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
                "Run Package 4 validation while the editor is idle in Edit Mode."
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

        CleanupValidationAssets();
        EnsureFolder(
            ValidationRoot
        );

        ValidateAuthoringMetadata();
        ValidateBaselineDefaults();
        ValidateLegacyVersionOneCompatibility();
        ValidateDefaultSanitization();

        return
            PrepareCreationAndUndoRedoValidation(
                realData.authoringRevision
            );
    }

    private static void ValidateAuthoringMetadata()
    {
        bool enumContract =
            (int)TerrainHeightBlendMode.Additive == 0
            &&
            (int)TerrainHeightBlendMode.Max == 1
            &&
            (int)TerrainHeightBlendMode.Min == 2
            &&
            (int)TerrainHeightBlendMode.Replace == 3;

        Add(
            "Blend-mode enum contract",
            enumContract,
            enumContract
                ? "Additive=0, Max=1, Min=2, Replace=3."
                : "TerrainHeightBlendMode numeric values changed unexpectedly."
        );

        bool supportContract =
            TerrainHeightBlendModeAuthoringUtility
                .IsSupported(
                    TerrainHeightBlendMode.Additive
                )
            &&
            TerrainHeightBlendModeAuthoringUtility
                .IsSupported(
                    TerrainHeightBlendMode.Max
                )
            &&
            TerrainHeightBlendModeAuthoringUtility
                .IsSupported(
                    TerrainHeightBlendMode.Min
                )
            &&
            TerrainHeightBlendModeAuthoringUtility
                .IsSupported(
                    TerrainHeightBlendMode.Replace
                )
            &&
            !TerrainHeightBlendModeAuthoringUtility
                .IsSupported(
                    (TerrainHeightBlendMode)999
                );

        Add(
            "Authoring blend-mode support contract",
            supportContract,
            "Expected all four declared modes to be supported and unknown mode 999 to be rejected."
        );

        bool fieldRules =
            TerrainHeightBlendModeAuthoringUtility
                .UsesHeightDelta(
                    TerrainHeightBlendMode.Additive
                )
            &&
            !TerrainHeightBlendModeAuthoringUtility
                .UsesTargetSurface(
                    TerrainHeightBlendMode.Additive
                )
            &&
            !TerrainHeightBlendModeAuthoringUtility
                .UsesHeightDelta(
                    TerrainHeightBlendMode.Max
                )
            &&
            TerrainHeightBlendModeAuthoringUtility
                .UsesTargetSurface(
                    TerrainHeightBlendMode.Max
                )
            &&
            !TerrainHeightBlendModeAuthoringUtility
                .UsesHeightDelta(
                    TerrainHeightBlendMode.Min
                )
            &&
            TerrainHeightBlendModeAuthoringUtility
                .UsesTargetSurface(
                    TerrainHeightBlendMode.Min
                )
            &&
            !TerrainHeightBlendModeAuthoringUtility
                .UsesHeightDelta(
                    TerrainHeightBlendMode.Replace
                )
            &&
            TerrainHeightBlendModeAuthoringUtility
                .UsesTargetSurface(
                    TerrainHeightBlendMode.Replace
                );

        Add(
            "Mode-aware field visibility rules",
            fieldRules,
            "Additive must use Height Delta only; Max/Min/Replace must use target-surface fields only."
        );

        bool descriptions =
            !string.IsNullOrEmpty(
                TerrainHeightBlendModeAuthoringUtility
                    .GetDescription(
                        TerrainHeightBlendMode.Additive
                    )
            )
            &&
            !string.IsNullOrEmpty(
                TerrainHeightBlendModeAuthoringUtility
                    .GetDescription(
                        TerrainHeightBlendMode.Max
                    )
            )
            &&
            !string.IsNullOrEmpty(
                TerrainHeightBlendModeAuthoringUtility
                    .GetDescription(
                        TerrainHeightBlendMode.Min
                    )
            )
            &&
            !string.IsNullOrEmpty(
                TerrainHeightBlendModeAuthoringUtility
                    .GetDescription(
                        TerrainHeightBlendMode.Replace
                    )
            );

        Add(
            "Blend-mode descriptions",
            descriptions,
            "Every supported mode should provide a concise non-empty user-facing description."
        );

        Add(
            "Runtime compiler version unchanged",
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion == 10,
            $"RuntimeHeightCompilerVersion={TerrainGenerationStateUtility.RuntimeHeightCompilerVersion}; expected Package 3 value 10."
        );
    }

    private static void ValidateBaselineDefaults()
    {
        TerrainHeightStampAsset asset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        try
        {
            bool passed =
                TerrainHeightStampAsset
                    .CurrentCreationDefaultsVersion == 2
                &&
                asset.DefaultBlendMode ==
                    TerrainHeightBlendMode.Additive
                &&
                Mathf.Approximately(
                    asset.DefaultHeightDelta,
                    10f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultTargetBaseHeight,
                    0f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultTargetHeightRange,
                    10f
                )
                &&
                Approximately(
                    asset.DefaultSizeXZ,
                    new Vector2(
                        128f,
                        128f
                    )
                )
                &&
                Mathf.Approximately(
                    asset.DefaultFalloff,
                    0.25f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSmoothingRadius,
                    0f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSmoothingStrength,
                    1f
                );

            Add(
                "Version 2 baseline defaults",
                passed,
                "Expected Additive, Height Delta 10, target 0 + source*10, size 128x128, falloff 0.25, smoothing 0/1."
            );
        }
        finally
        {
            Destroy(
                asset
            );
        }
    }

    private static void ValidateLegacyVersionOneCompatibility()
    {
        TerrainHeightStampAsset legacy =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        legacy.name =
            "LegacyV1";

        AssetDatabase.CreateAsset(
            legacy,
            LegacyAssetPath
        );

        SerializedObject serialized =
            new SerializedObject(
                legacy
            );

        serialized.FindProperty(
            "creationDefaultsVersion"
        ).intValue =
            1;

        serialized.FindProperty(
            "defaultSizeXZ"
        ).vector2Value =
            new Vector2(
                321f,
                123f
            );

        serialized.FindProperty(
            "defaultHeightDelta"
        ).floatValue =
            -27f;

        serialized.FindProperty(
            "defaultSourceInputMin"
        ).floatValue =
            0.2f;

        serialized.FindProperty(
            "defaultSourceInputMax"
        ).floatValue =
            0.8f;

        serialized.FindProperty(
            "defaultSourceGamma"
        ).floatValue =
            1.7f;

        serialized.FindProperty(
            "defaultFalloffShape"
        ).intValue =
            (int)TerrainStampFalloffShape.Ellipse;

        serialized.FindProperty(
            "defaultFalloffProfile"
        ).intValue =
            (int)TerrainStampFalloffProfile.Sharp;

        serialized.FindProperty(
            "defaultFalloff"
        ).floatValue =
            0.6f;

        serialized.FindProperty(
            "defaultSmoothingRadius"
        ).floatValue =
            7f;

        serialized.FindProperty(
            "defaultSmoothingStrength"
        ).floatValue =
            0.35f;

        /*
         * Deliberately store non-baseline V2 values. Version 1 accessors must
         * ignore these and use the safe Package 4 fallback contract.
         */
        serialized.FindProperty(
            "defaultBlendMode"
        ).intValue =
            (int)TerrainHeightBlendMode.Replace;

        serialized.FindProperty(
            "defaultTargetBaseHeight"
        ).floatValue =
            999f;

        serialized.FindProperty(
            "defaultTargetHeightRange"
        ).floatValue =
            -999f;

        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();

        bool dirtyBeforeRead =
            EditorUtility.IsDirty(
                legacy.GetInstanceID()
            );

        bool oldValuesPreserved =
            Approximately(
                legacy.DefaultSizeXZ,
                new Vector2(
                    321f,
                    123f
                )
            )
            &&
            Mathf.Approximately(
                legacy.DefaultHeightDelta,
                -27f
            )
            &&
            Mathf.Approximately(
                legacy.DefaultSourceInputMin,
                0.2f
            )
            &&
            Mathf.Approximately(
                legacy.DefaultSourceInputMax,
                0.8f
            )
            &&
            Mathf.Approximately(
                legacy.DefaultSourceGamma,
                1.7f
            )
            &&
            legacy.DefaultFalloffShape ==
                TerrainStampFalloffShape.Ellipse
            &&
            legacy.DefaultFalloffProfile ==
                TerrainStampFalloffProfile.Sharp
            &&
            Mathf.Approximately(
                legacy.DefaultFalloff,
                0.6f
            )
            &&
            Mathf.Approximately(
                legacy.DefaultSmoothingRadius,
                7f
            )
            &&
            Mathf.Approximately(
                legacy.DefaultSmoothingStrength,
                0.35f
            );

        Add(
            "Version 1 stored defaults survive Version 2 introduction",
            oldValuesPreserved,
            "Version 1 size, delta, source response, falloff, and smoothing must continue reading from their stored values."
        );

        bool newFallbacks =
            legacy.DefaultBlendMode ==
                TerrainHeightBlendMode.Additive
            &&
            Mathf.Approximately(
                legacy.DefaultTargetBaseHeight,
                0f
            )
            &&
            Mathf.Approximately(
                legacy.DefaultTargetHeightRange,
                10f
            );

        Add(
            "Version 1 blend defaults use safe fallbacks",
            newFallbacks,
            $"Blend={legacy.DefaultBlendMode}, target base={legacy.DefaultTargetBaseHeight}, target range={legacy.DefaultTargetHeightRange}."
        );

        bool dirtyAfterRead =
            EditorUtility.IsDirty(
                legacy.GetInstanceID()
            );

        int storedVersion =
            new SerializedObject(
                legacy
            )
                .FindProperty(
                    "creationDefaultsVersion"
                )
                .intValue;

        Add(
            "Reading Version 1 defaults does not auto-upgrade asset",
            !dirtyBeforeRead
            &&
            !dirtyAfterRead
            &&
            storedVersion == 1,
            $"Dirty before={dirtyBeforeRead}, dirty after={dirtyAfterRead}, stored version={storedVersion}."
        );
    }

    private static void ValidateDefaultSanitization()
    {
        TerrainHeightStampAsset asset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        try
        {
            asset.SetCreationDefaultsInternal(
                new Vector2(
                    128f,
                    128f
                ),
                (TerrainHeightBlendMode)999,
                10f,
                float.NaN,
                float.PositiveInfinity,
                0f,
                1f,
                1f,
                TerrainStampFalloffShape.Rectangle,
                TerrainStampFalloffProfile.Smooth,
                0.25f,
                0f,
                1f
            );

            bool invalidFallbacks =
                asset.DefaultBlendMode ==
                    TerrainHeightBlendMode.Additive
                &&
                Mathf.Approximately(
                    asset.DefaultTargetBaseHeight,
                    0f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultTargetHeightRange,
                    10f
                );

            Add(
                "Invalid Version 2 output defaults sanitize safely",
                invalidFallbacks,
                $"Blend={asset.DefaultBlendMode}, target base={asset.DefaultTargetBaseHeight}, target range={asset.DefaultTargetHeightRange}."
            );

            asset.SetCreationDefaultsInternal(
                new Vector2(
                    128f,
                    128f
                ),
                TerrainHeightBlendMode.Replace,
                10f,
                250f,
                -125f,
                0f,
                1f,
                1f,
                TerrainStampFalloffShape.Rectangle,
                TerrainStampFalloffProfile.Smooth,
                0.25f,
                0f,
                1f
            );

            bool negativeRange =
                asset.DefaultBlendMode ==
                    TerrainHeightBlendMode.Replace
                &&
                Mathf.Approximately(
                    asset.DefaultTargetBaseHeight,
                    250f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultTargetHeightRange,
                    -125f
                );

            Add(
                "Negative target creation range is preserved",
                negativeRange,
                $"Base={asset.DefaultTargetBaseHeight}, range={asset.DefaultTargetHeightRange}."
            );

            asset.SetCreationDefaultsInternal(
                new Vector2(
                    128f,
                    128f
                ),
                TerrainHeightBlendMode.Min,
                10f,
                75f,
                0f,
                0f,
                1f,
                1f,
                TerrainStampFalloffShape.Rectangle,
                TerrainStampFalloffProfile.Smooth,
                0.25f,
                0f,
                1f
            );

            Add(
                "Zero target creation range is preserved",
                Mathf.Approximately(
                    asset.DefaultTargetHeightRange,
                    0f
                ),
                $"Range={asset.DefaultTargetHeightRange}."
            );
        }
        finally
        {
            Destroy(
                asset
            );
        }
    }

    private static bool PrepareCreationAndUndoRedoValidation(
        int baseRevision
    )
    {
        mutationData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        mutationData.authoringRevision =
            baseRevision;

        AssetDatabase.CreateAsset(
            mutationData,
            ValidationDataPath
        );

        TerrainHeightStampAsset additiveAsset =
            CreateDefaultAsset(
                AdditiveAssetPath,
                TerrainHeightBlendMode.Additive,
                21f,
                110f,
                31f
            );

        TerrainHeightStampAsset maxAsset =
            CreateDefaultAsset(
                MaxAssetPath,
                TerrainHeightBlendMode.Max,
                22f,
                210f,
                -41f
            );

        TerrainHeightStampAsset minAsset =
            CreateDefaultAsset(
                MinAssetPath,
                TerrainHeightBlendMode.Min,
                23f,
                310f,
                0f
            );

        TerrainHeightStampAsset replaceAsset =
            CreateDefaultAsset(
                ReplaceAssetPath,
                TerrainHeightBlendMode.Replace,
                24f,
                410f,
                61f
            );

        AssetDatabase.SaveAssets();

        float tileSize =
            worldSettings.HeightTileWorldSize;

        Vector2 position =
            new Vector2(
                tileSize * 0.5f,
                tileSize * 0.5f
            );

        TerrainStampModifier additiveStamp =
            ValidateCreationFromDefaults(
                "Additive asset default creation",
                additiveAsset,
                position
            );

        TerrainStampModifier maxStamp =
            ValidateCreationFromDefaults(
                "Max asset default creation",
                maxAsset,
                position
            );

        TerrainStampModifier minStamp =
            ValidateCreationFromDefaults(
                "Min asset default creation",
                minAsset,
                position
            );

        TerrainStampModifier replaceStamp =
            ValidateCreationFromDefaults(
                "Replace asset default creation",
                replaceAsset,
                position
            );

        if (
            additiveStamp == null
            ||
            maxStamp == null
            ||
            minStamp == null
            ||
            replaceStamp == null
        )
        {
            Add(
                "Creation/default integration setup",
                false,
                "One or more blend-mode default stamps could not be created."
            );

            return false;
        }

        ValidateCopyOnCreationIndependence(
            replaceAsset,
            replaceStamp
        );

        ValidateAssetReassignment(
            maxAsset,
            replaceStamp
        );

        ValidateDuplication(
            replaceStamp
        );

        if (
            !ValidateModeSwitchingAndInactiveValues(
                additiveStamp
            )
        )
        {
            return false;
        }

        return
            PrepareUndoRedoValidation(
                additiveStamp
            );
    }

    private static TerrainHeightStampAsset CreateDefaultAsset(
        string path,
        TerrainHeightBlendMode blendMode,
        float heightDelta,
        float targetBaseHeight,
        float targetHeightRange
    )
    {
        TerrainHeightStampAsset asset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        asset.name =
            TerrainHeightBlendModeAuthoringUtility
                .GetDisplayName(
                    blendMode
                )
            +
            "Defaults";

        asset.SetCreationDefaultsInternal(
            new Vector2(
                96f,
                72f
            ),
            blendMode,
            heightDelta,
            targetBaseHeight,
            targetHeightRange,
            0.1f,
            0.9f,
            1.4f,
            TerrainStampFalloffShape.Ellipse,
            TerrainStampFalloffProfile.Sharp,
            0.4f,
            5f,
            0.65f
        );

        AssetDatabase.CreateAsset(
            asset,
            path
        );

        return asset;
    }

    private static TerrainStampModifier ValidateCreationFromDefaults(
        string name,
        TerrainHeightStampAsset asset,
        Vector2 position
    )
    {
        bool added =
            TerrainAuthoringModifierService
                .AddStampModifierFromAssetDefaults(
                    mutationData,
                    worldSettings,
                    asset,
                    position,
                    out string stableId,
                    out string errorMessage
                );

        TerrainStampModifier stamp =
            FindStampModifier(
                mutationData,
                stableId
            );

        bool passed =
            added
            &&
            stamp != null
            &&
            stamp.StampAsset == asset
            &&
            stamp.BlendMode == asset.DefaultBlendMode
            &&
            Mathf.Approximately(
                stamp.HeightDelta,
                asset.DefaultHeightDelta
            )
            &&
            Mathf.Approximately(
                stamp.TargetBaseHeight,
                asset.DefaultTargetBaseHeight
            )
            &&
            Mathf.Approximately(
                stamp.TargetHeightRange,
                asset.DefaultTargetHeightRange
            )
            &&
            Approximately(
                stamp.SizeXZ,
                asset.DefaultSizeXZ
            )
            &&
            Mathf.Approximately(
                stamp.SourceInputMin,
                asset.DefaultSourceInputMin
            )
            &&
            Mathf.Approximately(
                stamp.SourceInputMax,
                asset.DefaultSourceInputMax
            )
            &&
            Mathf.Approximately(
                stamp.SourceGamma,
                asset.DefaultSourceGamma
            )
            &&
            stamp.FalloffShape ==
                asset.DefaultFalloffShape
            &&
            stamp.FalloffProfile ==
                asset.DefaultFalloffProfile
            &&
            Mathf.Approximately(
                stamp.Falloff,
                asset.DefaultFalloff
            )
            &&
            Mathf.Approximately(
                stamp.SmoothingRadius,
                asset.DefaultSmoothingRadius
            )
            &&
            Mathf.Approximately(
                stamp.SmoothingStrength,
                asset.DefaultSmoothingStrength
            );

        Add(
            name,
            passed,
            passed
                ? $"Created {stamp.BlendMode} and copied active plus inactive output/default values once."
                : errorMessage
        );

        return
            passed
                ? stamp
                : null;
    }

    private static void ValidateCopyOnCreationIndependence(
        TerrainHeightStampAsset sourceAsset,
        TerrainStampModifier placed
    )
    {
        StampSnapshot before =
            StampSnapshot.Capture(
                placed
            );

        sourceAsset.SetCreationDefaultsInternal(
            new Vector2(
                512f,
                384f
            ),
            TerrainHeightBlendMode.Additive,
            99f,
            999f,
            -333f,
            0.25f,
            0.75f,
            2f,
            TerrainStampFalloffShape.Rectangle,
            TerrainStampFalloffProfile.Linear,
            0.1f,
            20f,
            0.2f
        );

        bool passed =
            before != null
            &&
            before.Matches(
                placed,
                true
            );

        Add(
            "Asset default changes do not mutate placed stamp",
            passed,
            passed
                ? "Blend mode, output values, and all other copied defaults remained instance-owned after placement."
                : "A placed stamp changed when its source asset defaults were edited."
        );
    }

    private static void ValidateAssetReassignment(
        TerrainHeightStampAsset replacementAsset,
        TerrainStampModifier placed
    )
    {
        StampSnapshot before =
            StampSnapshot.Capture(
                placed
            );

        bool changed =
            TerrainAuthoringModifierService
                .SetStampAsset(
                    mutationData,
                    worldSettings,
                    placed.StableId,
                    replacementAsset,
                    out string errorMessage
                );

        bool passed =
            changed
            &&
            placed.StampAsset == replacementAsset
            &&
            before != null
            &&
            before.Matches(
                placed,
                false
            );

        Add(
            "Stamp Asset reassignment does not reapply defaults",
            passed,
            passed
                ? "Only the StampAsset reference changed; blend, delta, target, source, falloff, and smoothing instance values were preserved."
                : errorMessage
        );
    }

    private static void ValidateDuplication(
        TerrainStampModifier source
    )
    {
        StampSnapshot before =
            StampSnapshot.Capture(
                source
            );

        bool duplicated =
            TerrainAuthoringModifierService
                .DuplicateModifier(
                    mutationData,
                    worldSettings,
                    source.StableId,
                    out string duplicateStableId,
                    out string errorMessage
                );

        TerrainStampModifier duplicate =
            FindStampModifier(
                mutationData,
                duplicateStableId
            );

        bool passed =
            duplicated
            &&
            duplicate != null
            &&
            duplicate.StableId !=
                source.StableId
            &&
            before != null
            &&
            before.Matches(
                duplicate,
                true
            );

        Add(
            "Duplicate preserves instance blend/output state",
            passed,
            passed
                ? "DuplicateModifier copied active and inactive instance values without re-reading asset defaults and generated a fresh StableId."
                : errorMessage
        );
    }

    private static bool ValidateModeSwitchingAndInactiveValues(
        TerrainStampModifier stamp
    )
    {
        bool deltaChanged =
            TerrainAuthoringModifierService
                .SetStampHeightDelta(
                    mutationData,
                    worldSettings,
                    stamp.StableId,
                    33f,
                    out string deltaError
                );

        bool baseChanged =
            TerrainAuthoringModifierService
                .SetStampTargetBaseHeight(
                    mutationData,
                    worldSettings,
                    stamp.StableId,
                    275f,
                    out string baseError
                );

        bool rangeChanged =
            TerrainAuthoringModifierService
                .SetStampTargetHeightRange(
                    mutationData,
                    worldSettings,
                    stamp.StableId,
                    -85f,
                    out string rangeError
                );

        if (
            !deltaChanged
            ||
            !baseChanged
            ||
            !rangeChanged
        )
        {
            Add(
                "Mode-switch inactive-value setup",
                false,
                !deltaChanged
                    ? deltaError
                    : !baseChanged
                        ? baseError
                        : rangeError
            );

            return false;
        }

        TerrainHeightBlendMode[] sequence =
        {
            TerrainHeightBlendMode.Max,
            TerrainHeightBlendMode.Min,
            TerrainHeightBlendMode.Replace,
            TerrainHeightBlendMode.Additive
        };

        bool switched =
            true;

        string switchError =
            "";

        for (
            int index = 0;
            index < sequence.Length;
            index++
        )
        {
            if (
                !TerrainAuthoringModifierService
                    .SetModifierBlendMode(
                        mutationData,
                        worldSettings,
                        stamp.StableId,
                        sequence[index],
                        out switchError
                    )
            )
            {
                switched =
                    false;
                break;
            }

            if (
                !Mathf.Approximately(
                    stamp.HeightDelta,
                    33f
                )
                ||
                !Mathf.Approximately(
                    stamp.TargetBaseHeight,
                    275f
                )
                ||
                !Mathf.Approximately(
                    stamp.TargetHeightRange,
                    -85f
                )
            )
            {
                switched =
                    false;
                switchError =
                    "A hidden inactive output value changed while switching blend modes.";
                break;
            }
        }

        bool passed =
            switched
            &&
            stamp.BlendMode ==
                TerrainHeightBlendMode.Additive
            &&
            Mathf.Approximately(
                stamp.HeightDelta,
                33f
            )
            &&
            Mathf.Approximately(
                stamp.TargetBaseHeight,
                275f
            )
            &&
            Mathf.Approximately(
                stamp.TargetHeightRange,
                -85f
            );

        Add(
            "Mode switching preserves hidden inactive output values",
            passed,
            passed
                ? "Additive -> Max -> Min -> Replace -> Additive preserved Height Delta and target-surface values."
                : switchError
        );

        return passed;
    }

    private static bool PrepareUndoRedoValidation(
        TerrainStampModifier stamp
    )
    {
        TerrainAuthoringModifierService
            .SetModifierBlendMode(
                mutationData,
                worldSettings,
                stamp.StableId,
                TerrainHeightBlendMode.Additive,
                out _
            );

        Undo.ClearUndo(
            mutationData
        );

        stamp =
            FindStampModifier(
                mutationData,
                stamp.StableId
            );

        if (stamp == null)
        {
            Add(
                "Undo/Redo setup",
                false,
                "The validation stamp could not be found before the final mode mutation."
            );

            return false;
        }

        undoModifierId =
            stamp.StableId;

        undoExpectedBlendMode =
            stamp.BlendMode;

        undoExpectedRevision =
            mutationData.authoringRevision;

        undoExpectedSignature =
            TerrainAuthoringStateUtility
                .GetModifierContentSignature(
                    stamp
                );

        expectedInactiveHeightDelta =
            stamp.HeightDelta;

        expectedInactiveTargetBaseHeight =
            stamp.TargetBaseHeight;

        expectedInactiveTargetHeightRange =
            stamp.TargetHeightRange;

        Bounds bounds =
            stamp.GetAffectedWorldBounds();

        bool changed =
            TerrainAuthoringModifierService
                .SetModifierBlendMode(
                    mutationData,
                    worldSettings,
                    stamp.StableId,
                    TerrainHeightBlendMode.Replace,
                    out string errorMessage
                );

        stamp =
            FindStampModifier(
                mutationData,
                undoModifierId
            );

        if (
            !changed
            ||
            stamp == null
        )
        {
            Add(
                "Undo/Redo setup",
                false,
                errorMessage
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

        bool setupPassed =
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
                undoExpectedSignature
            &&
            actualDirty.SetEquals(
                expectedDirty
            )
            &&
            Mathf.Approximately(
                stamp.HeightDelta,
                expectedInactiveHeightDelta
            )
            &&
            Mathf.Approximately(
                stamp.TargetBaseHeight,
                expectedInactiveTargetBaseHeight
            )
            &&
            Mathf.Approximately(
                stamp.TargetHeightRange,
                expectedInactiveTargetHeightRange
            );

        Add(
            "Blend-mode mutation uses existing revision/signature/dirty pipeline",
            setupPassed,
            setupPassed
                ? "Additive -> Replace changed revision/signature once, dirtied the expected footprint, and preserved inactive values."
                : errorMessage
        );

        return setupPassed;
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
                    undoModifierId
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
                    undoExpectedSignature
                &&
                OutputValuesMatchExpected(
                    stamp
                );

            Add(
                "Undo restores Blend Mode and output instance state",
                passed,
                passed
                    ? "Undo restored Additive mode, revision, signature, and preserved active/inactive output values."
                    : "Undo did not restore the expected Package 4 mode/default state."
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
                    undoModifierId
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
                    redoExpectedSignature
                &&
                OutputValuesMatchExpected(
                    stamp
                );

            Add(
                "Redo restores Blend Mode and output instance state",
                passed,
                passed
                    ? "Redo restored Replace mode, revision, signature, and preserved active/inactive output values."
                    : "Redo did not restore the expected Package 4 mode/default state."
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

    private static bool OutputValuesMatchExpected(
        TerrainStampModifier stamp
    )
    {
        return
            Mathf.Approximately(
                stamp.HeightDelta,
                expectedInactiveHeightDelta
            )
            &&
            Mathf.Approximately(
                stamp.TargetBaseHeight,
                expectedInactiveTargetBaseHeight
            )
            &&
            Mathf.Approximately(
                stamp.TargetHeightRange,
                expectedInactiveTargetHeightRange
            );
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
                stamp.StableId == stableId
            )
            {
                return stamp;
            }
        }

        return null;
    }

    private static void CleanupValidationAssets()
    {
        if (mutationData != null)
        {
            Undo.ClearUndo(
                mutationData
            );

            TerrainAuthoringModifierChangeTracker
                .Forget(
                    mutationData
                );
        }

        mutationData =
            null;

        undoModifierId =
            "";

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

    private static void EnsureFolder(
        string folderPath
    )
    {
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

    private static void Finish()
    {
        validationScheduled =
            false;

        validationRunning =
            false;

        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        int passed =
            0;

        int failed =
            0;

        StringBuilder summary =
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
                passed++;
            }
            else
            {
                failed++;
            }

            summary.Append(
                result.Passed
                    ? "PASS: "
                    : "FAIL: "
            );

            summary.AppendLine(
                result.Name
            );

            if (
                !string.IsNullOrEmpty(
                    result.Details
                )
            )
            {
                summary.AppendLine(
                    "  " +
                    result.Details
                );
            }
        }

        lastPassedCount =
            passed;

        lastFailedCount =
            failed;

        lastSummary =
            $"Passed: {passed}\nFailed: {failed}";

        if (failed == 0)
        {
            Debug.Log(
                "WorldMeshes Package 4 Blend Mode Authoring UX + Defaults validation passed.\n\n" +
                summary
            );
        }
        else
        {
            Debug.LogError(
                "WorldMeshes Package 4 Blend Mode Authoring UX + Defaults validation failed.\n\n" +
                summary
            );
        }

        CleanupValidationAssets();
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
                Details = details
            }
        );
    }

    private static bool Approximately(
        Vector2 left,
        Vector2 right
    )
    {
        return
            Mathf.Approximately(
                left.x,
                right.x
            )
            &&
            Mathf.Approximately(
                left.y,
                right.y
            );
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
