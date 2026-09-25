using System.Collections.Generic;
using UnityEngine;

public enum TerrainSurfaceCacheTransitionState
{
    Idle,
    LoadingSources,
    PopulatingStaging,
    CommitPending
}

/*
 * Independent full-resolution Surface cache geometry, readiness, coverage,
 * and transition state. TerrainHeightmapStreamer coordinates Surface
 * publication with the required multiresolution Height cache set.
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
                || surfaceCacheWidth <= 0
                || surfaceCacheHeight <= 0
                ||
                (
                    surfaceMaskCache == null
                    && stagingSurfaceMaskCache == null
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
                + 1
                + Mathf.Max(
                    0,
                    guardTileCount
                ) * 2
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
            || surfaceMaskCache == null
            || surfaceMaskManifest == null
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
            || surfaceMaskManifest == null
            || surfaceCacheWidth <= 0
            || surfaceCacheHeight <= 0
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
            || visibleMinimumZ > visibleMaximumZ
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
                sampleSpacing +
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
            && minimumTile.y >= cacheOrigin.y
            && maximumTile.x <
                cacheOrigin.x + surfaceCacheWidth
            && maximumTile.y <
                cacheOrigin.y + surfaceCacheHeight;
    }

    // =====================================================
    // SURFACE TRANSITION PLAN
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
