public partial class TerrainHeightmapStreamer
{
    public bool TryGetSurfaceLodDiagnostics(
        int level,
        out TerrainSurfaceLodDiagnosticsSnapshot snapshot
    )
    {
        snapshot = default;

        if (
            surfaceLodStates == null
            ||
            level < 0
            ||
            level >= surfaceLodStates.Length
            ||
            surfaceLodStates[level] == null
        )
        {
            return false;
        }

        int queuedRequired = 0;
        int queuedPrefetch = 0;
        int inFlightRequired = 0;
        int inFlightPrefetch = 0;

        if (surfacePageLoadScheduler != null)
        {
            surfacePageLoadScheduler.GetCountsForLod(
                level,
                out queuedRequired,
                out queuedPrefetch,
                out inFlightRequired,
                out inFlightPrefetch
            );
        }

        snapshot =
            new TerrainSurfaceLodDiagnosticsSnapshot(
                surfaceLodStates[level],
                queuedRequired,
                queuedPrefetch,
                inFlightRequired,
                inFlightPrefetch
            );

        return true;
    }

    public bool TryGetSurfaceSchedulerDiagnostics(
        out TerrainSurfaceSchedulerDiagnosticsSnapshot snapshot
    )
    {
        snapshot = default;

        if (surfacePageLoadScheduler == null)
        {
            return false;
        }

        snapshot =
            surfacePageLoadScheduler
                .GetDiagnosticsSnapshot();

        return true;
    }

    public void ResetSurfaceSchedulerValidationCounters()
    {
        if (surfacePageLoadScheduler != null)
        {
            surfacePageLoadScheduler
                .ResetDiagnosticsCounters();
        }
    }
}
