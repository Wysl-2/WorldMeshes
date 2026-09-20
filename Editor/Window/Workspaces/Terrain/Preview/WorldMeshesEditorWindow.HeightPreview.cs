using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawHeightPreviewSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Height Preview",
            EditorStyles.boldLabel
        );

        bool previewEnabled =
            TerrainAuthoringPreviewService.Enabled;

        EditorGUI.BeginChangeCheck();

        bool newPreviewEnabled =
            EditorGUILayout.Toggle(
                "Enabled",
                previewEnabled
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringPreviewService.Enabled =
                newPreviewEnabled;

            previewEnabled =
                newPreviewEnabled;
        }

        EditorGUILayout.LabelField(
            "Status",
            TerrainAuthoringPreviewService
                .StatusLabel
        );

        if (
            !string.IsNullOrEmpty(
                TerrainAuthoringPreviewService
                    .StatusMessage
            )
        )
        {
            MessageType messageType =
                TerrainAuthoringPreviewService.Status ==
                    TerrainAuthoringPreviewStatus.Error
                    ? MessageType.Error
                    :
                    TerrainAuthoringPreviewService.Status ==
                        TerrainAuthoringPreviewStatus.Ready
                        ? MessageType.Info
                        : MessageType.Warning;

            EditorGUILayout.HelpBox(
                TerrainAuthoringPreviewService
                    .StatusMessage,
                messageType
            );
        }

        if (
            previewEnabled
            &&
            TerrainAuthoringPreviewService.CacheReady
        )
        {
            GUILayout.Space(
                5f
            );

            EditorGUILayout.LabelField(
                "Cache Model",
                "Active + Staging Resident Composite"
            );

            EditorGUILayout.LabelField(
                "Active Tile Grid",
                $"{TerrainAuthoringPreviewService.CacheWidth} x " +
                $"{TerrainAuthoringPreviewService.CacheHeight}"
            );

            EditorGUILayout.LabelField(
                "Active Slices",
                TerrainAuthoringPreviewService
                    .CacheSliceCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Samples / Slice",
                $"{TerrainAuthoringPreviewService.SamplesPerSide} x " +
                $"{TerrainAuthoringPreviewService.SamplesPerSide}"
            );

            Vector2Int cacheOrigin =
                TerrainAuthoringPreviewService
                    .CacheOriginTile;

            EditorGUILayout.LabelField(
                "Active Cache Origin",
                $"{cacheOrigin.x}, {cacheOrigin.y}"
            );

            if (
                TerrainAuthoringPreviewService
                    .TryGetRequestedResidentWindow(
                        out TerrainHeightCacheWindow requestedWindow
                    )
            )
            {
                EditorGUILayout.LabelField(
                    "Requested Resident Window",
                    requestedWindow.ToString()
                );
            }

            if (
                TerrainAuthoringPreviewService
                    .HasTransitionDiagnostics
            )
            {
                GUILayout.Space(
                    5f
                );

                EditorGUILayout.LabelField(
                    "Transition State",
                    TerrainAuthoringPreviewService
                        .TransitionStateLabel
                );

                if (
                    TerrainAuthoringPreviewService
                        .TryGetStagingResidentWindow(
                            out TerrainHeightCacheWindow stagingWindow
                        )
                )
                {
                    EditorGUILayout.LabelField(
                        "Staging Window",
                        stagingWindow.ToString()
                    );
                }

                EditorGUILayout.LabelField(
                    "Retained Tiles",
                    TerrainAuthoringPreviewService
                        .LastTransitionRetainedTileCount
                        .ToString("N0")
                );

                EditorGUILayout.LabelField(
                    "Entering Tiles",
                    TerrainAuthoringPreviewService
                        .LastTransitionEnteringTileCount
                        .ToString("N0")
                );

                EditorGUILayout.LabelField(
                    "Leaving Tiles",
                    TerrainAuthoringPreviewService
                        .LastTransitionLeavingTileCount
                        .ToString("N0")
                );

                EditorGUILayout.LabelField(
                    "Reusable Retained",
                    TerrainAuthoringPreviewService
                        .LastTransitionReusableRetainedTileCount
                        .ToString("N0")
                );

                EditorGUILayout.LabelField(
                    "Retained GPU Copies",
                    TerrainAuthoringPreviewService
                        .LastTransitionRetainedGpuCopyCount
                        .ToString("N0")
                );

                EditorGUILayout.LabelField(
                    "Committed Source Loads",
                    TerrainAuthoringPreviewService
                        .LastTransitionCommittedSourceLoadCount
                        .ToString("N0")
                );

                EditorGUILayout.LabelField(
                    "Composed Tiles",
                    TerrainAuthoringPreviewService
                        .LastTransitionComposedTileCount
                        .ToString("N0")
                );

                if (
                    TerrainAuthoringPreviewService
                        .HasTransitionFailure
                )
                {
                    EditorGUILayout.HelpBox(
                        TerrainAuthoringPreviewService
                            .LastTransitionFailureMessage,
                        MessageType.Error
                    );
                }
            }

            EditorGUILayout.LabelField(
                "Active Height Range",
                $"{TerrainAuthoringPreviewService.MinimumPreviewHeight:R} -> " +
                $"{TerrainAuthoringPreviewService.MaximumPreviewHeight:R}"
            );

            EditorGUILayout.LabelField(
                "Active Texture ID",
                TerrainAuthoringPreviewService
                    .CacheTextureInstanceId
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Active Cache Activations",
                TerrainAuthoringPreviewService
                    .ResidentCacheBuildCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Pending Dirty Tiles",
                TerrainAuthoringPreviewService
                    .PendingDirtyTileCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Last Incremental Update",
                TerrainAuthoringPreviewService
                    .LastIncrementalSliceCount
                    .ToString("N0") +
                " slice(s)"
            );

            EditorGUILayout.LabelField(
                "Total Incremental Slices",
                TerrainAuthoringPreviewService
                    .TotalIncrementalSliceUpdates
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Approx. Active GPU Memory",
                FormatPreviewMemory(
                    TerrainAuthoringPreviewService
                        .ApproximateGpuMemoryBytes
                )
            );

            GUILayout.Space(
                5f
            );

            DrawShortSignature(
                "Committed Signature",
                TerrainAuthoringPreviewService
                    .SourceCommittedHeightfieldSignature
            );

            DrawShortSignature(
                "Overall Signature",
                TerrainAuthoringPreviewService
                    .SourceOverallAuthoringSignature
            );
        }

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            !previewEnabled
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Rebuild Committed Preview",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringPreviewService
                .ForceCommittedRebuildNow();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "The edit-mode Height Preview now owns an active resident cache " +
            "and a separate staging cache. The active cache remains bound " +
            "while replacement residency is prepared.\n\n" +

            "Overlapping final-composite slices are copied on the GPU when " +
            "their authoring state is still current. Entering or non-reusable " +
            "tiles load authoritative committed data and run the complete " +
            "current regional/modifier composition before activation.\n\n" +

            "A staging cache activates only after every slice reaches final " +
            "composite readiness. Package 03 still performs this work " +
            "synchronously, so hitches may remain; Package 04 introduces " +
            "multi-update incremental streaming.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private static void DrawShortSignature(
        string label,
        string signature
    )
    {
        string value =
            string.IsNullOrEmpty(
                signature
            )
                ? "(none)"
                :
                signature.Length <=
                    16
                    ? signature
                    :
                    signature.Substring(
                        0,
                        16
                    ) +
                    "...";

        EditorGUILayout.LabelField(
            label,
            value
        );
    }

    private static string FormatPreviewMemory(
        long byteCount
    )
    {
        if (byteCount <= 0L)
        {
            return
                "0 MiB";
        }

        double mebibytes =
            byteCount
            /
            (1024.0 * 1024.0);

        return
            $"{mebibytes:F1} MiB";
    }
}
