using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public enum TerrainSurfaceCacheTransitionState
{
    Idle,
    LoadingSources,
    PopulatingStaging,
    CommitPending
}

/*
 * MRH05 surface-residency ownership and composite transition orchestration.
 *
 * Surface masks remain full-resolution, but their active/requested cache
 * rectangle, readiness, coverage and diagnostics are independent from the
 * singular stride-1 Height cache. The same TerrainHeightmapStreamer component
 * still owns one composite transition coroutine so Height and Surface can only
 * publish a requested terrain layout after all required data is ready.
 */
public partial class TerrainHeightmapStreamer
{
    // =====================================================
    // SURFACE CACHE GEOMETRY / STATE
    // =====================================================

    private Vector2Int surfaceCacheOriginTile;

    private Vector2Int requestedSurfaceOriginTile;

    private int surfaceCacheWidth;

    private int surfaceCacheHeight;

    private bool surfaceCacheReady;

    private TerrainSurfaceCacheTransitionState
        surfaceTransitionState =
            TerrainSurfaceCacheTransitionState.Idle;

    private bool hasPublishedActiveSurfaceCacheCoverage;

    private Vector2 publishedActiveSurfaceCacheMinimumXZ =
        Vector2.zero;

    private Vector2 publishedActiveSurfaceCacheMaximumXZ =
        Vector2.zero;

    public event System.Action ActiveSurfaceCacheCoverageChanged;

    public Vector2Int SurfaceCacheOriginTile =>
        surfaceCacheOriginTile;

    public Vector2Int RequestedSurfaceOriginTile =>
        requestedSurfaceOriginTile;

    public int SurfaceCacheWidth =>
        surfaceCacheWidth;

    public int SurfaceCacheHeight =>
        surfaceCacheHeight;

    public bool SurfaceCacheReady =>
        surfaceCacheReady;

    public int ResidentSurfaceTileCount =>
        residentSurfaceTiles.Count;

    public TerrainSurfaceCacheTransitionState SurfaceTransitionState =>
        surfaceTransitionState;

    public long EstimatedSurfaceGpuCacheBytes
    {
        get
        {
            if (
                surfaceMaskManifest == null
                ||
                surfaceCacheWidth <= 0
                ||
                surfaceCacheHeight <= 0
                ||
                (
                    surfaceMaskCache == null
                    &&
                    stagingSurfaceMaskCache == null
                )
            )
            {
                return 0L;
            }

            long samplesPerSide =
                Mathf.Max(
                    0,
                    surfaceMaskManifest.samplesPerSide
                );

            long sliceCount =
                (long)surfaceCacheWidth *
                surfaceCacheHeight;

            /*
             * R8 is one byte per texel. Include both active and staging
             * Texture2DArray buffers. This is a raw payload estimate only.
             */
            return
                samplesPerSide *
                samplesPerSide *
                sliceCount *
                2L;
        }
    }

    // =====================================================
    // COMPOSITE TRANSITION REQUEST
    // =====================================================

    private readonly struct CacheTransitionRequest
    {
        public readonly Vector2Int HeightOrigin;
        public readonly Vector2Int SurfaceOrigin;

        public readonly bool HeightRequired;
        public readonly bool SurfaceRequired;

        public CacheTransitionRequest(
            Vector2Int heightOrigin,
            Vector2Int surfaceOrigin,
            bool heightRequired,
            bool surfaceRequired
        )
        {
            HeightOrigin =
                heightOrigin;

            SurfaceOrigin =
                surfaceOrigin;

            HeightRequired =
                heightRequired;

            SurfaceRequired =
                surfaceRequired;
        }
    }

    private void TryBeginRequestedCacheTransition()
    {
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

        Vector2Int desiredHeightOrigin =
            CalculateRequestedCacheOrigin();

        Vector2Int desiredSurfaceOrigin =
            CalculateRequestedSurfaceCacheOrigin();

        requestedOriginTile =
            desiredHeightOrigin;

        requestedSurfaceOriginTile =
            desiredSurfaceOrigin;

        if (loadRoutine != null)
        {
            return;
        }

        bool heightTransitionRequired =
            !cacheReady
            ||
            desiredHeightOrigin !=
                cacheOriginTile;

        bool surfaceTransitionRequired =
            !surfaceCacheReady
            ||
            desiredSurfaceOrigin !=
                surfaceCacheOriginTile;

        if (
            !heightTransitionRequired
            &&
            !surfaceTransitionRequired
        )
        {
            return;
        }

        CacheTransitionRequest request =
            new CacheTransitionRequest(
                desiredHeightOrigin,
                desiredSurfaceOrigin,
                heightTransitionRequired,
                surfaceTransitionRequired
            );

        loadRoutine =
            StartCoroutine(
                CompositeCacheTransitionRoutine(
                    request
                )
            );
    }

    // =====================================================
    // SURFACE CACHE DIMENSIONS
    // =====================================================

    private void CalculateSurfaceCacheDimensions()
    {
        if (surfaceMaskManifest == null)
        {
            surfaceCacheWidth =
                0;

            surfaceCacheHeight =
                0;

            return;
        }

        float clipmapDiameter =
            CalculateClipmapDiameter();

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                surfaceMaskManifest.tileWorldSize
            );

        int clipmapTileSpan =
            Mathf.CeilToInt(
                clipmapDiameter /
                tileWorldSize
            );

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

        surfaceCacheWidth =
            Mathf.Min(
                requestedCacheSize,
                Mathf.Max(
                    1,
                    surfaceMaskManifest.tileGridWidth
                )
            );

        surfaceCacheHeight =
            Mathf.Min(
                requestedCacheSize,
                Mathf.Max(
                    1,
                    surfaceMaskManifest.tileGridHeight
                )
            );
    }

    private bool CreateDecoupledSurfaceMaskCacheBuffers()
    {
        DestroySurfaceMaskCacheBuffers();

        if (
            surfaceMaskManifest == null
            ||
            surfaceCacheWidth <= 0
            ||
            surfaceCacheHeight <= 0
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot create the surface-mask cache because its independent cache dimensions are invalid.",
                this
            );

            return false;
        }

        int samplesPerSide =
            surfaceMaskManifest.samplesPerSide;

        int sliceCount =
            surfaceCacheWidth *
            surfaceCacheHeight;

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
        catch (System.Exception exception)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not create the independently sized double-buffered GPU surface-mask cache.\n\n" +
                exception.Message,
                this
            );

            DestroySurfaceMaskCacheBuffers();

            return false;
        }

        return true;
    }

    // =====================================================
    // SURFACE REQUESTED CACHE ORIGIN
    // =====================================================

    private Vector2Int CalculateRequestedSurfaceCacheOrigin()
    {
        if (!hasRequestedClipmapCenter)
        {
            Vector3 center =
                streamingTarget != null
                    ? streamingTarget.position
                    : transform.position;

            return
                CalculateRequiredSurfaceCacheOrigin(
                    center
                );
        }

        if (!surfaceCacheReady)
        {
            return
                CalculateRequiredSurfaceCacheOrigin(
                    requestedClipmapCenter
                );
        }

        return
            CalculatePrefetchSurfaceCacheOrigin(
                requestedClipmapCenter
            );
    }

    private Vector2Int CalculateRequiredSurfaceCacheOrigin(
        Vector3 center
    )
    {
        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                surfaceMaskManifest.tileWorldSize
            );

        float cacheWorldWidth =
            surfaceCacheWidth *
            tileWorldSize;

        float cacheWorldHeight =
            surfaceCacheHeight *
            tileWorldSize;

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
            GetMaximumSurfaceCacheOrigin();

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

    private Vector2Int CalculatePrefetchSurfaceCacheOrigin(
        Vector3 clipmapCenter
    )
    {
        if (!surfaceCacheReady)
        {
            return
                CalculateRequiredSurfaceCacheOrigin(
                    clipmapCenter
                );
        }

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                surfaceMaskManifest.tileWorldSize
            );

        float prefetchMargin =
            tileWorldSize *
            Mathf.Clamp01(
                prefetchTileFraction
            );

        if (
            !TryCalculateVisibleSurfaceTileBounds(
                clipmapCenter,
                prefetchMargin,
                out Vector2Int minimumTile,
                out Vector2Int maximumTile
            )
        )
        {
            return
                CalculateRequiredSurfaceCacheOrigin(
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
            requiredWidth > surfaceCacheWidth
            ||
            requiredHeight > surfaceCacheHeight
        )
        {
            return
                CalculateRequiredSurfaceCacheOrigin(
                    clipmapCenter
                );
        }

        int minimumAllowedOriginX =
            maximumTile.x -
            surfaceCacheWidth +
            1;

        int maximumAllowedOriginX =
            minimumTile.x;

        int minimumAllowedOriginZ =
            maximumTile.y -
            surfaceCacheHeight +
            1;

        int maximumAllowedOriginZ =
            minimumTile.y;

        int originX =
            Mathf.Clamp(
                surfaceCacheOriginTile.x,
                minimumAllowedOriginX,
                maximumAllowedOriginX
            );

        int originZ =
            Mathf.Clamp(
                surfaceCacheOriginTile.y,
                minimumAllowedOriginZ,
                maximumAllowedOriginZ
            );

        Vector2Int maximumOrigin =
            GetMaximumSurfaceCacheOrigin();

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

    private Vector2Int GetMaximumSurfaceCacheOrigin()
    {
        return
            new Vector2Int(
                Mathf.Max(
                    0,
                    surfaceMaskManifest.tileGridWidth -
                    surfaceCacheWidth
                ),
                Mathf.Max(
                    0,
                    surfaceMaskManifest.tileGridHeight -
                    surfaceCacheHeight
                )
            );
    }

    // =====================================================
    // SURFACE COVERAGE
    // =====================================================

    public bool CanActiveSurfaceCacheCoverClipmapAt(
        Vector3 clipmapCenter
    )
    {
        if (
            !surfaceCacheReady
            ||
            surfaceMaskCache == null
            ||
            surfaceMaskManifest == null
        )
        {
            return false;
        }

        float sampleMargin =
            Mathf.Max(
                0f,
                surfaceMaskManifest.sampleSpacing
            );

        if (
            !TryCalculateVisibleSurfaceTileBounds(
                clipmapCenter,
                sampleMargin,
                out Vector2Int minimumTile,
                out Vector2Int maximumTile
            )
        )
        {
            return true;
        }

        return
            AreSurfaceTileBoundsInsideCache(
                minimumTile,
                maximumTile,
                surfaceCacheOriginTile
            );
    }

    public bool CanActiveTerrainCachesCoverClipmapAt(
        Vector3 clipmapCenter
    )
    {
        return
            CanActiveCacheCoverClipmapAt(
                clipmapCenter
            )
            &&
            CanActiveSurfaceCacheCoverClipmapAt(
                clipmapCenter
            );
    }

    public bool TryGetActiveSurfaceCacheWorldCoverage(
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        if (
            !surfaceCacheReady
            ||
            surfaceMaskManifest == null
            ||
            surfaceCacheWidth <= 0
            ||
            surfaceCacheHeight <= 0
        )
        {
            return false;
        }

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                surfaceMaskManifest.tileWorldSize
            );

        minimumXZ =
            new Vector2(
                surfaceCacheOriginTile.x *
                    tileWorldSize,
                surfaceCacheOriginTile.y *
                    tileWorldSize
            );

        maximumXZ =
            new Vector2(
                Mathf.Min(
                    surfaceMaskManifest.worldSizeXZ.x,
                    (
                        surfaceCacheOriginTile.x +
                        surfaceCacheWidth
                    ) *
                    tileWorldSize
                ),
                Mathf.Min(
                    surfaceMaskManifest.worldSizeXZ.y,
                    (
                        surfaceCacheOriginTile.y +
                        surfaceCacheHeight
                    ) *
                    tileWorldSize
                )
            );

        return true;
    }

    private void NotifyActiveSurfaceCacheCoverageIfChanged()
    {
        bool hasCoverage =
            TryGetActiveSurfaceCacheWorldCoverage(
                out Vector2 minimumXZ,
                out Vector2 maximumXZ
            );

        bool unchanged =
            hasPublishedActiveSurfaceCacheCoverage ==
                hasCoverage
            &&
            (
                !hasCoverage
                ||
                (
                    publishedActiveSurfaceCacheMinimumXZ ==
                        minimumXZ
                    &&
                    publishedActiveSurfaceCacheMaximumXZ ==
                        maximumXZ
                )
            );

        if (unchanged)
        {
            return;
        }

        hasPublishedActiveSurfaceCacheCoverage =
            hasCoverage;

        publishedActiveSurfaceCacheMinimumXZ =
            hasCoverage
                ? minimumXZ
                : Vector2.zero;

        publishedActiveSurfaceCacheMaximumXZ =
            hasCoverage
                ? maximumXZ
                : Vector2.zero;

        ActiveSurfaceCacheCoverageChanged?.Invoke();
    }

    private bool TryCalculateVisibleSurfaceTileBounds(
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
                surfaceMaskManifest.worldSizeXZ.x
            );

        float worldSizeZ =
            Mathf.Max(
                0f,
                surfaceMaskManifest.worldSizeXZ.y
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
            visibleMinimumX > visibleMaximumX
            ||
            visibleMinimumZ > visibleMaximumZ
        )
        {
            return false;
        }

        minimumTile =
            new Vector2Int(
                SurfaceWorldPositionToTileCoordinate(
                    visibleMinimumX,
                    worldSizeX,
                    surfaceMaskManifest.tileGridWidth
                ),
                SurfaceWorldPositionToTileCoordinate(
                    visibleMinimumZ,
                    worldSizeZ,
                    surfaceMaskManifest.tileGridHeight
                )
            );

        maximumTile =
            new Vector2Int(
                SurfaceWorldPositionToTileCoordinate(
                    visibleMaximumX,
                    worldSizeX,
                    surfaceMaskManifest.tileGridWidth
                ),
                SurfaceWorldPositionToTileCoordinate(
                    visibleMaximumZ,
                    worldSizeZ,
                    surfaceMaskManifest.tileGridHeight
                )
            );

        return true;
    }

    private int SurfaceWorldPositionToTileCoordinate(
        float coordinate,
        float worldSize,
        int tileCount
    )
    {
        float sampleSpacing =
            Mathf.Max(
                0.000001f,
                surfaceMaskManifest.sampleSpacing
            );

        int samplesPerSide =
            Mathf.Max(
                2,
                surfaceMaskManifest.samplesPerSide
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
                    tileCount -
                    1
                )
            );
    }

    private bool AreSurfaceTileBoundsInsideCache(
        Vector2Int minimumTile,
        Vector2Int maximumTile,
        Vector2Int cacheOrigin
    )
    {
        return
            minimumTile.x >= cacheOrigin.x
            &&
            minimumTile.y >= cacheOrigin.y
            &&
            maximumTile.x <
                cacheOrigin.x +
                surfaceCacheWidth
            &&
            maximumTile.y <
                cacheOrigin.y +
                surfaceCacheHeight;
    }

    // =====================================================
    // SURFACE TRANSITION PLAN / STAGING
    // =====================================================

    private bool TryBuildSurfaceTransitionPlan(
        Vector2Int origin,
        HashSet<Vector2Int> requiredTiles,
        List<Vector2Int> enteringTiles,
        out int retainedTileCount,
        out string errorMessage
    )
    {
        retainedTileCount =
            0;

        errorMessage =
            "";

        for (
            int localZ = 0;
            localZ < surfaceCacheHeight;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < surfaceCacheWidth;
                localX++
            )
            {
                Vector2Int coordinate =
                    new Vector2Int(
                        origin.x + localX,
                        origin.y + localZ
                    );

                if (
                    !surfaceMaskManifest
                        .IsTileCoordinateValid(
                            coordinate.x,
                            coordinate.y
                        )
                )
                {
                    errorMessage =
                        "Terrain surface cache requested an invalid tile coordinate.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})";

                    return false;
                }

                requiredTiles.Add(
                    coordinate
                );

                if (
                    TryGetUsableResidentSurfaceTile(
                        coordinate,
                        out _
                    )
                )
                {
                    retainedTileCount++;
                }
                else
                {
                    enteringTiles.Add(
                        coordinate
                    );
                }
            }
        }

        return true;
    }

    private bool TryPopulateDecoupledSurfaceStagingCache(
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
            localZ < surfaceCacheHeight;
            localZ++
        )
        {
            for (
                int localX = 0;
                localX < surfaceCacheWidth;
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
                        "A required resident surface-mask tile was missing while building the independent staging cache.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})";

                    return false;
                }

                if (
                    !TryResolveCacheSlice(
                        coordinate,
                        origin,
                        surfaceCacheWidth,
                        surfaceCacheHeight,
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
                catch (System.Exception exception)
                {
                    errorMessage =
                        "Could not copy resident surface-mask tile into the independent staging GPU cache.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                        $"Slice: {slice}\n\n" +
                        exception.Message;

                    return false;
                }
            }
        }

        return true;
    }

    private bool TryPopulateHeightStagingCacheForTransition(
        Vector2Int origin,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (stagingHeightCache == null)
        {
            errorMessage =
                "The staging height cache is unavailable.";

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
                    !TryGetUsableResidentTile(
                        coordinate,
                        out ResidentTile tile
                    )
                )
                {
                    errorMessage =
                        "A required resident height tile was missing while building the staging GPU cache.\n\n" +
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
                        "Could not resolve a GPU cache slice for a required terrain height tile.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                        $"Cache Origin: ({origin.x}, {origin.y})\n" +
                        $"Cache Size: {cacheWidth} x {cacheHeight}";

                    return false;
                }

                try
                {
                    Graphics.CopyTexture(
                        tile.texture,
                        0,
                        0,
                        stagingHeightCache,
                        slice,
                        0
                    );
                }
                catch (System.Exception exception)
                {
                    errorMessage =
                        "Could not copy resident heightmap tile into the staging terrain height cache.\n\n" +
                        $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                        $"Slice: {slice}\n\n" +
                        exception.Message;

                    return false;
                }
            }
        }

        return true;
    }

    // =====================================================
    // COMPOSITE CACHE TRANSITION
    // =====================================================

    private IEnumerator CompositeCacheTransitionRoutine(
        CacheTransitionRequest request
    )
    {
        if (
            request.HeightRequired
            &&
            (
                heightCache == null
                ||
                stagingHeightCache == null
            )
        )
        {
            Debug.LogError(
                "Terrain height cache buffers are not available.",
                this
            );

            loadRoutine =
                null;

            yield break;
        }

        if (
            request.SurfaceRequired
            &&
            (
                surfaceMaskCache == null
                ||
                stagingSurfaceMaskCache == null
            )
        )
        {
            Debug.LogError(
                "Terrain surface-mask cache buffers are not available.",
                this
            );

            loadRoutine =
                null;

            yield break;
        }

        int samplesPerSide =
            heightmapManifest.heightTileSamplesPerSide;

        HashSet<Vector2Int> requiredHeightTiles =
            new HashSet<Vector2Int>();

        List<Vector2Int> enteringHeightTiles =
            new List<Vector2Int>();

        List<Vector2Int> newlyLoadedHeightTiles =
            new List<Vector2Int>();

        HashSet<Vector2Int> requiredSurfaceTiles =
            new HashSet<Vector2Int>();

        List<Vector2Int> enteringSurfaceTiles =
            new List<Vector2Int>();

        int retainedHeightTileCount =
            0;

        int retainedSurfaceTileCount =
            0;

        int loadedHeightTileCount =
            0;

        bool surfaceAttemptStarted =
            false;

        if (request.HeightRequired)
        {
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
                            request.HeightOrigin.x + localX,
                            request.HeightOrigin.y + localZ
                        );

                    if (
                        !heightmapManifest
                            .IsTileCoordinateValid(
                                coordinate.x,
                                coordinate.y
                            )
                    )
                    {
                        Debug.LogError(
                            "Terrain height cache requested an invalid tile coordinate.\n\n" +
                            $"Tile: ({coordinate.x}, {coordinate.y})",
                            this
                        );

                        FailCompositeCacheTransition(
                            newlyLoadedHeightTiles,
                            false
                        );

                        yield break;
                    }

                    requiredHeightTiles.Add(
                        coordinate
                    );

                    if (
                        TryGetUsableResidentTile(
                            coordinate,
                            out _
                        )
                    )
                    {
                        retainedHeightTileCount++;
                    }
                    else
                    {
                        enteringHeightTiles.Add(
                            coordinate
                        );
                    }
                }
            }
        }

        if (request.SurfaceRequired)
        {
            BeginSurfaceLoadAttempt();

            surfaceAttemptStarted =
                true;

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.LoadingSources;

            if (
                !TryBuildSurfaceTransitionPlan(
                    request.SurfaceOrigin,
                    requiredSurfaceTiles,
                    enteringSurfaceTiles,
                    out retainedSurfaceTileCount,
                    out string surfacePlanError
                )
            )
            {
                Debug.LogError(
                    surfacePlanError,
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    true
                );

                yield break;
            }
        }

        foreach (
            Vector2Int coordinate
            in enteringHeightTiles
        )
        {
            string address =
                heightmapManifest
                    .GetHeightRepresentationAddress(
                        1,
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
            catch (System.Exception exception)
            {
                Debug.LogError(
                    "Failed to begin loading Addressable heightmap tile.\n\n" +
                    $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                    $"Address: {address}\n\n" +
                    exception.Message,
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }

            ResidentTile residentTile =
                new ResidentTile(
                    coordinate,
                    address,
                    handle
                );

            residentTiles[
                coordinate
            ] =
                residentTile;

            newlyLoadedHeightTiles.Add(
                coordinate
            );
        }

        foreach (
            Vector2Int coordinate
            in enteringSurfaceTiles
        )
        {
            if (
                !TryBeginSurfaceTileLoad(
                    coordinate,
                    out string surfaceLoadError
                )
            )
            {
                Debug.LogError(
                    surfaceLoadError,
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }
        }

        foreach (
            Vector2Int coordinate
            in newlyLoadedHeightTiles
        )
        {
            if (
                !residentTiles.TryGetValue(
                    coordinate,
                    out ResidentTile residentTile
                )
                ||
                residentTile == null
            )
            {
                Debug.LogError(
                    "A newly requested terrain height tile was not present in the residency table.\n\n" +
                    $"Tile: ({coordinate.x}, {coordinate.y})",
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
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
                handle.Status != AsyncOperationStatus.Succeeded
                ||
                handle.Result == null
            )
            {
                Debug.LogError(
                    "Failed to load Addressable heightmap tile.\n\n" +
                    $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                    $"Address: {residentTile.address}",
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }

            Texture2D texture =
                handle.Result;

            residentTile.texture =
                texture;

            if (
                texture.width != samplesPerSide
                ||
                texture.height != samplesPerSide
                ||
                texture.format != TextureFormat.RFloat
            )
            {
                Debug.LogError(
                    "Loaded heightmap tile has an invalid layout or format.\n\n" +
                    $"Tile: ({coordinate.x}, {coordinate.y})\n" +
                    $"Expected: {samplesPerSide} x {samplesPerSide}, {TextureFormat.RFloat}\n" +
                    $"Actual: {texture.width} x {texture.height}, {texture.format}",
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }

            loadedHeightTileCount++;
        }

        foreach (
            Vector2Int coordinate
            in enteringSurfaceTiles
        )
        {
            if (
                !TryGetResidentSurfaceTile(
                    coordinate,
                    out ResidentSurfaceTile surfaceTile
                )
            )
            {
                Debug.LogError(
                    "A newly requested terrain surface-mask tile was not present in the residency table.\n\n" +
                    $"Tile: ({coordinate.x}, {coordinate.y})",
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }

            AsyncOperationHandle<Texture2D> surfaceHandle =
                surfaceTile.handle;

            if (!surfaceHandle.IsDone)
            {
                yield return surfaceHandle;
            }

            if (
                !TryFinalizeResidentSurfaceTile(
                    coordinate,
                    out string surfaceFinalizeError
                )
            )
            {
                Debug.LogError(
                    surfaceFinalizeError,
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }
        }

        if (
            request.HeightRequired
            &&
            !TryPopulateHeightStagingCacheForTransition(
                request.HeightOrigin,
                out string heightStagingError
            )
        )
        {
            Debug.LogError(
                heightStagingError,
                this
            );

            FailCompositeCacheTransition(
                newlyLoadedHeightTiles,
                surfaceAttemptStarted
            );

            yield break;
        }

        if (request.SurfaceRequired)
        {
            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.PopulatingStaging;

            if (
                !TryPopulateDecoupledSurfaceStagingCache(
                    request.SurfaceOrigin,
                    out string surfaceStagingError
                )
            )
            {
                Debug.LogError(
                    surfaceStagingError,
                    this
                );

                FailCompositeCacheTransition(
                    newlyLoadedHeightTiles,
                    surfaceAttemptStarted
                );

                yield break;
            }

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.CommitPending;
        }

        if (request.HeightRequired)
        {
            Texture2DArray previousActiveHeightCache =
                heightCache;

            heightCache =
                stagingHeightCache;

            stagingHeightCache =
                previousActiveHeightCache;

            cacheOriginTile =
                request.HeightOrigin;

            cacheReady =
                true;
        }

        if (request.SurfaceRequired)
        {
            SwapSurfaceMaskCaches();

            surfaceCacheOriginTile =
                request.SurfaceOrigin;

            surfaceCacheReady =
                true;

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.Idle;
        }

        BindHeightCacheToClipmapRenderers();

        if (request.HeightRequired)
        {
            NotifyActiveCacheCoverageIfChanged();
        }

        if (request.SurfaceRequired)
        {
            NotifyActiveSurfaceCacheCoverageIfChanged();
        }

        int releasedHeightTileCount =
            request.HeightRequired
                ? ReleaseResidentTilesNotRequired(
                    requiredHeightTiles
                )
                : 0;

        if (request.SurfaceRequired)
        {
            CompleteSurfaceLoadAttempt(
                requiredSurfaceTiles
            );
        }

        loadRoutine =
            null;

        if (logCacheUpdates)
        {
            if (request.HeightRequired)
            {
                LogCacheReady(
                    retainedHeightTileCount,
                    loadedHeightTileCount,
                    releasedHeightTileCount
                );
            }

            if (request.SurfaceRequired)
            {
                LogSurfaceCacheReady(
                    retainedSurfaceTileCount,
                    enteringSurfaceTiles.Count,
                    residentSurfaceTiles.Count
                );
            }
        }
    }

    private void FailCompositeCacheTransition(
        List<Vector2Int> newlyLoadedHeightTiles,
        bool surfaceAttemptStarted
    )
    {
        if (newlyLoadedHeightTiles != null)
        {
            foreach (
                Vector2Int coordinate
                in newlyLoadedHeightTiles
            )
            {
                ReleaseResidentTile(
                    coordinate
                );
            }
        }

        if (surfaceAttemptStarted)
        {
            RollbackSurfaceLoadAttempt();

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.Idle;
        }

        loadRoutine =
            null;
    }

    private void LogSurfaceCacheReady(
        int retainedTileCount,
        int loadedTileCount,
        int residentTileCount
    )
    {
        Debug.Log(
            "Terrain surface-mask cache ready.\n\n" +
            $"Cache Origin Tile: ({surfaceCacheOriginTile.x}, {surfaceCacheOriginTile.y})\n" +
            $"Cache Tile Grid: {surfaceCacheWidth} x {surfaceCacheHeight}\n" +
            $"Resident Surface Pages: {residentTileCount}\n\n" +
            "Residency Transition:\n" +
            $"Retained: {retainedTileCount}\n" +
            $"Loaded: {loadedTileCount}\n\n" +
            $"Estimated Double-Buffered GPU Payload: {EstimatedSurfaceGpuCacheBytes} bytes\n" +
            $"Transition State: {surfaceTransitionState}",
            this
        );
    }
}
