using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class TerrainHeightmapAddressablesUtility
{
    public const string HeightmapAddressablesGroupName =
        "Terrain Heightmap Tiles";

    public const string HeightStrideLabelPrefix =
        "TerrainHeight_Stride";

    // =====================================================
    // READ-ONLY VALIDATION
    // =====================================================

    public static bool ValidateExistingConfiguration(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
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
                HeightmapAddressablesGroupName
            );

        if (group == null)
        {
            errorMessage =
                "Height Addressables group is missing: " +
                HeightmapAddressablesGroupName;

            return false;
        }

        BundledAssetGroupSchema schema =
            group.GetSchema<BundledAssetGroupSchema>();

        if (
            schema == null
            ||
            schema.BundleMode !=
                TerrainHeightAddressablesPackingPolicy
                    .ExpectedBundleMode
            ||
            !schema.IncludeAddressInCatalog
        )
        {
            errorMessage =
                "Height Addressables group schema is missing or incompatible.";

            return false;
        }

        HashSet<string> expectedStrideLabels =
            new HashSet<string>();

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        long expectedEntryCountLong =
            (long)tileGridWidth *
            tileGridHeight *
            representationStrides.Count;

        if (expectedEntryCountLong > int.MaxValue)
        {
            errorMessage =
                "Height Addressables expected entry count exceeds the supported Int32 range.";

            return false;
        }

        foreach (int sampleStride in representationStrides)
        {
            if (
                !TryGetValidatedDescriptor(
                    manifest,
                    sampleStride,
                    tileGridWidth,
                    tileGridHeight,
                    out TerrainHeightStreamingLevelDescriptor descriptor,
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
                    "Height Addressables stride label is missing: " +
                    strideLabel;

                return false;
            }

            for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
            {
                for (int tileX = 0; tileX < tileGridWidth; tileX++)
                {
                    if (
                        !TryResolveHeightRepresentation(
                            manifest,
                            descriptor,
                            sampleStride,
                            tileX,
                            tileZ,
                            out string assetPath,
                            out string guid,
                            out string address,
                            out errorMessage
                        )
                    )
                    {
                        return false;
                    }

                    AddressableAssetEntry entry =
                        settings.FindAssetEntry(
                            guid
                        );

                    if (entry == null)
                    {
                        errorMessage =
                            "Height Addressables entry is missing:\n" +
                            assetPath;

                        return false;
                    }

                    if (entry.parentGroup != group)
                    {
                        errorMessage =
                            "Height Addressables entry is in the wrong group:\n" +
                            assetPath;

                        return false;
                    }

                    if (entry.address != address)
                    {
                        errorMessage =
                            "Height Addressables entry has an incorrect address:\n" +
                            assetPath +
                            "\n\nExpected: " +
                            address +
                            "\nActual: " +
                            entry.address;

                        return false;
                    }

                    int managedLabelCount = 0;
                    bool hasExpectedLabel = false;

                    foreach (string label in entry.labels)
                    {
                        if (!IsManagedStrideLabel(label))
                        {
                            continue;
                        }

                        managedLabelCount++;

                        if (label == strideLabel)
                        {
                            hasExpectedLabel = true;
                        }
                    }

                    if (
                        !hasExpectedLabel
                        ||
                        managedLabelCount != 1
                    )
                    {
                        errorMessage =
                            "Height Addressables entry has incorrect managed stride labels:\n" +
                            assetPath;

                        return false;
                    }
                }
            }
        }

        if (group.entries.Count != (int)expectedEntryCountLong)
        {
            errorMessage =
                "Height Addressables group contains an obsolete/unexpected entry or has an unexpected entry count.";

            return false;
        }

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (
                !TryGetSingleManagedStrideLabel(
                    entry,
                    out string managedLabel
                )
                ||
                !expectedStrideLabels.Contains(
                    managedLabel
                )
            )
            {
                errorMessage =
                    "Height Addressables group contains an obsolete/unexpected entry:\n" +
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
                    "Height Addressables settings contain an obsolete managed stride label: " +
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
        TerrainHeightmapManifest manifest,
        TerrainAddressablesOperationStats stats,
        out bool cancelled,
        out string errorMessage
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.AddressablesHeightReconcile.Auto();

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
            stats.heightConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

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
                "Height Addressables scale exceeds the supported Int32 entry range.";

            stats.heightConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        int geographicTileCount =
            (int)geographicTileCountLong;

        int expectedEntryCount =
            (int)expectedEntryCountLong;

        stats.heightGeographicTileCount =
            geographicTileCount;

        stats.heightRepresentationLevelCount =
            representationStrides.Count;

        stats.heightAuthoritativeAssetCount =
            geographicTileCount;

        stats.heightDerivedAssetCount =
            Mathf.Max(
                0,
                expectedEntryCount - geographicTileCount
            );

        stats.heightExpectedEntryCount =
            expectedEntryCount;

        stats.heightManagedStrideLabelCount =
            representationStrides.Count;

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(
                true
            );

        if (settings == null)
        {
            errorMessage =
                "Could not load or create Addressables settings.";

            stats.heightConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        bool configurationChanged = false;
        bool settingsChanged = false;

        AddressableAssetGroup group =
            settings.FindGroup(
                HeightmapAddressablesGroupName
            );

        if (group == null)
        {
            List<AddressableAssetGroupSchema> schemasToCopy =
                settings.DefaultGroup != null
                    ? settings.DefaultGroup.Schemas
                    : null;

            group =
                settings.CreateGroup(
                    HeightmapAddressablesGroupName,
                    false,
                    false,
                    true,
                    schemasToCopy
                );

            if (group == null)
            {
                errorMessage =
                    "Could not create Addressables group:\n" +
                    HeightmapAddressablesGroupName;

                stats.heightConfigurationReconciliationSeconds =
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
                    "Could not configure the height Addressables Content Packing & Loading schema.";

                stats.heightConfigurationReconciliationSeconds =
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
                TerrainHeightAddressablesPackingPolicy
                    .ExpectedBundleMode
        )
        {
            schema.BundleMode =
                TerrainHeightAddressablesPackingPolicy
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

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

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
                        out TerrainHeightStreamingLevelDescriptor descriptor,
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
                                "Preparing Heightmap Addressables",
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
                            !TryResolveHeightRepresentation(
                                manifest,
                                descriptor,
                                sampleStride,
                                tileX,
                                tileZ,
                                out string assetPath,
                                out string guid,
                                out string address,
                                out errorMessage
                            )
                        )
                        {
                            return false;
                        }

                        if (!expectedStrideGuids.Add(guid))
                        {
                            errorMessage =
                                "Multiple Height representation identities resolve to the same GUID:\n" +
                                guid;

                            return false;
                        }

                        if (
                            !ReconcileEntry(
                                settings,
                                group,
                                guid,
                                address,
                                strideLabel,
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

                stats.heightPeakStrideExpectedGuidCount =
                    Mathf.Max(
                        stats.heightPeakStrideExpectedGuidCount,
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
                stats.heightConfigurationChanged = true;
            }

            if (settingsChanged)
            {
                EditorUtility.SetDirty(settings);

                using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
                {
                    AssetDatabase.SaveAssets();
                }
            }

            stats.heightActualEntryCount =
                group.entries.Count;

            stats.heightConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        List<AddressableAssetEntry> malformedEntries =
            new List<AddressableAssetEntry>();

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (
                !TryGetSingleManagedStrideLabel(
                    entry,
                    out string managedLabel
                )
                ||
                !expectedStrideLabels.Contains(managedLabel)
            )
            {
                malformedEntries.Add(entry);
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
            stats.heightConfigurationChanged = true;
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

        stats.heightActualEntryCount =
            group.entries.Count;

        stats.heightConfigurationReconciliationSeconds =
            EditorApplication.timeSinceStartup - startedAt;

        return true;
    }

    // =====================================================
    // REPRESENTATION RESOLUTION
    // =====================================================

    private static bool TryBuildRepresentationStrides(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
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

        if (
            !ValidateManifest(
                worldSettings,
                manifest,
                out errorMessage
            )
        )
        {
            return false;
        }

        List<int> derivedStrides =
            new List<int>();

        if (
            !TerrainHeightStreamingPyramidPolicy
                .TryGetDerivedStrides(
                    worldSettings,
                    derivedStrides,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (
            manifest.StreamingLevelCount !=
                derivedStrides.Count
        )
        {
            errorMessage =
                "Height Streaming manifest descriptor count does not match the current streaming policy.";

            return false;
        }

        representationStrides.Add(1);
        representationStrides.AddRange(
            derivedStrides
        );

        tileGridWidth =
            Mathf.Max(
                1,
                manifest.heightTileGridWidth
            );

        tileGridHeight =
            Mathf.Max(
                1,
                manifest.heightTileGridHeight
            );

        return true;
    }

    private static bool TryGetValidatedDescriptor(
        TerrainHeightmapManifest manifest,
        int sampleStride,
        int tileGridWidth,
        int tileGridHeight,
        out TerrainHeightStreamingLevelDescriptor descriptor,
        out string errorMessage
    )
    {
        descriptor = default;
        errorMessage = "";

        if (
            !manifest.TryGetHeightRepresentationDescriptor(
                sampleStride,
                out descriptor
            )
        )
        {
            errorMessage =
                $"Height representation stride {sampleStride} has no valid manifest descriptor.";

            return false;
        }

        if (
            descriptor.TileGridWidth != tileGridWidth
            ||
            descriptor.TileGridHeight != tileGridHeight
            ||
            descriptor.TextureFormat != TextureFormat.RFloat
        )
        {
            errorMessage =
                $"Height representation stride {sampleStride} has an incompatible descriptor.";

            return false;
        }

        return true;
    }

    private static bool TryResolveHeightRepresentation(
        TerrainHeightmapManifest manifest,
        TerrainHeightStreamingLevelDescriptor descriptor,
        int sampleStride,
        int tileX,
        int tileZ,
        out string assetPath,
        out string guid,
        out string address,
        out string errorMessage
    )
    {
        assetPath =
            sampleStride == 1
                ? TerrainRuntimeHeightAssetUtility
                    .GetHeightTilePath(
                        tileX,
                        tileZ
                    )
                : TerrainRuntimeHeightAssetUtility
                    .GetStreamingHeightTilePath(
                        sampleStride,
                        tileX,
                        tileZ
                    );

        guid = "";
        address = "";
        errorMessage = "";

        Texture2D texture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(
                assetPath
            );

        if (texture == null)
        {
            errorMessage =
                $"Missing runtime height representation stride {sampleStride}:\n" +
                assetPath;

            return false;
        }

        try
        {
            if (
                texture.width != descriptor.SamplesPerSide
                ||
                texture.height != descriptor.SamplesPerSide
                ||
                texture.format != TextureFormat.RFloat
            )
            {
                errorMessage =
                    $"Runtime height representation stride {sampleStride} has an invalid layout or format:\n" +
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
                "Could not resolve runtime height representation GUID:\n" +
                assetPath;

            return false;
        }

        address =
            manifest.GetHeightRepresentationAddress(
                sampleStride,
                tileX,
                tileZ
            );

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

        List<string> labelsToRemove =
            new List<string>();

        bool hasExpectedLabel = false;

        foreach (string label in entry.labels)
        {
            if (!IsManagedStrideLabel(label))
            {
                continue;
            }

            if (label == expectedStrideLabel)
            {
                hasExpectedLabel = true;
            }
            else
            {
                labelsToRemove.Add(label);
            }
        }

        if (
            labelsToRemove.Count > 0
            ||
            !hasExpectedLabel
        )
        {
            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                foreach (string label in labelsToRemove)
                {
                    entry.SetLabel(
                        label,
                        false,
                        false,
                        true
                    );
                }

                if (!hasExpectedLabel)
                {
                    entry.SetLabel(
                        expectedStrideLabel,
                        true,
                        false,
                        true
                    );
                }
            }

            stats.labelsUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        return true;
    }

    private static bool TryGetSingleManagedStrideLabel(
        AddressableAssetEntry entry,
        out string managedLabel
    )
    {
        managedLabel = "";
        int managedLabelCount = 0;

        if (entry == null)
        {
            return false;
        }

        foreach (string label in entry.labels)
        {
            if (!IsManagedStrideLabel(label))
            {
                continue;
            }

            managedLabel = label;
            managedLabelCount++;
        }

        return
            managedLabelCount == 1;
    }

    // =====================================================
    // SHARED PREFLIGHT
    // =====================================================

    private static bool ValidateManifest(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is missing.";

            return false;
        }

        if (manifest == null)
        {
            errorMessage =
                "Heightmap manifest is missing.";

            return false;
        }

        if (!manifest.isComplete)
        {
            errorMessage =
                "Heightmap manifest is incomplete.";

            return false;
        }

        if (!manifest.streamingPyramidIsComplete)
        {
            errorMessage =
                "Height Streaming pyramid is incomplete.";

            return false;
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            != TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            errorMessage =
                "Runtime Heightmaps are not current.";

            return false;
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightStreamingStatus(
                    worldSettings
                )
            != TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            errorMessage =
                "Height Streaming is not current.";

            return false;
        }

        return true;
    }

    internal static string GetStrideLabel(
        int sampleStride
    )
    {
        return
            HeightStrideLabelPrefix +
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
                HeightStrideLabelPrefix,
                StringComparison.Ordinal
            );
    }
}
