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

    // =====================================================
    // READ-ONLY VALIDATION
    // =====================================================

    public static bool ValidateExistingConfiguration(
        TerrainSurfaceMaskManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (!ValidateManifest(manifest, out errorMessage))
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

        int tileGridWidth =
            Mathf.Max(
                1,
                manifest.tileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                manifest.tileGridHeight
            );

        long expectedTileCountLong =
            (long)tileGridWidth *
            tileGridHeight;

        if (expectedTileCountLong > int.MaxValue)
        {
            errorMessage =
                "Surface Addressables expected entry count exceeds the supported Int32 range.";

            return false;
        }

        HashSet<string> expectedGuids =
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

        for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
        {
            for (int tileX = 0; tileX < tileGridWidth; tileX++)
            {
                if (
                    !TryGetExpectedTile(
                        manifest,
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
                        "Multiple Surface tile identities resolve to the same GUID:\n" +
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
                    !EntryHasExactRegionLabel(
                        entry,
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

        if (group.entries.Count != (int)expectedTileCountLong)
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
                !TryGetSingleManagedRegionLabel(
                    entry,
                    out string regionLabel
                )
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
        TerrainSurfaceMaskManifest manifest,
        TerrainAddressablesOperationStats stats,
        out bool cancelled,
        out string errorMessage
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.AddressablesSurfaceReconcile.Auto();

        cancelled = false;
        errorMessage = "";

        if (stats == null)
        {
            stats =
                new TerrainAddressablesOperationStats();
        }

        if (!ValidateManifest(manifest, out errorMessage))
        {
            return false;
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

        bool configurationChanged = false;

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

                return false;
            }

            stats.groupsCreated++;
            configurationChanged = true;
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

                return false;
            }

            stats.schemasCreatedOrChanged++;
            configurationChanged = true;
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
        }

        int tileGridWidth =
            Mathf.Max(
                1,
                manifest.tileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                manifest.tileGridHeight
            );

        long expectedTileCountLong =
            (long)tileGridWidth *
            tileGridHeight;

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
            expectedTileCountLong > int.MaxValue
            ||
            regionCountLong > int.MaxValue
        )
        {
            errorMessage =
                "Surface Addressables scale exceeds the supported Int32 entry or region range.";

            return false;
        }

        int expectedTileCount =
            (int)expectedTileCountLong;

        stats.surfacePackingRegionTileSpan =
            TerrainSurfaceAddressablesPackingPolicy
                .RegionTileSpan;

        stats.surfacePackingRegionCount =
            (int)regionCountLong;

        stats.surfaceManagedRegionLabelCount =
            (int)regionCountLong;

        int currentTile = 0;

        HashSet<string> expectedGuids =
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
        }

        try
        {
            for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
            {
                for (int tileX = 0; tileX < tileGridWidth; tileX++)
                {
                    cancelled =
                        EditorUtility.DisplayCancelableProgressBar(
                            "Preparing Surface Mask Addressables",
                            "Tile (" +
                            tileX +
                            ", " +
                            tileZ +
                            ")\n\n" +
                            (currentTile + 1) +
                            " / " +
                            expectedTileCount,
                            expectedTileCount > 0
                                ? (float)currentTile / expectedTileCount
                                : 1f
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    if (
                        !TryGetExpectedTile(
                            manifest,
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
                            "Multiple Surface tile identities resolve to the same GUID:\n" +
                            guid;

                        return false;
                    }

                    string expectedRegionLabel =
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
                            expectedRegionLabel,
                            assetPath,
                            stats,
                            ref configurationChanged,
                            out errorMessage
                        )
                    )
                    {
                        return false;
                    }

                    currentTile++;
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
                stats.surfaceConfigurationChanged = true;
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
            if (
                !expectedGuids.Contains(entry.guid)
                ||
                !TryGetSingleManagedRegionLabel(
                    entry,
                    out string regionLabel
                )
                ||
                !expectedRegionLabels.Contains(regionLabel)
            )
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
        }

        List<string> staleRegionLabels =
            new List<string>();

        foreach (string label in settings.GetLabels())
        {
            if (
                TerrainSurfaceAddressablesPackingPolicy
                    .IsManagedRegionLabel(label)
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
        }

        if (configurationChanged)
        {
            stats.surfaceConfigurationChanged = true;

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
    // ENTRY RECONCILIATION
    // =====================================================

    private static bool ReconcileEntry(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string guid,
        string expectedAddress,
        string expectedRegionLabel,
        string assetPath,
        TerrainAddressablesOperationStats stats,
        ref bool configurationChanged,
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
        }

        if (
            !EntryHasExactRegionLabel(
                entry,
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
                    expectedRegionLabel,
                    true,
                    false,
                    true
                );
            }

            stats.labelsUpdated++;
            configurationChanged = true;
        }

        return true;
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
            if (
                !TerrainSurfaceAddressablesPackingPolicy
                    .IsManagedRegionLabel(label)
            )
            {
                return false;
            }

            regionLabel =
                label;
        }

        return
            !string.IsNullOrEmpty(
                regionLabel
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
    // SHARED PREFLIGHT
    // =====================================================

    private static bool ValidateManifest(
        TerrainSurfaceMaskManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (manifest == null)
        {
            errorMessage =
                "Surface-mask manifest is missing.";

            return false;
        }

        if (!manifest.isComplete)
        {
            errorMessage =
                "Surface-mask manifest is incomplete.";

            return false;
        }

        return true;
    }

    private static bool TryGetExpectedTile(
        TerrainSurfaceMaskManifest manifest,
        int tileX,
        int tileZ,
        out string assetPath,
        out string guid,
        out string expectedAddress,
        out string errorMessage
    )
    {
        assetPath =
            TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(
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
                "Missing runtime surface-mask tile:\n" +
                assetPath;

            return false;
        }

        try
        {
            if (
                texture.width != manifest.samplesPerSide
                ||
                texture.height != manifest.samplesPerSide
                ||
                texture.format != TextureFormat.R8
            )
            {
                errorMessage =
                    "Runtime surface-mask tile has an invalid layout or format:\n" +
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
                "Could not resolve runtime surface-mask tile GUID:\n" +
                assetPath;

            return false;
        }

        expectedAddress =
            manifest.GetSurfaceTileAddress(
                tileX,
                tileZ
            );

        return true;
    }
}
