using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Package 4 validation for GPU regional elevation composition and its shared
 * preview/runtime scheduling contract.
 *
 * GPU fixtures and authoring data are transient. The real TerrainAuthoringData
 * asset, committed authoring tiles, runtime height assets, and bake state are
 * never mutated by this validator.
 */
public static class TerrainRegionalElevationCompositionValidationUtility
{
    private const int SamplesPerSide =
        5;

    private const float SampleSpacing =
        25f;

    private const float TileWorldSize =
        100f;

    private const float GpuTolerance =
        0.02f;

    private static readonly Vector2 TestWorldSizeXZ =
        new Vector2(
            TileWorldSize,
            TileWorldSize
        );

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

    private struct NodeSpec
    {
        public Vector2 Position;
        public float Elevation;

        public NodeSpec(
            Vector2 position,
            float elevation
        )
        {
            Position =
                position;

            Elevation =
                elevation;
        }
    }

    private static readonly List<ValidationResult>
        results =
            new List<ValidationResult>();

    private static bool validationRunning;
    private static bool validationScheduled;

    private static WorldSettings realWorldSettings;
    private static TerrainAuthoringData realAuthoringData;

    private static int realRevisionBefore;
    private static string committedSignatureBefore = "";
    private static string overallSignatureBefore = "";
    private static TerrainRegionalElevationSource
        realRegionalSourceBefore;

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

    public static void ValidateRegionalElevationComposition()
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
            if (
                !TryValidatePrerequisites(
                    out string prerequisiteError
                )
            )
            {
                AddResult(
                    "Validation prerequisites",
                    ValidationOutcome.Blocked,
                    prerequisiteError
                );

                return;
            }

            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Pass,
                "WorldSettings and authoring signatures are available, and " +
                "the current graphics device supports compute shaders, " +
                "RFloat texture arrays, and synchronous validation readback."
            );

            CaptureRealBaseline();

            ValidateSignatureVersion();
            ValidateLegacyNullRegionalComposition();
            ValidateOneNodeAbsoluteComposition();
            ValidateTwoNodeCpuGpuParity();
            ValidateIrregularCpuGpuParity();
            ValidateCoincidentCpuGpuParity();
            ValidateStableIdIndependence();
            ValidateCompositionOrderWithAdditiveStamp();
            ValidateTargetBlendModesOnRegionalSurface();
            ValidateRegionalRangeAccounting();
            ValidateInvalidRegionalStates();
            ValidateFlatOnlySourceModeContract();
            ValidateWholeWorldTileCollection();
            ValidateRuntimeCompositionCoverage();
            ValidateRuntimeSharedCompositorOutput();
            ValidateRealStateUnchanged();
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
            validationRunning =
                false;

            WriteReport();
        }
    }

    private static bool TryValidatePrerequisites(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Validation cannot run in or while entering Play Mode.";

            return false;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            errorMessage =
                "Wait for Unity to finish compiling/importing and run the " +
                "validation again.";

            return false;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support compute shaders.";

            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support 2D texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The current graphics device does not support RFloat " +
                "RenderTextures.";

            return false;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            errorMessage =
                "Async GPU readback is required for Package 4 validation.";

            return false;
        }

        realWorldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        realAuthoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (
            realWorldSettings == null
            ||
            realAuthoringData == null
        )
        {
            errorMessage =
                "WorldSettings or TerrainAuthoringData could not be loaded.";

            return false;
        }

        committedSignatureBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    realWorldSettings
                );

        overallSignatureBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    realWorldSettings,
                    realAuthoringData
                );

        if (
            string.IsNullOrEmpty(
                committedSignatureBefore
            )
            ||
            string.IsNullOrEmpty(
                overallSignatureBefore
            )
        )
        {
            errorMessage =
                "Current committed/overall authoring signatures are not " +
                "available. Initialize the authoring heightfield first.";

            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore =
            realAuthoringData.authoringRevision;

        realRegionalSourceBefore =
            realAuthoringData.RegionalElevationSource;
    }

    private static void ValidateSignatureVersion()
    {
        TerrainNodeElevationSource source =
            CreateNodeSource(
                new NodeSpec(
                    Vector2.zero,
                    10f
                )
            );

        StringBuilder builder =
            new StringBuilder();

        bool appended =
            source.TryAppendDeterministicSignatureData(
                builder,
                out string errorMessage
            );

        string signatureData =
            builder.ToString();

        bool passed =
            appended
            &&
            signatureData.Contains(
                "TerrainNodeElevationSourceV2"
            );

        AddResult(
            "Node regional output semantics are versioned for Package 4",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "TerrainNodeElevationSource now identifies its operational " +
                  "terrain-output semantics as V2, invalidating pre-Package-4 " +
                  "derived node-regional output without globally versioning " +
                  "no-source worlds."
                : "Expected TerrainNodeElevationSourceV2 in deterministic " +
                  "regional signature data. " + errorMessage
        );
    }

    private static void ValidateLegacyNullRegionalComposition()
    {
        TerrainAuthoringData data =
            CreateAuthoringData(
                null,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool composed =
                TryComposeAndRead(
                    data,
                    37f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out int regionalDispatches,
                    out string errorMessage
                );

            bool passed =
                composed
                &&
                regionalDispatches == 0
                &&
                AllApproximately(
                    values,
                    37f,
                    GpuTolerance
                );

            AddResult(
                "Null regional source preserves legacy base composition",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "With no regional source and no modifiers, the shared " +
                      "compositor left the committed 37m base unchanged and " +
                      "executed no regional dispatch."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateOneNodeAbsoluteComposition()
    {
        TerrainNodeElevationSource source =
            CreateNodeSource(
                new NodeSpec(
                    new Vector2(
                        50f,
                        50f
                    ),
                    125f
                )
            );

        TerrainAuthoringData data =
            CreateAuthoringData(
                source,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool composed =
                TryComposeAndRead(
                    data,
                    500f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out int regionalDispatches,
                    out string errorMessage
                );

            bool passed =
                composed
                &&
                regionalDispatches == 1
                &&
                AllApproximately(
                    values,
                    125f,
                    GpuTolerance
                );

            AddResult(
                "One-node GPU composition is global and absolute",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "A 125m regional node replaced a seeded 500m committed " +
                      "base across the complete tile, proving constant-field " +
                      "and absolute rather than additive semantics."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateTwoNodeCpuGpuParity()
    {
        TerrainNodeElevationSource source =
            CreateNodeSource(
                new NodeSpec(
                    new Vector2(
                        0f,
                        0f
                    ),
                    0f
                ),
                new NodeSpec(
                    new Vector2(
                        100f,
                        0f
                    ),
                    100f
                )
            );

        TerrainAuthoringData data =
            CreateAuthoringData(
                source,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool composed =
                TryComposeAndRead(
                    data,
                    500f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out _,
                    out string errorMessage
                );

            bool parity =
                composed;

            StringBuilder mismatches =
                new StringBuilder();

            if (composed)
            {
                for (
                    int sampleX = 0;
                    sampleX < SamplesPerSide;
                    sampleX++
                )
                {
                    Vector2 sample =
                        new Vector2(
                            sampleX *
                                SampleSpacing,
                            0f
                        );

                    bool cpuOk =
                        TerrainNodeElevationEvaluator
                            .TryEvaluateHeight(
                                source,
                                sample,
                                out float cpuHeight,
                                out string cpuError
                            );

                    float gpuHeight =
                        values[
                            ToIndex(
                                sampleX,
                                0
                            )
                        ];

                    if (
                        !cpuOk
                        ||
                        !Approximately(
                            gpuHeight,
                            cpuHeight,
                            GpuTolerance
                        )
                    )
                    {
                        parity =
                            false;

                        mismatches.Append(
                            $"x={sample.x:R}: CPU={cpuHeight:R}, " +
                            $"GPU={gpuHeight:R}, error={cpuError}; "
                        );
                    }
                }
            }

            AddResult(
                "Two-node GPU field matches Package 3 CPU IDW",
                parity
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                parity
                    ? "Samples at 0/25/50/75/100m matched the CPU " +
                      "power-2 IDW reference within GPU tolerance."
                    : string.IsNullOrEmpty(errorMessage)
                        ? mismatches.ToString()
                        : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateIrregularCpuGpuParity()
    {
        TerrainNodeElevationSource source =
            CreateNodeSource(
                new NodeSpec(
                    new Vector2(
                        5f,
                        12f
                    ),
                    20f
                ),
                new NodeSpec(
                    new Vector2(
                        90f,
                        8f
                    ),
                    140f
                ),
                new NodeSpec(
                    new Vector2(
                        23f,
                        82f
                    ),
                    75f
                ),
                new NodeSpec(
                    new Vector2(
                        78f,
                        69f
                    ),
                    230f
                )
            );

        TerrainAuthoringData data =
            CreateAuthoringData(
                source,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool composed =
                TryComposeAndRead(
                    data,
                    -250f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out _,
                    out string errorMessage
                );

            bool parity =
                composed;

            float minimum =
                20f;

            float maximum =
                230f;

            if (composed)
            {
                for (
                    int z = 0;
                    z < SamplesPerSide;
                    z++
                )
                {
                    for (
                        int x = 0;
                        x < SamplesPerSide;
                        x++
                    )
                    {
                        Vector2 sample =
                            new Vector2(
                                x * SampleSpacing,
                                z * SampleSpacing
                            );

                        if (
                            !TerrainNodeElevationEvaluator
                                .TryEvaluateHeight(
                                    source,
                                    sample,
                                    out float cpuHeight,
                                    out _
                                )
                        )
                        {
                            parity =
                                false;

                            continue;
                        }

                        float gpuHeight =
                            values[
                                ToIndex(
                                    x,
                                    z
                                )
                            ];

                        if (
                            !Approximately(
                                gpuHeight,
                                cpuHeight,
                                GpuTolerance
                            )
                            ||
                            gpuHeight <
                                minimum -
                                GpuTolerance
                            ||
                            gpuHeight >
                                maximum +
                                GpuTolerance
                        )
                        {
                            parity =
                                false;
                        }
                    }
                }
            }

            AddResult(
                "Irregular node GPU field is finite, in range, and CPU-equivalent",
                parity
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                parity
                    ? "All 25 tile samples matched Package 3 CPU evaluation " +
                      "within tolerance and stayed inside the positive-weight " +
                      "IDW source elevation range."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateCoincidentCpuGpuParity()
    {
        TerrainNodeElevationSource source =
            CreateNodeSource(
                new NodeSpec(
                    new Vector2(
                        50f,
                        50f
                    ),
                    50f
                ),
                new NodeSpec(
                    new Vector2(
                        50f,
                        50f
                    ),
                    150f
                ),
                new NodeSpec(
                    new Vector2(
                        50f,
                        50f
                    ),
                    100f
                )
            );

        TerrainAuthoringData data =
            CreateAuthoringData(
                source,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool composed =
                TryComposeAndRead(
                    data,
                    0f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out _,
                    out string errorMessage
                );

            bool cpuOk =
                TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        new Vector2(
                            50f,
                            50f
                        ),
                        out float cpuHeight,
                        out string cpuError
                    );

            float gpuHeight =
                composed
                    ? values[
                        ToIndex(
                            2,
                            2
                        )
                    ]
                    : 0f;

            bool passed =
                composed
                &&
                cpuOk
                &&
                Approximately(
                    cpuHeight,
                    100f,
                    GpuTolerance
                )
                &&
                Approximately(
                    gpuHeight,
                    cpuHeight,
                    GpuTolerance
                );

            AddResult(
                "Coincident exact matches use arithmetic-mean GPU semantics",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Three coincident 50/150/100m nodes evaluated to 100m " +
                      "on CPU and GPU using the Package 3 exact-match rule."
                    : string.IsNullOrEmpty(errorMessage)
                        ? cpuError
                        : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateStableIdIndependence()
    {
        NodeSpec[] specs =
        {
            new NodeSpec(
                new Vector2(
                    0f,
                    0f
                ),
                20f
            ),
            new NodeSpec(
                new Vector2(
                    100f,
                    100f
                ),
                180f
            )
        };

        TerrainNodeElevationSource sourceA =
            CreateNodeSource(
                specs
            );

        TerrainNodeElevationSource sourceB =
            CreateNodeSource(
                specs
            );

        TerrainAuthoringData dataA =
            CreateAuthoringData(
                sourceA,
                TerrainHeightSourceMode.Flat
            );

        TerrainAuthoringData dataB =
            CreateAuthoringData(
                sourceB,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool identitiesDiffer =
                sourceA.Nodes[0].StableId !=
                sourceB.Nodes[0].StableId;

            bool composedA =
                TryComposeAndRead(
                    dataA,
                    0f,
                    false,
                    out float[] valuesA,
                    out _,
                    out _,
                    out _,
                    out string errorA
                );

            bool composedB =
                TryComposeAndRead(
                    dataB,
                    0f,
                    false,
                    out float[] valuesB,
                    out _,
                    out _,
                    out _,
                    out string errorB
                );

            bool passed =
                identitiesDiffer
                &&
                composedA
                &&
                composedB
                &&
                ArraysApproximately(
                    valuesA,
                    valuesB,
                    GpuTolerance
                );

            AddResult(
                "GPU node data is independent of StableId",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Independently generated node identities differed while " +
                      "geometrically identical GPU output remained equivalent."
                    : errorA + " " + errorB
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                dataA
            );

            UnityEngine.Object.DestroyImmediate(
                dataB
            );
        }
    }

    private static void ValidateCompositionOrderWithAdditiveStamp()
    {
        Texture2D stampTexture =
            null;

        TerrainHeightStampAsset stampAsset =
            null;

        TerrainAuthoringData data =
            null;

        try
        {
            stampTexture =
                CreateUniformStampTexture(
                    1f
                );

            stampAsset =
                ScriptableObject
                    .CreateInstance<TerrainHeightStampAsset>();

            stampAsset.SetHeightTextureInternal(
                stampTexture
            );

            TerrainStampModifier stamp =
                CreateFullTileStamp(
                    stampAsset,
                    TerrainHeightBlendMode.Additive
                );

            stamp.SetHeightDeltaInternal(
                30f
            );

            TerrainNodeElevationSource source =
                CreateNodeSource(
                    new NodeSpec(
                        new Vector2(
                            50f,
                            50f
                        ),
                        100f
                    )
                );

            data =
                CreateAuthoringData(
                    source,
                    TerrainHeightSourceMode.Flat
                );

            data.AddHeightModifierInternal(
                stamp
            );

            bool composed =
                TryComposeAndRead(
                    data,
                    500f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out _,
                    out string errorMessage
                );

            float center =
                composed
                    ? values[
                        ToIndex(
                            2,
                            2
                        )
                    ]
                    : 0f;

            bool passed =
                composed
                &&
                Approximately(
                    center,
                    130f,
                    GpuTolerance
                );

            AddResult(
                "Regional elevation composes before additive stamps",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "A 100m absolute regional surface followed by a +30m " +
                      "stamp produced approximately 130m from a 500m seeded " +
                      "committed base."
                    : errorMessage
            );
        }
        finally
        {
            if (data != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    data
                );
            }

            if (stampAsset != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    stampAsset
                );
            }

            if (stampTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    stampTexture
                );
            }
        }
    }

    private static void ValidateTargetBlendModesOnRegionalSurface()
    {
        bool maxPassed =
            ValidateTargetBlendCase(
                TerrainHeightBlendMode.Max,
                150f,
                150f,
                out string maxError
            );

        bool minPassed =
            ValidateTargetBlendCase(
                TerrainHeightBlendMode.Min,
                40f,
                40f,
                out string minError
            );

        bool replacePassed =
            ValidateTargetBlendCase(
                TerrainHeightBlendMode.Replace,
                180f,
                180f,
                out string replaceError
            );

        bool passed =
            maxPassed
            &&
            minPassed
            &&
            replacePassed;

        AddResult(
            "Max/Min/Replace stamps continue to operate on the regional surface",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Max, Min, and Replace target-surface modes produced their " +
                  "expected absolute results after the 100m regional stage."
                : maxError + " " + minError + " " + replaceError
        );
    }

    private static bool ValidateTargetBlendCase(
        TerrainHeightBlendMode blendMode,
        float targetHeight,
        float expectedHeight,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        Texture2D texture =
            null;

        TerrainHeightStampAsset asset =
            null;

        TerrainAuthoringData data =
            null;

        try
        {
            texture =
                CreateUniformStampTexture(
                    1f
                );

            asset =
                ScriptableObject
                    .CreateInstance<TerrainHeightStampAsset>();

            asset.SetHeightTextureInternal(
                texture
            );

            TerrainStampModifier stamp =
                CreateFullTileStamp(
                    asset,
                    blendMode
                );

            stamp.SetTargetBaseHeightInternal(
                targetHeight
            );

            stamp.SetTargetHeightRangeInternal(
                0f
            );

            data =
                CreateAuthoringData(
                    CreateNodeSource(
                        new NodeSpec(
                            new Vector2(
                                50f,
                                50f
                            ),
                            100f
                        )
                    ),
                    TerrainHeightSourceMode.Flat
                );

            data.AddHeightModifierInternal(
                stamp
            );

            if (
                !TryComposeAndRead(
                    data,
                    500f,
                    false,
                    out float[] values,
                    out _,
                    out _,
                    out _,
                    out errorMessage
                )
            )
            {
                return false;
            }

            float center =
                values[
                    ToIndex(
                        2,
                        2
                    )
                ];

            return
                Approximately(
                    center,
                    expectedHeight,
                    GpuTolerance
                );
        }
        finally
        {
            if (data != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    data
                );
            }

            if (asset != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    asset
                );
            }

            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    texture
                );
            }
        }
    }

    private static void ValidateRegionalRangeAccounting()
    {
        TerrainNodeElevationSource source =
            CreateNodeSource(
                new NodeSpec(
                    new Vector2(
                        0f,
                        0f
                    ),
                    20f
                ),
                new NodeSpec(
                    new Vector2(
                        100f,
                        100f
                    ),
                    240f
                )
            );

        TerrainAuthoringData data =
            CreateAuthoringData(
                source,
                TerrainHeightSourceMode.Flat
            );

        try
        {
            bool composed =
                TryComposeAndRead(
                    data,
                    500f,
                    true,
                    out _,
                    out float minimumHeight,
                    out float maximumHeight,
                    out _,
                    out string errorMessage
                );

            bool passed =
                composed
                &&
                Approximately(
                    minimumHeight,
                    20f,
                    0.0001f
                )
                &&
                Approximately(
                    maximumHeight,
                    240f,
                    0.0001f
                );

            AddResult(
                "Preview conservative range resets to regional node min/max",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Range-aware composition replaced the 500m committed " +
                      "base range with the conservative 20..240m global node " +
                      "range before modifier accounting."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateInvalidRegionalStates()
    {
        TerrainAuthoringData emptyData =
            CreateAuthoringData(
                new TerrainNodeElevationSource(),
                TerrainHeightSourceMode.Flat
            );

        TerrainAuthoringData malformedData =
            null;

        try
        {
            bool emptyRejected =
                !TryComposeAndRead(
                    emptyData,
                    0f,
                    false,
                    out _,
                    out _,
                    out _,
                    out _,
                    out string emptyError
                );

            TerrainNodeElevationSource malformedSource =
                CreateNodeSource(
                    new NodeSpec(
                        Vector2.zero,
                        10f
                    )
                );

            FieldInfo nodesField =
                typeof(TerrainNodeElevationSource)
                    .GetField(
                        "nodes",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic
                    );

            List<TerrainElevationNode> rawNodes =
                nodesField != null
                    ? nodesField.GetValue(
                        malformedSource
                    ) as List<TerrainElevationNode>
                    : null;

            rawNodes?.Add(
                null
            );

            malformedData =
                CreateAuthoringData(
                    malformedSource,
                    TerrainHeightSourceMode.Flat
                );

            string malformedError =
                "";

            bool malformedRejected =
                rawNodes != null
                &&
                !TryComposeAndRead(
                    malformedData,
                    0f,
                    false,
                    out _,
                    out _,
                    out _,
                    out _,
                    out malformedError
                );

            bool passed =
                emptyRejected
                &&
                malformedRejected;

            AddResult(
                "Empty and malformed active node sources fail before unsafe GPU output",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Zero-node and null-entry node sources were rejected " +
                      "instead of silently falling back to committed terrain."
                    : emptyError + " " + malformedError
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                emptyData
            );

            if (malformedData != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    malformedData
                );
            }
        }
    }

    private static void ValidateFlatOnlySourceModeContract()
    {
        TerrainNodeElevationSource proceduralSource =
            CreateNodeSource(
                new NodeSpec(
                    Vector2.zero,
                    100f
                )
            );

        TerrainNodeElevationSource importedSource =
            CreateNodeSource(
                new NodeSpec(
                    Vector2.zero,
                    100f
                )
            );

        TerrainAuthoringData proceduralData =
            CreateAuthoringData(
                proceduralSource,
                TerrainHeightSourceMode.Procedural
            );

        TerrainAuthoringData importedData =
            CreateAuthoringData(
                importedSource,
                TerrainHeightSourceMode.Imported
            );

        try
        {
            bool proceduralRejected =
                !TryComposeAndRead(
                    proceduralData,
                    0f,
                    false,
                    out _,
                    out _,
                    out _,
                    out _,
                    out string proceduralError
                );

            bool importedRejected =
                !TryComposeAndRead(
                    importedData,
                    0f,
                    false,
                    out _,
                    out _,
                    out _,
                    out _,
                    out string importedError
                );

            bool passed =
                proceduralRejected
                &&
                importedRejected
                &&
                proceduralError.Contains(
                    "Flat"
                )
                &&
                importedError.Contains(
                    "Flat"
                );

            AddResult(
                "Package 4 enforces Flat-only committed-base semantics",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Procedural and Imported base modes with an active node " +
                      "regional source failed clearly instead of inventing " +
                      "undefined blending semantics."
                    : proceduralError + " " + importedError
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                proceduralData
            );

            UnityEngine.Object.DestroyImmediate(
                importedData
            );
        }
    }

    private static void ValidateWholeWorldTileCollection()
    {
        TerrainAuthoringData data =
            CreateAuthoringData(
                CreateNodeSource(
                    new NodeSpec(
                        Vector2.zero,
                        100f
                    )
                ),
                TerrainHeightSourceMode.Flat
            );

        try
        {
            HashSet<Vector2Int> previewTiles =
                new HashSet<Vector2Int>();

            bool collected =
                TerrainRegionalElevationCompositionUtility
                    .TryCollectRequiredHeightTiles(
                        realWorldSettings,
                        data,
                        previewTiles,
                        1,
                        out bool regionalRequired,
                        out string errorMessage
                    );

            int expected =
                realWorldSettings.HeightTileGridWidth
                *
                realWorldSettings.HeightTileGridHeight;

            bool passed =
                collected
                &&
                regionalRequired
                &&
                previewTiles.Count ==
                    expected;

            AddResult(
                "Active regional source schedules the complete logical preview world",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? $"Shared composition scheduling collected all {expected:N0} " +
                      "logical authoring height tiles even with zero modifiers."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateRuntimeCompositionCoverage()
    {
        TerrainAuthoringData data =
            CreateAuthoringData(
                CreateNodeSource(
                    new NodeSpec(
                        Vector2.zero,
                        100f
                    )
                ),
                TerrainHeightSourceMode.Flat
            );

        TerrainRuntimeHeightCompositionContext context =
            new TerrainRuntimeHeightCompositionContext();

        try
        {
            bool prepared =
                context.TryPrepare(
                    realWorldSettings,
                    data,
                    out string errorMessage
                );

            int expected =
                realWorldSettings.HeightTileGridWidth
                *
                realWorldSettings.HeightTileGridHeight;

            bool cornerCoverage =
                expected > 0
                &&
                context.RequiresComposition(
                    Vector2Int.zero
                )
                &&
                context.RequiresComposition(
                    new Vector2Int(
                        realWorldSettings.HeightTileGridWidth - 1,
                        realWorldSettings.HeightTileGridHeight - 1
                    )
                );

            bool passed =
                prepared
                &&
                context.RegionalCompositionRequired
                &&
                context.AffectedTileCount ==
                    expected
                &&
                cornerCoverage;

            AddResult(
                "Runtime composition context classifies the whole world without modifiers",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? $"Runtime composition prepared {expected:N0} logical " +
                      "height tiles for regional GPU composition with an empty " +
                      "modifier stack."
                    : errorMessage
            );
        }
        finally
        {
            context.Dispose();

            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateRuntimeSharedCompositorOutput()
    {
        const float regionalHeight =
            123.5f;

        TerrainAuthoringData data =
            CreateAuthoringData(
                CreateNodeSource(
                    new NodeSpec(
                        Vector2.zero,
                        regionalHeight
                    )
                ),
                TerrainHeightSourceMode.Flat
            );

        TerrainRuntimeHeightCompositionContext context =
            new TerrainRuntimeHeightCompositionContext();

        Texture2D committedTile =
            null;

        try
        {
            bool prepared =
                context.TryPrepare(
                    realWorldSettings,
                    data,
                    out string errorMessage
                );

            int samplesPerSide =
                realWorldSettings.HeightTileSamplesPerSide;

            if (
                !prepared
                ||
                samplesPerSide <= 1
            )
            {
                AddResult(
                    "Runtime bake path executes the shared regional compositor",
                    ValidationOutcome.Fail,
                    !string.IsNullOrEmpty(
                        errorMessage
                    )
                        ? errorMessage
                        : "Runtime height-tile sample layout is invalid."
                );

                return;
            }

            float[] committedValues =
                new float[
                    samplesPerSide *
                    samplesPerSide
                ];

            for (
                int index = 0;
                index < committedValues.Length;
                index++
            )
            {
                committedValues[index] =
                    500f;
            }

            committedTile =
                new Texture2D(
                    samplesPerSide,
                    samplesPerSide,
                    TextureFormat.RFloat,
                    false,
                    true
                );

            committedTile.name =
                "WorldMeshes Package 4 Runtime Composition Validation Base";

            committedTile.wrapMode =
                TextureWrapMode.Clamp;

            committedTile.filterMode =
                FilterMode.Point;

            committedTile.SetPixelData(
                committedValues,
                0
            );

            committedTile.Apply(
                false,
                false
            );

            float[] output =
                new float[
                    committedValues.Length
                ];

            bool composed =
                context.TryComposeCommittedTile(
                    committedTile,
                    Vector2Int.zero,
                    output,
                    out string compositionError
                );

            bool valuesValid =
                composed
                &&
                AllApproximately(
                    output,
                    regionalHeight,
                    GpuTolerance
                );

            bool passed =
                prepared
                &&
                context.RegionalCompositionRequired
                &&
                context.RegionalElevationDispatchCount >
                    0
                &&
                valuesValid;

            AddResult(
                "Runtime bake path executes the shared regional compositor",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "A transient committed 500m runtime tile was passed " +
                      "through TerrainRuntimeHeightCompositionContext and " +
                      "became the constant 123.5m node regional surface. " +
                      "This proves regional-only runtime baking reaches the " +
                      "same TerrainHeightCompositor used by preview."
                    : !string.IsNullOrEmpty(
                        compositionError
                    )
                        ? compositionError
                        : errorMessage
            );
        }
        finally
        {
            context.Dispose();

            if (committedTile != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    committedTile
                );
            }

            UnityEngine.Object.DestroyImmediate(
                data
            );
        }
    }

    private static void ValidateRealStateUnchanged()
    {
        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    realWorldSettings
                );

        string overallAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    realWorldSettings,
                    realAuthoringData
                );

        bool passed =
            realAuthoringData.authoringRevision ==
                realRevisionBefore
            &&
            ReferenceEquals(
                realAuthoringData.RegionalElevationSource,
                realRegionalSourceBefore
            )
            &&
            committedAfter ==
                committedSignatureBefore
            &&
            overallAfter ==
                overallSignatureBefore;

        AddResult(
            "Real authoring and committed state remains unchanged",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Transient Package 4 GPU/range/scheduling tests did not " +
                  "alter the real regional source, authoring revision, " +
                  "committed signature, or overall authoring signature."
                : "Persistent authoring state changed during validation."
        );
    }

    private static bool TryComposeAndRead(
        TerrainAuthoringData authoringData,
        float committedBaseHeight,
        bool trackRange,
        out float[] values,
        out float compositeMinimumHeight,
        out float compositeMaximumHeight,
        out int regionalDispatchCount,
        out string errorMessage
    )
    {
        values =
            null;

        compositeMinimumHeight =
            committedBaseHeight;

        compositeMaximumHeight =
            committedBaseHeight;

        regionalDispatchCount =
            0;

        errorMessage =
            "";

        RenderTexture target =
            null;

        Texture2D seed =
            null;

        TerrainHeightCompositor compositor =
            new TerrainHeightCompositor();

        try
        {
            if (
                !TryCreateSeededTarget(
                    committedBaseHeight,
                    out target,
                    out seed,
                    out errorMessage
                )
            )
            {
                return false;
            }

            compositor.BeginTransactionDiagnostics();

            bool composed;

            if (trackRange)
            {
                composed =
                    compositor.TryComposeTile(
                        target,
                        Vector2Int.zero,
                        0,
                        SamplesPerSide,
                        SampleSpacing,
                        TileWorldSize,
                        TestWorldSizeXZ,
                        authoringData,
                        committedBaseHeight,
                        committedBaseHeight,
                        out compositeMinimumHeight,
                        out compositeMaximumHeight,
                        out errorMessage
                    );
            }
            else
            {
                composed =
                    compositor.TryComposeTile(
                        target,
                        Vector2Int.zero,
                        0,
                        SamplesPerSide,
                        SampleSpacing,
                        TileWorldSize,
                        TestWorldSizeXZ,
                        authoringData,
                        out errorMessage
                    );
            }

            regionalDispatchCount =
                compositor.LastRegionalElevationDispatchCount;

            if (!composed)
            {
                return false;
            }

            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    target,
                    0,
                    0,
                    SamplesPerSide,
                    0,
                    SamplesPerSide,
                    0,
                    1,
                    TextureFormat.RFloat,
                    null
                );

            request.WaitForCompletion();

            if (request.hasError)
            {
                errorMessage =
                    "GPU readback failed for the Package 4 validation target.";

                return false;
            }

            var data =
                request.GetData<float>();

            if (
                data.Length !=
                SamplesPerSide *
                SamplesPerSide
            )
            {
                errorMessage =
                    "GPU readback returned an unexpected sample count.";

                return false;
            }

            values =
                new float[
                    data.Length
                ];

            for (
                int index = 0;
                index < data.Length;
                index++
            )
            {
                values[index] =
                    data[index];
            }

            return true;
        }
        finally
        {
            compositor.Dispose();

            if (target != null)
            {
                if (target.IsCreated())
                {
                    target.Release();
                }

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
        }
    }

    private static bool TryCreateSeededTarget(
        float value,
        out RenderTexture target,
        out Texture2D seed,
        out string errorMessage
    )
    {
        target =
            null;

        seed =
            null;

        errorMessage =
            "";

        seed =
            new Texture2D(
                SamplesPerSide,
                SamplesPerSide,
                TextureFormat.RFloat,
                false,
                true
            );

        seed.name =
            "WorldMeshes Package 4 Validation Seed";

        float[] seedValues =
            new float[
                SamplesPerSide *
                SamplesPerSide
            ];

        for (
            int index = 0;
            index < seedValues.Length;
            index++
        )
        {
            seedValues[index] =
                value;
        }

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
                SamplesPerSide,
                SamplesPerSide,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );

        target.name =
            "WorldMeshes Package 4 Validation Target";

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

        target.filterMode =
            FilterMode.Point;

        target.wrapMode =
            TextureWrapMode.Clamp;

        if (!target.Create())
        {
            errorMessage =
                "Could not create the transient RFloat validation target.";

            return false;
        }

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
        catch (
            Exception exception
        )
        {
            errorMessage =
                "Could not seed the transient Package 4 validation target. " +
                exception.Message;

            return false;
        }

        return true;
    }

    private static TerrainAuthoringData CreateAuthoringData(
        TerrainRegionalElevationSource regionalSource,
        TerrainHeightSourceMode sourceMode
    )
    {
        TerrainAuthoringData data =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        data.sourceMode =
            sourceMode;

        data.SetRegionalElevationSourceInternal(
            regionalSource
        );

        return data;
    }

    private static TerrainNodeElevationSource CreateNodeSource(
        params NodeSpec[] specs
    )
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        if (specs != null)
        {
            for (
                int index = 0;
                index < specs.Length;
                index++
            )
            {
                TerrainElevationNode node =
                    new TerrainElevationNode();

                node.SetPositionXZInternal(
                    specs[index].Position
                );

                node.SetElevationInternal(
                    specs[index].Elevation
                );

                source.AddNodeInternal(
                    node
                );
            }
        }

        source.RepairNodeStableIds();

        return source;
    }

    private static Texture2D CreateUniformStampTexture(
        float value
    )
    {
        const int size =
            4;

        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RFloat,
                false,
                true
            );

        texture.name =
            "WorldMeshes Package 4 Uniform Stamp";

        texture.filterMode =
            FilterMode.Bilinear;

        texture.wrapMode =
            TextureWrapMode.Clamp;

        float[] values =
            new float[
                size *
                size
            ];

        for (
            int index = 0;
            index < values.Length;
            index++
        )
        {
            values[index] =
                value;
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

    private static TerrainStampModifier CreateFullTileStamp(
        TerrainHeightStampAsset stampAsset,
        TerrainHeightBlendMode blendMode
    )
    {
        TerrainStampModifier stamp =
            new TerrainStampModifier();

        stamp.SetStampAssetInternal(
            stampAsset
        );

        stamp.SetPositionXZInternal(
            new Vector2(
                50f,
                50f
            )
        );

        stamp.SetSizeXZInternal(
            new Vector2(
                100f,
                100f
            )
        );

        stamp.SetBlendModeInternal(
            blendMode
        );

        stamp.SetFalloffInternal(
            0f
        );

        stamp.SetSmoothingRadiusInternal(
            0f
        );

        stamp.SetSmoothingStrengthInternal(
            0f
        );

        stamp.SetEnabledInternal(
            true
        );

        return stamp;
    }

    private static int ToIndex(
        int x,
        int z
    )
    {
        return
            z *
            SamplesPerSide
            +
            x;
    }

    private static bool AllApproximately(
        float[] values,
        float expected,
        float tolerance
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
                !Approximately(
                    values[index],
                    expected,
                    tolerance
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArraysApproximately(
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
                !Approximately(
                    a[index],
                    b[index],
                    tolerance
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool Approximately(
        float a,
        float b,
        float tolerance
    )
    {
        return
            !float.IsNaN(a)
            &&
            !float.IsInfinity(a)
            &&
            !float.IsNaN(b)
            &&
            !float.IsInfinity(b)
            &&
            Mathf.Abs(
                a -
                b
            )
            <=
            tolerance;
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
                    details ?? ""
            }
        );
    }

    private static void WriteReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Regional Elevation Composition Validation"
        );

        builder.AppendLine(
            "================================================="
        );

        builder.AppendLine();

        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        for (
            int index = 0;
            index < results.Count;
            index++
        )
        {
            ValidationResult result =
                results[index];

            string label;

            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    label =
                        "PASS";

                    passed++;
                    break;

                case ValidationOutcome.Blocked:
                    label =
                        "BLOCKED";

                    blocked++;
                    break;

                default:
                    label =
                        "FAIL";

                    failed++;
                    break;
            }

            builder.AppendLine(
                label +
                " - " +
                result.Name
            );

            if (
                !string.IsNullOrEmpty(
                    result.Details
                )
            )
            {
                builder.AppendLine(
                    "       " +
                    result.Details.Replace(
                        "\n",
                        "\n       "
                    )
                );
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

        string finalStatus =
            failed == 0
            &&
            blocked == 0
                ? "PASSED"
                : failed > 0
                    ? "FAILED"
                    : "BLOCKED";

        builder.AppendLine(
            "Regional elevation composition validation: " +
            finalStatus
        );

        if (
            failed == 0
            &&
            blocked == 0
        )
        {
            Debug.Log(
                builder.ToString()
            );
        }
        else if (
            failed > 0
        )
        {
            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            Debug.LogWarning(
                builder.ToString()
            );
        }
    }
}
