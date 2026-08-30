using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Stage 11 data-model validation.
 *
 * Uses temporary assets only. The real TerrainAuthoringData asset and
 * committed heightfield are never modified.
 */
public static class TerrainModifierDataValidationUtility
{
    private const string TempFolder =
        "Assets/WorldMeshes/Editor/Validation/Stage11Temp";

    private const string TempAuthoringDataPath =
        TempFolder +
        "/TerrainAuthoringData_Stage11Validation.asset";

    private const string TempStampAPath =
        TempFolder +
        "/StampA.asset";

    private const string TempStampBPath =
        TempFolder +
        "/StampB.asset";

    private const string TempTexturePath =
        TempFolder +
        "/StampTexture.asset";

    private enum ValidationOutcome
    {
        Pass,
        Fail,
        Blocked
    }

    private sealed class ValidationResult
    {
        public string Name;
        public ValidationOutcome Outcome;
        public string Details;
    }

    private static readonly List<ValidationResult>
        results =
            new List<ValidationResult>();

    private static bool validationRunning;

    public static bool IsRunning
    {
        get
        {
            return
                validationRunning;
        }
    }

    public static void ValidateModifierDataFoundation()
    {
        if (validationRunning)
        {
            Debug.LogWarning(
                "WorldMeshes modifier data validation is already running."
            );

            return;
        }

        validationRunning =
            true;

        results.Clear();

        try
        {
            RunValidation();
        }
        catch (
            Exception exception
        )
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }
        finally
        {
            CleanupTemporaryAssets();

            validationRunning =
                false;

            WriteReport();
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
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "Validation cannot run in Play Mode."
            );

            return;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "Wait for Unity to finish compiling/importing and run " +
                "the validation again."
            );

            return;
        }

        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        TerrainAuthoringData realAuthoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (
            worldSettings == null
            ||
            realAuthoringData == null
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "WorldSettings or TerrainAuthoringData could not be loaded."
            );

            return;
        }

        string committedBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string realOverallBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    realAuthoringData
                );

        if (
            string.IsNullOrEmpty(
                committedBefore
            )
            ||
            string.IsNullOrEmpty(
                realOverallBefore
            )
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "Current committed/overall authoring signatures are not " +
                "available. Initialize the authoring heightfield first."
            );

            return;
        }

        AddResult(
            "Validation prerequisites",
            ValidationOutcome.Pass,
            "Current WorldSettings, TerrainAuthoringData, and authoring " +
            "signatures are available."
        );

        CleanupTemporaryAssets();

        EnsureTempFolder();

        Texture2D texture =
            CreateTemporaryTexture();

        TerrainHeightStampAsset stampA =
            CreateTemporaryStamp(
                TempStampAPath,
                texture
            );

        TerrainHeightStampAsset stampB =
            CreateTemporaryStamp(
                TempStampBPath,
                texture
            );

        TerrainAuthoringData tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.authoringRevision =
            realAuthoringData.authoringRevision;

        AssetDatabase.CreateAsset(
            tempData,
            TempAuthoringDataPath
        );

        string emptySignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        if (
            realAuthoringData.HeightModifierCount ==
            0
        )
        {
            AddResult(
                "Empty modifier stack preserves overall signature",
                emptySignature ==
                    realOverallBefore
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                emptySignature ==
                    realOverallBefore
                    ? "An empty Stage 11 modifier stack preserves the " +
                        "pre-modifier overall-signature behavior."
                    : "The empty temporary modifier stack did not match " +
                        "the current real authoring signature."
            );
        }
        else
        {
            AddResult(
                "Empty modifier stack preserves overall signature",
                ValidationOutcome.Blocked,
                "The real TerrainAuthoringData already contains modifiers, " +
                "so an empty-stack equivalence comparison is not meaningful."
            );
        }

        TerrainStampModifier modifierA =
            CreateStampModifier(
                stampA,
                new Vector2(
                    100f,
                    200f
                ),
                new Vector2(
                    64f,
                    96f
                ),
                12f,
                0.2f
            );

        TerrainStampModifier modifierB =
            CreateStampModifier(
                stampA,
                new Vector2(
                    500f,
                    700f
                ),
                new Vector2(
                    80f,
                    120f
                ),
                -7f,
                0.35f
            );

        TerrainStampModifier modifierC =
            CreateStampModifier(
                stampB,
                new Vector2(
                    900f,
                    300f
                ),
                new Vector2(
                    140f,
                    60f
                ),
                22f,
                0.5f
            );

        tempData.AddHeightModifierInternal(
            modifierA
        );

        tempData.AddHeightModifierInternal(
            modifierB
        );

        tempData.AddHeightModifierInternal(
            modifierC
        );

        int initialRepairs =
            tempData.RepairModifierStableIds();

        bool identitiesInitiallyValid =
            tempData
                .TryValidateModifierStableIds(
                    out string initialIdentityError
                );

        AddResult(
            "Stable ID generation",
            initialRepairs ==
                3
            &&
            identitiesInitiallyValid
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            initialRepairs ==
                3
            &&
            identitiesInitiallyValid
                ? "Three new modifiers received unique persistent GUID IDs."
                : $"Expected 3 generated IDs. Repairs={initialRepairs}. " +
                    initialIdentityError
        );

        AddResult(
            "Public modifier collection is read-only",
            ValidateReadOnlyCollection(
                tempData,
                out string readOnlyDetails
            )
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            readOnlyDetails
        );

        ValidateBounds(
            modifierA
        );

        string stackSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        AddResult(
            "Modifier stack changes overall signature",
            !string.IsNullOrEmpty(
                stackSignature
            )
            &&
            stackSignature !=
                emptySignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            stackSignature !=
                emptySignature
                ? "Adding ordered modifiers changed OverallAuthoringSignature."
                : "OverallAuthoringSignature did not change after modifiers " +
                    "were added."
        );

        ValidateParameterSignature(
            worldSettings,
            tempData,
            modifierB,
            stackSignature
        );

        ValidateOrderSignature(
            worldSettings,
            tempData,
            stackSignature
        );

        ValidateDependencySignature(
            worldSettings,
            tempData,
            modifierC,
            stampA,
            stampB,
            stackSignature
        );

        ValidateStableIdRepairDoesNotAffectSignature(
            worldSettings,
            tempData,
            modifierB,
            modifierC,
            stackSignature
        );

        string[] expectedStableIds =
        {
            modifierA.StableId,
            modifierB.StableId,
            modifierC.StableId
        };

        string committedAfterModifierChanges =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        AddResult(
            "Committed signature remains modifier-independent",
            committedAfterModifierChanges ==
                committedBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            committedAfterModifierChanges ==
                committedBefore
                ? "CommittedHeightfieldSignature remained unchanged while " +
                    "temporary modifier state changed."
                : "CommittedHeightfieldSignature changed because of " +
                    "modifier state."
        );

        EditorUtility.SetDirty(
            tempData
        );

        EditorUtility.SetDirty(
            stampA
        );

        EditorUtility.SetDirty(
            stampB
        );

        AssetDatabase.SaveAssets();

        string[] paths =
        {
            TempAuthoringDataPath,
            TempStampAPath,
            TempStampBPath,
            TempTexturePath
        };

        AssetDatabase.ForceReserializeAssets(
            paths
        );

        AssetDatabase.ImportAsset(
            TempAuthoringDataPath,
            ImportAssetOptions.ForceUpdate
        );

        AssetDatabase.ImportAsset(
            TempStampAPath,
            ImportAssetOptions.ForceUpdate
        );

        AssetDatabase.ImportAsset(
            TempStampBPath,
            ImportAssetOptions.ForceUpdate
        );

        TerrainAuthoringData reloadedData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    TempAuthoringDataPath
                );

        TerrainHeightStampAsset reloadedStampA =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightStampAsset>(
                    TempStampAPath
                );

        ValidateSerializedState(
            worldSettings,
            reloadedData,
            reloadedStampA,
            expectedStableIds,
            stackSignature
        );

        string committedAfterSerialization =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string realOverallAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    realAuthoringData
                );

        AddResult(
            "Real authoring state remains unchanged",
            committedAfterSerialization ==
                committedBefore
            &&
            realOverallAfter ==
                realOverallBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            committedAfterSerialization ==
                committedBefore
            &&
            realOverallAfter ==
                realOverallBefore
                ? "Temporary Stage 11 validation did not alter the real " +
                    "committed or overall authoring signatures."
                : "The real authoring state changed during Stage 11 " +
                    "validation."
        );
    }

    private static TerrainStampModifier CreateStampModifier(
        TerrainHeightStampAsset stamp,
        Vector2 position,
        Vector2 size,
        float heightDelta,
        float falloff
    )
    {
        TerrainStampModifier modifier =
            new TerrainStampModifier();

        modifier.SetStampAssetInternal(
            stamp
        );

        modifier.SetPositionXZInternal(
            position
        );

        modifier.SetSizeXZInternal(
            size
        );

        modifier.SetHeightDeltaInternal(
            heightDelta
        );

        modifier.SetFalloffInternal(
            falloff
        );

        modifier.SetBlendModeInternal(
            TerrainHeightBlendMode.Additive
        );

        modifier.SetEnabledInternal(
            true
        );

        return
            modifier;
    }

    private static void ValidateBounds(
        TerrainStampModifier modifier
    )
    {
        Bounds bounds =
            modifier.GetAffectedWorldBounds();

        bool correct =
            Approximately(
                bounds.center.x,
                modifier.PositionXZ.x
            )
            &&
            Approximately(
                bounds.center.z,
                modifier.PositionXZ.y
            )
            &&
            Approximately(
                bounds.size.x,
                modifier.SizeXZ.x
            )
            &&
            Approximately(
                bounds.size.z,
                modifier.SizeXZ.y
            );

        AddResult(
            "Deterministic affected world bounds",
            correct
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            correct
                ? "TerrainStampModifier bounds match serialized XZ " +
                    "position and size."
                : $"Expected center/size from modifier parameters; actual " +
                    $"center={bounds.center}, size={bounds.size}."
        );
    }

    private static void ValidateParameterSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainStampModifier modifier,
        string baselineSignature
    )
    {
        float original =
            modifier.HeightDelta;

        modifier.SetHeightDeltaInternal(
            original +
            1f
        );

        string changed =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        modifier.SetHeightDeltaInternal(
            original
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Modifier parameters contribute to signature",
            changed !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            changed !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? "Changing heightDelta changed the signature and restoring " +
                    "the value restored the original signature."
                : "Modifier parameter signature behavior was not " +
                    "deterministic."
        );
    }

    private static void ValidateOrderSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        string baselineSignature
    )
    {
        bool moved =
            data.MoveHeightModifierInternal(
                2,
                0
            );

        string reordered =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        bool restoredMove =
            data.MoveHeightModifierInternal(
                0,
                2
            );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Modifier order contributes to signature",
            moved
            &&
            restoredMove
            &&
            reordered !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            moved
            &&
            restoredMove
            &&
            reordered !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? "Reordering the stack changed the signature and restoring " +
                    "the order restored the original signature."
                : "Ordered modifier signature behavior failed."
        );
    }

    private static void ValidateDependencySignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainStampModifier modifier,
        TerrainHeightStampAsset alternateAsset,
        TerrainHeightStampAsset originalAsset,
        string baselineSignature
    )
    {
        modifier.SetStampAssetInternal(
            alternateAsset
        );

        string changed =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        modifier.SetStampAssetInternal(
            originalAsset
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Stamp asset dependency contributes to signature",
            changed !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            changed !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? "Changing the referenced TerrainHeightStampAsset changed " +
                    "the overall signature deterministically."
                : "Stamp asset dependency signature behavior failed."
        );
    }

    private static void ValidateStableIdRepairDoesNotAffectSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainHeightModifier source,
        TerrainHeightModifier duplicate,
        string baselineSignature
    )
    {
        FieldInfo stableIdField =
            typeof(TerrainHeightModifier)
                .GetField(
                    "stableId",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        if (stableIdField == null)
        {
            AddResult(
                "Duplicate stable ID detection/repair",
                ValidationOutcome.Fail,
                "Could not access stableId for validation."
            );

            return;
        }

        string sourceId =
            source.StableId;

        stableIdField.SetValue(
            duplicate,
            sourceId
        );

        bool invalidDetected =
            !data.TryValidateModifierStableIds(
                out _
            );

        int repaired =
            data.RepairModifierStableIds();

        bool validAfterRepair =
            data.TryValidateModifierStableIds(
                out string repairError
            );

        bool sourceKeptId =
            source.StableId ==
            sourceId;

        bool duplicateChanged =
            duplicate.StableId !=
            sourceId;

        string signatureAfterRepair =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        bool signatureStable =
            signatureAfterRepair ==
            baselineSignature;

        AddResult(
            "Duplicate stable ID detection/repair",
            invalidDetected
            &&
            repaired ==
                1
            &&
            validAfterRepair
            &&
            sourceKeptId
            &&
            duplicateChanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            invalidDetected
            &&
            repaired ==
                1
            &&
            validAfterRepair
            &&
            sourceKeptId
            &&
            duplicateChanged
                ? "Duplicate IDs were detected; the first occurrence kept " +
                    "its ID and the later duplicate received a new one."
                : $"Duplicate repair failed. repairs={repaired}. " +
                    repairError
        );

        AddResult(
            "Stable IDs do not affect terrain signature",
            signatureStable
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            signatureStable
                ? "Repairing editor identity did not change " +
                    "OverallAuthoringSignature because stable IDs are not " +
                    "terrain output."
                : "OverallAuthoringSignature changed because only stable ID " +
                    "identity changed."
        );
    }

    private static void ValidateSerializedState(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainHeightStampAsset stampA,
        string[] expectedStableIds,
        string expectedSignature
    )
    {
        if (data == null)
        {
            AddResult(
                "SerializeReference save/reimport",
                ValidationOutcome.Fail,
                "Temporary TerrainAuthoringData could not be reloaded."
            );

            return;
        }

        bool countCorrect =
            data.HeightModifierCount ==
            3;

        bool typeCorrect =
            countCorrect
            &&
            data.HeightModifiers[0] is TerrainStampModifier
            &&
            data.HeightModifiers[1] is TerrainStampModifier
            &&
            data.HeightModifiers[2] is TerrainStampModifier;

        AddResult(
            "SerializeReference save/reimport",
            countCorrect
            &&
            typeCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            countCorrect
            &&
            typeCorrect
                ? "Three polymorphic TerrainStampModifier managed " +
                    "references survived asset save/reimport."
                : $"Expected 3 TerrainStampModifier entries; actual " +
                    $"{data.HeightModifierCount}."
        );

        bool idsValid =
            data.TryValidateModifierStableIds(
                out string identityError
            );

        bool exactIdsPersisted =
            idsValid
            &&
            expectedStableIds != null
            &&
            expectedStableIds.Length ==
                data.HeightModifierCount;

        if (exactIdsPersisted)
        {
            for (
                int index = 0;
                index < expectedStableIds.Length;
                index++
            )
            {
                TerrainHeightModifier modifier =
                    data.HeightModifiers[
                        index
                    ];

                if (
                    modifier == null
                    ||
                    modifier.StableId !=
                        expectedStableIds[
                            index
                        ]
                )
                {
                    exactIdsPersisted =
                        false;

                    break;
                }
            }
        }

        AddResult(
            "Stable IDs persist through serialization",
            exactIdsPersisted
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            exactIdsPersisted
                ? "Reloaded modifiers retained the exact same valid and " +
                    "unique stable IDs."
                : string.IsNullOrEmpty(
                    identityError
                )
                    ? "One or more stable IDs changed after save/reimport."
                    : identityError
        );

        if (typeCorrect)
        {
            TerrainStampModifier a =
                (TerrainStampModifier)data.HeightModifiers[0];

            TerrainStampModifier b =
                (TerrainStampModifier)data.HeightModifiers[1];

            TerrainStampModifier c =
                (TerrainStampModifier)data.HeightModifiers[2];

            bool orderCorrect =
                Approximately(
                    a.PositionXZ.x,
                    100f
                )
                &&
                Approximately(
                    b.PositionXZ.x,
                    500f
                )
                &&
                Approximately(
                    c.PositionXZ.x,
                    900f
                );

            AddResult(
                "Modifier ordering persists",
                orderCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                orderCorrect
                    ? "Managed-reference list order A -> B -> C persisted."
                    : "Modifier order changed after save/reimport."
            );

            bool valuesCorrect =
                Approximately(
                    a.HeightDelta,
                    12f
                )
                &&
                Approximately(
                    b.HeightDelta,
                    -7f
                )
                &&
                Approximately(
                    c.HeightDelta,
                    22f
                )
                &&
                a.BlendMode ==
                    TerrainHeightBlendMode.Additive
                &&
                a.Enabled;

            AddResult(
                "Modifier parameters persist",
                valuesCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                valuesCorrect
                    ? "Stamp parameters, Enabled, and Additive blend state " +
                        "survived serialization."
                    : "One or more modifier parameters changed after " +
                        "save/reimport."
            );

            bool referenceCorrect =
                a.StampAsset ==
                    stampA
                &&
                stampA != null
                &&
                stampA.HeightTexture !=
                    null;

            AddResult(
                "Stamp asset references persist",
                referenceCorrect
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                referenceCorrect
                    ? "TerrainStampModifier -> TerrainHeightStampAsset -> " +
                        "Texture2D references survived serialization."
                    : "Stamp asset/texture reference did not survive " +
                        "save/reimport."
            );
        }

        string signatureAfterReload =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Modifier signature persists through serialization",
            signatureAfterReload ==
                expectedSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            signatureAfterReload ==
                expectedSignature
                ? "OverallAuthoringSignature remained identical after " +
                    "save/reimport."
                : "OverallAuthoringSignature changed after serializing " +
                    "identical modifier data."
        );
    }

    private static bool ValidateReadOnlyCollection(
        TerrainAuthoringData data,
        out string details
    )
    {
        details =
            "";

        ICollection<TerrainHeightModifier> collection =
            data.HeightModifiers as
                ICollection<TerrainHeightModifier>;

        if (
            collection == null
            ||
            !collection.IsReadOnly
        )
        {
            details =
                "HeightModifiers is not exposed through a read-only " +
                "collection wrapper.";

            return false;
        }

        int countBefore =
            data.HeightModifierCount;

        bool threw =
            false;

        try
        {
            collection.Add(
                new TerrainStampModifier()
            );
        }
        catch (
            NotSupportedException
        )
        {
            threw =
                true;
        }

        bool unchanged =
            data.HeightModifierCount ==
            countBefore;

        details =
            threw
            &&
            unchanged
                ? "Public HeightModifiers access rejected direct Add() " +
                    "and left the stack unchanged."
                : "Public HeightModifiers access allowed mutation or changed " +
                    "the stack.";

        return
            threw
            &&
            unchanged;
    }

    private static Texture2D CreateTemporaryTexture()
    {
        Texture2D texture =
            new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                true
            );

        texture.name =
            "Stage11ValidationStampTexture";

        texture.SetPixels(
            new[]
            {
                new Color(0f, 0f, 0f, 1f),
                new Color(0.5f, 0f, 0f, 1f),
                new Color(0.75f, 0f, 0f, 1f),
                new Color(1f, 0f, 0f, 1f)
            }
        );

        texture.Apply();

        AssetDatabase.CreateAsset(
            texture,
            TempTexturePath
        );

        return
            texture;
    }

    private static TerrainHeightStampAsset CreateTemporaryStamp(
        string path,
        Texture2D texture
    )
    {
        TerrainHeightStampAsset stamp =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        stamp.SetHeightTextureInternal(
            texture
        );

        AssetDatabase.CreateAsset(
            stamp,
            path
        );

        EditorUtility.SetDirty(
            stamp
        );

        return
            stamp;
    }

    private static void EnsureTempFolder()
    {
        string validationFolder =
            "Assets/WorldMeshes/Editor/Validation";

        if (
            !AssetDatabase.IsValidFolder(
                validationFolder
            )
        )
        {
            throw new InvalidOperationException(
                "Expected validation folder does not exist:\n" +
                validationFolder
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                TempFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                validationFolder,
                "Stage11Temp"
            );
        }
    }

    private static void CleanupTemporaryAssets()
    {
        if (
            AssetDatabase.IsValidFolder(
                TempFolder
            )
        )
        {
            AssetDatabase.DeleteAsset(
                TempFolder
            );

            AssetDatabase.Refresh();
        }
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Outcome = outcome,
                Details = details ?? ""
            }
        );
    }

    private static void WriteReport()
    {
        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Modifier Data Foundation Validation"
        );

        builder.AppendLine(
            "=============================================="
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
                    passed++;
                    break;

                case ValidationOutcome.Fail:
                    failed++;
                    break;

                default:
                    blocked++;
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
                string[] lines =
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
                    in lines
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
            $"{passed} passed"
        );

        builder.AppendLine(
            $"{failed} failed"
        );

        builder.AppendLine(
            $"{blocked} blocked"
        );

        builder.AppendLine();

        if (
            failed == 0
            &&
            blocked == 0
        )
        {
            builder.AppendLine(
                "Stage 11 modifier data foundation validation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failed > 0)
        {
            builder.AppendLine(
                "Stage 11 modifier data foundation validation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Stage 11 modifier data foundation validation: BLOCKED"
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }
    }

    private static bool Approximately(
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
            0.0001f;
    }
}
