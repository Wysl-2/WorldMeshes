using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Package I7 validation for production Triangulated Smooth GPU composition.
 *
 * I6 CPU evaluation is the semantic reference. Numerical checks use the real
 * production Smooth compute shader through TerrainHeightCompositor; production
 * code performs no GPU readback.
 */
public static class TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility
{
    private const float HeightAbsoluteTolerance = 0.006f;
    private const float HeightRelativeTolerance = 0.000002f;

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

    private struct ParityStats
    {
        public int SampleCount;
        public float MaximumAbsoluteError;
        public Vector2 WorstPosition;

        public string ToDetails()
        {
            return
                $"samples={SampleCount}, " +
                $"maxAbsError={MaximumAbsoluteError:R}, " +
                $"worst=({WorstPosition.x:R}, {WorstPosition.y:R})";
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
    private static readonly List<string> realSelectedIdsBefore =
        new List<string>();
    private static string realPrimaryBefore = "";

    public static bool IsRunning =>
        validationRunning ||
        validationScheduled;

    public static void ValidateTriangulatedSmoothGpu()
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
                "Compute shaders, current WorldSettings/TerrainAuthoringData, " +
                "I2/I5/I6 CPU derivation, and the production Smooth compute " +
                "shader are available.");

            ValidateCapabilityAndCompositionResolution();
            ValidateShaderKernels();
            ValidateSinglePointParity();
            ValidateTwoNodeHermiteParity();
            ValidateCollinearHermiteParity();
            ValidateExactNodeParity();
            ValidateInteriorParity();
            ValidateSharedMacroEdgeParity();
            ValidateCentroidSpokeParity();
            ValidateCentroidParity();
            ValidateHullExteriorParity();
            ValidateHullTieParity();
            ValidateHillFixture();
            ValidateValleyFixture();
            ValidateFlatFixture();
            ValidatePlanarFixture();
            ValidateRepeatedDispatchReuse();
            ValidateElevationOnlyInvalidation();
            ValidatePositionInvalidation();
            ValidateMembershipInvalidation();
            ValidateStableIdIgnored();
            ValidateModeSwitchingReuse();
            ValidateDragStylePreviewUpdates();
            ValidateUndoRedoStates();
            ValidateProductionTileAndBakePath();
            ValidateConservativeSmoothRange();
            ValidateIdwGpuRegression();
            ValidateLinearGpuRegression();
            ValidateEmptyFailureBoundary();
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

    private static bool TryValidatePrerequisites(
        out string errorMessage)
    {
        errorMessage = "";

        if (
            Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage =
                "Validation cannot run in or while entering Play Mode.";
            return false;
        }

        if (
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            errorMessage =
                "Wait for Unity to finish compiling/importing and run " +
                "validation again.";
            return false;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The active graphics device does not support compute shaders.";
            return false;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath);

        realAuthoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath);

        if (
            worldSettings == null ||
            realAuthoringData == null)
        {
            errorMessage =
                "WorldSettings or TerrainAuthoringData could not be loaded.";
            return false;
        }

        string committed =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);

        string overall =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);

        if (
            string.IsNullOrEmpty(committed) ||
            string.IsNullOrEmpty(overall))
        {
            errorMessage =
                "Initialize the committed authoring heightfield before " +
                "running Package I7 validation.";
            return false;
        }

        ComputeShader smoothShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                TerrainHeightCompositor
                    .TriangulatedSmoothComputeShaderAssetPath);

        if (smoothShader == null)
        {
            errorMessage =
                "The Package I7 production Triangulated Smooth compute " +
                "shader could not be loaded.";
            return false;
        }

        TerrainNodeElevationSource probe =
            CreateSmoothSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(0f, 10f), 50f));

        TerrainNodeElevationTopologyCache topologyCache =
            new TerrainNodeElevationTopologyCache();

        TerrainNodeElevationGradientCache gradientCache =
            new TerrainNodeElevationGradientCache();

        TerrainNodeElevationSmoothPatchCache patchCache =
            new TerrainNodeElevationSmoothPatchCache();

        if (!topologyCache.TryGetOrBuild(
            probe,
            out TerrainNodeElevationTopology topology,
            out errorMessage))
        {
            return false;
        }

        if (!gradientCache.TryGetOrBuild(
            probe,
            topology,
            out TerrainNodeElevationGradientData gradients,
            out errorMessage))
        {
            return false;
        }

        if (!patchCache.TryGetOrBuild(
            probe,
            topology,
            gradients,
            out TerrainNodeElevationSmoothPatchData patches,
            out errorMessage))
        {
            return false;
        }

        if (
            patches == null ||
            patches.PatchCount != topology.TriangleCount)
        {
            errorMessage =
                "I6 Smooth patch data did not match the I2 topology.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore =
            realAuthoringData.authoringRevision;

        realCommittedBefore =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);

        realOverallBefore =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);

        realRegionalBefore =
            realAuthoringData.RegionalElevationSource;

        TerrainNodeElevationSource realNodeSource =
            realRegionalBefore as TerrainNodeElevationSource;

        realInterpolationBefore =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode
                    .InverseDistanceWeighted;

        realSelectedIdsBefore.Clear();

        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            realSelectedIdsBefore);

        realPrimaryBefore =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);
    }

    private static void ValidateCapabilityAndCompositionResolution()
    {
        TerrainNodeElevationSource source =
            CreateSmoothSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(0f, 10f), 50f));

        TerrainAuthoringData data =
            CreateDataFromSource(source);

        bool capabilities =
            TerrainNodeElevationInterpolationModeUtility
                .SupportsCpuEvaluation(
                    TerrainNodeElevationInterpolationMode
                        .InverseDistanceWeighted) &&
            TerrainNodeElevationInterpolationModeUtility
                .SupportsGpuComposition(
                    TerrainNodeElevationInterpolationMode
                        .InverseDistanceWeighted) &&
            TerrainNodeElevationInterpolationModeUtility
                .SupportsCpuEvaluation(
                    TerrainNodeElevationInterpolationMode
                        .TriangulatedLinear) &&
            TerrainNodeElevationInterpolationModeUtility
                .SupportsGpuComposition(
                    TerrainNodeElevationInterpolationMode
                        .TriangulatedLinear) &&
            TerrainNodeElevationInterpolationModeUtility
                .SupportsCpuEvaluation(
                    TerrainNodeElevationInterpolationMode
                        .TriangulatedSmooth) &&
            TerrainNodeElevationInterpolationModeUtility
                .SupportsGpuComposition(
                    TerrainNodeElevationInterpolationMode
                        .TriangulatedSmooth) &&
            TerrainNodeElevationInterpolationModeUtility
                .IsImplemented(
                    TerrainNodeElevationInterpolationMode
                        .TriangulatedSmooth);

        bool resolver =
            TerrainRegionalElevationCompositionUtility.TryResolveNodeSource(
                data,
                out TerrainNodeElevationSource resolved,
                out bool required,
                out string error) &&
            ReferenceEquals(resolved, source) &&
            required;

        bool passed =
            capabilities &&
            resolver;

        AddResult(
            "Capability matrix and production resolver enable Smooth",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "IDW, Linear, and Smooth report complete CPU/GPU support; " +
                  "the production regional composition resolver accepts Smooth."
                : error);

        ClearFixture(data);
    }

    private static void ValidateShaderKernels()
    {
        string error = "";
        bool passed = false;

        try
        {
            ComputeShader shader =
                AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    TerrainHeightCompositor
                        .TriangulatedSmoothComputeShaderAssetPath);

            if (shader == null)
            {
                error =
                    "Smooth compute shader asset is missing.";
            }
            else
            {
                int compose =
                    shader.FindKernel(
                        "ComposeTriangulatedSmoothRegionalElevation");

                int samples =
                    shader.FindKernel(
                        "EvaluateTriangulatedSmoothSamples");

                passed =
                    compose >= 0 &&
                    samples >= 0;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        AddResult(
            "Production Smooth compose and validation kernels are loadable",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Both required kernels resolved from the production " +
                  "TerrainRegionalElevationSmooth.compute asset."
                : error);
    }

    private static void ValidateSinglePointParity()
    {
        RunParityCase(
            "Single-point Smooth topology matches CPU",
            CreateSmoothSource(
                new NodeSpec(new Vector2(3f, -7f), 200f)),
            new[]
            {
                new Vector2(3f, -7f),
                Vector2.zero,
                new Vector2(-500f, 900f),
                new Vector2(10000f, -10000f)
            });
    }

    private static void ValidateTwoNodeHermiteParity()
    {
        RunParityCase(
            "Two-node projected cubic-Hermite Smooth path matches CPU",
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new[]
            {
                new Vector2(-10f, 0f),
                new Vector2(0f, 0f),
                new Vector2(2.5f, 0f),
                new Vector2(5f, 100f),
                new Vector2(7.5f, -20f),
                new Vector2(10f, 0f),
                new Vector2(20f, 0f)
            });
    }

    private static void ValidateCollinearHermiteParity()
    {
        RunParityCase(
            "Collinear piecewise cubic-Hermite Smooth path matches CPU",
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(25f, 0f), 40f),
                new NodeSpec(new Vector2(40f, 0f), 160f)),
            new[]
            {
                new Vector2(-20f, 0f),
                new Vector2(0f, 0f),
                new Vector2(5f, 0f),
                new Vector2(10f, 0f),
                new Vector2(17.5f, 0f),
                new Vector2(32.5f, 0f),
                new Vector2(40f, 0f),
                new Vector2(60f, 0f),
                new Vector2(17.5f, 500f),
                new Vector2(17.5f, -500f)
            });
    }

    private static void ValidateExactNodeParity()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        List<Vector2> samples =
            new List<Vector2>();

        for (int index = 0;
            index < source.NodeCount;
            index++)
        {
            samples.Add(
                source.Nodes[index].PositionXZ);
        }

        RunParityCase(
            "Every exact Smooth node position matches CPU elevation",
            source,
            samples);
    }

    private static void ValidateInteriorParity()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        Vector2[] samples =
        {
            new Vector2(2f, 2f),
            new Vector2(4f, 3f),
            new Vector2(6f, 4f),
            new Vector2(8f, 7f),
            new Vector2(3f, 8f),
            new Vector2(6f, 7f)
        };

        RunParityCase(
            "Triangulated Smooth interior samples match CPU HCT evaluation",
            source,
            samples);
    }

    private static void ValidateSharedMacroEdgeParity()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        if (!TryBuildTopology(
            source,
            out TerrainNodeElevationTopology topology,
            out string error))
        {
            AddResult(
                "Shared I2 macro-edge samples match CPU Smooth",
                ValidationOutcome.Fail,
                error);
            return;
        }

        if (!TryFindSharedEdge(
            topology,
            out int vertexA,
            out int vertexB))
        {
            AddResult(
                "Shared I2 macro-edge samples match CPU Smooth",
                ValidationOutcome.Blocked,
                "The validation fixture did not produce a shared I2 edge.");
            return;
        }

        Vector2 a =
            topology.Vertices[vertexA].PositionXZ;

        Vector2 b =
            topology.Vertices[vertexB].PositionXZ;

        RunParityCase(
            "Shared I2 macro-edge samples match CPU Smooth",
            source,
            new[]
            {
                Vector2.Lerp(a, b, 0.2f),
                Vector2.Lerp(a, b, 0.5f),
                Vector2.Lerp(a, b, 0.8f)
            });
    }

    private static void ValidateCentroidSpokeParity()
    {
        TerrainNodeElevationSource source =
            CreateBasicTriangleSource();

        if (!TryGetFirstTriangleGeometry(
            source,
            out Vector2 a,
            out Vector2 b,
            out Vector2 c,
            out Vector2 centroid,
            out string error))
        {
            AddResult(
                "HCT centroid-spoke samples match CPU Smooth",
                ValidationOutcome.Fail,
                error);
            return;
        }

        RunParityCase(
            "HCT centroid-spoke samples match CPU Smooth",
            source,
            new[]
            {
                Vector2.Lerp(a, centroid, 0.2f),
                Vector2.Lerp(a, centroid, 0.5f),
                Vector2.Lerp(a, centroid, 0.8f),
                Vector2.Lerp(b, centroid, 0.2f),
                Vector2.Lerp(b, centroid, 0.5f),
                Vector2.Lerp(b, centroid, 0.8f),
                Vector2.Lerp(c, centroid, 0.2f),
                Vector2.Lerp(c, centroid, 0.5f),
                Vector2.Lerp(c, centroid, 0.8f)
            });
    }

    private static void ValidateCentroidParity()
    {
        TerrainNodeElevationSource source =
            CreateBasicTriangleSource();

        if (!TryGetFirstTriangleGeometry(
            source,
            out _,
            out _,
            out _,
            out Vector2 centroid,
            out string error))
        {
            AddResult(
                "HCT macro-triangle centroid matches CPU Smooth",
                ValidationOutcome.Fail,
                error);
            return;
        }

        RunParityCase(
            "HCT macro-triangle centroid matches CPU Smooth",
            source,
            new[] { centroid });
    }

    private static void ValidateHullExteriorParity()
    {
        RunParityCase(
            "Convex-hull exterior Hermite projection matches CPU Smooth",
            CreateSquareSource(),
            new[]
            {
                new Vector2(5f, -3f),
                new Vector2(5f, -300f),
                new Vector2(-4f, 10f),
                new Vector2(15f, 15f),
                new Vector2(15f, 5f),
                new Vector2(-20f, 5f)
            });
    }

    private static void ValidateHullTieParity()
    {
        Vector2 tie =
            new Vector2(-5f, -5f);

        RunParityCase(
            "Convex-hull nearest-edge tie behavior matches CPU Smooth",
            CreateSquareSource(),
            new[]
            {
                tie,
                tie,
                new Vector2(15f, -5f),
                new Vector2(15f, 15f)
            });
    }

    private static void ValidateHillFixture()
    {
        RunParityCase(
            "Smooth hill fixture matches CPU",
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(12f, 0f), 0f),
                new NodeSpec(new Vector2(12f, 12f), 0f),
                new NodeSpec(new Vector2(0f, 12f), 0f),
                new NodeSpec(new Vector2(6f, 6f), 180f)),
            CreateGridSamples(0f, 12f, 0f, 12f, 5));
    }

    private static void ValidateValleyFixture()
    {
        RunParityCase(
            "Smooth valley fixture matches CPU",
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 120f),
                new NodeSpec(new Vector2(12f, 0f), 120f),
                new NodeSpec(new Vector2(12f, 12f), 120f),
                new NodeSpec(new Vector2(0f, 12f), 120f),
                new NodeSpec(new Vector2(6f, 6f), -80f)),
            CreateGridSamples(0f, 12f, 0f, 12f, 5));
    }

    private static void ValidateFlatFixture()
    {
        TerrainNodeElevationSource source =
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 42f),
                new NodeSpec(new Vector2(15f, 0f), 42f),
                new NodeSpec(new Vector2(15f, 15f), 42f),
                new NodeSpec(new Vector2(0f, 15f), 42f),
                new NodeSpec(new Vector2(7f, 8f), 42f));

        Vector2[] samples =
            CreateGridSamples(-5f, 20f, -5f, 20f, 6);

        bool parity =
            TryValidateCpuGpuParity(
                source,
                samples,
                out ParityStats stats,
                out string error);

        bool flat = parity;

        if (parity)
        {
            using (TerrainHeightCompositor compositor =
                new TerrainHeightCompositor())
            {
                if (!compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    samples,
                    out float[] heights,
                    out error))
                {
                    flat = false;
                }
                else
                {
                    for (int index = 0;
                        index < heights.Length;
                        index++)
                    {
                        if (
                            Mathf.Abs(
                                heights[index] -
                                42f) >
                            HeightAbsoluteTolerance)
                        {
                            flat = false;
                            break;
                        }
                    }
                }
            }
        }

        AddResult(
            "Constant Smooth field remains flat inside and outside the hull",
            flat
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            flat
                ? stats.ToDetails()
                : error);
    }

    private static void ValidatePlanarFixture()
    {
        NodeSpec[] nodes =
        {
            PlanarNode(0f, 0f),
            PlanarNode(10f, 0f),
            PlanarNode(10f, 10f),
            PlanarNode(0f, 10f),
            PlanarNode(4f, 6f)
        };

        RunParityCase(
            "Planar Smooth fixture reproduces CPU plane behavior",
            CreateSmoothSource(nodes),
            CreateGridSamples(-3f, 13f, -3f, 13f, 7));
    }

    private static void ValidateRepeatedDispatchReuse()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        string error = "";
        bool passed = false;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            bool first =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            int topology =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            int gradients =
                compositor.TriangulatedSmoothGradientRebuildCount;

            int patches =
                compositor.TriangulatedSmoothPatchRebuildCount;

            int uploads =
                compositor.TriangulatedSmoothNumericUploadCount;

            int allocations =
                compositor.TriangulatedSmoothBufferAllocationCount;

            bool second =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(6f, 5f) },
                    out _,
                    out error);

            passed =
                first &&
                second &&
                compositor.TriangulatedSmoothTopologyRebuildCount ==
                    topology &&
                compositor.TriangulatedSmoothGradientRebuildCount ==
                    gradients &&
                compositor.TriangulatedSmoothPatchRebuildCount ==
                    patches &&
                compositor.TriangulatedSmoothNumericUploadCount ==
                    uploads &&
                compositor.TriangulatedSmoothBufferAllocationCount ==
                    allocations;
        }

        AddResult(
            "Repeated Smooth dispatch reuses topology, numerics, and GPU buffers",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "No mathematical cache rebuild, derived upload, or Smooth " +
                  "buffer allocation occurred for unchanged source data."
                : error);
    }

    private static void ValidateElevationOnlyInvalidation()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        string error = "";
        bool passed = false;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            bool first =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            int topology =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            int gradients =
                compositor.TriangulatedSmoothGradientRebuildCount;

            int patches =
                compositor.TriangulatedSmoothPatchRebuildCount;

            int uploads =
                compositor.TriangulatedSmoothNumericUploadCount;

            source.Nodes[0].SetElevationInternal(
                source.Nodes[0].Elevation + 75f);

            bool second =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            passed =
                first &&
                second &&
                compositor.TriangulatedSmoothTopologyRebuildCount ==
                    topology &&
                compositor.TriangulatedSmoothGradientRebuildCount ==
                    gradients + 1 &&
                compositor.TriangulatedSmoothPatchRebuildCount ==
                    patches + 1 &&
                compositor.TriangulatedSmoothNumericUploadCount ==
                    uploads + 1;
        }

        AddResult(
            "Elevation-only edits reuse I2 topology and rebuild I5/I6 numerics",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Topology rebuild count was unchanged; gradients, HCT " +
                  "patches, and the derived GPU upload advanced once."
                : error);
    }

    private static void ValidatePositionInvalidation()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        string error = "";
        bool passed = false;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            bool first =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            int topology =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            int gradients =
                compositor.TriangulatedSmoothGradientRebuildCount;

            int patches =
                compositor.TriangulatedSmoothPatchRebuildCount;

            source.Nodes[0].SetPositionXZInternal(
                source.Nodes[0].PositionXZ +
                new Vector2(0.5f, 0.25f));

            bool second =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            passed =
                first &&
                second &&
                compositor.TriangulatedSmoothTopologyRebuildCount ==
                    topology + 1 &&
                compositor.TriangulatedSmoothGradientRebuildCount ==
                    gradients + 1 &&
                compositor.TriangulatedSmoothPatchRebuildCount ==
                    patches + 1;
        }

        AddResult(
            "Position edits rebuild I2 topology and dependent Smooth data",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Position mutation rebuilt topology, gradients, and HCT " +
                  "patches exactly on the next actual evaluation."
                : error);
    }

    private static void ValidateMembershipInvalidation()
    {
        TerrainNodeElevationSource source =
            CreateBasicTriangleSource();

        string error = "";
        bool passed = false;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            bool first =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(4f, 4f) },
                    out _,
                    out error);

            int topology =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            int gradients =
                compositor.TriangulatedSmoothGradientRebuildCount;

            int patches =
                compositor.TriangulatedSmoothPatchRebuildCount;

            TerrainElevationNode added =
                new TerrainElevationNode();

            added.SetPositionXZInternal(
                new Vector2(12f, 12f));

            added.SetElevationInternal(90f);

            source.AddNodeInternal(added);
            source.RepairNodeStableIds();

            bool second =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(4f, 4f) },
                    out _,
                    out error);

            passed =
                first &&
                second &&
                compositor.TriangulatedSmoothTopologyRebuildCount ==
                    topology + 1 &&
                compositor.TriangulatedSmoothGradientRebuildCount ==
                    gradients + 1 &&
                compositor.TriangulatedSmoothPatchRebuildCount ==
                    patches + 1;
        }

        AddResult(
            "Node membership edits rebuild topology and all Smooth dependents",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Adding a valid node invalidated I2/I5/I6 derivation once."
                : error);
    }

    private static void ValidateStableIdIgnored()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        string error = "";
        bool passed = false;
        bool fieldAvailable = false;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            bool first =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            int topology =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            int gradients =
                compositor.TriangulatedSmoothGradientRebuildCount;

            int patches =
                compositor.TriangulatedSmoothPatchRebuildCount;

            int uploads =
                compositor.TriangulatedSmoothNumericUploadCount;

            FieldInfo stableIdField =
                typeof(TerrainElevationNode).GetField(
                    "stableId",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);

            fieldAvailable =
                stableIdField != null;

            if (fieldAvailable)
            {
                stableIdField.SetValue(
                    source.Nodes[0],
                    Guid.NewGuid().ToString("N"));

                bool second =
                    compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                        source,
                        new[] { new Vector2(5f, 5f) },
                        out _,
                        out error);

                passed =
                    first &&
                    second &&
                    compositor.TriangulatedSmoothTopologyRebuildCount ==
                        topology &&
                    compositor.TriangulatedSmoothGradientRebuildCount ==
                        gradients &&
                    compositor.TriangulatedSmoothPatchRebuildCount ==
                        patches &&
                    compositor.TriangulatedSmoothNumericUploadCount ==
                        uploads;
            }
        }

        AddResult(
            "StableId-only edits do not invalidate Smooth mathematics or uploads",
            !fieldAvailable
                ? ValidationOutcome.Blocked
                : passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
            !fieldAvailable
                ? "Validation could not access the current serialized StableId " +
                  "field to perform an identity-only mutation."
                : passed
                    ? "Changing only persistent node identity left I2/I5/I6 " +
                      "cache counts and Smooth numeric uploads unchanged."
                    : error);
    }

    private static void ValidateModeSwitchingReuse()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        string error = "";
        bool passed = false;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            bool smoothA =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            int topology =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            int gradients =
                compositor.TriangulatedSmoothGradientRebuildCount;

            int patches =
                compositor.TriangulatedSmoothPatchRebuildCount;

            int uploads =
                compositor.TriangulatedSmoothNumericUploadCount;

            source.SetInterpolationModeInternal(
                TerrainNodeElevationInterpolationMode
                    .TriangulatedLinear);

            bool linear =
                compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            source.SetInterpolationModeInternal(
                TerrainNodeElevationInterpolationMode
                    .TriangulatedSmooth);

            bool smoothB =
                compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    source,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            passed =
                smoothA &&
                linear &&
                smoothB &&
                compositor.TriangulatedSmoothTopologyRebuildCount ==
                    topology &&
                compositor.TriangulatedSmoothGradientRebuildCount ==
                    gradients &&
                compositor.TriangulatedSmoothPatchRebuildCount ==
                    patches &&
                compositor.TriangulatedSmoothNumericUploadCount ==
                    uploads;
        }

        AddResult(
            "Linear/Smooth mode switching preserves mathematical Smooth caches",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Switching modes changed dispatch semantics without " +
                  "rebuilding I2/I5/I6 data or reuploading unchanged Smooth " +
                  "numerics."
                : error);
    }

    private static void ValidateDragStylePreviewUpdates()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        string error = "";
        bool passed = true;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            if (!TryCompareWithCompositor(
                compositor,
                source,
                new[] { new Vector2(5f, 5f) },
                out _,
                out error))
            {
                passed = false;
            }

            int rebuilds =
                compositor.TriangulatedSmoothTopologyRebuildCount;

            for (int step = 0;
                passed && step < 3;
                step++)
            {
                source.Nodes[0].SetPositionXZInternal(
                    source.Nodes[0].PositionXZ +
                    new Vector2(0.1f, 0.05f));

                passed =
                    TryCompareWithCompositor(
                        compositor,
                        source,
                        new[] { new Vector2(5f, 5f) },
                        out _,
                        out error);
            }

            passed =
                passed &&
                compositor.TriangulatedSmoothTopologyRebuildCount ==
                    rebuilds + 3;
        }

        AddResult(
            "Drag-style repeated position states reach live Smooth GPU evaluation",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Three successive geometry states, matching Scene tool drag " +
                  "mutation cadence, each produced CPU/GPU-compatible Smooth " +
                  "output and lazy topology refresh."
                : error);
    }

    private static void ValidateUndoRedoStates()
    {
        TerrainNodeElevationSource source =
            CreateIrregularSource();

        TerrainAuthoringData data =
            CreateDataFromSource(source);

        string error = "";
        bool passed = false;

        try
        {
            float original =
                source.Nodes[0].Elevation;

            float edited =
                original + 123f;

            Undo.RegisterCompleteObjectUndo(
                data,
                "WorldMeshes I7 Smooth GPU validation");

            source.Nodes[0].SetElevationInternal(
                edited);

            EditorUtility.SetDirty(data);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();

            TerrainNodeElevationSource undone =
                data.RegionalElevationSource
                    as TerrainNodeElevationSource;

            bool undoState =
                undone != null &&
                Mathf.Abs(
                    undone.Nodes[0].Elevation -
                    original) <= 0.0001f;

            bool undoGpu =
                undoState &&
                TryValidateCpuGpuParity(
                    undone,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            Undo.PerformRedo();

            TerrainNodeElevationSource redone =
                data.RegionalElevationSource
                    as TerrainNodeElevationSource;

            bool redoState =
                redone != null &&
                Mathf.Abs(
                    redone.Nodes[0].Elevation -
                    edited) <= 0.0001f;

            bool redoGpu =
                redoState &&
                TryValidateCpuGpuParity(
                    redone,
                    new[] { new Vector2(5f, 5f) },
                    out _,
                    out error);

            passed =
                undoState &&
                redoState &&
                undoGpu &&
                redoGpu;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            ClearFixture(data);
        }

        AddResult(
            "Transient Undo/Redo Smooth states remain GPU-composable",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "A real Unity Undo/Redo cycle restored both serialized " +
                  "elevation states and each state matched CPU Smooth on GPU."
                : error);
    }

    private static void ValidateProductionTileAndBakePath()
    {
        TerrainNodeElevationSource source =
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(10f, 10f), 250f),
                new NodeSpec(new Vector2(0f, 10f), 50f),
                new NodeSpec(new Vector2(5f, 5f), 140f));

        bool passed =
            TryValidateProductionTile(
                source,
                false,
                out _,
                out _,
                out ParityStats stats,
                out string error);

        AddResult(
            "Production tile compositor/runtime-bake overload dispatches Smooth",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? stats.ToDetails() +
                  "; used the no-range TryComposeTile overload shared by " +
                  "runtime height baking."
                : error);
    }

    private static void ValidateConservativeSmoothRange()
    {
        TerrainNodeElevationSource source =
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 140f),
                new NodeSpec(new Vector2(10f, 10f), -50f),
                new NodeSpec(new Vector2(0f, 10f), 90f),
                new NodeSpec(new Vector2(5f, 5f), 260f));

        bool tile =
            TryValidateProductionTile(
                source,
                true,
                out float minimum,
                out float maximum,
                out ParityStats stats,
                out string error);

        bool rangeContainsSamples = tile;

        if (tile)
        {
            Vector2[] samples =
                CreateGridSamples(
                    0f,
                    10f,
                    0f,
                    10f,
                    9);

            for (int index = 0;
                index < samples.Length;
                index++)
            {
                if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                    source,
                    samples[index],
                    out float height,
                    out string cpuError))
                {
                    rangeContainsSamples = false;
                    error = cpuError;
                    break;
                }

                if (
                    height < minimum - HeightAbsoluteTolerance ||
                    height > maximum + HeightAbsoluteTolerance)
                {
                    rangeContainsSamples = false;
                    error =
                        $"CPU Smooth sample {index} height {height:R} fell " +
                        $"outside conservative range [{minimum:R}, " +
                        $"{maximum:R}].";
                    break;
                }
            }
        }

        AddResult(
            "Smooth preview range is conservative for HCT/Hermite overshoot",
            rangeContainsSamples
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            rangeContainsSamples
                ? $"range=[{minimum:R}, {maximum:R}], " +
                  stats.ToDetails()
                : error);
    }

    private static void ValidateIdwGpuRegression()
    {
        TerrainNodeElevationSource source =
            CreateSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f));

        TerrainAuthoringData data =
            CreateDataFromSource(source);

        const int samplesPerSide = 5;
        const float tileWorldSize = 10f;

        float sampleSpacing =
            tileWorldSize /
            (samplesPerSide - 1);

        RenderTexture target = null;
        string error = "";
        bool passed = false;

        try
        {
            target =
                CreateValidationTarget(
                    samplesPerSide);

            if (
                target == null ||
                !target.Create())
            {
                error =
                    "Could not create the temporary RFloat IDW target.";
            }
            else
            {
                bool composed;

                using (TerrainHeightCompositor compositor =
                    new TerrainHeightCompositor())
                {
                    composed =
                        compositor.TryComposeTile(
                            target,
                            Vector2Int.zero,
                            0,
                            samplesPerSide,
                            sampleSpacing,
                            tileWorldSize,
                            new Vector2(
                                tileWorldSize,
                                tileWorldSize),
                            data,
                            out error);
                }

                if (composed)
                {
                    AsyncGPUReadbackRequest request =
                        AsyncGPUReadback.Request(
                            target,
                            0,
                            0,
                            samplesPerSide,
                            0,
                            samplesPerSide,
                            0,
                            1);

                    request.WaitForCompletion();

                    if (request.hasError)
                    {
                        error =
                            "GPU readback failed for the IDW regression target.";
                    }
                    else
                    {
                        var gpu =
                            request.GetData<float>();

                        passed = true;

                        for (int x = 0;
                            x < samplesPerSide;
                            x++)
                        {
                            Vector2 sample =
                                new Vector2(
                                    x * sampleSpacing,
                                    0f);

                            if (!TerrainNodeElevationEvaluator
                                .TryEvaluateHeight(
                                    source,
                                    sample,
                                    out float cpuHeight,
                                    out string cpuError))
                            {
                                passed = false;
                                error = cpuError;
                                break;
                            }

                            float gpuHeight =
                                gpu[x];

                            if (!HeightsMatch(
                                cpuHeight,
                                gpuHeight))
                            {
                                passed = false;
                                error =
                                    $"IDW mismatch at x={sample.x:R}: " +
                                    $"CPU={cpuHeight:R}, GPU={gpuHeight:R}.";
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            DestroyValidationTarget(target);
            ClearFixture(data);
        }

        AddResult(
            "Existing IDW GPU composition remains compatible after I7",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "The unchanged production IDW tile path matched CPU IDW " +
                  "after Smooth dispatch integration."
                : error);
    }

    private static void ValidateLinearGpuRegression()
    {
        TerrainNodeElevationSource source =
            CreateLinearSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(10f, 10f), 250f),
                new NodeSpec(new Vector2(0f, 10f), 50f));

        Vector2[] samples =
        {
            new Vector2(0f, 0f),
            new Vector2(2f, 2f),
            new Vector2(5f, 5f),
            new Vector2(10f, 10f),
            new Vector2(5f, -10f)
        };

        float[] cpu =
            new float[samples.Length];

        string error = "";
        bool passed = true;

        for (int index = 0;
            index < samples.Length;
            index++)
        {
            if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                samples[index],
                out cpu[index],
                out error))
            {
                passed = false;
                break;
            }
        }

        if (passed)
        {
            using (TerrainHeightCompositor compositor =
                new TerrainHeightCompositor())
            {
                if (!compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    source,
                    samples,
                    out float[] gpu,
                    out error))
                {
                    passed = false;
                }
                else
                {
                    for (int index = 0;
                        index < cpu.Length;
                        index++)
                    {
                        if (!HeightsMatch(
                            cpu[index],
                            gpu[index]))
                        {
                            passed = false;
                            error =
                                $"Linear regression mismatch at {index}: " +
                                $"CPU={cpu[index]:R}, GPU={gpu[index]:R}.";
                            break;
                        }
                    }
                }
            }
        }

        AddResult(
            "Existing Triangulated Linear GPU path remains compatible",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "The production Linear evaluator still matches its CPU " +
                  "reference after sharing I2 topology buffers with Smooth."
                : error);
    }

    private static void ValidateEmptyFailureBoundary()
    {
        TerrainNodeElevationSource empty =
            CreateSmoothSource();

        bool rejected;
        string error;

        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            rejected =
                !compositor.TryEvaluateTriangulatedSmoothGpuSamples(
                    empty,
                    new[] { Vector2.zero },
                    out _,
                    out error);
        }

        AddResult(
            "Empty Smooth source fails explicitly instead of emitting GPU data",
            rejected
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            rejected
                ? "The production Smooth GPU preparation rejected an empty " +
                  "regional surface."
                : error);
    }

    private static void ValidateRealStateUnchanged()
    {
        List<string> selectedAfter =
            new List<string>();

        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            selectedAfter);

        string primaryAfter =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);

        TerrainNodeElevationSource realNodeSource =
            realAuthoringData.RegionalElevationSource
                as TerrainNodeElevationSource;

        TerrainNodeElevationInterpolationMode interpolationAfter =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode
                    .InverseDistanceWeighted;

        bool passed =
            realAuthoringData.authoringRevision ==
                realRevisionBefore &&
            ReferenceEquals(
                realAuthoringData.RegionalElevationSource,
                realRegionalBefore) &&
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings) ==
                realCommittedBefore &&
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData) ==
                realOverallBefore &&
            interpolationAfter ==
                realInterpolationBefore &&
            realPrimaryBefore ==
                primaryAfter &&
            SequenceEqual(
                realSelectedIdsBefore,
                selectedAfter);

        AddResult(
            "Real authoring and Package 7 selection state remain unchanged",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "Transient I7 validation did not mutate the real regional " +
                  "source, revision, committed/overall identity, mode, or " +
                  "selection state."
                : "Real WorldMeshes authoring or regional selection state " +
                  "changed during I7 validation.");
    }

    private static void RunParityCase(
        string name,
        TerrainNodeElevationSource source,
        IReadOnlyList<Vector2> samples)
    {
        bool passed =
            TryValidateCpuGpuParity(
                source,
                samples,
                out ParityStats stats,
                out string error);

        AddResult(
            name,
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? stats.ToDetails()
                : error);
    }

    private static bool TryValidateCpuGpuParity(
        TerrainNodeElevationSource source,
        IReadOnlyList<Vector2> samples,
        out ParityStats stats,
        out string errorMessage)
    {
        using (TerrainHeightCompositor compositor =
            new TerrainHeightCompositor())
        {
            return TryCompareWithCompositor(
                compositor,
                source,
                samples,
                out stats,
                out errorMessage);
        }
    }

    private static bool TryCompareWithCompositor(
        TerrainHeightCompositor compositor,
        TerrainNodeElevationSource source,
        IReadOnlyList<Vector2> samples,
        out ParityStats stats,
        out string errorMessage)
    {
        stats =
            default(ParityStats);

        errorMessage = "";

        if (
            compositor == null ||
            source == null ||
            samples == null ||
            samples.Count <= 0)
        {
            errorMessage =
                "CPU/GPU Smooth parity fixture is invalid.";
            return false;
        }

        float[] cpu =
            new float[samples.Count];

        for (int index = 0;
            index < samples.Count;
            index++)
        {
            if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                samples[index],
                out cpu[index],
                out string cpuError))
            {
                errorMessage =
                    $"CPU Smooth evaluation failed at sample {index}. " +
                    cpuError;
                return false;
            }
        }

        if (!compositor.TryEvaluateTriangulatedSmoothGpuSamples(
            source,
            samples,
            out float[] gpu,
            out errorMessage))
        {
            return false;
        }

        if (
            gpu == null ||
            gpu.Length != cpu.Length)
        {
            errorMessage =
                "GPU Smooth evaluator returned an unexpected sample count.";
            return false;
        }

        stats.SampleCount =
            cpu.Length;

        stats.MaximumAbsoluteError =
            0f;

        stats.WorstPosition =
            samples[0];

        for (int index = 0;
            index < cpu.Length;
            index++)
        {
            float absoluteError =
                Mathf.Abs(
                    cpu[index] -
                    gpu[index]);

            if (
                index == 0 ||
                absoluteError >
                    stats.MaximumAbsoluteError)
            {
                stats.MaximumAbsoluteError =
                    absoluteError;

                stats.WorstPosition =
                    samples[index];
            }

            if (!HeightsMatch(
                cpu[index],
                gpu[index]))
            {
                errorMessage =
                    $"CPU/GPU Smooth mismatch at sample {index} " +
                    $"({samples[index].x:R}, {samples[index].y:R}): " +
                    $"CPU={cpu[index]:R}, GPU={gpu[index]:R}, " +
                    $"absError={absoluteError:R}, " +
                    $"tolerance={AllowedTolerance(cpu[index]):R}.";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateProductionTile(
        TerrainNodeElevationSource source,
        bool rangeAware,
        out float minimumHeight,
        out float maximumHeight,
        out ParityStats stats,
        out string errorMessage)
    {
        minimumHeight = 0f;
        maximumHeight = 0f;
        stats = default(ParityStats);
        errorMessage = "";

        TerrainAuthoringData data =
            CreateDataFromSource(source);

        const int samplesPerSide = 9;
        const float tileWorldSize = 10f;

        float sampleSpacing =
            tileWorldSize /
            (samplesPerSide - 1);

        RenderTexture target = null;
        bool composed = false;

        try
        {
            target =
                CreateValidationTarget(
                    samplesPerSide);

            if (
                target == null ||
                !target.Create())
            {
                errorMessage =
                    "Could not create the temporary RFloat Smooth tile target.";
                return false;
            }

            using (TerrainHeightCompositor compositor =
                new TerrainHeightCompositor())
            {
                if (rangeAware)
                {
                    composed =
                        compositor.TryComposeTile(
                            target,
                            Vector2Int.zero,
                            0,
                            samplesPerSide,
                            sampleSpacing,
                            tileWorldSize,
                            new Vector2(
                                tileWorldSize,
                                tileWorldSize),
                            data,
                            0f,
                            0f,
                            out minimumHeight,
                            out maximumHeight,
                            out errorMessage);
                }
                else
                {
                    composed =
                        compositor.TryComposeTile(
                            target,
                            Vector2Int.zero,
                            0,
                            samplesPerSide,
                            sampleSpacing,
                            tileWorldSize,
                            new Vector2(
                                tileWorldSize,
                                tileWorldSize),
                            data,
                            out errorMessage);
                }
            }

            if (!composed)
            {
                return false;
            }

            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    target,
                    0,
                    0,
                    samplesPerSide,
                    0,
                    samplesPerSide,
                    0,
                    1);

            request.WaitForCompletion();

            if (request.hasError)
            {
                errorMessage =
                    "GPU readback failed for the Smooth production tile.";
                return false;
            }

            var gpu =
                request.GetData<float>();

            stats.SampleCount =
                samplesPerSide *
                samplesPerSide;

            bool firstSample = true;

            for (int z = 0;
                z < samplesPerSide;
                z++)
            {
                for (int x = 0;
                    x < samplesPerSide;
                    x++)
                {
                    int index =
                        z * samplesPerSide +
                        x;

                    Vector2 sample =
                        new Vector2(
                            x * sampleSpacing,
                            z * sampleSpacing);

                    if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                        source,
                        sample,
                        out float cpu,
                        out string cpuError))
                    {
                        errorMessage = cpuError;
                        return false;
                    }

                    float absoluteError =
                        Mathf.Abs(
                            cpu -
                            gpu[index]);

                    if (
                        firstSample ||
                        absoluteError >
                            stats.MaximumAbsoluteError)
                    {
                        firstSample = false;
                        stats.MaximumAbsoluteError =
                            absoluteError;
                        stats.WorstPosition =
                            sample;
                    }

                    if (!HeightsMatch(
                        cpu,
                        gpu[index]))
                    {
                        errorMessage =
                            $"Production tile mismatch at ({sample.x:R}, " +
                            $"{sample.y:R}): CPU={cpu:R}, " +
                            $"GPU={gpu[index]:R}, " +
                            $"absError={absoluteError:R}, " +
                            $"tolerance={AllowedTolerance(cpu):R}.";
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                exception.Message;
            return false;
        }
        finally
        {
            DestroyValidationTarget(target);
            ClearFixture(data);
        }
    }

    private static RenderTexture CreateValidationTarget(
        int samplesPerSide)
    {
        return
            new RenderTexture(
                samplesPerSide,
                samplesPerSide,
                0,
                RenderTextureFormat.RFloat)
            {
                dimension =
                    TextureDimension.Tex2DArray,
                volumeDepth = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
    }

    private static void DestroyValidationTarget(
        RenderTexture target)
    {
        if (target == null)
        {
            return;
        }

        target.Release();
        UnityEngine.Object.DestroyImmediate(
            target);
    }

    private static float AllowedTolerance(
        float expected)
    {
        return
            HeightAbsoluteTolerance +
            HeightRelativeTolerance *
            Mathf.Max(
                1f,
                Mathf.Abs(expected));
    }

    private static bool HeightsMatch(
        float expected,
        float actual)
    {
        return
            !float.IsNaN(expected) &&
            !float.IsInfinity(expected) &&
            !float.IsNaN(actual) &&
            !float.IsInfinity(actual) &&
            Mathf.Abs(
                expected -
                actual) <=
                AllowedTolerance(expected);
    }

    private static TerrainNodeElevationSource CreateBasicTriangleSource()
    {
        return
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 5f),
                new NodeSpec(new Vector2(14f, 1f), 120f),
                new NodeSpec(new Vector2(2f, 12f), 60f));
    }

    private static TerrainNodeElevationSource CreateIrregularSource()
    {
        return
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 10f),
                new NodeSpec(new Vector2(12f, 1f), 110f),
                new NodeSpec(new Vector2(11f, 12f), 210f),
                new NodeSpec(new Vector2(-1f, 11f), 70f),
                new NodeSpec(new Vector2(5f, 6f), 150f));
    }

    private static TerrainNodeElevationSource CreateSquareSource()
    {
        return
            CreateSmoothSource(
                new NodeSpec(new Vector2(0f, 0f), 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f),
                new NodeSpec(new Vector2(10f, 10f), 250f),
                new NodeSpec(new Vector2(0f, 10f), 50f));
    }

    private static NodeSpec PlanarNode(
        float x,
        float z)
    {
        return
            new NodeSpec(
                new Vector2(x, z),
                2f * x +
                3f * z +
                5f);
    }

    private static Vector2[] CreateGridSamples(
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        int samplesPerAxis)
    {
        int count =
            Mathf.Max(
                2,
                samplesPerAxis);

        Vector2[] samples =
            new Vector2[count * count];

        int index = 0;

        for (int z = 0;
            z < count;
            z++)
        {
            float tz =
                z /
                (float)(count - 1);

            for (int x = 0;
                x < count;
                x++)
            {
                float tx =
                    x /
                    (float)(count - 1);

                samples[index++] =
                    new Vector2(
                        Mathf.Lerp(
                            minX,
                            maxX,
                            tx),
                        Mathf.Lerp(
                            minZ,
                            maxZ,
                            tz));
            }
        }

        return samples;
    }

    private static bool TryGetFirstTriangleGeometry(
        TerrainNodeElevationSource source,
        out Vector2 a,
        out Vector2 b,
        out Vector2 c,
        out Vector2 centroid,
        out string errorMessage)
    {
        a = Vector2.zero;
        b = Vector2.zero;
        c = Vector2.zero;
        centroid = Vector2.zero;
        errorMessage = "";

        if (!TryBuildTopology(
            source,
            out TerrainNodeElevationTopology topology,
            out errorMessage))
        {
            return false;
        }

        if (
            topology.Kind !=
                TerrainNodeElevationTopologyKind.Triangulated ||
            topology.TriangleCount <= 0)
        {
            errorMessage =
                "Validation fixture did not produce a triangulated topology.";
            return false;
        }

        TerrainNodeElevationTopologyTriangle triangle =
            topology.Triangles[0];

        a =
            topology.Vertices[
                triangle.VertexA].PositionXZ;

        b =
            topology.Vertices[
                triangle.VertexB].PositionXZ;

        c =
            topology.Vertices[
                triangle.VertexC].PositionXZ;

        centroid =
            (a + b + c) /
            3f;

        return true;
    }

    private static bool TryBuildTopology(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        TerrainNodeElevationTopologyCache cache =
            new TerrainNodeElevationTopologyCache();

        return cache.TryGetOrBuild(
            source,
            out topology,
            out errorMessage);
    }

    private static bool TryFindSharedEdge(
        TerrainNodeElevationTopology topology,
        out int vertexA,
        out int vertexB)
    {
        vertexA = -1;
        vertexB = -1;

        if (
            topology == null ||
            topology.TriangleCount < 2)
        {
            return false;
        }

        Dictionary<string, int> counts =
            new Dictionary<string, int>();

        Dictionary<string, Vector2Int> edges =
            new Dictionary<string, Vector2Int>();

        for (int triangleIndex = 0;
            triangleIndex < topology.TriangleCount;
            triangleIndex++)
        {
            TerrainNodeElevationTopologyTriangle triangle =
                topology.Triangles[triangleIndex];

            CountEdge(
                triangle.VertexA,
                triangle.VertexB,
                counts,
                edges);

            CountEdge(
                triangle.VertexB,
                triangle.VertexC,
                counts,
                edges);

            CountEdge(
                triangle.VertexC,
                triangle.VertexA,
                counts,
                edges);
        }

        foreach (
            KeyValuePair<string, int> pair
            in counts)
        {
            if (pair.Value != 2)
            {
                continue;
            }

            Vector2Int edge =
                edges[pair.Key];

            vertexA = edge.x;
            vertexB = edge.y;
            return true;
        }

        return false;
    }

    private static void CountEdge(
        int a,
        int b,
        IDictionary<string, int> counts,
        IDictionary<string, Vector2Int> edges)
    {
        int first =
            Mathf.Min(a, b);

        int second =
            Mathf.Max(a, b);

        string key =
            first.ToString() +
            ":" +
            second.ToString();

        if (counts.TryGetValue(
            key,
            out int count))
        {
            counts[key] =
                count + 1;
        }
        else
        {
            counts.Add(
                key,
                1);

            edges.Add(
                key,
                new Vector2Int(
                    first,
                    second));
        }
    }

    private static TerrainNodeElevationSource CreateSource(
        params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        if (nodes != null)
        {
            for (int index = 0;
                index < nodes.Length;
                index++)
            {
                TerrainElevationNode node =
                    new TerrainElevationNode();

                node.SetPositionXZInternal(
                    nodes[index].Position);

                node.SetElevationInternal(
                    nodes[index].Elevation);

                source.AddNodeInternal(
                    node);
            }
        }

        source.RepairNodeStableIds();
        return source;
    }

    private static TerrainNodeElevationSource CreateSmoothSource(
        params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            CreateSource(nodes);

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode
                .TriangulatedSmooth);

        return source;
    }

    private static TerrainNodeElevationSource CreateLinearSource(
        params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            CreateSource(nodes);

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode
                .TriangulatedLinear);

        return source;
    }

    private static TerrainAuthoringData CreateDataFromSource(
        TerrainNodeElevationSource source)
    {
        TerrainAuthoringData data =
            ScriptableObject.CreateInstance<TerrainAuthoringData>();

        data.sourceMode =
            TerrainHeightSourceMode.Flat;

        data.SetRegionalElevationSourceInternal(
            source);

        return data;
    }

    private static void ClearFixture(
        TerrainAuthoringData data)
    {
        if (data == null)
        {
            return;
        }

        TerrainRegionalElevationChangeTracker.Forget(
            data);

        Undo.ClearUndo(
            data);

        UnityEngine.Object.DestroyImmediate(
            data);
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b)
    {
        if (
            a == null ||
            b == null ||
            a.Count != b.Count)
        {
            return false;
        }

        for (int index = 0;
            index < a.Count;
            index++)
        {
            if (!string.Equals(
                a[index],
                b[index],
                StringComparison.Ordinal))
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
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Regional Elevation Triangulated Smooth GPU Validation");

        builder.AppendLine(
            "==============================================================");

        builder.AppendLine();

        int passed = 0;
        int failed = 0;
        int blocked = 0;

        for (int index = 0;
            index < results.Count;
            index++)
        {
            ValidationResult result =
                results[index];

            string label =
                result.Outcome
                    .ToString()
                    .ToUpperInvariant();

            builder.AppendLine(
                label +
                " - " +
                result.Name);

            builder.AppendLine(
                "       " +
                result.Details);

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

        builder.AppendLine(
            "--------------------------------------------------------------");

        builder.AppendLine(
            passed +
            " passed");

        builder.AppendLine(
            failed +
            " failed");

        builder.AppendLine(
            blocked +
            " blocked");

        builder.AppendLine();

        bool validationPassed =
            failed == 0 &&
            blocked == 0;

        builder.AppendLine(
            validationPassed
                ? "Regional elevation Triangulated Smooth GPU validation: PASSED"
                : "Regional elevation Triangulated Smooth GPU validation: FAILED");

        if (validationPassed)
        {
            Debug.Log(
                builder.ToString());
        }
        else
        {
            Debug.LogError(
                builder.ToString());
        }
    }
}
