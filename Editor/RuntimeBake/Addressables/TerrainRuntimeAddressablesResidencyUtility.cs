using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

internal readonly struct TerrainAddressablesMemorySnapshot
{
    internal readonly long ManagedHeapBytes;
    internal readonly long UnityAllocatedBytes;
    internal readonly long ProcessWorkingSetBytes;

    internal TerrainAddressablesMemorySnapshot(
        long managedHeapBytes,
        long unityAllocatedBytes,
        long processWorkingSetBytes
    )
    {
        ManagedHeapBytes = managedHeapBytes;
        UnityAllocatedBytes = unityAllocatedBytes;
        ProcessWorkingSetBytes = processWorkingSetBytes;
    }
}

internal static class TerrainRuntimeAddressablesResidencyUtility
{
    /*
     * EditorUtility.UnloadUnusedAssetsImmediate does not use the managed call
     * stack as an asset root. Keep the long-lived Addressables/runtime bake
     * assets explicitly rooted while an unload boundary executes.
     */
    private static UnityEngine.Object[] unloadKeepAliveRoots;

    internal static bool TryReleaseConfigurationResidency(
        WorldSettings worldSettings,
        TerrainHeightmapManifest heightManifest,
        TerrainSurfaceMaskManifest surfaceManifest,
        string performanceName,
        out string errorMessage
    )
    {
        return
            TryReleaseUnusedAssets(
                worldSettings,
                heightManifest,
                surfaceManifest,
                performanceName,
                out errorMessage
            );
    }

    internal static bool TryPrepareForContentBuild(
        WorldSettings worldSettings,
        TerrainHeightmapManifest heightManifest,
        TerrainSurfaceMaskManifest surfaceManifest,
        out TerrainAddressablesMemorySnapshot before,
        out TerrainAddressablesMemorySnapshot after,
        out string errorMessage
    )
    {
        before =
            CaptureMemorySnapshot();

        after =
            before;

        if (
            !TryReleaseUnusedAssets(
                worldSettings,
                heightManifest,
                surfaceManifest,
                "Addressables.PreBuildMemoryBarrier",
                out errorMessage
            )
        )
        {
            return false;
        }

        try
        {
            /*
             * This is intentionally a single global content-build boundary,
             * not a per-entry/per-region collection policy. Configuration
             * methods have returned, so their dead managed scratch should not
             * compete with the monolithic Addressables build graph.
             */
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not complete the Addressables pre-build managed-memory barrier.\n\n" +
                exception.Message;

            return false;
        }

        after =
            CaptureMemorySnapshot();

        errorMessage = "";
        return true;
    }

    internal static bool TryReleaseUnusedAssets(
        WorldSettings worldSettings,
        TerrainHeightmapManifest heightManifest,
        TerrainSurfaceMaskManifest surfaceManifest,
        string performanceName,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (unloadKeepAliveRoots != null)
        {
            errorMessage =
                "Addressables unused-asset release is already in progress.";

            return false;
        }

        /*
         * Establish a static sentinel before resolving the root graph so an
         * exception during root collection cannot leave a nested release path
         * looking available. The final static root array is installed before
         * Unity begins the unload.
         */
        unloadKeepAliveRoots =
            Array.Empty<UnityEngine.Object>();

        try
        {
            AddressableAssetSettings settings =
                AddressableAssetSettingsDefaultObject.GetSettings(
                    false
                );

            TerrainCollisionManifest collisionManifest =
                AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                    WorldMeshesPaths.CollisionManifestAssetPath
                );

            List<UnityEngine.Object> roots =
                new List<UnityEngine.Object>();

            AddRoot(roots, worldSettings);
            AddRoot(roots, heightManifest);
            AddRoot(roots, surfaceManifest);
            AddRoot(roots, collisionManifest);
            AddRoot(roots, settings);

            if (settings != null)
            {
                AddRoot(
                    roots,
                    settings.FindGroup(
                        TerrainHeightmapAddressablesUtility
                            .HeightmapAddressablesGroupName
                    )
                );

                AddRoot(
                    roots,
                    settings.FindGroup(
                        TerrainSurfaceMaskAddressablesUtility
                            .SurfaceMaskAddressablesGroupName
                    )
                );

                AddRoot(
                    roots,
                    settings.FindGroup(
                        TerrainCollisionAddressablesUtility
                            .CollisionAddressablesGroupName
                    )
                );
            }

            unloadKeepAliveRoots =
                roots.ToArray();

            using TerrainRuntimeBakePerformanceScope releasePerformance =
                TerrainRuntimeBakePerformanceDiagnostics.BeginOperation(
                    string.IsNullOrEmpty(performanceName)
                        ? "Addressables.ResidencyRelease"
                        : performanceName,
                    TerrainRuntimeBakePipelineState.Addressables,
                    TerrainRuntimeBakePerformanceCategory.AssetDatabase
                );

            EditorUtility.UnloadUnusedAssetsImmediate();
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not release unused Addressables-stage assets.\n\n" +
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

    internal static TerrainAddressablesMemorySnapshot CaptureMemorySnapshot()
    {
        long managedBytes =
            -1L;

        long unityAllocatedBytes =
            -1L;

        long processWorkingSetBytes =
            -1L;

        try
        {
            managedBytes =
                GC.GetTotalMemory(
                    false
                );
        }
        catch
        {
            managedBytes = -1L;
        }

        try
        {
            unityAllocatedBytes =
                UnityEngine.Profiling.Profiler
                    .GetTotalAllocatedMemoryLong();
        }
        catch
        {
            unityAllocatedBytes = -1L;
        }

        try
        {
            using System.Diagnostics.Process process =
                System.Diagnostics.Process.GetCurrentProcess();

            processWorkingSetBytes =
                process.WorkingSet64;
        }
        catch
        {
            processWorkingSetBytes = -1L;
        }

        return
            new TerrainAddressablesMemorySnapshot(
                managedBytes,
                unityAllocatedBytes,
                processWorkingSetBytes
            );
    }

    private static void AddRoot(
        List<UnityEngine.Object> roots,
        UnityEngine.Object value
    )
    {
        if (value != null)
        {
            roots.Add(value);
        }
    }
}
