using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEditor;

public enum TerrainRuntimeBakeDiagnosticsLevel
{
    Off,
    Summary,
    Detailed,
    Trace
}

public enum TerrainRuntimeBakeStageDiagnosticStatus
{
    NotReached,
    Executed,
    Skipped
}

public enum TerrainRuntimeBakeTraceOutcome
{
    Completed,
    Failed,
    Cancelled,
    Blocked
}

public sealed class TerrainRuntimeBakeStageDiagnostic
{
    public TerrainRuntimeBakePipelineState Stage { get; private set; }
    public TerrainRuntimeBakeStageDiagnosticStatus Status { get; private set; }
    public DateTime? EnteredAtUtc { get; private set; }
    public double EnteredAtSeconds { get; private set; }

    internal TerrainRuntimeBakeStageDiagnostic(
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeStageDiagnosticStatus status,
        DateTime? enteredAtUtc,
        double enteredAtSeconds
    )
    {
        Stage = stage;
        Status = status;
        EnteredAtUtc = enteredAtUtc;
        EnteredAtSeconds = Math.Max(0d, enteredAtSeconds);
    }
}

public sealed class TerrainRuntimeBakeTraceRecord
{
    public long Id { get; private set; }
    public long ParentId { get; private set; }
    public string Name { get; private set; }
    public TerrainRuntimeBakePipelineState Stage { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime EndedAtUtc { get; private set; }
    public double StartedAtSeconds { get; private set; }
    public double EndedAtSeconds { get; private set; }
    public double DurationSeconds { get; private set; }
    public TerrainRuntimeBakeTraceOutcome Outcome { get; private set; }
    public string Message { get; private set; }

    internal TerrainRuntimeBakeTraceRecord(
        long id,
        long parentId,
        string name,
        TerrainRuntimeBakePipelineState stage,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        double startedAtSeconds,
        double endedAtSeconds,
        TerrainRuntimeBakeTraceOutcome outcome,
        string message
    )
    {
        Id = id;
        ParentId = parentId;
        Name = name ?? "";
        Stage = stage;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        StartedAtSeconds = Math.Max(0d, startedAtSeconds);
        EndedAtSeconds = Math.Max(StartedAtSeconds, endedAtSeconds);
        DurationSeconds = Math.Max(0d, EndedAtSeconds - StartedAtSeconds);
        Outcome = outcome;
        Message = message ?? "";
    }
}

public sealed partial class TerrainRuntimeBakeDiagnosticsSnapshot
{
    private readonly ReadOnlyCollection<TerrainRuntimeBakeStageDiagnostic>
        stageRecords;

    private readonly ReadOnlyCollection<TerrainRuntimeBakeTraceRecord>
        traceRecords;

    private readonly ReadOnlyCollection<string>
        warningMessages;

    public string RunId { get; private set; }
    public TerrainRuntimeBakeDiagnosticsLevel Level { get; private set; }
    public TerrainRuntimeBakePipelineMode Mode { get; private set; }
    public TerrainRuntimeBakePipelineOutcome Outcome { get; private set; }
    public TerrainRuntimeBakePipelineState FinalState { get; private set; }
    public TerrainRuntimeBakePipelineState LastStage { get; private set; }
    public TerrainRuntimeBakePipelineState FailedStage { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime EndedAtUtc { get; private set; }
    public double DurationSeconds { get; private set; }
    public IReadOnlyList<TerrainRuntimeBakeStageDiagnostic> StageRecords =>
        stageRecords;
    public IReadOnlyList<TerrainRuntimeBakeTraceRecord> TraceRecords =>
        traceRecords;
    public IReadOnlyList<string> WarningMessages =>
        warningMessages;
    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    internal TerrainRuntimeBakeDiagnosticsSnapshot(
        string runId,
        TerrainRuntimeBakeDiagnosticsLevel level,
        TerrainRuntimeBakePipelineMode mode,
        TerrainRuntimeBakePipelineOutcome outcome,
        TerrainRuntimeBakePipelineState finalState,
        TerrainRuntimeBakePipelineState lastStage,
        TerrainRuntimeBakePipelineState failedStage,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        double durationSeconds,
        IEnumerable<TerrainRuntimeBakeStageDiagnostic> stageRecords,
        IEnumerable<TerrainRuntimeBakeTraceRecord> traceRecords,
        IEnumerable<string> warningMessages,
        string errorMessage,
        string summaryMessage
    )
    {
        RunId = runId ?? "";
        Level = level;
        Mode = mode;
        Outcome = outcome;
        FinalState = finalState;
        LastStage = lastStage;
        FailedStage = failedStage;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        DurationSeconds = Math.Max(0d, durationSeconds);

        List<TerrainRuntimeBakeStageDiagnostic> stages =
            stageRecords != null
                ? new List<TerrainRuntimeBakeStageDiagnostic>(stageRecords)
                : new List<TerrainRuntimeBakeStageDiagnostic>();

        this.stageRecords = stages.AsReadOnly();

        List<TerrainRuntimeBakeTraceRecord> traces =
            traceRecords != null
                ? new List<TerrainRuntimeBakeTraceRecord>(traceRecords)
                : new List<TerrainRuntimeBakeTraceRecord>();

        this.traceRecords = traces.AsReadOnly();

        List<string> warnings =
            warningMessages != null
                ? new List<string>(warningMessages)
                : new List<string>();

        this.warningMessages = warnings.AsReadOnly();

        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }
}

public static class TerrainRuntimeBakeDiagnostics
{
    private static TerrainRuntimeBakeDiagnosticsSession activeSession;

    private static TerrainRuntimeBakeDiagnosticsLevel currentLevel =
        TerrainRuntimeBakeDiagnosticsLevel.Summary;

    public static TerrainRuntimeBakeDiagnosticsLevel Level
    {
        get => currentLevel;
        set
        {
            if (!Enum.IsDefined(typeof(TerrainRuntimeBakeDiagnosticsLevel), value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            currentLevel = value;
        }
    }

    internal static TerrainRuntimeBakeDiagnosticsSession ActiveSession =>
        activeSession;

    internal static TerrainRuntimeBakeDiagnosticsSession BeginSession(
        TerrainRuntimeBakePipelineMode mode,
        DateTime startedAtUtc,
        double startedAtEditorTime
    )
    {
        TerrainRuntimeBakeDiagnosticsLevel level =
            currentLevel;

        if (level == TerrainRuntimeBakeDiagnosticsLevel.Off)
        {
            activeSession = null;
            return null;
        }

        TerrainRuntimeBakeDiagnosticsSession session =
            new TerrainRuntimeBakeDiagnosticsSession(
                Guid.NewGuid().ToString("N"),
                level,
                mode,
                startedAtUtc,
                startedAtEditorTime
            );

        activeSession = session;
        return session;
    }

    internal static void NotifySessionCompleted(
        TerrainRuntimeBakeDiagnosticsSession session
    )
    {
        if (ReferenceEquals(activeSession, session))
        {
            activeSession = null;
        }
    }
}

internal sealed partial class TerrainRuntimeBakeDiagnosticsSession
{
    private sealed class StageEntry
    {
        public TerrainRuntimeBakePipelineState stage;
        public DateTime enteredAtUtc;
        public double enteredAtEditorTime;
    }

    internal sealed class MutableTraceRecord
    {
        public long id;
        public long parentId;
        public string name;
        public TerrainRuntimeBakePipelineState stage;
        public DateTime startedAtUtc;
        public DateTime endedAtUtc;
        public double startedAtEditorTime;
        public double endedAtEditorTime;
        public TerrainRuntimeBakeTraceOutcome outcome;
        public string message;
        public bool closed;
    }

    private readonly string runId;
    private readonly TerrainRuntimeBakeDiagnosticsLevel level;
    private readonly TerrainRuntimeBakePipelineMode mode;
    private readonly DateTime startedAtUtc;
    private readonly double startedAtEditorTime;

    private readonly List<StageEntry> stageEntries =
        new List<StageEntry>();

    private readonly List<MutableTraceRecord> traceRecords =
        new List<MutableTraceRecord>();

    private readonly List<MutableTraceRecord> traceStack =
        new List<MutableTraceRecord>();

    private long nextTraceId = 1;
    private bool completed;

    private TerrainRuntimeBakeDiagnosticsSnapshot completedSnapshot;

    public string RunId => runId;
    public TerrainRuntimeBakeDiagnosticsLevel Level => level;

    internal bool IsTraceEnabled =>
        !completed
        &&
        level == TerrainRuntimeBakeDiagnosticsLevel.Trace;

    internal TerrainRuntimeBakeDiagnosticsSession(
        string runId,
        TerrainRuntimeBakeDiagnosticsLevel level,
        TerrainRuntimeBakePipelineMode mode,
        DateTime startedAtUtc,
        double startedAtEditorTime
    )
    {
        this.runId = runId ?? "";
        this.level = level;
        this.mode = mode;
        this.startedAtUtc = startedAtUtc;
        this.startedAtEditorTime = startedAtEditorTime;
    }

    internal void RecordStageEntered(
        TerrainRuntimeBakePipelineState stage
    )
    {
        if (completed)
        {
            return;
        }

        for (
            int index = 0;
            index < stageEntries.Count;
            index++
        )
        {
            if (stageEntries[index].stage == stage)
            {
                return;
            }
        }

        stageEntries.Add(
            new StageEntry
            {
                stage = stage,
                enteredAtUtc = DateTime.UtcNow,
                enteredAtEditorTime = EditorApplication.timeSinceStartup
            }
        );
    }

    internal TerrainRuntimeBakeTraceScope BeginTrace(
        string name,
        TerrainRuntimeBakePipelineState stage
    )
    {
        if (
            !IsTraceEnabled
            ||
            string.IsNullOrEmpty(name)
        )
        {
            return null;
        }

        MutableTraceRecord parent =
            traceStack.Count > 0
                ? traceStack[traceStack.Count - 1]
                : null;

        MutableTraceRecord record =
            new MutableTraceRecord
            {
                id = nextTraceId++,
                parentId = parent != null ? parent.id : 0,
                name = name,
                stage = stage,
                startedAtUtc = DateTime.UtcNow,
                startedAtEditorTime = EditorApplication.timeSinceStartup,
                outcome = TerrainRuntimeBakeTraceOutcome.Completed,
                message = ""
            };

        traceRecords.Add(record);
        traceStack.Add(record);

        return
            new TerrainRuntimeBakeTraceScope(
                this,
                record
            );
    }

    internal void CloseTrace(
        MutableTraceRecord record,
        TerrainRuntimeBakeTraceOutcome outcome,
        string message
    )
    {
        if (
            record == null
            ||
            record.closed
        )
        {
            return;
        }

        CloseTrace(
            record,
            outcome,
            message,
            DateTime.UtcNow,
            EditorApplication.timeSinceStartup
        );
    }

    internal TerrainRuntimeBakeDiagnosticsSnapshot Complete(
        TerrainRuntimeBakePipelineOutcome outcome,
        TerrainRuntimeBakePipelineState finalState,
        TerrainRuntimeBakePipelineState lastStage,
        TerrainRuntimeBakePipelineState failedStage,
        bool heightStageExecuted,
        bool surfaceStageExecuted,
        bool collisionStageExecuted,
        bool addressablesStageExecuted,
        bool sceneSyncStageExecuted,
        IEnumerable<string> warningMessages,
        string errorMessage,
        string summaryMessage
    )
    {
        if (completed)
        {
            return completedSnapshot;
        }

        DateTime endedAtUtc =
            DateTime.UtcNow;

        double endedAtEditorTime =
            EditorApplication.timeSinceStartup;

        TerrainRuntimeBakeTraceOutcome traceOutcome =
            GetTraceOutcome(
                outcome
            );

        string traceMessage =
            !string.IsNullOrEmpty(errorMessage)
                ? errorMessage
                : summaryMessage;

        CloseOpenTraceScopes(
            traceOutcome,
            traceMessage,
            endedAtUtc,
            endedAtEditorTime
        );

        List<TerrainRuntimeBakeStageDiagnostic> stages =
            new List<TerrainRuntimeBakeStageDiagnostic>
            {
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.Preflight,
                    true
                ),
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.Heightmaps,
                    heightStageExecuted
                ),
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.SurfaceMasks,
                    surfaceStageExecuted
                ),
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.Collision,
                    collisionStageExecuted
                ),
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.Addressables,
                    addressablesStageExecuted
                ),
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.SceneSync,
                    sceneSyncStageExecuted
                ),
                BuildStageRecord(
                    TerrainRuntimeBakePipelineState.Finalizing,
                    true
                )
            };

        List<TerrainRuntimeBakeTraceRecord> traces =
            BuildTraceRecords();

        completedSnapshot =
            new TerrainRuntimeBakeDiagnosticsSnapshot(
                runId,
                level,
                mode,
                outcome,
                finalState,
                lastStage,
                failedStage,
                startedAtUtc,
                endedAtUtc,
                Math.Max(
                    0d,
                    endedAtEditorTime -
                    startedAtEditorTime
                ),
                stages,
                traces,
                warningMessages,
                errorMessage,
                summaryMessage
            );

        AttachPlanningDataTo(
            completedSnapshot
        );

        AttachExecutionDataTo(
            completedSnapshot
        );

        AttachPerformanceDataTo(
            completedSnapshot
        );

        completed =
            true;

        TerrainRuntimeBakeDiagnostics.NotifySessionCompleted(
            this
        );

        return completedSnapshot;
    }

    private TerrainRuntimeBakeStageDiagnostic BuildStageRecord(
        TerrainRuntimeBakePipelineState stage,
        bool executedIfEntered
    )
    {
        StageEntry entry =
            FindStageEntry(
                stage
            );

        if (entry == null)
        {
            return
                new TerrainRuntimeBakeStageDiagnostic(
                    stage,
                    TerrainRuntimeBakeStageDiagnosticStatus.NotReached,
                    null,
                    0d
                );
        }

        return
            new TerrainRuntimeBakeStageDiagnostic(
                stage,
                executedIfEntered
                    ? TerrainRuntimeBakeStageDiagnosticStatus.Executed
                    : TerrainRuntimeBakeStageDiagnosticStatus.Skipped,
                entry.enteredAtUtc,
                Math.Max(
                    0d,
                    entry.enteredAtEditorTime -
                    startedAtEditorTime
                )
            );
    }

    private StageEntry FindStageEntry(
        TerrainRuntimeBakePipelineState stage
    )
    {
        for (
            int index = 0;
            index < stageEntries.Count;
            index++
        )
        {
            StageEntry entry =
                stageEntries[index];

            if (entry.stage == stage)
            {
                return entry;
            }
        }

        return null;
    }

    private List<TerrainRuntimeBakeTraceRecord> BuildTraceRecords()
    {
        List<TerrainRuntimeBakeTraceRecord> records =
            new List<TerrainRuntimeBakeTraceRecord>(
                traceRecords.Count
            );

        for (
            int index = 0;
            index < traceRecords.Count;
            index++
        )
        {
            MutableTraceRecord record =
                traceRecords[index];

            records.Add(
                new TerrainRuntimeBakeTraceRecord(
                    record.id,
                    record.parentId,
                    record.name,
                    record.stage,
                    record.startedAtUtc,
                    record.endedAtUtc,
                    Math.Max(
                        0d,
                        record.startedAtEditorTime -
                        startedAtEditorTime
                    ),
                    Math.Max(
                        0d,
                        record.endedAtEditorTime -
                        startedAtEditorTime
                    ),
                    record.outcome,
                    record.message
                )
            );
        }

        return records;
    }

    private void CloseOpenTraceScopes(
        TerrainRuntimeBakeTraceOutcome outcome,
        string message,
        DateTime endedAtUtc,
        double endedAtEditorTime
    )
    {
        while (traceStack.Count > 0)
        {
            MutableTraceRecord record =
                traceStack[traceStack.Count - 1];

            CloseTrace(
                record,
                outcome,
                message,
                endedAtUtc,
                endedAtEditorTime
            );
        }
    }

    private void CloseTrace(
        MutableTraceRecord record,
        TerrainRuntimeBakeTraceOutcome outcome,
        string message,
        DateTime endedAtUtc,
        double endedAtEditorTime
    )
    {
        if (
            record == null
            ||
            record.closed
        )
        {
            return;
        }

        record.closed =
            true;

        record.outcome =
            outcome;

        record.message =
            message ?? "";

        record.endedAtUtc =
            endedAtUtc;

        record.endedAtEditorTime =
            Math.Max(
                record.startedAtEditorTime,
                endedAtEditorTime
            );

        int stackIndex =
            traceStack.LastIndexOf(
                record
            );

        if (stackIndex >= 0)
        {
            traceStack.RemoveAt(
                stackIndex
            );
        }
    }

    private static TerrainRuntimeBakeTraceOutcome GetTraceOutcome(
        TerrainRuntimeBakePipelineOutcome outcome
    )
    {
        switch (outcome)
        {
            case TerrainRuntimeBakePipelineOutcome.Cancelled:
                return TerrainRuntimeBakeTraceOutcome.Cancelled;

            case TerrainRuntimeBakePipelineOutcome.Blocked:
                return TerrainRuntimeBakeTraceOutcome.Blocked;

            case TerrainRuntimeBakePipelineOutcome.Failed:
                return TerrainRuntimeBakeTraceOutcome.Failed;

            default:
                return TerrainRuntimeBakeTraceOutcome.Completed;
        }
    }
}

internal sealed class TerrainRuntimeBakeTraceScope : IDisposable
{
    private TerrainRuntimeBakeDiagnosticsSession session;
    private TerrainRuntimeBakeDiagnosticsSession.MutableTraceRecord record;
    private bool closed;

    internal TerrainRuntimeBakeTraceScope(
        TerrainRuntimeBakeDiagnosticsSession session,
        TerrainRuntimeBakeDiagnosticsSession.MutableTraceRecord record
    )
    {
        this.session = session;
        this.record = record;
    }

    internal void Complete()
    {
        Close(
            TerrainRuntimeBakeTraceOutcome.Completed,
            ""
        );
    }

    internal void Fail(
        string message
    )
    {
        Close(
            TerrainRuntimeBakeTraceOutcome.Failed,
            message
        );
    }

    public void Dispose()
    {
        if (!closed)
        {
            Complete();
        }
    }

    private void Close(
        TerrainRuntimeBakeTraceOutcome outcome,
        string message
    )
    {
        if (closed)
        {
            return;
        }

        closed =
            true;

        TerrainRuntimeBakeDiagnosticsSession activeSession =
            session;

        TerrainRuntimeBakeDiagnosticsSession.MutableTraceRecord activeRecord =
            record;

        session =
            null;

        record =
            null;

        if (activeSession != null)
        {
            activeSession.CloseTrace(
                activeRecord,
                outcome,
                message
            );
        }
    }
}
