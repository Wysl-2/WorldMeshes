using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class TerrainRegionalElevationService
{
    private sealed class InteractiveGroupNodeStart
    {
        public string StableId;
        public Vector2 PositionXZ;
        public float Elevation;
    }

    private sealed class InteractiveNodeGroupEditState
    {
        public TerrainAuthoringData AuthoringData;
        public WorldSettings WorldSettings;
        public string Operation;
        public int UndoGroup;
        public int RevisionBefore;
        public bool NotifyPreview;
        public bool NotifyRuntime;
        public bool HasInteractiveChanges;
        public bool PreviewRefreshPending;
        public bool HasPreviewNotification;
        public double LastPreviewNotificationTime;
        public string CommittedSignatureBefore;
        public string OverallSignatureBefore;
        public TerrainRegionalElevationSnapshot InitialSnapshot;

        public readonly List<InteractiveGroupNodeStart> Nodes =
            new List<InteractiveGroupNodeStart>();

        public readonly HashSet<Vector2Int> InteractiveDirtyTiles =
            new HashSet<Vector2Int>();
    }

    private static InteractiveNodeGroupEditState activeInteractiveGroupEdit;

    public static bool HasActiveInteractiveGroupEdit =>
        activeInteractiveGroupEdit != null;

    public static int ActiveInteractiveGroupNodeCount =>
        activeInteractiveGroupEdit != null
            ? activeInteractiveGroupEdit.Nodes.Count
            : 0;

    public static void CopyActiveInteractiveGroupStableIds(ICollection<string> output)
    {
        if (output == null || activeInteractiveGroupEdit == null)
        {
            return;
        }

        for (int index = 0; index < activeInteractiveGroupEdit.Nodes.Count; index++)
        {
            output.Add(activeInteractiveGroupEdit.Nodes[index].StableId);
        }
    }

    public static bool BeginInteractiveNodeGroupEdit(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        IEnumerable<string> stableIds,
        string undoLabel,
        out string errorMessage)
    {
        errorMessage = "";

        if (HasActiveInteractiveEdit)
        {
            errorMessage = "Another regional elevation interactive edit is already active.";
            return false;
        }

        if (authoringData == null || worldSettings == null)
        {
            errorMessage = "Regional elevation group interactive edit received an invalid context.";
            return false;
        }

        List<TerrainElevationNode> nodes = new List<TerrainElevationNode>();
        if (!TryResolveUniqueNodes(authoringData, stableIds, nodes, out errorMessage))
        {
            return false;
        }

        if (nodes.Count < 2)
        {
            errorMessage = "Regional elevation group interactive editing requires at least two nodes.";
            return false;
        }

        if (!TerrainRegionalElevationSnapshot.TryCapture(
            authoringData,
            out TerrainRegionalElevationSnapshot initialSnapshot,
            out errorMessage))
        {
            return false;
        }

        string committedBefore =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);

        bool liveDerivedState =
            IsLiveAuthoringContext(authoringData, worldSettings) &&
            !string.IsNullOrEmpty(committedBefore);

        bool notifyPreview = liveDerivedState && TerrainAuthoringPreviewService.Enabled;
        bool notifyRuntime = liveDerivedState;

        TerrainRegionalElevationChangeTracker.EnsureTracked(
            authoringData,
            worldSettings,
            notifyPreview,
            notifyRuntime);

        string operation =
            string.IsNullOrWhiteSpace(undoLabel)
                ? "Move Regional Elevation Nodes"
                : undoLabel.Trim();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(operation);
        Undo.RegisterCompleteObjectUndo(authoringData, operation);

        InteractiveNodeGroupEditState state =
            new InteractiveNodeGroupEditState
            {
                AuthoringData = authoringData,
                WorldSettings = worldSettings,
                Operation = operation,
                UndoGroup = undoGroup,
                RevisionBefore = authoringData.authoringRevision,
                NotifyPreview = notifyPreview,
                NotifyRuntime = notifyRuntime,
                HasInteractiveChanges = false,
                PreviewRefreshPending = false,
                HasPreviewNotification = false,
                LastPreviewNotificationTime = double.NegativeInfinity,
                CommittedSignatureBefore = committedBefore,
                OverallSignatureBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData),
                InitialSnapshot = initialSnapshot
            };

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            state.Nodes.Add(
                new InteractiveGroupNodeStart
                {
                    StableId = node.StableId,
                    PositionXZ = node.PositionXZ,
                    Elevation = node.Elevation
                });
        }

        if (!string.IsNullOrEmpty(committedBefore))
        {
            TerrainRegionalElevationCompositionUtility.CollectAllHeightTiles(
                worldSettings,
                state.InteractiveDirtyTiles);
        }

        activeInteractiveGroupEdit = state;
        lastInteractivePreviewDirtyTileCount = 0;
        lastInteractivePreviewNotificationCount = 0;
        lastInteractiveRuntimeInvalidationCount = 0;
        return true;
    }

    public static bool UpdateInteractiveNodeGroupPositionDelta(
        Vector2 deltaXZ,
        out string errorMessage)
    {
        errorMessage = "";
        if (!ValidateFinite(deltaXZ, 0f, out errorMessage))
        {
            return false;
        }

        InteractiveNodeGroupEditState state = activeInteractiveGroupEdit;
        if (state == null)
        {
            errorMessage = "No regional elevation group interactive edit is active.";
            return false;
        }

        if (state.AuthoringData == null || state.WorldSettings == null)
        {
            errorMessage = "The active regional elevation group edit lost its context.";
            return false;
        }

        TerrainElevationNode[] resolved = new TerrainElevationNode[state.Nodes.Count];
        Vector2[] targets = new Vector2[state.Nodes.Count];
        bool anyChanged = false;

        for (int index = 0; index < state.Nodes.Count; index++)
        {
            InteractiveGroupNodeStart start = state.Nodes[index];
            if (!TryFindNode(
                state.AuthoringData,
                start.StableId,
                out _,
                out TerrainElevationNode node,
                out _,
                out errorMessage))
            {
                return false;
            }

            Vector2 target = start.PositionXZ + deltaXZ;
            if (!ValidateFinite(target, start.Elevation, out errorMessage))
            {
                errorMessage = "Regional elevation group drag would produce a non-finite node position.";
                return false;
            }

            resolved[index] = node;
            targets[index] = target;
            anyChanged |= node.PositionXZ != target;
        }

        if (!anyChanged)
        {
            return true;
        }

        for (int index = 0; index < resolved.Length; index++)
        {
            resolved[index].SetPositionXZInternal(targets[index]);
        }

        NotifyInteractiveGroupChanged(state);
        return true;
    }

    public static bool CommitInteractiveNodeGroupEdit(out string errorMessage)
    {
        errorMessage = "";
        InteractiveNodeGroupEditState state = activeInteractiveGroupEdit;
        if (state == null)
        {
            errorMessage = "No regional elevation group interactive edit is active.";
            return false;
        }

        if (state.AuthoringData == null || state.WorldSettings == null)
        {
            activeInteractiveGroupEdit = null;
            errorMessage = "The active regional elevation group edit lost its context.";
            return false;
        }

        if (state.AuthoringData.authoringRevision != state.RevisionBefore)
        {
            errorMessage = "Terrain authoring revision changed during the regional group edit.";
            CancelInteractiveNodeGroupEdit(out _);
            return false;
        }

        if (!ValidateResultingNodeSource(state.AuthoringData, out errorMessage) ||
            !TerrainRegionalElevationSnapshot.TryCapture(
                state.AuthoringData,
                out TerrainRegionalElevationSnapshot finalSnapshot,
                out errorMessage))
        {
            CancelInteractiveNodeGroupEdit(out _);
            return false;
        }

        if (state.InitialSnapshot.StateEquals(finalSnapshot))
        {
            int noOpGroup = state.UndoGroup;
            TerrainAuthoringData data = state.AuthoringData;
            WorldSettings world = state.WorldSettings;
            bool notifyPreview = state.NotifyPreview;
            bool notifyRuntime = state.NotifyRuntime;
            bool hadChanges = state.HasInteractiveChanges;
            string operation = state.Operation;

            activeInteractiveGroupEdit = null;
            Undo.RevertAllDownToGroup(noOpGroup);

            if (TerrainRegionalElevationSnapshot.TryCapture(
                data,
                out TerrainRegionalElevationSnapshot restored,
                out _))
            {
                TerrainRegionalElevationChangeTracker.UpdateTrackedState(
                    data,
                    world,
                    restored,
                    notifyPreview,
                    notifyRuntime);
            }

            if (notifyPreview && hadChanges && state.InteractiveDirtyTiles.Count > 0)
            {
                TerrainAuthoringPreviewService.NotifyCompositeAuthoringStateChanged(
                    state.InteractiveDirtyTiles);
            }

            SetNoChangeDiagnostics(operation, data, world);
            return true;
        }

        TryNotifyInteractiveGroupPreview(state, true);

        bool revisionWillAdvance =
            !string.IsNullOrEmpty(state.CommittedSignatureBefore);

        if (revisionWillAdvance && state.RevisionBefore == int.MaxValue)
        {
            errorMessage = "TerrainAuthoringData.authoringRevision reached Int32.MaxValue.";
            CancelInteractiveNodeGroupEdit(out _);
            return false;
        }

        if (revisionWillAdvance)
        {
            state.AuthoringData.authoringRevision =
                Mathf.Max(0, state.RevisionBefore) + 1;
        }

        EditorUtility.SetDirty(state.AuthoringData);

        TerrainRegionalElevationChangeTracker.UpdateTrackedState(
            state.AuthoringData,
            state.WorldSettings,
            finalSnapshot,
            state.NotifyPreview,
            state.NotifyRuntime);

        if (state.NotifyRuntime && state.InteractiveDirtyTiles.Count > 0)
        {
            TerrainRuntimeInvalidationService.InvalidateAuthoringHeightTiles(
                state.WorldSettings,
                state.AuthoringData,
                state.InteractiveDirtyTiles);
            lastInteractiveRuntimeInvalidationCount++;
        }

        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService.NotifyCompositeAuthoringStateChanged(null);
        }

        string committedAfter =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(state.WorldSettings);
        string overallAfter =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                state.WorldSettings,
                state.AuthoringData);

        TerrainRegionalElevationMutationDiagnostics diagnostics =
            new TerrainRegionalElevationMutationDiagnostics
            {
                Operation = state.Operation,
                Changed = true,
                RevisionBefore = state.RevisionBefore,
                RevisionAfter = state.AuthoringData.authoringRevision,
                CommittedSignatureBefore = state.CommittedSignatureBefore,
                CommittedSignatureAfter = committedAfter,
                OverallSignatureBefore = state.OverallSignatureBefore,
                OverallSignatureAfter = overallAfter,
                PreviewNotificationMode = state.NotifyPreview
                    ? "InteractiveGroupLivePreviewThenMetadata"
                    : "Suppressed",
                RuntimeInvalidationMode = state.NotifyRuntime
                    ? "InteractiveGroupCommitWholeWorld"
                    : "Suppressed",
                NodeCountBefore = state.InitialSnapshot.NodeCount,
                NodeCountAfter = finalSnapshot.NodeCount
            };

        diagnostics.SetDirtyTiles(state.InteractiveDirtyTiles);
        LastMutationDiagnostics = diagnostics;

        int committedGroup = state.UndoGroup;
        activeInteractiveGroupEdit = null;
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(committedGroup);
        return true;
    }

    public static bool CancelInteractiveNodeGroupEdit(out string errorMessage)
    {
        errorMessage = "";
        InteractiveNodeGroupEditState state = activeInteractiveGroupEdit;
        if (state == null)
        {
            return true;
        }

        TerrainAuthoringData data = state.AuthoringData;
        WorldSettings world = state.WorldSettings;
        int undoGroup = state.UndoGroup;
        bool notifyPreview = state.NotifyPreview;
        bool notifyRuntime = state.NotifyRuntime;
        bool hadChanges = state.HasInteractiveChanges;
        string operation = state.Operation;

        activeInteractiveGroupEdit = null;

        if (data == null || world == null)
        {
            errorMessage = "The active regional elevation group edit lost its context.";
            return false;
        }

        Undo.RevertAllDownToGroup(undoGroup);

        if (TerrainRegionalElevationSnapshot.TryCapture(
            data,
            out TerrainRegionalElevationSnapshot restored,
            out _))
        {
            TerrainRegionalElevationChangeTracker.UpdateTrackedState(
                data,
                world,
                restored,
                notifyPreview,
                notifyRuntime);
        }

        if (notifyPreview && hadChanges && state.InteractiveDirtyTiles.Count > 0)
        {
            TerrainAuthoringPreviewService.NotifyCompositeAuthoringStateChanged(
                state.InteractiveDirtyTiles);
        }

        SetNoChangeDiagnostics(operation, data, world);
        return true;
    }

    private static void NotifyInteractiveGroupChanged(InteractiveNodeGroupEditState state)
    {
        if (state == null || state.AuthoringData == null)
        {
            return;
        }

        state.HasInteractiveChanges = true;
        state.PreviewRefreshPending = true;
        EditorUtility.SetDirty(state.AuthoringData);
        lastInteractivePreviewDirtyTileCount = state.InteractiveDirtyTiles.Count;
        TryNotifyInteractiveGroupPreview(state, false);
    }

    private static bool TryNotifyInteractiveGroupPreview(
        InteractiveNodeGroupEditState state,
        bool force)
    {
        if (state == null ||
            !state.NotifyPreview ||
            !state.PreviewRefreshPending ||
            state.InteractiveDirtyTiles.Count <= 0)
        {
            return false;
        }

        double now = EditorApplication.timeSinceStartup;
        if (!force &&
            state.HasPreviewNotification &&
            now - state.LastPreviewNotificationTime < InteractivePreviewMinimumIntervalSeconds)
        {
            return false;
        }

        TerrainAuthoringPreviewService.NotifyCompositeAuthoringStateChanged(
            state.InteractiveDirtyTiles);

        state.PreviewRefreshPending = false;
        state.HasPreviewNotification = true;
        state.LastPreviewNotificationTime = now;
        lastInteractivePreviewNotificationCount++;
        return true;
    }
}
