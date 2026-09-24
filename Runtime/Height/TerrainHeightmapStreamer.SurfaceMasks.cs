using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/*
 * Surface-mask resource half of TerrainHeightmapStreamer.
 *
 * MRH05 moves Surface cache geometry, readiness and coverage ownership into
 * TerrainHeightmapStreamer.SurfaceResidency.cs while this partial continues
 * to own Surface Addressable handles and GPU texture resources.
 */
public partial class TerrainHeightmapStreamer
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [SerializeField]
    private TerrainSurfaceMaskManifest surfaceMaskManifest;

    // =====================================================
    // GPU CACHE
    // =====================================================

    private Texture2DArray surfaceMaskCache;

    private Texture2DArray stagingSurfaceMaskCache;

    private readonly Dictionary<Vector2Int, ResidentSurfaceTile>
        residentSurfaceTiles =
            new Dictionary<Vector2Int, ResidentSurfaceTile>();

    /*
     * Surface assets acquired by the current cache-window transition.
     * On failure these are rolled back without disturbing retained assets.
     */
    private readonly List<Vector2Int>
        currentSurfaceLoadAcquisitions =
            new List<Vector2Int>();

    public Texture2DArray SurfaceMaskCache =>
        surfaceMaskCache;

    public TerrainSurfaceMaskManifest SurfaceMaskManifest =>
        surfaceMaskManifest;

    // =====================================================
    // HIERARCHY CONFIGURATION
    // =====================================================

    public bool ConfigureSurfaceMaskManifest(
        TerrainSurfaceMaskManifest manifest
    )
    {
        if (surfaceMaskManifest == manifest)
        {
            return false;
        }

        surfaceMaskManifest =
            manifest;

        return true;
    }

    // =====================================================
    // VALIDATION
    // =====================================================

    private bool ValidateSurfaceMaskConfiguration()
    {
        if (surfaceMaskManifest == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainSurfaceMaskManifest is not assigned.\n\n" +
                "Open Runtime and run Bake Runtime Changes. If Runtime reports " +
                "a structural hierarchy problem, run Setup / Repair World Hierarchy.",
                this
            );

            return false;
        }

        if (!surfaceMaskManifest.isComplete)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The surface-mask manifest is incomplete.",
                this
            );

            return false;
        }

        if (
            surfaceMaskManifest.compilerVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentCompilerVersion
            ||
            surfaceMaskManifest.channelLayoutVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentChannelLayoutVersion
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The generated surface-mask format is out of date.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            !surfaceMaskManifest
                .MatchesHeightLayout(
                    heightmapManifest
                )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The surface-mask layout does not match the runtime heightmap layout.",
                this
            );

            return false;
        }

        if (
            surfaceMaskManifest
                .sourceHeightmapGenerationRevision !=
            worldSettings
                .heightmapGenerationRevision
            ||
            surfaceMaskManifest
                .sourceHeightmapSignature !=
            worldSettings
                .lastGeneratedHeightSignature
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The baked surface masks were generated from an older runtime heightmap revision.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            surfaceMaskManifest
                .surfaceMaskGenerationRevision !=
            worldSettings
                .surfaceMaskGenerationRevision
            ||
            surfaceMaskManifest
                .surfaceGenerationSignature !=
            worldSettings
                .lastGeneratedSurfaceSignature
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "WorldSettings and the surface-mask manifest do not describe the same generated surface revision.",
                this
            );

            return false;
        }

        TerrainSurfaceSettings settings =
            TerrainSurfaceSettings.LoadDefault();

        if (settings == null)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainSurfaceSettings could not be loaded from Resources.",
                this
            );

            return false;
        }

        string currentSettingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    settings
                );

        if (
            string.IsNullOrEmpty(
                currentSettingsSignature
            )
            ||
            currentSettingsSignature !=
                surfaceMaskManifest
                    .surfaceSettingsSignature
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "TerrainSurfaceSettings changed after the runtime surface masks were baked.\n\n" +
                "Open Runtime and run Bake Runtime Changes.",
                this
            );

            return false;
        }

        if (
            !SystemInfo.SupportsTextureFormat(
                TextureFormat.R8
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize.\n\n" +
                "The current graphics device does not support TextureFormat.R8.",
                this
            );

            return false;
        }

        return true;
    }

    // =====================================================
    // GPU CACHE BUFFERS
    // =====================================================

    private bool CreateSurfaceMaskCacheBuffers()
    {
        DestroySurfaceMaskCacheBuffers();

        int samplesPerSide =
            surfaceMaskManifest
                .samplesPerSide;

        int sliceCount =
            cacheWidth *
            cacheHeight;

        try
        {
            surfaceMaskCache =
                CreateSurfaceMaskCacheTexture(
                    "Terrain Surface Mask Cache A",
                    samplesPerSide,
                    sliceCount
                );

            stagingSurfaceMaskCache =
                CreateSurfaceMaskCacheTexture(
                    "Terrain Surface Mask Cache B",
                    samplesPerSide,
                    sliceCount
                );
        }
        catch (
            System.Exception exception
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not create the double-buffered GPU surface-mask cache.\n\n" +
                exception.Message,
                this
            );

            DestroySurfaceMaskCacheBuffers();

            return false;
        }

        return true;
    }

    private static Texture2DArray CreateSurfaceMaskCacheTexture(
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
                TextureFormat.R8,
                false,
                true
            );

        cache.name =
            textureName;

        cache.wrapMode =
            TextureWrapMode.Clamp;

        cache.filterMode =
            FilterMode.Bilinear;

        cache.anisoLevel =
            0;

        return cache;
    }

    private void DestroySurfaceMaskCacheBuffers()
    {
        if (surfaceMaskCache != null)
        {
            Destroy(
                surfaceMaskCache
            );

            surfaceMaskCache =
                null;
        }

        if (stagingSurfaceMaskCache != null)
        {
            Destroy(
                stagingSurfaceMaskCache
            );

            stagingSurfaceMaskCache =
                null;
        }
    }

    // =====================================================
    // LOAD ATTEMPT LIFETIME
    // =====================================================

    private void BeginSurfaceLoadAttempt()
    {
        currentSurfaceLoadAcquisitions
            .Clear();
    }

    private void RollbackSurfaceLoadAttempt()
    {
        for (
            int index =
                currentSurfaceLoadAcquisitions.Count - 1;
            index >= 0;
            index--
        )
        {
            ReleaseResidentSurfaceTile(
                currentSurfaceLoadAcquisitions[
                    index
                ]
            );
        }

        currentSurfaceLoadAcquisitions
            .Clear();
    }

    private void CompleteSurfaceLoadAttempt(
        HashSet<Vector2Int> requiredTiles
    )
    {
        currentSurfaceLoadAcquisitions
            .Clear();

        ReleaseResidentSurfaceTilesNotRequired(
            requiredTiles
        );
    }

    // =====================================================
    // RESIDENT SURFACE TILES
    // =====================================================

    private bool TryGetUsableResidentSurfaceTile(
        Vector2Int coordinate,
        out ResidentSurfaceTile residentTile
    )
    {
        residentTile =
            null;

        if (
            !residentSurfaceTiles.TryGetValue(
                coordinate,
                out ResidentSurfaceTile existing
            )
        )
        {
            return false;
        }

        bool usable =
            existing != null
            &&
            existing.texture != null
            &&
            existing.handle.IsValid()
            &&
            existing.handle.Status ==
                AsyncOperationStatus.Succeeded;

        if (!usable)
        {
            ReleaseResidentSurfaceTile(
                coordinate
            );

            return false;
        }

        residentTile =
            existing;

        return true;
    }

    private bool TryBeginSurfaceTileLoad(
        Vector2Int coordinate,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            TryGetUsableResidentSurfaceTile(
                coordinate,
                out _
            )
        )
        {
            return true;
        }

        string address =
            surfaceMaskManifest
                .GetSurfaceTileAddress(
                    coordinate.x,
                    coordinate.y
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
            errorMessage =
                "Failed to begin loading Addressable surface-mask tile.\n\n" +
                $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                $"Address: {address}\n\n" +
                exception.Message;

            return false;
        }

        ResidentSurfaceTile tile =
            new ResidentSurfaceTile(
                coordinate,
                address,
                handle
            );

        residentSurfaceTiles[
            coordinate
        ] =
            tile;

        currentSurfaceLoadAcquisitions
            .Add(
                coordinate
            );

        return true;
    }

    private bool TryGetResidentSurfaceTile(
        Vector2Int coordinate,
        out ResidentSurfaceTile tile
    )
    {
        return
            residentSurfaceTiles.TryGetValue(
                coordinate,
                out tile
            )
            &&
            tile != null;
    }

    private bool TryFinalizeResidentSurfaceTile(
        Vector2Int coordinate,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryGetResidentSurfaceTile(
                coordinate,
                out ResidentSurfaceTile tile
            )
        )
        {
            errorMessage =
                "A requested surface-mask tile was missing from the residency table.";

            return false;
        }

        if (
            !tile.handle.IsValid()
            ||
            tile.handle.Status !=
                AsyncOperationStatus.Succeeded
            ||
            tile.handle.Result == null
        )
        {
            errorMessage =
                "Failed to load Addressable surface-mask tile.\n\n" +
                $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                $"Address: {tile.address}";

            return false;
        }

        Texture2D texture =
            tile.handle.Result;

        if (
            texture.width !=
                surfaceMaskManifest.samplesPerSide
            ||
            texture.height !=
                surfaceMaskManifest.samplesPerSide
        )
        {
            errorMessage =
                "Loaded surface-mask tile has incorrect dimensions.\n\n" +
                $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                $"Expected: {surfaceMaskManifest.samplesPerSide} x {surfaceMaskManifest.samplesPerSide}\n" +
                $"Actual: {texture.width} x {texture.height}";

            return false;
        }

        if (
            texture.format !=
                TextureFormat.R8
        )
        {
            errorMessage =
                "Loaded surface-mask tile has incorrect texture format.\n\n" +
                $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                $"Expected: {TextureFormat.R8}\n" +
                $"Actual: {texture.format}";

            return false;
        }

        tile.texture =
            texture;

        return true;
    }

    // =====================================================
    // STAGING CACHE
    // =====================================================

    private bool TryPopulateSurfaceStagingCache(
        Vector2Int origin,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (stagingSurfaceMaskCache == null)
        {
            errorMessage =
                "The staging surface-mask cache is unavailable.";

            return false;
        }

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
                Vector2Int coordinate =
                    new Vector2Int(
                        origin.x + localX,
                        origin.y + localZ
                    );

                if (
                    !TryGetUsableResidentSurfaceTile(
                        coordinate,
                        out ResidentSurfaceTile tile
                    )
                )
                {
                    errorMessage =
                        "A required resident surface-mask tile was missing while building the staging cache.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})";

                    return false;
                }

                if (
                    !TryResolveCacheSlice(
                        coordinate,
                        origin,
                        cacheWidth,
                        cacheHeight,
                        out int slice
                    )
                )
                {
                    errorMessage =
                        "Could not resolve a GPU cache slice for a required surface-mask tile.";

                    return false;
                }

                try
                {
                    Graphics.CopyTexture(
                        tile.texture,
                        0,
                        0,
                        stagingSurfaceMaskCache,
                        slice,
                        0
                    );
                }
                catch (
                    System.Exception exception
                )
                {
                    errorMessage =
                        "Could not copy resident surface-mask tile into the staging GPU cache.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                        $"Slice: {slice}\n\n" +
                        exception.Message;

                    return false;
                }
            }
        }

        return true;
    }

    private void SwapSurfaceMaskCaches()
    {
        Texture2DArray previous =
            surfaceMaskCache;

        surfaceMaskCache =
            stagingSurfaceMaskCache;

        stagingSurfaceMaskCache =
            previous;
    }

    // =====================================================
    // SHADER BINDING
    // =====================================================

    private bool BindSurfaceMaskCacheToClipmapRenderers(
        out string errorMessage
    )
    {
        return
            TerrainSurfaceMaskBindingUtility
                .TryBind(
                    transform,
                    surfaceMaskCache,
                    surfaceCacheOriginTile,
                    new Vector2Int(
                        surfaceCacheWidth,
                        surfaceCacheHeight
                    ),
                    surfaceMaskManifest
                        .samplesPerSide,
                    surfaceMaskManifest
                        .sampleSpacing,
                    out _,
                    out errorMessage
                );
    }

    private void DisableSurfaceMaskOnClipmapRenderers()
    {
        TerrainSurfaceMaskBindingUtility
            .Disable(
                transform
            );
    }

    // =====================================================
    // RELEASE RESIDENT SURFACE TILES
    // =====================================================

    private void ReleaseResidentSurfaceTile(
        Vector2Int coordinate
    )
    {
        if (
            !residentSurfaceTiles.TryGetValue(
                coordinate,
                out ResidentSurfaceTile tile
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

        residentSurfaceTiles.Remove(
            coordinate
        );
    }

    private int ReleaseResidentSurfaceTilesNotRequired(
        HashSet<Vector2Int> requiredTiles
    )
    {
        List<Vector2Int> leaving =
            new List<Vector2Int>();

        foreach (
            KeyValuePair<
                Vector2Int,
                ResidentSurfaceTile
            > pair
            in residentSurfaceTiles
        )
        {
            if (
                requiredTiles == null
                ||
                !requiredTiles.Contains(
                    pair.Key
                )
            )
            {
                leaving.Add(
                    pair.Key
                );
            }
        }

        foreach (
            Vector2Int coordinate
            in leaving
        )
        {
            ReleaseResidentSurfaceTile(
                coordinate
            );
        }

        return leaving.Count;
    }

    private void ReleaseResidentSurfaceTiles()
    {
        List<Vector2Int> coordinates =
            new List<Vector2Int>(
                residentSurfaceTiles.Keys
            );

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            ReleaseResidentSurfaceTile(
                coordinate
            );
        }

        currentSurfaceLoadAcquisitions
            .Clear();
    }

    // =====================================================
    // RESIDENT TILE
    // =====================================================

    private sealed class ResidentSurfaceTile
    {
        public readonly Vector2Int coordinate;
        public readonly string address;

        public readonly AsyncOperationHandle<Texture2D>
            handle;

        public Texture2D texture;

        public ResidentSurfaceTile(
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
