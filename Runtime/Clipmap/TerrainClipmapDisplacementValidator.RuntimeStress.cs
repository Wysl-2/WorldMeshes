using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class TerrainClipmapDisplacementValidator
{
    [Header("Runtime Stress Certification")]

    [SerializeField]
    [Min(1)]
    private int continuousMovementStepsPerLeg =
        4;

    [SerializeField]
    [Min(1)]
    private int repeatedTransitionCycleCount =
        2;

    [SerializeField]
    [Min(1)]
    private int streamerLifecycleCycleCount =
        3;

    [SerializeField]
    [Min(1f)]
    private float resourceDrainTimeoutSeconds =
        20f;

    private TerrainRuntimeValidationStatus
        runtimeStressCertificationStatus =
            TerrainRuntimeValidationStatus.NotRun;

    private string runtimeStressCertificationSummary =
        "Full runtime stress certification has not been run.";

    private TerrainRuntimeStressCertificationResult
        runtimeStressCertificationResult;

    private bool runtimeStressOwnsStreamerState;
    private bool streamerWasEnabledBeforeStress;
    private bool runtimeStressCertificationActive;
    private bool runtimeStressCurrentSchedulerCaptured;

    private long runtimeStressHeightSourceUpperBoundBytes;
    private long runtimeStressSurfaceSourceUpperBoundBytes;

    public TerrainRuntimeValidationStatus
        RuntimeStressCertificationStatus =>
            runtimeStressCertificationStatus;

    public string RuntimeStressCertificationSummary =>
        runtimeStressCertificationSummary;

    public TerrainRuntimeStressCertificationResult
        RuntimeStressCertificationResult =>
            runtimeStressCertificationResult;

    [ContextMenu("Run Runtime Stress Certification")]
    public void BeginRuntimeStressCertification()
    {
        if (!CanBeginRuntimeValidation())
        {
            return;
        }

        runtimeStressCertificationStatus =
            TerrainRuntimeValidationStatus.Running;

        runtimeStressCertificationSummary =
            "Running boundary, movement, repeated-transition, resource-drain, and streamer lifecycle certification...";

        runtimeStressCertificationResult =
            new TerrainRuntimeStressCertificationResult
            {
                OverallStatus =
                    TerrainRuntimeValidationStatus.Running
            };

        validationRoutine =
            StartCoroutine(
                RunRuntimeStressCertificationRoutine()
            );
    }

    private IEnumerator RunRuntimeStressCertificationRoutine()
    {
        EnsureRuntimeValidationReferences();

        TerrainRuntimeStressCertificationResult result =
            runtimeStressCertificationResult;

        string failure = null;

        if (
            streamer == null
            || clipmapController == null
        )
        {
            failure =
                "Required runtime streaming components are unavailable.";
        }

        WorldSettings settings = null;
        TerrainHeightmapManifest heightManifest = null;

        if (
            failure == null
            && !streamer.TryGetRuntimeHeightConfigurationForInspection(
                out settings,
                out heightManifest
            )
        )
        {
            failure =
                "Runtime Height configuration is unavailable.";
        }

        if (
            failure == null
            && (
                !streamer.enabled
                || !streamer.RuntimeStreamingInitialized
            )
        )
        {
            failure =
                "The terrain streamer must be enabled and initialized before stress certification begins.";
        }

        TerrainRuntimeResidencyDiagnosticsSnapshot residency =
            default;

        if (
            failure == null
            && !streamer.TryGetRuntimeResidencyDiagnostics(
                out residency,
                out string residencyReason
            )
        )
        {
            failure =
                string.IsNullOrEmpty(residencyReason)
                    ? "Runtime residency diagnostics are unavailable."
                    : residencyReason;
        }

        Vector3 originalTarget =
            clipmapController != null
            && clipmapController.Target != null
                ? clipmapController.Target.position
                : transform.position;

        if (failure == null)
        {
            PopulateRuntimeStressConfiguration(
                result,
                settings,
                heightManifest,
                residency
            );

            runtimeStressHeightSourceUpperBoundBytes =
                residency.HeightSourceUpperBoundBytes;

            runtimeStressSurfaceSourceUpperBoundBytes =
                residency.Surface.EstimatedSourceUpperBoundBytes;

            TakeControllerOwnership();
            TakeRuntimeStressStreamerOwnership();

            yield return WaitForRuntimeSourceDrain(
                error => failure = error
            );
        }

        if (failure == null)
        {
            streamer.ResetHeightSchedulerValidationCounters();
            streamer.ResetHeightDeferredReleaseValidationCounters();
            runtimeStressCurrentSchedulerCaptured = false;
            runtimeStressCertificationActive = true;
        }

        TerrainClipmapLayoutApplier applier =
            null;

        if (failure == null)
        {
            applier =
                new TerrainClipmapLayoutApplier();

            applier.Configure(
                transform,
                settings
            );
        }

        if (failure == null)
        {
            string phaseFailure = null;

            yield return RunBoundaryStressPhase(
                settings,
                originalTarget.y,
                applier,
                result,
                error => phaseFailure = error
            );

            result.BoundaryStressStatus =
                PhaseStatus(
                    phaseFailure
                );

            failure = phaseFailure;
        }

        if (failure == null)
        {
            string phaseFailure = null;

            yield return RunContinuousMovementStressPhase(
                settings,
                originalTarget.y,
                applier,
                result,
                error => phaseFailure = error
            );

            result.ContinuousMovementStatus =
                PhaseStatus(
                    phaseFailure
                );

            failure = phaseFailure;
        }

        if (failure == null)
        {
            string phaseFailure = null;

            yield return RunRapidSupersessionStressPhase(
                settings,
                originalTarget.y,
                applier,
                result,
                error => phaseFailure = error
            );

            result.RapidSupersessionStatus =
                PhaseStatus(
                    phaseFailure
                );

            failure = phaseFailure;
        }

        if (failure == null)
        {
            string phaseFailure = null;

            yield return RunRepeatedTransitionStressPhase(
                settings,
                originalTarget.y,
                applier,
                result,
                error => phaseFailure = error
            );

            result.RepeatedTransitionStatus =
                PhaseStatus(
                    phaseFailure
                );

            failure = phaseFailure;
        }

        if (failure == null)
        {
            string lifecycleFailure = null;
            bool deferredTransferObserved = false;

            yield return RunStreamerLifecycleStressPhase(
                settings,
                originalTarget.y,
                applier,
                result,
                observed =>
                    deferredTransferObserved =
                        deferredTransferObserved
                        || observed,
                error => lifecycleFailure = error
            );

            result.StreamerLifecycleStatus =
                PhaseStatus(
                    lifecycleFailure
                );

            if (lifecycleFailure == null)
            {
                TerrainHeightDeferredReleaseDiagnosticsSnapshot
                    deferred =
                        streamer
                            .GetHeightDeferredReleaseDiagnostics();

                ObserveDeferredDiagnostics(
                    result,
                    deferred
                );

                if (deferred.ForcedReleaseCount != 0L)
                {
                    result.DeferredReleaseStatus =
                        TerrainRuntimeValidationStatus.Failed;

                    lifecycleFailure =
                        "Deferred Height sources required forced release during normal runtime stress certification.";
                }
                else if (
                    deferred.PendingCount != 0
                    || deferred.EnqueuedCount !=
                        deferred.ReleasedCount
                )
                {
                    result.DeferredReleaseStatus =
                        TerrainRuntimeValidationStatus.Failed;

                    lifecycleFailure =
                        "Deferred Height source ownership did not drain cleanly after lifecycle stress.";
                }
                else if (deferredTransferObserved)
                {
                    result.DeferredReleaseStatus =
                        TerrainRuntimeValidationStatus.Passed;
                }
                else
                {
                    result.DeferredReleaseStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    result.DeferredReleaseNote =
                        "No scheduler-owned GPU-safe pending source survived long enough to exercise scheduler-to-deferred ownership transfer.";
                }
            }
            else if (
                result.DeferredReleaseStatus ==
                TerrainRuntimeValidationStatus.NotRun
            )
            {
                result.DeferredReleaseStatus =
                    TerrainRuntimeValidationStatus.Failed;
            }

            failure = lifecycleFailure;
        }

        runtimeStressCertificationActive =
            false;

        string restoreFailure = null;

        if (
            runtimeStressOwnsStreamerState
            && streamer != null
            && streamerWasEnabledBeforeStress
            && !streamer.enabled
        )
        {
            streamer.enabled =
                true;

            if (
                !streamer.enabled
                || !streamer.RuntimeStreamingInitialized
            )
            {
                restoreFailure =
                    "The terrain streamer could not be re-enabled for final gameplay restoration.";
            }
            else
            {
                streamer.ResetHeightSchedulerValidationCounters();
                runtimeStressCurrentSchedulerCaptured = false;
            }
        }

        if (
            restoreFailure == null
            && streamer != null
            && streamer.enabled
            && settings != null
            && applier != null
        )
        {
            TerrainClipmapLayout restoreLayout =
                new TerrainClipmapLayout();

            if (
                !TerrainClipmapLayoutUtility.TryCalculateLayout(
                    settings,
                    originalTarget,
                    transform.position.y,
                    restoreLayout,
                    out restoreFailure
                )
            )
            {
                if (string.IsNullOrEmpty(restoreFailure))
                {
                    restoreFailure =
                        "Could not calculate the original gameplay clipmap layout for final restoration.";
                }
            }
            else
            {
                yield return PrepareAndActivateLayout(
                    restoreLayout,
                    applier,
                    error => restoreFailure = error
                );
            }

            if (restoreFailure == null)
            {
                yield return WaitForRuntimeSourceDrain(
                    error => restoreFailure = error
                );
            }

            if (
                restoreFailure == null
                && !TryValidateSettledRuntimeState(
                    result,
                    true,
                    out restoreFailure
                )
            )
            {
                // Failure populated by the helper.
            }
        }

        if (
            !runtimeStressCurrentSchedulerCaptured
            && streamer != null
            && streamer.TryGetHeightSchedulerDiagnostics(
                out TerrainHeightSchedulerDiagnosticsSnapshot finalScheduler
            )
        )
        {
            CaptureSchedulerLifetime(
                result,
                finalScheduler
            );

            runtimeStressCurrentSchedulerCaptured = true;
        }

        TerrainHeightDeferredReleaseDiagnosticsSnapshot finalDeferred =
            streamer != null
                ? streamer.GetHeightDeferredReleaseDiagnostics()
                : default;

        ObserveDeferredDiagnostics(
            result,
            finalDeferred
        );

        result.DeferredSourcesEnqueued =
            finalDeferred.EnqueuedCount;
        result.DeferredSourcesReleased =
            finalDeferred.ReleasedCount;
        result.DeferredSourcesForcedReleased =
            finalDeferred.ForcedReleaseCount;
        result.DeferredFenceFallbacks =
            finalDeferred.FenceFallbackCount;

        result.FinalRestoreStatus =
            restoreFailure == null
                ? TerrainRuntimeValidationStatus.Passed
                : TerrainRuntimeValidationStatus.Failed;

        if (
            failure == null
            && restoreFailure != null
        )
        {
            failure = restoreFailure;
        }

        if (
            failure == null
            && (
                result.PriorityViolations != 0
                || result.DuplicateStartViolations != 0
            )
        )
        {
            failure =
                "A Height scheduler diagnostic invariant failed during stress certification.";
        }

        result.FailureReason =
            failure ?? "";

        result.OverallStatus =
            DetermineRuntimeStressOverallStatus(
                result,
                failure
            );

        RestoreRuntimeStressOwnership();
        RestoreControllerOwnership();

        FinishRuntimeStressCertification(
            result
        );
    }

    private void PopulateRuntimeStressConfiguration(
        TerrainRuntimeStressCertificationResult result,
        WorldSettings settings,
        TerrainHeightmapManifest heightManifest,
        TerrainRuntimeResidencyDiagnosticsSnapshot residency
    )
    {
        result.WorldWidth =
            Mathf.Max(
                0f,
                settings.gridWidth * settings.chunkSize
            );

        result.WorldHeight =
            Mathf.Max(
                0f,
                settings.gridHeight * settings.chunkSize
            );

        result.ClipmapDiameter =
            TerrainClipmapTopologyUtility
                .CalculateClipmapDiameter(
                    settings
                );

        result.HeightLodCount =
            residency.HeightLodCount;

        result.MaximumHeightSampleStride =
            Mathf.Max(
                1,
                settings.heightStreamingMaximumStride
            );

        result.NativeHeightPageSamplesPerSide =
            heightManifest != null
                ? Mathf.Max(
                    0,
                    heightManifest.heightTileSamplesPerSide
                )
                : 0;

        result.HeightSourceUpperBoundBytes =
            residency.HeightSourceUpperBoundBytes;

        result.SurfaceCacheWidth =
            residency.Surface.CacheWidth;

        result.SurfaceCacheHeight =
            residency.Surface.CacheHeight;

        result.SurfaceSourceUpperBoundBytes =
            residency.Surface.EstimatedSourceUpperBoundBytes;
    }

    private IEnumerator RunBoundaryStressPhase(
        WorldSettings settings,
        float y,
        TerrainClipmapLayoutApplier applier,
        TerrainRuntimeStressCertificationResult result,
        Action<string> completed
    )
    {
        float worldX =
            Mathf.Max(
                1f,
                settings.gridWidth * settings.chunkSize
            );

        float worldZ =
            Mathf.Max(
                1f,
                settings.gridHeight * settings.chunkSize
            );

        Vector2[] positions =
        {
            new Vector2(worldX * 0.5f, worldZ * 0.5f),
            new Vector2(0f, 0f),
            new Vector2(worldX, 0f),
            new Vector2(worldX, worldZ),
            new Vector2(0f, worldZ),
            new Vector2(0f, worldZ * 0.5f),
            new Vector2(worldX, worldZ * 0.5f),
            new Vector2(worldX * 0.5f, 0f),
            new Vector2(worldX * 0.5f, worldZ)
        };

        for (
            int index = 0;
            index < positions.Length;
            index++
        )
        {
            if (
                !TryBuildStressLayout(
                    settings,
                    positions[index],
                    y,
                    out TerrainClipmapLayout layout,
                    out string layoutError
                )
            )
            {
                completed(layoutError);
                yield break;
            }

            string transitionFailure = null;

            yield return PrepareAndActivateLayout(
                layout,
                applier,
                error => transitionFailure = error
            );

            if (transitionFailure != null)
            {
                completed(transitionFailure);
                yield break;
            }

            if (
                !TryValidateSettledRuntimeState(
                    result,
                    false,
                    out string validationError
                )
            )
            {
                completed(validationError);
                yield break;
            }

            result.BoundaryLayoutsCompleted++;
        }

        completed(null);
    }

    private IEnumerator RunContinuousMovementStressPhase(
        WorldSettings settings,
        float y,
        TerrainClipmapLayoutApplier applier,
        TerrainRuntimeStressCertificationResult result,
        Action<string> completed
    )
    {
        float worldX =
            Mathf.Max(
                1f,
                settings.gridWidth * settings.chunkSize
            );

        float worldZ =
            Mathf.Max(
                1f,
                settings.gridHeight * settings.chunkSize
            );

        Vector2[] corners =
        {
            new Vector2(worldX * 0.15f, worldZ * 0.15f),
            new Vector2(worldX * 0.85f, worldZ * 0.15f),
            new Vector2(worldX * 0.85f, worldZ * 0.85f),
            new Vector2(worldX * 0.15f, worldZ * 0.85f),
            new Vector2(worldX * 0.15f, worldZ * 0.15f)
        };

        int steps =
            Mathf.Max(
                1,
                continuousMovementStepsPerLeg
            );

        for (
            int leg = 0;
            leg < corners.Length - 1;
            leg++
        )
        {
            for (
                int step = 1;
                step <= steps;
                step++
            )
            {
                float t =
                    step /
                    (float)steps;

                Vector2 position =
                    Vector2.Lerp(
                        corners[leg],
                        corners[leg + 1],
                        t
                    );

                if (
                    !TryBuildStressLayout(
                        settings,
                        position,
                        y,
                        out TerrainClipmapLayout layout,
                        out string layoutError
                    )
                )
                {
                    completed(layoutError);
                    yield break;
                }

                string transitionFailure = null;

                yield return PrepareAndActivateLayout(
                    layout,
                    applier,
                    error => transitionFailure = error
                );

                if (transitionFailure != null)
                {
                    completed(transitionFailure);
                    yield break;
                }

                if (
                    !TryValidateSettledRuntimeState(
                        result,
                        false,
                        out string validationError
                    )
                )
                {
                    completed(validationError);
                    yield break;
                }

                result.ContinuousMovementStepsCompleted++;
            }
        }

        completed(null);
    }

    private IEnumerator RunRapidSupersessionStressPhase(
        WorldSettings settings,
        float y,
        TerrainClipmapLayoutApplier applier,
        TerrainRuntimeStressCertificationResult result,
        Action<string> completed
    )
    {
        Vector2[] normalized =
        {
            new Vector2(0.15f, 0.15f),
            new Vector2(0.85f, 0.15f),
            new Vector2(0.85f, 0.85f),
            new Vector2(0.85f, 0.15f),
            new Vector2(0.15f, 0.15f),
            new Vector2(0.15f, 0.85f),
            new Vector2(0.15f, 0.15f)
        };

        List<TerrainClipmapLayout> layouts =
            new List<TerrainClipmapLayout>();

        for (
            int index = 0;
            index < normalized.Length;
            index++
        )
        {
            if (
                !TryBuildNormalizedStressLayout(
                    settings,
                    normalized[index],
                    y,
                    out TerrainClipmapLayout layout,
                    out string layoutError
                )
            )
            {
                completed(layoutError);
                yield break;
            }

            layouts.Add(layout);
        }

        for (
            int index = 0;
            index < layouts.Count;
            index++
        )
        {
            streamer.RequestCoverageForClipmapLayout(
                layouts[index]
            );

            result.RapidRequestsSubmitted++;

            if (
                !TryValidateRuntimeStressBounds(
                    result,
                    false,
                    out string boundsError
                )
            )
            {
                completed(boundsError);
                yield break;
            }

            yield return null;

            if (
                !TryValidateRuntimeStressBounds(
                    result,
                    false,
                    out boundsError
                )
            )
            {
                completed(boundsError);
                yield break;
            }
        }

        string transitionFailure = null;

        yield return PrepareAndActivateLayout(
            layouts[layouts.Count - 1],
            applier,
            error => transitionFailure = error
        );

        if (transitionFailure != null)
        {
            completed(transitionFailure);
            yield break;
        }

        if (
            !TryValidateSettledRuntimeState(
                result,
                false,
                out string validationError
            )
        )
        {
            completed(validationError);
            yield break;
        }

        completed(null);
    }

    private IEnumerator RunRepeatedTransitionStressPhase(
        WorldSettings settings,
        float y,
        TerrainClipmapLayoutApplier applier,
        TerrainRuntimeStressCertificationResult result,
        Action<string> completed
    )
    {
        Vector2[] normalized =
        {
            new Vector2(0.10f, 0.10f),
            new Vector2(0.90f, 0.90f),
            new Vector2(0.10f, 0.90f),
            new Vector2(0.90f, 0.10f)
        };

        List<TerrainClipmapLayout> layouts =
            new List<TerrainClipmapLayout>();

        for (
            int index = 0;
            index < normalized.Length;
            index++
        )
        {
            if (
                !TryBuildNormalizedStressLayout(
                    settings,
                    normalized[index],
                    y,
                    out TerrainClipmapLayout layout,
                    out string layoutError
                )
            )
            {
                completed(layoutError);
                yield break;
            }

            layouts.Add(layout);
        }

        int cycles =
            Mathf.Max(
                1,
                repeatedTransitionCycleCount
            );

        for (
            int cycle = 0;
            cycle < cycles;
            cycle++
        )
        {
            for (
                int index = 0;
                index < layouts.Count;
                index++
            )
            {
                string transitionFailure = null;

                yield return PrepareAndActivateLayout(
                    layouts[index],
                    applier,
                    error => transitionFailure = error
                );

                if (transitionFailure != null)
                {
                    completed(transitionFailure);
                    yield break;
                }

                if (
                    !TryValidateSettledRuntimeState(
                        result,
                        false,
                        out string validationError
                    )
                )
                {
                    completed(validationError);
                    yield break;
                }

                result.RepeatedTransitionsCompleted++;
            }
        }

        string drainFailure = null;

        yield return WaitForRuntimeSourceDrain(
            error => drainFailure = error
        );

        completed(drainFailure);
    }

    private IEnumerator RunStreamerLifecycleStressPhase(
        WorldSettings settings,
        float y,
        TerrainClipmapLayoutApplier applier,
        TerrainRuntimeStressCertificationResult result,
        Action<bool> deferredTransferObserved,
        Action<string> completed
    )
    {
        int cycles =
            Mathf.Max(
                1,
                streamerLifecycleCycleCount
            );

        Vector2[] beforeDisablePositions =
        {
            new Vector2(0.10f, 0.90f),
            new Vector2(0.90f, 0.10f)
        };

        Vector2[] afterEnablePositions =
        {
            new Vector2(0.15f, 0.15f),
            new Vector2(0.85f, 0.85f)
        };

        for (
            int cycle = 0;
            cycle < cycles;
            cycle++
        )
        {
            if (
                !TryBuildNormalizedStressLayout(
                    settings,
                    beforeDisablePositions[
                        cycle % beforeDisablePositions.Length
                    ],
                    y,
                    out TerrainClipmapLayout beforeDisableLayout,
                    out string beforeLayoutError
                )
            )
            {
                completed(beforeLayoutError);
                yield break;
            }

            string transitionFailure = null;

            yield return PrepareAndActivateLayout(
                beforeDisableLayout,
                applier,
                error => transitionFailure = error
            );

            if (transitionFailure != null)
            {
                completed(transitionFailure);
                yield break;
            }

            if (
                !TryValidateSettledRuntimeState(
                    result,
                    false,
                    out string settledError
                )
            )
            {
                completed(settledError);
                yield break;
            }

            if (
                !streamer.TryGetHeightSchedulerDiagnostics(
                    out TerrainHeightSchedulerDiagnosticsSnapshot beforeShutdown
                )
            )
            {
                completed(
                    "Height scheduler diagnostics were unavailable before streamer shutdown."
                );

                yield break;
            }

            ObserveSchedulerDiagnostics(
                result,
                beforeShutdown
            );

            CaptureSchedulerLifetime(
                result,
                beforeShutdown
            );

            runtimeStressCurrentSchedulerCaptured =
                true;

            int pendingBeforeShutdown =
                beforeShutdown.PendingGpuReleaseCount;

            int concurrencyLimit =
                Mathf.Max(
                    1,
                    beforeShutdown.ConcurrencyLimit
                );

            streamer.enabled =
                false;

            if (
                streamer.RuntimeStreamingInitialized
                || streamer.HeightLodRuntimeStateCount != 0
                || streamer.SurfaceCacheReady
                || streamer.ResidentSurfaceTileCount != 0
            )
            {
                completed(
                    "Streamer shutdown left runtime cache or source residency active."
                );

                yield break;
            }

            TerrainHeightDeferredReleaseDiagnosticsSnapshot
                deferredAfterShutdown =
                    streamer.GetHeightDeferredReleaseDiagnostics();

            ObserveDeferredDiagnostics(
                result,
                deferredAfterShutdown
            );

            if (pendingBeforeShutdown > 0)
            {
                deferredTransferObserved(true);

                if (deferredAfterShutdown.PendingCount <= 0)
                {
                    completed(
                        "Scheduler-owned GPU-safe Height releases were not transferred to deferred ownership during shutdown."
                    );

                    yield break;
                }

                if (
                    deferredAfterShutdown.PendingCount >
                        concurrencyLimit
                    || deferredAfterShutdown.EstimatedPendingSourceBytes >
                        runtimeStressHeightSourceUpperBoundBytes
                )
                {
                    completed(
                        "Deferred Height source ownership exceeded the configured transient-source bound."
                    );

                    yield break;
                }
            }

            string deferredDrainFailure = null;

            yield return WaitForDeferredReleaseDrain(
                result,
                error => deferredDrainFailure = error
            );

            if (deferredDrainFailure != null)
            {
                completed(deferredDrainFailure);
                yield break;
            }

            streamer.enabled =
                true;

            if (
                !streamer.enabled
                || !streamer.RuntimeStreamingInitialized
            )
            {
                completed(
                    "The terrain streamer did not reinitialize after being re-enabled."
                );

                yield break;
            }

            bool reacquireObserved =
                false;

            if (
                streamer.TryGetHeightSchedulerDiagnostics(
                    out TerrainHeightSchedulerDiagnosticsSnapshot initialScheduler
                )
            )
            {
                reacquireObserved =
                    initialScheduler.RequestsStarted > 0
                    || initialScheduler.ActiveLoadCount > 0
                    || (initialScheduler.QueuedRequiredCount + initialScheduler.QueuedPrefetchCount) > 0;
            }

            streamer.ResetHeightSchedulerValidationCounters();
            runtimeStressCurrentSchedulerCaptured = false;

            if (
                !TryBuildNormalizedStressLayout(
                    settings,
                    afterEnablePositions[
                        cycle % afterEnablePositions.Length
                    ],
                    y,
                    out TerrainClipmapLayout reacquireLayout,
                    out string reacquireLayoutError
                )
            )
            {
                completed(reacquireLayoutError);
                yield break;
            }

            transitionFailure = null;

            yield return PrepareAndActivateLayout(
                reacquireLayout,
                applier,
                error => transitionFailure = error
            );

            if (transitionFailure != null)
            {
                completed(transitionFailure);
                yield break;
            }

            if (
                streamer.TryGetHeightSchedulerDiagnostics(
                    out TerrainHeightSchedulerDiagnosticsSnapshot reacquireScheduler
                )
            )
            {
                ObserveSchedulerDiagnostics(
                    result,
                    reacquireScheduler
                );

                reacquireObserved =
                    reacquireObserved
                    || reacquireScheduler.RequestsStarted > 0
                    || reacquireScheduler.SourceUploadCount > 0;
            }

            if (!reacquireObserved)
            {
                completed(
                    "Streamer reinitialization did not observe Height Addressables reacquisition."
                );

                yield break;
            }

            if (streamer.ResidentSurfaceTileCount <= 0)
            {
                completed(
                    "Streamer reinitialization did not reacquire the active Surface source window."
                );

                yield break;
            }

            if (
                !TryValidateSettledRuntimeState(
                    result,
                    false,
                    out settledError
                )
            )
            {
                completed(settledError);
                yield break;
            }

            result.StreamerLifecycleCyclesCompleted++;
        }

        completed(null);
    }

    private bool TryValidateRuntimeStressBoundsIfActive(
        out string error
    )
    {
        error = null;

        if (
            !runtimeStressCertificationActive
            || runtimeStressCertificationResult == null
        )
        {
            return true;
        }

        return
            TryValidateRuntimeStressBounds(
                runtimeStressCertificationResult,
                false,
                out error
            );
    }

    private bool TryValidateRuntimeStressBounds(
        TerrainRuntimeStressCertificationResult result,
        bool allowDeferred,
        out string error
    )
    {
        error = null;

        if (
            !streamer.TryGetHeightSchedulerDiagnostics(
                out TerrainHeightSchedulerDiagnosticsSnapshot scheduler
            )
        )
        {
            error =
                "Height scheduler diagnostics are unavailable during runtime stress.";

            return false;
        }

        ObserveSchedulerDiagnostics(
            result,
            scheduler
        );

        if (
            scheduler.ActiveLoadCount >
                scheduler.ConcurrencyLimit
            || scheduler.TransientSourceSlotCount >
                scheduler.ConcurrencyLimit
            || scheduler.PeakActiveLoadCount >
                scheduler.ConcurrencyLimit
            || scheduler.PeakTransientSourceCount >
                scheduler.ConcurrencyLimit
        )
        {
            error =
                "Height scheduler exceeded its configured concurrency bound.";

            return false;
        }

        if (
            scheduler.EstimatedLogicalSourceBytes >
                runtimeStressHeightSourceUpperBoundBytes
            || scheduler.PeakEstimatedLogicalSourceBytes >
                runtimeStressHeightSourceUpperBoundBytes
        )
        {
            error =
                "Height logical source residency exceeded the configured runtime residency bound.";

            return false;
        }

        if (
            scheduler.PriorityViolationCount != 0
            || scheduler.DuplicateStartViolationCount != 0
        )
        {
            error =
                "Height scheduler priority or duplicate-start invariants failed.";

            return false;
        }

        if (
            !streamer.TryGetRuntimeResidencyDiagnostics(
                out TerrainRuntimeResidencyDiagnosticsSnapshot residency,
                out string residencyReason
            )
        )
        {
            error =
                string.IsNullOrEmpty(residencyReason)
                    ? "Runtime residency diagnostics are unavailable during stress."
                    : residencyReason;

            return false;
        }

        result.MaximumSurfaceResidentSources =
            Math.Max(
                result.MaximumSurfaceResidentSources,
                residency.Surface.ResidentSourceCount
            );

        result.MaximumSurfaceSourceBytes =
            Math.Max(
                result.MaximumSurfaceSourceBytes,
                residency.Surface.EstimatedCurrentSourceBytes
            );

        if (
            residency.Surface.EstimatedCurrentSourceBytes >
                runtimeStressSurfaceSourceUpperBoundBytes
        )
        {
            error =
                "Surface source residency exceeded its configured transition bound.";

            return false;
        }

        TerrainHeightDeferredReleaseDiagnosticsSnapshot deferred =
            streamer.GetHeightDeferredReleaseDiagnostics();

        ObserveDeferredDiagnostics(
            result,
            deferred
        );

        if (
            !allowDeferred
            && deferred.PendingCount != 0
        )
        {
            error =
                "Deferred Height releases were present during normal active-streamer stress.";

            return false;
        }

        return true;
    }

    private bool TryValidateSettledRuntimeState(
        TerrainRuntimeStressCertificationResult result,
        bool allowDeferred,
        out string error
    )
    {
        if (
            !TryValidateRuntimeStressBounds(
                result,
                allowDeferred,
                out error
            )
        )
        {
            return false;
        }

        if (streamer.MultiresolutionTransitionRunning)
        {
            error =
                "A terrain-cache transition was still running after the validation layout was expected to be settled.";

            return false;
        }

        if (streamer.PreparedMultiresolutionActivationPending)
        {
            error =
                "A prepared terrain-cache activation remained pending after the validation layout was expected to be settled.";

            return false;
        }

        if (
            streamer.SurfaceTransitionState !=
                TerrainSurfaceCacheTransitionState.Idle
        )
        {
            error =
                "Surface residency was not idle after the validation layout was fully settled.";

            return false;
        }

        int surfaceCapacity =
            Mathf.Max(
                0,
                streamer.SurfaceCacheWidth
            )
            *
            Mathf.Max(
                0,
                streamer.SurfaceCacheHeight
            );

        if (
            streamer.ResidentSurfaceTileCount >
                surfaceCapacity
        )
        {
            error =
                "Settled Surface source residency exceeded one active cache window.";

            return false;
        }

        if (
            !TryValidateCurrentMultiresolutionBindings(
                out string bindingSummary
            )
        )
        {
            error =
                bindingSummary;

            return false;
        }

        error = null;
        return true;
    }

    private IEnumerator WaitForRuntimeSourceDrain(
        Action<string> completed
    )
    {
        float started =
            Time.realtimeSinceStartup;

        while (true)
        {
            if (
                !streamer.TryGetHeightSchedulerDiagnostics(
                    out TerrainHeightSchedulerDiagnosticsSnapshot scheduler
                )
            )
            {
                completed(
                    "Height scheduler diagnostics became unavailable while waiting for runtime source drain."
                );

                yield break;
            }

            TerrainHeightDeferredReleaseDiagnosticsSnapshot deferred =
                streamer.GetHeightDeferredReleaseDiagnostics();

            if (
                scheduler.QueuedRequiredCount == 0
                && scheduler.QueuedPrefetchCount == 0
                && scheduler.ActiveLoadCount == 0
                && scheduler.ReadySourceCount == 0
                && scheduler.PendingGpuReleaseCount == 0
                && deferred.PendingCount == 0
            )
            {
                completed(null);
                yield break;
            }

            if (
                Time.realtimeSinceStartup - started >
                resourceDrainTimeoutSeconds
            )
            {
                completed(
                    "Timed out waiting for runtime source ownership to drain.\n" +
                    $"Queued Required: {scheduler.QueuedRequiredCount}\n" +
                    $"Queued Prefetch: {scheduler.QueuedPrefetchCount}\n" +
                    $"Active Loads: {scheduler.ActiveLoadCount}\n" +
                    $"Ready Sources: {scheduler.ReadySourceCount}\n" +
                    $"Scheduler Pending Releases: {scheduler.PendingGpuReleaseCount}\n" +
                    $"Deferred Pending Releases: {deferred.PendingCount}\n" +
                    $"Scheduler Source Bytes: {scheduler.EstimatedLogicalSourceBytes:N0}\n" +
                    $"Deferred Source Bytes: {deferred.EstimatedPendingSourceBytes:N0}"
                );

                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator WaitForDeferredReleaseDrain(
        TerrainRuntimeStressCertificationResult result,
        Action<string> completed
    )
    {
        float started =
            Time.realtimeSinceStartup;

        while (true)
        {
            TerrainHeightDeferredReleaseDiagnosticsSnapshot deferred =
                streamer.GetHeightDeferredReleaseDiagnostics();

            ObserveDeferredDiagnostics(
                result,
                deferred
            );

            if (deferred.PendingCount == 0)
            {
                completed(null);
                yield break;
            }

            if (
                deferred.EstimatedPendingSourceBytes >
                    runtimeStressHeightSourceUpperBoundBytes
            )
            {
                completed(
                    "Deferred Height source residency exceeded the configured transient-source bound."
                );

                yield break;
            }

            if (
                Time.realtimeSinceStartup - started >
                resourceDrainTimeoutSeconds
            )
            {
                completed(
                    "Timed out waiting for GPU-safe deferred Height source releases to drain.\n" +
                    $"Pending Releases: {deferred.PendingCount}\n" +
                    $"Pending Source Bytes: {deferred.EstimatedPendingSourceBytes:N0}"
                );

                yield break;
            }

            yield return null;
        }
    }

    private bool TryBuildNormalizedStressLayout(
        WorldSettings settings,
        Vector2 normalized,
        float y,
        out TerrainClipmapLayout layout,
        out string error
    )
    {
        float worldX =
            Mathf.Max(
                1f,
                settings.gridWidth * settings.chunkSize
            );

        float worldZ =
            Mathf.Max(
                1f,
                settings.gridHeight * settings.chunkSize
            );

        Vector2 position =
            new Vector2(
                worldX * Mathf.Clamp01(normalized.x),
                worldZ * Mathf.Clamp01(normalized.y)
            );

        return
            TryBuildStressLayout(
                settings,
                position,
                y,
                out layout,
                out error
            );
    }

    private bool TryBuildStressLayout(
        WorldSettings settings,
        Vector2 position,
        float y,
        out TerrainClipmapLayout layout,
        out string error
    )
    {
        layout =
            new TerrainClipmapLayout();

        error = null;

        if (
            !TerrainClipmapLayoutUtility.TryCalculateLayout(
                settings,
                new Vector3(
                    position.x,
                    y,
                    position.y
                ),
                transform.position.y,
                layout,
                out error
            )
        )
        {
            if (string.IsNullOrEmpty(error))
            {
                error =
                    "Could not calculate a runtime stress-test clipmap layout.";
            }

            return false;
        }

        return true;
    }

    private static TerrainRuntimeValidationStatus PhaseStatus(
        string failure
    )
    {
        return
            failure == null
                ? TerrainRuntimeValidationStatus.Passed
                : TerrainRuntimeValidationStatus.Failed;
    }

    private void ObserveSchedulerDiagnostics(
        TerrainRuntimeStressCertificationResult result,
        TerrainHeightSchedulerDiagnosticsSnapshot scheduler
    )
    {
        result.PeakActiveHeightLoads =
            Math.Max(
                result.PeakActiveHeightLoads,
                Math.Max(
                    scheduler.ActiveLoadCount,
                    scheduler.PeakActiveLoadCount
                )
            );

        result.PeakHeightTransientSources =
            Math.Max(
                result.PeakHeightTransientSources,
                Math.Max(
                    scheduler.TransientSourceSlotCount,
                    scheduler.PeakTransientSourceCount
                )
            );

        result.PeakHeightSourceBytes =
            Math.Max(
                result.PeakHeightSourceBytes,
                Math.Max(
                    scheduler.EstimatedLogicalSourceBytes,
                    scheduler.PeakEstimatedLogicalSourceBytes
                )
            );
    }

    private void CaptureSchedulerLifetime(
        TerrainRuntimeStressCertificationResult result,
        TerrainHeightSchedulerDiagnosticsSnapshot scheduler
    )
    {
        ObserveSchedulerDiagnostics(
            result,
            scheduler
        );

        result.HeightRequestsStarted +=
            scheduler.RequestsStarted;
        result.HeightSourceUploads +=
            scheduler.SourceUploadCount;
        result.HeightCacheToCacheReuses +=
            scheduler.CacheToCacheReuseCount;
        result.RepeatedPlanSkips +=
            scheduler.RepeatedPlanSubmissionSkipCount;
        result.CoalescedRequests +=
            scheduler.CoalescedRequestCount;
        result.PrefetchPromotions +=
            scheduler.PrefetchPromotedToRequiredCount;
        result.StaleQueuedDiscards +=
            scheduler.StaleQueuedRequestDiscardCount;
        result.StaleCompletedDiscards +=
            scheduler.StaleCompletedSourceDiscardCount;
        result.PriorityViolations +=
            scheduler.PriorityViolationCount;
        result.DuplicateStartViolations +=
            scheduler.DuplicateStartViolationCount;
    }

    private void ObserveDeferredDiagnostics(
        TerrainRuntimeStressCertificationResult result,
        TerrainHeightDeferredReleaseDiagnosticsSnapshot deferred
    )
    {
        result.PeakDeferredReleaseCount =
            Math.Max(
                result.PeakDeferredReleaseCount,
                Math.Max(
                    deferred.PendingCount,
                    deferred.PeakPendingCount
                )
            );

        result.PeakDeferredReleaseBytes =
            Math.Max(
                result.PeakDeferredReleaseBytes,
                Math.Max(
                    deferred.EstimatedPendingSourceBytes,
                    deferred.PeakEstimatedPendingSourceBytes
                )
            );
    }

    private void TakeRuntimeStressStreamerOwnership()
    {
        if (
            streamer == null
            || runtimeStressOwnsStreamerState
        )
        {
            return;
        }

        streamerWasEnabledBeforeStress =
            streamer.enabled;

        runtimeStressOwnsStreamerState =
            true;
    }

    private void RestoreRuntimeStressOwnership()
    {
        runtimeStressCertificationActive =
            false;

        if (!runtimeStressOwnsStreamerState)
        {
            return;
        }

        if (
            streamer != null
            && streamer.enabled !=
                streamerWasEnabledBeforeStress
        )
        {
            streamer.enabled =
                streamerWasEnabledBeforeStress;
        }

        runtimeStressOwnsStreamerState =
            false;
        runtimeStressCurrentSchedulerCaptured =
            false;
    }

    private TerrainRuntimeValidationStatus DetermineRuntimeStressOverallStatus(
        TerrainRuntimeStressCertificationResult result,
        string failure
    )
    {
        if (
            failure != null
            || result.BoundaryStressStatus == TerrainRuntimeValidationStatus.Failed
            || result.ContinuousMovementStatus == TerrainRuntimeValidationStatus.Failed
            || result.RapidSupersessionStatus == TerrainRuntimeValidationStatus.Failed
            || result.RepeatedTransitionStatus == TerrainRuntimeValidationStatus.Failed
            || result.DeferredReleaseStatus == TerrainRuntimeValidationStatus.Failed
            || result.StreamerLifecycleStatus == TerrainRuntimeValidationStatus.Failed
            || result.FinalRestoreStatus == TerrainRuntimeValidationStatus.Failed
        )
        {
            return
                TerrainRuntimeValidationStatus.Failed;
        }

        if (
            result.DeferredReleaseStatus ==
                TerrainRuntimeValidationStatus.Inconclusive
        )
        {
            return
                TerrainRuntimeValidationStatus.Inconclusive;
        }

        return
            TerrainRuntimeValidationStatus.Passed;
    }

    private void FinishRuntimeStressCertification(
        TerrainRuntimeStressCertificationResult result
    )
    {
        runtimeStressCertificationResult =
            result;

        runtimeStressCertificationStatus =
            result != null
                ? result.OverallStatus
                : TerrainRuntimeValidationStatus.Failed;

        runtimeStressCertificationSummary =
            result != null
                ? result.BuildDiagnosticReport()
                : "Runtime stress certification did not produce a result.";

        validationRoutine =
            null;

        LogRuntimeValidationResult(
            "Runtime terrain stress certification",
            runtimeStressCertificationStatus,
            runtimeStressCertificationSummary
        );
    }
}
