using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private const float SelectedStampThumbnailSize =
        84f;

    [SerializeField]
    private bool showStampSourceRemapping =
        true;

    [SerializeField]
    private bool showStampSmoothing =
        true;

    [SerializeField]
    private bool showStampSamplingDiagnostics;

    [SerializeField]
    private bool showSelectedModifierDiagnostics;

    // =====================================================
    // COMPACT MODIFIER STACK
    // =====================================================

    private void DrawCompactModifierStackPanel()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            GUILayout.Label(
                "Terrain Modifiers",
                EditorStyles.boldLabel
            );

            EditorGUILayout.HelpBox(
                "WorldSettings and TerrainAuthoringData must be assigned before terrain modifiers can be authored.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        GUILayout.BeginHorizontal();

        GUILayout.Label(
            "Terrain Modifiers",
            EditorStyles.boldLabel
        );

        GUILayout.FlexibleSpace();

        GUILayout.Label(
            terrainAuthoringData.HeightModifierCount +
                "  |  Top to bottom",
            EditorStyles.miniLabel
        );

        GUILayout.EndHorizontal();

        bool editingAllowed =
            IsModifierEditingAllowed();

        if (!editingAllowed)
        {
            EditorGUILayout.HelpBox(
                "Editing is disabled while entering or running Play Mode.",
                MessageType.Info
            );
        }

        DrawModifierRows(
            editingAllowed
        );

        GUILayout.Space(
            4f
        );

        DrawCompactModifierStackCommands(
            editingAllowed
        );

        GUILayout.EndVertical();
    }

    private void DrawCompactModifierStackCommands(
        bool editingAllowed
    )
    {
        bool hasSelection =
            TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    terrainAuthoringData,
                    out _,
                    out int selectedIndex
                );

        GUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
            ||
            !hasSelection
        );

        if (
            GUILayout.Button(
                "Duplicate"
            )
        )
        {
            DuplicateSelectedModifier();
        }

        if (
            GUILayout.Button(
                "Delete"
            )
        )
        {
            DeleteSelectedModifier();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
            ||
            !hasSelection
            ||
            selectedIndex <=
                0
        );

        if (
            GUILayout.Button(
                "Move Earlier"
            )
        )
        {
            ReorderSelectedModifier(
                selectedIndex -
                    1
            );
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
            ||
            !hasSelection
            ||
            selectedIndex >=
                terrainAuthoringData
                    .HeightModifierCount -
                1
        );

        if (
            GUILayout.Button(
                "Move Later"
            )
        )
        {
            ReorderSelectedModifier(
                selectedIndex +
                    1
            );
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndHorizontal();
    }

    // =====================================================
    // COMPACT SELECTED MODIFIER
    // =====================================================

    private void DrawCompactSelectedModifierPanel()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            GUILayout.Label(
                "Selected Stamp",
                EditorStyles.boldLabel
            );

            EditorGUILayout.HelpBox(
                "No modifier authoring context is available.",
                MessageType.None
            );

            GUILayout.EndVertical();
            return;
        }

        if (
            !TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    terrainAuthoringData,
                    out TerrainHeightModifier modifier,
                    out _
                )
        )
        {
            GUILayout.Label(
                "Selected Stamp",
                EditorStyles.boldLabel
            );

            EditorGUILayout.HelpBox(
                "Select a terrain modifier above to inspect and edit it.",
                MessageType.None
            );

            GUILayout.EndVertical();
            return;
        }

        if (
            modifier is
                TerrainStampModifier stamp
        )
        {
            DrawCompactSelectedStampInspector(
                stamp
            );
        }
        else
        {
            DrawCompactGenericModifierInspector(
                modifier
            );
        }

        GUILayout.EndVertical();
    }

    private void DrawCompactSelectedStampInspector(
        TerrainStampModifier stamp
    )
    {
        bool editingAllowed =
            IsModifierEditingAllowed();

        DrawCompactStampIdentityHeader(
            stamp,
            editingAllowed
        );

        if (
            stamp.StampAsset ==
                null
        )
        {
            EditorGUILayout.HelpBox(
                "This modifier has no stamp asset assigned and does not currently contribute terrain deformation.",
                MessageType.Warning
            );
        }

        GUILayout.Space(
            6f
        );

        DrawCompactSectionHeader(
            "Transform"
        );

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
        );

        DrawCompactStampTransformFields(
            stamp
        );

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            7f
        );

        DrawCompactSectionHeader(
            "Source"
        );

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
        );

        DrawCompactStampSourceFields(
            stamp
        );

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            7f
        );

        DrawCompactSectionHeader(
            "Output"
        );

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
        );

        DrawCompactStampOutputFields(
            stamp
        );

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            6f
        );

        DrawCompactSelectedModifierFooter(
            stamp
        );
    }

    private void DrawCompactStampIdentityHeader(
        TerrainStampModifier stamp,
        bool editingAllowed
    )
    {
        GUILayout.BeginHorizontal();

        Rect thumbnailRect =
            GUILayoutUtility.GetRect(
                SelectedStampThumbnailSize,
                SelectedStampThumbnailSize,
                GUILayout.Width(
                    SelectedStampThumbnailSize
                ),
                GUILayout.Height(
                    SelectedStampThumbnailSize
                )
            );

        GUI.Box(
            thumbnailRect,
            GUIContent.none
        );

        TerrainHeightStampAsset currentAsset =
            stamp.StampAsset;

        Texture2D thumbnail =
            currentAsset != null
                ? currentAsset.HeightTexture
                : null;

        Rect imageRect =
            new Rect(
                thumbnailRect.x +
                    4f,
                thumbnailRect.y +
                    4f,
                Mathf.Max(
                    1f,
                    thumbnailRect.width -
                        8f
                ),
                Mathf.Max(
                    1f,
                    thumbnailRect.height -
                        8f
                )
            );

        if (thumbnail != null)
        {
            GUI.DrawTexture(
                imageRect,
                thumbnail,
                ScaleMode.ScaleToFit,
                false
            );
        }
        else
        {
            GUI.Label(
                imageRect,
                "No\nStamp",
                EditorStyles.centeredGreyMiniLabel
            );
        }

        GUILayout.Space(
            7f
        );

        GUILayout.BeginVertical();

        GUILayout.Label(
            currentAsset != null
                ? currentAsset.DisplayName
                : "Unassigned Stamp",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
        );

        TerrainHeightStampAsset editedAsset =
            (TerrainHeightStampAsset)
            EditorGUILayout.ObjectField(
                new GUIContent(
                    "Stamp Asset",
                    "Changing the assigned asset changes only the source asset reference; it does not apply the asset's creation defaults."
                ),
                currentAsset,
                typeof(TerrainHeightStampAsset),
                false
            );

        if (
            editedAsset !=
                currentAsset
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampAsset(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedAsset,
                        out string stampAssetError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    stampAssetError
                );
            }
        }

        bool enabled =
            EditorGUILayout.Toggle(
                "Enabled",
                stamp.Enabled
            );

        if (
            enabled !=
                stamp.Enabled
        )
        {
            ApplyModifierEnabled(
                stamp,
                enabled
            );
        }

        EditorGUI.EndDisabledGroup();

        if (
            GUILayout.Button(
                "Frame In Scene View",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            if (
                !TerrainAuthoringModifierSceneUtility
                    .TryFrameModifier(
                        stamp,
                        out string frameError
                    )
            )
            {
                SetModifierAuthoringError(
                    frameError
                );
            }
            else
            {
                ClearModifierAuthoringError();
            }
        }

        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    // =====================================================
    // TRANSFORM
    // =====================================================

    private void DrawCompactStampTransformFields(
        TerrainStampModifier stamp
    )
    {
        Vector2 position =
            stamp.PositionXZ;

        float positionX =
            EditorGUILayout.DelayedFloatField(
                "Position X",
                position.x
            );

        float positionZ =
            EditorGUILayout.DelayedFloatField(
                "Position Z",
                position.y
            );

        Vector2 editedPosition =
            new Vector2(
                positionX,
                positionZ
            );

        if (
            editedPosition !=
                position
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampPositionXZ(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedPosition,
                        out string positionError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    positionError
                );
            }
        }

        Vector2 size =
            stamp.SizeXZ;

        float sizeX =
            EditorGUILayout.DelayedFloatField(
                "Size X",
                size.x
            );

        float sizeZ =
            EditorGUILayout.DelayedFloatField(
                "Size Z",
                size.y
            );

        Vector2 editedSize =
            new Vector2(
                sizeX,
                sizeZ
            );

        if (
            editedSize !=
                size
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampSizeXZ(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedSize,
                        out string sizeError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    sizeError
                );
            }
        }

        float editedRotation =
            EditorGUILayout.DelayedFloatField(
                "Rotation",
                stamp.RotationDegrees
            );

        float normalizedRotation =
            TerrainStampTransformUtility
                .NormalizeRotationDegrees(
                    editedRotation
                );

        if (
            !Mathf.Approximately(
                normalizedRotation,
                stamp.RotationDegrees
            )
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampRotationDegrees(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        normalizedRotation,
                        out string rotationError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    rotationError
                );
            }
        }
    }

    // =====================================================
    // SOURCE
    // =====================================================

    private void DrawCompactStampSourceFields(
        TerrainStampModifier stamp
    )
    {
        bool flipX =
            EditorGUILayout.Toggle(
                "Flip X",
                stamp.FlipX
            );

        if (
            flipX !=
                stamp.FlipX
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampFlipX(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        flipX,
                        out string flipXError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    flipXError
                );
            }
        }

        bool flipZ =
            EditorGUILayout.Toggle(
                "Flip Z",
                stamp.FlipZ
            );

        if (
            flipZ !=
                stamp.FlipZ
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampFlipZ(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        flipZ,
                        out string flipZError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    flipZError
                );
            }
        }

        /*
         * Reuse the existing interactive slider implementation so Package 5
         * does not create a second Undo/preview/cancel transaction path.
         */
        DrawStampSourceRemapSettings(
            stamp
        );
    }

    // =====================================================
    // OUTPUT
    // =====================================================

    private void DrawCompactStampOutputFields(
        TerrainStampModifier stamp
    )
    {
        TerrainHeightBlendMode activeBlendMode =
            stamp.BlendMode;

        TerrainHeightBlendMode editedBlendMode =
            (TerrainHeightBlendMode)
            EditorGUILayout.EnumPopup(
                "Blend Mode",
                activeBlendMode
            );

        if (
            editedBlendMode !=
                activeBlendMode
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetModifierBlendMode(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedBlendMode,
                        out string blendModeError
                    )
            )
            {
                activeBlendMode =
                    editedBlendMode;

                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    blendModeError
                );
            }
        }

        if (
            activeBlendMode ==
                TerrainHeightBlendMode.Additive
        )
        {
            float heightDelta =
                EditorGUILayout.DelayedFloatField(
                    "Height Delta",
                    stamp.HeightDelta
                );

            if (
                !Mathf.Approximately(
                    heightDelta,
                    stamp.HeightDelta
                )
            )
            {
                if (
                    TerrainAuthoringModifierService
                        .SetStampHeightDelta(
                            terrainAuthoringData,
                            worldSettings,
                            stamp.StableId,
                            heightDelta,
                            out string heightError
                        )
                )
                {
                    OnModifierMutationSucceeded();
                }
                else
                {
                    SetModifierAuthoringError(
                        heightError
                    );
                }
            }
        }
        else if (
            activeBlendMode ==
                TerrainHeightBlendMode.Max
            ||
            activeBlendMode ==
                TerrainHeightBlendMode.Min
        )
        {
            float targetBaseHeight =
                EditorGUILayout.DelayedFloatField(
                    "Target Base Height",
                    stamp.TargetBaseHeight
                );

            if (
                !Mathf.Approximately(
                    targetBaseHeight,
                    stamp.TargetBaseHeight
                )
            )
            {
                if (
                    TerrainAuthoringModifierService
                        .SetStampTargetBaseHeight(
                            terrainAuthoringData,
                            worldSettings,
                            stamp.StableId,
                            targetBaseHeight,
                            out string targetBaseError
                        )
                )
                {
                    OnModifierMutationSucceeded();
                }
                else
                {
                    SetModifierAuthoringError(
                        targetBaseError
                    );
                }
            }

            float targetHeightRange =
                EditorGUILayout.DelayedFloatField(
                    "Target Height Range",
                    stamp.TargetHeightRange
                );

            if (
                !Mathf.Approximately(
                    targetHeightRange,
                    stamp.TargetHeightRange
                )
            )
            {
                if (
                    TerrainAuthoringModifierService
                        .SetStampTargetHeightRange(
                            terrainAuthoringData,
                            worldSettings,
                            stamp.StableId,
                            targetHeightRange,
                            out string targetRangeError
                        )
                )
                {
                    OnModifierMutationSucceeded();
                }
                else
                {
                    SetModifierAuthoringError(
                        targetRangeError
                    );
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "The selected terrain height blend mode is not supported by the current compositor.",
                MessageType.Error
            );
        }

        DrawStampFalloffSettings(
            stamp
        );

        /*
         * Reuse the existing interactive smoothing implementation so its
         * begin/update/commit/Escape behavior remains authoritative.
         */
        DrawStampSmoothingSettings(
            stamp
        );
    }

    // =====================================================
    // FOOTER / DIAGNOSTICS
    // =====================================================

    private void DrawCompactSelectedModifierFooter(
        TerrainHeightModifier modifier
    )
    {
        showSelectedModifierDiagnostics =
            EditorGUILayout.Foldout(
                showSelectedModifierDiagnostics,
                "Selected Stamp Diagnostics",
                true
            );

        if (
            showSelectedModifierDiagnostics
        )
        {
            DrawAffectedTileDiagnostics(
                modifier
            );
        }
    }

    private void DrawCompactGenericModifierInspector(
        TerrainHeightModifier modifier
    )
    {
        GUILayout.Label(
            "Selected Modifier",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Name",
            GetModifierDisplayName(
                modifier
            )
        );

        EditorGUILayout.LabelField(
            "Type",
            GetModifierTypeDisplayName(
                modifier
            )
        );

        EditorGUILayout.LabelField(
            "Blend Mode",
            modifier.BlendMode.ToString()
        );

        bool editingAllowed =
            IsModifierEditingAllowed();

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
        );

        bool enabled =
            EditorGUILayout.Toggle(
                "Enabled",
                modifier.Enabled
            );

        if (
            enabled !=
                modifier.Enabled
        )
        {
            ApplyModifierEnabled(
                modifier,
                enabled
            );
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "This modifier type does not have a dedicated production inspector yet.",
            MessageType.Info
        );

        DrawCompactSelectedModifierFooter(
            modifier
        );

        if (
            GUILayout.Button(
                "Frame In Scene View",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            if (
                !TerrainAuthoringModifierSceneUtility
                    .TryFrameModifier(
                        modifier,
                        out string frameError
                    )
            )
            {
                SetModifierAuthoringError(
                    frameError
                );
            }
            else
            {
                ClearModifierAuthoringError();
            }
        }
    }

    private static void DrawCompactSectionHeader(
        string title
    )
    {
        GUILayout.Label(
            title,
            EditorStyles.miniBoldLabel
        );
    }
}
