using System;
using UnityEditor;
using UnityEngine;

internal static class TerrainCollisionBakeResidencyUtility
{
    internal const int DefaultFullMeshResidencyBudget =
        512;

    /*
     * EditorUtility.UnloadUnusedAssetsImmediate does not treat the active
     * managed execution stack as an asset root. Keep the long-lived owner
     * explicitly rooted while an unload boundary is executing so the caller's
     * WorldSettings cannot be collected out from under the synchronous bake.
     */
    private static UnityEngine.Object[] unloadKeepAliveRoots;

    internal static int GetFullMeshResidencyLimit(
        int heightTileChunkSpan
    )
    {
        int safeSpan =
            Math.Max(
                1,
                heightTileChunkSpan
            );

        long maximumTileChunkCount =
            (long)safeSpan *
            safeSpan;

        long limit =
            Math.Max(
                DefaultFullMeshResidencyBudget,
                maximumTileChunkCount
            );

        return
            limit > int.MaxValue
                ? int.MaxValue
                : (int)limit;
    }

    internal static int GetRegionMeshCount(
        int regionX,
        int regionZ,
        int regionSpan,
        int gridWidth,
        int gridHeight
    )
    {
        int safeSpan =
            Math.Max(
                1,
                regionSpan
            );

        int safeGridWidth =
            Math.Max(
                0,
                gridWidth
            );

        int safeGridHeight =
            Math.Max(
                0,
                gridHeight
            );

        long startChunkXLong =
            (long)regionX *
            safeSpan;

        long startChunkZLong =
            (long)regionZ *
            safeSpan;

        if (
            startChunkXLong < 0L
            ||
            startChunkZLong < 0L
            ||
            startChunkXLong >= safeGridWidth
            ||
            startChunkZLong >= safeGridHeight
        )
        {
            return 0;
        }

        int startChunkX =
            (int)startChunkXLong;

        int startChunkZ =
            (int)startChunkZLong;

        int endChunkX =
            Math.Min(
                safeGridWidth,
                startChunkX +
                safeSpan
            );

        int endChunkZ =
            Math.Min(
                safeGridHeight,
                startChunkZ +
                safeSpan
            );

        long count =
            (long)(
                endChunkX -
                startChunkX
            )
            *
            (
                endChunkZ -
                startChunkZ
            );

        return
            count > int.MaxValue
                ? int.MaxValue
                : (int)count;
    }

    internal static bool TryReleaseAfterProcessedMeshCount(
        ref int processedSinceRelease,
        int processedMeshCount,
        UnityEngine.Object keepAlive,
        string performanceName,
        TerrainRuntimeBakePipelineState stage,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        long nextCount =
            (long)Math.Max(
                0,
                processedSinceRelease
            )
            +
            Math.Max(
                0,
                processedMeshCount
            );

        processedSinceRelease =
            nextCount > int.MaxValue
                ? int.MaxValue
                : (int)nextCount;

        if (
            processedSinceRelease <
            DefaultFullMeshResidencyBudget
        )
        {
            return true;
        }

        processedSinceRelease =
            0;

        return
            TryReleaseUnusedAssets(
                keepAlive,
                performanceName,
                stage,
                out errorMessage
            );
    }

    internal static bool TryReleaseUnusedAssets(
        UnityEngine.Object keepAlive,
        string performanceName,
        TerrainRuntimeBakePipelineState stage,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (unloadKeepAliveRoots != null)
        {
            errorMessage =
                "Collision bake unused-asset release is already in progress.";

            return false;
        }

        unloadKeepAliveRoots =
            keepAlive != null
                ? new[]
                {
                    keepAlive
                }
                : Array.Empty<UnityEngine.Object>();

        try
        {
            using TerrainRuntimeBakePerformanceScope releasePerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    string.IsNullOrEmpty(
                        performanceName
                    )
                        ? "Collision.ResidencyRelease"
                        : performanceName,
                    stage,
                    TerrainRuntimeBakePerformanceCategory.AssetDatabase
                );

            EditorUtility
                .UnloadUnusedAssetsImmediate();
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not release unused collision bake assets.\n\n" +
                exception.Message;

            return false;
        }
        finally
        {
            unloadKeepAliveRoots =
                null;
        }

        return true;
    }
}
