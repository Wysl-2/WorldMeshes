using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class TerrainSurfaceMaskAddressablesUtility
{
    public const string SurfaceMaskAddressablesGroupName =
        "Terrain Surface Mask Tiles";

    public const string SurfaceStrideLabelPrefix =
        "TerrainSurface_Stride";

    // =====================================================
    // READ-ONLY VALIDATION
    // =====================================================

    public static bool ValidateExistingConfiguration(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryBuildRepresentationStrides(
                worldSettings,
                manifest,
                out List<int> representationStrides,
                out int tileGridWidth,
                out int tileGridHeight,
                out errorMessage
            )
        )
        {
            return false;
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(
                false
            );

        if (settings == null)
        {
            errorMessage =
                "AddressableAssetSettings is missing.";

            return false;
        }

        AddressableAssetGroup group =
            settings.FindGroup(
                SurfaceMaskAddressablesGroupName
            );

        if (group == null)
        {
            errorMessage =
                "Surface Addressables group is missing: " +
                SurfaceMaskAddressablesGroupName;

            return false;
        }

        BundledAssetGroupSchema schema =
            group.GetSchema<BundledAssetGroupSchema>();

        if (
            schema == null
            ||
            schema.BundleMode !=
                TerrainSurfaceAddressablesPackingPolicy
                    .ExpectedBundleMode
            ||
            !schema.IncludeAddressInCatalog
        )
        {
            errorMessage =
                "Surface Addressables group schema is missing or incompatible.";

            return false;
        }

        long geographicTileCountLong =
            (long)tileGridWidth *
            tileGridHeight;

        long expectedEntryCountLong =
            geographicTileCountLong *
            representationStrides.Count;

        if (
            geographicTileCountLong > int.MaxValue
            ||
            expectedEntryCountLong > int.MaxValue
        )
        {
            errorMessage =
                "Surface Addressables expected entry count exceeds the supported Int32 range.";

            return false;
        }

        HashSet<string> expectedStrideLabels =
            new HashSet<string>();

        HashSet<string> expectedRegionLabels =
            BuildExpectedRegionLabels(
                tileGridWidth,
                tileGridHeight
            );

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        foreach (string regionLabel in expectedRegionLabels)
        {
            if (!registeredLabels.Contains(regionLabel))
            {
                errorMessage =
                    "Surface Addressables region label is missing: " +
                    regionLabel;

                return false;
            }
        }

        HashSet<string> expectedGuids =
            new HashSet<string>();

        foreach (int sampleStride in representationStrides)
        {
            if (
                !TryGetValidatedDescriptor(
                    manifest,
                    sampleStride,
                    tileGridWidth,
                    tileGridHeight,
                    out TerrainSurfaceStreamingLevelDescriptor descriptor,
                    out errorMessage
                )
            )
            {
                return false;
            }

            string strideLabel =
                GetStrideLabel(
                    sampleStride
                );

            expectedStrideLabels.Add(
                strideLabel
            );

            if (!registeredLabels.Contains(strideLabel))
            {
                errorMessage =
                    "Surface Addressables stride label is missing: " +
                    strideLabel;

                return false;
            }

            for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
            {
                for (int tileX = 0; tileX < tileGridWidth; tileX++)
                {
                    if (
                        !TryResolveSurfaceRepresentation(
                            manifest,
                            descriptor,
                            sampleStride,
                            tileX,
                            tileZ,
                            out string assetPath,
                            out string guid,
                            out string expectedAddress,
                            out errorMessage
                        )
                    )
                    {
                        return false;
                    }

                    if (!expectedGuids.Add(guid))
                    {
                        errorMessage =
                            "Multiple Surface representation identities resolve to the same GUID:\n" +
                            guid;

                        return false;
                    }

                    AddressableAssetEntry entry =
                        settings.FindAssetEntry(
                            guid
                        );

                    if (entry == null)
                    {
                        errorMessage =
                            "Surface Addressables entry is missing:\n" +
                            assetPath;

                        return false;
                    }

                    if (entry.parentGroup != group)
                    {
                        errorMessage =
                            "Surface Addressables entry is in the wrong group:\n" +
                            assetPath;

                        return false;
                    }

                    if (entry.address != expectedAddress)
                    {
                        errorMessage =
                            "Surface Addressables entry has an incorrect address:\n" +
                            assetPath +
                            "\n\nExpected: " +
                            expectedAddress +
                            "\nActual: " +
                            entry.address;

                        return false;
                    }

                    string expectedRegionLabel =
                        TerrainSurfaceAddressablesPackingPolicy
                            .GetRegionLabel(
                                tileX,
                                tileZ
                            );

                    if (
                        !EntryHasExactManagedLabels(
                            entry,
                            strideLabel,
                            expectedRegionLabel
                        )
                    )
                    {
                        errorMessage =
                            "Surface Addressables entry has incorrect deterministic packing labels:\n" +
                            assetPath;

                        return false;
                    }
                }
            }
        }

        if (group.entries.Count != (int)expectedEntryCountLong)
        {
            errorMessage =
                "Surface Addressables group contains an obsolete/unexpected entry or has an unexpected entry count.";

            return false;
        }

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (
                !expectedGuids.Contains(entry.guid)
                ||
                !TryGetExactManagedLabels(
                    entry,
                    out string strideLabel,
                    out string regionLabel
                )
                ||
                !expectedStrideLabels.Contains(strideLabel)
                ||
                !expectedRegionLabels.Contains(regionLabel)
            )
            {
                errorMessage =
                    "Surface Addressables group contains an obsolete/unexpected or incorrectly labelled entry:\n" +
                    entry.address;

                return false;
            }
        }

        foreach (string label in settings.GetLabels())
        {
            if (
                IsManagedStrideLabel(label)
                &&
                !expectedStrideLabels.Contains(label)
            )
            {
                errorMessage =
                    "Surface Addressables settings contain an obsolete managed stride label: " +
                    label;

                return false;
            }

            if (
                TerrainSurfaceAddressablesPackingPolicy
                    .IsManagedRegionLabel(label)
                &&
                !expectedRegionLabels.Contains(label)
            )
            {
                errorMessage =
                    "Surface Addressables settings contain an obsolete managed region label: " +
                    label;

                return false;
            }
        }

        return true;
    }

    // =====================================================
    // STRUCTURAL RECONCILIATION
    // =====================================================

    internal static bool ReconcileConfiguration(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        TerrainAddressablesOperationStats stats,
        out bool cancelled,
        out string errorMessage
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.AddressablesSurfaceReconcile.Auto();

        double startedAt =
            EditorApplication.timeSinceStartup;

        cancelled = false;
        errorMessage = "";

        if (stats == null)
        {
            stats =
                new TerrainAddressablesOperationStats();
        }

        if (
            !TryBuildRepresentationStrides(
                worldSettings,
                manifest,
                out List<int> representationStrides,
                out int tileGridWidth,
                out int tileGridHeight,
                out errorMessage
            )
        )
        {
            stats.surfaceConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        long geographicTileCountLong =
            (long)tileGridWidth *
            tileGridHeight;

        long expectedEntryCountLong =
            geographicTileCountLong *
            representationStrides.Count;

        int regionGridWidth =
            TerrainSurfaceAddressablesPackingPolicy
                .GetRegionGridWidth(
                    tileGridWidth
                );

        int regionGridHeight =
            TerrainSurfaceAddressablesPackingPolicy
                .GetRegionGridHeight(
                    tileGridHeight
                );

        long regionCountLong =
            (long)regionGridWidth *
            regionGridHeight;

        if (
            geographicTileCountLong > int.MaxValue
            ||
            expectedEntryCountLong > int.MaxValue
            ||
            regionCountLong > int.MaxValue
        )
        {
            errorMessage =
                "Surface Addressables scale exceeds the supported Int32 entry or region range.";

            stats.surfaceConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        int geographicTileCount =
            (int)geographicTileCountLong;

        int expectedEntryCount =
            (int)expectedEntryCountLong;

        stats.surfaceGeographicTileCount =
            geographicTileCount;

        stats.surfaceRepresentationLevelCount =
            representationStrides.Count;

        stats.surfaceAuthoritativeAssetCount =
            geographicTileCount;

        stats.surfaceDerivedAssetCount =
            Mathf.Max(
                0,
                expectedEntryCount -
                geographicTileCount
            );

        stats.surfaceExpectedEntryCount =
            expectedEntryCount;

        stats.surfaceManagedStrideLabelCount =
            representationStrides.Count;

        stats.surfacePackingRegionTileSpan =
            TerrainSurfaceAddressablesPackingPolicy
                .RegionTileSpan;

        stats.surfacePackingRegionCount =
            (int)regionCountLong;

        stats.surfaceManagedRegionLabelCount =
            (int)regionCountLong;

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(
                true
            );

        if (settings == null)
        {
            errorMessage =
                "Could not load or create Addressables settings.";

            stats.surfaceConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        bool configurationChanged = false;
        bool settingsChanged = false;

        AddressableAssetGroup group =
            settings.FindGroup(
                SurfaceMaskAddressablesGroupName
            );

        if (group == null)
        {
            List<AddressableAssetGroupSchema> schemasToCopy =
                settings.DefaultGroup != null
                    ? settings.DefaultGroup.Schemas
                    : null;

            group =
                settings.CreateGroup(
                    SurfaceMaskAddressablesGroupName,
                    false,
                    false,
                    true,
                    schemasToCopy
                );

            if (group == null)
            {
                errorMessage =
                    "Could not create Addressables group:\n" +
                    SurfaceMaskAddressablesGroupName;

                stats.surfaceConfigurationReconciliationSeconds =
                    EditorApplication.timeSinceStartup - startedAt;

                return false;
            }

            stats.groupsCreated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        BundledAssetGroupSchema schema =
            group.GetSchema<BundledAssetGroupSchema>();

        if (schema == null)
        {
            schema =
                group.AddSchema<BundledAssetGroupSchema>(
                    true
                );

            if (schema == null)
            {
                errorMessage =
                    "Could not configure the surface Addressables Content Packing & Loading schema.";

                stats.surfaceConfigurationReconciliationSeconds =
                    EditorApplication.timeSinceStartup - startedAt;

                return false;
            }

            stats.schemasCreatedOrChanged++;
            configurationChanged = true;
            settingsChanged = true;
        }

        bool schemaChanged = false;

        if (
            schema.BundleMode !=
                TerrainSurfaceAddressablesPackingPolicy
                    .ExpectedBundleMode
        )
        {
            schema.BundleMode =
                TerrainSurfaceAddressablesPackingPolicy
                    .ExpectedBundleMode;

            schemaChanged = true;
        }

        if (!schema.IncludeAddressInCatalog)
        {
            schema.IncludeAddressInCatalog = true;
            schemaChanged = true;
        }

        if (schemaChanged)
        {
            EditorUtility.SetDirty(
                schema
            );

            stats.schemasCreatedOrChanged++;
            configurationChanged = true;
            settingsChanged = true;
        }

        HashSet<string> expectedStrideLabels =
            new HashSet<string>();

        HashSet<string> expectedRegionLabels =
            BuildExpectedRegionLabels(
                tileGridWidth,
                tileGridHeight
            );

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        foreach (string regionLabel in expectedRegionLabels)
        {
            if (registeredLabels.Contains(regionLabel))
            {
                continue;
            }

            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                settings.AddLabel(
                    regionLabel,
                    true
                );
            }

            registeredLabels.Add(
                regionLabel
            );

            stats.labelsUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        int processedEntries = 0;

        try
        {
            foreach (int sampleStride in representationStrides)
            {
                if (
                    !TryGetValidatedDescriptor(
                        manifest,
                        sampleStride,
                        tileGridWidth,
                        tileGridHeight,
                        out TerrainSurfaceStreamingLevelDescriptor descriptor,
                        out errorMessage
                    )
                )
                {
                    return false;
                }

                string strideLabel =
                    GetStrideLabel(
                        sampleStride
                    );

                expectedStrideLabels.Add(
                    strideLabel
                );

                if (!registeredLabels.Contains(strideLabel))
                {
                    using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
                    {
                        settings.AddLabel(
                            strideLabel,
                            true
                        );
                    }

                    registeredLabels.Add(
                        strideLabel
                    );

                    stats.labelsUpdated++;
                    configurationChanged = true;
                    settingsChanged = true;
                }

                HashSet<string> expectedStrideGuids =
                    new HashSet<string>();

                for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
                {
                    for (int tileX = 0; tileX < tileGridWidth; tileX++)
                    {
                        cancelled =
                            EditorUtility.DisplayCancelableProgressBar(
                                "Preparing Surface Mask Addressables",
                                $"Stride {sampleStride}, Tile ({tileX}, {tileZ})\n\n" +
                                $"{processedEntries + 1} / {expectedEntryCount}",
                                expectedEntryCount > 0
                                    ? (float)processedEntries / expectedEntryCount
                                    : 1f
                            );

                        if (cancelled)
                        {
                            break;
                        }

                        if (
                            !TryResolveSurfaceRepresentation(
                                manifest,
                                descriptor,
                                sampleStride,
                                tileX,
                                tileZ,
                                out string assetPath,
                                out string guid,
                                out string expectedAddress,
                                out errorMessage
                            )
                        )
                        {
                            return false;
                        }

                        if (!expectedStrideGuids.Add(guid))
                        {
                            errorMessage =
                                "Multiple Surface representation identities resolve to the same GUID:\n" +
                                guid;

                            return false;
                        }

                        string regionLabel =
                            TerrainSurfaceAddressablesPackingPolicy
                                .GetRegionLabel(
                                    tileX,
                                    tileZ
                                );

                        if (
                            !ReconcileEntry(
                                settings,
                                group,
                                guid,
                                expectedAddress,
                                strideLabel,
                                regionLabel,
                                assetPath,
                                stats,
                                ref configurationChanged,
                                ref settingsChanged,
                                out errorMessage
                            )
                        )
                        {
                            return false;
                        }

                        processedEntries++;
                    }

                    if (cancelled)
                    {
                        break;
                    }
                }

                stats.surfacePeakStrideExpectedGuidCount =
                    Mathf.Max(
                        stats.surfacePeakStrideExpectedGuidCount,
                        expectedStrideGuids.Count
                    );

                if (cancelled)
                {
                    break;
                }

                List<AddressableAssetEntry> staleStrideEntries =
                    new List<AddressableAssetEntry>();

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (
                        entry.labels.Contains(strideLabel)
                        &&
                        !expectedStrideGuids.Contains(entry.guid)
                    )
                    {
                        staleStrideEntries.Add(entry);
                    }
                }

                foreach (AddressableAssetEntry staleEntry in staleStrideEntries)
                {
                    using (WorldMeshesProfiler.AddressablesRemoveEntry.Auto())
                    {
                        group.RemoveAssetEntry(
                            staleEntry,
                            true
                        );
                    }

                    stats.entriesRemoved++;
                    configurationChanged = true;
                    settingsChanged = true;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (cancelled)
        {
            if (configurationChanged)
            {
                stats.surfaceConfigurationChanged = true;
            }

            if (settingsChanged)
            {
                EditorUtility.SetDirty(
                    settings
                );

                using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
                {
                    AssetDatabase.SaveAssets();
                }
            }

            stats.surfaceActualEntryCount =
                group.entries.Count;

            stats.surfaceConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        List<AddressableAssetEntry> malformedEntries =
            new List<AddressableAssetEntry>();

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (
                !TryGetExactManagedLabels(
                    entry,
                    out string managedStrideLabel,
                    out string managedRegionLabel
                )
                ||
                !expectedStrideLabels.Contains(
                    managedStrideLabel
                )
                ||
                !expectedRegionLabels.Contains(
                    managedRegionLabel
                )
            )
            {
                malformedEntries.Add(
                    entry
                );
            }
        }

        foreach (AddressableAssetEntry malformedEntry in malformedEntries)
        {
            using (WorldMeshesProfiler.AddressablesRemoveEntry.Auto())
            {
                group.RemoveAssetEntry(
                    malformedEntry,
                    true
                );
            }

            stats.entriesRemoved++;
            configurationChanged = true;
            settingsChanged = true;
        }

        List<string> staleManagedLabels =
            new List<string>();

        foreach (string label in settings.GetLabels())
        {
            if (
                IsManagedStrideLabel(label)
                &&
                !expectedStrideLabels.Contains(label)
            )
            {
                staleManagedLabels.Add(label);
                continue;
            }

            if (
                TerrainSurfaceAddressablesPackingPolicy
                    .IsManagedRegionLabel(label)
                &&
                !expectedRegionLabels.Contains(label)
            )
            {
                staleManagedLabels.Add(label);
            }
        }

        foreach (string staleLabel in staleManagedLabels)
        {
            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                settings.RemoveLabel(
                    staleLabel,
                    true
                );
            }

            stats.labelsUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        if (configurationChanged)
        {
            stats.surfaceConfigurationChanged = true;
        }

        if (settingsChanged)
        {
            EditorUtility.SetDirty(
                settings
            );

            using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
            {
                AssetDatabase.SaveAssets();
            }
        }

        stats.surfaceActualEntryCount =
            group.entries.Count;

        stats.surfaceConfigurationReconciliationSeconds =
            EditorApplication.timeSinceStartup - startedAt;

        return true;
    }

    // =====================================================
    // ENTRY RECONCILIATION
    // =====================================================

    private static bool ReconcileEntry(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string guid,
        string expectedAddress,
        string expectedStrideLabel,
        string expectedRegionLabel,
        string assetPath,
        TerrainAddressablesOperationStats stats,
        ref bool configurationChanged,
        ref bool settingsChanged,
        out string errorMessage
    )
    {
        errorMessage = "";

        AddressableAssetEntry existingEntry =
            settings.FindAssetEntry(
                guid
            );

        AddressableAssetEntry entry =
            existingEntry;

        if (existingEntry == null)
        {
            using (WorldMeshesProfiler.AddressablesCreateOrMoveEntry.Auto())
            {
                entry =
                    settings.CreateOrMoveEntry(
                        guid,
                        group,
                        false,
                        true
                    );
            }

            if (entry == null)
            {
                errorMessage =
                    "Could not create Addressables entry for:\n" +
                    assetPath;

                return false;
            }

            stats.entriesCreated++;
            configurationChanged = true;
            settingsChanged = true;
        }
        else if (existingEntry.parentGroup != group)
        {
            using (WorldMeshesProfiler.AddressablesCreateOrMoveEntry.Auto())
            {
                entry =
                    settings.CreateOrMoveEntry(
                        guid,
                        group,
                        false,
                        true
                    );
            }

            if (entry == null)
            {
                errorMessage =
                    "Could not move Addressables entry for:\n" +
                    assetPath;

                return false;
            }

            stats.entriesMoved++;
            configurationChanged = true;
            settingsChanged = true;
        }

        if (entry.address != expectedAddress)
        {
            using (WorldMeshesProfiler.AddressablesSetAddress.Auto())
            {
                entry.SetAddress(
                    expectedAddress,
                    true
                );
            }

            stats.addressesUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        if (
            !EntryHasExactManagedLabels(
                entry,
                expectedStrideLabel,
                expectedRegionLabel
            )
        )
        {
            List<string> existingLabels =
                new List<string>(
                    entry.labels
                );

            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                foreach (string label in existingLabels)
                {
                    entry.SetLabel(
                        label,
                        false,
                        false,
                        true
                    );
                }

                entry.SetLabel(
                    expectedStrideLabel,
                    true,
                    false,
                    true
                );

                entry.SetLabel(
                    expectedRegionLabel,
                    true,
                    false,
                    true
                );
            }

            stats.labelsUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        return true;
    }

    private static bool EntryHasExactManagedLabels(
        AddressableAssetEntry entry,
        string expectedStrideLabel,
        string expectedRegionLabel
    )
    {
        return
            entry != null
            &&
            entry.labels.Count == 2
            &&
            entry.labels.Contains(
                expectedStrideLabel
            )
            &&
            entry.labels.Contains(
                expectedRegionLabel
            );
    }

    private static bool TryGetExactManagedLabels(
        AddressableAssetEntry entry,
        out string strideLabel,
        out string regionLabel
    )
    {
        strideLabel = "";
        regionLabel = "";

        if (
            entry == null
            ||
            entry.labels.Count != 2
        )
        {
            return false;
        }

        int strideLabelCount = 0;
        int regionLabelCount = 0;

        foreach (string label in entry.labels)
        {
            if (IsManagedStrideLabel(label))
            {
                strideLabel =
                    label;

                strideLabelCount++;
                continue;
            }

            if (
                TerrainSurfaceAddressablesPackingPolicy
                    .IsManagedRegionLabel(label)
            )
            {
                regionLabel =
                    label;

                regionLabelCount++;
                continue;
            }

            return false;
        }

        return
            strideLabelCount == 1
            &&
            regionLabelCount == 1;
    }

    // =====================================================
    // LABEL / REGION HELPERS
    // =====================================================

    private static string GetStrideLabel(
        int sampleStride
    )
    {
        return
            SurfaceStrideLabelPrefix +
            sampleStride;
    }

    private static bool IsManagedStrideLabel(
        string label
    )
    {
        return
            !string.IsNullOrEmpty(label)
            &&
            label.StartsWith(
                SurfaceStrideLabelPrefix,
                StringComparison.Ordinal
            );
    }

    private static HashSet<string> BuildExpectedRegionLabels(
        int tileGridWidth,
        int tileGridHeight
    )
    {
        int regionGridWidth =
            TerrainSurfaceAddressablesPackingPolicy
                .GetRegionGridWidth(
                    tileGridWidth
                );

        int regionGridHeight =
            TerrainSurfaceAddressablesPackingPolicy
                .GetRegionGridHeight(
                    tileGridHeight
                );

        HashSet<string> labels =
            new HashSet<string>();

        for (int regionZ = 0; regionZ < regionGridHeight; regionZ++)
        {
            for (int regionX = 0; regionX < regionGridWidth; regionX++)
            {
                labels.Add(
                    TerrainSurfaceAddressablesPackingPolicy
                        .GetRegionLabel(
                            regionX *
                                TerrainSurfaceAddressablesPackingPolicy
                                    .RegionTileSpan,
                            regionZ *
                                TerrainSurfaceAddressablesPackingPolicy
                                    .RegionTileSpan
                        )
                );
            }
        }

        return labels;
    }

    // =====================================================
    // REPRESENTATION PREFLIGHT
    // =====================================================

    private static bool TryBuildRepresentationStrides(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        out List<int> representationStrides,
        out int tileGridWidth,
        out int tileGridHeight,
        out string errorMessage
    )
    {
        representationStrides =
            new List<int>();

        tileGridWidth = 0;
        tileGridHeight = 0;
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is unavailable.";

            return false;
        }

        if (
            manifest == null
            ||
            !manifest.isComplete
        )
        {
            errorMessage =
                "Surface-mask manifest is missing or incomplete.";

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(
                worldSettings
            ) !=
            TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Runtime Surface Masks are not current.";

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceStreamingStatus(
                worldSettings
            ) !=
            TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Surface Streaming is not current.";

            return false;
        }

        if (
            !TerrainGenerationStateUtility.TryBuildCurrentSurfaceStreamingTarget(
                worldSettings,
                manifest,
                out List<TerrainSurfaceStreamingLevelDescriptor>
                    targetDescriptors,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            manifest.StreamingLevelCount !=
            targetDescriptors.Count
        )
        {
            errorMessage =
                "Surface Streaming manifest descriptor count does not match the current streaming target.";

            return false;
        }

        representationStrides.Add(
            1
        );

        int previousStride = 1;

        for (
            int index = 0;
            index < targetDescriptors.Count;
            index++
        )
        {
            TerrainSurfaceStreamingLevelDescriptor descriptor =
                targetDescriptors[index];

            if (
                !manifest.TryGetStreamingLevelDescriptor(
                    descriptor.SampleStride,
                    out TerrainSurfaceStreamingLevelDescriptor current
                )
                ||
                !DescriptorsMatch(
                    current,
                    descriptor
                )
                ||
                descriptor.SampleStride <=
                    previousStride
            )
            {
                errorMessage =
                    "Surface Streaming manifest descriptors do not match the current streaming target.";

                representationStrides.Clear();
                return false;
            }

            representationStrides.Add(
                descriptor.SampleStride
            );

            previousStride =
                descriptor.SampleStride;
        }

        tileGridWidth =
            Mathf.Max(
                1,
                manifest.tileGridWidth
            );

        tileGridHeight =
            Mathf.Max(
                1,
                manifest.tileGridHeight
            );

        return true;
    }

    private static bool TryGetValidatedDescriptor(
        TerrainSurfaceMaskManifest manifest,
        int sampleStride,
        int tileGridWidth,
        int tileGridHeight,
        out TerrainSurfaceStreamingLevelDescriptor descriptor,
        out string errorMessage
    )
    {
        descriptor = default;
        errorMessage = "";

        if (
            !manifest.TryGetSurfaceRepresentationDescriptor(
                sampleStride,
                out descriptor
            )
        )
        {
            errorMessage =
                $"Surface representation stride {sampleStride} has no valid manifest descriptor.";

            return false;
        }

        if (
            descriptor.TileGridWidth != tileGridWidth
            ||
            descriptor.TileGridHeight != tileGridHeight
            ||
            descriptor.TextureFormat != TextureFormat.R8
            ||
            descriptor.SamplesPerSide < 2
        )
        {
            errorMessage =
                $"Surface representation stride {sampleStride} has an incompatible descriptor.";

            return false;
        }

        return true;
    }

    private static bool TryResolveSurfaceRepresentation(
        TerrainSurfaceMaskManifest manifest,
        TerrainSurfaceStreamingLevelDescriptor descriptor,
        int sampleStride,
        int tileX,
        int tileZ,
        out string assetPath,
        out string guid,
        out string expectedAddress,
        out string errorMessage
    )
    {
        assetPath =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetSurfaceRepresentationPath(
                    sampleStride,
                    tileX,
                    tileZ
                );

        guid = "";
        expectedAddress = "";
        errorMessage = "";

        Texture2D texture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(
                assetPath
            );

        if (texture == null)
        {
            errorMessage =
                "Missing runtime Surface representation:\n" +
                assetPath;

            return false;
        }

        try
        {
            if (
                texture.width !=
                    descriptor.SamplesPerSide
                ||
                texture.height !=
                    descriptor.SamplesPerSide
                ||
                texture.format !=
                    TextureFormat.R8
            )
            {
                errorMessage =
                    $"Runtime Surface representation stride {sampleStride} has an invalid layout or format:\n" +
                    assetPath;

                return false;
            }
        }
        finally
        {
            Resources.UnloadAsset(
                texture
            );
        }

        guid =
            AssetDatabase.AssetPathToGUID(
                assetPath
            );

        if (string.IsNullOrEmpty(guid))
        {
            errorMessage =
                "Could not resolve runtime Surface representation GUID:\n" +
                assetPath;

            return false;
        }

        expectedAddress =
            manifest.GetSurfaceRepresentationAddress(
                sampleStride,
                tileX,
                tileZ
            );

        return true;
    }

    private static bool DescriptorsMatch(
        TerrainSurfaceStreamingLevelDescriptor current,
        TerrainSurfaceStreamingLevelDescriptor expected
    )
    {
        return
            current.SampleStride ==
                expected.SampleStride
            &&
            current.SamplesPerSide ==
                expected.SamplesPerSide
            &&
            current.TileGridWidth ==
                expected.TileGridWidth
            &&
            current.TileGridHeight ==
                expected.TileGridHeight
            &&
            current.TextureFormat ==
                expected.TextureFormat
            &&
            Mathf.Approximately(
                current.SampleSpacing,
                expected.SampleSpacing
            )
            &&
            Mathf.Approximately(
                current.TileWorldSize,
                expected.TileWorldSize
            );
    }
}
