using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

internal static class TerrainHeightAddressablesScaleUtility
{
    internal static TerrainHeightAddressablesScaleReport CreateReport(
        TerrainAddressablesOperationStats stats,
        string buildOutputPath,
        double buildDuration
    )
    {
        int geographicTileCount =
            stats != null
                ? stats.heightGeographicTileCount
                : 0;

        int representationLevelCount =
            stats != null
                ? stats.heightRepresentationLevelCount
                : 0;

        int authoritativeAssetCount =
            stats != null
                ? stats.heightAuthoritativeAssetCount
                : 0;

        int derivedAssetCount =
            stats != null
                ? stats.heightDerivedAssetCount
                : 0;

        int expectedEntryCount =
            stats != null
                ? stats.heightExpectedEntryCount
                : 0;

        int actualEntryCount =
            stats != null
                ? stats.heightActualEntryCount
                : 0;

        int managedStrideLabelCount =
            stats != null
                ? stats.heightManagedStrideLabelCount
                : 0;

        TerrainHeightmapManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        if (
            manifest != null
            &&
            (
                geographicTileCount <= 0
                ||
                representationLevelCount <= 0
                ||
                expectedEntryCount <= 0
            )
        )
        {
            geographicTileCount =
                Math.Max(1, manifest.heightTileGridWidth) *
                Math.Max(1, manifest.heightTileGridHeight);

            representationLevelCount =
                Math.Max(1, manifest.StreamingLevelCount + 1);

            authoritativeAssetCount =
                geographicTileCount;

            derivedAssetCount =
                geographicTileCount *
                Math.Max(0, representationLevelCount - 1);

            expectedEntryCount =
                geographicTileCount *
                representationLevelCount;

            managedStrideLabelCount =
                representationLevelCount;
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(
                false
            );

        if (settings != null)
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    TerrainHeightmapAddressablesUtility
                        .HeightmapAddressablesGroupName
                );

            if (group != null)
            {
                actualEntryCount =
                    group.entries.Count;
            }
        }

        TerrainHeightAddressablesScaleReport report =
            new TerrainHeightAddressablesScaleReport
            {
                GeographicTileCount = geographicTileCount,
                RepresentationLevelCount = representationLevelCount,
                AuthoritativeAssetCount = authoritativeAssetCount,
                DerivedAssetCount = derivedAssetCount,
                ExpectedHeightEntryCount = expectedEntryCount,
                ActualHeightEntryCount = actualEntryCount,
                ManagedStrideLabelCount = managedStrideLabelCount,
                PackingMode =
                    TerrainHeightAddressablesPackingPolicy
                        .ExpectedBundleMode
                        .ToString(),
                ExpectedHeightBundleCount =
                    TerrainHeightAddressablesPackingPolicy
                        .EstimateHeightBundleCount(
                            expectedEntryCount,
                            representationLevelCount
                        ),
                BuiltBundleFileCount = -1,
                CatalogPayloadBytes = -1L,
                HeightConfigurationReconciliationSeconds =
                    stats != null
                        ? stats.heightConfigurationReconciliationSeconds
                        : 0d,
                AddressablesBuildSeconds = buildDuration,
                BuildOutputPath = buildOutputPath ?? ""
            };

        TryInspectBuildOutput(
            report.BuildOutputPath,
            out int bundleCount,
            out long catalogBytes
        );

        report.BuiltBundleFileCount = bundleCount;
        report.CatalogPayloadBytes = catalogBytes;

        return report;
    }

    private static void TryInspectBuildOutput(
        string buildOutputPath,
        out int bundleCount,
        out long catalogBytes
    )
    {
        bundleCount = -1;
        catalogBytes = -1L;

        if (string.IsNullOrEmpty(buildOutputPath))
        {
            return;
        }

        try
        {
            string absolutePath =
                Path.IsPathRooted(buildOutputPath)
                    ? buildOutputPath
                    : Path.GetFullPath(buildOutputPath);

            if (!Directory.Exists(absolutePath))
            {
                return;
            }

            string[] bundleFiles =
                Directory.GetFiles(
                    absolutePath,
                    "*.bundle",
                    SearchOption.AllDirectories
                );

            bundleCount = bundleFiles.Length;

            long measuredCatalogBytes = 0L;
            bool foundCatalogPayload = false;

            foreach (
                string filePath
                in Directory.GetFiles(
                    absolutePath,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string fileName =
                    Path.GetFileName(filePath);

                string extension =
                    Path.GetExtension(filePath);

                if (
                    !fileName.StartsWith(
                        "catalog",
                        StringComparison.OrdinalIgnoreCase
                    )
                    ||
                    (
                        !string.Equals(
                            extension,
                            ".json",
                            StringComparison.OrdinalIgnoreCase
                        )
                        &&
                        !string.Equals(
                            extension,
                            ".bin",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    continue;
                }

                measuredCatalogBytes +=
                    new FileInfo(filePath).Length;

                foundCatalogPayload = true;
            }

            if (foundCatalogPayload)
            {
                catalogBytes = measuredCatalogBytes;
            }
        }
        catch
        {
            bundleCount = -1;
            catalogBytes = -1L;
        }
    }
}
