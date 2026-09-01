using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    private sealed class InteractiveModifierEditState
    {
        public TerrainAuthoringData AuthoringData;
        public WorldSettings WorldSettings;
        public string StableId;
        public string Operation;
        public int UndoGroup;
        public int RevisionBefore;
        public bool NotifyPreview;
        public string CommittedSignatureBefore;
        public string OverallSignatureBefore;
        public List<TerrainHeightModifierSnapshot> InitialStack;
        public List<TerrainHeightModifierSnapshot> CurrentStack;
    }

    private static InteractiveModifierEditState
        activeInteractiveEdit;

    static TerrainAuthoringModifierService()
    {
        AssemblyReloadEvents.beforeAssemblyReload +=
            OnBeforeAssemblyReload;

        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        EditorApplication.quitting +=
            OnEditorQuitting;
    }

    public static bool HasActiveInteractiveEdit =>
        activeInteractiveEdit != null;

    public static string ActiveInteractiveStableId =>
        activeInteractiveEdit != null
            ? activeInteractiveEdit.StableId
            : "";

    // =====================================================
    // ATOMIC STAMP FOOTPRINT
    // =====================================================

    public static bool SetStampFootprint(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        Vector2 positionXZ,
        Vector2 sizeXZ,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Edit Terrain Stamp Footprint",
                () =>
                {
                    modifier.SetPositionXZInternal(
                        positionXZ
                    );

                    modifier.SetSizeXZInternal(
                        sizeXZ
                    );

                    return true;
                },
                out errorMessage
            );
    }

    // =====================================================
    // INTERACTIVE EDIT TRANSACTION
    // =====================================================

    public static bool BeginInteractiveModifierEdit(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        string undoLabel,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (activeInteractiveEdit != null)
        {
            errorMessage =
                "Another terrain modifier interactive edit is already active.";

            return false;
        }

        if (
            authoringData == null
            ||
            worldSettings == null
        )
        {
            errorMessage =
                "Terrain modifier interactive edit received an invalid context.";

            return false;
        }

        if (
            authoringData.authoringRevision ==
            int.MaxValue
        )
        {
            errorMessage =
                "TerrainAuthoringData.authoringRevision reached Int32.MaxValue.";

            return false;
        }

        if (
            string.IsNullOrEmpty(
                stableId
            )
        )
        {
            errorMessage =
                "Modifier stable ID is empty.";

            return false;
        }

        if (
            !authoringData
                .TryValidateModifierStableIds(
                    out string identityError
                )
            &&
            authoringData.HeightModifierCount >
                0
        )
        {
            errorMessage =
                "Modifier identity state is invalid before interactive edit.\n\n" +
                identityError;

            return false;
        }

        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out _,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        bool notifyPreview =
            ShouldNotifyPreview(
                authoringData
            );

        TerrainAuthoringModifierChangeTracker
            .EnsureTracked(
                authoringData,
                worldSettings,
                notifyPreview
            );

        if (
            !TerrainAuthoringModifierChangeTracker
                .TryCaptureStack(
                    authoringData,
                    out List<TerrainHeightModifierSnapshot> initialStack,
                    out errorMessage
                )
        )
        {
            return false;
        }

        string operation =
            string.IsNullOrWhiteSpace(
                undoLabel
            )
                ? "Edit Terrain Modifier"
                : undoLabel.Trim();

        int revisionBefore =
            authoringData.authoringRevision;

        string committedBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        /*
         * Interactive Scene handles may emit many MouseDrag events.
         * Register one complete-object snapshot at drag start so the
         * whole gesture is represented by one Unity Undo operation.
         */
        Undo.IncrementCurrentGroup();

        int undoGroup =
            Undo.GetCurrentGroup();

        Undo.SetCurrentGroupName(
            operation
        );

        Undo.RegisterCompleteObjectUndo(
            authoringData,
            operation
        );

        activeInteractiveEdit =
            new InteractiveModifierEditState
            {
                AuthoringData =
                    authoringData,

                WorldSettings =
                    worldSettings,

                StableId =
                    stableId,

                Operation =
                    operation,

                UndoGroup =
                    undoGroup,

                RevisionBefore =
                    revisionBefore,

                NotifyPreview =
                    notifyPreview,

                CommittedSignatureBefore =
                    committedBefore,

                OverallSignatureBefore =
                    overallBefore,

                InitialStack =
                    new List<TerrainHeightModifierSnapshot>(
                        initialStack
                    ),

                CurrentStack =
                    new List<TerrainHeightModifierSnapshot>(
                        initialStack
                    )
            };

        return true;
    }

    public static bool UpdateInteractiveStampFootprint(
        Vector2 positionXZ,
        Vector2 sizeXZ,
        out string errorMessage
    )
    {
        errorMessage = "";

        InteractiveModifierEditState state =
            activeInteractiveEdit;

        if (state == null)
        {
            errorMessage =
                "No terrain modifier interactive edit is active.";

            return false;
        }

        if (
            state.AuthoringData == null
            ||
            state.WorldSettings == null
        )
        {
            errorMessage =
                "The active terrain modifier interactive edit lost its context.";

            return false;
        }

        if (
            state.AuthoringData.authoringRevision !=
                state.RevisionBefore
        )
        {
            errorMessage =
                "Terrain authoring revision changed during the interactive edit.";

            return false;
        }

        if (
            !TryFindStampModifier(
                state.AuthoringData,
                state.StableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        Vector2 previousPosition =
            modifier.PositionXZ;

        Vector2 previousSize =
            modifier.SizeXZ;

        modifier.SetPositionXZInternal(
            positionXZ
        );

        modifier.SetSizeXZInternal(
            sizeXZ
        );

        if (
            modifier.PositionXZ ==
                previousPosition
            &&
            modifier.SizeXZ ==
                previousSize
        )
        {
            return true;
        }

        if (
            !TerrainAuthoringModifierChangeTracker
                .TryCaptureStack(
                    state.AuthoringData,
                    out List<TerrainHeightModifierSnapshot> current,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (
            TerrainHeightModifierSnapshot
                .StackEquals(
                    state.CurrentStack,
                    current
                )
        )
        {
            state.CurrentStack =
                current;

            return true;
        }

        HashSet<Vector2Int> dirtyTiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringModifierChangeTracker
            .CollectChangedTiles(
                state.WorldSettings,
                state.CurrentStack,
                current,
                dirtyTiles
            );

        EditorUtility.SetDirty(
            state.AuthoringData
        );

        TerrainAuthoringModifierChangeTracker
            .UpdateTrackedState(
                state.AuthoringData,
                state.WorldSettings,
                current,
                state.NotifyPreview
            );

        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService
                .NotifyCompositeAuthoringStateChanged(
                    dirtyTiles
                );
        }

        state.CurrentStack =
            current;

        return true;
    }

    public static bool CommitInteractiveEdit(
        out string errorMessage
    )
    {
        errorMessage = "";

        InteractiveModifierEditState state =
            activeInteractiveEdit;

        if (state == null)
        {
            errorMessage =
                "No terrain modifier interactive edit is active.";

            return false;
        }

        if (
            state.AuthoringData == null
            ||
            state.WorldSettings == null
        )
        {
            activeInteractiveEdit =
                null;

            errorMessage =
                "The active terrain modifier interactive edit lost its context.";

            return false;
        }

        if (
            state.AuthoringData.authoringRevision !=
                state.RevisionBefore
        )
        {
            errorMessage =
                "Terrain authoring revision changed during the interactive edit.";

            CancelInteractiveEdit(
                out _
            );

            return false;
        }

        if (
            !TerrainAuthoringModifierChangeTracker
                .TryCaptureStack(
                    state.AuthoringData,
                    out List<TerrainHeightModifierSnapshot> finalStack,
                    out errorMessage
                )
        )
        {
            CancelInteractiveEdit(
                out _
            );

            return false;
        }

        bool changed =
            !TerrainHeightModifierSnapshot
                .StackEquals(
                    state.InitialStack,
                    finalStack
                );

        if (!changed)
        {
            int undoGroup =
                state.UndoGroup;

            TerrainAuthoringData authoringData =
                state.AuthoringData;

            WorldSettings worldSettings =
                state.WorldSettings;

            bool notifyPreview =
                state.NotifyPreview;

            string operation =
                state.Operation;

            activeInteractiveEdit =
                null;

            /*
             * Remove the complete-object snapshot instead of leaving a
             * no-op Undo entry when the handle ends where it started.
             */
            Undo.RevertAllDownToGroup(
                undoGroup
            );

            RefreshTrackedStateAfterInteractiveRevert(
                authoringData,
                worldSettings,
                notifyPreview
            );

            SetNoChangeDiagnostics(
                operation,
                authoringData,
                worldSettings
            );

            return true;
        }

        state.AuthoringData.authoringRevision =
            Mathf.Max(
                0,
                state.RevisionBefore
            )
            +
            1;

        EditorUtility.SetDirty(
            state.AuthoringData
        );

        TerrainAuthoringModifierChangeTracker
            .UpdateTrackedState(
                state.AuthoringData,
                state.WorldSettings,
                finalStack,
                state.NotifyPreview
            );

        /*
         * The final drag sample has already recomposed the required
         * slices. Incrementing authoringRevision changes only the
         * overall authoring identity, so acknowledge it without forcing
         * another terrain slice update.
         */
        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService
                .NotifyCompositeAuthoringStateChanged(
                    new HashSet<Vector2Int>()
                );
        }

        HashSet<Vector2Int> logicalDirtyTiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringModifierChangeTracker
            .CollectChangedTiles(
                state.WorldSettings,
                state.InitialStack,
                finalStack,
                logicalDirtyTiles
            );

        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    state.WorldSettings
                );

        string overallAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    state.WorldSettings,
                    state.AuthoringData
                );

        TerrainAuthoringModifierMutationDiagnostics diagnostics =
            new TerrainAuthoringModifierMutationDiagnostics
            {
                Operation =
                    state.Operation,

                Changed =
                    true,

                RevisionBefore =
                    state.RevisionBefore,

                RevisionAfter =
                    state.AuthoringData.authoringRevision,

                CommittedSignatureBefore =
                    state.CommittedSignatureBefore,

                CommittedSignatureAfter =
                    committedAfter,

                OverallSignatureBefore =
                    state.OverallSignatureBefore,

                OverallSignatureAfter =
                    overallAfter,

                PreviewNotificationMode =
                    state.NotifyPreview
                        ? (
                            logicalDirtyTiles.Count > 0
                                ? "InteractiveDirtyTiles"
                                : "InteractiveMetadataOnly"
                        )
                        : "Suppressed"
            };

        diagnostics.SetDirtyTiles(
            logicalDirtyTiles
        );

        LastMutationDiagnostics =
            diagnostics;

        int committedUndoGroup =
            state.UndoGroup;

        activeInteractiveEdit =
            null;

        Undo.CollapseUndoOperations(
            committedUndoGroup
        );

        return true;
    }

    public static bool CancelInteractiveEdit(
        out string errorMessage
    )
    {
        errorMessage = "";

        InteractiveModifierEditState state =
            activeInteractiveEdit;

        if (state == null)
        {
            return true;
        }

        TerrainAuthoringData authoringData =
            state.AuthoringData;

        WorldSettings worldSettings =
            state.WorldSettings;

        bool notifyPreview =
            state.NotifyPreview;

        int undoGroup =
            state.UndoGroup;

        string operation =
            state.Operation;

        activeInteractiveEdit =
            null;

        if (
            authoringData == null
            ||
            worldSettings == null
        )
        {
            errorMessage =
                "The active terrain modifier interactive edit lost its context.";

            return false;
        }

        /*
         * Restore the complete-object snapshot captured at MouseDown.
         * RevertAllDownToGroup intentionally does not create a Redo item,
         * which is the expected behavior for Escape/cancel.
         *
         * TerrainAuthoringModifierChangeTracker's existing Undo callback
         * sees the final-drag snapshot -> restored snapshot transition and
         * refreshes the correct dirty terrain region.
         */
        Undo.RevertAllDownToGroup(
            undoGroup
        );

        RefreshTrackedStateAfterInteractiveRevert(
            authoringData,
            worldSettings,
            notifyPreview
        );

        SetNoChangeDiagnostics(
            operation + " (Canceled)",
            authoringData,
            worldSettings
        );

        return true;
    }

    // =====================================================
    // INTERACTIVE LIFECYCLE SAFETY
    // =====================================================

    private static void OnBeforeAssemblyReload()
    {
        CancelInteractiveEdit(
            out _
        );
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        if (
            state ==
                PlayModeStateChange.ExitingEditMode
            ||
            state ==
                PlayModeStateChange.EnteredPlayMode
        )
        {
            CancelInteractiveEdit(
                out _
            );
        }
    }

    private static void OnEditorQuitting()
    {
        CancelInteractiveEdit(
            out _
        );
    }

    private static void RefreshTrackedStateAfterInteractiveRevert(
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

        if (
            TerrainAuthoringModifierChangeTracker
                .TryCaptureStack(
                    authoringData,
                    out List<TerrainHeightModifierSnapshot> restored,
                    out _
                )
        )
        {
            TerrainAuthoringModifierChangeTracker
                .UpdateTrackedState(
                    authoringData,
                    worldSettings,
                    restored,
                    notifyPreview
                );
        }
    }
}
