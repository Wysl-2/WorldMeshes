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

    private int activeHandleControlId;

    private float activeHandlePlaneY =
        float.NaN;

    private bool activeResizeStateValid;

    private float activeResizeAspectRatio =
        1f;

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
                new Vector3(
                    footprint.CenterX,
                    planeY,
                    footprint.CenterZ
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

        StampFootprint inner =
            GetFalloffFootprint(
                stamp
            );

        Color oldColor =
            Handles.color;

        Handles.color =
            Handles.preselectionColor;

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
            Vector3 center =
                new Vector3(
                    outer.CenterX,
                    planeY,
                    outer.CenterZ
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
                    Vector3.right *
                    markerSize,
                center +
                    Vector3.right *
                    markerSize
            );

            Handles.DrawLine(
                center -
                    Vector3.forward *
                    markerSize,
                center +
                    Vector3.forward *
                    markerSize
            );
        }
        else
        {
            DrawFootprintOutline(
                inner,
                planeY,
                false
            );
        }

        Handles.color =
            oldColor;
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
                    minimumX,
                    minimumX +
                        tileWorldSize,
                    minimumZ,
                    minimumZ +
                        tileWorldSize
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

        Vector3 footprintEdge =
            new Vector3(
                footprint.MaximumX,
                planeY,
                footprint.CenterZ
            );

        float baseHandleSize =
            GetHandleSize(
                footprintEdge
            );

        Vector3 handleBase =
            footprintEdge +
            Vector3.right *
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
                Vector3.forward *
                tickSize,
            handleBase +
                Vector3.forward *
                tickSize
        );

        Handles.DrawLine(
            handlePosition -
                Vector3.forward *
                tickSize,
            handlePosition +
                Vector3.forward *
                tickSize
        );

        Handles.Label(
            handleBase +
                Vector3.right *
                baseHandleSize,
            "0 m",
            EditorStyles.miniLabel
        );

        Handles.Label(
            handlePosition +
                Vector3.right *
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
            new Vector3(
                footprint.CenterX,
                planeY,
                footprint.CenterZ
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
            Vector2 position;
            Vector2 size;

            CalculateEdgeFootprint(
                footprint,
                edge,
                moved,
                Event.current.alt,
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
            Vector2 position;
            Vector2 size;

            CalculateCornerFootprint(
                footprint,
                corner,
                moved,
                Event.current.alt,
                Event.current.shift,
                GetActiveResizeAspectRatio(
                    stamp
                ),
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
            (
                edge == StampEdge.MinimumX
                ||
                edge == StampEdge.MaximumX
            )
                ? Vector3.right
                : Vector3.forward;

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

        switch (edge)
        {
            case StampEdge.MinimumX:
                return
                    new Vector3(
                        inner.MinimumX,
                        planeY,
                        inner.CenterZ +
                            tangentOffsetZ
                    );

            case StampEdge.MaximumX:
                return
                    new Vector3(
                        inner.MaximumX,
                        planeY,
                        inner.CenterZ -
                            tangentOffsetZ
                    );

            case StampEdge.MinimumZ:
                return
                    new Vector3(
                        inner.CenterX -
                            tangentOffsetX,
                        planeY,
                        inner.MinimumZ
                    );

            default:
                return
                    new Vector3(
                        inner.CenterX +
                            tangentOffsetX,
                        planeY,
                        inner.MaximumZ
                    );
        }
    }

    private static float CalculateFalloffFromHandle(
        StampFootprint outer,
        StampEdge edge,
        Vector3 moved
    )
    {
        float falloff;

        switch (edge)
        {
            case StampEdge.MinimumX:
                falloff =
                    (
                        moved.x -
                        outer.MinimumX
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
                        outer.MaximumX -
                        moved.x
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
                        moved.z -
                        outer.MinimumZ
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
                        outer.MaximumZ -
                        moved.z
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
                Vector2 size =
                    stamp.SizeXZ;

                activeResizeAspectRatio =
                    Mathf.Max(
                        0.000001f,
                        size.x
                        /
                        Mathf.Max(
                            0.000001f,
                            size.y
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

    private void ResetActiveResizeState()
    {
        activeResizeStateValid =
            false;

        activeResizeAspectRatio =
            1f;
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

        float insetX =
            outer.Width *
            falloff *
            0.5f;

        float insetZ =
            outer.Depth *
            falloff *
            0.5f;

        return
            new StampFootprint(
                outer.MinimumX +
                    insetX,
                outer.MaximumX -
                    insetX,
                outer.MinimumZ +
                    insetZ,
                outer.MaximumZ -
                    insetZ
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
        float minimumX =
            source.MinimumX;

        float maximumX =
            source.MaximumX;

        float minimumZ =
            source.MinimumZ;

        float maximumZ =
            source.MaximumZ;

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
                            source.CenterX -
                                moved.x
                        );

                    minimumX =
                        source.CenterX -
                        halfSize;

                    maximumX =
                        source.CenterX +
                        halfSize;

                    break;
                }

                case StampEdge.MaximumX:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            moved.x -
                                source.CenterX
                        );

                    minimumX =
                        source.CenterX -
                        halfSize;

                    maximumX =
                        source.CenterX +
                        halfSize;

                    break;
                }

                case StampEdge.MinimumZ:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            source.CenterZ -
                                moved.z
                        );

                    minimumZ =
                        source.CenterZ -
                        halfSize;

                    maximumZ =
                        source.CenterZ +
                        halfSize;

                    break;
                }

                default:
                {
                    float halfSize =
                        Mathf.Max(
                            minimumHalfSize,
                            moved.z -
                                source.CenterZ
                        );

                    minimumZ =
                        source.CenterZ -
                        halfSize;

                    maximumZ =
                        source.CenterZ +
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

        ConvertBoundsToFootprint(
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
                        source.CenterX -
                            moved.x
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        source.CenterZ -
                            moved.z
                    );
                break;

            case StampCorner.MaximumXMinimumZ:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        moved.x -
                            source.CenterX
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        source.CenterZ -
                            moved.z
                    );
                break;

            case StampCorner.MaximumXMaximumZ:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        moved.x -
                            source.CenterX
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        moved.z -
                            source.CenterZ
                    );
                break;

            default:
                halfX =
                    Mathf.Max(
                        minimumHalfSize,
                        source.CenterX -
                            moved.x
                    );

                halfZ =
                    Mathf.Max(
                        minimumHalfSize,
                        moved.z -
                            source.CenterZ
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

        halfX =
            width *
            0.5f;

        halfZ =
            depth *
            0.5f;

        position =
            new Vector2(
                source.CenterX,
                source.CenterZ
            );

        size =
            new Vector2(
                Mathf.Max(
                    MinimumStampSize,
                    halfX *
                        2f
                ),
                Mathf.Max(
                    MinimumStampSize,
                    halfZ *
                        2f
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
            )
            *
            0.5f;

        public float CenterZ =>
            (
                MinimumZ +
                MaximumZ
            )
            *
            0.5f;

        public float Width =>
            Mathf.Max(
                0f,
                MaximumX -
                    MinimumX
            );

        public float Depth =>
            Mathf.Max(
                0f,
                MaximumZ -
                    MinimumZ
            );

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
