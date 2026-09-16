using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEditor;
using UnityEngine;

public enum TerrainRuntimeBakePlanSnapshotKind
{
    Initial,
    Height,
    Surface,
    Collision,
    Addressables,
    SceneSync,
    Final
}

public enum TerrainRuntimeBakeReasonOrigin
{
    Pipeline,
    Planner
}

public enum TerrainRuntimeBakeReasonTarget
{
    Run,
    Height,
    Surface,
    Collision,
    Addressables,
    SceneSync,
    Blocking
}

public enum TerrainRuntimeBakeReasonCode
{
    ExplicitRebuildAll,
    MissingRequiredAsset,
    AuthoringNotReady,
    InitialBake,
    MissingGeneratedData,
    OutdatedGeneratedData,
    GeneratedDataCurrent,
    PendingAuthoringChange,
    PendingSurfaceChange,
    PendingCollisionChange,
    AuthoringSignatureChanged,
    SurfaceSettingsChanged,
    CollisionSettingsChanged,
    TopologyChanged,
    LayoutChanged,
    CompilerVersionChanged,
    MissingGeneratedMetadata,
    InvalidGeneratedMetadata,
    FullRebuildFlag,
    InvalidPersistentDirtyState,
    NoProvableDirtySet,
    DependencyPropagation,
    DependencyPropagationFailed,
    AddressablesConfigurationDirty,
    AddressablesContentDirty,
    AddressablesRepairRequired,
    GeneratedDataChanged,
    RuntimeSceneMetadataDirty,
    BlockingValidation,
    PlanningFailed
}

public sealed class TerrainRuntimeBakeReasonRecord
{
    public TerrainRuntimeBakeReasonOrigin Origin { get; private set; }
    public TerrainRuntimeBakeReasonCode Code { get; private set; }
    public TerrainRuntimeBakeReasonTarget Target { get; private set; }
    public TerrainRuntimeBakeReasonTarget? SourceTarget { get; private set; }
    public string Message { get; private set; }
    public bool IsSafetyEscalation { get; private set; }
    public int AffectedItemCount { get; private set; }

    internal TerrainRuntimeBakeReasonRecord(
        TerrainRuntimeBakeReasonOrigin origin,
        TerrainRuntimeBakeReasonCode code,
        TerrainRuntimeBakeReasonTarget target,
        TerrainRuntimeBakeReasonTarget? sourceTarget,
        string message,
        bool isSafetyEscalation,
        int affectedItemCount
    )
    {
        Origin = origin;
        Code = code;
        Target = target;
        SourceTarget = sourceTarget;
        Message = message ?? "";
        IsSafetyEscalation = isSafetyEscalation;
        AffectedItemCount = Math.Max(0, affectedItemCount);
    }
}

public sealed class TerrainRuntimeBakePlanDiagnosticSnapshot
{
    private readonly ReadOnlyCollection<Vector2Int> heightTiles;
    private readonly ReadOnlyCollection<Vector2Int> surfaceTiles;
    private readonly ReadOnlyCollection<Vector2Int> collisionChunks;
    private readonly ReadOnlyCollection<string> safetyEscalationReasons;
    private readonly ReadOnlyCollection<TerrainRuntimeBakeReasonRecord> reasons;

    public TerrainRuntimeBakePlanSnapshotKind Kind { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }
    public double CapturedAtSeconds { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool CoordinatesCaptured { get; private set; }

    public TerrainRuntimeBakeWorkMode HeightWorkMode { get; private set; }
    public TerrainRuntimeBakeWorkMode SurfaceWorkMode { get; private set; }
    public TerrainRuntimeBakeWorkMode CollisionWorkMode { get; private set; }

    public IReadOnlyList<Vector2Int> HeightTiles => heightTiles;
    public IReadOnlyList<Vector2Int> SurfaceTiles => surfaceTiles;
    public IReadOnlyList<Vector2Int> CollisionChunks => collisionChunks;

    public int HeightTileCount { get; private set; }
    public int SurfaceTileCount { get; private set; }
    public int CollisionChunkCount { get; private set; }

    public bool AddressablesConfigurationRequired { get; private set; }
    public bool AddressablesContentBuildRequired { get; private set; }
    public bool RuntimeSceneMetadataUpdateRequired { get; private set; }
    public bool IsInitialBake { get; private set; }
    public bool IsBlocked { get; private set; }
    public string BlockReason { get; private set; }
    public bool HasWork { get; private set; }

    public IReadOnlyList<string> SafetyEscalationReasons =>
        safetyEscalationReasons;

    public long SourceStateRevision { get; private set; }
    public string CurrentAuthoringSignature { get; private set; }
    public string ObservedAuthoringSignature { get; private set; }
    public string CurrentSurfaceSettingsSignature { get; private set; }
    public string CurrentCollisionSettingsSignature { get; private set; }

    public IReadOnlyList<TerrainRuntimeBakeReasonRecord> Reasons => reasons;

    internal TerrainRuntimeBakePlanDiagnosticSnapshot(
        TerrainRuntimeBakePlanSnapshotKind kind,
        TerrainRuntimeBakePlan plan,
        bool captureCoordinates,
        IEnumerable<TerrainRuntimeBakeReasonRecord> reasons,
        DateTime capturedAtUtc,
        double capturedAtSeconds
    )
    {
        Kind = kind;
        CapturedAtUtc = capturedAtUtc;
        CapturedAtSeconds = Math.Max(0d, capturedAtSeconds);
        IsAvailable = plan != null;
        CoordinatesCaptured = captureCoordinates && plan != null;

        if (plan != null)
        {
            HeightWorkMode = plan.HeightWorkMode;
            SurfaceWorkMode = plan.SurfaceWorkMode;
            CollisionWorkMode = plan.CollisionWorkMode;

            HeightTileCount = plan.HeightTileCount;
            SurfaceTileCount = plan.SurfaceTileCount;
            CollisionChunkCount = plan.CollisionChunkCount;

            AddressablesConfigurationRequired =
                plan.AddressablesConfigurationRequired;

            AddressablesContentBuildRequired =
                plan.AddressablesContentBuildRequired;

            RuntimeSceneMetadataUpdateRequired =
                plan.RuntimeSceneMetadataUpdateRequired;

            IsInitialBake = plan.IsInitialBake;
            IsBlocked = plan.IsBlocked;
            BlockReason = plan.BlockReason ?? "";
            HasWork = plan.HasWork;
            SourceStateRevision = plan.SourceStateRevision;
            CurrentAuthoringSignature = plan.CurrentAuthoringSignature ?? "";
            ObservedAuthoringSignature = plan.ObservedAuthoringSignature ?? "";
            CurrentSurfaceSettingsSignature =
                plan.CurrentSurfaceSettingsSignature ?? "";
            CurrentCollisionSettingsSignature =
                plan.CurrentCollisionSettingsSignature ?? "";
        }
        else
        {
            HeightWorkMode = TerrainRuntimeBakeWorkMode.None;
            SurfaceWorkMode = TerrainRuntimeBakeWorkMode.None;
            CollisionWorkMode = TerrainRuntimeBakeWorkMode.None;
            BlockReason = "";
            CurrentAuthoringSignature = "";
            ObservedAuthoringSignature = "";
            CurrentSurfaceSettingsSignature = "";
            CurrentCollisionSettingsSignature = "";
        }

        heightTiles =
            CreateCoordinates(
                CoordinatesCaptured ? plan.HeightTiles : null
            );

        surfaceTiles =
            CreateCoordinates(
                CoordinatesCaptured ? plan.SurfaceTiles : null
            );

        collisionChunks =
            CreateCoordinates(
                CoordinatesCaptured ? plan.CollisionChunks : null
            );

        List<string> safety =
            plan != null
                ? new List<string>(plan.SafetyEscalationReasons)
                : new List<string>();

        safetyEscalationReasons = safety.AsReadOnly();

        List<TerrainRuntimeBakeReasonRecord> reasonList =
            reasons != null
                ? new List<TerrainRuntimeBakeReasonRecord>(reasons)
                : new List<TerrainRuntimeBakeReasonRecord>();

        this.reasons = reasonList.AsReadOnly();
    }

    private static ReadOnlyCollection<Vector2Int> CreateCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        List<Vector2Int> values =
            source != null
                ? new List<Vector2Int>(source)
                : new List<Vector2Int>();

        return values.AsReadOnly();
    }
}

public sealed partial class TerrainRuntimeBakeDiagnosticsSnapshot
{
    private ReadOnlyCollection<TerrainRuntimeBakePlanDiagnosticSnapshot>
        planSnapshots =
            new List<TerrainRuntimeBakePlanDiagnosticSnapshot>().AsReadOnly();

    private ReadOnlyCollection<TerrainRuntimeBakeReasonRecord>
        runReasons =
            new List<TerrainRuntimeBakeReasonRecord>().AsReadOnly();

    private bool planningDataAttached;

    public IReadOnlyList<TerrainRuntimeBakePlanDiagnosticSnapshot> PlanSnapshots =>
        planSnapshots;

    public IReadOnlyList<TerrainRuntimeBakeReasonRecord> RunReasons =>
        runReasons;

    public TerrainRuntimeBakePlanDiagnosticSnapshot GetPlanSnapshot(
        TerrainRuntimeBakePlanSnapshotKind kind
    )
    {
        for (int index = 0; index < planSnapshots.Count; index++)
        {
            TerrainRuntimeBakePlanDiagnosticSnapshot snapshot =
                planSnapshots[index];

            if (snapshot.Kind == kind)
            {
                return snapshot;
            }
        }

        return null;
    }

    internal void AttachPlanningData(
        IEnumerable<TerrainRuntimeBakePlanDiagnosticSnapshot> planSnapshots,
        IEnumerable<TerrainRuntimeBakeReasonRecord> runReasons
    )
    {
        if (planningDataAttached)
        {
            return;
        }

        List<TerrainRuntimeBakePlanDiagnosticSnapshot> plans =
            planSnapshots != null
                ? new List<TerrainRuntimeBakePlanDiagnosticSnapshot>(planSnapshots)
                : new List<TerrainRuntimeBakePlanDiagnosticSnapshot>();

        plans.Sort(
            (left, right) => left.Kind.CompareTo(right.Kind)
        );

        this.planSnapshots = plans.AsReadOnly();

        List<TerrainRuntimeBakeReasonRecord> reasons =
            runReasons != null
                ? new List<TerrainRuntimeBakeReasonRecord>(runReasons)
                : new List<TerrainRuntimeBakeReasonRecord>();

        this.runReasons = reasons.AsReadOnly();
        planningDataAttached = true;
    }
}

internal sealed partial class TerrainRuntimeBakeDiagnosticsSession
{
    private readonly List<TerrainRuntimeBakePlanDiagnosticSnapshot>
        planSnapshots =
            new List<TerrainRuntimeBakePlanDiagnosticSnapshot>();

    private readonly List<TerrainRuntimeBakeReasonRecord>
        runReasons =
            new List<TerrainRuntimeBakeReasonRecord>();

    private bool runIntentRecorded;

    internal TerrainRuntimeBakePlanningDiagnosticsContext BeginPlanningSnapshot(
        TerrainRuntimeBakePlanSnapshotKind kind,
        TerrainRuntimeBakePipelineState stage
    )
    {
        if (completed)
        {
            return null;
        }

        RecordRunIntentIfNeeded();

        return
            new TerrainRuntimeBakePlanningDiagnosticsContext(
                this,
                kind,
                stage
            );
    }

    internal void RecordPlanSnapshotAlias(
        TerrainRuntimeBakePlanSnapshotKind kind,
        TerrainRuntimeBakePlanSnapshotKind sourceKind,
        TerrainRuntimeBakePlan plan
    )
    {
        if (completed)
        {
            return;
        }

        IReadOnlyList<TerrainRuntimeBakeReasonRecord> sourceReasons =
            null;

        for (int index = 0; index < planSnapshots.Count; index++)
        {
            if (planSnapshots[index].Kind == sourceKind)
            {
                sourceReasons = planSnapshots[index].Reasons;
                break;
            }
        }

        RecordPlanSnapshot(
            kind,
            plan,
            sourceReasons
        );
    }

    internal void RecordPlanSnapshot(
        TerrainRuntimeBakePlanSnapshotKind kind,
        TerrainRuntimeBakePlan plan,
        IEnumerable<TerrainRuntimeBakeReasonRecord> reasons
    )
    {
        if (completed)
        {
            return;
        }

        for (int index = planSnapshots.Count - 1; index >= 0; index--)
        {
            if (planSnapshots[index].Kind == kind)
            {
                planSnapshots.RemoveAt(index);
            }
        }

        bool captureCoordinates =
            level == TerrainRuntimeBakeDiagnosticsLevel.Detailed
            ||
            level == TerrainRuntimeBakeDiagnosticsLevel.Trace;

        planSnapshots.Add(
            new TerrainRuntimeBakePlanDiagnosticSnapshot(
                kind,
                plan,
                captureCoordinates,
                reasons,
                DateTime.UtcNow,
                Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup -
                    startedAtEditorTime
                )
            )
        );
    }

    internal void AttachPlanningDataTo(
        TerrainRuntimeBakeDiagnosticsSnapshot snapshot
    )
    {
        snapshot?.AttachPlanningData(
            planSnapshots,
            runReasons
        );
    }

    private void RecordRunIntentIfNeeded()
    {
        if (runIntentRecorded)
        {
            return;
        }

        runIntentRecorded = true;

        if (mode == TerrainRuntimeBakePipelineMode.RebuildAll)
        {
            runReasons.Add(
                new TerrainRuntimeBakeReasonRecord(
                    TerrainRuntimeBakeReasonOrigin.Pipeline,
                    TerrainRuntimeBakeReasonCode.ExplicitRebuildAll,
                    TerrainRuntimeBakeReasonTarget.Run,
                    null,
                    "Rebuild All was explicitly requested. Pipeline stages may execute even when the reconciliation planner reports no pending work.",
                    false,
                    0
                )
            );
        }
    }
}

internal sealed class TerrainRuntimeBakePlanningDiagnosticsContext
{
    private readonly TerrainRuntimeBakeDiagnosticsSession session;
    private readonly TerrainRuntimeBakePlanSnapshotKind kind;
    private readonly TerrainRuntimeBakePipelineState stage;

    private readonly List<TerrainRuntimeBakeReasonRecord> reasons =
        new List<TerrainRuntimeBakeReasonRecord>();

    private bool completed;

    internal TerrainRuntimeBakePlanningDiagnosticsContext(
        TerrainRuntimeBakeDiagnosticsSession session,
        TerrainRuntimeBakePlanSnapshotKind kind,
        TerrainRuntimeBakePipelineState stage
    )
    {
        this.session = session;
        this.kind = kind;
        this.stage = stage;
    }

    internal TerrainRuntimeBakeTraceScope BeginTrace(
        string name
    )
    {
        return session?.BeginTrace(name, stage);
    }

    internal void AddPlannerReason(
        TerrainRuntimeBakeReasonCode code,
        TerrainRuntimeBakeReasonTarget target,
        string message,
        bool isSafetyEscalation = false,
        int affectedItemCount = 0,
        TerrainRuntimeBakeReasonTarget? sourceTarget = null
    )
    {
        if (completed)
        {
            return;
        }

        reasons.Add(
            new TerrainRuntimeBakeReasonRecord(
                TerrainRuntimeBakeReasonOrigin.Planner,
                code,
                target,
                sourceTarget,
                message,
                isSafetyEscalation,
                affectedItemCount
            )
        );
    }

    internal void Complete(
        TerrainRuntimeBakePlan plan
    )
    {
        if (completed)
        {
            return;
        }

        completed = true;
        session?.RecordPlanSnapshot(kind, plan, reasons);
    }

    internal void Fail(
        string message
    )
    {
        if (completed)
        {
            return;
        }

        AddPlannerReason(
            TerrainRuntimeBakeReasonCode.PlanningFailed,
            TerrainRuntimeBakeReasonTarget.Blocking,
            string.IsNullOrEmpty(message)
                ? "Runtime bake planning failed before a plan could be produced."
                : message
        );

        completed = true;
        session?.RecordPlanSnapshot(kind, null, reasons);
    }
}
