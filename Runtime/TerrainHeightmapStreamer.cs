using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Rendering;

public class TerrainHeightmapStreamer :
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
     * The position around which the required height tiles
     * are calculated.
     *
     * If null, this GameObject's transform is used.
     *
     * For the first stationary clipmap test, leave this null.
     */
    [SerializeField]
    private Transform streamingTarget;

    /*
     * Additional tile margin around the clipmap coverage.
     *
     * A value of 1 gives us one tile of safety around the
     * required clipmap region.
     */
    [SerializeField]
    [Min(0)]
    private int guardTileCount =
        1;

    /*
     * Begin preparing a neighboring cache window before the
     * visible clipmap reaches the active cache boundary.
     *
     * Measured as a fraction of one height tile.
     *
     * 0.25 means that a transition is requested roughly one
     * quarter tile before the current cache would become unsafe.
     *
     * Keeping this below 0.5 also leaves hysteresis between
     * neighboring cache windows, avoiding rapid back-and-forth
     * reloads if the player hovers near a transition threshold.
     */
    [SerializeField]
    [Range(0f, 1f)]
    private float prefetchTileFraction =
        0.25f;

    [SerializeField]
    private bool logCacheUpdates =
        true;
    
    // =====================================================
    // CACHE INSPECTION
    // =====================================================

    /*
     * Manual development tools may temporarily lock the
     * current cache while inspecting or reading it.
     *
     * Normal runtime streaming never enables this state.
     */
    private bool cacheInspectionActive;

    // =====================================================
    // RUNTIME CACHE
    // =====================================================

    /*
     * The cache currently bound to the terrain shader.
     *
     * Once cacheReady is true this texture remains valid and
     * visible while the next window is prepared.
     */
    private Texture2DArray heightCache;

    /*
     * Inactive GPU cache used to build the next window.
     *
     * The two Texture2DArray references swap roles after each
     * successful transition. This avoids allocating/destroying
     * GPU cache textures while the player is moving.
     */
    private Texture2DArray stagingHeightCache;

    private readonly Dictionary<Vector2Int, ResidentTile>
        residentTiles =
            new Dictionary<Vector2Int, ResidentTile>();

    private Coroutine loadRoutine;

    private Vector2Int cacheOriginTile;

    private Vector2Int requestedOriginTile;

    /*
     * TerrainClipmapController may request cache coverage for
     * the position it wants the clipmap to occupy.
     *
     * This is intentionally separate from transform.position:
     * if movement is temporarily held at the active-cache edge,
     * streaming must still continue toward the player's desired
     * position.
     */
    private bool hasRequestedClipmapCenter;

    private Vector3 requestedClipmapCenter;

    private int cacheWidth;

    private int cacheHeight;

    private bool initialized;

    private bool cacheReady;
    
    // =====================================================
// SHADER BINDING
// =====================================================

    private static readonly int HeightCachePropertyId =
        Shader.PropertyToID(
            "_HeightCache"
        );

    private static readonly int HeightCacheOriginTilePropertyId =
        Shader.PropertyToID(
            "_HeightCacheOriginTile"
        );

    private static readonly int HeightCacheSizePropertyId =
        Shader.PropertyToID(
            "_HeightCacheSize"
        );

    private static readonly int HeightTileSamplesPerSidePropertyId =
        Shader.PropertyToID(
            "_HeightTileSamplesPerSide"
        );

    private static readonly int HeightSampleSpacingPropertyId =
        Shader.PropertyToID(
            "_HeightSampleSpacing"
        );

    private static readonly int WorldSizeXZPropertyId =
        Shader.PropertyToID(
            "_WorldSizeXZ"
        );

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID(
            "_HeightCacheReady"
        );

    private MeshRenderer[] clipmapRenderers;

    private MaterialPropertyBlock shaderPropertyBlock;

    private bool shaderCacheBound;

    // =====================================================
    // PUBLIC RUNTIME STATE
    // =====================================================

    public Texture2DArray HeightCache
    {
        get
        {
            return heightCache;
        }
    }

    public Vector2Int CacheOriginTile
    {
        get
        {
            return cacheOriginTile;
        }
    }

    public int CacheWidth
    {
        get
        {
            return cacheWidth;
        }
    }

    public int CacheHeight
    {
        get
        {
            return cacheHeight;
        }
    }

    public bool CacheReady
    {
        get
        {
            return cacheReady;
        }
    }

    public bool IsLoading
    {
        get
        {
            return loadRoutine != null;
        }
    }
    
    public bool IsCacheInspectionActive
    {
        get
        {
            return cacheInspectionActive;
        }
    }

    // =====================================================
    // ACTIVE CACHE WORLD COVERAGE
    // =====================================================

    /*
     * Returns the nominal world-space XZ rectangle represented
     * by the currently active cache.
     *
     * maximumXZ is the geometric upper edge of the cache.
     * For exact sample-safety decisions use
     * CanActiveCacheCoverClipmapAt(), which follows the same
     * sample-to-tile mapping as the terrain shader.
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
            !cacheReady
            ||
            heightmapManifest == null
            ||
            cacheWidth <= 0
            ||
            cacheHeight <= 0
        )
        {
            return false;
        }

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                heightmapManifest.heightTileWorldSize
            );

        minimumXZ =
            new Vector2(
                cacheOriginTile.x *
                    tileWorldSize,

                cacheOriginTile.y *
                    tileWorldSize
            );

        maximumXZ =
            new Vector2(
                Mathf.Min(
                    heightmapManifest.WorldSizeX,
                    (
                        cacheOriginTile.x +
                        cacheWidth
                    )
                    *
                    tileWorldSize
                ),

                Mathf.Min(
                    heightmapManifest.WorldSizeZ,
                    (
                        cacheOriginTile.y +
                        cacheHeight
                    )
                    *
                    tileWorldSize
                )
            );

        return true;
    }

    // =====================================================
    // REQUEST CLIPMAP COVERAGE
    // =====================================================

    /*
     * Called by TerrainClipmapController with the position the
     * clipmap wants to occupy.
     *
     * The request is stored independently from the clipmap's
     * current transform so streaming can continue even if the
     * controller temporarily holds movement at a cache boundary.
     */
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
            ||
            !initialized
            ||
            cacheInspectionActive
        )
        {
            return;
        }

        Vector2Int desiredOrigin =
            CalculateRequestedCacheOrigin();

        requestedOriginTile =
            desiredOrigin;

        if (loadRoutine != null)
        {
            return;
        }

        if (
            cacheReady
            &&
            desiredOrigin ==
            cacheOriginTile
        )
        {
            return;
        }

        BeginLoadWindow(
            desiredOrigin
        );
    }

    // =====================================================
    // CLEAR CLIPMAP COVERAGE REQUEST
    // =====================================================

    public void ClearCoverageRequest()
    {
        hasRequestedClipmapCenter =
            false;
    }

    // =====================================================
    // CAN ACTIVE CACHE COVER CLIPMAP
    // =====================================================

    /*
     * Returns true only when every VISIBLE in-world part of
     * the clipmap can sample the current active cache.
     *
     * Geometry outside the authoritative world does not require
     * height data because ClipmapTerrain.shader clips those
     * fragments at the world boundary.
     *
     * One height-sample margin is included so the shader's
     * neighboring normal samples also remain inside the cache
     * wherever possible.
     */
    public bool CanActiveCacheCoverClipmapAt(
        Vector3 clipmapCenter
    )
    {
        if (
            !cacheReady
            ||
            heightCache == null
            ||
            heightmapManifest == null
        )
        {
            return false;
        }

        float sampleMargin =
            Mathf.Max(
                0f,
                heightmapManifest.HeightSampleSpacing
            );

        if (
            !TryCalculateVisibleClipmapTileBounds(
                clipmapCenter,
                sampleMargin,
                out Vector2Int minimumTile,
                out Vector2Int maximumTile
            )
        )
        {
            /*
             * The clipmap footprint does not intersect the
             * authoritative world at all.
             *
             * Nothing is visible there, so no height tile is
             * required for movement safety.
             */
            return true;
        }

        return
            AreTileBoundsInsideCache(
                minimumTile,
                maximumTile,
                cacheOriginTile
            );
    }

    // =====================================================
    // EDITOR / HIERARCHY CONFIGURATION
    // =====================================================

    /*
     * Called by TerrainWorldHierarchyGenerator.
     *
     * This keeps the runtime component synchronized with
     * the generated WorldSettings and heightmap manifest.
     */
    public bool Configure(
        WorldSettings settings,
        TerrainHeightmapManifest manifest
    )
    {
        bool changed =
            false;

        if (
            worldSettings !=
            settings
        )
        {
            worldSettings =
                settings;

            changed =
                true;
        }

        if (
            heightmapManifest !=
            manifest
        )
        {
            heightmapManifest =
                manifest;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // ENABLE
    // =====================================================

    private void OnEnable()
    {
        /*
         * This component is a runtime system.
         *
         * The hierarchy generator may add/configure it while
         * the Editor is not in Play Mode, but streaming should
         * only begin while the game is running.
         */
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

        requestedOriginTile =
            CalculateRequestedCacheOrigin();

        BeginLoadWindow(
            requestedOriginTile
        );
    }

    // =====================================================
    // UPDATE
    // =====================================================

    private void Update()
    {
        if (
            !Application.isPlaying
            ||
            !initialized
        )
        {
            return;
        }

        // =====================================================
        // SHADER BINDING
        // =====================================================

        /*
         * A successfully loaded cache may be used immediately.
         *
         * Cache validation and other development diagnostics
         * are not part of the normal streaming state machine.
         */
        UpdateShaderCacheBindingState();

        // =====================================================
        // CACHE INSPECTION SAFETY
        // =====================================================

        /*
         * A development diagnostic may temporarily lock the
         * active cache while performing GPU readback.
         *
         * This state is never entered by normal runtime
         * streaming.
         */
        if (cacheInspectionActive)
        {
            return;
        }

        // =====================================================
        // REQUIRED WINDOW
        // =====================================================

        Vector2Int desiredOrigin =
            CalculateRequestedCacheOrigin();

        requestedOriginTile =
            desiredOrigin;

        /*
         * Do not interrupt an in-progress load.
         *
         * If the target moves while the current load is running,
         * the desired origin will be recalculated after that load
         * completes.
         */
        if (loadRoutine != null)
        {
            return;
        }

        if (
            cacheReady
            &&
            desiredOrigin ==
            cacheOriginTile
        )
        {
            return;
        }

        BeginLoadWindow(
            desiredOrigin
        );
    }

    // =====================================================
    // UPDATE SHADER CACHE BINDING
    // =====================================================

    private void UpdateShaderCacheBindingState()
    {
        /*
         * A cache becomes usable as soon as the normal streaming
         * process has loaded and populated it successfully.
         */
        bool shouldBeBound =
            cacheReady
            &&
            heightCache != null;

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

    // =====================================================
    // FIND CLIPMAP RENDERERS
    // =====================================================

    private void FindClipmapRenderers()
    {
        clipmapRenderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        if (shaderPropertyBlock == null)
        {
            shaderPropertyBlock =
                new MaterialPropertyBlock();
        }
    }
    
    // =====================================================
    // IS CLIPMAP TERRAIN RENDERER
    // =====================================================

    private bool IsClipmapTerrainRenderer(
        MeshRenderer meshRenderer
    )
    {
        if (
            meshRenderer == null
            ||
            meshRenderer.sharedMaterial == null
        )
        {
            return false;
        }

        Material material =
            meshRenderer.sharedMaterial;

        /*
         * These properties identify a material using our
         * clipmap displacement shader.
         */

        return
            material.HasProperty(
                HeightCachePropertyId
            )
            &&
            material.HasProperty(
                HeightCacheReadyPropertyId
            );
    }
    
    // =====================================================
// BIND HEIGHT CACHE TO CLIPMAP RENDERERS
// =====================================================

private void BindHeightCacheToClipmapRenderers()
{
    if (
        heightCache == null
        ||
        heightmapManifest == null
    )
    {
        return;
    }

    FindClipmapRenderers();

    Vector4 cacheOrigin =
        new Vector4(
            cacheOriginTile.x,
            cacheOriginTile.y,
            0f,
            0f
        );

    Vector4 cacheSize =
        new Vector4(
            cacheWidth,
            cacheHeight,
            0f,
            0f
        );

    Vector4 worldSize =
        new Vector4(
            heightmapManifest.WorldSizeX,
            heightmapManifest.WorldSizeZ,
            0f,
            0f
        );

    int boundRendererCount =
        0;

    foreach (
        MeshRenderer meshRenderer
        in clipmapRenderers
    )
    {
        if (
            !IsClipmapTerrainRenderer(
                meshRenderer
            )
        )
        {
            continue;
        }

        /*
         * Preserve any existing overrides that may already
         * exist on this renderer.
         */

        meshRenderer.GetPropertyBlock(
            shaderPropertyBlock
        );

        shaderPropertyBlock.SetTexture(
            HeightCachePropertyId,
            heightCache
        );

        shaderPropertyBlock.SetVector(
            HeightCacheOriginTilePropertyId,
            cacheOrigin
        );

        shaderPropertyBlock.SetVector(
            HeightCacheSizePropertyId,
            cacheSize
        );

        shaderPropertyBlock.SetFloat(
            HeightTileSamplesPerSidePropertyId,
            heightmapManifest
                .heightTileSamplesPerSide
        );

        shaderPropertyBlock.SetFloat(
            HeightSampleSpacingPropertyId,
            heightmapManifest
                .HeightSampleSpacing
        );

        shaderPropertyBlock.SetVector(
            WorldSizeXZPropertyId,
            worldSize
        );

        /*
         * Set this last conceptually:
         *
         * all metadata and the texture cache are now valid.
         */

        shaderPropertyBlock.SetFloat(
            HeightCacheReadyPropertyId,
            1f
        );

        meshRenderer.SetPropertyBlock(
            shaderPropertyBlock
        );

        boundRendererCount++;
    }

    shaderCacheBound =
        boundRendererCount > 0;

    if (!shaderCacheBound)
    {
        Debug.LogError(
            "TerrainHeightmapStreamer could not bind the " +
            "height cache to the clipmap.\n\n" +

            "No child MeshRenderer was found using a " +
            "material with the expected clipmap shader " +
            "properties.",
            this
        );

        return;
    }

    if (logCacheUpdates)
    {
        Debug.Log(
            "Terrain height cache bound to clipmap shader.\n\n" +

            $"Renderers: " +
            $"{boundRendererCount}\n\n" +

            $"Cache Origin Tile: " +
            $"({cacheOriginTile.x}, " +
            $"{cacheOriginTile.y})\n" +

            $"Cache Tile Grid: " +
            $"{cacheWidth} x " +
            $"{cacheHeight}\n\n" +

            $"World Size: " +
            $"{heightmapManifest.WorldSizeX} x " +
            $"{heightmapManifest.WorldSizeZ}",
            this
        );
    }
}



    // =====================================================
    // DISABLE HEIGHT CACHE ON CLIPMAP RENDERERS
    // =====================================================

    private void DisableHeightCacheOnClipmapRenderers()
    {
        /*
         * There is nothing to disable unless a cache has
         * actually been bound to the clipmap shader.
         */

        if (!shaderCacheBound)
        {
            return;
        }

        FindClipmapRenderers();

        foreach (
            MeshRenderer meshRenderer
            in clipmapRenderers
        )
        {
            if (
                !IsClipmapTerrainRenderer(
                    meshRenderer
                )
            )
            {
                continue;
            }

            /*
             * Preserve any other property overrides already
             * present on this renderer.
             */

            meshRenderer.GetPropertyBlock(
                shaderPropertyBlock
            );

            /*
             * Do not call SetTexture(..., null).
             *
             * MaterialPropertyBlock.SetTexture requires a valid
             * Texture value.
             *
             * Setting _HeightCacheReady to 0 is sufficient to
             * disable height sampling in ClipmapTerrain.shader.
             *
             * The existing texture property will be replaced
             * with the new cache the next time
             * BindHeightCacheToClipmapRenderers() runs.
             */

            shaderPropertyBlock.SetFloat(
                HeightCacheReadyPropertyId,
                0f
            );

            meshRenderer.SetPropertyBlock(
                shaderPropertyBlock
            );
        }

        shaderCacheBound =
            false;
    }

    // =====================================================
    // DISABLE / DESTROY
    // =====================================================

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ShutdownStreamer();
    }

    // =====================================================
    // INITIALIZE
    // =====================================================

    private bool InitializeStreamer()
    {
        initialized =
            false;

        cacheReady =
            false;

        // -------------------------------------------------
        // WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +

                "WorldSettings is not assigned.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // Manifest
        // -------------------------------------------------

        if (heightmapManifest == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +

                "TerrainHeightmapManifest is not assigned.\n\n" +

                "Run Sync World Hierarchy after generating " +
                "the heightmaps.",
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

        // -------------------------------------------------
        // GPU support
        // -------------------------------------------------

        if (!SystemInfo.supports2DArrayTextures)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +

                "The current graphics device does not support " +
                "Texture2DArray textures.",
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

                "The current graphics device does not support " +
                "TextureFormat.RFloat.",
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

                "The current graphics device does not support " +
                "Graphics.CopyTexture.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // Manifest layout
        // -------------------------------------------------

        if (
            heightmapManifest.heightTileGridWidth <= 0
            ||
            heightmapManifest.heightTileGridHeight <= 0
            ||
            heightmapManifest.heightTileSamplesPerSide <= 1
            ||
            heightmapManifest.heightTileWorldSize <= 0f
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +

                "The heightmap manifest contains an invalid " +
                "tile layout.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // Cache dimensions
        // -------------------------------------------------

        CalculateCacheDimensions();

        if (
            cacheWidth <= 0
            ||
            cacheHeight <= 0
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +

                "Calculated height cache dimensions are invalid.",
                this
            );

            return false;
        }

        // -------------------------------------------------
        // GPU cache buffers
        // -------------------------------------------------

        if (!CreateHeightCacheBuffers())
        {
            return false;
        }

        initialized =
            true;

        return true;
    }

    // =====================================================
    // CACHE DIMENSIONS
    // =====================================================

    private void CalculateCacheDimensions()
    {
        float clipmapDiameter =
            CalculateClipmapDiameter();

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                heightmapManifest.heightTileWorldSize
            );

        /*
         * Number of tiles required to span the clipmap.
         */
        int clipmapTileSpan =
            Mathf.CeilToInt(
                clipmapDiameter /
                tileWorldSize
            );

        /*
         * Add guard tiles around the requested area.
         *
         * The result is capped by the actual generated
         * heightmap tile grid.
         */
        int requestedCacheSize =
            Mathf.Max(
                1,
                clipmapTileSpan
                +
                Mathf.Max(
                    0,
                    guardTileCount
                )
                *
                2
            );

        cacheWidth =
            Mathf.Min(
                requestedCacheSize,
                heightmapManifest.heightTileGridWidth
            );

        cacheHeight =
            Mathf.Min(
                requestedCacheSize,
                heightmapManifest.heightTileGridHeight
            );
    }

    // =====================================================
    // CREATE HEIGHT CACHE BUFFERS
    // =====================================================

    private bool CreateHeightCacheBuffers()
    {
        DestroyHeightCacheBuffers();

        int samplesPerSide =
            heightmapManifest
                .heightTileSamplesPerSide;

        int sliceCount =
            cacheWidth *
            cacheHeight;

        try
        {
            heightCache =
                CreateHeightCacheTexture(
                    "Terrain Height Cache A",
                    samplesPerSide,
                    sliceCount
                );

            stagingHeightCache =
                CreateHeightCacheTexture(
                    "Terrain Height Cache B",
                    samplesPerSide,
                    sliceCount
                );
        }
        catch (
            System.Exception exception
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not create " +
                "the double-buffered GPU height cache.\n\n" +
                exception.Message,
                this
            );

            DestroyHeightCacheBuffers();

            return false;
        }

        return true;
    }

    // =====================================================
    // CREATE HEIGHT CACHE TEXTURE
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

        return cache;
    }

    // =====================================================
    // CLIPMAP DIAMETER
    // =====================================================

    private float CalculateClipmapDiameter()
    {
        int centerResolution =
            Mathf.Max(
                1,
                worldSettings.clipmapCenterResolution
            );

        int levelCount =
            Mathf.Max(
                1,
                worldSettings.clipmapLevelCount
            );

        float baseSpacing =
            Mathf.Max(
                0.0001f,
                worldSettings.ClipmapBaseSpacing
            );

        /*
         * Example:
         *
         * resolution = 128
         * base spacing = 1
         * levels = 4
         *
         * diameter =
         * 128 * 1 * 2^3
         * =
         * 1024 metres
         */
        return
            centerResolution
            *
            baseSpacing
            *
            Mathf.Pow(
                2f,
                levelCount - 1
            );
    }

    // =====================================================
    // REQUESTED CACHE ORIGIN
    // =====================================================

    private Vector2Int CalculateRequestedCacheOrigin()
    {
        if (!hasRequestedClipmapCenter)
        {
            return
                CalculateRequiredCacheOrigin();
        }

        if (!cacheReady)
        {
            return
                CalculateRequiredCacheOrigin(
                    requestedClipmapCenter
                );
        }

        return
            CalculatePrefetchCacheOrigin(
                requestedClipmapCenter
            );
    }

    // =====================================================
    // REQUIRED CACHE ORIGIN
    // =====================================================

    private Vector2Int CalculateRequiredCacheOrigin()
    {
        Vector3 center =
            streamingTarget != null
                ? streamingTarget.position
                : transform.position;

        return
            CalculateRequiredCacheOrigin(
                center
            );
    }

    private Vector2Int CalculateRequiredCacheOrigin(
        Vector3 center
    )
    {
        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                heightmapManifest.heightTileWorldSize
            );

        float cacheWorldWidth =
            cacheWidth *
            tileWorldSize;

        float cacheWorldHeight =
            cacheHeight *
            tileWorldSize;

        /*
         * Position the cache approximately around the requested
         * center, then clamp the cache origin to actual generated
         * height tiles.
         *
         * The clipmap itself is allowed to move outside the world.
         * Only the DATA window is clamped because no Addressable
         * height tiles exist beyond the generated world.
         */

        int originX =
            Mathf.FloorToInt(
                (
                    center.x
                    -
                    cacheWorldWidth *
                    0.5f
                )
                /
                tileWorldSize
            );

        int originZ =
            Mathf.FloorToInt(
                (
                    center.z
                    -
                    cacheWorldHeight *
                    0.5f
                )
                /
                tileWorldSize
            );

        Vector2Int maximumOrigin =
            GetMaximumCacheOrigin();

        originX =
            Mathf.Clamp(
                originX,
                0,
                maximumOrigin.x
            );

        originZ =
            Mathf.Clamp(
                originZ,
                0,
                maximumOrigin.y
            );

        return
            new Vector2Int(
                originX,
                originZ
            );
    }

    // =====================================================
    // PREFETCH CACHE ORIGIN
    // =====================================================

    /*
     * Keep the active cache while the clipmap is comfortably
     * inside it.
     *
     * When the visible clipmap footprint plus a configurable
     * prefetch margin approaches an active-cache edge, shift the
     * requested origin by the minimum number of tiles necessary
     * to contain that expanded footprint.
     *
     * The footprint is intersected with the real world first.
     * Therefore geometry outside world bounds never creates an
     * impossible request for non-existent height tiles.
     */
    private Vector2Int CalculatePrefetchCacheOrigin(
        Vector3 clipmapCenter
    )
    {
        if (!cacheReady)
        {
            return
                CalculateRequiredCacheOrigin(
                    clipmapCenter
                );
        }

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                heightmapManifest.heightTileWorldSize
            );

        float prefetchMargin =
            tileWorldSize *
            Mathf.Clamp01(
                prefetchTileFraction
            );

        if (
            !TryCalculateVisibleClipmapTileBounds(
                clipmapCenter,
                prefetchMargin,
                out Vector2Int minimumTile,
                out Vector2Int maximumTile
            )
        )
        {
            /*
             * The desired clipmap is completely outside the
             * authoritative world.
             *
             * Request the nearest edge cache so data is already
             * prepared if the player approaches the world again.
             */
            return
                CalculateRequiredCacheOrigin(
                    clipmapCenter
                );
        }

        int requiredWidth =
            maximumTile.x -
            minimumTile.x +
            1;

        int requiredHeight =
            maximumTile.y -
            minimumTile.y +
            1;

        if (
            requiredWidth > cacheWidth
            ||
            requiredHeight > cacheHeight
        )
        {
            /*
             * The configured prefetch margin cannot fit inside
             * the cache. Fall back to normal centered placement.
             *
             * Movement safety is still enforced separately by
             * CanActiveCacheCoverClipmapAt().
             */
            return
                CalculateRequiredCacheOrigin(
                    clipmapCenter
                );
        }

        /*
         * Any cache origin in these ranges completely contains
         * the requested tile bounds.
         */
        int minimumAllowedOriginX =
            maximumTile.x -
            cacheWidth +
            1;

        int maximumAllowedOriginX =
            minimumTile.x;

        int minimumAllowedOriginZ =
            maximumTile.y -
            cacheHeight +
            1;

        int maximumAllowedOriginZ =
            minimumTile.y;

        /*
         * Keep the current origin whenever it is still valid.
         *
         * Otherwise move only as far as required. This avoids
         * continuously recentring the cache on every metre of
         * player movement.
         */
        int originX =
            Mathf.Clamp(
                cacheOriginTile.x,
                minimumAllowedOriginX,
                maximumAllowedOriginX
            );

        int originZ =
            Mathf.Clamp(
                cacheOriginTile.y,
                minimumAllowedOriginZ,
                maximumAllowedOriginZ
            );

        Vector2Int maximumOrigin =
            GetMaximumCacheOrigin();

        originX =
            Mathf.Clamp(
                originX,
                0,
                maximumOrigin.x
            );

        originZ =
            Mathf.Clamp(
                originZ,
                0,
                maximumOrigin.y
            );

        return
            new Vector2Int(
                originX,
                originZ
            );
    }

    // =====================================================
    // VISIBLE CLIPMAP TILE BOUNDS
    // =====================================================

    /*
     * Calculates which authoritative height tiles are needed by
     * the portion of the clipmap that actually intersects the
     * terrain world.
     *
     * margin expands the clipmap footprint before intersection.
     *
     * Returns false when the clipmap is completely outside the
     * world and therefore has no visible terrain to sample.
     */
    private bool TryCalculateVisibleClipmapTileBounds(
        Vector3 clipmapCenter,
        float margin,
        out Vector2Int minimumTile,
        out Vector2Int maximumTile
    )
    {
        minimumTile =
            Vector2Int.zero;

        maximumTile =
            Vector2Int.zero;

        float halfDiameter =
            CalculateClipmapDiameter() *
            0.5f;

        float expandedHalfDiameter =
            halfDiameter +
            Mathf.Max(
                0f,
                margin
            );

        float footprintMinimumX =
            clipmapCenter.x -
            expandedHalfDiameter;

        float footprintMaximumX =
            clipmapCenter.x +
            expandedHalfDiameter;

        float footprintMinimumZ =
            clipmapCenter.z -
            expandedHalfDiameter;

        float footprintMaximumZ =
            clipmapCenter.z +
            expandedHalfDiameter;

        float worldSizeX =
            Mathf.Max(
                0f,
                heightmapManifest.WorldSizeX
            );

        float worldSizeZ =
            Mathf.Max(
                0f,
                heightmapManifest.WorldSizeZ
            );

        float visibleMinimumX =
            Mathf.Max(
                0f,
                footprintMinimumX
            );

        float visibleMaximumX =
            Mathf.Min(
                worldSizeX,
                footprintMaximumX
            );

        float visibleMinimumZ =
            Mathf.Max(
                0f,
                footprintMinimumZ
            );

        float visibleMaximumZ =
            Mathf.Min(
                worldSizeZ,
                footprintMaximumZ
            );

        if (
            visibleMinimumX >
                visibleMaximumX
            ||
            visibleMinimumZ >
                visibleMaximumZ
        )
        {
            return false;
        }

        minimumTile =
            new Vector2Int(
                WorldPositionToTileCoordinate(
                    visibleMinimumX,
                    worldSizeX,
                    heightmapManifest
                        .heightTileGridWidth
                ),

                WorldPositionToTileCoordinate(
                    visibleMinimumZ,
                    worldSizeZ,
                    heightmapManifest
                        .heightTileGridHeight
                )
            );

        maximumTile =
            new Vector2Int(
                WorldPositionToTileCoordinate(
                    visibleMaximumX,
                    worldSizeX,
                    heightmapManifest
                        .heightTileGridWidth
                ),

                WorldPositionToTileCoordinate(
                    visibleMaximumZ,
                    worldSizeZ,
                    heightmapManifest
                        .heightTileGridHeight
                )
            );

        return true;
    }

    // =====================================================
    // WORLD POSITION TO HEIGHT TILE
    // =====================================================

    /*
     * Reproduces the shader's world-position -> global-sample
     * -> tile-coordinate mapping.
     */
    private int WorldPositionToTileCoordinate(
        float coordinate,
        float worldSize,
        int tileCount
    )
    {
        float sampleSpacing =
            Mathf.Max(
                0.000001f,
                heightmapManifest.HeightSampleSpacing
            );

        int samplesPerSide =
            Mathf.Max(
                2,
                heightmapManifest
                    .heightTileSamplesPerSide
            );

        int tileIntervals =
            samplesPerSide -
            1;

        float clampedCoordinate =
            Mathf.Clamp(
                coordinate,
                0f,
                Mathf.Max(
                    0f,
                    worldSize
                )
            );

        int globalSample =
            Mathf.FloorToInt(
                clampedCoordinate /
                sampleSpacing
                +
                0.5f
            );

        int tileCoordinate =
            globalSample /
            tileIntervals;

        return
            Mathf.Clamp(
                tileCoordinate,
                0,
                Mathf.Max(
                    0,
                    tileCount - 1
                )
            );
    }

    // =====================================================
    // TILE BOUNDS INSIDE CACHE
    // =====================================================

    private bool AreTileBoundsInsideCache(
        Vector2Int minimumTile,
        Vector2Int maximumTile,
        Vector2Int cacheOrigin
    )
    {
        return
            minimumTile.x >=
                cacheOrigin.x
            &&
            minimumTile.y >=
                cacheOrigin.y
            &&
            maximumTile.x <
                cacheOrigin.x +
                cacheWidth
            &&
            maximumTile.y <
                cacheOrigin.y +
                cacheHeight;
    }

    // =====================================================
    // MAXIMUM CACHE ORIGIN
    // =====================================================

    private Vector2Int GetMaximumCacheOrigin()
    {
        return
            new Vector2Int(
                Mathf.Max(
                    0,
                    heightmapManifest
                        .heightTileGridWidth
                    -
                    cacheWidth
                ),

                Mathf.Max(
                    0,
                    heightmapManifest
                        .heightTileGridHeight
                    -
                    cacheHeight
                )
            );
    }

    // =====================================================
// RESOLVE CACHE SLICE
// =====================================================

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

        // -------------------------------------------------
        // Cache dimensions
        // -------------------------------------------------

        if (
            cacheWidth <= 0
            ||
            cacheHeight <= 0
        )
        {
            return false;
        }

        // -------------------------------------------------
        // Cache-local tile coordinate
        // -------------------------------------------------

        Vector2Int localCoordinate =
            tileCoordinate
            -
            cacheOrigin;

        // -------------------------------------------------
        // Cache bounds
        // -------------------------------------------------

        if (
            localCoordinate.x < 0
            ||
            localCoordinate.y < 0
            ||
            localCoordinate.x >= cacheWidth
            ||
            localCoordinate.y >= cacheHeight
        )
        {
            return false;
        }

        // -------------------------------------------------
        // Texture2DArray slice
        // -------------------------------------------------

        slice =
            localCoordinate.x
            +
            localCoordinate.y *
            cacheWidth;

        return true;
    }

    // =====================================================
    // GET USABLE RESIDENT TILE
    // =====================================================

    private bool TryGetUsableResidentTile(
        Vector2Int coordinate,
        out ResidentTile residentTile
    )
    {
        residentTile =
            null;

        if (
            !residentTiles.TryGetValue(
                coordinate,
                out ResidentTile existingTile
            )
        )
        {
            return false;
        }

        bool usable =
            existingTile != null
            &&
            existingTile.texture != null
            &&
            existingTile.handle.IsValid()
            &&
            existingTile.handle.Status ==
                AsyncOperationStatus.Succeeded;

        if (!usable)
        {
            ReleaseResidentTile(
                coordinate
            );

            return false;
        }

        residentTile =
            existingTile;

        return true;
    }

    // =====================================================
    // BEGIN WINDOW LOAD
    // =====================================================

    private void BeginLoadWindow(
        Vector2Int origin
    )
    {
        if (
            loadRoutine != null
            ||
            cacheInspectionActive
        )
        {
            return;
        }

        loadRoutine =
            StartCoroutine(
                LoadWindowRoutine(
                    origin
                )
            );
    }

    // =====================================================
    // LOAD WINDOW
    // =====================================================

    private IEnumerator LoadWindowRoutine(
        Vector2Int origin
    )
    {
        /*
         * IMPORTANT:
         *
         * Do not invalidate or destroy the active cache here.
         *
         * If cacheReady is already true, heightCache remains
         * bound to the shader for the entire transition.
         *
         * The next window is assembled in stagingHeightCache
         * and becomes visible only after every required tile
         * has loaded and every GPU copy has succeeded.
         */

        if (
            heightCache == null
            ||
            stagingHeightCache == null
        )
        {
            Debug.LogError(
                "Terrain height-cache buffers are not available.",
                this
            );

            loadRoutine =
                null;

            yield break;
        }

        int samplesPerSide =
            heightmapManifest
                .heightTileSamplesPerSide;

        // =====================================================
        // REQUIRED TILE SET
        // =====================================================

        HashSet<Vector2Int> requiredTiles =
            new HashSet<Vector2Int>();

        List<Vector2Int> enteringTiles =
            new List<Vector2Int>();

        List<Vector2Int> newlyLoadedTiles =
            new List<Vector2Int>();

        int retainedTileCount =
            0;

        for (
            int localZ = 0;
            localZ < cacheHeight;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < cacheWidth;
                localX++
            )
            {
                Vector2Int tileCoordinate =
                    new Vector2Int(
                        origin.x + localX,
                        origin.y + localZ
                    );

                // -----------------------------------------
                // Validate coordinate
                // -----------------------------------------

                if (
                    !heightmapManifest
                        .IsTileCoordinateValid(
                            tileCoordinate.x,
                            tileCoordinate.y
                        )
                )
                {
                    Debug.LogError(
                        "Terrain height cache requested an " +
                        "invalid tile coordinate.\n\n" +

                        $"Tile: " +
                        $"({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})",
                        this
                    );

                    FailCurrentLoad(
                        newlyLoadedTiles
                    );

                    yield break;
                }

                requiredTiles.Add(
                    tileCoordinate
                );

                // -----------------------------------------
                // Retained or entering
                // -----------------------------------------

                if (
                    TryGetUsableResidentTile(
                        tileCoordinate,
                        out _
                    )
                )
                {
                    retainedTileCount++;
                }
                else
                {
                    enteringTiles.Add(
                        tileCoordinate
                    );
                }
            }
        }

        // =====================================================
        // BEGIN ALL ENTERING TILE LOADS
        // =====================================================

        /*
         * Start every missing Addressables request before
         * waiting for any one request to finish.
         *
         * Stage 2B waited for each entering tile sequentially.
         * Beginning them together allows independent tile loads
         * to make progress concurrently.
         */

        foreach (
            Vector2Int tileCoordinate
            in enteringTiles
        )
        {
            string address =
                heightmapManifest
                    .GetHeightTileAddress(
                        tileCoordinate.x,
                        tileCoordinate.y
                    );

            AsyncOperationHandle<Texture2D> handle;

            try
            {
                handle =
                    Addressables
                        .LoadAssetAsync<Texture2D>(
                            address
                        );
            }
            catch (
                System.Exception exception
            )
            {
                Debug.LogError(
                    "Failed to begin loading Addressable " +
                    "heightmap tile.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n" +

                    $"Address: {address}\n\n" +

                    exception.Message,
                    this
                );

                FailCurrentLoad(
                    newlyLoadedTiles
                );

                yield break;
            }

            ResidentTile residentTile =
                new ResidentTile(
                    tileCoordinate,
                    address,
                    handle
                );

            residentTiles[
                tileCoordinate
            ] =
                residentTile;

            newlyLoadedTiles.Add(
                tileCoordinate
            );
        }

        // =====================================================
        // COMPLETE ENTERING TILE LOADS
        // =====================================================

        int loadedTileCount =
            0;

        foreach (
            Vector2Int tileCoordinate
            in newlyLoadedTiles
        )
        {
            if (
                !residentTiles.TryGetValue(
                    tileCoordinate,
                    out ResidentTile residentTile
                )
                ||
                residentTile == null
            )
            {
                Debug.LogError(
                    "A newly requested terrain height tile " +
                    "was not present in the residency table.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})",
                    this
                );

                FailCurrentLoad(
                    newlyLoadedTiles
                );

                yield break;
            }

            AsyncOperationHandle<Texture2D> handle =
                residentTile.handle;

            if (!handle.IsDone)
            {
                yield return handle;
            }

            if (
                handle.Status !=
                AsyncOperationStatus.Succeeded
                ||
                handle.Result == null
            )
            {
                Debug.LogError(
                    "Failed to load Addressable " +
                    "heightmap tile.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n" +

                    $"Address: {residentTile.address}",
                    this
                );

                FailCurrentLoad(
                    newlyLoadedTiles
                );

                yield break;
            }

            Texture2D texture =
                handle.Result;

            residentTile.texture =
                texture;

            // -----------------------------------------
            // Validate dimensions
            // -----------------------------------------

            if (
                texture.width !=
                    samplesPerSide
                ||
                texture.height !=
                    samplesPerSide
            )
            {
                Debug.LogError(
                    "Loaded heightmap tile has incorrect " +
                    "dimensions.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n\n" +

                    $"Expected: " +
                    $"{samplesPerSide} x " +
                    $"{samplesPerSide}\n" +

                    $"Actual: " +
                    $"{texture.width} x " +
                    $"{texture.height}",
                    this
                );

                FailCurrentLoad(
                    newlyLoadedTiles
                );

                yield break;
            }

            // -----------------------------------------
            // Validate format
            // -----------------------------------------

            if (
                texture.format !=
                TextureFormat.RFloat
            )
            {
                Debug.LogError(
                    "Loaded heightmap tile has incorrect " +
                    "texture format.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n\n" +

                    $"Expected: " +
                    $"{TextureFormat.RFloat}\n" +

                    $"Actual: " +
                    $"{texture.format}",
                    this
                );

                FailCurrentLoad(
                    newlyLoadedTiles
                );

                yield break;
            }

            loadedTileCount++;
        }

        // =====================================================
        // POPULATE STAGING GPU CACHE
        // =====================================================

        /*
         * Every slice of the staging cache is rewritten.
         *
         * Retained tiles may move to different row-major slices
         * when cacheOriginTile changes, so rebuilding all slices
         * preserves the shader's simple local-slice mapping.
         *
         * Graphics.CopyTexture performs the texture copies on
         * the graphics side; no CPU height-data readback occurs.
         */

        for (
            int localZ = 0;
            localZ < cacheHeight;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < cacheWidth;
                localX++
            )
            {
                Vector2Int tileCoordinate =
                    new Vector2Int(
                        origin.x + localX,
                        origin.y + localZ
                    );

                if (
                    !TryGetUsableResidentTile(
                        tileCoordinate,
                        out ResidentTile residentTile
                    )
                )
                {
                    Debug.LogError(
                        "A required resident height tile was " +
                        "missing while building the staging GPU " +
                        "cache.\n\n" +

                        $"Tile: " +
                        $"({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})",
                        this
                    );

                    FailCurrentLoad(
                        newlyLoadedTiles
                    );

                    yield break;
                }

                if (
                    !TryResolveCacheSlice(
                        tileCoordinate,
                        origin,
                        cacheWidth,
                        cacheHeight,
                        out int slice
                    )
                )
                {
                    Debug.LogError(
                        "Could not resolve a GPU cache slice " +
                        "for a required terrain height tile.\n\n" +

                        $"Tile: " +
                        $"({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n" +

                        $"Cache Origin: " +
                        $"({origin.x}, {origin.y})\n" +

                        $"Cache Size: " +
                        $"{cacheWidth} x {cacheHeight}",
                        this
                    );

                    FailCurrentLoad(
                        newlyLoadedTiles
                    );

                    yield break;
                }

                try
                {
                    Graphics.CopyTexture(
                        residentTile.texture,
                        0,
                        0,

                        stagingHeightCache,
                        slice,
                        0
                    );
                }
                catch (
                    System.Exception exception
                )
                {
                    Debug.LogError(
                        "Could not copy resident heightmap tile " +
                        "into the staging terrain height cache.\n\n" +

                        $"Tile: " +
                        $"({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n" +

                        $"Slice: {slice}\n\n" +

                        exception.Message,
                        this
                    );

                    FailCurrentLoad(
                        newlyLoadedTiles
                    );

                    yield break;
                }
            }
        }

        // =====================================================
        // ATOMIC CACHE COMMIT
        // =====================================================

        /*
         * Until this point the currently bound cache and its
         * origin have remained untouched.
         *
         * Swap the two GPU buffers, update the active origin,
         * and immediately rebind the MaterialPropertyBlocks.
         *
         * _HeightCacheReady is never set to zero during a
         * successful runtime transition.
         */

        Texture2DArray previousActiveCache =
            heightCache;

        heightCache =
            stagingHeightCache;

        stagingHeightCache =
            previousActiveCache;

        cacheOriginTile =
            origin;

        cacheReady =
            true;

        BindHeightCacheToClipmapRenderers();

        // =====================================================
        // RELEASE LEAVING TILES
        // =====================================================

        /*
         * Source Addressables are released only after the new
         * GPU cache has committed successfully.
         */
        int releasedTileCount =
            ReleaseResidentTilesNotRequired(
                requiredTiles
            );

        // =====================================================
        // COMPLETE
        // =====================================================

        loadRoutine =
            null;

        if (logCacheUpdates)
        {
            LogCacheReady(
                retainedTileCount,
                loadedTileCount,
                releasedTileCount
            );
        }
    }

    // =====================================================
    // BEGIN CACHE INSPECTION
    // =====================================================

    internal bool TryBeginCacheInspection(
        out string reason
    )
    {
        reason =
            null;

        if (!Application.isPlaying)
        {
            reason =
                "Cache inspection can only begin in Play Mode.";

            return false;
        }

        if (!initialized)
        {
            reason =
                "The terrain heightmap streamer has not initialized.";

            return false;
        }

        if (cacheInspectionActive)
        {
            reason =
                "The current height cache is already being inspected.";

            return false;
        }

        if (loadRoutine != null)
        {
            reason =
                "A height-cache window is currently being loaded.";

            return false;
        }

        if (
            !cacheReady
            ||
            heightCache == null
        )
        {
            reason =
                "There is no ready height cache to inspect.";

            return false;
        }

        cacheInspectionActive =
            true;

        return true;
    }

    // =====================================================
    // END CACHE INSPECTION
    // =====================================================

    internal void EndCacheInspection()
    {
        cacheInspectionActive =
            false;
    }

    // =====================================================
    // GET LOADED TILE FOR INSPECTION
    // =====================================================

    internal bool TryGetLoadedTileForInspection(
        Vector2Int coordinate,
        out Texture2D texture,
        out int recordedSlice
    )
    {
        texture =
            null;

        recordedSlice =
            -1;

        // -------------------------------------------------
        // Resident source tile
        // -------------------------------------------------

        if (
            !residentTiles.TryGetValue(
                coordinate,
                out ResidentTile residentTile
            )
            ||
            residentTile == null
            ||
            residentTile.texture == null
        )
        {
            return false;
        }

        // -------------------------------------------------
        // Current GPU placement
        // -------------------------------------------------

        /*
         * The inspection API retains its existing shape so the
         * validator does not need to change during Stage 2A.
         *
         * The returned slice is now DERIVED from the current
         * cache layout instead of being stored on the tile.
         */
        if (
            !TryResolveCacheSlice(
                coordinate,
                cacheOriginTile,
                cacheWidth,
                cacheHeight,
                out recordedSlice
            )
        )
        {
            return false;
        }

        texture =
            residentTile.texture;

        return true;
    }

    // =====================================================
    // FAILED LOAD
    // =====================================================

    private void FailCurrentLoad(
        List<Vector2Int> newlyLoadedTiles
    )
    {
        /*
         * Roll back only tiles acquired by this load attempt.
         *
         * The active GPU cache, active cache origin and shader
         * binding are deliberately left untouched.
         *
         * If this was the initial load, cacheReady was already
         * false. If this was a runtime transition, the previous
         * cache therefore remains visible.
         */
        if (newlyLoadedTiles != null)
        {
            foreach (
                Vector2Int coordinate
                in newlyLoadedTiles
            )
            {
                ReleaseResidentTile(
                    coordinate
                );
            }
        }

        loadRoutine =
            null;
    }

    // =====================================================
    // RELEASE ONE RESIDENT TILE
    // =====================================================

    private void ReleaseResidentTile(
        Vector2Int coordinate
    )
    {
        if (
            !residentTiles.TryGetValue(
                coordinate,
                out ResidentTile tile
            )
        )
        {
            return;
        }

        if (
            tile != null
            &&
            tile.handle.IsValid()
        )
        {
            Addressables.Release(
                tile.handle
            );
        }

        residentTiles.Remove(
            coordinate
        );
    }

    // =====================================================
    // RELEASE LEAVING RESIDENT TILES
    // =====================================================

    private int ReleaseResidentTilesNotRequired(
        HashSet<Vector2Int> requiredTiles
    )
    {
        List<Vector2Int> leavingTiles =
            new List<Vector2Int>();

        foreach (
            KeyValuePair<Vector2Int, ResidentTile> pair
            in residentTiles
        )
        {
            if (
                !requiredTiles.Contains(
                    pair.Key
                )
            )
            {
                leavingTiles.Add(
                    pair.Key
                );
            }
        }

        foreach (
            Vector2Int coordinate
            in leavingTiles
        )
        {
            ReleaseResidentTile(
                coordinate
            );
        }

        return
            leavingTiles.Count;
    }

    // =====================================================
    // RELEASE ALL RESIDENT ADDRESSABLES
    // =====================================================

    private void ReleaseResidentTiles()
    {
        List<Vector2Int> coordinates =
            new List<Vector2Int>(
                residentTiles.Keys
            );

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            ReleaseResidentTile(
                coordinate
            );
        }
    }

    // =====================================================
    // DESTROY GPU CACHE
    // =====================================================

    private void DestroyHeightCacheBuffers()
    {
        /*
         * Shutdown is the only normal runtime path that disables
         * the currently bound cache.
         *
         * Cache-window transitions use the inactive buffer and
         * never call this method.
         */
        if (shaderCacheBound)
        {
            DisableHeightCacheOnClipmapRenderers();
        }

        if (heightCache != null)
        {
            Destroy(
                heightCache
            );

            heightCache =
                null;
        }

        if (stagingHeightCache != null)
        {
            Destroy(
                stagingHeightCache
            );

            stagingHeightCache =
                null;
        }
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

        ReleaseResidentTiles();

        DestroyHeightCacheBuffers();

        initialized =
            false;

        cacheReady =
            false;
    }

    // =====================================================
    // DEBUG LOG
    // =====================================================

    private void LogCacheReady(
        int retainedTileCount,
        int loadedTileCount,
        int releasedTileCount
    )
    {
        float clipmapDiameter =
            CalculateClipmapDiameter();

        int sliceCount =
            cacheWidth *
            cacheHeight;

        Debug.Log(
            "Terrain heightmap cache ready.\n\n" +

            $"Clipmap Diameter: " +
            $"{clipmapDiameter}\n\n" +

            $"Cache Origin Tile: " +
            $"({cacheOriginTile.x}, " +
            $"{cacheOriginTile.y})\n" +

            $"Cache Tile Grid: " +
            $"{cacheWidth} x " +
            $"{cacheHeight}\n" +

            $"Resident Tiles: " +
            $"{residentTiles.Count}\n\n" +

            $"Residency Transition:\n" +
            $"Retained: {retainedTileCount}\n" +
            $"Loaded: {loadedTileCount}\n" +
            $"Released: {releasedTileCount}\n\n" +

            $"Texture Array: " +
            $"{heightmapManifest.heightTileSamplesPerSide} x " +
            $"{heightmapManifest.heightTileSamplesPerSide} x " +
            $"{sliceCount}\n\n" +

            $"Tile World Size: " +
            $"{heightmapManifest.heightTileWorldSize}\n" +

            $"Height Sample Spacing: " +
            $"{heightmapManifest.HeightSampleSpacing}",
            this
        );
    }

    // =====================================================
    // RESIDENT TILE
    // =====================================================

    private sealed class ResidentTile
    {
        public readonly Vector2Int coordinate;

        public readonly string address;

        public readonly AsyncOperationHandle<Texture2D>
            handle;

        public Texture2D texture;

        public ResidentTile(
            Vector2Int coordinate,
            string address,
            AsyncOperationHandle<Texture2D> handle
        )
        {
            this.coordinate =
                coordinate;

            this.address =
                address;

            this.handle =
                handle;

            texture =
                null;
        }
    }
}

