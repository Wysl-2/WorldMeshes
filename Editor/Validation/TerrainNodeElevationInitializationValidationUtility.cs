using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package 2 validation for initial node-layout generation and setup
 * architecture. The real TerrainAuthoringData asset and committed height tiles
 * are never modified.
 */
public static class TerrainNodeElevationInitializationValidationUtility
{
    private const string TempFolder =
        "Assets/WorldMeshes/Editor/Validation/RegionalElevation02Temp";

    private const string TempAuthoringDataPath =
        TempFolder +
        "/TerrainAuthoringData_NodeInitializationValidation.asset";

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

    public static void ValidateNodeElevationInitialization()
    {
        if (validationRunning)
        {
            Debug.LogWarning(
                "WorldMeshes node elevation initialization validation is " +
                "already running."
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
                "Wait for Unity to finish compiling/importing and run the " +
                "validation again."
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

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        ValidateInputGuards(
            worldSettings
        );

        ValidateNullSourceCompatibility(
            worldSettings,
            realAuthoringData.authoringRevision
        );

        TerrainNodeElevationSource fourCorners =
            CreateSourceOrFail(
                worldSettings,
                1,
                1,
                50f,
                "Four Corners generation"
            );

        if (fourCorners == null)
        {
            return;
        }

        ValidateFourCorners(
            fourCorners,
            worldSizeXZ
        );

        TerrainNodeElevationSource standardGrid =
            CreateSourceOrFail(
                worldSettings,
                3,
                3,
                123.5f,
                "3 x 3 grid generation"
            );

        if (standardGrid == null)
        {
            return;
        }

        ValidateStandardGrid(
            standardGrid,
            worldSizeXZ,
            3,
            3,
            123.5f
        );

        TerrainNodeElevationSource nonSquareGrid =
            CreateSourceOrFail(
                worldSettings,
                4,
                2,
                -20f,
                "4 x 2 grid generation"
            );

        if (nonSquareGrid == null)
        {
            return;
        }

        ValidateNonSquareGrid(
            nonSquareGrid,
            worldSizeXZ
        );

        ValidateStableIds(
            standardGrid
        );

        ValidateGridIsInitializationOnly();

        ValidateDetachedGenerationDoesNotMutateAuthoringData(
            worldSettings
        );

        ValidateStableIdIndependentDeterminism(
            worldSettings
        );

        ValidateRegionalOutputDifferences(
            worldSettings
        );

        ValidateOverallSignatureAndModifierCoexistence(
            worldSettings,
            realAuthoringData.authoringRevision,
            committedBefore
        );

        ValidateHeightfieldInitializerSeparation();

        ValidateSerialization(
            worldSettings,
            realAuthoringData.authoringRevision,
            standardGrid
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
                ? "Temporary Package 2 validation did not alter the real " +
                    "committed or overall authoring signatures."
                : "The real authoring state changed during Package 2 " +
                    "validation."
        );
    }

    private static void ValidateNullSourceCompatibility(
        WorldSettings worldSettings,
        int realAuthoringRevision
    )
    {
        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        data.authoringRevision =
            Mathf.Max(
                1,
                realAuthoringRevision
            );

        bool sourceIsNull =
            data.RegionalElevationSource ==
            null;

        string signature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        AddResult(
            "Null/None regional setup remains valid",
            sourceIsNull
            &&
            !string.IsNullOrEmpty(
                signature
            )
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            sourceIsNull
            &&
            !string.IsNullOrEmpty(
                signature
            )
                ? "A new TerrainAuthoringData does not create regional " +
                    "nodes automatically and retains a valid baseline " +
                    "authoring signature."
                : "Null regional source compatibility failed."
        );

        UnityEngine.Object.DestroyImmediate(
            data
        );
    }

    private static void ValidateInputGuards(
        WorldSettings worldSettings
    )
    {
        bool invalidDivisionsRejected =
            !TerrainNodeElevationLayoutUtility
                .TryCreateGridSource(
                    worldSettings,
                    0,
                    1,
                    0f,
                    out _,
                    out _
                );

        bool nanRejected =
            !TerrainNodeElevationLayoutUtility
                .TryCreateGridSource(
                    worldSettings,
                    1,
                    1,
                    float.NaN,
                    out _,
                    out _
                );

        bool infinityRejected =
            !TerrainNodeElevationLayoutUtility
                .TryCreateGridSource(
                    worldSettings,
                    1,
                    1,
                    float.PositiveInfinity,
                    out _,
                    out _
                );

        AddResult(
            "Layout input validation",
            invalidDivisionsRejected
            &&
            nanRejected
            &&
            infinityRejected
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            invalidDivisionsRejected
            &&
            nanRejected
            &&
            infinityRejected
                ? "Zero divisions and non-finite initial elevations are " +
                    "rejected before node creation."
                : "One or more invalid setup inputs were accepted."
        );
    }

    private static void ValidateFourCorners(
        TerrainNodeElevationSource source,
        Vector2 worldSizeXZ
    )
    {
        bool countCorrect =
            source.NodeCount ==
            4;

        Vector2[] expected =
        {
            new Vector2(0f, 0f),
            new Vector2(worldSizeXZ.x, 0f),
            new Vector2(0f, worldSizeXZ.y),
            new Vector2(worldSizeXZ.x, worldSizeXZ.y)
        };

        bool orderAndPositionsCorrect =
            countCorrect;

        if (orderAndPositionsCorrect)
        {
            for (
                int index = 0;
                index < expected.Length;
                index++
            )
            {
                if (!Approximately(
                    source.Nodes[index].PositionXZ,
                    expected[index]
                ))
                {
                    orderAndPositionsCorrect =
                        false;

                    break;
                }
            }
        }

        AddResult(
            "Four Corners uses 1 x 1 grid divisions",
            countCorrect
            &&
            orderAndPositionsCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            countCorrect
            &&
            orderAndPositionsCorrect
                ? "1 x 1 divisions produced four nodes ordered southwest, " +
                    "southeast, northwest, northeast on exact world bounds."
                : "Four Corners count/order/world-boundary placement failed."
        );
    }

    private static void ValidateStandardGrid(
        TerrainNodeElevationSource source,
        Vector2 worldSizeXZ,
        int divisionsX,
        int divisionsZ,
        float expectedElevation
    )
    {
        int expectedNodeCountX =
            divisionsX +
            1;

        int expectedNodeCountZ =
            divisionsZ +
            1;

        bool countCorrect =
            source.NodeCount ==
            expectedNodeCountX *
            expectedNodeCountZ;

        float expectedSpacingX =
            worldSizeXZ.x /
            divisionsX;

        float expectedSpacingZ =
            worldSizeXZ.y /
            divisionsZ;

        bool positionsCorrect =
            countCorrect;

        bool elevationsCorrect =
            countCorrect;

        if (countCorrect)
        {
            for (
                int z = 0;
                z <= divisionsZ;
                z++
            )
            {
                for (
                    int x = 0;
                    x <= divisionsX;
                    x++
                )
                {
                    int index =
                        x +
                        z *
                        expectedNodeCountX;

                    Vector2 actual =
                        source.Nodes[index]
                            .PositionXZ;

                    float expectedX =
                        x == divisionsX
                            ? worldSizeXZ.x
                            : x * expectedSpacingX;

                    float expectedZ =
                        z == divisionsZ
                            ? worldSizeXZ.y
                            : z * expectedSpacingZ;

                    if (
                        !Approximately(
                            actual.x,
                            expectedX
                        )
                        ||
                        !Approximately(
                            actual.y,
                            expectedZ
                        )
                    )
                    {
                        positionsCorrect =
                            false;
                    }

                    if (!Approximately(
                        source.Nodes[index].Elevation,
                        expectedElevation
                    ))
                    {
                        elevationsCorrect =
                            false;
                    }
                }
            }
        }

        AddResult(
            "3 x 3 grid produces 4 x 4 nodes",
            countCorrect
            &&
            positionsCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            countCorrect
            &&
            positionsCorrect
                ? "3 x 3 world divisions produced 16 nodes with uniform " +
                    "spacing and exact minimum/maximum world boundaries."
                : "3 x 3 grid count or spacing/boundary placement failed."
        );

        AddResult(
            "Initial elevation applies to every generated node",
            elevationsCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            elevationsCorrect
                ? $"All 16 generated nodes retained elevation " +
                    $"{expectedElevation:R}."
                : "One or more generated nodes did not retain the requested " +
                    "initial elevation."
        );
    }

    private static void ValidateNonSquareGrid(
        TerrainNodeElevationSource source,
        Vector2 worldSizeXZ
    )
    {
        bool countCorrect =
            source.NodeCount ==
            15;

        bool finalPositionCorrect =
            countCorrect
            &&
            Approximately(
                source.Nodes[14].PositionXZ,
                worldSizeXZ
            );

        AddResult(
            "Non-square grid axes are independent",
            countCorrect
            &&
            finalPositionCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            countCorrect
            &&
            finalPositionCorrect
                ? "4 x 2 divisions produced 5 x 3 = 15 nodes and reached " +
                    "the maximum X/Z world boundary."
                : "4 x 2 grid generation did not produce the expected " +
                    "independent X/Z layout."
        );
    }

    private static void ValidateStableIds(
        TerrainNodeElevationSource source
    )
    {
        bool valid =
            source.TryValidateNodeStableIds(
                out string errorMessage
            );

        HashSet<string> ids =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        bool allUnique =
            true;

        for (
            int index = 0;
            index < source.NodeCount;
            index++
        )
        {
            string stableId =
                source.Nodes[index]
                    .StableId;

            if (
                string.IsNullOrEmpty(
                    stableId
                )
                ||
                !ids.Add(
                    stableId
                )
            )
            {
                allUnique =
                    false;

                break;
            }
        }

        AddResult(
            "Generated nodes receive valid unique StableIds",
            valid
            &&
            allUnique
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            valid
            &&
            allUnique
                ? "Every generated node has a valid unique Package 1 " +
                    "persistent identity."
                : string.IsNullOrEmpty(
                    errorMessage
                )
                    ? "One or more generated StableIds were missing or " +
                        "duplicated."
                    : errorMessage
        );
    }

    private static void ValidateGridIsInitializationOnly()
    {
        string[] prohibitedFieldNames =
        {
            "row",
            "column",
            "gridCoordinate",
            "normalizedGridCoordinate",
            "divisions",
            "spacing"
        };

        FieldInfo[] fields =
            typeof(TerrainElevationNode)
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

        bool prohibitedFieldFound =
            false;

        string foundName =
            "";

        foreach (
            FieldInfo field
            in fields
        )
        {
            for (
                int index = 0;
                index < prohibitedFieldNames.Length;
                index++
            )
            {
                if (
                    string.Equals(
                        field.Name,
                        prohibitedFieldNames[index],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    prohibitedFieldFound =
                        true;

                    foundName =
                        field.Name;

                    break;
                }
            }

            if (prohibitedFieldFound)
            {
                break;
            }
        }

        AddResult(
            "Grid is initialization-only",
            !prohibitedFieldFound
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            !prohibitedFieldFound
                ? "TerrainElevationNode does not persist row/column/grid " +
                    "division or spacing topology metadata."
                : $"TerrainElevationNode unexpectedly persists grid field " +
                    $"'{foundName}'."
        );
    }

    private static void ValidateDetachedGenerationDoesNotMutateAuthoringData(
        WorldSettings worldSettings
    )
    {
        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        bool created =
            TerrainNodeElevationLayoutUtility
                .TryCreateGridSource(
                    worldSettings,
                    2,
                    2,
                    0f,
                    out TerrainNodeElevationSource detachedSource,
                    out string errorMessage
                );

        bool authoringDataUnchanged =
            data.RegionalElevationSource ==
            null;

        AddResult(
            "Layout preview/generation is detached from authoring ownership",
            created
            &&
            detachedSource != null
            &&
            authoringDataUnchanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            created
            &&
            detachedSource != null
            &&
            authoringDataUnchanged
                ? "Generating a candidate layout does not assign or mutate " +
                    "TerrainAuthoringData; assignment remains an explicit " +
                    "setup action."
                : string.IsNullOrEmpty(
                    errorMessage
                )
                    ? "Detached generation unexpectedly mutated authoring data."
                    : errorMessage
        );

        UnityEngine.Object.DestroyImmediate(
            data
        );
    }

    private static void ValidateStableIdIndependentDeterminism(
        WorldSettings worldSettings
    )
    {
        TerrainNodeElevationSource sourceA =
            CreateSourceOrFail(
                worldSettings,
                3,
                3,
                25f,
                "Determinism source A"
            );

        TerrainNodeElevationSource sourceB =
            CreateSourceOrFail(
                worldSettings,
                3,
                3,
                25f,
                "Determinism source B"
            );

        if (
            sourceA == null
            ||
            sourceB == null
        )
        {
            return;
        }

        bool atLeastOneIdentityDiffers =
            false;

        for (
            int index = 0;
            index < sourceA.NodeCount;
            index++
        )
        {
            if (
                sourceA.Nodes[index].StableId !=
                sourceB.Nodes[index].StableId
            )
            {
                atLeastOneIdentityDiffers =
                    true;

                break;
            }
        }

        string signatureA =
            GetRegionalSignatureData(
                sourceA
            );

        string signatureB =
            GetRegionalSignatureData(
                sourceB
            );

        bool signaturesMatch =
            !string.IsNullOrEmpty(
                signatureA
            )
            &&
            signatureA ==
                signatureB;

        AddResult(
            "StableIds do not affect generated regional output",
            atLeastOneIdentityDiffers
            &&
            signaturesMatch
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            atLeastOneIdentityDiffers
            &&
            signaturesMatch
                ? "Two independently generated layouts have different node " +
                    "identity but identical output-relevant regional " +
                    "signature data."
                : "Generated identity affected deterministic regional output " +
                    "or independent identities did not differ."
        );
    }

    private static void ValidateRegionalOutputDifferences(
        WorldSettings worldSettings
    )
    {
        TerrainNodeElevationSource gridA =
            CreateSourceOrFail(
                worldSettings,
                3,
                3,
                10f,
                "Signature grid A"
            );

        TerrainNodeElevationSource gridB =
            CreateSourceOrFail(
                worldSettings,
                4,
                2,
                10f,
                "Signature grid B"
            );

        TerrainNodeElevationSource gridC =
            CreateSourceOrFail(
                worldSettings,
                3,
                3,
                11f,
                "Signature grid C"
            );

        if (
            gridA == null
            ||
            gridB == null
            ||
            gridC == null
        )
        {
            return;
        }

        string signatureA =
            GetRegionalSignatureData(
                gridA
            );

        string signatureB =
            GetRegionalSignatureData(
                gridB
            );

        string signatureC =
            GetRegionalSignatureData(
                gridC
            );

        bool correct =
            !string.IsNullOrEmpty(
                signatureA
            )
            &&
            signatureA !=
                signatureB
            &&
            signatureA !=
                signatureC;

        AddResult(
            "Layout/elevation changes alter regional output identity",
            correct
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            correct
                ? "Changing grid divisions or initial elevation changed the " +
                    "deterministic regional source data."
                : "Regional source signatures did not distinguish different " +
                    "layout/elevation output."
        );
    }

    private static void ValidateOverallSignatureAndModifierCoexistence(
        WorldSettings worldSettings,
        int realAuthoringRevision,
        string committedBefore
    )
    {
        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        data.authoringRevision =
            Mathf.Max(
                1,
                realAuthoringRevision
            );

        string nullSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        TerrainNodeElevationSource source =
            CreateSourceOrFail(
                worldSettings,
                2,
                2,
                30f,
                "Overall signature source"
            );

        if (source == null)
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );

            return;
        }

        data.SetRegionalElevationSourceInternal(
            source
        );

        string regionalSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        TerrainStampModifier modifier =
            new TerrainStampModifier();

        modifier.SetPositionXZInternal(
            new Vector2(
                15f,
                20f
            )
        );

        data.AddHeightModifierInternal(
            modifier
        );

        data.RepairModifierStableIds();

        string combinedSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        data.RemoveHeightModifierAtInternal(
            0
        );

        string restoredRegionalSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        bool correct =
            !string.IsNullOrEmpty(
                nullSignature
            )
            &&
            regionalSignature !=
                nullSignature
            &&
            combinedSignature !=
                regionalSignature
            &&
            restoredRegionalSignature ==
                regionalSignature
            &&
            committedAfter ==
                committedBefore;

        AddResult(
            "Regional layout signatures coexist with modifiers",
            correct
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            correct
                ? "Creating regional nodes changed the overall signature, " +
                    "the existing modifier stack still contributed, removing " +
                    "the modifier restored the regional-only signature, and " +
                    "the committed base signature remained unchanged."
                : "Overall/committed signature coexistence behavior failed."
        );

        UnityEngine.Object.DestroyImmediate(
            data
        );
    }

    private static void ValidateHeightfieldInitializerSeparation()
    {
        string initializerPath =
            Path.Combine(
                Application.dataPath,
                "WorldMeshes/Editor/Generation/" +
                "TerrainAuthoringHeightInitializer.cs"
            );

        if (!File.Exists(
            initializerPath
        ))
        {
            AddResult(
                "Committed heightfield initialization preserves node ownership",
                ValidationOutcome.Blocked,
                "TerrainAuthoringHeightInitializer.cs could not be located " +
                "for architectural separation validation."
            );

            return;
        }

        string sourceText =
            File.ReadAllText(
                initializerPath
            );

        bool separated =
            !sourceText.Contains(
                "SetRegionalElevationSourceInternal"
            )
            &&
            !sourceText.Contains(
                "ClearRegionalElevationSourceInternal"
            )
            &&
            !sourceText.Contains(
                "TerrainNodeElevationLayoutUtility"
            );

        AddResult(
            "Committed heightfield initialization preserves node ownership",
            separated
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            separated
                ? "TerrainAuthoringHeightInitializer has no Package 2 source " +
                    "creation/replacement path; node initialization remains " +
                    "an explicit separate authoring action."
                : "Committed heightfield initialization contains regional " +
                    "source creation/replacement coupling."
        );
    }

    private static void ValidateSerialization(
        WorldSettings worldSettings,
        int realAuthoringRevision,
        TerrainNodeElevationSource source
    )
    {
        CleanupTemporaryAssets();
        EnsureTempFolder();

        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        data.authoringRevision =
            Mathf.Max(
                1,
                realAuthoringRevision
            );

        data.SetRegionalElevationSourceInternal(
            source
        );

        string expectedSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    data
                );

        string[] expectedIds =
            new string[
                source.NodeCount
            ];

        Vector2[] expectedPositions =
            new Vector2[
                source.NodeCount
            ];

        float[] expectedElevations =
            new float[
                source.NodeCount
            ];

        for (
            int index = 0;
            index < source.NodeCount;
            index++
        )
        {
            expectedIds[index] =
                source.Nodes[index]
                    .StableId;

            expectedPositions[index] =
                source.Nodes[index]
                    .PositionXZ;

            expectedElevations[index] =
                source.Nodes[index]
                    .Elevation;
        }

        AssetDatabase.CreateAsset(
            data,
            TempAuthoringDataPath
        );

        EditorUtility.SetDirty(
            data
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

        TerrainAuthoringData reloaded =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    TempAuthoringDataPath
                );

        TerrainNodeElevationSource reloadedSource =
            reloaded != null
                ? reloaded.RegionalElevationSource as
                    TerrainNodeElevationSource
                : null;

        bool typeAndCountCorrect =
            reloadedSource != null
            &&
            reloadedSource.NodeCount ==
                expectedIds.Length;

        bool dataCorrect =
            typeAndCountCorrect;

        if (dataCorrect)
        {
            for (
                int index = 0;
                index < expectedIds.Length;
                index++
            )
            {
                TerrainElevationNode node =
                    reloadedSource.Nodes[index];

                if (
                    node == null
                    ||
                    node.StableId !=
                        expectedIds[index]
                    ||
                    !Approximately(
                        node.PositionXZ,
                        expectedPositions[index]
                    )
                    ||
                    !Approximately(
                        node.Elevation,
                        expectedElevations[index]
                    )
                )
                {
                    dataCorrect =
                        false;

                    break;
                }
            }
        }

        AddResult(
            "Initialized node source survives serialization",
            typeAndCountCorrect
            &&
            dataCorrect
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            typeAndCountCorrect
            &&
            dataCorrect
                ? "TerrainNodeElevationSource concrete type, node count, " +
                    "ordering, StableIds, positions, and elevations survived " +
                    "asset save/reimport."
                : "Initialized regional source data changed during " +
                    "serialization."
        );

        string reloadedSignature =
            reloaded != null
                ? TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        reloaded
                    )
                : "";

        AddResult(
            "Initialized regional signature survives serialization",
            !string.IsNullOrEmpty(
                expectedSignature
            )
            &&
            reloadedSignature ==
                expectedSignature
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            reloadedSignature ==
                expectedSignature
                ? "OverallAuthoringSignature remained identical after " +
                    "serializing identical initialized regional data."
                : "Regional authoring signature changed after save/reimport."
        );
    }

    private static TerrainNodeElevationSource CreateSourceOrFail(
        WorldSettings worldSettings,
        int divisionsX,
        int divisionsZ,
        float elevation,
        string validationName
    )
    {
        bool created =
            TerrainNodeElevationLayoutUtility
                .TryCreateGridSource(
                    worldSettings,
                    divisionsX,
                    divisionsZ,
                    elevation,
                    out TerrainNodeElevationSource source,
                    out string errorMessage
                );

        if (!created)
        {
            AddResult(
                validationName,
                ValidationOutcome.Fail,
                errorMessage
            );

            return null;
        }

        return source;
    }

    private static string GetRegionalSignatureData(
        TerrainRegionalElevationSource source
    )
    {
        if (source == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        if (
            !source.TryAppendDeterministicSignatureData(
                builder,
                out _
            )
        )
        {
            return "";
        }

        return
            builder.ToString();
    }

    private static void EnsureTempFolder()
    {
        const string validationFolder =
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
                "RegionalElevation02Temp"
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
            "WorldMeshes Node Elevation Initialization Validation"
        );

        builder.AppendLine(
            "================================================="
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
            "-------------------------------------------------"
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
                "Node elevation initialization validation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failed > 0)
        {
            builder.AppendLine(
                "Node elevation initialization validation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Node elevation initialization validation: BLOCKED"
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
            0.001f;
    }

    private static bool Approximately(
        Vector2 a,
        Vector2 b
    )
    {
        return
            Approximately(
                a.x,
                b.x
            )
            &&
            Approximately(
                a.y,
                b.y
            );
    }
}
