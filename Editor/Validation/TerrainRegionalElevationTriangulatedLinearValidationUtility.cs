using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package I3 validation for CPU Triangulated Linear regional elevation.
 *
 * All interpolation fixtures are transient. The real authoring asset is read
 * only so validation can prove that pure CPU evaluation leaves persistent
 * terrain state and Package 7 selection unchanged.
 */
public static class TerrainRegionalElevationTriangulatedLinearValidationUtility
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

    public static void ValidateTriangulatedLinearCpu()
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
                "Current WorldSettings, TerrainAuthoringData, Package I1/I2 state, and Package 7 selection state are available.");

            ValidateCapabilityMatrix();
            ValidateTriangleAnalyticSamples();
            ValidateSharedEdgeAndLocality();
            ValidateHullExteriorPolicy();
            ValidateDegenerateTopologyPolicies();
            ValidateNegativeLargeAndInvalidSamples();
            ValidateStableIdIndependence();
            ValidateTopologyCacheReuse();
            ValidateIdwRegression();
            ValidateSmoothAndGpuBoundaries();
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
                "Initialize the committed authoring heightfield before running Package I3 validation.";
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

    private static void ValidateCapabilityMatrix()
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
            !TerrainNodeElevationInterpolationModeUtility.SupportsCpuEvaluation(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth) &&
            !TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        AddResult(
            "CPU/GPU interpolation capability matrix is explicit",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "IDW and Linear are CPU/GPU-ready after Package I4, Smooth remains unavailable, and serialized enum values are unchanged."
                : "Interpolation capability reporting did not match the current package contract.");
    }

    private static void ValidateTriangleAnalyticSamples()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 200f));

        bool corners =
            EvaluateEquals(source, new Vector2(0f, 0f), 0f, 0f) &&
            EvaluateEquals(source, new Vector2(10f, 0f), 100f, 0f) &&
            EvaluateEquals(source, new Vector2(0f, 10f), 200f, 0f);

        bool centroid =
            EvaluateEquals(
                source,
                new Vector2(10f / 3f, 10f / 3f),
                100f,
                0.0002f);

        bool edgeMidpoints =
            EvaluateEquals(source, new Vector2(5f, 0f), 50f, 0.0001f) &&
            EvaluateEquals(source, new Vector2(0f, 5f), 100f, 0.0001f) &&
            EvaluateEquals(source, new Vector2(5f, 5f), 150f, 0.0001f);

        /*
         * At (2,3): barycentric weights are 0.5, 0.2, 0.3.
         * 0*0.5 + 100*0.2 + 200*0.3 = 80.
         */
        bool interior =
            EvaluateEquals(source, new Vector2(2f, 3f), 80f, 0.0001f);

        /*
         * z=2 plane slice: h = 10*x + 20*z for this fixture.
         */
        bool planar =
            EvaluateEquals(source, new Vector2(1f, 2f), 50f, 0.0001f) &&
            EvaluateEquals(source, new Vector2(3f, 2f), 70f, 0.0001f) &&
            EvaluateEquals(source, new Vector2(5f, 2f), 90f, 0.0001f);

        bool passed = corners && centroid && edgeMidpoints && interior && planar;

        AddResult(
            "Triangulated Linear reproduces vertices and the analytic triangle plane",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Exact corners, centroid, edge midpoints, an asymmetric interior sample, and multiple coplanar samples match barycentric linear interpolation."
                : "One or more analytic triangle samples did not match the expected linear plane.");
    }

    private static void ValidateSharedEdgeAndLocality()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 200f),
            new NodeSpec(new Vector2(12f, 12f), 350f),
            new NodeSpec(new Vector2(30f, 5f), 900f));

        bool built =
            TerrainNodeElevationTriangulationUtility.TryBuild(
                source,
                out TerrainNodeElevationTopology topology,
                out _);

        bool sharedEdgeContinuous = false;
        bool locality = false;

        if (built)
        {
            int edgeIndex = FindInternalEdge(topology);
            if (edgeIndex >= 0 &&
                TryFindTrianglesUsingEdge(
                    topology,
                    topology.Edges[edgeIndex],
                    out int firstTriangle,
                    out int secondTriangle))
            {
                TerrainNodeElevationTopologyEdge edge = topology.Edges[edgeIndex];
                Vector2 a = topology.Vertices[edge.VertexA].PositionXZ;
                Vector2 b = topology.Vertices[edge.VertexB].PositionXZ;

                sharedEdgeContinuous = true;
                double[] samples = { 0.0, 0.25, 0.5, 0.75, 1.0 };
                for (int index = 0; index < samples.Length; index++)
                {
                    Vector2 point = Vector2.Lerp(a, b, (float)samples[index]);

                    bool firstOk =
                        TerrainNodeElevationLinearInterpolationUtility
                            .TryEvaluateTriangleLinear(
                                source,
                                topology,
                                firstTriangle,
                                point,
                                out float firstHeight,
                                out _);

                    bool secondOk =
                        TerrainNodeElevationLinearInterpolationUtility
                            .TryEvaluateTriangleLinear(
                                source,
                                topology,
                                secondTriangle,
                                point,
                                out float secondHeight,
                                out _);

                    sharedEdgeContinuous &=
                        firstOk && secondOk &&
                        Mathf.Abs(firstHeight - secondHeight) <= 0.0005f;
                }
            }

            if (topology.TriangleCount > 0)
            {
                TerrainNodeElevationTopologyTriangle triangle = topology.Triangles[0];
                Vector2 centroid =
                    (topology.Vertices[triangle.VertexA].PositionXZ +
                     topology.Vertices[triangle.VertexB].PositionXZ +
                     topology.Vertices[triangle.VertexC].PositionXZ) / 3f;

                bool beforeOk =
                    TerrainNodeElevationEvaluator.TryEvaluateHeight(
                        source,
                        centroid,
                        out float before,
                        out _);

                HashSet<int> triangleSourceIndices = new HashSet<int>
                {
                    topology.Vertices[triangle.VertexA].SourceNodeIndex,
                    topology.Vertices[triangle.VertexB].SourceNodeIndex,
                    topology.Vertices[triangle.VertexC].SourceNodeIndex
                };

                int distantSourceIndex = -1;
                for (int index = 0; index < source.NodeCount; index++)
                {
                    if (!triangleSourceIndices.Contains(index))
                    {
                        distantSourceIndex = index;
                        break;
                    }
                }

                if (distantSourceIndex >= 0)
                {
                    source.Nodes[distantSourceIndex].SetElevationInternal(123456f);

                    bool afterOk =
                        TerrainNodeElevationEvaluator.TryEvaluateHeight(
                            source,
                            centroid,
                            out float after,
                            out _);

                    locality =
                        beforeOk && afterOk &&
                        Mathf.Abs(before - after) <= 0.0001f;
                }
            }
        }

        bool passed = built && sharedEdgeContinuous && locality;

        AddResult(
            "Shared edges are C0-continuous and interior evaluation is local",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Both adjacent triangle planes agree along their shared edge, and changing a non-triangle node elevation does not affect an interior sample."
                : "Shared-edge continuity or local-triangle isolation failed.");
    }

    private static void ValidateHullExteriorPolicy()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 200f));

        bool edgeCenter =
            EvaluateEquals(source, new Vector2(5f, -10f), 50f, 0.0001f);

        bool farOutside =
            EvaluateEquals(source, new Vector2(5f, -100000f), 50f, 0.0001f);

        bool cornerClamp =
            EvaluateEquals(source, new Vector2(-100f, -100f), 0f, 0.0001f);

        bool built =
            TerrainNodeElevationTriangulationUtility.TryBuild(
                source,
                out TerrainNodeElevationTopology topology,
                out _);

        bool tieDeterministic = false;
        if (built)
        {
            Vector2 cornerPoint = new Vector2(-100f, -100f);

            bool first =
                TerrainNodeElevationLinearInterpolationUtility.TryEvaluateHullExterior(
                    source,
                    topology,
                    cornerPoint,
                    out float firstHeight,
                    out int firstEdge,
                    out _);

            bool second =
                TerrainNodeElevationLinearInterpolationUtility.TryEvaluateHullExterior(
                    source,
                    topology,
                    cornerPoint,
                    out float secondHeight,
                    out int secondEdge,
                    out _);

            int cornerVertex = FindVertexByPosition(topology, Vector2.zero);
            int lowestIncidentHullEdge = FindLowestIncidentHullEdge(topology, cornerVertex);

            tieDeterministic =
                first && second &&
                firstEdge == secondEdge &&
                firstEdge == lowestIncidentHullEdge &&
                Mathf.Abs(firstHeight) <= 0.0001f &&
                Mathf.Abs(secondHeight) <= 0.0001f;
        }

        bool passed = edgeCenter && farOutside && cornerClamp && tieDeterministic;

        AddResult(
            "Convex-hull exterior evaluation projects to the nearest finite hull segment deterministically",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Exterior midpoint/far samples extend the hull-edge profile, corner samples clamp to the endpoint, and equidistant edge ties choose the lowest hull-edge index."
                : "Exterior hull projection or deterministic tie behavior failed.");
    }

    private static void ValidateDegenerateTopologyPolicies()
    {
        TerrainNodeElevationSource empty = CreateLinearSource();
        bool emptyFails =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                empty,
                Vector2.zero,
                out _,
                out string emptyError) &&
            !string.IsNullOrEmpty(emptyError);

        TerrainNodeElevationSource single = CreateLinearSource(
            new NodeSpec(new Vector2(30f, -20f), 200f));
        bool singleConstant =
            EvaluateEquals(single, Vector2.zero, 200f, 0f) &&
            EvaluateEquals(single, new Vector2(100000f, -90000f), 200f, 0f);

        TerrainNodeElevationSource pair = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f));

        bool pairPolicy =
            EvaluateEquals(pair, new Vector2(-10f, 0f), 0f, 0.0001f) &&
            EvaluateEquals(pair, new Vector2(0f, 0f), 0f, 0f) &&
            EvaluateEquals(pair, new Vector2(2.5f, 0f), 25f, 0.0001f) &&
            EvaluateEquals(pair, new Vector2(5f, 100f), 50f, 0.0001f) &&
            EvaluateEquals(pair, new Vector2(7.5f, -20f), 75f, 0.0001f) &&
            EvaluateEquals(pair, new Vector2(10f, 0f), 100f, 0f) &&
            EvaluateEquals(pair, new Vector2(20f, 0f), 100f, 0.0001f);

        TerrainNodeElevationSource collinear = CreateLinearSource(
            new NodeSpec(new Vector2(30f, 0f), 160f),
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(20f, 0f), 40f),
            new NodeSpec(new Vector2(10f, 0f), 100f));

        bool collinearPolicy =
            EvaluateEquals(collinear, new Vector2(-10f, 0f), 0f, 0.0001f) &&
            EvaluateEquals(collinear, new Vector2(5f, 0f), 50f, 0.0001f) &&
            EvaluateEquals(collinear, new Vector2(15f, 0f), 70f, 0.0001f) &&
            EvaluateEquals(collinear, new Vector2(25f, 0f), 100f, 0.0001f) &&
            EvaluateEquals(collinear, new Vector2(40f, 0f), 160f, 0.0001f) &&
            EvaluateEquals(collinear, new Vector2(15f, 1000f), 70f, 0.0001f);

        bool passed = emptyFails && singleConstant && pairPolicy && collinearPolicy;

        AddResult(
            "Empty, single, two-node, and collinear layouts follow explicit Linear policies",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Empty fails explicitly; one node is constant; two nodes use clamped segment projection; collinear nodes form a projected piecewise-linear profile with endpoint clamping."
                : "One or more degenerate topology policies failed. " + emptyError);
    }

    private static void ValidateNegativeLargeAndInvalidSamples()
    {
        TerrainNodeElevationSource negative = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), -500f),
            new NodeSpec(new Vector2(10f, 0f), -100f),
            new NodeSpec(new Vector2(0f, 10f), 300f));

        bool negativeOk =
            EvaluateEquals(
                negative,
                new Vector2(10f / 3f, 10f / 3f),
                -100f,
                0.001f);

        TerrainNodeElevationSource large = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), -1e20f),
            new NodeSpec(new Vector2(10f, 0f), 1e20f),
            new NodeSpec(new Vector2(0f, 10f), 5e19f));

        bool largeOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                large,
                new Vector2(2f, 2f),
                out float largeHeight,
                out _) &&
            !float.IsNaN(largeHeight) &&
            !float.IsInfinity(largeHeight);

        bool invalidSamplesRejected =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                negative,
                new Vector2(float.NaN, 0f),
                out _,
                out _) &&
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                negative,
                new Vector2(0f, float.PositiveInfinity),
                out _,
                out _);

        bool passed = negativeOk && largeOk && invalidSamplesRejected;

        AddResult(
            "Linear CPU evaluation supports signed/large finite elevations and rejects non-finite samples",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Negative and large finite interpolation remained finite while NaN/Infinity sample coordinates failed safely."
                : "Signed/large elevation or non-finite sample handling failed.");
    }

    private static void ValidateStableIdIndependence()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(100f, 0f), 110f),
            new NodeSpec(new Vector2(0f, 100f), 210f));

        bool beforeOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                new Vector2(20f, 30f),
                out float before,
                out _);

        bool changed =
            SetPrivateStableId(
                source.Nodes[1],
                Guid.NewGuid().ToString("N"));

        bool afterOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                new Vector2(20f, 30f),
                out float after,
                out _);

        bool passed =
            beforeOk && changed && afterOk &&
            Mathf.Abs(before - after) <= 0.0001f;

        AddResult(
            "StableId is not part of Triangulated Linear numerical evaluation",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Changing only transient node identity leaves topology reuse and sampled height unchanged."
                : "StableId unexpectedly affected Linear CPU output.");
    }

    private static void ValidateTopologyCacheReuse()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 100f), 200f),
            new NodeSpec(new Vector2(100f, 120f), 300f));

        TerrainNodeElevationEvaluator.ClearTriangulatedLinearTopologyCache();

        int countBefore =
            TerrainNodeElevationEvaluator.TriangulatedLinearTopologyRebuildCount;

        bool firstOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                new Vector2(20f, 20f),
                out float firstHeight,
                out _);

        int countAfterFirst =
            TerrainNodeElevationEvaluator.TriangulatedLinearTopologyRebuildCount;

        source.Nodes[0].SetElevationInternal(500f);

        bool elevationOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                new Vector2(20f, 20f),
                out float afterElevation,
                out _);

        int countAfterElevation =
            TerrainNodeElevationEvaluator.TriangulatedLinearTopologyRebuildCount;

        source.Nodes[0].SetPositionXZInternal(new Vector2(-10f, 0f));

        bool positionOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                new Vector2(20f, 20f),
                out _,
                out _);

        int countAfterPosition =
            TerrainNodeElevationEvaluator.TriangulatedLinearTopologyRebuildCount;

        bool passed =
            firstOk && elevationOk && positionOk &&
            countAfterFirst == countBefore + 1 &&
            countAfterElevation == countAfterFirst &&
            countAfterPosition == countAfterFirst + 1 &&
            Mathf.Abs(firstHeight - afterElevation) > 0.001f;

        AddResult(
            "Linear CPU evaluation reuses position-only topology and rebuilds lazily after geometry edits",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Changing elevation changed the sampled height without rebuilding topology; changing XZ rebuilt topology exactly when evaluation next requested it."
                : "Evaluator topology cache reuse/rebuild behavior did not match Package I2/I3 semantics.");
    }

    private static void ValidateIdwRegression()
    {
        bool oneNode = EvaluateIdwEquals(
            CreateSource(new NodeSpec(new Vector2(10f, 20f), 42f)),
            new Vector2(-500f, 900f),
            42f,
            0f);

        bool midpoint = EvaluateIdwEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new Vector2(5f, 0f),
            50f,
            0.0001f);

        bool exactNode = EvaluateIdwEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 15f),
                new NodeSpec(new Vector2(100f, 0f), 200f)),
            Vector2.zero,
            15f,
            0f);

        bool coincident = EvaluateIdwEquals(
            CreateSource(
                new NodeSpec(new Vector2(25f, 25f), 20f),
                new NodeSpec(new Vector2(25f, 25f), 40f),
                new NodeSpec(new Vector2(100f, 25f), 1000f)),
            new Vector2(25f, 25f),
            30f,
            0.0001f);

        bool arbitrary = EvaluateIdwEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new Vector2(2f, 0f),
            5.882353f,
            0.0005f);

        bool passed = oneNode && midpoint && exactNode && coincident && arbitrary;

        AddResult(
            "Existing IDW CPU mathematics remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "One-node, midpoint, exact-node, coincident averaging, and arbitrary p=2 IDW regression samples remain unchanged."
                : "At least one IDW regression sample changed in Package I3.");
    }

    private static void ValidateSmoothAndGpuBoundaries()
    {
        TerrainNodeElevationSource linear = CreateLinearSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 100f), 50f));

        bool linearCpu =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                linear,
                new Vector2(20f, 20f),
                out float linearHeight,
                out _) &&
            Mathf.Abs(linearHeight - 30f) <= 0.0001f;

        TerrainAuthoringData linearData = CreateDataFromSource(linear);

        bool linearGpuSupported =
            TerrainRegionalElevationCompositionUtility.TryResolveNodeSource(
                linearData,
                out TerrainNodeElevationSource resolvedLinearSource,
                out bool regionalCompositionRequired,
                out string gpuError) &&
            ReferenceEquals(resolvedLinearSource, linear) &&
            regionalCompositionRequired &&
            string.IsNullOrEmpty(gpuError);

        TerrainNodeElevationSource smooth = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 100f), 50f));
        smooth.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        bool smoothRejected =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                smooth,
                new Vector2(20f, 20f),
                out _,
                out string smoothError) &&
            smoothError.Contains("CPU");

        bool passed = linearCpu && linearGpuSupported && smoothRejected;

        AddResult(
            "Linear CPU semantics remain authoritative while GPU composition is now available",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Triangulated Linear still evaluates through the Package I3 CPU path and is now accepted by Package I4 GPU composition, while Smooth remains unsupported."
                : gpuError + " " + smoothError);

        ClearFixture(linearData);
    }

    private static void ValidateRealStateUnchanged()
    {
        List<string> selectedAfter = new List<string>();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            selectedAfter);

        string primaryAfter =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);

        TerrainNodeElevationSource realNodeSource =
            realAuthoringData.RegionalElevationSource as TerrainNodeElevationSource;

        TerrainNodeElevationInterpolationMode interpolationAfter =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;

        bool passed =
            realAuthoringData.authoringRevision == realRevisionBefore &&
            ReferenceEquals(realAuthoringData.RegionalElevationSource, realRegionalBefore) &&
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings) == realCommittedBefore &&
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData) == realOverallBefore &&
            interpolationAfter == realInterpolationBefore &&
            ArraysEqual(realStableIdsBefore, CaptureStableIds(realNodeSource)) &&
            ArraysEqual(realPositionsBefore, CapturePositions(realNodeSource)) &&
            ArraysEqual(realElevationsBefore, CaptureElevations(realNodeSource)) &&
            realPrimaryBefore == primaryAfter &&
            SequenceEqual(realSelectedIdsBefore, selectedAfter);

        AddResult(
            "Real authoring and Package 7 selection state remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Pure CPU Linear validation changed no real source, revision, signatures, node data, interpolation mode, or editor selection state."
                : "Real WorldMeshes authoring or selection state changed during Package I3 validation.");
    }

    private static TerrainNodeElevationSource CreateSource(params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source = new TerrainNodeElevationSource();

        if (nodes != null)
        {
            for (int index = 0; index < nodes.Length; index++)
            {
                TerrainElevationNode node = new TerrainElevationNode();
                node.SetPositionXZInternal(nodes[index].Position);
                node.SetElevationInternal(nodes[index].Elevation);
                source.AddNodeInternal(node);
            }
        }

        source.RepairNodeStableIds();
        return source;
    }

    private static TerrainNodeElevationSource CreateLinearSource(params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source = CreateSource(nodes);
        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);
        return source;
    }

    private static TerrainAuthoringData CreateDataFromSource(
        TerrainNodeElevationSource source)
    {
        TerrainAuthoringData data =
            ScriptableObject.CreateInstance<TerrainAuthoringData>();
        data.sourceMode = TerrainHeightSourceMode.Flat;
        data.SetRegionalElevationSourceInternal(source);
        return data;
    }

    private static bool EvaluateEquals(
        TerrainNodeElevationSource source,
        Vector2 sample,
        float expected,
        float tolerance)
    {
        return
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                sample,
                out float actual,
                out _) &&
            Mathf.Abs(actual - expected) <= tolerance;
    }

    private static bool EvaluateIdwEquals(
        TerrainNodeElevationSource source,
        Vector2 sample,
        float expected,
        float tolerance)
    {
        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.InverseDistanceWeighted);
        return EvaluateEquals(source, sample, expected, tolerance);
    }

    private static int FindInternalEdge(TerrainNodeElevationTopology topology)
    {
        if (topology == null)
        {
            return -1;
        }

        for (int index = 0; index < topology.EdgeCount; index++)
        {
            if (topology.Edges[index].TriangleReferenceCount == 2)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryFindTrianglesUsingEdge(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationTopologyEdge edge,
        out int firstTriangle,
        out int secondTriangle)
    {
        firstTriangle = -1;
        secondTriangle = -1;

        for (int index = 0; index < topology.TriangleCount; index++)
        {
            if (!TriangleContainsEdge(topology.Triangles[index], edge))
            {
                continue;
            }

            if (firstTriangle < 0)
            {
                firstTriangle = index;
            }
            else
            {
                secondTriangle = index;
                break;
            }
        }

        return firstTriangle >= 0 && secondTriangle >= 0;
    }

    private static bool TriangleContainsEdge(
        TerrainNodeElevationTopologyTriangle triangle,
        TerrainNodeElevationTopologyEdge edge)
    {
        return
            HasPair(triangle.VertexA, triangle.VertexB, edge.VertexA, edge.VertexB) ||
            HasPair(triangle.VertexB, triangle.VertexC, edge.VertexA, edge.VertexB) ||
            HasPair(triangle.VertexC, triangle.VertexA, edge.VertexA, edge.VertexB);
    }

    private static bool HasPair(int a, int b, int x, int y)
    {
        return (a == x && b == y) || (a == y && b == x);
    }

    private static int FindVertexByPosition(
        TerrainNodeElevationTopology topology,
        Vector2 position)
    {
        for (int index = 0; index < topology.VertexCount; index++)
        {
            if (topology.Vertices[index].PositionXZ == position)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindLowestIncidentHullEdge(
        TerrainNodeElevationTopology topology,
        int vertexIndex)
    {
        for (int index = 0; index < topology.HullEdgeCount; index++)
        {
            TerrainNodeElevationTopologyEdge edge = topology.HullEdges[index];
            if (edge.VertexA == vertexIndex || edge.VertexB == vertexIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool SetPrivateStableId(
        TerrainElevationNode node,
        string stableId)
    {
        FieldInfo field = typeof(TerrainElevationNode).GetField(
            "stableId",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null || node == null)
        {
            return false;
        }

        field.SetValue(node, stableId);
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

    private static void ClearFixture(TerrainAuthoringData data)
    {
        if (data == null)
        {
            return;
        }

        TerrainRegionalElevationChangeTracker.Forget(data);
        Undo.ClearUndo(data);
        UnityEngine.Object.DestroyImmediate(data);
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
        builder.AppendLine("WorldMeshes Regional Elevation Triangulated Linear CPU Validation");
        builder.AppendLine("================================================================");
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

        builder.AppendLine("----------------------------------------------------------------");
        builder.AppendLine(passed + " passed");
        builder.AppendLine(failed + " failed");
        builder.AppendLine(blocked + " blocked");
        builder.AppendLine();

        bool success = failed == 0 && blocked == 0;
        builder.AppendLine(
            "Regional elevation Triangulated Linear CPU validation: " +
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
