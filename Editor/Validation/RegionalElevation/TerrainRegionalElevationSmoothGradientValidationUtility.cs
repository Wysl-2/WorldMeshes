using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package I5 validation for derived regional-elevation node gradients.
 *
 * Fixtures are transient plain managed node sources. Production I2 topology
 * and production I5 gradient builder/cache code are exercised directly. The
 * real WorldMeshes authoring asset is observed only to prove validation does
 * not mutate persistent state or Package 7 selection.
 */
public static class TerrainRegionalElevationSmoothGradientValidationUtility
{
    private const float TightTolerance = 0.0005f;
    private const float LargeValueTolerance = 0.02f;

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

        public NodeSpec(Vector2 position, float elevation)
        {
            Position = position;
            Elevation = elevation;
        }
    }

    private static readonly List<ValidationResult> results =
        new List<ValidationResult>();

    private static bool validationRunning;
    private static bool validationScheduled;
    private static WorldSettings worldSettings;
    private static TerrainAuthoringData realAuthoringData;
    private static int realRevisionBefore;
    private static string realCommittedBefore = "";
    private static string realOverallBefore = "";
    private static TerrainRegionalElevationSource realRegionalBefore;
    private static TerrainNodeElevationInterpolationMode realInterpolationBefore;
    private static string[] realStableIdsBefore = new string[0];
    private static Vector2[] realPositionsBefore = new Vector2[0];
    private static float[] realElevationsBefore = new float[0];
    private static readonly List<string> realSelectedIdsBefore =
        new List<string>();
    private static string realPrimaryBefore = "";

    public static bool IsRunning =>
        validationRunning || validationScheduled;

    public static void ValidateSmoothGradients()
    {
        if (validationRunning || validationScheduled)
        {
            return;
        }

        validationScheduled = true;
        EditorApplication.delayCall -= RunScheduledValidation;
        EditorApplication.delayCall += RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -= RunScheduledValidation;

        if (!validationScheduled || validationRunning)
        {
            return;
        }

        validationScheduled = false;
        validationRunning = true;
        results.Clear();

        try
        {
            if (!TryValidatePrerequisites(out string prerequisiteError))
            {
                AddResult(
                    "Validation prerequisites",
                    ValidationOutcome.Blocked,
                    prerequisiteError);
                return;
            }

            CaptureRealBaseline();

            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Pass,
                "Current WorldSettings, TerrainAuthoringData, I2 topology, I5 gradient code, and Package 7 selection state are available.");

            ValidateCapabilityMatrixUnchanged();
            ValidateFlatAndPlanarFields();
            ValidateIrregularPlanarField();
            ValidateTranslationAndElevationOffsetInvariance();
            ValidateSymmetricPeakAndValley();
            ValidateSingleAndTwoNodePolicies();
            ValidateCollinearPolicies();
            ValidateHullAndTwoNeighborBehavior();
            ValidateNearSingularFallback();
            ValidateSignedLargeAndDeterministicBehavior();
            ValidateStableIdAndSourceOrderIndependence();
            ValidateElevationOnlyCacheBehavior();
            ValidateGeometryAndMembershipCacheBehavior();
            ValidateInterpolationModeCacheIndependence();
            ValidateInvalidDataSafety();
            ValidateRealStateUnchanged();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString());
        }
        finally
        {
            validationRunning = false;
            WriteReport();
        }
    }

    private static bool TryValidatePrerequisites(out string errorMessage)
    {
        errorMessage = "";

        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage = "Validation cannot run in or while entering Play Mode.";
            return false;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            errorMessage =
                "Wait for Unity to finish compiling/importing and run validation again.";
            return false;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath);

        realAuthoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath);

        if (worldSettings == null || realAuthoringData == null)
        {
            errorMessage = "WorldSettings or TerrainAuthoringData could not be loaded.";
            return false;
        }

        if (string.IsNullOrEmpty(
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings)) ||
            string.IsNullOrEmpty(
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    realAuthoringData)))
        {
            errorMessage =
                "Initialize the committed authoring heightfield before running Package I5 validation.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore = realAuthoringData.authoringRevision;
        realCommittedBefore =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
        realOverallBefore =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);
        realRegionalBefore = realAuthoringData.RegionalElevationSource;

        TerrainNodeElevationSource realNodeSource =
            realRegionalBefore as TerrainNodeElevationSource;

        realInterpolationBefore =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;

        realStableIdsBefore = CaptureStableIds(realNodeSource);
        realPositionsBefore = CapturePositions(realNodeSource);
        realElevationsBefore = CaptureElevations(realNodeSource);

        realSelectedIdsBefore.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            realSelectedIdsBefore);
        realPrimaryBefore =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);
    }

    private static void ValidateCapabilityMatrixUnchanged()
    {
        bool passed =
            (int)TerrainNodeElevationInterpolationMode.InverseDistanceWeighted == 0 &&
            (int)TerrainNodeElevationInterpolationMode.TriangulatedLinear == 1 &&
            (int)TerrainNodeElevationInterpolationMode.TriangulatedSmooth == 2 &&
            TerrainNodeElevationInterpolationModeUtility.SupportsCpuEvaluation(
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted) &&
            TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted) &&
            TerrainNodeElevationInterpolationModeUtility.SupportsCpuEvaluation(
                TerrainNodeElevationInterpolationMode.TriangulatedLinear) &&
            TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.TriangulatedLinear) &&
            TerrainNodeElevationInterpolationModeUtility.SupportsCpuEvaluation(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth) &&
            TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        AddResult(
            "Interpolation capability matrix reflects I6 CPU Smooth support",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "IDW, Linear, and Smooth are CPU/GPU-ready after I7, and enum values stay 0/1/2."
                : "Interpolation capability reporting does not match the post-I7 contract.");
    }

    private static void ValidateFlatAndPlanarFields()
    {
        Vector2[] positions =
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f),
            new Vector2(10f, 10f),
            new Vector2(5f, 5f)
        };

        bool flat = ValidatePlane(
            positions,
            0f,
            0f,
            125f,
            TightTolerance);

        bool xSlope = ValidatePlane(
            positions,
            2f,
            0f,
            50f,
            TightTolerance);

        bool zSlope = ValidatePlane(
            positions,
            0f,
            -1.5f,
            20f,
            TightTolerance);

        bool diagonal = ValidatePlane(
            positions,
            2f,
            -3f,
            100f,
            TightTolerance);

        bool passed = flat && xSlope && zSlope && diagonal;

        AddResult(
            "Flat, X-slope, Z-slope, and diagonal planes recover expected gradients",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Weighted difference-form least squares reproduced zero, axial, and two-component planar gradients."
                : "At least one exact planar gradient fixture did not match its analytic derivative.");
    }

    private static void ValidateIrregularPlanarField()
    {
        Vector2[] positions =
        {
            new Vector2(0f, 0f),
            new Vector2(13f, 4f),
            new Vector2(31f, 19f),
            new Vector2(7f, 28f),
            new Vector2(24f, 37f),
            new Vector2(46f, 8f),
            new Vector2(38f, 31f),
            new Vector2(18f, 16f)
        };

        bool passed = ValidatePlane(
            positions,
            2f,
            -3f,
            100f,
            0.001f);

        AddResult(
            "Irregular free-position topology reproduces an exact planar gradient",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Non-grid Delaunay adjacency recovered dh/dx=2 and dh/dz=-3 at every sufficiently conditioned vertex."
                : "Irregular planar samples did not reproduce the expected plane gradient.");
    }

    private static void ValidateTranslationAndElevationOffsetInvariance()
    {
        Vector2[] positions =
        {
            new Vector2(0f, 0f),
            new Vector2(12f, 3f),
            new Vector2(2f, 14f),
            new Vector2(15f, 18f),
            new Vector2(7f, 8f)
        };

        TerrainNodeElevationSource original =
            CreatePlaneSource(positions, 1.25f, -0.75f, 40f);

        TerrainNodeElevationSource translated =
            CreatePlaneSource(
                Translate(positions, new Vector2(5000f, -3200f)),
                1.25f,
                -0.75f,
                40f - 1.25f * 5000f - (-0.75f) * -3200f);

        TerrainNodeElevationSource elevated =
            CreatePlaneSource(positions, 1.25f, -0.75f, 25040f);

        bool builtOriginal = TryBuildGradients(
            original,
            out TerrainNodeElevationTopology originalTopology,
            out TerrainNodeElevationGradientData originalGradients,
            out _);

        bool builtTranslated = TryBuildGradients(
            translated,
            out TerrainNodeElevationTopology translatedTopology,
            out TerrainNodeElevationGradientData translatedGradients,
            out _);

        bool builtElevated = TryBuildGradients(
            elevated,
            out TerrainNodeElevationTopology elevatedTopology,
            out TerrainNodeElevationGradientData elevatedGradients,
            out _);

        bool translationInvariant =
            builtOriginal &&
            builtTranslated &&
            GradientsEqualByIndex(
                originalGradients,
                translatedGradients,
                0.001f);

        bool elevationInvariant =
            builtOriginal &&
            builtElevated &&
            PositionsEqual(
                originalTopology,
                elevatedTopology) &&
            GradientsEqualByIndex(
                originalGradients,
                elevatedGradients,
                0.001f);

        AddResult(
            "Gradient derivation is invariant to XZ translation and constant elevation offsets",
            translationInvariant && elevationInvariant
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            translationInvariant && elevationInvariant
                ? "Local dx/dz/dh formulation preserved the gradient field under large world translation and a +25000 elevation offset."
                : "Translation or constant-height-offset invariance failed.");
    }

    private static void ValidateSymmetricPeakAndValley()
    {
        NodeSpec[] hill =
        {
            new NodeSpec(new Vector2(0f, 0f), 200f),
            new NodeSpec(new Vector2(-10f, 0f), 100f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, -10f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 100f)
        };

        NodeSpec[] valley =
        {
            new NodeSpec(new Vector2(0f, 0f), 50f),
            new NodeSpec(new Vector2(-10f, 0f), 150f),
            new NodeSpec(new Vector2(10f, 0f), 150f),
            new NodeSpec(new Vector2(0f, -10f), 150f),
            new NodeSpec(new Vector2(0f, 10f), 150f)
        };

        bool hillOk = TryGetGradientAtPosition(
            CreateSource(hill),
            Vector2.zero,
            out Vector2 hillGradient) &&
            hillGradient.magnitude <= TightTolerance;

        bool valleyOk = TryGetGradientAtPosition(
            CreateSource(valley),
            Vector2.zero,
            out Vector2 valleyGradient) &&
            valleyGradient.magnitude <= TightTolerance;

        AddResult(
            "Symmetric hill and valley centers derive approximately zero slope",
            hillOk && valleyOk ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            hillOk && valleyOk
                ? "Opposing local slope evidence cancelled at both symmetric extrema."
                : "A symmetric peak or valley center produced an unexpected nonzero gradient.");
    }

    private static void ValidateSingleAndTwoNodePolicies()
    {
        TerrainNodeElevationSource empty = CreateSource();

        bool emptyOk = TryBuildGradients(
            empty,
            out TerrainNodeElevationTopology emptyTopology,
            out TerrainNodeElevationGradientData emptyGradients,
            out _) &&
            emptyTopology.Kind == TerrainNodeElevationTopologyKind.Empty &&
            emptyGradients.GradientCount == 0;

        TerrainNodeElevationSource single = CreateSource(
            new NodeSpec(new Vector2(13f, -7f), 200f));

        bool singleOk = TryGetGradientAtPosition(
            single,
            new Vector2(13f, -7f),
            out Vector2 singleGradient) &&
            singleGradient == Vector2.zero;

        bool horizontal = ValidateTwoNodeGradient(
            new Vector2(0f, 0f),
            0f,
            new Vector2(10f, 0f),
            100f,
            new Vector2(10f, 0f));

        bool vertical = ValidateTwoNodeGradient(
            new Vector2(0f, 0f),
            0f,
            new Vector2(0f, 20f),
            40f,
            new Vector2(0f, 2f));

        bool diagonal = ValidateTwoNodeGradient(
            new Vector2(0f, 0f),
            0f,
            new Vector2(10f, 10f),
            100f,
            new Vector2(5f, 5f));

        bool passed =
            emptyOk &&
            singleOk &&
            horizontal &&
            vertical &&
            diagonal;

        AddResult(
            "Empty, single, and two-node layouts use explicit derived-gradient policies",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Empty topology produces zero gradients; one node has zero slope; horizontal, vertical, and diagonal two-node sources recover the expected minimum-norm directional gradient."
                : "One of the empty/single/two-node gradient policies failed.");
    }

    private static void ValidateCollinearPolicies()
    {
        bool horizontal = ValidateCollinearDirection(
            new[]
            {
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(20f, 0f), 40f),
                new NodeSpec(new Vector2(30f, 0f), 180f)
            },
            Vector2.right);

        bool vertical = ValidateCollinearDirection(
            new[]
            {
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(0f, 10f), 20f),
                new NodeSpec(new Vector2(0f, 20f), -10f),
                new NodeSpec(new Vector2(0f, 30f), 50f)
            },
            Vector2.up);

        Vector2 diagonalDirection = new Vector2(1f, 1f).normalized;
        bool diagonal = ValidateCollinearDirection(
            new[]
            {
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 10f), 20f),
                new NodeSpec(new Vector2(20f, 20f), -10f),
                new NodeSpec(new Vector2(30f, 30f), 50f)
            },
            diagonalDirection);

        AddResult(
            "Collinear profiles derive finite deterministic slope only along their common line",
            horizontal && vertical && diagonal
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            horizontal && vertical && diagonal
                ? "Horizontal, vertical, and diagonal nonmonotonic profiles produced no invented perpendicular derivative."
                : "A collinear gradient contained an invalid or unexpected perpendicular component.");
    }

    private static void ValidateHullAndTwoNeighborBehavior()
    {
        TerrainNodeElevationSource triangle = CreatePlaneSource(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(2f, 9f)
            },
            2f,
            -3f,
            10f);

        bool triangleBuilt = TryBuildGradients(
            triangle,
            out TerrainNodeElevationTopology triangleTopology,
            out TerrainNodeElevationGradientData triangleGradients,
            out _);

        bool twoNeighborExact = triangleBuilt;
        if (triangleBuilt)
        {
            for (int index = 0; index < triangleTopology.VertexCount; index++)
            {
                twoNeighborExact &=
                    triangleTopology.GetVertexNeighbors(index).Count == 2 &&
                    GradientEquals(
                        triangleGradients.Gradients[index].GradientXZ,
                        new Vector2(2f, -3f),
                        TightTolerance);
            }
        }

        TerrainNodeElevationSource irregular = CreatePlaneSource(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(12f, 1f),
                new Vector2(24f, 9f),
                new Vector2(20f, 24f),
                new Vector2(4f, 20f),
                new Vector2(10f, 9f)
            },
            -0.5f,
            1.75f,
            30f);

        bool irregularBuilt = TryBuildGradients(
            irregular,
            out TerrainNodeElevationTopology irregularTopology,
            out TerrainNodeElevationGradientData irregularGradients,
            out _);

        bool hullOk = irregularBuilt;
        if (irregularBuilt)
        {
            for (int index = 0; index < irregularTopology.HullVertexCount; index++)
            {
                int vertexIndex = irregularTopology.HullVertexIndices[index];
                Vector2 gradient = irregularGradients.Gradients[vertexIndex].GradientXZ;
                hullOk &=
                    IsFinite(gradient) &&
                    GradientEquals(
                        gradient,
                        new Vector2(-0.5f, 1.75f),
                        0.001f);
            }
        }

        AddResult(
            "Hull nodes and exactly two non-collinear neighbors recover valid full 2D slopes",
            twoNeighborExact && hullOk
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            twoNeighborExact && hullOk
                ? "A three-node triangle recovered the complete plane from two independent equations, and irregular hull nodes remained finite/planar without synthetic exterior samples."
                : "Two-neighbor or hull-node gradient behavior failed.");
    }

    private static void ValidateNearSingularFallback()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(1000f, 0f), 1f),
            new NodeSpec(new Vector2(2000f, 0.01f), 2f));

        bool firstBuilt = TryBuildGradients(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData first,
            out string firstError);

        if (!firstBuilt && topology == null)
        {
            bool secondBuilt = TryBuildGradients(
                source,
                out TerrainNodeElevationTopology secondTopology,
                out _,
                out string secondError);

            bool safelyRejected =
                !secondBuilt &&
                secondTopology == null &&
                !string.IsNullOrEmpty(firstError) &&
                string.Equals(
                    firstError,
                    secondError,
                    StringComparison.Ordinal);

            AddResult(
                "Near-singular local geometry remains bounded or is rejected safely",
                safelyRejected
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                safelyRejected
                    ? "I2 deterministically rejected the pathological nearly-collinear fixture before I5 evaluation, so no unstable gradient data was produced."
                    : "The pathological near-singular fixture was not rejected deterministically. First error: " +
                        firstError +
                        " Second error: " +
                        secondError);
            return;
        }

        bool secondGradientBuilt = TryBuildGradients(
            source,
            out TerrainNodeElevationTopology repeatedTopology,
            out TerrainNodeElevationGradientData second,
            out string secondGradientError);

        bool passed =
            firstBuilt &&
            secondGradientBuilt &&
            topology != null &&
            repeatedTopology != null &&
            topology.Kind == TerrainNodeElevationTopologyKind.Triangulated &&
            repeatedTopology.Kind == TerrainNodeElevationTopologyKind.Triangulated &&
            GradientsEqualByIndex(first, second, 0f);

        if (passed)
        {
            for (int index = 0; index < first.GradientCount; index++)
            {
                Vector2 gradient = first.Gradients[index].GradientXZ;
                passed &=
                    IsFinite(gradient) &&
                    gradient.magnitude < 1f;
            }
        }

        AddResult(
            "Near-singular local geometry remains bounded or is rejected safely",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "I2 accepted the nearly-collinear topology and I5 produced finite, bounded, exactly repeatable directional gradients instead of dividing by a tiny 2D determinant."
                : "Near-singular gradient evaluation failed after I2 accepted the topology. First error: " +
                    firstError +
                    " Second error: " +
                    secondGradientError);
    }

    private static void ValidateSignedLargeAndDeterministicBehavior()
    {
        Vector2[] positions =
        {
            new Vector2(0f, 0f),
            new Vector2(20f, 3f),
            new Vector2(4f, 18f),
            new Vector2(24f, 21f),
            new Vector2(10f, 9f)
        };

        bool negative = ValidatePlane(
            positions,
            -2.5f,
            1.25f,
            -500f,
            0.001f);

        bool large = ValidatePlane(
            positions,
            2f,
            -3f,
            1000000f,
            LargeValueTolerance);

        TerrainNodeElevationSource deterministicSource =
            CreatePlaneSource(positions, 0.75f, -1.125f, 12f);

        bool firstBuilt = TryBuildGradients(
            deterministicSource,
            out _,
            out TerrainNodeElevationGradientData first,
            out _);

        bool secondBuilt = TryBuildGradients(
            deterministicSource,
            out _,
            out TerrainNodeElevationGradientData second,
            out _);

        bool deterministic =
            firstBuilt &&
            secondBuilt &&
            GradientsEqualByIndex(first, second, 0f);

        AddResult(
            "Signed/large finite elevations remain safe and repeated builds are deterministic",
            negative && large && deterministic
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            negative && large && deterministic
                ? "Negative and ~1,000,000-unit elevation fields produced finite gradients, and identical inputs reproduced identical float results."
                : "Signed/large-value safety or repeated-build determinism failed.");
    }

    private static void ValidateStableIdAndSourceOrderIndependence()
    {
        NodeSpec[] ordered =
        {
            new NodeSpec(new Vector2(0f, 0f), 25f),
            new NodeSpec(new Vector2(12f, 2f), 43f),
            new NodeSpec(new Vector2(4f, 15f), 6f),
            new NodeSpec(new Vector2(18f, 17f), 10f),
            new NodeSpec(new Vector2(9f, 8f), 19f)
        };

        TerrainNodeElevationSource source = CreateSource(ordered);
        TerrainNodeElevationTopologyCache topologyCache =
            new TerrainNodeElevationTopologyCache();
        TerrainNodeElevationGradientCache gradientCache =
            new TerrainNodeElevationGradientCache();

        TerrainNodeElevationTopology topology = null;
        TerrainNodeElevationGradientData before = null;

        bool built =
            topologyCache.TryGetOrBuild(source, out topology, out _);

        if (built)
        {
            built =
                gradientCache.TryGetOrBuild(
                    source,
                    topology,
                    out before,
                    out _);
        }

        int topologyRebuilds = topologyCache.RebuildCount;
        int gradientRebuilds = gradientCache.RebuildCount;

        bool changedId = built && SetPrivateStableId(
            source.Nodes[0],
            Guid.NewGuid().ToString("N"));

        TerrainNodeElevationTopology topologyAfterId = null;
        TerrainNodeElevationGradientData afterId = null;

        bool rebuiltAfterId =
            changedId &&
            topologyCache.TryGetOrBuild(source, out topologyAfterId, out _);

        if (rebuiltAfterId)
        {
            rebuiltAfterId =
                gradientCache.TryGetOrBuild(
                    source,
                    topologyAfterId,
                    out afterId,
                    out _);
        }

        bool stableIdIgnored =
            rebuiltAfterId &&
            topologyCache.RebuildCount == topologyRebuilds &&
            gradientCache.RebuildCount == gradientRebuilds &&
            ReferenceEquals(before, afterId);

        TerrainNodeElevationSource reordered = CreateSource(
            ordered[3],
            ordered[1],
            ordered[4],
            ordered[0],
            ordered[2]);

        bool firstGradientBuilt = TryBuildGradients(
            source,
            out TerrainNodeElevationTopology firstTopology,
            out TerrainNodeElevationGradientData firstGradients,
            out _);

        bool reorderedBuilt = TryBuildGradients(
            reordered,
            out TerrainNodeElevationTopology reorderedTopology,
            out TerrainNodeElevationGradientData reorderedGradients,
            out _);

        bool sourceOrderIgnored =
            firstGradientBuilt &&
            reorderedBuilt &&
            GradientFieldsEqualByPosition(
                firstTopology,
                firstGradients,
                reorderedTopology,
                reorderedGradients,
                TightTolerance);

        AddResult(
            "StableId and persistent source ordering do not participate in gradient mathematics",
            stableIdIgnored && sourceOrderIgnored
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            stableIdIgnored && sourceOrderIgnored
                ? "Changing only identity reused both caches; a reordered equivalent source produced the same geometric gradient field."
                : "StableId or source ordering unexpectedly affected derived gradients/cache validity.");
    }

    private static void ValidateElevationOnlyCacheBehavior()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 10f),
            new NodeSpec(new Vector2(0f, 10f), 20f),
            new NodeSpec(new Vector2(10f, 10f), 30f),
            new NodeSpec(new Vector2(5f, 5f), 15f));

        TerrainNodeElevationTopologyCache topologyCache =
            new TerrainNodeElevationTopologyCache();
        TerrainNodeElevationGradientCache gradientCache =
            new TerrainNodeElevationGradientCache();

        TerrainNodeElevationTopology firstTopology = null;
        TerrainNodeElevationGradientData firstGradients = null;

        bool initial =
            topologyCache.TryGetOrBuild(source, out firstTopology, out _);

        if (initial)
        {
            initial =
                gradientCache.TryGetOrBuild(
                    source,
                    firstTopology,
                    out firstGradients,
                    out _);
        }

        int topologyBefore = topologyCache.RebuildCount;
        int gradientBefore = gradientCache.RebuildCount;

        if (initial)
        {
            source.Nodes[4].SetElevationInternal(55f);
        }

        TerrainNodeElevationTopology secondTopology = null;
        TerrainNodeElevationGradientData secondGradients = null;

        bool refreshed =
            initial &&
            topologyCache.TryGetOrBuild(source, out secondTopology, out _);

        if (refreshed)
        {
            refreshed =
                gradientCache.TryGetOrBuild(
                    source,
                    secondTopology,
                    out secondGradients,
                    out _);
        }

        bool gradientChanged =
            refreshed &&
            !GradientDataExactlyEqual(firstGradients, secondGradients);

        bool passed =
            refreshed &&
            ReferenceEquals(firstTopology, secondTopology) &&
            topologyCache.RebuildCount == topologyBefore &&
            gradientCache.RebuildCount == gradientBefore + 1 &&
            gradientChanged;

        AddResult(
            "Elevation-only edits reuse I2 topology and lazily rebuild gradient data",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Topology RebuildCount stayed fixed while Gradient RebuildCount advanced once on the next request and reflected the new height."
                : "Elevation-only cache dependency behavior did not match the I5 contract.");
    }

    private static void ValidateGeometryAndMembershipCacheBehavior()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 10f),
            new NodeSpec(new Vector2(0f, 10f), 20f),
            new NodeSpec(new Vector2(10f, 10f), 30f));

        TerrainNodeElevationTopologyCache topologyCache =
            new TerrainNodeElevationTopologyCache();
        TerrainNodeElevationGradientCache gradientCache =
            new TerrainNodeElevationGradientCache();

        TerrainNodeElevationTopology topology = null;

        bool initial =
            topologyCache.TryGetOrBuild(source, out topology, out _);

        if (initial)
        {
            initial =
                gradientCache.TryGetOrBuild(
                    source,
                    topology,
                    out _,
                    out _);
        }

        int topologyStart = topologyCache.RebuildCount;
        int gradientStart = gradientCache.RebuildCount;

        if (initial)
        {
            source.Nodes[0].SetPositionXZInternal(new Vector2(-2f, 1f));
        }

        TerrainNodeElevationTopology movedTopology = null;

        bool positionRefresh =
            initial &&
            topologyCache.TryGetOrBuild(source, out movedTopology, out _);

        if (positionRefresh)
        {
            positionRefresh =
                gradientCache.TryGetOrBuild(
                    source,
                    movedTopology,
                    out _,
                    out _);
        }

        bool positionOk =
            positionRefresh &&
            topologyCache.RebuildCount == topologyStart + 1 &&
            gradientCache.RebuildCount == gradientStart + 1;

        int topologyAfterMove = topologyCache.RebuildCount;
        int gradientAfterMove = gradientCache.RebuildCount;

        if (positionRefresh)
        {
            TerrainElevationNode added = new TerrainElevationNode();
            added.SetPositionXZInternal(new Vector2(5f, 4f));
            added.SetElevationInternal(12f);
            source.AddNodeInternal(added);
            source.RepairNodeStableIds();
        }

        TerrainNodeElevationTopology addedTopology = null;
        TerrainNodeElevationGradientData addedGradients = null;

        bool membershipRefresh =
            positionRefresh &&
            topologyCache.TryGetOrBuild(source, out addedTopology, out _);

        if (membershipRefresh)
        {
            membershipRefresh =
                gradientCache.TryGetOrBuild(
                    source,
                    addedTopology,
                    out addedGradients,
                    out _);
        }

        bool addOk =
            membershipRefresh &&
            topologyCache.RebuildCount == topologyAfterMove + 1 &&
            gradientCache.RebuildCount == gradientAfterMove + 1 &&
            addedGradients != null &&
            addedGradients.GradientCount == source.NodeCount;

        int topologyAfterAdd = topologyCache.RebuildCount;
        int gradientAfterAdd = gradientCache.RebuildCount;

        bool removed =
            addOk &&
            source.RemoveNodeAtInternal(source.NodeCount - 1);

        TerrainNodeElevationTopology removedTopology = null;
        TerrainNodeElevationGradientData removedGradients = null;

        bool removalRefresh =
            removed &&
            topologyCache.TryGetOrBuild(source, out removedTopology, out _);

        if (removalRefresh)
        {
            removalRefresh =
                gradientCache.TryGetOrBuild(
                    source,
                    removedTopology,
                    out removedGradients,
                    out _);
        }

        bool removeOk =
            removalRefresh &&
            topologyCache.RebuildCount == topologyAfterAdd + 1 &&
            gradientCache.RebuildCount == gradientAfterAdd + 1 &&
            removedGradients != null &&
            removedGradients.GradientCount == source.NodeCount;

        bool passed =
            positionOk &&
            addOk &&
            removeOk;

        AddResult(
            "Position and membership edits lazily rebuild topology and gradients together",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "A later derived-data request rebuilt I2/I5 once after XZ movement, once after node addition, and once after node removal."
                : "Geometry or membership cache invalidation/rebuild behavior failed.");
    }

    private static void ValidateInterpolationModeCacheIndependence()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 10f),
            new NodeSpec(new Vector2(0f, 10f), 20f));

        TerrainNodeElevationTopologyCache topologyCache =
            new TerrainNodeElevationTopologyCache();
        TerrainNodeElevationGradientCache gradientCache =
            new TerrainNodeElevationGradientCache();

        TerrainNodeElevationTopology topology = null;
        TerrainNodeElevationGradientData before = null;

        bool initial =
            topologyCache.TryGetOrBuild(source, out topology, out _);

        if (initial)
        {
            initial =
                gradientCache.TryGetOrBuild(
                    source,
                    topology,
                    out before,
                    out _);
        }

        int topologyCount = topologyCache.RebuildCount;
        int gradientCount = gradientCache.RebuildCount;

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        TerrainNodeElevationTopology topologyLinear = null;
        TerrainNodeElevationGradientData linearData = null;

        bool afterLinear =
            initial &&
            topologyCache.TryGetOrBuild(source, out topologyLinear, out _);

        if (afterLinear)
        {
            afterLinear =
                gradientCache.TryGetOrBuild(
                    source,
                    topologyLinear,
                    out linearData,
                    out _);
        }

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.InverseDistanceWeighted);

        TerrainNodeElevationTopology topologyIdw = null;
        TerrainNodeElevationGradientData idwData = null;

        bool afterIdw =
            afterLinear &&
            topologyCache.TryGetOrBuild(source, out topologyIdw, out _);

        if (afterIdw)
        {
            afterIdw =
                gradientCache.TryGetOrBuild(
                    source,
                    topologyIdw,
                    out idwData,
                    out _);
        }

        bool passed =
            afterIdw &&
            topologyCache.RebuildCount == topologyCount &&
            gradientCache.RebuildCount == gradientCount &&
            ReferenceEquals(before, linearData) &&
            ReferenceEquals(before, idwData);

        AddResult(
            "Interpolation mode is not a topology or gradient cache dependency",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "IDW -> Linear -> IDW reused identical geometry/elevation-derived data without a rebuild."
                : "Changing only interpolation mode unexpectedly invalidated derived topology/gradients.");
    }

    private static void ValidateInvalidDataSafety()
    {
        TerrainNodeElevationSource invalidPosition = CreateSource(
            new NodeSpec(Vector2.zero, 10f));

        bool setInvalidPosition =
            SetPrivatePosition(
                invalidPosition.Nodes[0],
                new Vector2(float.NaN, 0f));

        TerrainNodeElevationTopologyCache topologyCache =
            new TerrainNodeElevationTopologyCache();

        bool nonFinitePositionRejected =
            setInvalidPosition &&
            !topologyCache.TryGetOrBuild(
                invalidPosition,
                out _,
                out _);

        TerrainNodeElevationSource invalidElevation = CreateSource(
            new NodeSpec(Vector2.zero, 10f));

        bool setInvalidElevation =
            SetPrivateElevation(
                invalidElevation.Nodes[0],
                float.PositiveInfinity);

        TerrainNodeElevationTopology invalidElevationTopology = null;

        bool invalidElevationTopologyBuilt =
            setInvalidElevation &&
            TerrainNodeElevationTriangulationUtility.TryBuild(
                invalidElevation,
                out invalidElevationTopology,
                out _);

        bool nonFiniteElevationRejected =
            invalidElevationTopologyBuilt &&
            !TerrainNodeElevationGradientUtility.TryBuild(
                invalidElevation,
                invalidElevationTopology,
                out _,
                out _);

        TerrainNodeElevationSource tooClose = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(0.0001f, 0f), 1f));

        bool nearCoincidentRejected =
            !TerrainNodeElevationTriangulationUtility.TryBuild(
                tooClose,
                out _,
                out _);

        TerrainNodeElevationSource validSource = CreateSource(
            new NodeSpec(Vector2.zero, 10f));

        TerrainNodeElevationTopology malformed =
            new TerrainNodeElevationTopology(
                TerrainNodeElevationTopologyKind.SinglePoint,
                new[]
                {
                    new TerrainNodeElevationTopologyVertex(
                        Vector2.zero,
                        99)
                },
                new TerrainNodeElevationTopologyTriangle[0],
                new TerrainNodeElevationTopologyEdge[0],
                new[] { 0 },
                new TerrainNodeElevationTopologyEdge[0],
                new[] { new int[0] });

        bool mappingRejected =
            !TerrainNodeElevationGradientUtility.TryBuild(
                validSource,
                malformed,
                out _,
                out _);

        bool passed =
            nonFinitePositionRejected &&
            nonFiniteElevationRejected &&
            nearCoincidentRejected &&
            mappingRejected;

        AddResult(
            "Malformed, non-finite, and unsafe-close gradient inputs fail explicitly",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Non-finite position/elevation data, below-minimum-separation geometry, and invalid topology SourceNodeIndex mapping were rejected without NaN/Infinity output."
                : "Invalid gradient input was accepted unexpectedly.");
    }

    private static void ValidateRealStateUnchanged()
    {
        TerrainNodeElevationSource realNodeSource =
            realAuthoringData.RegionalElevationSource as TerrainNodeElevationSource;

        List<string> selectedAfter = new List<string>();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            selectedAfter);

        string primaryAfter =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);

        TerrainNodeElevationInterpolationMode interpolationAfter =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;

        bool passed =
            realAuthoringData.authoringRevision == realRevisionBefore &&
            string.Equals(
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings),
                realCommittedBefore,
                StringComparison.Ordinal) &&
            string.Equals(
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    realAuthoringData),
                realOverallBefore,
                StringComparison.Ordinal) &&
            ReferenceEquals(
                realAuthoringData.RegionalElevationSource,
                realRegionalBefore) &&
            interpolationAfter == realInterpolationBefore &&
            ArraysEqual(CaptureStableIds(realNodeSource), realStableIdsBefore) &&
            ArraysEqual(CapturePositions(realNodeSource), realPositionsBefore) &&
            ArraysEqual(CaptureElevations(realNodeSource), realElevationsBefore) &&
            SequenceEqual(selectedAfter, realSelectedIdsBefore) &&
            string.Equals(primaryAfter, realPrimaryBefore, StringComparison.Ordinal);

        AddResult(
            "Real authoring and Package 7 selection state remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Package I5 validation changed no real source, revision, signatures, node data, interpolation mode, or editor selection state."
                : "Real WorldMeshes authoring or selection state changed during Package I5 validation.");
    }

    private static bool ValidatePlane(
        Vector2[] positions,
        float gradientX,
        float gradientZ,
        float intercept,
        float tolerance)
    {
        TerrainNodeElevationSource source =
            CreatePlaneSource(
                positions,
                gradientX,
                gradientZ,
                intercept);

        if (!TryBuildGradients(
            source,
            out _,
            out TerrainNodeElevationGradientData gradients,
            out _))
        {
            return false;
        }

        Vector2 expected =
            new Vector2(
                gradientX,
                gradientZ);

        for (int index = 0; index < gradients.GradientCount; index++)
        {
            if (!GradientEquals(
                gradients.Gradients[index].GradientXZ,
                expected,
                tolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateTwoNodeGradient(
        Vector2 aPosition,
        float aElevation,
        Vector2 bPosition,
        float bElevation,
        Vector2 expected)
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(aPosition, aElevation),
            new NodeSpec(bPosition, bElevation));

        if (!TryBuildGradients(
            source,
            out _,
            out TerrainNodeElevationGradientData gradients,
            out _))
        {
            return false;
        }

        return
            gradients.GradientCount == 2 &&
            GradientEquals(
                gradients.Gradients[0].GradientXZ,
                expected,
                TightTolerance) &&
            GradientEquals(
                gradients.Gradients[1].GradientXZ,
                expected,
                TightTolerance);
    }

    private static bool ValidateCollinearDirection(
        NodeSpec[] specs,
        Vector2 expectedDirection)
    {
        TerrainNodeElevationSource source = CreateSource(specs);

        if (!TryBuildGradients(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out _))
        {
            return false;
        }

        if (topology.Kind != TerrainNodeElevationTopologyKind.Collinear)
        {
            return false;
        }

        Vector2 normal =
            new Vector2(
                -expectedDirection.y,
                expectedDirection.x).normalized;

        for (int index = 0; index < gradients.GradientCount; index++)
        {
            Vector2 gradient = gradients.Gradients[index].GradientXZ;
            if (
                !IsFinite(gradient) ||
                Mathf.Abs(Vector2.Dot(gradient, normal)) > 0.001f)
            {
                return false;
            }
        }

        return true;
    }

    private static TerrainNodeElevationSource CreatePlaneSource(
        Vector2[] positions,
        float gradientX,
        float gradientZ,
        float intercept)
    {
        NodeSpec[] specs = new NodeSpec[positions.Length];

        for (int index = 0; index < positions.Length; index++)
        {
            Vector2 position = positions[index];
            float elevation =
                gradientX * position.x +
                gradientZ * position.y +
                intercept;

            specs[index] =
                new NodeSpec(
                    position,
                    elevation);
        }

        return CreateSource(specs);
    }

    private static TerrainNodeElevationSource CreateSource(
        params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        if (nodes != null)
        {
            for (int index = 0; index < nodes.Length; index++)
            {
                TerrainElevationNode node =
                    new TerrainElevationNode();
                node.SetPositionXZInternal(nodes[index].Position);
                node.SetElevationInternal(nodes[index].Elevation);
                source.AddNodeInternal(node);
            }
        }

        source.RepairNodeStableIds();
        return source;
    }

    private static bool TryBuildGradients(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopology topology,
        out TerrainNodeElevationGradientData gradients,
        out string errorMessage)
    {
        topology = null;
        gradients = null;
        errorMessage = "";

        if (!TerrainNodeElevationTriangulationUtility.TryBuild(
            source,
            out topology,
            out errorMessage))
        {
            return false;
        }

        return TerrainNodeElevationGradientUtility.TryBuild(
            source,
            topology,
            out gradients,
            out errorMessage);
    }

    private static bool TryGetGradientAtPosition(
        TerrainNodeElevationSource source,
        Vector2 position,
        out Vector2 gradient)
    {
        gradient = Vector2.zero;

        if (!TryBuildGradients(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out _))
        {
            return false;
        }

        for (int index = 0; index < topology.VertexCount; index++)
        {
            if (topology.Vertices[index].PositionXZ != position)
            {
                continue;
            }

            gradient = gradients.Gradients[index].GradientXZ;
            return true;
        }

        return false;
    }

    private static Vector2[] Translate(
        Vector2[] positions,
        Vector2 delta)
    {
        Vector2[] translated = new Vector2[positions.Length];
        for (int index = 0; index < positions.Length; index++)
        {
            translated[index] = positions[index] + delta;
        }

        return translated;
    }

    private static bool GradientEquals(
        Vector2 actual,
        Vector2 expected,
        float tolerance)
    {
        return
            IsFinite(actual) &&
            Mathf.Abs(actual.x - expected.x) <= tolerance &&
            Mathf.Abs(actual.y - expected.y) <= tolerance;
    }

    private static bool IsFinite(Vector2 value)
    {
        return
            !float.IsNaN(value.x) &&
            !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y);
    }

    private static bool PositionsEqual(
        TerrainNodeElevationTopology a,
        TerrainNodeElevationTopology b)
    {
        if (a == null || b == null || a.VertexCount != b.VertexCount)
        {
            return false;
        }

        for (int index = 0; index < a.VertexCount; index++)
        {
            if (a.Vertices[index].PositionXZ != b.Vertices[index].PositionXZ)
            {
                return false;
            }
        }

        return true;
    }

    private static bool GradientsEqualByIndex(
        TerrainNodeElevationGradientData a,
        TerrainNodeElevationGradientData b,
        float tolerance)
    {
        if (a == null || b == null || a.GradientCount != b.GradientCount)
        {
            return false;
        }

        for (int index = 0; index < a.GradientCount; index++)
        {
            if (!GradientEquals(
                a.Gradients[index].GradientXZ,
                b.Gradients[index].GradientXZ,
                tolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool GradientDataExactlyEqual(
        TerrainNodeElevationGradientData a,
        TerrainNodeElevationGradientData b)
    {
        return GradientsEqualByIndex(a, b, 0f);
    }

    private static bool GradientFieldsEqualByPosition(
        TerrainNodeElevationTopology firstTopology,
        TerrainNodeElevationGradientData firstGradients,
        TerrainNodeElevationTopology secondTopology,
        TerrainNodeElevationGradientData secondGradients,
        float tolerance)
    {
        if (
            firstTopology == null ||
            secondTopology == null ||
            firstGradients == null ||
            secondGradients == null ||
            firstTopology.VertexCount != secondTopology.VertexCount)
        {
            return false;
        }

        for (int firstIndex = 0;
            firstIndex < firstTopology.VertexCount;
            firstIndex++)
        {
            Vector2 position =
                firstTopology.Vertices[firstIndex].PositionXZ;

            int secondIndex = -1;
            for (int candidate = 0;
                candidate < secondTopology.VertexCount;
                candidate++)
            {
                if (secondTopology.Vertices[candidate].PositionXZ == position)
                {
                    secondIndex = candidate;
                    break;
                }
            }

            if (
                secondIndex < 0 ||
                !GradientEquals(
                    firstGradients.Gradients[firstIndex].GradientXZ,
                    secondGradients.Gradients[secondIndex].GradientXZ,
                    tolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SetPrivateStableId(
        TerrainElevationNode node,
        string value)
    {
        FieldInfo field = typeof(TerrainElevationNode).GetField(
            "stableId",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null || node == null)
        {
            return false;
        }

        field.SetValue(node, value);
        return true;
    }

    private static bool SetPrivatePosition(
        TerrainElevationNode node,
        Vector2 value)
    {
        FieldInfo field = typeof(TerrainElevationNode).GetField(
            "positionXZ",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null || node == null)
        {
            return false;
        }

        field.SetValue(node, value);
        return true;
    }

    private static bool SetPrivateElevation(
        TerrainElevationNode node,
        float value)
    {
        FieldInfo field = typeof(TerrainElevationNode).GetField(
            "elevation",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null || node == null)
        {
            return false;
        }

        field.SetValue(node, value);
        return true;
    }

    private static string[] CaptureStableIds(TerrainNodeElevationSource source)
    {
        if (source == null)
        {
            return new string[0];
        }

        string[] values = new string[source.NodeCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = source.Nodes[index].StableId;
        }

        return values;
    }

    private static Vector2[] CapturePositions(TerrainNodeElevationSource source)
    {
        if (source == null)
        {
            return new Vector2[0];
        }

        Vector2[] values = new Vector2[source.NodeCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = source.Nodes[index].PositionXZ;
        }

        return values;
    }

    private static float[] CaptureElevations(TerrainNodeElevationSource source)
    {
        if (source == null)
        {
            return new float[0];
        }

        float[] values = new float[source.NodeCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = source.Nodes[index].Elevation;
        }

        return values;
    }

    private static bool ArraysEqual<T>(T[] a, T[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < a.Length; index++)
        {
            if (!comparer.Equals(a[index], b[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b)
    {
        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }

        for (int index = 0; index < a.Count; index++)
        {
            if (!string.Equals(a[index], b[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details)
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Outcome = outcome,
                Details = details ?? ""
            });
    }

    private static void WriteReport()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Regional Elevation Smooth Gradient Validation");
        builder.AppendLine("========================================================");
        builder.AppendLine();

        int passed = 0;
        int failed = 0;
        int blocked = 0;

        for (int index = 0; index < results.Count; index++)
        {
            ValidationResult result = results[index];
            string label = result.Outcome.ToString().ToUpperInvariant();

            builder.AppendLine(label + " - " + result.Name);
            builder.AppendLine("       " + result.Details);
            builder.AppendLine();

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
        }

        builder.AppendLine("--------------------------------------------------------");
        builder.AppendLine(passed + " passed");
        builder.AppendLine(failed + " failed");
        builder.AppendLine(blocked + " blocked");
        builder.AppendLine();

        bool success = failed == 0 && blocked == 0;
        builder.AppendLine(
            "Regional elevation Smooth Gradient validation: " +
            (success ? "PASSED" : "FAILED"));

        if (success)
        {
            Debug.Log(builder.ToString());
        }
        else
        {
            Debug.LogError(builder.ToString());
        }
    }
}
