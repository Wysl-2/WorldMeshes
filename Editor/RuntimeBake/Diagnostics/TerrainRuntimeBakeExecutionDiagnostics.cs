using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

public sealed class TerrainRuntimeBakeStageExecutionDiagnostic
{
    private readonly ReadOnlyCollection<Vector2Int> requestedCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> succeededCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> failedCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> unprocessedCoordinates;

    public TerrainRuntimeBakePipelineState Stage { get; private set; }

    public int PlannedCount { get; private set; }
    public int RequestedCount { get; private set; }
    public int EvaluatedCount { get; private set; }
    public int GeneratedCount { get; private set; }
    public int CommittedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int UnchangedCount { get; private set; }
    public int FailedCount { get; private set; }

    public IReadOnlyList<Vector2Int> RequestedCoordinates => requestedCoordinates;
    public IReadOnlyList<Vector2Int> SucceededCoordinates => succeededCoordinates;
    public IReadOnlyList<Vector2Int> FailedCoordinates => failedCoordinates;
    public IReadOnlyList<Vector2Int> UnprocessedCoordinates => unprocessedCoordinates;

    public bool AddressablesConfigurationRequested { get; private set; }
    public bool AddressablesConfigurationPerformed { get; private set; }
    public bool AddressablesContentBuildRequested { get; private set; }
    public bool AddressablesContentBuildPerformed { get; private set; }
    public bool AddressablesMarkerWorkRequested { get; private set; }
    public bool AddressablesMarkerWorkPerformed { get; private set; }
    public int AddressablesMarkersRegenerated { get; private set; }
    public int AddressablesMarkersReused { get; private set; }
    public int AddressablesMarkersRemoved { get; private set; }

    public bool SceneMetadataRequested { get; private set; }
    public bool SceneMetadataPerformed { get; private set; }
    public bool SceneModificationPerformed { get; private set; }
    public int SceneSerializedComponentChangeCount { get; private set; }

    internal TerrainRuntimeBakeStageExecutionDiagnostic(
        TerrainRuntimeBakePipelineState stage,
        int plannedCount,
        int requestedCount,
        int evaluatedCount,
        int generatedCount,
        int committedCount,
        int skippedCount,
        int unchangedCount,
        int failedCount,
        IEnumerable<Vector2Int> requestedCoordinates,
        IEnumerable<Vector2Int> succeededCoordinates,
        IEnumerable<Vector2Int> failedCoordinates,
        IEnumerable<Vector2Int> unprocessedCoordinates,
        bool addressablesConfigurationRequested,
        bool addressablesConfigurationPerformed,
        bool addressablesContentBuildRequested,
        bool addressablesContentBuildPerformed,
        bool addressablesMarkerWorkRequested,
        bool addressablesMarkerWorkPerformed,
        int addressablesMarkersRegenerated,
        int addressablesMarkersReused,
        int addressablesMarkersRemoved,
        bool sceneMetadataRequested,
        bool sceneMetadataPerformed,
        bool sceneModificationPerformed,
        int sceneSerializedComponentChangeCount
    )
    {
        Stage = stage;
        PlannedCount = Mathf.Max(0, plannedCount);
        RequestedCount = Mathf.Max(0, requestedCount);
        EvaluatedCount = Mathf.Max(0, evaluatedCount);
        GeneratedCount = Mathf.Max(0, generatedCount);
        CommittedCount = Mathf.Max(0, committedCount);
        SkippedCount = Mathf.Max(0, skippedCount);
        UnchangedCount = Mathf.Max(0, unchangedCount);
        FailedCount = Mathf.Max(0, failedCount);

        this.requestedCoordinates = CopyCoordinates(requestedCoordinates);
        this.succeededCoordinates = CopyCoordinates(succeededCoordinates);
        this.failedCoordinates = CopyCoordinates(failedCoordinates);
        this.unprocessedCoordinates = CopyCoordinates(unprocessedCoordinates);

        AddressablesConfigurationRequested = addressablesConfigurationRequested;
        AddressablesConfigurationPerformed = addressablesConfigurationPerformed;
        AddressablesContentBuildRequested = addressablesContentBuildRequested;
        AddressablesContentBuildPerformed = addressablesContentBuildPerformed;
        AddressablesMarkerWorkRequested = addressablesMarkerWorkRequested;
        AddressablesMarkerWorkPerformed = addressablesMarkerWorkPerformed;
        AddressablesMarkersRegenerated = Mathf.Max(0, addressablesMarkersRegenerated);
        AddressablesMarkersReused = Mathf.Max(0, addressablesMarkersReused);
        AddressablesMarkersRemoved = Mathf.Max(0, addressablesMarkersRemoved);

        SceneMetadataRequested = sceneMetadataRequested;
        SceneMetadataPerformed = sceneMetadataPerformed;
        SceneModificationPerformed = sceneModificationPerformed;
        SceneSerializedComponentChangeCount = Mathf.Max(0, sceneSerializedComponentChangeCount);
    }

    private static ReadOnlyCollection<Vector2Int> CopyCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        List<Vector2Int> values =
            source != null
                ? new List<Vector2Int>(source)
                : new List<Vector2Int>();

        values.Sort(CompareCoordinates);
        return values.AsReadOnly();
    }

    private static int CompareCoordinates(Vector2Int left, Vector2Int right)
    {
        int yComparison = left.y.CompareTo(right.y);
        return yComparison != 0 ? yComparison : left.x.CompareTo(right.x);
    }
}

public sealed partial class TerrainRuntimeBakeDiagnosticsSnapshot
{
    private ReadOnlyCollection<TerrainRuntimeBakeStageExecutionDiagnostic>
        executionStages =
            new List<TerrainRuntimeBakeStageExecutionDiagnostic>().AsReadOnly();

    public IReadOnlyList<TerrainRuntimeBakeStageExecutionDiagnostic> ExecutionStages =>
        executionStages;

    internal void AttachExecutionData(
        IEnumerable<TerrainRuntimeBakeStageExecutionDiagnostic> stages
    )
    {
        executionStages =
            new List<TerrainRuntimeBakeStageExecutionDiagnostic>(
                stages ?? Array.Empty<TerrainRuntimeBakeStageExecutionDiagnostic>()
            )
            .AsReadOnly();
    }

    internal void AppendExecutionReport(StringBuilder builder)
    {
        if (builder == null || executionStages.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Execution Metrics:");

        for (int index = 0; index < executionStages.Count; index++)
        {
            TerrainRuntimeBakeStageExecutionDiagnostic stage =
                executionStages[index];

            builder.Append("  ");
            builder.Append(stage.Stage);
            builder.Append(": Planned ");
            builder.Append(stage.PlannedCount);
            builder.Append(", Requested ");
            builder.Append(stage.RequestedCount);
            builder.Append(", Evaluated ");
            builder.Append(stage.EvaluatedCount);
            builder.Append(", Generated ");
            builder.Append(stage.GeneratedCount);
            builder.Append(", Committed ");
            builder.Append(stage.CommittedCount);
            builder.Append(", Skipped ");
            builder.Append(stage.SkippedCount);
            builder.Append(", Unchanged ");
            builder.Append(stage.UnchangedCount);
            builder.Append(", Failed ");
            builder.AppendLine(stage.FailedCount.ToString());

            if (stage.Stage == TerrainRuntimeBakePipelineState.Addressables)
            {
                builder.AppendLine(
                    "    Configuration: requested=" +
                    stage.AddressablesConfigurationRequested +
                    ", performed=" +
                    stage.AddressablesConfigurationPerformed
                );
                builder.AppendLine(
                    "    Content Build: requested=" +
                    stage.AddressablesContentBuildRequested +
                    ", performed=" +
                    stage.AddressablesContentBuildPerformed
                );
                builder.AppendLine(
                    "    Marker Work: requested=" +
                    stage.AddressablesMarkerWorkRequested +
                    ", performed=" +
                    stage.AddressablesMarkerWorkPerformed +
                    ", regenerated=" +
                    stage.AddressablesMarkersRegenerated +
                    ", reused=" +
                    stage.AddressablesMarkersReused +
                    ", removed=" +
                    stage.AddressablesMarkersRemoved
                );
            }
            else if (stage.Stage == TerrainRuntimeBakePipelineState.SceneSync)
            {
                builder.AppendLine(
                    "    Metadata: requested=" +
                    stage.SceneMetadataRequested +
                    ", performed=" +
                    stage.SceneMetadataPerformed
                );
                builder.AppendLine(
                    "    Scene modifications: performed=" +
                    stage.SceneModificationPerformed +
                    ", serialized components changed=" +
                    stage.SceneSerializedComponentChangeCount
                );
            }
        }
    }
}

internal sealed partial class TerrainRuntimeBakeDiagnosticsSession
{
    private sealed class MutableExecutionStage
    {
        public TerrainRuntimeBakePipelineState stage;
        public int plannedCount;
        public int requestedCount;
        public int evaluatedCount;
        public int generatedCount;
        public int committedCount;
        public int skippedCount;
        public int unchangedCount;
        public int failedCount;

        public List<Vector2Int> requestedCoordinates;
        public List<Vector2Int> succeededCoordinates;
        public List<Vector2Int> failedCoordinates;
        public List<Vector2Int> unprocessedCoordinates;

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

    private readonly List<MutableExecutionStage> executionStages =
        new List<MutableExecutionStage>();

    internal void RecordExecutionPlan(
        TerrainRuntimeBakePipelineState stage,
        TerrainRuntimeBakePlan plan
    )
    {
        if (completed || plan == null)
        {
            return;
        }

        MutableExecutionStage record = GetOrCreateExecutionStage(stage);

        switch (stage)
        {
            case TerrainRuntimeBakePipelineState.Heightmaps:
                record.plannedCount = plan.HeightTileCount;
                break;

            case TerrainRuntimeBakePipelineState.SurfaceMasks:
                record.plannedCount = plan.SurfaceTileCount;
                break;

            case TerrainRuntimeBakePipelineState.Collision:
                record.plannedCount = plan.CollisionChunkCount;
                break;

            case TerrainRuntimeBakePipelineState.Addressables:
                record.addressablesConfigurationRequested =
                    plan.AddressablesConfigurationRequired;

                record.addressablesContentBuildRequested =
                    plan.AddressablesContentBuildRequired;

                record.addressablesMarkerWorkRequested =
                    plan.AddressablesConfigurationRequired;
                break;

            case TerrainRuntimeBakePipelineState.SceneSync:
                record.sceneMetadataRequested =
                    plan.RuntimeSceneMetadataUpdateRequired;
                break;
        }
    }

    internal void RecordHeightExecution(
        TerrainRuntimeHeightCompileResult result
    )
    {
        if (completed || result == null)
        {
            return;
        }

        MutableExecutionStage record =
            GetOrCreateExecutionStage(
                TerrainRuntimeBakePipelineState.Heightmaps
            );

        ApplyCoordinateResult(
            record,
            result.RequestedTileCount,
            result.SucceededTileCount,
            result.FailedTileCount,
            result.UnprocessedTileCount,
            result.CreatedTileCount + result.UpdatedTileCount,
            result.RequestedTiles,
            result.SucceededTiles,
            result.FailedTiles,
            result.UnprocessedTiles
        );
    }

    internal void RecordSurfaceExecution(
        TerrainSurfaceMaskGenerationResult result
    )
    {
        if (completed || result == null)
        {
            return;
        }

        MutableExecutionStage record =
            GetOrCreateExecutionStage(
                TerrainRuntimeBakePipelineState.SurfaceMasks
            );

        ApplyCoordinateResult(
            record,
            result.RequestedTileCount,
            result.SucceededTileCount,
            result.FailedTileCount,
            result.UnprocessedTileCount,
            result.CreatedTileCount + result.UpdatedTileCount,
            result.RequestedTiles,
            result.SucceededTiles,
            result.FailedTiles,
            result.UnprocessedTiles
        );
    }

    internal void RecordCollisionExecution(
        TerrainCollisionGenerationResult result
    )
    {
        if (completed || result == null)
        {
            return;
        }

        MutableExecutionStage record =
            GetOrCreateExecutionStage(
                TerrainRuntimeBakePipelineState.Collision
            );

        ApplyCoordinateResult(
            record,
            result.RequestedChunkCount,
            result.SucceededChunkCount,
            result.FailedChunkCount,
            result.UnprocessedChunkCount,
            result.CreatedMeshCount + result.UpdatedMeshCount,
            result.RequestedChunks,
            result.SucceededChunks,
            result.FailedChunks,
            result.UnprocessedChunks
        );
    }

    internal void RecordAddressablesExecution(
        TerrainRuntimeAddressablesResult result
    )
    {
        if (completed || result == null)
        {
            return;
        }

        MutableExecutionStage record =
            GetOrCreateExecutionStage(
                TerrainRuntimeBakePipelineState.Addressables
            );

        record.addressablesConfigurationRequested =
            result.ConfigurationWasRequired;

        record.addressablesConfigurationPerformed =
            result.ConfigurationPerformed;

        record.addressablesContentBuildRequested =
            result.ContentBuildWasRequired;

        record.addressablesContentBuildPerformed =
            result.ContentBuildPerformed;

        record.addressablesMarkersRegenerated =
            result.CollisionMarkersRegenerated;

        record.addressablesMarkersReused =
            result.CollisionMarkersReused;

        record.addressablesMarkersRemoved =
            result.CollisionMarkersRemoved;

        record.addressablesMarkerWorkPerformed =
            result.CollisionMarkersRegenerated > 0
            ||
            result.CollisionMarkersReused > 0
            ||
            result.CollisionMarkersRemoved > 0;

        record.requestedCount =
            (record.addressablesConfigurationRequested ? 1 : 0) +
            (record.addressablesContentBuildRequested ? 1 : 0) +
            (record.addressablesMarkerWorkRequested ? 1 : 0);

        record.evaluatedCount = record.requestedCount;

        record.committedCount =
            (record.addressablesConfigurationPerformed ? 1 : 0) +
            (record.addressablesContentBuildPerformed ? 1 : 0) +
            (record.addressablesMarkerWorkPerformed ? 1 : 0);

        record.generatedCount = record.committedCount;
        record.failedCount =
            IsAddressablesFailure(result.Outcome) ? 1 : 0;
    }

    internal void RecordSceneSyncExecution(
        TerrainRuntimeSceneSynchronizationResult result
    )
    {
        if (completed || result == null)
        {
            return;
        }

        MutableExecutionStage record =
            GetOrCreateExecutionStage(
                TerrainRuntimeBakePipelineState.SceneSync
            );

        record.sceneMetadataPerformed =
            result.PersistentSceneDirtyCleared
            ||
            result.BoundsApplied
            ||
            result.HeightStreamerSynchronized
            ||
            result.SurfaceManifestSynchronized
            ||
            result.CollisionStreamerSynchronized;

        record.sceneModificationPerformed =
            result.SceneMarkedDirty
            ||
            result.SerializedComponentChangeCount > 0;

        record.sceneSerializedComponentChangeCount =
            result.SerializedComponentChangeCount;

        record.requestedCount =
            record.sceneMetadataRequested ? 1 : 0;

        record.evaluatedCount =
            record.requestedCount;

        record.generatedCount =
            record.sceneMetadataPerformed ? 1 : 0;

        record.committedCount =
            record.sceneModificationPerformed || result.PersistentSceneDirtyCleared
                ? 1
                : 0;

        record.failedCount =
            IsSceneFailure(result.Outcome) ? 1 : 0;
    }

    internal void AttachExecutionDataTo(
        TerrainRuntimeBakeDiagnosticsSnapshot snapshot
    )
    {
        if (snapshot == null)
        {
            return;
        }

        List<TerrainRuntimeBakeStageExecutionDiagnostic> frozen =
            new List<TerrainRuntimeBakeStageExecutionDiagnostic>();

        for (int index = 0; index < executionStages.Count; index++)
        {
            MutableExecutionStage record = executionStages[index];

            frozen.Add(
                new TerrainRuntimeBakeStageExecutionDiagnostic(
                    record.stage,
                    record.plannedCount,
                    record.requestedCount,
                    record.evaluatedCount,
                    record.generatedCount,
                    record.committedCount,
                    record.skippedCount,
                    record.unchangedCount,
                    record.failedCount,
                    record.requestedCoordinates,
                    record.succeededCoordinates,
                    record.failedCoordinates,
                    record.unprocessedCoordinates,
                    record.addressablesConfigurationRequested,
                    record.addressablesConfigurationPerformed,
                    record.addressablesContentBuildRequested,
                    record.addressablesContentBuildPerformed,
                    record.addressablesMarkerWorkRequested,
                    record.addressablesMarkerWorkPerformed,
                    record.addressablesMarkersRegenerated,
                    record.addressablesMarkersReused,
                    record.addressablesMarkersRemoved,
                    record.sceneMetadataRequested,
                    record.sceneMetadataPerformed,
                    record.sceneModificationPerformed,
                    record.sceneSerializedComponentChangeCount
                )
            );
        }

        snapshot.AttachExecutionData(frozen);
    }

    private void ApplyCoordinateResult(
        MutableExecutionStage record,
        int requested,
        int succeeded,
        int failed,
        int unprocessed,
        int committed,
        IEnumerable<Vector2Int> requestedCoordinates,
        IEnumerable<Vector2Int> succeededCoordinates,
        IEnumerable<Vector2Int> failedCoordinates,
        IEnumerable<Vector2Int> unprocessedCoordinates
    )
    {
        record.requestedCount = Mathf.Max(0, requested);
        record.failedCount = Mathf.Max(0, failed);
        record.skippedCount = Mathf.Max(0, unprocessed);

        record.evaluatedCount =
            Mathf.Max(
                0,
                record.requestedCount -
                record.skippedCount
            );

        record.generatedCount =
            Mathf.Max(0, succeeded);

        record.committedCount =
            Mathf.Max(0, committed);

        /*
         * Current Height, Surface, and Collision compilers do not expose an
         * unchanged-output write-skip outcome. Keep this explicit instead of
         * guessing from aggregate counters. A future compiler optimization can
         * set this field from authoritative per-item state.
         */
        record.unchangedCount = 0;

        bool captureCoordinates =
            level == TerrainRuntimeBakeDiagnosticsLevel.Detailed
            ||
            level == TerrainRuntimeBakeDiagnosticsLevel.Trace;

        if (!captureCoordinates)
        {
            record.requestedCoordinates = null;
            record.succeededCoordinates = null;
            record.failedCoordinates = null;
            record.unprocessedCoordinates = null;
            return;
        }

        record.requestedCoordinates =
            requestedCoordinates != null
                ? new List<Vector2Int>(requestedCoordinates)
                : null;

        record.succeededCoordinates =
            succeededCoordinates != null
                ? new List<Vector2Int>(succeededCoordinates)
                : null;

        record.failedCoordinates =
            failedCoordinates != null
                ? new List<Vector2Int>(failedCoordinates)
                : null;

        record.unprocessedCoordinates =
            unprocessedCoordinates != null
                ? new List<Vector2Int>(unprocessedCoordinates)
                : null;
    }

    private MutableExecutionStage GetOrCreateExecutionStage(
        TerrainRuntimeBakePipelineState stage
    )
    {
        for (int index = 0; index < executionStages.Count; index++)
        {
            if (executionStages[index].stage == stage)
            {
                return executionStages[index];
            }
        }

        MutableExecutionStage created =
            new MutableExecutionStage
            {
                stage = stage
            };

        executionStages.Add(created);
        return created;
    }

    private static bool IsAddressablesFailure(
        TerrainRuntimeAddressablesOutcome outcome
    )
    {
        return
            outcome == TerrainRuntimeAddressablesOutcome.Failed
            ||
            outcome == TerrainRuntimeAddressablesOutcome.Blocked
            ||
            outcome == TerrainRuntimeAddressablesOutcome.StalePlan;
    }

    private static bool IsSceneFailure(
        TerrainRuntimeSceneSynchronizationOutcome outcome
    )
    {
        return
            outcome == TerrainRuntimeSceneSynchronizationOutcome.Failed
            ||
            outcome == TerrainRuntimeSceneSynchronizationOutcome.Blocked
            ||
            outcome == TerrainRuntimeSceneSynchronizationOutcome.RepairRequired;
    }
}
