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

    private Texture2DArray heightCache;

    private readonly Dictionary<Vector2Int, LoadedTile>
        loadedTiles =
            new Dictionary<Vector2Int, LoadedTile>();

    private Coroutine loadRoutine;

    private Vector2Int cacheOriginTile;

    private Vector2Int requestedOriginTile;

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
            CalculateRequiredCacheOrigin();

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
            CalculateRequiredCacheOrigin();

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
    // REQUIRED CACHE ORIGIN
    // =====================================================

    private Vector2Int CalculateRequiredCacheOrigin()
    {
        Vector3 center =
            streamingTarget != null
                ? streamingTarget.position
                : transform.position;

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
         * Position the cache approximately around the target.
         *
         * The origin is always clamped to the valid generated
         * tile grid. Therefore negative tile coordinates and
         * coordinates beyond the generated world are never
         * requested from Addressables.
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

        int maximumOriginX =
            Mathf.Max(
                0,
                heightmapManifest.heightTileGridWidth
                -
                cacheWidth
            );

        int maximumOriginZ =
            Mathf.Max(
                0,
                heightmapManifest.heightTileGridHeight
                -
                cacheHeight
            );

        originX =
            Mathf.Clamp(
                originX,
                0,
                maximumOriginX
            );

        originZ =
            Mathf.Clamp(
                originZ,
                0,
                maximumOriginZ
            );

        return
            new Vector2Int(
                originX,
                originZ
            );
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
        cacheReady =
            false;

        // -------------------------------------------------
        // Release previous window
        // -------------------------------------------------

        ReleaseLoadedTiles();

        DestroyHeightCache();

        // -------------------------------------------------
        // Create GPU cache
        // -------------------------------------------------

        int samplesPerSide =
            heightmapManifest
                .heightTileSamplesPerSide;

        int sliceCount =
            cacheWidth *
            cacheHeight;

        heightCache =
            new Texture2DArray(
                samplesPerSide,
                samplesPerSide,
                sliceCount,
                TextureFormat.RFloat,
                false,
                true
            );

        heightCache.name =
            "Terrain Height Cache";

        heightCache.wrapMode =
            TextureWrapMode.Clamp;

        heightCache.filterMode =
            FilterMode.Point;

        heightCache.anisoLevel =
            0;

        // =====================================================
        // LOAD TILES
        // =====================================================

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

                    FailCurrentLoad();

                    yield break;
                }

                // -----------------------------------------
                // Address
                // -----------------------------------------

                string address =
                    heightmapManifest
                        .GetHeightTileAddress(
                            tileCoordinate.x,
                            tileCoordinate.y
                        );

                // -----------------------------------------
                // Load
                // -----------------------------------------

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

                    FailCurrentLoad();

                    yield break;
                }

                int slice =
                    localX
                    +
                    localZ *
                    cacheWidth;

                LoadedTile loadedTile =
                    new LoadedTile(
                        tileCoordinate,
                        address,
                        handle,
                        slice
                    );

                loadedTiles[
                    tileCoordinate
                ] =
                    loadedTile;

                // -----------------------------------------
                // Wait for Addressables
                // -----------------------------------------

                if (!handle.IsDone)
                {
                    yield return handle;
                }

                // -----------------------------------------
                // Failed load
                // -----------------------------------------

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

                        $"Address: {address}",
                        this
                    );

                    FailCurrentLoad();

                    yield break;
                }

                Texture2D texture =
                    handle.Result;

                loadedTile.texture =
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

                    FailCurrentLoad();

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

                    FailCurrentLoad();

                    yield break;
                }

                // -----------------------------------------
                // Copy into GPU cache
                // -----------------------------------------

                try
                {
                    Graphics.CopyTexture(
                        texture,
                        0,
                        0,

                        heightCache,
                        slice,
                        0
                    );
                }
                catch (
                    System.Exception exception
                )
                {
                    Debug.LogError(
                        "Could not copy heightmap tile into " +
                        "the terrain height cache.\n\n" +

                        $"Tile: " +
                        $"({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n" +

                        $"Slice: {slice}\n\n" +

                        exception.Message,
                        this
                    );

                    FailCurrentLoad();

                    yield break;
                }
            }
        }

        // =====================================================
        // COMPLETE
        // =====================================================

        cacheOriginTile =
            origin;

        cacheReady =
            true;

        loadRoutine =
            null;

        if (logCacheUpdates)
        {
            LogCacheReady();
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

        if (
            !loadedTiles.TryGetValue(
                coordinate,
                out LoadedTile loadedTile
            )
            ||
            loadedTile == null
            ||
            loadedTile.texture == null
        )
        {
            return false;
        }

        texture =
            loadedTile.texture;

        recordedSlice =
            loadedTile.slice;

        return true;
    }

    // =====================================================
    // FAILED LOAD
    // =====================================================

    private void FailCurrentLoad()
    {
        ReleaseLoadedTiles();

        DestroyHeightCache();

        cacheReady =
            false;

        loadRoutine =
            null;
    }

    // =====================================================
    // RELEASE LOADED ADDRESSABLES
    // =====================================================

    private void ReleaseLoadedTiles()
    {
        foreach (
            KeyValuePair<Vector2Int, LoadedTile> pair
            in loadedTiles
        )
        {
            LoadedTile tile =
                pair.Value;

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
        }

        loadedTiles.Clear();
    }

    // =====================================================
    // DESTROY GPU CACHE
    // =====================================================

    private void DestroyHeightCache()
    {
        /*
         * If the current cache is actively bound to the
         * clipmap shader, disable shader access before
         * destroying the Texture2DArray.
         */

        if (shaderCacheBound)
        {
            DisableHeightCacheOnClipmapRenderers();
        }

        /*
         * There may be no previous cache.
         *
         * This is normal during the first cache load.
         */

        if (heightCache == null)
        {
            return;
        }

        Destroy(
            heightCache
        );

        heightCache =
            null;
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

        ReleaseLoadedTiles();

        DestroyHeightCache();

        initialized =
            false;

        cacheReady =
            false;
    }

    // =====================================================
    // DEBUG LOG
    // =====================================================

    private void LogCacheReady()
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

            $"Loaded Tiles: " +
            $"{loadedTiles.Count}\n\n" +

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
    // LOADED TILE
    // =====================================================

    private sealed class LoadedTile
    {
        public readonly Vector2Int coordinate;

        public readonly string address;

        public readonly AsyncOperationHandle<Texture2D>
            handle;

        public readonly int slice;

        public Texture2D texture;

        public LoadedTile(
            Vector2Int coordinate,
            string address,
            AsyncOperationHandle<Texture2D> handle,
            int slice
        )
        {
            this.coordinate =
                coordinate;

            this.address =
                address;

            this.handle =
                handle;

            this.slice =
                slice;

            texture =
                null;
        }
    }
}
