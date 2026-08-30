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
                "Committed Base + Incremental Composite"
            );

            EditorGUILayout.LabelField(
                "Cache Tile Grid",
                $"{TerrainAuthoringPreviewService.CacheWidth} x " +
                $"{TerrainAuthoringPreviewService.CacheHeight}"
            );

            EditorGUILayout.LabelField(
                "Cache Slices",
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
                "Full Cache Builds",
                TerrainAuthoringPreviewService
                    .FullCommittedBuildCount
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
            "The edit-mode preview now keeps committed base " +
            "heightfield identity separate from overall authoring " +
            "state.\n\n" +

            "Committed base/layout changes rebuild the full GPU " +
            "cache. Future modifier edits can instead notify only " +
            "their affected height tiles so those existing texture-" +
            "array slices are recomposited in place without replacing " +
            "or rebinding the full cache.\n\n" +

            "The Rebuild Committed Preview button performs the " +
            "expensive validation/full rebuild explicitly.",
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
