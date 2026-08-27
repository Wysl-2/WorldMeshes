using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TerrainAuthoringPreviewStatus
{
    Disabled,
    PlayMode,
    AuthoringUnavailable,
    ClipmapUnavailable,
    Ready,
    Error
}

[InitializeOnLoad]
public static class TerrainAuthoringPreviewService
{
    // =====================================================
    // EDITOR PREFERENCE
    // =====================================================

    private const string PreviewEnabledEditorPrefsKey =
        "WorldMeshes.TerrainAuthoringPreview.Enabled";

    // =====================================================
    // STATE
    // =====================================================

    private static TerrainAuthoringPreviewCache previewCache;

    private static Transform boundClipmapRoot;

    private static TerrainAuthoringPreviewStatus status =
        TerrainAuthoringPreviewStatus.Disabled;

    private static string statusMessage =
        "Terrain authoring preview is disabled.";

    private static bool refreshScheduled;

    private static bool rebuildRequested =
        true;

    private static bool rebindRequested =
        true;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    static TerrainAuthoringPreviewService()
    {
        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        EditorApplication.hierarchyChanged +=
            OnHierarchyChanged;

        EditorApplication.projectChanged +=
            OnProjectChanged;

        Undo.undoRedoPerformed +=
            OnUndoRedo;

        AssemblyReloadEvents.beforeAssemblyReload +=
            OnBeforeAssemblyReload;

        EditorApplication.quitting +=
            OnEditorQuitting;

        RequestRebuild();
    }

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public static bool Enabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    PreviewEnabledEditorPrefsKey,
                    true
                );
        }

        set
        {
            bool oldValue =
                Enabled;

            if (oldValue == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                PreviewEnabledEditorPrefsKey,
                value
            );

            if (!value)
            {
                ReleaseBinding();
                ReleaseCache();

                SetStatus(
                    TerrainAuthoringPreviewStatus.Disabled,
                    "Terrain authoring preview is disabled."
                );

                RepaintEditorViews();

                return;
            }

            RequestRebuild();
        }
    }

    public static TerrainAuthoringPreviewStatus Status
    {
        get
        {
            return status;
        }
    }

    public static string StatusLabel
    {
        get
        {
            switch (status)
            {
                case TerrainAuthoringPreviewStatus.PlayMode:
                    return
                        "Runtime Owns Height Cache";

                case TerrainAuthoringPreviewStatus.AuthoringUnavailable:
                    return
                        "Authoring Unavailable";

                case TerrainAuthoringPreviewStatus.ClipmapUnavailable:
                    return
                        "Clipmap Unavailable";

                case TerrainAuthoringPreviewStatus.Ready:
                    return
                        "Ready";

                case TerrainAuthoringPreviewStatus.Error:
                    return
                        "Error";

                default:
                    return
                        "Disabled";
            }
        }
    }

    public static string StatusMessage
    {
        get
        {
            return statusMessage;
        }
    }

    public static bool CacheReady
    {
        get
        {
            return
                previewCache != null
                &&
                previewCache.IsReady;
        }
    }

    public static int CacheWidth
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.CacheWidth
                    : 0;
        }
    }

    public static int CacheHeight
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.CacheHeight
                    : 0;
        }
    }

    public static int CacheSliceCount
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.SliceCount
                    : 0;
        }
    }

    public static int SamplesPerSide
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.SamplesPerSide
                    : 0;
        }
    }

    public static Vector2Int CacheOriginTile
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.CacheOriginTile
                    : Vector2Int.zero;
        }
    }

    public static long ApproximateGpuMemoryBytes
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.ApproximateGpuMemoryBytes
                    : 0L;
        }
    }

    public static float MinimumPreviewHeight
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.MinimumHeight
                    : 0f;
        }
    }

    public static float MaximumPreviewHeight
    {
        get
        {
            return
                previewCache != null
                    ? previewCache.MaximumHeight
                    : 0f;
        }
    }

    // =====================================================
    // PUBLIC REFRESH API
    // =====================================================

    public static void RequestRefresh()
    {
        ScheduleRefresh();
    }

    public static void RequestRebuild()
    {
        rebuildRequested =
            true;

        rebindRequested =
            true;

        ScheduleRefresh();
    }

    public static void RequestRebind()
    {
        rebindRequested =
            true;

        ScheduleRefresh();
    }

    public static void RefreshNow()
    {
        rebuildRequested =
            true;

        rebindRequested =
            true;

        ExecuteRefresh();
    }

    /*
     * Releases transient editor preview resources without
     * changing the user's Enabled preference.
     *
     * The service can be used again later in the same editor
     * session by RequestRebuild() / RequestRebind().
     */
    public static void Shutdown()
    {
        refreshScheduled =
            false;

        rebuildRequested =
            true;

        rebindRequested =
            true;

        ReleaseBinding();
        ReleaseCache();

        SetStatus(
            TerrainAuthoringPreviewStatus.Disabled,
            "Terrain authoring preview resources were released."
        );

        RepaintEditorViews();
    }

    // =====================================================
    // SCHEDULE
    // =====================================================

    private static void ScheduleRefresh()
    {
        if (refreshScheduled)
        {
            return;
        }

        refreshScheduled =
            true;

        EditorApplication.delayCall +=
            ExecuteScheduledRefresh;
    }

    private static void ExecuteScheduledRefresh()
    {
        refreshScheduled =
            false;

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            ScheduleRefresh();

            return;
        }

        ExecuteRefresh();
    }

    // =====================================================
    // REFRESH
    // =====================================================

    private static void ExecuteRefresh()
    {
        if (!Enabled)
        {
            ReleaseBinding();
            ReleaseCache();

            SetStatus(
                TerrainAuthoringPreviewStatus.Disabled,
                "Terrain authoring preview is disabled."
            );

            RepaintEditorViews();

            return;
        }

        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            ReleaseBinding();
            ReleaseCache();

            SetStatus(
                TerrainAuthoringPreviewStatus.PlayMode,
                "The editor preview is inactive in Play Mode. " +
                "TerrainHeightmapStreamer owns the clipmap " +
                "height cache while the game is running."
            );

            RepaintEditorViews();

            return;
        }

        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        worldSettings,
                        authoringData
                    );

        if (
            authoringStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            ReleaseBinding();
            ReleaseCache();

            SetStatus(
                TerrainAuthoringPreviewStatus.AuthoringUnavailable,
                "The committed authoring heightfield is not " +
                "current. Initialize or reinitialize the " +
                "authoring heightfield before building the " +
                "edit-mode preview."
            );

            RepaintEditorViews();

            return;
        }

        Transform clipmapRoot =
            FindActiveSceneClipmapRoot();

        if (clipmapRoot == null)
        {
            ReleaseBinding();

            SetStatus(
                TerrainAuthoringPreviewStatus.ClipmapUnavailable,
                "WorldRoot/Clipmap was not found in the active " +
                "scene. Generate clipmap meshes and run " +
                "Sync World Hierarchy."
            );

            RepaintEditorViews();

            return;
        }

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility
                .GetCurrentAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        bool cacheNeedsBuild =
            previewCache == null
            ||
            !previewCache.IsReady
            ||
            rebuildRequested
            ||
            previewCache.SourceAuthoringSignature !=
                currentAuthoringSignature;

        if (cacheNeedsBuild)
        {
            /*
             * Stop sampling the previous editor cache before the
             * cache object replaces/destroys its old GPU texture.
             * Rebinding occurs immediately after a successful
             * synchronous rebuild.
             */
            ReleaseBinding();

            rebindRequested =
                true;

            TerrainAuthoringPreviewCache newCache =
                previewCache
                ??
                new TerrainAuthoringPreviewCache();

            if (
                !newCache.TryBuild(
                    worldSettings,
                    authoringData,
                    out string buildError
                )
            )
            {
                ReleaseBinding();

                /*
                 * If this was a new cache object and its build
                 * failed, release any transient resources it may
                 * have created.
                 */
                if (previewCache == null)
                {
                    newCache.Dispose();
                }
                else
                {
                    ReleaseCache();
                }

                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    buildError
                );

                RepaintEditorViews();

                return;
            }

            previewCache =
                newCache;

            rebuildRequested =
                false;

            rebindRequested =
                true;
        }

        bool rootChanged =
            boundClipmapRoot !=
            clipmapRoot;

        if (
            rootChanged
            ||
            rebindRequested
        )
        {
            if (
                boundClipmapRoot != null
                &&
                boundClipmapRoot != clipmapRoot
            )
            {
                ReleaseBinding();
            }

            if (
                !BindPreviewToClipmap(
                    clipmapRoot,
                    out string bindError
                )
            )
            {
                SetStatus(
                    TerrainAuthoringPreviewStatus.Error,
                    bindError
                );

                RepaintEditorViews();

                return;
            }

            rebindRequested =
                false;
        }

        SetStatus(
            TerrainAuthoringPreviewStatus.Ready,
            "Committed authoring height data is bound directly " +
            "to the edit-mode clipmap."
        );

        RepaintEditorViews();
    }

    // =====================================================
    // BIND PREVIEW
    // =====================================================

    private static bool BindPreviewToClipmap(
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            clipmapRoot == null
            ||
            previewCache == null
            ||
            !previewCache.IsReady
        )
        {
            errorMessage =
                "The editor preview cannot bind because the " +
                "clipmap or preview cache is unavailable.";

            return false;
        }

        TerrainClipmapBoundsController boundsController =
            clipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController == null)
        {
            errorMessage =
                "TerrainClipmapBoundsController is missing from " +
                "WorldRoot/Clipmap. Run Sync World Hierarchy.";

            return false;
        }

        if (
            !TerrainHeightCacheBindingUtility
                .TryBind(
                    clipmapRoot,
                    previewCache.HeightCache,
                    previewCache.CacheOriginTile,
                    previewCache.CacheSize,
                    previewCache.SamplesPerSide,
                    previewCache.SampleSpacing,
                    previewCache.WorldSizeXZ,
                    out _,
                    out string bindingError
                )
        )
        {
            errorMessage =
                "The editor preview height cache could not be " +
                "bound to the clipmap.\n\n" +
                bindingError;

            return false;
        }

        if (
            !boundsController
                .ApplyBoundsForRange(
                    previewCache.MinimumHeight,
                    previewCache.MaximumHeight
                )
        )
        {
            TerrainHeightCacheBindingUtility
                .Disable(
                    clipmapRoot
                );

            errorMessage =
                "The editor preview cache was created, but the " +
                "clipmap displacement bounds could not be applied.";

            return false;
        }

        boundClipmapRoot =
            clipmapRoot;

        return true;
    }

    // =====================================================
    // RELEASE BINDING
    // =====================================================

    private static void ReleaseBinding()
    {
        if (boundClipmapRoot == null)
        {
            boundClipmapRoot =
                null;

            return;
        }

        TerrainHeightCacheBindingUtility
            .Disable(
                boundClipmapRoot
            );

        TerrainClipmapBoundsController boundsController =
            boundClipmapRoot
                .GetComponent<TerrainClipmapBoundsController>();

        if (boundsController != null)
        {
            boundsController
                .RestoreConfiguredBounds();
        }

        boundClipmapRoot =
            null;
    }

    // =====================================================
    // RELEASE CACHE
    // =====================================================

    private static void ReleaseCache()
    {
        if (previewCache == null)
        {
            return;
        }

        previewCache.Dispose();

        previewCache =
            null;
    }

    // =====================================================
    // FIND ACTIVE CLIPMAP
    // =====================================================

    private static Transform FindActiveSceneClipmapRoot()
    {
        Scene scene =
            SceneManager.GetActiveScene();

        if (
            !scene.IsValid()
            ||
            !scene.isLoaded
        )
        {
            return null;
        }

        foreach (
            GameObject rootObject
            in scene.GetRootGameObjects()
        )
        {
            if (
                rootObject == null
                ||
                rootObject.name !=
                    TerrainWorldHierarchyGenerator
                        .WorldRootName
            )
            {
                continue;
            }

            foreach (
                Transform child
                in rootObject.transform
            )
            {
                if (
                    child != null
                    &&
                    child.name ==
                        TerrainWorldHierarchyGenerator
                            .ClipmapRootName
                )
                {
                    return child;
                }
            }

            return null;
        }

        return null;
    }

    // =====================================================
    // EDITOR EVENTS
    // =====================================================

    private static void OnHierarchyChanged()
    {
        RequestRebind();
    }

    private static void OnProjectChanged()
    {
        RequestRefresh();
    }

    private static void OnUndoRedo()
    {
        RequestRefresh();
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            case PlayModeStateChange.EnteredPlayMode:
            {
                ReleaseBinding();
                ReleaseCache();

                SetStatus(
                    TerrainAuthoringPreviewStatus.PlayMode,
                    "The editor preview released its height " +
                    "cache for Play Mode."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.EnteredEditMode:
            {
                RequestRebuild();

                break;
            }
        }
    }

    private static void OnBeforeAssemblyReload()
    {
        ReleaseBinding();
        ReleaseCache();
    }

    private static void OnEditorQuitting()
    {
        ReleaseBinding();
        ReleaseCache();
    }

    // =====================================================
    // STATUS / REPAINT
    // =====================================================

    private static void SetStatus(
        TerrainAuthoringPreviewStatus newStatus,
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
