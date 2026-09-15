using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Asynchronous compiler for final runtime surface suitability.
 *
 * Package 06 keeps two truths deliberately separate:
 *
 * 1. Physical surface Texture2D assets may advance tile-by-tile.
 * 2. WorldSettings + TerrainSurfaceMaskManifest advance only after every tile
 *    required for the captured target has caught up.
 *
 * This lets cancelled/failed Incremental work resume without redoing already
 * successful tiles while preserving a valid last-complete manifest identity.
 */
public static class TerrainSurfaceMaskCompiler
{
    private const int ReadbackBatchTileCount = 8;

    private static BuildState activeBuild;

    /*
     * Keep at most one bake-owned persistent Curvature layer alive between
     * surface bakes. This lets the existing Terrain Analysis invalidation path
     * update that layer incrementally after local height edits. If curvature
     * scale changes, release the previous key before retaining the new one so
     * repeated global settings changes cannot accumulate full GPU layers.
     */
    private static bool hasRetainedCurvatureKey;
    private static TerrainAnalysisKey retainedCurvatureKey;

    public static bool IsGenerating => activeBuild != null;

    private enum SurfaceTileWriteOutcome
    {
        Failed,
        Created,
        Updated,
        Recreated
    }

    private struct PersistentDirtyTransition
    {
        public bool configurationBecameDirty;
        public bool contentBecameDirty;
    }

    // =====================================================
    // EXPLICIT FULL REBUILD
    // =====================================================

    public static bool RebuildAllSurfaceMasks(
        WorldSettings worldSettings,
        Action<TerrainSurfaceMaskGenerationResult> onCompleted
    )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        return TryStartBuild(
            worldSettings,
            TerrainRuntimeBakeWorkMode.Full,
            null,
            snapshot,
            null,
            onCompleted,
            true
        );
    }

    // =====================================================
    // PLANNED API
    // =====================================================

    /*
     * The bool indicates that an asynchronous BuildState was started.
     * Immediate terminal outcomes invoke the callback synchronously and return
     * false.
     */
    public static bool GeneratePlannedSurfaceMasks(
        WorldSettings worldSettings,
        TerrainRuntimeBakePlan plan,
        Action<TerrainSurfaceMaskGenerationResult> onCompleted
    )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        if (activeBuild != null)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    plan != null
                        ? plan.SurfaceWorkMode
                        : TerrainRuntimeBakeWorkMode.None,
                    worldSettings,
                    "Runtime surface-mask generation is already in progress."
                )
            );

            return false;
        }

        if (plan == null)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Failed,
                    TerrainRuntimeBakeWorkMode.None,
                    worldSettings,
                    "Surface bake plan is null."
                )
            );

            return false;
        }

        if (plan.IsBlocked)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    plan.SurfaceWorkMode,
                    worldSettings,
                    string.IsNullOrEmpty(plan.BlockReason)
                        ? "The runtime bake plan is blocked."
                        : plan.BlockReason
                )
            );

            return false;
        }

        if (snapshot.StateRevision != plan.SourceStateRevision)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    plan.SurfaceWorkMode,
                    worldSettings,
                    "Persistent runtime bake state changed after the surface plan was built."
                )
            );

            return false;
        }

        if (plan.SurfaceWorkMode == TerrainRuntimeBakeWorkMode.None)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.NoWork,
                    TerrainRuntimeBakeWorkMode.None,
                    worldSettings,
                    "",
                    "No runtime surface-mask generation work is required."
                )
            );

            return false;
        }

        if (worldSettings == null)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    plan.SurfaceWorkMode,
                    worldSettings,
                    "WorldSettings is unavailable."
                )
            );

            return false;
        }

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        string currentSurfaceSettingsSignature =
            TerrainSurfaceSignatureUtility.GetSettingsSignature(
                surfaceSettings
            );

        if (
            surfaceSettings == null
            ||
            string.IsNullOrEmpty(currentSurfaceSettingsSignature)
            ||
            !string.Equals(
                currentSurfaceSettingsSignature,
                plan.CurrentSurfaceSettingsSignature,
                StringComparison.Ordinal
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    plan.SurfaceWorkMode,
                    worldSettings,
                    "TerrainSurfaceSettings changed or became unavailable after the runtime bake plan was built."
                )
            );

            return false;
        }

        List<Vector2Int> requested =
            plan.SurfaceWorkMode == TerrainRuntimeBakeWorkMode.Full
                ? null
                : CopySortedUniqueCoordinates(plan.SurfaceTiles);

        return TryStartBuild(
            worldSettings,
            plan.SurfaceWorkMode,
            requested,
            snapshot,
            plan,
            onCompleted,
            false
        );
    }

    // =====================================================
    // START / PREFLIGHT
    // =====================================================

    private static bool TryStartBuild(
        WorldSettings worldSettings,
        TerrainRuntimeBakeWorkMode workMode,
        List<Vector2Int> requestedTiles,
        TerrainRuntimeBakeStateSnapshot startSnapshot,
        TerrainRuntimeBakePlan sourcePlan,
        Action<TerrainSurfaceMaskGenerationResult> onCompleted,
        bool allowCreateSurfaceSettings
    )
    {
        if (activeBuild != null)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Runtime surface-mask generation is already in progress."
                )
            );

            return false;
        }

        if (worldSettings == null)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "WorldSettings is unavailable."
                )
            );

            return false;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Runtime surface masks must be generated outside Play Mode."
                )
            );

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetHeightmapStatus(worldSettings)
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Runtime heightmaps are not current. Compile planned runtime height work first."
                )
            );

            return false;
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        if (heightManifest == null || !heightManifest.isComplete)
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "The runtime heightmap manifest is missing or incomplete."
                )
            );

            return false;
        }

        TerrainSurfaceSettings surfaceSettings;

        if (allowCreateSurfaceSettings)
        {
            surfaceSettings =
                TerrainSurfaceSettingsEditorUtility.LoadOrCreate(
                    out string settingsError
                );

            if (surfaceSettings == null)
            {
                CompleteImmediately(
                    onCompleted,
                    CreateSimpleResult(
                        TerrainSurfaceMaskGenerationOutcome.Blocked,
                        workMode,
                        worldSettings,
                        "TerrainSurfaceSettings is unavailable.\n\n" +
                        settingsError
                    )
                );

                return false;
            }
        }
        else
        {
            surfaceSettings =
                AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                    WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
                );

            if (surfaceSettings == null)
            {
                CompleteImmediately(
                    onCompleted,
                    CreateSimpleResult(
                        TerrainSurfaceMaskGenerationOutcome.Blocked,
                        workMode,
                        worldSettings,
                        "TerrainSurfaceSettings is unavailable."
                    )
                );

                return false;
            }
        }

        string settingsSignature =
            TerrainSurfaceSignatureUtility.GetSettingsSignature(
                surfaceSettings
            );

        string generationSignature =
            TerrainGenerationStateUtility.GetCurrentSurfaceMaskSignature(
                worldSettings,
                surfaceSettings
            );

        if (
            string.IsNullOrEmpty(settingsSignature)
            ||
            string.IsNullOrEmpty(generationSignature)
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Failed,
                    workMode,
                    worldSettings,
                    "The deterministic surface settings/generation signature could not be calculated."
                )
            );

            return false;
        }

        if (
            sourcePlan != null
            &&
            (
                startSnapshot.StateRevision != sourcePlan.SourceStateRevision
                ||
                !string.Equals(
                    settingsSignature,
                    sourcePlan.CurrentSurfaceSettingsSignature,
                    StringComparison.Ordinal
                )
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    workMode,
                    worldSettings,
                    "The surface plan became stale during compiler preflight."
                )
            );

            return false;
        }

        if (
            !TerrainAuthoringPreviewService.CacheReady
            ||
            TerrainAuthoringPreviewService.Status != TerrainAuthoringPreviewStatus.Ready
        )
        {
            TerrainAuthoringPreviewService.RequestRefresh();

            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Runtime surface-mask generation needs the ready Terrain Authoring Height Preview. A preview refresh was requested; run the surface bake again once Height Preview reports Ready."
                )
            );

            return false;
        }

        /*
         * Terrain Analysis is sourced from the live authoring preview while
         * runtime surface output targets the generated runtime height dataset.
         * Both must represent the same authoring identity.
         */
        if (
            string.IsNullOrEmpty(heightManifest.sourceAuthoringSignature)
            ||
            !string.Equals(
                TerrainAuthoringPreviewService.SourceOverallAuthoringSignature,
                heightManifest.sourceAuthoringSignature,
                StringComparison.Ordinal
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "The Terrain Authoring Height Preview does not represent the same authoring signature as the current runtime height dataset."
                )
            );

            return false;
        }

        ScreeSettings scree = surfaceSettings.Scree;

        TerrainAnalysisKey slopeKey = TerrainAnalysisKey.Slope;
        TerrainAnalysisKey curvatureKey =
            TerrainAnalysisKey.Curvature(scree.curvatureScale);

        TerrainAnalysisLayer slopeLayer =
            TerrainAnalysisService.RequestLayer(slopeKey);

        RetainCurvatureLayer(curvatureKey);

        TerrainAnalysisLayer curvatureLayer =
            TerrainAnalysisService.RequestLayer(curvatureKey);

        if (
            !ValidateAnalysisLayer(
                slopeLayer,
                heightManifest,
                out string slopeError
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Slope analysis is unavailable or does not match the runtime height-tile layout.\n\n" +
                    slopeError
                )
            );

            return false;
        }

        if (
            !ValidateAnalysisLayer(
                curvatureLayer,
                heightManifest,
                out string curvatureError
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Curvature analysis is unavailable or does not match the runtime height-tile layout.\n\n" +
                    curvatureError
                )
            );

            return false;
        }

        if (
            slopeLayer.CacheOriginTile != curvatureLayer.CacheOriginTile
            ||
            slopeLayer.CacheSize != curvatureLayer.CacheSize
            ||
            slopeLayer.SamplesPerSide != curvatureLayer.SamplesPerSide
            ||
            !Mathf.Approximately(
                slopeLayer.SampleSpacing,
                curvatureLayer.SampleSpacing
            )
            ||
            slopeLayer.SourceHeightCacheInstanceId
                != curvatureLayer.SourceHeightCacheInstanceId
            ||
            !string.Equals(
                slopeLayer.SourceSignature,
                curvatureLayer.SourceSignature,
                StringComparison.Ordinal
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Slope and Curvature analysis do not describe the same Terrain Authoring Height Preview."
                )
            );

            return false;
        }

        if (
            !TerrainAuthoringPreviewService.TryGetTerrainAnalysisSource(
                out RenderTexture currentAnalysisHeightCache,
                out _,
                out _,
                out _,
                out _,
                out _,
                out string currentAnalysisSourceSignature
            )
            ||
            currentAnalysisHeightCache == null
            ||
            slopeLayer.SourceHeightCacheInstanceId
                != currentAnalysisHeightCache.GetInstanceID()
            ||
            curvatureLayer.SourceHeightCacheInstanceId
                != currentAnalysisHeightCache.GetInstanceID()
            ||
            !string.Equals(
                slopeLayer.SourceSignature,
                currentAnalysisSourceSignature,
                StringComparison.Ordinal
            )
            ||
            !string.Equals(
                curvatureLayer.SourceSignature,
                currentAnalysisSourceSignature,
                StringComparison.Ordinal
            )
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.Blocked,
                    workMode,
                    worldSettings,
                    "Terrain Analysis does not represent the current Terrain Authoring Height Preview."
                )
            );

            return false;
        }

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (workMode == TerrainRuntimeBakeWorkMode.Incremental)
        {
            if (
                !IsIncrementalManifestCompatible(
                    surfaceManifest,
                    heightManifest,
                    settingsSignature
                )
            )
            {
                    CompleteImmediately(
                    onCompleted,
                    CreateSimpleResult(
                        TerrainSurfaceMaskGenerationOutcome.StalePlan,
                        workMode,
                        worldSettings,
                        "Incremental surface generation requires a previous complete, compiler-compatible surface manifest with the current tile layout and surface settings."
                    )
                );

                return false;
            }

            if (
                !AssetDatabase.IsValidFolder(
                    TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskTileFolder
                )
            )
            {
                    CompleteImmediately(
                    onCompleted,
                    CreateSimpleResult(
                        TerrainSurfaceMaskGenerationOutcome.StalePlan,
                        workMode,
                        worldSettings,
                        "Incremental surface generation cannot proceed because the generated surface tile folder is missing."
                    )
                );

                return false;
            }

            requestedTiles = CopySortedUniqueCoordinates(requestedTiles);

            if (requestedTiles.Count == 0)
            {
                    CompleteImmediately(
                    onCompleted,
                    CreateSimpleResult(
                        TerrainSurfaceMaskGenerationOutcome.StalePlan,
                        workMode,
                        worldSettings,
                        "Incremental surface work contains no tile coordinates."
                    )
                );

                return false;
            }
        }
        else if (workMode == TerrainRuntimeBakeWorkMode.Full)
        {
            EnsureOutputFolders();

            surfaceManifest = GetOrCreateManifest();

            if (surfaceManifest == null)
            {
                    CompleteImmediately(
                    onCompleted,
                    CreateSimpleResult(
                        TerrainSurfaceMaskGenerationOutcome.Failed,
                        workMode,
                        worldSettings,
                        "TerrainSurfaceMaskManifest could not be created."
                    )
                );

                return false;
            }

            requestedTiles = CollectAllSurfaceTiles(heightManifest);

            surfaceManifest.isComplete = false;

            EditorUtility.SetDirty(surfaceManifest);
            using (WorldMeshesProfiler.RuntimeBakeSurfaceSaveManifest.Auto())
            using (WorldMeshesProfiler.AssetDatabaseSaveAssetIfDirty.Auto())
            {
                AssetDatabase.SaveAssetIfDirty(surfaceManifest);
            }
        }
        else
        {
            CompleteImmediately(
                onCompleted,
                CreateSimpleResult(
                    TerrainSurfaceMaskGenerationOutcome.NoWork,
                    TerrainRuntimeBakeWorkMode.None,
                    worldSettings,
                    "",
                    "No runtime surface-mask generation work is required."
                )
            );

            return false;
        }

        foreach (Vector2Int coordinate in requestedTiles)
        {
            if (
                !TerrainRuntimeBakeDependencyUtility.IsHeightTileCoordinateValid(
                    worldSettings,
                    coordinate
                )
            )
            {
                    CompleteImmediately(
                    onCompleted,
                    CreateResult(
                        TerrainSurfaceMaskGenerationOutcome.StalePlan,
                        workMode,
                        requestedTiles,
                        null,
                        null,
                        requestedTiles,
                        worldSettings,
                        0,
                        0,
                        0,
                        false,
                        false,
                        false,
                        "Surface plan contains an invalid tile coordinate: (" +
                        coordinate.x +
                        ", " +
                        coordinate.y +
                        ").",
                        ""
                    )
                );

                return false;
            }
        }

        if (
            sourcePlan != null
            &&
            TerrainRuntimeBakeStateService.GetSummary().StateRevision
                != sourcePlan.SourceStateRevision
        )
        {
            CompleteImmediately(
                onCompleted,
                CreateResult(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    workMode,
                    requestedTiles,
                    null,
                    null,
                    requestedTiles,
                    worldSettings,
                    0,
                    0,
                    0,
                    false,
                    false,
                    false,
                    "Persistent runtime bake state changed during surface compiler preflight.",
                    ""
                )
            );

            return false;
        }

        activeBuild =
            new BuildState(
                worldSettings,
                heightManifest,
                surfaceManifest,
                surfaceSettings,
                slopeKey,
                curvatureKey,
                slopeLayer,
                curvatureLayer,
                settingsSignature,
                generationSignature,
                workMode,
                requestedTiles,
                startSnapshot.StateRevision,
                onCompleted
            );

        activeBuild.Begin();

        return true;
    }

    // =====================================================
    // ANALYSIS VALIDATION
    // =====================================================

    private static bool ValidateAnalysisLayer(
        TerrainAnalysisLayer layer,
        TerrainHeightmapManifest heightManifest,
        out string errorMessage
    )
    {
        errorMessage = "";

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
            layer.CacheOriginTile != Vector2Int.zero
            ||
            layer.CacheSize.x != heightManifest.heightTileGridWidth
            ||
            layer.CacheSize.y != heightManifest.heightTileGridHeight
            ||
            layer.SamplesPerSide != heightManifest.heightTileSamplesPerSide
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
                "  Origin: " + layer.CacheOriginTile + "\n" +
                "  Size: " + layer.CacheSize + "\n" +
                "  Samples: " + layer.SamplesPerSide + "\n" +
                "  Spacing: " + layer.SampleSpacing.ToString("R") + "\n\n" +
                "Runtime height layout:\n" +
                "  Size: " +
                heightManifest.heightTileGridWidth +
                " x " +
                heightManifest.heightTileGridHeight +
                "\n" +
                "  Samples: " +
                heightManifest.heightTileSamplesPerSide +
                "\n" +
                "  Spacing: " +
                heightManifest.HeightSampleSpacing.ToString("R");

            return false;
        }

        return true;
    }

    private static bool IsIncrementalManifestCompatible(
        TerrainSurfaceMaskManifest manifest,
        TerrainHeightmapManifest heightManifest,
        string settingsSignature
    )
    {
        return
            manifest != null
            &&
            manifest.isComplete
            &&
            manifest.compilerVersion
                == TerrainSurfaceMaskManifest.CurrentCompilerVersion
            &&
            manifest.channelLayoutVersion
                == TerrainSurfaceMaskManifest.CurrentChannelLayoutVersion
            &&
            manifest.MatchesHeightLayout(heightManifest)
            &&
            !string.IsNullOrEmpty(settingsSignature)
            &&
            string.Equals(
                manifest.surfaceSettingsSignature,
                settingsSignature,
                StringComparison.Ordinal
            );
    }

    private static void RetainCurvatureLayer(
        TerrainAnalysisKey key
    )
    {
        if (
            hasRetainedCurvatureKey
            &&
            !retainedCurvatureKey.Equals(key)
        )
        {
            TerrainAnalysisService.ReleaseLayer(
                retainedCurvatureKey
            );
        }

        retainedCurvatureKey = key;
        hasRetainedCurvatureKey = true;
    }

    // =====================================================
    // OUTPUT FOLDERS / MANIFEST
    // =====================================================

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
        string path = parent + "/" + child;

        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(parent))
        {
            Debug.LogError(
                "Could not create generated surface-mask folder because its parent does not exist:\n" +
                parent
            );

            return;
        }

        AssetDatabase.CreateFolder(parent, child);
    }

    private static TerrainSurfaceMaskManifest GetOrCreateManifest()
    {
        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (manifest != null)
        {
            return manifest;
        }

        manifest =
            ScriptableObject.CreateInstance<TerrainSurfaceMaskManifest>();

        if (manifest == null)
        {
            return null;
        }

        try
        {
            using (WorldMeshesProfiler.AssetDatabaseCreateAsset.Auto())
            {
                AssetDatabase.CreateAsset(
                    manifest,
                    TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Could not create TerrainSurfaceMaskManifest.\n\n" +
                exception.Message
            );

            UnityEngine.Object.DestroyImmediate(manifest);

            return null;
        }

        return manifest;
    }

    // =====================================================
    // SURFACE TILE WRITE
    // =====================================================

    private static SurfaceTileWriteOutcome WriteSurfaceTile(
        Vector2Int coordinate,
        int samplesPerSide,
        byte[] values,
        out bool topologyChanged,
        out string errorMessage
    )
    {
        topologyChanged = false;
        errorMessage = "";

        if (
            values == null
            ||
            values.Length != samplesPerSide * samplesPerSide
        )
        {
            errorMessage =
                "Surface tile (" +
                coordinate.x +
                ", " +
                coordinate.y +
                ") received an invalid sample buffer.";

            return SurfaceTileWriteOutcome.Failed;
        }

        string path =
            TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(
                coordinate.x,
                coordinate.y
            );

        Texture2D texture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        bool existedBefore = texture != null;
        bool createdNew = false;
        bool recreated = false;

        if (
            texture != null
            &&
            (
                texture.width != samplesPerSide
                ||
                texture.height != samplesPerSide
                ||
                texture.format != TextureFormat.R8
            )
        )
        {
            bool reinitialized = false;

            try
            {
                reinitialized =
                    texture.Reinitialize(
                        samplesPerSide,
                        samplesPerSide,
                        TextureFormat.R8,
                        false
                    );
            }
            catch (Exception)
            {
                reinitialized = false;
            }

            if (!reinitialized)
            {
                if (!AssetDatabase.DeleteAsset(path))
                {
                    errorMessage =
                        "Could not replace incompatible surface tile (" +
                        coordinate.x +
                        ", " +
                        coordinate.y +
                        ").";

                    return SurfaceTileWriteOutcome.Failed;
                }

                texture = null;
                recreated = true;
                topologyChanged = true;
            }
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

            if (texture == null)
            {
                errorMessage =
                    "Could not allocate surface tile (" +
                    coordinate.x +
                    ", " +
                    coordinate.y +
                    ").";

                return SurfaceTileWriteOutcome.Failed;
            }

            createdNew = !existedBefore;

            if (createdNew)
            {
                topologyChanged = true;
            }
        }

        try
        {
            texture.name =
                TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTileName(
                    coordinate.x,
                    coordinate.y
                );

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 0;

            texture.SetPixelData<byte>(values, 0);
            texture.Apply(false, false);

            if (AssetDatabase.GetAssetPath(texture) != path)
            {
                using (WorldMeshesProfiler.AssetDatabaseCreateAsset.Auto())
                {
                    AssetDatabase.CreateAsset(texture, path);
                }
            }

            EditorUtility.SetDirty(texture);
        }
        catch (Exception exception)
        {
            if (
                AssetDatabase.GetAssetPath(texture) == path
                &&
                (
                    createdNew
                    ||
                    recreated
                )
            )
            {
                AssetDatabase.DeleteAsset(path);
            }
            else if (AssetDatabase.GetAssetPath(texture) != path)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            errorMessage =
                "Could not write surface tile (" +
                coordinate.x +
                ", " +
                coordinate.y +
                ").\n\n" +
                exception.Message;

            return SurfaceTileWriteOutcome.Failed;
        }

        if (createdNew)
        {
            return SurfaceTileWriteOutcome.Created;
        }

        if (recreated)
        {
            return SurfaceTileWriteOutcome.Recreated;
        }

        return SurfaceTileWriteOutcome.Updated;
    }

    // =====================================================
    // FULL-ONLY OBSOLETE TILE CLEANUP
    // =====================================================

    private static bool TryRemoveObsoleteTiles(
        TerrainHeightmapManifest heightManifest,
        out int removedCount,
        out string errorMessage
    )
    {
        removedCount = 0;
        errorMessage = "";

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Texture2D",
                new[]
                {
                    TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskTileFolder
                }
            );

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (
                !TerrainRuntimeSurfaceMaskAssetUtility.TryGetSurfaceTileCoordinates(
                    path,
                    out int tileX,
                    out int tileZ
                )
            )
            {
                continue;
            }

            bool valid =
                tileX >= 0
                &&
                tileZ >= 0
                &&
                tileX < heightManifest.heightTileGridWidth
                &&
                tileZ < heightManifest.heightTileGridHeight;

            if (valid)
            {
                continue;
            }

            if (!AssetDatabase.DeleteAsset(path))
            {
                errorMessage =
                    "Could not remove obsolete runtime surface tile:\n" +
                    path;

                return false;
            }

            removedCount++;
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

        private readonly TerrainAnalysisLayer slopeLayer;
        private readonly TerrainAnalysisLayer curvatureLayer;

        public readonly string settingsSignature;
        public readonly string generationSignature;

        public readonly TerrainRuntimeBakeWorkMode workMode;
        public readonly long sourceBakeStateRevision;

        public readonly int sourceHeightmapGenerationRevision;
        public readonly string sourceHeightmapSignature;
        public readonly string sourceAuthoringSignature;

        public readonly string analysisSourceSignature;
        public readonly int analysisSourceHeightCacheInstanceId;

        public readonly int surfaceGenerationRevisionBefore;
        public readonly int sourceSurfaceHeightRevisionBefore;

        private readonly Action<TerrainSurfaceMaskGenerationResult>
            completionCallback;

        private readonly List<Vector2Int> coordinates;

        private readonly List<Vector2Int> succeeded =
            new List<Vector2Int>();

        private readonly List<Vector2Int> failed =
            new List<Vector2Int>();

        private int createdCount;
        private int updatedCount;
        private int removedCount;

        private bool addressablesConfigurationRequired;
        private bool anyPhysicalContentChange;

        private int nextTileIndex;

        private List<Vector2Int> currentBatch;

        private IReadOnlyList<TerrainAnalysisTileData> slopeBatch;
        private IReadOnlyList<TerrainAnalysisTileData> curvatureBatch;

        private bool waitingForSlope;
        private bool waitingForCurvature;

        private string batchError;

        private bool terminal;

        public BuildState(
            WorldSettings worldSettings,
            TerrainHeightmapManifest heightManifest,
            TerrainSurfaceMaskManifest surfaceManifest,
            TerrainSurfaceSettings surfaceSettings,
            TerrainAnalysisKey slopeKey,
            TerrainAnalysisKey curvatureKey,
            TerrainAnalysisLayer slopeLayer,
            TerrainAnalysisLayer curvatureLayer,
            string settingsSignature,
            string generationSignature,
            TerrainRuntimeBakeWorkMode workMode,
            IEnumerable<Vector2Int> requestedTiles,
            long sourceBakeStateRevision,
            Action<TerrainSurfaceMaskGenerationResult> completionCallback
        )
        {
            this.worldSettings = worldSettings;
            this.heightManifest = heightManifest;
            this.surfaceManifest = surfaceManifest;
            this.surfaceSettings = surfaceSettings;

            screeSettings =
                TerrainScreeSuitabilityUtility.Capture(
                    surfaceSettings.Scree
                );

            this.slopeKey = slopeKey;
            this.curvatureKey = curvatureKey;
            this.slopeLayer = slopeLayer;
            this.curvatureLayer = curvatureLayer;

            this.settingsSignature = settingsSignature;
            this.generationSignature = generationSignature;

            this.workMode = workMode;
            this.sourceBakeStateRevision = sourceBakeStateRevision;

            sourceHeightmapGenerationRevision =
                worldSettings.heightmapGenerationRevision;

            sourceHeightmapSignature =
                worldSettings.lastGeneratedHeightSignature ?? "";

            sourceAuthoringSignature =
                heightManifest.sourceAuthoringSignature ?? "";

            analysisSourceSignature =
                slopeLayer.SourceSignature ?? "";

            analysisSourceHeightCacheInstanceId =
                slopeLayer.SourceHeightCacheInstanceId;

            surfaceGenerationRevisionBefore =
                worldSettings.surfaceMaskGenerationRevision;

            sourceSurfaceHeightRevisionBefore =
                worldSettings.surfaceSourceHeightmapGenerationRevision;

            this.completionCallback = completionCallback;

            coordinates = CopySortedUniqueCoordinates(requestedTiles);
        }

        public void Begin()
        {
            nextTileIndex = 0;

            EditorApplication.delayCall += ProcessNextBatch;
        }

        private void ProcessNextBatch()
        {
            if (terminal || activeBuild != this)
            {
                return;
            }

            if (!TargetStillCurrent())
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    "Surface generation target changed while asynchronous surface-mask generation was running."
                );

                return;
            }

            if (nextTileIndex >= coordinates.Count)
            {
                FinalizeCompleteDataset();
                return;
            }

            int count =
                Mathf.Min(
                    ReadbackBatchTileCount,
                    coordinates.Count - nextTileIndex
                );

            currentBatch =
                coordinates.GetRange(
                    nextTileIndex,
                    count
                );

            float progress =
                coordinates.Count > 0
                    ? (float)nextTileIndex / coordinates.Count
                    : 1f;

            bool cancelled =
                EditorUtility.DisplayCancelableProgressBar(
                    "Baking Runtime Surface Masks",
                    "Tiles " +
                    (nextTileIndex + 1) +
                    " - " +
                    (nextTileIndex + count) +
                    " / " +
                    coordinates.Count,
                    progress
                );

            if (cancelled)
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.Cancelled,
                    ""
                );

                return;
            }

            slopeBatch = null;
            curvatureBatch = null;
            batchError = "";

            waitingForSlope = true;
            waitingForCurvature = true;

            TerrainAnalysisReadbackService.RequestTiles(
                slopeKey,
                currentBatch,
                OnSlopeReadback
            );

            TerrainAnalysisReadbackService.RequestTiles(
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
            if (terminal || activeBuild != this)
            {
                return;
            }

            slopeBatch = data;
            waitingForSlope = false;

            CaptureError(errorMessage);
            TryFinishCurrentBatch();
        }

        private void OnCurvatureReadback(
            IReadOnlyList<TerrainAnalysisTileData> data,
            string errorMessage
        )
        {
            if (terminal || activeBuild != this)
            {
                return;
            }

            curvatureBatch = data;
            waitingForCurvature = false;

            CaptureError(errorMessage);
            TryFinishCurrentBatch();
        }

        private void CaptureError(
            string errorMessage
        )
        {
            if (
                string.IsNullOrEmpty(batchError)
                &&
                !string.IsNullOrEmpty(errorMessage)
            )
            {
                batchError = errorMessage;
            }
        }

        private void TryFinishCurrentBatch()
        {
            if (
                terminal
                ||
                activeBuild != this
                ||
                waitingForSlope
                ||
                waitingForCurvature
            )
            {
                return;
            }

            using var profilerScope =
                WorldMeshesProfiler.RuntimeBakeSurfaceGeneration.Auto();

            if (!TargetStillCurrent())
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    "Surface generation target changed before the current analysis readback batch could be written."
                );

                return;
            }

            if (!string.IsNullOrEmpty(batchError))
            {
                AddFailedCoordinates(currentBatch);

                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.Failed,
                    batchError
                );

                return;
            }

            Dictionary<Vector2Int, TerrainAnalysisTileData> slopeByTile =
                BuildTileDictionary(slopeBatch);

            Dictionary<Vector2Int, TerrainAnalysisTileData> curvatureByTile =
                BuildTileDictionary(curvatureBatch);

            List<Vector2Int> preparedCoordinates =
                new List<Vector2Int>();

            int preparedCreatedCount = 0;
            int preparedUpdatedCount = 0;

            for (
                int batchIndex = 0;
                batchIndex < currentBatch.Count;
                batchIndex++
            )
            {
                Vector2Int coordinate = currentBatch[batchIndex];

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
                    failed.Add(coordinate);

                    FinishTerminal(
                        TerrainSurfaceMaskGenerationOutcome.Failed,
                        "Analysis readback did not return both Slope and Curvature for tile (" +
                        coordinate.x +
                        ", " +
                        coordinate.y +
                        ")."
                    );

                    return;
                }

                if (
                    slopeData.SamplesPerSide
                        != heightManifest.heightTileSamplesPerSide
                    ||
                    curvatureData.SamplesPerSide
                        != slopeData.SamplesPerSide
                    ||
                    !Mathf.Approximately(
                        slopeData.SampleSpacing,
                        curvatureData.SampleSpacing
                    )
                )
                {
                    failed.Add(coordinate);

                    FinishTerminal(
                        TerrainSurfaceMaskGenerationOutcome.Failed,
                        "Analysis tile layout mismatch at (" +
                        coordinate.x +
                        ", " +
                        coordinate.y +
                        ")."
                    );

                    return;
                }

                float[] slopeValues = slopeData.CopyValues();
                float[] curvatureValues = curvatureData.CopyValues();

                int samplesPerSide = slopeData.SamplesPerSide;
                int expectedCount = samplesPerSide * samplesPerSide;

                if (
                    slopeValues == null
                    ||
                    curvatureValues == null
                    ||
                    slopeValues.Length != expectedCount
                    ||
                    curvatureValues.Length != expectedCount
                )
                {
                    failed.Add(coordinate);

                    FinishTerminal(
                        TerrainSurfaceMaskGenerationOutcome.Failed,
                        "Analysis tile (" +
                        coordinate.x +
                        ", " +
                        coordinate.y +
                        ") contains an unexpected sample count."
                    );

                    return;
                }

                byte[] output = new byte[expectedCount];

                for (int sampleZ = 0; sampleZ < samplesPerSide; sampleZ++)
                {
                    for (int sampleX = 0; sampleX < samplesPerSide; sampleX++)
                    {
                        int index =
                            sampleX +
                            sampleZ * samplesPerSide;

                        Vector2 worldXZ =
                            slopeData.WorldOriginXZ +
                            new Vector2(
                                sampleX * slopeData.SampleSpacing,
                                sampleZ * slopeData.SampleSpacing
                            );

                        float suitability =
                            TerrainScreeSuitabilityUtility.Evaluate(
                                screeSettings,
                                worldXZ,
                                slopeValues[index],
                                curvatureValues[index]
                            );

                        output[index] =
                            (byte)Mathf.Clamp(
                                Mathf.RoundToInt(
                                    suitability * 255f
                                ),
                                0,
                                255
                            );
                    }
                }

                /*
                 * From this point the compiler is attempting a physical asset
                 * mutation. If a later Unity API fails, keep Addressables
                 * content conservatively dirty because an existing asset may
                 * have been partially modified.
                 */
                anyPhysicalContentChange = true;

                SurfaceTileWriteOutcome writeOutcome =
                    WriteSurfaceTile(
                        coordinate,
                        samplesPerSide,
                        output,
                        out bool topologyChanged,
                        out string writeError
                    );

                /*
                 * A recreate fallback can delete an incompatible old asset
                 * before a later write/create failure. Preserve configuration
                 * dirtiness even when that tile is not acknowledged.
                 */
                addressablesConfigurationRequired |= topologyChanged;

                if (writeOutcome == SurfaceTileWriteOutcome.Failed)
                {
                    failed.Add(coordinate);

                    FinishTerminal(
                        TerrainSurfaceMaskGenerationOutcome.Failed,
                        writeError
                    );

                    return;
                }

                preparedCoordinates.Add(coordinate);

                if (writeOutcome == SurfaceTileWriteOutcome.Created)
                {
                    preparedCreatedCount++;
                }
                else
                {
                    preparedUpdatedCount++;
                }
            }

            try
            {
                using (WorldMeshesProfiler.RuntimeBakeSurfaceSaveBatch.Auto())
                using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
                {
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception exception)
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.Failed,
                    "Surface tile batch persistence failed.\n\n" +
                    exception.Message
                );

                return;
            }

            succeeded.AddRange(preparedCoordinates);
            createdCount += preparedCreatedCount;
            updatedCount += preparedUpdatedCount;

            nextTileIndex += currentBatch.Count;

            TerrainAnalysisReadbackService.Clear();

            if (
                TerrainRuntimeBakeValidationHooks.ShouldCancelCoordinateStage(
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    succeeded.Count,
                    coordinates.Count
                )
            )
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.Cancelled,
                    "Package 10.2 validation intentionally cancelled Surface generation at a completed batch boundary."
                );

                return;
            }

            EditorApplication.delayCall += ProcessNextBatch;
        }

        private void FinalizeCompleteDataset()
        {
            if (terminal || activeBuild != this)
            {
                return;
            }

            if (
                !TargetStillCurrent()
                ||
                TerrainRuntimeBakeStateService.GetSummary().StateRevision
                    != sourceBakeStateRevision
            )
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.StalePlan,
                    "Height generation, surface settings, Terrain Analysis source identity, or persistent bake state changed before surface finalization."
                );

                return;
            }

            if (workMode == TerrainRuntimeBakeWorkMode.Full)
            {
                bool cleanupSucceeded =
                    TryRemoveObsoleteTiles(
                        heightManifest,
                        out int removed,
                        out string removeError
                    );

                /*
                 * Record partial obsolete cleanup even if a later deletion
                 * failed. Those physical topology changes must still dirty
                 * Addressables while Full Surface remains pending.
                 */
                removedCount += removed;

                if (removed > 0)
                {
                    anyPhysicalContentChange = true;
                    addressablesConfigurationRequired = true;
                }

                if (!cleanupSucceeded)
                {
                    FinishTerminal(
                        TerrainSurfaceMaskGenerationOutcome.Failed,
                        removeError
                    );

                    return;
                }
            }

            if (
                !TerrainGenerationStateUtility.MarkSurfaceMasksGenerated(
                    worldSettings,
                    generationSignature
                )
            )
            {
                FinishTerminal(
                    TerrainSurfaceMaskGenerationOutcome.Failed,
                    "Surface tiles were written, but generated surface state could not be recorded."
                );

                return;
            }

            FinalizeManifest();

            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation();

            if (workMode == TerrainRuntimeBakeWorkMode.Full)
            {
                mutation
                    .ClearAllSurfaceTiles()
                    .ClearFullSurface();
            }
            else
            {
                mutation.RemoveSurfaceTiles(succeeded);
            }

            if (anyPhysicalContentChange)
            {
                mutation.DirtyAddressablesContent();
            }

            if (addressablesConfigurationRequired)
            {
                mutation.DirtyAddressablesConfiguration();
            }

            PersistentDirtyTransition dirtyTransition =
                ApplyMutationAndMeasureDirtyTransition(mutation);

            using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
            {
                AssetDatabase.SaveAssets();
            }

            FinishTerminalWithResult(
                new TerrainSurfaceMaskGenerationResult(
                    TerrainSurfaceMaskGenerationOutcome.Completed,
                    workMode,
                    coordinates,
                    succeeded,
                    failed,
                    CalculateUnprocessed(
                        coordinates,
                        succeeded,
                        failed
                    ),
                    createdCount,
                    updatedCount,
                    removedCount,
                    true,
                    surfaceGenerationRevisionBefore,
                    worldSettings.surfaceMaskGenerationRevision,
                    sourceSurfaceHeightRevisionBefore,
                    worldSettings.surfaceSourceHeightmapGenerationRevision,
                    dirtyTransition.configurationBecameDirty,
                    dirtyTransition.contentBecameDirty,
                    "",
                    workMode == TerrainRuntimeBakeWorkMode.Full
                        ? "The complete runtime surface-mask dataset was rebuilt and finalized."
                        : "The planned runtime surface-mask tiles were updated and the complete surface dataset was finalized."
                )
            );
        }

        private void FinalizeManifest()
        {
            TerrainSurfaceMaskManifest manifest = surfaceManifest;

            manifest.compilerVersion =
                TerrainSurfaceMaskManifest.CurrentCompilerVersion;

            manifest.channelLayoutVersion =
                TerrainSurfaceMaskManifest.CurrentChannelLayoutVersion;

            manifest.sourceHeightmapGenerationRevision =
                worldSettings.heightmapGenerationRevision;

            manifest.sourceHeightmapSignature =
                worldSettings.lastGeneratedHeightSignature;

            manifest.sourceAuthoringSignature =
                heightManifest.sourceAuthoringSignature;

            manifest.sourceAuthoringContentHash =
                heightManifest.sourceAuthoringContentHash;

            manifest.surfaceSettingsSignature =
                settingsSignature;

            manifest.surfaceGenerationSignature =
                generationSignature;

            manifest.tileGridWidth =
                heightManifest.heightTileGridWidth;

            manifest.tileGridHeight =
                heightManifest.heightTileGridHeight;

            manifest.tileWorldSize =
                heightManifest.heightTileWorldSize;

            manifest.samplesPerSide =
                heightManifest.heightTileSamplesPerSide;

            manifest.sampleSpacing =
                heightManifest.HeightSampleSpacing;

            manifest.worldSizeXZ =
                heightManifest.WorldSizeXZ;

            manifest.surfaceMaskGenerationRevision =
                worldSettings.surfaceMaskGenerationRevision;

            manifest.isComplete = true;

            EditorUtility.SetDirty(manifest);
            using (WorldMeshesProfiler.RuntimeBakeSurfaceSaveManifest.Auto())
            using (WorldMeshesProfiler.AssetDatabaseSaveAssetIfDirty.Auto())
            {
                AssetDatabase.SaveAssetIfDirty(manifest);
            }
        }

        private bool TargetStillCurrent()
        {
            if (
                worldSettings == null
                ||
                heightManifest == null
                ||
                surfaceSettings == null
                ||
                slopeLayer == null
                ||
                curvatureLayer == null
            )
            {
                return false;
            }

            if (
                TerrainGenerationStateUtility.GetHeightmapStatus(worldSettings)
                != TerrainGenerationStateUtility.GenerationStatus.Current
            )
            {
                return false;
            }

            if (
                worldSettings.heightmapGenerationRevision
                    != sourceHeightmapGenerationRevision
                ||
                !string.Equals(
                    worldSettings.lastGeneratedHeightSignature ?? "",
                    sourceHeightmapSignature,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            string currentSettingsSignature =
                TerrainSurfaceSignatureUtility.GetSettingsSignature(
                    surfaceSettings
                );

            string currentGenerationSignature =
                TerrainGenerationStateUtility.GetCurrentSurfaceMaskSignature(
                    worldSettings,
                    surfaceSettings
                );

            if (
                !string.Equals(
                    currentSettingsSignature,
                    settingsSignature,
                    StringComparison.Ordinal
                )
                ||
                !string.Equals(
                    currentGenerationSignature,
                    generationSignature,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            if (
                !TerrainAuthoringPreviewService.CacheReady
                ||
                TerrainAuthoringPreviewService.Status
                    != TerrainAuthoringPreviewStatus.Ready
                ||
                !string.Equals(
                    TerrainAuthoringPreviewService.SourceOverallAuthoringSignature,
                    sourceAuthoringSignature,
                    StringComparison.Ordinal
                )
                ||
                !TerrainAuthoringPreviewService.TryGetTerrainAnalysisSource(
                    out RenderTexture currentAnalysisHeightCache,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out string currentAnalysisSourceSignature
                )
                ||
                currentAnalysisHeightCache == null
                ||
                currentAnalysisHeightCache.GetInstanceID()
                    != analysisSourceHeightCacheInstanceId
                ||
                !string.Equals(
                    currentAnalysisSourceSignature,
                    analysisSourceSignature,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            if (
                !slopeLayer.IsReady
                ||
                !curvatureLayer.IsReady
                ||
                slopeLayer.SourceHeightCacheInstanceId
                    != analysisSourceHeightCacheInstanceId
                ||
                curvatureLayer.SourceHeightCacheInstanceId
                    != analysisSourceHeightCacheInstanceId
                ||
                !string.Equals(
                    slopeLayer.SourceSignature,
                    analysisSourceSignature,
                    StringComparison.Ordinal
                )
                ||
                !string.Equals(
                    curvatureLayer.SourceSignature,
                    analysisSourceSignature,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            return
                ValidateAnalysisLayer(
                    slopeLayer,
                    heightManifest,
                    out _
                )
                &&
                ValidateAnalysisLayer(
                    curvatureLayer,
                    heightManifest,
                    out _
                );
        }

        private void FinishTerminal(
            TerrainSurfaceMaskGenerationOutcome outcome,
            string errorMessage
        )
        {
            if (terminal)
            {
                return;
            }

            List<Vector2Int> unprocessed =
                CalculateUnprocessed(
                    coordinates,
                    succeeded,
                    failed
                );

            bool stateStillMatches =
                TerrainRuntimeBakeStateService.GetSummary().StateRevision
                    == sourceBakeStateRevision;

            bool targetStillMatches = TargetStillCurrent();

            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation();

            bool acknowledgeIncrementalSuccess =
                workMode == TerrainRuntimeBakeWorkMode.Incremental
                &&
                succeeded.Count > 0
                &&
                stateStillMatches
                &&
                targetStillMatches;

            if (acknowledgeIncrementalSuccess)
            {
                /*
                 * Successful physical tiles are durable. The manifest continues
                 * to describe the previous complete logical generation until
                 * the remaining pending surface tiles catch up.
                 */
                mutation.RemoveSurfaceTiles(succeeded);
            }

            if (anyPhysicalContentChange)
            {
                mutation.DirtyAddressablesContent();
            }

            if (addressablesConfigurationRequired)
            {
                mutation.DirtyAddressablesConfiguration();
            }

            PersistentDirtyTransition dirtyTransition =
                ApplyMutationAndMeasureDirtyTransition(mutation);

            string safetySuffix = "";

            if (
                workMode == TerrainRuntimeBakeWorkMode.Incremental
                &&
                succeeded.Count > 0
                &&
                !acknowledgeIncrementalSuccess
            )
            {
                safetySuffix =
                    " Successful physical tiles were left pending because the captured target or persistent bake-state revision changed.";
            }

            string summary;

            if (outcome == TerrainSurfaceMaskGenerationOutcome.Cancelled)
            {
                summary =
                    "Surface-mask generation was cancelled. Completed Incremental tiles were preserved when safe to acknowledge." +
                    safetySuffix;
            }
            else if (outcome == TerrainSurfaceMaskGenerationOutcome.StalePlan)
            {
                summary =
                    "Physical surface outputs already completed were preserved, but the logical surface generation was not finalized." +
                    safetySuffix;
            }
            else
            {
                summary =
                    "Surface-mask generation stopped before complete logical finalization. Completed Incremental tiles were preserved when safe to acknowledge." +
                    safetySuffix;
            }

            FinishTerminalWithResult(
                new TerrainSurfaceMaskGenerationResult(
                    outcome,
                    workMode,
                    coordinates,
                    succeeded,
                    failed,
                    unprocessed,
                    createdCount,
                    updatedCount,
                    removedCount,
                    false,
                    surfaceGenerationRevisionBefore,
                    worldSettings != null
                        ? worldSettings.surfaceMaskGenerationRevision
                        : surfaceGenerationRevisionBefore,
                    sourceSurfaceHeightRevisionBefore,
                    worldSettings != null
                        ? worldSettings.surfaceSourceHeightmapGenerationRevision
                        : sourceSurfaceHeightRevisionBefore,
                    dirtyTransition.configurationBecameDirty,
                    dirtyTransition.contentBecameDirty,
                    errorMessage ?? "",
                    summary
                )
            );
        }

        private void FinishTerminalWithResult(
            TerrainSurfaceMaskGenerationResult result
        )
        {
            if (terminal)
            {
                return;
            }

            terminal = true;

            EditorUtility.ClearProgressBar();
            TerrainAnalysisReadbackService.Clear();

            /*
             * Keep the current bake-owned Curvature key alive so subsequent
             * local preview edits can update only affected analysis slices.
             * Owner-scoped transient visualization layers are separate.
             */
            if (
                workMode == TerrainRuntimeBakeWorkMode.Full
                &&
                result != null
                &&
                result.Outcome != TerrainSurfaceMaskGenerationOutcome.Completed
                &&
                surfaceManifest != null
            )
            {
                surfaceManifest.isComplete = false;

                EditorUtility.SetDirty(surfaceManifest);
                using (WorldMeshesProfiler.RuntimeBakeSurfaceSaveManifest.Auto())
                using (WorldMeshesProfiler.AssetDatabaseSaveAssetIfDirty.Auto())
                {
                    AssetDatabase.SaveAssetIfDirty(surfaceManifest);
                }
            }

            activeBuild = null;

            if (
                workMode == TerrainRuntimeBakeWorkMode.Full
                &&
                result != null
                &&
                result.Outcome == TerrainSurfaceMaskGenerationOutcome.Completed
                &&
                surfaceManifest != null
            )
            {
                Selection.activeObject = surfaceManifest;
            }

            try
            {
                completionCallback?.Invoke(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void AddFailedCoordinates(
            IEnumerable<Vector2Int> coordinatesToAdd
        )
        {
            if (coordinatesToAdd == null)
            {
                return;
            }

            HashSet<Vector2Int> existing =
                new HashSet<Vector2Int>(failed);

            foreach (Vector2Int coordinate in coordinatesToAdd)
            {
                if (existing.Add(coordinate))
                {
                    failed.Add(coordinate);
                }
            }

            failed.Sort(CompareCoordinates);
        }

        private static Dictionary<Vector2Int, TerrainAnalysisTileData>
            BuildTileDictionary(
                IReadOnlyList<TerrainAnalysisTileData> data
            )
        {
            Dictionary<Vector2Int, TerrainAnalysisTileData> result =
                new Dictionary<Vector2Int, TerrainAnalysisTileData>();

            if (data == null)
            {
                return result;
            }

            for (int index = 0; index < data.Count; index++)
            {
                TerrainAnalysisTileData tile = data[index];

                if (tile == null)
                {
                    continue;
                }

                result[tile.TileCoordinate] = tile;
            }

            return result;
        }
    }

    // =====================================================
    // PERSISTENT STATE / COLLECTION HELPERS
    // =====================================================

    private static PersistentDirtyTransition
        ApplyMutationAndMeasureDirtyTransition(
            TerrainRuntimeBakeStateMutation mutation
        )
    {
        TerrainRuntimeBakeStateSummary before =
            TerrainRuntimeBakeStateService.GetSummary();

        TerrainRuntimeBakeStateService.ApplyMutation(mutation);

        TerrainRuntimeBakeStateSummary after =
            TerrainRuntimeBakeStateService.GetSummary();

        return new PersistentDirtyTransition
        {
            configurationBecameDirty =
                !before.AddressablesConfigurationDirty
                &&
                after.AddressablesConfigurationDirty,

            contentBecameDirty =
                !before.AddressablesContentDirty
                &&
                after.AddressablesContentDirty
        };
    }

    private static List<Vector2Int> CollectAllSurfaceTiles(
        TerrainHeightmapManifest heightManifest
    )
    {
        List<Vector2Int> tiles = new List<Vector2Int>();

        if (heightManifest == null)
        {
            return tiles;
        }

        for (
            int tileZ = 0;
            tileZ < heightManifest.heightTileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < heightManifest.heightTileGridWidth;
                tileX++
            )
            {
                tiles.Add(
                    new Vector2Int(tileX, tileZ)
                );
            }
        }

        return tiles;
    }

    private static List<Vector2Int> CopySortedUniqueCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        HashSet<Vector2Int> unique =
            source != null
                ? new HashSet<Vector2Int>(source)
                : new HashSet<Vector2Int>();

        List<Vector2Int> result =
            new List<Vector2Int>(unique);

        result.Sort(CompareCoordinates);

        return result;
    }

    private static List<Vector2Int> CalculateUnprocessed(
        IEnumerable<Vector2Int> requested,
        IEnumerable<Vector2Int> succeeded,
        IEnumerable<Vector2Int> failed
    )
    {
        HashSet<Vector2Int> completed =
            new HashSet<Vector2Int>();

        if (succeeded != null)
        {
            foreach (Vector2Int coordinate in succeeded)
            {
                completed.Add(coordinate);
            }
        }

        if (failed != null)
        {
            foreach (Vector2Int coordinate in failed)
            {
                completed.Add(coordinate);
            }
        }

        List<Vector2Int> unprocessed =
            new List<Vector2Int>();

        if (requested != null)
        {
            foreach (Vector2Int coordinate in requested)
            {
                if (!completed.Contains(coordinate))
                {
                    unprocessed.Add(coordinate);
                }
            }
        }

        unprocessed.Sort(CompareCoordinates);

        return unprocessed;
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison = left.y.CompareTo(right.y);

        if (yComparison != 0)
        {
            return yComparison;
        }

        return left.x.CompareTo(right.x);
    }

    // =====================================================
    // IMMEDIATE RESULT / LOGGING
    // =====================================================

    private static TerrainSurfaceMaskGenerationResult CreateSimpleResult(
        TerrainSurfaceMaskGenerationOutcome outcome,
        TerrainRuntimeBakeWorkMode mode,
        WorldSettings worldSettings,
        string errorMessage,
        string summaryMessage = ""
    )
    {
        return CreateResult(
            outcome,
            mode,
            null,
            null,
            null,
            null,
            worldSettings,
            0,
            0,
            0,
            false,
            false,
            false,
            errorMessage,
            summaryMessage
        );
    }

    private static TerrainSurfaceMaskGenerationResult CreateResult(
        TerrainSurfaceMaskGenerationOutcome outcome,
        TerrainRuntimeBakeWorkMode mode,
        IEnumerable<Vector2Int> requested,
        IEnumerable<Vector2Int> succeeded,
        IEnumerable<Vector2Int> failed,
        IEnumerable<Vector2Int> unprocessed,
        WorldSettings worldSettings,
        int createdCount,
        int updatedCount,
        int removedCount,
        bool datasetFinalized,
        bool addressablesConfigurationBecameDirty,
        bool addressablesContentBecameDirty,
        string errorMessage,
        string summaryMessage
    )
    {
        int surfaceRevision =
            worldSettings != null
                ? worldSettings.surfaceMaskGenerationRevision
                : 0;

        int sourceHeightRevision =
            worldSettings != null
                ? worldSettings.surfaceSourceHeightmapGenerationRevision
                : 0;

        return new TerrainSurfaceMaskGenerationResult(
            outcome,
            mode,
            requested,
            succeeded,
            failed,
            unprocessed,
            createdCount,
            updatedCount,
            removedCount,
            datasetFinalized,
            surfaceRevision,
            surfaceRevision,
            sourceHeightRevision,
            sourceHeightRevision,
            addressablesConfigurationBecameDirty,
            addressablesContentBecameDirty,
            errorMessage,
            summaryMessage
        );
    }

    private static void CompleteImmediately(
        Action<TerrainSurfaceMaskGenerationResult> callback,
        TerrainSurfaceMaskGenerationResult result
    )
    {
        try
        {
            callback?.Invoke(result);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void LogResult(
        TerrainSurfaceMaskGenerationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError(
                "Runtime surface-mask generation returned no result."
            );

            return;
        }

        string report = result.BuildDiagnosticReport();

        if (
            result.Outcome == TerrainSurfaceMaskGenerationOutcome.Completed
            ||
            result.Outcome == TerrainSurfaceMaskGenerationOutcome.NoWork
        )
        {
            Debug.Log(report);
        }
        else if (
            result.Outcome == TerrainSurfaceMaskGenerationOutcome.Cancelled
        )
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }
}
