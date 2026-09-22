using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

internal static class TerrainCollisionPhysicsBatchBaker
{
    private const int PhysicsBakeInnerLoopBatchCount =
        1;

    private struct BakeCollisionMeshesJob :
        IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> meshInstanceIds;

        public bool convex;

        public MeshColliderCookingOptions cookingOptions;

        public void Execute(
            int index
        )
        {
            Physics.BakeMesh(
                meshInstanceIds[
                    index
                ],
                convex,
                cookingOptions
            );
        }
    }

    internal static bool TryBakeMeshes(
        IReadOnlyList<Mesh> meshes,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (meshes == null)
        {
            errorMessage =
                "Cannot bake a null collision Mesh collection.";

            return false;
        }

        if (meshes.Count == 0)
        {
            return true;
        }

        int[] validatedInstanceIds =
            new int[
                meshes.Count
            ];

        HashSet<int> uniqueInstanceIds =
            new HashSet<int>();

        for (
            int index = 0;
            index < meshes.Count;
            index++
        )
        {
            Mesh mesh =
                meshes[
                    index
                ];

            if (mesh == null)
            {
                errorMessage =
                    "Cannot bake a collision batch containing a null Mesh.\n\n" +
                    "Batch Index: " +
                    index;

                return false;
            }

            int instanceId =
                mesh.GetInstanceID();

            if (instanceId == 0)
            {
                errorMessage =
                    "Cannot bake a collision Mesh with an invalid instance ID.\n\n" +
                    "Batch Index: " +
                    index +
                    "\n" +
                    "Mesh: " +
                    mesh.name;

                return false;
            }

            if (
                !uniqueInstanceIds.Add(
                    instanceId
                )
            )
            {
                errorMessage =
                    "Cannot bake the same collision Mesh more than once in one parallel batch.\n\n" +
                    "Batch Index: " +
                    index +
                    "\n" +
                    "Mesh: " +
                    mesh.name +
                    "\n" +
                    "Instance ID: " +
                    instanceId;

                return false;
            }

            validatedInstanceIds[
                index
            ] =
                instanceId;
        }

        NativeArray<int> meshInstanceIds =
            default;

        try
        {
            meshInstanceIds =
                new NativeArray<int>(
                    validatedInstanceIds.Length,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory
                );

            for (
                int index = 0;
                index < validatedInstanceIds.Length;
                index++
            )
            {
                meshInstanceIds[
                    index
                ] =
                    validatedInstanceIds[
                        index
                    ];
            }

            BakeCollisionMeshesJob job =
                new BakeCollisionMeshesJob
                {
                    meshInstanceIds =
                        meshInstanceIds,

                    convex =
                        TerrainCollisionPhysicsSettings
                            .Convex,

                    cookingOptions =
                        TerrainCollisionPhysicsSettings
                            .CookingOptions
                };

            JobHandle handle =
                job.Schedule(
                    meshInstanceIds.Length,
                    PhysicsBakeInnerLoopBatchCount
                );

            handle.Complete();
        }
        catch (Exception exception)
        {
            errorMessage =
                "Parallel collision physics cooking failed.\n\n" +
                exception.Message;

            return false;
        }
        finally
        {
            if (meshInstanceIds.IsCreated)
            {
                meshInstanceIds.Dispose();
            }
        }

        return true;
    }
}
