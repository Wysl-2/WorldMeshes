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
    private readonly ReadOnlyCollection<Vector2Int>
        pendingHeightTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        pendingSurfaceTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        pendingCollisionChunks;

    public IReadOnlyList<Vector2Int> PendingHeightTiles
    {
        get
        {
            return pendingHeightTiles;
        }
    }

    public IReadOnlyList<Vector2Int> PendingSurfaceTiles
    {
        get
        {
            return pendingSurfaceTiles;
        }
    }

    public IReadOnlyList<Vector2Int> PendingCollisionChunks
    {
        get
        {
            return pendingCollisionChunks;
        }
    }

    public int PendingHeightTileCount
    {
        get
        {
            return pendingHeightTiles.Count;
        }
    }

    public int PendingSurfaceTileCount
    {
        get
        {
            return pendingSurfaceTiles.Count;
        }
    }

    public int PendingCollisionChunkCount
    {
        get
        {
            return pendingCollisionChunks.Count;
        }
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
                pendingHeightTiles.Count > 0
                ||
                pendingSurfaceTiles.Count > 0
                ||
                pendingCollisionChunks.Count > 0
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

    internal TerrainRuntimeBakeStateSnapshot(
        IEnumerable<Vector2Int> heightTiles,
        IEnumerable<Vector2Int> surfaceTiles,
        IEnumerable<Vector2Int> collisionChunks,
        bool fullHeightRebuildRequired,
        bool fullSurfaceRebuildRequired,
        bool fullCollisionRebuildRequired,
        bool addressablesConfigurationDirty,
        bool addressablesContentDirty,
        bool runtimeSceneMetadataDirty,
        int serializedVersion,
        long stateRevision
    )
    {
        pendingHeightTiles =
            new List<Vector2Int>(
                heightTiles
            )
            .AsReadOnly();

        pendingSurfaceTiles =
            new List<Vector2Int>(
                surfaceTiles
            )
            .AsReadOnly();

        pendingCollisionChunks =
            new List<Vector2Int>(
                collisionChunks
            )
            .AsReadOnly();

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

        SerializedVersion =
            serializedVersion;

        StateRevision =
            stateRevision;
    }
}
