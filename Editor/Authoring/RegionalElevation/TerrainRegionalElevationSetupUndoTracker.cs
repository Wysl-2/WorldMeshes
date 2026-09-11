/*
 * Compatibility shim for the Package 4 setup-only tracker name.
 *
 * Package 5 replaces the narrow setup listener with the generalized
 * TerrainRegionalElevationChangeTracker. Keeping this forwarding class avoids
 * breaking any older editor code that still calls RecordCurrentState(), while
 * ensuring there is only one regional Undo/Redo event subscription.
 */
public static class TerrainRegionalElevationSetupUndoTracker
{
    public static void RecordCurrentState()
    {
        TerrainRegionalElevationChangeTracker
            .RecordCurrentState();
    }
}
