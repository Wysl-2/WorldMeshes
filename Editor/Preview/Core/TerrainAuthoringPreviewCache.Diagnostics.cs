/*
 * Editor-domain lifetime diagnostics for preview cache ownership.
 *
 * These counters are observability only. They are never consulted by cache
 * allocation, transition, cancellation, activation, or disposal decisions.
 * Static values naturally reset on assembly reload.
 */
public sealed partial class TerrainAuthoringPreviewCache
{
    private static long diagnosticCreateCount;

    private static long diagnosticDisposeCount;

    private static int diagnosticLiveCount;

    private bool diagnosticDisposeRecorded;

    public TerrainAuthoringPreviewCache()
    {
        diagnosticCreateCount++;

        diagnosticLiveCount++;
    }

    internal static long DiagnosticCreateCount =>
        diagnosticCreateCount;

    internal static long DiagnosticDisposeCount =>
        diagnosticDisposeCount;

    internal static int DiagnosticLiveCount =>
        diagnosticLiveCount;

    private void RecordDiagnosticDispose()
    {
        if (diagnosticDisposeRecorded)
        {
            return;
        }

        diagnosticDisposeRecorded =
            true;

        diagnosticDisposeCount++;

        if (diagnosticLiveCount > 0)
        {
            diagnosticLiveCount--;
        }
    }
}
