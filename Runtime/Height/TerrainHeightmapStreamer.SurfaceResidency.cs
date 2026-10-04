using UnityEngine;

public enum TerrainSurfaceCacheTransitionState
{
    Idle,
    LoadingSources,
    PopulatingStaging,
    CommitPending
}

/*
 * Independent multiresolution Surface cache geometry, readiness, coverage,
 * and transition state. TerrainHeightmapStreamer coordinates Surface
 * publication with the required multiresolution Height cache set.
 */
public partial class TerrainHeightmapStreamer
{
    private sealed class TerrainSurfaceLodCoveragePlan
    {
        public int Level;
        public int SampleStride;
        public Vector3 Anchor;
        public TerrainHeightPageRect RequiredPages;
        public TerrainHeightPageRect PrefetchPages;
        public Vector2Int RequestedCacheOrigin;
        public bool TransitionRequired;
    }

    private sealed class TerrainSurfaceLayoutCoveragePlan
    {
        public int Generation;
        public int LevelCount;
        public Vector2 MinimumXZ;
        public Vector2 MaximumXZ;
        public Vector3 CoverageCenter;
        public TerrainSurfaceLodCoveragePlan[] Levels;
    }

    private TerrainSurfaceLodRuntimeState[]
        surfaceLodStates;

    private TerrainSurfacePageLoadScheduler
        surfacePageLoadScheduler;

    private TerrainSurfaceLayoutCoveragePlan
        latestRequestedSurfacePlan;

    private TerrainSurfaceLayoutCoveragePlan
        preparedSurfacePlan;

    private bool
        hasPublishedActiveSurfaceCacheCoverage;

    private Vector2
        publishedActiveSurfaceCacheMinimumXZ =
            Vector2.zero;

    private Vector2
        publishedActiveSurfaceCacheMaximumXZ =
            Vector2.zero;

    public event System.Action
        ActiveSurfaceCacheCoverageChanged;

    public int SurfaceLodRuntimeStateCount =>
        surfaceLodStates != null
            ? surfaceLodStates.Length
            : 0;

    public int ActiveSurfacePageLoadCount =>
        surfacePageLoadScheduler != null
            ? surfacePageLoadScheduler
                .ActiveLoadCount
            : 0;

    public int QueuedSurfacePageLoadCount =>
        surfacePageLoadScheduler != null
            ? surfacePageLoadScheduler
                .QueuedLoadCount
            : 0;

    public bool SurfaceCacheReady =>
        AreAllActiveSurfaceLodCachesReady();

    public int ResidentSurfaceTileCount
    {
        get
        {
            if (surfaceLodStates == null)
            {
                return 0;
            }

            int count =
                0;

            for (
                int level = 0;
                level < surfaceLodStates.Length;
                level++
            )
            {
                TerrainSurfaceLodRuntimeState state =
                    surfaceLodStates[level];

                if (state != null)
                {
                    count +=
                        state.ActiveValidPages.Count;
                }
            }

            return count;
        }
    }

    public TerrainSurfaceCacheTransitionState
        SurfaceTransitionState
    {
        get
        {
            if (surfaceLodStates == null)
            {
                return
                    TerrainSurfaceCacheTransitionState
                        .Idle;
            }

            for (
                int level = 0;
                level < surfaceLodStates.Length;
                level++
            )
            {
                TerrainSurfaceLodRuntimeState state =
                    surfaceLodStates[level];

                if (
                    state != null
                    &&
                    state.TransitionState !=
                        TerrainSurfaceCacheTransitionState
                            .Idle
                )
                {
                    return
                        state.TransitionState;
                }
            }

            return
                TerrainSurfaceCacheTransitionState
                    .Idle;
        }
    }

    public long EstimatedSurfaceGpuCacheBytes =>
        EstimateSurfaceActiveGpuCacheBytes()
        +
        EstimateSurfaceStagingGpuCacheBytes();

    // =====================================================
    // INITIALIZATION
    // =====================================================

    private bool InitializeMultiresolutionSurfaceRuntime()
    {
        ShutdownMultiresolutionSurfaceRuntime();

        if (
            surfaceMaskManifest == null
            ||
            worldSettings == null
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize the multiresolution Surface runtime because source configuration is missing.",
                this
            );

            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        surfaceLodStates =
            new TerrainSurfaceLodRuntimeState[
                levelCount
            ];

        try
        {
            for (
                int level = 0;
                level < levelCount;
                level++
            )
            {
                if (
                    !TerrainSurfaceStreamingPyramidPolicy
                        .TryGetRequiredStrideForClipmapLevel(
                            worldSettings,
                            level,
                            out int sampleStride,
                            out string strideError
                        )
                )
                {
                    throw
                        new System.InvalidOperationException(
                            strideError
                        );
                }

                if (
                    !surfaceMaskManifest
                        .TryGetSurfaceRepresentationDescriptor(
                            sampleStride,
                            out TerrainSurfaceStreamingLevelDescriptor
                                descriptor
                        )
                )
                {
                    throw
                        new System.InvalidOperationException(
                            $"Surface Streaming representation stride {sampleStride} required by LOD{level} is absent from the runtime manifest."
                        );
                }

                float expectedSpacing =
                    TerrainClipmapLayoutUtility
                        .GetLODSpacing(
                            worldSettings,
                            level
                        );

                if (
                    !Mathf.Approximately(
                        descriptor.SampleSpacing,
                        expectedSpacing
                    )
                )
                {
                    throw
                        new System.InvalidOperationException(
                            $"LOD{level} geometry spacing does not match its Surface representation.\n\n" +
                            $"Geometry Spacing: {expectedSpacing}\n" +
                            $"Surface Spacing: {descriptor.SampleSpacing}\n" +
                            $"Stride: {sampleStride}"
                        );
                }

                TerrainSurfaceLodRuntimeState state =
                    new TerrainSurfaceLodRuntimeState(
                        level,
                        sampleStride,
                        descriptor
                    );

                CalculateSurfaceLodCacheDimensions(
                    state,
                    levelCount
                );

                int sliceCount =
                    state.CacheWidth *
                    state.CacheHeight;

                if (sliceCount <= 0)
                {
                    throw
                        new System.InvalidOperationException(
                            $"LOD{level} Surface cache dimensions are invalid."
                        );
                }

                state.ActiveCache =
                    CreateSurfaceMaskCacheTexture(
                        $"Terrain Surface LOD{level} Cache A",
                        descriptor.SamplesPerSide,
                        sliceCount
                    );

                state.StagingCache =
                    CreateSurfaceMaskCacheTexture(
                        $"Terrain Surface LOD{level} Cache B",
                        descriptor.SamplesPerSide,
                        sliceCount
                    );

                surfaceLodStates[level] =
                    state;
            }
        }
        catch (
            System.Exception exception
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not initialize the per-LOD Surface caches.\n\n" +
                exception.Message,
                this
            );

            ShutdownMultiresolutionSurfaceRuntime();

            return false;
        }

        surfacePageLoadScheduler =
            new TerrainSurfacePageLoadScheduler();

        ConfigureSurfaceSourceScheduler();

        return true;
    }

    private void CalculateSurfaceLodCacheDimensions(
        TerrainSurfaceLodRuntimeState state,
        int levelCount
    )
    {
        float spacing =
            state.Descriptor.SampleSpacing;

        float halfExtent =
            TerrainClipmapTopologyUtility
                .GetLODHalfExtent(
                    worldSettings,
                    state.Level
                );

        float maximumHalfExtent;

        if (
            state.Level <
            levelCount - 1
        )
        {
            /*
             * Conservative arbitrary-anchor budget matching the terrain
             * transition geometry: adjacent-anchor displacement, stitch
             * coarse-side extension, and sampling margin.
             */
            maximumHalfExtent =
                halfExtent +
                spacing * 5f;
        }
        else
        {
            maximumHalfExtent =
                halfExtent +
                spacing;
        }

        float requiredWorldSpan =
            maximumHalfExtent *
            2f;

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                state.Descriptor
                    .TileWorldSize
            );

        int requiredPageSpan =
            Mathf.CeilToInt(
                requiredWorldSpan /
                tileWorldSize
            )
            +
            1;

        int configuredGuard =
            Mathf.Max(
                0,
                guardTileCount
            );

        int cacheSpan =
            requiredPageSpan +
            configuredGuard * 2;

        state.CacheWidth =
            Mathf.Clamp(
                cacheSpan,
                1,
                state.Descriptor
                    .TileGridWidth
            );

        state.CacheHeight =
            Mathf.Clamp(
                cacheSpan,
                1,
                state.Descriptor
                    .TileGridHeight
            );
    }

    // =====================================================
    // COVERAGE PLAN
    // =====================================================

    private bool TryBuildSurfaceLayoutCoveragePlan(
        TerrainClipmapLayout layout,
        int generation,
        out TerrainSurfaceLayoutCoveragePlan plan,
        out string errorMessage
    )
    {
        plan =
            null;

        errorMessage =
            "";

        if (
            layout == null
            ||
            !layout.IsValid
            ||
            surfaceLodStates == null
            ||
            layout.LevelCount !=
                surfaceLodStates.Length
        )
        {
            errorMessage =
                "The requested clipmap layout does not match the initialized Surface LOD state.";

            return false;
        }

        TerrainSurfaceLayoutCoveragePlan result =
            new TerrainSurfaceLayoutCoveragePlan
            {
                Generation = generation,
                LevelCount = layout.LevelCount,
                MinimumXZ = layout.MinimumXZ,
                MaximumXZ = layout.MaximumXZ,
                CoverageCenter = layout.CoverageCenter,
                Levels =
                    new TerrainSurfaceLodCoveragePlan[
                        layout.LevelCount
                    ]
            };

        for (
            int level = 0;
            level < layout.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            if (state == null)
            {
                errorMessage =
                    $"Surface runtime state LOD{level} is unavailable.";

                return false;
            }

            Vector3 anchor =
                layout.GetAnchor(
                    level
                );

            float halfExtent =
                TerrainClipmapTopologyUtility
                    .GetLODHalfExtent(
                        worldSettings,
                        level
                    );

            float minimumX =
                anchor.x -
                halfExtent;

            float maximumX =
                anchor.x +
                halfExtent;

            float minimumZ =
                anchor.z -
                halfExtent;

            float maximumZ =
                anchor.z +
                halfExtent;

            float sampleMargin =
                state.Descriptor
                    .SampleSpacing;

            if (
                level <
                layout.LevelCount - 1
            )
            {
                Vector3 coarseAnchor =
                    layout.GetAnchor(
                        level + 1
                    );

                float stitchHalfExtent =
                    halfExtent +
                    state.Descriptor
                        .SampleSpacing *
                    2f;

                minimumX =
                    Mathf.Min(
                        minimumX,
                        coarseAnchor.x -
                            stitchHalfExtent
                    );

                maximumX =
                    Mathf.Max(
                        maximumX,
                        coarseAnchor.x +
                            stitchHalfExtent
                    );

                minimumZ =
                    Mathf.Min(
                        minimumZ,
                        coarseAnchor.z -
                            stitchHalfExtent
                    );

                maximumZ =
                    Mathf.Max(
                        maximumZ,
                        coarseAnchor.z +
                            stitchHalfExtent
                    );

                sampleMargin =
                    Mathf.Max(
                        sampleMargin,
                        surfaceLodStates[
                            level + 1
                        ]
                            .Descriptor
                            .SampleSpacing
                    );
            }

            minimumX -=
                sampleMargin;

            maximumX +=
                sampleMargin;

            minimumZ -=
                sampleMargin;

            maximumZ +=
                sampleMargin;

            if (
                !TryWorldBoundsToSurfacePages(
                    minimumX,
                    minimumZ,
                    maximumX,
                    maximumZ,
                    state.Descriptor,
                    out TerrainHeightPageRect
                        requiredPages
                )
            )
            {
                errorMessage =
                    $"Could not calculate required Surface pages for LOD{level}.";

                return false;
            }

            TerrainHeightPageRect prefetchPages =
                requiredPages.Expand(
                    Mathf.Max(
                        0,
                        guardTileCount
                    ),
                    state.Descriptor
                        .TileGridWidth,
                    state.Descriptor
                        .TileGridHeight
                );

            if (
                requiredPages.Width >
                    state.CacheWidth
                ||
                requiredPages.Height >
                    state.CacheHeight
            )
            {
                errorMessage =
                    $"LOD{level} required Surface coverage exceeds its fixed cache capacity.\n\n" +
                    $"Required: {requiredPages.Width} x {requiredPages.Height}\n" +
                    $"Cache: {state.CacheWidth} x {state.CacheHeight}";

                return false;
            }

            Vector2Int requestedOrigin =
                ChooseSurfaceCacheOrigin(
                    state,
                    requiredPages,
                    prefetchPages
                );

            result.Levels[level] =
                new TerrainSurfaceLodCoveragePlan
                {
                    Level = level,
                    SampleStride =
                        state.SampleStride,
                    Anchor = anchor,
                    RequiredPages =
                        requiredPages,
                    PrefetchPages =
                        prefetchPages,
                    RequestedCacheOrigin =
                        requestedOrigin
                };
        }

        plan =
            result;

        return true;
    }

    private bool TryWorldBoundsToSurfacePages(
        float minimumX,
        float minimumZ,
        float maximumX,
        float maximumZ,
        TerrainSurfaceStreamingLevelDescriptor descriptor,
        out TerrainHeightPageRect pages
    )
    {
        pages =
            default;

        float worldSizeX =
            surfaceMaskManifest
                .worldSizeXZ.x;

        float worldSizeZ =
            surfaceMaskManifest
                .worldSizeXZ.y;

        float clampedMinimumX =
            Mathf.Clamp(
                minimumX,
                0f,
                worldSizeX
            );

        float clampedMaximumX =
            Mathf.Clamp(
                maximumX,
                0f,
                worldSizeX
            );

        float clampedMinimumZ =
            Mathf.Clamp(
                minimumZ,
                0f,
                worldSizeZ
            );

        float clampedMaximumZ =
            Mathf.Clamp(
                maximumZ,
                0f,
                worldSizeZ
            );

        if (
            clampedMinimumX >
                clampedMaximumX
            ||
            clampedMinimumZ >
                clampedMaximumZ
        )
        {
            return false;
        }

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                descriptor.TileWorldSize
            );

        int maximumTileX =
            Mathf.Max(
                0,
                descriptor.TileGridWidth -
                    1
            );

        int maximumTileZ =
            Mathf.Max(
                0,
                descriptor.TileGridHeight -
                    1
            );

        pages =
            new TerrainHeightPageRect(
                new Vector2Int(
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            clampedMinimumX /
                                tileWorldSize
                        ),
                        0,
                        maximumTileX
                    ),
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            clampedMinimumZ /
                                tileWorldSize
                        ),
                        0,
                        maximumTileZ
                    )
                ),
                new Vector2Int(
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            clampedMaximumX /
                                tileWorldSize
                        ),
                        0,
                        maximumTileX
                    ),
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            clampedMaximumZ /
                                tileWorldSize
                        ),
                        0,
                        maximumTileZ
                    )
                )
            );

        return
            pages.IsValid;
    }

    private Vector2Int ChooseSurfaceCacheOrigin(
        TerrainSurfaceLodRuntimeState state,
        TerrainHeightPageRect required,
        TerrainHeightPageRect prefetch
    )
    {
        if (
            state.CacheReady
            &&
            state.ActiveCachePages.Contains(
                required
            )
        )
        {
            return
                state.ActiveCacheOrigin;
        }

        TerrainHeightPageRect preferred =
            prefetch.Width <=
                state.CacheWidth
            &&
            prefetch.Height <=
                state.CacheHeight
                ? prefetch
                : required;

        int targetCenterX =
            (
                preferred.Minimum.x
                +
                preferred.Maximum.x
            )
            /
            2;

        int targetCenterZ =
            (
                preferred.Minimum.y
                +
                preferred.Maximum.y
            )
            /
            2;

        int originX =
            targetCenterX -
            state.CacheWidth /
                2;

        int originZ =
            targetCenterZ -
            state.CacheHeight /
                2;

        originX =
            Mathf.Clamp(
                originX,
                required.Maximum.x -
                    state.CacheWidth +
                    1,
                required.Minimum.x
            );

        originZ =
            Mathf.Clamp(
                originZ,
                required.Maximum.y -
                    state.CacheHeight +
                    1,
                required.Minimum.y
            );

        originX =
            Mathf.Clamp(
                originX,
                0,
                Mathf.Max(
                    0,
                    state.Descriptor
                        .TileGridWidth -
                        state.CacheWidth
                )
            );

        originZ =
            Mathf.Clamp(
                originZ,
                0,
                Mathf.Max(
                    0,
                    state.Descriptor
                        .TileGridHeight -
                        state.CacheHeight
                )
            );

        return
            new Vector2Int(
                originX,
                originZ
            );
    }

    private static bool AreSurfaceCoveragePlansEquivalent(
        TerrainSurfaceLayoutCoveragePlan left,
        TerrainSurfaceLayoutCoveragePlan right
    )
    {
        if (
            left == null
            ||
            right == null
            ||
            left.LevelCount !=
                right.LevelCount
        )
        {
            return false;
        }

        for (
            int level = 0;
            level < left.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan a =
                left.Levels[level];

            TerrainSurfaceLodCoveragePlan b =
                right.Levels[level];

            if (
                a.SampleStride !=
                    b.SampleStride
                ||
                a.Anchor !=
                    b.Anchor
                ||
                a.RequiredPages.Minimum !=
                    b.RequiredPages.Minimum
                ||
                a.RequiredPages.Maximum !=
                    b.RequiredPages.Maximum
                ||
                a.PrefetchPages.Minimum !=
                    b.PrefetchPages.Minimum
                ||
                a.PrefetchPages.Maximum !=
                    b.PrefetchPages.Maximum
            )
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================
    // COVERAGE / READINESS
    // =====================================================

    public bool CanActiveSurfaceCacheCoverClipmapAt(
        Vector3 clipmapCenter
    )
    {
        if (
            worldSettings == null
            ||
            surfaceLodStates == null
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
            CanActiveSurfaceCachesCoverLayout(
                layout
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

    private bool CanActiveSurfaceCachesCoverLayout(
        TerrainClipmapLayout layout
    )
    {
        if (
            layout == null
            ||
            !layout.IsValid
            ||
            surfaceLodStates == null
        )
        {
            return false;
        }

        if (
            !TryBuildSurfaceLayoutCoveragePlan(
                layout,
                0,
                out TerrainSurfaceLayoutCoveragePlan
                    plan,
                out _
            )
        )
        {
            return false;
        }

        return
            AreAllRequiredSurfacePagesActive(
                plan
            );
    }

    private bool AreAllRequiredSurfacePagesActive(
        TerrainSurfaceLayoutCoveragePlan plan
    )
    {
        if (
            plan == null
            ||
            surfaceLodStates == null
        )
        {
            return false;
        }

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            if (
                !IsRequiredSurfacePageRectActive(
                    surfaceLodStates[level],
                    plan.Levels[level]
                        .RequiredPages
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool IsRequiredSurfacePageRectActive(
        TerrainSurfaceLodRuntimeState state,
        TerrainHeightPageRect required
    )
    {
        if (
            state == null
            ||
            !state.CacheReady
            ||
            state.ActiveCache == null
            ||
            !state.ActiveCachePages.Contains(
                required
            )
        )
        {
            return false;
        }

        foreach (
            Vector2Int coordinate
            in EnumeratePageRect(
                required
            )
        )
        {
            if (
                !state.ActiveValidPages.Contains(
                    coordinate
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool AreAllActiveSurfaceLodCachesReady()
    {
        if (
            surfaceLodStates == null
            ||
            surfaceLodStates.Length == 0
        )
        {
            return false;
        }

        for (
            int level = 0;
            level < surfaceLodStates.Length;
            level++
        )
        {
            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            if (
                state == null
                ||
                !state.CacheReady
                ||
                state.ActiveCache == null
            )
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================
    // AGGREGATE COMPATIBILITY / DIAGNOSTIC STATE
    // =====================================================

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
            surfaceLodStates == null
            ||
            surfaceMaskManifest == null
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
            level < surfaceLodStates.Length;
            level++
        )
        {
            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            if (
                state == null
                ||
                !state.CacheReady
                ||
                state.ActiveCache == null
            )
            {
                continue;
            }

            TerrainHeightPageRect pages =
                state.ActiveRequiredPages.IsValid
                    ? state.ActiveRequiredPages
                    : state.ActiveCachePages;

            if (!pages.IsValid)
            {
                continue;
            }

            float tileWorldSize =
                Mathf.Max(
                    0.0001f,
                    state.Descriptor
                        .TileWorldSize
                );

            Vector2 stateMinimum =
                new Vector2(
                    pages.Minimum.x *
                        tileWorldSize,
                    pages.Minimum.y *
                        tileWorldSize
                );

            Vector2 stateMaximum =
                new Vector2(
                    Mathf.Min(
                        surfaceMaskManifest
                            .worldSizeXZ.x,
                        (
                            pages.Maximum.x +
                            1
                        )
                        *
                        tileWorldSize
                    ),
                    Mathf.Min(
                        surfaceMaskManifest
                            .worldSizeXZ.y,
                        (
                            pages.Maximum.y +
                            1
                        )
                        *
                        tileWorldSize
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

    private long EstimateSurfaceActiveGpuCacheBytes()
    {
        return
            EstimateSurfaceGpuCacheBytes(
                true
            );
    }

    private long EstimateSurfaceStagingGpuCacheBytes()
    {
        return
            EstimateSurfaceGpuCacheBytes(
                false
            );
    }

    private long EstimateSurfaceGpuCacheBytes(
        bool active
    )
    {
        if (surfaceLodStates == null)
        {
            return 0L;
        }

        long total =
            0L;

        for (
            int level = 0;
            level < surfaceLodStates.Length;
            level++
        )
        {
            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            if (state == null)
            {
                continue;
            }

            Texture2DArray cache =
                active
                    ? state.ActiveCache
                    : state.StagingCache;

            if (cache == null)
            {
                continue;
            }

            long samples =
                Mathf.Max(
                    0,
                    state.Descriptor
                        .SamplesPerSide
                );

            long slices =
                (long)Mathf.Max(
                    0,
                    state.CacheWidth
                )
                *
                Mathf.Max(
                    0,
                    state.CacheHeight
                );

            total +=
                samples *
                samples *
                slices;
        }

        return total;
    }

    // =====================================================
    // SHUTDOWN
    // =====================================================

    private void ShutdownMultiresolutionSurfaceRuntime()
    {
        if (surfacePageLoadScheduler != null)
        {
            surfacePageLoadScheduler.Shutdown();

            surfacePageLoadScheduler =
                null;
        }

        if (surfaceLodStates != null)
        {
            for (
                int level = 0;
                level < surfaceLodStates.Length;
                level++
            )
            {
                TerrainSurfaceLodRuntimeState state =
                    surfaceLodStates[level];

                if (state == null)
                {
                    continue;
                }

                if (state.ActiveCache != null)
                {
                    Destroy(
                        state.ActiveCache
                    );

                    state.ActiveCache =
                        null;
                }

                if (state.StagingCache != null)
                {
                    Destroy(
                        state.StagingCache
                    );

                    state.StagingCache =
                        null;
                }

                state.ActiveValidPages.Clear();
                state.StagingValidPages.Clear();

                state.CacheReady =
                    false;

                state.TransitionState =
                    TerrainSurfaceCacheTransitionState
                        .Idle;

                state.StagingGeneration =
                    0;

                state.StagingRequiredPages =
                    default;
            }
        }

        surfaceLodStates =
            null;

        latestRequestedSurfacePlan =
            null;

        preparedSurfacePlan =
            null;

        ResetSurfaceSourceResidencyTracking();
    }
}
