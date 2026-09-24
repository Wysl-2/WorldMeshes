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

    private readonly struct HeightRepresentationAssetRecord
    {
        public readonly int sampleStride;
        public readonly int tileX;
        public readonly int tileZ;
        public readonly string assetPath;
        public readonly string guid;
        public readonly string address;
        public readonly string strideLabel;
        public readonly TerrainHeightStreamingLevelDescriptor descriptor;

        public HeightRepresentationAssetRecord(
            int sampleStride,
            int tileX,
            int tileZ,
            string assetPath,
            string guid,
            string address,
            string strideLabel,
            TerrainHeightStreamingLevelDescriptor descriptor
        )
        {
            this.sampleStride = sampleStride;
            this.tileX = tileX;
            this.tileZ = tileZ;
            this.assetPath = assetPath ?? "";
            this.guid = guid ?? "";
            this.address = address ?? "";
            this.strideLabel = strideLabel ?? "";
            this.descriptor = descriptor;
        }
    }

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
            !TryCollectExpectedRepresentations(
                worldSettings,
                manifest,
                out List<HeightRepresentationAssetRecord> records,
                out HashSet<string> expectedGuids,
                out HashSet<string> expectedStrideLabels,
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

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        foreach (string expectedLabel in expectedStrideLabels)
        {
            if (!registeredLabels.Contains(expectedLabel))
            {
                errorMessage =
                    "Height Addressables stride label is missing: " +
                    expectedLabel;

                return false;
            }
        }

        foreach (
            HeightRepresentationAssetRecord record
            in records
        )
        {
            AddressableAssetEntry entry =
                settings.FindAssetEntry(
                    record.guid
                );

            if (entry == null)
            {
                errorMessage =
                    "Height Addressables entry is missing:\n" +
                    record.assetPath;

                return false;
            }

            if (entry.parentGroup != group)
            {
                errorMessage =
                    "Height Addressables entry is in the wrong group:\n" +
                    record.assetPath;

                return false;
            }

            if (entry.address != record.address)
            {
                errorMessage =
                    "Height Addressables entry has an incorrect address:\n" +
                    record.assetPath +
                    "\n\nExpected: " +
                    record.address +
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

                if (label == record.strideLabel)
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
                    record.assetPath;

                return false;
            }
        }

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (!expectedGuids.Contains(entry.guid))
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
            !TryCollectExpectedRepresentations(
                worldSettings,
                manifest,
                out List<HeightRepresentationAssetRecord> records,
                out HashSet<string> expectedGuids,
                out HashSet<string> expectedStrideLabels,
                out errorMessage
            )
        )
        {
            stats.heightConfigurationReconciliationSeconds =
                EditorApplication.timeSinceStartup - startedAt;

            return false;
        }

        int geographicTileCount =
            Mathf.Max(1, manifest.heightTileGridWidth) *
            Mathf.Max(1, manifest.heightTileGridHeight);

        int representationLevelCount =
            expectedStrideLabels.Count;

        stats.heightGeographicTileCount =
            geographicTileCount;

        stats.heightRepresentationLevelCount =
            representationLevelCount;

        stats.heightAuthoritativeAssetCount =
            geographicTileCount;

        stats.heightDerivedAssetCount =
            Mathf.Max(
                0,
                records.Count - geographicTileCount
            );

        stats.heightExpectedEntryCount =
            records.Count;

        stats.heightManagedStrideLabelCount =
            expectedStrideLabels.Count;

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

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        foreach (string expectedLabel in expectedStrideLabels)
        {
            if (registeredLabels.Contains(expectedLabel))
            {
                continue;
            }

            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                settings.AddLabel(
                    expectedLabel,
                    true
                );
            }

            registeredLabels.Add(
                expectedLabel
            );

            stats.labelsUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        try
        {
            for (
                int recordIndex = 0;
                recordIndex < records.Count;
                recordIndex++
            )
            {
                HeightRepresentationAssetRecord record =
                    records[recordIndex];

                cancelled =
                    EditorUtility.DisplayCancelableProgressBar(
                        "Preparing Heightmap Addressables",
                        $"Stride {record.sampleStride}, Tile ({record.tileX}, {record.tileZ})\n\n" +
                        $"{recordIndex + 1} / {records.Count}",
                        records.Count > 0
                            ? (float)recordIndex / records.Count
                            : 1f
                    );

                if (cancelled)
                {
                    break;
                }

                AddressableAssetEntry existingEntry =
                    settings.FindAssetEntry(
                        record.guid
                    );

                AddressableAssetEntry entry =
                    existingEntry;

                if (existingEntry == null)
                {
                    using (WorldMeshesProfiler.AddressablesCreateOrMoveEntry.Auto())
                    {
                        entry =
                            settings.CreateOrMoveEntry(
                                record.guid,
                                group,
                                false,
                                true
                            );
                    }

                    if (entry == null)
                    {
                        errorMessage =
                            "Could not create Addressables entry for:\n" +
                            record.assetPath;

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
                                record.guid,
                                group,
                                false,
                                true
                            );
                    }

                    if (entry == null)
                    {
                        errorMessage =
                            "Could not move Addressables entry for:\n" +
                            record.assetPath;

                        return false;
                    }

                    stats.entriesMoved++;
                    configurationChanged = true;
                    settingsChanged = true;
                }

                if (entry.address != record.address)
                {
                    using (WorldMeshesProfiler.AddressablesSetAddress.Auto())
                    {
                        entry.SetAddress(
                            record.address,
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

                    if (label == record.strideLabel)
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
                                record.strideLabel,
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

        List<AddressableAssetEntry> obsoleteEntries =
            new List<AddressableAssetEntry>();

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (!expectedGuids.Contains(entry.guid))
            {
                obsoleteEntries.Add(
                    entry
                );
            }
        }

        foreach (AddressableAssetEntry obsoleteEntry in obsoleteEntries)
        {
            using (WorldMeshesProfiler.AddressablesRemoveEntry.Auto())
            {
                group.RemoveAssetEntry(
                    obsoleteEntry,
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
                staleManagedLabels.Add(
                    label
                );
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
    // SHARED PREFLIGHT / EXPECTED RECORDS
    // =====================================================

    private static bool TryCollectExpectedRepresentations(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        out List<HeightRepresentationAssetRecord> records,
        out HashSet<string> expectedGuids,
        out HashSet<string> expectedStrideLabels,
        out string errorMessage
    )
    {
        records =
            new List<HeightRepresentationAssetRecord>();

        expectedGuids =
            new HashSet<string>();

        expectedStrideLabels =
            new HashSet<string>();

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

        List<int> representationStrides =
            new List<int>(
                derivedStrides.Count + 1
            )
            {
                1
            };

        representationStrides.AddRange(
            derivedStrides
        );

        int tileGridWidth =
            Mathf.Max(
                1,
                manifest.heightTileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                manifest.heightTileGridHeight
            );

        records.Capacity =
            tileGridWidth *
            tileGridHeight *
            representationStrides.Count;

        foreach (int sampleStride in representationStrides)
        {
            if (
                !manifest.TryGetHeightRepresentationDescriptor(
                    sampleStride,
                    out TerrainHeightStreamingLevelDescriptor descriptor
                )
            )
            {
                errorMessage =
                    $"Height representation stride {sampleStride} has no valid manifest descriptor.";

                return false;
            }

            if (
                descriptor.TileGridWidth !=
                    tileGridWidth
                ||
                descriptor.TileGridHeight !=
                    tileGridHeight
                ||
                descriptor.TextureFormat !=
                    TextureFormat.RFloat
            )
            {
                errorMessage =
                    $"Height representation stride {sampleStride} has an incompatible descriptor.";

                return false;
            }

            string strideLabel =
                GetStrideLabel(
                    sampleStride
                );

            expectedStrideLabels.Add(
                strideLabel
            );

            for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
            {
                for (int tileX = 0; tileX < tileGridWidth; tileX++)
                {
                    string assetPath =
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

                    Texture2D texture =
                        AssetDatabase
                            .LoadAssetAtPath<Texture2D>(
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
                            texture.width !=
                                descriptor.SamplesPerSide
                            ||
                            texture.height !=
                                descriptor.SamplesPerSide
                            ||
                            texture.format !=
                                TextureFormat.RFloat
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

                    string guid =
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

                    if (!expectedGuids.Add(guid))
                    {
                        errorMessage =
                            "Multiple Height representation identities resolve to the same GUID:\n" +
                            guid;

                        return false;
                    }

                    records.Add(
                        new HeightRepresentationAssetRecord(
                            sampleStride,
                            tileX,
                            tileZ,
                            assetPath,
                            guid,
                            manifest.GetHeightRepresentationAddress(
                                sampleStride,
                                tileX,
                                tileZ
                            ),
                            strideLabel,
                            descriptor
                        )
                    );
                }
            }
        }

        return true;
    }

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
