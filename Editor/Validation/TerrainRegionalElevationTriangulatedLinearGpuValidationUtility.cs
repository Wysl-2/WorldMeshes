using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Package I4 validation for production Triangulated Linear GPU composition.
 *
 * Package I3 CPU evaluation is the semantic reference. GPU parity fixtures are
 * transient and use the production I2 topology cache plus the same production
 * compute shader used by TerrainHeightCompositor tile composition.
 */
public static class TerrainRegionalElevationTriangulatedLinearGpuValidationUtility
{
    private const float HeightAbsoluteTolerance = 0.001f;
    private const float HeightRelativeTolerance = 0.000001f;

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
    private static readonly List<string> realSelectedIdsBefore =
        new List<string>();
    private static string realPrimaryBefore = "";

    public static bool IsRunning =>
        validationRunning || validationScheduled;

    public static void ValidateTriangulatedLinearGpu()
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
                "Compute shaders, current WorldSettings/TerrainAuthoringData, Package I1-I3 state, and the production Linear compute shader are available.");

            ValidateCapabilityAndCompositionResolution();
            ValidateBasicTriangleParity();
            ValidateLinearTileCompositionParity();
            ValidateSharedEdgeAndIrregularParity();
            ValidateHullExteriorParity();
            ValidateSingleAndTwoNodeParity();
            ValidateCollinearParity();
            ValidateElevationRangeParity();
            ValidateTopologyCacheReuse();
            ValidateIdwGpuRegression();
            ValidateFailureBoundaries();
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

        if (worldSettings == null || realAuthoringData == null)
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

        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            errorMessage =
                "Initialize the committed authoring heightfield before running Package I4 validation.";
            return false;
        }

        ComputeShader linearShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                TerrainHeightCompositor.TriangulatedLinearComputeShaderAssetPath);

        if (linearShader == null)
        {
            errorMessage =
                "The Package I4 production Triangulated Linear compute shader could not be loaded.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore = realAuthoringData.authoringRevision;
        realCommittedBefore =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);
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
        bool capabilities =
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
            !TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        TerrainNodeElevationSource linear = CreateLinearSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 200f));

        TerrainAuthoringData data = CreateDataFromSource(linear);

        bool resolverSupported =
            TerrainRegionalElevationCompositionUtility.TryResolveNodeSource(
                data,
                out TerrainNodeElevationSource resolved,
                out bool required,
                out string resolverError) &&
            ReferenceEquals(resolved, linear) &&
            required &&
            string.IsNullOrEmpty(resolverError);

        bool passed = capabilities && resolverSupported;

        AddResult(
            "CPU/GPU capability matrix and production composition resolution enable Linear",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "IDW and Linear are CPU/GPU-ready, Smooth is CPU-ready but remains GPU-pending, and production composition accepts a Linear node source."
                : resolverError);

        ClearFixture(data);
    }

    private static void ValidateBasicTriangleParity()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 200f));

        Vector2[] samples =
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f),
            new Vector2(10f / 3f, 10f / 3f),
            new Vector2(5f, 0f),
            new Vector2(0f, 5f),
            new Vector2(5f, 5f),
            new Vector2(2f, 3f)
        };

        bool passed = TryValidateCpuGpuParity(
            source,
            samples,
            out string errorMessage);

        AddResult(
            "Basic triangle vertices, edges, centroid, and interior samples match CPU Linear",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Production GPU barycentric evaluation matches Package I3 CPU semantics at corners, edge midpoints, centroid, and an irregular interior point."
                : errorMessage);
    }

    private static void ValidateLinearTileCompositionParity()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(10f, 10f), 250f),
            new NodeSpec(new Vector2(0f, 10f), 50f),
            new NodeSpec(new Vector2(5f, 5f), 140f));

        TerrainAuthoringData data = CreateDataFromSource(source);
        const int samplesPerSide = 9;
        const float tileWorldSize = 10f;
        float sampleSpacing = tileWorldSize / (samplesPerSide - 1);

        RenderTexture target = null;
        bool composed = false;
        bool parity = false;
        string error = "";

        try
        {
            target = new RenderTexture(
                samplesPerSide,
                samplesPerSide,
                0,
                RenderTextureFormat.RFloat)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };

            if (!target.Create())
            {
                error =
                    "Could not create the temporary RFloat Linear validation target.";
            }
            else
            {
                using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
                {
                    composed = compositor.TryComposeTile(
                        target,
                        Vector2Int.zero,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        new Vector2(tileWorldSize, tileWorldSize),
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
                            "GPU readback failed for the Linear production tile target.";
                    }
                    else
                    {
                        var gpuData = request.GetData<float>();
                        parity = true;

                        for (int z = 0; z < samplesPerSide && parity; z++)
                        {
                            for (int x = 0; x < samplesPerSide; x++)
                            {
                                Vector2 sample =
                                    new Vector2(
                                        x * sampleSpacing,
                                        z * sampleSpacing);

                                if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                                    source,
                                    sample,
                                    out float cpuHeight,
                                    out string cpuError))
                                {
                                    parity = false;
                                    error = cpuError;
                                    break;
                                }

                                int gpuIndex = z * samplesPerSide + x;
                                float gpuHeight = gpuData[gpuIndex];

                                if (!HeightsMatch(cpuHeight, gpuHeight))
                                {
                                    parity = false;
                                    error =
                                        $"Linear production-tile CPU/GPU mismatch at " +
                                        $"({sample.x}, {sample.y}): CPU={cpuHeight}, " +
                                        $"GPU={gpuHeight}, tolerance={AllowedTolerance(cpuHeight)}.";
                                    break;
                                }
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
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            ClearFixture(data);
        }

        bool passed = composed && parity;

        AddResult(
            "Production Linear tile composition matches Package I3 CPU semantics",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "The actual TerrainHeightCompositor Linear tile path, world-sample addressing, RFloat output, and GPU readback match CPU Linear over the full validation tile."
                : error);
    }

    private static void ValidateSharedEdgeAndIrregularParity()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(12f, 1f), 110f),
            new NodeSpec(new Vector2(11f, 11f), 230f),
            new NodeSpec(new Vector2(1f, 13f), 70f),
            new NodeSpec(new Vector2(5f, 6f), 160f));

        bool built = TerrainNodeElevationTriangulationUtility.TryBuild(
            source,
            out TerrainNodeElevationTopology topology,
            out string topologyError);

        TerrainNodeElevationTopologyEdge internalEdge =
            default(TerrainNodeElevationTopologyEdge);
        bool foundInternalEdge = false;

        if (built)
        {
            for (int index = 0; index < topology.EdgeCount; index++)
            {
                if (topology.Edges[index].TriangleReferenceCount == 2)
                {
                    internalEdge = topology.Edges[index];
                    foundInternalEdge = true;
                    break;
                }
            }
        }

        bool parity = false;
        string parityError = "";

        if (built && foundInternalEdge)
        {
            Vector2 a = topology.Vertices[internalEdge.VertexA].PositionXZ;
            Vector2 b = topology.Vertices[internalEdge.VertexB].PositionXZ;

            Vector2[] samples =
            {
                a,
                Vector2.Lerp(a, b, 0.25f),
                Vector2.Lerp(a, b, 0.5f),
                Vector2.Lerp(a, b, 0.75f),
                b,
                new Vector2(4.25f, 4.75f),
                new Vector2(7.5f, 7f)
            };

            parity = TryValidateCpuGpuParity(
                source,
                samples,
                out parityError);
        }

        bool passed = built && foundInternalEdge && parity;

        AddResult(
            "Shared edges and an irregular free-position layout preserve deterministic CPU/GPU parity",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Samples along one canonical internal edge and irregular interior locations match CPU Linear without grid assumptions or smoothing."
                : topologyError + " " + parityError);
    }

    private static void ValidateHullExteriorParity()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(10f, 10f), 250f),
            new NodeSpec(new Vector2(0f, 10f), 50f));

        Vector2 tieSample = new Vector2(-5f, -5f);

        Vector2[] samples =
        {
            new Vector2(5f, -3f),
            new Vector2(5f, -300f),
            new Vector2(-4f, 10f),
            new Vector2(15f, 15f),
            tieSample,
            tieSample
        };

        bool parity = TryValidateCpuGpuParity(
            source,
            samples,
            out string parityError);

        bool bounded =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                new Vector2(5f, -300f),
                out float farHeight,
                out _) &&
            NearlyEqual(farHeight, 50f);

        bool passed = parity && bounded;

        AddResult(
            "Convex-hull exterior projection, far samples, corners, and tie cases match CPU Linear",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "GPU exterior evaluation uses finite hull-segment projection with endpoint clamping and deterministic sequential edge selection; triangle planes are not extrapolated."
                : parityError);
    }

    private static void ValidateSingleAndTwoNodeParity()
    {
        TerrainNodeElevationSource single = CreateLinearSource(
            new NodeSpec(new Vector2(3f, -7f), 200f));

        bool singleParity = TryValidateCpuGpuParity(
            single,
            new[]
            {
                new Vector2(3f, -7f),
                Vector2.zero,
                new Vector2(-500f, 900f),
                new Vector2(10000f, -10000f)
            },
            out string singleError);

        TerrainNodeElevationSource segment = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f));

        bool segmentParity = TryValidateCpuGpuParity(
            segment,
            new[]
            {
                new Vector2(-10f, 0f),
                new Vector2(0f, 0f),
                new Vector2(2.5f, 0f),
                new Vector2(5f, 100f),
                new Vector2(7.5f, -20f),
                new Vector2(10f, 0f),
                new Vector2(20f, 0f)
            },
            out string segmentError);

        bool passed = singleParity && segmentParity;

        AddResult(
            "Single-point and two-node projection policies match Package I3 on GPU",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Single-node height is constant; two-node samples use finite segment projection with perpendicular invariance and endpoint clamping."
                : singleError + " " + segmentError);
    }

    private static void ValidateCollinearParity()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(25f, 0f), 40f),
            new NodeSpec(new Vector2(40f, 0f), 160f));

        Vector2[] samples =
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
        };

        bool passed = TryValidateCpuGpuParity(
            source,
            samples,
            out string errorMessage);

        AddResult(
            "Collinear projected piecewise-linear interpolation matches CPU including endpoint clamps",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "GPU collinear evaluation follows the deterministic I2 vertex order and is invariant to perpendicular distance."
                : errorMessage);
    }

    private static void ValidateElevationRangeParity()
    {
        TerrainNodeElevationSource negative = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), -500f),
            new NodeSpec(new Vector2(10f, 0f), -100f),
            new NodeSpec(new Vector2(0f, 10f), 300f));

        bool negativeParity = TryValidateCpuGpuParity(
            negative,
            new[]
            {
                new Vector2(2f, 2f),
                new Vector2(5f, 0f),
                new Vector2(5f, -20f)
            },
            out string negativeError);

        TerrainNodeElevationSource large = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 1000000f),
            new NodeSpec(new Vector2(100f, 0f), 1500000f),
            new NodeSpec(new Vector2(0f, 100f), 2000000f));

        bool largeParity = TryValidateCpuGpuParity(
            large,
            new[]
            {
                new Vector2(25f, 25f),
                new Vector2(50f, 0f),
                new Vector2(50f, -100f)
            },
            out string largeError);

        bool passed = negativeParity && largeParity;

        AddResult(
            "Negative and large finite elevations remain finite and within documented CPU/GPU tolerance",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "RFloat GPU Linear evaluation supports negative and large finite regional elevations without NaN/Infinity output."
                : negativeError + " " + largeError);
    }

    private static void ValidateTopologyCacheReuse()
    {
        TerrainNodeElevationSource source = CreateLinearSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(100f, 100f), 200f),
            new NodeSpec(new Vector2(0f, 100f), 50f));

        bool first;
        bool elevationReuse = false;
        bool positionRebuild = false;
        string error = "";

        using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
        {
            first = compositor.TryEvaluateTriangulatedLinearGpuSamples(
                source,
                new[] { new Vector2(25f, 25f) },
                out _,
                out error);

            int afterFirst = compositor.TriangulatedLinearTopologyRebuildCount;

            source.Nodes[0].SetElevationInternal(300f);

            bool afterElevation =
                compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    source,
                    new[] { new Vector2(25f, 25f) },
                    out _,
                    out error);

            elevationReuse =
                afterElevation &&
                afterFirst == 1 &&
                compositor.TriangulatedLinearTopologyRebuildCount == afterFirst;

            source.Nodes[0].SetPositionXZInternal(new Vector2(5f, 5f));

            bool afterPosition =
                compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    source,
                    new[] { new Vector2(25f, 25f) },
                    out _,
                    out error);

            positionRebuild =
                afterPosition &&
                compositor.TriangulatedLinearTopologyRebuildCount == afterFirst + 1;
        }

        bool passed = first && elevationReuse && positionRebuild;

        AddResult(
            "Linear GPU preparation reuses position-only topology and rebuilds lazily after geometry changes",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Elevation-only changes reused I2 topology; a later XZ position change rebuilt topology on the next actual GPU evaluation."
                : error);
    }

    private static void ValidateIdwGpuRegression()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f));

        TerrainAuthoringData data = CreateDataFromSource(source);
        const int samplesPerSide = 5;
        const float tileWorldSize = 10f;
        float sampleSpacing = tileWorldSize / (samplesPerSide - 1);

        bool composed = false;
        bool parity = false;
        string error = "";
        RenderTexture target = null;

        try
        {
            target = new RenderTexture(
                samplesPerSide,
                samplesPerSide,
                0,
                RenderTextureFormat.RFloat)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };

            if (!target.Create())
            {
                error = "Could not create the temporary RFloat IDW validation target.";
            }
            else
            {
                using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
                {
                    composed = compositor.TryComposeTile(
                        target,
                        Vector2Int.zero,
                        0,
                        samplesPerSide,
                        sampleSpacing,
                        tileWorldSize,
                        new Vector2(tileWorldSize, tileWorldSize),
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
                        error = "GPU readback failed for the IDW regression target.";
                    }
                    else
                    {
                        var gpuData = request.GetData<float>();
                        parity = true;

                        for (int x = 0; x < samplesPerSide; x++)
                        {
                            Vector2 sample = new Vector2(x * sampleSpacing, 0f);

                            if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                                source,
                                sample,
                                out float cpuHeight,
                                out string cpuError))
                            {
                                parity = false;
                                error = cpuError;
                                break;
                            }

                            float gpuHeight = gpuData[x];
                            if (!HeightsMatch(cpuHeight, gpuHeight))
                            {
                                parity = false;
                                error =
                                    $"IDW CPU/GPU mismatch at x={sample.x}: " +
                                    $"CPU={cpuHeight}, GPU={gpuHeight}.";
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
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            ClearFixture(data);
        }

        bool passed = composed && parity;

        AddResult(
            "Existing IDW GPU compositor remains numerically compatible with CPU IDW samples",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "The unchanged production IDW kernel matched CPU IDW at exact endpoints and multiple interior tile samples after I4 dispatch integration."
                : error);
    }

    private static void ValidateFailureBoundaries()
    {
        TerrainNodeElevationSource coincident = CreateLinearSource(
            new NodeSpec(new Vector2(50f, 50f), 10f),
            new NodeSpec(new Vector2(50f, 50f), 30f),
            new NodeSpec(new Vector2(100f, 0f), 100f));

        bool linearRejected;
        string linearError;

        using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
        {
            linearRejected =
                !compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    coincident,
                    new[] { new Vector2(50f, 50f) },
                    out _,
                    out linearError) &&
                (linearError.Contains("coincident") ||
                 linearError.Contains("separation"));
        }

        TerrainNodeElevationSource idwCoincident = CreateSource(
            new NodeSpec(new Vector2(50f, 50f), 10f),
            new NodeSpec(new Vector2(50f, 50f), 30f),
            new NodeSpec(new Vector2(100f, 0f), 100f));

        bool idwStillValid =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                idwCoincident,
                new Vector2(50f, 50f),
                out float idwHeight,
                out _) &&
            Mathf.Abs(idwHeight - 20f) <= 0.0001f;

        TerrainNodeElevationSource empty = CreateLinearSource();
        bool emptyRejected;
        string emptyError;

        using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
        {
            emptyRejected =
                !compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    empty,
                    new[] { Vector2.zero },
                    out _,
                    out emptyError);
        }

        TerrainNodeElevationSource smooth = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 50f));
        smooth.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth);
        TerrainAuthoringData smoothData = CreateDataFromSource(smooth);

        bool smoothRejected =
            !TerrainRegionalElevationCompositionUtility.TryResolveNodeSource(
                smoothData,
                out _,
                out _,
                out string smoothError) &&
            !TerrainNodeElevationInterpolationModeUtility.SupportsGpuComposition(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        bool invalidSampleRejected;
        string invalidSampleError;
        TerrainNodeElevationSource validLinear = CreateLinearSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(10f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 10f), 50f));

        using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
        {
            invalidSampleRejected =
                !compositor.TryEvaluateTriangulatedLinearGpuSamples(
                    validLinear,
                    new[] { new Vector2(float.NaN, 0f) },
                    out _,
                    out invalidSampleError);
        }

        bool passed =
            linearRejected &&
            idwStillValid &&
            emptyRejected &&
            smoothRejected &&
            invalidSampleRejected;

        AddResult(
            "Linear failure boundaries remain explicit without IDW or Smooth fallback",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Coincident/empty/invalid Linear inputs fail before valid GPU output, coincident IDW remains legal, and Smooth remains GPU-unsupported."
                : linearError + " " + emptyError + " " + smoothError + " " + invalidSampleError);

        ClearFixture(smoothData);
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
            realPrimaryBefore == primaryAfter &&
            SequenceEqual(realSelectedIdsBefore, selectedAfter);

        AddResult(
            "Real authoring and Package 7 selection state remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Transient Package I4 GPU validation did not mutate the real regional source, revision, committed/overall identity, interpolation mode, or selection state."
                : "Real WorldMeshes authoring or regional selection state changed during Package I4 validation.");
    }

    private static bool TryValidateCpuGpuParity(
        TerrainNodeElevationSource source,
        IReadOnlyList<Vector2> samples,
        out string errorMessage)
    {
        errorMessage = "";

        if (source == null || samples == null || samples.Count <= 0)
        {
            errorMessage = "CPU/GPU parity fixture is invalid.";
            return false;
        }

        float[] cpu = new float[samples.Count];

        for (int index = 0; index < samples.Count; index++)
        {
            if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                samples[index],
                out cpu[index],
                out string cpuError))
            {
                errorMessage =
                    $"CPU Linear evaluation failed at sample {index}. " +
                    cpuError;
                return false;
            }
        }

        using (TerrainHeightCompositor compositor = new TerrainHeightCompositor())
        {
            if (!compositor.TryEvaluateTriangulatedLinearGpuSamples(
                source,
                samples,
                out float[] gpu,
                out errorMessage))
            {
                return false;
            }

            if (gpu == null || gpu.Length != cpu.Length)
            {
                errorMessage =
                    "GPU Linear evaluator returned an unexpected sample count.";
                return false;
            }

            for (int index = 0; index < cpu.Length; index++)
            {
                if (!HeightsMatch(cpu[index], gpu[index]))
                {
                    errorMessage =
                        $"CPU/GPU Linear mismatch at sample {index} " +
                        $"({samples[index].x}, {samples[index].y}): " +
                        $"CPU={cpu[index]}, GPU={gpu[index]}, " +
                        $"tolerance={AllowedTolerance(cpu[index])}.";
                    return false;
                }
            }
        }

        return true;
    }

    private static float AllowedTolerance(float expected)
    {
        return
            HeightAbsoluteTolerance +
            HeightRelativeTolerance * Mathf.Max(1f, Mathf.Abs(expected));
    }

    private static bool HeightsMatch(float expected, float actual)
    {
        return
            !float.IsNaN(expected) &&
            !float.IsInfinity(expected) &&
            !float.IsNaN(actual) &&
            !float.IsInfinity(actual) &&
            Mathf.Abs(expected - actual) <= AllowedTolerance(expected);
    }

    private static bool NearlyEqual(float a, float b)
    {
        return HeightsMatch(a, b);
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
                TerrainElevationNode node = new TerrainElevationNode();
                node.SetPositionXZInternal(nodes[index].Position);
                node.SetElevationInternal(nodes[index].Elevation);
                source.AddNodeInternal(node);
            }
        }

        source.RepairNodeStableIds();
        return source;
    }

    private static TerrainNodeElevationSource CreateLinearSource(
        params NodeSpec[] nodes)
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

        builder.AppendLine(
            "WorldMeshes Regional Elevation Triangulated Linear GPU Validation");
        builder.AppendLine(
            "==============================================================");
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

        builder.AppendLine("--------------------------------------------------------------");
        builder.AppendLine(passed + " passed");
        builder.AppendLine(failed + " failed");
        builder.AppendLine(blocked + " blocked");
        builder.AppendLine();

        bool validationPassed = failed == 0 && blocked == 0;
        builder.AppendLine(
            validationPassed
                ? "Regional elevation Triangulated Linear GPU validation: PASSED"
                : "Regional elevation Triangulated Linear GPU validation: FAILED");

        if (validationPassed)
        {
            Debug.Log(builder.ToString());
        }
        else
        {
            Debug.LogError(builder.ToString());
        }
    }
}
