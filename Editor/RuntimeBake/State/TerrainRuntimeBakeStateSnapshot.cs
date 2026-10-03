using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/*
 * Immutable/read-only view of persistent runtime bake state.
 *
 * Later planners can consume this snapshot without receiving ownership
 * of the ScriptableSingleton or its mutable serialized collections.
 */
public sealed class TerrainRuntimeBakeStateSnapshot
{
    private readonly ReadOnlyCollection<Vector2Int> pendingHeightTiles;
    private readonly ReadOnlyCollection<Vector2Int> pendingHeightStreamingTiles;
    private readonly ReadOnlyCollection<Vector2Int> pendingSurfaceTiles;
    private readonly ReadOnlyCollection<Vector2Int> pendingSurfaceStreamingTiles;
    private readonly ReadOnlyCollection<Vector2Int> pendingCollisionChunks;

    public IReadOnlyList<Vector2Int> PendingHeightTiles => pendingHeightTiles;
    public IReadOnlyList<Vector2Int> PendingHeightStreamingTiles => pendingHeightStreamingTiles;
    public IReadOnlyList<Vector2Int> PendingSurfaceTiles => pendingSurfaceTiles;
    public IReadOnlyList<Vector2Int> PendingSurfaceStreamingTiles => pendingSurfaceStreamingTiles;
    public IReadOnlyList<Vector2Int> PendingCollisionChunks => pendingCollisionChunks;

    public int PendingHeightTileCount => pendingHeightTiles.Count;
    public int PendingHeightStreamingTileCount => pendingHeightStreamingTiles.Count;
    public int PendingSurfaceTileCount => pendingSurfaceTiles.Count;
    public int PendingSurfaceStreamingTileCount => pendingSurfaceStreamingTiles.Count;
    public int PendingCollisionChunkCount => pendingCollisionChunks.Count;

    public bool FullHeightRebuildRequired { get; private set; }
    public bool FullHeightStreamingRebuildRequired { get; private set; }
    public bool FullSurfaceRebuildRequired { get; private set; }
    public bool FullSurfaceStreamingRebuildRequired { get; private set; }
    public bool FullCollisionRebuildRequired { get; private set; }
    public bool AddressablesConfigurationDirty { get; private set; }
    public bool AddressablesContentDirty { get; private set; }
    public bool RuntimeSceneMetadataDirty { get; private set; }
    public string LastObservedAuthoringSignature { get; private set; }
    public int SerializedVersion { get; private set; }
    public long StateRevision { get; private set; }

    public bool HasPendingWork
    {
        get
        {
            return
                pendingHeightTiles.Count > 0
                || pendingHeightStreamingTiles.Count > 0
                || pendingSurfaceTiles.Count > 0
                || pendingSurfaceStreamingTiles.Count > 0
                || pendingCollisionChunks.Count > 0
                || FullHeightRebuildRequired
                || FullHeightStreamingRebuildRequired
                || FullSurfaceRebuildRequired
                || FullSurfaceStreamingRebuildRequired
                || FullCollisionRebuildRequired
                || AddressablesConfigurationDirty
                || AddressablesContentDirty
                || RuntimeSceneMetadataDirty;
        }
    }

    internal TerrainRuntimeBakeStateSnapshot(
        IEnumerable<Vector2Int> heightTiles,
        IEnumerable<Vector2Int> heightStreamingTiles,
        IEnumerable<Vector2Int> surfaceTiles,
        IEnumerable<Vector2Int> surfaceStreamingTiles,
        IEnumerable<Vector2Int> collisionChunks,
        bool fullHeightRebuildRequired,
        bool fullHeightStreamingRebuildRequired,
        bool fullSurfaceRebuildRequired,
        bool fullSurfaceStreamingRebuildRequired,
        bool fullCollisionRebuildRequired,
        bool addressablesConfigurationDirty,
        bool addressablesContentDirty,
        bool runtimeSceneMetadataDirty,
        string lastObservedAuthoringSignature,
        int serializedVersion,
        long stateRevision
    )
    {
        pendingHeightTiles = new List<Vector2Int>(heightTiles).AsReadOnly();
        pendingHeightStreamingTiles = new List<Vector2Int>(heightStreamingTiles).AsReadOnly();
        pendingSurfaceTiles = new List<Vector2Int>(surfaceTiles).AsReadOnly();
        pendingSurfaceStreamingTiles = new List<Vector2Int>(surfaceStreamingTiles).AsReadOnly();
        pendingCollisionChunks = new List<Vector2Int>(collisionChunks).AsReadOnly();

        FullHeightRebuildRequired = fullHeightRebuildRequired;
        FullHeightStreamingRebuildRequired = fullHeightStreamingRebuildRequired;
        FullSurfaceRebuildRequired = fullSurfaceRebuildRequired;
        FullSurfaceStreamingRebuildRequired = fullSurfaceStreamingRebuildRequired;
        FullCollisionRebuildRequired = fullCollisionRebuildRequired;
        AddressablesConfigurationDirty = addressablesConfigurationDirty;
        AddressablesContentDirty = addressablesContentDirty;
        RuntimeSceneMetadataDirty = runtimeSceneMetadataDirty;
        LastObservedAuthoringSignature = lastObservedAuthoringSignature ?? "";
        SerializedVersion = serializedVersion;
        StateRevision = stateRevision;
    }
}
