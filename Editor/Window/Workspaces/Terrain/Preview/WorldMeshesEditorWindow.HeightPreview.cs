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
                "Resident Committed Base + Incremental Composite"
            );

            EditorGUILayout.LabelField(
                "Resident Tile Grid",
                $"{TerrainAuthoringPreviewService.CacheWidth} x " +
                $"{TerrainAuthoringPreviewService.CacheHeight}"
            );

            EditorGUILayout.LabelField(
                "Resident Slices",
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
                "Cache Origin Tile",
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

            EditorGUILayout.LabelField(
                "Preview Height Range",
                $"{TerrainAuthoringPreviewService.MinimumPreviewHeight:R} -> " +
                $"{TerrainAuthoringPreviewService.MaximumPreviewHeight:R}"
            );

            EditorGUILayout.LabelField(
                "Cache Texture ID",
                TerrainAuthoringPreviewService
                    .CacheTextureInstanceId
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Resident Cache Builds",
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
                "Approx. GPU Memory",
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
            "The edit-mode Height Preview now keeps only the local " +
            "height-tile window required by the current clipmap, plus " +
            "sample safety and a one-tile residency guard.\n\n" +

            "Committed base/layout changes rebuild the current resident " +
            "window. Modifier edits still recomposite affected resident " +
            "slices in place. Nonresident terrain remains authoritative " +
            "authoring data rather than missing data.\n\n" +

            "Package 02 cache replacement is synchronous, so moving into " +
            "a new resident window may temporarily hitch. Staged and " +
            "incremental transitions are introduced by later packages.",
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
