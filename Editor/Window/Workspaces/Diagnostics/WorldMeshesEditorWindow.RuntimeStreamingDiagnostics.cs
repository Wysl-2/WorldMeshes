using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showRuntimeStreamingLiveDiagnostics;

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
}
