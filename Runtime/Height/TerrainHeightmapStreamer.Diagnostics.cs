using System.Collections.Generic;
using UnityEngine;

/*
 * MRH07 read-only multiresolution runtime diagnostics and validator access.
 *
 * No mutable runtime collections are exposed. Validation obtains short-lived
 * snapshots and page references while the existing cache-inspection lock is
 * held.
 */
public partial class TerrainHeightmapStreamer
{
    public bool MultiresolutionTransitionRunning =>
        loadRoutine != null;

    public bool PreparedMultiresolutionActivationPending =>
        multiresolutionBindingPending;

    public bool TryGetHeightLodDiagnostics(
        int level,
        out TerrainHeightLodDiagnosticsSnapshot snapshot
    )
    {
        snapshot = default;

        if (
            heightLodStates == null
            || level < 0
            || level >= heightLodStates.Length
            || heightLodStates[level] == null
        )
        {
            return false;
        }

        int queuedRequired = 0;
        int queuedPrefetch = 0;
        int inFlightRequired = 0;
        int inFlightPrefetch = 0;

        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler.GetCountsForLod(
                level,
                out queuedRequired,
                out queuedPrefetch,
                out inFlightRequired,
                out inFlightPrefetch
            );
        }

        snapshot =
            new TerrainHeightLodDiagnosticsSnapshot(
                heightLodStates[level],
                queuedRequired,
                queuedPrefetch,
                inFlightRequired,
                inFlightPrefetch
            );

        return true;
    }

    public bool TryGetHeightSchedulerDiagnostics(
        out TerrainHeightSchedulerDiagnosticsSnapshot snapshot
    )
    {
        snapshot = default;

        if (heightPageLoadScheduler == null)
        {
            return false;
        }

        snapshot =
            heightPageLoadScheduler
                .GetDiagnosticsSnapshot();

        return true;
    }

    public void ResetHeightSchedulerValidationCounters()
    {
        if (heightPageLoadScheduler != null)
        {
            heightPageLoadScheduler.ResetDiagnosticsCounters();
        }
    }

    internal bool TryBeginMultiresolutionCacheInspection(
        out string reason
    )
    {
        reason = null;

        if (!Application.isPlaying)
        {
            reason =
                "Multiresolution cache inspection can only begin in Play Mode.";

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
                "The runtime terrain caches are already being inspected.";

            return false;
        }

        if (loadRoutine != null)
        {
            reason =
                "A mandatory terrain-cache transition is currently running.";

            return false;
        }

        if (multiresolutionBindingPending)
        {
            reason =
                "A prepared terrain-cache activation is waiting for its matching clipmap layout.";

            return false;
        }

        if (
            heightLodStates == null
            || heightLodStates.Length == 0
        )
        {
            reason =
                "No multiresolution Height LOD states are available.";

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
                reason =
                    $"Height LOD{level} does not have a ready active cache.";

                return false;
            }
        }

        if (
            !surfaceCacheReady
            || surfaceMaskCache == null
        )
        {
            reason =
                "The active Surface cache is not ready.";

            return false;
        }

        cacheInspectionActive = true;
        return true;
    }

    internal bool TryGetHeightLodInspectionSnapshot(
        int level,
        out TerrainHeightLodInspectionSnapshot snapshot
    )
    {
        snapshot = default;

        if (
            !cacheInspectionActive
            || heightLodStates == null
            || level < 0
            || level >= heightLodStates.Length
            || heightLodStates[level] == null
        )
        {
            return false;
        }

        snapshot =
            new TerrainHeightLodInspectionSnapshot(
                heightLodStates[level]
            );

        return true;
    }

    internal bool TryGetHeightLodPageForInspection(
        int level,
        Vector2Int coordinate,
        out Texture2D sourceTexture,
        out int cacheSlice,
        out bool activeValid,
        out bool required
    )
    {
        sourceTexture = null;
        cacheSlice = -1;
        activeValid = false;
        required = false;

        if (
            !cacheInspectionActive
            || heightLodStates == null
            || level < 0
            || level >= heightLodStates.Length
        )
        {
            return false;
        }

        TerrainHeightLodRuntimeState state =
            heightLodStates[level];

        if (state == null)
        {
            return false;
        }

        activeValid =
            state.ActiveValidPages.Contains(
                coordinate
            );

        required =
            state.ActiveRequiredPages.IsValid
            && state.ActiveRequiredPages.Contains(
                coordinate
            );

        Vector2Int local =
            coordinate - state.ActiveCacheOrigin;

        if (
            local.x < 0
            || local.y < 0
            || local.x >= state.CacheWidth
            || local.y >= state.CacheHeight
        )
        {
            return false;
        }

        cacheSlice =
            local.x +
            local.y * state.CacheWidth;

        if (
            !state.ResidentPages.TryGetValue(
                coordinate,
                out TerrainHeightResidentPage page
            )
            || page == null
            || page.State != TerrainHeightResidentPageState.Loaded
            || page.Texture == null
        )
        {
            return false;
        }

        sourceTexture = page.Texture;
        return true;
    }

    internal void GetHeightLodActiveValidPagesForInspection(
        int level,
        List<Vector2Int> output
    )
    {
        if (output == null)
        {
            return;
        }

        output.Clear();

        if (
            !cacheInspectionActive
            || heightLodStates == null
            || level < 0
            || level >= heightLodStates.Length
            || heightLodStates[level] == null
        )
        {
            return;
        }

        foreach (
            Vector2Int coordinate
            in heightLodStates[level].ActiveValidPages
        )
        {
            output.Add(coordinate);
        }
    }

    internal bool TryGetRuntimeHeightConfigurationForInspection(
        out WorldSettings settings,
        out TerrainHeightmapManifest manifest
    )
    {
        settings = worldSettings;
        manifest = heightmapManifest;

        return
            settings != null
            && manifest != null;
    }
}
