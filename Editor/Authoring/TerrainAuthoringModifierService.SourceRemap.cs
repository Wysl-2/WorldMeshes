using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    // =====================================================
    // DISCRETE SOURCE REMAPPING
    // =====================================================

    public static bool SetStampSourceInputMin(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float inputMin,
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

        float safeInputMin =
            TerrainStampSourceRemapUtility
                .SanitizeInputMin(
                    inputMin,
                    modifier.SourceInputMax
                );

        if (
            Mathf.Approximately(
                modifier.SourceInputMin,
                safeInputMin
            )
        )
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Source Input Min",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Source Input Min",
                () =>
                {
                    modifier.SetSourceInputMinInternal(
                        safeInputMin
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampSourceInputMax(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float inputMax,
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

        float safeInputMax =
            TerrainStampSourceRemapUtility
                .SanitizeInputMax(
                    inputMax,
                    modifier.SourceInputMin
                );

        if (
            Mathf.Approximately(
                modifier.SourceInputMax,
                safeInputMax
            )
        )
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Source Input Max",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Source Input Max",
                () =>
                {
                    modifier.SetSourceInputMaxInternal(
                        safeInputMax
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampSourceGamma(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float gamma,
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

        float safeGamma =
            TerrainStampSourceRemapUtility
                .SanitizeGamma(
                    gamma
                );

        if (
            Mathf.Approximately(
                modifier.SourceGamma,
                safeGamma
            )
        )
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Source Gamma",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Source Gamma",
                () =>
                {
                    modifier.SetSourceGammaInternal(
                        safeGamma
                    );

                    return true;
                },
                out errorMessage
            );
    }

    // =====================================================
    // INTERACTIVE SOURCE REMAPPING
    // =====================================================

    /*
     * Package 2 production sliders use this grouped update.
     *
     * BeginInteractiveModifierEdit() owns the single complete-object Undo
     * snapshot. MouseDrag samples mutate only the known active stamp and
     * invalidate its existing footprint directly.
     */
    public static bool UpdateInteractiveStampSourceRemap(
        float inputMin,
        float inputMax,
        float gamma,
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

        TerrainStampSourceRemapUtility
            .SanitizeRequestedValues(
                inputMin,
                inputMax,
                gamma,
                out float safeInputMin,
                out float safeInputMax,
                out float safeGamma
            );

        float previousInputMin =
            modifier.SourceInputMin;

        float previousInputMax =
            modifier.SourceInputMax;

        float previousGamma =
            modifier.SourceGamma;

        modifier.SetSourceRemapInternal(
            safeInputMin,
            safeInputMax,
            safeGamma
        );

        if (
            Mathf.Approximately(
                modifier.SourceInputMin,
                previousInputMin
            )
            &&
            Mathf.Approximately(
                modifier.SourceInputMax,
                previousInputMax
            )
            &&
            Mathf.Approximately(
                modifier.SourceGamma,
                previousGamma
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
