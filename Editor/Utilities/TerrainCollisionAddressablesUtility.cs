using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class TerrainCollisionAddressablesUtility
{
    // =====================================================
    // SETTINGS
    // =====================================================

    public const string CollisionAddressablesGroupName =
        "Terrain Collision Meshes";

    public const int CollisionRegionChunkSpan =
        8;

    private const string CollisionManifestPath =
        WorldMeshesPaths.CollisionManifestAssetPath;

    // =====================================================
    // PREPARE FROM DEFAULT WORLD SETTINGS
    // =====================================================

    public static void PrepareCollisionMeshesForRuntime()
    {
        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot prepare collision meshes for runtime.\n\n" +

                "WorldSettings could not be found:\n" +
                WorldMeshesPaths.WorldSettingsAssetPath
            );

            return;
        }

        PrepareCollisionMeshesForRuntime(
            worldSettings
        );
    }

    // =====================================================
    // PREPARE
    // =====================================================

    public static bool PrepareCollisionMeshesForRuntime(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Editor state
        // -------------------------------------------------

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Collision Addressables preparation must be " +
                "performed outside Play Mode."
            );

            return false;
        }

        // -------------------------------------------------
        // World settings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot prepare collision meshes for runtime: " +
                "WorldSettings is null."
            );

            return false;
        }

        // -------------------------------------------------
        // Generation state
        // -------------------------------------------------

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        if (
            collisionStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot prepare collision meshes for runtime.\n\n" +

                "The generated collision meshes are not current.\n\n" +

                $"Collision State: " +
                $"{TerrainGenerationStateUtility.GetStatusLabel(collisionStatus)}\n\n" +

                "Generate or regenerate the collision meshes first."
            );

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

        // =====================================================
        // PREFLIGHT ALL GENERATED COLLISION ASSETS
        // =====================================================

        List<CollisionAssetRecord> records =
            new List<CollisionAssetRecord>(
                expectedMeshCount
            );

        HashSet<string> expectedGuids =
            new HashSet<string>();

        HashSet<string> expectedRegionLabels =
            new HashSet<string>();

        bool cancelled =
            false;

        int currentMesh =
            0;

        try
        {
            for (
                int chunkZ = 0;
                chunkZ < gridHeight;
                chunkZ++
            )
            {
                for (
                    int chunkX = 0;
                    chunkX < gridWidth;
                    chunkX++
                )
                {
                    cancelled =
                        EditorUtility
                            .DisplayCancelableProgressBar(
                                "Preparing Collision Addressables",

                                "Validating generated collision meshes\n\n" +
                                $"Chunk ({chunkX}, {chunkZ})\n" +
                                $"{currentMesh + 1} / {expectedMeshCount}",

                                expectedMeshCount > 0
                                    ? (float)currentMesh /
                                      expectedMeshCount
                                    : 1f
                            );

                    if (cancelled)
                    {
                        break;
                    }

                    string assetPath =
                        TerrainCollisionMeshGenerator
                            .GetCollisionMeshPath(
                                chunkX,
                                chunkZ
                            );

                    Mesh mesh =
                        AssetDatabase
                            .LoadAssetAtPath<Mesh>(
                                assetPath
                            );

                    if (mesh == null)
                    {
                        Debug.LogError(
                            "Cannot prepare collision meshes " +
                            "for runtime.\n\n" +

                            $"Missing collision mesh: " +
                            $"({chunkX}, {chunkZ})\n\n" +

                            $"Asset:\n{assetPath}"
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
                            "Could not resolve collision mesh GUID:\n" +
                            assetPath
                        );

                        return false;
                    }

                    if (
                        !expectedGuids.Add(
                            guid
                        )
                    )
                    {
                        Debug.LogError(
                            "Multiple collision coordinates resolved " +
                            "to the same asset GUID.\n\n" +

                            $"Chunk: ({chunkX}, {chunkZ})\n" +
                            $"GUID: {guid}"
                        );

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
            EditorUtility.ClearProgressBar();
        }

        if (cancelled)
        {
            Debug.LogWarning(
                "Preparing collision meshes for runtime was " +
                "cancelled during preflight.\n\n" +

                "No Addressables or manifest changes were made."
            );

            return false;
        }

        if (
            records.Count !=
            expectedMeshCount
        )
        {
            Debug.LogError(
                "Collision Addressables preflight produced an " +
                "unexpected asset count.\n\n" +

                $"Expected: {expectedMeshCount:N0}\n" +
                $"Found: {records.Count:N0}"
            );

            return false;
        }
        
        // =====================================================
        // COLLISION BAKE MARKER PREFABS
        // =====================================================

        if (
            !TerrainCollisionBakeMarkerUtility
                .GenerateOrUpdateBakeMarkers(
                    worldSettings,

                    out List<
                        TerrainCollisionBakeMarkerUtility
                        .BakeMarkerRecord
                    > bakeMarkerRecords
                )
        )
        {
            Debug.LogError(
                "Could not generate collision bake " +
                "marker prefabs."
            );

            return false;
        }

        /*
         * Marker prefabs are also managed entries in the same
         * Addressables group.
         *
         * Their MeshCollider -> Mesh references make the intended
         * collision usage visible to Unity's build pipeline.
         */
        foreach (
            TerrainCollisionBakeMarkerUtility
                .BakeMarkerRecord markerRecord
            in bakeMarkerRecords
        )
        {
            if (
                !expectedGuids.Add(
                    markerRecord.guid
                )
            )
            {
                Debug.LogError(
                    "Collision bake marker GUID collides with " +
                    "another managed Addressables asset.\n\n" +

                    $"Marker:\n{markerRecord.assetPath}\n\n" +

                    $"GUID: {markerRecord.guid}"
                );

                return false;
            }

            expectedRegionLabels.Add(
                markerRecord.regionLabel
            );
        }

        // =====================================================
        // MANIFEST — MARK INCOMPLETE BEFORE MUTATION
        // =====================================================

        TerrainCollisionManifest manifest =
            GetOrCreateManifest();

        if (manifest == null)
        {
            return false;
        }

        manifest.isComplete =
            false;

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        // =====================================================
        // ADDRESSABLE SETTINGS
        // =====================================================

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

        // =====================================================
        // GROUP
        // =====================================================

        AddressableAssetGroup group =
            settings.FindGroup(
                CollisionAddressablesGroupName
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
                    CollisionAddressablesGroupName,

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
                CollisionAddressablesGroupName
            );

            return false;
        }

        // =====================================================
        // BUNDLED-ASSET SCHEMA
        // =====================================================

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
                "Could not configure the collision Addressables " +
                "Content Packing & Loading schema."
            );

            return false;
        }

        /*
         * Every collision mesh has exactly one tool-owned region
         * label. PackTogetherByLabel therefore creates one bundle
         * for each unique collision region instead of one bundle
         * per mesh.
         */
        bundledSchema.BundleMode =
            BundledAssetGroupSchema
                .BundlePackingMode
                .PackTogetherByLabel;

        bundledSchema.IncludeAddressInCatalog =
            true;

        EditorUtility.SetDirty(
            bundledSchema
        );

        // =====================================================
        // ENSURE REGION LABELS EXIST
        // =====================================================

        HashSet<string> registeredLabels =
            new HashSet<string>(
                settings.GetLabels()
            );

        foreach (
            string regionLabel
            in expectedRegionLabels
        )
        {
            if (
                registeredLabels.Contains(
                    regionLabel
                )
            )
            {
                continue;
            }

            settings.AddLabel(
                regionLabel,
                true
            );

            registeredLabels.Add(
                regionLabel
            );
        }

        // =====================================================
        // CREATE / UPDATE ENTRIES
        // =====================================================

        int createdOrMovedCount =
            0;

        int addressUpdatedCount =
            0;

        int labelUpdatedCount =
            0;

        currentMesh =
            0;

        cancelled =
            false;

        try
        {
            foreach (
                CollisionAssetRecord record
                in records
            )
            {
                cancelled =
                    EditorUtility
                        .DisplayCancelableProgressBar(
                            "Preparing Collision Addressables",

                            "Registering collision meshes\n\n" +
                            $"Chunk ({record.chunkX}, {record.chunkZ})\n" +
                            $"{currentMesh + 1} / {expectedMeshCount}",

                            expectedMeshCount > 0
                                ? (float)currentMesh /
                                  expectedMeshCount
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

                bool needsMove =
                    existingEntry == null
                    ||
                    existingEntry.parentGroup !=
                        group;

                AddressableAssetEntry entry =
                    settings.CreateOrMoveEntry(
                        record.guid,
                        group,
                        false,
                        true
                    );

                if (entry == null)
                {
                    Debug.LogError(
                        "Could not create Addressables entry for:\n" +
                        record.assetPath
                    );

                    return false;
                }

                if (needsMove)
                {
                    createdOrMovedCount++;
                }

                // -----------------------------------------
                // Deterministic address
                // -----------------------------------------

                if (
                    entry.address !=
                    record.address
                )
                {
                    entry.SetAddress(
                        record.address,
                        true
                    );

                    addressUpdatedCount++;
                }

                // -----------------------------------------
                // Exactly one region label per entry
                // -----------------------------------------

                bool labelsAlreadyCorrect =
                    entry.labels.Count == 1
                    &&
                    entry.labels.Contains(
                        record.regionLabel
                    );

                if (!labelsAlreadyCorrect)
                {
                    List<string> existingLabels =
                        new List<string>(
                            entry.labels
                        );

                    foreach (
                        string existingLabel
                        in existingLabels
                    )
                    {
                        entry.SetLabel(
                            existingLabel,
                            false,
                            false,
                            true
                        );
                    }

                    entry.SetLabel(
                        record.regionLabel,
                        true,
                        false,
                        true
                    );

                    labelUpdatedCount++;
                }

                currentMesh++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (cancelled)
        {
            EditorUtility.SetDirty(
                settings
            );

            AssetDatabase.SaveAssets();

            Debug.LogWarning(
                "Preparing collision meshes for runtime was " +
                "cancelled.\n\n" +

                "Addressables entries processed before " +
                "cancellation were preserved.\n\n" +

                "CollisionManifest remains incomplete. Run " +
                "Prepare Collision Meshes For Runtime again " +
                "to finish."
            );

            return false;
        }
        
        // =====================================================
        // CREATE / UPDATE BAKE MARKER ENTRIES
        // =====================================================

        int currentMarker =
            0;

        cancelled =
            false;

        try
        {
            foreach (
                TerrainCollisionBakeMarkerUtility
                    .BakeMarkerRecord markerRecord
                in bakeMarkerRecords
            )
            {
                cancelled =
                    EditorUtility
                        .DisplayCancelableProgressBar(
                            "Preparing Collision Addressables",

                            "Registering collision bake markers\n\n" +

                            $"Region " +
                            $"({markerRecord.regionX}, " +
                            $"{markerRecord.regionZ})\n" +

                            $"{currentMarker + 1} / " +
                            $"{bakeMarkerRecords.Count}",

                            bakeMarkerRecords.Count > 0
                                ?
                                (float)currentMarker /
                                bakeMarkerRecords.Count
                                :
                                1f
                        );

                if (cancelled)
                {
                    break;
                }

                // -----------------------------------------
                // Entry
                // -----------------------------------------

                AddressableAssetEntry existingEntry =
                    settings.FindAssetEntry(
                        markerRecord.guid
                    );

                bool needsMove =
                    existingEntry == null
                    ||
                    existingEntry.parentGroup !=
                        group;

                AddressableAssetEntry entry =
                    settings.CreateOrMoveEntry(
                        markerRecord.guid,
                        group,
                        false,
                        true
                    );

                if (entry == null)
                {
                    Debug.LogError(
                        "Could not create Addressables entry " +
                        "for collision bake marker:\n" +
                        markerRecord.assetPath
                    );

                    return false;
                }

                if (needsMove)
                {
                    createdOrMovedCount++;
                }

                // -----------------------------------------
                // Deterministic address
                // -----------------------------------------

                if (
                    entry.address !=
                    markerRecord.address
                )
                {
                    entry.SetAddress(
                        markerRecord.address,
                        true
                    );

                    addressUpdatedCount++;
                }

                // -----------------------------------------
                // Exactly one region label
                // -----------------------------------------

                bool labelsAlreadyCorrect =
                    entry.labels.Count == 1
                    &&
                    entry.labels.Contains(
                        markerRecord.regionLabel
                    );

                if (!labelsAlreadyCorrect)
                {
                    List<string> existingLabels =
                        new List<string>(
                            entry.labels
                        );

                    foreach (
                        string existingLabel
                        in existingLabels
                    )
                    {
                        entry.SetLabel(
                            existingLabel,
                            false,
                            false,
                            true
                        );
                    }

                    entry.SetLabel(
                        markerRecord.regionLabel,
                        true,
                        false,
                        true
                    );

                    labelUpdatedCount++;
                }

                currentMarker++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (cancelled)
        {
            EditorUtility.SetDirty(
                settings
            );

            AssetDatabase.SaveAssets();

            Debug.LogWarning(
                "Preparing collision meshes for runtime was " +
                "cancelled while registering collision bake " +
                "markers.\n\n" +

                "CollisionManifest remains incomplete."
            );

            return false;
        }

        // =====================================================
        // REMOVE OBSOLETE ENTRIES FROM MANAGED GROUP
        // =====================================================

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

        // =====================================================
        // REMOVE STALE TOOL-OWNED REGION LABELS
        // =====================================================

        List<string> staleRegionLabels =
            new List<string>();

        foreach (
            string label
            in settings.GetLabels()
        )
        {
            if (
                label.StartsWith(
                    TerrainCollisionManifest
                        .CollisionRegionLabelPrefix
                    +
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

        foreach (
            string staleRegionLabel
            in staleRegionLabels
        )
        {
            settings.RemoveLabel(
                staleRegionLabel,
                true
            );
        }

        // =====================================================
        // COMPLETE MANIFEST
        // =====================================================

        manifest.manifestVersion =
            1;

        manifest.collisionGeneratorVersion =
            TerrainGenerationStateUtility
                .CollisionGeneratorVersion;

        manifest.gridWidth =
            gridWidth;

        manifest.gridHeight =
            gridHeight;

        manifest.chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        manifest.heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        manifest.collisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        manifest.collisionMeshGenerationRevision =
            worldSettings
                .collisionMeshGenerationRevision;

        manifest.collisionSourceHeightmapGenerationRevision =
            worldSettings
                .collisionSourceHeightmapGenerationRevision;

        manifest.regionChunkSpan =
            CollisionRegionChunkSpan;

        manifest.isComplete =
            true;

        // =====================================================
        // SAVE
        // =====================================================

        EditorUtility.SetDirty(
            manifest
        );

        EditorUtility.SetDirty(
            settings
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // =====================================================
        // COMPLETE
        // =====================================================

        Debug.Log(
            "Collision meshes prepared for runtime streaming.\n\n" +

            $"Addressables Group: " +
            $"{CollisionAddressablesGroupName}\n\n" +

            $"World Grid: " +
            $"{gridWidth} x {gridHeight}\n" +

            $"Collision Meshes: " +
            $"{expectedMeshCount:N0}\n\n" +
            
            $"Collision Bake Marker Prefabs: " +
            $"{bakeMarkerRecords.Count:N0}\n\n" +

            $"Region Chunk Span: " +
            $"{CollisionRegionChunkSpan} x " +
            $"{CollisionRegionChunkSpan}\n" +

            $"Region Grid: " +
            $"{manifest.RegionGridWidth} x " +
            $"{manifest.RegionGridHeight}\n" +

            $"Regions: " +
            $"{manifest.RegionCount:N0}\n\n" +

            $"Created / Moved Entries: " +
            $"{createdOrMovedCount:N0}\n" +

            $"Addresses Updated: " +
            $"{addressUpdatedCount:N0}\n" +

            $"Region Labels Updated: " +
            $"{labelUpdatedCount:N0}\n" +

            $"Obsolete Entries Removed: " +
            $"{obsoleteEntries.Count:N0}\n" +

            $"Stale Region Labels Removed: " +
            $"{staleRegionLabels.Count:N0}\n\n" +

            $"Address Pattern:\n" +
            $"{TerrainCollisionManifest.CollisionMeshAddressPrefix}_X_Z\n\n" +

            $"Manifest:\n" +
            $"{CollisionManifestPath}"
        );

        return true;
    }

    // =====================================================
    // GET / CREATE MANIFEST
    // =====================================================

    private static TerrainCollisionManifest
        GetOrCreateManifest()
    {
        TerrainCollisionManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainCollisionManifest>(
                    CollisionManifestPath
                );

        if (manifest != null)
        {
            return manifest;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .GeneratedCollisionMeshes
            )
        )
        {
            Debug.LogError(
                "Cannot create collision runtime manifest.\n\n" +

                "Generated collision mesh folder does not exist:\n" +
                WorldMeshesPaths.GeneratedCollisionMeshes
            );

            return null;
        }

        manifest =
            ScriptableObject
                .CreateInstance<TerrainCollisionManifest>();

        manifest.name =
            "CollisionManifest";

        manifest.isComplete =
            false;

        AssetDatabase.CreateAsset(
            manifest,
            CollisionManifestPath
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return manifest;
    }

    // =====================================================
    // ADDRESS
    // =====================================================

    private static string GetCollisionMeshAddress(
        int chunkX,
        int chunkZ
    )
    {
        return
            $"{TerrainCollisionManifest.CollisionMeshAddressPrefix}_" +
            $"{chunkX}_{chunkZ}";
    }

    // =====================================================
    // REGION LABEL
    // =====================================================

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
            $"{TerrainCollisionManifest.CollisionRegionLabelPrefix}_" +
            $"{regionX}_{regionZ}";
    }

    // =====================================================
    // ASSET RECORD
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
            this.chunkX =
                chunkX;

            this.chunkZ =
                chunkZ;

            this.assetPath =
                assetPath;

            this.guid =
                guid;

            this.address =
                address;

            this.regionLabel =
                regionLabel;
        }
    }
}
