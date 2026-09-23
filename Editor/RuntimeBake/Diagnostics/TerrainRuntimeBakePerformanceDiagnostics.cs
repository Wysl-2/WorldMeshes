using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEditor;
using UnityEngine;

public enum TerrainRuntimeBakePerformanceCategory
{
    Prepare,
    Gather,
    Generate,
    Validate,
    Commit,
    Finalize,
    AssetDatabase,
    Addressables,
    SceneSync,
    Other
}

public enum TerrainRuntimeBakeTrackedMemoryCategory
{
    HeightBuffer,
    HeightAssetResidency,
    SurfaceBuffer,
    CollisionBuffer,
    OtherTemporary
}

public sealed class TerrainRuntimeBakePerformanceRecord
{
    public string Name { get; private set; }
    public TerrainRuntimeBakePipelineState Stage { get; private set; }
    public TerrainRuntimeBakePerformanceCategory Category { get; private set; }
    public double StartedAtSeconds { get; private set; }
    public double DurationSeconds { get; private set; }

    internal TerrainRuntimeBakePerformanceRecord(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category,
        double startedAtSeconds,
        double durationSeconds
    )
    {
        Name = name ?? "";
        Stage = stage;
        Category = category;
        StartedAtSeconds = Math.Max(0d, startedAtSeconds);
        DurationSeconds = Math.Max(0d, durationSeconds);
    }
}

public sealed class TerrainRuntimeBakePerformanceAggregateRecord
{
    public string Name { get; private set; }
    public TerrainRuntimeBakePipelineState Stage { get; private set; }
    public TerrainRuntimeBakePerformanceCategory Category { get; private set; }
    public long SampleCount { get; private set; }
    public double TotalDurationSeconds { get; private set; }
    public double AverageDurationSeconds { get; private set; }
    public double MinimumDurationSeconds { get; private set; }
    public double MaximumDurationSeconds { get; private set; }

    internal TerrainRuntimeBakePerformanceAggregateRecord(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category,
        long sampleCount,
        double totalDurationSeconds,
        double minimumDurationSeconds,
        double maximumDurationSeconds
    )
    {
        Name = name ?? "";
        Stage = stage;
        Category = category;
        SampleCount = Math.Max(0L, sampleCount);
        TotalDurationSeconds = Math.Max(0d, totalDurationSeconds);
        AverageDurationSeconds =
            SampleCount > 0L
                ? TotalDurationSeconds / SampleCount
                : 0d;
        MinimumDurationSeconds =
            SampleCount > 0L
                ? Math.Max(0d, minimumDurationSeconds)
                : 0d;
        MaximumDurationSeconds =
            SampleCount > 0L
                ? Math.Max(0d, maximumDurationSeconds)
                : 0d;
    }
}

public sealed class TerrainRuntimeBakeTrackedMemorySummary
{
    public TerrainRuntimeBakeTrackedMemoryCategory Category { get; private set; }
    public long CurrentBytes { get; private set; }
    public long PeakBytes { get; private set; }

    internal TerrainRuntimeBakeTrackedMemorySummary(
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long currentBytes,
        long peakBytes
    )
    {
        Category = category;
        CurrentBytes = Math.Max(0L, currentBytes);
        PeakBytes = Math.Max(0L, peakBytes);
    }
}

public sealed class TerrainRuntimeBakeMemoryEvent
{
    public string Name { get; private set; }
    public TerrainRuntimeBakePipelineState Stage { get; private set; }
    public TerrainRuntimeBakeTrackedMemoryCategory Category { get; private set; }
    public long ByteDelta { get; private set; }
    public long CurrentTrackedBytes { get; private set; }
    public double AtSeconds { get; private set; }

    internal TerrainRuntimeBakeMemoryEvent(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long byteDelta,
        long currentTrackedBytes,
        double atSeconds
    )
    {
        Name = name ?? "";
        Stage = stage;
        Category = category;
        ByteDelta = byteDelta;
        CurrentTrackedBytes = Math.Max(0L, currentTrackedBytes);
        AtSeconds = Math.Max(0d, atSeconds);
    }
}

public sealed partial class TerrainRuntimeBakeDiagnosticsSnapshot
{
    private ReadOnlyCollection<TerrainRuntimeBakePerformanceRecord>
        performanceRecords =
            new List<TerrainRuntimeBakePerformanceRecord>().AsReadOnly();

    private ReadOnlyCollection<TerrainRuntimeBakePerformanceAggregateRecord>
        aggregatedPerformanceRecords =
            new List<TerrainRuntimeBakePerformanceAggregateRecord>().AsReadOnly();

    private ReadOnlyCollection<TerrainRuntimeBakeTrackedMemorySummary>
        trackedMemorySummaries =
            new List<TerrainRuntimeBakeTrackedMemorySummary>().AsReadOnly();

    private ReadOnlyCollection<TerrainRuntimeBakeMemoryEvent>
        memoryEvents =
            new List<TerrainRuntimeBakeMemoryEvent>().AsReadOnly();

    public IReadOnlyList<TerrainRuntimeBakePerformanceRecord> PerformanceRecords =>
        performanceRecords;

    public IReadOnlyList<TerrainRuntimeBakePerformanceAggregateRecord> AggregatedPerformanceRecords =>
        aggregatedPerformanceRecords;

    public IReadOnlyList<TerrainRuntimeBakeTrackedMemorySummary> TrackedMemorySummaries =>
        trackedMemorySummaries;

    public IReadOnlyList<TerrainRuntimeBakeMemoryEvent> MemoryEvents =>
        memoryEvents;

    public long CurrentTrackedTemporaryBytes { get; private set; }
    public long PeakTrackedTemporaryBytes { get; private set; }

    internal void AttachPerformanceData(
        IEnumerable<TerrainRuntimeBakePerformanceRecord> performanceRecords,
        IEnumerable<TerrainRuntimeBakePerformanceAggregateRecord> aggregatedPerformanceRecords,
        IEnumerable<TerrainRuntimeBakeTrackedMemorySummary> trackedMemorySummaries,
        IEnumerable<TerrainRuntimeBakeMemoryEvent> memoryEvents,
        long currentTrackedTemporaryBytes,
        long peakTrackedTemporaryBytes
    )
    {
        this.performanceRecords =
            new List<TerrainRuntimeBakePerformanceRecord>(
                performanceRecords ?? Array.Empty<TerrainRuntimeBakePerformanceRecord>()
            )
            .AsReadOnly();

        this.aggregatedPerformanceRecords =
            new List<TerrainRuntimeBakePerformanceAggregateRecord>(
                aggregatedPerformanceRecords ?? Array.Empty<TerrainRuntimeBakePerformanceAggregateRecord>()
            )
            .AsReadOnly();

        this.trackedMemorySummaries =
            new List<TerrainRuntimeBakeTrackedMemorySummary>(
                trackedMemorySummaries ?? Array.Empty<TerrainRuntimeBakeTrackedMemorySummary>()
            )
            .AsReadOnly();

        this.memoryEvents =
            new List<TerrainRuntimeBakeMemoryEvent>(
                memoryEvents ?? Array.Empty<TerrainRuntimeBakeMemoryEvent>()
            )
            .AsReadOnly();

        CurrentTrackedTemporaryBytes =
            Math.Max(0L, currentTrackedTemporaryBytes);

        PeakTrackedTemporaryBytes =
            Math.Max(0L, peakTrackedTemporaryBytes);
    }

    internal void AppendPerformanceReport(StringBuilder builder)
    {
        if (builder == null)
        {
            return;
        }

        if (performanceRecords.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Performance Metrics:");

            for (int index = 0; index < performanceRecords.Count; index++)
            {
                TerrainRuntimeBakePerformanceRecord record =
                    performanceRecords[index];

                builder.Append("  ");
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

        if (aggregatedPerformanceRecords.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Aggregated Performance Metrics:");

            for (int index = 0; index < aggregatedPerformanceRecords.Count; index++)
            {
                TerrainRuntimeBakePerformanceAggregateRecord record =
                    aggregatedPerformanceRecords[index];

                builder.Append("  ");
                builder.Append(record.Stage);
                builder.Append(" / ");
                builder.Append(record.Category);
                builder.Append(" / ");
                builder.Append(record.Name);
                builder.Append(": calls ");
                builder.Append(record.SampleCount);
                builder.Append(", total ");
                builder.Append(record.TotalDurationSeconds.ToString("0.000"));
                builder.Append(" s, avg ");
                builder.Append(record.AverageDurationSeconds.ToString("0.000000"));
                builder.Append(" s, min ");
                builder.Append(record.MinimumDurationSeconds.ToString("0.000000"));
                builder.Append(" s, max ");
                builder.Append(record.MaximumDurationSeconds.ToString("0.000000"));
                builder.AppendLine(" s");
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Tracked WorldMeshes Temporary Memory:"
        );
        builder.AppendLine(
            "  Current: " +
            FormatBytes(CurrentTrackedTemporaryBytes)
        );
        builder.AppendLine(
            "  Peak: " +
            FormatBytes(PeakTrackedTemporaryBytes)
        );

        for (int index = 0; index < trackedMemorySummaries.Count; index++)
        {
            TerrainRuntimeBakeTrackedMemorySummary summary =
                trackedMemorySummaries[index];

            builder.Append("  ");
            builder.Append(summary.Category);
            builder.Append(": current ");
            builder.Append(FormatBytes(summary.CurrentBytes));
            builder.Append(", peak ");
            builder.AppendLine(FormatBytes(summary.PeakBytes));
        }

        builder.AppendLine(
            "  Note: tracked WorldMeshes-owned temporary buffers only; " +
            "this is not total Unity/process/system memory."
        );
    }

    private static string FormatBytes(long bytes)
    {
        double value = Math.Max(0L, bytes);

        if (value >= 1024d * 1024d * 1024d)
        {
            return (value / (1024d * 1024d * 1024d)).ToString("0.00") + " GB";
        }

        if (value >= 1024d * 1024d)
        {
            return (value / (1024d * 1024d)).ToString("0.00") + " MB";
        }

        if (value >= 1024d)
        {
            return (value / 1024d).ToString("0.00") + " KB";
        }

        return bytes + " B";
    }
}

public static class TerrainRuntimeBakePerformanceDiagnostics
{
    public static TerrainRuntimeBakePerformanceScope BeginOperation(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category
    )
    {
        TerrainRuntimeBakeDiagnosticsSession session =
            TerrainRuntimeBakeDiagnostics.ActiveSession;

        return
            session != null
                ? session.BeginPerformanceOperation(
                    name,
                    stage,
                    category
                )
                : null;
    }

    public static TerrainRuntimeBakeAggregatedPerformanceScope BeginAggregatedOperation(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category
    )
    {
        TerrainRuntimeBakeDiagnosticsSession session =
            TerrainRuntimeBakeDiagnostics.ActiveSession;

        return
            session != null
                ? session.BeginAggregatedPerformanceOperation(
                    name,
                    stage,
                    category
                )
                : default;
    }

    public static TerrainRuntimeBakeRepeatedMemoryScope TrackRepeatedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        TerrainRuntimeBakeDiagnosticsSession session =
            TerrainRuntimeBakeDiagnostics.ActiveSession;

        return
            session != null
                ? session.TrackRepeatedTemporaryMemory(
                    name,
                    stage,
                    category,
                    bytes
                )
                : default;
    }

    public static TerrainRuntimeBakeTrackedMemoryLease TrackTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        TerrainRuntimeBakeDiagnosticsSession session =
            TerrainRuntimeBakeDiagnostics.ActiveSession;

        return
            session != null
                ? session.TrackTemporaryMemory(
                    name,
                    stage,
                    category,
                    bytes
                )
                : null;
    }
}

public sealed class TerrainRuntimeBakePerformanceScope : IDisposable
{
    private TerrainRuntimeBakeDiagnosticsSession session;
    private readonly string name;
    private readonly TerrainRuntimeBakePipelineState stage;
    private readonly TerrainRuntimeBakePerformanceCategory category;
    private readonly double startedAtEditorTime;
    private readonly TerrainRuntimeBakeTraceScope traceScope;
    private bool closed;

    internal TerrainRuntimeBakePerformanceScope(
        TerrainRuntimeBakeDiagnosticsSession session,
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category
    )
    {
        this.session = session;
        this.name = name ?? "";
        this.stage = stage;
        this.category = category;
        startedAtEditorTime = EditorApplication.timeSinceStartup;

        traceScope =
            session != null
                ? session.BeginTrace(
                    "Performance." + this.name,
                    stage
                )
                : null;
    }

    public void Complete()
    {
        Close();
    }

    public void Dispose()
    {
        Close();
    }

    private void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;

        TerrainRuntimeBakeDiagnosticsSession current =
            session;

        session = null;

        double endedAt =
            EditorApplication.timeSinceStartup;

        current?.RecordPerformanceOperation(
            name,
            stage,
            category,
            startedAtEditorTime,
            endedAt
        );

        traceScope?.Complete();
        traceScope?.Dispose();
    }
}

public struct TerrainRuntimeBakeAggregatedPerformanceScope : IDisposable
{
    private TerrainRuntimeBakeDiagnosticsSession session;
    private TerrainRuntimeBakeDiagnosticsSession.MutablePerformanceAggregate aggregate;
    private double startedAtEditorTime;
    private TerrainRuntimeBakeTraceScope traceScope;
    private bool closed;

    internal TerrainRuntimeBakeAggregatedPerformanceScope(
        TerrainRuntimeBakeDiagnosticsSession session,
        TerrainRuntimeBakeDiagnosticsSession.MutablePerformanceAggregate aggregate,
        double startedAtEditorTime,
        TerrainRuntimeBakeTraceScope traceScope
    )
    {
        this.session = session;
        this.aggregate = aggregate;
        this.startedAtEditorTime = startedAtEditorTime;
        this.traceScope = traceScope;
        closed = false;
    }

    public void Dispose()
    {
        if (closed)
        {
            return;
        }

        closed = true;

        TerrainRuntimeBakeDiagnosticsSession currentSession =
            session;

        TerrainRuntimeBakeDiagnosticsSession.MutablePerformanceAggregate currentAggregate =
            aggregate;

        TerrainRuntimeBakeTraceScope currentTrace =
            traceScope;

        session = null;
        aggregate = null;
        traceScope = null;

        if (
            currentSession == null
            ||
            currentAggregate == null
        )
        {
            return;
        }

        double endedAt =
            EditorApplication.timeSinceStartup;

        currentSession.RecordAggregatedPerformanceOperation(
            currentAggregate,
            startedAtEditorTime,
            endedAt
        );

        currentTrace?.Complete();
        currentTrace?.Dispose();
    }
}

public struct TerrainRuntimeBakeRepeatedMemoryScope : IDisposable
{
    private TerrainRuntimeBakeDiagnosticsSession session;
    private string name;
    private TerrainRuntimeBakePipelineState stage;
    private TerrainRuntimeBakeTrackedMemoryCategory category;
    private long bytes;
    private bool recordTraceEvent;
    private bool released;

    internal TerrainRuntimeBakeRepeatedMemoryScope(
        TerrainRuntimeBakeDiagnosticsSession session,
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes,
        bool recordTraceEvent
    )
    {
        this.session = session;
        this.name = name ?? "";
        this.stage = stage;
        this.category = category;
        this.bytes = Math.Max(0L, bytes);
        this.recordTraceEvent = recordTraceEvent;
        released = false;

        session?.AddTrackedTemporaryMemory(
            this.name,
            stage,
            category,
            this.bytes,
            recordTraceEvent
        );
    }

    public void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;

        TerrainRuntimeBakeDiagnosticsSession current =
            session;

        session = null;

        current?.RemoveTrackedTemporaryMemory(
            name,
            stage,
            category,
            bytes,
            recordTraceEvent
        );
    }
}

public sealed class TerrainRuntimeBakeTrackedMemoryLease : IDisposable
{
    private TerrainRuntimeBakeDiagnosticsSession session;
    private readonly string name;
    private readonly TerrainRuntimeBakePipelineState stage;
    private readonly TerrainRuntimeBakeTrackedMemoryCategory category;
    private readonly long bytes;
    private bool released;

    internal TerrainRuntimeBakeTrackedMemoryLease(
        TerrainRuntimeBakeDiagnosticsSession session,
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        this.session = session;
        this.name = name ?? "";
        this.stage = stage;
        this.category = category;
        this.bytes = Math.Max(0L, bytes);

        session?.AddTrackedTemporaryMemory(
            this.name,
            stage,
            category,
            this.bytes
        );
    }

    public void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;

        TerrainRuntimeBakeDiagnosticsSession current =
            session;

        session = null;

        current?.RemoveTrackedTemporaryMemory(
            name,
            stage,
            category,
            bytes
        );
    }
}

internal sealed partial class TerrainRuntimeBakeDiagnosticsSession
{
    internal sealed class MutablePerformanceAggregate
    {
        public string name;
        public TerrainRuntimeBakePipelineState stage;
        public TerrainRuntimeBakePerformanceCategory category;
        public long startedCount;
        public long sampleCount;
        public double totalDurationSeconds;
        public double minimumDurationSeconds =
            double.PositiveInfinity;
        public double maximumDurationSeconds;
    }

    private sealed class MutableRepeatedMemoryCounter
    {
        public string name;
        public TerrainRuntimeBakePipelineState stage;
        public TerrainRuntimeBakeTrackedMemoryCategory category;
        public long occurrenceCount;
    }

    private const long RepeatedTraceInitialSampleCount =
        8L;

    private const long RepeatedTraceSampleStride =
        1024L;

    private sealed class MutableTrackedMemory
    {
        public TerrainRuntimeBakeTrackedMemoryCategory category;
        public long currentBytes;
        public long peakBytes;
    }

    private readonly List<TerrainRuntimeBakePerformanceRecord>
        performanceRecords =
            new List<TerrainRuntimeBakePerformanceRecord>();

    private readonly List<MutablePerformanceAggregate>
        performanceAggregates =
            new List<MutablePerformanceAggregate>();

    private readonly List<MutableRepeatedMemoryCounter>
        repeatedMemoryCounters =
            new List<MutableRepeatedMemoryCounter>();

    private readonly List<MutableTrackedMemory>
        trackedMemoryByCategory =
            new List<MutableTrackedMemory>();

    private readonly List<TerrainRuntimeBakeMemoryEvent>
        memoryEvents =
            new List<TerrainRuntimeBakeMemoryEvent>();

    private long currentTrackedTemporaryBytes;
    private long peakTrackedTemporaryBytes;

    internal TerrainRuntimeBakePerformanceScope BeginPerformanceOperation(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category
    )
    {
        if (completed || string.IsNullOrEmpty(name))
        {
            return null;
        }

        return
            new TerrainRuntimeBakePerformanceScope(
                this,
                name,
                stage,
                category
            );
    }

    internal TerrainRuntimeBakeAggregatedPerformanceScope BeginAggregatedPerformanceOperation(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category
    )
    {
        if (completed || string.IsNullOrEmpty(name))
        {
            return default;
        }

        MutablePerformanceAggregate aggregate =
            GetOrCreatePerformanceAggregate(
                name,
                stage,
                category
            );

        aggregate.startedCount++;

        TerrainRuntimeBakeTraceScope traceScope =
            null;

        if (
            level == TerrainRuntimeBakeDiagnosticsLevel.Trace
            &&
            ShouldSampleRepeatedOccurrence(
                aggregate.startedCount
            )
        )
        {
            traceScope =
                BeginTrace(
                    "Performance." +
                    name +
                    ".Sample#" +
                    aggregate.startedCount,
                    stage
                );
        }

        return
            new TerrainRuntimeBakeAggregatedPerformanceScope(
                this,
                aggregate,
                EditorApplication.timeSinceStartup,
                traceScope
            );
    }

    internal TerrainRuntimeBakeRepeatedMemoryScope TrackRepeatedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        if (completed || bytes <= 0L)
        {
            return default;
        }

        MutableRepeatedMemoryCounter counter =
            GetOrCreateRepeatedMemoryCounter(
                name,
                stage,
                category
            );

        counter.occurrenceCount++;

        bool recordTraceEvent =
            level == TerrainRuntimeBakeDiagnosticsLevel.Trace
            &&
            ShouldSampleRepeatedOccurrence(
                counter.occurrenceCount
            );

        return
            new TerrainRuntimeBakeRepeatedMemoryScope(
                this,
                name,
                stage,
                category,
                bytes,
                recordTraceEvent
            );
    }

    internal TerrainRuntimeBakeTrackedMemoryLease TrackTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        if (completed || bytes <= 0L)
        {
            return null;
        }

        return
            new TerrainRuntimeBakeTrackedMemoryLease(
                this,
                name,
                stage,
                category,
                bytes
            );
    }

    internal void RecordPerformanceOperation(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category,
        double startedAtEditorTime,
        double endedAtEditorTime
    )
    {
        if (completed)
        {
            return;
        }

        performanceRecords.Add(
            new TerrainRuntimeBakePerformanceRecord(
                name,
                stage,
                category,
                Math.Max(
                    0d,
                    startedAtEditorTime -
                    startedAtEditorTimeForDiagnostics
                ),
                Math.Max(
                    0d,
                    endedAtEditorTime -
                    startedAtEditorTime
                )
            )
        );
    }

    internal void RecordAggregatedPerformanceOperation(
        MutablePerformanceAggregate aggregate,
        double startedAtEditorTime,
        double endedAtEditorTime
    )
    {
        if (completed || aggregate == null)
        {
            return;
        }

        double duration =
            Math.Max(
                0d,
                endedAtEditorTime -
                startedAtEditorTime
            );

        aggregate.sampleCount++;
        aggregate.totalDurationSeconds +=
            duration;
        aggregate.minimumDurationSeconds =
            Math.Min(
                aggregate.minimumDurationSeconds,
                duration
            );
        aggregate.maximumDurationSeconds =
            Math.Max(
                aggregate.maximumDurationSeconds,
                duration
            );
    }

    internal void AddTrackedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        AddTrackedTemporaryMemory(
            name,
            stage,
            category,
            bytes,
            true
        );
    }

    internal void AddTrackedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes,
        bool recordTraceEvent
    )
    {
        if (completed || bytes <= 0L)
        {
            return;
        }

        currentTrackedTemporaryBytes += bytes;

        if (currentTrackedTemporaryBytes > peakTrackedTemporaryBytes)
        {
            peakTrackedTemporaryBytes =
                currentTrackedTemporaryBytes;
        }

        MutableTrackedMemory summary =
            GetOrCreateTrackedMemory(category);

        summary.currentBytes += bytes;

        if (summary.currentBytes > summary.peakBytes)
        {
            summary.peakBytes =
                summary.currentBytes;
        }

        if (
            recordTraceEvent
            &&
            level == TerrainRuntimeBakeDiagnosticsLevel.Trace
        )
        {
            memoryEvents.Add(
                new TerrainRuntimeBakeMemoryEvent(
                    name,
                    stage,
                    category,
                    bytes,
                    currentTrackedTemporaryBytes,
                    Math.Max(
                        0d,
                        EditorApplication.timeSinceStartup -
                        startedAtEditorTimeForDiagnostics
                    )
                )
            );
        }
    }

    internal void RemoveTrackedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
    )
    {
        RemoveTrackedTemporaryMemory(
            name,
            stage,
            category,
            bytes,
            true
        );
    }

    internal void RemoveTrackedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes,
        bool recordTraceEvent
    )
    {
        if (bytes <= 0L)
        {
            return;
        }

        currentTrackedTemporaryBytes =
            Math.Max(
                0L,
                currentTrackedTemporaryBytes -
                bytes
            );

        MutableTrackedMemory summary =
            GetOrCreateTrackedMemory(category);

        summary.currentBytes =
            Math.Max(
                0L,
                summary.currentBytes -
                bytes
            );

        if (
            !completed
            &&
            recordTraceEvent
            &&
            level == TerrainRuntimeBakeDiagnosticsLevel.Trace
        )
        {
            memoryEvents.Add(
                new TerrainRuntimeBakeMemoryEvent(
                    name,
                    stage,
                    category,
                    -bytes,
                    currentTrackedTemporaryBytes,
                    Math.Max(
                        0d,
                        EditorApplication.timeSinceStartup -
                        startedAtEditorTimeForDiagnostics
                    )
                )
            );
        }
    }

    internal void AttachPerformanceDataTo(
        TerrainRuntimeBakeDiagnosticsSnapshot snapshot
    )
    {
        if (snapshot == null)
        {
            return;
        }

        List<TerrainRuntimeBakeTrackedMemorySummary> summaries =
            new List<TerrainRuntimeBakeTrackedMemorySummary>();

        for (int index = 0; index < trackedMemoryByCategory.Count; index++)
        {
            MutableTrackedMemory value =
                trackedMemoryByCategory[index];

            summaries.Add(
                new TerrainRuntimeBakeTrackedMemorySummary(
                    value.category,
                    value.currentBytes,
                    value.peakBytes
                )
            );
        }

        List<TerrainRuntimeBakePerformanceAggregateRecord> aggregates =
            new List<TerrainRuntimeBakePerformanceAggregateRecord>();

        for (int index = 0; index < performanceAggregates.Count; index++)
        {
            MutablePerformanceAggregate value =
                performanceAggregates[index];

            if (value.sampleCount <= 0L)
            {
                continue;
            }

            aggregates.Add(
                new TerrainRuntimeBakePerformanceAggregateRecord(
                    value.name,
                    value.stage,
                    value.category,
                    value.sampleCount,
                    value.totalDurationSeconds,
                    value.minimumDurationSeconds,
                    value.maximumDurationSeconds
                )
            );
        }

        snapshot.AttachPerformanceData(
            performanceRecords,
            aggregates,
            summaries,
            memoryEvents,
            currentTrackedTemporaryBytes,
            peakTrackedTemporaryBytes
        );
    }

    internal double startedAtEditorTimeForDiagnostics =>
        startedAtEditorTime;

    private MutablePerformanceAggregate GetOrCreatePerformanceAggregate(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePerformanceCategory category
    )
    {
        for (int index = 0; index < performanceAggregates.Count; index++)
        {
            MutablePerformanceAggregate value =
                performanceAggregates[index];

            if (
                value.stage == stage
                &&
                value.category == category
                &&
                string.Equals(
                    value.name,
                    name,
                    StringComparison.Ordinal
                )
            )
            {
                return value;
            }
        }

        MutablePerformanceAggregate created =
            new MutablePerformanceAggregate
            {
                name = name ?? "",
                stage = stage,
                category = category
            };

        performanceAggregates.Add(
            created
        );

        return created;
    }

    private MutableRepeatedMemoryCounter GetOrCreateRepeatedMemoryCounter(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category
    )
    {
        for (int index = 0; index < repeatedMemoryCounters.Count; index++)
        {
            MutableRepeatedMemoryCounter value =
                repeatedMemoryCounters[index];

            if (
                value.stage == stage
                &&
                value.category == category
                &&
                string.Equals(
                    value.name,
                    name,
                    StringComparison.Ordinal
                )
            )
            {
                return value;
            }
        }

        MutableRepeatedMemoryCounter created =
            new MutableRepeatedMemoryCounter
            {
                name = name ?? "",
                stage = stage,
                category = category
            };

        repeatedMemoryCounters.Add(
            created
        );

        return created;
    }

    private static bool ShouldSampleRepeatedOccurrence(
        long occurrence
    )
    {
        return
            occurrence > 0L
            &&
            (
                occurrence <=
                    RepeatedTraceInitialSampleCount
                ||
                occurrence %
                    RepeatedTraceSampleStride ==
                    0L
            );
    }

    private MutableTrackedMemory GetOrCreateTrackedMemory(
        TerrainRuntimeBakeTrackedMemoryCategory category
    )
    {
        for (int index = 0; index < trackedMemoryByCategory.Count; index++)
        {
            if (trackedMemoryByCategory[index].category == category)
            {
                return trackedMemoryByCategory[index];
            }
        }

        MutableTrackedMemory created =
            new MutableTrackedMemory
            {
                category = category
            };

        trackedMemoryByCategory.Add(created);
        return created;
    }
}
