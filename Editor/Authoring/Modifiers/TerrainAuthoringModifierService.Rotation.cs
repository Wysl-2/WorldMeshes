using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    // =====================================================
    // STAMP ROTATION
    // =====================================================

    public static bool SetStampRotationDegrees(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float rotationDegrees,
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

        float normalizedRotation =
            TerrainStampTransformUtility.NormalizeRotationDegrees(
                rotationDegrees
            );

        if (Mathf.Approximately(modifier.RotationDegrees, normalizedRotation))
        {
            SetNoChangeDiagnostics(
                "Rotate Terrain Stamp Modifier",
                authoringData,
                worldSettings
            );

            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Rotate Terrain Stamp Modifier",
            () =>
            {
                modifier.SetRotationDegreesInternal(normalizedRotation);
                return true;
            },
            out errorMessage
        );
    }

    /*
     * Exposes the optimized Package 2 interactive mutation primitive without
     * adding a production Scene handle yet.
     */
    public static bool UpdateInteractiveStampRotation(
        float rotationDegrees,
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

        float previousRotation = modifier.RotationDegrees;

        modifier.SetRotationDegreesInternal(rotationDegrees);

        if (Mathf.Approximately(modifier.RotationDegrees, previousRotation))
        {
            return true;
        }

        NotifyInteractiveModifierChanged(state, modifier);
        return true;
    }
}
