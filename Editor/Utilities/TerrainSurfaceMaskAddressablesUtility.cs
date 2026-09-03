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

    public static void PrepareSurfaceMaskTilesForRuntime()
    {
        TerrainSurfaceMaskManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskManifestPath
                );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot prepare surface-mask tiles for runtime.\n\n" +
                "Surface-mask manifest does not exist:\n" +
                TerrainRuntimeSurfaceMaskAssetUtility
                    .SurfaceMaskManifestPath
            );

            return;
        }

        PrepareSurfaceMaskTilesForRuntime(
            manifest
        );
    }

    public static bool PrepareSurfaceMaskTilesForRuntime(
        TerrainSurfaceMaskManifest manifest
    )
    {
        if (manifest == null)
        {
            Debug.LogError(
                "Cannot prepare surface-mask tiles for runtime: manifest is null."
            );

            return false;
        }

        if (!manifest.isComplete)
        {
            Debug.LogError(
                "Cannot prepare surface-mask tiles for runtime.\n\n" +
                "The surface-mask manifest is incomplete.\n\n" +
                "Bake Runtime Surface Masks first."
            );

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

        int expectedTileCount =
            tileGridWidth *
            tileGridHeight;

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject
                .GetSettings(
                    true
                );

        if (settings == null)
        {
            Debug.LogError(
                "Could not load or create Addressables settings."
            );

            return false;
        }

        AddressableAssetGroup group =
            settings.FindGroup(
                SurfaceMaskAddressablesGroupName
            );

        if (group == null)
        {
            List<AddressableAssetGroupSchema>
                schemasToCopy =
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
        }

        if (group == null)
        {
            Debug.LogError(
                "Could not create Addressables group:\n" +
                SurfaceMaskAddressablesGroupName
            );

            return false;
        }

        BundledAssetGroupSchema bundledSchema =
            group
                .GetSchema<BundledAssetGroupSchema>();

        if (bundledSchema == null)
        {
            bundledSchema =
                group
                    .AddSchema<BundledAssetGroupSchema>(
                        true
                    );
        }

        if (bundledSchema == null)
        {
            Debug.LogError(
                "Could not configure the surface-mask Addressables Content Packing & Loading schema."
            );

            return false;
        }

        bundledSchema.BundleMode =
            BundledAssetGroupSchema
                .BundlePackingMode
                .PackSeparately;

        bundledSchema.IncludeAddressInCatalog =
            true;

        EditorUtility.SetDirty(
            bundledSchema
        );

        HashSet<string> expectedGuids =
            new HashSet<string>();

        int createdOrMovedCount =
            0;

        int addressUpdatedCount =
            0;

        int currentTile =
            0;

        bool cancelled =
            false;

        try
        {
            for (
                int tileZ = 0;
                tileZ < tileGridHeight;
                tileZ++
            )
            {
                for (
                    int tileX = 0;
                    tileX < tileGridWidth;
                    tileX++
                )
                {
                    cancelled =
                        EditorUtility
                            .DisplayCancelableProgressBar(
                                "Preparing Surface Mask Addressables",
                                $"Tile ({tileX}, {tileZ})\n\n" +
                                $"{currentTile + 1} / {expectedTileCount}",
                                expectedTileCount > 0
                                    ? (float)currentTile /
                                      expectedTileCount
                                    : 1f
                            );

                    if (cancelled)
                    {
                        break;
                    }

                    string assetPath =
                        TerrainRuntimeSurfaceMaskAssetUtility
                            .GetSurfaceTilePath(
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
                        Debug.LogError(
                            "Cannot prepare surface-mask tiles for runtime.\n\n" +
                            $"Missing Tile: ({tileX}, {tileZ})\n\n" +
                            $"Asset:\n{assetPath}"
                        );

                        return false;
                    }

                    if (
                        texture.width !=
                            manifest.samplesPerSide
                        ||
                        texture.height !=
                            manifest.samplesPerSide
                        ||
                        texture.format !=
                            TextureFormat.R8
                    )
                    {
                        Debug.LogError(
                            "Cannot prepare surface-mask tiles for runtime.\n\n" +
                            $"Tile ({tileX}, {tileZ}) has an invalid layout or format.\n\n" +
                            $"Expected: {manifest.samplesPerSide} x {manifest.samplesPerSide}, {TextureFormat.R8}\n" +
                            $"Actual: {texture.width} x {texture.height}, {texture.format}"
                        );

                        return false;
                    }

                    string guid =
                        AssetDatabase
                            .AssetPathToGUID(
                                assetPath
                            );

                    if (
                        string.IsNullOrEmpty(
                            guid
                        )
                    )
                    {
                        Debug.LogError(
                            "Could not resolve asset GUID:\n" +
                            assetPath
                        );

                        return false;
                    }

                    expectedGuids.Add(
                        guid
                    );

                    AddressableAssetEntry existingEntry =
                        settings
                            .FindAssetEntry(
                                guid
                            );

                    bool needsMove =
                        existingEntry == null
                        ||
                        existingEntry.parentGroup !=
                            group;

                    AddressableAssetEntry entry =
                        settings
                            .CreateOrMoveEntry(
                                guid,
                                group,
                                false,
                                true
                            );

                    if (entry == null)
                    {
                        Debug.LogError(
                            "Could not create Addressables entry for:\n" +
                            assetPath
                        );

                        return false;
                    }

                    if (needsMove)
                    {
                        createdOrMovedCount++;
                    }

                    string expectedAddress =
                        manifest
                            .GetSurfaceTileAddress(
                                tileX,
                                tileZ
                            );

                    if (
                        entry.address !=
                        expectedAddress
                    )
                    {
                        entry.SetAddress(
                            expectedAddress,
                            true
                        );

                        addressUpdatedCount++;
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
            AssetDatabase.SaveAssets();

            Debug.LogWarning(
                "Preparing surface-mask Addressables was cancelled.\n\n" +
                "Entries processed before cancellation were preserved."
            );

            return false;
        }

        List<AddressableAssetEntry> obsoleteEntries =
            new List<AddressableAssetEntry>();

        foreach (
            AddressableAssetEntry entry
            in group.entries
        )
        {
            if (
                !expectedGuids.Contains(
                    entry.guid
                )
            )
            {
                obsoleteEntries.Add(
                    entry
                );
            }
        }

        foreach (
            AddressableAssetEntry obsoleteEntry
            in obsoleteEntries
        )
        {
            group.RemoveAssetEntry(
                obsoleteEntry,
                true
            );
        }

        EditorUtility.SetDirty(
            settings
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "Surface-mask tiles prepared for runtime streaming.\n\n" +
            $"Addressables Group: {SurfaceMaskAddressablesGroupName}\n\n" +
            $"Tile Grid: {tileGridWidth} x {tileGridHeight}\n" +
            $"Tiles: {expectedTileCount}\n\n" +
            $"Created / Moved Entries: {createdOrMovedCount}\n" +
            $"Addresses Updated: {addressUpdatedCount}\n" +
            $"Obsolete Entries Removed: {obsoleteEntries.Count}\n\n" +
            $"Address Pattern:\n{TerrainSurfaceMaskManifest.SurfaceTileAddressPrefix}_X_Z"
        );

        return true;
    }
}
