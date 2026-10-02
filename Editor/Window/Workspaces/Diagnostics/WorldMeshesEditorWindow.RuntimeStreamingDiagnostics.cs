using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showRuntimeStreamingLiveDiagnostics;

    private void OnInspectorUpdate()
    {
        if (
            EditorApplication.isPlaying
            || IsValidationSuiteRunning()
        )
        {
            Repaint();
        }
    }

    private void DrawRuntimeStreamingDiagnostics()
    {
        showRuntimeStreamingLiveDiagnostics =
            EditorGUILayout.Foldout(
                showRuntimeStreamingLiveDiagnostics,
                "Runtime Streaming",
                true
            );

        if (!showRuntimeStreamingLiveDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Runtime streaming diagnostics are available in Play Mode.",
                MessageType.None
            );

            GUILayout.EndVertical();
            return;
        }

        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        if (streamer == null)
        {
            EditorGUILayout.HelpBox(
                "The runtime Height streamer could not be found on WorldRoot/Clipmap.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        DrawLiveRuntimeStreamingState(
            streamer
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
                FormatDiagnosticsBytes(
                    scheduler.EstimatedLogicalSourceBytes
                )
            );

            EditorGUILayout.LabelField(
                "Peak Source Estimate",
                FormatDiagnosticsBytes(
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
            FormatDiagnosticsBytes(
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
                FormatRuntimeStreamingPageRect(
                    lod.ActiveRequiredPages
                )
            );

            EditorGUILayout.LabelField(
                "Requested Prefetch",
                FormatRuntimeStreamingPageRect(
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
                FormatDiagnosticsBytes(lod.EstimatedActiveGpuBytes) +
                " / " +
                FormatDiagnosticsBytes(lod.EstimatedStagingGpuBytes)
            );

            GUILayout.EndVertical();
        }
    }

    private static string FormatRuntimeStreamingPageRect(
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
