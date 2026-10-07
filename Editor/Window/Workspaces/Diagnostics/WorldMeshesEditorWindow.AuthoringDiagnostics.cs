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

    private void DrawStreamingPreviewDiagnostics()
    {
        var snapshot = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        DrawHeightPreviewDiagnostics(snapshot);
        EditorGUILayout.LabelField("Editor Lifecycle / Work Allowed / Resume Pending", $"{snapshot.EditorLifecycleStable} / {snapshot.PreviewWorkAllowed} / {snapshot.LifecycleResumePending}");
        EditorGUILayout.LabelField("Suspension", snapshot.SuspensionReasons.ToString());
        EditorGUILayout.LabelField("Controlling Scene View / Ownership", $"{snapshot.ControllingSceneViewInstanceId} / {snapshot.SceneViewOwnershipGeneration}");
        EditorGUILayout.LabelField("Follow / Source / Freeze", $"{snapshot.FollowSceneView} / {snapshot.FollowSource} / {snapshot.FreezePreview}");
    }




}
