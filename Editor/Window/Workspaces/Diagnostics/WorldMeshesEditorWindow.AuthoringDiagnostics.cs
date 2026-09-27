using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showLiveDiagnostics;

    [SerializeField]
    private bool showAuthoringLiveDiagnostics;

    private void DrawLiveDiagnostics()
    {
        showLiveDiagnostics =
            EditorGUILayout.Foldout(
                showLiveDiagnostics,
                "Live Diagnostics",
                true
            );

        if (!showLiveDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawAuthoringDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeStreamingDiagnostics();
    }

    private void DrawAuthoringDiagnostics()
    {
        showAuthoringLiveDiagnostics =
            EditorGUILayout.Foldout(
                showAuthoringLiveDiagnostics,
                "Authoring & Preview",
                true
            );

        if (!showAuthoringLiveDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawStreamingPreviewDiagnostics();
    }
}
