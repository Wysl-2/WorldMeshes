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
            !TryCollectCollisionAssetRecords(
                worldSettings,
                false,
                out List<CollisionAssetRecord> records,
                out _,
                out HashSet<string> expectedRegionLabels,
                out _,
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

        HashSet<string> expectedGuids =
            new HashSet<string>();

        foreach (CollisionAssetRecord record in records)
        {
            if (
                !ValidateEntry(
                    settings,
                    group,
                    record.guid,
                    record.address,
                    record.regionLabel,
                    record.assetPath,
                    out errorMessage
                )
            )
            {
                return false;
            }

            expectedGuids.Add(
                record.guid
            );
        }

        if (markerRecords == null)
        {
            errorMessage =
                "Collision bake marker records are unavailable.";

            return false;
        }

        for (int index = 0; index < markerRecords.Count; index++)
        {
            TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker =
                markerRecords[index];

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

            expectedGuids.Add(
                marker.guid
            );

            expectedRegionLabels.Add(
                marker.regionLabel
            );
        }

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (!expectedGuids.Contains(entry.guid))
            {
                errorMessage =
                    "Collision Addressables group contains an obsolete/unexpected entry:\n" +
                    entry.address;

                return false;
            }
        }

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        foreach (string expectedLabel in expectedRegionLabels)
        {
            if (!registeredLabels.Contains(expectedLabel))
            {
                errorMessage =
                    "Collision Addressables region label is missing:\n" +
                    expectedLabel;

                return false;
            }
        }

        foreach (string label in registeredLabels)
        {
            if (
                label.StartsWith(
                    TerrainCollisionManifest.CollisionRegionLabelPrefix +
                    "_"
                )
                &&
                !expectedRegionLabels.Contains(
                    label
                )
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

        /*
         * Generation revisions are intentionally excluded here. A complete
         * structurally valid manifest may lag the newly generated collision
         * Mesh contents and be refreshed without Addressables reconfiguration.
         */
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
            TerrainGenerationStateUtility.GetCollisionMeshStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Generated collision meshes are not current.";

            return false;
        }

        if (
            !TryCollectCollisionAssetRecords(
                worldSettings,
                true,
                out List<CollisionAssetRecord> records,
                out HashSet<string> meshGuids,
                out HashSet<string> expectedRegionLabels,
                out cancelled,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TerrainCollisionBakeMarkerUtility.ReconcileBakeMarkers(
                worldSettings,
                out List<
                    TerrainCollisionBakeMarkerUtility.BakeMarkerRecord
                > markerRecords,
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

        bool manifestCreated;

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

        foreach (
            TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker
            in markerRecords
        )
        {
            if (!meshGuids.Add(marker.guid))
            {
                errorMessage =
                    "Collision bake marker GUID collides with another managed collision asset:\n" +
                    marker.assetPath;

                return false;
            }

            expectedRegionLabels.Add(
                marker.regionLabel
            );
        }

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

        int totalEntries =
            records.Count +
            markerRecords.Count;

        int currentEntry = 0;

        try
        {
            foreach (CollisionAssetRecord record in records)
            {
                cancelled =
                    EditorUtility.DisplayCancelableProgressBar(
                        "Preparing Collision Addressables",
                        "Collision Mesh (" +
                        record.chunkX +
                        ", " +
                        record.chunkZ +
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
                    !ReconcileEntry(
                        settings,
                        group,
                        record.guid,
                        record.address,
                        record.regionLabel,
                        record.assetPath,
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

            if (!cancelled)
            {
                foreach (
                    TerrainCollisionBakeMarkerUtility.BakeMarkerRecord marker
                    in markerRecords
                )
                {
                    cancelled =
                        EditorUtility.DisplayCancelableProgressBar(
                            "Preparing Collision Addressables",
                            "Collision Bake Marker Region (" +
                            marker.regionX +
                            ", " +
                            marker.regionZ +
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

        List<AddressableAssetEntry> obsoleteEntries =
            new List<AddressableAssetEntry>();

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (!meshGuids.Contains(entry.guid))
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

        List<string> staleRegionLabels =
            new List<string>();

        foreach (string label in settings.GetLabels())
        {
            if (
                label.StartsWith(
                    TerrainCollisionManifest.CollisionRegionLabelPrefix +
                    "_"
                )
                &&
                !expectedRegionLabels.Contains(
                    label
                )
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
    // EXPECTED ASSETS
    // =====================================================

    private static bool TryCollectCollisionAssetRecords(
        WorldSettings worldSettings,
        bool showProgress,
        out List<CollisionAssetRecord> records,
        out HashSet<string> expectedGuids,
        out HashSet<string> expectedRegionLabels,
        out bool cancelled,
        out string errorMessage
    )
    {
        records =
            new List<CollisionAssetRecord>();

        expectedGuids =
            new HashSet<string>();

        expectedRegionLabels =
            new HashSet<string>();

        cancelled = false;
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

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
                "Generated collision meshes are not current.";

            return false;
        }

        int gridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        int gridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        int expectedMeshCount =
            gridWidth *
            gridHeight;

        records =
            new List<CollisionAssetRecord>(
                expectedMeshCount
            );

        int currentMesh = 0;

        try
        {
            for (int chunkZ = 0; chunkZ < gridHeight; chunkZ++)
            {
                for (int chunkX = 0; chunkX < gridWidth; chunkX++)
                {
                    if (showProgress)
                    {
                        cancelled =
                            EditorUtility.DisplayCancelableProgressBar(
                                "Preparing Collision Addressables",
                                "Validating generated collision mesh (" +
                                chunkX +
                                ", " +
                                chunkZ +
                                ")\n\n" +
                                (currentMesh + 1) +
                                " / " +
                                expectedMeshCount,
                                expectedMeshCount > 0
                                    ? (float)currentMesh / expectedMeshCount
                                    : 1f
                            );

                        if (cancelled)
                        {
                            break;
                        }
                    }

                    string assetPath =
                        TerrainCollisionMeshGenerator.GetCollisionMeshPath(
                            chunkX,
                            chunkZ
                        );

                    Mesh mesh =
                        AssetDatabase.LoadAssetAtPath<Mesh>(
                            assetPath
                        );

                    if (mesh == null)
                    {
                        errorMessage =
                            "Missing collision Mesh (" +
                            chunkX +
                            ", " +
                            chunkZ +
                            "):\n" +
                            assetPath;

                        return false;
                    }

                    string guid =
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

                    if (!expectedGuids.Add(guid))
                    {
                        errorMessage =
                            "Multiple collision coordinates resolved to the same GUID:\n" +
                            guid;

                        return false;
                    }

                    string address =
                        GetCollisionMeshAddress(
                            chunkX,
                            chunkZ
                        );

                    string regionLabel =
                        GetCollisionRegionLabel(
                            chunkX,
                            chunkZ
                        );

                    expectedRegionLabels.Add(
                        regionLabel
                    );

                    records.Add(
                        new CollisionAssetRecord(
                            chunkX,
                            chunkZ,
                            assetPath,
                            guid,
                            address,
                            regionLabel
                        )
                    );

                    currentMesh++;
                }

                if (cancelled)
                {
                    break;
                }
            }
        }
        finally
        {
            if (showProgress)
            {
                EditorUtility.ClearProgressBar();
            }
        }

        if (cancelled)
        {
            return false;
        }

        return
            records.Count ==
            expectedMeshCount;
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

        bool labelsCorrect =
            entry.labels.Count == 1
            &&
            entry.labels.Contains(
                expectedRegionLabel
            );

        if (!labelsCorrect)
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

        bool labelsCorrect =
            entry.labels.Count == 1
            &&
            entry.labels.Contains(
                expectedRegionLabel
            );

        if (!labelsCorrect)
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

    // =====================================================
    // RECORD
    // =====================================================

    private readonly struct CollisionAssetRecord
    {
        public readonly int chunkX;
        public readonly int chunkZ;
        public readonly string assetPath;
        public readonly string guid;
        public readonly string address;
        public readonly string regionLabel;

        public CollisionAssetRecord(
            int chunkX,
            int chunkZ,
            string assetPath,
            string guid,
            string address,
            string regionLabel
        )
        {
            this.chunkX = chunkX;
            this.chunkZ = chunkZ;
            this.assetPath = assetPath;
            this.guid = guid;
            this.address = address;
            this.regionLabel = regionLabel;
        }
    }
}
