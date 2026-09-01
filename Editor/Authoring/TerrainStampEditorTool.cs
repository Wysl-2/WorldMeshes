using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Rendering;

[EditorTool("Terrain Stamp Tool")]
public sealed class TerrainStampEditorTool :
    EditorTool
{
    private const float MinimumStampSize =
        0.01f;

    private const float FootprintInteriorPickDistance =
        8f;

    private const float HandleScreenScale =
        0.075f;

    private int activeHandleControlId;

    private float activeHandlePlaneY =
        float.NaN;

    private string toolErrorMessage =
        "";

    public override GUIContent toolbarIcon =>
        new GUIContent(
            "Stamp",
            "Edit WorldMeshes terrain stamp modifiers in the Scene View."
        );

    public override void OnActivated()
    {
        activeHandleControlId =
            0;

        activeHandlePlaneY =
            float.NaN;

        toolErrorMessage =
            "";

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        SceneView.RepaintAll();
    }

    public override void OnWillBeDeactivated()
    {
        CancelActiveHandleEdit();

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        SceneView.RepaintAll();
    }

    public override void OnToolGUI(
        EditorWindow window
    )
    {
        if (!(window is SceneView))
        {
            return;
        }

        if (HandleEscapeCancel())
        {
            return;
        }

        if (
            EditorApplication.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            DrawSceneMessage(
                "Terrain stamp editing is unavailable while entering or running Play Mode.",
                MessageType.Info
            );

            return;
        }

        if (
            !TerrainAuthoringModifierContextUtility
                .TryLoadDefault(
                    out WorldSettings worldSettings,
                    out TerrainAuthoringData authoringData,
                    out string contextError
                )
        )
        {
            DrawSceneMessage(
                contextError,
                MessageType.Warning
            );

            return;
        }

        TerrainAuthoringModifierSelection
            .EnsureValidSelection(
                authoringData
            );

        float planeY =
            GetCurrentAuthoringPlaneY();

        CompareFunction oldZTest =
            Handles.zTest;

        Color oldColor =
            Handles.color;

        Handles.zTest =
            CompareFunction.Always;

        DrawAllStampFootprints(
            authoringData,
            planeY
        );

        if (
            TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    authoringData,
                    out TerrainHeightModifier selectedModifier,
                    out _
                )
            &&
            selectedModifier is
                TerrainStampModifier selectedStamp
        )
        {
            DrawSelectedStampHandles(
                authoringData,
                worldSettings,
                selectedStamp,
                planeY
            );
        }

        Handles.color =
            oldColor;

        Handles.zTest =
            oldZTest;

        if (
            !string.IsNullOrEmpty(
                toolErrorMessage
            )
        )
        {
            DrawSceneMessage(
                toolErrorMessage,
                MessageType.Error
            );
        }
    }

    // =====================================================
    // FOOTPRINT VISUALIZATION / PICKING
    // =====================================================

    private void DrawAllStampFootprints(
        TerrainAuthoringData authoringData,
        float planeY
    )
    {
        for (
            int index = 0;
            index <
                authoringData.HeightModifierCount;
            index++
        )
        {
            if (
                !(authoringData.HeightModifiers[index]
                    is TerrainStampModifier stamp)
            )
            {
                continue;
            }

            bool selected =
                TerrainAuthoringModifierSelection
                    .SelectedStableId ==
                stamp.StableId;

            int pickerControlId =
                GUIUtility.GetControlID(
                    FocusType.Passive
                );

            HandleFootprintPicking(
                pickerControlId,
                stamp,
                planeY
            );

            bool hovered =
                HandleUtility.nearestControl ==
                    pickerControlId;

            DrawStampFootprint(
                stamp,
                planeY,
                selected,
                hovered
            );
        }
    }

    private void HandleFootprintPicking(
        int controlId,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        if (
            stamp == null
            ||
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
        )
        {
            return;
        }

        Event currentEvent =
            Event.current;

        switch (
            currentEvent.GetTypeForControl(
                controlId
            )
        )
        {
            case EventType.Layout:
            case EventType.MouseMove:
            {
                float distance =
                    GetFootprintPickDistance(
                        stamp,
                        planeY
                    );

                HandleUtility.AddControl(
                    controlId,
                    distance
                );

                break;
            }

            case EventType.MouseDown:
            {
                if (
                    currentEvent.button == 0
                    &&
                    !currentEvent.alt
                    &&
                    GUIUtility.hotControl == 0
                    &&
                    HandleUtility.nearestControl ==
                        controlId
                )
                {
                    TerrainAuthoringModifierSelection
                        .Select(
                            stamp.StableId
                        );

                    toolErrorMessage =
                        "";

                    GUIUtility.hotControl =
                        controlId;

                    currentEvent.Use();
                }

                break;
            }

            case EventType.MouseUp:
            {
                if (
                    currentEvent.button == 0
                    &&
                    GUIUtility.hotControl ==
                        controlId
                )
                {
                    GUIUtility.hotControl =
                        0;

                    currentEvent.Use();
                }

                break;
            }
        }
    }

    private static float GetFootprintPickDistance(
        TerrainStampModifier stamp,
        float planeY
    )
    {
        StampFootprint footprint =
            GetStampFootprint(
                stamp
            );

        Vector3 minimumMinimum =
            new Vector3(
                footprint.MinimumX,
                planeY,
                footprint.MinimumZ
            );

        Vector3 maximumMinimum =
            new Vector3(
                footprint.MaximumX,
                planeY,
                footprint.MinimumZ
            );

        Vector3 maximumMaximum =
            new Vector3(
                footprint.MaximumX,
                planeY,
                footprint.MaximumZ
            );

        Vector3 minimumMaximum =
            new Vector3(
                footprint.MinimumX,
                planeY,
                footprint.MaximumZ
            );

        float edgeDistance =
            Mathf.Min(
                HandleUtility.DistanceToLine(
                    minimumMinimum,
                    maximumMinimum
                ),
                HandleUtility.DistanceToLine(
                    maximumMinimum,
                    maximumMaximum
                ),
                HandleUtility.DistanceToLine(
                    maximumMaximum,
                    minimumMaximum
                ),
                HandleUtility.DistanceToLine(
                    minimumMaximum,
                    minimumMinimum
                )
            );

        Vector3 gui0 =
            HandleUtility.WorldToGUIPointWithDepth(
                minimumMinimum
            );

        Vector3 gui1 =
            HandleUtility.WorldToGUIPointWithDepth(
                maximumMinimum
            );

        Vector3 gui2 =
            HandleUtility.WorldToGUIPointWithDepth(
                maximumMaximum
            );

        Vector3 gui3 =
            HandleUtility.WorldToGUIPointWithDepth(
                minimumMaximum
            );

        if (
            gui0.z <= 0f
            ||
            gui1.z <= 0f
            ||
            gui2.z <= 0f
            ||
            gui3.z <= 0f
        )
        {
            return edgeDistance;
        }

        bool inside =
            IsPointInsideConvexQuad(
                Event.current.mousePosition,
                gui0,
                gui1,
                gui2,
                gui3
            );

        return
            inside
                ? Mathf.Min(
                    edgeDistance,
                    FootprintInteriorPickDistance
                )
                : edgeDistance;
    }

    private static bool IsPointInsideConvexQuad(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d
    )
    {
        float ab =
            Cross2D(
                b - a,
                point - a
            );

        float bc =
            Cross2D(
                c - b,
                point - b
            );

        float cd =
            Cross2D(
                d - c,
                point - c
            );

        float da =
            Cross2D(
                a - d,
                point - d
            );

        bool hasNegative =
            ab < 0f
            ||
            bc < 0f
            ||
            cd < 0f
            ||
            da < 0f;

        bool hasPositive =
            ab > 0f
            ||
            bc > 0f
            ||
            cd > 0f
            ||
            da > 0f;

        return
            !(hasNegative && hasPositive);
    }

    private static float Cross2D(
        Vector2 a,
        Vector2 b
    )
    {
        return
            a.x * b.y -
            a.y * b.x;
    }

    private static void DrawStampFootprint(
        TerrainStampModifier stamp,
        float planeY,
        bool selected,
        bool hovered
    )
    {
        StampFootprint footprint =
            GetStampFootprint(
                stamp
            );

        Vector3 p0 =
            new Vector3(
                footprint.MinimumX,
                planeY,
                footprint.MinimumZ
            );

        Vector3 p1 =
            new Vector3(
                footprint.MaximumX,
                planeY,
                footprint.MinimumZ
            );

        Vector3 p2 =
            new Vector3(
                footprint.MaximumX,
                planeY,
                footprint.MaximumZ
            );

        Vector3 p3 =
            new Vector3(
                footprint.MinimumX,
                planeY,
                footprint.MaximumZ
            );

        Color oldColor =
            Handles.color;

        if (selected)
        {
            Handles.color =
                Handles.selectedColor;
        }
        else if (hovered)
        {
            Handles.color =
                Handles.preselectionColor;
        }
        else
        {
            Handles.color =
                Handles.secondaryColor;
        }

        if (!stamp.Enabled)
        {
            Handles.DrawDottedLine(
                p0,
                p1,
                4f
            );

            Handles.DrawDottedLine(
                p1,
                p2,
                4f
            );

            Handles.DrawDottedLine(
                p2,
                p3,
                4f
            );

            Handles.DrawDottedLine(
                p3,
                p0,
                4f
            );
        }
        else
        {
            Handles.DrawAAPolyLine(
                2f,
                p0,
                p1,
                p2,
                p3,
                p0
            );
        }

        Handles.color =
            oldColor;
    }

    // =====================================================
    // SELECTED STAMP HANDLES
    // =====================================================

    private void DrawSelectedStampHandles(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        if (stamp == null)
        {
            return;
        }

        Color oldColor =
            Handles.color;

        Handles.color =
            Handles.selectedColor;

        DrawMoveHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY
        );

        DrawEdgeHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MinimumX
        );

        DrawEdgeHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MaximumX
        );

        DrawEdgeHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MinimumZ
        );

        DrawEdgeHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MaximumZ
        );

        DrawCornerHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampCorner.MinimumXMinimumZ
        );

        DrawCornerHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampCorner.MaximumXMinimumZ
        );

        DrawCornerHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampCorner.MaximumXMaximumZ
        );

        DrawCornerHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampCorner.MinimumXMaximumZ
        );

        Handles.color =
            oldColor;
    }

    private void DrawMoveHandle(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        StampFootprint footprint =
            GetStampFootprint(
                stamp
            );

        Vector3 center =
            new Vector3(
                footprint.CenterX,
                planeY,
                footprint.CenterZ
            );

        float handleSize =
            GetHandleSize(
                center
            );

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Vector3 movedCenter =
            Handles.Slider2D(
                center,
                Vector3.up,
                Vector3.right,
                Vector3.forward,
                handleSize,
                Handles.RectangleHandleCap,
                Vector2.zero,
                false
            );

        bool changed =
            EditorGUI.EndChangeCheck();

        int hotAfter =
            GUIUtility.hotControl;

        if (
            !UpdateHandleTransactionState(
                hotBefore,
                hotAfter,
                planeY,
                authoringData,
                worldSettings,
                stamp,
                "Move Terrain Stamp Modifier"
            )
        )
        {
            return;
        }

        if (
            changed
            &&
            IsActiveHandleControl(
                hotBefore,
                hotAfter
            )
        )
        {
            ApplyInteractiveFootprint(
                new Vector2(
                    movedCenter.x,
                    movedCenter.z
                ),
                stamp.SizeXZ
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );
    }

    private void DrawEdgeHandle(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY,
        StampEdge edge
    )
    {
        StampFootprint footprint =
            GetStampFootprint(
                stamp
            );

        Vector3 handlePosition;
        Vector3 direction;

        switch (edge)
        {
            case StampEdge.MinimumX:
                handlePosition =
                    new Vector3(
                        footprint.MinimumX,
                        planeY,
                        footprint.CenterZ
                    );
                direction =
                    Vector3.right;
                break;

            case StampEdge.MaximumX:
                handlePosition =
                    new Vector3(
                        footprint.MaximumX,
                        planeY,
                        footprint.CenterZ
                    );
                direction =
                    Vector3.right;
                break;

            case StampEdge.MinimumZ:
                handlePosition =
                    new Vector3(
                        footprint.CenterX,
                        planeY,
                        footprint.MinimumZ
                    );
                direction =
                    Vector3.forward;
                break;

            default:
                handlePosition =
                    new Vector3(
                        footprint.CenterX,
                        planeY,
                        footprint.MaximumZ
                    );
                direction =
                    Vector3.forward;
                break;
        }

        float handleSize =
            GetHandleSize(
                handlePosition
            );

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Vector3 moved =
            Handles.Slider(
                handlePosition,
                direction,
                handleSize,
                Handles.CubeHandleCap,
                0f
            );

        bool changed =
            EditorGUI.EndChangeCheck();

        int hotAfter =
            GUIUtility.hotControl;

        if (
            !UpdateHandleTransactionState(
                hotBefore,
                hotAfter,
                planeY,
                authoringData,
                worldSettings,
                stamp,
                "Resize Terrain Stamp Modifier"
            )
        )
        {
            return;
        }

        if (
            changed
            &&
            IsActiveHandleControl(
                hotBefore,
                hotAfter
            )
        )
        {
            Vector2 position;
            Vector2 size;

            CalculateEdgeFootprint(
                footprint,
                edge,
                moved,
                out position,
                out size
            );

            ApplyInteractiveFootprint(
                position,
                size
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );
    }

    private void DrawCornerHandle(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY,
        StampCorner corner
    )
    {
        StampFootprint footprint =
            GetStampFootprint(
                stamp
            );

        Vector3 handlePosition =
            GetCornerPosition(
                footprint,
                corner,
                planeY
            );

        float handleSize =
            GetHandleSize(
                handlePosition
            );

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Vector3 moved =
            Handles.Slider2D(
                handlePosition,
                Vector3.up,
                Vector3.right,
                Vector3.forward,
                handleSize,
                Handles.CubeHandleCap,
                Vector2.zero,
                false
            );

        bool changed =
            EditorGUI.EndChangeCheck();

        int hotAfter =
            GUIUtility.hotControl;

        if (
            !UpdateHandleTransactionState(
                hotBefore,
                hotAfter,
                planeY,
                authoringData,
                worldSettings,
                stamp,
                "Resize Terrain Stamp Modifier"
            )
        )
        {
            return;
        }

        if (
            changed
            &&
            IsActiveHandleControl(
                hotBefore,
                hotAfter
            )
        )
        {
            Vector2 position;
            Vector2 size;

            CalculateCornerFootprint(
                footprint,
                corner,
                moved,
                out position,
                out size
            );

            ApplyInteractiveFootprint(
                position,
                size
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );
    }

    // =====================================================
    // INTERACTIVE HANDLE TRANSACTION BRIDGE
    // =====================================================

    private bool UpdateHandleTransactionState(
        int hotBefore,
        int hotAfter,
        float planeY,
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        string undoLabel
    )
    {
        if (
            activeHandleControlId == 0
            &&
            hotBefore == 0
            &&
            hotAfter != 0
        )
        {
            if (
                !TerrainAuthoringModifierService
                    .BeginInteractiveModifierEdit(
                        authoringData,
                        worldSettings,
                        stamp.StableId,
                        undoLabel,
                        out string beginError
                    )
            )
            {
                toolErrorMessage =
                    beginError;

                GUIUtility.hotControl =
                    0;

                return false;
            }

            activeHandleControlId =
                hotAfter;

            activeHandlePlaneY =
                planeY;

            toolErrorMessage =
                "";
        }

        return true;
    }

    private bool IsActiveHandleControl(
        int hotBefore,
        int hotAfter
    )
    {
        return
            activeHandleControlId != 0
            &&
            (
                hotBefore ==
                    activeHandleControlId
                ||
                hotAfter ==
                    activeHandleControlId
            );
    }

    private void ApplyInteractiveFootprint(
        Vector2 positionXZ,
        Vector2 sizeXZ
    )
    {
        if (
            !TerrainAuthoringModifierService
                .UpdateInteractiveStampFootprint(
                    positionXZ,
                    sizeXZ,
                    out string updateError
                )
        )
        {
            toolErrorMessage =
                updateError;

            CancelActiveHandleEdit();

            return;
        }

        toolErrorMessage =
            "";

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();
    }

    private void CommitIfHandleReleased(
        int hotBefore,
        int hotAfter
    )
    {
        if (
            activeHandleControlId == 0
            ||
            hotBefore !=
                activeHandleControlId
            ||
            hotAfter ==
                activeHandleControlId
        )
        {
            return;
        }

        int releasedControlId =
            activeHandleControlId;

        activeHandleControlId =
            0;

        activeHandlePlaneY =
            float.NaN;

        if (
            !TerrainAuthoringModifierService
                .CommitInteractiveEdit(
                    out string commitError
                )
        )
        {
            toolErrorMessage =
                commitError;

            return;
        }

        if (
            GUIUtility.hotControl ==
                releasedControlId
        )
        {
            GUIUtility.hotControl =
                0;
        }

        toolErrorMessage =
            "";

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();
    }

    private bool HandleEscapeCancel()
    {
        Event currentEvent =
            Event.current;

        if (
            currentEvent.type !=
                EventType.KeyDown
            ||
            currentEvent.keyCode !=
                KeyCode.Escape
            ||
            !TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
        )
        {
            return false;
        }

        CancelActiveHandleEdit();

        GUIUtility.hotControl =
            0;

        currentEvent.Use();

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        return true;
    }

    private void CancelActiveHandleEdit()
    {
        bool hadActiveEdit =
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit;

        if (hadActiveEdit)
        {
            TerrainAuthoringModifierService
                .CancelInteractiveEdit(
                    out string cancelError
                );

            if (
                !string.IsNullOrEmpty(
                    cancelError
                )
            )
            {
                toolErrorMessage =
                    cancelError;
            }
        }

        activeHandleControlId =
            0;

        activeHandlePlaneY =
            float.NaN;

        if (hadActiveEdit)
        {
            TerrainAuthoringModifierSelection
                .NotifyModifierDataChanged();
        }
    }

    // =====================================================
    // FOOTPRINT GEOMETRY
    // =====================================================

    private static StampFootprint GetStampFootprint(
        TerrainStampModifier stamp
    )
    {
        Vector2 position =
            stamp.PositionXZ;

        Vector2 size =
            stamp.SizeXZ;

        float halfX =
            size.x *
            0.5f;

        float halfZ =
            size.y *
            0.5f;

        return
            new StampFootprint(
                position.x -
                    halfX,
                position.x +
                    halfX,
                position.y -
                    halfZ,
                position.y +
                    halfZ
            );
    }

    private static void CalculateEdgeFootprint(
        StampFootprint source,
        StampEdge edge,
        Vector3 moved,
        out Vector2 position,
        out Vector2 size
    )
    {
        float minimumX =
            source.MinimumX;

        float maximumX =
            source.MaximumX;

        float minimumZ =
            source.MinimumZ;

        float maximumZ =
            source.MaximumZ;

        switch (edge)
        {
            case StampEdge.MinimumX:
                minimumX =
                    Mathf.Min(
                        moved.x,
                        maximumX -
                            MinimumStampSize
                    );
                break;

            case StampEdge.MaximumX:
                maximumX =
                    Mathf.Max(
                        moved.x,
                        minimumX +
                            MinimumStampSize
                    );
                break;

            case StampEdge.MinimumZ:
                minimumZ =
                    Mathf.Min(
                        moved.z,
                        maximumZ -
                            MinimumStampSize
                    );
                break;

            case StampEdge.MaximumZ:
                maximumZ =
                    Mathf.Max(
                        moved.z,
                        minimumZ +
                            MinimumStampSize
                    );
                break;
        }

        ConvertBoundsToFootprint(
            minimumX,
            maximumX,
            minimumZ,
            maximumZ,
            out position,
            out size
        );
    }

    private static void CalculateCornerFootprint(
        StampFootprint source,
        StampCorner corner,
        Vector3 moved,
        out Vector2 position,
        out Vector2 size
    )
    {
        float minimumX =
            source.MinimumX;

        float maximumX =
            source.MaximumX;

        float minimumZ =
            source.MinimumZ;

        float maximumZ =
            source.MaximumZ;

        switch (corner)
        {
            case StampCorner.MinimumXMinimumZ:
                minimumX =
                    Mathf.Min(
                        moved.x,
                        maximumX -
                            MinimumStampSize
                    );

                minimumZ =
                    Mathf.Min(
                        moved.z,
                        maximumZ -
                            MinimumStampSize
                    );
                break;

            case StampCorner.MaximumXMinimumZ:
                maximumX =
                    Mathf.Max(
                        moved.x,
                        minimumX +
                            MinimumStampSize
                    );

                minimumZ =
                    Mathf.Min(
                        moved.z,
                        maximumZ -
                            MinimumStampSize
                    );
                break;

            case StampCorner.MaximumXMaximumZ:
                maximumX =
                    Mathf.Max(
                        moved.x,
                        minimumX +
                            MinimumStampSize
                    );

                maximumZ =
                    Mathf.Max(
                        moved.z,
                        minimumZ +
                            MinimumStampSize
                    );
                break;

            case StampCorner.MinimumXMaximumZ:
                minimumX =
                    Mathf.Min(
                        moved.x,
                        maximumX -
                            MinimumStampSize
                    );

                maximumZ =
                    Mathf.Max(
                        moved.z,
                        minimumZ +
                            MinimumStampSize
                    );
                break;
        }

        ConvertBoundsToFootprint(
            minimumX,
            maximumX,
            minimumZ,
            maximumZ,
            out position,
            out size
        );
    }

    private static void ConvertBoundsToFootprint(
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ,
        out Vector2 position,
        out Vector2 size
    )
    {
        float safeMaximumX =
            Mathf.Max(
                maximumX,
                minimumX +
                    MinimumStampSize
            );

        float safeMaximumZ =
            Mathf.Max(
                maximumZ,
                minimumZ +
                    MinimumStampSize
            );

        position =
            new Vector2(
                (
                    minimumX +
                    safeMaximumX
                ) *
                0.5f,
                (
                    minimumZ +
                    safeMaximumZ
                ) *
                0.5f
            );

        size =
            new Vector2(
                safeMaximumX -
                    minimumX,
                safeMaximumZ -
                    minimumZ
            );
    }

    private static Vector3 GetCornerPosition(
        StampFootprint footprint,
        StampCorner corner,
        float planeY
    )
    {
        switch (corner)
        {
            case StampCorner.MinimumXMinimumZ:
                return
                    new Vector3(
                        footprint.MinimumX,
                        planeY,
                        footprint.MinimumZ
                    );

            case StampCorner.MaximumXMinimumZ:
                return
                    new Vector3(
                        footprint.MaximumX,
                        planeY,
                        footprint.MinimumZ
                    );

            case StampCorner.MaximumXMaximumZ:
                return
                    new Vector3(
                        footprint.MaximumX,
                        planeY,
                        footprint.MaximumZ
                    );

            default:
                return
                    new Vector3(
                        footprint.MinimumX,
                        planeY,
                        footprint.MaximumZ
                    );
        }
    }

    private static float GetHandleSize(
        Vector3 position
    )
    {
        return
            Mathf.Max(
                0.01f,
                HandleUtility.GetHandleSize(
                    position
                ) *
                HandleScreenScale
            );
    }

    // =====================================================
    // AUTHORING PLANE / ERROR UI
    // =====================================================

    private float GetCurrentAuthoringPlaneY()
    {
        if (
            activeHandleControlId != 0
            &&
            IsFinite(
                activeHandlePlaneY
            )
        )
        {
            return
                activeHandlePlaneY;
        }

        float minimum =
            TerrainAuthoringPreviewService
                .MinimumPreviewHeight;

        float maximum =
            TerrainAuthoringPreviewService
                .MaximumPreviewHeight;

        if (
            TerrainAuthoringPreviewService
                .CacheReady
            &&
            IsFinite(
                minimum
            )
            &&
            IsFinite(
                maximum
            )
            &&
            maximum >=
                minimum
        )
        {
            return
                (
                    minimum +
                    maximum
                ) *
                0.5f;
        }

        return 0f;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

    private static void DrawSceneMessage(
        string message,
        MessageType messageType
    )
    {
        if (
            string.IsNullOrEmpty(
                message
            )
        )
        {
            return;
        }

        Handles.BeginGUI();

        GUILayout.BeginArea(
            new Rect(
                10f,
                10f,
                420f,
                90f
            )
        );

        EditorGUILayout.HelpBox(
            message,
            messageType
        );

        GUILayout.EndArea();

        Handles.EndGUI();
    }

    private enum StampEdge
    {
        MinimumX,
        MaximumX,
        MinimumZ,
        MaximumZ
    }

    private enum StampCorner
    {
        MinimumXMinimumZ,
        MaximumXMinimumZ,
        MaximumXMaximumZ,
        MinimumXMaximumZ
    }

    private readonly struct StampFootprint
    {
        public readonly float MinimumX;
        public readonly float MaximumX;
        public readonly float MinimumZ;
        public readonly float MaximumZ;

        public float CenterX =>
            (
                MinimumX +
                MaximumX
            ) *
            0.5f;

        public float CenterZ =>
            (
                MinimumZ +
                MaximumZ
            ) *
            0.5f;

        public StampFootprint(
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ
        )
        {
            MinimumX =
                minimumX;

            MaximumX =
                maximumX;

            MinimumZ =
                minimumZ;

            MaximumZ =
                maximumZ;
        }
    }
}
