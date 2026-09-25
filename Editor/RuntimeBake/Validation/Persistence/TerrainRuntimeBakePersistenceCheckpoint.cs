using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Package 10.2 diagnostic checkpoint.
 *
 * This is NOT runtime bake state. It is a serialized expectation used only to
 * prove that Package 01's persistent state survives editor lifecycle events.
 */
[Serializable]
public sealed class TerrainRuntimeBakePersistenceCheckpoint
{
    public const int CurrentFormatVersion = 2;

    [Serializable]
    public struct CoordinateRecord
    {
        public int x;
        public int y;

        public CoordinateRecord(Vector2Int coordinate)
        {
            x = coordinate.x;
            y = coordinate.y;
        }

        public Vector2Int ToVector2Int()
        {
            return new Vector2Int(x, y);
        }
    }

    public int formatVersion = CurrentFormatVersion;
    public string capturedAtUtc = "";
    public long stateRevision;
    public int serializedBakeStateVersion;

    public List<CoordinateRecord> pendingHeightTiles =
        new List<CoordinateRecord>();

    public List<CoordinateRecord> pendingHeightStreamingTiles =
        new List<CoordinateRecord>();

    public List<CoordinateRecord> pendingSurfaceTiles =
        new List<CoordinateRecord>();

    public List<CoordinateRecord> pendingCollisionChunks =
        new List<CoordinateRecord>();

    public bool fullHeightRebuildRequired;
    public bool fullHeightStreamingRebuildRequired;
    public bool fullSurfaceRebuildRequired;
    public bool fullCollisionRebuildRequired;

    public bool addressablesConfigurationDirty;
    public bool addressablesContentDirty;
    public bool runtimeSceneMetadataDirty;

    public string observedAuthoringSignature = "";
    public string currentAuthoringSignature = "";
    public string surfaceSettingsSignature = "";
    public string collisionSettingsSignature = "";

    public static TerrainRuntimeBakePersistenceCheckpoint Create(
        TerrainRuntimeBakeStateSnapshot snapshot,
        string currentAuthoringSignature,
        string surfaceSettingsSignature,
        string collisionSettingsSignature
    )
    {
        if (snapshot == null)
        {
            return null;
        }

        TerrainRuntimeBakePersistenceCheckpoint checkpoint =
            new TerrainRuntimeBakePersistenceCheckpoint
            {
                formatVersion = CurrentFormatVersion,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                stateRevision = snapshot.StateRevision,
                serializedBakeStateVersion = snapshot.SerializedVersion,
                fullHeightRebuildRequired = snapshot.FullHeightRebuildRequired,
                fullHeightStreamingRebuildRequired = snapshot.FullHeightStreamingRebuildRequired,
                fullSurfaceRebuildRequired = snapshot.FullSurfaceRebuildRequired,
                fullCollisionRebuildRequired = snapshot.FullCollisionRebuildRequired,
                addressablesConfigurationDirty = snapshot.AddressablesConfigurationDirty,
                addressablesContentDirty = snapshot.AddressablesContentDirty,
                runtimeSceneMetadataDirty = snapshot.RuntimeSceneMetadataDirty,
                observedAuthoringSignature = snapshot.LastObservedAuthoringSignature ?? "",
                currentAuthoringSignature = currentAuthoringSignature ?? "",
                surfaceSettingsSignature = surfaceSettingsSignature ?? "",
                collisionSettingsSignature = collisionSettingsSignature ?? ""
            };

        checkpoint.pendingHeightTiles = CopyCoordinates(snapshot.PendingHeightTiles);
        checkpoint.pendingHeightStreamingTiles = CopyCoordinates(snapshot.PendingHeightStreamingTiles);
        checkpoint.pendingSurfaceTiles = CopyCoordinates(snapshot.PendingSurfaceTiles);
        checkpoint.pendingCollisionChunks = CopyCoordinates(snapshot.PendingCollisionChunks);

        return checkpoint;
    }

    public List<Vector2Int> GetPendingHeightTiles()
    {
        return ToCoordinates(pendingHeightTiles);
    }

    public List<Vector2Int> GetPendingHeightStreamingTiles()
    {
        return ToCoordinates(pendingHeightStreamingTiles);
    }

    public List<Vector2Int> GetPendingSurfaceTiles()
    {
        return ToCoordinates(pendingSurfaceTiles);
    }

    public List<Vector2Int> GetPendingCollisionChunks()
    {
        return ToCoordinates(pendingCollisionChunks);
    }

    private static List<CoordinateRecord> CopyCoordinates(
        IReadOnlyList<Vector2Int> source
    )
    {
        List<Vector2Int> sorted = new List<Vector2Int>();

        if (source != null)
        {
            for (int index = 0; index < source.Count; index++)
            {
                sorted.Add(source[index]);
            }
        }

        sorted.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);

        List<CoordinateRecord> result =
            new List<CoordinateRecord>(sorted.Count);

        for (int index = 0; index < sorted.Count; index++)
        {
            result.Add(new CoordinateRecord(sorted[index]));
        }

        return result;
    }

    private static List<Vector2Int> ToCoordinates(
        List<CoordinateRecord> source
    )
    {
        List<Vector2Int> result = new List<Vector2Int>();

        if (source != null)
        {
            for (int index = 0; index < source.Count; index++)
            {
                result.Add(source[index].ToVector2Int());
            }
        }

        result.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
        return result;
    }
}
