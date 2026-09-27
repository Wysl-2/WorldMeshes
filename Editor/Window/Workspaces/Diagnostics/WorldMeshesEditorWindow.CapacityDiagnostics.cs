using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showCapacityDiagnostics;

    [SerializeField]
    private bool showRuntimeTerrainCapacity;

    [SerializeField]
    private bool showAddressablesCapacity;

    private void DrawCapacityDiagnostics()
    {
        showCapacityDiagnostics =
            EditorGUILayout.Foldout(
                showCapacityDiagnostics,
                "Capacity",
                true
            );

        if (!showCapacityDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeTerrainCapacity();

        DrawWorkspaceSectionGap();

        DrawAddressablesCapacity();
    }

    private void DrawRuntimeTerrainCapacity()
    {
        showRuntimeTerrainCapacity =
            EditorGUILayout.Foldout(
                showRuntimeTerrainCapacity,
                "Runtime Terrain",
                true
            );

        if (!showRuntimeTerrainCapacity)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        if (streamer == null)
        {
            EditorGUILayout.HelpBox(
                "Runtime terrain capacity diagnostics require the Height streamer on WorldRoot/Clipmap.",
                MessageType.None
            );

            return;
        }

        DrawRuntimeResidencyDiagnostics(
            streamer
        );
    }

    private void DrawAddressablesCapacity()
    {
        showAddressablesCapacity =
            EditorGUILayout.Foldout(
                showAddressablesCapacity,
                "Addressables",
                true
            );

        if (!showAddressablesCapacity)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeAddressablesDiagnostics();
    }
}
