using UnityEngine;

/*
 * Transient Surface source ownership and cache-to-cache reuse.
 *
 * Addressable Texture2Ds are short-lived transport resources. Durable Surface
 * residency remains exclusively in each LOD's active/staging Texture2DArray.
 */
public partial class TerrainHeightmapStreamer
{
    [Header("Surface Source Residency")]

    [SerializeField]
    [Min(1)]
    private int maxConcurrentSurfacePageLoads = 8;

    [SerializeField]
    [Min(1)]
    private int maxSurfacePageUploadsPerFrame = 4;

    [SerializeField]
    [Min(0)]
    private int reservedSurfaceLoadSlotsForRequired = 2;

    private int submittedSurfaceSourceGeneration =
        -1;

    private bool hasSubmittedSurfaceSourceCenter;

    private Vector3
        lastSubmittedSurfaceSourceCoverageCenter;

    public int MaxConcurrentSurfacePageLoads =>
        Mathf.Max(
            1,
            maxConcurrentSurfacePageLoads
        );

    private void ConfigureSurfaceSourceScheduler()
    {
        if (surfacePageLoadScheduler == null)
        {
            return;
        }

        int limit =
            MaxConcurrentSurfacePageLoads;

        surfacePageLoadScheduler
            .MaxConcurrentLoads =
                limit;

        surfacePageLoadScheduler
            .ReservedRequiredSlots =
                Mathf.Clamp(
                    reservedSurfaceLoadSlotsForRequired,
                    0,
                    Mathf.Max(
                        0,
                        limit - 1
                    )
                );
    }

    private void NotifyLatestSurfaceSourcePlan(
        TerrainSurfaceLayoutCoveragePlan plan
    )
    {
        if (
            plan == null
            ||
            surfacePageLoadScheduler == null
        )
        {
            return;
        }

        ConfigureSurfaceSourceScheduler();

        if (surfaceLodStates != null)
        {
            int count =
                Mathf.Min(
                    surfaceLodStates.Length,
                    plan.LevelCount
                );

            for (
                int level = 0;
                level < count;
                level++
            )
            {
                TerrainSurfaceLodRuntimeState state =
                    surfaceLodStates[level];

                TerrainSurfaceLodCoveragePlan levelPlan =
                    plan.Levels[level];

                if (
                    state == null
                    ||
                    levelPlan == null
                )
                {
                    continue;
                }

                state.RequestGeneration =
                    plan.Generation;

                state.RequestedCacheOrigin =
                    levelPlan.RequestedCacheOrigin;

                state.RequestedRequiredPages =
                    levelPlan.RequiredPages;

                state.RequestedPrefetchPages =
                    levelPlan.PrefetchPages;
            }
        }

        surfacePageLoadScheduler
            .DiscardQueuedOptionalOlderThan(
                plan.Generation
            );
    }

    private bool BeginSurfaceSourceTransition(
        TerrainSurfaceLayoutCoveragePlan plan,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            plan == null
            ||
            surfacePageLoadScheduler == null
            ||
            surfaceLodStates == null
        )
        {
            errorMessage =
                "Surface source scheduling is unavailable.";

            return false;
        }

        ConfigureSurfaceSourceScheduler();

        if (
            submittedSurfaceSourceGeneration ==
            plan.Generation
        )
        {
            return true;
        }

        Vector2 movementDirection =
            hasSubmittedSurfaceSourceCenter
                ? new Vector2(
                    plan.CoverageCenter.x -
                        lastSubmittedSurfaceSourceCoverageCenter.x,
                    plan.CoverageCenter.z -
                        lastSubmittedSurfaceSourceCoverageCenter.z
                )
                : Vector2.zero;

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan levelPlan =
                plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            if (
                !PrepareSurfaceLodStagingCache(
                    state,
                    levelPlan,
                    plan.Generation,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        for (
            int level = 0;
            level < plan.LevelCount;
            level++
        )
        {
            TerrainSurfaceLodCoveragePlan levelPlan =
                plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            SubmitSurfaceLodSources(
                surfaceLodStates[level],
                levelPlan,
                plan.LevelCount,
                plan.Generation,
                movementDirection
            );
        }

        submittedSurfaceSourceGeneration =
            plan.Generation;

        lastSubmittedSurfaceSourceCoverageCenter =
            plan.CoverageCenter;

        hasSubmittedSurfaceSourceCenter =
            true;

        return true;
    }

    private bool PrepareSurfaceLodStagingCache(
        TerrainSurfaceLodRuntimeState state,
        TerrainSurfaceLodCoveragePlan plan,
        int generation,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            state == null
            ||
            state.ActiveCache == null
            ||
            state.StagingCache == null
        )
        {
            errorMessage =
                "A Surface LOD cache is unavailable while preparing staging residency.";

            return false;
        }

        state.StagingValidPages.Clear();

        state.StagingCacheOrigin =
            plan.RequestedCacheOrigin;

        state.StagingRequiredPages =
            plan.RequiredPages;

        state.StagingGeneration =
            generation;

        TerrainHeightPageRect stagingPages =
            state.StagingCachePages;

        foreach (
            Vector2Int coordinate
            in state.ActiveValidPages
        )
        {
            if (
                !stagingPages.Contains(
                    coordinate
                )
            )
            {
                continue;
            }

            Vector2Int sourceLocal =
                coordinate -
                state.ActiveCacheOrigin;

            Vector2Int destinationLocal =
                coordinate -
                state.StagingCacheOrigin;

            if (
                sourceLocal.x < 0
                ||
                sourceLocal.y < 0
                ||
                sourceLocal.x >=
                    state.CacheWidth
                ||
                sourceLocal.y >=
                    state.CacheHeight
                ||
                destinationLocal.x < 0
                ||
                destinationLocal.y < 0
                ||
                destinationLocal.x >=
                    state.CacheWidth
                ||
                destinationLocal.y >=
                    state.CacheHeight
            )
            {
                continue;
            }

            int sourceSlice =
                sourceLocal.x +
                sourceLocal.y *
                state.CacheWidth;

            int destinationSlice =
                destinationLocal.x +
                destinationLocal.y *
                state.CacheWidth;

            try
            {
                Graphics.CopyTexture(
                    state.ActiveCache,
                    sourceSlice,
                    0,
                    state.StagingCache,
                    destinationSlice,
                    0
                );
            }
            catch (
                System.Exception exception
            )
            {
                errorMessage =
                    $"Could not reuse active Surface page ({coordinate.x}, {coordinate.y}) for LOD{state.Level}.\n\n" +
                    exception.Message;

                return false;
            }

            state.StagingValidPages.Add(
                coordinate
            );

            surfacePageLoadScheduler
                .RecordCacheToCacheReuse();
        }

        return true;
    }

    private void SubmitSurfaceLodSources(
        TerrainSurfaceLodRuntimeState state,
        TerrainSurfaceLodCoveragePlan plan,
        int levelCount,
        int generation,
        Vector2 movementDirection
    )
    {
        if (
            state == null
            ||
            !plan.PrefetchPages.IsValid
            ||
            state.StagingCache == null
        )
        {
            return;
        }

        TerrainHeightPageRect stagingPages =
            state.StagingCachePages;

        int centerX =
            (
                plan.RequiredPages.Minimum.x
                +
                plan.RequiredPages.Maximum.x
            )
            /
            2;

        int centerZ =
            (
                plan.RequiredPages.Minimum.y
                +
                plan.RequiredPages.Maximum.y
            )
            /
            2;

        for (
            int tileZ =
                plan.PrefetchPages.Minimum.y;
            tileZ <=
                plan.PrefetchPages.Maximum.y;
            tileZ++
        )
        {
            for (
                int tileX =
                    plan.PrefetchPages.Minimum.x;
                tileX <=
                    plan.PrefetchPages.Maximum.x;
                tileX++
            )
            {
                Vector2Int coordinate =
                    new Vector2Int(
                        tileX,
                        tileZ
                    );

                if (
                    !stagingPages.Contains(
                        coordinate
                    )
                    ||
                    state.StagingValidPages.Contains(
                        coordinate
                    )
                )
                {
                    continue;
                }

                bool required =
                    plan.RequiredPages.Contains(
                        coordinate
                    );

                Vector2Int local =
                    coordinate -
                    state.StagingCacheOrigin;

                if (
                    local.x < 0
                    ||
                    local.y < 0
                    ||
                    local.x >=
                        state.CacheWidth
                    ||
                    local.y >=
                        state.CacheHeight
                )
                {
                    continue;
                }

                int destinationSlice =
                    local.x +
                    local.y *
                    state.CacheWidth;

                int distancePriority =
                    Mathf.Abs(
                        tileX -
                        centerX
                    )
                    +
                    Mathf.Abs(
                        tileZ -
                        centerZ
                    );

                TerrainSurfacePagePriorityClass
                    priorityClass;

                if (required)
                {
                    bool boundary =
                        tileX ==
                            plan.RequiredPages.Minimum.x
                        ||
                        tileX ==
                            plan.RequiredPages.Maximum.x
                        ||
                        tileZ ==
                            plan.RequiredPages.Minimum.y
                        ||
                        tileZ ==
                            plan.RequiredPages.Maximum.y;

                    priorityClass =
                        boundary
                        &&
                        state.Level <
                            levelCount - 1
                            ? TerrainSurfacePagePriorityClass
                                .StitchRequired
                            : TerrainSurfacePagePriorityClass
                                .VisibleRequired;
                }
                else
                {
                    Vector2 towardPage =
                        new Vector2(
                            tileX -
                                centerX,
                            tileZ -
                                centerZ
                        );

                    priorityClass =
                        movementDirection
                            .sqrMagnitude >
                            0.0001f
                        &&
                        Vector2.Dot(
                            towardPage,
                            movementDirection
                        ) > 0f
                            ? TerrainSurfacePagePriorityClass
                                .DirectionalPrefetch
                            : TerrainSurfacePagePriorityClass
                                .GuardPrefetch;
                }

                string address =
                    surfaceMaskManifest
                        .GetSurfaceRepresentationAddress(
                            state.SampleStride,
                            tileX,
                            tileZ
                        );

                surfacePageLoadScheduler
                    .Enqueue(
                        state,
                        coordinate,
                        address,
                        state.StagingCache,
                        destinationSlice,
                        required,
                        priorityClass,
                        generation,
                        distancePriority
                    );
            }
        }
    }

    private void PumpSurfaceSources(
        TerrainSurfaceLayoutCoveragePlan plan
    )
    {
        if (
            surfacePageLoadScheduler == null
            ||
            plan == null
        )
        {
            return;
        }

        ConfigureSurfaceSourceScheduler();

        surfacePageLoadScheduler.Pump();

        int uploadLimit =
            Mathf.Clamp(
                maxSurfacePageUploadsPerFrame,
                1,
                MaxConcurrentSurfacePageLoads
            );

        for (
            int upload = 0;
            upload < uploadLimit;
            upload++
        )
        {
            if (
                !surfacePageLoadScheduler
                    .TryGetReadySource(
                        out TerrainSurfacePageSourceTransfer
                            source
                    )
            )
            {
                break;
            }

            TerrainSurfaceLodRuntimeState owner =
                source.Owner;

            bool destinationCurrent =
                owner != null
                &&
                owner.StagingGeneration ==
                    source.Generation
                &&
                source.Generation ==
                    plan.Generation
                &&
                owner.StagingCache ==
                    source.DestinationCache
                &&
                source.DestinationCache != null
                &&
                source.DestinationSlice >= 0;

            if (!destinationCurrent)
            {
                surfacePageLoadScheduler
                    .DiscardReadySource(
                        source,
                        true
                    );

                continue;
            }

            try
            {
                Texture2D sourceTexture =
                    source.Handle.IsValid()
                        ? source.Handle.Result
                        : null;

                if (sourceTexture == null)
                {
                    surfacePageLoadScheduler
                        .FailReadySourceUpload(
                            source
                        );

                    continue;
                }

                owner.TransitionState =
                    TerrainSurfaceCacheTransitionState
                        .PopulatingStaging;

                Graphics.CopyTexture(
                    sourceTexture,
                    0,
                    0,
                    source.DestinationCache,
                    source.DestinationSlice,
                    0
                );

                owner.StagingValidPages.Add(
                    source.Key.Coordinate
                );

                surfacePageLoadScheduler
                    .CompleteReadySourceUpload(
                        source
                    );
            }
            catch (System.Exception)
            {
                surfacePageLoadScheduler
                    .FailReadySourceUpload(
                        source
                    );
            }
        }
    }

    private bool AreAllRequiredSurfacePagesPrepared(
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
            TerrainSurfaceLodRuntimeState state =
                surfaceLodStates[level];

            TerrainSurfaceLodCoveragePlan levelPlan =
                plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                if (
                    !IsRequiredSurfacePageRectActive(
                        state,
                        levelPlan.RequiredPages
                    )
                )
                {
                    return false;
                }

                continue;
            }

            foreach (
                Vector2Int coordinate
                in EnumeratePageRect(
                    levelPlan.RequiredPages
                )
            )
            {
                if (
                    !state.StagingValidPages.Contains(
                        coordinate
                    )
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void CompleteSurfaceSourceTransition(
        TerrainSurfaceLayoutCoveragePlan plan
    )
    {
        if (plan == null)
        {
            return;
        }

        if (surfacePageLoadScheduler != null)
        {
            surfacePageLoadScheduler
                .CompleteGeneration(
                    plan.Generation
                );
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

                state.StagingGeneration =
                    0;

                state.StagingRequiredPages =
                    default;

                state.StagingCacheOrigin =
                    state.ActiveCacheOrigin;
            }
        }

        if (
            submittedSurfaceSourceGeneration ==
            plan.Generation
        )
        {
            submittedSurfaceSourceGeneration =
                -1;
        }
    }

    private void RollbackSurfaceSourceTransition(
        TerrainSurfaceLayoutCoveragePlan plan
    )
    {
        int generation =
            plan != null
                ? plan.Generation
                : -1;

        if (
            generation >= 0
            &&
            surfacePageLoadScheduler != null
        )
        {
            surfacePageLoadScheduler
                .AbandonGeneration(
                    generation
                );
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

                state.StagingValidPages.Clear();

                state.StagingGeneration =
                    0;

                state.StagingRequiredPages =
                    default;

                state.TransitionState =
                    TerrainSurfaceCacheTransitionState
                        .Idle;
            }
        }

        if (
            submittedSurfaceSourceGeneration ==
            generation
        )
        {
            submittedSurfaceSourceGeneration =
                -1;
        }
    }

    private void ResetSurfaceSourceResidencyTracking()
    {
        submittedSurfaceSourceGeneration =
            -1;

        hasSubmittedSurfaceSourceCenter =
            false;

        lastSubmittedSurfaceSourceCoverageCenter =
            Vector3.zero;
    }
}
