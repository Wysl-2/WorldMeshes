using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package 5 regional-elevation Undo/Redo observer.
 *
 * Normal mutations are owned by TerrainRegionalElevationService. This tracker
 * exists because Unity restores serialized TerrainAuthoringData directly when
 * Undo/Redo is performed; the service is not called during that restoration.
 */
[InitializeOnLoad]
public static class TerrainRegionalElevationChangeTracker
{
    private sealed class TrackedState
    {
        public TerrainAuthoringData AuthoringData;
        public WorldSettings WorldSettings;
        public TerrainRegionalElevationSnapshot Snapshot;
        public bool NotifyPreview;
        public bool NotifyRuntime;
    }

    private static readonly Dictionary<int, TrackedState>
        trackedStates =
            new Dictionary<int, TrackedState>();

    private static readonly List<Vector2Int>
        lastUndoRedoDirtyTiles =
            new List<Vector2Int>();

    private static int lastUndoRedoDirtyTileCount;

    private static string lastUndoRedoInvalidationKind =
        "None";

    public static int LastUndoRedoDirtyTileCount =>
        lastUndoRedoDirtyTileCount;

    public static IReadOnlyList<Vector2Int> LastUndoRedoDirtyTiles =>
        lastUndoRedoDirtyTiles;

    public static string LastUndoRedoInvalidationKind =>
        lastUndoRedoInvalidationKind;

    static TerrainRegionalElevationChangeTracker()
    {
        Undo.undoRedoPerformed +=
            OnUndoRedoPerformed;

        EditorApplication.delayCall +=
            RecordCurrentState;
    }

    public static void RecordCurrentState()
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
            authoringData == null
            ||
            worldSettings == null
        )
        {
            return;
        }

        bool liveDerivedState =
            TerrainRegionalElevationService
                .IsLiveAuthoringContext(
                    authoringData,
                    worldSettings
                )
            &&
            !string.IsNullOrEmpty(
                TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    )
            );

        if (
            !TerrainRegionalElevationSnapshot.TryCapture(
                authoringData,
                out TerrainRegionalElevationSnapshot snapshot,
                out _
            )
        )
        {
            return;
        }

        UpdateTrackedState(
            authoringData,
            worldSettings,
            snapshot,
            liveDerivedState && TerrainAuthoringPreviewService.Enabled,
            liveDerivedState
        );
    }

    internal static void EnsureTracked(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        bool notifyPreview,
        bool notifyRuntime
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

        int key =
            authoringData.GetInstanceID();

        if (
            trackedStates.TryGetValue(
                key,
                out TrackedState tracked
            )
        )
        {
            tracked.AuthoringData = authoringData;
            tracked.WorldSettings = worldSettings;
            tracked.NotifyPreview = notifyPreview;
            tracked.NotifyRuntime = notifyRuntime;
            return;
        }

        if (
            !TerrainRegionalElevationSnapshot.TryCapture(
                authoringData,
                out TerrainRegionalElevationSnapshot snapshot,
                out _
            )
        )
        {
            return;
        }

        UpdateTrackedState(
            authoringData,
            worldSettings,
            snapshot,
            notifyPreview,
            notifyRuntime
        );
    }

    internal static void UpdateTrackedState(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainRegionalElevationSnapshot snapshot,
        bool notifyPreview,
        bool notifyRuntime
    )
    {
        if (
            authoringData == null
            ||
            worldSettings == null
            ||
            snapshot == null
        )
        {
            return;
        }

        trackedStates[authoringData.GetInstanceID()] =
            new TrackedState
            {
                AuthoringData =
                    authoringData,

                WorldSettings =
                    worldSettings,

                Snapshot =
                    snapshot,

                NotifyPreview =
                    notifyPreview,

                NotifyRuntime =
                    notifyRuntime
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

    private static void OnUndoRedoPerformed()
    {
        lastUndoRedoDirtyTiles.Clear();

        lastUndoRedoDirtyTileCount =
            0;

        lastUndoRedoInvalidationKind =
            "None";

        List<int> keys =
            new List<int>(
                trackedStates.Keys
            );

        for (
            int keyIndex = 0;
            keyIndex < keys.Count;
            keyIndex++
        )
        {
            int key =
                keys[keyIndex];

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
                trackedStates.Remove(
                    key
                );

                continue;
            }

            if (
                !TerrainRegionalElevationSnapshot.TryCapture(
                    authoringData,
                    out TerrainRegionalElevationSnapshot current,
                    out _
                )
            )
            {
                continue;
            }

            bool stateChanged =
                tracked.Snapshot == null
                ||
                !tracked.Snapshot.StateEquals(
                    current
                );

            if (!stateChanged)
            {
                continue;
            }

            bool outputChanged =
                tracked.Snapshot == null
                ||
                !tracked.Snapshot.OutputEquals(
                    current
                );

            tracked.Snapshot =
                current;

            if (!outputChanged)
            {
                continue;
            }

            long logicalAffectedTileCount =
                TerrainRegionalElevationResidencyPolicy
                    .GetLogicalAffectedTileCount(
                        worldSettings,
                        TerrainRegionalElevationInvalidationScope.WholeWorld
                    );

            long combinedCount =
                (long)lastUndoRedoDirtyTileCount +
                logicalAffectedTileCount;

            lastUndoRedoDirtyTileCount =
                TerrainRegionalElevationResidencyPolicy
                    .ClampLogicalCountToInt(
                        combinedCount
                    );

            lastUndoRedoInvalidationKind =
                logicalAffectedTileCount > 0L
                    ? "WholeWorld"
                    : "None";

            if (
                tracked.NotifyRuntime
                &&
                logicalAffectedTileCount > 0L
            )
            {
                TerrainRuntimeInvalidationService
                    .InvalidateGlobalAuthoringHeightOutput(
                        worldSettings,
                        authoringData
                    );
            }

            if (tracked.NotifyPreview)
            {
                TerrainAuthoringPreviewService
                    .NotifyRegionalElevationAuthoringStateChanged(
                        logicalAffectedTileCount > 0L
                            ? TerrainRegionalElevationInvalidationScope.WholeWorld
                            : TerrainRegionalElevationInvalidationScope.None
                    );
            }
        }
    }
}
