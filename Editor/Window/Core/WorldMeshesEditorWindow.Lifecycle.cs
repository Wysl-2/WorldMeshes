using UnityEditor;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void OnEnable()
    {
        LoadDefaultEditorAssets();

        LoadEditorState();

        InitializeStampLibraryBrowser();

        TerrainRegionalElevationSceneTool
            .SetContext(
                worldSettings,
                terrainAuthoringData
            );

        InitializeRuntimeBakeDiagnosticsCache();
    }

    private void OnDisable()
    {
        ShutdownRuntimeBakeDiagnosticsCache();

        CancelActiveInteractions();

        ShutdownStampLibraryBrowser();

        TerrainRegionalElevationSceneTool
            .ClearContext();
    }

    private void LoadDefaultEditorAssets()
    {
        if (worldSettings == null)
        {
            worldSettings =
                AssetDatabase.LoadAssetAtPath<WorldSettings>(
                    DefaultWorldSettingsPath
                );
        }

        if (terrainAuthoringData == null)
        {
            terrainAuthoringData =
                AssetDatabase
                    .LoadAssetAtPath<TerrainAuthoringData>(
                        DefaultTerrainAuthoringDataPath
                    );
        }
    }

    private void LoadEditorState()
    {
        if (worldSettings != null)
        {
            LoadWorldSettingsIntoEditor();
        }

        if (terrainAuthoringData != null)
        {
            LoadTerrainAuthoringDataIntoEditor();
        }
    }

    private void CancelActiveInteractions()
    {
        CancelStampSmoothingInteractionOnDisable();

        CancelStampSourceRemapInteractionOnDisable();
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
