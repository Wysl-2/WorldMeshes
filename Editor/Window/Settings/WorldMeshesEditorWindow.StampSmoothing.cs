using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private enum StampSmoothingInteractiveField
    {
        None,
        Radius,
        Strength
    }

    private StampSmoothingInteractiveField
        activeStampSmoothingField =
            StampSmoothingInteractiveField.None;

    private int activeStampSmoothingControlId;

    private string activeStampSmoothingStableId =
        "";

    private void OnDisable()
    {
        if (
            activeStampSmoothingField !=
                StampSmoothingInteractiveField.None
            &&
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            TerrainAuthoringModifierService
                .ActiveInteractiveStableId ==
                activeStampSmoothingStableId
        )
        {
            TerrainAuthoringModifierService
                .CancelInteractiveEdit(
                    out _
                );
        }

        ClearSmoothingInteractiveState();

        CancelStampSourceRemapInteractionOnDisable();

        ShutdownStampLibraryBrowser();
    }

    // =====================================================
    // PRODUCTION SMOOTHING UI
    // =====================================================

    private void DrawStampSmoothingSettings(
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

        HandleStampSmoothingEscape();

        GUILayout.Space(
            4f
        );

        showStampSmoothing =
            EditorGUILayout.Foldout(
                showStampSmoothing,
                "Smoothing",
                true
            );

        if (!showStampSmoothing)
        {
            return;
        }

        Vector2 size =
            stamp.SizeXZ;

        /*
         * The data model itself has no arbitrary upper radius restriction.
         * The live slider uses a useful footprint-relative range and expands
         * when an existing exact value is larger. The delayed numeric field
         * beside it remains the authoritative unrestricted entry path.
         */
        float usefulRadiusMaximum =
            Mathf.Max(
                1f,
                Mathf.Max(
                    Mathf.Max(
                        size.x,
                        size.y
                    ),
                    stamp.SmoothingRadius *
                        2f
                )
            );

        DrawSmoothingParameterRow(
            stamp,
            StampSmoothingInteractiveField.Radius,
            "Radius (m)",
            stamp.SmoothingRadius,
            0f,
            usefulRadiusMaximum
        );

        DrawSmoothingParameterRow(
            stamp,
            StampSmoothingInteractiveField.Strength,
            "Strength",
            stamp.SmoothingStrength,
            0f,
            1f
        );

        GUILayout.Space(
            4f
        );

        showStampSamplingDiagnostics =
            EditorGUILayout.Foldout(
                showStampSamplingDiagnostics,
                "Sampling Diagnostics",
                true
            );

        if (showStampSamplingDiagnostics)
        {
            DrawStampSamplingDiagnostics(
                stamp
            );
        }
    }

    private void DrawSmoothingParameterRow(
        TerrainStampModifier stamp,
        StampSmoothingInteractiveField field,
        string label,
        float currentValue,
        float minimumValue,
        float maximumValue
    )
    {
        bool anotherSmoothingFieldActive =
            activeStampSmoothingField !=
                StampSmoothingInteractiveField.None
            &&
            activeStampSmoothingField !=
                field;

        GUILayout.BeginHorizontal();

        EditorGUILayout.PrefixLabel(
            label
        );

        EditorGUI.BeginDisabledGroup(
            anotherSmoothingFieldActive
        );

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        float sliderValue =
            GUILayout.HorizontalSlider(
                currentValue,
                minimumValue,
                maximumValue,
                GUILayout.MinWidth(
                    80f
                )
            );

        bool sliderChanged =
            EditorGUI.EndChangeCheck();

        int hotAfter =
            GUIUtility.hotControl;

        EditorGUI.EndDisabledGroup();

        HandleSmoothingSliderInteraction(
            stamp,
            field,
            sliderValue,
            sliderChanged,
            hotBefore,
            hotAfter
        );

        EditorGUI.BeginDisabledGroup(
            activeStampSmoothingField !=
                StampSmoothingInteractiveField.None
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
            ApplyDiscreteSmoothingValue(
                stamp,
                field,
                exactValue
            );
        }
    }

    // =====================================================
    // LIVE SLIDER TRANSACTION
    // =====================================================

    private void HandleSmoothingSliderInteraction(
        TerrainStampModifier stamp,
        StampSmoothingInteractiveField field,
        float sliderValue,
        bool sliderChanged,
        int hotBefore,
        int hotAfter
    )
    {
        bool capturedThisControl =
            activeStampSmoothingField ==
                StampSmoothingInteractiveField.None
            &&
            hotBefore == 0
            &&
            hotAfter != 0;

        if (capturedThisControl)
        {
            string undoLabel =
                field ==
                    StampSmoothingInteractiveField.Radius
                    ? "Set Terrain Stamp Smoothing Radius"
                    : "Set Terrain Stamp Smoothing Strength";

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

            activeStampSmoothingField =
                field;

            activeStampSmoothingControlId =
                hotAfter;

            activeStampSmoothingStableId =
                stamp.StableId;

            ClearModifierAuthoringError();
        }

        bool ownsActiveControl =
            activeStampSmoothingField ==
                field
            &&
            activeStampSmoothingControlId !=
                0
            &&
            activeStampSmoothingStableId ==
                stamp.StableId;

        if (!ownsActiveControl)
        {
            return;
        }

        if (sliderChanged)
        {
            float radius =
                field ==
                    StampSmoothingInteractiveField.Radius
                    ? sliderValue
                    : stamp.SmoothingRadius;

            float strength =
                field ==
                    StampSmoothingInteractiveField.Strength
                    ? sliderValue
                    : stamp.SmoothingStrength;

            if (
                !TerrainAuthoringModifierService
                    .UpdateInteractiveStampSmoothing(
                        radius,
                        strength,
                        out string updateError
                    )
            )
            {
                TerrainAuthoringModifierService
                    .CancelInteractiveEdit(
                        out _
                    );

                ClearSmoothingInteractiveState();

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
                activeStampSmoothingControlId
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
            ClearSmoothingInteractiveState();

            SetModifierAuthoringError(
                commitError
            );

            return;
        }

        ClearSmoothingInteractiveState();

        OnModifierMutationSucceeded();
    }

    private void HandleStampSmoothingEscape()
    {
        if (
            activeStampSmoothingField ==
                StampSmoothingInteractiveField.None
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

        GUIUtility.hotControl =
            0;

        ClearSmoothingInteractiveState();

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        currentEvent.Use();

        Repaint();
    }

    private void ClearSmoothingInteractiveState()
    {
        activeStampSmoothingField =
            StampSmoothingInteractiveField.None;

        activeStampSmoothingControlId =
            0;

        activeStampSmoothingStableId =
            "";
    }

    // =====================================================
    // DISCRETE EXACT ENTRY
    // =====================================================

    private void ApplyDiscreteSmoothingValue(
        TerrainStampModifier stamp,
        StampSmoothingInteractiveField field,
        float value
    )
    {
        bool success;
        string errorMessage;

        if (
            field ==
            StampSmoothingInteractiveField.Radius
        )
        {
            success =
                TerrainAuthoringModifierService
                    .SetStampSmoothingRadius(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        value,
                        out errorMessage
                    );
        }
        else
        {
            success =
                TerrainAuthoringModifierService
                    .SetStampSmoothingStrength(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        value,
                        out errorMessage
                    );
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

    // =====================================================
    // SAMPLING DIAGNOSTICS
    // =====================================================

    private void DrawStampSamplingDiagnostics(
        TerrainStampModifier stamp
    )
    {
        GUILayout.Space(6f);

        GUILayout.Label(
            "Sampling",
            EditorStyles.miniBoldLabel
        );

        TerrainHeightStampAsset asset =
            stamp.StampAsset;

        Texture2D texture =
            asset != null
                ? asset.HeightTexture
                : null;

        if (texture == null)
        {
            EditorGUILayout.LabelField(
                "Texture Resolution",
                "Unassigned"
            );

            return;
        }

        Vector2 size =
            stamp.SizeXZ;

        int width =
            Mathf.Max(
                1,
                texture.width
            );

        int height =
            Mathf.Max(
                1,
                texture.height
            );

        float texelSizeX =
            size.x /
            width;

        float texelSizeZ =
            size.y /
            height;

        float terrainSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        float ratioX =
            texelSizeX /
            terrainSpacing;

        float ratioZ =
            texelSizeZ /
            terrainSpacing;

        EditorGUILayout.LabelField(
            "Texture Resolution",
            $"{width} x {height}"
        );

        EditorGUILayout.LabelField(
            "Stamp Texel Size",
            $"{texelSizeX:0.###} m x " +
            $"{texelSizeZ:0.###} m"
        );

        EditorGUILayout.LabelField(
            "Terrain Sample Spacing",
            $"{terrainSpacing:0.###} m"
        );

        EditorGUILayout.LabelField(
            "Source / Terrain Ratio",
            $"{ratioX:0.##}x X / " +
            $"{ratioZ:0.##}x Z"
        );

        float coarsestRatio =
            Mathf.Max(
                ratioX,
                ratioZ
            );

        if (coarsestRatio > 1.25f)
        {
            EditorGUILayout.HelpBox(
                "This stamp is spatially coarser than the native terrain " +
                "heightfield at its current footprint size. This is allowed " +
                "and can be intentional. Radius and Strength can be used to " +
                "artistically control how much of the source's small-scale " +
                "structure remains in the final deformation.",
                MessageType.Info
            );
        }
    }
}
