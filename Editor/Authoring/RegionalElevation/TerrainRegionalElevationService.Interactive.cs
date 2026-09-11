using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class TerrainRegionalElevationService
{
    private sealed class InteractiveNodeEditState
    {
        public TerrainAuthoringData AuthoringData;
        public WorldSettings WorldSettings;
        public string StableId;
        public string Operation;
        public int UndoGroup;
        public int RevisionBefore;
        public bool NotifyPreview;
        public bool NotifyRuntime;
        public string CommittedSignatureBefore;
        public string OverallSignatureBefore;
        public TerrainRegionalElevationSnapshot InitialSnapshot;
    }

    private static InteractiveNodeEditState activeInteractiveEdit;

    static TerrainRegionalElevationService()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += OnEditorQuitting;
    }

    public static bool HasActiveInteractiveEdit => activeInteractiveEdit != null;
    public static string ActiveInteractiveNodeStableId =>
        activeInteractiveEdit != null ? activeInteractiveEdit.StableId : "";

    public static bool BeginInteractiveNodeEdit(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        string undoLabel,
        out string errorMessage)
    {
        errorMessage = "";

        if (activeInteractiveEdit != null)
        {
            errorMessage = "Another regional elevation interactive edit is already active.";
            return false;
        }

        if (authoringData == null || worldSettings == null)
        {
            errorMessage = "Regional elevation interactive edit received an invalid context.";
            return false;
        }

        if (!TryFindNode(authoringData, stableId, out _, out _, out _, out errorMessage))
        {
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
                ? "Edit Regional Elevation Node"
                : undoLabel.Trim();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(operation);
        Undo.RegisterCompleteObjectUndo(authoringData, operation);

        activeInteractiveEdit =
            new InteractiveNodeEditState
            {
                AuthoringData = authoringData,
                WorldSettings = worldSettings,
                StableId = stableId,
                Operation = operation,
                UndoGroup = undoGroup,
                RevisionBefore = authoringData.authoringRevision,
                NotifyPreview = notifyPreview,
                NotifyRuntime = notifyRuntime,
                CommittedSignatureBefore = committedBefore,
                OverallSignatureBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData),
                InitialSnapshot = initialSnapshot
            };

        return true;
    }

    public static bool UpdateInteractiveNodePosition(
        Vector2 positionXZ,
        out string errorMessage)
    {
        if (!ValidateFinite(positionXZ, 0f, out errorMessage))
        {
            return false;
        }

        if (!TryGetActiveInteractiveNode(out _, out TerrainElevationNode node, out errorMessage))
        {
            return false;
        }

        if (node.PositionXZ != positionXZ)
        {
            node.SetPositionXZInternal(positionXZ);
            EditorUtility.SetDirty(activeInteractiveEdit.AuthoringData);
        }

        return true;
    }

    public static bool UpdateInteractiveNodeElevation(
        float elevation,
        out string errorMessage)
    {
        errorMessage = "";
        if (!IsFinite(elevation))
        {
            errorMessage = "Regional elevation node height must be finite.";
            return false;
        }

        if (!TryGetActiveInteractiveNode(out _, out TerrainElevationNode node, out errorMessage))
        {
            return false;
        }

        if (node.Elevation != elevation)
        {
            node.SetElevationInternal(elevation);
            EditorUtility.SetDirty(activeInteractiveEdit.AuthoringData);
        }

        return true;
    }

    public static bool UpdateInteractiveNode(
        Vector2 positionXZ,
        float elevation,
        out string errorMessage)
    {
        if (!ValidateFinite(positionXZ, elevation, out errorMessage))
        {
            return false;
        }

        if (!TryGetActiveInteractiveNode(out _, out TerrainElevationNode node, out errorMessage))
        {
            return false;
        }

        if (node.PositionXZ != positionXZ || node.Elevation != elevation)
        {
            node.SetPositionXZInternal(positionXZ);
            node.SetElevationInternal(elevation);
            EditorUtility.SetDirty(activeInteractiveEdit.AuthoringData);
        }

        return true;
    }

    public static bool CommitInteractiveNodeEdit(out string errorMessage)
    {
        errorMessage = "";
        InteractiveNodeEditState state = activeInteractiveEdit;

        if (state == null)
        {
            errorMessage = "No regional elevation interactive edit is active.";
            return false;
        }

        if (state.AuthoringData == null || state.WorldSettings == null)
        {
            activeInteractiveEdit = null;
            errorMessage = "The active regional elevation interactive edit lost its context.";
            return false;
        }

        if (state.AuthoringData.authoringRevision != state.RevisionBefore)
        {
            errorMessage = "Terrain authoring revision changed during the regional interactive edit.";
            CancelInteractiveNodeEdit(out _);
            return false;
        }

        if (!ValidateResultingNodeSource(state.AuthoringData, out errorMessage) ||
            !TerrainRegionalElevationSnapshot.TryCapture(
                state.AuthoringData,
                out TerrainRegionalElevationSnapshot finalSnapshot,
                out errorMessage))
        {
            CancelInteractiveNodeEdit(out _);
            return false;
        }

        if (state.InitialSnapshot.StateEquals(finalSnapshot))
        {
            int noOpGroup = state.UndoGroup;
            TerrainAuthoringData data = state.AuthoringData;
            WorldSettings world = state.WorldSettings;
            string operation = state.Operation;
            bool notifyPreview = state.NotifyPreview;
            bool notifyRuntime = state.NotifyRuntime;

            activeInteractiveEdit = null;
            Undo.RevertAllDownToGroup(noOpGroup);

            if (TerrainRegionalElevationSnapshot.TryCapture(data, out TerrainRegionalElevationSnapshot restored, out _))
            {
                TerrainRegionalElevationChangeTracker.UpdateTrackedState(
                    data,
                    world,
                    restored,
                    notifyPreview,
                    notifyRuntime);
            }

            SetNoChangeDiagnostics(operation, data, world);
            return true;
        }

        bool revisionWillAdvance =
            !string.IsNullOrEmpty(state.CommittedSignatureBefore);

        if (revisionWillAdvance && state.RevisionBefore == int.MaxValue)
        {
            errorMessage = "TerrainAuthoringData.authoringRevision reached Int32.MaxValue.";
            CancelInteractiveNodeEdit(out _);
            return false;
        }

        if (revisionWillAdvance)
        {
            state.AuthoringData.authoringRevision =
                Mathf.Max(0, state.RevisionBefore) + 1;
        }

        EditorUtility.SetDirty(state.AuthoringData);

        HashSet<Vector2Int> dirtyTiles = new HashSet<Vector2Int>();
        if (!string.IsNullOrEmpty(state.CommittedSignatureBefore))
        {
            TerrainRegionalElevationCompositionUtility.CollectAllHeightTiles(
                state.WorldSettings,
                dirtyTiles);
        }

        TerrainRegionalElevationChangeTracker.UpdateTrackedState(
            state.AuthoringData,
            state.WorldSettings,
            finalSnapshot,
            state.NotifyPreview,
            state.NotifyRuntime);

        if (state.NotifyRuntime && dirtyTiles.Count > 0)
        {
            TerrainRuntimeInvalidationService.InvalidateAuthoringHeightTiles(
                state.WorldSettings,
                state.AuthoringData,
                dirtyTiles);
        }

        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService.NotifyCompositeAuthoringStateChanged(dirtyTiles);
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
                PreviewNotificationMode = state.NotifyPreview ? "InteractiveCommitWholeWorld" : "Suppressed",
                RuntimeInvalidationMode = state.NotifyRuntime ? "InteractiveCommitWholeWorld" : "Suppressed",
                NodeCountBefore = state.InitialSnapshot.NodeCount,
                NodeCountAfter = finalSnapshot.NodeCount
            };

        diagnostics.SetDirtyTiles(dirtyTiles);
        LastMutationDiagnostics = diagnostics;

        int committedGroup = state.UndoGroup;
        activeInteractiveEdit = null;
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(committedGroup);
        return true;
    }

    public static bool CancelInteractiveNodeEdit(out string errorMessage)
    {
        errorMessage = "";
        InteractiveNodeEditState state = activeInteractiveEdit;
        if (state == null)
        {
            return true;
        }

        TerrainAuthoringData data = state.AuthoringData;
        WorldSettings world = state.WorldSettings;
        int undoGroup = state.UndoGroup;
        bool notifyPreview = state.NotifyPreview;
        bool notifyRuntime = state.NotifyRuntime;
        string operation = state.Operation;

        activeInteractiveEdit = null;

        if (data == null || world == null)
        {
            errorMessage = "The active regional elevation interactive edit lost its context.";
            return false;
        }

        Undo.RevertAllDownToGroup(undoGroup);

        if (TerrainRegionalElevationSnapshot.TryCapture(data, out TerrainRegionalElevationSnapshot restored, out _))
        {
            TerrainRegionalElevationChangeTracker.UpdateTrackedState(
                data,
                world,
                restored,
                notifyPreview,
                notifyRuntime);
        }

        SetNoChangeDiagnostics(operation, data, world);
        return true;
    }

    private static bool TryGetActiveInteractiveNode(
        out InteractiveNodeEditState state,
        out TerrainElevationNode node,
        out string errorMessage)
    {
        state = activeInteractiveEdit;
        node = null;
        errorMessage = "";

        if (state == null)
        {
            errorMessage = "No regional elevation interactive edit is active.";
            return false;
        }

        if (state.AuthoringData == null || state.WorldSettings == null)
        {
            errorMessage = "The active regional elevation interactive edit lost its context.";
            return false;
        }

        return TryFindNode(
            state.AuthoringData,
            state.StableId,
            out _,
            out node,
            out _,
            out errorMessage);
    }

    private static void OnBeforeAssemblyReload()
    {
        if (activeInteractiveEdit != null)
        {
            CancelInteractiveNodeEdit(out _);
        }
    }

    private static void OnEditorQuitting()
    {
        if (activeInteractiveEdit != null)
        {
            CancelInteractiveNodeEdit(out _);
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode && activeInteractiveEdit != null)
        {
            CancelInteractiveNodeEdit(out _);
        }
    }
}
