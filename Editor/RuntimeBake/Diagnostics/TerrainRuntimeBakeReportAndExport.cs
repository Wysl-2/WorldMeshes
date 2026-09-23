using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeBakeReportFormatter
{
    private const int MaxSummaryCoordinates = 24;
    private const int MaxDetailedCoordinates = 256;

    public static string BuildTextReport(
        TerrainRuntimeBakePipelineResult result,
        TerrainRuntimeBakeDiagnosticsLevel detailLevel
    )
    {
        StringBuilder builder =
            new StringBuilder();

        if (result == null)
        {
            builder.AppendLine(
                "WorldMeshes Runtime Bake Report"
            );
            builder.AppendLine(
                "No runtime bake result is available."
            );

            return builder.ToString();
        }

        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            result.Diagnostics;

        TerrainRuntimeBakeDiagnosticsLevel effectiveLevel =
            detailLevel == TerrainRuntimeBakeDiagnosticsLevel.Off
                ? TerrainRuntimeBakeDiagnosticsLevel.Summary
                : detailLevel;

        AppendHeader(
            builder,
            result,
            diagnostics
        );

        AppendStageDurations(
            builder,
            result
        );

        AppendStageStatus(
            builder,
            diagnostics
        );

        AppendExecutionSummary(
            builder,
            diagnostics,
            effectiveLevel
        );

        AppendPerformanceSummary(
            builder,
            diagnostics,
            effectiveLevel
        );

        AppendWarningsAndErrors(
            builder,
            result,
            diagnostics
        );

        if (
            effectiveLevel == TerrainRuntimeBakeDiagnosticsLevel.Detailed
            ||
            effectiveLevel == TerrainRuntimeBakeDiagnosticsLevel.Trace
        )
        {
            AppendPlanningDetails(
                builder,
                diagnostics
            );

            AppendExecutionDetails(
                builder,
                diagnostics
            );

            AppendMemoryDetails(
                builder,
                diagnostics,
                effectiveLevel
            );
        }

        if (effectiveLevel == TerrainRuntimeBakeDiagnosticsLevel.Trace)
        {
            AppendTraceDetails(
                builder,
                diagnostics
            );
        }

        if (!string.IsNullOrEmpty(result.SummaryMessage))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Summary:"
            );
            builder.AppendLine(
                result.SummaryMessage
            );
        }

        return builder.ToString();
    }

    private static void AppendHeader(
        StringBuilder builder,
        TerrainRuntimeBakePipelineResult result,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        builder.AppendLine(
            "WorldMeshes Runtime Bake Report"
        );
        builder.AppendLine(
            "=================================="
        );
        builder.AppendLine(
            "Outcome: " +
            result.Outcome
        );
        builder.AppendLine(
            "Mode: " +
            result.Mode
        );
        builder.AppendLine(
            "Final State: " +
            result.FinalState
        );
        builder.AppendLine(
            "Last Stage: " +
            result.LastStage
        );

        if (
            result.FailedStage !=
            TerrainRuntimeBakePipelineState.Idle
        )
        {
            builder.AppendLine(
                "Failed Stage: " +
                result.FailedStage
            );
        }

        builder.AppendLine(
            "Started UTC: " +
            result.StartedAtUtc.ToString("u")
        );
        builder.AppendLine(
            "Total Duration: " +
            result.DurationSeconds.ToString("0.000") +
            " s"
        );

        if (diagnostics != null)
        {
            builder.AppendLine(
                "Diagnostics Run ID: " +
                diagnostics.RunId
            );
            builder.AppendLine(
                "Diagnostics Level Captured: " +
                diagnostics.Level
            );
            builder.AppendLine(
                "Diagnostics Started UTC: " +
                diagnostics.StartedAtUtc.ToString("u")
            );
            builder.AppendLine(
                "Diagnostics Ended UTC: " +
                diagnostics.EndedAtUtc.ToString("u")
            );
        }
        else
        {
            builder.AppendLine(
                "Diagnostics: Not captured"
            );
        }
    }

    private static void AppendStageDurations(
        StringBuilder builder,
        TerrainRuntimeBakePipelineResult result
    )
    {
        builder.AppendLine();
        builder.AppendLine(
            "Stage Durations:"
        );
        AppendDuration(
            builder,
            "Heightmaps",
            result.HeightDurationSeconds
        );
        AppendDuration(
            builder,
            "Height Streaming",
            result.HeightStreamingDurationSeconds
        );
        AppendDuration(
            builder,
            "Surface Masks",
            result.SurfaceDurationSeconds
        );
        AppendDuration(
            builder,
            "Collision",
            result.CollisionDurationSeconds
        );
        AppendDuration(
            builder,
            "Addressables",
            result.AddressablesDurationSeconds
        );
        AppendDuration(
            builder,
            "Scene Sync",
            result.SceneSyncDurationSeconds
        );
        AppendDuration(
            builder,
            "Total",
            result.DurationSeconds
        );
    }

    private static void AppendDuration(
        StringBuilder builder,
        string label,
        double seconds
    )
    {
        builder.Append("  ");
        builder.Append(label);
        builder.Append(": ");
        builder.Append(seconds.ToString("0.000"));
        builder.AppendLine(" s");
    }

    private static void AppendStageStatus(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (
            diagnostics == null
            ||
            diagnostics.StageRecords == null
            ||
            diagnostics.StageRecords.Count == 0
        )
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Stage Status:"
        );

        for (int index = 0; index < diagnostics.StageRecords.Count; index++)
        {
            TerrainRuntimeBakeStageDiagnostic stage =
                diagnostics.StageRecords[index];

            builder.Append("  ");
            builder.Append(stage.Stage);
            builder.Append(": ");
            builder.Append(stage.Status);

            if (stage.EnteredAtUtc.HasValue)
            {
                builder.Append(" at +");
                builder.Append(stage.EnteredAtSeconds.ToString("0.000"));
                builder.Append(" s");
            }

            builder.AppendLine();
        }
    }

    private static void AppendExecutionSummary(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics,
        TerrainRuntimeBakeDiagnosticsLevel detailLevel
    )
    {
        if (
            diagnostics == null
            ||
            diagnostics.ExecutionStages == null
            ||
            diagnostics.ExecutionStages.Count == 0
        )
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Execution Summary:"
        );
        builder.AppendLine(
            "  Stage           Planned  Requested  Evaluated  Generated  Committed  Skipped  Unchanged  Failed"
        );

        for (int index = 0; index < diagnostics.ExecutionStages.Count; index++)
        {
            TerrainRuntimeBakeStageExecutionDiagnostic stage =
                diagnostics.ExecutionStages[index];

            builder.Append("  ");
            builder.Append(PadRight(stage.Stage.ToString(), 15));
            builder.Append(PadLeft(stage.PlannedCount.ToString(), 7));
            builder.Append(PadLeft(stage.RequestedCount.ToString(), 11));
            builder.Append(PadLeft(stage.EvaluatedCount.ToString(), 11));
            builder.Append(PadLeft(stage.GeneratedCount.ToString(), 11));
            builder.Append(PadLeft(stage.CommittedCount.ToString(), 11));
            builder.Append(PadLeft(stage.SkippedCount.ToString(), 9));
            builder.Append(PadLeft(stage.UnchangedCount.ToString(), 11));
            builder.Append(PadLeft(stage.FailedCount.ToString(), 8));
            builder.AppendLine();
        }
    }

    private static void AppendPerformanceSummary(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics,
        TerrainRuntimeBakeDiagnosticsLevel detailLevel
    )
    {
        if (diagnostics == null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Performance Summary:"
        );
        builder.AppendLine(
            "  Peak Tracked Temporary Memory: " +
            FormatBytes(
                diagnostics.PeakTrackedTemporaryBytes
            )
        );
        builder.AppendLine(
            "  Current Tracked Temporary Memory: " +
            FormatBytes(
                diagnostics.CurrentTrackedTemporaryBytes
            )
        );
        builder.AppendLine(
            "  Note: tracked WorldMeshes temporary memory only; not total Unity/process/system memory."
        );

        if (
            diagnostics.PerformanceRecords == null
            ||
            diagnostics.PerformanceRecords.Count == 0
        )
        {
            builder.AppendLine(
                "  Operation timings: None captured"
            );
            return;
        }

        builder.AppendLine(
            "  Slowest Operations:"
        );

        List<TerrainRuntimeBakePerformanceRecord> records =
            new List<TerrainRuntimeBakePerformanceRecord>(
                diagnostics.PerformanceRecords
            );

        records.Sort(
            (left, right) =>
                right.DurationSeconds.CompareTo(
                    left.DurationSeconds
                )
        );

        int count =
            Mathf.Min(
                detailLevel == TerrainRuntimeBakeDiagnosticsLevel.Summary
                    ? 8
                    : 32,
                records.Count
            );

        for (int index = 0; index < count; index++)
        {
            TerrainRuntimeBakePerformanceRecord record =
                records[index];

            builder.Append("    ");
            builder.Append(record.Stage);
            builder.Append(" / ");
            builder.Append(record.Category);
            builder.Append(" / ");
            builder.Append(record.Name);
            builder.Append(": ");
            builder.Append(record.DurationSeconds.ToString("0.000"));
            builder.AppendLine(" s");
        }
    }

    private static void AppendWarningsAndErrors(
        StringBuilder builder,
        TerrainRuntimeBakePipelineResult result,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        builder.AppendLine();
        builder.AppendLine(
            "Warnings / Errors:"
        );

        int warningCount =
            result.WarningMessages != null
                ? result.WarningMessages.Count
                : 0;

        builder.AppendLine(
            "  Warnings: " +
            warningCount
        );

        if (warningCount > 0)
        {
            for (int index = 0; index < warningCount; index++)
            {
                builder.AppendLine(
                    "  - " +
                    result.WarningMessages[index]
                );
            }
        }

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            builder.AppendLine(
                "  Error: " +
                result.ErrorMessage
            );
        }
        else
        {
            builder.AppendLine(
                "  Error: None"
            );
        }
    }

    private static void AppendPlanningDetails(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (diagnostics == null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Planning Details:"
        );

        if (
            diagnostics.RunReasons != null
            &&
            diagnostics.RunReasons.Count > 0
        )
        {
            builder.AppendLine(
                "  Run Reasons:"
            );

            AppendReasons(
                builder,
                diagnostics.RunReasons,
                "    "
            );
        }

        if (
            diagnostics.PlanSnapshots == null
            ||
            diagnostics.PlanSnapshots.Count == 0
        )
        {
            builder.AppendLine(
                "  No plan snapshots captured."
            );
            return;
        }

        for (int index = 0; index < diagnostics.PlanSnapshots.Count; index++)
        {
            TerrainRuntimeBakePlanDiagnosticSnapshot snapshot =
                diagnostics.PlanSnapshots[index];

            builder.AppendLine();
            builder.AppendLine(
                "  " +
                snapshot.Kind +
                " Plan:"
            );

            if (!snapshot.IsAvailable)
            {
                builder.AppendLine(
                    "    Unavailable"
                );
                continue;
            }

            builder.AppendLine(
                "    Height: " +
                snapshot.HeightWorkMode +
                " (" +
                snapshot.HeightTileCount +
                " tiles)"
            );
            builder.AppendLine(
                "    Surface: " +
                snapshot.SurfaceWorkMode +
                " (" +
                snapshot.SurfaceTileCount +
                " tiles)"
            );
            builder.AppendLine(
                "    Collision: " +
                snapshot.CollisionWorkMode +
                " (" +
                snapshot.CollisionChunkCount +
                " chunks)"
            );
            builder.AppendLine(
                "    Addressables Configuration: " +
                snapshot.AddressablesConfigurationRequired
            );
            builder.AppendLine(
                "    Addressables Content Build: " +
                snapshot.AddressablesContentBuildRequired
            );
            builder.AppendLine(
                "    Runtime Scene Metadata: " +
                snapshot.RuntimeSceneMetadataUpdateRequired
            );
            builder.AppendLine(
                "    Initial Bake: " +
                snapshot.IsInitialBake
            );
            builder.AppendLine(
                "    Has Work: " +
                snapshot.HasWork
            );
            builder.AppendLine(
                "    Source State Revision: " +
                snapshot.SourceStateRevision
            );

            if (snapshot.IsBlocked)
            {
                builder.AppendLine(
                    "    Blocked: " +
                    snapshot.BlockReason
                );
            }

            AppendCoordinates(
                builder,
                "    Height Tiles",
                snapshot.HeightTiles,
                snapshot.CoordinatesCaptured,
                MaxDetailedCoordinates
            );
            AppendCoordinates(
                builder,
                "    Surface Tiles",
                snapshot.SurfaceTiles,
                snapshot.CoordinatesCaptured,
                MaxDetailedCoordinates
            );
            AppendCoordinates(
                builder,
                "    Collision Chunks",
                snapshot.CollisionChunks,
                snapshot.CoordinatesCaptured,
                MaxDetailedCoordinates
            );

            if (
                snapshot.SafetyEscalationReasons != null
                &&
                snapshot.SafetyEscalationReasons.Count > 0
            )
            {
                builder.AppendLine(
                    "    Safety Escalations:"
                );

                for (
                    int reasonIndex = 0;
                    reasonIndex < snapshot.SafetyEscalationReasons.Count;
                    reasonIndex++
                )
                {
                    builder.AppendLine(
                        "      - " +
                        snapshot.SafetyEscalationReasons[reasonIndex]
                    );
                }
            }

            if (
                snapshot.Reasons != null
                &&
                snapshot.Reasons.Count > 0
            )
            {
                builder.AppendLine(
                    "    Reasons:"
                );

                AppendReasons(
                    builder,
                    snapshot.Reasons,
                    "      "
                );
            }
        }
    }

    private static void AppendExecutionDetails(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (
            diagnostics == null
            ||
            diagnostics.ExecutionStages == null
        )
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Execution Details:"
        );

        for (int index = 0; index < diagnostics.ExecutionStages.Count; index++)
        {
            TerrainRuntimeBakeStageExecutionDiagnostic stage =
                diagnostics.ExecutionStages[index];

            builder.AppendLine(
                "  " +
                stage.Stage +
                ":"
            );

            AppendCoordinates(
                builder,
                "    Requested",
                stage.RequestedCoordinates,
                true,
                MaxDetailedCoordinates
            );
            AppendCoordinates(
                builder,
                "    Succeeded",
                stage.SucceededCoordinates,
                true,
                MaxDetailedCoordinates
            );
            AppendCoordinates(
                builder,
                "    Failed",
                stage.FailedCoordinates,
                true,
                MaxDetailedCoordinates
            );
            AppendCoordinates(
                builder,
                "    Unprocessed",
                stage.UnprocessedCoordinates,
                true,
                MaxDetailedCoordinates
            );

            if (stage.Stage == TerrainRuntimeBakePipelineState.Addressables)
            {
                builder.AppendLine(
                    "    Configuration requested/performed: " +
                    stage.AddressablesConfigurationRequested +
                    " / " +
                    stage.AddressablesConfigurationPerformed
                );
                builder.AppendLine(
                    "    Content build requested/performed: " +
                    stage.AddressablesContentBuildRequested +
                    " / " +
                    stage.AddressablesContentBuildPerformed
                );
                builder.AppendLine(
                    "    Marker work requested/performed: " +
                    stage.AddressablesMarkerWorkRequested +
                    " / " +
                    stage.AddressablesMarkerWorkPerformed
                );
            }
            else if (stage.Stage == TerrainRuntimeBakePipelineState.SceneSync)
            {
                builder.AppendLine(
                    "    Metadata requested/performed: " +
                    stage.SceneMetadataRequested +
                    " / " +
                    stage.SceneMetadataPerformed
                );
                builder.AppendLine(
                    "    Scene modification performed: " +
                    stage.SceneModificationPerformed
                );
                builder.AppendLine(
                    "    Serialized component changes: " +
                    stage.SceneSerializedComponentChangeCount
                );
            }
        }
    }

    private static void AppendMemoryDetails(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics,
        TerrainRuntimeBakeDiagnosticsLevel detailLevel
    )
    {
        if (diagnostics == null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Memory Details:"
        );

        if (
            diagnostics.TrackedMemorySummaries != null
            &&
            diagnostics.TrackedMemorySummaries.Count > 0
        )
        {
            for (
                int index = 0;
                index < diagnostics.TrackedMemorySummaries.Count;
                index++
            )
            {
                TerrainRuntimeBakeTrackedMemorySummary summary =
                    diagnostics.TrackedMemorySummaries[index];

                builder.AppendLine(
                    "  " +
                    summary.Category +
                    ": current " +
                    FormatBytes(summary.CurrentBytes) +
                    ", peak " +
                    FormatBytes(summary.PeakBytes)
                );
            }
        }
        else
        {
            builder.AppendLine(
                "  No tracked memory categories captured."
            );
        }

        if (
            detailLevel == TerrainRuntimeBakeDiagnosticsLevel.Trace
            &&
            diagnostics.MemoryEvents != null
            &&
            diagnostics.MemoryEvents.Count > 0
        )
        {
            builder.AppendLine();
            builder.AppendLine(
                "  Memory Events:"
            );

            for (int index = 0; index < diagnostics.MemoryEvents.Count; index++)
            {
                TerrainRuntimeBakeMemoryEvent memoryEvent =
                    diagnostics.MemoryEvents[index];

                builder.Append("    +");
                builder.Append(memoryEvent.AtSeconds.ToString("0.000"));
                builder.Append(" s ");
                builder.Append(memoryEvent.Stage);
                builder.Append(" / ");
                builder.Append(memoryEvent.Category);
                builder.Append(" / ");
                builder.Append(memoryEvent.Name);
                builder.Append(" delta ");
                builder.Append(FormatSignedBytes(memoryEvent.ByteDelta));
                builder.Append(", current ");
                builder.AppendLine(
                    FormatBytes(memoryEvent.CurrentTrackedBytes)
                );
            }
        }
    }

    private static void AppendTraceDetails(
        StringBuilder builder,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (
            diagnostics == null
            ||
            diagnostics.TraceRecords == null
            ||
            diagnostics.TraceRecords.Count == 0
        )
        {
            builder.AppendLine();
            builder.AppendLine(
                "Trace:"
            );
            builder.AppendLine(
                "  No Trace records captured."
            );

            return;
        }

        builder.AppendLine();
        builder.AppendLine(
            "Trace:"
        );

        List<TerrainRuntimeBakeTraceRecord> records =
            new List<TerrainRuntimeBakeTraceRecord>(
                diagnostics.TraceRecords
            );

        records.Sort(
            (left, right) =>
                left.StartedAtSeconds.CompareTo(
                    right.StartedAtSeconds
                )
        );

        AppendTraceChildren(
            builder,
            records,
            0,
            "  "
        );
    }

    private static void AppendTraceChildren(
        StringBuilder builder,
        IReadOnlyList<TerrainRuntimeBakeTraceRecord> records,
        long parentId,
        string indent
    )
    {
        for (int index = 0; index < records.Count; index++)
        {
            TerrainRuntimeBakeTraceRecord record =
                records[index];

            if (record.ParentId != parentId)
            {
                continue;
            }

            builder.Append(indent);
            builder.Append("+");
            builder.Append(record.StartedAtSeconds.ToString("0.000"));
            builder.Append("s ");
            builder.Append(record.Name);
            builder.Append(" [");
            builder.Append(record.Stage);
            builder.Append(", ");
            builder.Append(record.DurationSeconds.ToString("0.000"));
            builder.Append("s, ");
            builder.Append(record.Outcome);
            builder.AppendLine("]");

            if (!string.IsNullOrEmpty(record.Message))
            {
                builder.Append(indent);
                builder.Append("  ");
                builder.AppendLine(record.Message);
            }

            AppendTraceChildren(
                builder,
                records,
                record.Id,
                indent + "  "
            );
        }
    }

    private static void AppendReasons(
        StringBuilder builder,
        IReadOnlyList<TerrainRuntimeBakeReasonRecord> reasons,
        string indent
    )
    {
        for (int index = 0; index < reasons.Count; index++)
        {
            TerrainRuntimeBakeReasonRecord reason =
                reasons[index];

            builder.Append(indent);
            builder.Append("- ");
            builder.Append(reason.Origin);
            builder.Append(" / ");
            builder.Append(reason.Target);
            builder.Append(" / ");
            builder.Append(reason.Code);

            if (reason.SourceTarget.HasValue)
            {
                builder.Append(" <- ");
                builder.Append(reason.SourceTarget.Value);
            }

            if (reason.AffectedItemCount > 0)
            {
                builder.Append(" (");
                builder.Append(reason.AffectedItemCount);
                builder.Append(" items)");
            }

            if (reason.IsSafetyEscalation)
            {
                builder.Append(" [Safety]");
            }

            if (!string.IsNullOrEmpty(reason.Message))
            {
                builder.Append(": ");
                builder.Append(reason.Message);
            }

            builder.AppendLine();
        }
    }

    private static void AppendCoordinates(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates,
        bool available,
        int limit
    )
    {
        builder.Append(label);
        builder.Append(": ");

        if (!available)
        {
            builder.AppendLine(
                "Not captured at this diagnostics level"
            );
            return;
        }

        if (
            coordinates == null
            ||
            coordinates.Count == 0
        )
        {
            builder.AppendLine(
                "None"
            );
            return;
        }

        int count =
            Mathf.Min(
                Mathf.Max(0, limit),
                coordinates.Count
            );

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            Vector2Int coordinate =
                coordinates[index];

            builder.Append("(");
            builder.Append(coordinate.x);
            builder.Append(", ");
            builder.Append(coordinate.y);
            builder.Append(")");
        }

        if (coordinates.Count > count)
        {
            builder.Append(", ... +");
            builder.Append(coordinates.Count - count);
            builder.Append(" more");
        }

        builder.AppendLine();
    }

    private static string PadRight(
        string value,
        int width
    )
    {
        value = value ?? "";
        return value.Length >= width
            ? value
            : value + new string(' ', width - value.Length);
    }

    private static string PadLeft(
        string value,
        int width
    )
    {
        value = value ?? "";
        return value.Length >= width
            ? value
            : new string(' ', width - value.Length) + value;
    }

    internal static string FormatBytes(
        long bytes
    )
    {
        double value =
            Math.Max(0L, bytes);

        if (value >= 1024d * 1024d * 1024d)
        {
            return
                (value / (1024d * 1024d * 1024d)).ToString("0.00") +
                " GB";
        }

        if (value >= 1024d * 1024d)
        {
            return
                (value / (1024d * 1024d)).ToString("0.00") +
                " MB";
        }

        if (value >= 1024d)
        {
            return
                (value / 1024d).ToString("0.00") +
                " KB";
        }

        return bytes + " B";
    }

    private static string FormatSignedBytes(
        long bytes
    )
    {
        if (bytes >= 0L)
        {
            return "+" + FormatBytes(bytes);
        }

        return "-" + FormatBytes(-bytes);
    }
}

public static class TerrainRuntimeBakeReportJsonExporter
{
    [Serializable]
    private sealed class ReportDto
    {
        public int schemaVersion = 1;
        public string schemaName = "WorldMeshes.RuntimeBakeDiagnostics";
        public string runId;
        public string mode;
        public string outcome;
        public string finalState;
        public string lastStage;
        public string failedStage;
        public string startedAtUtc;
        public double durationSeconds;
        public int warningCount;
        public string errorMessage;
        public string summaryMessage;

        public List<StageTimingDto> stageTimings =
            new List<StageTimingDto>();

        public List<StageStatusDto> stageStatuses =
            new List<StageStatusDto>();

        public List<PlanSnapshotDto> planSnapshots =
            new List<PlanSnapshotDto>();

        public List<ReasonDto> runReasons =
            new List<ReasonDto>();

        public List<ExecutionStageDto> executionStages =
            new List<ExecutionStageDto>();

        public List<PerformanceRecordDto> performanceRecords =
            new List<PerformanceRecordDto>();

        public long currentTrackedTemporaryBytes;
        public long peakTrackedTemporaryBytes;

        public List<TrackedMemoryDto> trackedMemory =
            new List<TrackedMemoryDto>();

        public List<MemoryEventDto> memoryEvents =
            new List<MemoryEventDto>();

        public List<TraceRecordDto> traceRecords =
            new List<TraceRecordDto>();

        public List<string> warnings =
            new List<string>();
    }

    [Serializable]
    private sealed class StageTimingDto
    {
        public string stage;
        public double durationSeconds;
    }

    [Serializable]
    private sealed class StageStatusDto
    {
        public string stage;
        public string status;
        public double enteredAtSeconds;
        public string enteredAtUtc;
    }

    [Serializable]
    private sealed class PlanSnapshotDto
    {
        public string kind;
        public bool isAvailable;
        public bool coordinatesCaptured;
        public string heightWorkMode;
        public string surfaceWorkMode;
        public string collisionWorkMode;
        public int heightTileCount;
        public int surfaceTileCount;
        public int collisionChunkCount;
        public bool addressablesConfigurationRequired;
        public bool addressablesContentBuildRequired;
        public bool runtimeSceneMetadataUpdateRequired;
        public bool isInitialBake;
        public bool isBlocked;
        public bool hasWork;
        public string blockReason;
        public long sourceStateRevision;
        public string currentAuthoringSignature;
        public string observedAuthoringSignature;
        public string currentSurfaceSettingsSignature;
        public string currentCollisionSettingsSignature;
        public List<CoordinateDto> heightTiles =
            new List<CoordinateDto>();
        public List<CoordinateDto> surfaceTiles =
            new List<CoordinateDto>();
        public List<CoordinateDto> collisionChunks =
            new List<CoordinateDto>();
        public List<string> safetyEscalationReasons =
            new List<string>();
        public List<ReasonDto> reasons =
            new List<ReasonDto>();
    }

    [Serializable]
    private sealed class ReasonDto
    {
        public string origin;
        public string code;
        public string target;
        public string sourceTarget;
        public string message;
        public bool isSafetyEscalation;
        public int affectedItemCount;
    }

    [Serializable]
    private sealed class ExecutionStageDto
    {
        public string stage;
        public int plannedCount;
        public int requestedCount;
        public int evaluatedCount;
        public int generatedCount;
        public int committedCount;
        public int skippedCount;
        public int unchangedCount;
        public int failedCount;
        public List<CoordinateDto> requestedCoordinates =
            new List<CoordinateDto>();
        public List<CoordinateDto> succeededCoordinates =
            new List<CoordinateDto>();
        public List<CoordinateDto> failedCoordinates =
            new List<CoordinateDto>();
        public List<CoordinateDto> unprocessedCoordinates =
            new List<CoordinateDto>();
        public bool addressablesConfigurationRequested;
        public bool addressablesConfigurationPerformed;
        public bool addressablesContentBuildRequested;
        public bool addressablesContentBuildPerformed;
        public bool addressablesMarkerWorkRequested;
        public bool addressablesMarkerWorkPerformed;
        public int addressablesMarkersRegenerated;
        public int addressablesMarkersReused;
        public int addressablesMarkersRemoved;
        public bool sceneMetadataRequested;
        public bool sceneMetadataPerformed;
        public bool sceneModificationPerformed;
        public int sceneSerializedComponentChangeCount;
    }

    [Serializable]
    private sealed class PerformanceRecordDto
    {
        public string name;
        public string stage;
        public string category;
        public double startedAtSeconds;
        public double durationSeconds;
    }

    [Serializable]
    private sealed class TrackedMemoryDto
    {
        public string category;
        public long currentBytes;
        public long peakBytes;
    }

    [Serializable]
    private sealed class MemoryEventDto
    {
        public string name;
        public string stage;
        public string category;
        public long byteDelta;
        public long currentTrackedBytes;
        public double atSeconds;
    }

    [Serializable]
    private sealed class TraceRecordDto
    {
        public long id;
        public long parentId;
        public string name;
        public string stage;
        public string outcome;
        public string message;
        public string startedAtUtc;
        public string endedAtUtc;
        public double startedAtSeconds;
        public double endedAtSeconds;
        public double durationSeconds;
    }

    [Serializable]
    private sealed class CoordinateDto
    {
        public int x;
        public int y;

        public CoordinateDto(
            Vector2Int coordinate
        )
        {
            x = coordinate.x;
            y = coordinate.y;
        }
    }

    public static string BuildJson(
        TerrainRuntimeBakePipelineResult result
    )
    {
        ReportDto dto =
            CreateDto(
                result
            );

        return
            JsonUtility.ToJson(
                dto,
                true
            );
    }

    private static ReportDto CreateDto(
        TerrainRuntimeBakePipelineResult result
    )
    {
        ReportDto dto =
            new ReportDto();

        if (result == null)
        {
            dto.outcome = "NoResult";
            dto.summaryMessage = "No runtime bake result is available.";
            return dto;
        }

        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics =
            result.Diagnostics;

        dto.runId =
            diagnostics != null
                ? diagnostics.RunId
                : "";

        dto.mode =
            result.Mode.ToString();

        dto.outcome =
            result.Outcome.ToString();

        dto.finalState =
            result.FinalState.ToString();

        dto.lastStage =
            result.LastStage.ToString();

        dto.failedStage =
            result.FailedStage.ToString();

        dto.startedAtUtc =
            result.StartedAtUtc.ToString("u");

        dto.durationSeconds =
            result.DurationSeconds;

        dto.warningCount =
            result.WarningMessages != null
                ? result.WarningMessages.Count
                : 0;

        dto.errorMessage =
            result.ErrorMessage ?? "";

        dto.summaryMessage =
            result.SummaryMessage ?? "";

        AddStageTiming(
            dto,
            "Heightmaps",
            result.HeightDurationSeconds
        );
        AddStageTiming(
            dto,
            "HeightStreaming",
            result.HeightStreamingDurationSeconds
        );
        AddStageTiming(
            dto,
            "SurfaceMasks",
            result.SurfaceDurationSeconds
        );
        AddStageTiming(
            dto,
            "Collision",
            result.CollisionDurationSeconds
        );
        AddStageTiming(
            dto,
            "Addressables",
            result.AddressablesDurationSeconds
        );
        AddStageTiming(
            dto,
            "SceneSync",
            result.SceneSyncDurationSeconds
        );
        AddStageTiming(
            dto,
            "Total",
            result.DurationSeconds
        );

        if (result.WarningMessages != null)
        {
            for (int index = 0; index < result.WarningMessages.Count; index++)
            {
                dto.warnings.Add(
                    result.WarningMessages[index] ?? ""
                );
            }
        }

        if (diagnostics == null)
        {
            return dto;
        }

        dto.currentTrackedTemporaryBytes =
            diagnostics.CurrentTrackedTemporaryBytes;

        dto.peakTrackedTemporaryBytes =
            diagnostics.PeakTrackedTemporaryBytes;

        AddStageStatuses(
            dto,
            diagnostics
        );
        AddPlanning(
            dto,
            diagnostics
        );
        AddExecution(
            dto,
            diagnostics
        );
        AddPerformance(
            dto,
            diagnostics
        );
        AddTrace(
            dto,
            diagnostics
        );

        return dto;
    }

    private static void AddStageTiming(
        ReportDto dto,
        string stage,
        double duration
    )
    {
        dto.stageTimings.Add(
            new StageTimingDto
            {
                stage = stage,
                durationSeconds = Math.Max(0d, duration)
            }
        );
    }

    private static void AddStageStatuses(
        ReportDto dto,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (diagnostics.StageRecords == null)
        {
            return;
        }

        for (int index = 0; index < diagnostics.StageRecords.Count; index++)
        {
            TerrainRuntimeBakeStageDiagnostic stage =
                diagnostics.StageRecords[index];

            dto.stageStatuses.Add(
                new StageStatusDto
                {
                    stage = stage.Stage.ToString(),
                    status = stage.Status.ToString(),
                    enteredAtSeconds = stage.EnteredAtSeconds,
                    enteredAtUtc = stage.EnteredAtUtc.HasValue
                        ? stage.EnteredAtUtc.Value.ToString("u")
                        : ""
                }
            );
        }
    }

    private static void AddPlanning(
        ReportDto dto,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (diagnostics.RunReasons != null)
        {
            AddReasons(
                dto.runReasons,
                diagnostics.RunReasons
            );
        }

        if (diagnostics.PlanSnapshots == null)
        {
            return;
        }

        for (int index = 0; index < diagnostics.PlanSnapshots.Count; index++)
        {
            TerrainRuntimeBakePlanDiagnosticSnapshot source =
                diagnostics.PlanSnapshots[index];

            PlanSnapshotDto target =
                new PlanSnapshotDto
                {
                    kind = source.Kind.ToString(),
                    isAvailable = source.IsAvailable,
                    coordinatesCaptured = source.CoordinatesCaptured,
                    heightWorkMode = source.HeightWorkMode.ToString(),
                    surfaceWorkMode = source.SurfaceWorkMode.ToString(),
                    collisionWorkMode = source.CollisionWorkMode.ToString(),
                    heightTileCount = source.HeightTileCount,
                    surfaceTileCount = source.SurfaceTileCount,
                    collisionChunkCount = source.CollisionChunkCount,
                    addressablesConfigurationRequired =
                        source.AddressablesConfigurationRequired,
                    addressablesContentBuildRequired =
                        source.AddressablesContentBuildRequired,
                    runtimeSceneMetadataUpdateRequired =
                        source.RuntimeSceneMetadataUpdateRequired,
                    isInitialBake = source.IsInitialBake,
                    isBlocked = source.IsBlocked,
                    hasWork = source.HasWork,
                    blockReason = source.BlockReason ?? "",
                    sourceStateRevision = source.SourceStateRevision,
                    currentAuthoringSignature =
                        source.CurrentAuthoringSignature ?? "",
                    observedAuthoringSignature =
                        source.ObservedAuthoringSignature ?? "",
                    currentSurfaceSettingsSignature =
                        source.CurrentSurfaceSettingsSignature ?? "",
                    currentCollisionSettingsSignature =
                        source.CurrentCollisionSettingsSignature ?? ""
                };

            AddCoordinates(
                target.heightTiles,
                source.HeightTiles
            );
            AddCoordinates(
                target.surfaceTiles,
                source.SurfaceTiles
            );
            AddCoordinates(
                target.collisionChunks,
                source.CollisionChunks
            );

            if (source.SafetyEscalationReasons != null)
            {
                for (
                    int reasonIndex = 0;
                    reasonIndex < source.SafetyEscalationReasons.Count;
                    reasonIndex++
                )
                {
                    target.safetyEscalationReasons.Add(
                        source.SafetyEscalationReasons[reasonIndex] ?? ""
                    );
                }
            }

            AddReasons(
                target.reasons,
                source.Reasons
            );

            dto.planSnapshots.Add(
                target
            );
        }
    }

    private static void AddReasons(
        List<ReasonDto> target,
        IReadOnlyList<TerrainRuntimeBakeReasonRecord> source
    )
    {
        if (source == null)
        {
            return;
        }

        for (int index = 0; index < source.Count; index++)
        {
            TerrainRuntimeBakeReasonRecord reason =
                source[index];

            target.Add(
                new ReasonDto
                {
                    origin = reason.Origin.ToString(),
                    code = reason.Code.ToString(),
                    target = reason.Target.ToString(),
                    sourceTarget = reason.SourceTarget.HasValue
                        ? reason.SourceTarget.Value.ToString()
                        : "",
                    message = reason.Message ?? "",
                    isSafetyEscalation = reason.IsSafetyEscalation,
                    affectedItemCount = reason.AffectedItemCount
                }
            );
        }
    }

    private static void AddExecution(
        ReportDto dto,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (diagnostics.ExecutionStages == null)
        {
            return;
        }

        for (int index = 0; index < diagnostics.ExecutionStages.Count; index++)
        {
            TerrainRuntimeBakeStageExecutionDiagnostic source =
                diagnostics.ExecutionStages[index];

            ExecutionStageDto target =
                new ExecutionStageDto
                {
                    stage = source.Stage.ToString(),
                    plannedCount = source.PlannedCount,
                    requestedCount = source.RequestedCount,
                    evaluatedCount = source.EvaluatedCount,
                    generatedCount = source.GeneratedCount,
                    committedCount = source.CommittedCount,
                    skippedCount = source.SkippedCount,
                    unchangedCount = source.UnchangedCount,
                    failedCount = source.FailedCount,
                    addressablesConfigurationRequested =
                        source.AddressablesConfigurationRequested,
                    addressablesConfigurationPerformed =
                        source.AddressablesConfigurationPerformed,
                    addressablesContentBuildRequested =
                        source.AddressablesContentBuildRequested,
                    addressablesContentBuildPerformed =
                        source.AddressablesContentBuildPerformed,
                    addressablesMarkerWorkRequested =
                        source.AddressablesMarkerWorkRequested,
                    addressablesMarkerWorkPerformed =
                        source.AddressablesMarkerWorkPerformed,
                    addressablesMarkersRegenerated =
                        source.AddressablesMarkersRegenerated,
                    addressablesMarkersReused =
                        source.AddressablesMarkersReused,
                    addressablesMarkersRemoved =
                        source.AddressablesMarkersRemoved,
                    sceneMetadataRequested =
                        source.SceneMetadataRequested,
                    sceneMetadataPerformed =
                        source.SceneMetadataPerformed,
                    sceneModificationPerformed =
                        source.SceneModificationPerformed,
                    sceneSerializedComponentChangeCount =
                        source.SceneSerializedComponentChangeCount
                };

            AddCoordinates(
                target.requestedCoordinates,
                source.RequestedCoordinates
            );
            AddCoordinates(
                target.succeededCoordinates,
                source.SucceededCoordinates
            );
            AddCoordinates(
                target.failedCoordinates,
                source.FailedCoordinates
            );
            AddCoordinates(
                target.unprocessedCoordinates,
                source.UnprocessedCoordinates
            );

            dto.executionStages.Add(
                target
            );
        }
    }

    private static void AddPerformance(
        ReportDto dto,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (diagnostics.PerformanceRecords != null)
        {
            for (
                int index = 0;
                index < diagnostics.PerformanceRecords.Count;
                index++
            )
            {
                TerrainRuntimeBakePerformanceRecord source =
                    diagnostics.PerformanceRecords[index];

                dto.performanceRecords.Add(
                    new PerformanceRecordDto
                    {
                        name = source.Name ?? "",
                        stage = source.Stage.ToString(),
                        category = source.Category.ToString(),
                        startedAtSeconds = source.StartedAtSeconds,
                        durationSeconds = source.DurationSeconds
                    }
                );
            }
        }

        if (diagnostics.TrackedMemorySummaries != null)
        {
            for (
                int index = 0;
                index < diagnostics.TrackedMemorySummaries.Count;
                index++
            )
            {
                TerrainRuntimeBakeTrackedMemorySummary source =
                    diagnostics.TrackedMemorySummaries[index];

                dto.trackedMemory.Add(
                    new TrackedMemoryDto
                    {
                        category = source.Category.ToString(),
                        currentBytes = source.CurrentBytes,
                        peakBytes = source.PeakBytes
                    }
                );
            }
        }

        if (diagnostics.MemoryEvents != null)
        {
            for (int index = 0; index < diagnostics.MemoryEvents.Count; index++)
            {
                TerrainRuntimeBakeMemoryEvent source =
                    diagnostics.MemoryEvents[index];

                dto.memoryEvents.Add(
                    new MemoryEventDto
                    {
                        name = source.Name ?? "",
                        stage = source.Stage.ToString(),
                        category = source.Category.ToString(),
                        byteDelta = source.ByteDelta,
                        currentTrackedBytes = source.CurrentTrackedBytes,
                        atSeconds = source.AtSeconds
                    }
                );
            }
        }
    }

    private static void AddTrace(
        ReportDto dto,
        TerrainRuntimeBakeDiagnosticsSnapshot diagnostics
    )
    {
        if (diagnostics.TraceRecords == null)
        {
            return;
        }

        for (int index = 0; index < diagnostics.TraceRecords.Count; index++)
        {
            TerrainRuntimeBakeTraceRecord source =
                diagnostics.TraceRecords[index];

            dto.traceRecords.Add(
                new TraceRecordDto
                {
                    id = source.Id,
                    parentId = source.ParentId,
                    name = source.Name ?? "",
                    stage = source.Stage.ToString(),
                    outcome = source.Outcome.ToString(),
                    message = source.Message ?? "",
                    startedAtUtc = source.StartedAtUtc.ToString("u"),
                    endedAtUtc = source.EndedAtUtc.ToString("u"),
                    startedAtSeconds = source.StartedAtSeconds,
                    endedAtSeconds = source.EndedAtSeconds,
                    durationSeconds = source.DurationSeconds
                }
            );
        }
    }

    private static void AddCoordinates(
        List<CoordinateDto> target,
        IReadOnlyList<Vector2Int> source
    )
    {
        if (
            target == null
            ||
            source == null
        )
        {
            return;
        }

        for (int index = 0; index < source.Count; index++)
        {
            target.Add(
                new CoordinateDto(
                    source[index]
                )
            );
        }
    }
}

public sealed class TerrainRuntimeBakeDiagnosticsReportWindow : EditorWindow
{
    private Vector2 scrollPosition;
    private TerrainRuntimeBakeDiagnosticsLevel reportLevel =
        TerrainRuntimeBakeDiagnosticsLevel.Summary;

    private string cachedText = "";
    private TerrainRuntimeBakePipelineResult cachedResult;
    private TerrainRuntimeBakeDiagnosticsLevel cachedLevel;

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/View Latest Report"
    )]
    public static void OpenWindow()
    {
        TerrainRuntimeBakeDiagnosticsReportWindow window =
            GetWindow<TerrainRuntimeBakeDiagnosticsReportWindow>();

        window.titleContent =
            new GUIContent(
                "Bake Diagnostics"
            );

        window.RefreshText();
        window.Show();
    }

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/Copy Summary Report"
    )]
    public static void CopySummaryReport()
    {
        CopyReport(
            TerrainRuntimeBakeDiagnosticsLevel.Summary
        );
    }

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/Copy Detailed Report"
    )]
    public static void CopyDetailedReport()
    {
        CopyReport(
            TerrainRuntimeBakeDiagnosticsLevel.Detailed
        );
    }

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/Copy Trace Report"
    )]
    public static void CopyTraceReport()
    {
        CopyReport(
            TerrainRuntimeBakeDiagnosticsLevel.Trace
        );
    }

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/Export Text Report..."
    )]
    public static void ExportTextReport()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        string path =
            EditorUtility.SaveFilePanel(
                "Export Runtime Bake Text Report",
                "",
                BuildDefaultFileName("txt"),
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

        AssetDatabase.Refresh();
    }

    [MenuItem(
        "Tools/WorldMeshes/Runtime Bake Diagnostics/Export JSON Report..."
    )]
    public static void ExportJsonReport()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        string path =
            EditorUtility.SaveFilePanel(
                "Export Runtime Bake JSON Report",
                "",
                BuildDefaultFileName("json"),
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

        AssetDatabase.Refresh();
    }

    private static void CopyReport(
        TerrainRuntimeBakeDiagnosticsLevel level
    )
    {
        EditorGUIUtility.systemCopyBuffer =
            TerrainRuntimeBakeReportFormatter.BuildTextReport(
                TerrainRuntimeBakePipeline.LastResult,
                level
            );

        Debug.Log(
            "WorldMeshes runtime bake " +
            level +
            " report copied to the clipboard."
        );
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        if (result == null)
        {
            EditorGUILayout.HelpBox(
                "No runtime bake result is available yet.",
                MessageType.Info
            );
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            reportLevel =
                (TerrainRuntimeBakeDiagnosticsLevel)
                EditorGUILayout.EnumPopup(
                    "Report Level",
                    reportLevel
                );

            if (reportLevel == TerrainRuntimeBakeDiagnosticsLevel.Off)
            {
                reportLevel =
                    TerrainRuntimeBakeDiagnosticsLevel.Summary;
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
            {
                RefreshText();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Copy"))
            {
                RefreshText();
                EditorGUIUtility.systemCopyBuffer =
                    cachedText;
            }

            if (GUILayout.Button("Export Text..."))
            {
                ExportTextReport();
            }

            if (GUILayout.Button("Export JSON..."))
            {
                ExportJsonReport();
            }
        }

        if (
            cachedResult != result
            ||
            cachedLevel != reportLevel
            ||
            string.IsNullOrEmpty(cachedText)
        )
        {
            RefreshText();
        }

        scrollPosition =
            EditorGUILayout.BeginScrollView(
                scrollPosition
            );

        EditorGUILayout.TextArea(
            cachedText,
            GUILayout.ExpandHeight(true)
        );

        EditorGUILayout.EndScrollView();
    }

    private void RefreshText()
    {
        cachedResult =
            TerrainRuntimeBakePipeline.LastResult;

        cachedLevel =
            reportLevel;

        cachedText =
            TerrainRuntimeBakeReportFormatter.BuildTextReport(
                cachedResult,
                cachedLevel
            );
    }

    private static string BuildDefaultFileName(
        string extension
    )
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        string runId =
            result != null &&
            result.Diagnostics != null &&
            !string.IsNullOrEmpty(result.Diagnostics.RunId)
                ? result.Diagnostics.RunId
                : DateTime.Now.ToString("yyyyMMdd_HHmmss");

        return
            "WorldMeshes_RuntimeBake_" +
            runId +
            "." +
            extension;
    }
}
