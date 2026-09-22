using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

internal enum CollisionGeometryJobStatus : byte
{
    Uninitialized = 0,
    Success = 1,
    InvalidHeight = 2
}

internal struct CollisionGeometryJobInput
{
    public int sourceStartX;
    public int sourceStartZ;
}

internal struct CollisionGeometryJobResult
{
    public CollisionGeometryJobStatus status;
    public float minimumHeight;
    public float maximumHeight;
    public int invalidSourceX;
    public int invalidSourceZ;
}

internal static class TerrainCollisionGeometryJobRunner
{
    private const int GeometryInnerLoopBatchCount =
        1;

    [BurstCompile(
        FloatPrecision.Standard,
        FloatMode.Strict
    )]
    private struct PopulateCollisionMeshDataJob :
        IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<float> heightData;

        [ReadOnly]
        public NativeArray<int> collisionTopology;

        [ReadOnly]
        public NativeArray<CollisionGeometryJobInput> inputs;

        public Mesh.MeshDataArray meshDataArray;

        [WriteOnly]
        public NativeArray<CollisionGeometryJobResult> results;

        public int collisionResolution;
        public int heightSampleStep;
        public int heightSamplesPerTile;
        public int verticesPerSide;
        public int triangleIndexCount;
        public float collisionVertexSpacing;
        public bool useUInt16Indices;

        public void Execute(
            int index
        )
        {
            CollisionGeometryJobInput input =
                inputs[
                    index
                ];

            Mesh.MeshData meshData =
                meshDataArray[
                    index
                ];

            NativeArray<Vector3> vertexBuffer =
                meshData.GetVertexData<Vector3>(
                    0
                );

            float minimumHeight =
                float.PositiveInfinity;

            float maximumHeight =
                float.NegativeInfinity;

            for (
                int z = 0;
                z <= collisionResolution;
                z++
            )
            {
                int sourceZ =
                    input.sourceStartZ +
                    z *
                    heightSampleStep;

                int sourceRowStart =
                    sourceZ *
                    heightSamplesPerTile;

                for (
                    int x = 0;
                    x <= collisionResolution;
                    x++
                )
                {
                    int sourceX =
                        input.sourceStartX +
                        x *
                        heightSampleStep;

                    int sourceIndex =
                        sourceRowStart +
                        sourceX;

                    float height =
                        heightData[
                            sourceIndex
                        ];

                    if (
                        float.IsNaN(
                            height
                        )
                        ||
                        float.IsInfinity(
                            height
                        )
                    )
                    {
                        results[
                            index
                        ] =
                            new CollisionGeometryJobResult
                            {
                                status =
                                    CollisionGeometryJobStatus
                                        .InvalidHeight,

                                invalidSourceX =
                                    sourceX,

                                invalidSourceZ =
                                    sourceZ
                            };

                        return;
                    }

                    if (
                        height <
                        minimumHeight
                    )
                    {
                        minimumHeight =
                            height;
                    }

                    if (
                        height >
                        maximumHeight
                    )
                    {
                        maximumHeight =
                            height;
                    }

                    int vertexIndex =
                        z *
                        verticesPerSide +
                        x;

                    vertexBuffer[
                        vertexIndex
                    ] =
                        new Vector3(
                            x *
                                collisionVertexSpacing,
                            height,
                            z *
                                collisionVertexSpacing
                        );
                }
            }

            if (useUInt16Indices)
            {
                NativeArray<ushort> indexBuffer =
                    meshData.GetIndexData<ushort>();

                for (
                    int topologyIndex = 0;
                    topologyIndex < triangleIndexCount;
                    topologyIndex++
                )
                {
                    indexBuffer[
                        topologyIndex
                    ] =
                        (ushort)collisionTopology[
                            topologyIndex
                        ];
                }
            }
            else
            {
                NativeArray<int> indexBuffer =
                    meshData.GetIndexData<int>();

                for (
                    int topologyIndex = 0;
                    topologyIndex < triangleIndexCount;
                    topologyIndex++
                )
                {
                    indexBuffer[
                        topologyIndex
                    ] =
                        collisionTopology[
                            topologyIndex
                        ];
                }
            }

            results[
                index
            ] =
                new CollisionGeometryJobResult
                {
                    status =
                        CollisionGeometryJobStatus
                            .Success,

                    minimumHeight =
                        minimumHeight,

                    maximumHeight =
                        maximumHeight
                };
        }
    }

    internal static bool TryRun(
        Mesh.MeshDataArray meshDataArray,
        NativeArray<float> heightData,
        NativeArray<int> collisionTopology,
        NativeArray<CollisionGeometryJobInput> inputs,
        NativeArray<CollisionGeometryJobResult> results,
        int collisionResolution,
        int heightSampleStep,
        int heightSamplesPerTile,
        int verticesPerSide,
        int triangleIndexCount,
        float collisionVertexSpacing,
        bool useUInt16Indices,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !heightData.IsCreated
            ||
            !collisionTopology.IsCreated
            ||
            !inputs.IsCreated
            ||
            !results.IsCreated
        )
        {
            errorMessage =
                "Collision geometry job received an uncreated native buffer.";

            return false;
        }

        if (
            meshDataArray.Length !=
                inputs.Length
            ||
            results.Length !=
                inputs.Length
        )
        {
            errorMessage =
                "Collision geometry job batch lengths do not match.";

            return false;
        }

        if (inputs.Length == 0)
        {
            return true;
        }

        if (
            collisionResolution < 1
            ||
            heightSampleStep < 1
            ||
            heightSamplesPerTile < 1
            ||
            verticesPerSide !=
                collisionResolution +
                1
            ||
            triangleIndexCount !=
                collisionTopology.Length
            ||
            !(collisionVertexSpacing > 0f)
        )
        {
            errorMessage =
                "Collision geometry job received invalid shared generation parameters.";

            return false;
        }

        JobHandle handle =
            default;

        bool scheduled =
            false;

        try
        {
            PopulateCollisionMeshDataJob job =
                new PopulateCollisionMeshDataJob
                {
                    heightData =
                        heightData,

                    collisionTopology =
                        collisionTopology,

                    inputs =
                        inputs,

                    meshDataArray =
                        meshDataArray,

                    results =
                        results,

                    collisionResolution =
                        collisionResolution,

                    heightSampleStep =
                        heightSampleStep,

                    heightSamplesPerTile =
                        heightSamplesPerTile,

                    verticesPerSide =
                        verticesPerSide,

                    triangleIndexCount =
                        triangleIndexCount,

                    collisionVertexSpacing =
                        collisionVertexSpacing,

                    useUInt16Indices =
                        useUInt16Indices
                };

            handle =
                job.Schedule(
                    inputs.Length,
                    GeometryInnerLoopBatchCount
                );

            scheduled =
                true;

            handle.Complete();
        }
        catch (Exception exception)
        {
            if (scheduled)
            {
                try
                {
                    handle.Complete();
                }
                catch
                {
                    // Best effort: the original completion failure is reported below.
                }
            }

            errorMessage =
                "Parallel collision geometry generation failed.\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }
}
