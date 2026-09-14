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

    // =====================================================
    // READ-ONLY VALIDATION
    // =====================================================

    public static bool ValidateExistingConfiguration(
        TerrainHeightmapManifest manifest,
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
            schema.BundleMode
                != BundledAssetGroupSchema.BundlePackingMode.PackSeparately
            ||
            !schema.IncludeAddressInCatalog
        )
        {
            errorMessage =
                "Height Addressables group schema is missing or incompatible.";

            return false;
        }

        HashSet<string> expectedGuids =
            new HashSet<string>();

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

                expectedGuids.Add(
                    guid
                );

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

                if (entry.address != expectedAddress)
                {
                    errorMessage =
                        "Height Addressables entry has an incorrect address:\n" +
                        assetPath +
                        "\n\nExpected: " +
                        expectedAddress +
                        "\nActual: " +
                        entry.address;

                    return false;
                }
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

        return true;
    }

    // =====================================================
    // STRUCTURAL RECONCILIATION
    // =====================================================

    internal static bool ReconcileConfiguration(
        TerrainHeightmapManifest manifest,
        TerrainAddressablesOperationStats stats,
        out bool cancelled,
        out string errorMessage
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.AddressablesHeightReconcile.Auto();

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
                    "Could not configure the height Addressables Content Packing & Loading schema.";

                return false;
            }

            stats.schemasCreatedOrChanged++;
            configurationChanged = true;
        }

        bool schemaChanged = false;

        if (
            schema.BundleMode
            != BundledAssetGroupSchema.BundlePackingMode.PackSeparately
        )
        {
            schema.BundleMode =
                BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

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
                manifest.heightTileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                manifest.heightTileGridHeight
            );

        int expectedTileCount =
            tileGridWidth *
            tileGridHeight;

        int currentTile = 0;

        HashSet<string> expectedGuids =
            new HashSet<string>();

        try
        {
            for (int tileZ = 0; tileZ < tileGridHeight; tileZ++)
            {
                for (int tileX = 0; tileX < tileGridWidth; tileX++)
                {
                    cancelled =
                        EditorUtility.DisplayCancelableProgressBar(
                            "Preparing Heightmap Addressables",
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

                    expectedGuids.Add(
                        guid
                    );

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
                stats.heightConfigurationChanged = true;
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
        }

        if (configurationChanged)
        {
            stats.heightConfigurationChanged = true;

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
    // SHARED PREFLIGHT
    // =====================================================

    private static bool ValidateManifest(
        TerrainHeightmapManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

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

        return true;
    }

    private static bool TryGetExpectedTile(
        TerrainHeightmapManifest manifest,
        int tileX,
        int tileZ,
        out string assetPath,
        out string guid,
        out string expectedAddress,
        out string errorMessage
    )
    {
        assetPath =
            TerrainRuntimeHeightAssetUtility.GetHeightTilePath(
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
                "Missing runtime height tile:\n" +
                assetPath;

            return false;
        }

        if (
            texture.width != manifest.heightTileSamplesPerSide
            ||
            texture.height != manifest.heightTileSamplesPerSide
            ||
            texture.format != TextureFormat.RFloat
        )
        {
            errorMessage =
                "Runtime height tile has an invalid layout or format:\n" +
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
                "Could not resolve runtime height tile GUID:\n" +
                assetPath;

            return false;
        }

        expectedAddress =
            manifest.GetHeightTileAddress(
                tileX,
                tileZ
            );

        return true;
    }
}
