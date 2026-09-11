using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package 3 validation for the pure CPU node-elevation interpolation field.
 *
 * Tests operate on transient in-memory sources. The real TerrainAuthoringData
 * asset and committed authoring heightfield are never modified.
 */
public static class TerrainNodeElevationInterpolationValidationUtility
{
    private const float HeightTolerance =
        0.0001f;

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

    public static bool IsRunning
    {
        get
        {
            return
                validationRunning;
        }
    }

    public static void ValidateNodeElevationInterpolation()
    {
        if (validationRunning)
        {
            Debug.LogWarning(
                "WorldMeshes node elevation interpolation validation is " +
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

        string overallBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    realAuthoringData
                );

        int revisionBefore =
            realAuthoringData.authoringRevision;

        TerrainRegionalElevationSource realRegionalSourceBefore =
            realAuthoringData.RegionalElevationSource;

        if (
            string.IsNullOrEmpty(
                committedBefore
            )
            ||
            string.IsNullOrEmpty(
                overallBefore
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

        ValidateNullAndInvalidSamples();
        ValidateEmptySource();
        ValidateOneNodeConstantField();
        ValidateConstantMultiNodeField();
        ValidateExactNodeValues();
        ValidateTwoNodeSymmetryAndPowerTwo();
        ValidateIrregularLayoutRangeAndDeterminism();
        ValidateCoincidentNodes();
        ValidateNearCoincidentNodes();
        ValidatePackage2FourCorners(
            worldSettings
        );
        ValidateWorldEdgesAndNoZeroFallback(
            worldSettings
        );
        ValidateOrderAndStableIdIndependence();
        ValidateMalformedOutputAndIdentitySeparation();
        ValidateSideEffectFreeEvaluation();

        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    realAuthoringData
                );

        bool realStateUnchanged =
            committedBefore ==
                committedAfter
            &&
            overallBefore ==
                overallAfter
            &&
            revisionBefore ==
                realAuthoringData.authoringRevision
            &&
            ReferenceEquals(
                realRegionalSourceBefore,
                realAuthoringData.RegionalElevationSource
            );

        AddResult(
            "Real authoring state remains unchanged",
            realStateUnchanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            realStateUnchanged
                ? "Package 3 interpolation validation did not alter the real " +
                    "regional source, authoring revision, committed signature, " +
                    "or overall authoring signature."
                : "Real WorldMeshes authoring state changed while running the " +
                    "Package 3 interpolation validation."
        );
    }

    private static void ValidateNullAndInvalidSamples()
    {
        bool nullRejected =
            !TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    null,
                    Vector2.zero,
                    out _,
                    out _
                );

        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(
                    Vector2.zero,
                    10f
                )
            );

        Vector2[] invalidSamples =
        {
            new Vector2(
                float.NaN,
                0f
            ),
            new Vector2(
                0f,
                float.NaN
            ),
            new Vector2(
                float.PositiveInfinity,
                0f
            ),
            new Vector2(
                0f,
                float.NegativeInfinity
            )
        };

        bool invalidRejected =
            true;

        for (
            int index = 0;
            index < invalidSamples.Length;
            index++
        )
        {
            if (
                TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        invalidSamples[index],
                        out _,
                        out _
                    )
            )
            {
                invalidRejected =
                    false;

                break;
            }
        }

        bool passed =
            nullRejected
            &&
            invalidRejected;

        AddResult(
            "Null source and invalid sample positions are rejected",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Null sources plus NaN/Infinity XZ sample coordinates fail " +
                    "cleanly instead of producing terrain heights."
                : "A null source or non-finite sample coordinate was accepted."
        );
    }

    private static void ValidateEmptySource()
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        bool outputStructurallyValid =
            source.TryValidateOutputData(
                out _
            );

        bool evaluated =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    source,
                    Vector2.zero,
                    out float height,
                    out string errorMessage
                );

        bool passed =
            outputStructurallyValid
            &&
            source.NodeCount == 0
            &&
            !evaluated
            &&
            height == 0f
            &&
            !string.IsNullOrEmpty(
                errorMessage
            );

        AddResult(
            "Empty source is structurally valid but not evaluable",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "A zero-node source remains valid serialized structure but " +
                    "does not silently define a 0m regional surface."
                : "Zero-node interpolation semantics did not match the Package " +
                    "3 contract."
        );
    }

    private static void ValidateOneNodeConstantField()
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(
                    new Vector2(
                        100f,
                        200f
                    ),
                    125f
                )
            );

        Vector2[] samples =
        {
            new Vector2(
                100f,
                200f
            ),
            Vector2.zero,
            new Vector2(
                5000f,
                5000f
            ),
            new Vector2(
                -250000f,
                90000f
            )
        };

        bool passed =
            true;

        for (
            int index = 0;
            index < samples.Length;
            index++
        )
        {
            if (
                !TryEvaluateApprox(
                    source,
                    samples[index],
                    125f,
                    HeightTolerance
                )
            )
            {
                passed =
                    false;

                break;
            }
        }

        AddResult(
            "One node defines a constant global field",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "A single 125m node evaluated to 125m at its position, the " +
                    "origin, and distant finite sample positions."
                : "One-node evaluation did not remain constant everywhere."
        );
    }

    private static void ValidateConstantMultiNodeField()
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(
                    new Vector2(
                        0f,
                        0f
                    ),
                    80f
                ),
                new NodeSpec(
                    new Vector2(
                        100f,
                        15f
                    ),
                    80f
                ),
                new NodeSpec(
                    new Vector2(
                        30f,
                        120f
                    ),
                    80f
                ),
                new NodeSpec(
                    new Vector2(
                        160f,
                        200f
                    ),
                    80f
                )
            );

        Vector2[] samples =
        {
            new Vector2(20f, 20f),
            new Vector2(70f, 80f),
            new Vector2(-1000f, 50f),
            new Vector2(4000f, -3000f),
            new Vector2(0f, 60f)
        };

        bool passed =
            true;

        for (
            int index = 0;
            index < samples.Length;
            index++
        )
        {
            if (
                !TryEvaluateApprox(
                    source,
                    samples[index],
                    80f,
                    HeightTolerance
                )
            )
            {
                passed =
                    false;

                break;
            }
        }

        AddResult(
            "Constant multi-node source remains constant",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Interior and far outside-convex-hull samples remained at " +
                    "80m, confirming normalized IDW does not drift to zero."
                : "A constant-height node field did not remain constant."
        );
    }

    private static void ValidateExactNodeValues()
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(
                    new Vector2(5f, 10f),
                    -20f
                ),
                new NodeSpec(
                    new Vector2(120f, 30f),
                    75f
                ),
                new NodeSpec(
                    new Vector2(-40f, 85f),
                    210f
                )
            );

        bool passed =
            true;

        for (
            int index = 0;
            index < source.NodeCount;
            index++
        )
        {
            TerrainElevationNode node =
                source.Nodes[index];

            if (
                !TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        node.PositionXZ,
                        out float evaluated,
                        out _
                    )
                ||
                evaluated !=
                    node.Elevation
            )
            {
                passed =
                    false;

                break;
            }
        }

        AddResult(
            "Exact node samples return exact node elevations",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Sampling each distinct node position returned that node's " +
                    "stored elevation exactly."
                : "At least one exact node sample did not return its exact " +
                    "stored elevation."
        );
    }

    private static void ValidateTwoNodeSymmetryAndPowerTwo()
    {
        TerrainNodeElevationSource symmetrySource =
            CreateSource(
                new NodeSpec(
                    new Vector2(0f, 0f),
                    0f
                ),
                new NodeSpec(
                    new Vector2(100f, 0f),
                    100f
                )
            );

        bool midpointValid =
            TryEvaluateApprox(
                symmetrySource,
                new Vector2(50f, 0f),
                50f,
                HeightTolerance
            );

        bool leftValid =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    symmetrySource,
                    new Vector2(25f, 0f),
                    out float leftHeight,
                    out _
                );

        bool rightValid =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    symmetrySource,
                    new Vector2(75f, 0f),
                    out float rightHeight,
                    out _
                );

        bool symmetric =
            leftValid
            &&
            rightValid
            &&
            Approximately(
                leftHeight +
                    rightHeight,
                100f,
                HeightTolerance
            );

        TerrainNodeElevationSource referenceSource =
            CreateSource(
                new NodeSpec(
                    new Vector2(0f, 0f),
                    0f
                ),
                new NodeSpec(
                    new Vector2(3f, 0f),
                    100f
                )
            );

        /*
         * At x=1 the distances are 1 and 2, so power-2 IDW weights are
         * 1 and 1/4. Expected result = 25 / 1.25 = 20.
         */
        bool powerTwoValid =
            TryEvaluateApprox(
                referenceSource,
                new Vector2(1f, 0f),
                20f,
                HeightTolerance
            );

        bool passed =
            midpointValid
            &&
            symmetric
            &&
            powerTwoValid;

        AddResult(
            "Two-node symmetry and power-2 IDW reference value",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "The midpoint evaluated to 50m, mirrored samples were " +
                    "symmetric, and a 1:2 distance reference evaluated to " +
                    "20m using 1/distanceSquared weights."
                : "Two-node symmetry or the independent power-2 reference " +
                    "value failed."
        );
    }

    private static void ValidateIrregularLayoutRangeAndDeterminism()
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(new Vector2(-30f, 10f), -50f),
                new NodeSpec(new Vector2(17f, 83f), 20f),
                new NodeSpec(new Vector2(140f, -45f), 190f),
                new NodeSpec(new Vector2(77f, 156f), 80f),
                new NodeSpec(new Vector2(-90f, 220f), 130f)
            );

        Vector2[] samples =
        {
            new Vector2(0f, 0f),
            new Vector2(25f, 35f),
            new Vector2(60f, 120f),
            new Vector2(-400f, 500f),
            new Vector2(1000f, -700f)
        };

        bool passed =
            true;

        for (
            int index = 0;
            index < samples.Length;
            index++
        )
        {
            bool successA =
                TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        samples[index],
                        out float heightA,
                        out _
                    );

            bool successB =
                TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        samples[index],
                        out float heightB,
                        out _
                    );

            if (
                !successA
                ||
                !successB
                ||
                !IsFinite(heightA)
                ||
                !Approximately(
                    heightA,
                    heightB,
                    0.000001f
                )
                ||
                heightA < -50f - HeightTolerance
                ||
                heightA > 190f + HeightTolerance
            )
            {
                passed =
                    false;

                break;
            }
        }

        AddResult(
            "Irregular layouts remain finite, deterministic, and in range",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Irregular free-placement nodes produced repeatable finite " +
                    "results within the source elevation range at every sample."
                : "Irregular-node evaluation failed determinism, finiteness, " +
                    "or the weighted-average range property."
        );
    }

    private static void ValidateCoincidentNodes()
    {
        TerrainNodeElevationSource twoNodes =
            CreateSource(
                new NodeSpec(new Vector2(100f, 100f), 50f),
                new NodeSpec(new Vector2(100f, 100f), 150f)
            );

        bool twoValid =
            TryEvaluateApprox(
                twoNodes,
                new Vector2(100f, 100f),
                100f,
                HeightTolerance
            );

        TerrainNodeElevationSource reversed =
            CreateSource(
                new NodeSpec(new Vector2(100f, 100f), 150f),
                new NodeSpec(new Vector2(100f, 100f), 50f)
            );

        bool reverseValid =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    reversed,
                    new Vector2(100f, 100f),
                    out float reversedHeight,
                    out _
                )
            &&
            Approximately(
                reversedHeight,
                100f,
                HeightTolerance
            );

        TerrainNodeElevationSource threeNodes =
            CreateSource(
                new NodeSpec(new Vector2(-25f, 40f), 30f),
                new NodeSpec(new Vector2(-25f, 40f), 60f),
                new NodeSpec(new Vector2(-25f, 40f), 120f)
            );

        bool threeValid =
            TryEvaluateApprox(
                threeNodes,
                new Vector2(-25f, 40f),
                70f,
                HeightTolerance
            );

        bool passed =
            twoValid
            &&
            reverseValid
            &&
            threeValid;

        AddResult(
            "Coincident nodes use arithmetic-mean exact-match semantics",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Two coincident 50/150m nodes evaluated to 100m, three " +
                    "30/60/120m nodes evaluated to 70m, and order did not " +
                    "change the exact-match result."
                : "Coincident-node exact-match averaging did not match the " +
                    "Package 3 contract."
        );
    }

    private static void ValidateNearCoincidentNodes()
    {
        float separation =
            TerrainNodeElevationEvaluator
                .ExactNodeDistance *
            2.5f;

        float midpoint =
            separation *
            0.5f;

        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(Vector2.zero, 40f),
                new NodeSpec(new Vector2(separation, 0f), 160f)
            );

        bool success =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    source,
                    new Vector2(midpoint, 0f),
                    out float height,
                    out _
                );

        bool passed =
            success
            &&
            IsFinite(height)
            &&
            height >= 40f - HeightTolerance
            &&
            height <= 160f + HeightTolerance
            &&
            Approximately(
                height,
                100f,
                0.001f
            );

        AddResult(
            "Near-coincident nodes remain numerically stable",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? $"Nodes separated by {separation:R}m (outside the " +
                    $"{TerrainNodeElevationEvaluator.ExactNodeDistance:R}m " +
                    "exact-match tolerance) used normal IDW and produced a " +
                    "finite in-range result."
                : "Near-coincident nodes outside the exact-match tolerance did " +
                    "not evaluate stably."
        );
    }

    private static void ValidatePackage2FourCorners(
        WorldSettings worldSettings
    )
    {
        bool generated =
            TerrainNodeElevationLayoutUtility
                .TryCreateFourCornerSource(
                    worldSettings,
                    0f,
                    out TerrainNodeElevationSource source,
                    out string generationError
                );

        if (!generated)
        {
            AddResult(
                "Package 2 Four Corners compatibility",
                ValidationOutcome.Fail,
                "Could not generate the Package 2 Four Corners source. " +
                generationError
            );

            return;
        }

        float[] elevations =
        {
            10f,
            20f,
            30f,
            40f
        };

        for (
            int index = 0;
            index < source.NodeCount;
            index++
        )
        {
            source.Nodes[index]
                .SetElevationInternal(
                    elevations[index]
                );
        }

        bool passed =
            source.NodeCount == 4;

        for (
            int index = 0;
            index < source.NodeCount && passed;
            index++
        )
        {
            TerrainElevationNode node =
                source.Nodes[index];

            passed =
                TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        node.PositionXZ,
                        out float height,
                        out _
                    )
                &&
                height ==
                    elevations[index];
        }

        AddResult(
            "Package 2 Four Corners integrates with the evaluator",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Distinct southwest, southeast, northwest, and northeast " +
                    "corner elevations were returned exactly at Package 2 " +
                    "generated node positions."
                : "Package 2 Four Corners data did not evaluate correctly."
        );
    }

    private static void ValidateWorldEdgesAndNoZeroFallback(
        WorldSettings worldSettings
    )
    {
        bool generated =
            TerrainNodeElevationLayoutUtility
                .TryCreateFourCornerSource(
                    worldSettings,
                    0f,
                    out TerrainNodeElevationSource cornerSource,
                    out string generationError
                );

        if (!generated)
        {
            AddResult(
                "World-edge global interpolation",
                ValidationOutcome.Fail,
                "Could not generate the Package 2 Four Corners source. " +
                generationError
            );

            return;
        }

        float[] cornerElevations =
        {
            100f,
            200f,
            300f,
            400f
        };

        for (
            int index = 0;
            index < cornerSource.NodeCount;
            index++
        )
        {
            cornerSource.Nodes[index]
                .SetElevationInternal(
                    cornerElevations[index]
                );
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        Vector2[] edgeSamples =
        {
            new Vector2(0f, worldSize.y * 0.5f),
            new Vector2(worldSize.x, worldSize.y * 0.5f),
            new Vector2(worldSize.x * 0.5f, 0f),
            new Vector2(worldSize.x * 0.5f, worldSize.y)
        };

        bool edgesValid =
            true;

        for (
            int index = 0;
            index < edgeSamples.Length;
            index++
        )
        {
            if (
                !TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        cornerSource,
                        edgeSamples[index],
                        out float height,
                        out _
                    )
                ||
                !IsFinite(height)
                ||
                height < 100f - HeightTolerance
                ||
                height > 400f + HeightTolerance
            )
            {
                edgesValid =
                    false;

                break;
            }
        }

        TerrainNodeElevationSource distantSource =
            CreateSource(
                new NodeSpec(new Vector2(1000f, 1000f), 100f),
                new NodeSpec(new Vector2(1100f, 1000f), 150f),
                new NodeSpec(new Vector2(1050f, 1150f), 200f)
            );

        bool distantValid =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    distantSource,
                    new Vector2(-100000f, -100000f),
                    out float distantHeight,
                    out _
                )
            &&
            distantHeight >= 100f - HeightTolerance
            &&
            distantHeight <= 200f + HeightTolerance;

        bool passed =
            edgesValid
            &&
            distantValid;

        AddResult(
            "World edges and distant samples remain globally interpolated",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Boundary samples remained finite/in-range and a sample far " +
                    "from all >=100m nodes stayed >=100m instead of falling " +
                    "toward an implicit 0m baseline."
                : "Global-field/world-edge behavior introduced an invalid or " +
                    "implicit-zero result."
        );
    }

    private static void ValidateOrderAndStableIdIndependence()
    {
        NodeSpec a =
            new NodeSpec(
                new Vector2(0f, 0f),
                10f
            );

        NodeSpec b =
            new NodeSpec(
                new Vector2(20f, 5f),
                100f
            );

        NodeSpec c =
            new NodeSpec(
                new Vector2(5f, 30f),
                50f
            );

        TerrainNodeElevationSource sourceABC =
            CreateSource(
                a,
                b,
                c
            );

        TerrainNodeElevationSource sourceCBA =
            CreateSource(
                c,
                b,
                a
            );

        Vector2 sample =
            new Vector2(
                7f,
                11f
            );

        bool orderA =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    sourceABC,
                    sample,
                    out float heightABC,
                    out _
                );

        bool orderB =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    sourceCBA,
                    sample,
                    out float heightCBA,
                    out _
                );

        bool orderIndependent =
            orderA
            &&
            orderB
            &&
            Approximately(
                heightABC,
                heightCBA,
                HeightTolerance
            );

        string stableIdBefore =
            sourceABC.Nodes[0].StableId;

        bool changedIdentity =
            TrySetPrivateField(
                sourceABC.Nodes[0],
                "stableId",
                Guid.NewGuid()
                    .ToString("N"),
                out string identityError
            );

        bool identityEvaluated =
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    sourceABC,
                    sample,
                    out float heightAfterIdentityChange,
                    out _
                );

        bool stableIdIndependent =
            changedIdentity
            &&
            sourceABC.Nodes[0].StableId !=
                stableIdBefore
            &&
            identityEvaluated
            &&
            Approximately(
                heightABC,
                heightAfterIdentityChange,
                HeightTolerance
            );

        bool passed =
            orderIndependent
            &&
            stableIdIndependent;

        AddResult(
            "Interpolation is independent of node order and StableId",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Reordered geometric data evaluated equivalently and changing " +
                    "only a node StableId did not change the mathematical " +
                    "height."
                : "Order/identity independence failed. " + identityError
        );
    }

    private static void ValidateMalformedOutputAndIdentitySeparation()
    {
        bool nonFinitePositionRejected =
            ValidateMalformedSourceField(
                "positionXZ",
                new Vector2(
                    float.NaN,
                    0f
                )
            );

        bool nonFiniteElevationRejected =
            ValidateMalformedSourceField(
                "elevation",
                float.PositiveInfinity
            );

        TerrainNodeElevationSource nullNodeSource =
            CreateSource(
                new NodeSpec(Vector2.zero, 10f)
            );

        bool nullInjected =
            TryAppendNullNode(
                nullNodeSource,
                out string nullInjectionError
            );

        bool nullRejected =
            nullInjected
            &&
            !TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    nullNodeSource,
                    Vector2.zero,
                    out _,
                    out _
                );

        TerrainNodeElevationSource badIdentitySource =
            CreateSource(
                new NodeSpec(new Vector2(1f, 2f), 25f),
                new NodeSpec(new Vector2(10f, 7f), 75f)
            );

        bool identityCorrupted =
            TrySetPrivateField(
                badIdentitySource.Nodes[0],
                "stableId",
                "not-a-valid-guid",
                out string identityReflectionError
            );

        bool outputStillValid =
            identityCorrupted
            &&
            badIdentitySource.TryValidateOutputData(
                out _
            );

        bool identityInvalid =
            identityCorrupted
            &&
            !badIdentitySource.TryValidateNodeStableIds(
                out _
            );

        bool mathStillValid =
            identityCorrupted
            &&
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    badIdentitySource,
                    new Vector2(5f, 4f),
                    out float identityHeight,
                    out _
                )
            &&
            IsFinite(identityHeight);

        bool passed =
            nonFinitePositionRejected
            &&
            nonFiniteElevationRejected
            &&
            nullRejected
            &&
            outputStillValid
            &&
            identityInvalid
            &&
            mathStillValid;

        AddResult(
            "Malformed output data is rejected while malformed identity is ignored",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "NaN position, infinite elevation, and null node data were " +
                    "rejected; a malformed StableId remained an identity-only " +
                    "problem and did not block valid interpolation."
                : "Output/identity validation separation failed. " +
                    nullInjectionError + " " + identityReflectionError
        );
    }

    private static void ValidateSideEffectFreeEvaluation()
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(new Vector2(0f, 0f), 10f),
                new NodeSpec(new Vector2(100f, 25f), 80f),
                new NodeSpec(new Vector2(15f, 130f), -20f)
            );

        string beforeSnapshot =
            CaptureSourceSnapshot(
                source
            );

        TerrainAuthoringData tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.SetRegionalElevationSourceInternal(
            source
        );

        tempData.authoringRevision =
            37;

        bool evaluationsSucceeded =
            true;

        for (
            int index = 0;
            index < 64;
            index++
        )
        {
            Vector2 sample =
                new Vector2(
                    index * 3.25f - 50f,
                    index * -1.75f + 90f
                );

            if (
                !TerrainNodeElevationEvaluator
                    .TryEvaluateHeight(
                        source,
                        sample,
                        out float height,
                        out _
                    )
                ||
                !IsFinite(height)
            )
            {
                evaluationsSucceeded =
                    false;

                break;
            }
        }

        string afterSnapshot =
            CaptureSourceSnapshot(
                source
            );

        bool passed =
            evaluationsSucceeded
            &&
            beforeSnapshot ==
                afterSnapshot
            &&
            tempData.authoringRevision ==
                37
            &&
            ReferenceEquals(
                source,
                tempData.RegionalElevationSource
            );

        UnityEngine.Object.DestroyImmediate(
            tempData
        );

        AddResult(
            "Evaluation is read-only and side-effect-free",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Repeated interpolation left node count/order/StableIds/" +
                    "positions/elevations unchanged and did not advance an " +
                    "owning TerrainAuthoringData authoringRevision."
                : "Interpolation changed persistent source/authoring state."
        );
    }

    private static bool ValidateMalformedSourceField(
        string fieldName,
        object value
    )
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(Vector2.zero, 10f),
                new NodeSpec(new Vector2(10f, 10f), 20f)
            );

        if (
            !TrySetPrivateField(
                source.Nodes[0],
                fieldName,
                value,
                out _
            )
        )
        {
            return false;
        }

        return
            !TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    source,
                    new Vector2(5f, 5f),
                    out _,
                    out _
                );
    }

    private static TerrainNodeElevationSource CreateSource(
        params NodeSpec[] nodes
    )
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        for (
            int index = 0;
            index < nodes.Length;
            index++
        )
        {
            TerrainElevationNode node =
                new TerrainElevationNode();

            node.SetPositionXZInternal(
                nodes[index].Position
            );

            node.SetElevationInternal(
                nodes[index].Elevation
            );

            source.AddNodeInternal(
                node
            );
        }

        source.RepairNodeStableIds();

        if (
            !source.TryValidateNodeStableIds(
                out string identityError
            )
        )
        {
            throw new InvalidOperationException(
                "Could not create validation node source. " +
                identityError
            );
        }

        return
            source;
    }

    private static bool TryEvaluateApprox(
        TerrainNodeElevationSource source,
        Vector2 sample,
        float expected,
        float tolerance
    )
    {
        return
            TerrainNodeElevationEvaluator
                .TryEvaluateHeight(
                    source,
                    sample,
                    out float actual,
                    out _
                )
            &&
            Approximately(
                actual,
                expected,
                tolerance
            );
    }

    private static bool TrySetPrivateField(
        object target,
        string fieldName,
        object value,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (target == null)
        {
            errorMessage =
                "Reflection target is null.";

            return false;
        }

        FieldInfo field =
            target.GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        if (field == null)
        {
            errorMessage =
                $"Could not locate private field '{fieldName}' on " +
                $"{target.GetType().Name}.";

            return false;
        }

        field.SetValue(
            target,
            value
        );

        return true;
    }

    private static bool TryAppendNullNode(
        TerrainNodeElevationSource source,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        FieldInfo field =
            typeof(TerrainNodeElevationSource)
                .GetField(
                    "nodes",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic
                );

        if (field == null)
        {
            errorMessage =
                "Could not locate TerrainNodeElevationSource.nodes for " +
                "validation-only malformed-data injection.";

            return false;
        }

        List<TerrainElevationNode> nodes =
            field.GetValue(
                source
            ) as
            List<TerrainElevationNode>;

        if (nodes == null)
        {
            errorMessage =
                "TerrainNodeElevationSource.nodes is unavailable for " +
                "validation-only malformed-data injection.";

            return false;
        }

        nodes.Add(
            null
        );

        return true;
    }

    private static string CaptureSourceSnapshot(
        TerrainNodeElevationSource source
    )
    {
        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            source.NodeCount
        );

        for (
            int index = 0;
            index < source.NodeCount;
            index++
        )
        {
            TerrainElevationNode node =
                source.Nodes[index];

            builder.Append('|');
            builder.Append(index);
            builder.Append('|');
            builder.Append(node.StableId);
            builder.Append('|');
            builder.Append(node.PositionXZ.x.ToString("R"));
            builder.Append('|');
            builder.Append(node.PositionXZ.y.ToString("R"));
            builder.Append('|');
            builder.Append(node.Elevation.ToString("R"));
        }

        return
            builder.ToString();
    }

    private static bool Approximately(
        float a,
        float b,
        float tolerance
    )
    {
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
            "WorldMeshes Node Elevation Interpolation Validation"
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
                "Node elevation interpolation validation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failed > 0)
        {
            builder.AppendLine(
                "Node elevation interpolation validation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Node elevation interpolation validation: BLOCKED"
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }
    }
}
