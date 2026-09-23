using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

internal sealed class TerrainHeightBakeResidencyBatch :
    IDisposable
{
    private readonly TerrainRuntimeBakePipelineState stage;

    private readonly List<Texture2D> trackedOutputs =
        new List<Texture2D>();

    private readonly HashSet<int> trackedInstanceIds =
        new HashSet<int>();

    private readonly List<TerrainRuntimeBakeTrackedMemoryLease>
        memoryLeases =
            new List<TerrainRuntimeBakeTrackedMemoryLease>();

    private long estimatedResidentBytes;

    internal TerrainHeightBakeResidencyBatch(
        TerrainRuntimeBakePipelineState stage
    )
    {
        this.stage =
            stage;
    }

    internal int TrackedOutputCount =>
        trackedOutputs.Count;

    internal long EstimatedResidentBytes =>
        estimatedResidentBytes;

    internal void TrackDirtyOutput(
        Texture2D texture
    )
    {
        if (texture == null)
        {
            return;
        }

        int instanceId =
            texture.GetInstanceID();

        if (!trackedInstanceIds.Add(instanceId))
        {
            return;
        }

        trackedOutputs.Add(
            texture
        );

        long bytes =
            EstimateTextureBytes(
                texture
            );

        estimatedResidentBytes +=
            bytes;

        TerrainRuntimeBakeTrackedMemoryLease lease =
            TerrainRuntimeBakePerformanceDiagnostics
                .TrackTemporaryMemory(
                    "Height.GeneratedTextureResidency",
                    stage,
                    TerrainRuntimeBakeTrackedMemoryCategory
                        .HeightAssetResidency,
                    bytes
                );

        if (lease != null)
        {
            memoryLeases.Add(
                lease
            );
        }
    }

    internal void ReleasePersistedOutputs()
    {
        for (
            int index = 0;
            index < trackedOutputs.Count;
            index++
        )
        {
            Texture2D texture =
                trackedOutputs[index];

            if (texture == null)
            {
                continue;
            }

            try
            {
                Resources.UnloadAsset(
                    texture
                );
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "WorldMeshes could not explicitly unload a persisted height texture after a durability checkpoint.\n\n" +
                    exception.Message
                );
            }
        }

        ClearTracking();
    }

    internal void ClearTrackingWithoutUnload()
    {
        ClearTracking();
    }

    public void Dispose()
    {
        /*
         * Dispose is deliberately conservative. If a caller leaves the
         * scope before a successful SaveAssets checkpoint, the tracked
         * outputs may still contain dirty editor state. Drop ownership and
         * diagnostics, but do not unload them as though they were durable.
         */
        ClearTracking();
    }

    private void ClearTracking()
    {
        for (
            int index = 0;
            index < memoryLeases.Count;
            index++
        )
        {
            memoryLeases[index]?.Dispose();
        }

        memoryLeases.Clear();
        trackedOutputs.Clear();
        trackedInstanceIds.Clear();

        estimatedResidentBytes =
            0L;
    }

    private static long EstimateTextureBytes(
        Texture2D texture
    )
    {
        if (texture == null)
        {
            return 0L;
        }

        long bytes =
            0L;

        try
        {
            bytes =
                Profiler.GetRuntimeMemorySizeLong(
                    texture
                );
        }
        catch
        {
            bytes =
                0L;
        }

        if (bytes > 0L)
        {
            return bytes;
        }

        return
            Math.Max(
                0L,
                (long)texture.width *
                texture.height *
                sizeof(float)
            );
    }
}
