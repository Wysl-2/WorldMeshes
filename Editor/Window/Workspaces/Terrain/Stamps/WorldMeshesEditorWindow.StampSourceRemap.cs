using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private enum StampSourceRemapInteractiveField
    {
        None,
        InputMin,
        InputMax,
        Gamma
    }

    private StampSourceRemapInteractiveField
        activeStampSourceRemapField =
            StampSourceRemapInteractiveField.None;

    private int activeStampSourceRemapControlId;

    private string activeStampSourceRemapStableId =
        "";

    private const float UsefulSourceGammaMinimum =
        0.1f;

    private const float UsefulSourceGammaMaximum =
        4f;

    // =====================================================
    // PRODUCTION SOURCE REMAPPING UI
    // =====================================================

    private void DrawStampSourceRemapSettings(
        TerrainStampModifier stamp
    )
    {
        if (
            stamp == null
            ||
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            return;
        }

        ReconcileStampSourceRemapInteractiveState(
            stamp
        );

        HandleStampSourceRemapEscape();

        GUILayout.Space(
            4f
        );

        showStampSourceRemapping =
            EditorGUILayout.Foldout(
                showStampSourceRemapping,
                "Source Remapping",
                true
            );

        if (!showStampSourceRemapping)
        {
            return;
        }

        float inputMinMaximum =
            Mathf.Max(
                0f,
                stamp.SourceInputMax -
                    TerrainStampSourceRemapUtility
                        .MinimumInputRange
            );

        DrawSourceRemapParameterRow(
            stamp,
            StampSourceRemapInteractiveField.InputMin,
            "Input Min",
            stamp.SourceInputMin,
            0f,
            inputMinMaximum
        );

        float inputMaxMinimum =
            Mathf.Min(
                1f,
                stamp.SourceInputMin +
                    TerrainStampSourceRemapUtility
                        .MinimumInputRange
            );

        DrawSourceRemapParameterRow(
            stamp,
            StampSourceRemapInteractiveField.InputMax,
            "Input Max",
            stamp.SourceInputMax,
            inputMaxMinimum,
            1f
        );

        /*
         * 0.1..4 is a useful artistic slider range, not a data-model limit.
         * Expand around an existing exact value so uncommon Gamma values
         * remain representable by the live slider without changing them.
         */
        float gammaSliderMinimum =
            Mathf.Min(
                UsefulSourceGammaMinimum,
                stamp.SourceGamma
            );

        float gammaSliderMaximum =
            Mathf.Max(
                UsefulSourceGammaMaximum,
                stamp.SourceGamma
            );

        DrawSourceRemapParameterRow(
            stamp,
            StampSourceRemapInteractiveField.Gamma,
            "Gamma",
            stamp.SourceGamma,
            gammaSliderMinimum,
            gammaSliderMaximum
        );
    }

    private void DrawSourceRemapParameterRow(
        TerrainStampModifier stamp,
        StampSourceRemapInteractiveField field,
        string label,
        float currentValue,
        float sliderMinimum,
        float sliderMaximum
    )
    {
        bool ownsSourceRemapTransaction =
            activeStampSourceRemapField !=
                StampSourceRemapInteractiveField.None
            &&
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            TerrainAuthoringModifierService
                .ActiveInteractiveStableId ==
                activeStampSourceRemapStableId
            &&
            activeStampSourceRemapStableId ==
                stamp.StableId;

        bool unrelatedInteractiveEditActive =
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            !ownsSourceRemapTransaction;

        bool anotherSourceRemapFieldActive =
            activeStampSourceRemapField !=
                StampSourceRemapInteractiveField.None
            &&
            activeStampSourceRemapField !=
                field;

        bool hasSliderRange =
            sliderMaximum >
            sliderMinimum +
                Mathf.Epsilon;

        /*
         * Avoid a mathematically degenerate HorizontalSlider range when Min
         * and Max are authored exactly one MinimumInputRange apart. The slider
         * is disabled in that state; its tiny synthetic span is never editable
         * and exists only to keep IMGUI drawing well-defined.
         */
        float drawnSliderMaximum =
            hasSliderRange
                ? sliderMaximum
                : sliderMinimum +
                    Mathf.Epsilon;

        float drawnSliderValue =
            Mathf.Clamp(
                currentValue,
                sliderMinimum,
                drawnSliderMaximum
            );

        GUILayout.BeginHorizontal();

        EditorGUILayout.PrefixLabel(
            label
        );

        EditorGUI.BeginDisabledGroup(
            anotherSourceRemapFieldActive
            ||
            unrelatedInteractiveEditActive
            ||
            !hasSliderRange
        );

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        float sliderValue =
            GUILayout.HorizontalSlider(
                drawnSliderValue,
                sliderMinimum,
                drawnSliderMaximum,
                GUILayout.MinWidth(
                    80f
                )
            );

        bool sliderChanged =
            EditorGUI.EndChangeCheck();

        int hotAfter =
            GUIUtility.hotControl;

        EditorGUI.EndDisabledGroup();

        HandleSourceRemapSliderInteraction(
            stamp,
            field,
            sliderValue,
            sliderChanged,
            hotBefore,
            hotAfter
        );

        /*
         * Exact numeric entry is discrete and must never run while any terrain
         * modifier interactive transaction owns the global authoring service.
         */
        EditorGUI.BeginDisabledGroup(
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
        );

        EditorGUI.BeginChangeCheck();

        float exactValue =
            EditorGUILayout.DelayedFloatField(
                GUIContent.none,
                currentValue,
                GUILayout.Width(
                    72f
                )
            );

        bool exactChanged =
            EditorGUI.EndChangeCheck();

        EditorGUI.EndDisabledGroup();

        GUILayout.EndHorizontal();

        if (exactChanged)
        {
            ApplyDiscreteSourceRemapValue(
                stamp,
                field,
                exactValue
            );
        }
    }

    // =====================================================
    // LIVE SLIDER TRANSACTION
    // =====================================================

    private void HandleSourceRemapSliderInteraction(
        TerrainStampModifier stamp,
        StampSourceRemapInteractiveField field,
        float sliderValue,
        bool sliderChanged,
        int hotBefore,
        int hotAfter
    )
    {
        bool capturedThisControl =
            activeStampSourceRemapField ==
                StampSourceRemapInteractiveField.None
            &&
            !TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            hotBefore == 0
            &&
            hotAfter != 0;

        if (capturedThisControl)
        {
            string undoLabel =
                GetSourceRemapUndoLabel(
                    field
                );

            if (
                !TerrainAuthoringModifierService
                    .BeginInteractiveModifierEdit(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        undoLabel,
                        out string beginError
                    )
            )
            {
                SetModifierAuthoringError(
                    beginError
                );

                return;
            }

            activeStampSourceRemapField =
                field;

            activeStampSourceRemapControlId =
                hotAfter;

            activeStampSourceRemapStableId =
                stamp.StableId;

            ClearModifierAuthoringError();
        }

        bool ownsActiveControl =
            activeStampSourceRemapField ==
                field
            &&
            activeStampSourceRemapControlId !=
                0
            &&
            activeStampSourceRemapStableId ==
                stamp.StableId
            &&
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            TerrainAuthoringModifierService
                .ActiveInteractiveStableId ==
                activeStampSourceRemapStableId;

        if (!ownsActiveControl)
        {
            return;
        }

        if (sliderChanged)
        {
            float inputMin =
                field ==
                    StampSourceRemapInteractiveField.InputMin
                    ? sliderValue
                    : stamp.SourceInputMin;

            float inputMax =
                field ==
                    StampSourceRemapInteractiveField.InputMax
                    ? sliderValue
                    : stamp.SourceInputMax;

            float gamma =
                field ==
                    StampSourceRemapInteractiveField.Gamma
                    ? sliderValue
                    : stamp.SourceGamma;

            if (
                !TerrainAuthoringModifierService
                    .UpdateInteractiveStampSourceRemap(
                        inputMin,
                        inputMax,
                        gamma,
                        out string updateError
                    )
            )
            {
                TerrainAuthoringModifierService
                    .CancelInteractiveEdit(
                        out _
                    );

                if (
                    GUIUtility.hotControl ==
                        activeStampSourceRemapControlId
                )
                {
                    GUIUtility.hotControl =
                        0;
                }

                ClearSourceRemapInteractiveState();

                SetModifierAuthoringError(
                    updateError
                );

                return;
            }

            TerrainAuthoringModifierSelection
                .NotifyModifierDataChanged();

            Repaint();
        }

        bool released =
            hotBefore ==
                activeStampSourceRemapControlId
            &&
            hotAfter == 0;

        if (!released)
        {
            return;
        }

        if (
            !TerrainAuthoringModifierService
                .CommitInteractiveEdit(
                    out string commitError
                )
        )
        {
            ClearSourceRemapInteractiveState();

            SetModifierAuthoringError(
                commitError
            );

            return;
        }

        ClearSourceRemapInteractiveState();

        OnModifierMutationSucceeded();
    }

    private void HandleStampSourceRemapEscape()
    {
        if (
            activeStampSourceRemapField ==
                StampSourceRemapInteractiveField.None
        )
        {
            return;
        }

        Event currentEvent =
            Event.current;

        if (
            currentEvent == null
            ||
            currentEvent.type !=
                EventType.KeyDown
            ||
            currentEvent.keyCode !=
                KeyCode.Escape
        )
        {
            return;
        }

        if (
            !TerrainAuthoringModifierService
                .CancelInteractiveEdit(
                    out string cancelError
                )
            &&
            !string.IsNullOrEmpty(
                cancelError
            )
        )
        {
            SetModifierAuthoringError(
                cancelError
            );
        }
        else
        {
            ClearModifierAuthoringError();
        }

        if (
            GUIUtility.hotControl ==
                activeStampSourceRemapControlId
        )
        {
            GUIUtility.hotControl =
                0;
        }

        ClearSourceRemapInteractiveState();

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        currentEvent.Use();

        Repaint();
    }

    /*
     * A selected modifier should not change during an ordinary slider drag,
     * but selection can still be changed by editor lifecycle or external code.
     * Keep local IMGUI ownership synchronized with the service transaction so
     * stale UI state can never remain attached to the wrong stamp.
     */
    private void ReconcileStampSourceRemapInteractiveState(
        TerrainStampModifier displayedStamp
    )
    {
        if (
            activeStampSourceRemapField ==
                StampSourceRemapInteractiveField.None
        )
        {
            return;
        }

        bool displayedStampMatches =
            displayedStamp != null
            &&
            displayedStamp.StableId ==
                activeStampSourceRemapStableId;

        bool serviceTransactionMatches =
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            TerrainAuthoringModifierService
                .ActiveInteractiveStableId ==
                activeStampSourceRemapStableId;

        if (
            displayedStampMatches
            &&
            serviceTransactionMatches
        )
        {
            return;
        }

        if (serviceTransactionMatches)
        {
            if (
                !TerrainAuthoringModifierService
                    .CancelInteractiveEdit(
                        out string cancelError
                    )
                &&
                !string.IsNullOrEmpty(
                    cancelError
                )
            )
            {
                SetModifierAuthoringError(
                    cancelError
                );
            }

            TerrainAuthoringModifierSelection
                .NotifyModifierDataChanged();
        }

        if (
            GUIUtility.hotControl ==
                activeStampSourceRemapControlId
        )
        {
            GUIUtility.hotControl =
                0;
        }

        ClearSourceRemapInteractiveState();
    }

    private void CancelStampSourceRemapInteractionOnDisable()
    {
        bool ownsServiceTransaction =
            activeStampSourceRemapField !=
                StampSourceRemapInteractiveField.None
            &&
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            TerrainAuthoringModifierService
                .ActiveInteractiveStableId ==
                activeStampSourceRemapStableId;

        if (ownsServiceTransaction)
        {
            TerrainAuthoringModifierService
                .CancelInteractiveEdit(
                    out _
                );
        }

        if (
            GUIUtility.hotControl ==
                activeStampSourceRemapControlId
        )
        {
            GUIUtility.hotControl =
                0;
        }

        ClearSourceRemapInteractiveState();
    }

    private void ClearSourceRemapInteractiveState()
    {
        activeStampSourceRemapField =
            StampSourceRemapInteractiveField.None;

        activeStampSourceRemapControlId =
            0;

        activeStampSourceRemapStableId =
            "";
    }

    // =====================================================
    // DISCRETE EXACT ENTRY
    // =====================================================

    private void ApplyDiscreteSourceRemapValue(
        TerrainStampModifier stamp,
        StampSourceRemapInteractiveField field,
        float value
    )
    {
        bool success;
        string errorMessage;

        switch (field)
        {
            case StampSourceRemapInteractiveField.InputMin:
            {
                float safeInputMin =
                    TerrainStampSourceRemapUtility
                        .SanitizeInputMin(
                            value,
                            stamp.SourceInputMax
                        );

                success =
                    TerrainAuthoringModifierService
                        .SetStampSourceInputMin(
                            terrainAuthoringData,
                            worldSettings,
                            stamp.StableId,
                            safeInputMin,
                            out errorMessage
                        );

                break;
            }

            case StampSourceRemapInteractiveField.InputMax:
            {
                float safeInputMax =
                    TerrainStampSourceRemapUtility
                        .SanitizeInputMax(
                            value,
                            stamp.SourceInputMin
                        );

                success =
                    TerrainAuthoringModifierService
                        .SetStampSourceInputMax(
                            terrainAuthoringData,
                            worldSettings,
                            stamp.StableId,
                            safeInputMax,
                            out errorMessage
                        );

                break;
            }

            case StampSourceRemapInteractiveField.Gamma:
            {
                float safeGamma =
                    TerrainStampSourceRemapUtility
                        .SanitizeGamma(
                            value
                        );

                success =
                    TerrainAuthoringModifierService
                        .SetStampSourceGamma(
                            terrainAuthoringData,
                            worldSettings,
                            stamp.StableId,
                            safeGamma,
                            out errorMessage
                        );

                break;
            }

            default:
            {
                SetModifierAuthoringError(
                    "Unsupported terrain stamp source-remap field."
                );

                return;
            }
        }

        if (success)
        {
            OnModifierMutationSucceeded();
        }
        else
        {
            SetModifierAuthoringError(
                errorMessage
            );
        }
    }

    private static string GetSourceRemapUndoLabel(
        StampSourceRemapInteractiveField field
    )
    {
        switch (field)
        {
            case StampSourceRemapInteractiveField.InputMin:
                return
                    "Set Terrain Stamp Source Input Min";

            case StampSourceRemapInteractiveField.InputMax:
                return
                    "Set Terrain Stamp Source Input Max";

            case StampSourceRemapInteractiveField.Gamma:
                return
                    "Set Terrain Stamp Source Gamma";

            default:
                return
                    "Set Terrain Stamp Source Remapping";
        }
    }
}
