using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package I6 validation for the authoritative Triangulated Smooth CPU surface.
 *
 * Fixtures are transient. Production I2 topology, I5 gradients, I6 reduced-HCT
 * patch construction/cache, and I6 CPU evaluation are exercised directly. The
 * real project asset is observed only to prove that validation is read-only and
 * preserves Package 7 selection state.
 */
public static class TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility
{
    private const float HeightTolerance = 0.003f;
    private const float GradientTolerance = 0.004f;
    private const float SeamHeightTolerance = 0.006f;
    private const float SeamGradientTolerance = 0.01f;
    private const float LargeHeightTolerance = 0.2f;

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

    public static void ValidateTriangulatedSmoothCpu()
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
                "Current WorldSettings, TerrainAuthoringData, I2 topology, I5 gradients, I6 Smooth code, and Package 7 selection state are available.");

            ValidateCapabilityMatrix();
            ValidateExactNodeHeightsAndGradients();
            ValidateFlatAndPlanarPrecision();
            ValidateInternalHctSpokes();
            ValidateOriginalSharedEdges();
            ValidateHillAndValleyCurvature();
            ValidateIrregularDeterminismAndSourceOrder();
            ValidateLocality();
            ValidateDegeneratePolicies();
            ValidateHullExteriorAndBoundary();
            ValidateSignedAndLargeElevations();
            ValidateCacheDependencies();
            ValidateStableIdAndModeCacheIndependence();
            ValidateInvalidDataSafety();
            ValidateIdwAndLinearRegression();
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
            errorMessage =
                "WorldSettings or TerrainAuthoringData could not be loaded.";
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
                "Initialize the committed authoring heightfield before running Package I6 validation.";
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
            TerrainNodeElevationInterpolationModeUtility.SupportsCpuEvaluation(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth) &&
            TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth) &&
            TerrainNodeElevationInterpolationModeUtility.IsImplemented(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        AddResult(
            "Interpolation capability matrix exposes production Smooth CPU/GPU support",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "IDW, Linear, and Smooth are CPU/GPU-ready; I6 remains the authoritative Smooth CPU reference after I7."
                : "Interpolation capability reporting does not match the post-I7 contract.");
    }

    private static void ValidateExactNodeHeightsAndGradients()
    {
        TerrainNodeElevationSource source = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(12f, 1f), 80f),
            new NodeSpec(new Vector2(11f, 13f), 140f),
            new NodeSpec(new Vector2(-2f, 10f), 50f),
            new NodeSpec(new Vector2(5f, 6f), 120f));

        bool built = TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out TerrainNodeElevationSmoothPatchData patches,
            out string buildError);

        bool passed = built;

        if (built)
        {
            for (int vertexIndex = 0;
                vertexIndex < topology.VertexCount && passed;
                vertexIndex++)
            {
                TerrainNodeElevationTopologyVertex vertex =
                    topology.Vertices[vertexIndex];

                float expectedHeight =
                    source.Nodes[vertex.SourceNodeIndex].Elevation;

                Vector2 expectedGradient =
                    gradients.Gradients[vertexIndex].GradientXZ;

                passed =
                    TerrainNodeElevationSmoothInterpolationUtility
                        .TryEvaluateHeightAndGradient(
                            source,
                            topology,
                            gradients,
                            patches,
                            vertex.PositionXZ,
                            out float actualHeight,
                            out Vector2 actualGradient,
                            out _) &&
                    actualHeight == expectedHeight &&
                    GradientMatches(
                        actualGradient,
                        expectedGradient,
                        0f);
            }
        }

        AddResult(
            "Exact source node heights and I5 gradients are reproduced",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Exact-node short-circuiting returned persistent heights and the corresponding I5 world gradient without cubic round-trip error."
                : buildError);
    }

    private static void ValidateFlatAndPlanarPrecision()
    {
        Vector2[] regular =
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(20f, 0f),
            new Vector2(0f, 10f),
            new Vector2(10f, 10f),
            new Vector2(20f, 10f),
            new Vector2(0f, 20f),
            new Vector2(10f, 20f),
            new Vector2(20f, 20f)
        };

        Vector2[] irregular =
        {
            new Vector2(0f, 0f),
            new Vector2(13f, 2f),
            new Vector2(27f, 7f),
            new Vector2(4f, 16f),
            new Vector2(18f, 14f),
            new Vector2(31f, 21f),
            new Vector2(8f, 29f),
            new Vector2(22f, 33f)
        };

        bool flat = ValidatePlane(
            regular,
            0f,
            0f,
            100f,
            HeightTolerance,
            GradientTolerance);

        bool xSlope = ValidatePlane(
            regular,
            2f,
            0f,
            50f,
            HeightTolerance,
            GradientTolerance);

        bool zSlope = ValidatePlane(
            regular,
            0f,
            -1.5f,
            20f,
            HeightTolerance,
            GradientTolerance);

        bool diagonal = ValidatePlane(
            irregular,
            2f,
            -3f,
            100f,
            HeightTolerance,
            GradientTolerance);

        bool passed =
            flat && xSlope && zSlope && diagonal;

        AddResult(
            "Flat and affine planar fields remain exact within float patch tolerance",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Regular and irregular layouts preserved constant fields plus X, Z, and diagonal planes without introducing curvature."
                : "At least one flat/planar Smooth fixture deviated from its analytic plane.");
    }

    private static void ValidateInternalHctSpokes()
    {
        TerrainNodeElevationSource source = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 5f),
            new NodeSpec(new Vector2(14f, 1f), 120f),
            new NodeSpec(new Vector2(2f, 12f), 60f));

        bool built = TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out _,
            out TerrainNodeElevationSmoothPatchData patches,
            out string buildError);

        bool passed =
            built &&
            topology.Kind == TerrainNodeElevationTopologyKind.Triangulated &&
            topology.TriangleCount == 1 &&
            patches.PatchCount == 1;

        if (passed)
        {
            TerrainNodeElevationTopologyTriangle triangle =
                topology.Triangles[0];

            Vector2[] vertices =
            {
                topology.Vertices[triangle.VertexA].PositionXZ,
                topology.Vertices[triangle.VertexB].PositionXZ,
                topology.Vertices[triangle.VertexC].PositionXZ
            };

            Vector2 centroid =
                (vertices[0] + vertices[1] + vertices[2]) / 3f;

            int[,] seams =
            {
                { 0, 0, 2 },
                { 1, 0, 1 },
                { 2, 1, 2 }
            };

            float[] parameters = { 0.2f, 0.5f, 0.8f };

            for (int seamIndex = 0;
                seamIndex < seams.GetLength(0) && passed;
                seamIndex++)
            {
                for (int parameterIndex = 0;
                    parameterIndex < parameters.Length && passed;
                    parameterIndex++)
                {
                    Vector2 sample =
                        Vector2.Lerp(
                            vertices[seams[seamIndex, 0]],
                            centroid,
                            parameters[parameterIndex]);

                    passed =
                        CompareDirectSubpatches(
                            topology,
                            patches,
                            0,
                            seams[seamIndex, 1],
                            0,
                            seams[seamIndex, 2],
                            sample,
                            SeamHeightTolerance,
                            SeamGradientTolerance);
                }
            }
        }

        AddResult(
            "The three HCT subpatches are C1 across all centroid spokes",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Independent production subpatch evaluations matched in both height and analytic world gradient along A-G, B-G, and C-G."
                : buildError);
    }

    private static void ValidateOriginalSharedEdges()
    {
        TerrainNodeElevationSource source = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(12f, 1f), 110f),
            new NodeSpec(new Vector2(11f, 12f), 210f),
            new NodeSpec(new Vector2(-1f, 11f), 70f),
            new NodeSpec(new Vector2(5f, 6f), 150f));

        bool built = TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out _,
            out TerrainNodeElevationSmoothPatchData patches,
            out string buildError);

        bool passed = built;
        bool testedEdge = false;

        if (built)
        {
            for (int edgeIndex = 0;
                edgeIndex < topology.EdgeCount && !testedEdge;
                edgeIndex++)
            {
                TerrainNodeElevationTopologyEdge edge =
                    topology.Edges[edgeIndex];

                if (edge.TriangleReferenceCount != 2)
                {
                    continue;
                }

                if (!TryFindTrianglesForEdge(
                    topology,
                    edge,
                    out int firstTriangle,
                    out int secondTriangle,
                    out int firstSubpatch,
                    out int secondSubpatch))
                {
                    passed = false;
                    break;
                }

                Vector2 a =
                    topology.Vertices[edge.VertexA].PositionXZ;

                Vector2 b =
                    topology.Vertices[edge.VertexB].PositionXZ;

                float[] parameters = { 0.2f, 0.5f, 0.8f };

                for (int parameterIndex = 0;
                    parameterIndex < parameters.Length && passed;
                    parameterIndex++)
                {
                    Vector2 sample =
                        Vector2.Lerp(a, b, parameters[parameterIndex]);

                    passed =
                        CompareDirectSubpatches(
                            topology,
                            patches,
                            firstTriangle,
                            firstSubpatch,
                            secondTriangle,
                            secondSubpatch,
                            sample,
                            SeamHeightTolerance,
                            SeamGradientTolerance);
                }

                testedEdge = true;
            }
        }

        passed = passed && testedEdge;

        AddResult(
            "Adjacent I2 macro-triangles are C1 across a shared Delaunay edge",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Canonical edge constraints produced matching boundary height and analytic world gradient from both incident macro-triangles."
                : buildError);
    }

    private static void ValidateHillAndValleyCurvature()
    {
        TerrainNodeElevationSource hill = CreateGridField(
            new float[,]
            {
                { 50f, 100f, 50f },
                { 100f, 200f, 100f },
                { 50f, 100f, 50f }
            });

        TerrainNodeElevationSource valley = CreateGridField(
            new float[,]
            {
                { 200f, 150f, 200f },
                { 150f, 50f, 150f },
                { 200f, 150f, 200f }
            });

        bool hillOk = ValidateCurvedCenterField(
            hill,
            new Vector2(10f, 10f),
            new Vector2(15f, 10f));

        bool valleyOk = ValidateCurvedCenterField(
            valley,
            new Vector2(10f, 10f),
            new Vector2(15f, 10f));

        AddResult(
            "Broad hill and valley fixtures form finite curved surfaces",
            hillOk && valleyOk
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            hillOk && valleyOk
                ? "Center gradients remained near zero and representative center-to-neighbor samples differed from piecewise Linear while staying finite and bounded."
                : "The broad hill or valley did not exhibit the expected Smooth curvature behavior.");
    }

    private static void ValidateIrregularDeterminismAndSourceOrder()
    {
        NodeSpec[] nodes =
        {
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(13f, 2f), 80f),
            new NodeSpec(new Vector2(29f, 7f), 135f),
            new NodeSpec(new Vector2(3f, 18f), 55f),
            new NodeSpec(new Vector2(17f, 15f), 160f),
            new NodeSpec(new Vector2(32f, 23f), 210f),
            new NodeSpec(new Vector2(8f, 31f), -20f),
            new NodeSpec(new Vector2(23f, 35f), 95f)
        };

        TerrainNodeElevationSource first =
            CreateSmoothSource(nodes);

        TerrainNodeElevationSource reordered =
            CreateSmoothSource(
                nodes[5],
                nodes[2],
                nodes[7],
                nodes[0],
                nodes[4],
                nodes[1],
                nodes[6],
                nodes[3]);

        Vector2[] samples =
        {
            new Vector2(8f, 7f),
            new Vector2(15f, 12f),
            new Vector2(20f, 20f),
            new Vector2(11f, 25f),
            new Vector2(26f, 24f)
        };

        bool firstPass = EvaluateSamples(first, samples, out float[] firstHeights);
        bool repeatPass = EvaluateSamples(first, samples, out float[] repeatHeights);
        bool reorderPass = EvaluateSamples(reordered, samples, out float[] reorderedHeights);

        bool passed =
            firstPass &&
            repeatPass &&
            reorderPass &&
            ArraysApproximatelyEqual(firstHeights, repeatHeights, 0f) &&
            ArraysApproximatelyEqual(firstHeights, reorderedHeights, HeightTolerance);

        AddResult(
            "Irregular free-position Smooth evaluation is deterministic and source-order independent",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Repeated samples were bit-stable for one source and a geometrically equivalent persistent reorder produced the same world-space surface within float tolerance."
                : "Irregular Smooth evaluation changed across repetition or equivalent source ordering.");
    }

    private static void ValidateLocality()
    {
        List<NodeSpec> specs = new List<NodeSpec>();

        for (int z = 0; z < 4; z++)
        {
            for (int x = 0; x < 4; x++)
            {
                specs.Add(
                    new NodeSpec(
                        new Vector2(x * 10f, z * 10f),
                        x * 17f + z * 31f + ((x + z) % 3) * 9f));
            }
        }

        TerrainNodeElevationSource source =
            CreateSmoothSource(specs.ToArray());

        bool built = TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out _,
            out _,
            out string buildError);

        bool passed = false;

        if (built)
        {
            for (int triangleIndex = 0;
                triangleIndex < topology.TriangleCount && !passed;
                triangleIndex++)
            {
                TerrainNodeElevationTopologyTriangle triangle =
                    topology.Triangles[triangleIndex];

                HashSet<int> dependencyVertices = new HashSet<int>
                {
                    triangle.VertexA,
                    triangle.VertexB,
                    triangle.VertexC
                };

                int[] triangleVertices =
                {
                    triangle.VertexA,
                    triangle.VertexB,
                    triangle.VertexC
                };

                for (int localIndex = 0; localIndex < triangleVertices.Length; localIndex++)
                {
                    IReadOnlyList<int> neighbors =
                        topology.GetVertexNeighbors(triangleVertices[localIndex]);

                    for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                    {
                        dependencyVertices.Add(neighbors[neighborIndex]);
                    }
                }

                int distantVertex = -1;

                for (int candidate = 0;
                    candidate < topology.VertexCount;
                    candidate++)
                {
                    if (!dependencyVertices.Contains(candidate))
                    {
                        distantVertex = candidate;
                        break;
                    }
                }

                if (distantVertex < 0)
                {
                    continue;
                }

                Vector2 sample =
                    (
                        topology.Vertices[triangle.VertexA].PositionXZ +
                        topology.Vertices[triangle.VertexB].PositionXZ +
                        topology.Vertices[triangle.VertexC].PositionXZ
                    ) / 3f;

                if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                    source,
                    sample,
                    out float before,
                    out _))
                {
                    continue;
                }

                int sourceNodeIndex =
                    topology.Vertices[distantVertex].SourceNodeIndex;

                TerrainElevationNode distantNode =
                    source.Nodes[sourceNodeIndex];

                distantNode.SetElevationInternal(
                    distantNode.Elevation + 1000f);

                if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                    source,
                    sample,
                    out float after,
                    out _))
                {
                    continue;
                }

                passed =
                    Mathf.Abs(before - after) <= HeightTolerance;
            }
        }

        AddResult(
            "Smooth patch influence remains local to triangle vertices and their I5 one-rings",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Changing a node outside the sampled triangle's vertex one-ring dependencies left the local Smooth result unchanged."
                : buildError);
    }

    private static void ValidateDegeneratePolicies()
    {
        TerrainNodeElevationSource empty =
            CreateSmoothSource();

        bool emptyRejected =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                empty,
                Vector2.zero,
                out _,
                out _);

        TerrainNodeElevationSource single =
            CreateSmoothSource(
                new NodeSpec(new Vector2(5f, 7f), -25f));

        bool singleOk =
            EvaluateHeight(single, new Vector2(-100f, 300f), -25f, 0f) &&
            EvaluateHeight(single, new Vector2(5f, 7f), -25f, 0f);

        TerrainNodeElevationSource two =
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f));

        bool twoOk =
            EvaluateHeight(two, new Vector2(5f, 0f), 50f, HeightTolerance) &&
            EvaluateHeight(two, new Vector2(5f, 200f), 50f, HeightTolerance) &&
            EvaluateHeight(two, new Vector2(-50f, 0f), 0f, 0f) &&
            EvaluateHeight(two, new Vector2(50f, 0f), 100f, 0f);

        TerrainNodeElevationSource collinear =
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 80f),
                new NodeSpec(new Vector2(20f, 0f), 20f),
                new NodeSpec(new Vector2(30f, 0f), 120f));

        bool collinearOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                collinear,
                new Vector2(15f, 0f),
                out float center,
                out _) &&
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                collinear,
                new Vector2(15f, 100f),
                out float perpendicular,
                out _) &&
            IsFinite(center) &&
            Mathf.Abs(center - perpendicular) <= HeightTolerance &&
            EvaluateHeight(collinear, new Vector2(-20f, 0f), 0f, 0f) &&
            EvaluateHeight(collinear, new Vector2(60f, 0f), 120f, 0f);

        bool passed =
            emptyRejected &&
            singleOk &&
            twoOk &&
            collinearOk;

        AddResult(
            "Empty, single, two-node, and collinear Smooth policies are explicit",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Empty fails; one node is constant; two-node and collinear layouts use projected cubic Hermite with perpendicular invariance and endpoint clamping."
                : "At least one degenerate Smooth topology policy failed.");
    }

    private static void ValidateHullExteriorAndBoundary()
    {
        TerrainNodeElevationSource source = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(12f, 0f), 100f),
            new NodeSpec(new Vector2(10f, 10f), 170f),
            new NodeSpec(new Vector2(0f, 12f), 40f),
            new NodeSpec(new Vector2(5f, 5f), 130f));

        bool built = TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out TerrainNodeElevationSmoothPatchData patches,
            out string buildError);

        bool passed =
            built &&
            topology.HullEdgeCount > 0;

        if (passed)
        {
            TerrainNodeElevationTopologyEdge edge =
                topology.HullEdges[0];

            if (!TryFindSingleTriangleForHullEdge(
                topology,
                edge,
                out int triangleIndex,
                out int subpatchIndex,
                out int thirdVertexIndex))
            {
                passed = false;
            }
            else
            {
                Vector2 a = topology.Vertices[edge.VertexA].PositionXZ;
                Vector2 b = topology.Vertices[edge.VertexB].PositionXZ;
                Vector2 midpoint = (a + b) * 0.5f;
                Vector2 edgeVector = b - a;
                Vector2 normal =
                    new Vector2(-edgeVector.y, edgeVector.x).normalized;
                Vector2 third = topology.Vertices[thirdVertexIndex].PositionXZ;

                if (Vector2.Dot(third - midpoint, normal) > 0f)
                {
                    normal = -normal;
                }

                Vector2 outside = midpoint + normal * 50f;

                bool exteriorOk =
                    TerrainNodeElevationSmoothInterpolationUtility
                        .TryEvaluateHullExteriorForValidation(
                            source,
                            topology,
                            gradients,
                            outside,
                            out float exteriorHeight,
                            out _,
                            out int selectedEdge,
                            out _) &&
                    selectedEdge >= 0 &&
                    IsFinite(exteriorHeight);

                bool insideBoundaryOk =
                    TerrainNodeElevationSmoothInterpolationUtility
                        .TryEvaluateTriangleSubpatchHeightAndGradient(
                            topology,
                            patches,
                            triangleIndex,
                            subpatchIndex,
                            midpoint,
                            out float insideHeight,
                            out Vector2 insideGradient,
                            out _);

                bool edgeBoundaryOk =
                    TerrainNodeElevationSmoothInterpolationUtility
                        .TryEvaluateHullExteriorForValidation(
                            source,
                            topology,
                            gradients,
                            midpoint,
                            out float edgeHeight,
                            out Vector2 edgeGradient,
                            out int boundaryEdge,
                            out _);

                Vector2 tangent = edgeVector.normalized;

                passed =
                    exteriorOk &&
                    insideBoundaryOk &&
                    edgeBoundaryOk &&
                    boundaryEdge == 0 &&
                    Mathf.Abs(insideHeight - edgeHeight) <= SeamHeightTolerance &&
                    Mathf.Abs(
                        Vector2.Dot(insideGradient, tangent) -
                        Vector2.Dot(edgeGradient, tangent)) <=
                        SeamGradientTolerance;
            }
        }

        AddResult(
            "Convex-hull exterior uses nearest finite edge Hermite and matches the HCT boundary",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Exterior evaluation projected to a canonical hull edge without HCT extrapolation; boundary height and tangential derivative matched the interior patch."
                : buildError);
    }

    private static void ValidateSignedAndLargeElevations()
    {
        TerrainNodeElevationSource negativePlane = CreatePlaneSource(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(20f, 0f),
                new Vector2(0f, 20f),
                new Vector2(20f, 20f),
                new Vector2(8f, 7f)
            },
            1.5f,
            -2f,
            -500f);

        TerrainNodeElevationSource largePlane = CreatePlaneSource(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(100f, 5f),
                new Vector2(7f, 90f),
                new Vector2(110f, 100f),
                new Vector2(45f, 38f)
            },
            2f,
            -1f,
            1000000f);

        bool negativeOk =
            EvaluateHeight(
                negativePlane,
                new Vector2(7f, 8f),
                1.5f * 7f - 2f * 8f - 500f,
                HeightTolerance);

        bool largeOk =
            EvaluateHeight(
                largePlane,
                new Vector2(45f, 38f),
                1000000f + 2f * 45f - 38f,
                LargeHeightTolerance) &&
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                largePlane,
                new Vector2(35f, 30f),
                out float largeInterior,
                out _) &&
            IsFinite(largeInterior);

        AddResult(
            "Negative and large finite elevations remain numerically safe",
            negativeOk && largeOk
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            negativeOk && largeOk
                ? "Signed fields and approximately one-million-unit fields produced finite Smooth coefficients and evaluations within documented float tolerance."
                : "Signed or large finite Smooth evaluation failed or became non-finite.");
    }

    private static void ValidateCacheDependencies()
    {
        TerrainNodeElevationSource source = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(20f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 20f), 50f),
            new NodeSpec(new Vector2(20f, 20f), 150f),
            new NodeSpec(new Vector2(10f, 10f), 90f));

        TerrainNodeElevationEvaluator.ClearTriangulatedSmoothCaches();

        int topologyBefore =
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount;
        int gradientBefore =
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount;
        int patchBefore =
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount;

        bool firstOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(7f, 7f),
            out float firstHeight,
            out _);

        int topologyAfterFirst =
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount;
        int gradientAfterFirst =
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount;
        int patchAfterFirst =
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount;

        bool repeatOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(8f, 8f),
            out _,
            out _);

        bool repeatReused =
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount == topologyAfterFirst &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount == gradientAfterFirst &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount == patchAfterFirst;

        source.Nodes[4].SetElevationInternal(source.Nodes[4].Elevation + 25f);

        bool elevationOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(7f, 7f),
            out float elevationHeight,
            out _);

        int topologyAfterElevation =
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount;
        int gradientAfterElevation =
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount;
        int patchAfterElevation =
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount;

        source.Nodes[4].SetPositionXZInternal(new Vector2(11f, 9f));

        bool positionOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(7f, 7f),
            out _,
            out _);

        int topologyAfterPosition =
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount;
        int gradientAfterPosition =
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount;
        int patchAfterPosition =
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount;

        TerrainElevationNode added = new TerrainElevationNode();
        added.SetPositionXZInternal(new Vector2(30f, 10f));
        added.SetElevationInternal(175f);
        source.AddNodeInternal(added);
        source.RepairNodeStableIds();

        bool membershipOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(7f, 7f),
            out _,
            out _);

        bool passed =
            firstOk &&
            repeatOk &&
            elevationOk &&
            positionOk &&
            membershipOk &&
            topologyAfterFirst == topologyBefore + 1 &&
            gradientAfterFirst == gradientBefore + 1 &&
            patchAfterFirst == patchBefore + 1 &&
            repeatReused &&
            topologyAfterElevation == topologyAfterFirst &&
            gradientAfterElevation == gradientAfterFirst + 1 &&
            patchAfterElevation == patchAfterFirst + 1 &&
            topologyAfterPosition == topologyAfterElevation + 1 &&
            gradientAfterPosition == gradientAfterElevation + 1 &&
            patchAfterPosition == patchAfterElevation + 1 &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount == topologyAfterPosition + 1 &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount == gradientAfterPosition + 1 &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount == patchAfterPosition + 1 &&
            Mathf.Abs(firstHeight - elevationHeight) > 0.0001f;

        AddResult(
            "Smooth topology, gradient, and patch caches rebuild only for their numerical dependencies",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Repeated sampling reused all layers; elevation rebuilt gradients/patches only; position and membership changes rebuilt topology, gradients, and patches lazily."
                : "Package I6 derived cache rebuild counts did not match the dependency chain.");
    }

    private static void ValidateStableIdAndModeCacheIndependence()
    {
        TerrainNodeElevationSource source = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 50f));

        TerrainNodeElevationEvaluator.ClearTriangulatedSmoothCaches();

        bool firstOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(2f, 2f),
            out float firstHeight,
            out _);

        int topologyCount =
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount;
        int gradientCount =
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount;
        int patchCount =
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount;

        bool stableIdChanged =
            SetPrivateStableId(
                source.Nodes[0],
                Guid.NewGuid().ToString("N"));

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.InverseDistanceWeighted);
        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        bool secondOk = TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            new Vector2(2f, 2f),
            out float secondHeight,
            out _);

        bool passed =
            firstOk &&
            stableIdChanged &&
            secondOk &&
            firstHeight == secondHeight &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothTopologyRebuildCount == topologyCount &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothGradientRebuildCount == gradientCount &&
            TerrainNodeElevationEvaluator.TriangulatedSmoothPatchRebuildCount == patchCount;

        AddResult(
            "StableId and interpolation mode are not Smooth derived-data dependencies",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Identity-only and mode-only changes reused topology, gradients, and HCT patches with identical numerical output."
                : "A non-numerical source change unexpectedly invalidated Smooth derived data.");
    }

    private static void ValidateInvalidDataSafety()
    {
        bool nullRejected =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                null,
                Vector2.zero,
                out _,
                out _);

        TerrainNodeElevationSource valid = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 50f));

        bool invalidSampleRejected =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                valid,
                new Vector2(float.NaN, 0f),
                out _,
                out _);

        bool built = TryBuildDerived(
            valid,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out _,
            out _);

        TerrainNodeElevationSmoothPatchData malformed =
            new TerrainNodeElevationSmoothPatchData(
                new TerrainNodeElevationSmoothTrianglePatch[0]);

        bool malformedRejected =
            built &&
            !TerrainNodeElevationSmoothInterpolationUtility
                .TryEvaluateHeightAndGradient(
                    valid,
                    topology,
                    gradients,
                    malformed,
                    new Vector2(2f, 2f),
                    out _,
                    out _,
                    out _);

        TerrainNodeElevationSource nonFinite = CreateSmoothSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 50f));

        bool injected =
            SetPrivateElevation(
                nonFinite.Nodes[1],
                float.PositiveInfinity);

        bool nonFiniteRejected =
            injected &&
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                nonFinite,
                new Vector2(2f, 2f),
                out _,
                out _);

        bool passed =
            nullRejected &&
            invalidSampleRejected &&
            malformedRejected &&
            nonFiniteRejected;

        AddResult(
            "Malformed and non-finite Smooth inputs fail explicitly",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Null sources, NaN samples, stale/malformed patch fields, and non-finite persistent elevation data were rejected without fallback or NaN/Infinity output."
                : "At least one invalid Smooth input was accepted.");
    }

    private static void ValidateIdwAndLinearRegression()
    {
        TerrainNodeElevationSource idw = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f));

        bool idwOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                idw,
                new Vector2(2f, 0f),
                out float idwHeight,
                out _) &&
            Mathf.Abs(idwHeight - 5.882353f) <= 0.0005f;

        TerrainNodeElevationSource linear = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 200f));
        linear.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        bool linearOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                linear,
                new Vector2(2f, 3f),
                out float linearHeight,
                out _) &&
            Mathf.Abs(linearHeight - 80f) <= 0.0001f;

        AddResult(
            "Representative IDW and Triangulated Linear CPU output remains unchanged",
            idwOk && linearOk
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            idwOk && linearOk
                ? "The fixed p=2 IDW sample and Package I3 barycentric Linear sample retained their established values."
                : "Package I6 changed existing IDW or Linear CPU mathematics.");
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
                ? "I6 validation changed no real source, revision, signatures, interpolation mode, node data, or editor selection state."
                : "Real WorldMeshes authoring or selection state changed during Package I6 validation.");
    }

    private static bool ValidatePlane(
        Vector2[] positions,
        float gradientX,
        float gradientZ,
        float offset,
        float heightTolerance,
        float gradientTolerance)
    {
        TerrainNodeElevationSource source =
            CreatePlaneSource(
                positions,
                gradientX,
                gradientZ,
                offset);

        if (!TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out TerrainNodeElevationSmoothPatchData patches,
            out _))
        {
            return false;
        }

        if (
            topology.Kind != TerrainNodeElevationTopologyKind.Triangulated ||
            patches.PatchCount != topology.TriangleCount)
        {
            return false;
        }

        Vector2 expectedGradient =
            new Vector2(gradientX, gradientZ);

        for (int triangleIndex = 0;
            triangleIndex < topology.TriangleCount;
            triangleIndex++)
        {
            TerrainNodeElevationTopologyTriangle triangle =
                topology.Triangles[triangleIndex];

            Vector2 a = topology.Vertices[triangle.VertexA].PositionXZ;
            Vector2 b = topology.Vertices[triangle.VertexB].PositionXZ;
            Vector2 c = topology.Vertices[triangle.VertexC].PositionXZ;

            Vector2[] samples =
            {
                (a + b + c) / 3f,
                a * 0.6f + b * 0.2f + c * 0.2f,
                a * 0.2f + b * 0.6f + c * 0.2f,
                a * 0.2f + b * 0.2f + c * 0.6f
            };

            for (int sampleIndex = 0;
                sampleIndex < samples.Length;
                sampleIndex++)
            {
                Vector2 sample = samples[sampleIndex];
                float expectedHeight =
                    gradientX * sample.x +
                    gradientZ * sample.y +
                    offset;

                if (!TerrainNodeElevationSmoothInterpolationUtility
                    .TryEvaluateHeightAndGradient(
                        source,
                        topology,
                        gradients,
                        patches,
                        sample,
                        out float actualHeight,
                        out Vector2 actualGradient,
                        out _) ||
                    Mathf.Abs(actualHeight - expectedHeight) > heightTolerance ||
                    !GradientMatches(
                        actualGradient,
                        expectedGradient,
                        gradientTolerance))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateCurvedCenterField(
        TerrainNodeElevationSource source,
        Vector2 center,
        Vector2 sample)
    {
        if (!TryBuildDerived(
            source,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out TerrainNodeElevationSmoothPatchData patches,
            out _))
        {
            return false;
        }

        if (!TerrainNodeElevationSmoothInterpolationUtility
            .TryEvaluateHeightAndGradient(
                source,
                topology,
                gradients,
                patches,
                center,
                out _,
                out Vector2 centerGradient,
                out _))
        {
            return false;
        }

        if (centerGradient.magnitude > 0.02f)
        {
            return false;
        }

        if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            sample,
            out float smoothHeight,
            out _))
        {
            return false;
        }

        TerrainNodeElevationInterpolationMode previous =
            source.InterpolationMode;

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        bool linearOk =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                sample,
                out float linearHeight,
                out _);

        source.SetInterpolationModeInternal(previous);

        return
            linearOk &&
            IsFinite(smoothHeight) &&
            IsFinite(linearHeight) &&
            Mathf.Abs(smoothHeight - linearHeight) > 0.05f &&
            Mathf.Abs(smoothHeight) < 10000f;
    }

    private static bool CompareDirectSubpatches(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationSmoothPatchData patches,
        int firstTriangle,
        int firstSubpatch,
        int secondTriangle,
        int secondSubpatch,
        Vector2 sample,
        float heightTolerance,
        float gradientTolerance)
    {
        bool firstOk =
            TerrainNodeElevationSmoothInterpolationUtility
                .TryEvaluateTriangleSubpatchHeightAndGradient(
                    topology,
                    patches,
                    firstTriangle,
                    firstSubpatch,
                    sample,
                    out float firstHeight,
                    out Vector2 firstGradient,
                    out _);

        bool secondOk =
            TerrainNodeElevationSmoothInterpolationUtility
                .TryEvaluateTriangleSubpatchHeightAndGradient(
                    topology,
                    patches,
                    secondTriangle,
                    secondSubpatch,
                    sample,
                    out float secondHeight,
                    out Vector2 secondGradient,
                    out _);

        return
            firstOk &&
            secondOk &&
            Mathf.Abs(firstHeight - secondHeight) <= heightTolerance &&
            GradientMatches(
                firstGradient,
                secondGradient,
                gradientTolerance);
    }

    private static bool TryFindTrianglesForEdge(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationTopologyEdge edge,
        out int firstTriangle,
        out int secondTriangle,
        out int firstSubpatch,
        out int secondSubpatch)
    {
        firstTriangle = -1;
        secondTriangle = -1;
        firstSubpatch = -1;
        secondSubpatch = -1;

        for (int triangleIndex = 0;
            triangleIndex < topology.TriangleCount;
            triangleIndex++)
        {
            int subpatch =
                GetOuterSubpatchForEdge(
                    topology.Triangles[triangleIndex],
                    edge.VertexA,
                    edge.VertexB);

            if (subpatch < 0)
            {
                continue;
            }

            if (firstTriangle < 0)
            {
                firstTriangle = triangleIndex;
                firstSubpatch = subpatch;
            }
            else
            {
                secondTriangle = triangleIndex;
                secondSubpatch = subpatch;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindSingleTriangleForHullEdge(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationTopologyEdge edge,
        out int triangleIndex,
        out int subpatchIndex,
        out int thirdVertexIndex)
    {
        triangleIndex = -1;
        subpatchIndex = -1;
        thirdVertexIndex = -1;

        for (int index = 0; index < topology.TriangleCount; index++)
        {
            TerrainNodeElevationTopologyTriangle triangle =
                topology.Triangles[index];

            int subpatch =
                GetOuterSubpatchForEdge(
                    triangle,
                    edge.VertexA,
                    edge.VertexB);

            if (subpatch < 0)
            {
                continue;
            }

            triangleIndex = index;
            subpatchIndex = subpatch;

            int[] vertices =
            {
                triangle.VertexA,
                triangle.VertexB,
                triangle.VertexC
            };

            for (int local = 0; local < vertices.Length; local++)
            {
                if (
                    vertices[local] != edge.VertexA &&
                    vertices[local] != edge.VertexB)
                {
                    thirdVertexIndex = vertices[local];
                    return true;
                }
            }
        }

        return false;
    }

    private static int GetOuterSubpatchForEdge(
        TerrainNodeElevationTopologyTriangle triangle,
        int edgeA,
        int edgeB)
    {
        if (SameEdge(triangle.VertexA, triangle.VertexB, edgeA, edgeB))
        {
            return 0;
        }

        if (SameEdge(triangle.VertexB, triangle.VertexC, edgeA, edgeB))
        {
            return 1;
        }

        if (SameEdge(triangle.VertexC, triangle.VertexA, edgeA, edgeB))
        {
            return 2;
        }

        return -1;
    }

    private static bool SameEdge(int a, int b, int c, int d)
    {
        return
            (a == c && b == d) ||
            (a == d && b == c);
    }

    private static bool TryBuildDerived(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopology topology,
        out TerrainNodeElevationGradientData gradients,
        out TerrainNodeElevationSmoothPatchData patches,
        out string errorMessage)
    {
        topology = null;
        gradients = null;
        patches = null;
        errorMessage = "";

        if (!TerrainNodeElevationTriangulationUtility.TryBuild(
            source,
            out topology,
            out errorMessage))
        {
            return false;
        }

        if (!TerrainNodeElevationGradientUtility.TryBuild(
            source,
            topology,
            out gradients,
            out errorMessage))
        {
            return false;
        }

        return TerrainNodeElevationSmoothPatchUtility.TryBuild(
            source,
            topology,
            gradients,
            out patches,
            out errorMessage);
    }

    private static TerrainNodeElevationSource CreateGridField(float[,] heights)
    {
        List<NodeSpec> nodes = new List<NodeSpec>();

        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                nodes.Add(
                    new NodeSpec(
                        new Vector2(x * 10f, z * 10f),
                        heights[z, x]));
            }
        }

        return CreateSmoothSource(nodes.ToArray());
    }

    private static TerrainNodeElevationSource CreatePlaneSource(
        Vector2[] positions,
        float gradientX,
        float gradientZ,
        float offset)
    {
        NodeSpec[] specs =
            new NodeSpec[positions.Length];

        for (int index = 0; index < positions.Length; index++)
        {
            Vector2 position = positions[index];
            specs[index] =
                new NodeSpec(
                    position,
                    gradientX * position.x +
                    gradientZ * position.y +
                    offset);
        }

        return CreateSmoothSource(specs);
    }

    private static TerrainNodeElevationSource CreateSource(params NodeSpec[] nodes)
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

    private static TerrainNodeElevationSource CreateSmoothSource(params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            CreateSource(nodes);

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        return source;
    }

    private static bool EvaluateSamples(
        TerrainNodeElevationSource source,
        Vector2[] samples,
        out float[] heights)
    {
        heights = new float[samples.Length];

        for (int index = 0; index < samples.Length; index++)
        {
            if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                samples[index],
                out heights[index],
                out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateHeight(
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

    private static bool ArraysApproximatelyEqual(
        float[] a,
        float[] b,
        float tolerance)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Length; index++)
        {
            if (
                !IsFinite(a[index]) ||
                !IsFinite(b[index]) ||
                Mathf.Abs(a[index] - b[index]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool GradientMatches(
        Vector2 a,
        Vector2 b,
        float tolerance)
    {
        return
            IsFinite(a) &&
            IsFinite(b) &&
            Mathf.Abs(a.x - b.x) <= tolerance &&
            Mathf.Abs(a.y - b.y) <= tolerance;
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

    private static bool IsFinite(Vector2 value)
    {
        return
            IsFinite(value.x) &&
            IsFinite(value.y);
    }

    private static bool IsFinite(float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
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
        builder.AppendLine("WorldMeshes Regional Elevation Triangulated Smooth CPU Validation");
        builder.AppendLine("===============================================================");
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
            "Regional elevation Triangulated Smooth CPU validation: " +
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
