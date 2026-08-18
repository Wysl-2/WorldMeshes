using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
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
    // CACHE VALIDATION
    // =====================================================

    [Header("Cache Validation")]

    [SerializeField]
    private bool validateCacheAfterLoad =
        true;

    /*
     * Because both the source tiles and the cache use
     * TextureFormat.RFloat, an exact copy should normally
     * produce a difference of zero.
     *
     * This can be increased later if a platform requires
     * a small tolerance.
     */
    [SerializeField]
    [Min(0f)]
    private float cacheValidationTolerance =
        0f;

    private Coroutine validationRoutine;

    private bool hasValidatedCurrentCache;

    private bool lastCacheValidationPassed;

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
    
    public bool LastCacheValidationPassed
    {
        get
        {
            return lastCacheValidationPassed;
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
         * The shader is only allowed to use the height cache
         * after:
         *
         * 1. The cache has finished loading.
         *
         * 2. If cache validation is enabled, that validation
         *    has completed successfully.
         *
         * Until then, _HeightCacheReady remains 0 and the
         * clipmap stays undisplaced.
         */

        UpdateShaderCacheBindingState();

        // =====================================================
        // CACHE VALIDATION
        // =====================================================

        /*
         * Once a newly loaded cache becomes ready, validate it
         * exactly once before allowing another cache-window
         * change.
         */

        if (
            cacheReady
            &&
            validateCacheAfterLoad
            &&
            !hasValidatedCurrentCache
            &&
            validationRoutine == null
        )
        {
            validationRoutine =
                StartCoroutine(
                    ValidateHeightCacheRoutine()
                );

            return;
        }

        /*
         * Keep the current cache stable while its GPU contents
         * are being read back and validated.
         */

        if (validationRoutine != null)
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
         * If the required window changes during loading,
         * Update will request the new window after the current
         * load has completed.
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
            bool validationAllowsBinding =
                !validateCacheAfterLoad
                ||
                (
                    hasValidatedCurrentCache
                    &&
                    lastCacheValidationPassed
                );

            bool shouldBeBound =
                cacheReady
                &&
                heightCache != null
                &&
                validationAllowsBinding;

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
            validationRoutine != null
        )
        {
            return;
        }

        /*
         * A new cache window will contain different data,
         * therefore its validation state must be reset.
         */

        hasValidatedCurrentCache =
            false;

        lastCacheValidationPassed =
            false;

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
// VALIDATE HEIGHT CACHE
// =====================================================

private IEnumerator ValidateHeightCacheRoutine()
{
    lastCacheValidationPassed =
        false;

    // -------------------------------------------------
    // Basic state
    // -------------------------------------------------

    if (
        heightCache == null
        ||
        !cacheReady
    )
    {
        FailCacheValidation(
            "The terrain height cache is not ready."
        );

        yield break;
    }

    int samplesPerSide =
        heightCache.width;

    int samplesPerTile =
        samplesPerSide *
        samplesPerSide;

    int expectedTileCount =
        cacheWidth *
        cacheHeight;

    // -------------------------------------------------
    // GPU readback
    // -------------------------------------------------

    /*
     * Request mip 0 from the entire Texture2DArray.
     *
     * The destination format is explicitly RFloat so the
     * returned NativeArray<float> for each layer corresponds
     * directly to the stored terrain heights.
     */

    AsyncGPUReadbackRequest readback;

    try
    {
        readback =
            AsyncGPUReadback.Request(
                heightCache,
                0,
                TextureFormat.RFloat,
                null
            );
    }
    catch (
        System.Exception exception
    )
    {
        FailCacheValidation(
            "Could not begin GPU readback.\n\n" +
            exception.Message
        );

        yield break;
    }

    while (!readback.done)
    {
        yield return null;
    }

    if (readback.hasError)
    {
        FailCacheValidation(
            "GPU readback of the terrain height cache failed."
        );

        yield break;
    }

    // =====================================================
    // VALIDATION STATISTICS
    // =====================================================

    int tilesValidated =
        0;

    int slicesValidated =
        0;

    int missingTiles =
        0;

    int sliceMappingMismatches =
        0;

    int invalidSourceTextures =
        0;

    long samplesCompared =
        0L;

    long sampleMismatches =
        0L;

    float maximumHeightDifference =
        0f;

    string firstSampleMismatch =
        null;

    string firstSliceMismatch =
        null;

    // =====================================================
    // VALIDATE EVERY TILE / SLICE
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
                    cacheOriginTile.x +
                    localX,

                    cacheOriginTile.y +
                    localZ
                );

            int expectedSlice =
                localX
                +
                localZ *
                cacheWidth;

            // -----------------------------------------
            // Loaded source tile
            // -----------------------------------------

            if (
                !loadedTiles.TryGetValue(
                    tileCoordinate,
                    out LoadedTile loadedTile
                )
                ||
                loadedTile == null
                ||
                loadedTile.texture == null
            )
            {
                missingTiles++;

                continue;
            }

            tilesValidated++;

            // -----------------------------------------
            // Validate recorded slice mapping
            // -----------------------------------------

            if (
                loadedTile.slice !=
                expectedSlice
            )
            {
                sliceMappingMismatches++;

                if (
                    firstSliceMismatch ==
                    null
                )
                {
                    firstSliceMismatch =
                        $"Tile " +
                        $"({tileCoordinate.x}, " +
                        $"{tileCoordinate.y})\n" +

                        $"Expected Slice: " +
                        $"{expectedSlice}\n" +

                        $"Recorded Slice: " +
                        $"{loadedTile.slice}";
                }
            }

            Texture2D sourceTexture =
                loadedTile.texture;

            // -----------------------------------------
            // Validate source texture
            // -----------------------------------------

            if (
                sourceTexture.width !=
                    samplesPerSide
                ||
                sourceTexture.height !=
                    samplesPerSide
                ||
                sourceTexture.format !=
                    TextureFormat.RFloat
                ||
                !sourceTexture.isReadable
            )
            {
                invalidSourceTextures++;

                continue;
            }

            // -----------------------------------------
            // CPU source data
            // -----------------------------------------

            NativeArray<float> sourceData;

            try
            {
                sourceData =
                    sourceTexture
                        .GetPixelData<float>(
                            0
                        );
            }
            catch (
                System.Exception exception
            )
            {
                invalidSourceTextures++;

                Debug.LogError(
                    "Could not read source heightmap tile.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n\n" +

                    exception.Message,
                    this
                );

                continue;
            }

            // -----------------------------------------
            // GPU cache slice
            // -----------------------------------------

            NativeArray<float> cacheData;

            try
            {
                cacheData =
                    readback
                        .GetData<float>(
                            expectedSlice
                        );
            }
            catch (
                System.Exception exception
            )
            {
                FailCacheValidation(
                    "Could not access a Texture2DArray " +
                    "readback layer.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n" +

                    $"Slice: {expectedSlice}\n\n" +

                    exception.Message
                );

                yield break;
            }

            // -----------------------------------------
            // Validate array lengths
            // -----------------------------------------

            if (
                sourceData.Length !=
                    samplesPerTile
                ||
                cacheData.Length !=
                    samplesPerTile
            )
            {
                invalidSourceTextures++;

                Debug.LogError(
                    "Height cache validation encountered " +
                    "an unexpected sample count.\n\n" +

                    $"Tile: " +
                    $"({tileCoordinate.x}, " +
                    $"{tileCoordinate.y})\n\n" +

                    $"Expected Samples: " +
                    $"{samplesPerTile:N0}\n" +

                    $"Source Samples: " +
                    $"{sourceData.Length:N0}\n" +

                    $"Cache Samples: " +
                    $"{cacheData.Length:N0}",
                    this
                );

                continue;
            }

            slicesValidated++;

            // =================================================
            // COMPARE EVERY FLOAT
            // =================================================

            for (
                int sampleIndex = 0;
                sampleIndex < samplesPerTile;
                sampleIndex++
            )
            {
                float sourceHeight =
                    sourceData[
                        sampleIndex
                    ];

                float cacheHeightValue =
                    cacheData[
                        sampleIndex
                    ];

                samplesCompared++;

                bool invalidValue =
                    float.IsNaN(
                        sourceHeight
                    )
                    ||
                    float.IsInfinity(
                        sourceHeight
                    )
                    ||
                    float.IsNaN(
                        cacheHeightValue
                    )
                    ||
                    float.IsInfinity(
                        cacheHeightValue
                    );

                float difference =
                    invalidValue
                        ? float.PositiveInfinity
                        : Mathf.Abs(
                            sourceHeight -
                            cacheHeightValue
                        );

                if (
                    !float.IsInfinity(
                        difference
                    )
                )
                {
                    maximumHeightDifference =
                        Mathf.Max(
                            maximumHeightDifference,
                            difference
                        );
                }

                if (
                    invalidValue
                    ||
                    difference >
                        cacheValidationTolerance
                )
                {
                    sampleMismatches++;

                    if (
                        firstSampleMismatch ==
                        null
                    )
                    {
                        int sampleX =
                            sampleIndex %
                            samplesPerSide;

                        int sampleZ =
                            sampleIndex /
                            samplesPerSide;

                        firstSampleMismatch =
                            $"Tile: " +
                            $"({tileCoordinate.x}, " +
                            $"{tileCoordinate.y})\n" +

                            $"Slice: {expectedSlice}\n" +

                            $"Sample: " +
                            $"({sampleX}, {sampleZ})\n" +

                            $"Source Height: " +
                            $"{sourceHeight:R}\n" +

                            $"Cache Height: " +
                            $"{cacheHeightValue:R}\n" +

                            $"Difference: " +
                            $"{difference:R}";
                    }
                }
            }
        }
    }

    // =====================================================
    // VALIDATE CACHE TILE BOUNDARIES
    // =====================================================

    long boundarySamplesCompared =
        0L;

    long boundaryMismatches =
        0L;

    float maximumBoundaryDifference =
        0f;

    string firstBoundaryMismatch =
        null;

    ValidateCacheBoundaries(
        readback,
        samplesPerSide,

        ref boundarySamplesCompared,
        ref boundaryMismatches,
        ref maximumBoundaryDifference,
        ref firstBoundaryMismatch
    );

    // =====================================================
    // RESULT
    // =====================================================

    bool passed =
        tilesValidated ==
            expectedTileCount
        &&
        slicesValidated ==
            expectedTileCount
        &&
        missingTiles == 0
        &&
        invalidSourceTextures == 0
        &&
        sliceMappingMismatches == 0
        &&
        sampleMismatches == 0
        &&
        boundaryMismatches == 0;

    lastCacheValidationPassed =
        passed;

    hasValidatedCurrentCache =
        true;

    validationRoutine =
        null;

    if (passed)
    {
        Debug.Log(
            "Terrain height cache validation passed.\n\n" +

            $"Cache Origin Tile: " +
            $"({cacheOriginTile.x}, " +
            $"{cacheOriginTile.y})\n" +

            $"Cache Tile Grid: " +
            $"{cacheWidth} x " +
            $"{cacheHeight}\n\n" +

            $"Tiles Validated: " +
            $"{tilesValidated:N0}\n" +

            $"Slices Validated: " +
            $"{slicesValidated:N0}\n\n" +

            $"Samples Compared: " +
            $"{samplesCompared:N0}\n" +

            $"Sample Mismatches: " +
            $"{sampleMismatches:N0}\n\n" +

            $"Slice Mapping Mismatches: " +
            $"{sliceMappingMismatches:N0}\n\n" +

            $"Boundary Samples Compared: " +
            $"{boundarySamplesCompared:N0}\n" +

            $"Boundary Mismatches: " +
            $"{boundaryMismatches:N0}\n\n" +

            $"Maximum Height Difference: " +
            $"{maximumHeightDifference:R}\n" +

            $"Maximum Boundary Difference: " +
            $"{maximumBoundaryDifference:R}",
            this
        );
    }
    else
    {
        string details =
            "";

        if (
            firstSliceMismatch !=
            null
        )
        {
            details +=
                "\n\nFirst Slice Mapping Mismatch:\n" +
                firstSliceMismatch;
        }

        if (
            firstSampleMismatch !=
            null
        )
        {
            details +=
                "\n\nFirst Height Mismatch:\n" +
                firstSampleMismatch;
        }

        if (
            firstBoundaryMismatch !=
            null
        )
        {
            details +=
                "\n\nFirst Boundary Mismatch:\n" +
                firstBoundaryMismatch;
        }

        Debug.LogError(
            "Terrain height cache validation FAILED.\n\n" +

            $"Expected Tiles: " +
            $"{expectedTileCount:N0}\n" +

            $"Tiles Validated: " +
            $"{tilesValidated:N0}\n" +

            $"Slices Validated: " +
            $"{slicesValidated:N0}\n" +

            $"Missing Tiles: " +
            $"{missingTiles:N0}\n" +

            $"Invalid Source Textures: " +
            $"{invalidSourceTextures:N0}\n\n" +

            $"Samples Compared: " +
            $"{samplesCompared:N0}\n" +

            $"Sample Mismatches: " +
            $"{sampleMismatches:N0}\n\n" +

            $"Slice Mapping Mismatches: " +
            $"{sliceMappingMismatches:N0}\n\n" +

            $"Boundary Samples Compared: " +
            $"{boundarySamplesCompared:N0}\n" +

            $"Boundary Mismatches: " +
            $"{boundaryMismatches:N0}\n\n" +

            $"Maximum Height Difference: " +
            $"{maximumHeightDifference:R}\n" +

            $"Maximum Boundary Difference: " +
            $"{maximumBoundaryDifference:R}" +

            details,
            this
        );
    }
}

// =====================================================
// VALIDATE CACHE BOUNDARIES
// =====================================================

private void ValidateCacheBoundaries(
    AsyncGPUReadbackRequest readback,
    int samplesPerSide,

    ref long samplesCompared,
    ref long mismatches,
    ref float maximumDifference,
    ref string firstMismatch
)
{
    int maximumSample =
        samplesPerSide - 1;

    // =====================================================
    // X-AXIS NEIGHBOURS
    // =====================================================

    for (
        int localZ = 0;
        localZ < cacheHeight;
        localZ++
    )
    {
        for (
            int localX = 0;
            localX < cacheWidth - 1;
            localX++
        )
        {
            int leftSlice =
                localX
                +
                localZ *
                cacheWidth;

            int rightSlice =
                localX + 1
                +
                localZ *
                cacheWidth;

            NativeArray<float> leftData =
                readback
                    .GetData<float>(
                        leftSlice
                    );

            NativeArray<float> rightData =
                readback
                    .GetData<float>(
                        rightSlice
                    );

            for (
                int sampleZ = 0;
                sampleZ < samplesPerSide;
                sampleZ++
            )
            {
                int leftIndex =
                    maximumSample
                    +
                    sampleZ *
                    samplesPerSide;

                int rightIndex =
                    sampleZ *
                    samplesPerSide;

                float leftHeight =
                    leftData[
                        leftIndex
                    ];

                float rightHeight =
                    rightData[
                        rightIndex
                    ];

                float difference =
                    Mathf.Abs(
                        leftHeight -
                        rightHeight
                    );

                samplesCompared++;

                maximumDifference =
                    Mathf.Max(
                        maximumDifference,
                        difference
                    );

                if (
                    difference >
                    cacheValidationTolerance
                )
                {
                    mismatches++;

                    if (
                        firstMismatch ==
                        null
                    )
                    {
                        Vector2Int leftTile =
                            new Vector2Int(
                                cacheOriginTile.x +
                                localX,

                                cacheOriginTile.y +
                                localZ
                            );

                        Vector2Int rightTile =
                            leftTile +
                            Vector2Int.right;

                        firstMismatch =
                            $"X Boundary\n" +

                            $"Left Tile: " +
                            $"({leftTile.x}, " +
                            $"{leftTile.y})\n" +

                            $"Right Tile: " +
                            $"({rightTile.x}, " +
                            $"{rightTile.y})\n" +

                            $"Sample Z: " +
                            $"{sampleZ}\n" +

                            $"Left Height: " +
                            $"{leftHeight:R}\n" +

                            $"Right Height: " +
                            $"{rightHeight:R}\n" +

                            $"Difference: " +
                            $"{difference:R}";
                    }
                }
            }
        }
    }

    // =====================================================
    // Z-AXIS NEIGHBOURS
    // =====================================================

    for (
        int localZ = 0;
        localZ < cacheHeight - 1;
        localZ++
    )
    {
        for (
            int localX = 0;
            localX < cacheWidth;
            localX++
        )
        {
            int lowerSlice =
                localX
                +
                localZ *
                cacheWidth;

            int upperSlice =
                localX
                +
                (localZ + 1) *
                cacheWidth;

            NativeArray<float> lowerData =
                readback
                    .GetData<float>(
                        lowerSlice
                    );

            NativeArray<float> upperData =
                readback
                    .GetData<float>(
                        upperSlice
                    );

            for (
                int sampleX = 0;
                sampleX < samplesPerSide;
                sampleX++
            )
            {
                int lowerIndex =
                    sampleX
                    +
                    maximumSample *
                    samplesPerSide;

                int upperIndex =
                    sampleX;

                float lowerHeight =
                    lowerData[
                        lowerIndex
                    ];

                float upperHeight =
                    upperData[
                        upperIndex
                    ];

                float difference =
                    Mathf.Abs(
                        lowerHeight -
                        upperHeight
                    );

                samplesCompared++;

                maximumDifference =
                    Mathf.Max(
                        maximumDifference,
                        difference
                    );

                if (
                    difference >
                    cacheValidationTolerance
                )
                {
                    mismatches++;

                    if (
                        firstMismatch ==
                        null
                    )
                    {
                        Vector2Int lowerTile =
                            new Vector2Int(
                                cacheOriginTile.x +
                                localX,

                                cacheOriginTile.y +
                                localZ
                            );

                        Vector2Int upperTile =
                            lowerTile +
                            Vector2Int.up;

                        firstMismatch =
                            $"Z Boundary\n" +

                            $"Lower Tile: " +
                            $"({lowerTile.x}, " +
                            $"{lowerTile.y})\n" +

                            $"Upper Tile: " +
                            $"({upperTile.x}, " +
                            $"{upperTile.y})\n" +

                            $"Sample X: " +
                            $"{sampleX}\n" +

                            $"Lower Height: " +
                            $"{lowerHeight:R}\n" +

                            $"Upper Height: " +
                            $"{upperHeight:R}\n" +

                            $"Difference: " +
                            $"{difference:R}";
                    }
                }
            }
        }
    }
}

// =====================================================
// CACHE VALIDATION FAILURE
// =====================================================

private void FailCacheValidation(
    string reason
)
{
    lastCacheValidationPassed =
        false;

    hasValidatedCurrentCache =
        true;

    validationRoutine =
        null;

    Debug.LogError(
        "Terrain height cache validation FAILED.\n\n" +
        reason,
        this
    );
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

        if (validationRoutine != null)
        {
            StopCoroutine(
                validationRoutine
            );

            validationRoutine =
                null;
        }

        ReleaseLoadedTiles();

        DestroyHeightCache();

        initialized =
            false;

        cacheReady =
            false;

        hasValidatedCurrentCache =
            false;

        lastCacheValidationPassed =
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