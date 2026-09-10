using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public enum TerrainAuthoringWireframeMode
{
    Overlay = 0,
    WireframeOnly = 1
}

public enum TerrainAuthoringWireframeStatus
{
    Disabled,
    Ready,
    ClipmapUnavailable,
    ShaderUnavailable,
    PlayMode,
    Error
}

/*
 * Editor-only displaced clipmap wireframe renderer.
 *
 * Spatial explicit-edge section meshes are generated persistently with the
 * clipmap mesh assets. Enabling this visualization only synchronizes
 * lightweight source-to-preview bindings and submits already-generated
 * meshes to the current Scene View.
 */
[InitializeOnLoad]
public static class TerrainAuthoringWireframeRenderer
{
    // =====================================================
    // EDITOR PREFS
    // =====================================================

    private const string EnabledEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.Enabled";

    private const string ModeEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.Mode";

    private const string ColorREditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.ColorR";

    private const string ColorGEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.ColorG";

    private const string ColorBEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.ColorB";

    private const string OpacityEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.Opacity";

    private const string WireframeShaderName =
        "Hidden/WorldMeshes/ClipmapTerrainWireframe";

    private const float DefaultOpacity =
        0.8f;

    private static readonly Color DefaultColor =
        Color.white;

    // =====================================================
    // SOURCE SHADER PROPERTY IDS
    // =====================================================

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID(
            "_HeightCacheReady"
        );

    private static readonly int WorldBoundsReadyPropertyId =
        Shader.PropertyToID(
            "_WorldBoundsReady"
        );

    private static readonly int TransitionOffsetPropertyId =
        Shader.PropertyToID(
            "_ClipmapTransitionOffset"
        );

    private static readonly int WireframeOnlyPropertyId =
        Shader.PropertyToID(
            "_AuthoringWireframeOnly"
        );

    // =====================================================
    // WIREFRAME SHADER PROPERTY IDS
    // =====================================================

    internal static readonly int WireframeColorPropertyId =
        Shader.PropertyToID(
            "_WireframeColor"
        );

    internal static readonly int WireframeOpacityPropertyId =
        Shader.PropertyToID(
            "_WireframeOpacity"
        );

    private static readonly int WireframeZWritePropertyId =
        Shader.PropertyToID(
            "_WireframeZWrite"
        );

    private static readonly int WireframeColorMaskPropertyId =
        Shader.PropertyToID(
            "_WireframeColorMask"
        );

    private static readonly int WireframeSrcBlendPropertyId =
        Shader.PropertyToID(
            "_WireframeSrcBlend"
        );

    private static readonly int WireframeDstBlendPropertyId =
        Shader.PropertyToID(
            "_WireframeDstBlend"
        );

    private static readonly int WireframeDepthBiasPropertyId =
        Shader.PropertyToID(
            "_WireframeDepthBias"
        );

    // =====================================================
    // TRANSIENT STATE
    // =====================================================

    private static Transform boundClipmapRoot;

    private static Material lineMaterial;

    private static Material depthMaterial;

    private static MaterialPropertyBlock sourcePropertyBlock;

    private static bool reapplyScheduled;

    private static bool suspendedForPlayMode;

    private static bool isApplyingState;

    private static TerrainAuthoringWireframeStatus status =
        TerrainAuthoringWireframeStatus.Disabled;

    private static string statusMessage =
        "True displaced wireframe is disabled.";

    private static int sourceRendererCount;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    static TerrainAuthoringWireframeRenderer()
    {
        SceneView.duringSceneGui +=
            OnSceneViewGUI;

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

    public static bool WireframeEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    EnabledEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (WireframeEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                EnabledEditorPrefsKey,
                value
            );

            RequestReapply();
        }
    }

    public static TerrainAuthoringWireframeMode Mode
    {
        get
        {
            int storedValue =
                EditorPrefs.GetInt(
                    ModeEditorPrefsKey,
                    (int)TerrainAuthoringWireframeMode.Overlay
                );

            if (
                storedValue <
                    (int)TerrainAuthoringWireframeMode.Overlay
                ||
                storedValue >
                    (int)TerrainAuthoringWireframeMode.WireframeOnly
            )
            {
                return
                    TerrainAuthoringWireframeMode.Overlay;
            }

            return
                (TerrainAuthoringWireframeMode)
                storedValue;
        }

        set
        {
            int integerValue =
                (int)value;

            if (
                integerValue <
                    (int)TerrainAuthoringWireframeMode.Overlay
                ||
                integerValue >
                    (int)TerrainAuthoringWireframeMode.WireframeOnly
            )
            {
                value =
                    TerrainAuthoringWireframeMode.Overlay;
            }

            if (Mode == value)
            {
                return;
            }

            EditorPrefs.SetInt(
                ModeEditorPrefsKey,
                (int)value
            );

            RequestReapply();
        }
    }

    public static Color WireframeColor
    {
        get
        {
            return
                new Color(
                    Mathf.Clamp01(
                        EditorPrefs.GetFloat(
                            ColorREditorPrefsKey,
                            DefaultColor.r
                        )
                    ),
                    Mathf.Clamp01(
                        EditorPrefs.GetFloat(
                            ColorGEditorPrefsKey,
                            DefaultColor.g
                        )
                    ),
                    Mathf.Clamp01(
                        EditorPrefs.GetFloat(
                            ColorBEditorPrefsKey,
                            DefaultColor.b
                        )
                    ),
                    1f
                );
        }

        set
        {
            Color safeColor =
                new Color(
                    Mathf.Clamp01(value.r),
                    Mathf.Clamp01(value.g),
                    Mathf.Clamp01(value.b),
                    1f
                );

            Color current =
                WireframeColor;

            if (
                Mathf.Approximately(current.r, safeColor.r)
                &&
                Mathf.Approximately(current.g, safeColor.g)
                &&
                Mathf.Approximately(current.b, safeColor.b)
            )
            {
                return;
            }

            EditorPrefs.SetFloat(
                ColorREditorPrefsKey,
                safeColor.r
            );

            EditorPrefs.SetFloat(
                ColorGEditorPrefsKey,
                safeColor.g
            );

            EditorPrefs.SetFloat(
                ColorBEditorPrefsKey,
                safeColor.b
            );

            RepaintEditorViews();
        }
    }

    public static float WireframeOpacity
    {
        get
        {
            return
                Mathf.Clamp01(
                    EditorPrefs.GetFloat(
                        OpacityEditorPrefsKey,
                        DefaultOpacity
                    )
                );
        }

        set
        {
            float safeValue =
                float.IsNaN(value)
                ||
                float.IsInfinity(value)
                    ? DefaultOpacity
                    : Mathf.Clamp01(value);

            if (
                Mathf.Approximately(
                    WireframeOpacity,
                    safeValue
                )
            )
            {
                return;
            }

            EditorPrefs.SetFloat(
                OpacityEditorPrefsKey,
                safeValue
            );

            RepaintEditorViews();
        }
    }

    // =====================================================
    // PUBLIC STATUS / DIAGNOSTICS
    // =====================================================

    public static TerrainAuthoringWireframeStatus Status
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
                case TerrainAuthoringWireframeStatus.Ready:
                    return "Ready";

                case TerrainAuthoringWireframeStatus.ClipmapUnavailable:
                    return "Clipmap Unavailable";

                case TerrainAuthoringWireframeStatus.ShaderUnavailable:
                    return "Shader Unavailable";

                case TerrainAuthoringWireframeStatus.PlayMode:
                    return "Runtime Terrain";

                case TerrainAuthoringWireframeStatus.Error:
                    return "Error";

                default:
                    return "Disabled";
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

    public static int SourceRendererCount
    {
        get
        {
            return
                sourceRendererCount;
        }
    }

    // =====================================================
    // PUBLIC COMMANDS
    // =====================================================

    public static void RequestReapply()
    {
        ScheduleReapply();
    }

    public static void InvalidateBindings()
    {
        TerrainAuthoringWireframeSectionCache
            .ClearBindings();

        RequestReapply();
    }

    internal static void RequestRepaint()
    {
        RepaintEditorViews();
    }

    // =====================================================
    // SCHEDULED STATE APPLICATION
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
                TerrainAuthoringWireframeStatus.PlayMode,
                "True displaced wireframe is disabled while runtime owns terrain rendering."
            );

            RepaintEditorViews();

            return;
        }

        ApplyState();
    }

    // =====================================================
    // APPLY EDITOR STATE
    // =====================================================

    private static void ApplyState()
    {
        if (isApplyingState)
        {
            return;
        }

        isApplyingState =
            true;

        try
        {
            if (!WireframeEnabled)
            {
                SetFillSuppression(
                    false
                );

                TerrainAuthoringWireframeCulling
                    .ResetRenderedDiagnostics();

                SetStatus(
                    TerrainAuthoringWireframeStatus.Disabled,
                    "True displaced wireframe is disabled. Generated preview assets remain available for immediate reuse."
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
                SetFillSuppression(
                    false
                );

                boundClipmapRoot =
                    null;

                SetStatus(
                    TerrainAuthoringWireframeStatus.Error,
                    sceneLookupError
                );

                RepaintEditorViews();

                return;
            }

            if (clipmapRoot == null)
            {
                SetFillSuppression(
                    false
                );

                boundClipmapRoot =
                    null;

                sourceRendererCount =
                    0;

                SetStatus(
                    TerrainAuthoringWireframeStatus.ClipmapUnavailable,
                    "WorldRoot/Clipmap was not found in the active scene. Run Setup / Repair World Hierarchy."
                );

                RepaintEditorViews();

                return;
            }

            boundClipmapRoot =
                clipmapRoot;

            if (
                !EnsureMaterials(
                    out string materialError
                )
            )
            {
                SetFillSuppression(
                    false
                );

                SetStatus(
                    TerrainAuthoringWireframeStatus.ShaderUnavailable,
                    materialError
                );

                RepaintEditorViews();

                return;
            }

            if (
                !SynchronizeSources(
                    clipmapRoot,
                    out string sourceError
                )
            )
            {
                SetFillSuppression(
                    false
                );

                SetStatus(
                    TerrainAuthoringWireframeStatus.Error,
                    sourceError
                );

                RepaintEditorViews();

                return;
            }

            bool wireframeOnly =
                Mode ==
                    TerrainAuthoringWireframeMode.WireframeOnly;

            SetFillSuppression(
                wireframeOnly
            );

            SetStatus(
                TerrainAuthoringWireframeStatus.Ready,
                wireframeOnly
                    ? "Wireframe Only is active. Generated explicit-edge sections render an invisible displaced triangle depth pass before visible line topology."
                    : "Overlay mode is active. Generated explicit-edge wireframe sections render over the normal displaced terrain."
            );

            RepaintEditorViews();
        }
        finally
        {
            isApplyingState =
                false;
        }
    }

    // =====================================================
    // SCENE VIEW DRAWING
    // =====================================================

    private static void OnSceneViewGUI(
        SceneView sceneView
    )
    {
        if (
            !WireframeEnabled
            ||
            suspendedForPlayMode
            ||
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
            ||
            status != TerrainAuthoringWireframeStatus.Ready
        )
        {
            return;
        }

        if (
            sceneView == null
            ||
            sceneView.camera == null
        )
        {
            return;
        }

        Event currentEvent =
            Event.current;

        if (
            currentEvent == null
            ||
            currentEvent.type != EventType.Repaint
        )
        {
            return;
        }

        if (
            lineMaterial == null
            ||
            depthMaterial == null
        )
        {
            RequestReapply();

            return;
        }

        TerrainAuthoringWireframeSectionCache
            .RenderSceneView(
                sceneView.camera,
                lineMaterial,
                depthMaterial,
                Mode == TerrainAuthoringWireframeMode.WireframeOnly,
                WireframeColor,
                WireframeOpacity
            );
    }

    // =====================================================
    // SOURCE SYNCHRONIZATION
    // =====================================================

    private static bool SynchronizeSources(
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

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
            sourceRendererCount =
                0;

            errorMessage =
                "No MeshRenderer components were found under WorldRoot/Clipmap.";

            return false;
        }

        HashSet<int> seenRendererIds =
            new HashSet<int>();

        int compatibleRendererCount =
            0;

        string firstPreviewError =
            "";

        foreach (
            MeshRenderer renderer
            in renderers
        )
        {
            if (
                !TryGetCompatibleSourceMesh(
                    renderer,
                    out Mesh sourceMesh
                )
            )
            {
                continue;
            }

            compatibleRendererCount++;

            int rendererId =
                renderer.GetInstanceID();

            seenRendererIds.Add(
                rendererId
            );

            if (
                !TerrainAuthoringWireframeSectionCache
                    .TryRegisterSource(
                        renderer,
                        sourceMesh,
                        clipmapRoot,
                        out string previewError
                    )
                &&
                string.IsNullOrEmpty(
                    firstPreviewError
                )
            )
            {
                firstPreviewError =
                    previewError;
            }
        }

        TerrainAuthoringWireframeSectionCache
            .RemoveStaleSources(
                seenRendererIds
            );

        sourceRendererCount =
            compatibleRendererCount;

        if (compatibleRendererCount <= 0)
        {
            errorMessage =
                "No compatible ClipmapTerrain renderers were found. The terrain material must expose the Stage 6 _AuthoringWireframeOnly property.";

            return false;
        }

        if (
            !string.IsNullOrEmpty(
                firstPreviewError
            )
        )
        {
            errorMessage =
                firstPreviewError;

            return false;
        }

        if (
            TerrainAuthoringWireframeSectionCache.SourceRendererCount <= 0
            ||
            TerrainAuthoringWireframeSectionCache.GeneratedSectionCount <= 0
        )
        {
            errorMessage =
                "Wireframe preview assets are missing or out of date. Regenerate Clipmap Meshes.";

            return false;
        }

        return true;
    }

    private static bool TryGetCompatibleSourceMesh(
        MeshRenderer renderer,
        out Mesh sourceMesh
    )
    {
        sourceMesh =
            null;

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

        if (
            !material.HasProperty(
                HeightCacheReadyPropertyId
            )
            ||
            !material.HasProperty(
                WorldBoundsReadyPropertyId
            )
            ||
            !material.HasProperty(
                TransitionOffsetPropertyId
            )
            ||
            !material.HasProperty(
                WireframeOnlyPropertyId
            )
        )
        {
            return false;
        }

        MeshFilter meshFilter =
            renderer
                .GetComponent<MeshFilter>();

        if (
            meshFilter == null
            ||
            meshFilter.sharedMesh == null
        )
        {
            return false;
        }

        sourceMesh =
            meshFilter.sharedMesh;

        return true;
    }

    // =====================================================
    // FILL SUPPRESSION
    // =====================================================

    private static void SetFillSuppression(
        bool suppress
    )
    {
        Transform root =
            boundClipmapRoot;

        if (root == null)
        {
            return;
        }

        MeshRenderer[] renderers =
            root.GetComponentsInChildren<MeshRenderer>(
                true
            );

        EnsureSourcePropertyBlock();

        foreach (
            MeshRenderer renderer
            in renderers
        )
        {
            if (
                !TryGetCompatibleSourceMesh(
                    renderer,
                    out _
                )
            )
            {
                continue;
            }

            renderer.GetPropertyBlock(
                sourcePropertyBlock
            );

            sourcePropertyBlock.SetFloat(
                WireframeOnlyPropertyId,
                suppress
                    ? 1f
                    : 0f
            );

            renderer.SetPropertyBlock(
                sourcePropertyBlock
            );
        }
    }

    // =====================================================
    // MATERIALS
    // =====================================================

    private static bool EnsureMaterials(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            lineMaterial != null
            &&
            depthMaterial != null
        )
        {
            return true;
        }

        Shader shader =
            Shader.Find(
                WireframeShaderName
            );

        if (shader == null)
        {
            errorMessage =
                $"Could not find shader '{WireframeShaderName}'.";

            return false;
        }

        DestroyMaterials();

        lineMaterial =
            new Material(
                shader
            );

        lineMaterial.name =
            "WorldMeshes Authoring Wireframe Lines";

        lineMaterial.hideFlags =
            HideFlags.HideAndDontSave;

        lineMaterial.renderQueue =
            (int)RenderQueue.Transparent +
            100;

        lineMaterial.SetFloat(
            WireframeZWritePropertyId,
            0f
        );

        lineMaterial.SetFloat(
            WireframeColorMaskPropertyId,
            15f
        );

        lineMaterial.SetFloat(
            WireframeSrcBlendPropertyId,
            (float)BlendMode.SrcAlpha
        );

        lineMaterial.SetFloat(
            WireframeDstBlendPropertyId,
            (float)BlendMode.OneMinusSrcAlpha
        );

        lineMaterial.SetFloat(
            WireframeDepthBiasPropertyId,
            0.00001f
        );

        depthMaterial =
            new Material(
                shader
            );

        depthMaterial.name =
            "WorldMeshes Authoring Wireframe Depth";

        depthMaterial.hideFlags =
            HideFlags.HideAndDontSave;

        depthMaterial.renderQueue =
            (int)RenderQueue.Geometry -
            10;

        depthMaterial.SetFloat(
            WireframeZWritePropertyId,
            1f
        );

        depthMaterial.SetFloat(
            WireframeColorMaskPropertyId,
            0f
        );

        depthMaterial.SetFloat(
            WireframeSrcBlendPropertyId,
            (float)BlendMode.One
        );

        depthMaterial.SetFloat(
            WireframeDstBlendPropertyId,
            (float)BlendMode.Zero
        );

        depthMaterial.SetFloat(
            WireframeDepthBiasPropertyId,
            0f
        );

        return true;
    }

    // =====================================================
    // RESOURCE RELEASE
    // =====================================================

    private static void ReleaseTransientResources()
    {
        TerrainAuthoringWireframeSectionCache
            .ClearBindings();

        DestroyMaterials();
    }

    private static void DestroyMaterials()
    {
        if (lineMaterial != null)
        {
            Object.DestroyImmediate(
                lineMaterial
            );

            lineMaterial =
                null;
        }

        if (depthMaterial != null)
        {
            Object.DestroyImmediate(
                depthMaterial
            );

            depthMaterial =
                null;
        }
    }

    // =====================================================
    // EDITOR EVENTS
    // =====================================================

    private static void OnHierarchyChanged()
    {
        if (isApplyingState)
        {
            return;
        }

        RequestReapply();
    }

    private static void OnProjectChanged()
    {
        /*
         * Generated preview assets are persistent. Generic project changes
         * only revalidate the existing bindings; explicit clipmap
         * regeneration calls InvalidateBindings after updating preview
         * assets.
         */
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
        SetFillSuppression(
            false
        );

        boundClipmapRoot =
            null;

        sourceRendererCount =
            0;

        TerrainAuthoringWireframeSectionCache
            .ClearBindings();

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
                SetFillSuppression(
                    false
                );

                suspendedForPlayMode =
                    true;

                ReleaseTransientResources();

                sourceRendererCount =
                    0;

                SetStatus(
                    TerrainAuthoringWireframeStatus.PlayMode,
                    "True displaced wireframe was released for Play Mode."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.EnteredPlayMode:
            {
                suspendedForPlayMode =
                    true;

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
        SetFillSuppression(
            false
        );

        ReleaseTransientResources();
    }

    private static void OnEditorQuitting()
    {
        SetFillSuppression(
            false
        );

        ReleaseTransientResources();
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static void EnsureSourcePropertyBlock()
    {
        if (sourcePropertyBlock == null)
        {
            sourcePropertyBlock =
                new MaterialPropertyBlock();
        }
    }

    private static void SetStatus(
        TerrainAuthoringWireframeStatus newStatus,
        string message
    )
    {
        status =
            newStatus;

        statusMessage =
            string.IsNullOrEmpty(message)
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
