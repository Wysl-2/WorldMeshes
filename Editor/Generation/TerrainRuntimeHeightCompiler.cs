using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeHeightCompiler
{
    // =====================================================
    // PATHS
    // =====================================================

    public const string HeightmapRootFolder =
        WorldMeshesPaths.GeneratedHeightmaps;

    public const string HeightmapTileFolder =
        WorldMeshesPaths.HeightmapTiles;

    public const string HeightmapManifestPath =
        WorldMeshesPaths.HeightmapManifestAssetPath;

    // =====================================================
    // COMPILE RUNTIME HEIGHTMAPS
    // =====================================================

    public static void CompileRuntimeHeightmaps(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        // -------------------------------------------------
        // Basic validation
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot compile runtime heightmaps: " +
                "WorldSettings is null."
            );

            return;
        }

        if (authoringData == null)
        {
            Debug.LogError(
                "Cannot compile runtime heightmaps: " +
                "TerrainAuthoringData is null."
            );

            return;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Runtime heightmaps must be compiled " +
                "outside Play Mode."
            );

            return;
        }

        if (
            !SystemInfo.SupportsTextureFormat(
                TextureFormat.RFloat
            )
        )
        {
            Debug.LogError(
                "Cannot compile runtime heightmaps.\n\n" +
                "TextureFormat.RFloat is not supported " +
                "by the current graphics device."
            );

            return;
        }

        // -------------------------------------------------
        // Validate the complete committed authoring source
        // and calculate its compile-time content hash.
        // -------------------------------------------------

        if (
            !TerrainAuthoringStateUtility
                .TryGetCurrentAuthoringContentHash(
                    worldSettings,
                    authoringData,
                    out string authoringContentHash,
                    out string authoringValidationError
                )
        )
        {
            Debug.LogError(
                "Cannot compile runtime heightmaps because the " +
                "committed authoring heightfield is invalid.\n\n" +
                authoringValidationError
            );

            return;
        }

        string authoringSignature =
            TerrainAuthoringStateUtility
                .GetCurrentAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                authoringSignature
            )
        )
        {
            Debug.LogError(
                "Cannot compile runtime heightmaps because the " +
                "current authoring signature could not be calculated."
            );

            return;
        }

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int totalTiles =
            tileGridWidth *
            tileGridHeight;

        // -------------------------------------------------
        // Ensure generated output folders exist.
        // -------------------------------------------------

        EnsureFoldersExist();

        // -------------------------------------------------
        // Runtime manifest
        // -------------------------------------------------

        TerrainHeightmapManifest manifest =
            GetOrCreateManifest();

        if (manifest == null)
        {
            return;
        }

        /*
         * The generated output is considered invalid from the
         * moment compilation starts until every required tile
         * and the manifest have completed successfully.
         */
        manifest.isComplete =
            false;

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        // -------------------------------------------------
        // Existing runtime output
        // -------------------------------------------------

        Dictionary<Vector2Int, Texture2D>
            existingTiles =
                FindExistingRuntimeHeightTiles();

        List<KeyValuePair<Vector2Int, Texture2D>>
            obsoleteTiles =
                new List<KeyValuePair<Vector2Int, Texture2D>>();

        foreach (
            KeyValuePair<Vector2Int, Texture2D> pair
            in existingTiles
        )
        {
            Vector2Int coordinate =
                pair.Key;

            if (
                coordinate.x < 0
                ||
                coordinate.y < 0
                ||
                coordinate.x >= tileGridWidth
                ||
                coordinate.y >= tileGridHeight
            )
            {
                obsoleteTiles.Add(
                    pair
                );
            }
        }

        int totalOperations =
            totalTiles +
            obsoleteTiles.Count;

        int currentOperation =
            0;

        int createdCount =
            0;

        int updatedCount =
            0;

        int removedCount =
            0;

        int failedCount =
            0;

        bool cancelled =
            false;

        // =================================================
        // COMPILE REQUIRED TILES
        // =================================================

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
                        ShowProgress(
                            "Compiling runtime heightmap tiles",

                            $"Tile ({tileX}, {tileZ}) " +
                            $"{tileX + tileZ * tileGridWidth + 1} " +
                            $"/ {totalTiles}",

                            currentOperation,
                            totalOperations
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    Vector2Int coordinate =
                        new Vector2Int(
                            tileX,
                            tileZ
                        );

                    bool existedBefore =
                        existingTiles.ContainsKey(
                            coordinate
                        );

                    bool success =
                        CompileOneTile(
                            tileX,
                            tileZ,
                            samplesPerSide
                        );

                    if (!success)
                    {
                        failedCount++;
                    }
                    else if (existedBefore)
                    {
                        updatedCount++;
                    }
                    else
                    {
                        createdCount++;
                    }

                    currentOperation++;
                }

                if (cancelled)
                {
                    break;
                }
            }

            // =============================================
            // REMOVE OBSOLETE GENERATED TILES
            // =============================================

            if (
                !cancelled
                &&
                failedCount == 0
            )
            {
                foreach (
                    KeyValuePair<Vector2Int, Texture2D> pair
                    in obsoleteTiles
                )
                {
                    Vector2Int coordinate =
                        pair.Key;

                    cancelled =
                        ShowProgress(
                            "Removing obsolete runtime heightmap tiles",

                            $"Tile ({coordinate.x}, " +
                            $"{coordinate.y})",

                            currentOperation,
                            totalOperations
                        );

                    if (cancelled)
                    {
                        break;
                    }

                    string path =
                        AssetDatabase.GetAssetPath(
                            pair.Value
                        );

                    if (
                        !string.IsNullOrEmpty(path)
                        &&
                        AssetDatabase.DeleteAsset(
                            path
                        )
                    )
                    {
                        removedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }

                    currentOperation++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // =================================================
        // CANCELLED / FAILED
        // =================================================

        if (cancelled)
        {
            Debug.LogWarning(
                "Runtime heightmap compilation was cancelled.\n\n" +

                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +

                "The runtime heightmap manifest remains incomplete."
            );

            return;
        }

        if (failedCount > 0)
        {
            Debug.LogError(
                "Runtime heightmap compilation did not complete " +
                "successfully.\n\n" +

                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +

                "The runtime heightmap manifest remains incomplete."
            );

            return;
        }

        // =================================================
        // SUCCESSFUL MANIFEST
        // =================================================

        UpdateManifest(
            manifest,
            worldSettings,
            authoringData,
            authoringSignature,
            authoringContentHash
        );

        manifest.isComplete =
            true;

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        AssetDatabase.SaveAssets();

        // -------------------------------------------------
        // Generated height revision / dependency state
        // -------------------------------------------------

        TerrainGenerationStateUtility
            .MarkHeightmapsCompiled(
                worldSettings,
                authoringSignature
            );

        AssetDatabase.SaveAssets();

        Selection.activeObject =
            manifest;

        Debug.Log(
            "Runtime heightmap compilation complete.\n\n" +

            $"Authoring Revision: " +
            $"{authoringData.authoringRevision}\n" +

            $"Authoring Signature: " +
            $"{authoringSignature}\n\n" +

            $"Height Tile Grid: " +
            $"{tileGridWidth} x " +
            $"{tileGridHeight}\n" +

            $"Total Tiles: {totalTiles}\n" +

            $"Samples Per Tile: " +
            $"{samplesPerSide} x " +
            $"{samplesPerSide}\n\n" +

            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Failed: {failedCount}\n\n" +

            $"Saved To:\n" +
            $"{HeightmapTileFolder}"
        );
    }

    // =====================================================
    // COMPILE ONE TILE
    // =====================================================

    private static bool CompileOneTile(
        int tileX,
        int tileZ,
        int samplesPerSide
    )
    {
        string sourcePath =
            TerrainAuthoringStateUtility
                .GetAuthoringHeightTilePath(
                    tileX,
                    tileZ
                );

        Texture2D sourceTexture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    sourcePath
                );

        if (sourceTexture == null)
        {
            Debug.LogError(
                "Authoring height tile disappeared during " +
                "runtime compilation:\n" +
                sourcePath
            );

            return false;
        }

        NativeArray<float> sourceHeightData;

        try
        {
            sourceHeightData =
                sourceTexture.GetPixelData<float>(
                    0
                );
        }
        catch (
            System.Exception exception
        )
        {
            Debug.LogError(
                $"Could not read authoring height tile " +
                $"({tileX}, {tileZ}) during compilation.\n\n" +
                exception.Message
            );

            return false;
        }

        return SaveOrUpdateRuntimeHeightTile(
            tileX,
            tileZ,
            samplesPerSide,
            sourceHeightData
        );
    }

    // =====================================================
    // SAVE / UPDATE RUNTIME TILE
    // =====================================================

    private static bool SaveOrUpdateRuntimeHeightTile(
        int tileX,
        int tileZ,
        int samplesPerSide,
        NativeArray<float> heightData
    )
    {
        string assetPath =
            GetRuntimeHeightTilePath(
                tileX,
                tileZ
            );

        Texture2D existingTexture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    assetPath
                );

        if (existingTexture == null)
        {
            Texture2D texture =
                new Texture2D(
                    samplesPerSide,
                    samplesPerSide,
                    TextureFormat.RFloat,
                    false,
                    true
                );

            texture.name =
                GetRuntimeHeightTileName(
                    tileX,
                    tileZ
                );

            texture.wrapMode =
                TextureWrapMode.Clamp;

            texture.filterMode =
                FilterMode.Point;

            texture.SetPixelData(
                heightData,
                0
            );

            texture.Apply(
                false,
                false
            );

            AssetDatabase.CreateAsset(
                texture,
                assetPath
            );

            return true;
        }

        bool reinitialized =
            existingTexture.Reinitialize(
                samplesPerSide,
                samplesPerSide,
                TextureFormat.RFloat,
                false
            );

        if (!reinitialized)
        {
            Debug.LogError(
                "Could not reinitialize existing runtime " +
                "heightmap texture:\n" +
                assetPath
            );

            return false;
        }

        existingTexture.name =
            GetRuntimeHeightTileName(
                tileX,
                tileZ
            );

        existingTexture.wrapMode =
            TextureWrapMode.Clamp;

        existingTexture.filterMode =
            FilterMode.Point;

        existingTexture.SetPixelData(
            heightData,
            0
        );

        existingTexture.Apply(
            false,
            false
        );

        EditorUtility.SetDirty(
            existingTexture
        );

        AssetDatabase.SaveAssetIfDirty(
            existingTexture
        );

        return true;
    }

    // =====================================================
    // MANIFEST
    // =====================================================

    private static TerrainHeightmapManifest
        GetOrCreateManifest()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    HeightmapManifestPath
                );

        if (manifest != null)
        {
            return manifest;
        }

        manifest =
            ScriptableObject
                .CreateInstance<TerrainHeightmapManifest>();

        manifest.name =
            "HeightmapManifest";

        AssetDatabase.CreateAsset(
            manifest,
            HeightmapManifestPath
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return manifest;
    }

    private static void UpdateManifest(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        string authoringSignature,
        string authoringContentHash
    )
    {
        manifest.generatorVersion =
            TerrainGenerationStateUtility
                .HeightGeneratorVersion;

        manifest.compilerVersion =
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion;

        manifest.sourceAuthoringRevision =
            authoringData.authoringRevision;

        manifest.sourceAuthoringSignature =
            authoringSignature;

        manifest.sourceAuthoringContentHash =
            authoringContentHash;

        // -------------------------------------------------
        // World
        // -------------------------------------------------

        manifest.gridWidth =
            worldSettings.gridWidth;

        manifest.gridHeight =
            worldSettings.gridHeight;

        // -------------------------------------------------
        // Mesh / height layout
        // -------------------------------------------------

        manifest.chunkSize =
            worldSettings.chunkSize;

        manifest.lod0Resolution =
            worldSettings.lod0Resolution;

        manifest.heightTileChunkSpan =
            worldSettings.heightTileChunkSpan;

        manifest.heightTileGridWidth =
            worldSettings.HeightTileGridWidth;

        manifest.heightTileGridHeight =
            worldSettings.HeightTileGridHeight;

        manifest.heightTileWorldSize =
            worldSettings.HeightTileWorldSize;

        manifest.heightTileSamplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        /*
         * These fields are retained for compatibility with the
         * current TerrainHeightmapValidator while the project is
         * transitioning away from direct procedural runtime
         * generation. They no longer define the runtime terrain
         * source of truth.
         */
        manifest.heightSeed =
            worldSettings.heightSeed;

        manifest.heightNoiseScale =
            worldSettings.heightNoiseScale;

        manifest.heightBaseHeight =
            worldSettings.heightBaseHeight;

        manifest.heightAmplitude =
            worldSettings.heightAmplitude;

        manifest.heightOctaves =
            worldSettings.heightOctaves;

        manifest.heightPersistence =
            worldSettings.heightPersistence;

        manifest.heightLacunarity =
            worldSettings.heightLacunarity;
    }

    // =====================================================
    // EXISTING GENERATED TILES
    // =====================================================

    private static Dictionary<Vector2Int, Texture2D>
        FindExistingRuntimeHeightTiles()
    {
        Dictionary<Vector2Int, Texture2D> tiles =
            new Dictionary<Vector2Int, Texture2D>();

        if (
            !AssetDatabase.IsValidFolder(
                HeightmapTileFolder
            )
        )
        {
            return tiles;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Texture2D",
                new[]
                {
                    HeightmapTileFolder
                }
            );

        foreach (
            string guid
            in guids
        )
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            if (
                !TryGetRuntimeHeightTileCoordinates(
                    path,
                    out int tileX,
                    out int tileZ
                )
            )
            {
                continue;
            }

            Texture2D texture =
                AssetDatabase
                    .LoadAssetAtPath<Texture2D>(
                        path
                    );

            if (texture == null)
            {
                continue;
            }

            tiles[
                new Vector2Int(
                    tileX,
                    tileZ
                )
            ] =
                texture;
        }

        return tiles;
    }

    // =====================================================
    // RUNTIME TILE PATH / NAME
    // =====================================================

    public static string GetRuntimeHeightTilePath(
        int tileX,
        int tileZ
    )
    {
        return
            $"{HeightmapTileFolder}/" +
            $"{GetRuntimeHeightTileName(tileX, tileZ)}" +
            ".asset";
    }

    public static string GetRuntimeHeightTileName(
        int tileX,
        int tileZ
    )
    {
        return
            $"HeightTile_{tileX}_{tileZ}";
    }

    private static bool TryGetRuntimeHeightTileCoordinates(
        string assetPath,
        out int tileX,
        out int tileZ
    )
    {
        tileX =
            0;

        tileZ =
            0;

        string fileName =
            Path.GetFileNameWithoutExtension(
                assetPath
            );

        string[] parts =
            fileName.Split(
                '_'
            );

        if (
            parts.Length != 3
            ||
            parts[0] != "HeightTile"
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[1],
                out tileX
            )
            ||
            !int.TryParse(
                parts[2],
                out tileZ
            )
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // FOLDERS
    // =====================================================

    private static void EnsureFoldersExist()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Generated
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Root,
                "Generated"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                HeightmapRootFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Generated,
                "Heightmaps"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                HeightmapTileFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                HeightmapRootFolder,
                "Tiles"
            );
        }
    }

    // =====================================================
    // PROGRESS
    // =====================================================

    private static bool ShowProgress(
        string operation,
        string detail,
        int current,
        int total
    )
    {
        float progress =
            total > 0
                ? (float)current /
                  total
                : 1f;

        return
            EditorUtility
                .DisplayCancelableProgressBar(
                    "Compile Runtime Terrain Heightmaps",
                    operation +
                    "\n\n" +
                    detail,
                    progress
                );
    }
}
