using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TerrainAuthoringVisualizationMode
{
    Lit = 0,
    Height = 1,
    Slope = 2,
    Curvature = 3,
    ScreeSuitability = 4
}

public enum TerrainAuthoringVisualizationStatus
{
    Inactive,
    Ready,
    HeightPreviewRequired,
    ClipmapUnavailable,
    PlayMode,
    Error
}

/*
 * Editor-only authoring visualization controller.
 *
 * This service owns only shader visualization properties. It does not:
 *
 * - move the clipmap
 * - rebuild or reload authoring height data
 * - modify the terrain Material asset
 * - change runtime terrain settings
 *
 * All visualization state is applied transiently through
 * MaterialPropertyBlock so it can coexist with:
 *
 * - TerrainHeightCacheBindingUtility
 * - TerrainClipmapWorldBoundsBindingUtility
 * - TerrainClipmapLayoutApplier
 */
[InitializeOnLoad]
public static partial class TerrainAuthoringVisualizationController
{
    // =====================================================
    // EDITOR PREFERENCES
    // =====================================================

    private const string BaseModeEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.BaseMode";

    private const string CurvatureScaleEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.CurvatureScale";

    private const string ContoursEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.Contours";

    private const string ContourIntervalEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.ContourInterval";

    private const string ChunkGridEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.ChunkGrid";

    private const string HeightTileGridEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.HeightTileGrid";

    private const string WorldBoundaryEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.WorldBoundary";

    private const string LODRegionsEditorPrefsKey =
        "WorldMeshes.AuthoringVisualization.LODRegions";

    private const float DefaultContourInterval =
        10f;

    private const float DefaultCurvatureScale =
        16f;

    private const float MinimumCurvatureScale =
        0.25f;

    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int VisualizationEnabledPropertyId =
        Shader.PropertyToID(
            "_AuthoringVisualizationEnabled"
        );

    private static readonly int VisualizationModePropertyId =
        Shader.PropertyToID(
            "_AuthoringVisualizationMode"
        );

    private static readonly int HeightRangePropertyId =
        Shader.PropertyToID(
            "_AuthoringHeightRange"
        );

    private static readonly int CurvatureScalePropertyId =
        Shader.PropertyToID(
            "_AuthoringCurvatureScale"
        );

    private static readonly int ContoursEnabledPropertyId =
        Shader.PropertyToID(
            "_AuthoringContoursEnabled"
        );

    private static readonly int ContourIntervalPropertyId =
        Shader.PropertyToID(
            "_AuthoringContourInterval"
        );

    private static readonly int ChunkGridEnabledPropertyId =
        Shader.PropertyToID(
            "_AuthoringChunkGridEnabled"
        );

    private static readonly int ChunkSizePropertyId =
        Shader.PropertyToID(
            "_AuthoringChunkSize"
        );

    private static readonly int HeightTileGridEnabledPropertyId =
        Shader.PropertyToID(
            "_AuthoringHeightTileGridEnabled"
        );

    private static readonly int HeightTileWorldSizePropertyId =
        Shader.PropertyToID(
            "_AuthoringHeightTileWorldSize"
        );

    private static readonly int WorldBoundaryEnabledPropertyId =
        Shader.PropertyToID(
            "_AuthoringWorldBoundaryEnabled"
        );

    private static readonly int LODRegionsEnabledPropertyId =
        Shader.PropertyToID(
            "_AuthoringLODRegionsEnabled"
        );

    private static readonly int LODLevelPropertyId =
        Shader.PropertyToID(
            "_AuthoringLODLevel"
        );

    private static readonly int LODCountPropertyId =
        Shader.PropertyToID(
            "_AuthoringLODCount"
        );

    // =====================================================
    // STATE
    // =====================================================

    private static WorldSettings worldSettings;

    private static Transform boundClipmapRoot;

    private static MaterialPropertyBlock propertyBlock;

    private static bool reapplyScheduled;

    private static bool suspendedForPlayMode;

    private static bool isApplying;

    private static TerrainAuthoringVisualizationStatus status =
        TerrainAuthoringVisualizationStatus.Inactive;

    private static string statusMessage =
        "Normal Lit terrain rendering is active with no authoring overlays.";

    private static int boundRendererCount;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    static TerrainAuthoringVisualizationController()
    {
        TerrainAuthoringPreviewService.PreviewStateChanged +=
            OnPreviewStateChanged;

        EditorApplication.hierarchyChanged +=
            OnHierarchyChanged;

        EditorApplication.projectChanged +=
            OnProjectChanged;

        Undo.undoRedoPerformed +=
            OnUndoRedo;

        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        EditorSceneManager.activeSceneChangedInEditMode +=
            OnActiveSceneChangedInEditMode;

        AssemblyReloadEvents.beforeAssemblyReload +=
            OnBeforeAssemblyReload;

        EditorApplication.quitting +=
            OnEditorQuitting;

        RequestReapply();
    }

    // =====================================================
    // PUBLIC SETTINGS
    // =====================================================

    public static TerrainAuthoringVisualizationMode BaseMode
    {
        get
        {
            int storedValue =
                EditorPrefs.GetInt(
                    BaseModeEditorPrefsKey,
                    (int)TerrainAuthoringVisualizationMode.Lit
                );

            if (
                storedValue <
                    (int)TerrainAuthoringVisualizationMode.Lit
                ||
                storedValue >
                    (int)TerrainAuthoringVisualizationMode.ScreeSuitability
            )
            {
                return
                    TerrainAuthoringVisualizationMode.Lit;
            }

            return
                (TerrainAuthoringVisualizationMode)
                storedValue;
        }

        set
        {
            int integerValue =
                (int)value;

            if (
                integerValue <
                    (int)TerrainAuthoringVisualizationMode.Lit
                ||
                integerValue >
                    (int)TerrainAuthoringVisualizationMode.ScreeSuitability
            )
            {
                value =
                    TerrainAuthoringVisualizationMode.Lit;
            }

            if (BaseMode == value)
            {
                return;
            }

            EditorPrefs.SetInt(
                BaseModeEditorPrefsKey,
                (int)value
            );

            RequestReapply();
        }
    }

    public static float CurvatureScale
    {
        get
        {
            return
                Mathf.Max(
                    MinimumCurvatureScale,
                    EditorPrefs.GetFloat(
                        CurvatureScaleEditorPrefsKey,
                        DefaultCurvatureScale
                    )
                );
        }

        set
        {
            float safeValue =
                IsFinite(
                    value
                )
                    ? Mathf.Max(
                        MinimumCurvatureScale,
                        value
                    )
                    : DefaultCurvatureScale;

            if (
                Mathf.Approximately(
                    CurvatureScale,
                    safeValue
                )
            )
            {
                return;
            }

            EditorPrefs.SetFloat(
                CurvatureScaleEditorPrefsKey,
                safeValue
            );

            RequestReapply();
        }
    }

    public static bool ContoursEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    ContoursEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (ContoursEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                ContoursEditorPrefsKey,
                value
            );

            RequestReapply();
        }
    }

    public static float ContourInterval
    {
        get
        {
            return
                Mathf.Max(
                    0.1f,
                    EditorPrefs.GetFloat(
                        ContourIntervalEditorPrefsKey,
                        DefaultContourInterval
                    )
                );
        }

        set
        {
            float safeValue =
                IsFinite(
                    value
                )
                    ? Mathf.Max(
                        0.1f,
                        value
                    )
                    : DefaultContourInterval;

            if (
                Mathf.Approximately(
                    ContourInterval,
                    safeValue
                )
            )
            {
                return;
            }

            EditorPrefs.SetFloat(
                ContourIntervalEditorPrefsKey,
                safeValue
            );

            RequestReapply();
        }
    }

    public static bool ChunkGridEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    ChunkGridEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (ChunkGridEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                ChunkGridEditorPrefsKey,
                value
            );

            RequestReapply();
        }
    }

    public static bool HeightTileGridEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    HeightTileGridEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (HeightTileGridEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                HeightTileGridEditorPrefsKey,
                value
            );

            RequestReapply();
        }
    }

    public static bool WorldBoundaryEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    WorldBoundaryEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (WorldBoundaryEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                WorldBoundaryEditorPrefsKey,
                value
            );

            RequestReapply();
        }
    }

    public static bool LODRegionsEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    LODRegionsEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (LODRegionsEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                LODRegionsEditorPrefsKey,
                value
            );

            RequestReapply();
        }
    }

    // =====================================================
    // PUBLIC STATUS
    // =====================================================

    public static TerrainAuthoringVisualizationStatus Status
    {
        get
        {
            return
                status;
        }
    }

    public static string StatusLabel
    {
        get
        {
            switch (status)
            {
                case TerrainAuthoringVisualizationStatus.Ready:
                    return
                        "Ready";

                case TerrainAuthoringVisualizationStatus.HeightPreviewRequired:
                    return
                        "Height Preview Required";

                case TerrainAuthoringVisualizationStatus.ClipmapUnavailable:
                    return
                        "Clipmap Unavailable";

                case TerrainAuthoringVisualizationStatus.PlayMode:
                    return
                        "Runtime Terrain";

                case TerrainAuthoringVisualizationStatus.Error:
                    return
                        "Error";

                default:
                    return
                        "Inactive";
            }
        }
    }

    public static string StatusMessage
    {
        get
        {
            return
                statusMessage;
        }
    }

    public static int BoundRendererCount
    {
        get
        {
            return
                boundRendererCount;
        }
    }

    public static bool RequiresHeightPreview
    {
        get
        {
            return
                BaseMode ==
                    TerrainAuthoringVisualizationMode.Height
                ||
                BaseMode ==
                    TerrainAuthoringVisualizationMode.Slope
                ||
                BaseMode ==
                    TerrainAuthoringVisualizationMode.Curvature
                ||
                BaseMode ==
                    TerrainAuthoringVisualizationMode.ScreeSuitability
                ||
                ContoursEnabled;
        }
    }

    public static bool HeightPreviewAvailable
    {
        get
        {
            return
                TerrainAuthoringPreviewService
                    .CacheReady;
        }
    }

    public static bool HasVisualizationOverrides
    {
        get
        {
            return
                BaseMode !=
                    TerrainAuthoringVisualizationMode.Lit
                ||
                ContoursEnabled
                ||
                ChunkGridEnabled
                ||
                HeightTileGridEnabled
                ||
                WorldBoundaryEnabled
                ||
                LODRegionsEnabled;
        }
    }

    // =====================================================
    // PUBLIC COMMANDS
    // =====================================================

    public static void RequestReapply()
    {
        ScheduleReapply();
    }

    public static void ResetVisualization()
    {
        EditorPrefs.SetInt(
            BaseModeEditorPrefsKey,
            (int)TerrainAuthoringVisualizationMode.Lit
        );

        EditorPrefs.SetFloat(
            CurvatureScaleEditorPrefsKey,
            DefaultCurvatureScale
        );

        EditorPrefs.SetBool(
            ContoursEditorPrefsKey,
            false
        );

        EditorPrefs.SetFloat(
            ContourIntervalEditorPrefsKey,
            DefaultContourInterval
        );

        EditorPrefs.SetBool(
            ChunkGridEditorPrefsKey,
            false
        );

        EditorPrefs.SetBool(
            HeightTileGridEditorPrefsKey,
            false
        );

        EditorPrefs.SetBool(
            WorldBoundaryEditorPrefsKey,
            false
        );

        EditorPrefs.SetBool(
            LODRegionsEditorPrefsKey,
            false
        );

        RequestReapply();
    }

    // =====================================================
    // SCHEDULE
    // =====================================================

    private static void ScheduleReapply()
    {
        if (reapplyScheduled)
        {
            return;
        }

        reapplyScheduled =
            true;

        EditorApplication.delayCall +=
            ExecuteScheduledReapply;
    }

    private static void ExecuteScheduledReapply()
    {
        reapplyScheduled =
            false;

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            ScheduleReapply();

            return;
        }

        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
            ||
            suspendedForPlayMode
        )
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.PlayMode,
                "Authoring visualization is disabled while runtime owns terrain rendering."
            );

            RepaintEditorViews();

            return;
        }

        ApplyVisualization();
    }

    // =====================================================
    // APPLY
    // =====================================================

    private static void ApplyVisualization()
    {
        if (isApplying)
        {
            return;
        }

        if (
            !TryLoadWorldSettings(
                out string settingsError
            )
        )
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.Error,
                settingsError
            );

            RepaintEditorViews();

            return;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out Transform clipmapRoot,
                    out string sceneLookupError
                )
        )
        {
            boundClipmapRoot =
                null;

            boundRendererCount =
                0;

            SetStatus(
                TerrainAuthoringVisualizationStatus.Error,
                sceneLookupError
            );

            RepaintEditorViews();

            return;
        }

        if (clipmapRoot == null)
        {
            boundClipmapRoot =
                null;

            boundRendererCount =
                0;

            SetStatus(
                TerrainAuthoringVisualizationStatus.ClipmapUnavailable,
                "WorldRoot/Clipmap was not found in the active scene. Run Sync World Hierarchy."
            );

            RepaintEditorViews();

            return;
        }

        boundClipmapRoot =
            clipmapRoot;

        MeshRenderer[] renderers =
            clipmapRoot
                .GetComponentsInChildren<MeshRenderer>(
                    true
                );

        if (
            renderers == null
            ||
            renderers.Length == 0
        )
        {
            boundRendererCount =
                0;

            SetStatus(
                TerrainAuthoringVisualizationStatus.ClipmapUnavailable,
                "No MeshRenderer components were found under WorldRoot/Clipmap."
            );

            RepaintEditorViews();

            return;
        }

        EnsurePropertyBlock();

        bool heightPreviewReady =
            TerrainAuthoringPreviewService
                .CacheReady;

        TerrainAuthoringVisualizationMode
            requestedMode =
                BaseMode;

        TerrainAuthoringVisualizationMode
            effectiveMode =
                requestedMode;

        bool requestedHeightDependentMode =
            requestedMode ==
                TerrainAuthoringVisualizationMode.Height
            ||
            requestedMode ==
                TerrainAuthoringVisualizationMode.Slope
            ||
            requestedMode ==
                TerrainAuthoringVisualizationMode.Curvature
            ||
            requestedMode ==
                TerrainAuthoringVisualizationMode.ScreeSuitability;

        if (
            requestedHeightDependentMode
            &&
            !heightPreviewReady
        )
        {
            effectiveMode =
                TerrainAuthoringVisualizationMode.Lit;
        }

        TerrainAnalysisLayer curvatureAnalysisLayer =
            null;

        string curvatureAnalysisError =
            "";

        if (
            effectiveMode ==
                TerrainAuthoringVisualizationMode.Curvature
        )
        {
            if (
                !TryPrepareCurvatureAnalysis(
                    out curvatureAnalysisLayer,
                    out curvatureAnalysisError
                )
            )
            {
                effectiveMode =
                    TerrainAuthoringVisualizationMode.Lit;
            }
        }
        else
        {
            /*
             * Curvature visualization owns one transient analysis slot.
             * Leaving the mode releases that scratch allocation.
             */
            ReleaseCurvatureVisualizationAnalysis();
        }

        bool effectiveContours =
            ContoursEnabled
            &&
            heightPreviewReady;

        bool visualizationEnabled =
            HasVisualizationOverrides;

        float minimumHeight =
            heightPreviewReady
                ? TerrainAuthoringPreviewService
                    .MinimumPreviewHeight
                : 0f;

        float maximumHeight =
            heightPreviewReady
                ? TerrainAuthoringPreviewService
                    .MaximumPreviewHeight
                : 1f;

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        float heightTileWorldSize =
            Mathf.Max(
                0.01f,
                worldSettings.HeightTileWorldSize
            );

        int lodCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        int appliedRendererCount =
            0;

        isApplying =
            true;

        try
        {
            foreach (
                MeshRenderer renderer
                in renderers
            )
            {
                if (
                    !IsCompatibleTerrainRenderer(
                        renderer
                    )
                )
                {
                    continue;
                }

                int lodLevel =
                    DetermineLODLevel(
                        renderer.transform,
                        clipmapRoot
                    );

                renderer.GetPropertyBlock(
                    propertyBlock
                );

                propertyBlock.SetFloat(
                    VisualizationEnabledPropertyId,
                    visualizationEnabled
                        ? 1f
                        : 0f
                );

                propertyBlock.SetFloat(
                    VisualizationModePropertyId,
                    (float)effectiveMode
                );

                propertyBlock.SetVector(
                    HeightRangePropertyId,
                    new Vector4(
                        minimumHeight,
                        maximumHeight,
                        0f,
                        0f
                    )
                );

                propertyBlock.SetFloat(
                    CurvatureScalePropertyId,
                    CurvatureScale
                );

                ApplyCurvatureAnalysisProperties(
                    propertyBlock,
                    curvatureAnalysisLayer
                );

                propertyBlock.SetFloat(
                    ContoursEnabledPropertyId,
                    effectiveContours
                        ? 1f
                        : 0f
                );

                propertyBlock.SetFloat(
                    ContourIntervalPropertyId,
                    ContourInterval
                );

                propertyBlock.SetFloat(
                    ChunkGridEnabledPropertyId,
                    ChunkGridEnabled
                        ? 1f
                        : 0f
                );

                propertyBlock.SetFloat(
                    ChunkSizePropertyId,
                    chunkSize
                );

                propertyBlock.SetFloat(
                    HeightTileGridEnabledPropertyId,
                    HeightTileGridEnabled
                        ? 1f
                        : 0f
                );

                propertyBlock.SetFloat(
                    HeightTileWorldSizePropertyId,
                    heightTileWorldSize
                );

                propertyBlock.SetFloat(
                    WorldBoundaryEnabledPropertyId,
                    WorldBoundaryEnabled
                        ? 1f
                        : 0f
                );

                propertyBlock.SetFloat(
                    LODRegionsEnabledPropertyId,
                    LODRegionsEnabled
                        ? 1f
                        : 0f
                );

                propertyBlock.SetFloat(
                    LODLevelPropertyId,
                    Mathf.Max(
                        0,
                        lodLevel
                    )
                );

                propertyBlock.SetFloat(
                    LODCountPropertyId,
                    lodCount
                );

                renderer.SetPropertyBlock(
                    propertyBlock
                );

                appliedRendererCount++;
            }
        }
        finally
        {
            isApplying =
                false;
        }

        boundRendererCount =
            appliedRendererCount;

        if (boundRendererCount <= 0)
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.Error,
                "No clipmap renderer uses a terrain material with the Stage 5 authoring visualization properties."
            );

            RepaintEditorViews();

            return;
        }

        bool missingRequiredHeightPreview =
            RequiresHeightPreview
            &&
            !heightPreviewReady;

        if (
            !string.IsNullOrEmpty(
                curvatureAnalysisError
            )
        )
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.Error,
                "Curvature visualization could not obtain its cached Terrain Analysis layer.\n\n" +
                curvatureAnalysisError
            );
        }
        else if (missingRequiredHeightPreview)
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.HeightPreviewRequired,
                "Height, Slope, Curvature, Scree Suitability, and Contour diagnostics require a ready Height Preview. Height/Slope/Curvature/Scree Suitability currently fall back to Lit and contours are suppressed; spatial overlays remain available."
            );
        }
        else if (!visualizationEnabled)
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.Inactive,
                "Normal Lit terrain rendering is active with no authoring overlays."
            );
        }
        else
        {
            SetStatus(
                TerrainAuthoringVisualizationStatus.Ready,
                "Transient authoring visualization is bound to the edit-mode clipmap."
            );
        }

        RepaintEditorViews();
    }

    // =====================================================
    // DISABLE TRANSIENT VISUALIZATION
    // =====================================================

    private static void DisableVisualization()
    {
        ReleaseCurvatureVisualizationAnalysis();

        if (
            Application.isPlaying
            ||
            isApplying
        )
        {
            return;
        }

        Transform clipmapRoot =
            boundClipmapRoot;

        if (clipmapRoot == null)
        {
            TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out clipmapRoot,
                    out _
                );
        }

        if (clipmapRoot == null)
        {
            boundClipmapRoot =
                null;

            boundRendererCount =
                0;

            return;
        }

        MeshRenderer[] renderers =
            clipmapRoot
                .GetComponentsInChildren<MeshRenderer>(
                    true
                );

        if (renderers == null)
        {
            return;
        }

        EnsurePropertyBlock();

        isApplying =
            true;

        try
        {
            foreach (
                MeshRenderer renderer
                in renderers
            )
            {
                if (
                    !IsCompatibleTerrainRenderer(
                        renderer
                    )
                )
                {
                    continue;
                }

                renderer.GetPropertyBlock(
                    propertyBlock
                );

                /*
                 * Disable only the master visualization switch.
                 *
                 * Do not clear the whole MaterialPropertyBlock:
                 * height-cache, world-boundary, and stitch systems
                 * own unrelated values in the same block.
                 */
                propertyBlock.SetFloat(
                    VisualizationEnabledPropertyId,
                    0f
                );

                renderer.SetPropertyBlock(
                    propertyBlock
                );
            }
        }
        finally
        {
            isApplying =
                false;
        }
    }

    // =====================================================
    // LOD METADATA
    // =====================================================

    private static int DetermineLODLevel(
        Transform rendererTransform,
        Transform clipmapRoot
    )
    {
        Transform current =
            rendererTransform;

        while (
            current != null
            &&
            current != clipmapRoot
        )
        {
            if (
                current.name ==
                "Center_LOD0"
            )
            {
                return 0;
            }

            if (
                TryParseLODGroupName(
                    current.name,
                    out int level
                )
            )
            {
                return
                    level;
            }

            current =
                current.parent;
        }

        return
            0;
    }

    private static bool TryParseLODGroupName(
        string objectName,
        out int level
    )
    {
        level =
            0;

        if (
            string.IsNullOrEmpty(
                objectName
            )
            ||
            !objectName.StartsWith(
                "LOD"
            )
            ||
            objectName.Length <= 3
        )
        {
            return false;
        }

        return
            int.TryParse(
                objectName.Substring(
                    3
                ),
                out level
            );
    }

    // =====================================================
    // MATERIAL COMPATIBILITY
    // =====================================================

    private static bool IsCompatibleTerrainRenderer(
        MeshRenderer renderer
    )
    {
        if (
            renderer == null
            ||
            renderer.sharedMaterial == null
        )
        {
            return false;
        }

        Material material =
            renderer.sharedMaterial;

        return
            material.HasProperty(
                VisualizationEnabledPropertyId
            )
            &&
            material.HasProperty(
                VisualizationModePropertyId
            )
            &&
            material.HasProperty(
                HeightRangePropertyId
            )
            &&
            material.HasProperty(
                CurvatureScalePropertyId
            )
            &&
            material.HasProperty(
                CurvatureAnalysisReadyPropertyId
            )
            &&
            material.HasProperty(
                LODLevelPropertyId
            );
    }

    // =====================================================
    // WORLD SETTINGS
    // =====================================================

    private static bool TryLoadWorldSettings(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings != null)
        {
            return true;
        }

        worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (worldSettings != null)
        {
            return true;
        }

        errorMessage =
            "WorldSettings could not be loaded from:\n" +
            WorldMeshesPaths.WorldSettingsAssetPath;

        return false;
    }

    // =====================================================
    // EDITOR EVENTS
    // =====================================================

    private static void OnPreviewStateChanged()
    {
        RequestReapply();
    }

    private static void OnHierarchyChanged()
    {
        if (isApplying)
        {
            return;
        }

        boundClipmapRoot =
            null;

        RequestReapply();
    }

    private static void OnProjectChanged()
    {
        worldSettings =
            null;

        RequestReapply();
    }

    private static void OnUndoRedo()
    {
        RequestReapply();
    }

    private static void OnActiveSceneChangedInEditMode(
        Scene previousScene,
        Scene newScene
    )
    {
        boundClipmapRoot =
            null;

        worldSettings =
            null;

        RequestReapply();
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            {
                DisableVisualization();

                suspendedForPlayMode =
                    true;

                SetStatus(
                    TerrainAuthoringVisualizationStatus.PlayMode,
                    "Authoring visualization was disabled for Play Mode."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.EnteredPlayMode:
            {
                suspendedForPlayMode =
                    true;

                SetStatus(
                    TerrainAuthoringVisualizationStatus.PlayMode,
                    "Runtime terrain rendering is active."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.ExitingPlayMode:
            {
                break;
            }

            case PlayModeStateChange.EnteredEditMode:
            {
                suspendedForPlayMode =
                    false;

                boundClipmapRoot =
                    null;

                RequestReapply();

                break;
            }
        }
    }

    private static void OnBeforeAssemblyReload()
    {
        DisableVisualization();
    }

    private static void OnEditorQuitting()
    {
        DisableVisualization();
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static void EnsurePropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock =
                new MaterialPropertyBlock();
        }
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

    private static void SetStatus(
        TerrainAuthoringVisualizationStatus newStatus,
        string message
    )
    {
        status =
            newStatus;

        statusMessage =
            string.IsNullOrEmpty(
                message
            )
                ? ""
                : message;
    }

    private static void RepaintEditorViews()
    {
        SceneView.RepaintAll();

        WorldMeshesEditorWindow[] windows =
            Resources
                .FindObjectsOfTypeAll<WorldMeshesEditorWindow>();

        foreach (
            WorldMeshesEditorWindow window
            in windows
        )
        {
            if (window != null)
            {
                window.Repaint();
            }
        }
    }
}
