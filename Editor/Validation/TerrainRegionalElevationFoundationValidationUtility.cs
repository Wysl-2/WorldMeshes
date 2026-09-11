using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Regional-elevation Package 1 foundation validation.
 *
 * Uses temporary authoring data only. The real TerrainAuthoringData asset and
 * committed authoring heightfield are never modified.
 */
public static class TerrainRegionalElevationFoundationValidationUtility
{
    private const string TempFolder =
        "Assets/WorldMeshes/Editor/Validation/RegionalElevationFoundationTemp";

    private const string TempAuthoringDataPath =
        TempFolder +
        "/TerrainAuthoringData_RegionalElevationFoundation.asset";

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

    public static void ValidateRegionalElevationFoundation()
    {
        if (validationRunning)
        {
            Debug.LogWarning(
                "WorldMeshes regional elevation foundation validation " +
                "is already running."
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

        TerrainAuthoringData tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.authoringRevision =
            realAuthoringData.authoringRevision;

        AssetDatabase.CreateAsset(
            tempData,
            TempAuthoringDataPath
        );

        ValidateDefaultCompatibility(
            worldSettings,
            tempData,
            out string nullSourceSignature
        );

        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        tempData.SetRegionalElevationSourceInternal(
            source
        );

        string emptySourceSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        string emptySourceSignatureRepeat =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        tempData.ClearRegionalElevationSourceInternal();

        string restoredNullSourceSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        tempData.SetRegionalElevationSourceInternal(
            source
        );

        bool emptyOutputValid =
            source.TryValidateOutputData(
                out string emptyOutputError
            );

        AddResult(
            "Empty node source is valid",
            source.NodeCount ==
                0
            &&
            emptyOutputValid
            &&
            !string.IsNullOrEmpty(
                emptySourceSignature
            )
            &&
            emptySourceSignature ==
                emptySourceSignatureRepeat
            &&
            emptySourceSignature !=
                nullSourceSignature
            &&
            restoredNullSourceSignature ==
                nullSourceSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            source.NodeCount ==
                0
            &&
            emptyOutputValid
            &&
            !string.IsNullOrEmpty(
                emptySourceSignature
            )
            &&
            emptySourceSignature ==
                emptySourceSignatureRepeat
            &&
            emptySourceSignature !=
                nullSourceSignature
            &&
            restoredNullSourceSignature ==
                nullSourceSignature
                ? "An empty TerrainNodeElevationSource is valid, distinct " +
                    "from no source, and has deterministic source identity."
                : "Empty node-source validation/signature behavior failed. " +
                    emptyOutputError
        );

        TerrainElevationNode nodeA =
            CreateNode(
                new Vector2(
                    100f,
                    200f
                ),
                40f
            );

        TerrainElevationNode nodeB =
            CreateNode(
                new Vector2(
                    500f,
                    700f
                ),
                120f
            );

        TerrainElevationNode nodeC =
            CreateNode(
                new Vector2(
                    900f,
                    300f
                ),
                220f
            );

        source.AddNodeInternal(
            nodeA
        );

        source.AddNodeInternal(
            nodeB
        );

        source.AddNodeInternal(
            nodeC
        );

        int initialRepairs =
            source.RepairNodeStableIds();

        bool identitiesInitiallyValid =
            source.TryValidateNodeStableIds(
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
                ? "Three new elevation nodes received unique persistent " +
                    "GUID IDs."
                : $"Expected 3 generated IDs. Repairs={initialRepairs}. " +
                    initialIdentityError
        );

        AddResult(
            "Public node collection is read-only",
            ValidateReadOnlyCollection(
                source,
                out string readOnlyDetails
            )
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            readOnlyDetails
        );

        string nodeSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        AddResult(
            "Node data changes overall signature",
            !string.IsNullOrEmpty(
                nodeSignature
            )
            &&
            nodeSignature !=
                emptySourceSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            nodeSignature !=
                emptySourceSignature
                ? "Adding regional elevation nodes changed the overall " +
                    "authoring signature."
                : "Adding nodes did not change the overall authoring " +
                    "signature."
        );

        ValidatePositionSignature(
            worldSettings,
            tempData,
            nodeA,
            nodeSignature
        );

        ValidateElevationSignature(
            worldSettings,
            tempData,
            nodeB,
            nodeSignature
        );

        ValidateAddRemoveSignature(
            worldSettings,
            tempData,
            source,
            nodeSignature
        );

        ValidateOrderSignature(
            worldSettings,
            tempData,
            source,
            nodeSignature
        );

        ValidateStableIdBehavior(
            worldSettings,
            tempData,
            source,
            nodeA,
            nodeB,
            nodeC,
            nodeSignature
        );

        ValidateMalformedOutputSafety(
            worldSettings,
            tempData,
            source,
            nodeA,
            nodeSignature
        );

        ValidateModifierCompatibility(
            worldSettings,
            tempData,
            nodeSignature
        );

        string[] expectedStableIds =
        {
            nodeA.StableId,
            nodeB.StableId,
            nodeC.StableId
        };

        Vector2[] expectedPositions =
        {
            nodeA.PositionXZ,
            nodeB.PositionXZ,
            nodeC.PositionXZ
        };

        float[] expectedElevations =
        {
            nodeA.Elevation,
            nodeB.Elevation,
            nodeC.Elevation
        };

        EditorUtility.SetDirty(
            tempData
        );

        AssetDatabase.SaveAssets();

        AssetDatabase.ForceReserializeAssets(
            new[]
            {
                TempAuthoringDataPath
            }
        );

        AssetDatabase.ImportAsset(
            TempAuthoringDataPath,
            ImportAssetOptions.ForceUpdate
        );

        TerrainAuthoringData reloadedData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    TempAuthoringDataPath
                );

        ValidateSerializedState(
            worldSettings,
            reloadedData,
            expectedStableIds,
            expectedPositions,
            expectedElevations,
            nodeSignature
        );

        string committedAfter =
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
            committedAfter ==
                committedBefore
            &&
            realOverallAfter ==
                realOverallBefore
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            committedAfter ==
                committedBefore
            &&
            realOverallAfter ==
                realOverallBefore
                ? "Temporary regional-elevation validation did not alter " +
                    "the real committed or overall authoring signatures."
                : "The real authoring state changed during regional " +
                    "elevation validation."
        );
    }

    private static void ValidateDefaultCompatibility(
        WorldSettings worldSettings,
        TerrainAuthoringData tempData,
        out string nullSourceSignature
    )
    {
        bool defaultsCorrect =
            tempData.RegionalElevationSource ==
                null
            &&
            !tempData.HasRegionalElevationSource;

        nullSourceSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        AddResult(
            "Default/null regional source compatibility",
            defaultsCorrect
            &&
            !string.IsNullOrEmpty(
                nullSourceSignature
            )
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            defaultsCorrect
            &&
            !string.IsNullOrEmpty(
                nullSourceSignature
            )
                ? "A new TerrainAuthoringData keeps regional elevation null " +
                    "and retains a valid baseline overall signature."
                : "The default regional source or baseline signature is " +
                    "invalid."
        );
    }

    private static TerrainElevationNode CreateNode(
        Vector2 positionXZ,
        float elevation
    )
    {
        TerrainElevationNode node =
            new TerrainElevationNode();

        node.SetPositionXZInternal(
            positionXZ
        );

        node.SetElevationInternal(
            elevation
        );

        return
            node;
    }

    private static void ValidatePositionSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainElevationNode node,
        string baselineSignature
    )
    {
        Vector2 original =
            node.PositionXZ;

        node.SetPositionXZInternal(
            original +
            new Vector2(
                1f,
                -2f
            )
        );

        string changed =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        node.SetPositionXZInternal(
            original
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Node position contributes to signature",
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
                ? "Changing PositionXZ changed the signature and restoring " +
                    "the value restored the original signature."
                : "Node position signature behavior was not deterministic."
        );
    }

    private static void ValidateElevationSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainElevationNode node,
        string baselineSignature
    )
    {
        float original =
            node.Elevation;

        node.SetElevationInternal(
            original +
            5f
        );

        string changed =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        node.SetElevationInternal(
            original
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Node elevation contributes to signature",
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
                ? "Changing Elevation changed the signature and restoring " +
                    "the value restored the original signature."
                : "Node elevation signature behavior was not deterministic."
        );
    }

    private static void ValidateAddRemoveSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainNodeElevationSource source,
        string baselineSignature
    )
    {
        TerrainElevationNode addedNode =
            CreateNode(
                new Vector2(
                    321f,
                    654f
                ),
                88f
            );

        source.AddNodeInternal(
            addedNode
        );

        string added =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        bool removed =
            source.RemoveNodeAtInternal(
                source.NodeCount -
                1
            );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Adding/removing nodes contributes to signature",
            removed
            &&
            added !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            removed
            &&
            added !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? "Adding a node changed the signature and removing it " +
                    "restored the original signature."
                : "Node add/remove signature behavior failed."
        );
    }

    private static void ValidateOrderSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainNodeElevationSource source,
        string baselineSignature
    )
    {
        TerrainElevationNode moved =
            source.Nodes[2];

        bool removed =
            source.RemoveNodeAtInternal(
                2
            );

        source.InsertNodeInternal(
            0,
            moved
        );

        string reordered =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        bool removedFromFront =
            source.RemoveNodeAtInternal(
                0
            );

        source.InsertNodeInternal(
            2,
            moved
        );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Serialized node order contributes to signature",
            removed
            &&
            removedFromFront
            &&
            reordered !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            removed
            &&
            removedFromFront
            &&
            reordered !=
                baselineSignature
            &&
            restored ==
                baselineSignature
                ? "Changing node order changed the deterministic signature " +
                    "and restoring order restored the original signature."
                : "Ordered node signature behavior failed."
        );
    }

    private static void ValidateStableIdBehavior(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainNodeElevationSource source,
        TerrainElevationNode first,
        TerrainElevationNode second,
        TerrainElevationNode third,
        string baselineSignature
    )
    {
        FieldInfo stableIdField =
            typeof(TerrainElevationNode)
                .GetField(
                    "stableId",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        if (stableIdField == null)
        {
            AddResult(
                "Stable ID validation/repair",
                ValidationOutcome.Fail,
                "Could not access TerrainElevationNode.stableId for " +
                    "validation."
            );

            return;
        }

        string firstId =
            first.StableId;

        stableIdField.SetValue(
            second,
            firstId
        );

        bool duplicateDetected =
            !source.TryValidateNodeStableIds(
                out _
            );

        string duplicateSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        int duplicateRepairs =
            source.RepairNodeStableIds();

        bool validAfterDuplicateRepair =
            source.TryValidateNodeStableIds(
                out string duplicateRepairError
            );

        bool duplicatePolicyCorrect =
            first.StableId ==
                firstId
            &&
            second.StableId !=
                firstId;

        string afterDuplicateRepair =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Duplicate stable ID validation/repair",
            duplicateDetected
            &&
            duplicateRepairs ==
                1
            &&
            validAfterDuplicateRepair
            &&
            duplicatePolicyCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            duplicateDetected
            &&
            duplicateRepairs ==
                1
            &&
            validAfterDuplicateRepair
            &&
            duplicatePolicyCorrect
                ? "Duplicate IDs were detected; the first occurrence kept " +
                    "its ID and the later duplicate received a new one."
                : $"Duplicate repair failed. repairs={duplicateRepairs}. " +
                    duplicateRepairError
        );

        AddResult(
            "Stable IDs do not affect terrain signature",
            duplicateSignature ==
                baselineSignature
            &&
            afterDuplicateRepair ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            duplicateSignature ==
                baselineSignature
            &&
            afterDuplicateRepair ==
                baselineSignature
                ? "Changing and repairing editor identity did not change " +
                    "the terrain-output authoring signature."
                : "The authoring signature changed because only stable " +
                    "identity changed."
        );

        stableIdField.SetValue(
            third,
            ""
        );

        bool missingDetected =
            !source.TryValidateNodeStableIds(
                out _
            );

        int missingRepairs =
            source.RepairNodeStableIds();

        bool validAfterMissingRepair =
            source.TryValidateNodeStableIds(
                out _
            );

        stableIdField.SetValue(
            third,
            "not-a-guid"
        );

        bool malformedDetected =
            !source.TryValidateNodeStableIds(
                out _
            );

        int malformedRepairs =
            source.RepairNodeStableIds();

        bool validAfterMalformedRepair =
            source.TryValidateNodeStableIds(
                out string malformedRepairError
            );

        string afterIdentityRepairs =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Missing/malformed stable ID validation/repair",
            missingDetected
            &&
            missingRepairs ==
                1
            &&
            validAfterMissingRepair
            &&
            malformedDetected
            &&
            malformedRepairs ==
                1
            &&
            validAfterMalformedRepair
            &&
            afterIdentityRepairs ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            missingDetected
            &&
            missingRepairs ==
                1
            &&
            validAfterMissingRepair
            &&
            malformedDetected
            &&
            malformedRepairs ==
                1
            &&
            validAfterMalformedRepair
            &&
            afterIdentityRepairs ==
                baselineSignature
                ? "Missing and malformed IDs were detected/repaired without " +
                    "changing terrain-output signature data."
                : $"Missing/malformed identity repair failed. " +
                    malformedRepairError
        );
    }

    private static void ValidateMalformedOutputSafety(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        TerrainNodeElevationSource source,
        TerrainElevationNode node,
        string baselineSignature
    )
    {
        FieldInfo positionField =
            typeof(TerrainElevationNode)
                .GetField(
                    "positionXZ",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        FieldInfo elevationField =
            typeof(TerrainElevationNode)
                .GetField(
                    "elevation",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        FieldInfo nodesField =
            typeof(TerrainNodeElevationSource)
                .GetField(
                    "nodes",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        if (
            positionField == null
            ||
            elevationField == null
            ||
            nodesField == null
        )
        {
            AddResult(
                "Malformed output data safety",
                ValidationOutcome.Fail,
                "Could not access serialized node fields for validation."
            );

            return;
        }

        Vector2 originalPosition =
            node.PositionXZ;

        float originalElevation =
            node.Elevation;

        positionField.SetValue(
            node,
            new Vector2(
                float.NaN,
                originalPosition.y
            )
        );

        bool invalidPositionDetected =
            !source.TryValidateOutputData(
                out _
            );

        string invalidPositionSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        node.SetPositionXZInternal(
            originalPosition
        );

        elevationField.SetValue(
            node,
            float.PositiveInfinity
        );

        bool invalidElevationDetected =
            !source.TryValidateOutputData(
                out _
            );

        string invalidElevationSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        node.SetElevationInternal(
            originalElevation
        );

        List<TerrainElevationNode> rawNodes =
            nodesField.GetValue(
                source
            ) as
            List<TerrainElevationNode>;

        bool nullNodeDetected =
            false;

        bool nullIdentityDetected =
            false;

        string nullNodeSignature =
            "unexpected";

        if (rawNodes != null)
        {
            rawNodes.Add(
                null
            );

            nullNodeDetected =
                !source.TryValidateOutputData(
                    out _
                );

            nullIdentityDetected =
                !source.TryValidateNodeStableIds(
                    out _
                );

            nullNodeSignature =
                TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        data
                    );

            rawNodes.RemoveAt(
                rawNodes.Count -
                1
            );
        }

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Malformed output data safety",
            invalidPositionDetected
            &&
            string.IsNullOrEmpty(
                invalidPositionSignature
            )
            &&
            invalidElevationDetected
            &&
            string.IsNullOrEmpty(
                invalidElevationSignature
            )
            &&
            rawNodes !=
                null
            &&
            nullNodeDetected
            &&
            nullIdentityDetected
            &&
            string.IsNullOrEmpty(
                nullNodeSignature
            )
            &&
            restored ==
                baselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            invalidPositionDetected
            &&
            string.IsNullOrEmpty(
                invalidPositionSignature
            )
            &&
            invalidElevationDetected
            &&
            string.IsNullOrEmpty(
                invalidElevationSignature
            )
            &&
            rawNodes !=
                null
            &&
            nullNodeDetected
            &&
            nullIdentityDetected
            &&
            string.IsNullOrEmpty(
                nullNodeSignature
            )
            &&
            restored ==
                baselineSignature
                ? "Non-finite values and null node entries are rejected " +
                    "rather than producing valid deterministic signatures."
                : "Malformed regional-elevation data was not rejected " +
                    "correctly."
        );
    }

    private static void ValidateModifierCompatibility(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        string regionalBaselineSignature
    )
    {
        TerrainStampModifier modifier =
            new TerrainStampModifier();

        modifier.SetPositionXZInternal(
            new Vector2(
                250f,
                350f
            )
        );

        modifier.SetSizeXZInternal(
            new Vector2(
                64f,
                96f
            )
        );

        modifier.SetHeightDeltaInternal(
            12f
        );

        modifier.SetBlendModeInternal(
            TerrainHeightBlendMode.Additive
        );

        modifier.SetEnabledInternal(
            true
        );

        data.AddHeightModifierInternal(
            modifier
        );

        data.RepairModifierStableIds();

        string combined =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        bool removed =
            data.RemoveHeightModifierAtInternal(
                0
            );

        string restored =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Regional source and modifier signatures coexist",
            removed
            &&
            combined !=
                regionalBaselineSignature
            &&
            restored ==
                regionalBaselineSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            removed
            &&
            combined !=
                regionalBaselineSignature
            &&
            restored ==
                regionalBaselineSignature
                ? "The existing modifier stack still contributes after " +
                    "regional source data and removing it restores the " +
                    "regional-only signature."
                : "Regional signature integration interfered with modifier " +
                    "signature behavior."
        );
    }

    private static void ValidateSerializedState(
        WorldSettings worldSettings,
        TerrainAuthoringData data,
        string[] expectedStableIds,
        Vector2[] expectedPositions,
        float[] expectedElevations,
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

        TerrainNodeElevationSource source =
            data.RegionalElevationSource as
            TerrainNodeElevationSource;

        bool sourceCorrect =
            source !=
                null
            &&
            source.NodeCount ==
                3;

        AddResult(
            "Regional source SerializeReference save/reimport",
            sourceCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            sourceCorrect
                ? "TerrainNodeElevationSource survived TerrainAuthoringData " +
                    "save/reimport as the concrete managed-reference type."
                : "TerrainNodeElevationSource did not survive " +
                    "save/reimport correctly."
        );

        if (!sourceCorrect)
        {
            return;
        }

        bool identitiesValid =
            source.TryValidateNodeStableIds(
                out string identityError
            );

        bool outputValid =
            source.TryValidateOutputData(
                out string outputError
            );

        bool valuesPersisted =
            identitiesValid
            &&
            outputValid
            &&
            expectedStableIds !=
                null
            &&
            expectedPositions !=
                null
            &&
            expectedElevations !=
                null
            &&
            expectedStableIds.Length ==
                source.NodeCount
            &&
            expectedPositions.Length ==
                source.NodeCount
            &&
            expectedElevations.Length ==
                source.NodeCount;

        if (valuesPersisted)
        {
            for (
                int index = 0;
                index < source.NodeCount;
                index++
            )
            {
                TerrainElevationNode node =
                    source.Nodes[index];

                if (
                    node == null
                    ||
                    node.StableId !=
                        expectedStableIds[index]
                    ||
                    !Approximately(
                        node.PositionXZ.x,
                        expectedPositions[index].x
                    )
                    ||
                    !Approximately(
                        node.PositionXZ.y,
                        expectedPositions[index].y
                    )
                    ||
                    !Approximately(
                        node.Elevation,
                        expectedElevations[index]
                    )
                )
                {
                    valuesPersisted =
                        false;

                    break;
                }
            }
        }

        AddResult(
            "Node data persists through serialization",
            valuesPersisted
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            valuesPersisted
                ? "Stable IDs, PositionXZ, Elevation, and ordered node " +
                    "storage survived save/reimport."
                : "One or more node values/identities changed after " +
                    "save/reimport. " +
                    identityError +
                    " " +
                    outputError
        );

        string signatureAfterReload =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Regional signature persists through serialization",
            signatureAfterReload ==
                expectedSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            signatureAfterReload ==
                expectedSignature
                ? "OverallAuthoringSignature remained identical after " +
                    "serializing identical regional elevation data."
                : "OverallAuthoringSignature changed after save/reimport."
        );
    }

    private static bool ValidateReadOnlyCollection(
        TerrainNodeElevationSource source,
        out string details
    )
    {
        details =
            "";

        ICollection<TerrainElevationNode> collection =
            source.Nodes as
                ICollection<TerrainElevationNode>;

        if (
            collection == null
            ||
            !collection.IsReadOnly
        )
        {
            details =
                "Nodes is not exposed through a read-only collection " +
                "wrapper.";

            return false;
        }

        int countBefore =
            source.NodeCount;

        bool threw =
            false;

        try
        {
            collection.Add(
                new TerrainElevationNode()
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
            source.NodeCount ==
            countBefore;

        details =
            threw
            &&
            unchanged
                ? "Public Nodes access rejected direct Add() and left " +
                    "persistent storage unchanged."
                : "Public Nodes access allowed mutation or changed storage.";

        return
            threw
            &&
            unchanged;
    }

    private static void EnsureTempFolder()
    {
        string validationFolder =
            "Assets/WorldMeshes/Editor/Validation";

        if (!AssetDatabase.IsValidFolder(
            validationFolder
        ))
        {
            throw new InvalidOperationException(
                "Expected validation folder does not exist:\n" +
                validationFolder
            );
        }

        if (!AssetDatabase.IsValidFolder(
            TempFolder
        ))
        {
            AssetDatabase.CreateFolder(
                validationFolder,
                "RegionalElevationFoundationTemp"
            );
        }
    }

    private static void CleanupTemporaryAssets()
    {
        if (AssetDatabase.IsValidFolder(
            TempFolder
        ))
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
            "WorldMeshes Regional Elevation Foundation Validation"
        );

        builder.AppendLine(
            "=================================================="
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

            if (!string.IsNullOrEmpty(
                result.Details
            ))
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
            "--------------------------------------------------"
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
                "Regional elevation foundation validation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failed > 0)
        {
            builder.AppendLine(
                "Regional elevation foundation validation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Regional elevation foundation validation: BLOCKED"
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
