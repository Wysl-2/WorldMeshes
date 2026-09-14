/*
 * Lightweight immutable view of persistent runtime bake state.
 *
 * Unlike TerrainRuntimeBakeStateSnapshot, this summary intentionally
 * contains no pending-coordinate collections. It is intended for
 * diagnostics/status consumers that only need scalar state and counts.
 */
public sealed class TerrainRuntimeBakeStateSummary
{
    public int PendingHeightTileCount
    {
        get;
        private set;
    }

    public int PendingSurfaceTileCount
    {
        get;
        private set;
    }

    public int PendingCollisionChunkCount
    {
        get;
        private set;
    }

    public bool FullHeightRebuildRequired
    {
        get;
        private set;
    }

    public bool FullSurfaceRebuildRequired
    {
        get;
        private set;
    }

    public bool FullCollisionRebuildRequired
    {
        get;
        private set;
    }

    public bool AddressablesConfigurationDirty
    {
        get;
        private set;
    }

    public bool AddressablesContentDirty
    {
        get;
        private set;
    }

    public bool RuntimeSceneMetadataDirty
    {
        get;
        private set;
    }

    public string LastObservedAuthoringSignature
    {
        get;
        private set;
    }

    public int SerializedVersion
    {
        get;
        private set;
    }

    public long StateRevision
    {
        get;
        private set;
    }

    public bool HasPendingWork
    {
        get
        {
            return
                PendingHeightTileCount > 0
                ||
                PendingSurfaceTileCount > 0
                ||
                PendingCollisionChunkCount > 0
                ||
                FullHeightRebuildRequired
                ||
                FullSurfaceRebuildRequired
                ||
                FullCollisionRebuildRequired
                ||
                AddressablesConfigurationDirty
                ||
                AddressablesContentDirty
                ||
                RuntimeSceneMetadataDirty;
        }
    }

    internal TerrainRuntimeBakeStateSummary(
        int pendingHeightTileCount,
        int pendingSurfaceTileCount,
        int pendingCollisionChunkCount,
        bool fullHeightRebuildRequired,
        bool fullSurfaceRebuildRequired,
        bool fullCollisionRebuildRequired,
        bool addressablesConfigurationDirty,
        bool addressablesContentDirty,
        bool runtimeSceneMetadataDirty,
        string lastObservedAuthoringSignature,
        int serializedVersion,
        long stateRevision
    )
    {
        PendingHeightTileCount =
            pendingHeightTileCount;

        PendingSurfaceTileCount =
            pendingSurfaceTileCount;

        PendingCollisionChunkCount =
            pendingCollisionChunkCount;

        FullHeightRebuildRequired =
            fullHeightRebuildRequired;

        FullSurfaceRebuildRequired =
            fullSurfaceRebuildRequired;

        FullCollisionRebuildRequired =
            fullCollisionRebuildRequired;

        AddressablesConfigurationDirty =
            addressablesConfigurationDirty;

        AddressablesContentDirty =
            addressablesContentDirty;

        RuntimeSceneMetadataDirty =
            runtimeSceneMetadataDirty;

        LastObservedAuthoringSignature =
            lastObservedAuthoringSignature ??
            "";

        SerializedVersion =
            serializedVersion;

        StateRevision =
            stateRevision;
    }
}
