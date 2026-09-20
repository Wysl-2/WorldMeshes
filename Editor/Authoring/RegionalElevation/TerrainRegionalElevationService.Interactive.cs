using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class TerrainRegionalElevationService
{
    /*
     * Regional node interpolation currently dirties the complete logical
     * height world. Re-requesting that GPU composition for every SceneView
     * mouse sample makes the handle itself contend with terrain recomposition,
     * especially as the node count grows. Keep node state updates unthrottled
     * while limiting live terrain preview requests to a useful interactive
     * cadence. Commit/cancel always force the final/restored preview state.
     */
    private const double InteractivePreviewMinimumIntervalSeconds =
        0.1;

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
        public bool HasInteractiveChanges;
        public bool PreviewRefreshPending;
        public bool HasPreviewNotification;
        public double LastPreviewNotificationTime;
        public string CommittedSignatureBefore;
        public string OverallSignatureBefore;
        public TerrainRegionalElevationSnapshot InitialSnapshot;

        public long LogicalAffectedTileCount;
    }

    private static InteractiveNodeEditState activeInteractiveEdit;
    private static int lastInteractivePreviewDirtyTileCount;
    private static int lastInteractivePreviewNotificationCount;
    private static int lastInteractiveRuntimeInvalidationCount;

    static TerrainRegionalElevationService()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += OnEditorQuitting;
    }

    public static bool HasActiveInteractiveEdit => activeInteractiveEdit != null;
    public static string ActiveInteractiveNodeStableId =>
        activeInteractiveEdit != null ? activeInteractiveEdit.StableId : "";

    internal static int ActiveInteractiveDirtyTileCount =>
        activeInteractiveEdit != null
            ? TerrainRegionalElevationResidencyPolicy
                .ClampLogicalCountToInt(
                    activeInteractiveEdit.LogicalAffectedTileCount
                )
            : 0;

    internal static bool ActiveInteractiveHasChanges =>
        activeInteractiveEdit != null &&
        activeInteractiveEdit.HasInteractiveChanges;

    internal static int LastInteractivePreviewDirtyTileCount =>
        lastInteractivePreviewDirtyTileCount;

    internal static int LastInteractivePreviewNotificationCount =>
        lastInteractivePreviewNotificationCount;

    internal static int LastInteractiveRuntimeInvalidationCount =>
        lastInteractiveRuntimeInvalidationCount;

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

        InteractiveNodeEditState state =
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

        if (!string.IsNullOrEmpty(committedBefore))
        {
            state.LogicalAffectedTileCount =
                TerrainRegionalElevationResidencyPolicy
                    .GetLogicalAffectedTileCount(
                        worldSettings,
                        TerrainRegionalElevationInvalidationScope.WholeWorld
                    );
        }

        activeInteractiveEdit = state;
        lastInteractivePreviewDirtyTileCount = 0;
        lastInteractivePreviewNotificationCount = 0;
        lastInteractiveRuntimeInvalidationCount = 0;

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

        if (!TryGetActiveInteractiveNode(
            out InteractiveNodeEditState state,
            out TerrainElevationNode node,
            out errorMessage))
        {
            return false;
        }

        if (node.PositionXZ == positionXZ)
        {
            return true;
        }

        node.SetPositionXZInternal(positionXZ);
        NotifyInteractiveNodeChanged(state);
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

        if (!TryGetActiveInteractiveNode(
            out InteractiveNodeEditState state,
            out TerrainElevationNode node,
            out errorMessage))
        {
            return false;
        }

        if (node.Elevation == elevation)
        {
            return true;
        }

        node.SetElevationInternal(elevation);
        NotifyInteractiveNodeChanged(state);
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

        if (!TryGetActiveInteractiveNode(
            out InteractiveNodeEditState state,
            out TerrainElevationNode node,
            out errorMessage))
        {
            return false;
        }

        if (node.PositionXZ == positionXZ && node.Elevation == elevation)
        {
            return true;
        }

        node.SetPositionXZInternal(positionXZ);
        node.SetElevationInternal(elevation);
        NotifyInteractiveNodeChanged(state);
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
            bool hadInteractiveChanges = state.HasInteractiveChanges;

            activeInteractiveEdit = null;
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

            if (
                notifyPreview &&
                hadInteractiveChanges &&
                state.LogicalAffectedTileCount > 0L)
            {
                /*
                 * The gesture may have returned to its starting value after a
                 * throttled preview sample. Recompose the restored regional
                 * state across the current active residency.
                 */
                TerrainAuthoringPreviewService
                    .NotifyRegionalElevationAuthoringStateChanged(
                        TerrainRegionalElevationInvalidationScope.WholeWorld
                    );
            }

            SetNoChangeDiagnostics(operation, data, world);
            return true;
        }

        /*
         * If the most recent mouse sample was throttled, make sure the final
         * node value has a whole-world preview request queued before the
         * revision/signature acknowledgement below.
         */
        TryNotifyInteractivePreview(
            state,
            true);

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

        TerrainRegionalElevationChangeTracker.UpdateTrackedState(
            state.AuthoringData,
            state.WorldSettings,
            finalSnapshot,
            state.NotifyPreview,
            state.NotifyRuntime);

        if (
            state.NotifyRuntime
            &&
            state.LogicalAffectedTileCount > 0L
        )
        {
            TerrainRuntimeInvalidationService
                .InvalidateGlobalAuthoringHeightOutput(
                    state.WorldSettings,
                    state.AuthoringData
                );

            lastInteractiveRuntimeInvalidationCount++;
        }

        /*
         * Live drag samples already requested the final preview pixels. Once
         * revision advances, only acknowledge the new overall authoring
         * identity instead of redundantly scheduling resident tiles again.
         */
        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService
                .NotifyRegionalElevationAuthoringStateChanged(
                    TerrainRegionalElevationInvalidationScope.None
                );
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
                    ? "InteractiveRegionalWholeWorldThenMetadata"
                    : "Suppressed",
                RuntimeInvalidationMode = state.NotifyRuntime
                    ? "GlobalHeightOutput"
                    : "Suppressed",
                RegionalInvalidationKind = state.LogicalAffectedTileCount > 0L
                    ? "WholeWorld"
                    : "None",
                LogicalAffectedTileCount = state.LogicalAffectedTileCount,
                NodeCountBefore = state.InitialSnapshot.NodeCount,
                NodeCountAfter = finalSnapshot.NodeCount
            };

        diagnostics.SetLogicalDirtyTileCount(
            state.LogicalAffectedTileCount
        );

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
        bool hadInteractiveChanges = state.HasInteractiveChanges;

        activeInteractiveEdit = null;

        if (data == null || world == null)
        {
            errorMessage = "The active regional elevation interactive edit lost its context.";
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

        if (
            notifyPreview &&
            hadInteractiveChanges &&
            state.LogicalAffectedTileCount > 0L)
        {
            TerrainAuthoringPreviewService
                .NotifyRegionalElevationAuthoringStateChanged(
                    TerrainRegionalElevationInvalidationScope.WholeWorld
                );
        }

        SetNoChangeDiagnostics(operation, data, world);
        return true;
    }

    private static void NotifyInteractiveNodeChanged(
        InteractiveNodeEditState state)
    {
        if (state == null || state.AuthoringData == null)
        {
            return;
        }

        state.HasInteractiveChanges = true;
        state.PreviewRefreshPending = true;
        EditorUtility.SetDirty(state.AuthoringData);

        lastInteractivePreviewDirtyTileCount =
            TerrainRegionalElevationResidencyPolicy
                .ClampLogicalCountToInt(
                    state.LogicalAffectedTileCount
                );

        TryNotifyInteractivePreview(
            state,
            false);
    }

    private static bool TryNotifyInteractivePreview(
        InteractiveNodeEditState state,
        bool force)
    {
        if (
            state == null ||
            !state.NotifyPreview ||
            !state.PreviewRefreshPending ||
            state.LogicalAffectedTileCount <= 0L)
        {
            return false;
        }

        double now =
            EditorApplication.timeSinceStartup;

        if (
            !force &&
            state.HasPreviewNotification &&
            now - state.LastPreviewNotificationTime <
                InteractivePreviewMinimumIntervalSeconds)
        {
            return false;
        }

        TerrainAuthoringPreviewService
            .NotifyRegionalElevationAuthoringStateChanged(
                TerrainRegionalElevationInvalidationScope.WholeWorld
            );

        state.PreviewRefreshPending = false;
        state.HasPreviewNotification = true;
        state.LastPreviewNotificationTime = now;
        lastInteractivePreviewNotificationCount++;
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
