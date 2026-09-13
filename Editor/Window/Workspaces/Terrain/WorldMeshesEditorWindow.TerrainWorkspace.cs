using UnityEditor;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawTerrainWorkspace()
    {
        DrawWorkspaceHeader(
            "Terrain",
            "Manage non-destructive terrain modifiers and control the " +
            "edit-mode terrain preview and authoring visualization."
        );

        DrawStampAuthoringToolbarSettings();

        DrawWorkspaceSectionGap();

        DrawStampLibraryBrowserSettings();

        DrawWorkspaceSectionGap();

        DrawModifierAuthoringSettings();

        DrawWorkspaceSectionGap();

        DrawHeightPreviewSettings();

        DrawWorkspaceSectionGap();

        DrawSceneViewFollowingSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringVisualizationSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringWireframeSettings();
    }
}
