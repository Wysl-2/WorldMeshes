using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainCollisionBakeMarkerUtility
{
    public const string BakeMarkerFolder =
        WorldMeshesPaths.GeneratedCollisionBakeMarkers;

    private const string BakeMarkerAddressPrefix =
        "WorldMeshes_CollisionBakeMarker";

    internal static bool ReconcileBakeMarkers(
        WorldSettings worldSettings,
        out List<BakeMarkerRecord> markerRecords,
        out int regeneratedCount,
        out int reusedCount,
        out int removedCount,
        out bool cancelled,
        out string errorMessage
    )
    {
        markerRecords =
            new List<BakeMarkerRecord>();

        regeneratedCount = 0;
        reusedCount = 0;
        removedCount = 0;
        cancelled = false;
        errorMessage = "";

        if (!ValidateBaseRequirements(worldSettings, out errorMessage))
        {
            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                BakeMarkerFolder
            )
        )
        {
            string folderGuid =
                AssetDatabase.CreateFolder(
                    WorldMeshesPaths.GeneratedCollisionMeshes,
                    "BakeMarkers"
                );

            if (
                string.IsNullOrEmpty(folderGuid)
                ||
                !AssetDatabase.IsValidFolder(
                    BakeMarkerFolder
                )
            )
            {
                errorMessage =
                    "Could not create collision bake marker folder:\n" +
                    BakeMarkerFolder;

                return false;
            }
        }

        GetRegionLayout(
            worldSettings,
            out int gridWidth,
            out int gridHeight,
            out int regionSpan,
            out int regionGridWidth,
            out int regionGridHeight
        );

        int expectedRegionCount =
            regionGridWidth *
            regionGridHeight;

        int currentRegion = 0;

        HashSet<string> expectedMarkerPaths =
            new HashSet<string>();

        try
        {
            for (int regionZ = 0; regionZ < regionGridHeight; regionZ++)
            {
                for (int regionX = 0; regionX < regionGridWidth; regionX++)
                {
                    cancelled =
                        EditorUtility.DisplayCancelableProgressBar(
                            "Preparing Collision Bake Markers",
                            "Region (" +
                            regionX +
                            ", " +
                            regionZ +
                            ")\n\n" +
                            (currentRegion + 1) +
                            " / " +
                            expectedRegionCount,
                            expectedRegionCount > 0
                                ? (float)currentRegion / expectedRegionCount
                                : 1f
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    string markerPath =
                        GetBakeMarkerAssetPath(
                            regionX,
                            regionZ
                        );

                    expectedMarkerPaths.Add(
                        markerPath
                    );

                    if (
                        ValidateOneMarker(
                            regionX,
                            regionZ,
                            regionSpan,
                            gridWidth,
                            gridHeight,
                            markerPath,
                            out BakeMarkerRecord existingRecord,
                            out _
                        )
                    )
                    {
                        markerRecords.Add(
                            existingRecord
                        );

                        reusedCount++;
                    }
                    else
                    {
                        if (
                            !GenerateOrUpdateOneMarker(
                                regionX,
                                regionZ,
                                regionSpan,
                                gridWidth,
                                gridHeight,
                                markerPath
                            )
                        )
                        {
                            errorMessage =
                                "Could not regenerate collision bake marker:\n" +
                                markerPath;

                            return false;
                        }

                        if (
                            !ValidateOneMarker(
                                regionX,
                                regionZ,
                                regionSpan,
                                gridWidth,
                                gridHeight,
                                markerPath,
                                out BakeMarkerRecord regeneratedRecord,
                                out string markerError
                            )
                        )
                        {
                            errorMessage =
                                "Regenerated collision bake marker is invalid:\n" +
                                markerPath +
                                "\n\n" +
                                markerError;

                            return false;
                        }

                        markerRecords.Add(
                            regeneratedRecord
                        );

                        regeneratedCount++;
                    }

                    currentRegion++;
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
            using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
            {
                AssetDatabase.SaveAssets();
            }
            return false;
        }

        /*
         * Obsolete marker discovery is structural reconciliation work only.
         * ContentOnly validation never scans/deletes this folder.
         */
        string[] existingMarkerGuids =
            AssetDatabase.FindAssets(
                "t:Prefab",
                new[]
                {
                    BakeMarkerFolder
                }
            );

        foreach (string existingMarkerGuid in existingMarkerGuids)
        {
            string existingPath =
                AssetDatabase.GUIDToAssetPath(
                    existingMarkerGuid
                );

            if (
                expectedMarkerPaths.Contains(
                    existingPath
                )
            )
            {
                continue;
            }

            if (
                !AssetDatabase.DeleteAsset(
                    existingPath
                )
            )
            {
                errorMessage =
                    "Could not remove obsolete collision bake marker:\n" +
                    existingPath;

                return false;
            }

            removedCount++;
        }

        using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
        {
            AssetDatabase.SaveAssets();
        }

        return true;
    }

    // =====================================================
    // READ-ONLY VALIDATION
    // =====================================================

    public static bool ValidateExistingBakeMarkers(
        WorldSettings worldSettings,
        out List<BakeMarkerRecord> markerRecords,
        out string errorMessage
    )
    {
        markerRecords =
            new List<BakeMarkerRecord>();

        errorMessage = "";

        if (!ValidateBaseRequirements(worldSettings, out errorMessage))
        {
            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                BakeMarkerFolder
            )
        )
        {
            errorMessage =
                "Collision bake marker folder is missing:\n" +
                BakeMarkerFolder;

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

        for (int regionZ = 0; regionZ < regionGridHeight; regionZ++)
        {
            for (int regionX = 0; regionX < regionGridWidth; regionX++)
            {
                string markerPath =
                    GetBakeMarkerAssetPath(
                        regionX,
                        regionZ
                    );

                if (
                    !ValidateOneMarker(
                        regionX,
                        regionZ,
                        regionSpan,
                        gridWidth,
                        gridHeight,
                        markerPath,
                        out BakeMarkerRecord record,
                        out string markerError
                    )
                )
                {
                    errorMessage =
                        "Collision bake marker structure needs repair.\n\n" +
                        markerError;

                    return false;
                }

                markerRecords.Add(
                    record
                );
            }
        }

        return true;
    }

    private static bool ValidateOneMarker(
        int regionX,
        int regionZ,
        int regionSpan,
        int gridWidth,
        int gridHeight,
        string markerPath,
        out BakeMarkerRecord record,
        out string errorMessage
    )
    {
        record = default;
        errorMessage = "";

        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                markerPath
            );

        if (prefab == null)
        {
            errorMessage =
                "Missing marker prefab:\n" +
                markerPath;

            return false;
        }

        string markerGuid =
            AssetDatabase.AssetPathToGUID(
                markerPath
            );

        if (string.IsNullOrEmpty(markerGuid))
        {
            errorMessage =
                "Could not resolve marker GUID:\n" +
                markerPath;

            return false;
        }

        string expectedName =
            GetBakeMarkerName(
                regionX,
                regionZ
            );

        if (prefab.name != expectedName)
        {
            errorMessage =
                "Marker root has unexpected name:\n" +
                markerPath;

            return false;
        }

        int startChunkX =
            regionX *
            regionSpan;

        int startChunkZ =
            regionZ *
            regionSpan;

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

        int expectedColliderCount =
            (endChunkX - startChunkX)
            *
            (endChunkZ - startChunkZ);

        MeshCollider[] allColliders =
            prefab.GetComponentsInChildren<MeshCollider>(
                true
            );

        if (
            allColliders.Length !=
            expectedColliderCount
        )
        {
            errorMessage =
                "Marker has unexpected MeshCollider count:\n" +
                markerPath +
                "\nExpected: " +
                expectedColliderCount +
                "\nActual: " +
                allColliders.Length;

            return false;
        }

        if (
            prefab.transform.childCount !=
            expectedColliderCount
        )
        {
            errorMessage =
                "Marker has unexpected child count:\n" +
                markerPath;

            return false;
        }

        for (int chunkZ = startChunkZ; chunkZ < endChunkZ; chunkZ++)
        {
            for (int chunkX = startChunkX; chunkX < endChunkX; chunkX++)
            {
                string childName =
                    "Chunk_" +
                    chunkX +
                    "_" +
                    chunkZ;

                Transform child =
                    prefab.transform.Find(
                        childName
                    );

                if (
                    child == null
                    ||
                    child.parent != prefab.transform
                )
                {
                    errorMessage =
                        "Marker is missing expected chunk child " +
                        childName +
                        ":\n" +
                        markerPath;

                    return false;
                }

                MeshCollider[] childColliders =
                    child.GetComponents<MeshCollider>();

                if (childColliders.Length != 1)
                {
                    errorMessage =
                        "Marker chunk child must contain exactly one MeshCollider:\n" +
                        markerPath +
                        "\nChild: " +
                        childName;

                    return false;
                }

                MeshCollider meshCollider =
                    childColliders[0];

                if (
                    !meshCollider.enabled
                    ||
                    meshCollider.isTrigger
                    ||
                    meshCollider.convex
                        != TerrainCollisionPhysicsSettings.Convex
                    ||
                    meshCollider.cookingOptions
                        != TerrainCollisionPhysicsSettings.CookingOptions
                )
                {
                    errorMessage =
                        "Marker MeshCollider configuration is incorrect:\n" +
                        markerPath +
                        "\nChild: " +
                        childName;

                    return false;
                }

                Mesh referencedMesh =
                    meshCollider.sharedMesh;

                if (referencedMesh == null)
                {
                    errorMessage =
                        "Marker MeshCollider has no sharedMesh:\n" +
                        markerPath +
                        "\nChild: " +
                        childName;

                    return false;
                }

                string expectedMeshPath =
                    TerrainCollisionMeshGenerator.GetCollisionMeshPath(
                        chunkX,
                        chunkZ
                    );

                string expectedMeshGuid =
                    AssetDatabase.AssetPathToGUID(
                        expectedMeshPath
                    );

                string referencedMeshPath =
                    AssetDatabase.GetAssetPath(
                        referencedMesh
                    );

                string referencedMeshGuid =
                    AssetDatabase.AssetPathToGUID(
                        referencedMeshPath
                    );

                if (
                    string.IsNullOrEmpty(expectedMeshGuid)
                    ||
                    string.IsNullOrEmpty(referencedMeshGuid)
                    ||
                    expectedMeshGuid != referencedMeshGuid
                )
                {
                    errorMessage =
                        "Marker MeshCollider references the wrong collision Mesh GUID:\n" +
                        markerPath +
                        "\nChild: " +
                        childName;

                    return false;
                }
            }
        }

        record =
            new BakeMarkerRecord(
                regionX,
                regionZ,
                markerPath,
                markerGuid,
                GetBakeMarkerAddress(
                    regionX,
                    regionZ
                ),
                GetRegionLabel(
                    regionX,
                    regionZ
                )
            );

        return true;
    }

    // =====================================================
    // GENERATE ONE REGION MARKER
    // =====================================================

    private static bool GenerateOrUpdateOneMarker(
        int regionX,
        int regionZ,
        int regionSpan,
        int gridWidth,
        int gridHeight,
        string markerPath
    )
    {
        GameObject root = null;

        try
        {
            root =
                new GameObject(
                    GetBakeMarkerName(
                        regionX,
                        regionZ
                    )
                );

            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            int startChunkX =
                regionX *
                regionSpan;

            int startChunkZ =
                regionZ *
                regionSpan;

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

            int colliderCount = 0;

            for (int chunkZ = startChunkZ; chunkZ < endChunkZ; chunkZ++)
            {
                for (int chunkX = startChunkX; chunkX < endChunkX; chunkX++)
                {
                    string meshPath =
                        TerrainCollisionMeshGenerator.GetCollisionMeshPath(
                            chunkX,
                            chunkZ
                        );

                    Mesh mesh =
                        AssetDatabase.LoadAssetAtPath<Mesh>(
                            meshPath
                        );

                    if (mesh == null)
                    {
                        Debug.LogError(
                            "Cannot create collision bake marker.\n\n" +
                            "Region: (" +
                            regionX +
                            ", " +
                            regionZ +
                            ")\n" +
                            "Chunk: (" +
                            chunkX +
                            ", " +
                            chunkZ +
                            ")\n\n" +
                            "Collision Mesh could not be loaded:\n" +
                            meshPath
                        );

                        return false;
                    }

                    GameObject child =
                        new GameObject(
                            "Chunk_" +
                            chunkX +
                            "_" +
                            chunkZ
                        );

                    child.transform.SetParent(
                        root.transform,
                        false
                    );

                    child.transform.localPosition = Vector3.zero;
                    child.transform.localRotation = Quaternion.identity;
                    child.transform.localScale = Vector3.one;

                    MeshCollider meshCollider =
                        child.AddComponent<MeshCollider>();

                    meshCollider.convex =
                        TerrainCollisionPhysicsSettings.Convex;

                    meshCollider.isTrigger = false;

                    meshCollider.cookingOptions =
                        TerrainCollisionPhysicsSettings.CookingOptions;

                    /*
                     * Assign the Mesh last. This serialized GUID reference is
                     * structural and remains valid when the Mesh asset is later
                     * updated in place.
                     */
                    meshCollider.sharedMesh = mesh;
                    meshCollider.enabled = true;

                    colliderCount++;
                }
            }

            if (colliderCount <= 0)
            {
                Debug.LogError(
                    "Collision bake marker contains no MeshColliders."
                );

                return false;
            }

            GameObject savedPrefab;
            bool savedSuccessfully;

            using (WorldMeshesProfiler.PrefabSaveAsPrefabAsset.Auto())
            {
                savedPrefab =
                    PrefabUtility.SaveAsPrefabAsset(
                        root,
                        markerPath,
                        out savedSuccessfully
                    );
            }

            if (
                !savedSuccessfully
                ||
                savedPrefab == null
            )
            {
                Debug.LogError(
                    "Could not save collision bake marker prefab:\n" +
                    markerPath
                );

                return false;
            }
        }
        finally
        {
            if (root != null)
            {
                Object.DestroyImmediate(
                    root
                );
            }
        }

        return true;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static bool ValidateBaseRequirements(
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Collision bake marker operations must run outside Play Mode.";

            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.GeneratedCollisionMeshes
            )
        )
        {
            errorMessage =
                "Generated collision mesh folder does not exist:\n" +
                WorldMeshesPaths.GeneratedCollisionMeshes;

            return false;
        }

        return true;
    }

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
                TerrainCollisionAddressablesUtility.CollisionRegionChunkSpan
            );

        regionGridWidth =
            (gridWidth + regionSpan - 1)
            /
            regionSpan;

        regionGridHeight =
            (gridHeight + regionSpan - 1)
            /
            regionSpan;
    }

    private static string GetBakeMarkerName(
        int regionX,
        int regionZ
    )
    {
        return
            "CollisionBakeMarker_" +
            regionX +
            "_" +
            regionZ;
    }

    public static string GetBakeMarkerAssetPath(
        int regionX,
        int regionZ
    )
    {
        return
            BakeMarkerFolder +
            "/" +
            GetBakeMarkerName(
                regionX,
                regionZ
            ) +
            ".prefab";
    }

    private static string GetBakeMarkerAddress(
        int regionX,
        int regionZ
    )
    {
        return
            BakeMarkerAddressPrefix +
            "_" +
            regionX +
            "_" +
            regionZ;
    }

    private static string GetRegionLabel(
        int regionX,
        int regionZ
    )
    {
        return
            TerrainCollisionManifest.CollisionRegionLabelPrefix +
            "_" +
            regionX +
            "_" +
            regionZ;
    }

    public readonly struct BakeMarkerRecord
    {
        public readonly int regionX;
        public readonly int regionZ;
        public readonly string assetPath;
        public readonly string guid;
        public readonly string address;
        public readonly string regionLabel;

        public BakeMarkerRecord(
            int regionX,
            int regionZ,
            string assetPath,
            string guid,
            string address,
            string regionLabel
        )
        {
            this.regionX = regionX;
            this.regionZ = regionZ;
            this.assetPath = assetPath;
            this.guid = guid;
            this.address = address;
            this.regionLabel = regionLabel;
        }
    }
}
