using UnityEngine;

/*
 * Transient Height source ownership and cache-to-cache reuse.
 *
 * Addressable Texture2Ds are short-lived transport resources. Durable terrain
 * residency remains exclusively in each LOD's active/staging Texture2DArray.
 */
public partial class TerrainHeightmapStreamer
{
    [Header("Height Source Residency")]

    [SerializeField]
    [Min(1)]
    private int maxHeightPageUploadsPerFrame = 4;

    [SerializeField]
    [Min(0)]
    private int reservedHeightLoadSlotsForRequired = 2;

    private int submittedHeightSourceGeneration = -1;
    private bool hasSubmittedHeightSourceCenter;
    private Vector3 lastSubmittedHeightSourceCoverageCenter;

    private void ConfigureHeightSourceScheduler()
    {
        if (heightPageLoadScheduler == null)
        {
            return;
        }

        int limit = MaxConcurrentHeightPageLoads;

        heightPageLoadScheduler.MaxConcurrentLoads = limit;
        heightPageLoadScheduler.ReservedRequiredSlots =
            Mathf.Clamp(
                reservedHeightLoadSlotsForRequired,
                0,
                Mathf.Max(0, limit - 1)
            );
    }

    private void NotifyLatestHeightSourcePlan(
        TerrainHeightLayoutCoveragePlan plan
    )
    {
        if (
            plan == null
            || heightPageLoadScheduler == null
        )
        {
            return;
        }

        ConfigureHeightSourceScheduler();
        heightPageLoadScheduler.DiscardQueuedOptionalOlderThan(
            plan.Generation
        );
    }

    private bool BeginHeightSourceTransition(
        TerrainHeightLayoutCoveragePlan plan,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            plan == null
            || heightPageLoadScheduler == null
            || heightLodStates == null
        )
        {
            errorMessage =
                "Height source scheduling is unavailable.";

            return false;
        }

        ConfigureHeightSourceScheduler();

        if (submittedHeightSourceGeneration == plan.Generation)
        {
            heightPageLoadScheduler.RecordRepeatedPlanSubmissionSkip();
            return true;
        }

        Vector2 movementDirection =
            hasSubmittedHeightSourceCenter
                ? new Vector2(
                    plan.CoverageCenter.x - lastSubmittedHeightSourceCoverageCenter.x,
                    plan.CoverageCenter.z - lastSubmittedHeightSourceCoverageCenter.z
                )
                : Vector2.zero;

        for (int level = 0; level < plan.LevelCount; level++)
        {
            TerrainHeightLodCoveragePlan levelPlan = plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            TerrainHeightLodRuntimeState state = heightLodStates[level];

            if (
                !PrepareHeightLodStagingCache(
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

        for (int level = 0; level < plan.LevelCount; level++)
        {
            TerrainHeightLodCoveragePlan levelPlan = plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                continue;
            }

            SubmitHeightLodSources(
                heightLodStates[level],
                levelPlan,
                plan.LevelCount,
                plan.Generation,
                movementDirection
            );
        }

        submittedHeightSourceGeneration = plan.Generation;
        lastSubmittedHeightSourceCoverageCenter = plan.CoverageCenter;
        hasSubmittedHeightSourceCenter = true;

        return true;
    }

    private bool PrepareHeightLodStagingCache(
        TerrainHeightLodRuntimeState state,
        TerrainHeightLodCoveragePlan plan,
        int generation,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            state == null
            || state.ActiveCache == null
            || state.StagingCache == null
        )
        {
            errorMessage =
                "A Height LOD cache is unavailable while preparing staging residency.";

            return false;
        }

        state.StagingValidPages.Clear();
        state.StagingCacheOrigin = plan.RequestedCacheOrigin;
        state.StagingRequiredPages = plan.RequiredPages;
        state.StagingGeneration = generation;

        TerrainHeightPageRect stagingPages = state.StagingCachePages;

        foreach (Vector2Int coordinate in state.ActiveValidPages)
        {
            if (!stagingPages.Contains(coordinate))
            {
                continue;
            }

            Vector2Int sourceLocal = coordinate - state.ActiveCacheOrigin;
            Vector2Int destinationLocal = coordinate - state.StagingCacheOrigin;

            if (
                sourceLocal.x < 0
                || sourceLocal.y < 0
                || sourceLocal.x >= state.CacheWidth
                || sourceLocal.y >= state.CacheHeight
                || destinationLocal.x < 0
                || destinationLocal.y < 0
                || destinationLocal.x >= state.CacheWidth
                || destinationLocal.y >= state.CacheHeight
            )
            {
                continue;
            }

            int sourceSlice =
                sourceLocal.x +
                sourceLocal.y * state.CacheWidth;

            int destinationSlice =
                destinationLocal.x +
                destinationLocal.y * state.CacheWidth;

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
            catch (System.Exception exception)
            {
                errorMessage =
                    $"Could not reuse active Height page ({coordinate.x}, {coordinate.y}) for LOD{state.Level}.\n\n" +
                    exception.Message;

                return false;
            }

            state.StagingValidPages.Add(coordinate);
            heightPageLoadScheduler.RecordCacheToCacheReuse();
        }

        return true;
    }

    private void SubmitHeightLodSources(
        TerrainHeightLodRuntimeState state,
        TerrainHeightLodCoveragePlan plan,
        int levelCount,
        int generation,
        Vector2 movementDirection
    )
    {
        if (
            state == null
            || !plan.PrefetchPages.IsValid
            || state.StagingCache == null
        )
        {
            return;
        }

        TerrainHeightPageRect stagingPages = state.StagingCachePages;

        int centerX =
            (plan.RequiredPages.Minimum.x + plan.RequiredPages.Maximum.x) / 2;

        int centerZ =
            (plan.RequiredPages.Minimum.y + plan.RequiredPages.Maximum.y) / 2;

        for (
            int tileZ = plan.PrefetchPages.Minimum.y;
            tileZ <= plan.PrefetchPages.Maximum.y;
            tileZ++
        )
        {
            for (
                int tileX = plan.PrefetchPages.Minimum.x;
                tileX <= plan.PrefetchPages.Maximum.x;
                tileX++
            )
            {
                Vector2Int coordinate = new Vector2Int(tileX, tileZ);

                if (
                    !stagingPages.Contains(coordinate)
                    || state.StagingValidPages.Contains(coordinate)
                )
                {
                    continue;
                }

                bool required = plan.RequiredPages.Contains(coordinate);

                Vector2Int local = coordinate - state.StagingCacheOrigin;

                if (
                    local.x < 0
                    || local.y < 0
                    || local.x >= state.CacheWidth
                    || local.y >= state.CacheHeight
                )
                {
                    continue;
                }

                int destinationSlice =
                    local.x +
                    local.y * state.CacheWidth;

                int distancePriority =
                    Mathf.Abs(tileX - centerX) +
                    Mathf.Abs(tileZ - centerZ);

                TerrainHeightPagePriorityClass priorityClass;

                if (required)
                {
                    bool boundary =
                        tileX == plan.RequiredPages.Minimum.x
                        || tileX == plan.RequiredPages.Maximum.x
                        || tileZ == plan.RequiredPages.Minimum.y
                        || tileZ == plan.RequiredPages.Maximum.y;

                    if (!boundary)
                    {
                        priorityClass =
                            TerrainHeightPagePriorityClass.VisibleRequired;
                    }
                    else if (state.Level < levelCount - 1)
                    {
                        priorityClass =
                            TerrainHeightPagePriorityClass.StitchRequired;
                    }
                    else
                    {
                        priorityClass =
                            TerrainHeightPagePriorityClass.NormalMarginRequired;
                    }
                }
                else
                {
                    Vector2 towardPage =
                        new Vector2(
                            tileX - centerX,
                            tileZ - centerZ
                        );

                    priorityClass =
                        movementDirection.sqrMagnitude > 0.0001f
                        && Vector2.Dot(towardPage, movementDirection) > 0f
                            ? TerrainHeightPagePriorityClass.DirectionalPrefetch
                            : TerrainHeightPagePriorityClass.GuardPrefetch;
                }

                string address =
                    heightmapManifest.GetHeightRepresentationAddress(
                        state.SampleStride,
                        tileX,
                        tileZ
                    );

                heightPageLoadScheduler.Enqueue(
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

    private void PumpHeightSources(
        TerrainHeightLayoutCoveragePlan plan
    )
    {
        if (heightPageLoadScheduler == null)
        {
            return;
        }

        ConfigureHeightSourceScheduler();
        heightPageLoadScheduler.Pump();

        int uploadLimit =
            Mathf.Clamp(
                maxHeightPageUploadsPerFrame,
                1,
                MaxConcurrentHeightPageLoads
            );

        for (int upload = 0; upload < uploadLimit; upload++)
        {
            if (
                !heightPageLoadScheduler.TryGetReadySource(
                    out TerrainHeightPageSourceTransfer source
                )
            )
            {
                break;
            }

            TerrainHeightLodRuntimeState owner = source.Owner;

            bool destinationCurrent =
                owner != null
                && owner.StagingGeneration == source.Generation
                && source.Generation == plan.Generation
                && owner.StagingCache == source.DestinationCache
                && source.DestinationCache != null
                && source.DestinationSlice >= 0;

            if (!destinationCurrent)
            {
                heightPageLoadScheduler.DiscardReadySource(
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
                    heightPageLoadScheduler.FailReadySourceUpload(
                        source
                    );

                    continue;
                }

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

                heightPageLoadScheduler.CompleteReadySourceUpload(
                    source
                );
            }
            catch (System.Exception)
            {
                heightPageLoadScheduler.FailReadySourceUpload(
                    source
                );
            }
        }
    }

    private bool AreAllRequiredHeightPagesPrepared(
        TerrainHeightLayoutCoveragePlan plan
    )
    {
        if (plan == null)
        {
            return false;
        }

        for (int level = 0; level < plan.LevelCount; level++)
        {
            TerrainHeightLodRuntimeState state = heightLodStates[level];
            TerrainHeightLodCoveragePlan levelPlan = plan.Levels[level];

            if (!levelPlan.TransitionRequired)
            {
                if (!IsRequiredPageRectActive(state, levelPlan.RequiredPages))
                {
                    return false;
                }

                continue;
            }

            foreach (Vector2Int coordinate in EnumeratePageRect(levelPlan.RequiredPages))
            {
                if (!state.StagingValidPages.Contains(coordinate))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void CompleteHeightSourceTransition(
        TerrainHeightLayoutCoveragePlan plan
    )
    {
        if (plan == null)
        {
            return;
        }

        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler.CompleteGeneration(
                plan.Generation
            );
        }

        if (heightLodStates != null)
        {
            for (int level = 0; level < heightLodStates.Length; level++)
            {
                TerrainHeightLodRuntimeState state = heightLodStates[level];

                if (state == null)
                {
                    continue;
                }

                state.StagingGeneration = 0;
                state.StagingRequiredPages = default;
                state.StagingCacheOrigin = state.ActiveCacheOrigin;
            }
        }

        if (submittedHeightSourceGeneration == plan.Generation)
        {
            submittedHeightSourceGeneration = -1;
        }
    }

    private void RollbackHeightSourceTransition(
        TerrainHeightLayoutCoveragePlan plan
    )
    {
        int generation = plan != null ? plan.Generation : -1;

        if (
            generation >= 0
            && heightPageLoadScheduler != null
        )
        {
            heightPageLoadScheduler.AbandonGeneration(generation);
        }

        if (heightLodStates != null)
        {
            for (int level = 0; level < heightLodStates.Length; level++)
            {
                TerrainHeightLodRuntimeState state = heightLodStates[level];

                if (state == null)
                {
                    continue;
                }

                state.StagingValidPages.Clear();
                state.StagingGeneration = 0;
                state.StagingRequiredPages = default;
            }
        }

        if (submittedHeightSourceGeneration == generation)
        {
            submittedHeightSourceGeneration = -1;
        }
    }
}
