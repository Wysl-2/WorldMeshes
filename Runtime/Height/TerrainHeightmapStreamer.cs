using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainHeightmapStreamer :
    MonoBehaviour
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [Header("Source Data")]

    [SerializeField]
    private WorldSettings worldSettings;

    [SerializeField]
    private TerrainHeightmapManifest heightmapManifest;

    // =====================================================
    // STREAMING
    // =====================================================

    [Header("Streaming")]

    /*
     * The position around which fallback terrain coverage is calculated.
     * TerrainClipmapController normally supplies complete independently
     * snapped layouts directly.
     */
    [SerializeField]
    private Transform streamingTarget;

    /*
     * Additional page margin around required terrain coverage.
     */
    [SerializeField]
    [Min(0)]
    private int guardTileCount =
        1;

    [SerializeField]
    private bool logCacheUpdates =
        true;

    // =====================================================
    // CACHE INSPECTION
    // =====================================================

    /*
     * Manual development tools may temporarily lock the current terrain
     * caches while inspecting or reading them. Normal runtime streaming
     * never enables this state.
     */
    private bool cacheInspectionActive;

    // =====================================================
    // RUNTIME COORDINATION
    // =====================================================

    private Coroutine loadRoutine;

    /*
     * TerrainClipmapController may request coverage for the position the
     * clipmap wants to occupy. This remains separate from transform.position
     * so streaming can continue while movement waits at a coverage boundary.
     */
    private bool hasRequestedClipmapCenter;

    private Vector3 requestedClipmapCenter;

    private bool initialized;

    private bool hasPublishedActiveCacheCoverage;

    private Vector2 publishedActiveCacheMinimumXZ =
        Vector2.zero;

    private Vector2 publishedActiveCacheMaximumXZ =
        Vector2.zero;

    // =====================================================
    // SHADER BINDING STATE
    // =====================================================

    private bool shaderCacheBound;

    // =====================================================
    // PUBLIC RUNTIME STATE
    // =====================================================

    public event System.Action ActiveCacheCoverageChanged;

    /*
     * Compatibility inspection properties expose LOD0 directly without
     * maintaining a second singular Height cache state. Production terrain
     * residency is owned exclusively by heightLodStates.
     */
    public Texture2DArray HeightCache =>
        TryGetLod0State(out TerrainHeightLodRuntimeState state)
            ? state.ActiveCache
            : null;

    public Vector2Int CacheOriginTile =>
        TryGetLod0State(out TerrainHeightLodRuntimeState state)
            ? state.ActiveCacheOrigin
            : Vector2Int.zero;

    public int CacheWidth =>
        TryGetLod0State(out TerrainHeightLodRuntimeState state)
            ? state.CacheWidth
            : 0;

    public int CacheHeight =>
        TryGetLod0State(out TerrainHeightLodRuntimeState state)
            ? state.CacheHeight
            : 0;

    public bool CacheReady =>
        TryGetLod0State(out TerrainHeightLodRuntimeState state)
        && state.CacheReady
        && state.ActiveCache != null;

    public bool IsLoading =>
        loadRoutine != null;

    public bool IsCacheInspectionActive =>
        cacheInspectionActive;

    private bool TryGetLod0State(
        out TerrainHeightLodRuntimeState state
    )
    {
        state =
            heightLodStates != null
            && heightLodStates.Length > 0
                ? heightLodStates[0]
                : null;

        return state != null;
    }

    // =====================================================
    // ACTIVE HEIGHT CACHE WORLD COVERAGE
    // =====================================================

    /*
     * Returns the union of the currently ready per-LOD Height cache
     * rectangles. This is a visualization/diagnostic extent only; exact
     * gameplay movement safety uses CanActiveCachesCoverLayout().
     */
    public bool TryGetActiveCacheWorldCoverage(
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        if (
            heightLodStates == null
            || heightLodStates.Length == 0
            || heightmapManifest == null
        )
        {
            return false;
        }

        bool haveCoverage =
            false;

        Vector2 aggregateMinimum =
            new Vector2(
                float.PositiveInfinity,
                float.PositiveInfinity
            );

        Vector2 aggregateMaximum =
            new Vector2(
                float.NegativeInfinity,
                float.NegativeInfinity
            );

        for (
            int level = 0;
            level < heightLodStates.Length;
            level++
        )
        {
            TerrainHeightLodRuntimeState state =
                heightLodStates[level];

            if (
                state == null
                || !state.CacheReady
                || state.ActiveCache == null
                || state.CacheWidth <= 0
                || state.CacheHeight <= 0
            )
            {
                continue;
            }

            TerrainHeightPageRect pages =
                state.ActiveCachePages;

            if (!pages.IsValid)
            {
                continue;
            }

            float tileWorldSize =
                Mathf.Max(
                    0.0001f,
                    state.Descriptor.TileWorldSize
                );

            Vector2 stateMinimum =
                new Vector2(
                    pages.Minimum.x * tileWorldSize,
                    pages.Minimum.y * tileWorldSize
                );

            Vector2 stateMaximum =
                new Vector2(
                    Mathf.Min(
                        heightmapManifest.WorldSizeX,
                        (pages.Maximum.x + 1) * tileWorldSize
                    ),
                    Mathf.Min(
                        heightmapManifest.WorldSizeZ,
                        (pages.Maximum.y + 1) * tileWorldSize
                    )
                );

            aggregateMinimum.x =
                Mathf.Min(
                    aggregateMinimum.x,
                    stateMinimum.x
                );

            aggregateMinimum.y =
                Mathf.Min(
                    aggregateMinimum.y,
                    stateMinimum.y
                );

            aggregateMaximum.x =
                Mathf.Max(
                    aggregateMaximum.x,
                    stateMaximum.x
                );

            aggregateMaximum.y =
                Mathf.Max(
                    aggregateMaximum.y,
                    stateMaximum.y
                );

            haveCoverage =
                true;
        }

        if (!haveCoverage)
        {
            return false;
        }

        minimumXZ =
            aggregateMinimum;

        maximumXZ =
            aggregateMaximum;

        return true;
    }

    private void NotifyActiveCacheCoverageIfChanged()
    {
        bool hasCoverage =
            TryGetActiveCacheWorldCoverage(
                out Vector2 minimumXZ,
                out Vector2 maximumXZ
            );

        bool unchanged =
            hasPublishedActiveCacheCoverage ==
                hasCoverage
            &&
            (
                !hasCoverage
                ||
                (
                    publishedActiveCacheMinimumXZ ==
                        minimumXZ
                    &&
                    publishedActiveCacheMaximumXZ ==
                        maximumXZ
                )
            );

        if (unchanged)
        {
            return;
        }

        hasPublishedActiveCacheCoverage =
            hasCoverage;

        publishedActiveCacheMinimumXZ =
            hasCoverage
                ? minimumXZ
                : Vector2.zero;

        publishedActiveCacheMaximumXZ =
            hasCoverage
                ? maximumXZ
                : Vector2.zero;

        ActiveCacheCoverageChanged?.Invoke();
    }

    // =====================================================
    // REQUEST CLIPMAP COVERAGE
    // =====================================================

    public void RequestCoverageForClipmapCenter(
        Vector3 clipmapCenter
    )
    {
        requestedClipmapCenter =
            clipmapCenter;

        hasRequestedClipmapCenter =
            true;

        if (
            !Application.isPlaying
            || !initialized
            || cacheInspectionActive
        )
        {
            return;
        }

        TryBeginRequestedCacheTransition();
    }

    public void ClearCoverageRequest()
    {
        hasRequestedClipmapCenter =
            false;
    }

    /*
     * Compatibility query for callers that still provide only a center
     * position. It resolves a complete clipmap layout and validates the
     * per-LOD active Height caches rather than consulting an LOD0 window.
     */
    public bool CanActiveCacheCoverClipmapAt(
        Vector3 clipmapCenter
    )
    {
        if (
            worldSettings == null
            || heightLodStates == null
        )
        {
            return false;
        }

        TerrainClipmapLayout layout =
            new TerrainClipmapLayout();

        if (
            !TerrainClipmapLayoutUtility
                .TryCalculateLayout(
                    worldSettings,
                    clipmapCenter,
                    transform.position.y,
                    layout,
                    out _
                )
        )
        {
            return false;
        }

        return
            CanActiveHeightCachesCoverLayout(
                layout
            );
    }

    // =====================================================
    // EDITOR / HIERARCHY CONFIGURATION
    // =====================================================

    public bool Configure(
        WorldSettings settings,
        TerrainHeightmapManifest manifest
    )
    {
        bool changed =
            false;

        if (worldSettings != settings)
        {
            worldSettings =
                settings;

            changed =
                true;
        }

        if (heightmapManifest != manifest)
        {
            heightmapManifest =
                manifest;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // UNITY LIFECYCLE
    // =====================================================

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!InitializeStreamer())
        {
            enabled =
                false;

            return;
        }

        TryBeginRequestedCacheTransition();
    }

    private void Update()
    {
        if (
            !Application.isPlaying
            || !initialized
        )
        {
            return;
        }

        UpdateShaderCacheBindingState();

        if (cacheInspectionActive)
        {
            return;
        }

        TryBeginRequestedCacheTransition();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ShutdownStreamer();
    }

    // =====================================================
    // SHADER CACHE BINDING
    // =====================================================

    private void UpdateShaderCacheBindingState()
    {
        if (multiresolutionBindingPending)
        {
            return;
        }

        bool shouldBeBound =
            AreAllActiveHeightLodCachesReady()
            && surfaceCacheReady
            && surfaceMaskCache != null;

        if (shouldBeBound)
        {
            if (!shaderCacheBound)
            {
                BindHeightCacheToClipmapRenderers();
            }

            return;
        }

        if (shaderCacheBound)
        {
            DisableHeightCacheOnClipmapRenderers();
        }
    }

    private void BindHeightCacheToClipmapRenderers()
    {
        BindMultiresolutionHeightCachesToClipmapRenderers();
    }

    private void DisableHeightCacheOnClipmapRenderers()
    {
        if (!shaderCacheBound)
        {
            return;
        }

        TerrainHeightCacheBindingUtility
            .Disable(
                transform
            );

        DisableSurfaceMaskOnClipmapRenderers();

        shaderCacheBound =
            false;
    }

    // =====================================================
    // INITIALIZATION
    // =====================================================

    private bool InitializeStreamer()
    {
        initialized =
            false;

        surfaceCacheReady =
            false;

        surfaceTransitionState =
            TerrainSurfaceCacheTransitionState.Idle;

        if (worldSettings == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "WorldSettings is not assigned.",
                this
            );

            return false;
        }

        if (heightmapManifest == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainHeightmapManifest is not assigned.\n\n" +
                "Open Runtime and run Bake Runtime Changes. If Runtime reports " +
                "a structural hierarchy problem, run Setup / Repair World Hierarchy.",
                this
            );

            return false;
        }

        if (!heightmapManifest.isComplete)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The heightmap manifest is marked incomplete.",
                this
            );

            return false;
        }

        if (!ValidateSurfaceMaskConfiguration())
        {
            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The current graphics device does not support Texture2DArray textures.",
                this
            );

            return false;
        }

        if (
            !SystemInfo.SupportsTextureFormat(
                TextureFormat.RFloat
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The current graphics device does not support TextureFormat.RFloat.",
                this
            );

            return false;
        }

        if (
            SystemInfo.copyTextureSupport ==
            CopyTextureSupport.None
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The current graphics device does not support Graphics.CopyTexture.",
                this
            );

            return false;
        }

        if (
            heightmapManifest.heightTileGridWidth <= 0
            || heightmapManifest.heightTileGridHeight <= 0
            || heightmapManifest.heightTileSamplesPerSide <= 1
            || heightmapManifest.heightTileWorldSize <= 0f
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The heightmap manifest contains an invalid tile layout.",
                this
            );

            return false;
        }

        if (!InitializeMultiresolutionHeightRuntime())
        {
            return false;
        }

        CalculateSurfaceCacheDimensions();

        if (
            surfaceCacheWidth <= 0
            || surfaceCacheHeight <= 0
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "Calculated surface-mask cache dimensions are invalid.",
                this
            );

            ShutdownMultiresolutionHeightRuntime();

            return false;
        }

        if (!CreateSurfaceMaskCacheBuffers())
        {
            ShutdownMultiresolutionHeightRuntime();

            return false;
        }

        initialized =
            true;

        return true;
    }

    // =====================================================
    // SHARED CACHE RESOURCE HELPERS
    // =====================================================

    private static Texture2DArray CreateHeightCacheTexture(
        string textureName,
        int samplesPerSide,
        int sliceCount
    )
    {
        Texture2DArray cache =
            new Texture2DArray(
                samplesPerSide,
                samplesPerSide,
                sliceCount,
                TextureFormat.RFloat,
                false,
                true
            );

        cache.name =
            textureName;

        cache.wrapMode =
            TextureWrapMode.Clamp;

        cache.filterMode =
            FilterMode.Point;

        cache.anisoLevel =
            0;

        cache.Apply(
            false,
            true
        );

        return cache;
    }

    private float CalculateClipmapDiameter()
    {
        return
            TerrainClipmapLayoutUtility
                .CalculateClipmapDiameter(
                    worldSettings
                );
    }

    private static bool TryResolveCacheSlice(
        Vector2Int tileCoordinate,
        Vector2Int cacheOrigin,
        int cacheWidth,
        int cacheHeight,
        out int slice
    )
    {
        slice =
            -1;

        if (
            cacheWidth <= 0
            || cacheHeight <= 0
        )
        {
            return false;
        }

        Vector2Int local =
            tileCoordinate -
            cacheOrigin;

        if (
            local.x < 0
            || local.y < 0
            || local.x >= cacheWidth
            || local.y >= cacheHeight
        )
        {
            return false;
        }

        slice =
            local.x +
            local.y * cacheWidth;

        return true;
    }

    // =====================================================
    // CACHE INSPECTION
    // =====================================================

    internal void EndCacheInspection()
    {
        cacheInspectionActive =
            false;
    }

    // =====================================================
    // SHUTDOWN
    // =====================================================

    private void ShutdownStreamer()
    {
        if (loadRoutine != null)
        {
            StopCoroutine(
                loadRoutine
            );

            loadRoutine =
                null;
        }

        cacheInspectionActive =
            false;

        hasRequestedClipmapCenter =
            false;

        ShutdownMultiresolutionHeightRuntime();

        ReleaseResidentSurfaceTiles();
        DestroySurfaceMaskCacheBuffers();

        initialized =
            false;

        surfaceCacheReady =
            false;

        surfaceCacheOriginTile =
            Vector2Int.zero;

        requestedSurfaceOriginTile =
            Vector2Int.zero;

        surfaceTransitionState =
            TerrainSurfaceCacheTransitionState.Idle;

        NotifyActiveCacheCoverageIfChanged();
        NotifyActiveSurfaceCacheCoverageIfChanged();
    }
}
