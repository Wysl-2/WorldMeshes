using UnityEditor;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawWorldWorkspace()
    {
        DrawWorkspaceHeader(
            "World",
            "Configure the world, clipmap layout, committed authoring " +
            "heightfield, and generated scene hierarchy."
        );

        DrawWorldSettings();

        DrawWorkspaceSectionGap();

        DrawClipmapGenerationSettings();

        DrawWorkspaceSectionGap();

        DrawHeightAuthoringSettings();

        DrawWorkspaceSectionGap();

        DrawRegionalElevationSetupSettings();

        DrawWorkspaceSectionGap();

        DrawRegionalElevationInterpolationSettings();

        DrawWorkspaceSectionGap();

        DrawRegionalElevationManagementSettings();

        DrawWorkspaceSectionGap();

        DrawRegionalElevationSceneToolSettings();

        DrawWorkspaceSectionGap();

        DrawWorldHierarchySettings();
    }
}
