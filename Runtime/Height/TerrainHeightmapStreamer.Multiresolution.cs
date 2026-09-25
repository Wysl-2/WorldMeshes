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
        public TerrainHeightPageRect SurfaceRequiredPages;
        public Vector2Int SurfaceRequestedOrigin;
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
        && preparedHeightPlan != null;

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
                out TerrainHeightLayoutCoveragePlan plan,
                out string errorMessage
            )
        )
        {
            Debug.LogError(
                "TerrainHeightmapStreamer could not plan multiresolution Height coverage.\n\n" +
                errorMessage,
                this
            );

            return;
        }

        if (
            latestRequestedHeightPlan != null
            && AreCoveragePlansEquivalent(
                latestRequestedHeightPlan,
                plan
            )
        )
        {
            plan.Generation =
                latestRequestedHeightPlan.Generation;
        }
        else
        {
            nextHeightLayoutGeneration++;
        }

        latestRequestedHeightPlan =
            plan;

        NotifyLatestHeightSourcePlan(
            plan
        );

        requestedClipmapCenter =
            plan.CoverageCenter;

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
        if (
            layout == null
            || !layout.IsValid
        )
        {
            return false;
        }

        if (
            !CanActiveHeightCachesCoverLayout(
                layout
            )
        )
        {
            return false;
        }

        if (
            !TryCalculateSurfaceRequiredPages(
                layout.MinimumXZ,
                layout.MaximumXZ,
                out TerrainHeightPageRect surfaceRequired
            )
        )
        {
            return false;
        }

        return
            IsSurfacePageRectActive(
                surfaceRequired
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

        preparedHeightPlan =
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

        if (multiresolutionBindingPending)
        {
            return;
        }

        EnsureFallbackRequestedLayout();

        TerrainHeightLayoutCoveragePlan plan =
            latestRequestedHeightPlan;

        if (plan == null)
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
            level < plan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                plan.Levels[level];

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

        bool surfaceTransitionRequired =
            !IsSurfacePageRectActive(
                plan.SurfaceRequiredPages
            );

        if (
            !anyHeightTransition
            && !surfaceTransitionRequired
        )
        {
            return;
        }

        loadRoutine =
            StartCoroutine(
                MultiresolutionTerrainTransitionRoutine(
                    plan,
                    surfaceTransitionRequired
                )
            );
    }

    private void EnsureFallbackRequestedLayout()
    {
        if (latestRequestedHeightPlan != null)
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

        if (
            TryBuildHeightLayoutCoveragePlan(
                fallbackStreamingLayout,
                nextHeightLayoutGeneration++,
                out TerrainHeightLayoutCoveragePlan plan,
                out _
            )
        )
        {
            latestRequestedHeightPlan =
                plan;
        }
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
            || left.SurfaceRequiredPages.Minimum != right.SurfaceRequiredPages.Minimum
            || left.SurfaceRequiredPages.Maximum != right.SurfaceRequiredPages.Maximum
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

            float halfExtent =
                TerrainClipmapTopologyUtility
                    .GetLODHalfExtent(
                        worldSettings,
                        level
                    );

            float minimumX =
                anchor.x - halfExtent;

            float maximumX =
                anchor.x + halfExtent;

            float minimumZ =
                anchor.z - halfExtent;

            float maximumZ =
                anchor.z + halfExtent;

            float normalMargin =
                state.Descriptor.SampleSpacing;

            if (level < layout.LevelCount - 1)
            {
                Vector3 coarseAnchor =
                    layout.GetAnchor(
                        level + 1
                    );

                float stitchHalfExtent =
                    halfExtent +
                    state.Descriptor.SampleSpacing * 2f;

                minimumX =
                    Mathf.Min(
                        minimumX,
                        coarseAnchor.x - stitchHalfExtent
                    );

                maximumX =
                    Mathf.Max(
                        maximumX,
                        coarseAnchor.x + stitchHalfExtent
                    );

                minimumZ =
                    Mathf.Min(
                        minimumZ,
                        coarseAnchor.z - stitchHalfExtent
                    );

                maximumZ =
                    Mathf.Max(
                        maximumZ,
                        coarseAnchor.z + stitchHalfExtent
                    );

                normalMargin =
                    Mathf.Max(
                        normalMargin,
                        heightLodStates[level + 1]
                            .Descriptor
                            .SampleSpacing
                    );
            }

            minimumX -= normalMargin;
            maximumX += normalMargin;
            minimumZ -= normalMargin;
            maximumZ += normalMargin;

            if (
                !TryWorldBoundsToHeightPages(
                    minimumX,
                    minimumZ,
                    maximumX,
                    maximumZ,
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

        if (
            !TryCalculateSurfaceRequiredPages(
                result.MinimumXZ,
                result.MaximumXZ,
                out TerrainHeightPageRect surfaceRequiredPages
            )
        )
        {
            errorMessage =
                "Could not calculate required Surface cache pages for the clipmap layout.";

            return false;
        }

        if (
            surfaceRequiredPages.Width > surfaceCacheWidth
            || surfaceRequiredPages.Height > surfaceCacheHeight
        )
        {
            errorMessage =
                "The independently owned Surface cache is too small for the requested clipmap layout.";

            return false;
        }

        result.SurfaceRequiredPages =
            surfaceRequiredPages;

        result.SurfaceRequestedOrigin =
            ChooseSurfaceCacheOrigin(
                surfaceRequiredPages
            );

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
    // SURFACE LAYOUT COVERAGE
    // =====================================================

    private bool TryCalculateSurfaceRequiredPages(
        Vector2 layoutMinimumXZ,
        Vector2 layoutMaximumXZ,
        out TerrainHeightPageRect pages
    )
    {
        pages = default;

        if (
            surfaceMaskManifest == null
            || surfaceMaskManifest.tileGridWidth <= 0
            || surfaceMaskManifest.tileGridHeight <= 0
        )
        {
            return false;
        }

        float margin =
            Mathf.Max(
                0f,
                surfaceMaskManifest.sampleSpacing
            );

        float minimumX =
            Mathf.Clamp(
                layoutMinimumXZ.x - margin,
                0f,
                surfaceMaskManifest.worldSizeXZ.x
            );

        float minimumZ =
            Mathf.Clamp(
                layoutMinimumXZ.y - margin,
                0f,
                surfaceMaskManifest.worldSizeXZ.y
            );

        float maximumX =
            Mathf.Clamp(
                layoutMaximumXZ.x + margin,
                0f,
                surfaceMaskManifest.worldSizeXZ.x
            );

        float maximumZ =
            Mathf.Clamp(
                layoutMaximumXZ.y + margin,
                0f,
                surfaceMaskManifest.worldSizeXZ.y
            );

        float tileWorldSize =
            Mathf.Max(
                0.0001f,
                surfaceMaskManifest.tileWorldSize
            );

        int maximumTileX =
            Mathf.Max(
                0,
                surfaceMaskManifest.tileGridWidth - 1
            );

        int maximumTileZ =
            Mathf.Max(
                0,
                surfaceMaskManifest.tileGridHeight - 1
            );

        pages =
            new TerrainHeightPageRect(
                new Vector2Int(
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            minimumX /
                            tileWorldSize
                        ),
                        0,
                        maximumTileX
                    ),
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            minimumZ /
                            tileWorldSize
                        ),
                        0,
                        maximumTileZ
                    )
                ),
                new Vector2Int(
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            maximumX /
                            tileWorldSize
                        ),
                        0,
                        maximumTileX
                    ),
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            maximumZ /
                            tileWorldSize
                        ),
                        0,
                        maximumTileZ
                    )
                )
            );

        return pages.IsValid;
    }

    private bool IsSurfacePageRectActive(
        TerrainHeightPageRect required
    )
    {
        if (
            !surfaceCacheReady
            || surfaceMaskCache == null
            || !required.IsValid
        )
        {
            return false;
        }

        TerrainHeightPageRect active =
            new TerrainHeightPageRect(
                surfaceCacheOriginTile,
                new Vector2Int(
                    surfaceCacheOriginTile.x + surfaceCacheWidth - 1,
                    surfaceCacheOriginTile.y + surfaceCacheHeight - 1
                )
            );

        return active.Contains(required);
    }

    private Vector2Int ChooseSurfaceCacheOrigin(
        TerrainHeightPageRect required
    )
    {
        if (IsSurfacePageRectActive(required))
        {
            return surfaceCacheOriginTile;
        }

        int centerX =
            (required.Minimum.x + required.Maximum.x) / 2;

        int centerZ =
            (required.Minimum.y + required.Maximum.y) / 2;

        int originX =
            centerX - surfaceCacheWidth / 2;

        int originZ =
            centerZ - surfaceCacheHeight / 2;

        originX =
            Mathf.Clamp(
                originX,
                required.Maximum.x - surfaceCacheWidth + 1,
                required.Minimum.x
            );

        originZ =
            Mathf.Clamp(
                originZ,
                required.Maximum.y - surfaceCacheHeight + 1,
                required.Minimum.y
            );

        Vector2Int maximumOrigin =
            GetMaximumSurfaceCacheOrigin();

        return
            new Vector2Int(
                Mathf.Clamp(
                    originX,
                    0,
                    maximumOrigin.x
                ),
                Mathf.Clamp(
                    originZ,
                    0,
                    maximumOrigin.y
                )
            );
    }

    // =====================================================
    // MULTI-LOD TRANSITION
    // =====================================================

    private IEnumerator MultiresolutionTerrainTransitionRoutine(
        TerrainHeightLayoutCoveragePlan plan,
        bool surfaceTransitionRequired
    )
    {
        if (plan == null)
        {
            loadRoutine = null;
            yield break;
        }

        int generation =
            plan.Generation;

        bool surfaceAttemptStarted =
            false;

        HashSet<Vector2Int> requiredSurfaceTiles =
            null;

        List<Vector2Int> enteringSurfaceTiles =
            null;

        int retainedSurfaceTileCount =
            0;

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                plan.Levels[level];

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
                    TerrainHeightLodTransitionState.WaitingForRequiredPages;
            }
        }

        if (
            !BeginHeightSourceTransition(
                plan,
                out string heightSourceError
            )
        )
        {
            Debug.LogError(
                heightSourceError,
                this
            );

            FailMultiresolutionTransition(
                plan,
                surfaceAttemptStarted
            );

            yield break;
        }

        if (surfaceTransitionRequired)
        {
            BeginSurfaceLoadAttempt();
            surfaceAttemptStarted = true;

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.LoadingSources;

            requiredSurfaceTiles =
                new HashSet<Vector2Int>();

            enteringSurfaceTiles =
                new List<Vector2Int>();

            if (
                !TryBuildSurfaceTransitionPlan(
                    plan.SurfaceRequestedOrigin,
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

                FailMultiresolutionTransition(
                    plan,
                    surfaceAttemptStarted
                );

                yield break;
            }

            for (
                int index = 0;
                index < enteringSurfaceTiles.Count;
                index++
            )
            {
                if (
                    !TryBeginSurfaceTileLoad(
                        enteringSurfaceTiles[index],
                        out string surfaceLoadError
                    )
                )
                {
                    Debug.LogError(
                        surfaceLoadError,
                        this
                    );

                    FailMultiresolutionTransition(
                        plan,
                        surfaceAttemptStarted
                    );

                    yield break;
                }
            }
        }

        while (
            !AreAllRequiredHeightPagesPrepared(
                plan
            )
        )
        {
            PumpHeightSources(
                plan
            );

            if (
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
                    plan,
                    surfaceAttemptStarted
                );

                yield break;
            }

            yield return null;
        }

        if (surfaceTransitionRequired)
        {
            for (
                int index = 0;
                index < enteringSurfaceTiles.Count;
                index++
            )
            {
                Vector2Int coordinate =
                    enteringSurfaceTiles[index];

                if (
                    !TryGetResidentSurfaceTile(
                        coordinate,
                        out ResidentSurfaceTile surfaceTile
                    )
                )
                {
                    Debug.LogError(
                        $"A requested Surface page is missing from residency: ({coordinate.x}, {coordinate.y}).",
                        this
                    );

                    FailMultiresolutionTransition(
                        plan,
                        surfaceAttemptStarted
                    );

                    yield break;
                }

                var handle =
                    surfaceTile.handle;

                if (!handle.IsDone)
                {
                    yield return handle;
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

                    FailMultiresolutionTransition(
                        plan,
                        surfaceAttemptStarted
                    );

                    yield break;
                }
            }
        }

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            TerrainHeightLodRuntimeState state =
                heightLodStates[level];

            state.TransitionState =
                TerrainHeightLodTransitionState.CommitPending;
        }

        if (surfaceTransitionRequired)
        {
            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.PopulatingStaging;

            if (
                !TryPopulateSurfaceStagingCache(
                    plan.SurfaceRequestedOrigin,
                    out string surfaceStagingError
                )
            )
            {
                Debug.LogError(
                    surfaceStagingError,
                    this
                );

                FailMultiresolutionTransition(
                    plan,
                    surfaceAttemptStarted
                );

                yield break;
            }

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.CommitPending;
        }

        // -------------------------------------------------
        // Atomic publish of every mandatory changed cache.
        // -------------------------------------------------

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            TerrainHeightLodCoveragePlan levelPlan =
                plan.Levels[level];

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
                TerrainHeightLodTransitionState.Idle;
        }

        if (surfaceTransitionRequired)
        {
            SwapSurfaceMaskCaches();

            surfaceCacheOriginTile =
                plan.SurfaceRequestedOrigin;

            requestedSurfaceOriginTile =
                plan.SurfaceRequestedOrigin;

            surfaceCacheReady =
                true;

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.Idle;
        }

        preparedHeightLayoutGeneration =
            generation;

        preparedHeightPlan =
            plan;

        /*
         * Do not rebind renderers here. The controller may still have the
         * previously applied clipmap geometry for the remainder of this
         * Update pass. It will activate these prepared bindings immediately
         * after successfully applying the matching layout.
         */
        multiresolutionBindingPending =
            true;

        NotifyActiveCacheCoverageIfChanged();

        if (surfaceTransitionRequired)
        {
            NotifyActiveSurfaceCacheCoverageIfChanged();
        }

        CompleteHeightSourceTransition(
            plan
        );

        if (surfaceTransitionRequired)
        {
            CompleteSurfaceLoadAttempt(
                requiredSurfaceTiles
            );
        }

        heightPageLoadScheduler.ClearRequiredFailure(
            generation
        );

        if (
            latestRequestedHeightPlan != null
            && latestRequestedHeightPlan.Generation > generation
        )
        {
            heightPageLoadScheduler
                .DiscardQueuedOptionalOlderThan(
                    latestRequestedHeightPlan.Generation
                );
        }

        loadRoutine =
            null;

        if (logCacheUpdates)
        {
            Debug.Log(
                "Multiresolution terrain Height caches committed.\n\n" +
                $"LOD States: {heightLodStates.Length}\n" +
                $"Active Height Loads: {ActiveHeightPageLoadCount}\n" +
                $"Queued Height Loads: {QueuedHeightPageLoadCount}\n" +
                $"Surface Transition: {(surfaceTransitionRequired ? "Committed" : "Retained")}",
                this
            );

            if (surfaceTransitionRequired)
            {
                LogSurfaceCacheReady(
                    retainedSurfaceTileCount,
                    enteringSurfaceTiles.Count,
                    residentSurfaceTiles.Count
                );
            }
        }
    }

    private void FailMultiresolutionTransition(
        TerrainHeightLayoutCoveragePlan plan,
        bool surfaceAttemptStarted
    )
    {
        int generation =
            plan != null
                ? plan.Generation
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
                    TerrainHeightLodTransitionState.Idle;

                state.StagingValidPages.Clear();
            }
        }

        RollbackHeightSourceTransition(
            plan
        );

        if (surfaceAttemptStarted)
        {
            RollbackSurfaceLoadAttempt();

            surfaceTransitionState =
                TerrainSurfaceCacheTransitionState.Idle;
        }

        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler.ClearRequiredFailure(
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
            || !surfaceCacheReady
            || heightmapManifest == null
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
                "TerrainHeightmapStreamer could not bind the Surface cache after multiresolution Height binding.\n\n" +
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
                $"Height LOD States: {heightLodStates.Length}",
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
