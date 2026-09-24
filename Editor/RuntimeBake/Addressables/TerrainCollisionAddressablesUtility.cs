using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class TerrainCollisionAddressablesUtility
{
    public const string CollisionAddressablesGroupName =
        "Terrain Collision Meshes";

    public const int CollisionRegionChunkSpan =
        8;

    private const string CollisionManifestPath =
        WorldMeshesPaths.CollisionManifestAssetPath;

    // =====================================================
    // READ-ONLY VALIDATION
    // =====================================================

    internal static bool ValidateExistingConfiguration(
        WorldSettings worldSettings,
        IReadOnlyList<TerrainCollisionBakeMarkerUtility.BakeMarkerRecord>
            markerRecords,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            worldSettings == null
            ||
            TerrainGenerationStateUtility.GetCollisionMeshStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                worldSettings == null
                    ? "WorldSettings is null."
                    : "Generated collision meshes are not current.";

            return false;
        }

        if (markerRecords == null)
        {
            errorMessage =
                "Collision bake marker records are unavailable.";

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
                CollisionAddressablesGroupName
            );

        if (group == null)
        {
            errorMessage =
                "Collision Addressables group is missing: " +
                CollisionAddressablesGroupName;

            return false;
        }

        BundledAssetGroupSchema schema =
            group.GetSchema<BundledAssetGroupSchema>();

        if (
            schema == null
            ||
            schema.BundleMode
                != BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel
            ||
            !schema.IncludeAddressInCatalog
        )
        {
            errorMessage =
                "Collision Addressables group schema is missing or incompatible.";

            return false;
        }

        GetRegionLayout(
            worldSettings,
            out int gridWidth,
            out int gridHeight,
            out int regionSpan,
            out int regionGridWidth,
            out int regionGridHeight
        );

        long expectedMeshCountLong =
            (long)gridWidth *
            gridHeight;

        long expectedRegionCountLong =
            (long)regionGridWidth *
            regionGridHeight;

        long expectedEntryCountLong =
            expectedMeshCountLong +
            markerRecords.Count;

        if (
            expectedMeshCountLong > int.MaxValue
            ||
            expectedRegionCountLong > int.MaxValue
            ||
            expectedEntryCountLong > int.MaxValue
        )
        {
            errorMessage =
                "Collision Addressables scale exceeds the supported Int32 entry range.";

            return false;
        }

        Dictionary<string, TerrainCollisionBakeMarkerUtility.BakeMarkerRecord>
            markerByRegionLabel =
                new Dictionary<string, TerrainCollisionBakeMarkerUtility.BakeMarkerRecord>();

        for (int index = 0; index < markerRecords.Count; index++)
        {
            TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker =
                markerRecords[index];

            if (
                string.IsNullOrEmpty(marker.regionLabel)
                ||
                markerByRegionLabel.ContainsKey(marker.regionLabel)
            )
            {
                errorMessage =
                    "Collision bake marker region identity is missing or duplicated.";

                return false;
            }

            markerByRegionLabel.Add(
                marker.regionLabel,
                marker
            );
        }

        if (markerByRegionLabel.Count != (int)expectedRegionCountLong)
        {
            errorMessage =
                "Collision bake marker count does not match the expected collision region count.";

            return false;
        }

        HashSet<string> expectedRegionLabels =
            new HashSet<string>();

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        for (int regionZ = 0; regionZ < regionGridHeight; regionZ++)
        {
            for (int regionX = 0; regionX < regionGridWidth; regionX++)
            {
                string regionLabel =
                    GetCollisionRegionLabelForRegion(
                        regionX,
                        regionZ,
                        regionSpan
                    );

                expectedRegionLabels.Add(
                    regionLabel
                );

                if (!registeredLabels.Contains(regionLabel))
                {
                    errorMessage =
                        "Collision Addressables region label is missing:\n" +
                        regionLabel;

                    return false;
                }

                if (
                    !markerByRegionLabel.TryGetValue(
                        regionLabel,
                        out TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker
                    )
                )
                {
                    errorMessage =
                        "Collision bake marker is missing for region label:\n" +
                        regionLabel;

                    return false;
                }

                HashSet<string> expectedRegionGuids =
                    new HashSet<string>();

                int startChunkX =
                    regionX * regionSpan;

                int startChunkZ =
                    regionZ * regionSpan;

                int endChunkX =
                    Mathf.Min(
                        startChunkX + regionSpan,
                        gridWidth
                    );

                int endChunkZ =
                    Mathf.Min(
                        startChunkZ + regionSpan,
                        gridHeight
                    );

                for (int chunkZ = startChunkZ; chunkZ < endChunkZ; chunkZ++)
                {
                    for (int chunkX = startChunkX; chunkX < endChunkX; chunkX++)
                    {
                        if (
                            !TryResolveCollisionMeshIdentity(
                                chunkX,
                                chunkZ,
                                out string assetPath,
                                out string guid,
                                out string address,
                                out string expectedRegionLabel,
                                out errorMessage
                            )
                        )
                        {
                            return false;
                        }

                        if (!expectedRegionGuids.Add(guid))
                        {
                            errorMessage =
                                "Multiple collision coordinates resolved to the same GUID:\n" +
                                guid;

                            return false;
                        }

                        if (
                            !ValidateEntry(
                                settings,
                                group,
                                guid,
                                address,
                                expectedRegionLabel,
                                assetPath,
                                out errorMessage
                            )
                        )
                        {
                            return false;
                        }
                    }
                }

                if (!expectedRegionGuids.Add(marker.guid))
                {
                    errorMessage =
                        "Collision bake marker GUID collides with another managed collision asset:\n" +
                        marker.assetPath;

                    return false;
                }

                if (
                    !ValidateEntry(
                        settings,
                        group,
                        marker.guid,
                        marker.address,
                        marker.regionLabel,
                        marker.assetPath,
                        out errorMessage
                    )
                )
                {
                    return false;
                }
            }
        }

        if (group.entries.Count != (int)expectedEntryCountLong)
        {
            errorMessage =
                "Collision Addressables group contains an obsolete/unexpected entry or has an unexpected entry count.";

            return false;
        }

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (
                !TryGetSingleManagedRegionLabel(
                    entry,
                    out string regionLabel
                )
                ||
                !expectedRegionLabels.Contains(regionLabel)
            )
            {
                errorMessage =
                    "Collision Addressables group contains an obsolete/unexpected entry:\n" +
                    entry.address;

                return false;
            }
        }

        foreach (string label in registeredLabels)
        {
            if (
                IsManagedRegionLabel(label)
                &&
                !expectedRegionLabels.Contains(label)
            )
            {
                errorMessage =
                    "Collision Addressables contains a stale tool-owned region label:\n" +
                    label;

                return false;
            }
        }

        return true;
    }

    public static bool ValidatePreparedManifestStructure(
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is unavailable.";

            return false;
        }

        TerrainCollisionManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                CollisionManifestPath
            );

        if (manifest == null)
        {
            errorMessage =
                "Collision prepared manifest is missing.";

            return false;
        }

        if (!manifest.isComplete)
        {
            errorMessage =
                "Collision prepared manifest is incomplete.";

            return false;
        }

        if (
            manifest.manifestVersion != 1
            ||
            manifest.collisionGeneratorVersion
                != TerrainGenerationStateUtility.CollisionGeneratorVersion
            ||
            manifest.gridWidth != Mathf.Max(1, worldSettings.gridWidth)
            ||
            manifest.gridHeight != Mathf.Max(1, worldSettings.gridHeight)
            ||
            !Mathf.Approximately(
                manifest.chunkSize,
                Mathf.Max(0.01f, worldSettings.chunkSize)
            )
            ||
            manifest.heightfieldResolutionPerChunk
                != Mathf.Max(
                    1,
                    worldSettings.heightfieldResolutionPerChunk
                )
            ||
            manifest.collisionResolution
                != Mathf.Max(
                    1,
                    worldSettings.collisionResolution
                )
            ||
            manifest.regionChunkSpan
                != Mathf.Max(
                    1,
                    CollisionRegionChunkSpan
                )
        )
        {
            errorMessage =
                "Collision prepared manifest structure does not match the current world/collision layout.";

            return false;
        }

        return true;
    }

    // =====================================================
    // STRUCTURAL RECONCILIATION
    // =====================================================

    internal static bool ReconcileConfiguration(
        WorldSettings worldSettings,
        TerrainAddressablesOperationStats stats,
        out bool cancelled,
        out string errorMessage
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.AddressablesCollisionReconcile.Auto();

        cancelled = false;
        errorMessage = "";

        if (stats == null)
        {
            stats =
                new TerrainAddressablesOperationStats();
        }

        if (
            worldSettings == null
            ||
            TerrainGenerationStateUtility.GetCollisionMeshStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                worldSettings == null
                    ? "WorldSettings is null."
                    : "Generated collision meshes are not current.";

            return false;
        }

        if (
            !TerrainCollisionBakeMarkerUtility.ReconcileBakeMarkers(
                worldSettings,
                out List<TerrainCollisionBakeMarkerUtility.BakeMarkerRecord>
                    markerRecords,
                out int regeneratedMarkers,
                out int reusedMarkers,
                out int removedMarkers,
                out cancelled,
                out errorMessage
            )
        )
        {
            return false;
        }

        stats.collisionMarkersRegenerated +=
            regeneratedMarkers;

        stats.collisionMarkersReused +=
            reusedMarkers;

        stats.collisionMarkersRemoved +=
            removedMarkers;

        bool configurationChanged =
            regeneratedMarkers > 0
            ||
            removedMarkers > 0;

        bool settingsChanged =
            false;

        TerrainCollisionManifest manifest =
            GetOrCreateManifest(
                out bool manifestCreated
            );

        if (manifest == null)
        {
            errorMessage =
                "Could not create/load collision runtime manifest.";

            return false;
        }

        if (manifestCreated)
        {
            stats.collisionManifestCreated = true;
            configurationChanged = true;
        }

        if (manifest.isComplete)
        {
            manifest.isComplete = false;
            EditorUtility.SetDirty(manifest);

            using (WorldMeshesProfiler.AssetDatabaseSaveAssetIfDirty.Auto())
            {
                AssetDatabase.SaveAssetIfDirty(manifest);
            }
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(
                true
            );

        if (settings == null)
        {
            errorMessage =
                "Could not load or create Addressables settings.";

            return false;
        }

        AddressableAssetGroup group =
            settings.FindGroup(
                CollisionAddressablesGroupName
            );

        if (group == null)
        {
            List<AddressableAssetGroupSchema> schemasToCopy =
                settings.DefaultGroup != null
                    ? settings.DefaultGroup.Schemas
                    : null;

            group =
                settings.CreateGroup(
                    CollisionAddressablesGroupName,
                    false,
                    false,
                    true,
                    schemasToCopy
                );

            if (group == null)
            {
                errorMessage =
                    "Could not create Addressables group:\n" +
                    CollisionAddressablesGroupName;

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
                    "Could not configure collision Addressables Content Packing & Loading schema.";

                return false;
            }

            stats.schemasCreatedOrChanged++;
            configurationChanged = true;
            settingsChanged = true;
        }

        bool schemaChanged = false;

        if (
            schema.BundleMode
            != BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel
        )
        {
            schema.BundleMode =
                BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;

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

        GetRegionLayout(
            worldSettings,
            out int gridWidth,
            out int gridHeight,
            out int regionSpan,
            out int regionGridWidth,
            out int regionGridHeight
        );

        long expectedMeshCountLong =
            (long)gridWidth *
            gridHeight;

        long expectedRegionCountLong =
            (long)regionGridWidth *
            regionGridHeight;

        long totalEntriesLong =
            expectedMeshCountLong +
            markerRecords.Count;

        if (
            expectedMeshCountLong > int.MaxValue
            ||
            expectedRegionCountLong > int.MaxValue
            ||
            totalEntriesLong > int.MaxValue
        )
        {
            errorMessage =
                "Collision Addressables scale exceeds the supported Int32 entry range.";

            return false;
        }

        int totalEntries =
            (int)totalEntriesLong;

        Dictionary<string, TerrainCollisionBakeMarkerUtility.BakeMarkerRecord>
            markerByRegionLabel =
                new Dictionary<string, TerrainCollisionBakeMarkerUtility.BakeMarkerRecord>();

        for (int index = 0; index < markerRecords.Count; index++)
        {
            TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker =
                markerRecords[index];

            if (
                string.IsNullOrEmpty(marker.regionLabel)
                ||
                markerByRegionLabel.ContainsKey(marker.regionLabel)
            )
            {
                errorMessage =
                    "Collision bake marker region identity is missing or duplicated.";

                return false;
            }

            markerByRegionLabel.Add(
                marker.regionLabel,
                marker
            );
        }

        if (markerByRegionLabel.Count != (int)expectedRegionCountLong)
        {
            errorMessage =
                "Collision bake marker count does not match the expected collision region count.";

            return false;
        }

        HashSet<string> expectedRegionLabels =
            new HashSet<string>();

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        Dictionary<string, List<AddressableAssetEntry>>
            existingEntriesByRegionLabel;

        List<AddressableAssetEntry> initiallyMalformedEntries;

        using (WorldMeshesProfiler.AddressablesCollisionCollectAssets.Auto())
        {
            BuildExistingRegionEntryIndex(
                group,
                out existingEntriesByRegionLabel,
                out initiallyMalformedEntries
            );
        }

        int currentEntry = 0;

        try
        {
            for (int regionZ = 0; regionZ < regionGridHeight; regionZ++)
            {
                for (int regionX = 0; regionX < regionGridWidth; regionX++)
                {
                    string regionLabel =
                        GetCollisionRegionLabelForRegion(
                            regionX,
                            regionZ,
                            regionSpan
                        );

                    expectedRegionLabels.Add(
                        regionLabel
                    );

                    if (!registeredLabels.Contains(regionLabel))
                    {
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

                    if (
                        !markerByRegionLabel.TryGetValue(
                            regionLabel,
                            out TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker
                        )
                    )
                    {
                        errorMessage =
                            "Collision bake marker is missing for region label:\n" +
                            regionLabel;

                        return false;
                    }

                    int startChunkX =
                        regionX * regionSpan;

                    int startChunkZ =
                        regionZ * regionSpan;

                    int endChunkX =
                        Mathf.Min(
                            startChunkX + regionSpan,
                            gridWidth
                        );

                    int endChunkZ =
                        Mathf.Min(
                            startChunkZ + regionSpan,
                            gridHeight
                        );

                    int regionMeshCount =
                        (endChunkX - startChunkX) *
                        (endChunkZ - startChunkZ);

                    HashSet<string> expectedRegionGuids =
                        new HashSet<string>();

                    for (int chunkZ = startChunkZ; chunkZ < endChunkZ; chunkZ++)
                    {
                        for (int chunkX = startChunkX; chunkX < endChunkX; chunkX++)
                        {
                            cancelled =
                                EditorUtility.DisplayCancelableProgressBar(
                                    "Preparing Collision Addressables",
                                    "Collision Mesh (" +
                                    chunkX +
                                    ", " +
                                    chunkZ +
                                    ")\n\n" +
                                    (currentEntry + 1) +
                                    " / " +
                                    totalEntries,
                                    totalEntries > 0
                                        ? (float)currentEntry / totalEntries
                                        : 1f
                                );

                            if (cancelled)
                            {
                                break;
                            }

                            if (
                                !TryResolveCollisionMeshIdentity(
                                    chunkX,
                                    chunkZ,
                                    out string assetPath,
                                    out string guid,
                                    out string address,
                                    out string expectedRegionLabel,
                                    out errorMessage
                                )
                            )
                            {
                                return false;
                            }

                            if (!expectedRegionGuids.Add(guid))
                            {
                                errorMessage =
                                    "Multiple collision coordinates resolved to the same GUID:\n" +
                                    guid;

                                return false;
                            }

                            if (
                                !ReconcileEntry(
                                    settings,
                                    group,
                                    guid,
                                    address,
                                    expectedRegionLabel,
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

                            currentEntry++;
                        }

                        if (cancelled)
                        {
                            break;
                        }
                    }

                    if (cancelled)
                    {
                        break;
                    }

                    cancelled =
                        EditorUtility.DisplayCancelableProgressBar(
                            "Preparing Collision Addressables",
                            "Collision Bake Marker Region (" +
                            regionX +
                            ", " +
                            regionZ +
                            ")\n\n" +
                            (currentEntry + 1) +
                            " / " +
                            totalEntries,
                            totalEntries > 0
                                ? (float)currentEntry / totalEntries
                                : 1f
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    if (!expectedRegionGuids.Add(marker.guid))
                    {
                        errorMessage =
                            "Collision bake marker GUID collides with another managed collision asset:\n" +
                            marker.assetPath;

                        return false;
                    }

                    if (
                        !ReconcileEntry(
                            settings,
                            group,
                            marker.guid,
                            marker.address,
                            marker.regionLabel,
                            marker.assetPath,
                            stats,
                            ref configurationChanged,
                            ref settingsChanged,
                            out errorMessage
                        )
                    )
                    {
                        return false;
                    }

                    currentEntry++;

                    stats.collisionPeakRegionExpectedGuidCount =
                        Mathf.Max(
                            stats.collisionPeakRegionExpectedGuidCount,
                            expectedRegionGuids.Count
                        );

                    if (
                        existingEntriesByRegionLabel.TryGetValue(
                            regionLabel,
                            out List<AddressableAssetEntry> existingRegionEntries
                        )
                    )
                    {
                        for (int index = 0; index < existingRegionEntries.Count; index++)
                        {
                            AddressableAssetEntry existingEntry =
                                existingRegionEntries[index];

                            if (
                                existingEntry == null
                                ||
                                existingEntry.parentGroup != group
                                ||
                                !EntryHasExactRegionLabel(
                                    existingEntry,
                                    regionLabel
                                )
                                ||
                                expectedRegionGuids.Contains(
                                    existingEntry.guid
                                )
                            )
                            {
                                continue;
                            }

                            using (WorldMeshesProfiler.AddressablesRemoveEntry.Auto())
                            {
                                group.RemoveAssetEntry(
                                    existingEntry,
                                    true
                                );
                            }

                            stats.entriesRemoved++;
                            configurationChanged = true;
                            settingsChanged = true;
                        }
                    }

                    if (
                        expectedRegionGuids.Count !=
                            regionMeshCount + 1
                    )
                    {
                        errorMessage =
                            "Collision Addressables region identity count did not match the expected mesh-plus-marker count.";

                        return false;
                    }
                }

                if (cancelled)
                {
                    break;
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
                stats.collisionConfigurationChanged = true;
            }

            if (settingsChanged)
            {
                EditorUtility.SetDirty(settings);

                using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
                {
                    AssetDatabase.SaveAssets();
                }
            }

            return false;
        }

        for (int index = 0; index < initiallyMalformedEntries.Count; index++)
        {
            AddressableAssetEntry malformedEntry =
                initiallyMalformedEntries[index];

            if (
                malformedEntry == null
                ||
                malformedEntry.parentGroup != group
                ||
                EntryHasOneExpectedRegionLabel(
                    malformedEntry,
                    expectedRegionLabels
                )
            )
            {
                continue;
            }

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

        List<string> staleRegionLabels =
            new List<string>();

        foreach (string label in settings.GetLabels())
        {
            if (
                IsManagedRegionLabel(label)
                &&
                !expectedRegionLabels.Contains(label)
            )
            {
                staleRegionLabels.Add(
                    label
                );
            }
        }

        foreach (string staleRegionLabel in staleRegionLabels)
        {
            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                settings.RemoveLabel(
                    staleRegionLabel,
                    true
                );
            }

            stats.labelsUpdated++;
            configurationChanged = true;
            settingsChanged = true;
        }

        if (configurationChanged)
        {
            stats.collisionConfigurationChanged = true;
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

        return true;
    }

    // =====================================================
    // METADATA-ONLY COLLISION PREPARATION
    // =====================================================

    public static bool RefreshRuntimeMetadata(
        WorldSettings worldSettings,
        out bool metadataChanged,
        out bool manifestCreated,
        out string errorMessage
    )
    {
        metadataChanged = false;
        manifestCreated = false;
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "Cannot refresh collision runtime metadata because WorldSettings is null.";

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetCollisionMeshStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Cannot refresh collision runtime metadata because generated collision meshes are not current.";

            return false;
        }

        TerrainCollisionManifest manifest =
            GetOrCreateManifest(
                out manifestCreated
            );

        if (manifest == null)
        {
            errorMessage =
                "Could not create/load collision runtime manifest.";

            return false;
        }

        bool changed = false;

        changed |= SetIfDifferent(
            ref manifest.manifestVersion,
            1
        );

        changed |= SetIfDifferent(
            ref manifest.collisionGeneratorVersion,
            TerrainGenerationStateUtility.CollisionGeneratorVersion
        );

        changed |= SetIfDifferent(
            ref manifest.gridWidth,
            Mathf.Max(1, worldSettings.gridWidth)
        );

        changed |= SetIfDifferent(
            ref manifest.gridHeight,
            Mathf.Max(1, worldSettings.gridHeight)
        );

        changed |= SetIfDifferent(
            ref manifest.chunkSize,
            Mathf.Max(0.01f, worldSettings.chunkSize)
        );

        changed |= SetIfDifferent(
            ref manifest.heightfieldResolutionPerChunk,
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            )
        );

        changed |= SetIfDifferent(
            ref manifest.collisionResolution,
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            )
        );

        changed |= SetIfDifferent(
            ref manifest.collisionMeshGenerationRevision,
            worldSettings.collisionMeshGenerationRevision
        );

        changed |= SetIfDifferent(
            ref manifest.collisionSourceHeightmapGenerationRevision,
            worldSettings.collisionSourceHeightmapGenerationRevision
        );

        changed |= SetIfDifferent(
            ref manifest.regionChunkSpan,
            Mathf.Max(
                1,
                CollisionRegionChunkSpan
            )
        );

        if (!manifest.isComplete)
        {
            manifest.isComplete = true;
            changed = true;
        }

        metadataChanged =
            changed
            ||
            manifestCreated;

        if (metadataChanged)
        {
            EditorUtility.SetDirty(
                manifest
            );

            using (WorldMeshesProfiler.AssetDatabaseSaveAssetIfDirty.Auto())
            {
                AssetDatabase.SaveAssetIfDirty(
                    manifest
                );
            }
        }

        return true;
    }

    // =====================================================
    // REGION / IDENTITY HELPERS
    // =====================================================

    private static void GetRegionLayout(
        WorldSettings worldSettings,
        out int gridWidth,
        out int gridHeight,
        out int regionSpan,
        out int regionGridWidth,
        out int regionGridHeight
    )
    {
        gridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        gridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        regionSpan =
            Mathf.Max(
                1,
                CollisionRegionChunkSpan
            );

        regionGridWidth =
            Mathf.CeilToInt(
                (float)gridWidth /
                regionSpan
            );

        regionGridHeight =
            Mathf.CeilToInt(
                (float)gridHeight /
                regionSpan
            );
    }

    private static bool TryResolveCollisionMeshIdentity(
        int chunkX,
        int chunkZ,
        out string assetPath,
        out string guid,
        out string address,
        out string regionLabel,
        out string errorMessage
    )
    {
        assetPath =
            TerrainCollisionMeshGenerator.GetCollisionMeshPath(
                chunkX,
                chunkZ
            );

        guid = "";
        address = "";
        regionLabel = "";
        errorMessage = "";

        if (
            AssetDatabase.GetMainAssetTypeAtPath(
                assetPath
            )
            != typeof(Mesh)
        )
        {
            errorMessage =
                "Missing or invalid collision Mesh (" +
                chunkX +
                ", " +
                chunkZ +
                "):\n" +
                assetPath;

            return false;
        }

        guid =
            AssetDatabase.AssetPathToGUID(
                assetPath
            );

        if (string.IsNullOrEmpty(guid))
        {
            errorMessage =
                "Could not resolve collision Mesh GUID:\n" +
                assetPath;

            return false;
        }

        address =
            GetCollisionMeshAddress(
                chunkX,
                chunkZ
            );

        regionLabel =
            GetCollisionRegionLabel(
                chunkX,
                chunkZ
            );

        return true;
    }

    private static void BuildExistingRegionEntryIndex(
        AddressableAssetGroup group,
        out Dictionary<string, List<AddressableAssetEntry>> entriesByRegionLabel,
        out List<AddressableAssetEntry> malformedEntries
    )
    {
        entriesByRegionLabel =
            new Dictionary<string, List<AddressableAssetEntry>>();

        malformedEntries =
            new List<AddressableAssetEntry>();

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (
                !TryGetSingleManagedRegionLabel(
                    entry,
                    out string regionLabel
                )
            )
            {
                malformedEntries.Add(
                    entry
                );

                continue;
            }

            if (
                !entriesByRegionLabel.TryGetValue(
                    regionLabel,
                    out List<AddressableAssetEntry> entries
                )
            )
            {
                entries =
                    new List<AddressableAssetEntry>();

                entriesByRegionLabel.Add(
                    regionLabel,
                    entries
                );
            }

            entries.Add(
                entry
            );
        }
    }

    private static bool TryGetSingleManagedRegionLabel(
        AddressableAssetEntry entry,
        out string regionLabel
    )
    {
        regionLabel = "";

        if (
            entry == null
            ||
            entry.labels.Count != 1
        )
        {
            return false;
        }

        foreach (string label in entry.labels)
        {
            if (!IsManagedRegionLabel(label))
            {
                return false;
            }

            regionLabel = label;
        }

        return
            !string.IsNullOrEmpty(regionLabel);
    }

    private static bool EntryHasExactRegionLabel(
        AddressableAssetEntry entry,
        string expectedRegionLabel
    )
    {
        return
            entry != null
            &&
            entry.labels.Count == 1
            &&
            entry.labels.Contains(
                expectedRegionLabel
            );
    }

    private static bool EntryHasOneExpectedRegionLabel(
        AddressableAssetEntry entry,
        HashSet<string> expectedRegionLabels
    )
    {
        if (
            !TryGetSingleManagedRegionLabel(
                entry,
                out string regionLabel
            )
        )
        {
            return false;
        }

        return
            expectedRegionLabels.Contains(
                regionLabel
            );
    }

    private static bool IsManagedRegionLabel(
        string label
    )
    {
        return
            !string.IsNullOrEmpty(label)
            &&
            label.StartsWith(
                TerrainCollisionManifest.CollisionRegionLabelPrefix + "_",
                System.StringComparison.Ordinal
            );
    }

    // =====================================================
    // ENTRY VALIDATE / RECONCILE
    // =====================================================

    private static bool ValidateEntry(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string guid,
        string expectedAddress,
        string expectedRegionLabel,
        string assetPath,
        out string errorMessage
    )
    {
        errorMessage = "";

        AddressableAssetEntry entry =
            settings.FindAssetEntry(
                guid
            );

        if (entry == null)
        {
            errorMessage =
                "Collision Addressables entry is missing:\n" +
                assetPath;

            return false;
        }

        if (entry.parentGroup != group)
        {
            errorMessage =
                "Collision Addressables entry is in the wrong group:\n" +
                assetPath;

            return false;
        }

        if (entry.address != expectedAddress)
        {
            errorMessage =
                "Collision Addressables entry has an incorrect address:\n" +
                assetPath;

            return false;
        }

        if (!EntryHasExactRegionLabel(entry, expectedRegionLabel))
        {
            errorMessage =
                "Collision Addressables entry has incorrect region labels:\n" +
                assetPath;

            return false;
        }

        return true;
    }

    private static bool ReconcileEntry(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string guid,
        string expectedAddress,
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
                    "Could not create collision Addressables entry for:\n" +
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
                    "Could not move collision Addressables entry for:\n" +
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

        if (!EntryHasExactRegionLabel(entry, expectedRegionLabel))
        {
            List<string> existingLabels =
                new List<string>(
                    entry.labels
                );

            using (WorldMeshesProfiler.AddressablesSetLabel.Auto())
            {
                foreach (string existingLabel in existingLabels)
                {
                    entry.SetLabel(
                        existingLabel,
                        false,
                        false,
                        true
                    );
                }

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

    // =====================================================
    // MANIFEST
    // =====================================================

    private static TerrainCollisionManifest GetOrCreateManifest(
        out bool created
    )
    {
        created = false;

        TerrainCollisionManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                CollisionManifestPath
            );

        if (manifest != null)
        {
            return manifest;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.GeneratedCollisionMeshes
            )
        )
        {
            return null;
        }

        manifest =
            ScriptableObject.CreateInstance<TerrainCollisionManifest>();

        if (manifest == null)
        {
            return null;
        }

        manifest.name =
            "CollisionManifest";

        manifest.isComplete =
            false;

        using (WorldMeshesProfiler.AssetDatabaseCreateAsset.Auto())
        {
            AssetDatabase.CreateAsset(
                manifest,
                CollisionManifestPath
            );
        }

        using (WorldMeshesProfiler.AssetDatabaseSaveAssetIfDirty.Auto())
        {
            AssetDatabase.SaveAssetIfDirty(
                manifest
            );
        }

        created = true;

        return manifest;
    }

    private static bool SetIfDifferent(
        ref int target,
        int value
    )
    {
        if (target == value)
        {
            return false;
        }

        target = value;
        return true;
    }

    private static bool SetIfDifferent(
        ref float target,
        float value
    )
    {
        if (Mathf.Approximately(target, value))
        {
            return false;
        }

        target = value;
        return true;
    }

    // =====================================================
    // ADDRESS / REGION
    // =====================================================

    private static string GetCollisionMeshAddress(
        int chunkX,
        int chunkZ
    )
    {
        return
            TerrainCollisionManifest.CollisionMeshAddressPrefix +
            "_" +
            chunkX +
            "_" +
            chunkZ;
    }

    private static string GetCollisionRegionLabel(
        int chunkX,
        int chunkZ
    )
    {
        int safeSpan =
            Mathf.Max(
                1,
                CollisionRegionChunkSpan
            );

        int regionX =
            chunkX /
            safeSpan;

        int regionZ =
            chunkZ /
            safeSpan;

        return
            TerrainCollisionManifest.CollisionRegionLabelPrefix +
            "_" +
            regionX +
            "_" +
            regionZ;
    }

    private static string GetCollisionRegionLabelForRegion(
        int regionX,
        int regionZ,
        int regionSpan
    )
    {
        return
            GetCollisionRegionLabel(
                regionX * regionSpan,
                regionZ * regionSpan
            );
    }
}
