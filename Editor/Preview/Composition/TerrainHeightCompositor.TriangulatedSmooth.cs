using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package I7 GPU execution for Triangulated Smooth regional elevation.
 *
 * I2 remains the topology authority, I5 remains the gradient authority, and I6
 * remains the reduced-HCT coefficient authority. This partial class only
 * prepares/uploads those derived CPU products and dispatches the production
 * Smooth compute shader.
 */
public sealed partial class TerrainHeightCompositor
{
    public const string TriangulatedSmoothComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/Authoring/" +
        "TerrainRegionalElevationSmooth.compute";

    private const string TriangulatedSmoothComposeKernelName =
        "ComposeTriangulatedSmoothRegionalElevation";

    private const string TriangulatedSmoothSampleKernelName =
        "EvaluateTriangulatedSmoothSamples";

    private const int TriangulatedSmoothGradientStride =
        sizeof(float) * 2;

    private const int TriangulatedSmoothCoefficientStride =
        sizeof(float);

    private const int TriangulatedSmoothSamplePositionStride =
        sizeof(float) * 2;

    private const int TriangulatedSmoothSampleResultStride =
        sizeof(float);

    private const int TriangulatedSmoothCoefficientsPerSubpatch = 10;
    private const int TriangulatedSmoothSubpatchesPerTriangle = 3;
    private const int TriangulatedSmoothCoefficientsPerTriangle =
        TriangulatedSmoothCoefficientsPerSubpatch *
        TriangulatedSmoothSubpatchesPerTriangle;

    private ComputeShader triangulatedSmoothComputeShader;

    private int triangulatedSmoothComposeKernel = -1;
    private int triangulatedSmoothSampleKernel = -1;

    private uint triangulatedSmoothComposeThreadGroupSizeX;
    private uint triangulatedSmoothComposeThreadGroupSizeY;
    private uint triangulatedSmoothSampleThreadGroupSizeX;

    private readonly TerrainNodeElevationGradientCache
        triangulatedSmoothGradientCache =
            new TerrainNodeElevationGradientCache();

    private readonly TerrainNodeElevationSmoothPatchCache
        triangulatedSmoothPatchCache =
            new TerrainNodeElevationSmoothPatchCache();

    private TerrainNodeElevationTopology triangulatedSmoothPreparedTopology;
    private TerrainNodeElevationGradientData triangulatedSmoothPreparedGradients;
    private TerrainNodeElevationSmoothPatchData triangulatedSmoothPreparedPatches;

    private ComputeBuffer triangulatedSmoothGradientBuffer;
    private ComputeBuffer triangulatedSmoothCoefficientBuffer;

    private Vector2[] triangulatedSmoothGradientUploadData;
    private float[] triangulatedSmoothCoefficientUploadData;

    private int triangulatedSmoothNumericUploadCount;
    private int triangulatedSmoothBufferAllocationCount;

    internal int TriangulatedSmoothTopologyRebuildCount =>
        triangulatedLinearTopologyCache.RebuildCount;

    internal int TriangulatedSmoothGradientRebuildCount =>
        triangulatedSmoothGradientCache.RebuildCount;

    internal int TriangulatedSmoothPatchRebuildCount =>
        triangulatedSmoothPatchCache.RebuildCount;

    internal int TriangulatedSmoothNumericUploadCount =>
        triangulatedSmoothNumericUploadCount;

    internal int TriangulatedSmoothBufferAllocationCount =>
        triangulatedSmoothBufferAllocationCount;

    internal int TriangulatedSmoothPreparedGradientCount =>
        triangulatedSmoothPreparedGradients != null
            ? triangulatedSmoothPreparedGradients.GradientCount
            : 0;

    internal int TriangulatedSmoothPreparedPatchCount =>
        triangulatedSmoothPreparedPatches != null
            ? triangulatedSmoothPreparedPatches.PatchCount
            : 0;

    private bool TryDispatchTriangulatedSmoothRegionalElevation(
        RenderTexture heightCache,
        Vector2Int tileCoordinate,
        Vector2 tileWorldOriginXZ,
        int sliceIndex,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ,
        TerrainNodeElevationSource nodeSource,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TryPrepareTriangulatedSmoothGpuData(
            nodeSource,
            out TerrainNodeElevationTopology topology,
            out _,
            out _,
            out errorMessage))
        {
            return false;
        }

        if (!TryPrepareTriangulatedSmoothShader(out errorMessage))
        {
            return false;
        }

        int groupsX = DivideRoundUpLinear(
            samplesPerSide,
            triangulatedSmoothComposeThreadGroupSizeX);

        int groupsY = DivideRoundUpLinear(
            samplesPerSide,
            triangulatedSmoothComposeThreadGroupSizeY);

        if (groupsX <= 0 || groupsY <= 0)
        {
            errorMessage =
                "The Triangulated Smooth regional compositor calculated an " +
                "invalid compute dispatch size.";
            return false;
        }

        try
        {
            SetTriangulatedSmoothDerivedParameters(
                triangulatedSmoothComposeKernel,
                topology);

            triangulatedSmoothComputeShader.SetInt(
                "_SamplesPerSide",
                samplesPerSide);

            triangulatedSmoothComputeShader.SetFloat(
                "_SampleSpacing",
                sampleSpacing);

            triangulatedSmoothComputeShader.SetFloat(
                "_TileWorldSize",
                tileWorldSize);

            triangulatedSmoothComputeShader.SetInts(
                "_TileCoordinate",
                tileCoordinate.x,
                tileCoordinate.y);

            triangulatedSmoothComputeShader.SetVector(
                "_TileWorldOriginXZ",
                new Vector4(
                    tileWorldOriginXZ.x,
                    tileWorldOriginXZ.y,
                    0f,
                    0f));

            triangulatedSmoothComputeShader.SetVector(
                "_WorldSizeXZ",
                new Vector4(
                    worldSizeXZ.x,
                    worldSizeXZ.y,
                    0f,
                    0f));

            triangulatedSmoothComputeShader.SetInt(
                "_TargetSlice",
                sliceIndex);

            triangulatedSmoothComputeShader.SetTexture(
                triangulatedSmoothComposeKernel,
                "_HeightCache",
                heightCache);

            triangulatedSmoothComputeShader.Dispatch(
                triangulatedSmoothComposeKernel,
                groupsX,
                groupsY,
                1);

            MarkRegionalElevationDispatchSucceeded();
        }
        catch (Exception exception)
        {
            errorMessage =
                "The Triangulated Smooth regional elevation pass could not " +
                $"be dispatched for tile ({tileCoordinate.x}, " +
                $"{tileCoordinate.y}) and cache slice {sliceIndex}.\n\n" +
                exception.Message;
            return false;
        }

        return true;
    }

    /*
     * Validation-only readback hook. Production composition never reads
     * Smooth samples back to the CPU.
     */
    internal bool TryEvaluateTriangulatedSmoothGpuSamples(
        TerrainNodeElevationSource nodeSource,
        IReadOnlyList<Vector2> samplePositions,
        out float[] heights,
        out string errorMessage)
    {
        heights = null;
        errorMessage = "";

        if (samplePositions == null || samplePositions.Count <= 0)
        {
            errorMessage =
                "Triangulated Smooth GPU sample validation requires at least " +
                "one sample position.";
            return false;
        }

        for (int index = 0; index < samplePositions.Count; index++)
        {
            if (!TerrainNodeElevationGeometryUtility.IsFinite(
                samplePositions[index]))
            {
                errorMessage =
                    $"Triangulated Smooth GPU sample {index} contains a " +
                    "non-finite position.";
                return false;
            }
        }

        if (!TryPrepareTriangulatedSmoothGpuData(
            nodeSource,
            out TerrainNodeElevationTopology topology,
            out _,
            out _,
            out errorMessage))
        {
            return false;
        }

        if (!TryPrepareTriangulatedSmoothShader(out errorMessage))
        {
            return false;
        }

        ComputeBuffer samplePositionBuffer = null;
        ComputeBuffer sampleResultBuffer = null;

        try
        {
            Vector2[] positions =
                new Vector2[samplePositions.Count];

            for (int index = 0; index < positions.Length; index++)
            {
                positions[index] = samplePositions[index];
            }

            samplePositionBuffer =
                new ComputeBuffer(
                    positions.Length,
                    TriangulatedSmoothSamplePositionStride,
                    ComputeBufferType.Structured);

            sampleResultBuffer =
                new ComputeBuffer(
                    positions.Length,
                    TriangulatedSmoothSampleResultStride,
                    ComputeBufferType.Structured);

            samplePositionBuffer.SetData(positions);

            SetTriangulatedSmoothDerivedParameters(
                triangulatedSmoothSampleKernel,
                topology);

            triangulatedSmoothComputeShader.SetBuffer(
                triangulatedSmoothSampleKernel,
                "_SmoothSamplePositions",
                samplePositionBuffer);

            triangulatedSmoothComputeShader.SetBuffer(
                triangulatedSmoothSampleKernel,
                "_SmoothSampleResults",
                sampleResultBuffer);

            triangulatedSmoothComputeShader.SetInt(
                "_SmoothSampleCount",
                positions.Length);

            int groupsX = DivideRoundUpLinear(
                positions.Length,
                triangulatedSmoothSampleThreadGroupSizeX);

            if (groupsX <= 0)
            {
                errorMessage =
                    "The Triangulated Smooth GPU sample evaluator calculated " +
                    "an invalid dispatch size.";
                return false;
            }

            triangulatedSmoothComputeShader.Dispatch(
                triangulatedSmoothSampleKernel,
                groupsX,
                1,
                1);

            heights = new float[positions.Length];
            sampleResultBuffer.GetData(heights);

            for (int index = 0; index < heights.Length; index++)
            {
                if (!IsFinite(heights[index]))
                {
                    heights = null;
                    errorMessage =
                        $"Triangulated Smooth GPU sample {index} produced a " +
                        "non-finite height.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            heights = null;
            errorMessage =
                "Triangulated Smooth GPU sample evaluation failed.\n\n" +
                exception.Message;
            return false;
        }
        finally
        {
            if (samplePositionBuffer != null)
            {
                samplePositionBuffer.Release();
            }

            if (sampleResultBuffer != null)
            {
                sampleResultBuffer.Release();
            }
        }
    }

    private bool TryPrepareTriangulatedSmoothGpuData(
        TerrainNodeElevationSource nodeSource,
        out TerrainNodeElevationTopology topology,
        out TerrainNodeElevationGradientData gradients,
        out TerrainNodeElevationSmoothPatchData patches,
        out string errorMessage)
    {
        topology = null;
        gradients = null;
        patches = null;
        errorMessage = "";

        if (nodeSource == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        if (
            nodeSource.InterpolationMode !=
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth)
        {
            errorMessage =
                "Triangulated Smooth GPU preparation received interpolation " +
                "mode '" +
                TerrainNodeElevationInterpolationModeUtility.GetDisplayName(
                    nodeSource.InterpolationMode) +
                "'.";
            return false;
        }

        /*
         * Reuse Package I4's canonical I2 geometry upload. The Linear shader is
         * not dispatched here; only its topology cache/buffers are shared.
         */
        if (!TryPrepareTriangulatedLinearGpuData(
            nodeSource,
            out topology,
            out errorMessage))
        {
            return false;
        }

        if (!triangulatedSmoothGradientCache.TryGetOrBuild(
            nodeSource,
            topology,
            out gradients,
            out string gradientError))
        {
            errorMessage =
                "Triangulated Smooth gradients could not be built. " +
                gradientError;
            return false;
        }

        if (!triangulatedSmoothPatchCache.TryGetOrBuild(
            nodeSource,
            topology,
            gradients,
            out patches,
            out string patchError))
        {
            errorMessage =
                "Triangulated Smooth HCT patches could not be built. " +
                patchError;
            return false;
        }

        int expectedPatchCount =
            topology.Kind == TerrainNodeElevationTopologyKind.Triangulated
                ? topology.TriangleCount
                : 0;

        if (
            gradients == null ||
            gradients.GradientCount != topology.VertexCount ||
            patches == null ||
            patches.PatchCount != expectedPatchCount)
        {
            errorMessage =
                "Triangulated Smooth derived data does not match the current " +
                "topology.";
            return false;
        }

        bool derivedChanged =
            !ReferenceEquals(
                topology,
                triangulatedSmoothPreparedTopology) ||
            !ReferenceEquals(
                gradients,
                triangulatedSmoothPreparedGradients) ||
            !ReferenceEquals(
                patches,
                triangulatedSmoothPreparedPatches);

        if (
            !derivedChanged &&
            HasTriangulatedSmoothGpuBuffers(
                gradients,
                patches))
        {
            return true;
        }

        if (!TryUploadTriangulatedSmoothDerivedData(
            gradients,
            patches,
            out errorMessage))
        {
            ReleaseTriangulatedSmoothGpuBuffers();
            return false;
        }

        triangulatedSmoothPreparedTopology = topology;
        triangulatedSmoothPreparedGradients = gradients;
        triangulatedSmoothPreparedPatches = patches;
        triangulatedSmoothNumericUploadCount++;

        return true;
    }

    private bool TryUploadTriangulatedSmoothDerivedData(
        TerrainNodeElevationGradientData gradients,
        TerrainNodeElevationSmoothPatchData patches,
        out string errorMessage)
    {
        errorMessage = "";

        int gradientCount = gradients.GradientCount;
        int coefficientCount =
            patches.PatchCount *
            TriangulatedSmoothCoefficientsPerTriangle;

        bool gradientAllocationRequired =
            triangulatedSmoothGradientBuffer == null ||
            triangulatedSmoothGradientBuffer.count !=
                Mathf.Max(1, gradientCount) ||
            triangulatedSmoothGradientBuffer.stride !=
                TriangulatedSmoothGradientStride;

        if (!TryEnsureComputeBuffer(
            ref triangulatedSmoothGradientBuffer,
            gradientCount,
            TriangulatedSmoothGradientStride,
            "Triangulated Smooth gradient",
            out errorMessage))
        {
            return false;
        }

        if (gradientAllocationRequired)
        {
            triangulatedSmoothBufferAllocationCount++;
        }

        bool coefficientAllocationRequired =
            triangulatedSmoothCoefficientBuffer == null ||
            triangulatedSmoothCoefficientBuffer.count !=
                Mathf.Max(1, coefficientCount) ||
            triangulatedSmoothCoefficientBuffer.stride !=
                TriangulatedSmoothCoefficientStride;

        if (!TryEnsureComputeBuffer(
            ref triangulatedSmoothCoefficientBuffer,
            coefficientCount,
            TriangulatedSmoothCoefficientStride,
            "Triangulated Smooth HCT coefficient",
            out errorMessage))
        {
            return false;
        }

        if (coefficientAllocationRequired)
        {
            triangulatedSmoothBufferAllocationCount++;
        }

        if (
            triangulatedSmoothGradientUploadData == null ||
            triangulatedSmoothGradientUploadData.Length != gradientCount)
        {
            triangulatedSmoothGradientUploadData =
                new Vector2[gradientCount];
        }

        for (int index = 0; index < gradientCount; index++)
        {
            if (!gradients.TryGetGradient(
                index,
                out TerrainNodeElevationGradient gradient))
            {
                errorMessage =
                    $"Triangulated Smooth gradient {index} could not be read.";
                return false;
            }

            Vector2 value = gradient.GradientXZ;

            if (!TerrainNodeElevationGeometryUtility.IsFinite(value))
            {
                errorMessage =
                    $"Triangulated Smooth gradient {index} is non-finite.";
                return false;
            }

            triangulatedSmoothGradientUploadData[index] = value;
        }

        if (
            triangulatedSmoothCoefficientUploadData == null ||
            triangulatedSmoothCoefficientUploadData.Length != coefficientCount)
        {
            triangulatedSmoothCoefficientUploadData =
                new float[coefficientCount];
        }

        int coefficientIndex = 0;

        for (int triangleIndex = 0;
            triangleIndex < patches.PatchCount;
            triangleIndex++)
        {
            if (!patches.TryGetTrianglePatch(
                triangleIndex,
                out TerrainNodeElevationSmoothTrianglePatch trianglePatch))
            {
                errorMessage =
                    $"Triangulated Smooth patch {triangleIndex} could not be read.";
                return false;
            }

            for (int subpatchIndex = 0;
                subpatchIndex < TriangulatedSmoothSubpatchesPerTriangle;
                subpatchIndex++)
            {
                TerrainNodeElevationSmoothCubicPatch cubic =
                    trianglePatch.GetSubpatch(subpatchIndex);

                for (int localIndex = 0;
                    localIndex < TriangulatedSmoothCoefficientsPerSubpatch;
                    localIndex++)
                {
                    float coefficient =
                        cubic.GetCoefficient(localIndex);

                    if (!IsFinite(coefficient))
                    {
                        errorMessage =
                            "Triangulated Smooth HCT data contains a " +
                            "non-finite coefficient.";
                        return false;
                    }

                    triangulatedSmoothCoefficientUploadData[
                        coefficientIndex++] =
                            coefficient;
                }
            }
        }

        try
        {
            if (gradientCount > 0)
            {
                triangulatedSmoothGradientBuffer.SetData(
                    triangulatedSmoothGradientUploadData);
            }

            if (coefficientCount > 0)
            {
                triangulatedSmoothCoefficientBuffer.SetData(
                    triangulatedSmoothCoefficientUploadData);
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not upload Triangulated Smooth derived data.\n\n" +
                exception.Message;
            return false;
        }

        return true;
    }

    private bool TryPrepareTriangulatedSmoothShader(
        out string errorMessage)
    {
        errorMessage = "";

        if (
            triangulatedSmoothComputeShader != null &&
            triangulatedSmoothComposeKernel >= 0 &&
            triangulatedSmoothSampleKernel >= 0 &&
            triangulatedSmoothComposeThreadGroupSizeX > 0 &&
            triangulatedSmoothComposeThreadGroupSizeY > 0 &&
            triangulatedSmoothSampleThreadGroupSizeX > 0)
        {
            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support compute shaders.";
            return false;
        }

        triangulatedSmoothComputeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                TriangulatedSmoothComputeShaderAssetPath);

        if (triangulatedSmoothComputeShader == null)
        {
            ResetTriangulatedSmoothShaderState();
            errorMessage =
                "The Triangulated Smooth regional elevation compute shader " +
                "could not be loaded:\n\n" +
                TriangulatedSmoothComputeShaderAssetPath;
            return false;
        }

        try
        {
            triangulatedSmoothComposeKernel =
                triangulatedSmoothComputeShader.FindKernel(
                    TriangulatedSmoothComposeKernelName);

            triangulatedSmoothSampleKernel =
                triangulatedSmoothComputeShader.FindKernel(
                    TriangulatedSmoothSampleKernelName);

            /*
             * These values mirror the shader's numthreads declarations. This
             * follows the established Linear path and avoids backend-specific
             * kernel metadata queries.
             */
            triangulatedSmoothComposeThreadGroupSizeX = 8;
            triangulatedSmoothComposeThreadGroupSizeY = 8;
            triangulatedSmoothSampleThreadGroupSizeX = 64;
        }
        catch (Exception exception)
        {
            ResetTriangulatedSmoothShaderState();
            errorMessage =
                "One or more required Triangulated Smooth compute kernels " +
                "could not be found.\n\nRequired:\n- " +
                TriangulatedSmoothComposeKernelName +
                "\n- " +
                TriangulatedSmoothSampleKernelName +
                "\n\n" +
                exception.Message;
            return false;
        }

        return true;
    }

    private bool HasTriangulatedSmoothGpuBuffers(
        TerrainNodeElevationGradientData gradients,
        TerrainNodeElevationSmoothPatchData patches)
    {
        if (gradients == null || patches == null)
        {
            return false;
        }

        int coefficientCount =
            patches.PatchCount *
            TriangulatedSmoothCoefficientsPerTriangle;

        return
            triangulatedSmoothGradientBuffer != null &&
            triangulatedSmoothGradientBuffer.count ==
                Mathf.Max(1, gradients.GradientCount) &&
            triangulatedSmoothGradientBuffer.stride ==
                TriangulatedSmoothGradientStride &&
            triangulatedSmoothCoefficientBuffer != null &&
            triangulatedSmoothCoefficientBuffer.count ==
                Mathf.Max(1, coefficientCount) &&
            triangulatedSmoothCoefficientBuffer.stride ==
                TriangulatedSmoothCoefficientStride;
    }

    private void SetTriangulatedSmoothDerivedParameters(
        int kernel,
        TerrainNodeElevationTopology topology)
    {
        triangulatedSmoothComputeShader.SetInt(
            "_LinearTopologyKind",
            (int)topology.Kind);

        triangulatedSmoothComputeShader.SetInt(
            "_LinearVertexCount",
            topology.VertexCount);

        triangulatedSmoothComputeShader.SetInt(
            "_LinearTriangleCount",
            topology.TriangleCount);

        triangulatedSmoothComputeShader.SetInt(
            "_LinearHullEdgeCount",
            topology.HullEdgeCount);

        triangulatedSmoothComputeShader.SetBuffer(
            kernel,
            "_LinearVertices",
            triangulatedLinearVertexBuffer);

        triangulatedSmoothComputeShader.SetBuffer(
            kernel,
            "_LinearTriangles",
            triangulatedLinearTriangleBuffer);

        triangulatedSmoothComputeShader.SetBuffer(
            kernel,
            "_LinearHullEdges",
            triangulatedLinearHullEdgeBuffer);

        triangulatedSmoothComputeShader.SetBuffer(
            kernel,
            "_SmoothGradients",
            triangulatedSmoothGradientBuffer);

        triangulatedSmoothComputeShader.SetBuffer(
            kernel,
            "_SmoothPatchCoefficients",
            triangulatedSmoothCoefficientBuffer);
    }

    /*
     * The editor preview tracks a conservative absolute range. HCT interiors
     * are bounded by their Bernstein coefficients. Smooth hull/degenerate
     * paths are cubic Hermite intervals, whose interior extrema are solved
     * exactly here.
     */
    private bool TryGetTriangulatedSmoothElevationRange(
        TerrainNodeElevationSource nodeSource,
        out float minimumHeight,
        out float maximumHeight,
        out string errorMessage)
    {
        minimumHeight = 0f;
        maximumHeight = 0f;
        errorMessage = "";

        if (!TryPrepareTriangulatedSmoothGpuData(
            nodeSource,
            out TerrainNodeElevationTopology topology,
            out TerrainNodeElevationGradientData gradients,
            out TerrainNodeElevationSmoothPatchData patches,
            out errorMessage))
        {
            return false;
        }

        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;

        for (int vertexIndex = 0;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            int sourceNodeIndex =
                topology.Vertices[vertexIndex].SourceNodeIndex;

            if (
                sourceNodeIndex < 0 ||
                sourceNodeIndex >= nodeSource.NodeCount)
            {
                errorMessage =
                    "Triangulated Smooth range calculation found an invalid " +
                    "source-node mapping.";
                return false;
            }

            double elevation =
                nodeSource.Nodes[sourceNodeIndex].Elevation;

            ExpandSmoothRange(
                elevation,
                ref minimum,
                ref maximum);
        }

        if (topology.Kind == TerrainNodeElevationTopologyKind.Triangulated)
        {
            for (int triangleIndex = 0;
                triangleIndex < patches.PatchCount;
                triangleIndex++)
            {
                if (!patches.TryGetTrianglePatch(
                    triangleIndex,
                    out TerrainNodeElevationSmoothTrianglePatch trianglePatch))
                {
                    errorMessage =
                        "Triangulated Smooth range calculation could not read " +
                        $"patch {triangleIndex}.";
                    return false;
                }

                for (int subpatchIndex = 0;
                    subpatchIndex < TriangulatedSmoothSubpatchesPerTriangle;
                    subpatchIndex++)
                {
                    TerrainNodeElevationSmoothCubicPatch cubic =
                        trianglePatch.GetSubpatch(subpatchIndex);

                    for (int coefficientIndex = 0;
                        coefficientIndex <
                            TriangulatedSmoothCoefficientsPerSubpatch;
                        coefficientIndex++)
                    {
                        double coefficient =
                            cubic.GetCoefficient(coefficientIndex);

                        if (!IsFiniteSmoothDouble(coefficient))
                        {
                            errorMessage =
                                "Triangulated Smooth range calculation found " +
                                "a non-finite HCT coefficient.";
                            return false;
                        }

                        ExpandSmoothRange(
                            coefficient,
                            ref minimum,
                            ref maximum);
                    }
                }
            }

            for (int edgeIndex = 0;
                edgeIndex < topology.HullEdgeCount;
                edgeIndex++)
            {
                TerrainNodeElevationTopologyEdge edge =
                    topology.HullEdges[edgeIndex];

                if (!TryExpandTriangulatedSmoothHermiteRange(
                    nodeSource,
                    topology,
                    gradients,
                    edge.VertexA,
                    edge.VertexB,
                    ref minimum,
                    ref maximum,
                    out errorMessage))
                {
                    return false;
                }
            }
        }
        else if (
            topology.Kind == TerrainNodeElevationTopologyKind.LineSegment)
        {
            if (!TryExpandTriangulatedSmoothHermiteRange(
                nodeSource,
                topology,
                gradients,
                0,
                1,
                ref minimum,
                ref maximum,
                out errorMessage))
            {
                return false;
            }
        }
        else if (
            topology.Kind == TerrainNodeElevationTopologyKind.Collinear)
        {
            for (int vertexIndex = 1;
                vertexIndex < topology.VertexCount;
                vertexIndex++)
            {
                if (!TryExpandTriangulatedSmoothHermiteRange(
                    nodeSource,
                    topology,
                    gradients,
                    vertexIndex - 1,
                    vertexIndex,
                    ref minimum,
                    ref maximum,
                    out errorMessage))
                {
                    return false;
                }
            }
        }

        if (
            !IsFiniteSmoothDouble(minimum) ||
            !IsFiniteSmoothDouble(maximum) ||
            maximum < minimum ||
            minimum < -float.MaxValue ||
            maximum > float.MaxValue)
        {
            errorMessage =
                "Triangulated Smooth conservative height range is invalid.";
            return false;
        }

        minimumHeight = (float)minimum;
        maximumHeight = (float)maximum;
        return true;
    }

    private static bool TryExpandTriangulatedSmoothHermiteRange(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        int vertexIndexA,
        int vertexIndexB,
        ref double minimum,
        ref double maximum,
        out string errorMessage)
    {
        errorMessage = "";

        if (
            vertexIndexA < 0 ||
            vertexIndexA >= topology.VertexCount ||
            vertexIndexB < 0 ||
            vertexIndexB >= topology.VertexCount)
        {
            errorMessage =
                "Triangulated Smooth range calculation received an invalid " +
                "Hermite vertex index.";
            return false;
        }

        TerrainNodeElevationTopologyVertex topologyA =
            topology.Vertices[vertexIndexA];

        TerrainNodeElevationTopologyVertex topologyB =
            topology.Vertices[vertexIndexB];

        if (
            topologyA.SourceNodeIndex < 0 ||
            topologyA.SourceNodeIndex >= source.NodeCount ||
            topologyB.SourceNodeIndex < 0 ||
            topologyB.SourceNodeIndex >= source.NodeCount ||
            !gradients.TryGetGradient(
                vertexIndexA,
                out TerrainNodeElevationGradient gradientA) ||
            !gradients.TryGetGradient(
                vertexIndexB,
                out TerrainNodeElevationGradient gradientB))
        {
            errorMessage =
                "Triangulated Smooth range calculation could not resolve " +
                "Hermite endpoint data.";
            return false;
        }

        double hA =
            source.Nodes[topologyA.SourceNodeIndex].Elevation;

        double hB =
            source.Nodes[topologyB.SourceNodeIndex].Elevation;

        double deltaX =
            (double)topologyB.PositionXZ.x -
            topologyA.PositionXZ.x;

        double deltaZ =
            (double)topologyB.PositionXZ.y -
            topologyA.PositionXZ.y;

        double mA =
            (double)gradientA.GradientX * deltaX +
            (double)gradientA.GradientZ * deltaZ;

        double mB =
            (double)gradientB.GradientX * deltaX +
            (double)gradientB.GradientZ * deltaZ;

        if (
            !IsFiniteSmoothDouble(hA) ||
            !IsFiniteSmoothDouble(hB) ||
            !IsFiniteSmoothDouble(mA) ||
            !IsFiniteSmoothDouble(mB))
        {
            errorMessage =
                "Triangulated Smooth range calculation found non-finite " +
                "Hermite endpoint data.";
            return false;
        }

        ExpandSmoothRange(hA, ref minimum, ref maximum);
        ExpandSmoothRange(hB, ref minimum, ref maximum);

        double cubic =
            2.0 * hA -
            2.0 * hB +
            mA +
            mB;

        double quadratic =
            -3.0 * hA +
            3.0 * hB -
            2.0 * mA -
            mB;

        double linear = mA;

        double derivativeA = 3.0 * cubic;
        double derivativeB = 2.0 * quadratic;
        double derivativeC = linear;

        const double epsilon = 1e-12;

        if (Math.Abs(derivativeA) <= epsilon)
        {
            if (Math.Abs(derivativeB) > epsilon)
            {
                double root =
                    -derivativeC / derivativeB;

                ExpandSmoothHermiteRoot(
                    root,
                    hA,
                    hB,
                    mA,
                    mB,
                    ref minimum,
                    ref maximum);
            }

            return true;
        }

        double discriminant =
            derivativeB * derivativeB -
            4.0 * derivativeA * derivativeC;

        if (discriminant < 0.0)
        {
            return true;
        }

        double sqrtDiscriminant =
            Math.Sqrt(
                Math.Max(
                    0.0,
                    discriminant));

        double denominator =
            2.0 * derivativeA;

        ExpandSmoothHermiteRoot(
            (-derivativeB - sqrtDiscriminant) / denominator,
            hA,
            hB,
            mA,
            mB,
            ref minimum,
            ref maximum);

        ExpandSmoothHermiteRoot(
            (-derivativeB + sqrtDiscriminant) / denominator,
            hA,
            hB,
            mA,
            mB,
            ref minimum,
            ref maximum);

        return true;
    }

    private static void ExpandSmoothHermiteRoot(
        double t,
        double hA,
        double hB,
        double mA,
        double mB,
        ref double minimum,
        ref double maximum)
    {
        if (
            !IsFiniteSmoothDouble(t) ||
            t <= 0.0 ||
            t >= 1.0)
        {
            return;
        }

        double t2 = t * t;
        double t3 = t2 * t;

        double h00 =
            2.0 * t3 -
            3.0 * t2 +
            1.0;

        double h10 =
            t3 -
            2.0 * t2 +
            t;

        double h01 =
            -2.0 * t3 +
            3.0 * t2;

        double h11 =
            t3 -
            t2;

        double value =
            h00 * hA +
            h10 * mA +
            h01 * hB +
            h11 * mB;

        if (IsFiniteSmoothDouble(value))
        {
            ExpandSmoothRange(
                value,
                ref minimum,
                ref maximum);
        }
    }

    private static void ExpandSmoothRange(
        double value,
        ref double minimum,
        ref double maximum)
    {
        minimum = Math.Min(minimum, value);
        maximum = Math.Max(maximum, value);
    }

    private static bool IsFiniteSmoothDouble(double value)
    {
        return
            !double.IsNaN(value) &&
            !double.IsInfinity(value);
    }

    private void ReleaseTriangulatedSmoothGpuResources()
    {
        ReleaseTriangulatedSmoothGpuExecutionResources();
        triangulatedSmoothGradientCache.Clear();
        triangulatedSmoothPatchCache.Clear();
    }

    private void ReleaseTriangulatedSmoothGpuExecutionResources()
    {
        ReleaseTriangulatedSmoothGpuBuffers();
        ResetTriangulatedSmoothShaderState();
    }

    private void ReleaseTriangulatedSmoothGpuBuffers()
    {
        ReleaseComputeBuffer(
            ref triangulatedSmoothGradientBuffer);

        ReleaseComputeBuffer(
            ref triangulatedSmoothCoefficientBuffer);

        triangulatedSmoothGradientUploadData = null;
        triangulatedSmoothCoefficientUploadData = null;
        triangulatedSmoothPreparedTopology = null;
        triangulatedSmoothPreparedGradients = null;
        triangulatedSmoothPreparedPatches = null;
    }

    private void ResetTriangulatedSmoothShaderState()
    {
        triangulatedSmoothComputeShader = null;
        triangulatedSmoothComposeKernel = -1;
        triangulatedSmoothSampleKernel = -1;
        triangulatedSmoothComposeThreadGroupSizeX = 0;
        triangulatedSmoothComposeThreadGroupSizeY = 0;
        triangulatedSmoothSampleThreadGroupSizeX = 0;
    }
}
