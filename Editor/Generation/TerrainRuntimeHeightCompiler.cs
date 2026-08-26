using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeHeightCompiler
{
    public static void CompileRuntimeHeightmaps(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
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

        // =================================================
        // AUTHORITATIVE SOURCE VALIDATION
        // =================================================

        if (
            !TerrainAuthoringStateUtility
                .TryValidateCommittedHeightfield(
                    worldSettings,
                    authoringData,
                    out TerrainAuthoringHeightManifest
                        authoringManifest,
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

        EnsureFoldersExist();

        TerrainHeightmapManifest runtimeManifest =
            GetOrCreateRuntimeManifest();

        if (runtimeManifest == null)
        {
            return;
        }

        /*
         * Generated output is invalid while a compile is in
         * progress. It becomes complete only after every tile
         * and manifest field is written successfully.
         */
        runtimeManifest.isComplete =
            false;

        EditorUtility.SetDirty(
            runtimeManifest
        );

        AssetDatabase.SaveAssetIfDirty(
            runtimeManifest
        );

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

        try
        {
            // =============================================
            // COMPILE REQUIRED TILES
            // =============================================

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
                            $"Tile ({coordinate.x}, {coordinate.y})",
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

        UpdateRuntimeManifest(
            runtimeManifest,
            worldSettings,
            authoringData,
            authoringManifest,
            authoringSignature,
            authoringContentHash
        );

        runtimeManifest.isComplete =
            true;

        EditorUtility.SetDirty(
            runtimeManifest
        );

        AssetDatabase.SaveAssetIfDirty(
            runtimeManifest
        );

        AssetDatabase.SaveAssets();

        TerrainGenerationStateUtility
            .MarkHeightmapsCompiled(
                worldSettings,
                authoringSignature
            );

        AssetDatabase.SaveAssets();

        Selection.activeObject =
            runtimeManifest;

        Debug.Log(
            "Runtime heightmap compilation complete.\n\n" +
            $"Authoring Revision: " +
            $"{authoringData.authoringRevision}\n" +
            $"Authoring Signature: " +
            $"{authoringSignature}\n" +
            $"Authoring Content Hash: " +
            $"{authoringContentHash}\n\n" +
            $"Height Tile Grid: " +
            $"{tileGridWidth} x {tileGridHeight}\n" +
            $"Total Tiles: {totalTiles}\n" +
            $"Samples Per Tile: " +
            $"{samplesPerSide} x {samplesPerSide}\n\n" +
            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Failed: {failedCount}\n\n" +
            $"Saved To:\n" +
            $"{TerrainRuntimeHeightAssetUtility.HeightmapTileFolder}"
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
            TerrainRuntimeHeightAssetUtility
                .GetHeightTilePath(
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
                TerrainRuntimeHeightAssetUtility
                    .GetHeightTileName(
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
            TerrainRuntimeHeightAssetUtility
                .GetHeightTileName(
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
    // RUNTIME MANIFEST
    // =====================================================

    private static TerrainHeightmapManifest
        GetOrCreateRuntimeManifest()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
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
            TerrainRuntimeHeightAssetUtility
                .HeightmapManifestPath
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return manifest;
    }

    private static void UpdateRuntimeManifest(
        TerrainHeightmapManifest runtimeManifest,
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainAuthoringHeightManifest authoringManifest,
        string authoringSignature,
        string authoringContentHash
    )
    {
        runtimeManifest.compilerVersion =
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion;

        runtimeManifest.sourceAuthoringRevision =
            authoringData.authoringRevision;

        runtimeManifest.sourceAuthoringSignature =
            authoringSignature;

        runtimeManifest.sourceAuthoringContentHash =
            authoringContentHash;

        runtimeManifest.gridWidth =
            worldSettings.gridWidth;

        runtimeManifest.gridHeight =
            worldSettings.gridHeight;

        runtimeManifest.chunkSize =
            worldSettings.chunkSize;

        runtimeManifest.lod0Resolution =
            worldSettings.lod0Resolution;

        runtimeManifest.heightTileChunkSpan =
            worldSettings.heightTileChunkSpan;

        runtimeManifest.heightTileGridWidth =
            worldSettings.HeightTileGridWidth;

        runtimeManifest.heightTileGridHeight =
            worldSettings.HeightTileGridHeight;

        runtimeManifest.heightTileWorldSize =
            worldSettings.HeightTileWorldSize;

        runtimeManifest.heightTileSamplesPerSide =
            worldSettings.HeightTileSamplesPerSide;
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
                TerrainRuntimeHeightAssetUtility
                    .HeightmapTileFolder
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
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapTileFolder
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
                !TerrainRuntimeHeightAssetUtility
                    .TryGetHeightTileCoordinates(
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
                WorldMeshesPaths.GeneratedHeightmaps
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
                WorldMeshesPaths.HeightmapTiles
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.GeneratedHeightmaps,
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
