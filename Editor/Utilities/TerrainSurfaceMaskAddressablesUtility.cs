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
    // LEGACY PREPARE
    // =====================================================

    public static void PrepareSurfaceMaskTilesForRuntime()
    {
        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot prepare surface-mask tiles for runtime.\n\n" +
                "Surface-mask manifest does not exist:\n" +
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

            return;
        }

        if (
            PrepareSurfaceMaskTilesForRuntime(
                manifest
            )
        )
        {
            Debug.Log(
                "Surface-mask Addressables structural configuration is valid."
            );
        }
    }

    public static bool PrepareSurfaceMaskTilesForRuntime(
        TerrainSurfaceMaskManifest manifest
    )
    {
        TerrainAddressablesOperationStats stats =
            new TerrainAddressablesOperationStats();

        bool success =
            ReconcileConfiguration(
                manifest,
                stats,
                out bool cancelled,
                out string errorMessage
            );

        if (!success)
        {
            if (cancelled)
            {
                Debug.LogWarning(
                    "Preparing surface-mask Addressables was cancelled."
                );
            }
            else
            {
                Debug.LogError(
                    errorMessage
                );
            }

            return false;
        }

        Debug.Log(
            "Surface-mask tiles prepared for runtime streaming.\n\n" +
            "Addressables Group: " +
            SurfaceMaskAddressablesGroupName +
            "\n\n" +
            "Created Entries: " +
            stats.entriesCreated +
            "\n" +
            "Moved Entries: " +
            stats.entriesMoved +
            "\n" +
            "Addresses Updated: " +
            stats.addressesUpdated +
            "\n" +
            "Obsolete Entries Removed: " +
            stats.entriesRemoved
        );

        return true;
    }

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
            schema.BundleMode
                != BundledAssetGroupSchema.BundlePackingMode.PackSeparately
            ||
            !schema.IncludeAddressInCatalog
        )
        {
            errorMessage =
                "Surface Addressables group schema is missing or incompatible.";

            return false;
        }

        HashSet<string> expectedGuids =
            new HashSet<string>();

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
            }
        }

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (!expectedGuids.Contains(entry.guid))
            {
                errorMessage =
                    "Surface Addressables group contains an obsolete/unexpected entry:\n" +
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
        TerrainSurfaceMaskManifest manifest,
        TerrainAddressablesOperationStats stats,
        out bool cancelled,
        out string errorMessage
    )
    {
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
                manifest.tileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                1,
                manifest.tileGridHeight
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
                        entry =
                            settings.CreateOrMoveEntry(
                                guid,
                                group,
                                false,
                                true
                            );

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
                        entry =
                            settings.CreateOrMoveEntry(
                                guid,
                                group,
                                false,
                                true
                            );

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
                        entry.SetAddress(
                            expectedAddress,
                            true
                        );

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
                stats.surfaceConfigurationChanged = true;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
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
            group.RemoveAssetEntry(
                obsoleteEntry,
                true
            );

            stats.entriesRemoved++;
            configurationChanged = true;
        }

        if (configurationChanged)
        {
            stats.surfaceConfigurationChanged = true;

            EditorUtility.SetDirty(
                settings
            );

            AssetDatabase.SaveAssets();
        }

        return true;
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
