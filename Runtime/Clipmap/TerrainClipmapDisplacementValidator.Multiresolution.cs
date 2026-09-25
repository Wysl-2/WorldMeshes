using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class TerrainClipmapDisplacementValidator
{
    [Header("MRH07 Validation")]

    [SerializeField]
    [Min(0f)]
    private float multiresolutionBindingTolerance =
        0.0001f;

    [SerializeField]
    [Min(1f)]
    private float stressValidationTimeoutSeconds =
        120f;

    private Coroutine mrh07ValidationRoutine;

    private TerrainRuntimeValidationStatus
        multiresolutionValidationStatus =
            TerrainRuntimeValidationStatus.NotRun;

    private TerrainRuntimeValidationStatus
        independentAnchorValidationStatus =
            TerrainRuntimeValidationStatus.NotRun;

    private TerrainRuntimeValidationStatus
        schedulerStressValidationStatus =
            TerrainRuntimeValidationStatus.NotRun;

    private string multiresolutionValidationSummary =
        "Renderer/displacement validation has not been run.";

    private string independentAnchorValidationSummary =
        "Independent-anchor stress validation has not been run.";

    private string schedulerStressValidationSummary =
        "Height scheduler stress validation has not been run.";

    private bool mrh07StressOwnsController;
    private bool mrh07ControllerWasEnabled;

    private static readonly int
        Mrh07HeightNormalSampleSpacingFinePropertyId =
            Shader.PropertyToID(
                "_HeightNormalSampleSpacingFine"
            );

    private static readonly int
        Mrh07HeightNormalSampleSpacingCoarsePropertyId =
            Shader.PropertyToID(
                "_HeightNormalSampleSpacingCoarse"
            );

    public TerrainRuntimeValidationStatus
        MultiresolutionValidationStatus =>
            multiresolutionValidationStatus;

    public TerrainRuntimeValidationStatus
        IndependentAnchorValidationStatus =>
            independentAnchorValidationStatus;

    public TerrainRuntimeValidationStatus
        SchedulerStressValidationStatus =>
            schedulerStressValidationStatus;

    public string MultiresolutionValidationSummary =>
        multiresolutionValidationSummary;

    public string IndependentAnchorValidationSummary =>
        independentAnchorValidationSummary;

    public string SchedulerStressValidationSummary =>
        schedulerStressValidationSummary;

    public bool IsMrh07ValidationRunning =>
        mrh07ValidationRoutine != null;

    [ContextMenu("Validate Multiresolution Renderer Bindings")]
    public void BeginMultiresolutionValidation()
    {
        if (!CanBeginMrh07Validation())
        {
            return;
        }

        multiresolutionValidationStatus =
            TerrainRuntimeValidationStatus.Running;

        multiresolutionValidationSummary =
            "Validating semantic renderer roles and multiresolution shader bindings...";

        mrh07ValidationRoutine =
            StartCoroutine(
                ValidateMultiresolutionBindingsRoutine()
            );
    }

    [ContextMenu("Run Independent Anchor Stress Validation")]
    public void BeginIndependentAnchorStressValidation()
    {
        if (!CanBeginMrh07Validation())
        {
            return;
        }

        independentAnchorValidationStatus =
            TerrainRuntimeValidationStatus.Running;

        independentAnchorValidationSummary =
            "Searching for and validating an independently snapped clipmap layout...";

        mrh07ValidationRoutine =
            StartCoroutine(
                RunIndependentAnchorStressRoutine()
            );
    }

    [ContextMenu("Run Height Scheduler Stress Validation")]
    public void BeginSchedulerStressValidation()
    {
        if (!CanBeginMrh07Validation())
        {
            return;
        }

        schedulerStressValidationStatus =
            TerrainRuntimeValidationStatus.Running;

        schedulerStressValidationSummary =
            "Running deterministic normal/rapid/teleport Height scheduler stress...";

        mrh07ValidationRoutine =
            StartCoroutine(
                RunSchedulerStressRoutine()
            );
    }

    internal void CancelMrh07Validation()
    {
        if (mrh07ValidationRoutine != null)
        {
            StopCoroutine(
                mrh07ValidationRoutine
            );

            mrh07ValidationRoutine = null;
        }

        RestoreControllerOwnership();
    }

    private bool CanBeginMrh07Validation()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "MRH07 runtime validation can only run in Play Mode.",
                this
            );

            return false;
        }

        if (
            validationRoutine != null
            || mrh07ValidationRoutine != null
        )
        {
            return false;
        }

        EnsureMrh07References();

        if (
            streamer == null
            || clipmapController == null
        )
        {
            Debug.LogError(
                "MRH07 runtime validation requires TerrainHeightmapStreamer and TerrainClipmapController on the same GameObject.",
                this
            );

            return false;
        }

        return true;
    }

    private void EnsureMrh07References()
    {
        if (streamer == null)
        {
            streamer =
                GetComponent<TerrainHeightmapStreamer>();
        }

        if (boundsController == null)
        {
            boundsController =
                GetComponent<TerrainClipmapBoundsController>();
        }

        if (clipmapController == null)
        {
            clipmapController =
                GetComponent<TerrainClipmapController>();
        }
    }

    private IEnumerator ValidateMultiresolutionBindingsRoutine()
    {
        yield return null;

        bool passed =
            TryValidateCurrentMultiresolutionBindings(
                out string summary
            );

        multiresolutionValidationStatus =
            passed
                ? TerrainRuntimeValidationStatus.Passed
                : TerrainRuntimeValidationStatus.Failed;

        multiresolutionValidationSummary =
            summary;

        mrh07ValidationRoutine = null;

        if (passed)
        {
            Debug.Log(
                "Multiresolution renderer/displacement validation passed.\n\n" +
                summary,
                this
            );
        }
        else
        {
            Debug.LogError(
                "Multiresolution renderer/displacement validation FAILED.\n\n" +
                summary,
                this
            );
        }
    }

    private bool TryValidateCurrentMultiresolutionBindings(
        out string summary
    )
    {
        summary = "";

        EnsureMrh07References();

        if (
            streamer == null
            || !streamer.TryGetRuntimeHeightConfigurationForInspection(
                out WorldSettings settings,
                out _
            )
        )
        {
            summary =
                "Runtime Height configuration is unavailable.";

            return false;
        }

        TerrainClipmapLayoutApplier applier =
            new TerrainClipmapLayoutApplier();

        applier.Configure(
            transform,
            settings
        );

        List<TerrainClipmapRendererBinding> bindings =
            new List<TerrainClipmapRendererBinding>();

        if (
            !applier.TryGetValidatedRendererBindings(
                bindings,
                out string bindingError
            )
        )
        {
            summary = bindingError;
            return false;
        }

        int centers = 0;
        int rings = 0;
        int stitches = 0;
        int transitionVertices = 0;
        int divergentAdjacentAnchors = 0;

        MaterialPropertyBlock block =
            new MaterialPropertyBlock();

        for (
            int index = 0;
            index < bindings.Count;
            index++
        )
        {
            TerrainClipmapRendererBinding binding =
                bindings[index];

            TerrainClipmapRendererRole role =
                binding.Role;

            int ownerLevel =
                role.HeightOwnerLevel;

            if (
                !streamer.TryGetHeightLodDiagnostics(
                    ownerLevel,
                    out TerrainHeightLodDiagnosticsSnapshot owner
                )
            )
            {
                summary =
                    $"Renderer '{binding.Renderer.name}' has no diagnostics snapshot for Height owner LOD{ownerLevel}.";

                return false;
            }

            binding.Renderer.GetPropertyBlock(
                block
            );

            Texture boundTexture =
                block.GetTexture(
                    HeightCachePropertyId
                );

            if (
                !(boundTexture is Texture2DArray boundCache)
                || boundCache == null
            )
            {
                summary =
                    $"Renderer '{binding.Renderer.name}' has no Texture2DArray _HeightCache binding.";

                return false;
            }

            if (
                !TryGetOwnerCacheReference(
                    ownerLevel,
                    out Texture2DArray expectedCache
                )
                || boundCache != expectedCache
            )
            {
                summary =
                    $"Renderer '{binding.Renderer.name}' does not reference its semantic Height owner LOD{ownerLevel} cache.";

                return false;
            }

            Vector4 origin =
                block.GetVector(
                    HeightCacheOriginTilePropertyId
                );

            Vector4 size =
                block.GetVector(
                    HeightCacheSizePropertyId
                );

            float samplesPerSide =
                block.GetFloat(
                    HeightTileSamplesPerSidePropertyId
                );

            float sampleSpacing =
                block.GetFloat(
                    HeightSampleSpacingPropertyId
                );

            float ready =
                block.GetFloat(
                    HeightCacheReadyPropertyId
                );

            if (
                Mathf.Abs(origin.x - owner.CacheOrigin.x) >
                    multiresolutionBindingTolerance
                || Mathf.Abs(origin.y - owner.CacheOrigin.y) >
                    multiresolutionBindingTolerance
                || Mathf.Abs(size.x - owner.CacheWidth) >
                    multiresolutionBindingTolerance
                || Mathf.Abs(size.y - owner.CacheHeight) >
                    multiresolutionBindingTolerance
                || Mathf.Abs(samplesPerSide - owner.SamplesPerSide) >
                    multiresolutionBindingTolerance
                || Mathf.Abs(sampleSpacing - owner.SampleSpacing) >
                    multiresolutionBindingTolerance
                || ready < 0.5f
            )
            {
                summary =
                    $"Renderer '{binding.Renderer.name}' has Height cache metadata that does not match owner LOD{ownerLevel}.";

                return false;
            }

            float fineNormalSpacing =
                block.GetFloat(
                    Mrh07HeightNormalSampleSpacingFinePropertyId
                );

            float coarseNormalSpacing =
                block.GetFloat(
                    Mrh07HeightNormalSampleSpacingCoarsePropertyId
                );

            float expectedCoarseNormalSpacing =
                owner.SampleSpacing;

            if (
                role.Kind ==
                    TerrainClipmapRendererKind.Stitch
            )
            {
                if (
                    !streamer.TryGetHeightLodDiagnostics(
                        role.CoarseLevel,
                        out TerrainHeightLodDiagnosticsSnapshot coarse
                    )
                )
                {
                    summary =
                        $"Stitch '{binding.Renderer.name}' has no coarse LOD{role.CoarseLevel} diagnostics state.";

                    return false;
                }

                expectedCoarseNormalSpacing =
                    coarse.SampleSpacing;
            }

            if (
                Mathf.Abs(
                    fineNormalSpacing - owner.SampleSpacing
                ) > multiresolutionBindingTolerance
                || Mathf.Abs(
                    coarseNormalSpacing - expectedCoarseNormalSpacing
                ) > multiresolutionBindingTolerance
            )
            {
                summary =
                    $"Renderer '{binding.Renderer.name}' has invalid fine/coarse normal sample spacing bindings.";

                return false;
            }

            switch (role.Kind)
            {
                case TerrainClipmapRendererKind.Center:
                    centers++;
                    break;

                case TerrainClipmapRendererKind.Ring:
                    rings++;
                    break;

                case TerrainClipmapRendererKind.Stitch:
                    stitches++;

                    if (
                        !ValidateStitchRenderer(
                            applier,
                            binding,
                            block,
                            ref transitionVertices,
                            ref divergentAdjacentAnchors,
                            out string stitchError
                        )
                    )
                    {
                        summary = stitchError;
                        return false;
                    }
                    break;
            }
        }

        summary =
            $"Renderers Validated: {bindings.Count}\n" +
            $"Centers: {centers}\n" +
            $"Rings: {rings}\n" +
            $"Stitches: {stitches}\n" +
            $"Stitch Transition Vertices: {transitionVertices:N0}\n" +
            $"Divergent Adjacent Anchors Observed: {divergentAdjacentAnchors}";

        return true;
    }

    private bool TryGetOwnerCacheReference(
        int level,
        out Texture2DArray cache
    )
    {
        cache = null;

        if (
            !streamer.TryBeginMultiresolutionCacheInspection(
                out _
            )
        )
        {
            return false;
        }

        try
        {
            if (
                !streamer.TryGetHeightLodInspectionSnapshot(
                    level,
                    out TerrainHeightLodInspectionSnapshot snapshot
                )
            )
            {
                return false;
            }

            cache = snapshot.ActiveCache;
            return cache != null;
        }
        finally
        {
            streamer.EndCacheInspection();
        }
    }

    private bool ValidateStitchRenderer(
        TerrainClipmapLayoutApplier applier,
        TerrainClipmapRendererBinding binding,
        MaterialPropertyBlock block,
        ref int transitionVertices,
        ref int divergentAdjacentAnchors,
        out string error
    )
    {
        error = null;

        TerrainClipmapRendererRole role =
            binding.Role;

        if (
            role.HeightOwnerLevel != role.FineLevel
            || role.CoarseLevel != role.FineLevel + 1
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' does not use finer-LOD Height ownership.";

            return false;
        }

        bool fineAnchorValid =
            applier.TryGetAppliedLODAnchor(
                role.FineLevel,
                out Vector3 fineAnchor,
                out string fineError
            );

        bool coarseAnchorValid =
            applier.TryGetAppliedLODAnchor(
                role.CoarseLevel,
                out Vector3 coarseAnchor,
                out string coarseError
            );

        if (
            !fineAnchorValid
            || !coarseAnchorValid
        )
        {
            error =
                !string.IsNullOrEmpty(fineError)
                    ? fineError
                    : coarseError;

            return false;
        }

        Vector3 expectedOffset =
            fineAnchor - coarseAnchor;

        Vector4 boundOffset =
            block.GetVector(
                ClipmapTransitionOffsetPropertyId
            );

        if (
            Mathf.Abs(boundOffset.x - expectedOffset.x) >
                multiresolutionBindingTolerance
            || Mathf.Abs(boundOffset.z - expectedOffset.z) >
                multiresolutionBindingTolerance
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' transition offset does not match applied fine/coarse anchors.";

            return false;
        }

        if (
            Mathf.Abs(expectedOffset.x) >
                multiresolutionBindingTolerance
            || Mathf.Abs(expectedOffset.z) >
                multiresolutionBindingTolerance
        )
        {
            divergentAdjacentAnchors++;
        }

        MeshFilter filter =
            binding.Renderer.GetComponent<MeshFilter>();

        if (
            filter == null
            || filter.sharedMesh == null
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' has no MeshFilter/shared mesh.";

            return false;
        }

        List<Vector4> clipmapData =
            new List<Vector4>();

        filter.sharedMesh.GetUVs(
            3,
            clipmapData
        );

        if (
            clipmapData.Count !=
                filter.sharedMesh.vertexCount
        )
        {
            error =
                $"Stitch '{binding.Renderer.name}' TEXCOORD3 data does not match its vertex count.";

            return false;
        }

        for (
            int index = 0;
            index < clipmapData.Count;
            index++
        )
        {
            float weight =
                clipmapData[index].x;

            if (
                float.IsNaN(weight)
                || float.IsInfinity(weight)
                || weight < -multiresolutionBindingTolerance
                || weight > 1f + multiresolutionBindingTolerance
            )
            {
                error =
                    $"Stitch '{binding.Renderer.name}' has invalid TEXCOORD3.x transition weight at vertex {index}.";

                return false;
            }

            transitionVertices++;
        }

        return true;
    }

    private IEnumerator RunIndependentAnchorStressRoutine()
    {
        EnsureMrh07References();

        if (
            !streamer.TryGetRuntimeHeightConfigurationForInspection(
                out WorldSettings settings,
                out _
            )
        )
        {
            FinishIndependentAnchorValidation(
                TerrainRuntimeValidationStatus.Failed,
                "Runtime WorldSettings are unavailable."
            );

            yield break;
        }

        TerrainClipmapLayout candidate =
            new TerrainClipmapLayout();

        Vector3 originalTarget =
            clipmapController.Target != null
                ? clipmapController.Target.position
                : transform.position;

        bool found =
            TryFindDivergentLayout(
                settings,
                originalTarget,
                candidate
            );

        if (!found)
        {
            FinishIndependentAnchorValidation(
                TerrainRuntimeValidationStatus.Inconclusive,
                "No deterministic in-world test position produced divergent adjacent LOD anchors."
            );

            yield break;
        }

        TakeControllerOwnership();

        TerrainClipmapLayoutApplier applier =
            new TerrainClipmapLayoutApplier();

        applier.Configure(
            transform,
            settings
        );

        string failure = null;

        yield return PrepareAndActivateLayout(
            candidate,
            applier,
            error => failure = error
        );

        if (failure == null)
        {
            bool valid =
                TryValidateCurrentMultiresolutionBindings(
                    out string bindingSummary
                );

            if (!valid)
            {
                failure = bindingSummary;
            }
        }

        yield return RestoreGameplayLayout(
            settings,
            originalTarget,
            applier
        );

        RestoreControllerOwnership();

        FinishIndependentAnchorValidation(
            failure == null
                ? TerrainRuntimeValidationStatus.Passed
                : TerrainRuntimeValidationStatus.Failed,
            failure == null
                ? "A deterministic independently snapped layout was streamed, applied, activated, and passed semantic renderer/stitch validation."
                : failure
        );
    }

    private IEnumerator RunSchedulerStressRoutine()
    {
        EnsureMrh07References();

        if (
            !streamer.TryGetRuntimeHeightConfigurationForInspection(
                out WorldSettings settings,
                out _
            )
        )
        {
            FinishSchedulerStressValidation(
                TerrainRuntimeValidationStatus.Failed,
                "Runtime WorldSettings are unavailable."
            );

            yield break;
        }

        Vector3 originalTarget =
            clipmapController.Target != null
                ? clipmapController.Target.position
                : transform.position;

        TakeControllerOwnership();
        streamer.ResetHeightSchedulerValidationCounters();

        TerrainClipmapLayoutApplier applier =
            new TerrainClipmapLayoutApplier();

        applier.Configure(
            transform,
            settings
        );

        List<TerrainClipmapLayout> layouts =
            BuildStressLayouts(
                settings,
                originalTarget
            );

        string failure = null;

        for (
            int index = 0;
            index < layouts.Count
            && failure == null;
            index++
        )
        {
            yield return PrepareAndActivateLayout(
                layouts[index],
                applier,
                error => failure = error
            );

            if (
                failure == null
                && !SchedulerBoundIsValid(
                    out failure
                )
            )
            {
                break;
            }
        }

        if (
            failure == null
            && layouts.Count >= 3
        )
        {
            int rapidStart =
                Mathf.Max(
                    0,
                    layouts.Count - 3
                );

            for (
                int index = rapidStart;
                index < layouts.Count;
                index++
            )
            {
                streamer.RequestCoverageForClipmapLayout(
                    layouts[index]
                );

                if (!SchedulerBoundIsValid(out failure))
                {
                    break;
                }

                yield return null;
            }

            if (failure == null)
            {
                yield return PrepareAndActivateLayout(
                    layouts[layouts.Count - 1],
                    applier,
                    error => failure = error
                );
            }
        }

        TerrainHeightSchedulerDiagnosticsSnapshot scheduler =
            default;

        bool haveScheduler =
            streamer.TryGetHeightSchedulerDiagnostics(
                out scheduler
            );

        if (
            failure == null
            && haveScheduler
            && (
                scheduler.PeakActiveLoadCount >
                    scheduler.ConcurrencyLimit
                || scheduler.PeakTransientSourceCount >
                    scheduler.ConcurrencyLimit
                || scheduler.PriorityViolationCount != 0
                || scheduler.DuplicateStartViolationCount != 0
            )
        )
        {
            failure =
                "Height scheduler diagnostic invariant failed.";
        }

        yield return RestoreGameplayLayout(
            settings,
            originalTarget,
            applier
        );

        RestoreControllerOwnership();

        if (!haveScheduler)
        {
            FinishSchedulerStressValidation(
                TerrainRuntimeValidationStatus.Failed,
                "Height scheduler diagnostics were unavailable."
            );

            yield break;
        }

        string summary =
            $"Peak Active Loads: {scheduler.PeakActiveLoadCount}/{scheduler.ConcurrencyLimit}\n" +
            $"Peak Transient Sources: {scheduler.PeakTransientSourceCount}/{scheduler.ConcurrencyLimit}\n" +
            $"Peak Source Estimate: {scheduler.PeakEstimatedLogicalSourceBytes:N0} bytes\n" +
            $"Requests Started: {scheduler.RequestsStarted}\n" +
            $"Source Uploads: {scheduler.SourceUploadCount}\n" +
            $"Cache-to-Cache Reuses: {scheduler.CacheToCacheReuseCount}\n" +
            $"Repeated Plan Skips: {scheduler.RepeatedPlanSubmissionSkipCount}\n" +
            $"Coalesced Requests: {scheduler.CoalescedRequestCount}\n" +
            $"Prefetch Promotions: {scheduler.PrefetchPromotedToRequiredCount}\n" +
            $"Stale Queued Discards: {scheduler.StaleQueuedRequestDiscardCount}\n" +
            $"Stale Completed Discards: {scheduler.StaleCompletedSourceDiscardCount}\n" +
            $"Priority Violations: {scheduler.PriorityViolationCount}\n" +
            $"Duplicate Starts: {scheduler.DuplicateStartViolationCount}";

        TerrainRuntimeValidationStatus status;

        if (failure != null)
        {
            status =
                TerrainRuntimeValidationStatus.Failed;

            summary +=
                "\n\n" +
                failure;
        }
        else if (scheduler.RequestsStarted <= 0)
        {
            status =
                TerrainRuntimeValidationStatus.Inconclusive;

            summary +=
                "\n\nNo new Height Addressables request was required by the deterministic stress layouts.";
        }
        else
        {
            status =
                TerrainRuntimeValidationStatus.Passed;
        }

        FinishSchedulerStressValidation(
            status,
            summary
        );
    }

    private bool TryFindDivergentLayout(
        WorldSettings settings,
        Vector3 origin,
        TerrainClipmapLayout output
    )
    {
        float baseSpacing =
            Mathf.Max(
                0.01f,
                settings.ClipmapBaseSpacing
            );

        float worldX =
            Mathf.Max(
                baseSpacing,
                settings.gridWidth * settings.chunkSize
            );

        float worldZ =
            Mathf.Max(
                baseSpacing,
                settings.gridHeight * settings.chunkSize
            );

        for (int ring = 1; ring <= 24; ring++)
        {
            Vector3 candidate =
                new Vector3(
                    Mathf.Clamp(
                        origin.x + baseSpacing * (ring * 3 + 1),
                        0f,
                        worldX
                    ),
                    origin.y,
                    Mathf.Clamp(
                        origin.z + baseSpacing * (ring * 2 + 1),
                        0f,
                        worldZ
                    )
                );

            if (
                !TerrainClipmapLayoutUtility.TryCalculateLayout(
                    settings,
                    candidate,
                    transform.position.y,
                    output,
                    out _
                )
            )
            {
                continue;
            }

            for (
                int level = 0;
                level < output.LevelCount - 1;
                level++
            )
            {
                if (
                    output.GetAnchor(level) !=
                    output.GetAnchor(level + 1)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private List<TerrainClipmapLayout> BuildStressLayouts(
        WorldSettings settings,
        Vector3 original
    )
    {
        List<TerrainClipmapLayout> result =
            new List<TerrainClipmapLayout>();

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

        Vector2[] normalized =
        {
            new Vector2(0.45f, 0.45f),
            new Vector2(0.55f, 0.45f),
            new Vector2(0.55f, 0.55f),
            new Vector2(0.15f, 0.15f),
            new Vector2(0.85f, 0.85f)
        };

        for (
            int index = 0;
            index < normalized.Length;
            index++
        )
        {
            TerrainClipmapLayout layout =
                new TerrainClipmapLayout();

            Vector3 candidate =
                new Vector3(
                    worldX * normalized[index].x,
                    original.y,
                    worldZ * normalized[index].y
                );

            if (
                TerrainClipmapLayoutUtility.TryCalculateLayout(
                    settings,
                    candidate,
                    transform.position.y,
                    layout,
                    out _
                )
            )
            {
                result.Add(layout);
            }
        }

        return result;
    }

    private IEnumerator PrepareAndActivateLayout(
        TerrainClipmapLayout layout,
        TerrainClipmapLayoutApplier applier,
        Action<string> completed
    )
    {
        if (
            layout == null
            || !layout.IsValid
        )
        {
            completed("A stress-test layout is invalid.");
            yield break;
        }

        streamer.RequestCoverageForClipmapLayout(
            layout
        );

        float started = Time.realtimeSinceStartup;

        while (
            !streamer.CanActiveCachesCoverLayout(
                layout
            )
        )
        {
            if (
                Time.realtimeSinceStartup - started >
                stressValidationTimeoutSeconds
            )
            {
                completed(
                    "Timed out waiting for required multiresolution terrain coverage during stress validation."
                );

                yield break;
            }

            if (
                !SchedulerBoundIsValid(
                    out string schedulerError
                )
            )
            {
                completed(schedulerError);
                yield break;
            }

            if (streamer.HasPreparedCacheActivation)
            {
                TerrainClipmapLayout prepared =
                    new TerrainClipmapLayout();

                if (
                    streamer.TryGetPreparedClipmapLayout(
                        prepared
                    )
                )
                {
                    if (
                        !applier.TryApply(
                            prepared,
                            out string preparedError
                        )
                        || !streamer.ActivatePreparedCachesForLayout(
                            prepared
                        )
                    )
                    {
                        completed(
                            string.IsNullOrEmpty(preparedError)
                                ? "Could not activate an intermediate prepared terrain layout during stress validation."
                                : preparedError
                        );

                        yield break;
                    }
                }
            }

            streamer.RequestCoverageForClipmapLayout(
                layout
            );

            yield return null;
        }

        if (
            !applier.TryApply(
                layout,
                out string applyError
            )
        )
        {
            completed(applyError);
            yield break;
        }

        if (
            streamer.HasPreparedCacheActivation
            && !streamer.ActivatePreparedCachesForLayout(
                layout
            )
        )
        {
            completed(
                "Prepared multiresolution terrain caches could not be activated for the stress-test layout."
            );

            yield break;
        }

        completed(null);
    }

    private IEnumerator RestoreGameplayLayout(
        WorldSettings settings,
        Vector3 targetPosition,
        TerrainClipmapLayoutApplier applier
    )
    {
        TerrainClipmapLayout restore =
            new TerrainClipmapLayout();

        if (
            !TerrainClipmapLayoutUtility.TryCalculateLayout(
                settings,
                targetPosition,
                transform.position.y,
                restore,
                out _
            )
        )
        {
            yield break;
        }

        string ignored = null;

        yield return PrepareAndActivateLayout(
            restore,
            applier,
            error => ignored = error
        );
    }

    private bool SchedulerBoundIsValid(
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
            return true;
        }

        if (
            scheduler.ActiveLoadCount >
                scheduler.ConcurrencyLimit
            || scheduler.TransientSourceSlotCount >
                scheduler.ConcurrencyLimit
        )
        {
            error =
                $"Height scheduler exceeded its concurrency limit: {scheduler.ActiveLoadCount}/{scheduler.ConcurrencyLimit}.";

            return false;
        }

        return true;
    }

    private void TakeControllerOwnership()
    {
        EnsureMrh07References();

        if (
            clipmapController == null
            || mrh07StressOwnsController
        )
        {
            return;
        }

        mrh07ControllerWasEnabled =
            clipmapController.enabled;

        clipmapController.enabled = false;
        mrh07StressOwnsController = true;
    }

    private void RestoreControllerOwnership()
    {
        if (!mrh07StressOwnsController)
        {
            return;
        }

        if (clipmapController != null)
        {
            clipmapController.enabled =
                mrh07ControllerWasEnabled;
        }

        mrh07StressOwnsController = false;
    }

    private void FinishIndependentAnchorValidation(
        TerrainRuntimeValidationStatus status,
        string summary
    )
    {
        independentAnchorValidationStatus = status;
        independentAnchorValidationSummary = summary ?? "";
        mrh07ValidationRoutine = null;

        LogMrh07Result(
            "Independent-anchor stress validation",
            status,
            independentAnchorValidationSummary
        );
    }

    private void FinishSchedulerStressValidation(
        TerrainRuntimeValidationStatus status,
        string summary
    )
    {
        schedulerStressValidationStatus = status;
        schedulerStressValidationSummary = summary ?? "";
        mrh07ValidationRoutine = null;

        LogMrh07Result(
            "Height scheduler stress validation",
            status,
            schedulerStressValidationSummary
        );
    }

    private void LogMrh07Result(
        string title,
        TerrainRuntimeValidationStatus status,
        string summary
    )
    {
        string message =
            title + "\n\n" +
            summary;

        if (status == TerrainRuntimeValidationStatus.Passed)
        {
            Debug.Log(message, this);
        }
        else if (status == TerrainRuntimeValidationStatus.Inconclusive)
        {
            Debug.LogWarning(message, this);
        }
        else
        {
            Debug.LogError(message, this);
        }
    }
}
