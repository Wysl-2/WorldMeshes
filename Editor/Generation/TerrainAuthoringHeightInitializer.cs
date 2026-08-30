using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainAuthoringHeightInitializer
{
    public const string AuthoringHeightRootFolder =
        WorldMeshesPaths.AuthoringHeight;

    public const string AuthoringHeightTileFolder =
        WorldMeshesPaths.AuthoringHeightTiles;

    private const string ComputeShaderPath =
        WorldMeshesPaths.TerrainHeightmapComputeShaderPath;

    private const string ProceduralKernelName =
        "GenerateHeight";

    // =====================================================
    // INITIALIZE HEIGHTFIELD
    // =====================================================

    public static void InitializeHeightfield(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot initialize authoring heightfield: " +
                "WorldSettings is null."
            );

            return;
        }

        if (authoringData == null)
        {
            Debug.LogError(
                "Cannot initialize authoring heightfield: " +
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
                "The authoring heightfield must be " +
                "initialized outside Play Mode."
            );

            return;
        }

        /*
         * Imported initialization remains intentionally
         * unimplemented. Reject it before touching the current
         * authoring manifest or committed height tiles.
         */
        if (
            authoringData.sourceMode ==
            TerrainHeightSourceMode.Imported
        )
        {
            Debug.LogWarning(
                "Imported terrain height initialization " +
                "is not implemented yet.\n\n" +
                "The existing authoring heightfield has " +
                "not been modified."
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
                "Cannot initialize authoring heightfield.\n\n" +
                "TextureFormat.RFloat is not supported " +
                "by the current graphics device."
            );

            return;
        }

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int intervalsPerSide =
            samplesPerSide - 1;

        int heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        float sampleSpacing =
            chunkSize /
            heightfieldResolutionPerChunk;

        int totalTiles =
            tileGridWidth *
            tileGridHeight;

        if (
            samplesPerSide >
            SystemInfo.maxTextureSize
        )
        {
            Debug.LogError(
                "Cannot initialize authoring heightfield.\n\n" +
                $"Authoring height tile requires " +
                $"{samplesPerSide} x {samplesPerSide} samples.\n\n" +
                $"Maximum supported texture size: " +
                $"{SystemInfo.maxTextureSize}\n\n" +
                "Reduce Tile Chunk Span or Heightfield Resolution / Chunk."
            );

            return;
        }

        // =================================================
        // PROCEDURAL COMPUTE SETUP
        // =================================================

        ComputeShader proceduralComputeShader =
            null;

        int proceduralKernel =
            -1;

        int dispatchGroupsX =
            0;

        int dispatchGroupsY =
            0;

        if (
            authoringData.sourceMode ==
            TerrainHeightSourceMode.Procedural
        )
        {
            if (
                !TryPrepareProceduralGenerator(
                    samplesPerSide,
                    out proceduralComputeShader,
                    out proceduralKernel,
                    out dispatchGroupsX,
                    out dispatchGroupsY
                )
            )
            {
                return;
            }
        }

        EnsureFoldersExist();

        // =================================================
        // AUTHORING MANIFEST TRANSACTION BOUNDARY
        // =================================================

        TerrainAuthoringHeightManifest manifest =
            TerrainAuthoringStateUtility
                .GetOrCreateAuthoringHeightManifest();

        if (manifest == null)
        {
            Debug.LogError(
                "Could not create or load the authoring " +
                "heightfield manifest."
            );

            return;
        }

        /*
         * From this point until successful finalization, the
         * committed authoring tile set must not be used as a
         * compilation source.
         */
        TerrainAuthoringStateUtility
            .MarkAuthoringHeightfieldIncomplete(
                manifest,
                worldSettings
            );

        Dictionary<Vector2Int, Texture2D>
            existingTiles =
                FindExistingAuthoringHeightTiles();

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

        try
        {
            // =============================================
            // REMOVE OBSOLETE TILES
            // =============================================

            foreach (
                KeyValuePair<Vector2Int, Texture2D> pair
                in existingTiles
            )
            {
                Vector2Int coordinate =
                    pair.Key;

                cancelled =
                    ShowProgress(
                        "Checking existing authoring tiles",
                        $"Tile ({coordinate.x}, {coordinate.y})",
                        currentOperation,
                        totalOperations
                    );

                if (cancelled)
                {
                    break;
                }

                bool outsideGrid =
                    coordinate.x < 0
                    ||
                    coordinate.y < 0
                    ||
                    coordinate.x >= tileGridWidth
                    ||
                    coordinate.y >= tileGridHeight;

                if (outsideGrid)
                {
                    string assetPath =
                        AssetDatabase
                            .GetAssetPath(
                                pair.Value
                            );

                    if (
                        !string.IsNullOrEmpty(
                            assetPath
                        )
                        &&
                        AssetDatabase.DeleteAsset(
                            assetPath
                        )
                    )
                    {
                        removedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                }

                currentOperation++;
            }

            // =============================================
            // WRITE REQUIRED TILES
            // =============================================

            if (
                !cancelled
                &&
                failedCount == 0
            )
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
                                GetGenerationOperationLabel(
                                    authoringData.sourceMode
                                ),
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
                            existingTiles
                                .ContainsKey(
                                    coordinate
                                );

                        bool success;

                        switch (
                            authoringData.sourceMode
                        )
                        {
                            case TerrainHeightSourceMode.Flat:
                            {
                                success =
                                    GenerateAndSaveFlatTile(
                                        authoringData.flatHeight,
                                        tileX,
                                        tileZ,
                                        samplesPerSide
                                    );

                                break;
                            }

                            case TerrainHeightSourceMode.Procedural:
                            {
                                success =
                                    GenerateAndSaveProceduralTile(
                                        proceduralComputeShader,
                                        proceduralKernel,
                                        worldSettings,
                                        tileX,
                                        tileZ,
                                        samplesPerSide,
                                        intervalsPerSide,
                                        sampleSpacing,
                                        dispatchGroupsX,
                                        dispatchGroupsY
                                    );

                                break;
                            }

                            default:
                            {
                                success =
                                    false;

                                break;
                            }
                        }

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

        if (cancelled)
        {
            Debug.LogWarning(
                "Authoring heightfield initialization " +
                "was cancelled.\n\n" +
                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +
                "The authoring manifest remains incomplete. " +
                "Runtime compilation is blocked until the " +
                "authoring heightfield is reinitialized successfully."
            );

            return;
        }

        if (failedCount > 0)
        {
            Debug.LogError(
                "Authoring heightfield initialization " +
                "did not complete successfully.\n\n" +
                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +
                "The authoring manifest remains incomplete."
            );

            return;
        }

        // =================================================
        // VERIFY COMPLETE COMMITTED SET
        // =================================================

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (
            !TerrainAuthoringStateUtility
                .TryCalculateCommittedHeightContentHash(
                    worldSettings,
                    out string committedContentHash,
                    out float minimumCommittedHeight,
                    out float maximumCommittedHeight,
                    out string contentHashError
                )
        )
        {
            Debug.LogError(
                "Authoring heightfield initialization wrote all " +
                "requested tiles, but final committed-heightfield " +
                "validation failed.\n\n" +
                contentHashError +
                "\n\nThe authoring manifest remains incomplete."
            );

            return;
        }

        // =================================================
        // SUCCESS
        // =================================================

        if (
            authoringData.authoringRevision < 0
        )
        {
            authoringData.authoringRevision =
                0;
        }

        authoringData.authoringRevision++;

        EditorUtility.SetDirty(
            authoringData
        );

        AssetDatabase.SaveAssetIfDirty(
            authoringData
        );

        if (
            !TerrainAuthoringStateUtility
                .FinalizeAuthoringHeightfield(
                    manifest,
                    worldSettings,
                    authoringData,
                    committedContentHash,
                    minimumCommittedHeight,
                    maximumCommittedHeight,
                    out string finalizeError
                )
        )
        {
            Debug.LogError(
                "Authoring tiles were written successfully, " +
                "but the authoring manifest could not be finalized.\n\n" +
                finalizeError
            );

            return;
        }

        AssetDatabase.SaveAssets();

        /*
         * The committed authoring source has changed.
         * Rebuild the transient edit-mode height cache directly
         * from the new authoring tiles.
         */
        TerrainAuthoringPreviewService
            .NotifyCommittedHeightfieldChanged();

        Selection.activeObject =
            authoringData;

        Debug.Log(
            "Authoring heightfield initialization complete.\n\n" +
            $"Source Mode: {authoringData.sourceMode}\n\n" +
            $"Height Tile Grid: " +
            $"{tileGridWidth} x {tileGridHeight}\n" +
            $"Total Tiles: {totalTiles}\n\n" +
            $"Tile World Size: " +
            $"{worldSettings.HeightTileWorldSize}\n" +
            $"Samples Per Tile: " +
            $"{samplesPerSide} x {samplesPerSide}\n" +
            $"Sample Spacing: {sampleSpacing}\n\n" +
            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Failed: {failedCount}\n\n" +
            $"Authoring Revision: " +
            $"{authoringData.authoringRevision}\n\n" +
            $"Authoring Content Hash:\n" +
            $"{committedContentHash}\n\n" +
            $"Committed Height Range: " +
            $"{minimumCommittedHeight:R} -> " +
            $"{maximumCommittedHeight:R}\n\n" +
            $"Saved To:\n" +
            $"{AuthoringHeightTileFolder}"
        );
    }

    // =====================================================
    // PREPARE PROCEDURAL GENERATOR
    // =====================================================

    private static bool TryPrepareProceduralGenerator(
        int samplesPerSide,
        out ComputeShader computeShader,
        out int kernel,
        out int dispatchGroupsX,
        out int dispatchGroupsY
    )
    {
        computeShader =
            null;

        kernel =
            -1;

        dispatchGroupsX =
            0;

        dispatchGroupsY =
            0;

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError(
                "Cannot initialize procedural authoring " +
                "heightfield.\n\n" +
                "Compute shaders are not supported " +
                "by the current graphics device."
            );

            return false;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogError(
                "Cannot initialize procedural authoring " +
                "heightfield.\n\n" +
                "Async GPU readback is not supported " +
                "by the current graphics device."
            );

            return false;
        }

        computeShader =
            AssetDatabase
                .LoadAssetAtPath<ComputeShader>(
                    ComputeShaderPath
                );

        if (computeShader == null)
        {
            Debug.LogError(
                "Cannot initialize procedural authoring " +
                "heightfield.\n\n" +
                "Compute shader could not be found:\n" +
                ComputeShaderPath
            );

            return false;
        }

        try
        {
            kernel =
                computeShader.FindKernel(
                    ProceduralKernelName
                );
        }
        catch
        {
            Debug.LogError(
                "Cannot initialize procedural authoring " +
                "heightfield.\n\n" +
                $"Kernel '{ProceduralKernelName}' could not " +
                $"be found in:\n" +
                ComputeShaderPath
            );

            return false;
        }

        computeShader
            .GetKernelThreadGroupSizes(
                kernel,
                out uint threadGroupSizeX,
                out uint threadGroupSizeY,
                out _
            );

        if (
            threadGroupSizeX == 0
            ||
            threadGroupSizeY == 0
        )
        {
            Debug.LogError(
                "Cannot initialize procedural authoring " +
                "heightfield.\n\n" +
                "The procedural compute kernel reported " +
                "an invalid thread-group size."
            );

            return false;
        }

        dispatchGroupsX =
            Mathf.CeilToInt(
                samplesPerSide /
                (float)threadGroupSizeX
            );

        dispatchGroupsY =
            Mathf.CeilToInt(
                samplesPerSide /
                (float)threadGroupSizeY
            );

        return true;
    }

    // =====================================================
    // FLAT TILE
    // =====================================================

    private static bool GenerateAndSaveFlatTile(
        float flatHeight,
        int tileX,
        int tileZ,
        int samplesPerSide
    )
    {
        int sampleCount =
            samplesPerSide *
            samplesPerSide;

        NativeArray<float> heightData =
            new NativeArray<float>(
                sampleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );

        try
        {
            for (
                int index = 0;
                index < sampleCount;
                index++
            )
            {
                heightData[index] =
                    flatHeight;
            }

            return SaveOrUpdateAuthoringHeightTile(
                tileX,
                tileZ,
                samplesPerSide,
                heightData
            );
        }
        finally
        {
            if (heightData.IsCreated)
            {
                heightData.Dispose();
            }
        }
    }

    // =====================================================
    // PROCEDURAL TILE
    // =====================================================

    private static bool GenerateAndSaveProceduralTile(
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
            heightBuffer =
                new ComputeBuffer(
                    sampleCount,
                    sizeof(float)
                );

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

            computeShader.SetBuffer(
                kernel,
                "_HeightData",
                heightBuffer
            );

            computeShader.Dispatch(
                kernel,
                dispatchGroupsX,
                dispatchGroupsY,
                1
            );

            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.Request(
                    heightBuffer
                );

            request.WaitForCompletion();

            if (request.hasError)
            {
                Debug.LogError(
                    "GPU readback failed for procedural " +
                    "authoring height tile " +
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
                    "Unexpected procedural authoring " +
                    "height data size for tile " +
                    $"({tileX}, {tileZ}).\n\n" +
                    $"Expected: {sampleCount}\n" +
                    $"Received: {heightData.Length}"
                );

                return false;
            }

            return SaveOrUpdateAuthoringHeightTile(
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
    // SAVE / UPDATE AUTHORING TILE
    // =====================================================

    private static bool SaveOrUpdateAuthoringHeightTile(
        int tileX,
        int tileZ,
        int samplesPerSide,
        NativeArray<float> heightData
    )
    {
        string assetPath =
            GetAuthoringHeightTilePath(
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
                GetAuthoringHeightTileName(
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
                "Could not reinitialize existing " +
                "authoring height texture:\n" +
                assetPath
            );

            return false;
        }

        existingTexture.name =
            GetAuthoringHeightTileName(
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
    // FIND EXISTING AUTHORING TILES
    // =====================================================

    private static Dictionary<Vector2Int, Texture2D>
        FindExistingAuthoringHeightTiles()
    {
        Dictionary<Vector2Int, Texture2D> tiles =
            new Dictionary<Vector2Int, Texture2D>();

        if (
            !AssetDatabase.IsValidFolder(
                AuthoringHeightTileFolder
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
                    AuthoringHeightTileFolder
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
                !TryGetAuthoringHeightTileCoordinates(
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

    public static string GetAuthoringHeightTilePath(
        int tileX,
        int tileZ
    )
    {
        return
            TerrainAuthoringStateUtility
                .GetAuthoringHeightTilePath(
                    tileX,
                    tileZ
                );
    }

    public static string GetAuthoringHeightTileName(
        int tileX,
        int tileZ
    )
    {
        return
            TerrainAuthoringStateUtility
                .GetAuthoringHeightTileName(
                    tileX,
                    tileZ
                );
    }

    private static bool TryGetAuthoringHeightTileCoordinates(
        string assetPath,
        out int tileX,
        out int tileZ
    )
    {
        tileX = 0;
        tileZ = 0;

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

        return
            int.TryParse(
                parts[1],
                out tileX
            )
            &&
            int.TryParse(
                parts[2],
                out tileZ
            );
    }

    // =====================================================
    // FOLDERS
    // =====================================================

    private static void EnsureFoldersExist()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Authoring
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Root,
                "Authoring"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                AuthoringHeightRootFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Authoring,
                "Height"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                AuthoringHeightTileFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                AuthoringHeightRootFolder,
                "Tiles"
            );
        }
    }

    private static string GetGenerationOperationLabel(
        TerrainHeightSourceMode sourceMode
    )
    {
        switch (sourceMode)
        {
            case TerrainHeightSourceMode.Flat:
                return
                    "Initializing flat authoring heightfield";

            case TerrainHeightSourceMode.Procedural:
                return
                    "Initializing procedural authoring heightfield";

            default:
                return
                    "Initializing authoring heightfield";
        }
    }

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
                    "Terrain Authoring Height Initialization",
                    operation +
                    "\n\n" +
                    detail,
                    progress
                );
    }
}
