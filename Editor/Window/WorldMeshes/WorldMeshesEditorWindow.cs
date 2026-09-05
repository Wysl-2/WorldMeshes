using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // LAYOUT
    // =====================================================

    private float contentPadding = 10f;
    private float columnGap = 10f;
    private float settingsMinWidth = 300f;

    [MenuItem(
        "Tools/WorldMeshes/Open Editor Window",
        false,
        0
    )]
    public static void ShowWindow()
    {
        GetWindow<WorldMeshesEditorWindow>(
            "World Meshes"
        );
    }

    private void OnEnable()
    {
        // -------------------------------------------------
        // WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            worldSettings =
                AssetDatabase.LoadAssetAtPath<WorldSettings>(
                    DefaultWorldSettingsPath
                );
        }

        // -------------------------------------------------
        // TerrainAuthoringData
        // -------------------------------------------------

        if (terrainAuthoringData == null)
        {
            terrainAuthoringData =
                AssetDatabase
                    .LoadAssetAtPath<TerrainAuthoringData>(
                        DefaultTerrainAuthoringDataPath
                    );
        }

        // -------------------------------------------------
        // Load temporary editor inputs
        // -------------------------------------------------

        if (worldSettings != null)
        {
            LoadWorldSettingsIntoEditor();
        }

        if (terrainAuthoringData != null)
        {
            LoadTerrainAuthoringDataIntoEditor();
        }

        InitializeStampLibraryBrowser();
    }

    private void OnGUI()
    {
        Rect marker =
            GUILayoutUtility.GetRect(
                0f,
                0f
            );

        Rect contentArea =
            new Rect(
                contentPadding,
                marker.y + contentPadding,

                position.width
                    - contentPadding * 2f,

                position.height
                    - marker.y
                    - contentPadding * 2f
            );

        DrawMainLayout(
            contentArea
        );
    }

    private void DrawMainLayout(
        Rect contentArea
    )
    {
        float maxViewportWidth =
            contentArea.width
            - columnGap
            - settingsMinWidth;

        float viewportSize =
            Mathf.Max(
                0f,
                Mathf.Min(
                    maxViewportWidth,
                    contentArea.height
                )
            );

        Rect viewport =
            new Rect(
                contentArea.x,
                contentArea.y,
                viewportSize,
                viewportSize
            );

        float settingsX =
            viewport.xMax
            + columnGap;

        float settingsWidth =
            contentArea.xMax
            - settingsX;

        Rect settingsArea =
            new Rect(
                settingsX,
                contentArea.y,
                settingsWidth,
                contentArea.height
            );

        DrawChunkGrid(
            viewport
        );

        DrawSettingsPanel(
            settingsArea
        );
    }
}
