using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainHeightmapGenerator
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

    private const string ComputeShaderPath =
        WorldMeshesPaths.TerrainHeightmapComputeShaderPath;

    private const string KernelName =
        "GenerateHeight";

    // =====================================================
    // GENERATE HEIGHTMAPS
    // =====================================================

    public static void GenerateHeightmaps(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot generate heightmaps: " +
                "WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Heightmaps must be generated " +
                "outside Play Mode."
            );

            return;
        }

        // -------------------------------------------------
        // Hardware support
        // -------------------------------------------------

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError(
                "Cannot generate heightmaps.\n\n" +
                "Compute shaders are not supported " +
                "by the current graphics device."
            );

            return;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogError(
                "Cannot generate heightmaps.\n\n" +
                "Async GPU readback is not supported " +
                "by the current graphics device."
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
                "Cannot generate heightmaps.\n\n" +
                "TextureFormat.RFloat is not supported " +
                "by the current graphics device."
            );

            return;
        }

        // -------------------------------------------------
        // Load compute shader
        // -------------------------------------------------

        ComputeShader computeShader =
            AssetDatabase
                .LoadAssetAtPath<ComputeShader>(
                    ComputeShaderPath
                );

        if (computeShader == null)
        {
            Debug.LogError(
                "Cannot generate heightmaps.\n\n" +
                "Compute shader could not be found:\n" +
                ComputeShaderPath
            );

            return;
        }

        int kernel;

        try
        {
            kernel =
                computeShader.FindKernel(
                    KernelName
                );
        }
        catch
        {
            Debug.LogError(
                "Cannot generate heightmaps.\n\n" +
                $"Kernel '{KernelName}' could not " +
                "be found in:\n" +
                ComputeShaderPath
            );

            return;
        }

        // -------------------------------------------------
        // Derived layout
        // -------------------------------------------------

        int tileGridWidth =
            worldSettings
                .HeightTileGridWidth;

        int tileGridHeight =
            worldSettings
                .HeightTileGridHeight;

        int samplesPerSide =
            worldSettings
                .HeightTileSamplesPerSide;

        int intervalsPerSide =
            samplesPerSide - 1;

        int lod0Resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        float sampleSpacing =
            chunkSize /
            lod0Resolution;

        int totalTiles =
            tileGridWidth *
            tileGridHeight;

        // -------------------------------------------------
        // Validate texture dimensions
        // -------------------------------------------------

        if (
            samplesPerSide >
            SystemInfo.maxTextureSize
        )
        {
            Debug.LogError(
                "Cannot generate heightmaps.\n\n" +

                $"Heightmap tile requires " +
                $"{samplesPerSide} x " +
                $"{samplesPerSide} samples.\n\n" +

                $"Maximum supported texture size: " +
                $"{SystemInfo.maxTextureSize}\n\n" +

                "Reduce Tile Chunk Span or " +
                "LOD0 Resolution."
            );

            return;
        }

        // -------------------------------------------------
        // Ensure folders
        // -------------------------------------------------

        EnsureFoldersExist();

        // -------------------------------------------------
        // Manifest
        // -------------------------------------------------

        TerrainHeightmapManifest manifest =
            GetOrCreateManifest();

        if (manifest == null)
        {
            return;
        }

        /*
         * Invalidate the manifest before generation.
         *
         * If generation fails or is cancelled, later
         * systems can see that the heightmaps are not
         * a complete synchronized set.
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
        // Existing tiles
        // -------------------------------------------------

        Dictionary<Vector2Int, Texture2D>
            existingTiles =
                FindExistingHeightTiles();

        // -------------------------------------------------
        // Statistics
        // -------------------------------------------------

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

        int totalOperations =
            existingTiles.Count +
            totalTiles;

        int currentOperation =
            0;

        // -------------------------------------------------
        // Thread group dimensions
        // -------------------------------------------------

        computeShader
            .GetKernelThreadGroupSizes(
                kernel,
                out uint threadGroupSizeX,
                out uint threadGroupSizeY,
                out _
            );

        int dispatchGroupsX =
            Mathf.CeilToInt(
                samplesPerSide /
                (float)threadGroupSizeX
            );

        int dispatchGroupsY =
            Mathf.CeilToInt(
                samplesPerSide /
                (float)threadGroupSizeY
            );

        // -------------------------------------------------
        // Synchronize
        // -------------------------------------------------

        try
        {
            // =============================================
            // PHASE 1
            // Remove obsolete heightmap tiles
            // =============================================

            foreach (
                KeyValuePair<Vector2Int, Texture2D>
                    pair
                in existingTiles
            )
            {
                Vector2Int coordinate =
                    pair.Key;

                Texture2D texture =
                    pair.Value;

                cancelled =
                    ShowProgress(
                        "Checking existing heightmap tiles",

                        $"Tile " +
                        $"({coordinate.x}, " +
                        $"{coordinate.y})",

                        currentOperation,
                        totalOperations
                    );

                if (cancelled)
                {
                    break;
                }

                bool outsideGrid =
                    coordinate.x < 0 ||
                    coordinate.y < 0 ||
                    coordinate.x >=
                        tileGridWidth ||
                    coordinate.y >=
                        tileGridHeight;

                if (outsideGrid)
                {
                    string assetPath =
                        AssetDatabase
                            .GetAssetPath(
                                texture
                            );

                    if (
                        AssetDatabase.DeleteAsset(
                            assetPath
                        )
                    )
                    {
                        removedCount++;
                    }
                }

                currentOperation++;
            }

            // =============================================
            // PHASE 2
            // Generate required heightmap tiles
            // =============================================

            if (!cancelled)
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
                                "Generating heightmap tiles",

                                $"Tile " +
                                $"({tileX}, {tileZ}) " +
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
                            existingTiles
                                .ContainsKey(
                                    coordinate
                                );

                        bool success =
                            GenerateAndSaveTile(
                                computeShader,
                                kernel,

                                worldSettings,

                                tileX,
                                tileZ,

                                samplesPerSide,
                                intervalsPerSide,
                                sampleSpacing,

                                dispatchGroupsX,
                                dispatchGroupsY
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
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // -------------------------------------------------
        // Cancelled
        // -------------------------------------------------

        if (cancelled)
        {
            Debug.LogWarning(
                "Heightmap generation cancelled.\n\n" +

                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +

                "Heightmap manifest remains incomplete."
            );

            return;
        }

        // -------------------------------------------------
        // Failed
        // -------------------------------------------------

        if (failedCount > 0)
        {
            Debug.LogError(
                "Heightmap generation did not " +
                "complete successfully.\n\n" +

                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +

                "Heightmap manifest remains incomplete."
            );

            return;
        }

        // -------------------------------------------------
        // Complete manifest
        // -------------------------------------------------

        UpdateManifest(
            manifest,
            worldSettings
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
        // Record successful heightmap generation state
        // -------------------------------------------------

        TerrainGenerationStateUtility
            .MarkHeightmapsGenerated(
                worldSettings
            );

        AssetDatabase.SaveAssets();

        // -------------------------------------------------
        // Selection
        // -------------------------------------------------

        Selection.activeObject =
            manifest;

        // -------------------------------------------------
        // Result
        // -------------------------------------------------

        Debug.Log(
            "Heightmap generation complete.\n\n" +

            $"Height Tile Grid: " +
            $"{tileGridWidth} x " +
            $"{tileGridHeight}\n" +

            $"Total Tiles: " +
            $"{totalTiles}\n\n" +

            $"Tile World Size: " +
            $"{worldSettings.HeightTileWorldSize}\n" +

            $"Samples Per Tile: " +
            $"{samplesPerSide} x " +
            $"{samplesPerSide}\n" +

            $"Sample Spacing: " +
            $"{sampleSpacing}\n\n" +

            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Failed: {failedCount}\n\n" +

            $"Saved To:\n" +
            $"{HeightmapTileFolder}"
        );
    }

    // =====================================================
    // GENERATE ONE TILE
    // =====================================================

    private static bool GenerateAndSaveTile(
        ComputeShader computeShader,
        int kernel,

        WorldSettings worldSettings,

        int tileX,
        int tileZ,

        int samplesPerSide,
        int intervalsPerSide,
        float sampleSpacing,

        int dispatchGroupsX,
        int dispatchGroupsY
    )
    {
        int sampleCount =
            samplesPerSide *
            samplesPerSide;

        ComputeBuffer heightBuffer =
            null;

        try
        {
            // -------------------------------------------------
            // GPU buffer
            // -------------------------------------------------

            heightBuffer =
                new ComputeBuffer(
                    sampleCount,
                    sizeof(float)
                );

            // -------------------------------------------------
            // Tile layout
            // -------------------------------------------------

            computeShader.SetInt(
                "_SamplesPerSide",
                samplesPerSide
            );

            computeShader.SetInt(
                "_TileX",
                tileX
            );

            computeShader.SetInt(
                "_TileZ",
                tileZ
            );

            computeShader.SetInt(
                "_TileIntervalsPerSide",
                intervalsPerSide
            );

            computeShader.SetFloat(
                "_SampleSpacing",
                sampleSpacing
            );

            // -------------------------------------------------
            // Height settings
            // -------------------------------------------------

            computeShader.SetInt(
                "_Seed",
                worldSettings.heightSeed
            );

            computeShader.SetFloat(
                "_NoiseScale",
                worldSettings.heightNoiseScale
            );

            computeShader.SetFloat(
                "_BaseHeight",
                worldSettings.heightBaseHeight
            );

            computeShader.SetFloat(
                "_HeightAmplitude",
                worldSettings.heightAmplitude
            );

            computeShader.SetInt(
                "_Octaves",
                worldSettings.heightOctaves
            );

            computeShader.SetFloat(
                "_Persistence",
                worldSettings.heightPersistence
            );

            computeShader.SetFloat(
                "_Lacunarity",
                worldSettings.heightLacunarity
            );

            // -------------------------------------------------
            // Output buffer
            // -------------------------------------------------

            computeShader.SetBuffer(
                kernel,
                "_HeightData",
                heightBuffer
            );

            // -------------------------------------------------
            // Dispatch
            // -------------------------------------------------

            computeShader.Dispatch(
                kernel,
                dispatchGroupsX,
                dispatchGroupsY,
                1
            );

            // -------------------------------------------------
            // GPU -> CPU
            // -------------------------------------------------

            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    heightBuffer
                );

            /*
             * This is an Editor generation tool.
             *
             * For now we deliberately wait for each tile
             * before saving it. Later this can be changed
             * to pipeline multiple asynchronous requests.
             */

            request.WaitForCompletion();

            if (request.hasError)
            {
                Debug.LogError(
                    "GPU readback failed for " +
                    $"heightmap tile " +
                    $"({tileX}, {tileZ})."
                );

                return false;
            }

            NativeArray<float> heightData =
                request.GetData<float>();

            if (
                heightData.Length !=
                sampleCount
            )
            {
                Debug.LogError(
                    "Unexpected height data size for " +
                    $"tile ({tileX}, {tileZ}).\n\n" +

                    $"Expected: {sampleCount}\n" +
                    $"Received: {heightData.Length}"
                );

                return false;
            }

            // -------------------------------------------------
            // Save
            // -------------------------------------------------

            return SaveOrUpdateHeightTile(
                tileX,
                tileZ,
                samplesPerSide,
                heightData
            );
        }
        finally
        {
            if (heightBuffer != null)
            {
                heightBuffer.Release();
            }
        }
    }

    // =====================================================
    // SAVE / UPDATE TILE
    // =====================================================

    private static bool SaveOrUpdateHeightTile(
        int tileX,
        int tileZ,
        int samplesPerSide,
        NativeArray<float> heightData
    )
    {
        string assetPath =
            GetHeightTilePath(
                tileX,
                tileZ
            );

        Texture2D existingTexture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    assetPath
                );

        // -------------------------------------------------
        // Create
        // -------------------------------------------------

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
                GetHeightTileName(
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

        // -------------------------------------------------
        // Update existing asset in place
        // -------------------------------------------------

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
                "Could not reinitialize existing " +
                "heightmap texture:\n" +
                assetPath
            );

            return false;
        }

        existingTexture.name =
            GetHeightTileName(
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
                .LoadAssetAtPath
                    <TerrainHeightmapManifest>(
                        HeightmapManifestPath
                    );

        if (manifest != null)
        {
            return manifest;
        }

        manifest =
            ScriptableObject
                .CreateInstance
                    <TerrainHeightmapManifest>();

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
        WorldSettings worldSettings
    )
    {
        manifest.generatorVersion =
            TerrainGenerationStateUtility
                .HeightGeneratorVersion;

        // -------------------------------------------------
        // World
        // -------------------------------------------------

        manifest.gridWidth =
            worldSettings.gridWidth;

        manifest.gridHeight =
            worldSettings.gridHeight;

        // -------------------------------------------------
        // Mesh layout
        // -------------------------------------------------

        manifest.chunkSize =
            worldSettings.chunkSize;

        manifest.lod0Resolution =
            worldSettings.lod0Resolution;

        // -------------------------------------------------
        // Height tile layout
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Noise
        // -------------------------------------------------

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
    // FIND EXISTING TILES
    // =====================================================

    private static Dictionary<Vector2Int, Texture2D>
        FindExistingHeightTiles()
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
                !TryGetHeightTileCoordinates(
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
    // TILE PATH / NAME
    // =====================================================

    public static string GetHeightTilePath(
        int tileX,
        int tileZ
    )
    {
        return
            $"{HeightmapTileFolder}/" +
            $"{GetHeightTileName(tileX, tileZ)}" +
            ".asset";
    }

    private static string GetHeightTileName(
        int tileX,
        int tileZ
    )
    {
        return
            $"HeightTile_{tileX}_{tileZ}";
    }

    // =====================================================
    // PARSE TILE COORDINATES
    // =====================================================

    private static bool TryGetHeightTileCoordinates(
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

        /*
         * Expected:
         *
         * HeightTile_12_7
         */

        string[] parts =
            fileName.Split(
                '_'
            );

        if (parts.Length != 3)
        {
            return false;
        }

        if (
            parts[0] !=
            "HeightTile"
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[1],
                out tileX
            )
        )
        {
            return false;
        }

        if (
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
        // -------------------------------------------------
        // Generated
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Generated/Heightmaps
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Generated/Heightmaps/Tiles
        // -------------------------------------------------

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
                    "Terrain Heightmap Generation",

                    operation +
                    "\n\n" +
                    detail,

                    progress
                );
    }
}