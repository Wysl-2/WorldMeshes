using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainCollisionBakeMarkerUtility
{
    // =====================================================
    // SETTINGS
    // =====================================================

    private const string BakeMarkerFolder =
        WorldMeshesPaths.GeneratedCollisionBakeMarkers;

    private const string BakeMarkerAddressPrefix =
        "WorldMeshes_CollisionBakeMarker";

    // =====================================================
    // GENERATE / UPDATE
    // =====================================================

    public static bool GenerateOrUpdateBakeMarkers(
        WorldSettings worldSettings,
        out List<BakeMarkerRecord> markerRecords
    )
    {
        markerRecords =
            new List<BakeMarkerRecord>();

        // -------------------------------------------------
        // Validate
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot generate collision bake markers: " +
                "WorldSettings is null."
            );

            return false;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Collision bake markers must be generated " +
                "outside Play Mode."
            );

            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .GeneratedCollisionMeshes
            )
        )
        {
            Debug.LogError(
                "Cannot generate collision bake markers.\n\n" +

                "The generated collision mesh folder " +
                "does not exist:\n" +

                WorldMeshesPaths
                    .GeneratedCollisionMeshes
            );

            return false;
        }

        // -------------------------------------------------
        // Ensure marker folder
        // -------------------------------------------------

        if (
            !AssetDatabase.IsValidFolder(
                BakeMarkerFolder
            )
        )
        {
            string folderGuid =
                AssetDatabase.CreateFolder(
                    WorldMeshesPaths
                        .GeneratedCollisionMeshes,

                    "BakeMarkers"
                );

            if (
                string.IsNullOrEmpty(
                    folderGuid
                )
                ||
                !AssetDatabase.IsValidFolder(
                    BakeMarkerFolder
                )
            )
            {
                Debug.LogError(
                    "Could not create collision bake " +
                    "marker folder:\n" +
                    BakeMarkerFolder
                );

                return false;
            }
        }

        // -------------------------------------------------
        // Layout
        // -------------------------------------------------

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

        int regionSpan =
            Mathf.Max(
                1,
                TerrainCollisionAddressablesUtility
                    .CollisionRegionChunkSpan
            );

        int regionGridWidth =
            (
                gridWidth +
                regionSpan -
                1
            )
            /
            regionSpan;

        int regionGridHeight =
            (
                gridHeight +
                regionSpan -
                1
            )
            /
            regionSpan;

        HashSet<string> expectedMarkerPaths =
            new HashSet<string>();

        // =====================================================
        // BUILD ONE PREFAB PER COLLISION REGION
        // =====================================================

        for (
            int regionZ = 0;
            regionZ < regionGridHeight;
            regionZ++
        )
        {
            for (
                int regionX = 0;
                regionX < regionGridWidth;
                regionX++
            )
            {
                string markerPath =
                    GetBakeMarkerAssetPath(
                        regionX,
                        regionZ
                    );

                expectedMarkerPaths.Add(
                    markerPath
                );

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
                    return false;
                }

                string markerGuid =
                    AssetDatabase
                        .AssetPathToGUID(
                            markerPath
                        );

                if (
                    string.IsNullOrEmpty(
                        markerGuid
                    )
                )
                {
                    Debug.LogError(
                        "Could not obtain GUID for collision " +
                        "bake marker:\n" +
                        markerPath
                    );

                    return false;
                }

                markerRecords.Add(
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
                    )
                );
            }
        }

        // =====================================================
        // DELETE OBSOLETE MARKERS
        // =====================================================

        string[] existingMarkerGuids =
            AssetDatabase.FindAssets(
                "t:Prefab",
                new[]
                {
                    BakeMarkerFolder
                }
            );

        foreach (
            string existingMarkerGuid
            in existingMarkerGuids
        )
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
                Debug.LogWarning(
                    "Could not remove obsolete collision " +
                    "bake marker:\n" +
                    existingPath
                );
            }
        }

        AssetDatabase.SaveAssets();

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
        GameObject root =
            null;

        try
        {
            root =
                new GameObject(
                    GetBakeMarkerName(
                        regionX,
                        regionZ
                    )
                );

            root.transform.position =
                Vector3.zero;

            root.transform.rotation =
                Quaternion.identity;

            root.transform.localScale =
                Vector3.one;

            int startChunkX =
                regionX *
                regionSpan;

            int startChunkZ =
                regionZ *
                regionSpan;

            int endChunkX =
                Mathf.Min(
                    startChunkX +
                    regionSpan,

                    gridWidth
                );

            int endChunkZ =
                Mathf.Min(
                    startChunkZ +
                    regionSpan,

                    gridHeight
                );

            int colliderCount =
                0;

            // =================================================
            // ONE CHILD MESHCOLLIDER PER COLLISION MESH
            // =================================================

            for (
                int chunkZ = startChunkZ;
                chunkZ < endChunkZ;
                chunkZ++
            )
            {
                for (
                    int chunkX = startChunkX;
                    chunkX < endChunkX;
                    chunkX++
                )
                {
                    string meshPath =
                        TerrainCollisionMeshGenerator
                            .GetCollisionMeshPath(
                                chunkX,
                                chunkZ
                            );

                    Mesh mesh =
                        AssetDatabase
                            .LoadAssetAtPath<Mesh>(
                                meshPath
                            );

                    if (mesh == null)
                    {
                        Debug.LogError(
                            "Cannot create collision bake " +
                            "marker.\n\n" +

                            $"Region: " +
                            $"({regionX}, {regionZ})\n" +

                            $"Chunk: " +
                            $"({chunkX}, {chunkZ})\n\n" +

                            "Collision Mesh could not be " +
                            "loaded:\n" +
                            meshPath
                        );

                        return false;
                    }

                    GameObject child =
                        new GameObject(
                            $"Chunk_{chunkX}_{chunkZ}"
                        );

                    child.transform.SetParent(
                        root.transform,
                        false
                    );

                    child.transform.localPosition =
                        Vector3.zero;

                    child.transform.localRotation =
                        Quaternion.identity;

                    child.transform.localScale =
                        Vector3.one;

                    MeshCollider meshCollider =
                        child.AddComponent<MeshCollider>();

                    /*
                     * IMPORTANT:
                     *
                     * These settings must exactly match both:
                     *
                     * - Physics.BakeMesh()
                     * - runtime MeshCollider configuration
                     */
                    meshCollider.convex =
                        TerrainCollisionPhysicsSettings
                            .Convex;

                    meshCollider.isTrigger =
                        false;

                    meshCollider.cookingOptions =
                        TerrainCollisionPhysicsSettings
                            .CookingOptions;

                    /*
                     * Assign sharedMesh LAST, after all cooking
                     * configuration is already correct.
                     *
                     * This serialized MeshCollider -> Mesh
                     * relationship is the entire purpose of the
                     * marker prefab.
                     */
                    meshCollider.sharedMesh =
                        mesh;

                    /*
                     * Leave the component enabled in the prefab.
                     *
                     * The prefab is never instantiated at runtime,
                     * so this has no runtime physics cost. Leaving
                     * the MeshCollider in its normal enabled state
                     * gives Unity's build-time collision prebaking
                     * pipeline the clearest possible representation
                     * of its intended use.
                     */
                    meshCollider.enabled =
                        true;

                    colliderCount++;
                }
            }

            if (colliderCount <= 0)
            {
                Debug.LogError(
                    "Collision bake marker contains no " +
                    "MeshColliders.\n\n" +

                    $"Region: " +
                    $"({regionX}, {regionZ})"
                );

                return false;
            }

            // =================================================
            // SAVE PREFAB
            // =================================================

            GameObject savedPrefab =
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    markerPath,
                    out bool savedSuccessfully
                );

            if (
                !savedSuccessfully
                ||
                savedPrefab == null
            )
            {
                Debug.LogError(
                    "Could not save collision bake " +
                    "marker prefab:\n" +
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

        // =====================================================
        // VERIFY SERIALIZED PREFAB
        // =====================================================

        GameObject loadedPrefab =
            AssetDatabase
                .LoadAssetAtPath<GameObject>(
                    markerPath
                );

        if (loadedPrefab == null)
        {
            Debug.LogError(
                "Collision bake marker was saved but " +
                "could not be reloaded:\n" +
                markerPath
            );

            return false;
        }

        MeshCollider[] colliders =
            loadedPrefab
                .GetComponentsInChildren<MeshCollider>(
                    true
                );

        if (colliders.Length == 0)
        {
            Debug.LogError(
                "Collision bake marker contains no " +
                "serialized MeshColliders:\n" +
                markerPath
            );

            return false;
        }

        foreach (
            MeshCollider meshCollider
            in colliders
        )
        {
            if (
                meshCollider.sharedMesh ==
                    null
            )
            {
                Debug.LogError(
                    "Collision bake marker contains a " +
                    "MeshCollider without a Mesh:\n" +
                    markerPath
                );

                return false;
            }

            if (
                meshCollider.convex !=
                    TerrainCollisionPhysicsSettings
                        .Convex
            )
            {
                Debug.LogError(
                    "Collision bake marker has incorrect " +
                    "Convex configuration:\n" +
                    markerPath
                );

                return false;
            }

            if (
                meshCollider.cookingOptions !=
                    TerrainCollisionPhysicsSettings
                        .CookingOptions
            )
            {
                Debug.LogError(
                    "Collision bake marker has incorrect " +
                    "cooking options:\n" +
                    markerPath
                );

                return false;
            }
        }

        return true;
    }

    // =====================================================
    // NAME
    // =====================================================

    private static string GetBakeMarkerName(
        int regionX,
        int regionZ
    )
    {
        return
            $"CollisionBakeMarker_" +
            $"{regionX}_{regionZ}";
    }

    // =====================================================
    // PATH
    // =====================================================

    private static string GetBakeMarkerAssetPath(
        int regionX,
        int regionZ
    )
    {
        return
            $"{BakeMarkerFolder}/" +
            $"{GetBakeMarkerName(regionX, regionZ)}" +
            ".prefab";
    }

    // =====================================================
    // ADDRESS
    // =====================================================

    private static string GetBakeMarkerAddress(
        int regionX,
        int regionZ
    )
    {
        return
            $"{BakeMarkerAddressPrefix}_" +
            $"{regionX}_{regionZ}";
    }

    // =====================================================
    // REGION LABEL
    // =====================================================

    private static string GetRegionLabel(
        int regionX,
        int regionZ
    )
    {
        return
            $"{TerrainCollisionManifest.CollisionRegionLabelPrefix}_" +
            $"{regionX}_{regionZ}";
    }

    // =====================================================
    // RECORD
    // =====================================================

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
            this.regionX =
                regionX;

            this.regionZ =
                regionZ;

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