using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Read-only integration validation for generated runtime height tiles.
 *
 * Validation does not compile heightmaps and does not mutate authoring
 * state. Compile Runtime Heightmaps first, then run this validation.
 */
public static class TerrainRuntimeHeightCompositionValidationUtility
{
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
    private static bool validationScheduled;

    private static int lastPassedCount;
    private static int lastFailedCount;
    private static int lastBlockedCount;
    private static string lastSummary =
        "Not run.";

    public static bool IsRunning =>
        validationRunning;

    public static bool IsScheduled =>
        validationScheduled;

    public static int LastPassedCount =>
        lastPassedCount;

    public static int LastFailedCount =>
        lastFailedCount;

    public static int LastBlockedCount =>
        lastBlockedCount;

    public static string LastSummary =>
        lastSummary;

    public static void RequestValidation()
    {
        if (
            validationRunning
            ||
            validationScheduled
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

        if (!validationScheduled)
        {
            return;
        }

        validationScheduled =
            false;

        if (validationRunning)
        {
            return;
        }

        validationRunning =
            true;

        results.Clear();

        try
        {
            RunValidation();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }
        finally
        {
            FinishValidation();
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
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "Run runtime height composition validation while the " +
                "editor is idle in Edit Mode."
            );

            return;
        }

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        TerrainHeightmapManifest runtimeManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
            );

        if (
            worldSettings == null
            ||
            authoringData == null
            ||
            runtimeManifest == null
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "WorldSettings, TerrainAuthoringData, or the generated " +
                "runtime heightmap manifest is unavailable."
            );

            return;
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            AddResult(
                "Runtime heightmap generation state",
                ValidationOutcome.Blocked,
                "Runtime heightmaps are not current. Compile Runtime " +
                "Heightmaps before running validation."
            );

            return;
        }

        if (
            !TerrainAuthoringPreviewService.CacheReady
            ||
            TerrainAuthoringPreviewService.Status !=
                TerrainAuthoringPreviewStatus.Ready
        )
        {
            AddResult(
                "Editor composite preview",
                ValidationOutcome.Blocked,
                "The editor composite preview must be Ready so " +
                "modifier-affected generated tiles can be compared " +
                "against the live composite cache."
            );

            return;
        }

        AddResult(
            "Validation prerequisites",
            ValidationOutcome.Pass,
            "Current authoring state, generated runtime heightmaps, " +
            "and editor composite preview are available."
        );

        string currentOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            runtimeManifest.compilerVersion !=
                TerrainGenerationStateUtility
                    .RuntimeHeightCompilerVersion
            ||
            runtimeManifest.sourceAuthoringRevision !=
                authoringData.authoringRevision
            ||
            runtimeManifest.sourceAuthoringSignature !=
                currentOverallSignature
        )
        {
            AddResult(
                "Runtime manifest source identity",
                ValidationOutcome.Fail,
                "The generated runtime manifest does not describe the " +
                "current complete authoring state."
            );

            return;
        }

        if (
            TerrainAuthoringPreviewService
                .SourceOverallAuthoringSignature
            !=
            currentOverallSignature
        )
        {
            AddResult(
                "Editor preview source identity",
                ValidationOutcome.Blocked,
                "The live editor preview does not yet represent the " +
                "current OverallAuthoringSignature. Allow the preview " +
                "refresh transaction to complete before validating."
            );

            return;
        }

        AddResult(
            "Runtime manifest source identity",
            ValidationOutcome.Pass,
            "Compiler version, authoring revision, and overall " +
            "authoring signature match the current authoring state."
        );

        AddResult(
            "Editor preview source identity",
            ValidationOutcome.Pass,
            "The live editor preview represents the same current " +
            "OverallAuthoringSignature."
        );

        HashSet<Vector2Int> affectedTiles =
            new HashSet<Vector2Int>();

        IReadOnlyList<TerrainHeightModifier>
            modifiers =
                authoringData.HeightModifiers;

        for (
            int modifierIndex = 0;
            modifierIndex < modifiers.Count;
            modifierIndex++
        )
        {
            TerrainHeightModifier modifier =
                modifiers[
                    modifierIndex
                ];

            if (modifier == null)
            {
                AddResult(
                    "Modifier tile classification",
                    ValidationOutcome.Fail,
                    $"Height modifier index {modifierIndex} is null."
                );

                return;
            }

            if (!modifier.Enabled)
            {
                continue;
            }

            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    worldSettings,
                    modifier.GetAffectedWorldBounds(),
                    affectedTiles,
                    0
                );
        }

        if (affectedTiles.Count == 0)
        {
            AddResult(
                "Modifier composition coverage",
                ValidationOutcome.Blocked,
                "No enabled modifier currently affects an in-world " +
                "height tile. Add or enable a test stamp before using " +
                "this validation to prove modifier-inclusive runtime " +
                "composition."
            );

            return;
        }

        AddResult(
            "Modifier tile classification",
            ValidationOutcome.Pass,
            $"{affectedTiles.Count} in-world tile(s) are affected by " +
            "the current enabled modifier stack."
        );

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        int affectedCompared =
            0;

        int baseCompared =
            0;

        float observedMinimum =
            float.PositiveInfinity;

        float observedMaximum =
            float.NegativeInfinity;

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                Vector2Int coordinate =
                    new Vector2Int(
                        tileX,
                        tileZ
                    );

                Texture2D runtimeTile =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        TerrainRuntimeHeightAssetUtility
                            .GetHeightTilePath(
                                tileX,
                                tileZ
                            )
                    );

                if (runtimeTile == null)
                {
                    AddResult(
                        "Generated tile completeness",
                        ValidationOutcome.Fail,
                        $"Generated runtime tile ({tileX}, {tileZ}) " +
                        "is missing."
                    );

                    return;
                }

                var runtimeData =
                    runtimeTile.GetPixelData<float>(
                        0
                    );

                if (
                    runtimeData.Length !=
                        expectedSampleCount
                )
                {
                    AddResult(
                        "Generated tile sample layout",
                        ValidationOutcome.Fail,
                        $"Runtime tile ({tileX}, {tileZ}) contains " +
                        $"{runtimeData.Length} samples; expected " +
                        $"{expectedSampleCount}."
                    );

                    return;
                }

                for (
                    int index = 0;
                    index < runtimeData.Length;
                    index++
                )
                {
                    float value =
                        runtimeData[
                            index
                        ];

                    if (!IsFinite(value))
                    {
                        AddResult(
                            "Generated tile finite samples",
                            ValidationOutcome.Fail,
                            $"Runtime tile ({tileX}, {tileZ}) contains " +
                            "a non-finite height sample."
                        );

                        return;
                    }

                    observedMinimum =
                        Mathf.Min(
                            observedMinimum,
                            value
                        );

                    observedMaximum =
                        Mathf.Max(
                            observedMaximum,
                            value
                        );
                }

                if (
                    affectedTiles.Contains(
                        coordinate
                    )
                )
                {
                    if (
                        !TerrainAuthoringPreviewService
                            .TryReadCompositeSlice(
                                tileX,
                                tileZ,
                                out float[] previewData,
                                out string previewError
                            )
                    )
                    {
                        AddResult(
                            "Editor/runtime composite parity",
                            ValidationOutcome.Fail,
                            $"Could not read editor composite tile " +
                            $"({tileX}, {tileZ}).\n\n" +
                            previewError
                        );

                        return;
                    }

                    if (
                        !CompareSamples(
                            runtimeData,
                            previewData,
                            out int mismatchIndex,
                            out float runtimeValue,
                            out float expectedValue
                        )
                    )
                    {
                        AddResult(
                            "Editor/runtime composite parity",
                            ValidationOutcome.Fail,
                            $"Generated runtime tile ({tileX}, " +
                            $"{tileZ}) differs from the live editor " +
                            $"composite at sample {mismatchIndex}. " +
                            $"Runtime={runtimeValue:R}, " +
                            $"Preview={expectedValue:R}."
                        );

                        return;
                    }

                    affectedCompared++;
                }
                else
                {
                    Texture2D committedTile =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            TerrainAuthoringStateUtility
                                .GetAuthoringHeightTilePath(
                                    tileX,
                                    tileZ
                                )
                        );

                    if (committedTile == null)
                    {
                        AddResult(
                            "Committed/runtime base parity",
                            ValidationOutcome.Fail,
                            $"Committed authoring tile ({tileX}, " +
                            $"{tileZ}) is missing."
                        );

                        return;
                    }

                    var committedData =
                        committedTile.GetPixelData<float>(
                            0
                        );

                    if (
                        !CompareSamples(
                            runtimeData,
                            committedData,
                            out int mismatchIndex,
                            out float runtimeValue,
                            out float expectedValue
                        )
                    )
                    {
                        AddResult(
                            "Committed/runtime base parity",
                            ValidationOutcome.Fail,
                            $"Unaffected runtime tile ({tileX}, " +
                            $"{tileZ}) differs from committed base at " +
                            $"sample {mismatchIndex}. " +
                            $"Runtime={runtimeValue:R}, " +
                            $"Committed={expectedValue:R}."
                        );

                        return;
                    }

                    baseCompared++;
                }
            }
        }

        AddResult(
            "Editor/runtime composite parity",
            ValidationOutcome.Pass,
            $"{affectedCompared} modifier-affected runtime tile(s) " +
            "match the live editor composite."
        );

        AddResult(
            "Committed/runtime base parity",
            ValidationOutcome.Pass,
            $"{baseCompared} unaffected runtime tile(s) match the " +
            "committed authoring base."
        );

        if (
            !ApproximatelyHeight(
                observedMinimum,
                runtimeManifest.minimumTerrainHeight
            )
            ||
            !ApproximatelyHeight(
                observedMaximum,
                runtimeManifest.maximumTerrainHeight
            )
        )
        {
            AddResult(
                "Runtime manifest exact height range",
                ValidationOutcome.Fail,
                $"Observed generated range is " +
                $"{observedMinimum:R} -> {observedMaximum:R}, but the " +
                $"manifest stores " +
                $"{runtimeManifest.minimumTerrainHeight:R} -> " +
                $"{runtimeManifest.maximumTerrainHeight:R}."
            );

            return;
        }

        AddResult(
            "Runtime manifest exact height range",
            ValidationOutcome.Pass,
            $"Generated samples and manifest agree on exact range " +
            $"{observedMinimum:R} -> {observedMaximum:R}."
        );

        if (
            !ValidateGeneratedSharedBorders(
                tileGridWidth,
                tileGridHeight,
                samplesPerSide,
                out string borderError
            )
        )
        {
            AddResult(
                "Generated runtime shared borders",
                ValidationOutcome.Fail,
                borderError
            );

            return;
        }

        AddResult(
            "Generated runtime shared borders",
            ValidationOutcome.Pass,
            "All generated X and Z shared-border samples match within " +
            "float tolerance."
        );

        AddResult(
            "Runtime generation state",
            ValidationOutcome.Pass,
            "Generated runtime heightmaps are Current for the complete " +
            "modifier-inclusive authoring signature."
        );
    }

    private static bool ValidateGeneratedSharedBorders(
        int tileGridWidth,
        int tileGridHeight,
        int samplesPerSide,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                Texture2D tile =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        TerrainRuntimeHeightAssetUtility
                            .GetHeightTilePath(
                                tileX,
                                tileZ
                            )
                    );

                if (tile == null)
                {
                    errorMessage =
                        $"Generated runtime tile ({tileX}, {tileZ}) " +
                        "is missing during border validation.";

                    return false;
                }

                var data =
                    tile.GetPixelData<float>(
                        0
                    );

                if (tileX + 1 < tileGridWidth)
                {
                    Texture2D rightTile =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            TerrainRuntimeHeightAssetUtility
                                .GetHeightTilePath(
                                    tileX + 1,
                                    tileZ
                                )
                        );

                    if (rightTile == null)
                    {
                        errorMessage =
                            $"Generated runtime tile ({tileX + 1}, " +
                            $"{tileZ}) is missing during X-border " +
                            "validation.";

                        return false;
                    }

                    var rightData =
                        rightTile.GetPixelData<float>(
                            0
                        );

                    for (
                        int sampleZ = 0;
                        sampleZ < samplesPerSide;
                        sampleZ++
                    )
                    {
                        int leftIndex =
                            sampleZ *
                            samplesPerSide
                            +
                            (
                                samplesPerSide -
                                1
                            );

                        int rightIndex =
                            sampleZ *
                            samplesPerSide;

                        if (
                            !ApproximatelyHeight(
                                data[
                                    leftIndex
                                ],
                                rightData[
                                    rightIndex
                                ]
                            )
                        )
                        {
                            errorMessage =
                                $"X border mismatch between runtime " +
                                $"tiles ({tileX}, {tileZ}) and " +
                                $"({tileX + 1}, {tileZ}) at shared " +
                                $"sample Z={sampleZ}.";

                            return false;
                        }
                    }
                }

                if (tileZ + 1 < tileGridHeight)
                {
                    Texture2D upperTile =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            TerrainRuntimeHeightAssetUtility
                                .GetHeightTilePath(
                                    tileX,
                                    tileZ + 1
                                )
                        );

                    if (upperTile == null)
                    {
                        errorMessage =
                            $"Generated runtime tile ({tileX}, " +
                            $"{tileZ + 1}) is missing during Z-border " +
                            "validation.";

                        return false;
                    }

                    var upperData =
                        upperTile.GetPixelData<float>(
                            0
                        );

                    int lowerRow =
                        (
                            samplesPerSide -
                            1
                        )
                        *
                        samplesPerSide;

                    for (
                        int sampleX = 0;
                        sampleX < samplesPerSide;
                        sampleX++
                    )
                    {
                        int lowerIndex =
                            lowerRow +
                            sampleX;

                        int upperIndex =
                            sampleX;

                        if (
                            !ApproximatelyHeight(
                                data[
                                    lowerIndex
                                ],
                                upperData[
                                    upperIndex
                                ]
                            )
                        )
                        {
                            errorMessage =
                                $"Z border mismatch between runtime " +
                                $"tiles ({tileX}, {tileZ}) and " +
                                $"({tileX}, {tileZ + 1}) at shared " +
                                $"sample X={sampleX}.";

                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private static bool CompareSamples(
        Unity.Collections.NativeArray<float> actual,
        float[] expected,
        out int mismatchIndex,
        out float actualValue,
        out float expectedValue
    )
    {
        mismatchIndex =
            -1;

        actualValue =
            0f;

        expectedValue =
            0f;

        if (
            expected == null
            ||
            actual.Length !=
                expected.Length
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < actual.Length;
            index++
        )
        {
            if (
                !ApproximatelyHeight(
                    actual[
                        index
                    ],
                    expected[
                        index
                    ]
                )
            )
            {
                mismatchIndex =
                    index;

                actualValue =
                    actual[
                        index
                    ];

                expectedValue =
                    expected[
                        index
                    ];

                return false;
            }
        }

        return true;
    }

    private static bool CompareSamples(
        Unity.Collections.NativeArray<float> actual,
        Unity.Collections.NativeArray<float> expected,
        out int mismatchIndex,
        out float actualValue,
        out float expectedValue
    )
    {
        mismatchIndex =
            -1;

        actualValue =
            0f;

        expectedValue =
            0f;

        if (
            actual.Length !=
                expected.Length
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < actual.Length;
            index++
        )
        {
            if (
                !ApproximatelyHeight(
                    actual[
                        index
                    ],
                    expected[
                        index
                    ]
                )
            )
            {
                mismatchIndex =
                    index;

                actualValue =
                    actual[
                        index
                    ];

                expectedValue =
                    expected[
                        index
                    ];

                return false;
            }
        }

        return true;
    }

    private static bool ApproximatelyHeight(
        float a,
        float b
    )
    {
        if (
            !IsFinite(
                a
            )
            ||
            !IsFinite(
                b
            )
        )
        {
            return false;
        }

        float tolerance =
            Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(
                        a
                    ),
                    Mathf.Abs(
                        b
                    )
                )
                *
                0.000001f
            );

        return
            Mathf.Abs(
                a -
                b
            )
            <=
            tolerance;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
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
                Name =
                    name,
                Outcome =
                    outcome,
                Details =
                    details
            }
        );
    }

    private static void FinishValidation()
    {
        validationRunning =
            false;

        lastPassedCount =
            0;

        lastFailedCount =
            0;

        lastBlockedCount =
            0;

        foreach (
            ValidationResult result
            in results
        )
        {
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    lastPassedCount++;
                    break;

                case ValidationOutcome.Fail:
                    lastFailedCount++;
                    break;

                case ValidationOutcome.Blocked:
                    lastBlockedCount++;
                    break;
            }
        }

        lastSummary =
            $"{lastPassedCount} passed, " +
            $"{lastFailedCount} failed, " +
            $"{lastBlockedCount} blocked";

        foreach (
            ValidationResult result
            in results
        )
        {
            string message =
                $"[Runtime Height Composition] " +
                $"{result.Outcome}: {result.Name}\n" +
                result.Details;

            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    Debug.Log(
                        message
                    );
                    break;

                case ValidationOutcome.Fail:
                    Debug.LogError(
                        message
                    );
                    break;

                default:
                    Debug.LogWarning(
                        message
                    );
                    break;
            }
        }

        if (lastFailedCount == 0)
        {
            Debug.Log(
                "Runtime height composition validation complete.\n\n" +
                lastSummary
            );
        }
        else
        {
            Debug.LogError(
                "Runtime height composition validation complete.\n\n" +
                lastSummary
            );
        }
    }
}
