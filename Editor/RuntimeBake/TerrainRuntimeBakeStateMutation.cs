using System.Collections.Generic;
using UnityEngine;

/*
 * Storage-only description of one logical persistent bake-state mutation.
 *
 * This type deliberately contains no dependency propagation policy. The
 * invalidation/compiler layer decides which primitives are required; the state
 * service merely applies those primitives atomically.
 */
public sealed class TerrainRuntimeBakeStateMutation
{
    private readonly HashSet<Vector2Int>
        heightTilesToAdd =
            new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int>
        heightTilesToRemove =
            new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int>
        surfaceTilesToAdd =
            new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int>
        collisionChunksToAdd =
            new HashSet<Vector2Int>();

    internal IEnumerable<Vector2Int> HeightTilesToAdd =>
        heightTilesToAdd;

    internal IEnumerable<Vector2Int> HeightTilesToRemove =>
        heightTilesToRemove;

    internal IEnumerable<Vector2Int> SurfaceTilesToAdd =>
        surfaceTilesToAdd;

    internal IEnumerable<Vector2Int> CollisionChunksToAdd =>
        collisionChunksToAdd;

    internal bool ClearAllHeightTilesRequested
    {
        get;
        private set;
    }

    internal bool RequireFullHeightRebuild
    {
        get;
        private set;
    }

    internal bool ClearFullHeightRebuildRequired
    {
        get;
        private set;
    }

    internal bool RequireFullSurfaceRebuild
    {
        get;
        private set;
    }

    internal bool RequireFullCollisionRebuild
    {
        get;
        private set;
    }

    internal bool MarkAddressablesConfigurationDirty
    {
        get;
        private set;
    }

    internal bool MarkAddressablesContentDirty
    {
        get;
        private set;
    }

    internal bool MarkRuntimeSceneMetadataDirty
    {
        get;
        private set;
    }

    internal bool HasObservedAuthoringSignatureUpdate
    {
        get;
        private set;
    }

    internal string ObservedAuthoringSignature
    {
        get;
        private set;
    } = "";

    public TerrainRuntimeBakeStateMutation AddHeightTiles(
        IEnumerable<Vector2Int> coordinates
    )
    {
        AddCoordinates(
            heightTilesToAdd,
            coordinates
        );

        return this;
    }

    public TerrainRuntimeBakeStateMutation RemoveHeightTiles(
        IEnumerable<Vector2Int> coordinates
    )
    {
        AddCoordinates(
            heightTilesToRemove,
            coordinates
        );

        return this;
    }

    public TerrainRuntimeBakeStateMutation ClearAllHeightTiles()
    {
        ClearAllHeightTilesRequested =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation AddSurfaceTiles(
        IEnumerable<Vector2Int> coordinates
    )
    {
        AddCoordinates(
            surfaceTilesToAdd,
            coordinates
        );

        return this;
    }

    public TerrainRuntimeBakeStateMutation AddCollisionChunks(
        IEnumerable<Vector2Int> coordinates
    )
    {
        AddCoordinates(
            collisionChunksToAdd,
            coordinates
        );

        return this;
    }

    public TerrainRuntimeBakeStateMutation RequireFullHeight()
    {
        RequireFullHeightRebuild =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation ClearFullHeight()
    {
        ClearFullHeightRebuildRequired =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation RequireFullSurface()
    {
        RequireFullSurfaceRebuild =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation RequireFullCollision()
    {
        RequireFullCollisionRebuild =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation DirtyAddressablesConfiguration()
    {
        MarkAddressablesConfigurationDirty =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation DirtyAddressablesContent()
    {
        MarkAddressablesContentDirty =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation DirtyRuntimeSceneMetadata()
    {
        MarkRuntimeSceneMetadataDirty =
            true;

        return this;
    }

    public TerrainRuntimeBakeStateMutation SetObservedAuthoringSignature(
        string signature
    )
    {
        HasObservedAuthoringSignatureUpdate =
            true;

        ObservedAuthoringSignature =
            signature ??
            "";

        return this;
    }

    private static void AddCoordinates(
        HashSet<Vector2Int> target,
        IEnumerable<Vector2Int> coordinates
    )
    {
        if (coordinates == null)
        {
            return;
        }

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            target.Add(
                coordinate
            );
        }
    }
}
