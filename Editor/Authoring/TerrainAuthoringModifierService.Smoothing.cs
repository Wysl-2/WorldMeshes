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
     * BeginInteractiveModifierEdit() owns the original complete-object Undo
     * snapshot. This method may be called many times during one slider drag.
     * It updates the current snapshot/dirty region and live preview without
     * advancing authoringRevision. CommitInteractiveEdit() performs the single
     * logical revision increment at gesture completion.
     */
    public static bool UpdateInteractiveStampSmoothing(
        float smoothingRadius,
        float smoothingStrength,
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
