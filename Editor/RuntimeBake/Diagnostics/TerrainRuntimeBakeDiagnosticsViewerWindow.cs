using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public sealed class TerrainRuntimeBakeDiagnosticsViewerWindow : EditorWindow
{
    private enum ViewerSection
    {
        Overview,
        Planning,
        Execution,
        Performance,
        Trace,
        Events,
        Raw
    }

    private enum ViewerSeverity
    {
        All,
        Info,
        Warning,
        Error
    }

    private enum RawViewMode
    {
        Text,
        Json
    }

    private sealed class ViewerEvent
    {
        public ViewerSeverity severity;
        public TerrainRuntimeBakePipelineState? stage;
        public string category;
        public double durationSeconds;
        public string message;
        public string detail;
    }

    private sealed class TraceTree : TreeView
    {
        private readonly Dictionary<int, TerrainRuntimeBakeTraceRecord>
            recordByItemId =
                new Dictionary<int, TerrainRuntimeBakeTraceRecord>();

        private readonly Action<TerrainRuntimeBakeTraceRecord>
            onSelected;

        private IReadOnlyList<TerrainRuntimeBakeTraceRecord> records;
        private Func<TerrainRuntimeBakeTraceRecord, bool> filter;
        private double expensiveThresholdSeconds;
        private int nextItemId;

        public TraceTree(
            TreeViewState state,
            Action<TerrainRuntimeBakeTraceRecord> onSelected
        )
            : base(state)
        {
            this.onSelected = onSelected;
            showBorder = true;
            showAlternatingRowBackgrounds = true;
            rowHeight = 20f;
        }

        public void SetData(
            IReadOnlyList<TerrainRuntimeBakeTraceRecord> records,
            Func<TerrainRuntimeBakeTraceRecord, bool> filter
        )
        {
            this.records = records;
            this.filter = filter;
            expensiveThresholdSeconds =
                CalculateExpensiveThreshold(
                    records
                );

            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            recordByItemId.Clear();
            nextItemId = 1;

            TreeViewItem root =
                new TreeViewItem
                {
                    id = 0,
                    depth = -1,
                    displayName = "Root"
                };

            if (
                records == null
                ||
                records.Count == 0
            )
            {
                root.children =
                    new List<TreeViewItem>();

                SetupDepthsFromParentsAndChildren(
                    root
                );

                return root;
            }

            Dictionary<long, TerrainRuntimeBakeTraceRecord>
                recordByTraceId =
                    new Dictionary<long, TerrainRuntimeBakeTraceRecord>();

            Dictionary<long, List<TerrainRuntimeBakeTraceRecord>>
                childrenByParent =
                    new Dictionary<long, List<TerrainRuntimeBakeTraceRecord>>();

            for (int index = 0; index < records.Count; index++)
            {
                TerrainRuntimeBakeTraceRecord record =
                    records[index];

                recordByTraceId[record.Id] =
                    record;

                if (
                    !childrenByParent.TryGetValue(
                        record.ParentId,
                        out List<TerrainRuntimeBakeTraceRecord> children
                    )
                )
                {
                    children =
                        new List<TerrainRuntimeBakeTraceRecord>();

                    childrenByParent.Add(
                        record.ParentId,
                        children
                    );
                }

                children.Add(
                    record
                );
            }

            foreach (
                List<TerrainRuntimeBakeTraceRecord> children
                in childrenByParent.Values
            )
            {
                children.Sort(
                    (left, right) =>
                        left.StartedAtSeconds.CompareTo(
                            right.StartedAtSeconds
                        )
                );
            }

            List<TerrainRuntimeBakeTraceRecord> roots =
                new List<TerrainRuntimeBakeTraceRecord>();

            for (int index = 0; index < records.Count; index++)
            {
                TerrainRuntimeBakeTraceRecord record =
                    records[index];

                if (
                    record.ParentId == 0
                    ||
                    !recordByTraceId.ContainsKey(
                        record.ParentId
                    )
                )
                {
                    roots.Add(
                        record
                    );
                }
            }

            roots.Sort(
                (left, right) =>
                    left.StartedAtSeconds.CompareTo(
                        right.StartedAtSeconds
                    )
            );

            HashSet<long> visited =
                new HashSet<long>();

            List<TreeViewItem> rootItems =
                new List<TreeViewItem>();

            for (int index = 0; index < roots.Count; index++)
            {
                TreeViewItem item =
                    BuildItem(
                        roots[index],
                        childrenByParent,
                        visited
                    );

                if (item != null)
                {
                    rootItems.Add(
                        item
                    );
                }
            }

            root.children =
                rootItems;

            SetupDepthsFromParentsAndChildren(
                root
            );

            return root;
        }

        protected override void SelectionChanged(
            IList<int> selectedIds
        )
        {
            if (
                selectedIds == null
                ||
                selectedIds.Count == 0
            )
            {
                onSelected?.Invoke(
                    null
                );

                return;
            }

            if (
                recordByItemId.TryGetValue(
                    selectedIds[0],
                    out TerrainRuntimeBakeTraceRecord record
                )
            )
            {
                onSelected?.Invoke(
                    record
                );
            }
        }

        private TreeViewItem BuildItem(
            TerrainRuntimeBakeTraceRecord record,
            Dictionary<long, List<TerrainRuntimeBakeTraceRecord>>
                childrenByParent,
            HashSet<long> visited
        )
        {
            if (
                record == null
                ||
                !visited.Add(
                    record.Id
                )
            )
            {
                return null;
            }

            List<TreeViewItem> childItems =
                new List<TreeViewItem>();

            if (
                childrenByParent.TryGetValue(
                    record.Id,
                    out List<TerrainRuntimeBakeTraceRecord> children
                )
            )
            {
                for (int index = 0; index < children.Count; index++)
                {
                    TreeViewItem child =
                        BuildItem(
                            children[index],
                            childrenByParent,
                            visited
                        );

                    if (child != null)
                    {
                        childItems.Add(
                            child
                        );
                    }
                }
            }

            bool matches =
                filter == null
                ||
                filter(
                    record
                );

            if (
                !matches
                &&
                childItems.Count == 0
            )
            {
                return null;
            }

            int itemId =
                nextItemId++;

            string prefix =
                record.DurationSeconds >=
                    expensiveThresholdSeconds
                &&
                expensiveThresholdSeconds > 0d
                    ? "[Cost] "
                    : "";

            string displayName =
                prefix +
                record.Name +
                "  |  +" +
                record.StartedAtSeconds.ToString("0.000") +
                "s  |  " +
                (record.DurationSeconds * 1000d).ToString("0.0") +
                " ms  |  " +
                record.Outcome;

            TreeViewItem item =
                new TreeViewItem(
                    itemId,
                    0,
                    displayName
                );

            if (childItems.Count > 0)
            {
                item.children =
                    childItems;
            }

            recordByItemId.Add(
                itemId,
                record
            );

            return item;
        }

        private static double CalculateExpensiveThreshold(
            IReadOnlyList<TerrainRuntimeBakeTraceRecord> records
        )
        {
            if (
                records == null
                ||
                records.Count == 0
            )
            {
                return 0d;
            }

            double maximum =
                0d;

            for (int index = 0; index < records.Count; index++)
            {
                maximum =
                    Math.Max(
                        maximum,
                        records[index].DurationSeconds
                    );
            }

            return
                Math.Max(
                    0.050d,
                    maximum * 0.25d
                );
        }
    }

    private static readonly string[] SectionLabels =
    {
        "Overview",
        "Planning",
        "Execution",
        "Performance",
        "Trace",
        "Events",
        "Raw"
    };

    private static readonly TerrainRuntimeBakePipelineState[] FilterStages =
    {
        TerrainRuntimeBakePipelineState.Preflight,
        TerrainRuntimeBakePipelineState.Heightmaps,
        TerrainRuntimeBakePipelineState.SurfaceMasks,
        TerrainRuntimeBakePipelineState.Collision,
        TerrainRuntimeBakePipelineState.Addressables,
        TerrainRuntimeBakePipelineState.SceneSync,
        TerrainRuntimeBakePipelineState.Finalizing,
        TerrainRuntimeBakePipelineState.Completed,
        TerrainRuntimeBakePipelineState.Failed,
        TerrainRuntimeBakePipelineState.Cancelled,
        TerrainRuntimeBakePipelineState.Blocked
    };

    private static readonly string[] StageFilterLabels =
    {
        "All",
        "Preflight",
        "Heightmaps",
        "SurfaceMasks",
        "Collision",
        "Addressables",
        "SceneSync",
        "Finalizing",
        "Completed",
        "Failed",
        "Cancelled",
        "Blocked"
    };

    private static readonly string[] CategoryFilterLabels =
    {
        "All",
        "Prepare",
        "Gather",
        "Generate",
        "Validate",
        "Commit",
        "Finalize",
        "AssetDatabase",
        "Addressables",
        "SceneSync",
        "Other",
        "HeightBuffer",
        "SurfaceBuffer",
        "CollisionBuffer",
        "OtherTemporary",
        "Pipeline",
        "Trace",
        "Memory"
    };

    private const int MaxCoordinateDisplayCount = 256;

    private ViewerSection currentSection =
        ViewerSection.Overview;

    private RawViewMode rawViewMode =
        RawViewMode.Text;

    private int stageFilterIndex;
    private int categoryFilterIndex;
    private ViewerSeverity severityFilter =
        ViewerSeverity.All;

    private string coordinateFilter = "";
    private double minimumDurationMilliseconds;
    private string textSearch = "";

    private Vector2 contentScrollPosition;
    private Vector2 rawScrollPosition;

    private readonly Dictionary<TerrainRuntimeBakePlanSnapshotKind, bool>
        planningFoldouts =
            new Dictionary<TerrainRuntimeBakePlanSnapshotKind, bool>();

    private TreeViewState traceTreeState;
    private TraceTree traceTree;
    private TerrainRuntimeBakeTraceRecord selectedTraceRecord;

    private TerrainRuntimeBakePipelineResult observedResult;
    private string cachedRawText = "";
    private string cachedRawJson = "";

    private string selectedInformation = "";
    private string selectedInformationLabel = "";

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/Open Diagnostics Viewer"
    )]
    public static void OpenWindow()
    {
        TerrainRuntimeBakeDiagnosticsViewerWindow window =
            GetWindow<TerrainRuntimeBakeDiagnosticsViewerWindow>();

        window.titleContent =
            new GUIContent(
                "Bake Diagnostics"
            );

        window.minSize =
            new Vector2(
                760f,
                460f
            );

        window.SyncObservedResult(
            true
        );

        window.Show();
    }

    private void OnEnable()
    {
        if (traceTreeState == null)
        {
            traceTreeState =
                new TreeViewState();
        }

        if (traceTree == null)
        {
            traceTree =
                new TraceTree(
                    traceTreeState,
                    OnTraceSelected
                );
        }

        InitializePlanningFoldouts();
        SyncObservedResult(
            true
        );
    }

    private void OnInspectorUpdate()
    {
        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        SyncObservedResult(
            false
        );

        DrawTopStatus();

        currentSection =
            (ViewerSection)
            GUILayout.Toolbar(
                (int)currentSection,
                SectionLabels
            );

        EditorGUILayout.Space(
            4f
        );

        bool filtersChanged =
            DrawFilters();

        if (filtersChanged)
        {
            RefreshTraceTree();
        }

        EditorGUILayout.Space(
            4f
        );

        DrawActions();

        EditorGUILayout.Space(
            4f
        );

        if (currentSection == ViewerSection.Trace)
        {
            DrawTrace();
            return;
        }

        if (currentSection == ViewerSection.Raw)
        {
            DrawRaw();
            return;
        }

        contentScrollPosition =
            EditorGUILayout.BeginScrollView(
                contentScrollPosition
            );

        switch (currentSection)
        {
            case ViewerSection.Overview:
                DrawOverview();
                break;

            case ViewerSection.Planning:
                DrawPlanning();
                break;

            case ViewerSection.Execution:
                DrawExecution();
                break;

            case ViewerSection.Performance:
                DrawPerformance();
                break;

            case ViewerSection.Events:
                DrawEvents();
                break;

        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawTopStatus()
    {
        using (
            new EditorGUILayout.HorizontalScope(
                EditorStyles.toolbar
            )
        )
        {
            GUILayout.Label(
                "WorldMeshes Runtime Bake Diagnostics",
                EditorStyles.boldLabel
            );

            GUILayout.FlexibleSpace();

            if (TerrainRuntimeBakePipeline.IsRunning)
            {
                GUILayout.Label(
                    "Running: " +
                    TerrainRuntimeBakePipeline.CurrentStageLabel
                );
            }
            else
            {
                GUILayout.Label(
                    "Idle"
                );
            }
        }

        if (
            TerrainRuntimeBakePipeline.IsRunning
            &&
            TerrainRuntimeBakePipeline.LastResult != null
        )
        {
            EditorGUILayout.HelpBox(
                "A Runtime Bake is currently running. The viewer remains read-only and displays the latest completed result until the active bake finishes.",
                MessageType.Info
            );
        }
    }

    private bool DrawFilters()
    {
        bool changed =
            false;

        EditorGUI.BeginChangeCheck();

        using (new EditorGUILayout.HorizontalScope())
        {
            stageFilterIndex =
                EditorGUILayout.Popup(
                    "Stage",
                    stageFilterIndex,
                    StageFilterLabels
                );

            categoryFilterIndex =
                EditorGUILayout.Popup(
                    "Category",
                    categoryFilterIndex,
                    CategoryFilterLabels
                );

            severityFilter =
                (ViewerSeverity)
                EditorGUILayout.EnumPopup(
                    "Severity",
                    severityFilter
                );
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            coordinateFilter =
                EditorGUILayout.TextField(
                    "Chunk / Tile",
                    coordinateFilter
                );

            minimumDurationMilliseconds =
                Math.Max(
                    0d,
                    EditorGUILayout.DoubleField(
                        "Minimum ms",
                        minimumDurationMilliseconds
                    )
                );

            textSearch =
                EditorGUILayout.TextField(
                    "Search",
                    textSearch
                );

            if (
                GUILayout.Button(
                    "Reset",
                    GUILayout.Width(72f)
                )
            )
            {
                stageFilterIndex = 0;
                categoryFilterIndex = 0;
                severityFilter = ViewerSeverity.All;
                coordinateFilter = "";
                minimumDurationMilliseconds = 0d;
                textSearch = "";
                changed = true;
                GUI.FocusControl(
                    null
                );
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            changed = true;
        }

        if (
            !string.IsNullOrWhiteSpace(
                coordinateFilter
            )
            &&
            !TryParseCoordinateFilter(
                out _
            )
        )
        {
            EditorGUILayout.HelpBox(
                "Chunk / Tile filter expects coordinates such as 12,8 or (12, 8). Trace/Events can still match the raw text, but Planning/Execution coordinate matching requires valid coordinates.",
                MessageType.Warning
            );
        }

        if (
            currentSection == ViewerSection.Trace
            &&
            categoryFilterIndex != 0
        )
        {
            EditorGUILayout.HelpBox(
                "Trace records do not store a performance category. The Category filter applies to Performance and Events; Trace filtering uses Stage, Severity, coordinate text, minimum duration, and Search.",
                MessageType.Info
            );
        }

        return changed;
    }

    private void DrawActions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(
                string.IsNullOrEmpty(
                    selectedInformation
                )
            );

            if (
                GUILayout.Button(
                    "Copy Selected",
                    GUILayout.Width(110f)
                )
            )
            {
                EditorGUIUtility.systemCopyBuffer =
                    selectedInformation;
            }

            EditorGUI.EndDisabledGroup();

            if (
                GUILayout.Button(
                    "Export Text...",
                    GUILayout.Width(100f)
                )
            )
            {
                ExportText();
            }

            if (
                GUILayout.Button(
                    "Export JSON...",
                    GUILayout.Width(100f)
                )
            )
            {
                ExportJson();
            }

            GUILayout.FlexibleSpace();

            if (
                !string.IsNullOrEmpty(
                    selectedInformationLabel
                )
            )
            {
                GUILayout.Label(
                    "Selected: " +
                    selectedInformationLabel,
                    EditorStyles.miniLabel
                );

                if (
                    GUILayout.Button(
                        "Clear",
                        GUILayout.Width(56f)
                    )
                )
                {
                    ClearSelection();
                }
            }
        }
    }

    private void DrawOverview()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        if (result == null)
        {
            EditorGUILayout.HelpBox(
                "No completed Runtime Bake result is available.",
                MessageType.Info
            );
            return;
        }

        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            result.Diagnostics;

        DrawSectionHeading(
            "Bake"
        );

        DrawKeyValue(
            "Bake ID",
            diagnostics != null
                ? diagnostics.RunId
                : "Not captured"
        );
        DrawKeyValue(
            "Mode",
            result.Mode.ToString()
        );
        DrawKeyValue(
            "Outcome",
            result.Outcome.ToString()
        );
        DrawKeyValue(
            "Final State",
            result.FinalState.ToString()
        );
        DrawKeyValue(
            "Last Stage",
            result.LastStage.ToString()
        );
        DrawKeyValue(
            "Total Duration",
            result.DurationSeconds.ToString("0.000") +
            " s"
        );
        DrawKeyValue(
            "Diagnostics Level",
            diagnostics != null
                ? diagnostics.Level.ToString()
                : "Off / Not captured"
        );

        if (
            result.FailedStage !=
            TerrainRuntimeBakePipelineState.Idle
        )
        {
            DrawKeyValue(
                "Failed Stage",
                result.FailedStage.ToString()
            );
        }

        DrawSectionHeading(
            "Stage Summary"
        );

        if (
            diagnostics != null
            &&
            diagnostics.StageRecords != null
        )
        {
            for (
                int index = 0;
                index < diagnostics.StageRecords.Count;
                index++
            )
            {
                TerrainRuntimeBakeStageDiagnostic stage =
                    diagnostics.StageRecords[index];

                DrawKeyValue(
                    stage.Stage.ToString(),
                    stage.Status +
                    (
                        stage.EnteredAtUtc.HasValue
                            ? " at +" +
                              stage.EnteredAtSeconds.ToString("0.000") +
                              " s"
                            : ""
                    )
                );
            }
        }
        else
        {
            EditorGUILayout.LabelField(
                "No diagnostics stage records captured."
            );
        }

        DrawSectionHeading(
            "Work Counts"
        );

        if (
            diagnostics != null
            &&
            diagnostics.ExecutionStages != null
            &&
            diagnostics.ExecutionStages.Count > 0
        )
        {
            DrawExecutionTable(
                diagnostics.ExecutionStages,
                false
            );
        }
        else
        {
            EditorGUILayout.LabelField(
                "No execution metrics captured."
            );
        }

        DrawSectionHeading(
            "Memory"
        );

        DrawKeyValue(
            "Peak Tracked Temporary Memory",
            diagnostics != null
                ? FormatBytes(
                    diagnostics.PeakTrackedTemporaryBytes
                )
                : "Not captured"
        );

        if (diagnostics != null)
        {
            EditorGUILayout.LabelField(
                "Tracked values cover explicitly instrumented WorldMeshes temporary buffers only.",
                EditorStyles.miniLabel
            );
        }

        DrawSectionHeading(
            "Warnings / Errors"
        );

        int warningCount =
            result.WarningMessages != null
                ? result.WarningMessages.Count
                : 0;

        DrawKeyValue(
            "Warnings",
            warningCount.ToString()
        );
        DrawKeyValue(
            "Error",
            string.IsNullOrEmpty(
                result.ErrorMessage
            )
                ? "None"
                : result.ErrorMessage
        );
    }

    private void DrawPlanning()
    {
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            GetDiagnostics();

        if (diagnostics == null)
        {
            DrawMissingDiagnostics(
                "Planning data was not captured."
            );
            return;
        }

        DrawSectionHeading(
            "Run Reasons"
        );

        int runReasonCount =
            DrawReasonList(
                diagnostics.RunReasons,
                "Run"
            );

        if (runReasonCount == 0)
        {
            EditorGUILayout.LabelField(
                "No matching run reasons."
            );
        }

        DrawSectionHeading(
            "Plan Snapshots"
        );

        if (
            diagnostics.PlanSnapshots == null
            ||
            diagnostics.PlanSnapshots.Count == 0
        )
        {
            EditorGUILayout.LabelField(
                "No plan snapshots captured."
            );
        }
        else
        {
            for (
                int index = 0;
                index < diagnostics.PlanSnapshots.Count;
                index++
            )
            {
                TerrainRuntimeBakePlanDiagnosticSnapshot snapshot =
                    diagnostics.PlanSnapshots[index];

                if (
                    !PlanningSnapshotMatchesFilters(
                        snapshot
                    )
                )
                {
                    continue;
                }

                bool expanded =
                    GetPlanningFoldout(
                        snapshot.Kind
                    );

                expanded =
                    EditorGUILayout.Foldout(
                        expanded,
                        snapshot.Kind +
                        " Plan",
                        true
                    );

                planningFoldouts[snapshot.Kind] =
                    expanded;

                if (!expanded)
                {
                    continue;
                }

                using (
                    new EditorGUI.IndentLevelScope()
                )
                {
                    DrawPlanSnapshot(
                        snapshot
                    );
                }

                EditorGUILayout.Space(
                    4f
                );
            }
        }

        DrawSectionHeading(
            "Dependency Propagation"
        );

        int dependencyCount =
            0;

        if (diagnostics.PlanSnapshots != null)
        {
            for (
                int snapshotIndex = 0;
                snapshotIndex < diagnostics.PlanSnapshots.Count;
                snapshotIndex++
            )
            {
                TerrainRuntimeBakePlanDiagnosticSnapshot snapshot =
                    diagnostics.PlanSnapshots[snapshotIndex];

                if (snapshot.Reasons == null)
                {
                    continue;
                }

                for (
                    int reasonIndex = 0;
                    reasonIndex < snapshot.Reasons.Count;
                    reasonIndex++
                )
                {
                    TerrainRuntimeBakeReasonRecord reason =
                        snapshot.Reasons[reasonIndex];

                    if (
                        reason.Code != TerrainRuntimeBakeReasonCode.DependencyPropagation
                        &&
                        reason.Code != TerrainRuntimeBakeReasonCode.DependencyPropagationFailed
                    )
                    {
                        continue;
                    }

                    if (!ReasonMatchesFilters(reason))
                    {
                        continue;
                    }

                    dependencyCount++;

                    DrawSelectableRow(
                        FormatReason(
                            reason
                        ),
                        "Dependency reason",
                        FormatReason(
                            reason
                        )
                    );
                }
            }
        }

        if (dependencyCount == 0)
        {
            EditorGUILayout.LabelField(
                "No matching dependency-propagation reasons."
            );
        }
    }

    private void DrawExecution()
    {
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            GetDiagnostics();

        if (
            diagnostics == null
            ||
            diagnostics.ExecutionStages == null
            ||
            diagnostics.ExecutionStages.Count == 0
        )
        {
            DrawMissingDiagnostics(
                "Execution metrics were not captured."
            );
            return;
        }

        DrawSectionHeading(
            "Execution Metrics"
        );

        List<TerrainRuntimeBakeStageExecutionDiagnostic> matching =
            new List<TerrainRuntimeBakeStageExecutionDiagnostic>();

        for (
            int index = 0;
            index < diagnostics.ExecutionStages.Count;
            index++
        )
        {
            TerrainRuntimeBakeStageExecutionDiagnostic stage =
                diagnostics.ExecutionStages[index];

            if (
                ExecutionStageMatchesFilters(
                    stage
                )
            )
            {
                matching.Add(
                    stage
                );
            }
        }

        if (matching.Count == 0)
        {
            EditorGUILayout.LabelField(
                "No execution stages match the current filters."
            );
            return;
        }

        DrawExecutionTable(
            matching,
            true
        );

        EditorGUILayout.Space(
            6f
        );

        for (
            int index = 0;
            index < matching.Count;
            index++
        )
        {
            TerrainRuntimeBakeStageExecutionDiagnostic stage =
                matching[index];

            bool expanded =
                EditorGUILayout.Foldout(
                    true,
                    stage.Stage +
                    " Details",
                    true
                );

            if (!expanded)
            {
                continue;
            }

            using (
                new EditorGUI.IndentLevelScope()
            )
            {
                DrawExecutionStageDetails(
                    stage
                );
            }
        }
    }

    private void DrawPerformance()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            GetDiagnostics();

        if (result == null)
        {
            EditorGUILayout.HelpBox(
                "No completed Runtime Bake result is available.",
                MessageType.Info
            );
            return;
        }

        DrawSectionHeading(
            "Stage Timing"
        );

        DrawStageTimingRow(
            TerrainRuntimeBakePipelineState.Heightmaps,
            result.HeightDurationSeconds
        );
        DrawStageTimingRow(
            TerrainRuntimeBakePipelineState.SurfaceMasks,
            result.SurfaceDurationSeconds
        );
        DrawStageTimingRow(
            TerrainRuntimeBakePipelineState.Collision,
            result.CollisionDurationSeconds
        );
        DrawStageTimingRow(
            TerrainRuntimeBakePipelineState.Addressables,
            result.AddressablesDurationSeconds
        );
        DrawStageTimingRow(
            TerrainRuntimeBakePipelineState.SceneSync,
            result.SceneSyncDurationSeconds
        );

        if (diagnostics == null)
        {
            DrawMissingDiagnostics(
                "Fine-grained performance and memory diagnostics were not captured."
            );
            return;
        }

        DrawSectionHeading(
            "Operation Timing"
        );

        List<TerrainRuntimeBakePerformanceRecord> records =
            GetFilteredPerformanceRecords(
                diagnostics
            );

        if (records.Count == 0)
        {
            EditorGUILayout.LabelField(
                "No performance operations match the current filters."
            );
        }
        else
        {
            double maximum =
                0d;

            for (
                int index = 0;
                index < records.Count;
                index++
            )
            {
                maximum =
                    Math.Max(
                        maximum,
                        records[index].DurationSeconds
                    );
            }

            double expensiveThreshold =
                Math.Max(
                    0.050d,
                    maximum * 0.25d
                );

            GUIStyle expensiveStyle =
                new GUIStyle(
                    EditorStyles.label
                );

            expensiveStyle.fontStyle =
                FontStyle.Bold;

            EditorGUILayout.LabelField(
                "Bold = at least 25% of the longest shown operation and at least 50 ms.",
                EditorStyles.miniLabel
            );

            for (
                int index = 0;
                index < records.Count;
                index++
            )
            {
                TerrainRuntimeBakePerformanceRecord record =
                    records[index];

                string text =
                    record.Stage +
                    " / " +
                    record.Category +
                    " / " +
                    record.Name +
                    " | +" +
                    record.StartedAtSeconds.ToString("0.000") +
                    " s | " +
                    (record.DurationSeconds * 1000d).ToString("0.0") +
                    " ms";

                GUIStyle style =
                    record.DurationSeconds >=
                        expensiveThreshold
                        ? expensiveStyle
                        : EditorStyles.label;

                DrawSelectableRow(
                    text,
                    "Performance operation",
                    BuildPerformanceSelectionText(
                        record
                    ),
                    style
                );
            }
        }

        DrawSectionHeading(
            "Memory"
        );

        DrawKeyValue(
            "Current Tracked Temporary Memory",
            FormatBytes(
                diagnostics.CurrentTrackedTemporaryBytes
            )
        );
        DrawKeyValue(
            "Peak Tracked Temporary Memory",
            FormatBytes(
                diagnostics.PeakTrackedTemporaryBytes
            )
        );

        EditorGUILayout.LabelField(
            "Tracked WorldMeshes temporary buffers only; not total Unity/process/system memory.",
            EditorStyles.miniLabel
        );

        if (diagnostics.TrackedMemorySummaries != null)
        {
            for (
                int index = 0;
                index < diagnostics.TrackedMemorySummaries.Count;
                index++
            )
            {
                TerrainRuntimeBakeTrackedMemorySummary summary =
                    diagnostics.TrackedMemorySummaries[index];

                if (!CategoryMatches(summary.Category.ToString()))
                {
                    continue;
                }

                string text =
                    summary.Category +
                    " | current " +
                    FormatBytes(
                        summary.CurrentBytes
                    ) +
                    " | peak " +
                    FormatBytes(
                        summary.PeakBytes
                    );

                DrawSelectableRow(
                    text,
                    "Memory category",
                    text
                );
            }
        }
    }

    private void DrawTrace()
    {
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            GetDiagnostics();

        if (
            diagnostics == null
            ||
            diagnostics.TraceRecords == null
            ||
            diagnostics.TraceRecords.Count == 0
        )
        {
            EditorGUILayout.HelpBox(
                "No Trace records are available. Run a bake with TerrainRuntimeBakeDiagnostics.Level set to Trace to populate this view.",
                MessageType.Info
            );
            return;
        }

        if (traceTree == null)
        {
            traceTreeState =
                traceTreeState ??
                new TreeViewState();

            traceTree =
                new TraceTree(
                    traceTreeState,
                    OnTraceSelected
                );

            RefreshTraceTree();
        }

        EditorGUILayout.LabelField(
            "[Cost] marks scopes whose duration is at least 25% of the longest captured Trace scope and at least 50 ms.",
            EditorStyles.miniLabel
        );

        float treeHeight =
            Mathf.Max(
                220f,
                position.height - 360f
            );

        Rect treeRect =
            GUILayoutUtility.GetRect(
                100f,
                treeHeight,
                GUILayout.ExpandWidth(true)
            );

        traceTree.OnGUI(
            treeRect
        );

        if (selectedTraceRecord != null)
        {
            EditorGUILayout.Space(
                4f
            );

            DrawSectionHeading(
                "Selected Trace Scope"
            );

            DrawKeyValue(
                "Name",
                selectedTraceRecord.Name
            );
            DrawKeyValue(
                "Stage",
                selectedTraceRecord.Stage.ToString()
            );
            DrawKeyValue(
                "Outcome",
                selectedTraceRecord.Outcome.ToString()
            );
            DrawKeyValue(
                "Start",
                "+" +
                selectedTraceRecord.StartedAtSeconds.ToString("0.000") +
                " s / " +
                selectedTraceRecord.StartedAtUtc.ToString("u")
            );
            DrawKeyValue(
                "End",
                "+" +
                selectedTraceRecord.EndedAtSeconds.ToString("0.000") +
                " s / " +
                selectedTraceRecord.EndedAtUtc.ToString("u")
            );
            DrawKeyValue(
                "Duration",
                (selectedTraceRecord.DurationSeconds * 1000d).ToString("0.000") +
                " ms"
            );
            DrawKeyValue(
                "Parent ID",
                selectedTraceRecord.ParentId.ToString()
            );
            DrawKeyValue(
                "Scope ID",
                selectedTraceRecord.Id.ToString()
            );

            if (
                !string.IsNullOrEmpty(
                    selectedTraceRecord.Message
                )
            )
            {
                EditorGUILayout.LabelField(
                    "Message",
                    EditorStyles.boldLabel
                );
                EditorGUILayout.SelectableLabel(
                    selectedTraceRecord.Message,
                    GUILayout.MinHeight(38f)
                );
            }
        }
    }

    private void DrawEvents()
    {
        List<ViewerEvent> events =
            BuildFilteredEvents();

        DrawSectionHeading(
            "Diagnostic Events"
        );

        EditorGUILayout.LabelField(
            "Events are derived from existing immutable bake data: pipeline warnings/errors, non-completed Trace scopes, planner safety/blocking reasons, and tracked-memory allocation events.",
            EditorStyles.miniLabel
        );

        if (events.Count == 0)
        {
            EditorGUILayout.LabelField(
                "No events match the current filters."
            );
            return;
        }

        for (
            int index = 0;
            index < events.Count;
            index++
        )
        {
            ViewerEvent viewerEvent =
                events[index];

            string stage =
                viewerEvent.stage.HasValue
                    ? viewerEvent.stage.Value.ToString()
                    : "Run";

            string text =
                viewerEvent.severity +
                " | " +
                stage +
                " | " +
                viewerEvent.category +
                " | " +
                viewerEvent.message;

            DrawSelectableRow(
                text,
                "Event",
                BuildEventSelectionText(
                    viewerEvent
                )
            );
        }
    }

    private void DrawRaw()
    {
        rawViewMode =
            (RawViewMode)
            GUILayout.Toolbar(
                (int)rawViewMode,
                new[]
                {
                    "Text Report",
                    "JSON"
                }
            );

        EditorGUILayout.Space(
            4f
        );

        EditorGUILayout.LabelField(
            "Raw data is intentionally unfiltered.",
            EditorStyles.miniLabel
        );

        string value =
            rawViewMode == RawViewMode.Text
                ? cachedRawText
                : cachedRawJson;

        rawScrollPosition =
            EditorGUILayout.BeginScrollView(
                rawScrollPosition,
                GUILayout.ExpandHeight(true)
            );

        EditorGUILayout.TextArea(
            value ?? "",
            GUILayout.ExpandHeight(true)
        );

        EditorGUILayout.EndScrollView();
    }

    private void DrawPlanSnapshot(
        TerrainRuntimeBakePlanDiagnosticSnapshot snapshot
    )
    {
        if (!snapshot.IsAvailable)
        {
            EditorGUILayout.LabelField(
                "Unavailable"
            );
            return;
        }

        DrawKeyValue(
            "Captured",
            "+" +
            snapshot.CapturedAtSeconds.ToString("0.000") +
            " s / " +
            snapshot.CapturedAtUtc.ToString("u")
        );
        DrawKeyValue(
            "Height",
            snapshot.HeightWorkMode +
            " (" +
            snapshot.HeightTileCount +
            " tiles)"
        );
        DrawKeyValue(
            "Surface",
            snapshot.SurfaceWorkMode +
            " (" +
            snapshot.SurfaceTileCount +
            " tiles)"
        );
        DrawKeyValue(
            "Collision",
            snapshot.CollisionWorkMode +
            " (" +
            snapshot.CollisionChunkCount +
            " chunks)"
        );
        DrawKeyValue(
            "Addressables Configuration",
            snapshot.AddressablesConfigurationRequired.ToString()
        );
        DrawKeyValue(
            "Addressables Content Build",
            snapshot.AddressablesContentBuildRequired.ToString()
        );
        DrawKeyValue(
            "Runtime Scene Metadata",
            snapshot.RuntimeSceneMetadataUpdateRequired.ToString()
        );
        DrawKeyValue(
            "Initial Bake",
            snapshot.IsInitialBake.ToString()
        );
        DrawKeyValue(
            "Has Work",
            snapshot.HasWork.ToString()
        );
        DrawKeyValue(
            "Source State Revision",
            snapshot.SourceStateRevision.ToString()
        );

        if (snapshot.IsBlocked)
        {
            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(
                    snapshot.BlockReason
                )
                    ? "Plan is blocked."
                    : snapshot.BlockReason,
                MessageType.Error
            );
        }

        if (
            !string.IsNullOrEmpty(
                snapshot.CurrentAuthoringSignature
            )
        )
        {
            DrawKeyValue(
                "Current Authoring Signature",
                snapshot.CurrentAuthoringSignature
            );
        }

        if (
            !string.IsNullOrEmpty(
                snapshot.ObservedAuthoringSignature
            )
        )
        {
            DrawKeyValue(
                "Observed Authoring Signature",
                snapshot.ObservedAuthoringSignature
            );
        }

        if (
            !string.IsNullOrEmpty(
                snapshot.CurrentSurfaceSettingsSignature
            )
        )
        {
            DrawKeyValue(
                "Surface Settings Signature",
                snapshot.CurrentSurfaceSettingsSignature
            );
        }

        if (
            !string.IsNullOrEmpty(
                snapshot.CurrentCollisionSettingsSignature
            )
        )
        {
            DrawKeyValue(
                "Collision Settings Signature",
                snapshot.CurrentCollisionSettingsSignature
            );
        }

        if (snapshot.CoordinatesCaptured)
        {
            DrawCoordinateList(
                "Height Tiles",
                snapshot.HeightTiles
            );
            DrawCoordinateList(
                "Surface Tiles",
                snapshot.SurfaceTiles
            );
            DrawCoordinateList(
                "Collision Chunks",
                snapshot.CollisionChunks
            );
        }
        else
        {
            EditorGUILayout.LabelField(
                "Coordinates were not captured at this diagnostics level.",
                EditorStyles.miniLabel
            );
        }

        if (
            snapshot.SafetyEscalationReasons != null
            &&
            snapshot.SafetyEscalationReasons.Count > 0
        )
        {
            EditorGUILayout.LabelField(
                "Safety Escalations",
                EditorStyles.boldLabel
            );

            for (
                int index = 0;
                index < snapshot.SafetyEscalationReasons.Count;
                index++
            )
            {
                string message =
                    snapshot.SafetyEscalationReasons[index];

                DrawSelectableRow(
                    "- " +
                    message,
                    "Safety escalation",
                    message
                );
            }
        }

        if (
            snapshot.Reasons != null
            &&
            snapshot.Reasons.Count > 0
        )
        {
            EditorGUILayout.LabelField(
                "Reasons",
                EditorStyles.boldLabel
            );

            int count =
                DrawReasonList(
                    snapshot.Reasons,
                    snapshot.Kind.ToString()
                );

            if (count == 0)
            {
                EditorGUILayout.LabelField(
                    "No reasons match the current filters.",
                    EditorStyles.miniLabel
                );
            }
        }
    }

    private void DrawExecutionStageDetails(
        TerrainRuntimeBakeStageExecutionDiagnostic stage
    )
    {
        string summary =
            BuildExecutionSelectionText(
                stage
            );

        DrawSelectableRow(
            "Copyable stage summary",
            stage.Stage + " execution",
            summary
        );

        DrawCoordinateList(
            "Requested",
            stage.RequestedCoordinates
        );
        DrawCoordinateList(
            "Succeeded",
            stage.SucceededCoordinates
        );
        DrawCoordinateList(
            "Failed",
            stage.FailedCoordinates
        );
        DrawCoordinateList(
            "Unprocessed",
            stage.UnprocessedCoordinates
        );

        if (stage.Stage == TerrainRuntimeBakePipelineState.Addressables)
        {
            DrawKeyValue(
                "Configuration Requested / Performed",
                stage.AddressablesConfigurationRequested +
                " / " +
                stage.AddressablesConfigurationPerformed
            );
            DrawKeyValue(
                "Content Build Requested / Performed",
                stage.AddressablesContentBuildRequested +
                " / " +
                stage.AddressablesContentBuildPerformed
            );
            DrawKeyValue(
                "Marker Work Requested / Performed",
                stage.AddressablesMarkerWorkRequested +
                " / " +
                stage.AddressablesMarkerWorkPerformed
            );
            DrawKeyValue(
                "Markers Regenerated / Reused / Removed",
                stage.AddressablesMarkersRegenerated +
                " / " +
                stage.AddressablesMarkersReused +
                " / " +
                stage.AddressablesMarkersRemoved
            );
        }
        else if (stage.Stage == TerrainRuntimeBakePipelineState.SceneSync)
        {
            DrawKeyValue(
                "Metadata Requested / Performed",
                stage.SceneMetadataRequested +
                " / " +
                stage.SceneMetadataPerformed
            );
            DrawKeyValue(
                "Scene Modification Performed",
                stage.SceneModificationPerformed.ToString()
            );
            DrawKeyValue(
                "Serialized Component Changes",
                stage.SceneSerializedComponentChangeCount.ToString()
            );
        }
    }

    private int DrawReasonList(
        IReadOnlyList<TerrainRuntimeBakeReasonRecord> reasons,
        string label
    )
    {
        if (reasons == null)
        {
            return 0;
        }

        int count =
            0;

        for (
            int index = 0;
            index < reasons.Count;
            index++
        )
        {
            TerrainRuntimeBakeReasonRecord reason =
                reasons[index];

            if (!ReasonMatchesFilters(reason))
            {
                continue;
            }

            count++;

            string text =
                FormatReason(
                    reason
                );

            DrawSelectableRow(
                text,
                label + " reason",
                text
            );
        }

        return count;
    }

    private void DrawExecutionTable(
        IReadOnlyList<TerrainRuntimeBakeStageExecutionDiagnostic> stages,
        bool respectFilters
    )
    {
        EditorGUILayout.LabelField(
            "Stage | Planned | Requested | Evaluated | Generated | Committed | Skipped | Unchanged | Failed",
            EditorStyles.miniBoldLabel
        );

        for (
            int index = 0;
            index < stages.Count;
            index++
        )
        {
            TerrainRuntimeBakeStageExecutionDiagnostic stage =
                stages[index];

            if (
                respectFilters
                &&
                !ExecutionStageMatchesFilters(
                    stage
                )
            )
            {
                continue;
            }

            string text =
                stage.Stage +
                " | " +
                stage.PlannedCount +
                " | " +
                stage.RequestedCount +
                " | " +
                stage.EvaluatedCount +
                " | " +
                stage.GeneratedCount +
                " | " +
                stage.CommittedCount +
                " | " +
                stage.SkippedCount +
                " | " +
                stage.UnchangedCount +
                " | " +
                stage.FailedCount;

            DrawSelectableRow(
                text,
                stage.Stage + " execution",
                BuildExecutionSelectionText(
                    stage
                )
            );
        }
    }

    private void DrawStageTimingRow(
        TerrainRuntimeBakePipelineState stage,
        double seconds
    )
    {
        if (!StageMatches(stage))
        {
            return;
        }

        if (
            seconds * 1000d <
            minimumDurationMilliseconds
        )
        {
            return;
        }

        string text =
            stage +
            " | " +
            seconds.ToString("0.000") +
            " s";

        if (!TextMatches(text))
        {
            return;
        }

        DrawSelectableRow(
            text,
            stage + " timing",
            text
        );
    }

    private List<TerrainRuntimeBakePerformanceRecord>
        GetFilteredPerformanceRecords(
            TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
        )
    {
        List<TerrainRuntimeBakePerformanceRecord> records =
            new List<TerrainRuntimeBakePerformanceRecord>();

        if (
            diagnostics == null
            ||
            diagnostics.PerformanceRecords == null
        )
        {
            return records;
        }

        for (
            int index = 0;
            index < diagnostics.PerformanceRecords.Count;
            index++
        )
        {
            TerrainRuntimeBakePerformanceRecord record =
                diagnostics.PerformanceRecords[index];

            if (!StageMatches(record.Stage))
            {
                continue;
            }

            if (!CategoryMatches(record.Category.ToString()))
            {
                continue;
            }

            if (
                record.DurationSeconds * 1000d <
                minimumDurationMilliseconds
            )
            {
                continue;
            }

            if (
                !TextMatches(
                    record.Name,
                    record.Stage.ToString(),
                    record.Category.ToString()
                )
            )
            {
                continue;
            }

            records.Add(
                record
            );
        }

        records.Sort(
            (left, right) =>
                right.DurationSeconds.CompareTo(
                    left.DurationSeconds
                )
        );

        return records;
    }

    private List<ViewerEvent> BuildFilteredEvents()
    {
        List<ViewerEvent> events =
            new List<ViewerEvent>();

        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            GetDiagnostics();

        if (result != null)
        {
            if (result.WarningMessages != null)
            {
                for (
                    int index = 0;
                    index < result.WarningMessages.Count;
                    index++
                )
                {
                    events.Add(
                        new ViewerEvent
                        {
                            severity = ViewerSeverity.Warning,
                            stage = null,
                            category = "Pipeline",
                            durationSeconds = 0d,
                            message =
                                result.WarningMessages[index] ?? "",
                            detail =
                                result.WarningMessages[index] ?? ""
                        }
                    );
                }
            }

            if (
                !string.IsNullOrEmpty(
                    result.ErrorMessage
                )
            )
            {
                events.Add(
                    new ViewerEvent
                    {
                        severity = ViewerSeverity.Error,
                        stage =
                            result.FailedStage !=
                            TerrainRuntimeBakePipelineState.Idle
                                ? result.FailedStage
                                : (TerrainRuntimeBakePipelineState?)null,
                        category = "Pipeline",
                        durationSeconds = 0d,
                        message = result.ErrorMessage,
                        detail = result.ErrorMessage
                    }
                );
            }
        }

        if (diagnostics != null)
        {
            if (diagnostics.RunReasons != null)
            {
                for (
                    int index = 0;
                    index < diagnostics.RunReasons.Count;
                    index++
                )
                {
                    TerrainRuntimeBakeReasonRecord reason =
                        diagnostics.RunReasons[index];

                    if (
                        reason.IsSafetyEscalation
                        ||
                        reason.Code == TerrainRuntimeBakeReasonCode.BlockingValidation
                        ||
                        reason.Code == TerrainRuntimeBakeReasonCode.PlanningFailed
                    )
                    {
                        events.Add(
                            new ViewerEvent
                            {
                                severity =
                                    reason.Code ==
                                        TerrainRuntimeBakeReasonCode.PlanningFailed
                                        ? ViewerSeverity.Error
                                        : ViewerSeverity.Warning,
                                stage =
                                    StageForReasonTarget(
                                        reason.Target
                                    ),
                                category = "Pipeline",
                                durationSeconds = 0d,
                                message =
                                    FormatReason(
                                        reason
                                    ),
                                detail =
                                    FormatReason(
                                        reason
                                    )
                            }
                        );
                    }
                }
            }

            if (diagnostics.TraceRecords != null)
            {
                for (
                    int index = 0;
                    index < diagnostics.TraceRecords.Count;
                    index++
                )
                {
                    TerrainRuntimeBakeTraceRecord trace =
                        diagnostics.TraceRecords[index];

                    if (
                        trace.Outcome ==
                        TerrainRuntimeBakeTraceOutcome.Completed
                    )
                    {
                        continue;
                    }

                    events.Add(
                        new ViewerEvent
                        {
                            severity =
                                trace.Outcome ==
                                TerrainRuntimeBakeTraceOutcome.Failed
                                    ? ViewerSeverity.Error
                                    : ViewerSeverity.Warning,
                            stage = trace.Stage,
                            category = "Trace",
                            durationSeconds =
                                trace.DurationSeconds,
                            message =
                                trace.Name +
                                " -> " +
                                trace.Outcome,
                            detail =
                                BuildTraceSelectionText(
                                    trace
                                )
                        }
                    );
                }
            }

            if (diagnostics.MemoryEvents != null)
            {
                for (
                    int index = 0;
                    index < diagnostics.MemoryEvents.Count;
                    index++
                )
                {
                    TerrainRuntimeBakeMemoryEvent memoryEvent =
                        diagnostics.MemoryEvents[index];

                    string delta =
                        memoryEvent.ByteDelta >= 0L
                            ? "+" +
                              FormatBytes(
                                  memoryEvent.ByteDelta
                              )
                            : "-" +
                              FormatBytes(
                                  -memoryEvent.ByteDelta
                              );

                    events.Add(
                        new ViewerEvent
                        {
                            severity = ViewerSeverity.Info,
                            stage = memoryEvent.Stage,
                            category =
                                memoryEvent.Category.ToString(),
                            durationSeconds = 0d,
                            message =
                                memoryEvent.Name +
                                " " +
                                delta +
                                " at +" +
                                memoryEvent.AtSeconds.ToString("0.000") +
                                " s",
                            detail =
                                memoryEvent.Stage +
                                " / " +
                                memoryEvent.Category +
                                " / " +
                                memoryEvent.Name +
                                "\nDelta: " +
                                delta +
                                "\nCurrent tracked: " +
                                FormatBytes(
                                    memoryEvent.CurrentTrackedBytes
                                ) +
                                "\nAt: +" +
                                memoryEvent.AtSeconds.ToString("0.000") +
                                " s"
                        }
                    );
                }
            }
        }

        List<ViewerEvent> filtered =
            new List<ViewerEvent>();

        for (
            int index = 0;
            index < events.Count;
            index++
        )
        {
            ViewerEvent viewerEvent =
                events[index];

            if (
                severityFilter != ViewerSeverity.All
                &&
                viewerEvent.severity != severityFilter
            )
            {
                continue;
            }

            if (
                viewerEvent.stage.HasValue
                &&
                !StageMatches(
                    viewerEvent.stage.Value
                )
            )
            {
                continue;
            }

            if (
                !viewerEvent.stage.HasValue
                &&
                stageFilterIndex != 0
            )
            {
                continue;
            }

            if (!CategoryMatches(viewerEvent.category))
            {
                continue;
            }

            if (
                viewerEvent.durationSeconds * 1000d <
                minimumDurationMilliseconds
            )
            {
                continue;
            }

            if (
                !TextMatches(
                    viewerEvent.message,
                    viewerEvent.detail,
                    viewerEvent.category
                )
            )
            {
                continue;
            }

            if (
                !CoordinateTextMatches(
                    viewerEvent.message +
                    "\n" +
                    viewerEvent.detail
                )
            )
            {
                continue;
            }

            filtered.Add(
                viewerEvent
            );
        }

        return filtered;
    }

    private bool PlanningSnapshotMatchesFilters(
        TerrainRuntimeBakePlanDiagnosticSnapshot snapshot
    )
    {
        if (snapshot == null)
        {
            return false;
        }

        if (
            stageFilterIndex != 0
            &&
            snapshot.Kind != TerrainRuntimeBakePlanSnapshotKind.Initial
            &&
            snapshot.Kind != TerrainRuntimeBakePlanSnapshotKind.Final
        )
        {
            TerrainRuntimeBakePipelineState? mappedStage =
                StageForPlanKind(
                    snapshot.Kind
                );

            if (
                !mappedStage.HasValue
                ||
                !StageMatches(
                    mappedStage.Value
                )
            )
            {
                return false;
            }
        }

        if (
            TryParseCoordinateFilter(
                out Vector2Int coordinate
            )
        )
        {
            bool coordinateFound =
                ContainsCoordinate(
                    snapshot.HeightTiles,
                    coordinate
                )
                ||
                ContainsCoordinate(
                    snapshot.SurfaceTiles,
                    coordinate
                )
                ||
                ContainsCoordinate(
                    snapshot.CollisionChunks,
                    coordinate
                );

            if (!coordinateFound)
            {
                return false;
            }
        }

        if (
            !TextMatches(
                snapshot.Kind.ToString(),
                snapshot.HeightWorkMode.ToString(),
                snapshot.SurfaceWorkMode.ToString(),
                snapshot.CollisionWorkMode.ToString(),
                snapshot.BlockReason,
                snapshot.CurrentAuthoringSignature,
                snapshot.ObservedAuthoringSignature,
                snapshot.CurrentSurfaceSettingsSignature,
                snapshot.CurrentCollisionSettingsSignature
            )
        )
        {
            bool reasonMatch =
                false;

            if (snapshot.Reasons != null)
            {
                for (
                    int index = 0;
                    index < snapshot.Reasons.Count;
                    index++
                )
                {
                    if (
                        TextMatches(
                            FormatReason(
                                snapshot.Reasons[index]
                            )
                        )
                    )
                    {
                        reasonMatch = true;
                        break;
                    }
                }
            }

            if (!reasonMatch)
            {
                return false;
            }
        }

        return true;
    }

    private bool ExecutionStageMatchesFilters(
        TerrainRuntimeBakeStageExecutionDiagnostic stage
    )
    {
        if (stage == null)
        {
            return false;
        }

        if (!StageMatches(stage.Stage))
        {
            return false;
        }

        if (
            minimumDurationMilliseconds > 0d
        )
        {
            TerrainRuntimeBakePipelineResult result =
                TerrainRuntimeBakePipeline.LastResult;

            double duration =
                GetStageDuration(
                    result,
                    stage.Stage
                );

            if (
                duration * 1000d <
                minimumDurationMilliseconds
            )
            {
                return false;
            }
        }

        if (
            TryParseCoordinateFilter(
                out Vector2Int coordinate
            )
        )
        {
            bool found =
                ContainsCoordinate(
                    stage.RequestedCoordinates,
                    coordinate
                )
                ||
                ContainsCoordinate(
                    stage.SucceededCoordinates,
                    coordinate
                )
                ||
                ContainsCoordinate(
                    stage.FailedCoordinates,
                    coordinate
                )
                ||
                ContainsCoordinate(
                    stage.UnprocessedCoordinates,
                    coordinate
                );

            if (!found)
            {
                return false;
            }
        }

        return
            TextMatches(
                stage.Stage.ToString(),
                BuildExecutionSelectionText(
                    stage
                )
            );
    }

    private bool TraceRecordMatchesFilters(
        TerrainRuntimeBakeTraceRecord record
    )
    {
        if (record == null)
        {
            return false;
        }

        if (!StageMatches(record.Stage))
        {
            return false;
        }

        if (
            minimumDurationMilliseconds > 0d
            &&
            record.DurationSeconds * 1000d <
                minimumDurationMilliseconds
        )
        {
            return false;
        }

        if (
            severityFilter != ViewerSeverity.All
        )
        {
            ViewerSeverity traceSeverity =
                record.Outcome ==
                    TerrainRuntimeBakeTraceOutcome.Failed
                    ? ViewerSeverity.Error
                    : record.Outcome ==
                        TerrainRuntimeBakeTraceOutcome.Cancelled
                        ||
                        record.Outcome ==
                        TerrainRuntimeBakeTraceOutcome.Blocked
                            ? ViewerSeverity.Warning
                            : ViewerSeverity.Info;

            if (traceSeverity != severityFilter)
            {
                return false;
            }
        }

        if (
            !TextMatches(
                record.Name,
                record.Message,
                record.Stage.ToString(),
                record.Outcome.ToString()
            )
        )
        {
            return false;
        }

        return
            CoordinateTextMatches(
                record.Name +
                "\n" +
                record.Message
            );
    }

    private bool ReasonMatchesFilters(
        TerrainRuntimeBakeReasonRecord reason
    )
    {
        if (reason == null)
        {
            return false;
        }

        TerrainRuntimeBakePipelineState? stage =
            StageForReasonTarget(
                reason.Target
            );

        if (
            stageFilterIndex != 0
            &&
            (
                !stage.HasValue
                ||
                !StageMatches(
                    stage.Value
                )
            )
        )
        {
            return false;
        }

        if (
            severityFilter != ViewerSeverity.All
        )
        {
            ViewerSeverity reasonSeverity =
                reason.Code ==
                    TerrainRuntimeBakeReasonCode.PlanningFailed
                    ? ViewerSeverity.Error
                    : reason.IsSafetyEscalation
                        ||
                        reason.Code ==
                            TerrainRuntimeBakeReasonCode.BlockingValidation
                        ||
                        reason.Code ==
                            TerrainRuntimeBakeReasonCode.DependencyPropagationFailed
                            ? ViewerSeverity.Warning
                            : ViewerSeverity.Info;

            if (reasonSeverity != severityFilter)
            {
                return false;
            }
        }

        string text =
            FormatReason(
                reason
            );

        if (!TextMatches(text))
        {
            return false;
        }

        return
            CoordinateTextMatches(
                text
            );
    }

    private void SyncObservedResult(
        bool force
    )
    {
        TerrainRuntimeBakePipelineResult current =
            TerrainRuntimeBakePipeline.LastResult;

        if (
            !force
            &&
            ReferenceEquals(
                current,
                observedResult
            )
        )
        {
            return;
        }

        observedResult =
            current;

        selectedTraceRecord =
            null;

        ClearSelection();

        cachedRawText =
            TerrainRuntimeBakeReportFormatter.BuildTextReport(
                current,
                TerrainRuntimeBakeDiagnosticsLevel.Trace
            );

        cachedRawJson =
            TerrainRuntimeBakeReportJsonExporter.BuildJson(
                current
            );

        RefreshTraceTree();
        Repaint();
    }

    private void RefreshTraceTree()
    {
        if (traceTree == null)
        {
            return;
        }

        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            GetDiagnostics();

        traceTree.SetData(
            diagnostics != null
                ? diagnostics.TraceRecords
                : null,
            TraceRecordMatchesFilters
        );
    }

    private void OnTraceSelected(
        TerrainRuntimeBakeTraceRecord record
    )
    {
        selectedTraceRecord =
            record;

        if (record == null)
        {
            ClearSelection();
            return;
        }

        SetSelection(
            "Trace scope",
            BuildTraceSelectionText(
                record
            )
        );

        Repaint();
    }

    private void InitializePlanningFoldouts()
    {
        Array values =
            Enum.GetValues(
                typeof(
                    TerrainRuntimeBakePlanSnapshotKind
                )
            );

        for (int index = 0; index < values.Length; index++)
        {
            TerrainRuntimeBakePlanSnapshotKind kind =
                (TerrainRuntimeBakePlanSnapshotKind)
                values.GetValue(
                    index
                );

            if (!planningFoldouts.ContainsKey(kind))
            {
                planningFoldouts.Add(
                    kind,
                    kind ==
                        TerrainRuntimeBakePlanSnapshotKind.Initial
                    ||
                    kind ==
                        TerrainRuntimeBakePlanSnapshotKind.Final
                );
            }
        }
    }

    private bool GetPlanningFoldout(
        TerrainRuntimeBakePlanSnapshotKind kind
    )
    {
        if (
            planningFoldouts.TryGetValue(
                kind,
                out bool expanded
            )
        )
        {
            return expanded;
        }

        planningFoldouts[kind] =
            false;

        return false;
    }

    private TerrainRuntimeBakeDiagnosticsSnapshot GetDiagnostics()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        return
            result != null
                ? result.Diagnostics
                : null;
    }

    private bool StageMatches(
        TerrainRuntimeBakePipelineState stage
    )
    {
        if (stageFilterIndex == 0)
        {
            return true;
        }

        int arrayIndex =
            stageFilterIndex -
            1;

        return
            arrayIndex >= 0
            &&
            arrayIndex < FilterStages.Length
            &&
            FilterStages[arrayIndex] == stage;
    }

    private bool CategoryMatches(
        string category
    )
    {
        if (categoryFilterIndex == 0)
        {
            return true;
        }

        if (
            categoryFilterIndex < 0
            ||
            categoryFilterIndex >=
                CategoryFilterLabels.Length
        )
        {
            return true;
        }

        return
            string.Equals(
                category ?? "",
                CategoryFilterLabels[
                    categoryFilterIndex
                ],
                StringComparison.OrdinalIgnoreCase
            )
            ||
            string.Equals(
                CategoryFilterLabels[
                    categoryFilterIndex
                ],
                "Memory",
                StringComparison.OrdinalIgnoreCase
            )
            &&
            (
                string.Equals(
                    category,
                    "HeightBuffer",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                string.Equals(
                    category,
                    "SurfaceBuffer",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                string.Equals(
                    category,
                    "CollisionBuffer",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                string.Equals(
                    category,
                    "OtherTemporary",
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    private bool TextMatches(
        params string[] values
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                textSearch
            )
        )
        {
            return true;
        }

        string search =
            textSearch.Trim();

        if (values == null)
        {
            return false;
        }

        for (int index = 0; index < values.Length; index++)
        {
            string value =
                values[index];

            if (
                !string.IsNullOrEmpty(
                    value
                )
                &&
                value.IndexOf(
                    search,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool CoordinateTextMatches(
        string value
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                coordinateFilter
            )
        )
        {
            return true;
        }

        string raw =
            coordinateFilter.Trim();

        if (
            !TryParseCoordinateFilter(
                out Vector2Int coordinate
            )
        )
        {
            return
                !string.IsNullOrEmpty(
                    value
                )
                &&
                value.IndexOf(
                    raw,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        string canonical =
            "(" +
            coordinate.x +
            ", " +
            coordinate.y +
            ")";

        string compact =
            coordinate.x +
            "," +
            coordinate.y;

        return
            !string.IsNullOrEmpty(
                value
            )
            &&
            (
                value.IndexOf(
                    canonical,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
                ||
                value.IndexOf(
                    compact,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
            );
    }

    private bool TryParseCoordinateFilter(
        out Vector2Int coordinate
    )
    {
        coordinate =
            default;

        if (
            string.IsNullOrWhiteSpace(
                coordinateFilter
            )
        )
        {
            return false;
        }

        string value =
            coordinateFilter
                .Trim()
                .Replace("(", "")
                .Replace(")", "")
                .Replace(" ", "");

        string[] parts =
            value.Split(',');

        if (
            parts.Length != 2
            ||
            !int.TryParse(
                parts[0],
                out int x
            )
            ||
            !int.TryParse(
                parts[1],
                out int y
            )
        )
        {
            return false;
        }

        coordinate =
            new Vector2Int(
                x,
                y
            );

        return true;
    }

    private static bool ContainsCoordinate(
        IReadOnlyList<Vector2Int> coordinates,
        Vector2Int target
    )
    {
        if (coordinates == null)
        {
            return false;
        }

        for (int index = 0; index < coordinates.Count; index++)
        {
            if (coordinates[index] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static TerrainRuntimeBakePipelineState?
        StageForPlanKind(
            TerrainRuntimeBakePlanSnapshotKind kind
        )
    {
        switch (kind)
        {
            case TerrainRuntimeBakePlanSnapshotKind.Height:
                return TerrainRuntimeBakePipelineState.Heightmaps;

            case TerrainRuntimeBakePlanSnapshotKind.Surface:
                return TerrainRuntimeBakePipelineState.SurfaceMasks;

            case TerrainRuntimeBakePlanSnapshotKind.Collision:
                return TerrainRuntimeBakePipelineState.Collision;

            case TerrainRuntimeBakePlanSnapshotKind.Addressables:
                return TerrainRuntimeBakePipelineState.Addressables;

            case TerrainRuntimeBakePlanSnapshotKind.SceneSync:
                return TerrainRuntimeBakePipelineState.SceneSync;

            default:
                return null;
        }
    }

    private static TerrainRuntimeBakePipelineState?
        StageForReasonTarget(
            TerrainRuntimeBakeReasonTarget target
        )
    {
        switch (target)
        {
            case TerrainRuntimeBakeReasonTarget.Height:
                return TerrainRuntimeBakePipelineState.Heightmaps;

            case TerrainRuntimeBakeReasonTarget.Surface:
                return TerrainRuntimeBakePipelineState.SurfaceMasks;

            case TerrainRuntimeBakeReasonTarget.Collision:
                return TerrainRuntimeBakePipelineState.Collision;

            case TerrainRuntimeBakeReasonTarget.Addressables:
                return TerrainRuntimeBakePipelineState.Addressables;

            case TerrainRuntimeBakeReasonTarget.SceneSync:
                return TerrainRuntimeBakePipelineState.SceneSync;

            case TerrainRuntimeBakeReasonTarget.Blocking:
                return TerrainRuntimeBakePipelineState.Preflight;

            default:
                return null;
        }
    }

    private static double GetStageDuration(
        TerrainRuntimeBakePipelineResult result,
        TerrainRuntimeBakePipelineState stage
    )
    {
        if (result == null)
        {
            return 0d;
        }

        switch (stage)
        {
            case TerrainRuntimeBakePipelineState.Heightmaps:
                return result.HeightDurationSeconds;

            case TerrainRuntimeBakePipelineState.SurfaceMasks:
                return result.SurfaceDurationSeconds;

            case TerrainRuntimeBakePipelineState.Collision:
                return result.CollisionDurationSeconds;

            case TerrainRuntimeBakePipelineState.Addressables:
                return result.AddressablesDurationSeconds;

            case TerrainRuntimeBakePipelineState.SceneSync:
                return result.SceneSyncDurationSeconds;

            default:
                return 0d;
        }
    }

    private static string FormatReason(
        TerrainRuntimeBakeReasonRecord reason
    )
    {
        if (reason == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            reason.Origin
        );
        builder.Append(
            " / "
        );
        builder.Append(
            reason.Target
        );
        builder.Append(
            " / "
        );
        builder.Append(
            reason.Code
        );

        if (reason.SourceTarget.HasValue)
        {
            builder.Append(
                " <- "
            );
            builder.Append(
                reason.SourceTarget.Value
            );
        }

        if (reason.AffectedItemCount > 0)
        {
            builder.Append(
                " ("
            );
            builder.Append(
                reason.AffectedItemCount
            );
            builder.Append(
                " items)"
            );
        }

        if (reason.IsSafetyEscalation)
        {
            builder.Append(
                " [Safety]"
            );
        }

        if (
            !string.IsNullOrEmpty(
                reason.Message
            )
        )
        {
            builder.Append(
                ": "
            );
            builder.Append(
                reason.Message
            );
        }

        return
            builder.ToString();
    }

    private static string BuildExecutionSelectionText(
        TerrainRuntimeBakeStageExecutionDiagnostic stage
    )
    {
        if (stage == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            stage.Stage.ToString()
        );
        builder.AppendLine(
            "Planned: " +
            stage.PlannedCount
        );
        builder.AppendLine(
            "Requested: " +
            stage.RequestedCount
        );
        builder.AppendLine(
            "Evaluated: " +
            stage.EvaluatedCount
        );
        builder.AppendLine(
            "Generated: " +
            stage.GeneratedCount
        );
        builder.AppendLine(
            "Committed: " +
            stage.CommittedCount
        );
        builder.AppendLine(
            "Skipped: " +
            stage.SkippedCount
        );
        builder.AppendLine(
            "Unchanged: " +
            stage.UnchangedCount
        );
        builder.AppendLine(
            "Failed: " +
            stage.FailedCount
        );

        return
            builder.ToString();
    }

    private static string BuildPerformanceSelectionText(
        TerrainRuntimeBakePerformanceRecord record
    )
    {
        if (record == null)
        {
            return "";
        }

        return
            record.Name +
            "\nStage: " +
            record.Stage +
            "\nCategory: " +
            record.Category +
            "\nStart: +" +
            record.StartedAtSeconds.ToString("0.000") +
            " s" +
            "\nDuration: " +
            (record.DurationSeconds * 1000d).ToString("0.000") +
            " ms";
    }

    private static string BuildTraceSelectionText(
        TerrainRuntimeBakeTraceRecord record
    )
    {
        if (record == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            record.Name
        );
        builder.AppendLine(
            "Stage: " +
            record.Stage
        );
        builder.AppendLine(
            "Outcome: " +
            record.Outcome
        );
        builder.AppendLine(
            "Scope ID: " +
            record.Id
        );
        builder.AppendLine(
            "Parent ID: " +
            record.ParentId
        );
        builder.AppendLine(
            "Start: +" +
            record.StartedAtSeconds.ToString("0.000") +
            " s / " +
            record.StartedAtUtc.ToString("u")
        );
        builder.AppendLine(
            "End: +" +
            record.EndedAtSeconds.ToString("0.000") +
            " s / " +
            record.EndedAtUtc.ToString("u")
        );
        builder.AppendLine(
            "Duration: " +
            (record.DurationSeconds * 1000d).ToString("0.000") +
            " ms"
        );

        if (
            !string.IsNullOrEmpty(
                record.Message
            )
        )
        {
            builder.AppendLine(
                "Message: " +
                record.Message
            );
        }

        return
            builder.ToString();
    }

    private static string BuildEventSelectionText(
        ViewerEvent viewerEvent
    )
    {
        if (viewerEvent == null)
        {
            return "";
        }

        return
            "Severity: " +
            viewerEvent.severity +
            "\nStage: " +
            (
                viewerEvent.stage.HasValue
                    ? viewerEvent.stage.Value.ToString()
                    : "Run"
            ) +
            "\nCategory: " +
            viewerEvent.category +
            (
                viewerEvent.durationSeconds > 0d
                    ? "\nDuration: " +
                      (viewerEvent.durationSeconds * 1000d)
                          .ToString("0.000") +
                      " ms"
                    : ""
            ) +
            "\n" +
            viewerEvent.detail;
    }

    private void DrawCoordinateList(
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        EditorGUILayout.LabelField(
            label,
            EditorStyles.boldLabel
        );

        if (
            coordinates == null
            ||
            coordinates.Count == 0
        )
        {
            EditorGUILayout.LabelField(
                "None"
            );
            return;
        }

        if (
            TryParseCoordinateFilter(
                out Vector2Int coordinate
            )
        )
        {
            if (
                ContainsCoordinate(
                    coordinates,
                    coordinate
                )
            )
            {
                EditorGUILayout.LabelField(
                    "(" +
                    coordinate.x +
                    ", " +
                    coordinate.y +
                    ")"
                );
            }
            else
            {
                EditorGUILayout.LabelField(
                    "No matching coordinate."
                );
            }

            return;
        }

        StringBuilder builder =
            new StringBuilder();

        int count =
            Mathf.Min(
                MaxCoordinateDisplayCount,
                coordinates.Count
            );

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(
                    ", "
                );
            }

            Vector2Int value =
                coordinates[index];

            builder.Append(
                "(" +
                value.x +
                ", " +
                value.y +
                ")"
            );
        }

        if (coordinates.Count > count)
        {
            builder.Append(
                ", ... +" +
                (
                    coordinates.Count -
                    count
                ) +
                " more"
            );
        }

        EditorGUILayout.SelectableLabel(
            builder.ToString(),
            GUILayout.MinHeight(36f)
        );
    }

    private static void DrawSectionHeading(
        string text
    )
    {
        EditorGUILayout.Space(
            6f
        );
        EditorGUILayout.LabelField(
            text,
            EditorStyles.boldLabel
        );
    }

    private static void DrawKeyValue(
        string key,
        string value
    )
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                key,
                GUILayout.Width(210f)
            );

            EditorGUILayout.SelectableLabel(
                value ?? "",
                GUILayout.Height(
                    EditorGUIUtility.singleLineHeight
                )
            );
        }
    }

    private void DrawSelectableRow(
        string visibleText,
        string selectionLabel,
        string selectionText,
        GUIStyle style = null
    )
    {
        GUIStyle effectiveStyle =
            style ??
            EditorStyles.label;

        if (
            GUILayout.Button(
                visibleText ?? "",
                effectiveStyle
            )
        )
        {
            SetSelection(
                selectionLabel,
                selectionText
            );
        }
    }

    private void SetSelection(
        string label,
        string text
    )
    {
        selectedInformationLabel =
            label ?? "";

        selectedInformation =
            text ?? "";
    }

    private void ClearSelection()
    {
        selectedInformationLabel =
            "";

        selectedInformation =
            "";

        selectedTraceRecord =
            null;

        if (traceTree != null)
        {
            traceTree.SetSelection(
                new List<int>()
            );
        }
    }

    private static void DrawMissingDiagnostics(
        string message
    )
    {
        EditorGUILayout.HelpBox(
            message,
            MessageType.Info
        );
    }

    private void ExportText()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        string path =
            EditorUtility.SaveFilePanel(
                "Export Runtime Bake Text Report",
                "",
                BuildDefaultFileName(
                    result,
                    "txt"
                ),
                "txt"
            );

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        System.IO.File.WriteAllText(
            path,
            TerrainRuntimeBakeReportFormatter.BuildTextReport(
                result,
                TerrainRuntimeBakeDiagnosticsLevel.Trace
            ),
            Encoding.UTF8
        );
    }

    private void ExportJson()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        string path =
            EditorUtility.SaveFilePanel(
                "Export Runtime Bake JSON Report",
                "",
                BuildDefaultFileName(
                    result,
                    "json"
                ),
                "json"
            );

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        System.IO.File.WriteAllText(
            path,
            TerrainRuntimeBakeReportJsonExporter.BuildJson(
                result
            ),
            Encoding.UTF8
        );
    }

    private static string BuildDefaultFileName(
        TerrainRuntimeBakePipelineResult result,
        string extension
    )
    {
        string runId =
            result != null
            &&
            result.Diagnostics != null
            &&
            !string.IsNullOrEmpty(
                result.Diagnostics.RunId
            )
                ? result.Diagnostics.RunId
                : DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss"
                );

        return
            "WorldMeshes_RuntimeBake_" +
            runId +
            "." +
            extension;
    }

    private static string FormatBytes(
        long bytes
    )
    {
        double value =
            Math.Max(
                0L,
                bytes
            );

        if (
            value >=
            1024d * 1024d * 1024d
        )
        {
            return
                (
                    value /
                    (
                        1024d *
                        1024d *
                        1024d
                    )
                )
                .ToString("0.00") +
                " GB";
        }

        if (
            value >=
            1024d * 1024d
        )
        {
            return
                (
                    value /
                    (
                        1024d *
                        1024d
                    )
                )
                .ToString("0.00") +
                " MB";
        }

        if (value >= 1024d)
        {
            return
                (
                    value /
                    1024d
                )
                .ToString("0.00") +
                " KB";
        }

        return
            bytes +
            " B";
    }
}
