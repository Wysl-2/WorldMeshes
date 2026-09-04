using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    // =====================================================
    // DISCRETE SMOOTHING PARAMETERS
    // =====================================================

    public static bool SetStampSmoothingRadius(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float smoothingRadius,
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

        float safeRadius =
            SanitizeSmoothingRadius(
                smoothingRadius
            );

        if (
            Mathf.Approximately(
                modifier.SmoothingRadius,
                safeRadius
            )
        )
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Smoothing Radius",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Smoothing Radius",
                () =>
                {
                    modifier.SetSmoothingRadiusInternal(
                        safeRadius
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampSmoothingStrength(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float smoothingStrength,
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

        float safeStrength =
            SanitizeSmoothingStrength(
                smoothingStrength
            );

        if (
            Mathf.Approximately(
                modifier.SmoothingStrength,
                safeStrength
            )
        )
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Smoothing Strength",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Smoothing Strength",
                () =>
                {
                    modifier.SetSmoothingStrengthInternal(
                        safeStrength
                    );

                    return true;
                },
                out errorMessage
            );
    }

    // =====================================================
    // INTERACTIVE SMOOTHING
    // =====================================================

    /*
     * Used by the production WorldMeshes smoothing sliders.
     *
     * BeginInteractiveModifierEdit() owns the complete-object Undo snapshot.
     * MouseDrag samples mutate only the known active stamp and invalidate its
     * existing footprint directly. The complete modifier stack is captured
     * again only when CommitInteractiveEdit() closes the gesture.
     */
    public static bool UpdateInteractiveStampSmoothing(
        float smoothingRadius,
        float smoothingStrength,
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

        float safeRadius =
            SanitizeSmoothingRadius(
                smoothingRadius
            );

        float safeStrength =
            SanitizeSmoothingStrength(
                smoothingStrength
            );

        float previousRadius =
            modifier.SmoothingRadius;

        float previousStrength =
            modifier.SmoothingStrength;

        modifier.SetSmoothingRadiusInternal(
            safeRadius
        );

        modifier.SetSmoothingStrengthInternal(
            safeStrength
        );

        if (
            Mathf.Approximately(
                modifier.SmoothingRadius,
                previousRadius
            )
            &&
            Mathf.Approximately(
                modifier.SmoothingStrength,
                previousStrength
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

    // =====================================================
    // SANITIZATION
    // =====================================================

    private static float SanitizeSmoothingRadius(
        float value
    )
    {
        if (
            float.IsNaN(
                value
            )
            ||
            float.IsInfinity(
                value
            )
        )
        {
            return 0f;
        }

        return
            Mathf.Max(
                0f,
                value
            );
    }

    private static float SanitizeSmoothingStrength(
        float value
    )
    {
        if (
            float.IsNaN(
                value
            )
            ||
            float.IsInfinity(
                value
            )
        )
        {
            return 1f;
        }

        return
            Mathf.Clamp01(
                value
            );
    }
}
