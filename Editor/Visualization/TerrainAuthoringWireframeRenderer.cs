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
 * The renderer deliberately does not rely on Unity's Scene View
 * wireframe mode. It creates transient proxy meshes whose second
 * submesh contains deduplicated triangle edges with MeshTopology.Lines.
 *
 * The proxy meshes preserve:
 *
 * - source vertex positions
 * - source triangle topology
 * - UV channel 3 / TEXCOORD3 adaptive stitch weights
 *
 * Rendering reuses the source terrain renderer's current
 * MaterialPropertyBlock so the wireframe receives the same:
 *
 * - height cache
 * - world bounds
 * - clipmap transition offset
 *
 * The source renderer's current localToWorldMatrix supplies the
 * independently-snapped LOD placement from the normal clipmap hierarchy.
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

    private static readonly int WireframeColorPropertyId =
        Shader.PropertyToID(
            "_WireframeColor"
        );

    private static readonly int WireframeOpacityPropertyId =
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

    private static readonly Dictionary<int, WireframeEntry>
        entries =
            new Dictionary<int, WireframeEntry>();

    private static Material lineMaterial;

    private static Material depthMaterial;

    private static MaterialPropertyBlock drawPropertyBlock;

    private static MaterialPropertyBlock sourcePropertyBlock;

    private static bool reapplyScheduled;

    private static bool suspendedForPlayMode;

    private static bool isApplyingState;

    private static TerrainAuthoringWireframeStatus status =
        TerrainAuthoringWireframeStatus.Disabled;

    private static string statusMessage =
        "True displaced wireframe is disabled.";

    private static int sourceRendererCount;

    private static int cachedProxyMeshCount;

    private static int cachedEdgeCount;

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
                    Mathf.Clamp01(
                        value.r
                    ),
                    Mathf.Clamp01(
                        value.g
                    ),
                    Mathf.Clamp01(
                        value.b
                    ),
                    1f
                );

            Color current =
                WireframeColor;

            if (
                Mathf.Approximately(
                    current.r,
                    safeColor.r
                )
                &&
                Mathf.Approximately(
                    current.g,
                    safeColor.g
                )
                &&
                Mathf.Approximately(
                    current.b,
                    safeColor.b
                )
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
                float.IsNaN(
                    value
                )
                ||
                float.IsInfinity(
                    value
                )
                    ? DefaultOpacity
                    : Mathf.Clamp01(
                        value
                    );

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
    // PUBLIC STATUS
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
                    return
                        "Ready";

                case TerrainAuthoringWireframeStatus.ClipmapUnavailable:
                    return
                        "Clipmap Unavailable";

                case TerrainAuthoringWireframeStatus.ShaderUnavailable:
                    return
                        "Shader Unavailable";

                case TerrainAuthoringWireframeStatus.PlayMode:
                    return
                        "Runtime Terrain";

                case TerrainAuthoringWireframeStatus.Error:
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

    public static int CachedProxyMeshCount
    {
        get
        {
            return
                cachedProxyMeshCount;
        }
    }

    public static int CachedEdgeCount
    {
        get
        {
            return
                cachedEdgeCount;
        }
    }

    // =====================================================
    // PUBLIC COMMANDS
    // =====================================================

    public static void RequestReapply()
    {
        ScheduleReapply();
    }

    public static void RequestRebuild()
    {
        DestroyProxyMeshes();

        RequestReapply();
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

                ReleaseTransientResources();

                SetStatus(
                    TerrainAuthoringWireframeStatus.Disabled,
                    "True displaced wireframe is disabled."
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
                !SynchronizeProxyMeshes(
                    clipmapRoot,
                    out string proxyError
                )
            )
            {
                SetFillSuppression(
                    false
                );

                SetStatus(
                    TerrainAuthoringWireframeStatus.Error,
                    proxyError
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
                    ? "Wireframe Only is active. A transient displaced depth proxy preserves terrain self-occlusion while the normal terrain fill is suppressed."
                    : "Overlay mode is active. The transient line proxy is rendered over the normal displaced terrain."
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
            status !=
                TerrainAuthoringWireframeStatus.Ready
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
            currentEvent.type !=
                EventType.Repaint
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

        EnsurePropertyBlocks();

        Color color =
            WireframeColor;

        float opacity =
            WireframeOpacity;

        bool wireframeOnly =
            Mode ==
                TerrainAuthoringWireframeMode.WireframeOnly;

        foreach (
            WireframeEntry entry
            in entries.Values
        )
        {
            if (
                entry == null
                ||
                entry.SourceRenderer == null
                ||
                entry.ProxyMesh == null
            )
            {
                continue;
            }

            MeshRenderer sourceRenderer =
                entry.SourceRenderer;

            if (
                !sourceRenderer.enabled
                ||
                !sourceRenderer.gameObject.activeInHierarchy
            )
            {
                continue;
            }

            MeshFilter meshFilter =
                sourceRenderer
                    .GetComponent<MeshFilter>();

            if (
                meshFilter == null
                ||
                meshFilter.sharedMesh == null
                ||
                meshFilter.sharedMesh !=
                    entry.SourceMesh
            )
            {
                RequestReapply();

                continue;
            }

            Bounds sourceBounds =
                sourceRenderer.localBounds;

            if (
                entry.ProxyMesh.bounds !=
                sourceBounds
            )
            {
                /*
                 * Mirror the source renderer's current conservative
                 * GPU-displacement bounds. TerrainClipmapBoundsController
                 * owns vertical expansion and the layout applier owns
                 * extra stitch X/Z expansion.
                 */
                entry.ProxyMesh.bounds =
                    sourceBounds;
            }

            sourceRenderer.GetPropertyBlock(
                drawPropertyBlock
            );

            drawPropertyBlock.SetColor(
                WireframeColorPropertyId,
                color
            );

            drawPropertyBlock.SetFloat(
                WireframeOpacityPropertyId,
                opacity
            );

            Matrix4x4 matrix =
                sourceRenderer.localToWorldMatrix;

            int layer =
                sourceRenderer.gameObject.layer;

            if (wireframeOnly)
            {
                RenderParams depthParams =
                    CreateRenderParams(
                        depthMaterial,
                        sceneView.camera,
                        layer,
                        drawPropertyBlock
                    );

                Graphics.RenderMesh(
                    depthParams,
                    entry.ProxyMesh,
                    0,
                    matrix
                );
            }

            RenderParams lineParams =
                CreateRenderParams(
                    lineMaterial,
                    sceneView.camera,
                    layer,
                    drawPropertyBlock
                );

            Graphics.RenderMesh(
                lineParams,
                entry.ProxyMesh,
                1,
                matrix
            );
        }
    }

    private static RenderParams CreateRenderParams(
        Material material,
        Camera camera,
        int layer,
        MaterialPropertyBlock propertyBlock
    )
    {
        RenderParams renderParams =
            new RenderParams(
                material
            );

        renderParams.camera =
            camera;

        renderParams.layer =
            layer;

        renderParams.matProps =
            propertyBlock;

        renderParams.shadowCastingMode =
            ShadowCastingMode.Off;

        renderParams.receiveShadows =
            false;

        renderParams.lightProbeUsage =
            LightProbeUsage.Off;

        renderParams.reflectionProbeUsage =
            ReflectionProbeUsage.Off;

        return
            renderParams;
    }

    // =====================================================
    // PROXY MESH SYNCHRONIZATION
    // =====================================================

    private static bool SynchronizeProxyMeshes(
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

            cachedProxyMeshCount =
                entries.Count;

            cachedEdgeCount =
                CalculateCachedEdgeCount();

            errorMessage =
                "No MeshRenderer components were found under WorldRoot/Clipmap.";

            return false;
        }

        HashSet<int> seenRendererIds =
            new HashSet<int>();

        int compatibleRendererCount =
            0;

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
                entries.TryGetValue(
                    rendererId,
                    out WireframeEntry existingEntry
                )
                &&
                existingEntry != null
                &&
                existingEntry.SourceRenderer ==
                    renderer
                &&
                existingEntry.SourceMesh ==
                    sourceMesh
                &&
                existingEntry.ProxyMesh !=
                    null
            )
            {
                continue;
            }

            if (existingEntry != null)
            {
                DestroyEntry(
                    existingEntry
                );

                entries.Remove(
                    rendererId
                );
            }

            if (
                !TryBuildProxyMesh(
                    renderer,
                    sourceMesh,
                    out WireframeEntry newEntry,
                    out string buildError
                )
            )
            {
                errorMessage =
                    "Could not build the transient displaced wireframe proxy.\n\n" +
                    buildError;

                return false;
            }

            entries.Add(
                rendererId,
                newEntry
            );
        }

        List<int> staleIds =
            new List<int>();

        foreach (
            KeyValuePair<int, WireframeEntry> pair
            in entries
        )
        {
            if (
                !seenRendererIds.Contains(
                    pair.Key
                )
            )
            {
                staleIds.Add(
                    pair.Key
                );
            }
        }

        foreach (
            int staleId
            in staleIds
        )
        {
            if (
                entries.TryGetValue(
                    staleId,
                    out WireframeEntry staleEntry
                )
            )
            {
                DestroyEntry(
                    staleEntry
                );
            }

            entries.Remove(
                staleId
            );
        }

        sourceRendererCount =
            compatibleRendererCount;

        cachedProxyMeshCount =
            entries.Count;

        cachedEdgeCount =
            CalculateCachedEdgeCount();

        if (
            compatibleRendererCount <= 0
            ||
            entries.Count <= 0
        )
        {
            errorMessage =
                "No compatible ClipmapTerrain renderers were found. The terrain material must expose the Stage 6 _AuthoringWireframeOnly property.";

            return false;
        }

        return true;
    }

    private static bool TryBuildProxyMesh(
        MeshRenderer renderer,
        Mesh sourceMesh,
        out WireframeEntry entry,
        out string errorMessage
    )
    {
        entry =
            null;

        errorMessage =
            "";

        if (
            renderer == null
            ||
            sourceMesh == null
        )
        {
            errorMessage =
                "The source renderer or mesh is null.";

            return false;
        }

        if (!sourceMesh.isReadable)
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' is not readable in the Editor.";

            return false;
        }

        List<Vector3> vertices =
            new List<Vector3>();

        sourceMesh.GetVertices(
            vertices
        );

        if (vertices.Count <= 0)
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' has no vertices.";

            return false;
        }

        List<Vector4> clipmapData =
            new List<Vector4>();

        sourceMesh.GetUVs(
            3,
            clipmapData
        );

        if (
            clipmapData.Count !=
            vertices.Count
        )
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' does not contain one UV3/TEXCOORD3 clipmap-data value per vertex.\n\n" +
                $"Vertices: {vertices.Count}\n" +
                $"Clipmap Data: {clipmapData.Count}";

            return false;
        }

        List<int> triangleIndices =
            new List<int>();

        HashSet<ulong> uniqueEdges =
            new HashSet<ulong>();

        List<int> lineIndices =
            new List<int>();

        int subMeshCount =
            sourceMesh.subMeshCount;

        for (
            int subMeshIndex = 0;
            subMeshIndex < subMeshCount;
            subMeshIndex++
        )
        {
            if (
                sourceMesh.GetTopology(
                    subMeshIndex
                )
                !=
                MeshTopology.Triangles
            )
            {
                continue;
            }

            int[] sourceIndices =
                sourceMesh.GetIndices(
                    subMeshIndex
                );

            for (
                int index = 0;
                index + 2 < sourceIndices.Length;
                index += 3
            )
            {
                int a =
                    sourceIndices[
                        index
                    ];

                int b =
                    sourceIndices[
                        index + 1
                    ];

                int c =
                    sourceIndices[
                        index + 2
                    ];

                triangleIndices.Add(
                    a
                );

                triangleIndices.Add(
                    b
                );

                triangleIndices.Add(
                    c
                );

                AddUniqueEdge(
                    a,
                    b,
                    uniqueEdges,
                    lineIndices
                );

                AddUniqueEdge(
                    b,
                    c,
                    uniqueEdges,
                    lineIndices
                );

                AddUniqueEdge(
                    c,
                    a,
                    uniqueEdges,
                    lineIndices
                );
            }
        }

        if (
            triangleIndices.Count <= 0
            ||
            lineIndices.Count <= 0
        )
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' contains no triangle topology suitable for displaced wireframe generation.";

            return false;
        }

        Mesh proxyMesh =
            new Mesh();

        proxyMesh.name =
            $"WorldMeshes_Wireframe_{sourceMesh.name}_{renderer.GetInstanceID()}";

        proxyMesh.hideFlags =
            HideFlags.HideAndDontSave;

        proxyMesh.indexFormat =
            vertices.Count >
                65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

        proxyMesh.SetVertices(
            vertices
        );

        proxyMesh.SetUVs(
            3,
            clipmapData
        );

        proxyMesh.subMeshCount =
            2;

        /*
         * Submesh 0:
         * source triangle topology, used only as an invisible depth
         * proxy in Wireframe Only mode.
         */
        proxyMesh.SetIndices(
            triangleIndices,
            MeshTopology.Triangles,
            0,
            false
        );

        /*
         * Submesh 1:
         * every unique undirected triangle edge exactly once.
         */
        proxyMesh.SetIndices(
            lineIndices,
            MeshTopology.Lines,
            1,
            false
        );

        proxyMesh.bounds =
            renderer.localBounds;

        entry =
            new WireframeEntry(
                renderer,
                sourceMesh,
                proxyMesh,
                lineIndices.Count /
                    2
            );

        return true;
    }

    private static void AddUniqueEdge(
        int indexA,
        int indexB,
        HashSet<ulong> uniqueEdges,
        List<int> lineIndices
    )
    {
        if (indexA == indexB)
        {
            return;
        }

        int minimumIndex =
            Mathf.Min(
                indexA,
                indexB
            );

        int maximumIndex =
            Mathf.Max(
                indexA,
                indexB
            );

        ulong edgeKey =
            (
                (ulong)(uint)minimumIndex
                <<
                32
            )
            |
            (uint)maximumIndex;

        if (
            !uniqueEdges.Add(
                edgeKey
            )
        )
        {
            return;
        }

        lineIndices.Add(
            minimumIndex
        );

        lineIndices.Add(
            maximumIndex
        );
    }

    // =====================================================
    // SOURCE RENDERER COMPATIBILITY
    // =====================================================

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

        EnsurePropertyBlocks();

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
        DestroyProxyMeshes();
        DestroyMaterials();
    }

    private static void DestroyProxyMeshes()
    {
        foreach (
            WireframeEntry entry
            in entries.Values
        )
        {
            DestroyEntry(
                entry
            );
        }

        entries.Clear();

        sourceRendererCount =
            0;

        cachedProxyMeshCount =
            0;

        cachedEdgeCount =
            0;
    }

    private static void DestroyEntry(
        WireframeEntry entry
    )
    {
        if (
            entry == null
            ||
            entry.ProxyMesh == null
        )
        {
            return;
        }

        Object.DestroyImmediate(
            entry.ProxyMesh
        );
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

    private static int CalculateCachedEdgeCount()
    {
        int total =
            0;

        foreach (
            WireframeEntry entry
            in entries.Values
        )
        {
            if (entry != null)
            {
                total +=
                    entry.EdgeCount;
            }
        }

        return
            total;
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
         * Generated clipmap mesh assets are updated in place.
         * Their Unity object identity can therefore remain unchanged
         * while triangle topology changes. Force line topology rebuild.
         */
        DestroyProxyMeshes();

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

        DestroyProxyMeshes();

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

    private static void EnsurePropertyBlocks()
    {
        if (drawPropertyBlock == null)
        {
            drawPropertyBlock =
                new MaterialPropertyBlock();
        }

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

    // =====================================================
    // ENTRY
    // =====================================================

    private sealed class WireframeEntry
    {
        public readonly MeshRenderer SourceRenderer;

        public readonly Mesh SourceMesh;

        public readonly Mesh ProxyMesh;

        public readonly int EdgeCount;

        public WireframeEntry(
            MeshRenderer sourceRenderer,
            Mesh sourceMesh,
            Mesh proxyMesh,
            int edgeCount
        )
        {
            SourceRenderer =
                sourceRenderer;

            SourceMesh =
                sourceMesh;

            ProxyMesh =
                proxyMesh;

            EdgeCount =
                edgeCount;
        }
    }
}
