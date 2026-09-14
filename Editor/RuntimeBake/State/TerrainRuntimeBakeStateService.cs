using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Public editor-facing API for persistent runtime bake state.
 *
 * This service deliberately performs storage operations only. It does
 * not decide that one kind of dirty output implies another kind of
 * dirty output. That dependency policy belongs to Package 02's
 * invalidation service and bake planner.
 */
public static class TerrainRuntimeBakeStateService
{
    public const string PersistencePath =
        TerrainRuntimeBakeStateStorage.PersistencePath;

    public static event Action StateChanged;

    // =====================================================
    // SNAPSHOT
    // =====================================================

    public static TerrainRuntimeBakeStateSnapshot GetSnapshot()
    {
        using var profilerScope =
            WorldMeshesProfiler.RuntimeBakeStateSnapshot.Auto();

        TerrainRuntimeBakeState state =
            GetState();

        return
            new TerrainRuntimeBakeStateSnapshot(
                state.PendingHeightTiles,
                state.PendingSurfaceTiles,
                state.PendingCollisionChunks,
                state.FullHeightRebuildRequired,
                state.FullSurfaceRebuildRequired,
                state.FullCollisionRebuildRequired,
                state.AddressablesConfigurationDirty,
                state.AddressablesContentDirty,
                state.RuntimeSceneMetadataDirty,
                state.LastObservedAuthoringSignature,
                state.SerializedVersion,
                state.StateRevision
            );
    }

    /*
     * Scalar-only state view for diagnostics/status consumers. Unlike
     * GetSnapshot(), this does not copy any pending-coordinate collection.
     */
    public static TerrainRuntimeBakeStateSummary GetSummary()
    {
        using var profilerScope =
            WorldMeshesProfiler.RuntimeBakeStateSummary.Auto();

        TerrainRuntimeBakeState state =
            GetState();

        return
            new TerrainRuntimeBakeStateSummary(
                state.PendingHeightTiles.Count,
                state.PendingSurfaceTiles.Count,
                state.PendingCollisionChunks.Count,
                state.FullHeightRebuildRequired,
                state.FullSurfaceRebuildRequired,
                state.FullCollisionRebuildRequired,
                state.AddressablesConfigurationDirty,
                state.AddressablesContentDirty,
                state.RuntimeSceneMetadataDirty,
                state.LastObservedAuthoringSignature,
                state.SerializedVersion,
                state.StateRevision
            );
    }

    // =====================================================
    // ATOMIC BATCH MUTATION
    // =====================================================

    /*
     * Package 02 invalidation often touches several independent storage
     * primitives at once. Apply the complete logical invalidation with one
     * revision increment, one persistence write, and one StateChanged event.
     *
     * Dependency policy intentionally remains outside this service.
     */
    public static bool ApplyMutation(
        TerrainRuntimeBakeStateMutation mutation
    )
    {
        if (mutation == null)
        {
            return false;
        }

        TerrainRuntimeBakeState state =
            GetState();

        bool changed =
            false;

        changed |=
            AddCoordinates(
                state.PendingHeightTiles,
                mutation.HeightTilesToAdd
            );

        changed |=
            AddCoordinates(
                state.PendingSurfaceTiles,
                mutation.SurfaceTilesToAdd
            );

        changed |=
            RemoveCoordinates(
                state.PendingSurfaceTiles,
                mutation.SurfaceTilesToRemove
            );

        if (
            mutation.ClearAllSurfaceTilesRequested
            &&
            state.PendingSurfaceTiles.Count > 0
        )
        {
            state.PendingSurfaceTiles.Clear();

            changed =
                true;
        }

        changed |=
            AddCoordinates(
                state.PendingCollisionChunks,
                mutation.CollisionChunksToAdd
            );

        changed |=
            RemoveCoordinates(
                state.PendingCollisionChunks,
                mutation.CollisionChunksToRemove
            );

        if (
            mutation.ClearAllCollisionChunksRequested
            &&
            state.PendingCollisionChunks.Count > 0
        )
        {
            state.PendingCollisionChunks.Clear();

            changed =
                true;
        }

        changed |=
            RemoveCoordinates(
                state.PendingHeightTiles,
                mutation.HeightTilesToRemove
            );

        if (
            mutation.ClearAllHeightTilesRequested
            &&
            state.PendingHeightTiles.Count > 0
        )
        {
            state.PendingHeightTiles.Clear();

            changed =
                true;
        }

        if (
            mutation.RequireFullHeightRebuild
            &&
            !state.FullHeightRebuildRequired
        )
        {
            state.FullHeightRebuildRequired =
                true;

            changed =
                true;
        }

        if (
            mutation.ClearFullHeightRebuildRequired
            &&
            state.FullHeightRebuildRequired
        )
        {
            state.FullHeightRebuildRequired =
                false;

            changed =
                true;
        }

        if (
            mutation.RequireFullSurfaceRebuild
            &&
            !state.FullSurfaceRebuildRequired
        )
        {
            state.FullSurfaceRebuildRequired =
                true;

            changed =
                true;
        }

        if (
            mutation.ClearFullSurfaceRebuildRequired
            &&
            state.FullSurfaceRebuildRequired
        )
        {
            state.FullSurfaceRebuildRequired =
                false;

            changed =
                true;
        }

        if (
            mutation.RequireFullCollisionRebuild
            &&
            !state.FullCollisionRebuildRequired
        )
        {
            state.FullCollisionRebuildRequired =
                true;

            changed =
                true;
        }

        if (
            mutation.ClearFullCollisionRebuildRequired
            &&
            state.FullCollisionRebuildRequired
        )
        {
            state.FullCollisionRebuildRequired =
                false;

            changed =
                true;
        }

        if (
            mutation.MarkAddressablesConfigurationDirty
            &&
            !state.AddressablesConfigurationDirty
        )
        {
            state.AddressablesConfigurationDirty =
                true;

            changed =
                true;
        }

        if (
            mutation.MarkAddressablesContentDirty
            &&
            !state.AddressablesContentDirty
        )
        {
            state.AddressablesContentDirty =
                true;

            changed =
                true;
        }

        if (
            mutation.MarkRuntimeSceneMetadataDirty
            &&
            !state.RuntimeSceneMetadataDirty
        )
        {
            state.RuntimeSceneMetadataDirty =
                true;

            changed =
                true;
        }

        if (mutation.HasObservedAuthoringSignatureUpdate)
        {
            string signature =
                mutation.ObservedAuthoringSignature ??
                "";

            if (
                !string.Equals(
                    state.LastObservedAuthoringSignature,
                    signature,
                    StringComparison.Ordinal
                )
            )
            {
                state.LastObservedAuthoringSignature =
                    signature;

                changed =
                    true;
            }
        }

        if (!changed)
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool SetLastObservedAuthoringSignature(
        string signature
    )
    {
        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        mutation.SetObservedAuthoringSignature(
            signature
        );

        return
            ApplyMutation(
                mutation
            );
    }

    // =====================================================
    // HEIGHT TILES
    // =====================================================

    public static bool MarkHeightTileDirty(
        Vector2Int coordinate
    )
    {
        return
            MarkHeightTilesDirty(
                SingleCoordinate(
                    coordinate
                )
            );
    }

    public static bool MarkHeightTilesDirty(
        IEnumerable<Vector2Int> coordinates
    )
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (
            !AddCoordinates(
                state.PendingHeightTiles,
                coordinates
            )
        )
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearHeightTile(
        Vector2Int coordinate
    )
    {
        return
            ClearHeightTiles(
                SingleCoordinate(
                    coordinate
                )
            );
    }

    public static bool ClearHeightTiles(
        IEnumerable<Vector2Int> coordinates
    )
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (
            !RemoveCoordinates(
                state.PendingHeightTiles,
                coordinates
            )
        )
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool RequireFullHeightRebuild()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (state.FullHeightRebuildRequired)
        {
            return false;
        }

        state.FullHeightRebuildRequired =
            true;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearFullHeightRebuildRequirement()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (!state.FullHeightRebuildRequired)
        {
            return false;
        }

        state.FullHeightRebuildRequired =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    // =====================================================
    // SURFACE TILES
    // =====================================================

    public static bool MarkSurfaceTileDirty(
        Vector2Int coordinate
    )
    {
        return
            MarkSurfaceTilesDirty(
                SingleCoordinate(
                    coordinate
                )
            );
    }

    public static bool MarkSurfaceTilesDirty(
        IEnumerable<Vector2Int> coordinates
    )
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (
            !AddCoordinates(
                state.PendingSurfaceTiles,
                coordinates
            )
        )
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearSurfaceTile(
        Vector2Int coordinate
    )
    {
        return
            ClearSurfaceTiles(
                SingleCoordinate(
                    coordinate
                )
            );
    }

    public static bool ClearSurfaceTiles(
        IEnumerable<Vector2Int> coordinates
    )
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (
            !RemoveCoordinates(
                state.PendingSurfaceTiles,
                coordinates
            )
        )
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool RequireFullSurfaceRebuild()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (state.FullSurfaceRebuildRequired)
        {
            return false;
        }

        state.FullSurfaceRebuildRequired =
            true;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearFullSurfaceRebuildRequirement()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (!state.FullSurfaceRebuildRequired)
        {
            return false;
        }

        state.FullSurfaceRebuildRequired =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    // =====================================================
    // COLLISION CHUNKS
    // =====================================================

    public static bool MarkCollisionChunkDirty(
        Vector2Int coordinate
    )
    {
        return
            MarkCollisionChunksDirty(
                SingleCoordinate(
                    coordinate
                )
            );
    }

    public static bool MarkCollisionChunksDirty(
        IEnumerable<Vector2Int> coordinates
    )
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (
            !AddCoordinates(
                state.PendingCollisionChunks,
                coordinates
            )
        )
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearCollisionChunk(
        Vector2Int coordinate
    )
    {
        return
            ClearCollisionChunks(
                SingleCoordinate(
                    coordinate
                )
            );
    }

    public static bool ClearCollisionChunks(
        IEnumerable<Vector2Int> coordinates
    )
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (
            !RemoveCoordinates(
                state.PendingCollisionChunks,
                coordinates
            )
        )
        {
            return false;
        }

        CommitMutation(
            state
        );

        return true;
    }

    public static bool RequireFullCollisionRebuild()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (state.FullCollisionRebuildRequired)
        {
            return false;
        }

        state.FullCollisionRebuildRequired =
            true;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearFullCollisionRebuildRequirement()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (!state.FullCollisionRebuildRequired)
        {
            return false;
        }

        state.FullCollisionRebuildRequired =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    // =====================================================
    // ADDRESSABLES
    // =====================================================

    public static bool MarkAddressablesConfigurationDirty()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (state.AddressablesConfigurationDirty)
        {
            return false;
        }

        state.AddressablesConfigurationDirty =
            true;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearAddressablesConfigurationDirty()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (!state.AddressablesConfigurationDirty)
        {
            return false;
        }

        state.AddressablesConfigurationDirty =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool MarkAddressablesContentDirty()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (state.AddressablesContentDirty)
        {
            return false;
        }

        state.AddressablesContentDirty =
            true;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearAddressablesContentDirty()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (!state.AddressablesContentDirty)
        {
            return false;
        }

        state.AddressablesContentDirty =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    // =====================================================
    // RUNTIME SCENE
    // =====================================================

    public static bool MarkRuntimeSceneMetadataDirty()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (state.RuntimeSceneMetadataDirty)
        {
            return false;
        }

        state.RuntimeSceneMetadataDirty =
            true;

        CommitMutation(
            state
        );

        return true;
    }

    public static bool ClearRuntimeSceneMetadataDirty()
    {
        TerrainRuntimeBakeState state =
            GetState();

        if (!state.RuntimeSceneMetadataDirty)
        {
            return false;
        }

        state.RuntimeSceneMetadataDirty =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    // =====================================================
    // EXPLICIT DIAGNOSTIC RESET
    // =====================================================

    public static bool ClearAllPendingState()
    {
        TerrainRuntimeBakeState state =
            GetState();

        bool changed =
            state.PendingHeightTiles.Count > 0
            ||
            state.PendingSurfaceTiles.Count > 0
            ||
            state.PendingCollisionChunks.Count > 0
            ||
            state.FullHeightRebuildRequired
            ||
            state.FullSurfaceRebuildRequired
            ||
            state.FullCollisionRebuildRequired
            ||
            state.AddressablesConfigurationDirty
            ||
            state.AddressablesContentDirty
            ||
            state.RuntimeSceneMetadataDirty;

        if (!changed)
        {
            return false;
        }

        state.PendingHeightTiles.Clear();
        state.PendingSurfaceTiles.Clear();
        state.PendingCollisionChunks.Clear();

        state.FullHeightRebuildRequired =
            false;

        state.FullSurfaceRebuildRequired =
            false;

        state.FullCollisionRebuildRequired =
            false;

        state.AddressablesConfigurationDirty =
            false;

        state.AddressablesContentDirty =
            false;

        state.RuntimeSceneMetadataDirty =
            false;

        CommitMutation(
            state
        );

        return true;
    }

    // =====================================================
    // STORAGE
    // =====================================================

    private static TerrainRuntimeBakeState GetState()
    {
        TerrainRuntimeBakeState state =
            TerrainRuntimeBakeState.instance;

        bool storageChanged =
            state.EnsureStorageInitialized();

        storageChanged |=
            NormalizeCoordinates(
                state.PendingHeightTiles
            );

        storageChanged |=
            NormalizeCoordinates(
                state.PendingSurfaceTiles
            );

        storageChanged |=
            NormalizeCoordinates(
                state.PendingCollisionChunks
            );

        if (storageChanged)
        {
            /*
             * Storage normalization does not represent a new logical
             * bake request, so do not increment StateRevision or emit
             * StateChanged.
             */
            state.Persist();
        }

        return state;
    }

    private static void CommitMutation(
        TerrainRuntimeBakeState state
    )
    {
        if (state.StateRevision < long.MaxValue)
        {
            state.StateRevision++;
        }

        state.Persist();

        Action handler =
            StateChanged;

        if (handler != null)
        {
            handler();
        }
    }

    // =====================================================
    // SET SEMANTICS
    // =====================================================

    private static bool AddCoordinates(
        List<Vector2Int> target,
        IEnumerable<Vector2Int> coordinates
    )
    {
        if (coordinates == null)
        {
            return false;
        }

        HashSet<Vector2Int> set =
            new HashSet<Vector2Int>(
                target
            );

        bool changed =
            set.Count !=
            target.Count;

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            if (
                set.Add(
                    coordinate
                )
            )
            {
                changed =
                    true;
            }
        }

        if (!changed)
        {
            return false;
        }

        target.Clear();

        target.AddRange(
            set
        );

        target.Sort(
            CompareCoordinates
        );

        return true;
    }

    private static bool RemoveCoordinates(
        List<Vector2Int> target,
        IEnumerable<Vector2Int> coordinates
    )
    {
        if (
            coordinates == null
            ||
            target.Count == 0
        )
        {
            return false;
        }

        HashSet<Vector2Int> toRemove =
            new HashSet<Vector2Int>();

        foreach (
            Vector2Int coordinate
            in coordinates
        )
        {
            toRemove.Add(
                coordinate
            );
        }

        if (toRemove.Count == 0)
        {
            return false;
        }

        int removed =
            target.RemoveAll(
                delegate(Vector2Int coordinate)
                {
                    return
                        toRemove.Contains(
                            coordinate
                        );
                }
            );

        if (removed <= 0)
        {
            return false;
        }

        target.Sort(
            CompareCoordinates
        );

        return true;
    }

    private static bool NormalizeCoordinates(
        List<Vector2Int> coordinates
    )
    {
        if (coordinates.Count <= 1)
        {
            return false;
        }

        HashSet<Vector2Int> unique =
            new HashSet<Vector2Int>(
                coordinates
            );

        List<Vector2Int> normalized =
            new List<Vector2Int>(
                unique
            );

        normalized.Sort(
            CompareCoordinates
        );

        bool changed =
            normalized.Count !=
            coordinates.Count;

        if (!changed)
        {
            for (
                int index = 0;
                index < normalized.Count;
                index++
            )
            {
                if (
                    normalized[index] !=
                    coordinates[index]
                )
                {
                    changed =
                        true;

                    break;
                }
            }
        }

        if (!changed)
        {
            return false;
        }

        coordinates.Clear();

        coordinates.AddRange(
            normalized
        );

        return true;
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison =
            left.y.CompareTo(
                right.y
            );

        if (yComparison != 0)
        {
            return yComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }

    private static IEnumerable<Vector2Int> SingleCoordinate(
        Vector2Int coordinate
    )
    {
        yield return coordinate;
    }
}
