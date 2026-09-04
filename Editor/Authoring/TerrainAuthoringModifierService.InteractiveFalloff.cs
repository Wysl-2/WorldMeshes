using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    /*
     * Interactive Falloff editing for TerrainStampEditorTool.
     *
     * The shared interactive transaction owns Undo, revision, and preview
     * state. Each drag sample mutates only the known active stamp and uses
     * direct affected-bounds dirty tracking instead of whole-stack snapshots.
     */
    public static bool UpdateInteractiveStampFalloff(
        float falloff,
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

        NotifyInteractiveModifierChanged(
            state,
            modifier
        );

        return true;
    }
}
