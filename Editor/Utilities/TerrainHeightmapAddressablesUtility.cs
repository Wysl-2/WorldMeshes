using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class TerrainHeightmapAddressablesUtility
{
    public const string HeightmapAddressablesGroupName =
        "Terrain Heightmap Tiles";

    // =====================================================
    // PREPARE FROM GENERATED MANIFEST
    // =====================================================

    public static void PrepareHeightmapTilesForRuntime()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot prepare heightmap tiles for runtime.\n\n" +
                "Heightmap manifest does not exist:\n" +
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
            );

            return;
        }

        PrepareHeightmapTilesForRuntime(
            manifest
        );
    }

    // =====================================================
    // PREPARE
    // =====================================================

    public static bool PrepareHeightmapTilesForRuntime(
        TerrainHeightmapManifest manifest
    )
    {
        if (manifest == null)
        {
            Debug.LogError(
                "Cannot prepare heightmap tiles for runtime: " +
                "manifest is null."
            );

            return false;
        }

        if (!manifest.isComplete)
        {
            Debug.LogError(
                "Cannot prepare heightmap tiles for runtime.\n\n" +
                "The heightmap manifest is incomplete.\n\n" +
                "Compile the runtime heightmaps first."
            );

            return false;
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
                HeightmapAddressablesGroupName
            );

        if (group == null)
        {
            List<AddressableAssetGroupSchema>
                schemasToCopy =
                    settings.DefaultGroup != null
                        ? settings
                            .DefaultGroup
                            .Schemas
                        : null;

            group =
                settings.CreateGroup(
                    HeightmapAddressablesGroupName,
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
                HeightmapAddressablesGroupName
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
                "Could not configure the Addressables " +
                "Content Packing & Loading schema."
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
                                "Preparing Heightmap Addressables",
                                $"Tile ({tileX}, {tileZ})\n\n" +
                                $"{currentTile + 1} / " +
                                $"{expectedTileCount}",
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
                        TerrainRuntimeHeightAssetUtility
                            .GetHeightTilePath(
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
                            "Cannot prepare heightmap tiles " +
                            "for runtime.\n\n" +
                            $"Missing Tile: ({tileX}, {tileZ})\n\n" +
                            $"Asset:\n{assetPath}"
                        );

                        return false;
                    }

                    if (
                        texture.width !=
                            manifest.heightTileSamplesPerSide
                        ||
                        texture.height !=
                            manifest.heightTileSamplesPerSide
                    )
                    {
                        Debug.LogError(
                            "Cannot prepare heightmap tiles " +
                            "for runtime.\n\n" +
                            $"Tile ({tileX}, {tileZ}) has " +
                            "unexpected dimensions.\n\n" +
                            $"Expected: " +
                            $"{manifest.heightTileSamplesPerSide} x " +
                            $"{manifest.heightTileSamplesPerSide}\n" +
                            $"Actual: {texture.width} x " +
                            $"{texture.height}"
                        );

                        return false;
                    }

                    if (
                        texture.format !=
                        TextureFormat.RFloat
                    )
                    {
                        Debug.LogError(
                            "Cannot prepare heightmap tiles " +
                            "for runtime.\n\n" +
                            $"Tile ({tileX}, {tileZ}) does not " +
                            "use TextureFormat.RFloat.\n\n" +
                            $"Actual Format: {texture.format}"
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
                            "Could not create Addressables " +
                            "entry for:\n" +
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
                            .GetHeightTileAddress(
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
                "Preparing heightmap Addressables was " +
                "cancelled.\n\n" +
                "Entries processed before cancellation " +
                "were preserved."
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
            "Heightmap tiles prepared for runtime streaming.\n\n" +
            $"Addressables Group: " +
            $"{HeightmapAddressablesGroupName}\n\n" +
            $"Tile Grid: " +
            $"{tileGridWidth} x {tileGridHeight}\n" +
            $"Tiles: {expectedTileCount}\n\n" +
            $"Created / Moved Entries: " +
            $"{createdOrMovedCount}\n" +
            $"Addresses Updated: " +
            $"{addressUpdatedCount}\n" +
            $"Obsolete Entries Removed: " +
            $"{obsoleteEntries.Count}\n\n" +
            $"Address Pattern:\n" +
            $"{TerrainHeightmapManifest.HeightTileAddressPrefix}_X_Z"
        );

        return true;
    }

    // =====================================================
    // PREPARE + BUILD
    // =====================================================

    public static bool PrepareAndBuildHeightmapTilesForRuntime()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot prepare and build heightmap Addressables.\n\n" +
                "Heightmap manifest does not exist:\n" +
                TerrainRuntimeHeightAssetUtility
                    .HeightmapManifestPath
            );

            return false;
        }

        if (
            !PrepareHeightmapTilesForRuntime(
                manifest
            )
        )
        {
            return false;
        }

        return BuildAddressablesContent();
    }

    // =====================================================
    // BUILD ADDRESSABLES CONTENT
    // =====================================================

    public static bool BuildAddressablesContent()
    {
        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Addressables content cannot be rebuilt while " +
                "entering or running Play Mode."
            );

            return false;
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject
                .GetSettings(
                    false
                );

        if (settings == null)
        {
            Debug.LogError(
                "Cannot build Addressables content.\n\n" +
                "AddressableAssetSettings could not be loaded."
            );

            return false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "Building Addressables player content..."
        );

        AddressableAssetSettings
            .BuildPlayerContent(
                out AddressablesPlayerBuildResult result
            );

        if (result == null)
        {
            Debug.LogError(
                "Addressables content build failed.\n\n" +
                "No build result was returned."
            );

            return false;
        }

        if (
            !string.IsNullOrEmpty(
                result.Error
            )
        )
        {
            Debug.LogError(
                "Addressables content build failed.\n\n" +
                result.Error
            );

            return false;
        }

        Debug.Log(
            "Addressables content build complete.\n\n" +
            $"Output Path:\n" +
            $"{result.OutputPath}\n\n" +
            $"Build Duration: " +
            $"{result.Duration:0.00} seconds\n\n" +
            "Use Existing Build will now use the latest " +
            "compiled runtime heightmap assets."
        );

        return true;
    }
}
