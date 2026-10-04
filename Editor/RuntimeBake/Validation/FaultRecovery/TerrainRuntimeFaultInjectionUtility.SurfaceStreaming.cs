using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static partial class TerrainRuntimeFaultInjectionUtility
{
    private static bool IsSurfaceStreamingFaultScenario(
        TerrainRuntimeFaultRecoveryScenario scenario
    )
    {
        switch (scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingStreamingSurfaceTexture:
            case TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceStreamingPyramid:
            case TerrainRuntimeFaultRecoveryScenario.InvalidSurfaceStreamingMetadata:
            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceStreamingAddressableEntry:
            case TerrainRuntimeFaultRecoveryScenario.ObsoleteSurfaceStreamingStride:
            case TerrainRuntimeFaultRecoveryScenario.SurfaceStreamingPolicySignatureMismatch:
                return true;

            default:
                return false;
        }
    }

    private static bool InjectSurfaceStreamingFault(
        WorldSettings worldSettings,
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingStreamingSurfaceTexture:
                return InjectMissingStreamingSurfaceTexture(
                    state,
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceStreamingPyramid:
            case TerrainRuntimeFaultRecoveryScenario.InvalidSurfaceStreamingMetadata:
            case TerrainRuntimeFaultRecoveryScenario.SurfaceStreamingPolicySignatureMismatch:
                return InjectSurfaceStreamingMetadataFault(
                    state,
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceStreamingAddressableEntry:
                return InjectMissingSurfaceStreamingAddressableEntry(
                    state,
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.ObsoleteSurfaceStreamingStride:
                return InjectObsoleteSurfaceStreamingStride(
                    worldSettings,
                    state,
                    out error
                );

            default:
                error =
                    "Selected Surface Streaming fault is unsupported.";
                return false;
        }
    }

    private static bool TryGetFirstSurfaceStreamingStride(
        out int sampleStride,
        out string error
    )
    {
        sampleStride = 0;
        error = "";

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        List<int> strides =
            new List<int>();

        if (
            !TerrainSurfaceStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                manifest,
                strides,
                out error
            )
            ||
            strides.Count == 0
        )
        {
            if (string.IsNullOrEmpty(error))
            {
                error =
                    "No derived Surface Streaming stride is configured.";
            }

            return false;
        }

        sampleStride =
            strides[0];

        return true;
    }

    private static bool InjectMissingStreamingSurfaceTexture(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        if (
            !TryGetFirstSurfaceStreamingStride(
                out int stride,
                out error
            )
        )
        {
            return false;
        }

        state.TargetPath =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetStreamingSurfaceTilePath(
                    stride,
                    0,
                    0
                );

        state.FaultDescription =
            "Removed derived Surface Streaming Texture2D at stride " +
            stride +
            " coordinate (0, 0).";

        if (
            !BackupAsset(
                state,
                state.TargetPath,
                out error
            )
        )
        {
            return false;
        }

        string absolute =
            AbsoluteProjectPath(
                state.TargetPath
            );

        if (!File.Exists(absolute))
        {
            error =
                "Fault target does not exist:\n" +
                state.TargetPath;

            return false;
        }

        File.Delete(
            absolute
        );

        return true;
    }

    private static bool InjectSurfaceStreamingMetadataFault(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        state.TargetPath =
            TerrainRuntimeSurfaceMaskAssetUtility
                .SurfaceMaskManifestPath;

        if (
            !BackupAsset(
                state,
                state.TargetPath,
                out error
            )
        )
        {
            return false;
        }

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                state.TargetPath
            );

        if (manifest == null)
        {
            error =
                "Runtime Surface manifest is unavailable.";

            return false;
        }

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceStreamingPyramid:
                manifest.streamingPyramidIsComplete =
                    false;

                state.FaultDescription =
                    "Set Surface Streaming pyramid completeness to false.";
                break;

            case TerrainRuntimeFaultRecoveryScenario.InvalidSurfaceStreamingMetadata:
                manifest.streamingPyramidCompilerVersion =
                    TerrainSurfaceMaskManifest
                        .CurrentStreamingPyramidCompilerVersion +
                    1000;

                state.FaultDescription =
                    "Set Surface Streaming compiler metadata to an incompatible validation value.";
                break;

            case TerrainRuntimeFaultRecoveryScenario.SurfaceStreamingPolicySignatureMismatch:
                manifest.streamingGenerationSignature =
                    "__WorldMeshesFaultRecovery_InvalidSurfaceStreamingSignature";

                state.FaultDescription =
                    "Replaced the Surface Streaming generation signature with an invalid validation value.";
                break;

            default:
                error =
                    "Selected Surface Streaming metadata fault is unsupported.";

                return false;
        }

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return true;
    }

    private static bool InjectMissingSurfaceStreamingAddressableEntry(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject
                .GetSettings(
                    true
                );

        if (settings == null)
        {
            error =
                "AddressableAssetSettings is unavailable.";

            return false;
        }

        AddressableAssetGroup group =
            settings.FindGroup(
                TerrainSurfaceMaskAddressablesUtility
                    .SurfaceMaskAddressablesGroupName
            );

        if (group == null)
        {
            error =
                "Surface Addressables group is unavailable.";

            return false;
        }

        if (
            !TryGetFirstSurfaceStreamingStride(
                out int stride,
                out error
            )
        )
        {
            return false;
        }

        string path =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetStreamingSurfaceTilePath(
                    stride,
                    0,
                    0
                );

        string guid =
            AssetDatabase.AssetPathToGUID(
                path
            );

        if (string.IsNullOrEmpty(guid))
        {
            error =
                "Derived Surface Streaming asset GUID is unavailable:\n" +
                path;

            return false;
        }

        AddressableAssetEntry entry =
            settings.FindAssetEntry(
                guid
            );

        if (
            entry == null
            ||
            entry.parentGroup != group
        )
        {
            error =
                "Derived Surface Streaming Addressables entry is unavailable:\n" +
                path;

            return false;
        }

        state.AddressableGuid =
            entry.guid;

        state.OriginalGroupName =
            entry.parentGroup != null
                ? entry.parentGroup.name
                : "";

        state.OriginalAddress =
            entry.address;

        state.OriginalLabels =
            new List<string>(
                entry.labels
            );

        state.TargetGuid =
            entry.guid;

        state.TargetPath =
            path;

        group.RemoveAssetEntry(
            entry,
            true
        );

        state.FaultDescription =
            "Removed required Addressables entry for derived Surface Streaming asset " +
            path +
            ".";

        EditorUtility.SetDirty(
            settings
        );

        AssetDatabase.SaveAssets();

        return true;
    }

    private static bool InjectObsoleteSurfaceStreamingStride(
        WorldSettings worldSettings,
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        List<int> strides =
            new List<int>();

        if (
            !TerrainSurfaceStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                manifest,
                strides,
                out error
            )
            ||
            strides.Count == 0
        )
        {
            if (string.IsNullOrEmpty(error))
            {
                error =
                    "No derived Surface Streaming stride is configured.";
            }

            return false;
        }

        int sourceStride =
            strides[0];

        long candidate =
            (long)strides[
                strides.Count - 1
            ] * 2L;

        if (candidate > int.MaxValue)
        {
            error =
                "Could not calculate a validation-only obsolete Surface Streaming stride.";

            return false;
        }

        int obsoleteStride =
            (int)candidate;

        string sourcePath =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetStreamingSurfaceTilePath(
                    sourceStride,
                    0,
                    0
                );

        state.TargetPath =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetStreamingSurfaceTilePath(
                    obsoleteStride,
                    0,
                    0
                );

        string folder =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetStreamingStrideFolder(
                    obsoleteStride
                );

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder(
                TerrainRuntimeSurfaceMaskAssetUtility
                    .SurfaceMaskStreamingFolder,
                "Stride_" +
                obsoleteStride
            );
        }

        if (
            !AssetDatabase.CopyAsset(
                sourcePath,
                state.TargetPath
            )
        )
        {
            error =
                "Could not create the validation-only obsolete Surface Streaming representation.";

            return false;
        }

        state.CreatedFaultAsset =
            true;

        state.FaultDescription =
            "Created validation-only obsolete Surface Streaming stride " +
            obsoleteStride +
            " at coordinate (0, 0).";

        return true;
    }
}
