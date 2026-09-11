using UnityEditor;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void OnDisable()
    {
        CancelStampSmoothingInteractionOnDisable();

        CancelStampSourceRemapInteractionOnDisable();

        ShutdownStampLibraryBrowser();

        TerrainRegionalElevationSceneTool
            .ClearContext();
    }
    
    private void CancelStampSmoothingInteractionOnDisable()
    {
        if (
            activeStampSmoothingField !=
            StampSmoothingInteractiveField.None
            &&
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
            &&
            TerrainAuthoringModifierService
                .ActiveInteractiveStableId ==
            activeStampSmoothingStableId
        )
        {
            TerrainAuthoringModifierService
                .CancelInteractiveEdit(
                    out _
                );
        }

        ClearSmoothingInteractiveState();
    }
}