using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // LAYOUT
    // =====================================================

    private float contentPadding = 10f;

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

        DrawSettingsPanel(
            contentArea
        );
    }
}
