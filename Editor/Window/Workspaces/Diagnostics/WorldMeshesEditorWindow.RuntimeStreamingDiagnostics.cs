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

        DrawMultiresolutionRuntimeStreamingState(
            streamer
        );

        GUILayout.EndVertical();
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
