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
            GUILayout.Space(5f);

            EditorGUILayout.LabelField(
                "Source",
                "Committed Authoring Heightfield"
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
                "Approx. GPU Memory",
                FormatPreviewMemory(
                    TerrainAuthoringPreviewService
                        .ApproximateGpuMemoryBytes
                )
            );
        }

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            !previewEnabled
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Refresh Preview",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringPreviewService
                .RefreshNow();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "The edit-mode preview reads committed authoring " +
            "height tiles directly and binds them to the same " +
            "clipmap height-cache shader interface used at " +
            "runtime.\n\n" +

            "Compiling runtime heightmaps or rebuilding " +
            "Addressables is not required to refresh this preview.",
            MessageType.Info
        );

        GUILayout.EndVertical();
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
