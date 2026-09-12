using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package I4 GPU execution for Triangulated Linear regional elevation.
 *
 * Package I2 remains the sole topology authority. Package I3 remains the CPU
 * semantic reference. This partial class converts the current derived topology
 * into disposable GPU buffers and dispatches the production Linear shader.
 */
public sealed partial class TerrainHeightCompositor
{
    public const string TriangulatedLinearComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/Authoring/" +
        "TerrainRegionalElevationLinear.compute";

    private const string TriangulatedLinearComposeKernelName =
        "ComposeTriangulatedLinearRegionalElevation";

    private const string TriangulatedLinearSampleKernelName =
        "EvaluateTriangulatedLinearSamples";

    private const int TriangulatedLinearVertexStride =
        sizeof(float) * 4;

    private const int TriangulatedLinearTriangleStride =
        sizeof(int) * 4;

    private const int TriangulatedLinearHullEdgeStride =
        sizeof(int) * 4;

    private const int TriangulatedLinearSamplePositionStride =
        sizeof(float) * 2;

    private const int TriangulatedLinearSampleResultStride =
        sizeof(float);

    [StructLayout(LayoutKind.Sequential)]
    private struct TriangulatedLinearVertexGpu
    {
        public Vector2 PositionXZ;
        public float Elevation;
        public float Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TriangulatedLinearTriangleGpu
    {
        public int VertexA;
        public int VertexB;
        public int VertexC;
        public int Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TriangulatedLinearHullEdgeGpu
    {
        public int VertexA;
        public int VertexB;
        public int Padding0;
        public int Padding1;
    }

    private ComputeShader triangulatedLinearComputeShader;

    private int triangulatedLinearComposeKernel = -1;
    private int triangulatedLinearSampleKernel = -1;

    private uint triangulatedLinearComposeThreadGroupSizeX;
    private uint triangulatedLinearComposeThreadGroupSizeY;
    private uint triangulatedLinearSampleThreadGroupSizeX;

    private readonly TerrainNodeElevationTopologyCache
        triangulatedLinearTopologyCache =
            new TerrainNodeElevationTopologyCache();

    private TerrainNodeElevationSource triangulatedLinearPreparedSource;
    private TerrainNodeElevationTopology triangulatedLinearPreparedTopology;

    private string triangulatedLinearPreparedSourceSignature =
        "";

    private ComputeBuffer triangulatedLinearVertexBuffer;
    private ComputeBuffer triangulatedLinearTriangleBuffer;
    private ComputeBuffer triangulatedLinearHullEdgeBuffer;

    private TriangulatedLinearVertexGpu[] triangulatedLinearVertexUploadData;
    private TriangulatedLinearTriangleGpu[] triangulatedLinearTriangleUploadData;
    private TriangulatedLinearHullEdgeGpu[] triangulatedLinearHullEdgeUploadData;

    internal int TriangulatedLinearTopologyRebuildCount =>
        triangulatedLinearTopologyCache.RebuildCount;

    internal int TriangulatedLinearPreparedVertexCount =>
        triangulatedLinearPreparedTopology != null
            ? triangulatedLinearPreparedTopology.VertexCount
            : 0;

    internal int TriangulatedLinearPreparedTriangleCount =>
        triangulatedLinearPreparedTopology != null
            ? triangulatedLinearPreparedTopology.TriangleCount
            : 0;

    internal int TriangulatedLinearPreparedHullEdgeCount =>
        triangulatedLinearPreparedTopology != null
            ? triangulatedLinearPreparedTopology.HullEdgeCount
            : 0;

    private bool TryDispatchTriangulatedLinearRegionalElevation(
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

        if (!TryPrepareTriangulatedLinearGpuData(
            nodeSource,
            out TerrainNodeElevationTopology topology,
            out errorMessage))
        {
            return false;
        }

        if (!TryPrepareTriangulatedLinearShader(out errorMessage))
        {
            return false;
        }

        int groupsX = DivideRoundUpLinear(
            samplesPerSide,
            triangulatedLinearComposeThreadGroupSizeX);

        int groupsY = DivideRoundUpLinear(
            samplesPerSide,
            triangulatedLinearComposeThreadGroupSizeY);

        if (groupsX <= 0 || groupsY <= 0)
        {
            errorMessage =
                "The Triangulated Linear regional compositor calculated an " +
                "invalid compute dispatch size.";
            return false;
        }

        try
        {
            SetTriangulatedLinearTopologyParameters(
                triangulatedLinearComposeKernel,
                topology);

            triangulatedLinearComputeShader.SetInt(
                "_SamplesPerSide",
                samplesPerSide);

            triangulatedLinearComputeShader.SetFloat(
                "_SampleSpacing",
                sampleSpacing);

            triangulatedLinearComputeShader.SetFloat(
                "_TileWorldSize",
                tileWorldSize);

            triangulatedLinearComputeShader.SetInts(
                "_TileCoordinate",
                tileCoordinate.x,
                tileCoordinate.y);

            triangulatedLinearComputeShader.SetVector(
                "_TileWorldOriginXZ",
                new Vector4(
                    tileWorldOriginXZ.x,
                    tileWorldOriginXZ.y,
                    0f,
                    0f));

            triangulatedLinearComputeShader.SetVector(
                "_WorldSizeXZ",
                new Vector4(
                    worldSizeXZ.x,
                    worldSizeXZ.y,
                    0f,
                    0f));

            triangulatedLinearComputeShader.SetInt(
                "_TargetSlice",
                sliceIndex);

            triangulatedLinearComputeShader.SetTexture(
                triangulatedLinearComposeKernel,
                "_HeightCache",
                heightCache);

            triangulatedLinearComputeShader.Dispatch(
                triangulatedLinearComposeKernel,
                groupsX,
                groupsY,
                1);

            MarkRegionalElevationDispatchSucceeded();
        }
        catch (Exception exception)
        {
            errorMessage =
                "The Triangulated Linear regional elevation pass could not " +
                $"be dispatched for tile ({tileCoordinate.x}, " +
                $"{tileCoordinate.y}) and cache slice {sliceIndex}.\n\n" +
                exception.Message;
            return false;
        }

        return true;
    }

    /*
     * Narrow production-shader evaluation hook used by Package I4 validation.
     * The same topology preparation and HLSL evaluator used by tile composition
     * are exercised; no test-only interpolation implementation exists.
     */
    internal bool TryEvaluateTriangulatedLinearGpuSamples(
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
                "Triangulated Linear GPU sample validation requires at least " +
                "one sample position.";
            return false;
        }

        for (int index = 0; index < samplePositions.Count; index++)
        {
            if (!TerrainNodeElevationGeometryUtility.IsFinite(
                samplePositions[index]))
            {
                errorMessage =
                    $"Triangulated Linear GPU sample {index} contains a " +
                    "non-finite position.";
                return false;
            }
        }

        if (!TryPrepareTriangulatedLinearGpuData(
            nodeSource,
            out TerrainNodeElevationTopology topology,
            out errorMessage))
        {
            return false;
        }

        if (!TryPrepareTriangulatedLinearShader(out errorMessage))
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
                    TriangulatedLinearSamplePositionStride,
                    ComputeBufferType.Structured);

            sampleResultBuffer =
                new ComputeBuffer(
                    positions.Length,
                    TriangulatedLinearSampleResultStride,
                    ComputeBufferType.Structured);

            samplePositionBuffer.SetData(positions);

            SetTriangulatedLinearTopologyParameters(
                triangulatedLinearSampleKernel,
                topology);

            triangulatedLinearComputeShader.SetBuffer(
                triangulatedLinearSampleKernel,
                "_LinearSamplePositions",
                samplePositionBuffer);

            triangulatedLinearComputeShader.SetBuffer(
                triangulatedLinearSampleKernel,
                "_LinearSampleResults",
                sampleResultBuffer);

            triangulatedLinearComputeShader.SetInt(
                "_LinearSampleCount",
                positions.Length);

            int groupsX = DivideRoundUpLinear(
                positions.Length,
                triangulatedLinearSampleThreadGroupSizeX);

            if (groupsX <= 0)
            {
                errorMessage =
                    "The Triangulated Linear GPU sample evaluator calculated " +
                    "an invalid dispatch size.";
                return false;
            }

            triangulatedLinearComputeShader.Dispatch(
                triangulatedLinearSampleKernel,
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
                        $"Triangulated Linear GPU sample {index} produced a " +
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
                "Triangulated Linear GPU sample evaluation failed.\n\n" +
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

    private bool TryPrepareTriangulatedLinearGpuData(
        TerrainNodeElevationSource nodeSource,
        out TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        topology = null;
        errorMessage = "";

        if (nodeSource == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        if (
            nodeSource.InterpolationMode !=
            TerrainNodeElevationInterpolationMode.TriangulatedLinear)
        {
            errorMessage =
                "Triangulated Linear GPU preparation received interpolation " +
                "mode '" +
                TerrainNodeElevationInterpolationModeUtility.GetDisplayName(
                    nodeSource.InterpolationMode) +
                "'.";
            return false;
        }

        if (!nodeSource.TryValidateOutputData(out string sourceError))
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;
            return false;
        }

        if (nodeSource.NodeCount <= 0)
        {
            errorMessage =
                "Triangulated Linear regional composition requires at least " +
                "one elevation node.";
            return false;
        }

        if (!triangulatedLinearTopologyCache.TryGetOrBuild(
            nodeSource,
            out topology,
            out string topologyError))
        {
            errorMessage =
                "Triangulated Linear topology could not be built. " +
                topologyError;
            return false;
        }

        if (
            topology == null ||
            topology.Kind == TerrainNodeElevationTopologyKind.Empty ||
            topology.VertexCount <= 0)
        {
            errorMessage =
                "Triangulated Linear topology does not define an evaluable " +
                "regional surface.";
            return false;
        }

        if (!ValidateTriangulatedLinearTopology(
            nodeSource,
            topology,
            out errorMessage))
        {
            return false;
        }

        StringBuilder signatureBuilder = new StringBuilder();
        if (!nodeSource.TryAppendDeterministicSignatureData(
            signatureBuilder,
            out string signatureError))
        {
            errorMessage =
                "Triangulated Linear GPU upload signature could not be " +
                "calculated. " +
                signatureError;
            return false;
        }

        string sourceSignature = signatureBuilder.ToString();
        bool topologyChanged =
            !ReferenceEquals(
                topology,
                triangulatedLinearPreparedTopology);

        bool sourceUnchanged =
            ReferenceEquals(
                nodeSource,
                triangulatedLinearPreparedSource) &&
            triangulatedLinearPreparedSourceSignature == sourceSignature;

        if (
            sourceUnchanged &&
            !topologyChanged &&
            HasTriangulatedLinearGpuBuffers(topology))
        {
            return true;
        }

        if (!TryEnsureTriangulatedLinearVertexBuffer(
            nodeSource,
            topology,
            out errorMessage))
        {
            ReleaseTriangulatedLinearGpuBuffers();
            return false;
        }

        if (
            topologyChanged ||
            !HasTriangulatedLinearTopologyBuffers(topology))
        {
            if (!TryEnsureTriangulatedLinearTopologyBuffers(
                topology,
                out errorMessage))
            {
                ReleaseTriangulatedLinearGpuBuffers();
                return false;
            }
        }

        triangulatedLinearPreparedSource = nodeSource;
        triangulatedLinearPreparedTopology = topology;
        triangulatedLinearPreparedSourceSignature = sourceSignature;
        return true;
    }

    private bool TryPrepareTriangulatedLinearShader(
        out string errorMessage)
    {
        errorMessage = "";

        if (
            triangulatedLinearComputeShader != null &&
            triangulatedLinearComposeKernel >= 0 &&
            triangulatedLinearSampleKernel >= 0 &&
            triangulatedLinearComposeThreadGroupSizeX > 0 &&
            triangulatedLinearComposeThreadGroupSizeY > 0 &&
            triangulatedLinearSampleThreadGroupSizeX > 0)
        {
            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support compute shaders.";
            return false;
        }

        triangulatedLinearComputeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                TriangulatedLinearComputeShaderAssetPath);

        if (triangulatedLinearComputeShader == null)
        {
            ResetTriangulatedLinearShaderState();
            errorMessage =
                "The Triangulated Linear regional elevation compute shader " +
                "could not be loaded:\n\n" +
                TriangulatedLinearComputeShaderAssetPath;
            return false;
        }

        try
        {
            triangulatedLinearComposeKernel =
                triangulatedLinearComputeShader.FindKernel(
                    TriangulatedLinearComposeKernelName);

            triangulatedLinearSampleKernel =
                triangulatedLinearComputeShader.FindKernel(
                    TriangulatedLinearSampleKernelName);

            /*
             * These thread-group dimensions are declared directly by the
             * production shader's numthreads attributes. Avoid querying them
             * through GetKernelThreadGroupSizes here because some Unity/editor
             * graphics backends can reject an otherwise valid kernel index
             * during that metadata query.
             */
            triangulatedLinearComposeThreadGroupSizeX = 8;
            triangulatedLinearComposeThreadGroupSizeY = 8;
            triangulatedLinearSampleThreadGroupSizeX = 64;
        }
        catch (Exception exception)
        {
            ResetTriangulatedLinearShaderState();
            errorMessage =
                "One or more required Triangulated Linear compute kernels " +
                "could not be found.\n\nRequired:\n- " +
                TriangulatedLinearComposeKernelName +
                "\n- " +
                TriangulatedLinearSampleKernelName +
                "\n\n" +
                exception.Message;
            return false;
        }

        if (
            triangulatedLinearComposeThreadGroupSizeX == 0 ||
            triangulatedLinearComposeThreadGroupSizeY == 0 ||
            triangulatedLinearSampleThreadGroupSizeX == 0)
        {
            ResetTriangulatedLinearShaderState();
            errorMessage =
                "Triangulated Linear compute shader reported an invalid " +
                "thread-group size.";
            return false;
        }

        return true;
    }

    private static bool ValidateTriangulatedLinearTopology(
        TerrainNodeElevationSource nodeSource,
        TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        errorMessage = "";

        if (nodeSource == null || topology == null)
        {
            errorMessage =
                "Triangulated Linear GPU topology validation received an " +
                "invalid source or topology.";
            return false;
        }

        for (int vertexIndex = 0; vertexIndex < topology.VertexCount; vertexIndex++)
        {
            TerrainNodeElevationTopologyVertex vertex =
                topology.Vertices[vertexIndex];

            if (
                vertex.SourceNodeIndex < 0 ||
                vertex.SourceNodeIndex >= nodeSource.NodeCount)
            {
                errorMessage =
                    "Triangulated Linear topology contains an invalid " +
                    "SourceNodeIndex mapping.";
                return false;
            }

            if (!TerrainNodeElevationGeometryUtility.IsFinite(vertex.PositionXZ))
            {
                errorMessage =
                    "Triangulated Linear topology contains a non-finite vertex " +
                    "position.";
                return false;
            }

            float elevation =
                nodeSource.Nodes[vertex.SourceNodeIndex].Elevation;

            if (!IsFinite(elevation))
            {
                errorMessage =
                    "Triangulated Linear topology references a non-finite node " +
                    "elevation.";
                return false;
            }
        }

        if (
            topology.Kind == TerrainNodeElevationTopologyKind.SinglePoint &&
            topology.VertexCount != 1)
        {
            errorMessage =
                "Single-point topology does not contain exactly one vertex.";
            return false;
        }

        if (
            topology.Kind == TerrainNodeElevationTopologyKind.LineSegment &&
            topology.VertexCount != 2)
        {
            errorMessage =
                "Line-segment topology does not contain exactly two vertices.";
            return false;
        }

        if (
            topology.Kind == TerrainNodeElevationTopologyKind.Collinear &&
            topology.VertexCount < 3)
        {
            errorMessage =
                "Collinear topology does not contain at least three vertices.";
            return false;
        }

        if (topology.Kind == TerrainNodeElevationTopologyKind.Triangulated)
        {
            if (topology.TriangleCount <= 0 || topology.HullEdgeCount <= 0)
            {
                errorMessage =
                    "Triangulated topology does not contain usable triangles " +
                    "and convex-hull edges.";
                return false;
            }

            for (int triangleIndex = 0; triangleIndex < topology.TriangleCount; triangleIndex++)
            {
                TerrainNodeElevationTopologyTriangle triangle =
                    topology.Triangles[triangleIndex];

                if (!IsValidTopologyVertexIndex(topology, triangle.VertexA) ||
                    !IsValidTopologyVertexIndex(topology, triangle.VertexB) ||
                    !IsValidTopologyVertexIndex(topology, triangle.VertexC))
                {
                    errorMessage =
                        "Triangulated topology contains an invalid triangle " +
                        "vertex index.";
                    return false;
                }
            }

            for (int edgeIndex = 0; edgeIndex < topology.HullEdgeCount; edgeIndex++)
            {
                TerrainNodeElevationTopologyEdge edge = topology.HullEdges[edgeIndex];
                if (!IsValidTopologyVertexIndex(topology, edge.VertexA) ||
                    !IsValidTopologyVertexIndex(topology, edge.VertexB))
                {
                    errorMessage =
                        "Triangulated topology contains an invalid hull-edge " +
                        "vertex index.";
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryEnsureTriangulatedLinearVertexBuffer(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        errorMessage = "";
        int count = topology.VertexCount;

        if (!TryEnsureComputeBuffer(
            ref triangulatedLinearVertexBuffer,
            count,
            TriangulatedLinearVertexStride,
            "Triangulated Linear vertex",
            out errorMessage))
        {
            return false;
        }

        if (
            triangulatedLinearVertexUploadData == null ||
            triangulatedLinearVertexUploadData.Length != count)
        {
            triangulatedLinearVertexUploadData =
                new TriangulatedLinearVertexGpu[count];
        }

        for (int index = 0; index < count; index++)
        {
            TerrainNodeElevationTopologyVertex vertex = topology.Vertices[index];
            float elevation = source.Nodes[vertex.SourceNodeIndex].Elevation;

            triangulatedLinearVertexUploadData[index] =
                new TriangulatedLinearVertexGpu
                {
                    PositionXZ = vertex.PositionXZ,
                    Elevation = elevation,
                    Padding = 0f
                };
        }

        try
        {
            triangulatedLinearVertexBuffer.SetData(
                triangulatedLinearVertexUploadData);
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not upload Triangulated Linear vertex data.\n\n" +
                exception.Message;
            return false;
        }

        return true;
    }

    private bool TryEnsureTriangulatedLinearTopologyBuffers(
        TerrainNodeElevationTopology topology,
        out string errorMessage)
    {
        errorMessage = "";

        int triangleBufferCount = Mathf.Max(1, topology.TriangleCount);
        int hullBufferCount = Mathf.Max(1, topology.HullEdgeCount);

        if (!TryEnsureComputeBuffer(
            ref triangulatedLinearTriangleBuffer,
            triangleBufferCount,
            TriangulatedLinearTriangleStride,
            "Triangulated Linear triangle",
            out errorMessage) ||
            !TryEnsureComputeBuffer(
                ref triangulatedLinearHullEdgeBuffer,
                hullBufferCount,
                TriangulatedLinearHullEdgeStride,
                "Triangulated Linear hull-edge",
                out errorMessage))
        {
            return false;
        }

        if (topology.TriangleCount > 0)
        {
            triangulatedLinearTriangleUploadData =
                new TriangulatedLinearTriangleGpu[topology.TriangleCount];

            for (int index = 0; index < topology.TriangleCount; index++)
            {
                TerrainNodeElevationTopologyTriangle triangle =
                    topology.Triangles[index];

                triangulatedLinearTriangleUploadData[index] =
                    new TriangulatedLinearTriangleGpu
                    {
                        VertexA = triangle.VertexA,
                        VertexB = triangle.VertexB,
                        VertexC = triangle.VertexC,
                        Padding = 0
                    };
            }

            triangulatedLinearTriangleBuffer.SetData(
                triangulatedLinearTriangleUploadData);
        }
        else
        {
            triangulatedLinearTriangleUploadData = null;
        }

        if (topology.HullEdgeCount > 0)
        {
            triangulatedLinearHullEdgeUploadData =
                new TriangulatedLinearHullEdgeGpu[topology.HullEdgeCount];

            for (int index = 0; index < topology.HullEdgeCount; index++)
            {
                TerrainNodeElevationTopologyEdge edge = topology.HullEdges[index];

                triangulatedLinearHullEdgeUploadData[index] =
                    new TriangulatedLinearHullEdgeGpu
                    {
                        VertexA = edge.VertexA,
                        VertexB = edge.VertexB,
                        Padding0 = 0,
                        Padding1 = 0
                    };
            }

            triangulatedLinearHullEdgeBuffer.SetData(
                triangulatedLinearHullEdgeUploadData);
        }
        else
        {
            triangulatedLinearHullEdgeUploadData = null;
        }

        return true;
    }

    private static bool TryEnsureComputeBuffer(
        ref ComputeBuffer buffer,
        int count,
        int stride,
        string label,
        out string errorMessage)
    {
        errorMessage = "";
        int safeCount = Mathf.Max(1, count);

        if (
            buffer != null &&
            buffer.count == safeCount &&
            buffer.stride == stride)
        {
            return true;
        }

        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }

        try
        {
            buffer = new ComputeBuffer(
                safeCount,
                stride,
                ComputeBufferType.Structured);
            return true;
        }
        catch (Exception exception)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }

            errorMessage =
                $"Could not allocate the {label} GPU buffer.\n\n" +
                exception.Message;
            return false;
        }
    }

    private bool HasTriangulatedLinearGpuBuffers(
        TerrainNodeElevationTopology topology)
    {
        return
            topology != null &&
            triangulatedLinearVertexBuffer != null &&
            triangulatedLinearVertexBuffer.count == Mathf.Max(1, topology.VertexCount) &&
            HasTriangulatedLinearTopologyBuffers(topology);
    }

    private bool HasTriangulatedLinearTopologyBuffers(
        TerrainNodeElevationTopology topology)
    {
        return
            topology != null &&
            triangulatedLinearTriangleBuffer != null &&
            triangulatedLinearTriangleBuffer.count == Mathf.Max(1, topology.TriangleCount) &&
            triangulatedLinearHullEdgeBuffer != null &&
            triangulatedLinearHullEdgeBuffer.count == Mathf.Max(1, topology.HullEdgeCount);
    }

    private void SetTriangulatedLinearTopologyParameters(
        int kernel,
        TerrainNodeElevationTopology topology)
    {
        triangulatedLinearComputeShader.SetInt(
            "_LinearTopologyKind",
            (int)topology.Kind);

        triangulatedLinearComputeShader.SetInt(
            "_LinearVertexCount",
            topology.VertexCount);

        triangulatedLinearComputeShader.SetInt(
            "_LinearTriangleCount",
            topology.TriangleCount);

        triangulatedLinearComputeShader.SetInt(
            "_LinearHullEdgeCount",
            topology.HullEdgeCount);

        triangulatedLinearComputeShader.SetBuffer(
            kernel,
            "_LinearVertices",
            triangulatedLinearVertexBuffer);

        triangulatedLinearComputeShader.SetBuffer(
            kernel,
            "_LinearTriangles",
            triangulatedLinearTriangleBuffer);

        triangulatedLinearComputeShader.SetBuffer(
            kernel,
            "_LinearHullEdges",
            triangulatedLinearHullEdgeBuffer);
    }

    private void ReleaseTriangulatedLinearGpuResources()
    {
        ReleaseTriangulatedLinearGpuBuffers();
        ResetTriangulatedLinearShaderState();
        triangulatedLinearTopologyCache.Clear();
    }

    private void ReleaseTriangulatedLinearGpuBuffers()
    {
        ReleaseComputeBuffer(ref triangulatedLinearVertexBuffer);
        ReleaseComputeBuffer(ref triangulatedLinearTriangleBuffer);
        ReleaseComputeBuffer(ref triangulatedLinearHullEdgeBuffer);

        triangulatedLinearVertexUploadData = null;
        triangulatedLinearTriangleUploadData = null;
        triangulatedLinearHullEdgeUploadData = null;
        triangulatedLinearPreparedSource = null;
        triangulatedLinearPreparedTopology = null;
        triangulatedLinearPreparedSourceSignature = "";
    }

    private void ResetTriangulatedLinearShaderState()
    {
        triangulatedLinearComputeShader = null;
        triangulatedLinearComposeKernel = -1;
        triangulatedLinearSampleKernel = -1;
        triangulatedLinearComposeThreadGroupSizeX = 0;
        triangulatedLinearComposeThreadGroupSizeY = 0;
        triangulatedLinearSampleThreadGroupSizeX = 0;
    }

    private static void ReleaseComputeBuffer(
        ref ComputeBuffer buffer)
    {
        if (buffer == null)
        {
            return;
        }

        buffer.Release();
        buffer = null;
    }

    private static bool IsValidTopologyVertexIndex(
        TerrainNodeElevationTopology topology,
        int index)
    {
        return
            topology != null &&
            index >= 0 &&
            index < topology.VertexCount;
    }

    private static int DivideRoundUpLinear(
        int value,
        uint divisor)
    {
        if (value <= 0 || divisor == 0)
        {
            return 0;
        }

        return Mathf.CeilToInt(value / (float)divisor);
    }
}
