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
        public Bounds InitialAffectedWorldBounds;
        public Bounds CurrentAffectedWorldBounds;
        public bool InitialEnabled;
        public bool CurrentEnabled;
        public bool HasInteractiveChanges;

        /*
         * Reused for every MouseDrag sample so interactive modifier updates do
         * not allocate a new dirty-tile set on every pointer movement.
         */
        public readonly HashSet<Vector2Int>
            InteractiveDirtyTiles =
                new HashSet<Vector2Int>();
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
                out TerrainHeightModifier activeModifier,
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

        /*
         * Keep the general Undo/Redo tracker anchored to the authoritative
         * state at MouseDown. Interactive MouseDrag samples deliberately do
         * not advance this tracker; Commit/Cancel resynchronize it once.
         */
        TerrainAuthoringModifierChangeTracker
            .UpdateTrackedState(
                authoringData,
                worldSettings,
                initialStack,
                notifyPreview
            );

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

        Bounds initialBounds =
            activeModifier.GetAffectedWorldBounds();

        bool initialEnabled =
            activeModifier.Enabled;

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
                    initialStack,

                InitialAffectedWorldBounds =
                    initialBounds,

                CurrentAffectedWorldBounds =
                    initialBounds,

                InitialEnabled =
                    initialEnabled,

                CurrentEnabled =
                    initialEnabled,

                HasInteractiveChanges =
                    false
            };

        return true;
    }

    public static bool UpdateInteractiveStampFootprint(
        Vector2 positionXZ,
        Vector2 sizeXZ,
        out string errorMessage
    )
    {
        if (
            !TryGetActiveInteractiveStamp(
                out InteractiveModifierEditState state,
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

        NotifyInteractiveModifierChanged(
            state,
            modifier
        );

        return true;
    }

    public static bool UpdateInteractiveStampHeightDelta(
        float heightDelta,
        out string errorMessage
    )
    {
        if (
            !TryGetActiveInteractiveStamp(
                out InteractiveModifierEditState state,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        float previousHeightDelta =
            modifier.HeightDelta;

        modifier.SetHeightDeltaInternal(
            heightDelta
        );

        if (
            Mathf.Approximately(
                modifier.HeightDelta,
                previousHeightDelta
            )
        )
        {
            return true;
        }

        NotifyInteractiveModifierChanged(
            state,
            modifier
        );

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

        /*
         * This is intentionally the first complete stack capture since
         * MouseDown. The hot MouseDrag path tracks only the known active
         * modifier and its affected bounds.
         */
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
             *
             * Any pending interactive dirty tiles already represent the
             * restored final state because the last live sample returned
             * the modifier to its original values.
             */
            Undo.RevertAllDownToGroup(
                undoGroup
            );

            RefreshTrackedStateAfterInteractiveRevert(
                authoringData,
                worldSettings,
                notifyPreview
            );

            /*
             * A gesture can produce real intermediate terrain changes yet end
             * exactly where it started. In Lit/Height mode Terrain Analysis
             * may still be deferred at this point, while no revision change
             * exists to produce the normal commit acknowledgement.
             *
             * Request a metadata-only preview acknowledgement after the
             * interactive state has ended so PreviewStateChanged can safely
             * release any deferred analysis work. Existing pending height
             * dirty tiles, if any, are processed first by the same refresh.
             */
            if (
                notifyPreview
                &&
                state.HasInteractiveChanges
            )
            {
                TerrainAuthoringPreviewService
                    .NotifyCompositeAuthoringStateChanged(
                        null
                    );
            }

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

        /*
         * The general ChangeTracker was deliberately held at InitialStack
         * throughout MouseDrag. Synchronize it once to the committed final
         * state so later Ctrl+Z / Redo comparisons remain unchanged.
         */
        TerrainAuthoringModifierChangeTracker
            .UpdateTrackedState(
                state.AuthoringData,
                state.WorldSettings,
                finalStack,
                state.NotifyPreview
            );

        /*
         * The final live sample already recomposed terrain pixels. The
         * authoringRevision increment changes only overall authoring identity,
         * so request metadata/signature acknowledgement without extra tiles.
         */
        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService
                .NotifyCompositeAuthoringStateChanged(
                    null
                );
        }

        HashSet<Vector2Int> logicalDirtyTiles =
            new HashSet<Vector2Int>();

        /*
         * Full stack comparison remains useful once per completed gesture for
         * logical diagnostics and transaction verification.
         */
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

        if (
            authoringData == null
            ||
            worldSettings == null
        )
        {
            activeInteractiveEdit =
                null;

            errorMessage =
                "The active terrain modifier interactive edit lost its context.";

            return false;
        }

        /*
         * Interactive MouseDrag no longer advances the general ChangeTracker,
         * so cancellation must explicitly identify terrain that can contain
         * either the temporary or restored modifier contribution.
         */
        state.InteractiveDirtyTiles.Clear();

        if (
            state.HasInteractiveChanges
            &&
            state.CurrentEnabled
        )
        {
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    worldSettings,
                    state.CurrentAffectedWorldBounds,
                    state.InteractiveDirtyTiles,
                    1
                );
        }

        if (
            state.HasInteractiveChanges
            &&
            state.InitialEnabled
        )
        {
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    worldSettings,
                    state.InitialAffectedWorldBounds,
                    state.InteractiveDirtyTiles,
                    1
                );
        }

        activeInteractiveEdit =
            null;

        /*
         * Restore the complete-object snapshot captured at MouseDown.
         * RevertAllDownToGroup intentionally does not create a Redo item,
         * which is the expected behavior for Escape/cancel.
         */
        Undo.RevertAllDownToGroup(
            undoGroup
        );

        RefreshTrackedStateAfterInteractiveRevert(
            authoringData,
            worldSettings,
            notifyPreview
        );

        /*
         * Notify only after serialized authoring data has been restored so
         * PreviewService recomposes the final authoritative state. Existing
         * pending dirty tiles are coalesced with this set.
         */
        if (
            notifyPreview
            &&
            state.HasInteractiveChanges
        )
        {
            TerrainAuthoringPreviewService
                .NotifyCompositeAuthoringStateChanged(
                    state.InteractiveDirtyTiles
                );
        }

        SetNoChangeDiagnostics(
            operation + " (Canceled)",
            authoringData,
            worldSettings
        );

        return true;
    }

    // =====================================================
    // FAST INTERACTIVE DIRTY TRACKING
    // =====================================================

    private static bool TryGetActiveInteractiveStamp(
        out InteractiveModifierEditState state,
        out TerrainStampModifier modifier,
        out string errorMessage
    )
    {
        state =
            activeInteractiveEdit;

        modifier =
            null;

        errorMessage =
            "";

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

        return
            TryFindStampModifier(
                state.AuthoringData,
                state.StableId,
                out modifier,
                out errorMessage
            );
    }

    private static void NotifyInteractiveModifierChanged(
        InteractiveModifierEditState state,
        TerrainHeightModifier modifier
    )
    {
        if (
            state == null
            ||
            modifier == null
            ||
            state.AuthoringData == null
            ||
            state.WorldSettings == null
        )
        {
            return;
        }

        Bounds previousBounds =
            state.CurrentAffectedWorldBounds;

        bool previousEnabled =
            state.CurrentEnabled;

        Bounds currentBounds =
            modifier.GetAffectedWorldBounds();

        bool currentEnabled =
            modifier.Enabled;

        state.InteractiveDirtyTiles.Clear();

        if (previousEnabled)
        {
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    state.WorldSettings,
                    previousBounds,
                    state.InteractiveDirtyTiles,
                    1
                );
        }

        /*
         * A parameter-only edit has identical previous/current bounds, so one
         * collection is enough. Movement/resizing adds the new footprint too.
         */
        if (
            currentEnabled
            &&
            (
                !previousEnabled
                ||
                !AffectedBoundsMatch(
                    previousBounds,
                    currentBounds
                )
            )
        )
        {
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    state.WorldSettings,
                    currentBounds,
                    state.InteractiveDirtyTiles,
                    1
                );
        }

        state.CurrentAffectedWorldBounds =
            currentBounds;

        state.CurrentEnabled =
            currentEnabled;

        state.HasInteractiveChanges =
            true;

        EditorUtility.SetDirty(
            state.AuthoringData
        );

        if (state.NotifyPreview)
        {
            TerrainAuthoringPreviewService
                .NotifyCompositeAuthoringStateChanged(
                    state.InteractiveDirtyTiles
                );
        }
    }

    private static bool AffectedBoundsMatch(
        Bounds a,
        Bounds b
    )
    {
        return
            a.center ==
                b.center
            &&
            a.size ==
                b.size;
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
