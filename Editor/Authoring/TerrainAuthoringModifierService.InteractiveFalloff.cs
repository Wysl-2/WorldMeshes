using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    /*
     * Interactive Falloff editing for TerrainStampEditorTool.
     *
     * This follows the same transaction contract as interactive footprint
     * and Height Delta editing: individual drag samples update the current
     * authoring preview without advancing authoringRevision, while the shared
     * CommitInteractiveEdit() boundary records one logical revision and one
     * Unity Undo operation for the complete gesture.
     */
    public static bool UpdateInteractiveStampFalloff(
        float falloff,
        out string errorMessage
    )
    {
        errorMessage =
            "";

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

        float previousFalloff =
            modifier.Falloff;

        modifier.SetFalloffInternal(
            falloff
        );

        if (
            Mathf.Approximately(
                modifier.Falloff,
                previousFalloff
            )
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
}
