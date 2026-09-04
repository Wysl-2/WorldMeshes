using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private const float ModifierStackMaximumHeight =
        220f;

    private const float DefaultStampHeightDelta =
        10f;

    private const float DefaultStampFalloff =
        0.25f;

    [SerializeField]
    private Vector2 modifierStackScrollPosition =
        Vector2.zero;

    [SerializeField]
    private bool showAffectedModifierTiles;

    private string modifierAuthoringErrorMessage =
        "";

    // =====================================================
    // PRODUCTION MODIFIER AUTHORING
    // =====================================================

    private void DrawModifierAuthoringSettings()
    {
        TerrainAuthoringModifierSelection
            .EnsureValidSelection(
                terrainAuthoringData
            );

        DrawModifierStackPanel();

        GUILayout.Space(6f);

        DrawSelectedModifierPanel();

        DrawModifierAuthoringError();
    }

    // =====================================================
    // MODIFIER STACK
    // =====================================================

    private void DrawModifierStackPanel()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Terrain Modifiers",
            EditorStyles.boldLabel
        );

        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            EditorGUILayout.HelpBox(
                "WorldSettings and TerrainAuthoringData must be assigned " +
                "before terrain modifiers can be authored.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        bool editingAllowed =
            IsModifierEditingAllowed();

        if (!editingAllowed)
        {
            EditorGUILayout.HelpBox(
                "Terrain modifier editing is disabled while entering or " +
                "running Play Mode. Selection and inspection remain available.",
                MessageType.Info
            );
        }

        EditorGUILayout.LabelField(
            "Modifier Count",
            terrainAuthoringData
                .HeightModifierCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Evaluation Order",
            "Top to bottom"
        );

        GUILayout.Space(4f);

        DrawModifierRows(
            editingAllowed
        );

        GUILayout.Space(5f);

        DrawModifierStackCommands(
            editingAllowed
        );

        GUILayout.EndVertical();
    }

    private void DrawModifierRows(
        bool editingAllowed
    )
    {
        int modifierCount =
            terrainAuthoringData
                .HeightModifierCount;

        if (modifierCount <= 0)
        {
            EditorGUILayout.HelpBox(
                "No terrain modifiers exist. Add a stamp modifier to begin " +
                "non-destructive terrain authoring.",
                MessageType.None
            );

            return;
        }

        float rowHeight =
            EditorGUIUtility.singleLineHeight +
            3f;

        float stackHeight =
            Mathf.Min(
                ModifierStackMaximumHeight,
                modifierCount *
                    rowHeight +
                6f
            );

        modifierStackScrollPosition =
            EditorGUILayout.BeginScrollView(
                modifierStackScrollPosition,
                false,
                false,
                GUILayout.Height(
                    stackHeight
                )
            );

        IReadOnlyList<TerrainHeightModifier> modifiers =
            terrainAuthoringData
                .HeightModifiers;

        for (
            int index = 0;
            index < modifiers.Count;
            index++
        )
        {
            TerrainHeightModifier modifier =
                modifiers[
                    index
                ];

            DrawModifierRow(
                modifier,
                index,
                editingAllowed
            );
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawModifierRow(
        TerrainHeightModifier modifier,
        int modifierIndex,
        bool editingAllowed
    )
    {
        if (modifier == null)
        {
            EditorGUILayout.HelpBox(
                "A null modifier exists in TerrainAuthoringData.",
                MessageType.Error
            );

            return;
        }

        GUILayout.BeginHorizontal();

        GUILayout.Label(
            (modifierIndex + 1).ToString(),
            EditorStyles.miniLabel,
            GUILayout.Width(22f)
        );

        EditorGUI.BeginDisabledGroup(
            !editingAllowed
        );

        bool enabled =
            GUILayout.Toggle(
                modifier.Enabled,
                GUIContent.none,
                GUILayout.Width(18f)
            );

        EditorGUI.EndDisabledGroup();

        if (
            editingAllowed
            &&
            enabled !=
                modifier.Enabled
        )
        {
            ApplyModifierEnabled(
                modifier,
                enabled
            );
        }

        bool selected =
            TerrainAuthoringModifierSelection
                .SelectedStableId ==
            modifier.StableId;

        string displayName =
            GetModifierDisplayName(
                modifier
            );

        if (!modifier.Enabled)
        {
            displayName +=
                " (Disabled)";
        }

        bool pressed =
            GUILayout.Toggle(
                selected,
                new GUIContent(
                    displayName,
                    GetModifierTooltip(
                        modifier
                    )
                ),
                EditorStyles.miniButton,
                GUILayout.ExpandWidth(true)
            );

        if (
            pressed
            &&
            !selected
        )
        {
            TerrainAuthoringModifierSelection
                .Select(
                    modifier.StableId
                );

            ClearModifierAuthoringError();
        }

        GUILayout.Label(
            GetModifierTypeLabel(
                modifier
            ),
            EditorStyles.miniLabel,
            GUILayout.Width(48f)
        );

        GUILayout.EndHorizontal();
    }

    private void DrawModifierStackCommands(
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
        );

        if (
            GUILayout.Button(
                "+ Add Stamp"
            )
        )
        {
            AddStampModifier();
        }

        EditorGUI.EndDisabledGroup();

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
            selectedIndex <= 0
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
    // SELECTED MODIFIER INSPECTOR
    // =====================================================

    private void DrawSelectedModifierPanel()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Selected Modifier",
            EditorStyles.boldLabel
        );

        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
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
            EditorGUILayout.HelpBox(
                "Select a modifier from the stack to inspect and edit it.",
                MessageType.None
            );

            GUILayout.EndVertical();
            return;
        }

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

        if (
            modifier is
                TerrainStampModifier stamp
        )
        {
            DrawStampModifierInspector(
                stamp
            );
        }
        else
        {
            EditorGUILayout.HelpBox(
                "This modifier type does not have a production inspector yet.",
                MessageType.Info
            );
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(6f);

        DrawAffectedTileDiagnostics(
            modifier
        );

        GUILayout.Space(5f);

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

        GUILayout.EndVertical();
    }

    private void DrawStampModifierInspector(
        TerrainStampModifier stamp
    )
    {
        GUILayout.Space(6f);

        GUILayout.Label(
            "Height Stamp",
            EditorStyles.miniBoldLabel
        );

        TerrainHeightStampAsset stampAsset =
            (TerrainHeightStampAsset)
            EditorGUILayout.ObjectField(
                "Stamp Asset",
                stamp.StampAsset,
                typeof(TerrainHeightStampAsset),
                false
            );

        if (
            stampAsset !=
                stamp.StampAsset
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampAsset(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        stampAsset,
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

        if (stamp.StampAsset == null)
        {
            EditorGUILayout.HelpBox(
                "This modifier has no stamp asset assigned and therefore " +
                "does not currently contribute terrain deformation.",
                MessageType.Warning
            );
        }

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

        float falloff =
            EditorGUILayout.DelayedFloatField(
                "Falloff (0-1)",
                stamp.Falloff
            );

        if (
            !Mathf.Approximately(
                falloff,
                stamp.Falloff
            )
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampFalloff(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        falloff,
                        out string falloffError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    falloffError
                );
            }
        }

        DrawStampSmoothingSettings(
            stamp
        );
    }

    // =====================================================
    // AFFECTED TILE DIAGNOSTICS
    // =====================================================

    private void DrawAffectedTileDiagnostics(
        TerrainHeightModifier modifier
    )
    {
        if (
            modifier == null
            ||
            worldSettings == null
        )
        {
            return;
        }

        Bounds affectedBounds =
            modifier.GetAffectedWorldBounds();

        List<Vector2Int> affectedTiles =
            new List<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                affectedBounds,
                affectedTiles,
                1
            );

        affectedTiles.Sort(
            CompareTileCoordinates
        );

        EditorGUILayout.LabelField(
            "World Coverage",
            GetModifierWorldCoverageLabel(
                affectedBounds
            )
        );

        EditorGUILayout.LabelField(
            "Affected Height Tiles",
            affectedTiles.Count.ToString()
        );

        showAffectedModifierTiles =
            EditorGUILayout.Foldout(
                showAffectedModifierTiles,
                "Tile Coordinates",
                true
            );

        if (!showAffectedModifierTiles)
        {
            return;
        }

        if (affectedTiles.Count <= 0)
        {
            EditorGUILayout.HelpBox(
                "The modifier footprint does not affect any in-world " +
                "height tiles.",
                MessageType.Info
            );

            return;
        }

        StringBuilder summary =
            new StringBuilder();

        for (
            int index = 0;
            index < affectedTiles.Count;
            index++
        )
        {
            if (index > 0)
            {
                summary.Append(
                    index % 4 == 0
                        ? "\n"
                        : "  "
                );
            }

            Vector2Int tile =
                affectedTiles[
                    index
                ];

            summary.Append('(');
            summary.Append(tile.x);
            summary.Append(", ");
            summary.Append(tile.y);
            summary.Append(')');
        }

        EditorGUILayout.TextArea(
            summary.ToString(),
            GUILayout.MinHeight(36f)
        );
    }

    private string GetModifierWorldCoverageLabel(
        Bounds bounds
    )
    {
        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        bool outside =
            bounds.max.x < 0f
            ||
            bounds.max.z < 0f
            ||
            bounds.min.x >
                worldSize.x
            ||
            bounds.min.z >
                worldSize.y;

        if (outside)
        {
            return
                "Outside World";
        }

        bool partiallyOutside =
            bounds.min.x < 0f
            ||
            bounds.min.z < 0f
            ||
            bounds.max.x >
                worldSize.x
            ||
            bounds.max.z >
                worldSize.y;

        return
            partiallyOutside
                ? "Partially Outside World"
                : "In World";
    }

    private static int CompareTileCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int zComparison =
            left.y.CompareTo(
                right.y
            );

        if (zComparison != 0)
        {
            return
                zComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }

    // =====================================================
    // STACK OPERATIONS
    // =====================================================

    private void AddStampModifier()
    {
        Vector2 positionXZ =
            GetDefaultNewStampPositionXZ();

        float defaultSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        if (
            !TerrainAuthoringModifierService
                .AddStampModifier(
                    terrainAuthoringData,
                    worldSettings,
                    null,
                    positionXZ,
                    new Vector2(
                        defaultSize,
                        defaultSize
                    ),
                    DefaultStampHeightDelta,
                    DefaultStampFalloff,
                    out string stableId,
                    out string errorMessage
                )
        )
        {
            SetModifierAuthoringError(
                errorMessage
            );

            return;
        }

        TerrainAuthoringModifierSelection
            .Select(
                stableId
            );

        OnModifierMutationSucceeded();
    }

    private void DuplicateSelectedModifier()
    {
        string stableId =
            TerrainAuthoringModifierSelection
                .SelectedStableId;

        if (
            !TerrainAuthoringModifierService
                .DuplicateModifier(
                    terrainAuthoringData,
                    worldSettings,
                    stableId,
                    out string duplicateStableId,
                    out string errorMessage
                )
        )
        {
            SetModifierAuthoringError(
                errorMessage
            );

            return;
        }

        TerrainAuthoringModifierSelection
            .Select(
                duplicateStableId
            );

        OnModifierMutationSucceeded();
    }

    private void DeleteSelectedModifier()
    {
        if (
            !TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    terrainAuthoringData,
                    out _,
                    out int selectedIndex
                )
        )
        {
            return;
        }

        string stableId =
            TerrainAuthoringModifierSelection
                .SelectedStableId;

        if (
            !TerrainAuthoringModifierService
                .RemoveModifier(
                    terrainAuthoringData,
                    worldSettings,
                    stableId,
                    out string errorMessage
                )
        )
        {
            SetModifierAuthoringError(
                errorMessage
            );

            return;
        }

        int remainingCount =
            terrainAuthoringData
                .HeightModifierCount;

        if (remainingCount <= 0)
        {
            TerrainAuthoringModifierSelection
                .Clear();
        }
        else
        {
            TerrainAuthoringModifierSelection
                .SelectIndex(
                    terrainAuthoringData,
                    Mathf.Min(
                        selectedIndex,
                        remainingCount -
                            1
                    )
                );
        }

        OnModifierMutationSucceeded();
    }

    private void ReorderSelectedModifier(
        int newIndex
    )
    {
        string stableId =
            TerrainAuthoringModifierSelection
                .SelectedStableId;

        if (
            !TerrainAuthoringModifierService
                .ReorderModifier(
                    terrainAuthoringData,
                    worldSettings,
                    stableId,
                    newIndex,
                    out string errorMessage
                )
        )
        {
            SetModifierAuthoringError(
                errorMessage
            );

            return;
        }

        /*
         * Selection identity remains unchanged because it is StableId based.
         */
        OnModifierMutationSucceeded();
    }

    private void ApplyModifierEnabled(
        TerrainHeightModifier modifier,
        bool enabled
    )
    {
        if (modifier == null)
        {
            return;
        }

        if (
            TerrainAuthoringModifierService
                .SetModifierEnabled(
                    terrainAuthoringData,
                    worldSettings,
                    modifier.StableId,
                    enabled,
                    out string errorMessage
                )
        )
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

    private Vector2 GetDefaultNewStampPositionXZ()
    {
        Vector3 target =
            TerrainClipmapLayoutUtility
                .CalculateWorldCenterPosition(
                    worldSettings,
                    0f
                );

        SceneView sceneView =
            SceneView.lastActiveSceneView;

        if (sceneView != null)
        {
            target =
                sceneView.pivot;
        }

        target =
            TerrainClipmapLayoutUtility
                .ClampTargetXZToWorld(
                    worldSettings,
                    target
                );

        return
            new Vector2(
                target.x,
                target.z
            );
    }

    // =====================================================
    // DISPLAY HELPERS
    // =====================================================

    private static string GetModifierDisplayName(
        TerrainHeightModifier modifier
    )
    {
        if (
            modifier is
                TerrainStampModifier stamp
        )
        {
            if (stamp.StampAsset != null)
            {
                return
                    stamp.StampAsset.name;
            }

            return
                "Unassigned Stamp";
        }

        return
            modifier != null
                ? modifier.GetType().Name
                : "Unknown Modifier";
    }

    private static string GetModifierTypeLabel(
        TerrainHeightModifier modifier
    )
    {
        return
            modifier is
                TerrainStampModifier
                ? "Stamp"
                : "Other";
    }

    private static string GetModifierTypeDisplayName(
        TerrainHeightModifier modifier
    )
    {
        return
            modifier is
                TerrainStampModifier
                ? "Height Stamp"
                : modifier != null
                    ? modifier.GetType().Name
                    : "Unknown";
    }

    private static string GetModifierTooltip(
        TerrainHeightModifier modifier
    )
    {
        if (
            modifier is
                TerrainStampModifier stamp
        )
        {
            return
                stamp.StampAsset != null
                    ? "Height stamp modifier using " +
                        stamp.StampAsset.name +
                        "."
                    : "Height stamp modifier with no stamp asset assigned.";
        }

        return
            "Terrain height modifier.";
    }

    private static bool IsModifierEditingAllowed()
    {
        return
            !Application.isPlaying
            &&
            !EditorApplication
                .isPlayingOrWillChangePlaymode;
    }

    // =====================================================
    // STATUS / REPAINT
    // =====================================================

    private void OnModifierMutationSucceeded()
    {
        ClearModifierAuthoringError();

        TerrainAuthoringModifierSelection
            .EnsureValidSelection(
                terrainAuthoringData
            );

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        Repaint();
    }

    private void SetModifierAuthoringError(
        string message
    )
    {
        modifierAuthoringErrorMessage =
            string.IsNullOrEmpty(
                message
            )
                ? "The terrain modifier operation failed."
                : message;

        Repaint();
    }

    private void ClearModifierAuthoringError()
    {
        modifierAuthoringErrorMessage =
            "";
    }

    private void DrawModifierAuthoringError()
    {
        if (
            string.IsNullOrEmpty(
                modifierAuthoringErrorMessage
            )
        )
        {
            return;
        }

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            modifierAuthoringErrorMessage,
            MessageType.Error
        );
    }
}
