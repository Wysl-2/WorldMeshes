using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Stage 8 editor compiler for final runtime surface suitability.
 *
 * Raw Slope/Curvature stay in the editor Terrain Analysis system. The compiler
 * reads complete analysis tiles asynchronously in small batches, evaluates
 * the Scree suitability rules once, quantizes the final result to R8, and
 * saves tile-aligned runtime assets.
 */
public static class TerrainSurfaceMaskCompiler
{
    private const int ReadbackBatchTileCount =
        8;

    private static BuildState activeBuild;

    public static bool IsGenerating =>
        activeBuild != null;

    public static void GenerateSurfaceMasks(
        WorldSettings worldSettings
    )
    {
        if (activeBuild != null)
        {
            Debug.LogWarning(
                "Runtime surface-mask generation is already in progress."
            );

            return;
        }

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot generate runtime surface masks: WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Runtime surface masks must be generated outside Play Mode."
            );

            return;
        }

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because the runtime heightmaps are not current.\n\n" +
                "Compile Runtime Heightmaps first."
            );

            return;
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (
            heightManifest == null
            ||
            !heightManifest.isComplete
        )
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because the runtime heightmap manifest is missing or incomplete."
            );

            return;
        }

        TerrainSurfaceSettings surfaceSettings =
            TerrainSurfaceSettingsEditorUtility
                .LoadOrCreate(
                    out string settingsError
                );

        if (surfaceSettings == null)
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because TerrainSurfaceSettings is unavailable.\n\n" +
                settingsError
            );

            return;
        }

        if (!TerrainAuthoringPreviewService.CacheReady)
        {
            TerrainAuthoringPreviewService
                .RequestRefresh();

            Debug.LogWarning(
                "Runtime surface-mask generation needs the ready Terrain Authoring Height Preview because the Terrain Analysis GPU cache is its source.\n\n" +
                "A preview refresh was requested. Run Bake Runtime Surface Masks again once Height Preview reports Ready."
            );

            return;
        }

        ScreeSettings scree =
            surfaceSettings.Scree;

        TerrainAnalysisKey slopeKey =
            TerrainAnalysisKey.Slope;

        TerrainAnalysisKey curvatureKey =
            TerrainAnalysisKey.Curvature(
                scree.curvatureScale
            );

        TerrainAnalysisLayer slopeLayer =
            TerrainAnalysisService
                .RequestLayer(
                    slopeKey
                );

        TerrainAnalysisLayer curvatureLayer =
            TerrainAnalysisService
                .RequestLayer(
                    curvatureKey
                );

        if (
            !ValidateAnalysisLayer(
                slopeLayer,
                heightManifest,
                out string slopeError
            )
        )
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because Slope analysis is unavailable or does not match the runtime height-tile layout.\n\n" +
                slopeError
            );

            TerrainAnalysisService
                .ReleaseLayer(
                    curvatureKey
                );

            return;
        }

        if (
            !ValidateAnalysisLayer(
                curvatureLayer,
                heightManifest,
                out string curvatureError
            )
        )
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because Curvature analysis is unavailable or does not match the runtime height-tile layout.\n\n" +
                curvatureError
            );

            TerrainAnalysisService
                .ReleaseLayer(
                    curvatureKey
                );

            return;
        }

        if (
            slopeLayer.CacheOriginTile !=
                curvatureLayer.CacheOriginTile
            ||
            slopeLayer.CacheSize !=
                curvatureLayer.CacheSize
            ||
            slopeLayer.SourceSignature !=
                curvatureLayer.SourceSignature
        )
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because Slope and Curvature analysis do not describe the same Terrain Authoring Height Preview."
            );

            TerrainAnalysisService
                .ReleaseLayer(
                    curvatureKey
                );

            return;
        }

        string settingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    surfaceSettings
                );

        string generationSignature =
            TerrainGenerationStateUtility
                .GetCurrentSurfaceMaskSignature(
                    worldSettings,
                    surfaceSettings
                );

        if (
            string.IsNullOrEmpty(
                settingsSignature
            )
            ||
            string.IsNullOrEmpty(
                generationSignature
            )
        )
        {
            Debug.LogError(
                "Cannot generate runtime surface masks because the deterministic surface signature could not be calculated."
            );

            TerrainAnalysisService
                .ReleaseLayer(
                    curvatureKey
                );

            return;
        }

        EnsureOutputFolders();

        TerrainSurfaceMaskManifest surfaceManifest =
            GetOrCreateManifest();

        if (surfaceManifest == null)
        {
            TerrainAnalysisService
                .ReleaseLayer(
                    curvatureKey
                );

            return;
        }

        surfaceManifest.isComplete =
            false;

        EditorUtility.SetDirty(
            surfaceManifest
        );

        AssetDatabase.SaveAssetIfDirty(
            surfaceManifest
        );

        activeBuild =
            new BuildState(
                worldSettings,
                heightManifest,
                surfaceManifest,
                surfaceSettings,
                slopeKey,
                curvatureKey,
                settingsSignature,
                generationSignature
            );

        activeBuild.Begin();
    }

    private static bool ValidateAnalysisLayer(
        TerrainAnalysisLayer layer,
        TerrainHeightmapManifest heightManifest,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            layer == null
            ||
            !layer.IsReady
            ||
            layer.Texture == null
            ||
            !layer.Texture.IsCreated()
        )
        {
            errorMessage =
                layer != null
                    ? layer.ErrorMessage
                    : "Analysis layer is null.";

            return false;
        }

        if (
            layer.CacheOriginTile !=
                Vector2Int.zero
            ||
            layer.CacheSize.x !=
                heightManifest.heightTileGridWidth
            ||
            layer.CacheSize.y !=
                heightManifest.heightTileGridHeight
            ||
            layer.SamplesPerSide !=
                heightManifest.heightTileSamplesPerSide
            ||
            !Mathf.Approximately(
                layer.SampleSpacing,
                heightManifest.HeightSampleSpacing
            )
            ||
            !Mathf.Approximately(
                layer.WorldSizeXZ.x,
                heightManifest.WorldSizeX
            )
            ||
            !Mathf.Approximately(
                layer.WorldSizeXZ.y,
                heightManifest.WorldSizeZ
            )
        )
        {
            errorMessage =
                "Analysis layout:\n" +
                $"  Origin: {layer.CacheOriginTile}\n" +
                $"  Size: {layer.CacheSize}\n" +
                $"  Samples: {layer.SamplesPerSide}\n" +
                $"  Spacing: {layer.SampleSpacing:R}\n\n" +
                "Runtime height layout:\n" +
                $"  Size: {heightManifest.heightTileGridWidth} x {heightManifest.heightTileGridHeight}\n" +
                $"  Samples: {heightManifest.heightTileSamplesPerSide}\n" +
                $"  Spacing: {heightManifest.HeightSampleSpacing:R}";

            return false;
        }

        return true;
    }

    private static void EnsureOutputFolders()
    {
        EnsureFolder(
            WorldMeshesPaths.Generated,
            "SurfaceMasks"
        );

        EnsureFolder(
            WorldMeshesPaths.GeneratedSurfaceMasks,
            "Tiles"
        );
    }

    private static void EnsureFolder(
        string parent,
        string child
    )
    {
        string path =
            parent +
            "/" +
            child;

        if (
            AssetDatabase.IsValidFolder(
                path
            )
        )
        {
            return;
        }

        if (
            !AssetDatabase.IsValidFolder(
                parent
            )
        )
        {
            Debug.LogError(
                "Could not create generated surface-mask folder because its parent does not exist:\n" +
                parent
            );

            return;
        }

        AssetDatabase.CreateFolder(
            parent,
            child
        );
    }

    private static TerrainSurfaceMaskManifest GetOrCreateManifest()
    {
        TerrainSurfaceMaskManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskManifestPath
                );

        if (manifest != null)
        {
            return manifest;
        }

        manifest =
            ScriptableObject
                .CreateInstance<TerrainSurfaceMaskManifest>();

        if (manifest == null)
        {
            Debug.LogError(
                "Could not create TerrainSurfaceMaskManifest."
            );

            return null;
        }

        AssetDatabase.CreateAsset(
            manifest,
            TerrainRuntimeSurfaceMaskAssetUtility
                .SurfaceMaskManifestPath
        );

        return manifest;
    }

    private static void CompleteBuild(
        BuildState state
    )
    {
        if (
            activeBuild != state
            ||
            state == null
        )
        {
            return;
        }

        if (
            state.worldSettings.heightmapGenerationRevision !=
                state.sourceHeightmapGenerationRevision
            ||
            state.worldSettings.lastGeneratedHeightSignature !=
                state.sourceHeightmapSignature
            ||
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    state.worldSettings
                )
                !=
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            AbortBuild(
                state,
                "Runtime heightmap state changed while surface-mask generation was running. Compile/validate heightmaps and run the surface bake again."
            );

            return;
        }

        string currentSettingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    state.surfaceSettings
                );

        if (
            currentSettingsSignature !=
                state.settingsSignature
        )
        {
            AbortBuild(
                state,
                "TerrainSurfaceSettings changed while surface-mask generation was running. Run the surface bake again."
            );

            return;
        }

        TerrainSurfaceMaskManifest manifest =
            state.surfaceManifest;

        TerrainHeightmapManifest heightManifest =
            state.heightManifest;

        manifest.compilerVersion =
            TerrainSurfaceMaskManifest
                .CurrentCompilerVersion;

        manifest.channelLayoutVersion =
            TerrainSurfaceMaskManifest
                .CurrentChannelLayoutVersion;

        manifest.sourceHeightmapGenerationRevision =
            state.worldSettings
                .heightmapGenerationRevision;

        manifest.sourceHeightmapSignature =
            state.worldSettings
                .lastGeneratedHeightSignature;

        manifest.sourceAuthoringSignature =
            heightManifest
                .sourceAuthoringSignature;

        manifest.sourceAuthoringContentHash =
            heightManifest
                .sourceAuthoringContentHash;

        manifest.surfaceSettingsSignature =
            state.settingsSignature;

        manifest.surfaceGenerationSignature =
            state.generationSignature;

        manifest.tileGridWidth =
            heightManifest
                .heightTileGridWidth;

        manifest.tileGridHeight =
            heightManifest
                .heightTileGridHeight;

        manifest.tileWorldSize =
            heightManifest
                .heightTileWorldSize;

        manifest.samplesPerSide =
            heightManifest
                .heightTileSamplesPerSide;

        manifest.sampleSpacing =
            heightManifest
                .HeightSampleSpacing;

        manifest.worldSizeXZ =
            heightManifest
                .WorldSizeXZ;

        RemoveObsoleteTiles(
            manifest
        );

        if (
            !TerrainGenerationStateUtility
                .MarkSurfaceMasksGenerated(
                    state.worldSettings,
                    state.generationSignature
                )
        )
        {
            AbortBuild(
                state,
                "Surface tiles were written, but generated surface state could not be recorded."
            );

            return;
        }

        manifest.surfaceMaskGenerationRevision =
            state.worldSettings
                .surfaceMaskGenerationRevision;

        manifest.isComplete =
            true;

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();

        TerrainAnalysisReadbackService
            .Clear();

        /*
         * Curvature scale is configuration-driven. Do not let repeated bakes
         * at different scales accumulate full persistent GPU analysis layers.
         * Editor Curvature/Scree visualizations use their own transient owner
         * slots, so the bake-owned persistent layer can be released here.
         */
        TerrainAnalysisService
            .ReleaseLayer(
                state.curvatureKey
            );

        activeBuild =
            null;

        Selection.activeObject =
            manifest;

        Debug.Log(
            "Runtime surface-mask generation complete.\n\n" +
            $"Surface Revision: {manifest.surfaceMaskGenerationRevision}\n" +
            $"Source Height Revision: {manifest.sourceHeightmapGenerationRevision}\n" +
            $"Tile Grid: {manifest.tileGridWidth} x {manifest.tileGridHeight}\n" +
            $"Samples Per Tile: {manifest.samplesPerSide} x {manifest.samplesPerSide}\n" +
            $"Format: {TextureFormat.R8}\n" +
            $"Channel Layout: R = Scree Suitability\n\n" +
            $"Surface Settings Signature: {manifest.surfaceSettingsSignature}\n" +
            $"Generation Signature: {manifest.surfaceGenerationSignature}\n\n" +
            $"Saved To:\n{TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskTileFolder}"
        );
    }

    private static void AbortBuild(
        BuildState state,
        string errorMessage
    )
    {
        if (
            state != null
            &&
            state.surfaceManifest != null
        )
        {
            state.surfaceManifest.isComplete =
                false;

            EditorUtility.SetDirty(
                state.surfaceManifest
            );

            AssetDatabase.SaveAssetIfDirty(
                state.surfaceManifest
            );
        }

        EditorUtility.ClearProgressBar();

        TerrainAnalysisReadbackService
            .Clear();

        if (state != null)
        {
            TerrainAnalysisService
                .ReleaseLayer(
                    state.curvatureKey
                );
        }

        activeBuild =
            null;

        Debug.LogError(
            "Runtime surface-mask generation failed.\n\n" +
            errorMessage +
            "\n\nThe surface-mask manifest remains incomplete."
        );
    }

    private static void RemoveObsoleteTiles(
        TerrainSurfaceMaskManifest manifest
    )
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "t:Texture2D",
                new[]
                {
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskTileFolder
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
                !TerrainRuntimeSurfaceMaskAssetUtility
                    .TryGetSurfaceTileCoordinates(
                        path,
                        out int tileX,
                        out int tileZ
                    )
            )
            {
                continue;
            }

            if (
                manifest.IsTileCoordinateValid(
                    tileX,
                    tileZ
                )
            )
            {
                continue;
            }

            AssetDatabase.DeleteAsset(
                path
            );
        }
    }

    private static bool WriteSurfaceTile(
        Vector2Int coordinate,
        int samplesPerSide,
        byte[] values,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            values == null
            ||
            values.Length !=
                samplesPerSide *
                samplesPerSide
        )
        {
            errorMessage =
                $"Surface tile ({coordinate.x}, {coordinate.y}) received an invalid sample buffer.";

            return false;
        }

        string path =
            TerrainRuntimeSurfaceMaskAssetUtility
                .GetSurfaceTilePath(
                    coordinate.x,
                    coordinate.y
                );

        Texture2D texture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    path
                );

        if (
            texture != null
            &&
            (
                texture.width !=
                    samplesPerSide
                ||
                texture.height !=
                    samplesPerSide
                ||
                texture.format !=
                    TextureFormat.R8
            )
        )
        {
            AssetDatabase.DeleteAsset(
                path
            );

            texture =
                null;
        }

        if (texture == null)
        {
            texture =
                new Texture2D(
                    samplesPerSide,
                    samplesPerSide,
                    TextureFormat.R8,
                    false,
                    true
                );

            texture.name =
                TerrainRuntimeSurfaceMaskAssetUtility
                    .GetSurfaceTileName(
                        coordinate.x,
                        coordinate.y
                    );

            texture.wrapMode =
                TextureWrapMode.Clamp;

            texture.filterMode =
                FilterMode.Bilinear;

            texture.anisoLevel =
                0;

            AssetDatabase.CreateAsset(
                texture,
                path
            );
        }

        try
        {
            texture.wrapMode =
                TextureWrapMode.Clamp;

            texture.filterMode =
                FilterMode.Bilinear;

            texture.anisoLevel =
                0;

            texture.SetPixelData<byte>(
                values,
                0
            );

            texture.Apply(
                false,
                false
            );

            EditorUtility.SetDirty(
                texture
            );
        }
        catch (
            System.Exception exception
        )
        {
            errorMessage =
                $"Could not write surface tile ({coordinate.x}, {coordinate.y}).\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }

    // =====================================================
    // BUILD STATE
    // =====================================================

    private sealed class BuildState
    {
        public readonly WorldSettings worldSettings;
        public readonly TerrainHeightmapManifest heightManifest;
        public readonly TerrainSurfaceMaskManifest surfaceManifest;
        public readonly TerrainSurfaceSettings surfaceSettings;

        private readonly TerrainScreeSuitabilitySettings screeSettings;

        private readonly TerrainAnalysisKey slopeKey;
        public readonly TerrainAnalysisKey curvatureKey;

        public readonly string settingsSignature;
        public readonly string generationSignature;

        public readonly int sourceHeightmapGenerationRevision;
        public readonly string sourceHeightmapSignature;

        private readonly List<Vector2Int> coordinates =
            new List<Vector2Int>();

        private int nextTileIndex;

        private List<Vector2Int> currentBatch;

        private IReadOnlyList<TerrainAnalysisTileData>
            slopeBatch;

        private IReadOnlyList<TerrainAnalysisTileData>
            curvatureBatch;

        private bool waitingForSlope;
        private bool waitingForCurvature;

        private string batchError;

        public BuildState(
            WorldSettings worldSettings,
            TerrainHeightmapManifest heightManifest,
            TerrainSurfaceMaskManifest surfaceManifest,
            TerrainSurfaceSettings surfaceSettings,
            TerrainAnalysisKey slopeKey,
            TerrainAnalysisKey curvatureKey,
            string settingsSignature,
            string generationSignature
        )
        {
            this.worldSettings =
                worldSettings;

            this.heightManifest =
                heightManifest;

            this.surfaceManifest =
                surfaceManifest;

            this.surfaceSettings =
                surfaceSettings;

            screeSettings =
                TerrainScreeSuitabilityUtility
                    .Capture(
                        surfaceSettings.Scree
                    );

            this.slopeKey =
                slopeKey;

            this.curvatureKey =
                curvatureKey;

            this.settingsSignature =
                settingsSignature;

            this.generationSignature =
                generationSignature;

            sourceHeightmapGenerationRevision =
                worldSettings.heightmapGenerationRevision;

            sourceHeightmapSignature =
                worldSettings.lastGeneratedHeightSignature;

            for (
                int tileZ = 0;
                tileZ <
                    heightManifest.heightTileGridHeight;
                tileZ++
            )
            {
                for (
                    int tileX = 0;
                    tileX <
                        heightManifest.heightTileGridWidth;
                    tileX++
                )
                {
                    coordinates.Add(
                        new Vector2Int(
                            tileX,
                            tileZ
                        )
                    );
                }
            }
        }

        public void Begin()
        {
            nextTileIndex =
                0;

            EditorApplication.delayCall +=
                ProcessNextBatch;
        }

        private void ProcessNextBatch()
        {
            if (activeBuild != this)
            {
                return;
            }

            if (
                nextTileIndex >=
                coordinates.Count
            )
            {
                CompleteBuild(
                    this
                );

                return;
            }

            int count =
                Mathf.Min(
                    ReadbackBatchTileCount,
                    coordinates.Count -
                        nextTileIndex
                );

            currentBatch =
                coordinates.GetRange(
                    nextTileIndex,
                    count
                );

            float progress =
                coordinates.Count > 0
                    ? (float)nextTileIndex /
                      coordinates.Count
                    : 1f;

            bool cancelled =
                EditorUtility
                    .DisplayCancelableProgressBar(
                        "Baking Runtime Surface Masks",
                        $"Tiles {nextTileIndex + 1} - {nextTileIndex + count} / {coordinates.Count}",
                        progress
                    );

            if (cancelled)
            {
                AbortBuild(
                    this,
                    "Generation was cancelled by the user."
                );

                return;
            }

            slopeBatch =
                null;

            curvatureBatch =
                null;

            batchError =
                "";

            waitingForSlope =
                true;

            waitingForCurvature =
                true;

            TerrainAnalysisReadbackService
                .RequestTiles(
                    slopeKey,
                    currentBatch,
                    OnSlopeReadback
                );

            TerrainAnalysisReadbackService
                .RequestTiles(
                    curvatureKey,
                    currentBatch,
                    OnCurvatureReadback
                );
        }

        private void OnSlopeReadback(
            IReadOnlyList<TerrainAnalysisTileData> data,
            string errorMessage
        )
        {
            slopeBatch =
                data;

            waitingForSlope =
                false;

            CaptureError(
                errorMessage
            );

            TryFinishCurrentBatch();
        }

        private void OnCurvatureReadback(
            IReadOnlyList<TerrainAnalysisTileData> data,
            string errorMessage
        )
        {
            curvatureBatch =
                data;

            waitingForCurvature =
                false;

            CaptureError(
                errorMessage
            );

            TryFinishCurrentBatch();
        }

        private void CaptureError(
            string errorMessage
        )
        {
            if (
                string.IsNullOrEmpty(
                    batchError
                )
                &&
                !string.IsNullOrEmpty(
                    errorMessage
                )
            )
            {
                batchError =
                    errorMessage;
            }
        }

        private void TryFinishCurrentBatch()
        {
            if (
                activeBuild != this
                ||
                waitingForSlope
                ||
                waitingForCurvature
            )
            {
                return;
            }

            if (
                !string.IsNullOrEmpty(
                    batchError
                )
            )
            {
                AbortBuild(
                    this,
                    batchError
                );

                return;
            }

            Dictionary<Vector2Int, TerrainAnalysisTileData>
                slopeByTile =
                    BuildTileDictionary(
                        slopeBatch
                    );

            Dictionary<Vector2Int, TerrainAnalysisTileData>
                curvatureByTile =
                    BuildTileDictionary(
                        curvatureBatch
                    );

            foreach (
                Vector2Int coordinate
                in currentBatch
            )
            {
                if (
                    !slopeByTile.TryGetValue(
                        coordinate,
                        out TerrainAnalysisTileData slopeData
                    )
                    ||
                    !curvatureByTile.TryGetValue(
                        coordinate,
                        out TerrainAnalysisTileData curvatureData
                    )
                )
                {
                    AbortBuild(
                        this,
                        $"Analysis readback did not return both Slope and Curvature for tile ({coordinate.x}, {coordinate.y})."
                    );

                    return;
                }

                if (
                    slopeData.SamplesPerSide !=
                        heightManifest.heightTileSamplesPerSide
                    ||
                    curvatureData.SamplesPerSide !=
                        slopeData.SamplesPerSide
                    ||
                    !Mathf.Approximately(
                        slopeData.SampleSpacing,
                        curvatureData.SampleSpacing
                    )
                )
                {
                    AbortBuild(
                        this,
                        $"Analysis tile layout mismatch at ({coordinate.x}, {coordinate.y})."
                    );

                    return;
                }

                float[] slopeValues =
                    slopeData.CopyValues();

                float[] curvatureValues =
                    curvatureData.CopyValues();

                int samplesPerSide =
                    slopeData.SamplesPerSide;

                int expectedCount =
                    samplesPerSide *
                    samplesPerSide;

                if (
                    slopeValues.Length !=
                        expectedCount
                    ||
                    curvatureValues.Length !=
                        expectedCount
                )
                {
                    AbortBuild(
                        this,
                        $"Analysis tile ({coordinate.x}, {coordinate.y}) contains an unexpected sample count."
                    );

                    return;
                }

                byte[] output =
                    new byte[
                        expectedCount
                    ];

                for (
                    int sampleZ = 0;
                    sampleZ < samplesPerSide;
                    sampleZ++
                )
                {
                    for (
                        int sampleX = 0;
                        sampleX < samplesPerSide;
                        sampleX++
                    )
                    {
                        int index =
                            sampleX +
                            sampleZ *
                            samplesPerSide;

                        Vector2 worldXZ =
                            slopeData.WorldOriginXZ +
                            new Vector2(
                                sampleX *
                                    slopeData.SampleSpacing,
                                sampleZ *
                                    slopeData.SampleSpacing
                            );

                        float suitability =
                            TerrainScreeSuitabilityUtility
                                .Evaluate(
                                    screeSettings,
                                    worldXZ,
                                    slopeValues[index],
                                    curvatureValues[index]
                                );

                        output[index] =
                            (byte)
                            Mathf.Clamp(
                                Mathf.RoundToInt(
                                    suitability *
                                    255f
                                ),
                                0,
                                255
                            );
                    }
                }

                if (
                    !WriteSurfaceTile(
                        coordinate,
                        samplesPerSide,
                        output,
                        out string writeError
                    )
                )
                {
                    AbortBuild(
                        this,
                        writeError
                    );

                    return;
                }
            }

            nextTileIndex +=
                currentBatch.Count;

            /*
             * Keep CPU readback memory bounded. The next batch will request
             * only the next group of whole analysis tiles.
             */
            TerrainAnalysisReadbackService
                .Clear();

            EditorApplication.delayCall +=
                ProcessNextBatch;
        }

        private static Dictionary<
            Vector2Int,
            TerrainAnalysisTileData
        > BuildTileDictionary(
            IReadOnlyList<TerrainAnalysisTileData> data
        )
        {
            Dictionary<
                Vector2Int,
                TerrainAnalysisTileData
            > result =
                new Dictionary<
                    Vector2Int,
                    TerrainAnalysisTileData
                >();

            if (data == null)
            {
                return result;
            }

            for (
                int index = 0;
                index < data.Count;
                index++
            )
            {
                TerrainAnalysisTileData tile =
                    data[index];

                if (tile == null)
                {
                    continue;
                }

                result[
                    tile.TileCoordinate
                ] =
                    tile;
            }

            return result;
        }
    }
}
