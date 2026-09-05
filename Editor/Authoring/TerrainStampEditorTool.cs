using System.Collections.Generic;
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

    private const float FootprintCyclePickDistance =
        8.5f;

    private const float OverlapCycleMouseTolerance =
        6f;

    private const double OverlapCycleTimeSeconds =
        1.0;

    private const float HandleScreenScale =
        0.075f;

    private const float MoveHandleScaleMultiplier =
        2f;

    private const float FalloffHandleScaleMultiplier =
        0.65f;

    private const float FalloffHandleTangentOffsetFraction =
        0.18f;

    private const float RotationSnapDegrees =
        15f;

    private const float RotationHandleGapScaleMultiplier =
        4f;

    private const float RotationLabelOffsetScaleMultiplier =
        1.5f;

    private int activeHandleControlId;

    private float activeHandlePlaneY =
        float.NaN;

    private bool activeResizeStateValid;

    private float activeResizeAspectRatio =
        1f;

    /*
     * Resize calculations are always evaluated from the immutable transform
     * captured when the handle first becomes hot. Handle drawing still uses
     * the current live footprint, avoiding cumulative drift while preserving
     * normal Unity handle feedback.
     */
    private StampFootprint activeResizeInitialFootprint;

    private Vector2 lastFootprintSelectionClickPosition =
        new Vector2(
            float.NaN,
            float.NaN
        );

    private double lastFootprintSelectionClickTime =
        double.NegativeInfinity;

    private readonly List<StampPickCandidate>
        stampPickCandidates =
            new List<StampPickCandidate>();

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

        ResetActiveResizeState();
        ResetOverlapSelectionCycle();

        toolErrorMessage =
            "";

        TerrainAuthoringModifierSelection
            .NotifyModifierDataChanged();

        SceneView.RepaintAll();
    }

    public override void OnWillBeDeactivated()
    {
        CancelActiveHandleEdit();

        ResetOverlapSelectionCycle();

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

        if (
            HandleFrameSelectedShortcut(
                authoringData
            )
        )
        {
            return;
        }

        float selectedPlaneY =
            GetCurrentAuthoringPlaneY();

        CompareFunction oldZTest =
            Handles.zTest;

        Color oldColor =
            Handles.color;

        Handles.zTest =
            CompareFunction.Always;

        DrawAllStampFootprints(
            worldSettings,
            authoringData,
            selectedPlaneY
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
            DrawSelectedStampVisualizations(
                worldSettings,
                selectedStamp,
                selectedPlaneY
            );

            DrawSelectedStampHandles(
                authoringData,
                worldSettings,
                selectedStamp,
                selectedPlaneY
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
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        float selectedPlaneY
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

            float stampPlaneY =
                GetStampVisualizationPlaneY(
                    worldSettings,
                    stamp,
                    selected,
                    selectedPlaneY
                );

            int pickerControlId =
                GUIUtility.GetControlID(
                    FocusType.Passive
                );

            HandleFootprintPicking(
                pickerControlId,
                worldSettings,
                authoringData,
                stamp,
                selectedPlaneY
            );

            bool hovered =
                HandleUtility.nearestControl ==
                    pickerControlId;

            DrawStampFootprint(
                stamp,
                stampPlaneY,
                selected,
                hovered
            );
        }
    }

    private void HandleFootprintPicking(
        int controlId,
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainStampModifier stamp,
        float selectedPlaneY
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

        bool selected =
            TerrainAuthoringModifierSelection
                .SelectedStableId ==
            stamp.StableId;

        float stampPlaneY =
            GetStampVisualizationPlaneY(
                worldSettings,
                stamp,
                selected,
                selectedPlaneY
            );

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
                        stampPlaneY
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
                    SelectFootprintAtCurrentMousePosition(
                        worldSettings,
                        authoringData,
                        selectedPlaneY
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

    private void SelectFootprintAtCurrentMousePosition(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        float selectedPlaneY
    )
    {
        stampPickCandidates.Clear();

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (
            int index = 0;
            index < modifiers.Count;
            index++
        )
        {
            if (
                !(modifiers[index]
                    is TerrainStampModifier stamp)
            )
            {
                continue;
            }

            bool selected =
                TerrainAuthoringModifierSelection
                    .SelectedStableId ==
                stamp.StableId;

            float stampPlaneY =
                GetStampVisualizationPlaneY(
                    worldSettings,
                    stamp,
                    selected,
                    selectedPlaneY
                );

            float distance =
                GetFootprintPickDistance(
                    stamp,
                    stampPlaneY
                );

            if (
                distance >
                    FootprintCyclePickDistance
            )
            {
                continue;
            }

            stampPickCandidates.Add(
                new StampPickCandidate(
                    stamp,
                    distance,
                    index
                )
            );
        }

        if (stampPickCandidates.Count <= 0)
        {
            ResetOverlapSelectionCycle();
            return;
        }

        stampPickCandidates.Sort(
            CompareStampPickCandidates
        );

        Vector2 clickPosition =
            Event.current.mousePosition;

        double clickTime =
            EditorApplication
                .timeSinceStartup;

        bool repeatedClick =
            IsRepeatFootprintSelectionClick(
                clickPosition,
                clickTime
            );

        int selectedCandidateIndex =
            0;

        if (
            repeatedClick
            &&
            stampPickCandidates.Count > 1
        )
        {
            string selectedStableId =
                TerrainAuthoringModifierSelection
                    .SelectedStableId;

            for (
                int index = 0;
                index <
                    stampPickCandidates.Count;
                index++
            )
            {
                if (
                    stampPickCandidates[index]
                        .Stamp
                        .StableId ==
                    selectedStableId
                )
                {
                    selectedCandidateIndex =
                        (
                            index +
                            1
                        )
                        %
                        stampPickCandidates.Count;

                    break;
                }
            }
        }

        TerrainStampModifier selectedStamp =
            stampPickCandidates[
                selectedCandidateIndex
            ].Stamp;

        TerrainAuthoringModifierSelection
            .Select(
                selectedStamp.StableId
            );

        lastFootprintSelectionClickPosition =
            clickPosition;

        lastFootprintSelectionClickTime =
            clickTime;

        SceneView.RepaintAll();
    }

    private bool IsRepeatFootprintSelectionClick(
        Vector2 clickPosition,
        double clickTime
    )
    {
        if (
            !IsFinite(
                lastFootprintSelectionClickPosition.x
            )
            ||
            !IsFinite(
                lastFootprintSelectionClickPosition.y
            )
        )
        {
            return false;
        }

        if (
            clickTime -
                lastFootprintSelectionClickTime >
            OverlapCycleTimeSeconds
        )
        {
            return false;
        }

        return
            Vector2.Distance(
                clickPosition,
                lastFootprintSelectionClickPosition
            )
            <=
            OverlapCycleMouseTolerance;
    }

    private void ResetOverlapSelectionCycle()
    {
        lastFootprintSelectionClickPosition =
            new Vector2(
                float.NaN,
                float.NaN
            );

        lastFootprintSelectionClickTime =
            double.NegativeInfinity;

        stampPickCandidates.Clear();
    }

    private static int CompareStampPickCandidates(
        StampPickCandidate a,
        StampPickCandidate b
    )
    {
        int distanceComparison =
            a.Distance.CompareTo(
                b.Distance
            );

        if (distanceComparison != 0)
        {
            return
                distanceComparison;
        }

        return
            a.ModifierIndex.CompareTo(
                b.ModifierIndex
            );
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

        Vector3 p0 =
            GetCornerPosition(
                footprint,
                StampCorner.MinimumXMinimumZ,
                planeY
            );

        Vector3 p1 =
            GetCornerPosition(
                footprint,
                StampCorner.MaximumXMinimumZ,
                planeY
            );

        Vector3 p2 =
            GetCornerPosition(
                footprint,
                StampCorner.MaximumXMaximumZ,
                planeY
            );

        Vector3 p3 =
            GetCornerPosition(
                footprint,
                StampCorner.MinimumXMaximumZ,
                planeY
            );

        float edgeDistance =
            Mathf.Min(
                HandleUtility.DistanceToLine(
                    p0,
                    p1
                ),
                HandleUtility.DistanceToLine(
                    p1,
                    p2
                ),
                HandleUtility.DistanceToLine(
                    p2,
                    p3
                ),
                HandleUtility.DistanceToLine(
                    p3,
                    p0
                )
            );

        Vector3 gui0 =
            HandleUtility.WorldToGUIPointWithDepth(
                p0
            );

        Vector3 gui1 =
            HandleUtility.WorldToGUIPointWithDepth(
                p1
            );

        Vector3 gui2 =
            HandleUtility.WorldToGUIPointWithDepth(
                p2
            );

        Vector3 gui3 =
            HandleUtility.WorldToGUIPointWithDepth(
                p3
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
            return
                edgeDistance;
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
            GetCornerPosition(
                footprint,
                StampCorner.MinimumXMinimumZ,
                planeY
            );

        Vector3 p1 =
            GetCornerPosition(
                footprint,
                StampCorner.MaximumXMinimumZ,
                planeY
            );

        Vector3 p2 =
            GetCornerPosition(
                footprint,
                StampCorner.MaximumXMaximumZ,
                planeY
            );

        Vector3 p3 =
            GetCornerPosition(
                footprint,
                StampCorner.MinimumXMaximumZ,
                planeY
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
    // SELECTED STAMP VISUALIZATION
    // =====================================================

    private static void DrawSelectedStampVisualizations(
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        if (stamp == null)
        {
            return;
        }

        if (
            TerrainStampEditorToolPreferences
                .ShowAffectedTileOverlay
        )
        {
            DrawAffectedTileOverlay(
                worldSettings,
                stamp,
                planeY
            );
        }

        if (
            TerrainStampEditorToolPreferences
                .ShowFalloffVisualization
        )
        {
            DrawFalloffVisualization(
                stamp,
                planeY
            );
        }

        if (stamp.StampAsset == null)
        {
            StampFootprint footprint =
                GetStampFootprint(
                    stamp
                );

            Vector3 labelPosition =
                ToWorldPosition(
                    footprint.CenterXZ,
                    planeY
                );

            Handles.Label(
                labelPosition,
                "Unassigned Stamp",
                EditorStyles.miniBoldLabel
            );
        }
    }

    private static void DrawFalloffVisualization(
        TerrainStampModifier stamp,
        float planeY
    )
    {
        StampFootprint outer =
            GetStampFootprint(
                stamp
            );

        Color oldColor =
            Handles.color;

        Handles.color =
            Handles.preselectionColor;

        if (
            stamp.FalloffShape ==
                TerrainStampFalloffShape.Ellipse
        )
        {
            DrawEllipseFalloffVisualization(
                outer,
                stamp.Falloff,
                planeY
            );
        }
        else
        {
            DrawRectangleFalloffVisualization(
                stamp,
                outer,
                planeY
            );
        }

        Handles.color =
            oldColor;
    }

    private static void DrawRectangleFalloffVisualization(
        TerrainStampModifier stamp,
        StampFootprint outer,
        float planeY
    )
    {
        StampFootprint inner =
            GetFalloffFootprint(
                stamp
            );

        const float minimumRegionSize =
            0.0001f;

        bool collapsed =
            inner.Width <=
                minimumRegionSize
            ||
            inner.Depth <=
                minimumRegionSize;

        if (collapsed)
        {
            DrawCollapsedFalloffMarker(
                outer,
                planeY
            );

            return;
        }

        /*
         * Preserve the established Rectangle visualization exactly: the source
         * footprint is already the outer contribution boundary, so this draws
         * only the inner full-weight rectangle.
         */
        DrawFootprintOutline(
            inner,
            planeY,
            false
        );
    }

    private static void DrawEllipseFalloffVisualization(
        StampFootprint outer,
        float falloff,
        float planeY
    )
    {
        /*
         * The rectangular source footprint is drawn independently by
         * DrawStampFootprint. Ellipse adds the contribution-shape boundary
         * without replacing source SizeXZ / picking / resize geometry.
         */
        DrawEllipseOutline(
            outer,
            1f,
            planeY
        );

        float safeFalloff =
            Mathf.Clamp01(
                falloff
            );

        if (safeFalloff <= 0f)
        {
            /*
             * Inner and outer ellipses coincide at Falloff=0. Avoid drawing the
             * same line twice.
             */
            return;
        }

        float innerScale =
            1f -
            safeFalloff;

        const float minimumScale =
            0.0001f;

        if (innerScale <= minimumScale)
        {
            DrawCollapsedFalloffMarker(
                outer,
                planeY
            );

            return;
        }

        DrawEllipseOutline(
            outer,
            innerScale,
            planeY
        );
    }

    private static void DrawEllipseOutline(
        StampFootprint footprint,
        float scale,
        float planeY
    )
    {
        float safeScale =
            Mathf.Max(
                0f,
                scale
            );

        float radiusX =
            footprint.HalfWidth *
            safeScale;

        float radiusZ =
            footprint.HalfDepth *
            safeScale;

        if (
            radiusX <= 0f
            ||
            radiusZ <= 0f
        )
        {
            return;
        }

        Matrix4x4 oldMatrix =
            Handles.matrix;

        Vector3 center =
            ToWorldPosition(
                footprint.CenterXZ,
                planeY
            );

        Handles.matrix =
            Matrix4x4.TRS(
                center,
                Quaternion.Euler(
                    0f,
                    footprint.RotationDegrees,
                    0f
                ),
                new Vector3(
                    radiusX,
                    1f,
                    radiusZ
                )
            );

        Handles.DrawWireDisc(
            Vector3.zero,
            Vector3.up,
            1f
        );

        Handles.matrix =
            oldMatrix;
    }

    private static void DrawCollapsedFalloffMarker(
        StampFootprint footprint,
        float planeY
    )
    {
        Vector3 center =
            ToWorldPosition(
                footprint.CenterXZ,
                planeY
            );

        Vector3 right =
            ToWorldDirection(
                footprint.RightXZ
            );

        Vector3 forward =
            ToWorldDirection(
                footprint.ForwardXZ
            );

        float markerSize =
            Mathf.Max(
                0.01f,
                GetHandleSize(
                    center
                )
                *
                0.75f
            );

        Handles.DrawLine(
            center -
                right *
                markerSize,
            center +
                right *
                markerSize
        );

        Handles.DrawLine(
            center -
                forward *
                markerSize,
            center +
                forward *
                markerSize
        );
    }

    private static void DrawAffectedTileOverlay(
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        if (
            worldSettings == null
            ||
            stamp == null
        )
        {
            return;
        }

        List<Vector2Int> affectedTiles =
            new List<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                stamp.GetAffectedWorldBounds(),
                affectedTiles,
                1
            );

        if (affectedTiles.Count <= 0)
        {
            return;
        }

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                worldSettings.HeightTileWorldSize
            );

        Color oldColor =
            Handles.color;

        Handles.color =
            Handles.secondaryColor;

        for (
            int index = 0;
            index <
                affectedTiles.Count;
            index++
        )
        {
            Vector2Int tile =
                affectedTiles[
                    index
                ];

            float minimumX =
                tile.x *
                tileWorldSize;

            float minimumZ =
                tile.y *
                tileWorldSize;

            StampFootprint tileFootprint =
                new StampFootprint(
                    new Vector2(
                        minimumX +
                            tileWorldSize *
                            0.5f,
                        minimumZ +
                            tileWorldSize *
                            0.5f
                    ),
                    new Vector2(
                        tileWorldSize,
                        tileWorldSize
                    ),
                    0f
                );

            DrawFootprintOutline(
                tileFootprint,
                planeY,
                true
            );
        }

        Handles.color =
            oldColor;
    }

    private static void DrawFootprintOutline(
        StampFootprint footprint,
        float planeY,
        bool dotted
    )
    {
        Vector3 p0 =
            GetCornerPosition(
                footprint,
                StampCorner.MinimumXMinimumZ,
                planeY
            );

        Vector3 p1 =
            GetCornerPosition(
                footprint,
                StampCorner.MaximumXMinimumZ,
                planeY
            );

        Vector3 p2 =
            GetCornerPosition(
                footprint,
                StampCorner.MaximumXMaximumZ,
                planeY
            );

        Vector3 p3 =
            GetCornerPosition(
                footprint,
                StampCorner.MinimumXMaximumZ,
                planeY
            );

        if (dotted)
        {
            Handles.DrawDottedLine(
                p0,
                p1,
                5f
            );

            Handles.DrawDottedLine(
                p1,
                p2,
                5f
            );

            Handles.DrawDottedLine(
                p2,
                p3,
                5f
            );

            Handles.DrawDottedLine(
                p3,
                p0,
                5f
            );

            return;
        }

        Handles.DrawAAPolyLine(
            1.5f,
            p0,
            p1,
            p2,
            p3,
            p0
        );
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

        DrawRotationHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY
        );

        DrawHeightDeltaHandle(
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

        if (
            TerrainStampEditorToolPreferences
                .ShowFalloffVisualization
        )
        {
            DrawFalloffHandles(
                authoringData,
                worldSettings,
                stamp,
                planeY
            );
        }

        Handles.color =
            oldColor;
    }

    // =====================================================
    // ROTATION
    // =====================================================

    private void DrawRotationHandle(
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
            ToWorldPosition(
                footprint.CenterXZ,
                planeY
            );

        float radius =
            GetRotationHandleRadius(
                footprint,
                center
            );

        Quaternion currentRotation =
            Quaternion.Euler(
                0f,
                stamp.RotationDegrees,
                0f
            );

        float snapDegrees =
            Event.current.shift
                ? RotationSnapDegrees
                : 0f;

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Quaternion editedRotation =
            Handles.Disc(
                currentRotation,
                center,
                Vector3.up,
                radius,
                false,
                snapDegrees
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
                "Rotate Terrain Stamp Modifier"
            )
        )
        {
            return;
        }

        bool active =
            IsActiveHandleControl(
                hotBefore,
                hotAfter
            );

        if (
            changed
            &&
            active
        )
        {
            float rotationDegrees =
                GetSignedYRotationDegrees(
                    editedRotation,
                    stamp.RotationDegrees
                );

            ApplyInteractiveRotation(
                rotationDegrees
            );
        }

        if (active)
        {
            DrawRotationFeedback(
                stamp,
                planeY
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );
    }

    private static float GetRotationHandleRadius(
        StampFootprint footprint,
        Vector3 center
    )
    {
        float footprintRadius =
            Mathf.Sqrt(
                footprint.HalfWidth *
                    footprint.HalfWidth
                +
                footprint.HalfDepth *
                    footprint.HalfDepth
            );

        float screenGap =
            GetHandleSize(
                center
            )
            *
            RotationHandleGapScaleMultiplier;

        return
            Mathf.Max(
                0.01f,
                footprintRadius +
                    screenGap
            );
    }

    private static float GetSignedYRotationDegrees(
        Quaternion rotation,
        float fallbackDegrees
    )
    {
        Vector3 forward =
            rotation *
            Vector3.forward;

        Vector2 horizontalForward =
            new Vector2(
                forward.x,
                forward.z
            );

        if (
            horizontalForward.sqrMagnitude <=
                0.00000001f
        )
        {
            return
                TerrainStampTransformUtility
                    .NormalizeRotationDegrees(
                        fallbackDegrees
                    );
        }

        float rotationDegrees =
            Mathf.Atan2(
                horizontalForward.x,
                horizontalForward.y
            )
            *
            Mathf.Rad2Deg;

        return
            TerrainStampTransformUtility
                .NormalizeRotationDegrees(
                    rotationDegrees
                );
    }

    private static void DrawRotationFeedback(
        TerrainStampModifier stamp,
        float planeY
    )
    {
        StampFootprint footprint =
            GetStampFootprint(
                stamp
            );

        Vector3 center =
            ToWorldPosition(
                footprint.CenterXZ,
                planeY
            );

        float radius =
            GetRotationHandleRadius(
                footprint,
                center
            );

        float labelOffset =
            GetHandleSize(
                center
            )
            *
            RotationLabelOffsetScaleMultiplier;

        Vector3 forward =
            ToWorldDirection(
                footprint.ForwardXZ
            );

        Vector3 labelPosition =
            center +
            forward *
                (
                    radius +
                    labelOffset
                );

        Handles.Label(
            labelPosition,
            $"Rotation {stamp.RotationDegrees:0.#}°",
            EditorStyles.miniBoldLabel
        );
    }

    // =====================================================
    // HEIGHT DELTA
    // =====================================================

    private void DrawHeightDeltaHandle(
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

        Vector3 right =
            ToWorldDirection(
                footprint.RightXZ
            );

        Vector3 forward =
            ToWorldDirection(
                footprint.ForwardXZ
            );

        Vector3 footprintEdge =
            ToWorldPosition(
                footprint.GetEdgeCenterXZ(
                    StampEdge.MaximumX
                ),
                planeY
            );

        float baseHandleSize =
            GetHandleSize(
                footprintEdge
            );

        Vector3 handleBase =
            footprintEdge +
            right *
                baseHandleSize *
                3f;

        Vector3 handlePosition =
            handleBase +
            Vector3.up *
                stamp.HeightDelta;

        float handleSize =
            GetHandleSize(
                handlePosition
            );

        float tickSize =
            Mathf.Max(
                0.01f,
                baseHandleSize *
                    0.8f
            );

        Color oldColor =
            Handles.color;

        Handles.color =
            Handles.selectedColor;

        Handles.DrawDottedLine(
            footprintEdge,
            handleBase,
            4f
        );

        Handles.DrawAAPolyLine(
            2.5f,
            handleBase,
            handlePosition
        );

        Handles.DrawLine(
            handleBase -
                forward *
                tickSize,
            handleBase +
                forward *
                tickSize
        );

        Handles.DrawLine(
            handlePosition -
                forward *
                tickSize,
            handlePosition +
                forward *
                tickSize
        );

        Handles.Label(
            handleBase +
                right *
                baseHandleSize,
            "0 m",
            EditorStyles.miniLabel
        );

        Handles.Label(
            handlePosition +
                right *
                handleSize,
            $"ΔH {stamp.HeightDelta:+0.##;-0.##;0} m",
            EditorStyles.miniBoldLabel
        );

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Vector3 moved =
            Handles.Slider(
                handlePosition,
                Vector3.up,
                handleSize,
                Handles.ConeHandleCap,
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
                "Set Terrain Stamp Height"
            )
        )
        {
            Handles.color =
                oldColor;

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
            ApplyInteractiveHeightDelta(
                moved.y -
                planeY
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );

        Handles.color =
            oldColor;
    }

    // =====================================================
    // MOVE
    // =====================================================

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
            ToWorldPosition(
                footprint.CenterXZ,
                planeY
            );

        float handleSize =
            GetHandleSize(
                center
            )
            *
            MoveHandleScaleMultiplier;

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

    // =====================================================
    // RESIZE
    // =====================================================

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

        Vector3 handlePosition =
            ToWorldPosition(
                footprint.GetEdgeCenterXZ(
                    edge
                ),
                planeY
            );

        Vector3 direction =
            ToWorldDirection(
                IsXEdge(
                    edge
                )
                    ? footprint.RightXZ
                    : footprint.ForwardXZ
            );

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
                "Resize Terrain Stamp Modifier",
                true
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
            StampFootprint source =
                GetActiveResizeSourceFootprint(
                    stamp
                );

            CalculateEdgeFootprint(
                source,
                edge,
                moved,
                Event.current.alt,
                out Vector2 position,
                out Vector2 size
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
                ToWorldDirection(
                    footprint.RightXZ
                ),
                ToWorldDirection(
                    footprint.ForwardXZ
                ),
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
                "Resize Terrain Stamp Modifier",
                true
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
            StampFootprint source =
                GetActiveResizeSourceFootprint(
                    stamp
                );

            CalculateCornerFootprint(
                source,
                corner,
                moved,
                Event.current.alt,
                Event.current.shift,
                GetActiveResizeAspectRatio(
                    stamp
                ),
                out Vector2 position,
                out Vector2 size
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
    // FALLOFF
    // =====================================================

    private void DrawFalloffHandles(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        Color oldColor =
            Handles.color;

        Handles.color =
            Handles.preselectionColor;

        if (
            stamp.FalloffShape ==
                TerrainStampFalloffShape.Ellipse
        )
        {
            DrawEllipseFalloffHandle(
                authoringData,
                worldSettings,
                stamp,
                planeY
            );

            Handles.color =
                oldColor;

            return;
        }

        /*
         * Preserve the established Rectangle authoring path: four equivalent
         * edge handles continue to edit the same scalar Falloff amount.
         */
        DrawFalloffHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MinimumX
        );

        DrawFalloffHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MaximumX
        );

        DrawFalloffHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MinimumZ
        );

        DrawFalloffHandle(
            authoringData,
            worldSettings,
            stamp,
            planeY,
            StampEdge.MaximumZ
        );

        Handles.color =
            oldColor;
    }

    private void DrawFalloffHandle(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY,
        StampEdge edge
    )
    {
        StampFootprint outer =
            GetStampFootprint(
                stamp
            );

        StampFootprint inner =
            GetFalloffFootprint(
                stamp
            );

        Vector3 handlePosition =
            GetFalloffHandlePosition(
                outer,
                inner,
                edge,
                planeY
            );

        Vector3 direction =
            ToWorldDirection(
                IsXEdge(
                    edge
                )
                    ? outer.RightXZ
                    : outer.ForwardXZ
            );

        float handleSize =
            GetHandleSize(
                handlePosition
            )
            *
            FalloffHandleScaleMultiplier;

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Vector3 moved =
            Handles.Slider(
                handlePosition,
                direction,
                handleSize,
                Handles.DotHandleCap,
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
                "Set Terrain Stamp Falloff"
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
            float falloff =
                CalculateFalloffFromHandle(
                    outer,
                    edge,
                    moved
                );

            ApplyInteractiveFalloff(
                falloff
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );
    }

    private void DrawEllipseFalloffHandle(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        float planeY
    )
    {
        StampFootprint outer =
            GetStampFootprint(
                stamp
            );

        float safeFalloff =
            Mathf.Clamp01(
                stamp.Falloff
            );

        float innerRadiusX =
            outer.HalfWidth *
            (
                1f -
                safeFalloff
            );

        /*
         * The logical anchor stays exactly on local +X of the inner ellipse.
         * A display-only local -Z tangent offset prevents the dot from
         * overlapping the +X resize handle at Falloff=0 and the center move
         * handle at Falloff=1.
         */
        Vector2 anchorLocal =
            new Vector2(
                innerRadiusX,
                0f
            );

        Vector2 handleLocal =
            new Vector2(
                innerRadiusX,
                -outer.Depth *
                    FalloffHandleTangentOffsetFraction
            );

        Vector3 anchorPosition =
            ToWorldPosition(
                outer.LocalToWorldXZ(
                    anchorLocal
                ),
                planeY
            );

        Vector3 handlePosition =
            ToWorldPosition(
                outer.LocalToWorldXZ(
                    handleLocal
                ),
                planeY
            );

        Handles.DrawDottedLine(
            anchorPosition,
            handlePosition,
            4f
        );

        Vector3 direction =
            ToWorldDirection(
                outer.RightXZ
            );

        float handleSize =
            GetHandleSize(
                handlePosition
            )
            *
            FalloffHandleScaleMultiplier;

        int hotBefore =
            GUIUtility.hotControl;

        EditorGUI.BeginChangeCheck();

        Vector3 moved =
            Handles.Slider(
                handlePosition,
                direction,
                handleSize,
                Handles.DotHandleCap,
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
                "Set Terrain Stamp Falloff"
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
            float falloff =
                CalculateEllipseFalloffFromHandle(
                    outer,
                    moved
                );

            ApplyInteractiveFalloff(
                falloff
            );
        }

        CommitIfHandleReleased(
            hotBefore,
            hotAfter
        );
    }

    private static Vector3 GetFalloffHandlePosition(
        StampFootprint outer,
        StampFootprint inner,
        StampEdge edge,
        float planeY
    )
    {
        float tangentOffsetX =
            outer.Width *
            FalloffHandleTangentOffsetFraction;

        float tangentOffsetZ =
            outer.Depth *
            FalloffHandleTangentOffsetFraction;

        Vector2 localPosition;

        switch (edge)
        {
            case StampEdge.MinimumX:
                localPosition =
                    new Vector2(
                        -inner.HalfWidth,
                        tangentOffsetZ
                    );
                break;

            case StampEdge.MaximumX:
                localPosition =
                    new Vector2(
                        inner.HalfWidth,
                        -tangentOffsetZ
                    );
                break;

            case StampEdge.MinimumZ:
                localPosition =
                    new Vector2(
                        -tangentOffsetX,
                        -inner.HalfDepth
                    );
                break;

            default:
                localPosition =
                    new Vector2(
                        tangentOffsetX,
                        inner.HalfDepth
                    );
                break;
        }

        return
            ToWorldPosition(
                outer.LocalToWorldXZ(
                    localPosition
                ),
                planeY
            );
    }

    private static float CalculateFalloffFromHandle(
        StampFootprint outer,
        StampEdge edge,
        Vector3 moved
    )
    {
        Vector2 local =
            outer.WorldToLocalXZ(
                new Vector2(
                    moved.x,
                    moved.z
                )
            );

        float falloff;

        switch (edge)
        {
            case StampEdge.MinimumX:
                falloff =
                    (
                        local.x +
                        outer.HalfWidth
                    )
                    *
                    2f
                    /
                    Mathf.Max(
                        MinimumStampSize,
                        outer.Width
                    );
                break;

            case StampEdge.MaximumX:
                falloff =
                    (
                        outer.HalfWidth -
                        local.x
                    )
                    *
                    2f
                    /
                    Mathf.Max(
                        MinimumStampSize,
                        outer.Width
                    );
                break;

            case StampEdge.MinimumZ:
                falloff =
                    (
                        local.y +
                        outer.HalfDepth
                    )
                    *
                    2f
                    /
                    Mathf.Max(
                        MinimumStampSize,
                        outer.Depth
                    );
                break;

            default:
                falloff =
                    (
                        outer.HalfDepth -
                        local.y
                    )
                    *
                    2f
                    /
                    Mathf.Max(
                        MinimumStampSize,
                        outer.Depth
                    );
                break;
        }

        return
            Mathf.Clamp01(
                falloff
            );
    }

    private static float CalculateEllipseFalloffFromHandle(
        StampFootprint outer,
        Vector3 moved
    )
    {
        Vector2 local =
            outer.WorldToLocalXZ(
                new Vector2(
                    moved.x,
                    moved.z
                )
            );

        float safeOuterRadius =
            Mathf.Max(
                MinimumStampSize *
                    0.5f,
                outer.HalfWidth
            );

        float innerRadius =
            Mathf.Clamp(
                local.x,
                0f,
                safeOuterRadius
            );

        return
            Mathf.Clamp01(
                1f -
                innerRadius /
                safeOuterRadius
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
        string undoLabel,
        bool captureResizeState = false
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

            if (captureResizeState)
            {
                activeResizeInitialFootprint =
                    GetStampFootprint(
                        stamp
                    );

                activeResizeAspectRatio =
                    Mathf.Max(
                        0.000001f,
                        activeResizeInitialFootprint
                            .Width
                        /
                        Mathf.Max(
                            0.000001f,
                            activeResizeInitialFootprint
                                .Depth
                        )
                    );

                activeResizeStateValid =
                    true;
            }
            else
            {
                ResetActiveResizeState();
            }

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

    private float GetActiveResizeAspectRatio(
        TerrainStampModifier stamp
    )
    {
        if (
            activeResizeStateValid
            &&
            activeResizeAspectRatio >
                0f
        )
        {
            return
                activeResizeAspectRatio;
        }

        Vector2 size =
            stamp.SizeXZ;

        return
            Mathf.Max(
                0.000001f,
                size.x
                /
                Mathf.Max(
                    0.000001f,
                    size.y
                )
            );
    }

    private StampFootprint GetActiveResizeSourceFootprint(
        TerrainStampModifier stamp
    )
    {
        if (activeResizeStateValid)
        {
            return
                activeResizeInitialFootprint;
        }

        return
            GetStampFootprint(
                stamp
            );
    }

    private void ResetActiveResizeState()
    {
        activeResizeStateValid =
            false;

        activeResizeAspectRatio =
            1f;

        activeResizeInitialFootprint =
            default;
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

    private void ApplyInteractiveHeightDelta(
        float heightDelta
    )
    {
        if (
            !TerrainAuthoringModifierService
                .UpdateInteractiveStampHeightDelta(
                    heightDelta,
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

    private void ApplyInteractiveFalloff(
        float falloff
    )
    {
        if (
            !TerrainAuthoringModifierService
                .UpdateInteractiveStampFalloff(
                    falloff,
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

    private void ApplyInteractiveRotation(
        float rotationDegrees
    )
    {
        if (
            !TerrainAuthoringModifierService
                .UpdateInteractiveStampRotation(
                    rotationDegrees,
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

        ResetActiveResizeState();

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

        ResetActiveResizeState();

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
        return
            new StampFootprint(
                stamp.PositionXZ,
                stamp.SizeXZ,
                stamp.RotationDegrees
            );
    }

    private static StampFootprint GetFalloffFootprint(
        TerrainStampModifier stamp
    )
    {
        StampFootprint outer =
            GetStampFootprint(
                stamp
            );

        float falloff =
            Mathf.Clamp01(
                stamp.Falloff
            );

        Vector2 innerSize =
            outer.SizeXZ *
            (
                1f -
                falloff
            );

        return
            new StampFootprint(
                outer.CenterXZ,
                innerSize,
                outer.RotationDegrees
            );
    }

    private static void CalculateEdgeFootprint(
        StampFootprint source,
        StampEdge edge,
        Vector3 moved,
        bool symmetric,
        out Vector2 position,
        out Vector2 size
    )
    {
        Vector2 movedLocal =
            source.WorldToLocalXZ(
                new Vector2(
                    moved.x,
                    moved.z
                )
            );

        float minimumX =
            -source.HalfWidth;

        float maximumX =
            source.HalfWidth;

        float minimumZ =
            -source.HalfDepth;

        float maximumZ =
            source.HalfDepth;

        if (symmetric)
        {
            float minimumHalfSize =
                MinimumStampSize *
                0.5f;

            switch (edge)
            {
                case StampEdge.MinimumX:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            -movedLocal.x
                        );

                    minimumX =
                        -halfSize;

                    maximumX =
                        halfSize;

                    break;
                }

                case StampEdge.MaximumX:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            movedLocal.x
                        );

                    minimumX =
                        -halfSize;

                    maximumX =
                        halfSize;

                    break;
                }

                case StampEdge.MinimumZ:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            -movedLocal.y
                        );

                    minimumZ =
                        -halfSize;

                    maximumZ =
                        halfSize;

                    break;
                }

                default:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            movedLocal.y
                        );

                    minimumZ =
                        -halfSize;

                    maximumZ =
                        halfSize;

                    break;
                }
            }
        }
        else
        {
            switch (edge)
            {
                case StampEdge.MinimumX:
                    minimumX =
                        Mathf.Min(
                            movedLocal.x,
                            maximumX -
                                MinimumStampSize
                        );
                    break;

                case StampEdge.MaximumX:
                    maximumX =
                        Mathf.Max(
                            movedLocal.x,
                            minimumX +
                                MinimumStampSize
                        );
                    break;

                case StampEdge.MinimumZ:
                    minimumZ =
                        Mathf.Min(
                            movedLocal.y,
                            maximumZ -
                                MinimumStampSize
                        );
                    break;

                case StampEdge.MaximumZ:
                    maximumZ =
                        Mathf.Max(
                            movedLocal.y,
                            minimumZ +
                                MinimumStampSize
                        );
                    break;
            }
        }

        ConvertLocalBoundsToFootprint(
            source,
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
        bool symmetric,
        bool preserveAspect,
        float aspectRatio,
        out Vector2 position,
        out Vector2 size
    )
    {
        if (symmetric)
        {
            CalculateSymmetricCornerFootprint(
                source,
                corner,
                moved,
                preserveAspect,
                aspectRatio,
                out position,
                out size
            );

            return;
        }

        Vector2 movedLocal =
            source.WorldToLocalXZ(
                new Vector2(
                    moved.x,
                    moved.z
                )
            );

        float minimumX =
            -source.HalfWidth;

        float maximumX =
            source.HalfWidth;

        float minimumZ =
            -source.HalfDepth;

        float maximumZ =
            source.HalfDepth;

        switch (corner)
        {
            case StampCorner.MinimumXMinimumZ:
                minimumX =
                    Mathf.Min(
                        movedLocal.x,
                        maximumX -
                            MinimumStampSize
                    );

                minimumZ =
                    Mathf.Min(
                        movedLocal.y,
                        maximumZ -
                            MinimumStampSize
                    );
                break;

            case StampCorner.MaximumXMinimumZ:
                maximumX =
                    Mathf.Max(
                        movedLocal.x,
                        minimumX +
                            MinimumStampSize
                    );

                minimumZ =
                    Mathf.Min(
                        movedLocal.y,
                        maximumZ -
                            MinimumStampSize
                    );
                break;

            case StampCorner.MaximumXMaximumZ:
                maximumX =
                    Mathf.Max(
                        movedLocal.x,
                        minimumX +
                            MinimumStampSize
                    );

                maximumZ =
                    Mathf.Max(
                        movedLocal.y,
                        minimumZ +
                            MinimumStampSize
                    );
                break;

            case StampCorner.MinimumXMaximumZ:
                minimumX =
                    Mathf.Min(
                        movedLocal.x,
                        maximumX -
                            MinimumStampSize
                    );

                maximumZ =
                    Mathf.Max(
                        movedLocal.y,
                        minimumZ +
                            MinimumStampSize
                    );
                break;
        }

        if (preserveAspect)
        {
            float width =
                maximumX -
                minimumX;

            float depth =
                maximumZ -
                minimumZ;

            EnforceAspectRatio(
                ref width,
                ref depth,
                source.Width,
                source.Depth,
                aspectRatio
            );

            switch (corner)
            {
                case StampCorner.MinimumXMinimumZ:
                    minimumX =
                        maximumX -
                        width;

                    minimumZ =
                        maximumZ -
                        depth;
                    break;

                case StampCorner.MaximumXMinimumZ:
                    maximumX =
                        minimumX +
                        width;

                    minimumZ =
                        maximumZ -
                        depth;
                    break;

                case StampCorner.MaximumXMaximumZ:
                    maximumX =
                        minimumX +
                        width;

                    maximumZ =
                        minimumZ +
                        depth;
                    break;

                case StampCorner.MinimumXMaximumZ:
                    minimumX =
                        maximumX -
                        width;

                    maximumZ =
                        minimumZ +
                        depth;
                    break;
            }
        }

        ConvertLocalBoundsToFootprint(
            source,
            minimumX,
            maximumX,
            minimumZ,
            maximumZ,
            out position,
            out size
        );
    }

    private static void CalculateSymmetricCornerFootprint(
        StampFootprint source,
        StampCorner corner,
        Vector3 moved,
        bool preserveAspect,
        float aspectRatio,
        out Vector2 position,
        out Vector2 size
    )
    {
        Vector2 movedLocal =
            source.WorldToLocalXZ(
                new Vector2(
                    moved.x,
                    moved.z
                )
            );

        float minimumHalfSize =
            MinimumStampSize *
            0.5f;

        float halfX;
        float halfZ;

        switch (corner)
        {
            case StampCorner.MinimumXMinimumZ:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        -movedLocal.x
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        -movedLocal.y
                    );
                break;

            case StampCorner.MaximumXMinimumZ:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        movedLocal.x
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        -movedLocal.y
                    );
                break;

            case StampCorner.MaximumXMaximumZ:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        movedLocal.x
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        movedLocal.y
                    );
                break;

            default:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        -movedLocal.x
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        movedLocal.y
                    );
                break;
        }

        float width =
            halfX *
            2f;

        float depth =
            halfZ *
            2f;

        if (preserveAspect)
        {
            EnforceAspectRatio(
                ref width,
                ref depth,
                source.Width,
                source.Depth,
                aspectRatio
            );
        }

        position =
            source.CenterXZ;

        size =
            new Vector2(
                Mathf.Max(
                    MinimumStampSize,
                    width
                ),
                Mathf.Max(
                    MinimumStampSize,
                    depth
                )
            );
    }

    private static void EnforceAspectRatio(
        ref float width,
        ref float depth,
        float sourceWidth,
        float sourceDepth,
        float aspectRatio
    )
    {
        float safeAspectRatio =
            Mathf.Max(
                0.000001f,
                aspectRatio
            );

        float safeSourceWidth =
            Mathf.Max(
                MinimumStampSize,
                sourceWidth
            );

        float safeSourceDepth =
            Mathf.Max(
                MinimumStampSize,
                sourceDepth
            );

        width =
            Mathf.Max(
                MinimumStampSize,
                width
            );

        depth =
            Mathf.Max(
                MinimumStampSize,
                depth
            );

        float relativeWidthChange =
            Mathf.Abs(
                width /
                    safeSourceWidth -
                1f
            );

        float relativeDepthChange =
            Mathf.Abs(
                depth /
                    safeSourceDepth -
                1f
            );

        if (
            relativeWidthChange >=
                relativeDepthChange
        )
        {
            depth =
                width /
                safeAspectRatio;
        }
        else
        {
            width =
                depth *
                safeAspectRatio;
        }

        float minimumScale =
            Mathf.Max(
                1f,
                MinimumStampSize /
                    Mathf.Max(
                        0.000001f,
                        width
                    ),
                MinimumStampSize /
                    Mathf.Max(
                        0.000001f,
                        depth
                    )
            );

        width *=
            minimumScale;

        depth *=
            minimumScale;
    }

    private static void ConvertLocalBoundsToFootprint(
        StampFootprint source,
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

        Vector2 localCenter =
            new Vector2(
                (
                    minimumX +
                    safeMaximumX
                )
                *
                0.5f,
                (
                    minimumZ +
                    safeMaximumZ
                )
                *
                0.5f
            );

        position =
            source.LocalToWorldXZ(
                localCenter
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
        return
            ToWorldPosition(
                footprint.GetCornerXZ(
                    corner
                ),
                planeY
            );
    }

    private static Vector3 ToWorldPosition(
        Vector2 positionXZ,
        float worldY
    )
    {
        return
            new Vector3(
                positionXZ.x,
                worldY,
                positionXZ.y
            );
    }

    private static Vector3 ToWorldDirection(
        Vector2 directionXZ
    )
    {
        return
            new Vector3(
                directionXZ.x,
                0f,
                directionXZ.y
            );
    }

    private static bool IsXEdge(
        StampEdge edge
    )
    {
        return
            edge == StampEdge.MinimumX
            ||
            edge == StampEdge.MaximumX;
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
                )
                *
                HandleScreenScale
            );
    }

    // =====================================================
    // FRAME SHORTCUT
    // =====================================================

    private bool HandleFrameSelectedShortcut(
        TerrainAuthoringData authoringData
    )
    {
        Event currentEvent =
            Event.current;

        if (
            currentEvent.type !=
                EventType.KeyDown
            ||
            currentEvent.keyCode !=
                KeyCode.F
            ||
            currentEvent.alt
            ||
            currentEvent.control
            ||
            currentEvent.command
            ||
            currentEvent.shift
            ||
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
        )
        {
            return false;
        }

        if (
            !TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    authoringData,
                    out TerrainHeightModifier modifier,
                    out _
                )
        )
        {
            return false;
        }

        if (
            !TerrainAuthoringModifierSceneUtility
                .TryFrameModifier(
                    modifier,
                    out string frameError
                )
        )
        {
            toolErrorMessage =
                frameError;
        }
        else
        {
            toolErrorMessage =
                "";
        }

        currentEvent.Use();

        return true;
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

        if (
            TerrainAuthoringModifierContextUtility
                .TryLoadDefault(
                    out WorldSettings worldSettings,
                    out TerrainAuthoringData authoringData,
                    out _
                )
            &&
            TerrainAuthoringModifierSelection
                .TryGetSelectedModifier(
                    authoringData,
                    out TerrainHeightModifier selectedModifier,
                    out _
                )
            &&
            selectedModifier is
                TerrainStampModifier selectedStamp
            &&
            TerrainAuthoringModifierSceneUtility
                .TryGetStampInteractionPlaneY(
                    worldSettings,
                    selectedStamp,
                    out float localPlaneY
                )
            &&
            IsFinite(
                localPlaneY
            )
        )
        {
            return
                localPlaneY;
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
                )
                *
                0.5f;
        }

        return
            0f;
    }

    private static float GetStampVisualizationPlaneY(
        WorldSettings worldSettings,
        TerrainStampModifier stamp,
        bool selected,
        float selectedPlaneY
    )
    {
        if (selected)
        {
            return
                selectedPlaneY;
        }

        if (
            TerrainAuthoringModifierSceneUtility
                .TryGetStampInteractionPlaneY(
                    worldSettings,
                    stamp,
                    out float localPlaneY
                )
            &&
            IsFinite(
                localPlaneY
            )
        )
        {
            return
                localPlaneY;
        }

        return
            selectedPlaneY;
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

    private readonly struct StampPickCandidate
    {
        public readonly TerrainStampModifier Stamp;
        public readonly float Distance;
        public readonly int ModifierIndex;

        public StampPickCandidate(
            TerrainStampModifier stamp,
            float distance,
            int modifierIndex
        )
        {
            Stamp =
                stamp;

            Distance =
                distance;

            ModifierIndex =
                modifierIndex;
        }
    }

    /*
     * Oriented stamp-local footprint used only by Scene tooling.
     *
     * Minimum/Maximum X/Z enums throughout the tool refer to the stamp's local
     * axes, not world-axis-aligned Bounds. The actual rotation convention and
     * world/local conversion remain owned by TerrainStampTransformUtility.
     */
    private readonly struct StampFootprint
    {
        public readonly Vector2 CenterXZ;
        public readonly Vector2 SizeXZ;

        public readonly float RotationDegrees;

        public readonly Vector2 RightXZ;
        public readonly Vector2 ForwardXZ;

        public readonly Vector2 MinimumXMinimumZ;
        public readonly Vector2 MaximumXMinimumZ;
        public readonly Vector2 MaximumXMaximumZ;
        public readonly Vector2 MinimumXMaximumZ;

        public float CenterX =>
            CenterXZ.x;

        public float CenterZ =>
            CenterXZ.y;

        public float Width =>
            Mathf.Max(
                0f,
                SizeXZ.x
            );

        public float Depth =>
            Mathf.Max(
                0f,
                SizeXZ.y
            );

        public float HalfWidth =>
            Width *
            0.5f;

        public float HalfDepth =>
            Depth *
            0.5f;

        public StampFootprint(
            Vector2 centerXZ,
            Vector2 sizeXZ,
            float rotationDegrees
        )
        {
            CenterXZ =
                centerXZ;

            SizeXZ =
                new Vector2(
                    Mathf.Max(
                        0f,
                        sizeXZ.x
                    ),
                    Mathf.Max(
                        0f,
                        sizeXZ.y
                    )
                );

            RotationDegrees =
                TerrainStampTransformUtility
                    .NormalizeRotationDegrees(
                        rotationDegrees
                    );

            TerrainStampTransformUtility
                .GetWorldAxes(
                    RotationDegrees,
                    out RightXZ,
                    out ForwardXZ
                );

            TerrainStampTransformUtility
                .GetWorldCorners(
                    CenterXZ,
                    SizeXZ,
                    RotationDegrees,
                    out MinimumXMinimumZ,
                    out MaximumXMinimumZ,
                    out MaximumXMaximumZ,
                    out MinimumXMaximumZ
                );
        }

        public Vector2 WorldToLocalXZ(
            Vector2 worldXZ
        )
        {
            return
                TerrainStampTransformUtility
                    .WorldToLocalXZ(
                        worldXZ,
                        CenterXZ,
                        RotationDegrees
                    );
        }

        public Vector2 LocalToWorldXZ(
            Vector2 localXZ
        )
        {
            return
                TerrainStampTransformUtility
                    .LocalToWorldXZ(
                        localXZ,
                        CenterXZ,
                        RotationDegrees
                    );
        }

        public Vector2 GetEdgeCenterXZ(
            StampEdge edge
        )
        {
            switch (edge)
            {
                case StampEdge.MinimumX:
                    return
                        LocalToWorldXZ(
                            new Vector2(
                                -HalfWidth,
                                0f
                            )
                        );

                case StampEdge.MaximumX:
                    return
                        LocalToWorldXZ(
                            new Vector2(
                                HalfWidth,
                                0f
                            )
                        );

                case StampEdge.MinimumZ:
                    return
                        LocalToWorldXZ(
                            new Vector2(
                                0f,
                                -HalfDepth
                            )
                        );

                default:
                    return
                        LocalToWorldXZ(
                            new Vector2(
                                0f,
                                HalfDepth
                            )
                        );
            }
        }

        public Vector2 GetCornerXZ(
            StampCorner corner
        )
        {
            switch (corner)
            {
                case StampCorner.MinimumXMinimumZ:
                    return
                        MinimumXMinimumZ;

                case StampCorner.MaximumXMinimumZ:
                    return
                        MaximumXMinimumZ;

                case StampCorner.MaximumXMaximumZ:
                    return
                        MaximumXMaximumZ;

                default:
                    return
                        MinimumXMaximumZ;
            }
        }
    }
}
