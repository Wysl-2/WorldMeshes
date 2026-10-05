using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * Multiresolution runtime Height activation.
 *
 * Production Height residency is owned by one TerrainHeightLodRuntimeState
 * per clipmap LOD. Source Texture2Ds remain transient scheduler resources.
 */
public partial class TerrainHeightmapStreamer
{
    [Header("Multiresolution Height Streaming")]

    [SerializeField]
    [Min(1)]
    private int maxConcurrentHeightPageLoads = 8;

    private sealed class TerrainHeightLodCoveragePlan
    {
        public int Level;
        public int SampleStride;
        public Vector3 Anchor;
        public TerrainHeightPageRect RequiredPages;
        public TerrainHeightPageRect PrefetchPages;
        public Vector2Int RequestedCacheOrigin;
        public bool TransitionRequired;
    }

    private sealed class TerrainHeightLayoutCoveragePlan
    {
        public int Generation;
        public int LevelCount;
        public Vector2 MinimumXZ;
        public Vector2 MaximumXZ;
        public Vector3 CoverageCenter;
        public TerrainHeightLodCoveragePlan[] Levels;
    }

    private TerrainHeightLodRuntimeState[] heightLodStates;

    private TerrainHeightPageLoadScheduler
        heightPageLoadScheduler;

    private TerrainHeightLayoutCoveragePlan
        latestRequestedHeightPlan;

    private int nextHeightLayoutGeneration = 1;

    private int preparedHeightLayoutGeneration;

    private TerrainHeightLayoutCoveragePlan
        preparedHeightPlan;

    private bool multiresolutionBindingPending;

    private TerrainClipmapLayoutApplier
        multiresolutionBindingLayoutApplier;

    private readonly List<TerrainClipmapRendererBinding>
        multiresolutionRendererBindings =
            new List<TerrainClipmapRendererBinding>();

    private readonly TerrainClipmapLayout
        fallbackStreamingLayout =
            new TerrainClipmapLayout();

    public int HeightLodRuntimeStateCount =>
        heightLodStates != null
            ? heightLodStates.Length
            : 0;

    public int ActiveHeightPageLoadCount =>
        heightPageLoadScheduler != null
            ? heightPageLoadScheduler.ActiveLoadCount
            : 0;

    public int QueuedHeightPageLoadCount =>
        heightPageLoadScheduler != null
            ? heightPageLoadScheduler.QueuedLoadCount
            : 0;

    public int MaxConcurrentHeightPageLoads =>
        Mathf.Max(
            1,
            maxConcurrentHeightPageLoads
        );

    public bool HasPreparedCacheActivation =>
        multiresolutionBindingPending
        &&
        preparedHeightPlan != null
        &&
        preparedSurfacePlan != null
        &&
        preparedHeightPlan.Generation ==
            preparedSurfacePlan.Generation
        &&
        preparedHeightPlan.LevelCount ==
            preparedSurfacePlan.LevelCount;

    public bool TryGetPreparedClipmapLayout(
        TerrainClipmapLayout output
    )
    {
        if (
            output == null
            || !HasPreparedCacheActivation
            || heightLodStates == null
            || preparedHeightPlan.LevelCount != heightLodStates.Length
        )
        {
            return false;
        }

        output.EnsureCapacity(
            preparedHeightPlan.LevelCount
        );

        for (
            int level = 0;
            level < preparedHeightPlan.LevelCount;
            level++
        )
        {
            output.SetLOD(
                level,
                preparedHeightPlan.Levels[level].Anchor,
                heightLodStates[level].Descriptor.SampleSpacing
            );
        }

        output.LevelCount =
            preparedHeightPlan.LevelCount;

        output.MinimumXZ =
            preparedHeightPlan.MinimumXZ;

        output.MaximumXZ =
            preparedHeightPlan.MaximumXZ;

        output.CoverageCenter =
            preparedHeightPlan.CoverageCenter;

        output.Diameter =
            TerrainClipmapTopologyUtility
                .CalculateClipmapDiameter(
                    worldSettings
                );

        output.IsValid =
            true;

        return true;
    }

    // =====================================================
    // INITIALIZATION
    // =====================================================

    private bool InitializeMultiresolutionHeightRuntime()
    {
        ShutdownMultiresolutionHeightRuntime();

        if (
            heightmapManifest == null
            || worldSettings == null
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize the multiresolution Height runtime because source configuration is missing.",
                this
            );

            return false;
        }

        if (
            !heightmapManifest.isComplete
            || !heightmapManifest.streamingPyramidIsComplete
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize multiresolution Height streaming.\n\n" +
                "The authoritative Height dataset and complete Height Streaming pyramid are required.",
                this
            );

            return false;
        }

        if (
            !TerrainHeightStreamingPyramidPolicy
                .TryValidateClipmapCompatibility(
                    worldSettings,
                    out string compatibilityError
                )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer cannot initialize multiresolution Height streaming.\n\n" +
                compatibilityError,
                this
            );

            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility.GetLevelCount(
                worldSettings
            );

        heightLodStates =
            new TerrainHeightLodRuntimeState[levelCount];

        try
        {
            for (
                int level = 0;
                level < levelCount;
                level++
            )
            {
                if (
                    !TerrainHeightStreamingPyramidPolicy
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
                    !heightmapManifest
                        .TryGetHeightRepresentationDescriptor(
                            sampleStride,
                            out TerrainHeightStreamingLevelDescriptor descriptor
                        )
                )
                {
                    throw
                        new System.InvalidOperationException(
                            $"Height Streaming representation stride {sampleStride} required by LOD{level} is absent from the runtime manifest."
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
                            $"LOD{level} geometry spacing does not match its Height representation.\n\n" +
                            $"Geometry Spacing: {expectedSpacing}\n" +
                            $"Height Spacing: {descriptor.SampleSpacing}\n" +
                            $"Stride: {sampleStride}"
                        );
                }

                TerrainHeightLodRuntimeState state =
                    new TerrainHeightLodRuntimeState(
                        level,
                        sampleStride,
                        descriptor
                    );

                CalculateLodCacheDimensions(
                    state,
                    levelCount
                );

                int sliceCount =
                    state.CacheWidth *
                    state.CacheHeight;

                state.ActiveCache =
                    CreateHeightCacheTexture(
                        $"Terrain Height LOD{level} Cache A",
                        descriptor.SamplesPerSide,
                        sliceCount
                    );

                state.StagingCache =
                    CreateHeightCacheTexture(
                        $"Terrain Height LOD{level} Cache B",
                        descriptor.SamplesPerSide,
                        sliceCount
                    );

                heightLodStates[level] =
                    state;
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not initialize the per-LOD Height caches.\n\n" +
                exception.Message,
                this
            );

            ShutdownMultiresolutionHeightRuntime();

            return false;
        }

        heightPageLoadScheduler =
            new TerrainHeightPageLoadScheduler
            {
                MaxConcurrentLoads =
                    MaxConcurrentHeightPageLoads
            };

        multiresolutionBindingLayoutApplier =
            new TerrainClipmapLayoutApplier();

        multiresolutionBindingLayoutApplier.Configure(
            transform,
            worldSettings
        );

        return true;
    }

    private void CalculateLodCacheDimensions(
        TerrainHeightLodRuntimeState state,
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

        if (state.Level < levelCount - 1)
        {
            /*
             * Conservative arbitrary-anchor budget:
             *
             *   +1 fine spacing adjacent-anchor displacement
             *   +2 fine spacing stitch coarse-side extension
             *   +2 fine spacing adjacent coarse normal radius
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
            maximumHalfExtent * 2f;

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                state.Descriptor.TileWorldSize
            );

        int requiredPageSpan =
            Mathf.CeilToInt(
                requiredWorldSpan /
                tileWorldSize
            )
            + 1;

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
                state.Descriptor.TileGridWidth
            );

        state.CacheHeight =
            Mathf.Clamp(
                cacheSpan,
                1,
                state.Descriptor.TileGridHeight
            );
    }

    // =====================================================
    // COMPLETE LAYOUT REQUEST
    // =====================================================

    public void RequestCoverageForClipmapLayout(
        TerrainClipmapLayout layout
    )
    {
        if (
            !Application.isPlaying
            || !initialized
            || layout == null
            || !layout.IsValid
        )
        {
            return;
        }

        int candidateGeneration =
            nextHeightLayoutGeneration;

        if (
            !TryBuildHeightLayoutCoveragePlan(
                layout,
                candidateGeneration,
                out TerrainHeightLayoutCoveragePlan heightPlan,
                out string heightError
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not plan multiresolution Height coverage.\n\n" +
                heightError,
                this
            );

            return;
        }

        if (
            !TryBuildSurfaceLayoutCoveragePlan(
                layout,
                candidateGeneration,
                out TerrainSurfaceLayoutCoveragePlan surfacePlan,
                out string surfaceError
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not plan multiresolution Surface coverage.\n\n" +
                surfaceError,
                this
            );

            return;
        }

        bool equivalent =
            latestRequestedHeightPlan != null
            &&
            latestRequestedSurfacePlan != null
            &&
            AreCoveragePlansEquivalent(
                latestRequestedHeightPlan,
                heightPlan
            )
            &&
            AreSurfaceCoveragePlansEquivalent(
                latestRequestedSurfacePlan,
                surfacePlan
            );

        if (equivalent)
        {
            int existingGeneration =
                latestRequestedHeightPlan.Generation;

            heightPlan.Generation =
                existingGeneration;

            surfacePlan.Generation =
                existingGeneration;
        }
        else
        {
            nextHeightLayoutGeneration++;
        }

        latestRequestedHeightPlan =
            heightPlan;

        latestRequestedSurfacePlan =
            surfacePlan;

        NotifyLatestHeightSourcePlan(
            heightPlan
        );

        NotifyLatestSurfaceSourcePlan(
            surfacePlan
        );

        requestedClipmapCenter =
            heightPlan.CoverageCenter;

        hasRequestedClipmapCenter =
            true;

        TryBeginRequestedCacheTransition();
    }

    public bool CanActiveHeightCachesCoverLayout(
        TerrainClipmapLayout layout
    )
    {
        if (
            layout == null
            || !layout.IsValid
            || heightLodStates == null
        )
        {
            return false;
        }

        if (
            !TryBuildHeightLayoutCoveragePlan(
                layout,
                0,
                out TerrainHeightLayoutCoveragePlan plan,
                out _
            )
        )
        {
            return false;
        }

        return
            AreAllRequiredHeightPagesActive(
                plan
            );
    }

    public bool CanActiveCachesCoverLayout(
        TerrainClipmapLayout layout
    )
    {
        return
            layout != null
            &&
            layout.IsValid
            &&
            CanActiveHeightCachesCoverLayout(
                layout
            )
            &&
            CanActiveSurfaceCachesCoverLayout(
                layout
            );
    }

    public bool ActivatePreparedCachesForLayout(
        TerrainClipmapLayout layout
    )
    {
        if (
            layout == null
            || !layout.IsValid
            || !CanActiveCachesCoverLayout(
                layout
            )
        )
        {
            return false;
        }

        if (!multiresolutionBindingPending)
        {
            return shaderCacheBound;
        }

        multiresolutionBindingPending =
            false;

        BindHeightCacheToClipmapRenderers();

        if (!shaderCacheBound)
        {
            multiresolutionBindingPending =
                true;

            return false;
        }

        preparedHeightLayoutGeneration =
            0;

        preparedHeightPlan =
            null;

        preparedSurfacePlan =
            null;

        return true;
    }

    // =====================================================
    // PRODUCTION TRANSITION ENTRY POINT
    // =====================================================

    private void TryBeginRequestedCacheTransition()
    {
        if (
            !Application.isPlaying
            || !initialized
            || cacheInspectionActive
        )
        {
            return;
        }

        if (heightPageLoadScheduler != null)
        {
            ConfigureHeightSourceScheduler();
            heightPageLoadScheduler.Pump();
        }

        if (surfacePageLoadScheduler != null)
        {
            ConfigureSurfaceSourceScheduler();
            surfacePageLoadScheduler.Pump();
        }

        if (multiresolutionBindingPending)
        {
            return;
        }

        EnsureFallbackRequestedLayout();

        TerrainHeightLayoutCoveragePlan heightPlan =
            latestRequestedHeightPlan;

        TerrainSurfaceLayoutCoveragePlan surfacePlan =
            latestRequestedSurfacePlan;

        if (
            heightPlan == null
            ||
            surfacePlan == null
            ||
            heightPlan.Generation !=
                surfacePlan.Generation
        )
        {
            return;
        }

        if (loadRoutine != null)
        {
            return;
        }

        bool anyHeightTransition =
            false;

        for (
            int level = 0;
            level < heightPlan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                heightPlan.Levels[level];

            TerrainHeightLodRuntimeState state =
                heightLodStates[level];

            levelPlan.TransitionRequired =
                !IsRequiredPageRectActive(
                    state,
                    levelPlan.RequiredPages
                );

            anyHeightTransition |=
                levelPlan.TransitionRequired;
        }

        bool anySurfaceTransition =
            false;

        for (
            int level = 0;
            level < surfacePlan.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan levelPlan =
                surfacePlan.Levels[level];

            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            levelPlan.TransitionRequired =
                !IsRequiredSurfacePageRectActive(
                    state,
                    levelPlan.RequiredPages
                );

            anySurfaceTransition |=
                levelPlan.TransitionRequired;
        }

        if (
            !anyHeightTransition
            &&
            !anySurfaceTransition
        )
        {
            return;
        }

        loadRoutine =
            StartCoroutine(
                MultiresolutionTerrainTransitionRoutine(
                    heightPlan,
                    surfacePlan
                )
            );
    }

    private void EnsureFallbackRequestedLayout()
    {
        if (
            latestRequestedHeightPlan != null
            &&
            latestRequestedSurfacePlan != null
        )
        {
            return;
        }

        Vector3 targetPosition;

        if (hasRequestedClipmapCenter)
        {
            targetPosition =
                requestedClipmapCenter;
        }
        else if (streamingTarget != null)
        {
            targetPosition =
                streamingTarget.position;
        }
        else
        {
            targetPosition =
                transform.position;
        }

        if (
            !TerrainClipmapLayoutUtility
                .TryCalculateLayout(
                    worldSettings,
                    targetPosition,
                    transform.position.y,
                    fallbackStreamingLayout,
                    out _
                )
        )
        {
            return;
        }

        int generation =
            nextHeightLayoutGeneration++;

        if (
            !TryBuildHeightLayoutCoveragePlan(
                fallbackStreamingLayout,
                generation,
                out TerrainHeightLayoutCoveragePlan heightPlan,
                out _
            )
            ||
            !TryBuildSurfaceLayoutCoveragePlan(
                fallbackStreamingLayout,
                generation,
                out TerrainSurfaceLayoutCoveragePlan surfacePlan,
                out _
            )
        )
        {
            return;
        }

        latestRequestedHeightPlan =
            heightPlan;

        latestRequestedSurfacePlan =
            surfacePlan;

        NotifyLatestHeightSourcePlan(
            heightPlan
        );

        NotifyLatestSurfaceSourcePlan(
            surfacePlan
        );
    }

    // =====================================================
    // COVERAGE PLAN
    // =====================================================

    private static bool AreCoveragePlansEquivalent(
        TerrainHeightLayoutCoveragePlan left,
        TerrainHeightLayoutCoveragePlan right
    )
    {
        if (
            left == null
            || right == null
            || left.LevelCount != right.LevelCount
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
            TerrainHeightLodCoveragePlan a =
                left.Levels[level];

            TerrainHeightLodCoveragePlan b =
                right.Levels[level];

            if (
                a.SampleStride != b.SampleStride
                || a.Anchor != b.Anchor
                || a.RequiredPages.Minimum != b.RequiredPages.Minimum
                || a.RequiredPages.Maximum != b.RequiredPages.Maximum
                || a.PrefetchPages.Minimum != b.PrefetchPages.Minimum
                || a.PrefetchPages.Maximum != b.PrefetchPages.Maximum
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool TryBuildHeightLayoutCoveragePlan(
        TerrainClipmapLayout layout,
        int generation,
        out TerrainHeightLayoutCoveragePlan plan,
        out string errorMessage
    )
    {
        plan = null;
        errorMessage = "";

        if (
            layout == null
            || !layout.IsValid
            || heightLodStates == null
            || layout.LevelCount != heightLodStates.Length
        )
        {
            errorMessage =
                "The requested clipmap layout does not match the initialized Height LOD state.";

            return false;
        }

        TerrainHeightLayoutCoveragePlan result =
            new TerrainHeightLayoutCoveragePlan
            {
                Generation = generation,
                LevelCount = layout.LevelCount,
                MinimumXZ = layout.MinimumXZ,
                MaximumXZ = layout.MaximumXZ,
                CoverageCenter = layout.CoverageCenter,
                Levels =
                    new TerrainHeightLodCoveragePlan[
                        layout.LevelCount
                    ]
            };

        for (
            int level = 0;
            level < layout.LevelCount;
            level++
        )
        {
            TerrainHeightLodRuntimeState state =
                heightLodStates[level];

            if (state == null)
            {
                errorMessage =
                    $"Height runtime state LOD{level} is unavailable.";

                return false;
            }

            Vector3 anchor =
                layout.GetAnchor(
                    level
                );

            float coarseSampleSpacing =
                state.Descriptor.SampleSpacing;

            if (level < layout.LevelCount - 1)
            {
                TerrainHeightLodRuntimeState coarseState =
                    heightLodStates[
                        level + 1
                    ];

                if (coarseState == null)
                {
                    errorMessage =
                        $"Height runtime state LOD{level + 1} is unavailable.";

                    return false;
                }

                coarseSampleSpacing =
                    coarseState
                        .Descriptor
                        .SampleSpacing;
            }

            if (
                !TerrainHeightClipmapCoverageUtility
                    .TryCalculateRequiredWorldBounds(
                        worldSettings,
                        layout,
                        level,
                        state.Descriptor.SampleSpacing,
                        coarseSampleSpacing,
                        out Vector2 requiredMinimumXZ,
                        out Vector2 requiredMaximumXZ,
                        out string coverageError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate required Height coverage for LOD{level}.\n\n" +
                    coverageError;

                return false;
            }

            if (
                !TryWorldBoundsToHeightPages(
                    requiredMinimumXZ.x,
                    requiredMinimumXZ.y,
                    requiredMaximumXZ.x,
                    requiredMaximumXZ.y,
                    state.Descriptor,
                    out TerrainHeightPageRect requiredPages
                )
            )
            {
                errorMessage =
                    $"Could not calculate required Height pages for LOD{level}.";

                return false;
            }

            TerrainHeightPageRect prefetchPages =
                requiredPages.Expand(
                    Mathf.Max(
                        0,
                        guardTileCount
                    ),
                    state.Descriptor.TileGridWidth,
                    state.Descriptor.TileGridHeight
                );

            if (
                requiredPages.Width > state.CacheWidth
                || requiredPages.Height > state.CacheHeight
            )
            {
                errorMessage =
                    $"LOD{level} required Height coverage exceeds its fixed cache capacity.\n\n" +
                    $"Required: {requiredPages.Width} x {requiredPages.Height}\n" +
                    $"Cache: {state.CacheWidth} x {state.CacheHeight}";

                return false;
            }

            Vector2Int requestedOrigin =
                ChooseCacheOrigin(
                    state,
                    requiredPages,
                    prefetchPages
                );

            result.Levels[level] =
                new TerrainHeightLodCoveragePlan
                {
                    Level = level,
                    SampleStride = state.SampleStride,
                    Anchor = anchor,
                    RequiredPages = requiredPages,
                    PrefetchPages = prefetchPages,
                    RequestedCacheOrigin = requestedOrigin
                };
        }

        plan = result;

        return true;
    }

    private bool TryWorldBoundsToHeightPages(
        float minimumX,
        float minimumZ,
        float maximumX,
        float maximumZ,
        TerrainHeightStreamingLevelDescriptor descriptor,
        out TerrainHeightPageRect pages
    )
    {
        pages = default;

        float worldSizeX =
            heightmapManifest.WorldSizeX;

        float worldSizeZ =
            heightmapManifest.WorldSizeZ;

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
            clampedMinimumX > clampedMaximumX
            || clampedMinimumZ > clampedMaximumZ
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
                descriptor.TileGridWidth - 1
            );

        int maximumTileZ =
            Mathf.Max(
                0,
                descriptor.TileGridHeight - 1
            );

        Vector2Int minimumTile =
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
            );

        Vector2Int maximumTile =
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
            );

        pages =
            new TerrainHeightPageRect(
                minimumTile,
                maximumTile
            );

        return pages.IsValid;
    }

    private Vector2Int ChooseCacheOrigin(
        TerrainHeightLodRuntimeState state,
        TerrainHeightPageRect required,
        TerrainHeightPageRect prefetch
    )
    {
        if (
            state.CacheReady
            && state.ActiveCachePages.Contains(required)
        )
        {
            return state.ActiveCacheOrigin;
        }

        TerrainHeightPageRect preferred =
            prefetch.Width <= state.CacheWidth
            && prefetch.Height <= state.CacheHeight
                ? prefetch
                : required;

        int targetCenterX =
            (preferred.Minimum.x + preferred.Maximum.x) / 2;

        int targetCenterZ =
            (preferred.Minimum.y + preferred.Maximum.y) / 2;

        int originX =
            targetCenterX -
            state.CacheWidth / 2;

        int originZ =
            targetCenterZ -
            state.CacheHeight / 2;

        originX =
            Mathf.Clamp(
                originX,
                required.Maximum.x - state.CacheWidth + 1,
                required.Minimum.x
            );

        originZ =
            Mathf.Clamp(
                originZ,
                required.Maximum.y - state.CacheHeight + 1,
                required.Minimum.y
            );

        originX =
            Mathf.Clamp(
                originX,
                0,
                Mathf.Max(
                    0,
                    state.Descriptor.TileGridWidth - state.CacheWidth
                )
            );

        originZ =
            Mathf.Clamp(
                originZ,
                0,
                Mathf.Max(
                    0,
                    state.Descriptor.TileGridHeight - state.CacheHeight
                )
            );

        return
            new Vector2Int(
                originX,
                originZ
            );
    }

    // =====================================================
    // MULTI-LOD TRANSITION
    // =====================================================

    private IEnumerator MultiresolutionTerrainTransitionRoutine(
        TerrainHeightLayoutCoveragePlan heightPlan,
        TerrainSurfaceLayoutCoveragePlan surfacePlan
    )
    {
        if (
            heightPlan == null
            ||
            surfacePlan == null
            ||
            heightPlan.Generation !=
                surfacePlan.Generation
        )
        {
            loadRoutine =
                null;

            yield break;
        }

        int generation =
            heightPlan.Generation;

        for (
            int level = 0;
            level < heightPlan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                heightPlan.Levels[level];

            TerrainHeightLodRuntimeState state =
                heightLodStates[level];

            levelPlan.TransitionRequired =
                !IsRequiredPageRectActive(
                    state,
                    levelPlan.RequiredPages
                );

            if (levelPlan.TransitionRequired)
            {
                state.TransitionState =
                    TerrainHeightLodTransitionState
                        .WaitingForRequiredPages;
            }
        }

        for (
            int level = 0;
            level < surfacePlan.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan levelPlan =
                surfacePlan.Levels[level];

            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            levelPlan.TransitionRequired =
                !IsRequiredSurfacePageRectActive(
                    state,
                    levelPlan.RequiredPages
                );

            if (levelPlan.TransitionRequired)
            {
                state.TransitionState =
                    TerrainSurfaceCacheTransitionState
                        .LoadingSources;
            }
        }

        if (
            !BeginHeightSourceTransition(
                heightPlan,
                out string heightSourceError
            )
        )
        {
            Debug.LogError(
                heightSourceError,
                this
            );

            FailMultiresolutionTransition(
                heightPlan,
                surfacePlan
            );

            yield break;
        }

        if (
            !BeginSurfaceSourceTransition(
                surfacePlan,
                out string surfaceSourceError
            )
        )
        {
            Debug.LogError(
                surfaceSourceError,
                this
            );

            FailMultiresolutionTransition(
                heightPlan,
                surfacePlan
            );

            yield break;
        }

        while (
            !AreAllRequiredHeightPagesPrepared(
                heightPlan
            )
            ||
            !AreAllRequiredSurfacePagesPrepared(
                surfacePlan
            )
        )
        {
            PumpHeightSources(
                heightPlan
            );

            PumpSurfaceSources(
                surfacePlan
            );

            if (
                heightPageLoadScheduler != null
                &&
                heightPageLoadScheduler
                    .HasRequiredFailure(
                        generation
                    )
            )
            {
                Debug.LogError(
                    "A mandatory multiresolution Height page failed to load. The previously active terrain layout remains in use.",
                    this
                );

                FailMultiresolutionTransition(
                    heightPlan,
                    surfacePlan
                );

                yield break;
            }

            if (
                surfacePageLoadScheduler != null
                &&
                surfacePageLoadScheduler
                    .HasRequiredFailure(
                        generation
                    )
            )
            {
                Debug.LogError(
                    "A mandatory multiresolution Surface page failed to load. The previously active terrain layout remains in use.",
                    this
                );

                FailMultiresolutionTransition(
                    heightPlan,
                    surfacePlan
                );

                yield break;
            }

            yield return
                null;
        }

        for (
            int level = 0;
            level < heightPlan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                heightPlan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            heightLodStates[level]
                .TransitionState =
                    TerrainHeightLodTransitionState
                        .CommitPending;
        }

        for (
            int level = 0;
            level < surfacePlan.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan levelPlan =
                surfacePlan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            surfaceLodStates[level]
                .TransitionState =
                    TerrainSurfaceCacheTransitionState
                        .CommitPending;
        }

        // -------------------------------------------------
        // Atomic publish of every mandatory changed cache.
        // -------------------------------------------------

        for (
            int level = 0;
            level < heightPlan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                heightPlan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            TerrainHeightLodRuntimeState state =
                heightLodStates[level];

            Texture2DArray previousActive =
                state.ActiveCache;

            state.ActiveCache =
                state.StagingCache;

            state.StagingCache =
                previousActive;

            state.ActiveCacheOrigin =
                levelPlan.RequestedCacheOrigin;

            state.ActiveRequiredPages =
                levelPlan.RequiredPages;

            state.ActiveValidPages.Clear();

            foreach (
                Vector2Int coordinate
                in state.StagingValidPages
            )
            {
                state.ActiveValidPages.Add(
                    coordinate
                );
            }

            state.StagingValidPages.Clear();

            state.CacheReady =
                true;

            state.TransitionState =
                TerrainHeightLodTransitionState
                    .Idle;
        }

        for (
            int level = 0;
            level < surfacePlan.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan levelPlan =
                surfacePlan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            Texture2DArray previousActive =
                state.ActiveCache;

            state.ActiveCache =
                state.StagingCache;

            state.StagingCache =
                previousActive;

            state.ActiveCacheOrigin =
                levelPlan.RequestedCacheOrigin;

            state.ActiveRequiredPages =
                levelPlan.RequiredPages;

            state.ActiveValidPages.Clear();

            foreach (
                Vector2Int coordinate
                in state.StagingValidPages
            )
            {
                state.ActiveValidPages.Add(
                    coordinate
                );
            }

            state.StagingValidPages.Clear();

            state.CacheReady =
                true;

            state.TransitionState =
                TerrainSurfaceCacheTransitionState
                    .Idle;
        }

        preparedHeightLayoutGeneration =
            generation;

        preparedHeightPlan =
            heightPlan;

        preparedSurfacePlan =
            surfacePlan;

        /*
         * Do not rebind renderers here. The controller may still have the
         * previously applied clipmap geometry for the remainder of this
         * Update pass. It will activate these prepared bindings immediately
         * after successfully applying the matching layout.
         */
        multiresolutionBindingPending =
            true;

        NotifyActiveCacheCoverageIfChanged();
        NotifyActiveSurfaceCacheCoverageIfChanged();

        CompleteHeightSourceTransition(
            heightPlan
        );

        CompleteSurfaceSourceTransition(
            surfacePlan
        );

        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler
                .ClearRequiredFailure(
                    generation
                );
        }

        if (surfacePageLoadScheduler != null)
        {
            surfacePageLoadScheduler
                .ClearRequiredFailure(
                    generation
                );
        }

        if (
            latestRequestedHeightPlan != null
            &&
            latestRequestedHeightPlan.Generation >
                generation
            &&
            heightPageLoadScheduler != null
        )
        {
            heightPageLoadScheduler
                .DiscardQueuedOptionalOlderThan(
                    latestRequestedHeightPlan
                        .Generation
                );
        }

        if (
            latestRequestedSurfacePlan != null
            &&
            latestRequestedSurfacePlan.Generation >
                generation
            &&
            surfacePageLoadScheduler != null
        )
        {
            surfacePageLoadScheduler
                .DiscardQueuedOptionalOlderThan(
                    latestRequestedSurfacePlan
                        .Generation
                );
        }

        loadRoutine =
            null;

        if (logCacheUpdates)
        {
            Debug.Log(
                "Multiresolution terrain Height + Surface caches committed.\n\n" +
                $"Height LOD States: {heightLodStates.Length}\n" +
                $"Surface LOD States: {surfaceLodStates.Length}\n" +
                $"Active Height Loads: {ActiveHeightPageLoadCount}\n" +
                $"Queued Height Loads: {QueuedHeightPageLoadCount}\n" +
                $"Active Surface Loads: {ActiveSurfacePageLoadCount}\n" +
                $"Queued Surface Loads: {QueuedSurfacePageLoadCount}",
                this
            );
        }
    }

    private void FailMultiresolutionTransition(
        TerrainHeightLayoutCoveragePlan heightPlan,
        TerrainSurfaceLayoutCoveragePlan surfacePlan
    )
    {
        int generation =
            heightPlan != null
                ? heightPlan.Generation
                : surfacePlan != null
                    ? surfacePlan.Generation
                    : -1;

        if (heightLodStates != null)
        {
            for (
                int level = 0;
                level < heightLodStates.Length;
                level++
            )
            {
                TerrainHeightLodRuntimeState state =
                    heightLodStates[level];

                if (state == null)
                {
                    continue;
                }

                state.TransitionState =
                    TerrainHeightLodTransitionState
                        .Idle;

                state.StagingValidPages.Clear();
            }
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

                state.TransitionState =
                    TerrainSurfaceCacheTransitionState
                        .Idle;

                state.StagingValidPages.Clear();
            }
        }

        RollbackHeightSourceTransition(
            heightPlan
        );

        RollbackSurfaceSourceTransition(
            surfacePlan
        );

        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler
                .ClearRequiredFailure(
                    generation
                );
        }

        if (surfacePageLoadScheduler != null)
        {
            surfacePageLoadScheduler
                .ClearRequiredFailure(
                    generation
                );
        }

        loadRoutine =
            null;
    }

    // =====================================================
    // COVERAGE / RESIDENCY TESTS
    // =====================================================

    private bool AreAllRequiredHeightPagesActive(
        TerrainHeightLayoutCoveragePlan plan
    )
    {
        if (plan == null)
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
                !IsRequiredPageRectActive(
                    heightLodStates[level],
                    plan.Levels[level].RequiredPages
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool IsRequiredPageRectActive(
        TerrainHeightLodRuntimeState state,
        TerrainHeightPageRect required
    )
    {
        if (
            state == null
            || !state.CacheReady
            || state.ActiveCache == null
            || !state.ActiveCachePages.Contains(required)
        )
        {
            return false;
        }

        foreach (
            Vector2Int coordinate
            in EnumeratePageRect(required)
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

    private static IEnumerable<Vector2Int> EnumeratePageRect(
        TerrainHeightPageRect pages
    )
    {
        if (!pages.IsValid)
        {
            yield break;
        }

        for (
            int tileZ = pages.Minimum.y;
            tileZ <= pages.Maximum.y;
            tileZ++
        )
        {
            for (
                int tileX = pages.Minimum.x;
                tileX <= pages.Maximum.x;
                tileX++
            )
            {
                yield return
                    new Vector2Int(
                        tileX,
                        tileZ
                    );
            }
        }
    }

    // =====================================================
    // SHADER BINDING
    // =====================================================

    private bool AreAllActiveHeightLodCachesReady()
    {
        if (
            heightLodStates == null
            || heightLodStates.Length == 0
        )
        {
            return false;
        }

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
            )
            {
                return false;
            }
        }

        return true;
    }

    private void BindMultiresolutionHeightCachesToClipmapRenderers()
    {
        shaderCacheBound =
            false;

        if (
            !AreAllActiveHeightLodCachesReady()
            ||
            !AreAllActiveSurfaceLodCachesReady()
            ||
            heightmapManifest == null
            ||
            surfaceMaskManifest == null
        )
        {
            return;
        }

        if (multiresolutionBindingLayoutApplier == null)
        {
            multiresolutionBindingLayoutApplier =
                new TerrainClipmapLayoutApplier();
        }

        multiresolutionBindingLayoutApplier.Configure(
            transform,
            worldSettings
        );

        if (
            !multiresolutionBindingLayoutApplier
                .TryGetRendererBindings(
                    multiresolutionRendererBindings,
                    out string rendererError
                )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not resolve semantic clipmap renderer roles.\n\n" +
                rendererError,
                this
            );

            return;
        }

        bool heightSuccess =
            TerrainHeightMultiresolutionBindingUtility
                .TryBind(
                    transform,
                    multiresolutionRendererBindings,
                    heightLodStates,
                    heightmapManifest.WorldSizeXZ,
                    out int boundRendererCount,
                    out string heightError
                );

        if (!heightSuccess)
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not bind multiresolution Height caches.\n\n" +
                heightError,
                this
            );

            return;
        }

        if (
            !BindSurfaceMaskCacheToClipmapRenderers(
                out string surfaceError
            )
        )
        {
            TerrainHeightCacheBindingUtility.Disable(
                transform
            );

            TerrainSurfaceMaskBindingUtility.Disable(
                transform
            );

            Debug.LogError(
                "TerrainHeightmapStreamer could not bind multiresolution Surface caches after Height binding.\n\n" +
                surfaceError,
                this
            );

            return;
        }

        shaderCacheBound =
            true;

        if (logCacheUpdates)
        {
            Debug.Log(
                "Multiresolution Height + Surface caches bound to clipmap shader.\n\n" +
                $"Renderers: {boundRendererCount}\n" +
                $"Height LOD States: {heightLodStates.Length}\n" +
                $"Surface LOD States: {surfaceLodStates.Length}",
                this
            );
        }
    }

    // =====================================================
    // SHUTDOWN
    // =====================================================

    private void ShutdownMultiresolutionHeightRuntime()
    {
        if (shaderCacheBound)
        {
            DisableHeightCacheOnClipmapRenderers();
        }

        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler.Shutdown();
            heightPageLoadScheduler = null;
        }

        if (heightLodStates != null)
        {
            for (
                int level = 0;
                level < heightLodStates.Length;
                level++
            )
            {
                TerrainHeightLodRuntimeState state =
                    heightLodStates[level];

                if (state == null)
                {
                    continue;
                }

                if (state.ActiveCache != null)
                {
                    Destroy(
                        state.ActiveCache
                    );

                    state.ActiveCache = null;
                }

                if (state.StagingCache != null)
                {
                    Destroy(
                        state.StagingCache
                    );

                    state.StagingCache = null;
                }

                state.ActiveValidPages.Clear();
                state.StagingValidPages.Clear();
                state.CacheReady = false;
                state.TransitionState = TerrainHeightLodTransitionState.Idle;
                state.StagingGeneration = 0;
                state.StagingRequiredPages = default;
            }
        }

        heightLodStates = null;
        latestRequestedHeightPlan = null;
        preparedHeightLayoutGeneration = 0;
        preparedHeightPlan = null;
        multiresolutionBindingPending = false;
        multiresolutionBindingLayoutApplier = null;
        multiresolutionRendererBindings.Clear();
    }
}
