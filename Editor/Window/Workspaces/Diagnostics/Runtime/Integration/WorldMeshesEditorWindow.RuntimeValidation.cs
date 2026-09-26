using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void OnInspectorUpdate()
    {
        if (EditorApplication.isPlaying)
        {
            Repaint();
        }
    }

    private void DrawRuntimeValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Multiresolution runtime validation is manual development tooling. " +
            "Live snapshots are lightweight; GPU readback and stress tests " +
            "only run when explicitly requested.",
            MessageType.Info
        );

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to run multiresolution runtime validation.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        TerrainHeightmapCacheValidator cacheValidator =
            FindRuntimeHeightmapCacheValidator();

        TerrainClipmapDisplacementValidator displacementValidator =
            FindRuntimeClipmapDisplacementValidator();

        if (
            streamer == null
            || cacheValidator == null
            || displacementValidator == null
        )
        {
            EditorGUILayout.HelpBox(
                "Required runtime validation components were not found on " +
                "WorldRoot/Clipmap.\n\nExit Play Mode and run Setup / Repair " +
                "World Hierarchy.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        DrawLiveRuntimeStreamingState(
            streamer
        );

        bool anyValidationRunning =
            cacheValidator.IsValidating
            || displacementValidator.IsValidating;

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationAction(
            "Height Cache Validation",
            cacheValidator.CacheValidationStatus,
            cacheValidator.CacheValidationSummary,
            "Validate All Height LOD Caches",
            anyValidationRunning,
            cacheValidator.BeginValidation
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationAction(
            "Cross-Resolution Validation",
            cacheValidator.CrossResolutionValidationStatus,
            cacheValidator.CrossResolutionValidationSummary,
            "Validate Cross-Resolution Height Consistency",
            anyValidationRunning,
            cacheValidator.BeginCrossResolutionValidation
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationAction(
            "Renderer / Displacement / Stitch Validation",
            displacementValidator.MultiresolutionValidationStatus,
            displacementValidator.MultiresolutionValidationSummary,
            "Validate Renderer Bindings & Displacement",
            anyValidationRunning,
            displacementValidator.BeginMultiresolutionValidation
        );

        DrawWorkspaceSectionGap();

        EditorGUILayout.HelpBox(
            "Independent-anchor stress temporarily moves the runtime clipmap " +
            "and cache requests without moving the Player/streaming source. " +
            "The gameplay layout is restored afterward.",
            MessageType.None
        );

        DrawRuntimeValidationAction(
            "Independent Anchor Stress",
            displacementValidator.IndependentAnchorValidationStatus,
            displacementValidator.IndependentAnchorValidationSummary,
            "Run Independent-Anchor Stress Validation",
            anyValidationRunning,
            displacementValidator.BeginIndependentAnchorStressValidation
        );

        DrawWorkspaceSectionGap();

        EditorGUILayout.HelpBox(
            "Scheduler stress submits deterministic nearby, rapid, and distant " +
            "layout requests. It verifies bounded concurrency and scheduler " +
            "diagnostic invariants, then restores the gameplay layout.",
            MessageType.None
        );

        DrawRuntimeValidationAction(
            "Height Scheduler Stress",
            displacementValidator.SchedulerStressValidationStatus,
            displacementValidator.SchedulerStressValidationSummary,
            "Run Height Scheduler Stress Test",
            anyValidationRunning,
            displacementValidator.BeginSchedulerStressValidation
        );

        GUILayout.EndVertical();
    }

    private void DrawLiveRuntimeStreamingState(
        TerrainHeightmapStreamer streamer
    )
    {
        GUILayout.Space(4f);

        GUILayout.Label(
            "Live Multiresolution Streaming State",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Height LOD States",
            streamer.HeightLodRuntimeStateCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Transition Running",
            streamer.MultiresolutionTransitionRunning
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Prepared Activation Pending",
            streamer.PreparedMultiresolutionActivationPending
                ? "Yes"
                : "No"
        );

        if (
            streamer.TryGetHeightSchedulerDiagnostics(
                out TerrainHeightSchedulerDiagnosticsSnapshot scheduler
            )
        )
        {
            EditorGUILayout.LabelField(
                "Scheduler Active / Limit",
                $"{scheduler.ActiveLoadCount} / {scheduler.ConcurrencyLimit}"
            );

            EditorGUILayout.LabelField(
                "Reserved Required Slots",
                scheduler.ReservedRequiredSlots.ToString()
            );

            EditorGUILayout.LabelField(
                "Transient Sources / Limit",
                $"{scheduler.TransientSourceSlotCount} / {scheduler.ConcurrencyLimit}"
            );

            EditorGUILayout.LabelField(
                "Ready / Pending Release",
                $"{scheduler.ReadySourceCount} / {scheduler.PendingGpuReleaseCount}"
            );

            EditorGUILayout.LabelField(
                "Scheduler Required Queue",
                scheduler.QueuedRequiredCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Scheduler Prefetch Queue",
                scheduler.QueuedPrefetchCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Scheduler Peak Active",
                scheduler.PeakActiveLoadCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Peak Transient Sources",
                scheduler.PeakTransientSourceCount.ToString()
            );

            EditorGUILayout.LabelField(
                "Logical Source Estimate",
                FormatRuntimeValidationBytes(
                    scheduler.EstimatedLogicalSourceBytes
                )
            );

            EditorGUILayout.LabelField(
                "Peak Source Estimate",
                FormatRuntimeValidationBytes(
                    scheduler.PeakEstimatedLogicalSourceBytes
                )
            );

            EditorGUILayout.LabelField(
                "Source Uploads / Cache Reuses",
                $"{scheduler.SourceUploadCount} / {scheduler.CacheToCacheReuseCount}"
            );

            EditorGUILayout.LabelField(
                "Stale Queue / Completed Discards",
                $"{scheduler.StaleQueuedRequestDiscardCount} / {scheduler.StaleCompletedSourceDiscardCount}"
            );

            EditorGUILayout.LabelField(
                "Source Release Mode",
                scheduler.GraphicsFenceSupported
                    ? "GPU Fence"
                    : "Conservative Frame Delay"
            );
        }

        EditorGUILayout.LabelField(
            "Surface Ready",
            streamer.SurfaceCacheReady
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Surface Origin",
            streamer.SurfaceCacheOriginTile.ToString()
        );

        EditorGUILayout.LabelField(
            "Surface Cache Size",
            $"{streamer.SurfaceCacheWidth} x {streamer.SurfaceCacheHeight}"
        );

        EditorGUILayout.LabelField(
            "Surface Resident Pages",
            streamer.ResidentSurfaceTileCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Surface GPU Estimate",
            FormatRuntimeValidationBytes(
                streamer.EstimatedSurfaceGpuCacheBytes
            )
        );

        GUILayout.Space(5f);

        for (
            int level = 0;
            level < streamer.HeightLodRuntimeStateCount;
            level++
        )
        {
            if (
                !streamer.TryGetHeightLodDiagnostics(
                    level,
                    out TerrainHeightLodDiagnosticsSnapshot lod
                )
            )
            {
                continue;
            }

            GUILayout.BeginVertical(
                EditorStyles.helpBox
            );

            GUILayout.Label(
                $"LOD{lod.Level}  |  Stride {lod.SampleStride}",
                EditorStyles.boldLabel
            );

            EditorGUILayout.LabelField(
                "Sample Spacing",
                lod.SampleSpacing.ToString("R")
            );

            EditorGUILayout.LabelField(
                "Page Resolution",
                $"{lod.SamplesPerSide} x {lod.SamplesPerSide}"
            );

            EditorGUILayout.LabelField(
                "Cache Origin",
                lod.CacheOrigin.ToString()
            );

            EditorGUILayout.LabelField(
                "Cache Size",
                $"{lod.CacheWidth} x {lod.CacheHeight}"
            );

            EditorGUILayout.LabelField(
                "Required Pages",
                FormatRuntimeValidationPageRect(
                    lod.ActiveRequiredPages
                )
            );

            EditorGUILayout.LabelField(
                "Requested Prefetch",
                FormatRuntimeValidationPageRect(
                    lod.RequestedPrefetchPages
                )
            );

            EditorGUILayout.LabelField(
                "Active / Staging Valid",
                $"{lod.ActiveValidPageCount} / {lod.StagingValidPageCount}"
            );

            EditorGUILayout.LabelField(
                "Queued Required / Prefetch",
                $"{lod.QueuedRequiredPageCount} / {lod.QueuedPrefetchPageCount}"
            );

            EditorGUILayout.LabelField(
                "In Flight Required / Prefetch",
                $"{lod.InFlightRequiredPageCount} / {lod.InFlightPrefetchPageCount}"
            );

            EditorGUILayout.LabelField(
                "Transition State",
                lod.TransitionState.ToString()
            );

            EditorGUILayout.LabelField(
                "Ready",
                lod.CacheReady
                    ? "Yes"
                    : "No"
            );

            EditorGUILayout.LabelField(
                "GPU Active / Staging",
                FormatRuntimeValidationBytes(lod.EstimatedActiveGpuBytes) +
                " / " +
                FormatRuntimeValidationBytes(lod.EstimatedStagingGpuBytes)
            );

            GUILayout.EndVertical();
        }
    }

    private void DrawRuntimeValidationAction(
        string title,
        TerrainRuntimeValidationStatus status,
        string summary,
        string buttonLabel,
        bool anyValidationRunning,
        System.Action action
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox
        );

        GUILayout.Label(
            title,
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Status",
            status.ToString()
        );

        if (!string.IsNullOrEmpty(summary))
        {
            EditorGUILayout.HelpBox(
                summary,
                MessageTypeForRuntimeValidationStatus(status)
            );
        }

        EditorGUI.BeginDisabledGroup(
            anyValidationRunning
        );

        if (
            GUILayout.Button(
                buttonLabel,
                GUILayout.ExpandWidth(true)
            )
        )
        {
            action?.Invoke();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();
        GUILayout.EndVertical();
    }

    private static MessageType MessageTypeForRuntimeValidationStatus(
        TerrainRuntimeValidationStatus status
    )
    {
        switch (status)
        {
            case TerrainRuntimeValidationStatus.Failed:
                return MessageType.Error;

            case TerrainRuntimeValidationStatus.Running:
                return MessageType.Info;

            case TerrainRuntimeValidationStatus.Passed:
                return MessageType.Info;

            case TerrainRuntimeValidationStatus.Inconclusive:
                return MessageType.Warning;

            default:
                return MessageType.None;
        }
    }

    private static string FormatRuntimeValidationBytes(
        long bytes
    )
    {
        if (bytes <= 0L)
        {
            return "0 MiB";
        }

        double mib =
            bytes /
            (1024d * 1024d);

        return
            mib.ToString("N2") +
            " MiB";
    }

    private static string FormatRuntimeValidationPageRect(
        TerrainHeightPageRect pages
    )
    {
        if (!pages.IsValid)
        {
            return "None";
        }

        return
            $"({pages.Minimum.x}, {pages.Minimum.y}) -> " +
            $"({pages.Maximum.x}, {pages.Maximum.y}) " +
            $"[{pages.Width} x {pages.Height}]";
    }

    private TerrainHeightmapStreamer
        FindRuntimeHeightmapStreamer()
    {
        Transform clipmapRoot =
            FindRuntimeClipmapRoot();

        return
            clipmapRoot != null
                ? clipmapRoot.GetComponent<TerrainHeightmapStreamer>()
                : null;
    }

    private TerrainHeightmapCacheValidator
        FindRuntimeHeightmapCacheValidator()
    {
        Transform clipmapRoot =
            FindRuntimeClipmapRoot();

        return
            clipmapRoot != null
                ? clipmapRoot.GetComponent<TerrainHeightmapCacheValidator>()
                : null;
    }

    private TerrainClipmapDisplacementValidator
        FindRuntimeClipmapDisplacementValidator()
    {
        Transform clipmapRoot =
            FindRuntimeClipmapRoot();

        return
            clipmapRoot != null
                ? clipmapRoot.GetComponent<TerrainClipmapDisplacementValidator>()
                : null;
    }

    private Transform FindRuntimeClipmapRoot()
    {
        GameObject worldRoot =
            GameObject.Find(
                TerrainWorldHierarchyGenerator
                    .WorldRootName
            );

        if (worldRoot == null)
        {
            return null;
        }

        return
            worldRoot.transform.Find(
                TerrainWorldHierarchyGenerator
                    .ClipmapRootName
            );
    }
}
