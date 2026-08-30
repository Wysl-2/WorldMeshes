using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainAuthoringModifierChangeTracker
{
    private sealed class TrackedState
    {
        public TerrainAuthoringData AuthoringData;
        public WorldSettings WorldSettings;
        public List<TerrainHeightModifierSnapshot> Snapshots;
        public bool NotifyPreview;
    }

    private static readonly Dictionary<int, TrackedState>
        trackedStates =
            new Dictionary<int, TrackedState>();

    private static readonly List<Vector2Int>
        lastUndoRedoDirtyTiles =
            new List<Vector2Int>();

    public static int LastUndoRedoDirtyTileCount =>
        lastUndoRedoDirtyTiles.Count;

    public static IReadOnlyList<Vector2Int>
        LastUndoRedoDirtyTiles =>
            lastUndoRedoDirtyTiles;

    static TerrainAuthoringModifierChangeTracker()
    {
        Undo.undoRedoPerformed +=
            OnUndoRedoPerformed;

        EditorApplication.delayCall +=
            TrackDefaultAuthoringData;
    }

    internal static bool TryCaptureStack(
        TerrainAuthoringData authoringData,
        out List<TerrainHeightModifierSnapshot> snapshots,
        out string errorMessage
    )
    {
        snapshots =
            new List<TerrainHeightModifierSnapshot>();

        errorMessage = "";

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";
            return false;
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (int index = 0; index < modifiers.Count; index++)
        {
            if (
                !TerrainHeightModifierSnapshot.TryCreate(
                    modifiers[index],
                    index,
                    out TerrainHeightModifierSnapshot snapshot
                )
            )
            {
                errorMessage =
                    $"Could not capture modifier snapshot at index {index}.";
                return false;
            }

            snapshots.Add(snapshot);
        }

        return true;
    }

    internal static void EnsureTracked(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        bool notifyPreview
    )
    {
        if (
            authoringData == null
            ||
            worldSettings == null
        )
        {
            return;
        }

        int key = authoringData.GetInstanceID();

        if (
            trackedStates.TryGetValue(
                key,
                out TrackedState existing
            )
        )
        {
            existing.AuthoringData = authoringData;
            existing.WorldSettings = worldSettings;
            existing.NotifyPreview = notifyPreview;
            return;
        }

        if (
            !TryCaptureStack(
                authoringData,
                out List<TerrainHeightModifierSnapshot> snapshots,
                out _
            )
        )
        {
            return;
        }

        trackedStates[key] =
            new TrackedState
            {
                AuthoringData = authoringData,
                WorldSettings = worldSettings,
                Snapshots = snapshots,
                NotifyPreview = notifyPreview
            };
    }

    internal static void UpdateTrackedState(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        List<TerrainHeightModifierSnapshot> snapshots,
        bool notifyPreview
    )
    {
        if (
            authoringData == null
            ||
            worldSettings == null
            ||
            snapshots == null
        )
        {
            return;
        }

        trackedStates[authoringData.GetInstanceID()] =
            new TrackedState
            {
                AuthoringData = authoringData,
                WorldSettings = worldSettings,
                Snapshots =
                    new List<TerrainHeightModifierSnapshot>(
                        snapshots
                    ),
                NotifyPreview = notifyPreview
            };
    }

    internal static void Forget(
        TerrainAuthoringData authoringData
    )
    {
        if (authoringData != null)
        {
            trackedStates.Remove(
                authoringData.GetInstanceID()
            );
        }
    }

    internal static void CollectChangedTiles(
        WorldSettings worldSettings,
        IReadOnlyList<TerrainHeightModifierSnapshot> before,
        IReadOnlyList<TerrainHeightModifierSnapshot> after,
        ISet<Vector2Int> output
    )
    {
        if (
            worldSettings == null
            ||
            output == null
        )
        {
            return;
        }

        Dictionary<string, TerrainHeightModifierSnapshot>
            beforeById = BuildSnapshotMap(before);

        Dictionary<string, TerrainHeightModifierSnapshot>
            afterById = BuildSnapshotMap(after);

        foreach (var pair in beforeById)
        {
            TerrainHeightModifierSnapshot oldSnapshot =
                pair.Value;

            if (
                !afterById.TryGetValue(
                    pair.Key,
                    out TerrainHeightModifierSnapshot newSnapshot
                )
            )
            {
                AddSnapshotTiles(
                    worldSettings,
                    oldSnapshot,
                    output
                );
                continue;
            }

            bool changed =
                oldSnapshot.Index != newSnapshot.Index
                ||
                oldSnapshot.ContentSignature !=
                    newSnapshot.ContentSignature;

            if (!changed)
            {
                continue;
            }

            AddSnapshotTiles(
                worldSettings,
                oldSnapshot,
                output
            );

            AddSnapshotTiles(
                worldSettings,
                newSnapshot,
                output
            );
        }

        foreach (var pair in afterById)
        {
            if (beforeById.ContainsKey(pair.Key))
            {
                continue;
            }

            AddSnapshotTiles(
                worldSettings,
                pair.Value,
                output
            );
        }
    }

    private static void OnUndoRedoPerformed()
    {
        lastUndoRedoDirtyTiles.Clear();

        List<int> keys =
            new List<int>(trackedStates.Keys);

        foreach (int key in keys)
        {
            if (
                !trackedStates.TryGetValue(
                    key,
                    out TrackedState tracked
                )
            )
            {
                continue;
            }

            TerrainAuthoringData authoringData =
                tracked.AuthoringData;

            WorldSettings worldSettings =
                tracked.WorldSettings;

            if (
                authoringData == null
                ||
                worldSettings == null
            )
            {
                trackedStates.Remove(key);
                continue;
            }

            if (
                !TryCaptureStack(
                    authoringData,
                    out List<TerrainHeightModifierSnapshot> current,
                    out _
                )
            )
            {
                continue;
            }

            if (
                TerrainHeightModifierSnapshot.StackEquals(
                    tracked.Snapshots,
                    current
                )
            )
            {
                continue;
            }

            HashSet<Vector2Int> dirtyTiles =
                new HashSet<Vector2Int>();

            CollectChangedTiles(
                worldSettings,
                tracked.Snapshots,
                current,
                dirtyTiles
            );

            lastUndoRedoDirtyTiles.AddRange(dirtyTiles);

            // authoringRevision is restored by Unity Undo itself.
            if (tracked.NotifyPreview)
            {
                TerrainAuthoringPreviewService
                    .NotifyCompositeAuthoringStateChanged(
                        dirtyTiles
                    );
            }

            tracked.Snapshots =
                new List<TerrainHeightModifierSnapshot>(
                    current
                );
        }
    }

    private static void TrackDefaultAuthoringData()
    {
        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        if (
            authoringData != null
            &&
            worldSettings != null
        )
        {
            EnsureTracked(
                authoringData,
                worldSettings,
                true
            );
        }
    }

    private static Dictionary<string, TerrainHeightModifierSnapshot>
        BuildSnapshotMap(
            IReadOnlyList<TerrainHeightModifierSnapshot> snapshots
        )
    {
        var result =
            new Dictionary<string, TerrainHeightModifierSnapshot>(
                StringComparer.Ordinal
            );

        if (snapshots == null)
        {
            return result;
        }

        foreach (TerrainHeightModifierSnapshot snapshot in snapshots)
        {
            if (
                snapshot != null
                &&
                !string.IsNullOrEmpty(snapshot.StableId)
            )
            {
                result[snapshot.StableId] = snapshot;
            }
        }

        return result;
    }

    private static void AddSnapshotTiles(
        WorldSettings worldSettings,
        TerrainHeightModifierSnapshot snapshot,
        ISet<Vector2Int> output
    )
    {
        if (
            snapshot == null
            ||
            !snapshot.Enabled
        )
        {
            return;
        }

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                snapshot.AffectedWorldBounds,
                output,
                1
            );
    }
}
