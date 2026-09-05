using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightStampAssetDefaultsValidationUtility
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

    private static bool validationScheduled;
    private static bool validationRunning;

    private static int lastPassedCount;
    private static int lastFailedCount;

    private static string lastSummary =
        "Not run.";

    private static readonly string ValidationRoot =
        WorldMeshesPaths.GeneratedValidation
        +
        "/StampAssetDefaults";

    private static readonly string ValidationDataPath =
        ValidationRoot
        +
        "/TerrainAuthoringData.asset";

    private static readonly string StampAPath =
        ValidationRoot
        +
        "/Heightmap_037.asset";

    private static readonly string StampBPath =
        ValidationRoot
        +
        "/Heightmap_038.asset";

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
            CleanupValidationAssets();
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
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "Run stamp asset defaults validation while the editor is idle in Edit Mode."
            );

            return;
        }

        WorldSettings worldSettings =
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
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "WorldSettings or TerrainAuthoringData could not be loaded."
            );

            return;
        }

        Add(
            "Validation prerequisites",
            true,
            "WorldSettings and TerrainAuthoringData are available."
        );

        ValidateBaselineDefaults();
        ValidateSanitization();

        CleanupValidationAssets();
        PrepareValidationFolder();

        TerrainAuthoringData tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.authoringRevision =
            realData.authoringRevision;

        AssetDatabase.CreateAsset(
            tempData,
            ValidationDataPath
        );

        TerrainHeightStampAsset stampA =
            CreateValidationStamp(
                StampAPath,
                37,
                new Vector2(
                    300f,
                    180f
                ),
                45f,
                0.1f,
                0.9f,
                1.6f,
                TerrainStampFalloffShape.Ellipse,
                TerrainStampFalloffProfile.Sharp,
                0.4f,
                6.5f,
                0.65f
            );

        TerrainHeightStampAsset stampB =
            CreateValidationStamp(
                StampBPath,
                38,
                new Vector2(
                    48f,
                    64f
                ),
                -12f,
                0.25f,
                0.75f,
                0.6f,
                TerrainStampFalloffShape.Rectangle,
                TerrainStampFalloffProfile.Linear,
                0.8f,
                2f,
                0.2f
            );

        AssetDatabase.SaveAssets();

        ValidateIdentityContract(
            stampA
        );

        Vector2 requestedPosition =
            new Vector2(
                120f,
                240f
            );

        bool created =
            TerrainAuthoringModifierService
                .AddStampModifierFromAssetDefaults(
                    tempData,
                    worldSettings,
                    stampA,
                    requestedPosition,
                    out string createdId,
                    out string createError
                );

        TerrainStampModifier placed =
            FindStampModifier(
                tempData,
                createdId
            );

        bool copyPassed =
            created
            &&
            placed != null
            &&
            placed.StampAsset == stampA
            &&
            Approximately(
                placed.PositionXZ,
                requestedPosition
            )
            &&
            Approximately(
                placed.SizeXZ,
                new Vector2(
                    300f,
                    180f
                )
            )
            &&
            Mathf.Approximately(
                placed.HeightDelta,
                45f
            )
            &&
            Mathf.Approximately(
                placed.SourceInputMin,
                0.1f
            )
            &&
            Mathf.Approximately(
                placed.SourceInputMax,
                0.9f
            )
            &&
            Mathf.Approximately(
                placed.SourceGamma,
                1.6f
            )
            &&
            placed.FalloffShape ==
                TerrainStampFalloffShape.Ellipse
            &&
            placed.FalloffProfile ==
                TerrainStampFalloffProfile.Sharp
            &&
            Mathf.Approximately(
                placed.Falloff,
                0.4f
            )
            &&
            Mathf.Approximately(
                placed.SmoothingRadius,
                6.5f
            )
            &&
            Mathf.Approximately(
                placed.SmoothingStrength,
                0.65f
            );

        Add(
            "Creation copies all asset defaults",
            copyPassed,
            copyPassed
                ? "Size, height, source response, falloff, and smoothing were copied into the new modifier."
                : createError
        );

        bool orientationPassed =
            placed != null
            &&
            Mathf.Approximately(
                placed.RotationDegrees,
                0f
            )
            &&
            !placed.FlipX
            &&
            !placed.FlipZ;

        Add(
            "Rotation and flips remain instance defaults",
            orientationPassed,
            orientationPassed
                ? "New asset-default creation used Rotation=0, FlipX=false, FlipZ=false."
                : "Unexpected orientation state was copied into the new modifier."
        );

        if (placed == null)
        {
            Add(
                "Asset changes do not mutate placed modifiers",
                false,
                "The validation modifier was not created."
            );

            Add(
                "SetStampAsset changes only the asset reference",
                false,
                "The validation modifier was not created."
            );

            Add(
                "DuplicateModifier preserves instance values",
                false,
                "The validation modifier was not created."
            );
        }
        else
        {
            StampSnapshot placedSnapshot =
                StampSnapshot.Capture(
                    placed
                );

            stampA.SetCreationDefaultsInternal(
                new Vector2(
                    900f,
                    700f
                ),
                99f,
                0.3f,
                0.6f,
                2.2f,
                TerrainStampFalloffShape.Rectangle,
                TerrainStampFalloffProfile.Linear,
                0.1f,
                20f,
                0.15f
            );

            bool independent =
                placedSnapshot.Matches(
                    placed,
                    true
                );

            Add(
                "Asset changes do not mutate placed modifiers",
                independent,
                independent
                    ? "Changing the source asset defaults left the already-placed modifier unchanged."
                    : "A placed modifier changed after its source asset defaults were edited."
            );

            StampSnapshot beforeReplacement =
                StampSnapshot.Capture(
                    placed
                );

            bool replacementSucceeded =
                TerrainAuthoringModifierService
                    .SetStampAsset(
                        tempData,
                        worldSettings,
                        placed.StableId,
                        stampB,
                        out string replacementError
                    );

            bool replacementPassed =
                replacementSucceeded
                &&
                placed.StampAsset == stampB
                &&
                beforeReplacement.Matches(
                    placed,
                    false
                );

            Add(
                "SetStampAsset changes only the asset reference",
                replacementPassed,
                replacementPassed
                    ? "Replacing StampAsset did not apply the replacement asset's defaults."
                    : replacementError
            );

            bool duplicateSucceeded =
                TerrainAuthoringModifierService
                    .DuplicateModifier(
                        tempData,
                        worldSettings,
                        placed.StableId,
                        out string duplicateId,
                        out string duplicateError
                    );

            TerrainStampModifier duplicate =
                FindStampModifier(
                    tempData,
                    duplicateId
                );

            bool duplicatePassed =
                duplicateSucceeded
                &&
                duplicate != null
                &&
                duplicate.StableId !=
                    placed.StableId
                &&
                StampSnapshot
                    .Capture(
                        placed
                    )
                    .Matches(
                        duplicate,
                        true
                    );

            Add(
                "DuplicateModifier preserves instance values",
                duplicatePassed,
                duplicatePassed
                    ? "DuplicateModifier copied the placed modifier state and generated a fresh StableId."
                    : duplicateError
            );
        }

        ValidateExplicitParameterizedAdd(
            tempData,
            worldSettings,
            stampA
        );

        ValidateIdentityContract(
            stampA
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
                Approximately(
                    asset.DefaultSizeXZ,
                    new Vector2(
                        128f,
                        128f
                    )
                )
                &&
                Mathf.Approximately(
                    asset.DefaultHeightDelta,
                    10f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSourceInputMin,
                    TerrainStampSourceRemapUtility
                        .IdentityInputMin
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSourceInputMax,
                    TerrainStampSourceRemapUtility
                        .IdentityInputMax
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSourceGamma,
                    TerrainStampSourceRemapUtility
                        .IdentityGamma
                )
                &&
                asset.DefaultFalloffShape ==
                    TerrainStampFalloffShape.Rectangle
                &&
                asset.DefaultFalloffProfile ==
                    TerrainStampFalloffProfile.Smooth
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
                "Generic creation defaults match existing behavior",
                passed,
                "Expected 128x128, height 10, identity source response, Rectangle/Smooth 0.25 falloff, smoothing 0/1."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                asset
            );
        }
    }

    private static void ValidateSanitization()
    {
        TerrainHeightStampAsset asset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        try
        {
            asset.SetCreationDefaultsInternal(
                new Vector2(
                    -4f,
                    float.NaN
                ),
                float.NaN,
                0.9f,
                0.1f,
                -2f,
                (TerrainStampFalloffShape)999,
                (TerrainStampFalloffProfile)999,
                2f,
                -4f,
                float.NaN
            );

            bool sourcePassed =
                Mathf.Approximately(
                    asset.DefaultSourceInputMin,
                    0.9f
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSourceInputMax,
                    0.9f +
                        TerrainStampSourceRemapUtility
                            .MinimumInputRange
                )
                &&
                Mathf.Approximately(
                    asset.DefaultSourceGamma,
                    TerrainStampSourceRemapUtility
                        .MinimumGamma
                );

            Add(
                "Source-remap defaults sanitize through canonical utility",
                sourcePassed,
                $"Min={asset.DefaultSourceInputMin}, Max={asset.DefaultSourceInputMax}, Gamma={asset.DefaultSourceGamma}"
            );

            bool falloffPassed =
                asset.DefaultFalloffShape ==
                    TerrainStampFalloffShape.Rectangle
                &&
                asset.DefaultFalloffProfile ==
                    TerrainStampFalloffProfile.Smooth
                &&
                Mathf.Approximately(
                    asset.DefaultFalloff,
                    1f
                );

            Add(
                "Falloff defaults sanitize through canonical utility",
                falloffPassed,
                $"Shape={asset.DefaultFalloffShape}, Profile={asset.DefaultFalloffProfile}, Amount={asset.DefaultFalloff}"
            );

            bool smoothingPassed =
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
                "Smoothing defaults respect existing constraints",
                smoothingPassed,
                $"Radius={asset.DefaultSmoothingRadius}, Strength={asset.DefaultSmoothingStrength}"
            );

            bool placementPassed =
                Approximately(
                    asset.DefaultSizeXZ,
                    new Vector2(
                        4f,
                        128f
                    )
                )
                &&
                Mathf.Approximately(
                    asset.DefaultHeightDelta,
                    10f
                );

            Add(
                "Placement defaults sanitize to safe values",
                placementPassed,
                $"Size={asset.DefaultSizeXZ}, Height={asset.DefaultHeightDelta}"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                asset
            );
        }
    }

    private static void ValidateExplicitParameterizedAdd(
        TerrainAuthoringData tempData,
        WorldSettings worldSettings,
        TerrainHeightStampAsset stampAsset
    )
    {
        bool added =
            TerrainAuthoringModifierService
                .AddStampModifier(
                    tempData,
                    worldSettings,
                    stampAsset,
                    new Vector2(
                        400f,
                        500f
                    ),
                    new Vector2(
                        77f,
                        66f
                    ),
                    12f,
                    0.2f,
                    out string stableId,
                    out string errorMessage
                );

        TerrainStampModifier modifier =
            FindStampModifier(
                tempData,
                stableId
            );

        bool passed =
            added
            &&
            modifier != null
            &&
            Approximately(
                modifier.SizeXZ,
                new Vector2(
                    77f,
                    66f
                )
            )
            &&
            Mathf.Approximately(
                modifier.HeightDelta,
                12f
            )
            &&
            Mathf.Approximately(
                modifier.Falloff,
                0.2f
            )
            &&
            Mathf.Approximately(
                modifier.SourceInputMin,
                TerrainStampSourceRemapUtility
                    .IdentityInputMin
            )
            &&
            Mathf.Approximately(
                modifier.SourceInputMax,
                TerrainStampSourceRemapUtility
                    .IdentityInputMax
            )
            &&
            Mathf.Approximately(
                modifier.SourceGamma,
                TerrainStampSourceRemapUtility
                    .IdentityGamma
            );

        Add(
            "Explicit parameterized AddStampModifier remains compatible",
            passed,
            passed
                ? "The existing explicit add path continued using its caller-supplied values."
                : errorMessage
        );
    }

    private static void ValidateIdentityContract(
        TerrainHeightStampAsset asset
    )
    {
        bool passed =
            asset != null
            &&
            asset.LibraryId ==
                37
            &&
            asset.DisplayName ==
                "Heightmap_037";

        Add(
            "Imported library identity remains independent from defaults",
            passed,
            asset != null
                ? $"LibraryId={asset.LibraryId}, DisplayName={asset.DisplayName}"
                : "Validation stamp is unavailable."
        );
    }

    private static TerrainHeightStampAsset CreateValidationStamp(
        string path,
        int libraryId,
        Vector2 sizeXZ,
        float heightDelta,
        float sourceInputMin,
        float sourceInputMax,
        float sourceGamma,
        TerrainStampFalloffShape falloffShape,
        TerrainStampFalloffProfile falloffProfile,
        float falloff,
        float smoothingRadius,
        float smoothingStrength
    )
    {
        TerrainHeightStampAsset asset =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        asset.name =
            TerrainHeightStampIdentityUtility
                .FormatDisplayName(
                    libraryId
                );

        asset.SetLibraryIdInternal(
            libraryId
        );

        asset.SetCreationDefaultsInternal(
            sizeXZ,
            heightDelta,
            sourceInputMin,
            sourceInputMax,
            sourceGamma,
            falloffShape,
            falloffProfile,
            falloff,
            smoothingRadius,
            smoothingStrength
        );

        AssetDatabase.CreateAsset(
            asset,
            path
        );

        return asset;
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
                modifiers[index] is
                    TerrainStampModifier stamp
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

    private static void PrepareValidationFolder()
    {
        EnsureFolder(
            WorldMeshesPaths.Root,
            "Generated",
            WorldMeshesPaths.Generated
        );

        EnsureFolder(
            WorldMeshesPaths.Generated,
            "Validation",
            WorldMeshesPaths.GeneratedValidation
        );

        EnsureFolder(
            WorldMeshesPaths.GeneratedValidation,
            "StampAssetDefaults",
            ValidationRoot
        );
    }

    private static void EnsureFolder(
        string parentPath,
        string folderName,
        string fullPath
    )
    {
        if (
            AssetDatabase.IsValidFolder(
                fullPath
            )
        )
        {
            return;
        }

        string guid =
            AssetDatabase.CreateFolder(
                parentPath,
                folderName
            );

        if (
            string.IsNullOrEmpty(
                guid
            )
            ||
            !string.Equals(
                AssetDatabase.GUIDToAssetPath(
                    guid
                ),
                fullPath,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Could not create validation folder: "
                +
                fullPath
            );
        }
    }

    private static void CleanupValidationAssets()
    {
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

        if (
            lastFailedCount >
            0
        )
        {
            Debug.LogError(
                "Stamp Asset Defaults validation failed.\n\n"
                +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp Asset Defaults validation passed.\n\n"
                +
                lastSummary
            );
        }

        SceneView.RepaintAll();
    }

    private sealed class StampSnapshot
    {
        private TerrainHeightStampAsset stampAsset;
        private Vector2 positionXZ;
        private Vector2 sizeXZ;
        private float rotationDegrees;
        private bool flipX;
        private bool flipZ;
        private float sourceInputMin;
        private float sourceInputMax;
        private float sourceGamma;
        private float heightDelta;
        private float falloff;
        private TerrainStampFalloffShape falloffShape;
        private TerrainStampFalloffProfile falloffProfile;
        private float smoothingRadius;
        private float smoothingStrength;
        private bool enabled;
        private TerrainHeightBlendMode blendMode;

        public static StampSnapshot Capture(
            TerrainStampModifier stamp
        )
        {
            return
                new StampSnapshot
                {
                    stampAsset =
                        stamp.StampAsset,

                    positionXZ =
                        stamp.PositionXZ,

                    sizeXZ =
                        stamp.SizeXZ,

                    rotationDegrees =
                        stamp.RotationDegrees,

                    flipX =
                        stamp.FlipX,

                    flipZ =
                        stamp.FlipZ,

                    sourceInputMin =
                        stamp.SourceInputMin,

                    sourceInputMax =
                        stamp.SourceInputMax,

                    sourceGamma =
                        stamp.SourceGamma,

                    heightDelta =
                        stamp.HeightDelta,

                    falloff =
                        stamp.Falloff,

                    falloffShape =
                        stamp.FalloffShape,

                    falloffProfile =
                        stamp.FalloffProfile,

                    smoothingRadius =
                        stamp.SmoothingRadius,

                    smoothingStrength =
                        stamp.SmoothingStrength,

                    enabled =
                        stamp.Enabled,

                    blendMode =
                        stamp.BlendMode
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
                (
                    !compareAsset
                    ||
                    stamp.StampAsset ==
                        stampAsset
                )
                &&
                Approximately(
                    stamp.PositionXZ,
                    positionXZ
                )
                &&
                Approximately(
                    stamp.SizeXZ,
                    sizeXZ
                )
                &&
                Mathf.Approximately(
                    stamp.RotationDegrees,
                    rotationDegrees
                )
                &&
                stamp.FlipX ==
                    flipX
                &&
                stamp.FlipZ ==
                    flipZ
                &&
                Mathf.Approximately(
                    stamp.SourceInputMin,
                    sourceInputMin
                )
                &&
                Mathf.Approximately(
                    stamp.SourceInputMax,
                    sourceInputMax
                )
                &&
                Mathf.Approximately(
                    stamp.SourceGamma,
                    sourceGamma
                )
                &&
                Mathf.Approximately(
                    stamp.HeightDelta,
                    heightDelta
                )
                &&
                Mathf.Approximately(
                    stamp.Falloff,
                    falloff
                )
                &&
                stamp.FalloffShape ==
                    falloffShape
                &&
                stamp.FalloffProfile ==
                    falloffProfile
                &&
                Mathf.Approximately(
                    stamp.SmoothingRadius,
                    smoothingRadius
                )
                &&
                Mathf.Approximately(
                    stamp.SmoothingStrength,
                    smoothingStrength
                )
                &&
                stamp.Enabled ==
                    enabled
                &&
                stamp.BlendMode ==
                    blendMode;
        }
    }
}
