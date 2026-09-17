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

    private ReadOnlyCollection<TerrainRuntimeBakeTrackedMemorySummary>
        trackedMemorySummaries =
            new List<TerrainRuntimeBakeTrackedMemorySummary>().AsReadOnly();

    private ReadOnlyCollection<TerrainRuntimeBakeMemoryEvent>
        memoryEvents =
            new List<TerrainRuntimeBakeMemoryEvent>().AsReadOnly();

    public IReadOnlyList<TerrainRuntimeBakePerformanceRecord> PerformanceRecords =>
        performanceRecords;

    public IReadOnlyList<TerrainRuntimeBakeTrackedMemorySummary> TrackedMemorySummaries =>
        trackedMemorySummaries;

    public IReadOnlyList<TerrainRuntimeBakeMemoryEvent> MemoryEvents =>
        memoryEvents;

    public long CurrentTrackedTemporaryBytes { get; private set; }
    public long PeakTrackedTemporaryBytes { get; private set; }

    internal void AttachPerformanceData(
        IEnumerable<TerrainRuntimeBakePerformanceRecord> performanceRecords,
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
    private sealed class MutableTrackedMemory
    {
        public TerrainRuntimeBakeTrackedMemoryCategory category;
        public long currentBytes;
        public long peakBytes;
    }

    private readonly List<TerrainRuntimeBakePerformanceRecord>
        performanceRecords =
            new List<TerrainRuntimeBakePerformanceRecord>();

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

    internal void AddTrackedTemporaryMemory(
        string name,
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakeTrackedMemoryCategory category,
        long bytes
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

        if (level == TerrainRuntimeBakeDiagnosticsLevel.Trace)
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

        snapshot.AttachPerformanceData(
            performanceRecords,
            summaries,
            memoryEvents,
            currentTrackedTemporaryBytes,
            peakTrackedTemporaryBytes
        );
    }

    internal double startedAtEditorTimeForDiagnostics =>
        startedAtEditorTime;

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
